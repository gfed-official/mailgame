namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The push side of the <see cref="InputLatch"/>, as the router sees it (ADR-0012 section 4). Defined in the Sim so
/// Client.Core depends on the Sim and not the reverse. The latch only latches: it never dispatches a verb and never
/// resolves an edge. Not thread-safe: push and step on the same thread, between ticks. Pushing allocates 0 B.
/// <para>
/// Edge overflow policy (see <see cref="InputLatch"/>): more than 32 undelivered edges coalesce same-action level
/// presses first, then drop the newest Press edges; a Release or Cancel is never dropped.
/// </para>
/// </summary>
/// <example>
/// <code>
/// IInputLatchWriter w = layer.InputWriter;
/// w.PushEdge(new InputEdge(FrameAction.Jump, EdgeKind.Press, arrivalSeq: 1, routerStateId: 1));
/// w.PushSteps(FrameAction.Interact, 2);
/// w.SetState(new InputState { HeldBits = FrameActions.Bit(FrameAction.Sprint) });
/// </code>
/// </example>
public interface IInputLatchWriter
{
    /// <summary>Latches one edge behind the ones already latched. Edges must be pushed in <c>ArrivalSeq</c> order.</summary>
    /// <param name="e">The edge, arrival-stamped and gated by the router.</param>
    /// <example><code>w.PushEdge(new InputEdge(FrameAction.Aim, EdgeKind.Release, 7, 1));</code></example>
    void PushEdge(in InputEdge e);

    /// <summary>Adds <paramref name="n"/> signed steps to the action's accumulator (saturating at -127 to 127).</summary>
    /// <param name="a">The action.</param>
    /// <param name="n">The signed steps to add (the router has applied the per-action cap).</param>
    /// <example><code>w.PushSteps(FrameAction.Interact, -1);</code></example>
    void PushSteps(FrameAction a, int n);

    /// <summary>Replaces the continuous state; the latest call before a tick wins.</summary>
    /// <param name="s">The state.</param>
    /// <example><code>w.SetState(new InputState { LookYaw = 0x2AAA });</code></example>
    void SetState(in InputState s);
}
