namespace PerformativeMail.Sim.Hosting;

/// <summary>
/// The two per-frame hooks <see cref="FrameRunner"/> calls on the network adapter (ADR-0011 section 2, manifest I-7).
/// Defined by the Sim so it never references the adapter assembly; <c>HostAdapter</c> and <c>ClientAdapter</c> implement it.
/// Transport callbacks only enqueue into the adapter's own inbound queue (TR-sim-008); <see cref="PumpInbound"/> is the
/// only place that queue is drained into <c>EnqueueOp</c> and the sample ring, on the main thread.
/// Implementations must not allocate in steady state. Not thread-safe: called from the main thread only.
/// </summary>
/// <example>
/// <code>
/// sealed class NullTransport : IFrameTransport
/// {
///     public void PumpInbound() { }
///     public void FlushOutbound(uint tick) { }
/// }
/// </code>
/// </example>
public interface IFrameTransport
{
    /// <summary>Frame step 1: drains received messages (remote poll, then loopback poll) into the Sim's queues.</summary>
    void PumpInbound();

    /// <summary>Runs after each tick: sends that tick's outbound batch.</summary>
    /// <param name="tick">The number of the tick that just ran (the world's <c>CurrentTick</c> before the call to <c>Tick()</c>; the client tick for a pure client).</param>
    void FlushOutbound(uint tick);
}
