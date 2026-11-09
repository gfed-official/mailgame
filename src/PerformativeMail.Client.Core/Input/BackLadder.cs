using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The sink of Back (Esc, pad B) and Pause (P, pad Menu) at M-1a (WP-F; GDD input-controls R5 Back ladder, shortened): a Back press first
/// cancels a running aim, then a toggled Interact hold (the router's own ladder steps); only when neither ran does it reach the end of
/// the ladder, the pause toggle. A Pause press toggles at once. The panel steps of the ladder are not built (the panel stays closed).
/// Presses only; releases are ignored.
/// </summary>
/// <param name="router">The router whose ladder steps run first.</param>
/// <param name="togglePause">Pauses when running, resumes when paused (the host shell's call).</param>
/// <example><code>var back = new BackLadder(router, () => paused = !paused); router.RegisterSink(ActionId.Back, back); router.RegisterSink(ActionId.Pause, back);</code></example>
public sealed class BackLadder(InputRouter router, Action togglePause) : IImmediateActionSink
{
    private readonly InputRouter _router = router ?? throw new ArgumentNullException(nameof(router));
    private readonly Action _togglePause = togglePause ?? throw new ArgumentNullException(nameof(togglePause));

    /// <summary>Presses that reached the pause toggle.</summary>
    public int Toggles { get; private set; }

    /// <inheritdoc />
    public void OnImmediate(in ImmediateAction a)
    {
        if (a.EdgeKind != EdgeKind.Press)
        {
            return;
        }

        if (a.ActionId == ActionId.Back && (_router.TryCancelAim() || _router.TryCancelToggledHold()))
        {
            return;
        }

        Toggles++;
        _togglePause();
    }
}
