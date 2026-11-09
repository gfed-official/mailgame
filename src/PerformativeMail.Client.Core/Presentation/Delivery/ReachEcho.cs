using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.Delivery.Client;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Presentation.Delivery;

/// <summary>A reach echo to play: the focused destination and the render time of the press (story 011 plays and withdraws it).</summary>
/// <param name="Destination">The focused destination.</param>
/// <param name="StartMs">The <see cref="IRenderClock.RenderSimTimeMs"/> of the press.</param>
public readonly record struct ReachEchoStart(EntityId Destination, long StartMs);

/// <summary>
/// The visual-only reach echo's gate (GDD Game Feel "Tick quantisation"; ADR-0016 section 5; TR-delivery-018). A predictive visual,
/// so not an outcome beat: on the Interact press input event it starts only if <see cref="DeliverPreviewState.CanStartHoldNext"/> was
/// true in the last completed client tick's published state, which it reads and never recomputes.
/// </summary>
/// <example><code>if (echo.TryStart(out ReachEchoStart start)) { view.Reach(in start); }</code></example>
/// <param name="preview">The published deliver preview.</param>
/// <param name="clock">The render clock.</param>
public sealed class ReachEcho(IDeliverPreviewRead preview, IRenderClock clock)
{
    /// <summary>Called on an Interact press: the echo to start, if the last completed tick allowed a hold.</summary>
    /// <param name="start">The echo; default when none starts.</param>
    /// <returns>True when the echo starts.</returns>
    public bool TryStart(out ReachEchoStart start)
    {
        ref readonly DeliverPreviewState state = ref preview.Current;
        start = state.CanStartHoldNext ? new ReachEchoStart(state.Destination, clock.RenderSimTimeMs) : default;
        return state.CanStartHoldNext;
    }
}
