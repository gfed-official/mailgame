using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// F9 complaint deltas (GDD R9; TR-mail-010, TR-mail-012; ADR-0017 section 7; mail-destinations story 006): the per-door and team
/// token buckets and the team-wide late-complaint window. Each bucket is stored ×<c>window</c> in <see cref="long"/> permille-ticks
/// and refills lazily, on touch only: <c>tokens = min(cap × window, tokens + min(now − lastTick, window) × cap)</c>, with the elapsed
/// time clamped to <c>window</c> before multiplying. Nothing iterates the buckets per tick, and time only enters as the
/// <c>now</c> tick argument, so a paused world freezes them (a second call at the same tick refills 0).
/// <para>
/// Door state lives in the registry's <see cref="DestinationRegistry.TokensPm"/> / <see cref="DestinationRegistry.LastTick"/> columns;
/// the team bucket, <c>lastLateTick</c> and its "never charged" flag are fields here. All buckets start full: the constructor writes
/// <c>cap × window</c> into every row and the team bucket, with <c>lastTick</c> 0 (Evan approved 2026-10-29 (TD-187 item 2)).
/// </para>
/// <para>
/// Each lazy refill adds 1 to <see cref="CounterId.MailBucketsVisited"/>: 2 per <see cref="ChargeMisdelivery"/> (door and team), 0 per
/// <see cref="ChargeLate"/> (Evan approved 2026-10-29 (TD-187 item 4)). A touch before the stored tick fires
/// <see cref="SimAssertCode.MailComplaintTickRegressed"/> under <c>SIM_ASSERT</c>; otherwise the elapsed time is 0
/// (Evan approved 2026-10-29 (TD-187 item 5)). Integer-only, 0 B per call. Not thread-safe: Sim tick thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var buckets = new ComplaintBuckets(registry, content.Mail, playerScalePm: 1000, counters);
/// SimCounterSink sink = counters.PrimitiveSink();
/// int raised = buckets.ChargeMisdelivery(d, now, rawPm: 100_000, stats, ref sink);   // 20,000 from a fresh door
/// int late = buckets.ChargeLate(now, stats.Has(RuleFlag.SuppressLateComplaint));    // 2,000, then 0 for 900 ticks
/// </code>
/// </example>
internal sealed class ComplaintBuckets
{
    private readonly DestinationRegistry _registry;
    private readonly SimCounters _counters;
    private readonly long _capPm;
    private readonly long _teamCapPm;
    private readonly long _window;
    private readonly long _lateWindow;
    private readonly int _latePm;
    private long _teamTokensPm;
    private long _teamLastTick;
    private long _lastLateTick;
    private bool _lateCharged;

    /// <summary>Creates the buckets over a registry's rows, every one of them and the team bucket full.</summary>
    /// <param name="registry">The destination registry whose complaint columns hold the door buckets.</param>
    /// <param name="mail">The mail balance (<c>doorComplaintCap</c>, the two windows, <c>lateComplaint</c>, <c>teamComplaintCap</c>).</param>
    /// <param name="playerScalePm">Run Structure's <c>playerScale</c> (n^0.65) in permille; 1000 solo.</param>
    /// <param name="counters">The world's counters (<see cref="CounterId.MailBucketsVisited"/> and the numerics block).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ComplaintBuckets(DestinationRegistry registry, MailBalance mail, int playerScalePm, SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(counters);
        _registry = registry;
        _counters = counters;

        // Milli-points equal permille, so the caps are the key values; the windows are content ms (30_000 ms = 900 ticks).
        _capPm = mail.DoorComplaintCap;
        _window = TickTime.ContentTicks(mail.DoorComplaintWindowSeconds);
        _lateWindow = TickTime.ContentTicks(mail.LateComplaintWindowSeconds);
        _latePm = mail.LateComplaint;

        // Rounded to whole points, then back to permille: 40_000 x 3864 / 10^6 = 154.56 -> 155 -> 155,000; solo 40,000.
        SimCounterSink sink = counters.PrimitiveSink();
        _teamCapPm = Fix.MulDivRH(mail.TeamComplaintCap, playerScalePm, 1_000_000, ref sink) * 1000;

        for (ushort i = 0; i < registry.Count; i++)
        {
            registry.TokensPm(new DestinationOrdinal(i)) = _capPm * _window;
        }

        _teamTokensPm = _teamCapPm * _window;
    }

    /// <summary>The door cap, permille (<c>capPm</c>).</summary>
    public long CapPm => _capPm;

    /// <summary>The team cap, permille (<c>teamCapPm</c>).</summary>
    public long TeamCapPm => _teamCapPm;

    /// <summary>The bucket window, ticks.</summary>
    public long Window => _window;

    /// <summary>The team bucket's tokens, permille-ticks (×<see cref="Window"/>).</summary>
    public long TeamTokensPm => _teamTokensPm;

    /// <summary>The tick of the team bucket's last touch.</summary>
    public long TeamLastTick => _teamLastTick;

    /// <summary>The tick of the last late charge; meaningful only when <see cref="LateCharged"/>.</summary>
    public long LastLateTick => _lastLateTick;

    /// <summary>True once a late complaint has been charged (<c>lastLateTick</c> starts at "never").</summary>
    public bool LateCharged => _lateCharged;

    /// <summary>
    /// Charges one op's misdelivery complaint at a door (the caller sums <c>complaintOnMisdelivery</c> over the units the op consumed as
    /// Misdeliver and calls once per op). Refills the door and team buckets at <paramref name="now"/>, then
    /// <c>allowed = min(raw, doorTokens / window, teamTokens / window)</c> and debits <c>allowed × window</c> from both. The raised value
    /// is <c>allowed</c> scaled by <see cref="Stat.MisdeliveryComplaintMultPm"/> after the cap, so the buckets are still debited the full
    /// <c>allowed</c> (M-R9-2c; Mail Dev default, pending Evan (TD-193 item 7)). 0 B.
    /// </summary>
    /// <param name="d">The door.</param>
    /// <param name="now">The current tick.</param>
    /// <param name="rawPm">The op's raw complaint, permille; a negative value counts as 0.</param>
    /// <param name="stats">The world's stat sheet (the Team-scope multiplier).</param>
    /// <param name="sink">The numerics sink.</param>
    /// <returns>The complaint to raise, permille; 0 when the buckets allow nothing.</returns>
    public int ChargeMisdelivery(DestinationOrdinal d, long now, int rawPm, IStatSheet stats, ref SimCounterSink sink)
    {
        Refill(ref _registry.TokensPm(d), ref _registry.LastTick(d), now, _capPm);
        Refill(ref _teamTokensPm, ref _teamLastTick, now, _teamCapPm);
        long allowed = Math.Min(
            Math.Max(rawPm, 0),
            Math.Min(Fix.FloorDiv(_registry.TokensPm(d), _window, ref sink), Fix.FloorDiv(_teamTokensPm, _window, ref sink)));
        if (allowed <= 0)
        {
            return 0;
        }

        _registry.TokensPm(d) -= allowed * _window;
        _teamTokensPm -= allowed * _window;
        long raised = Fix.MulDivRH(allowed, stats.Read(Stat.MisdeliveryComplaintMultPm), 1000, ref sink);
        return (int)Math.Min(raised, int.MaxValue);
    }

    /// <summary>
    /// Charges one Accept op's late complaint (the caller calls once per Accept that consumed at least one late unit, never for a
    /// dead-letter-only Accept or a Misdeliver op). Returns <c>lateComplaint</c> and sets <c>lastLateTick = now</c> when Grace Period is off
    /// and no late complaint was charged in the last <c>lateComplaintWindow</c> ticks, team-wide; otherwise 0 with nothing changed.
    /// Visits no bucket. 0 B.
    /// </summary>
    /// <param name="now">The current tick.</param>
    /// <param name="graceSuppresses">The caller's <c>stats.Has(RuleFlag.SuppressLateComplaint)</c> (Grace Period).</param>
    /// <returns>The late complaint to raise, permille, or 0.</returns>
    public int ChargeLate(long now, bool graceSuppresses)
    {
        if (graceSuppresses)
        {
            return 0;
        }

        if (_lateCharged && Elapsed(now, _lastLateTick) < _lateWindow)
        {
            return 0;
        }

        _lastLateTick = now;
        _lateCharged = true;
        return _latePm;
    }

    /// <summary>Writes the team state after the registry's columns: <c>teamTokensPm i64, teamLastTick i64, lastLateTick i64, lateCharged u8</c>.</summary>
    /// <param name="w">The writer, inside the Mail section.</param>
    public void WriteCanonical(ref CanonicalWriter w)
    {
        w.WriteI64(_teamTokensPm);
        w.WriteI64(_teamLastTick);
        w.WriteI64(_lastLateTick);
        w.WriteU8(_lateCharged ? (byte)1 : (byte)0);
    }

    private void Refill(ref long tokens, ref long lastTick, long now, long cap)
    {
        long elapsed = Math.Min(Elapsed(now, lastTick), _window);
        tokens = Math.Min(cap * _window, tokens + (elapsed * cap));

        // A regressed tick is kept, never moved back, so the next touch does not refill the same ticks twice
        // (Mail Dev default, pending Evan (TD-193 item 1)).
        lastTick = Math.Max(lastTick, now);
        _counters.Add(CounterId.MailBucketsVisited);
    }

    private static long Elapsed(long now, long since)
    {
        // Evan approved 2026-10-29 (TD-187 item 5).
        SimAssert.That(now >= since, SimAssertCode.MailComplaintTickRegressed);
        return Math.Max(now - since, 0L);
    }
}
