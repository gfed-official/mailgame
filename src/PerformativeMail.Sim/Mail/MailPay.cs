using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Mail payment, penalty, wallet guard and value-hint (Mail F5, F6, F7, F8, F10; TR-mail-001, TR-mail-005, TR-mail-023; ADR-0017
/// section 4, ADR-0006, ADR-0016 section 3). Static, pure and allocation-free: every function is integer math over
/// <see cref="Cents"/> and permille with <b>one</b> <see cref="Fix.MulDivRH"/> per unit (never a rounding per factor, never a
/// float). The host consume routine and the client preview call these same functions. Stats come only through
/// <see cref="IStatSheet.Read"/> (a read-only, deterministic read), with a fixed read count per call. No function takes a wallet,
/// RNG, clock or registry. Every money function takes the <see cref="SimCounterSink"/> last, so a clamp or divide-by-zero in the
/// numerics is counted instead of dropped (an addition to ADR-0017's sketch).
/// </summary>
/// <example>
/// <code>
/// int t = MailPay.Timeliness(currentShift: 3, deadlineShift: 2, MailPay.LateValueRatioPm(sheet), balance.DeadLetterShifts); // 500
/// Cents pay = MailPay.UnitPay(in unit, t, thrownPm: 1000, sheet, player, ref sink);
/// Cents penalty = MailPay.UnitPenalty(in unit, balance.MisdeliveryPenaltyRatio, balance.MinPenaltyCents, sheet, ref sink);
/// bool allowed = MailPay.WalletGuard(wallet, penalty, floor);
/// </code>
/// </example>
public static class MailPay
{
    private const long PermilleCubed = 1_000_000_000L;
    private const long PermilleSquared = 1_000_000L;

    /// <summary>
    /// F5 timeliness: <c>L = max(0, currentShift - deadlineShift)</c>; 1000 when L is 0, <paramref name="lateValueRatioPm"/> when
    /// 1 &lt;= L &lt; <paramref name="deadLetterShifts"/>, otherwise 0 (a dead letter). Pure integers; reads no sheet.
    /// </summary>
    /// <param name="currentShift">The shift at the apply tick.</param>
    /// <param name="deadlineShift">The unit's deadline shift.</param>
    /// <param name="lateValueRatioPm">The pay share one shift late, in permille (see <see cref="LateValueRatioPm"/>).</param>
    /// <param name="deadLetterShifts">The lateness at which mail pays 0.</param>
    /// <returns>The timeliness in permille.</returns>
    /// <example>
    /// <code>
    /// int a = MailPay.Timeliness(3, 2, 500, 2);   // 500 (one shift late)
    /// int b = MailPay.Timeliness(4, 2, 500, 2);   // 0 (dead letter)
    /// </code>
    /// </example>
    public static int Timeliness(int currentShift, int deadlineShift, int lateValueRatioPm, int deadLetterShifts)
    {
        int lateness = Math.Max(0, currentShift - deadlineShift);
        if (lateness == 0)
        {
            return 1000;
        }

        return lateness < deadLetterShifts ? lateValueRatioPm : 0;
    }

    /// <summary>
    /// The late-value ratio in permille as the sheet resolves it. The stat's base is the <c>mail.lateValueRatio</c> balance value
    /// (500 by default) and Grace Period sets it to 1000, so a caller gets the data value through the sheet and never reads the
    /// balance key itself. One <see cref="IStatSheet.Read"/>.
    /// </summary>
    /// <param name="s">The stat sheet.</param>
    /// <returns>The late-value ratio in permille.</returns>
    /// <example>
    /// <code>int t = MailPay.Timeliness(shift, deadline, MailPay.LateValueRatioPm(sheet), balance.DeadLetterShifts);</code>
    /// </example>
    public static int LateValueRatioPm(IStatSheet s)
    {
        return s.Read(Stat.LateValueRatioPm);
    }

    /// <summary>
    /// F6 pay per unit: <c>RH(spawnValue x T x MailValueMult[kind] x thrownPm / 10^9)</c>, rounded half away from zero exactly
    /// once. One <see cref="IStatSheet.Read"/> (<see cref="Stat.MailValueMultPm"/> qualified by the kind, a Team stat) and one
    /// <see cref="Fix.MulDivRH"/>. <paramref name="thrownPm"/> is a parameter (the thrower's Cannon Arm value snapshotted at the
    /// throw, or 1000), never read inside. The three-factor product is at most 65535 x 1000 x 100000 and fits a <see cref="long"/>.
    /// </summary>
    /// <param name="u">The unit's facts (<see cref="UnitFacts.Kind"/> and <see cref="UnitFacts.SpawnValue"/> are used).</param>
    /// <param name="timelinessPm">The F5 timeliness in permille.</param>
    /// <param name="thrownPm">The thrown-correct-pay multiplier in permille: Cannon Arm for a thrown Accept, else 1000.</param>
    /// <param name="s">The stat sheet.</param>
    /// <param name="p">The paying player; passed through as the read slot for the ADR signature, unused by a Team stat today.</param>
    /// <param name="sink">Counter sink for numerics clamps (last parameter by convention).</param>
    /// <returns>The pay in cents.</returns>
    /// <example>
    /// <code>
    /// Cents pay = MailPay.UnitPay(in letter, timelinessPm: 500, thrownPm: 1000, sheet, slot, ref sink);   // 14 -> 9 with Stamp Collector
    /// </code>
    /// </example>
    public static Cents UnitPay(in UnitFacts u, int timelinessPm, int thrownPm, IStatSheet s, PlayerSlot? p, ref SimCounterSink sink)
    {
        int mult = s.Read(Stat.MailValueMultPm, u.Kind.Value, p);
        long pay = Fix.MulDivRH((long)u.SpawnValue * timelinessPm * mult, thrownPm, PermilleCubed, ref sink);
        return Cents.FromCents(pay, ref sink);
    }

    /// <summary>
    /// F7 penalty per unit: <c>max(minPenaltyCents, RH(spawnValue x misdeliveryPenaltyRatio x MisdeliveryPenaltyMult / 10^6))</c>.
    /// Spawn-based, so a dead letter still pays the penalty. One <see cref="IStatSheet.Read"/> (a Team stat) and one
    /// <see cref="Fix.MulDivRH"/>.
    /// </summary>
    /// <param name="u">The unit's facts (<see cref="UnitFacts.SpawnValue"/> is used).</param>
    /// <param name="misdeliveryPenaltyRatioPm">The penalty share of value in permille (balance <c>mail.misdeliveryPenaltyRatio</c>).</param>
    /// <param name="minPenaltyCents">The defensive floor in cents (balance <c>mail.minPenaltyCents</c>).</param>
    /// <param name="s">The stat sheet.</param>
    /// <param name="sink">Counter sink for numerics clamps (last parameter by convention).</param>
    /// <returns>The penalty in cents.</returns>
    /// <example>
    /// <code>Cents penalty = MailPay.UnitPenalty(in letter, 500, 1, sheet, ref sink);   // 8 -> 4</code>
    /// </example>
    public static Cents UnitPenalty(in UnitFacts u, int misdeliveryPenaltyRatioPm, int minPenaltyCents, IStatSheet s, ref SimCounterSink sink)
    {
        int mult = s.Read(Stat.MisdeliveryPenaltyMultPm);
        long raw = Fix.MulDivRH((long)u.SpawnValue * misdeliveryPenaltyRatioPm, mult, PermilleSquared, ref sink);
        return Cents.FromCents(Math.Max((long)minPenaltyCents, raw), ref sink);
    }

    /// <summary>
    /// F8 wallet-floor guard: a misdelivery may be consumed when <c>balance - penalty &gt;= floor</c>. Computed in
    /// <see cref="long"/> on the cent values. Precondition: <paramref name="penalty"/> is non-negative (F7 gives at least
    /// <c>minPenaltyCents</c>), so with both values within +/-2^62 the difference cannot overflow; no division anywhere.
    /// The caller applies it per unit, oldest first, stopping at the first failure (story 007/008).
    /// </summary>
    /// <param name="balance">The team balance at the apply tick.</param>
    /// <param name="penalty">The unit's F7 penalty.</param>
    /// <param name="floor">The lowest balance a misdelivery may reach (Economy-owned, for example -500).</param>
    /// <returns>True when the unit may be consumed.</returns>
    /// <example>
    /// <code>
    /// bool a = MailPay.WalletGuard(Cents.FromCents(-496, ref sink), Cents.FromCents(4, ref sink), Cents.FromCents(-500, ref sink));   // true
    /// bool b = MailPay.WalletGuard(Cents.FromCents(-500, ref sink), Cents.FromCents(1, ref sink), Cents.FromCents(-500, ref sink));   // false
    /// </code>
    /// </example>
    public static bool WalletGuard(Cents balance, Cents penalty, Cents floor)
    {
        return balance.Value - penalty.Value >= floor.Value;
    }

    /// <summary>
    /// F10 value-hint payout: the sum over runs of <c>Count x UnitPay(unit of the run)</c>, each unit rounded once by
    /// <see cref="UnitPay"/> (ADR-0016 section 3: the preview is the sum of per-unit pays, never one rounding over a total).
    /// <c>Count</c> copies of an identical unit round identically, so <c>Count x pay</c> equals the sum of the per-unit pays. The
    /// timeliness of each run comes from <paramref name="currentShift"/> and the run's deadline, so recomputing at a new shift
    /// needs no new hint input. Reads the sheet once per run.
    /// </summary>
    /// <param name="runs">The entry's runs (at most 5).</param>
    /// <param name="currentShift">The shift at the apply tick.</param>
    /// <param name="kind">The mail kind index of the entry.</param>
    /// <param name="lateValueRatioPm">The late-value ratio in permille (<see cref="LateValueRatioPm"/>).</param>
    /// <param name="deadLetterShifts">The lateness at which mail pays 0.</param>
    /// <param name="thrownPm">The thrown-correct-pay multiplier in permille (1000 when not thrown).</param>
    /// <param name="s">The stat sheet.</param>
    /// <param name="p">The paying player, passed through to <see cref="UnitPay"/>.</param>
    /// <param name="sink">Counter sink for numerics clamps (last parameter by convention).</param>
    /// <returns>The payout in cents.</returns>
    /// <example>
    /// <code>
    /// Cents payout = MailPay.HintPayout(runs, 2, letterKind, 500, 2, 1000, sheet, null, ref sink);   // 30 for [(1,3,8),(2,2,9)]
    /// </code>
    /// </example>
    public static Cents HintPayout(
        ReadOnlySpan<HintRun> runs,
        int currentShift,
        DefIndex kind,
        int lateValueRatioPm,
        int deadLetterShifts,
        int thrownPm,
        IStatSheet s,
        PlayerSlot? p,
        ref SimCounterSink sink)
    {
        long total = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            HintRun run = runs[i];
            var unit = new UnitFacts(kind, ItemCategory.Mail, AddressId.None, run.UnitSpawnValue, run.DeadlineShift, MailFlags.None);
            int t = Timeliness(currentShift, run.DeadlineShift, lateValueRatioPm, deadLetterShifts);
            Cents pay = UnitPay(in unit, t, thrownPm, s, p, ref sink);
            total += run.Count * pay.Value;
        }

        return Cents.FromCents(total, ref sink);
    }

    /// <summary>
    /// F10 one-unit penalty: <see cref="UnitPenalty"/> of the first run's unit, or 0 when there are no runs.
    /// </summary>
    /// <param name="runs">The entry's runs (at most 5).</param>
    /// <param name="kind">The mail kind index of the entry.</param>
    /// <param name="misdeliveryPenaltyRatioPm">The penalty share of value in permille.</param>
    /// <param name="minPenaltyCents">The defensive floor in cents.</param>
    /// <param name="s">The stat sheet.</param>
    /// <param name="sink">Counter sink for numerics clamps (last parameter by convention).</param>
    /// <returns>The penalty for misdelivering one unit, in cents.</returns>
    /// <example>
    /// <code>Cents oneUnit = MailPay.HintPenalty(runs, letterKind, 500, 1, sheet, ref sink);   // 4 for a first run valued 8</code>
    /// </example>
    public static Cents HintPenalty(
        ReadOnlySpan<HintRun> runs,
        DefIndex kind,
        int misdeliveryPenaltyRatioPm,
        int minPenaltyCents,
        IStatSheet s,
        ref SimCounterSink sink)
    {
        if (runs.Length == 0)
        {
            return default;
        }

        HintRun first = runs[0];
        var unit = new UnitFacts(kind, ItemCategory.Mail, AddressId.None, first.UnitSpawnValue, first.DeadlineShift, MailFlags.None);
        return UnitPenalty(in unit, misdeliveryPenaltyRatioPm, minPenaltyCents, s, ref sink);
    }
}
