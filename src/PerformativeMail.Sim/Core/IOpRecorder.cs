using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// Optional observer of the Sim's inputs (ADR-0006 section 7), bound with <see cref="SimWorldBuilder.BindRecorder"/>. Called on the
/// Sim tick thread (or the enqueueing thread, which is the tick thread); implementations must not allocate on the tick
/// path and must not call back into the world. <see cref="SimWorld.EnqueueOp"/> calls <see cref="OnEnqueued"/>, the phase-0
/// drain calls <see cref="OnDrained"/>, the tick prologue calls <see cref="OnSample"/> and phase 13 calls
/// <see cref="OnOverflow"/>. The in-memory implementation is <see cref="Replay.InMemoryRecorder"/>.
/// </summary>
/// <example>
/// <code>
/// sealed class Counting : IOpRecorder
/// {
///     public int Drained;
///     public void OnEnqueued(uint boundaryTick, in Op op) { }
///     public void OnDrained(uint applyTick, in Op op, in Reply reply) => Drained++;
///     public void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample) { }
///     public void OnOverflow(uint tick) { }
/// }
/// </code>
/// </example>
public interface IOpRecorder
{
    /// <summary>An op was accepted into the queue (after <c>opSeq</c> was stamped); never called for a refused enqueue.</summary>
    /// <param name="boundaryTick">
    /// The tick whose phase-0 drain first sees the op: <c>CurrentTick</c> between ticks and before the drain of the tick in
    /// progress (a phase-0 hook), <c>CurrentTick + 1</c> for an enqueue made after that drain began (a handler or a later phase).
    /// </param>
    /// <param name="op">The stamped op.</param>
    void OnEnqueued(uint boundaryTick, in Op op);

    /// <summary>A queued op was drained in phase 0 and answered.</summary>
    /// <param name="applyTick">The tick whose phase 0 applied the op.</param>
    /// <param name="op">The op as drained.</param>
    /// <param name="reply">The reply appended to the event log (a Core reject or the handler's).</param>
    void OnDrained(uint applyTick, in Op op, in Reply reply);

    /// <summary>
    /// A transform sample was latched in the tick prologue (called once per accepted sample, in latch order: slot then sequence;
    /// stale samples are not reported). Called before phase 0 of <paramref name="receivedTick"/>.
    /// </summary>
    /// <param name="receivedTick">The tick at which the sample was latched.</param>
    /// <param name="slot">The sender's slot.</param>
    /// <param name="sample">The sample.</param>
    void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample);

    /// <summary>The event log overflowed this tick (called once, in phase 13 after delivery, when <see cref="OverflowInfo.Overflow"/> is set).</summary>
    /// <param name="tick">The tick.</param>
    void OnOverflow(uint tick);
}

/// <summary>The default recorder: does nothing, so the drain never null-checks.</summary>
internal sealed class NullOpRecorder : IOpRecorder
{
    /// <summary>The shared instance.</summary>
    public static NullOpRecorder Instance { get; } = new();

    public void OnEnqueued(uint boundaryTick, in Op op)
    {
    }

    public void OnDrained(uint applyTick, in Op op, in Reply reply)
    {
    }

    public void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample)
    {
    }

    public void OnOverflow(uint tick)
    {
    }
}
