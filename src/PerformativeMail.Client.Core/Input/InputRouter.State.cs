using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Router state transitions (story 008; ADR-0012 section 2, GDD R2 and States and Transitions). The router owns <c>capture</c>,
/// <c>overlay</c>, <c>submenu</c> and <c>aiming</c>; <c>body</c> and <c>holding</c> follow the published carrier status, read at
/// event time and at every flush. Every change runs G3 (<see cref="OnStateChanged"/>), logs a <see cref="LogKind.Transition"/>
/// and recomputes <see cref="MouseModeRequest"/> and <see cref="InGameContext"/>, then raises <see cref="StateChanged"/>.
/// </summary>
public sealed partial class InputRouter
{
    // Transition entries: Reason = cause, IdA = trigger action (None unless the cause is an action), IdB = old state, IdC = new state.
    private const byte CauseAction = 0;
    private const byte CauseCarrierStatus = 1;
    private const byte CauseForced = 2;

    private BodyLayer _statusBody = BodyLayer.OnFoot;
    private bool _statusHolding;

    /// <summary>
    /// The mouse mode for the adapter to apply (story 027): <see cref="Input.MouseModeRequest.Captured"/> in states where Look is
    /// live (World, Holding-closed, aiming), <see cref="Input.MouseModeRequest.Visible"/> otherwise (Panel, Holding-open, Pause).
    /// Changes only on a state change.
    /// </summary>
    public MouseModeRequest MouseModeRequest { get; private set; }

    /// <summary>
    /// True while no capture layer is up (story 027): every play state but TextEntry and Pause, whose text field and menu keep
    /// Godot's GUI focus and stock <c>ui_*</c> navigation. The adapter releases GUI focus whenever a change lands in one.
    /// </summary>
    public bool InGameContext { get; private set; }

    /// <summary>
    /// Raised after every router-state change, once G3 has run and <see cref="State"/>, <see cref="MouseModeRequest"/> and
    /// <see cref="InGameContext"/> hold the new state; never raised without a change. Runs inside <see cref="Feed"/> or
    /// <see cref="FlushToLatch"/>, on their thread (story 027: the adapter applies the mouse mode and GUI focus from it).
    /// </summary>
    public event Action? StateChanged;

    // Re-reads body and holding from the published status (and drops a toggled sprint on death, mount, swim or panel open); a
    // change is a transition. Only a change of the status moves the
    // state, so a ForceState test hook holds until the status next changes.
    private void SyncCarrierStatus(long timeMs)
    {
        ref readonly CarrierStatus s = ref _carrier.Current;
        SyncSprintLatch(in s);
        var body = (BodyLayer)InputContexts.RouterBody(in s);
        bool holding = InputContexts.RouterHolding(in s);
        if (body == _statusBody && holding == _statusHolding)
        {
            return;
        }

        _statusBody = body;
        _statusHolding = holding;
        _ = RouterStateInfo.Unpack(_state, out CaptureLayer c, out OverlayLayer o, out _, out _, out bool a, out SubmenuLayer sub);
        ChangeState(Compose(c, o, body, holding, a, sub), timeMs, CauseCarrierStatus, ActionId.None);
    }

    // Applies the first transition row of the action that just fired, if any.
    private void ApplyTransition(ushort action, long timeMs)
    {
        if (!TransitionTable.TryMatch(action, _state, out OverlayLayer overlay))
        {
            return;
        }

        _ = RouterStateInfo.Unpack(_state, out CaptureLayer c, out _, out BodyLayer b, out bool h, out bool a, out SubmenuLayer s);
        ChangeState(Compose(c, overlay, b, h, a, s), timeMs, CauseAction, action);
    }

    private void ChangeState(ushort newState, long timeMs, byte cause, ushort trigger)
    {
        ushort old = _state;
        if (old == newState)
        {
            return;
        }

        _state = newState;
        _log.Write(timeMs, LogKind.Transition, cause, trigger, old, newState);
        MouseModeRequest = MouseModeOf(newState);
        InGameContext = IsGameContext(newState);
        OnStateChanged(old, newState);
        StateChanged?.Invoke();
    }

    private MouseModeRequest MouseModeOf(ushort stateId) =>
        _look.IsLive(stateId) ? MouseModeRequest.Captured : MouseModeRequest.Visible;

    private static bool IsGameContext(ushort stateId) =>
        RouterStateInfo.Unpack(stateId, out CaptureLayer c, out _, out _, out _, out _, out _) && c == CaptureLayer.None;

    // Packs a tuple, first dropping the router-owned layers the reachable rules forbid beside it: the panel closes when the body
    // leaves OnFoot, a submenu needs the panel, and aiming needs OnFoot with no capture, no overlay and nothing held (so opening
    // the panel clears aiming).
    private static ushort Compose(CaptureLayer c, OverlayLayer o, BodyLayer b, bool holding, bool aiming, SubmenuLayer s)
    {
        if (b != BodyLayer.OnFoot && o == OverlayLayer.Panel)
        {
            o = OverlayLayer.None;
        }

        if (o != OverlayLayer.Panel)
        {
            s = SubmenuLayer.None;
        }

        aiming &= b == BodyLayer.OnFoot && c == CaptureLayer.None && o == OverlayLayer.None && !holding;
        return RouterStateInfo.Pack(c, o, b, holding, aiming, s);
    }
}
