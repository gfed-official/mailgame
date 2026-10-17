namespace PerformativeMail.Sim.Core;

/// <summary>
/// An open call scope (ADR-0008 section 5), returned by <see cref="EventLog.BeginCall"/>. A <c>ref struct</c> held in
/// a <c>using</c> declaration: allocation-free. Entries raised while it is the innermost scope carry its
/// <see cref="EventEntry.CallSeq"/>; <see cref="Dispose"/> restores the outer scope's. Disposing twice is harmless, and so
/// is disposing a copy of the scope or a scope that outlived <c>EventLog.Reset</c>: the log closes a scope only when it is
/// still the innermost one of the same tick, so a stale scope can neither pop another scope nor restore a stale
/// <c>CallSeq</c>.
/// Writing <c>log.BeginCall();</c> as a discarded statement compiles but never closes the scope: Sim Core asserts
/// (<c>SIM_ASSERT</c>) that the depth is back to 0 after every phase run.
/// </summary>
/// <example>
/// <code>
/// using EventCallScope scope = log.BeginCall();
/// log.TryRaise(EventKind.OpReply, 0, in payload);
/// </code>
/// </example>
public ref struct EventCallScope
{
    private EventLog? _log;
    private readonly uint _outerCallSeq;
    private readonly uint _callSeq;
    private readonly uint _epoch;

    internal EventCallScope(EventLog log, uint outerCallSeq, uint callSeq, uint epoch)
    {
        _log = log;
        _outerCallSeq = outerCallSeq;
        _callSeq = callSeq;
        _epoch = epoch;
    }

    /// <summary>Closes the scope and restores the outer scope's <c>CallSeq</c>.</summary>
    public void Dispose()
    {
        EventLog? log = _log;
        if (log is null)
        {
            return;
        }

        _log = null;
        log.EndCall(_outerCallSeq, _callSeq, _epoch);
    }
}
