namespace PerformativeMail.Sim.Core;

/// <summary>
/// The single consumer of a tick's event log, called exactly once in phase 13 (ADR-0008 section 7), bound with
/// <see cref="SimWorldBuilder.BindEventSink"/> by the host adapter at composition. Not a subscriber: it is called
/// after every phase, never synchronously from a raise.
/// <para>
/// Implementations are copy-only. They must copy what they need into preallocated buffers and resolve masks to peers
/// inside the call. They must never keep <paramref name="entries"/> (it is overwritten with <c>0xDD</c> in
/// <c>SIM_ASSERT</c> builds, and the log is reused next tick), and must never call the transport, Godot or the Sim:
/// during <c>Deliver</c> <c>EnqueueOp</c> returns false (counted), a nested <c>Tick</c> runs nothing (counted) and raises
/// are dropped; <c>SIM_ASSERT</c> builds throw <see cref="Primitives.SimAssertException"/> at the first two.
/// An exception escaping <c>Deliver</c> faults the world (same as an exception escaping a phase): the log is reset
/// without further delivery, and <c>SIM_ASSERT</c> builds rethrow it from <c>Tick()</c>.
/// </para>
/// </summary>
/// <example>
/// <code>
/// sealed class CopySink : IEventSink
/// {
///     public void Deliver(uint tick, ReadOnlySpan&lt;EventEntry&gt; entries, in OverflowInfo overflow)
///     {
///         entries.CopyTo(_buffer);   // preallocated
///     }
/// }
/// </code>
/// </example>
public interface IEventSink
{
    /// <summary>Receives the whole tick's log: every entry, <c>Internal</c> included, in raise order (<c>Ordinal</c> 0 to n-1).</summary>
    /// <param name="tick">The tick that just ran.</param>
    /// <param name="entries">The entries; valid only until this call returns.</param>
    /// <param name="overflow">What was dropped this tick. A live <c>in</c> reference to the log's accumulator, not a snapshot: a misuse raise made inside <c>Deliver</c> (Release drops and flags it) updates it while the sink runs.</param>
    void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow);
}
