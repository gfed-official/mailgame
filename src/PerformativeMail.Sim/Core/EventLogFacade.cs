using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// The event-log writer facade a system receives in its constructor (ADR-0003 section 4 injectable list; shape ADR-0008).
/// Systems are constructed before <see cref="SimWorldBuilder.Freeze"/> creates the world's <see cref="EventLog"/>, so the
/// composition root hands out this facade and attaches the log after the freeze; every member then forwards to it
/// (one null check per call, allocation-free). Before attachment nothing can be inside a tick: <see cref="TryRaise{T}"/>
/// behaves like a raise between ticks (asserts under <c>SIM_ASSERT</c>; otherwise drops, counts
/// <see cref="CounterId.EventsOutsideTick"/> and <see cref="CounterId.EventsDropped"/>, returns false),
/// <see cref="Read"/> returns false and <see cref="BeginCall"/> returns an inert scope. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// using EventCallScope scope = events.BeginCall();
/// events.TryRaise(MailEvents.Delivered, audience: 0xFF, in delivered);
/// </code>
/// </example>
public sealed class EventLogFacade
{
    private readonly SimCounters _counters;
    private EventLog? _log;

    /// <summary>Creates an unattached facade (composition root and tests).</summary>
    /// <param name="counters">The world's counters (receives the pre-attachment drop counts).</param>
    internal EventLogFacade(SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(counters);
        _counters = counters;
    }

    /// <summary>True once the world's log is attached (after <see cref="SimWorldBuilder.Freeze"/>).</summary>
    public bool IsAttached => _log is not null;

    /// <summary>Entries in this tick's log (0 before attachment).</summary>
    public int Count => _log?.Count ?? 0;

    /// <summary>The innermost open call scope's sequence number (0 before attachment or outside any scope).</summary>
    public uint CurrentCallSeq => _log?.CurrentCallSeq ?? 0;

    /// <summary>The attached log, for tests and Sim Core (null before attachment).</summary>
    internal EventLog? Log => _log;

    /// <summary>The world's counters this facade counts misuse into (Grid Inventory's probe counters share them, story grid-inventory/009).</summary>
    internal SimCounters Counters => _counters;

    /// <summary>Opens a call scope on the world's log; see <see cref="EventLog.BeginCall"/>. Before attachment: an inert scope.</summary>
    /// <returns>The scope; keep it in a <c>using</c> declaration.</returns>
    /// <example><code>using EventCallScope scope = events.BeginCall();</code></example>
    public EventCallScope BeginCall() => _log is null ? default : _log.BeginCall();

    /// <summary>Raises an event on the world's log; see <see cref="EventLog.TryRaise{T}"/>.</summary>
    /// <typeparam name="T">The kind's payload struct.</typeparam>
    /// <param name="kind">What happened.</param>
    /// <param name="audience">Slot bitmask resolved by the raiser.</param>
    /// <param name="payload">The payload.</param>
    /// <returns>True when appended.</returns>
    /// <example><code>bool ok = events.TryRaise(kind, 0, in payload);</code></example>
    public bool TryRaise<T>(EventKind kind, byte audience, in T payload)
        where T : unmanaged
    {
        EventLog? log = _log;
        if (log is null)
        {
            SimAssert.Fail(SimAssertCode.EventRaiseOutsideTick);
            _counters.Add(CounterId.EventsOutsideTick);
            _counters.Add(CounterId.EventsDropped);
            return false;
        }

        return log.TryRaise(kind, audience, in payload);
    }

    /// <summary>Raises a <c>Continuation</c> chunk on the world's log; see <see cref="EventLog.TryRaiseContinuation{T}"/>. Before attachment: as <see cref="TryRaise{T}"/>.</summary>
    /// <typeparam name="T">The chunk struct.</typeparam>
    /// <param name="kind">The head entry's kind.</param>
    /// <param name="audience">The head entry's audience.</param>
    /// <param name="chunk">The chunk.</param>
    /// <param name="length">Meaningful leading bytes of the chunk.</param>
    /// <returns>True when appended.</returns>
    /// <example><code>bool ok = events.TryRaiseContinuation(kind, audience, in runs, 12);</code></example>
    public bool TryRaiseContinuation<T>(EventKind kind, byte audience, in T chunk, int length)
        where T : unmanaged
    {
        EventLog? log = _log;
        if (log is null)
        {
            SimAssert.Fail(SimAssertCode.EventRaiseOutsideTick);
            _counters.Add(CounterId.EventsOutsideTick);
            _counters.Add(CounterId.EventsDropped);
            return false;
        }

        return log.TryRaiseContinuation(kind, audience, in chunk, length);
    }

    /// <summary>Reads the next entry for a bound pull reader; see <see cref="EventLog.Read"/>. False before attachment.</summary>
    /// <param name="cursor">The reader's cursor.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>True when an entry was returned.</returns>
    /// <example><code>while (events.Read(ref cursor, out EventRef e)) { }</code></example>
    public bool Read(ref EventCursor cursor, out EventRef entry)
    {
        EventLog? log = _log;
        if (log is null)
        {
            entry = default;
            return false;
        }

        return log.Read(ref cursor, out entry);
    }

    /// <summary>Attaches the world's log (once, by the composition root right after the freeze).</summary>
    /// <param name="log">The world's log.</param>
    /// <exception cref="InvalidOperationException">Already attached.</exception>
    internal void Attach(EventLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (_log is not null)
        {
            throw new InvalidOperationException("The event-log facade is already attached.");
        }

        _log = log;
    }
}
