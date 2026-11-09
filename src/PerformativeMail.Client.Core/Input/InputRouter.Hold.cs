using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Hold classes (story 012; ADR-0012 section 3; GDD G1, G7, F8, R4.2 to R4.4). An Interact press is stamped at event time with
/// the target of the <b>presented</b> prompt (<see cref="IPresentedPromptSource"/>), never the live focus, and in Toggle mode on a
/// presented Hold target it is translated by <see cref="ToggleTranslator"/> into logical Press / Release / Cancel. Sprint follows
/// its per-device mode: Hold (the bit is the OR of the held inputs), Toggle (<see cref="SprintLatch"/>, unlatched by F8, death,
/// mount, swimming and panel open) or Auto (set at <c>|moveIntent| ≥ 0.9</c>; a held Sprint input walks). The Sim sees only
/// logical edges and held bits, never a mode, and the router never times a hold: it reads the Sim's
/// <see cref="IInteractionQuery"/> snapshot and copies its hold lines into the log (<see cref="HoldLogMirror"/>).
/// </summary>
public sealed partial class InputRouter
{
    private readonly IInteractionQuery _interaction;
    private readonly IPresentedPromptSource _presented;
    private readonly ActionDef? _interactDef;
    private readonly ActionDef? _sprintDef;
    private readonly HoldLogMirror _holdLog;
    private readonly ToggleTranslator _toggle = new();
    private readonly SprintLatch _sprintLatch = new();
    private int _autoWalkers;

    /// <summary>
    /// Back ladder step 3b (story 016): while a toggled Interact hold is running (the published state is Holding, Channel or
    /// Toggle-armed), queues <c>Cancel(Back)</c> for it and returns true, so the Back press is consumed; otherwise false.
    /// </summary>
    /// <returns>True when a toggled hold was cancelled.</returns>
    internal bool TryCancelToggledHold()
    {
        if (!_toggle.IsOpen || !ToggleTranslator.IsRunning(_interaction.Current.State))
        {
            return false;
        }

        _edges.PushClosing(FrameAction.Interact, EdgeKind.Cancel, EdgeReason.Back, _state, _toggle.Device);
        _toggle.Close();
        return true;
    }

    // Copies the Sim's hold lines into the log, and releases an open toggle once the Sim has ended the hold it started.
    private void SyncInteraction(long timeMs)
    {
        ref readonly InteractionSnapshot s = ref _interaction.Current;
        _holdLog.Observe(in s, timeMs, _state, _log);
        if (_toggle.HasEnded(s.State))
        {
            _edges.PushClosing(FrameAction.Interact, EdgeKind.Release, EdgeReason.None, _state, _toggle.Device);
            _toggle.Close();
        }
    }

    // An Interact press. With a toggled hold open, a press while it runs is its second press: Release(ToggleSecondPress),
    // consumed, never resolved. Otherwise the press is fresh (an open toggle the Sim never ran or already ended is released
    // first) and carries the presented target; it is toggled only in Toggle mode on a presented Hold target.
    private PressPhase BeginInteract(in PressMachine m, out bool dropped)
    {
        dropped = false;
        if (_toggle.IsOpen)
        {
            bool second = ToggleTranslator.IsRunning(_interaction.Current.State);
            _edges.PushClosing(m.Bit, EdgeKind.Release, second ? EdgeReason.ToggleSecondPress : EdgeReason.None, m.StateId, _toggle.Device);
            _toggle.Close();
            if (second)
            {
                return PressPhase.Fired;
            }
        }

        bool hasPrompt = _presented.TryGet(out PresentedPrompt prompt);
        if (!_edges.TryPushPress(m.Bit, m.StateId, m.Device, opensClosingEdge: true, in prompt))
        {
            dropped = true;
            return PressPhase.Muted;
        }

        if (ToggleTranslator.ShouldToggle(Settings.HoldModeDeliver, hasPrompt, in prompt))
        {
            _toggle.Open(m.Device);
            return PressPhase.Toggled;
        }

        return PressPhase.HoldActive;
    }

    // A Sprint press, by the mode of the device it came from.
    private PressPhase BeginSprint(int index, in PressMachine m)
    {
        switch (SprintMode.OfSlot(Settings, index))
        {
            case HoldMode.Toggle:
                if (_sprintLatch.Flip())
                {
                    _held.AddAction(m.Bit);
                }
                else
                {
                    _held.RemoveAction(m.Bit);
                }

                return PressPhase.Fired;
            case HoldMode.Auto:
                _autoWalkers++;
                return PressPhase.Walking;
            default:
                _held.AddAction(m.Bit);
                return PressPhase.Active;
        }
    }

    // G3 for the hold classes: a state where Interact is not live cancels an open toggle, and an Auto walk press stops counting.
    private void OnHoldClassesStateChanged(ushort newState)
    {
        if (_toggle.IsOpen && (_interactDef is null || !_interactDef.IsLive(newState)))
        {
            _edges.PushClosing(FrameAction.Interact, EdgeKind.Cancel, EdgeReason.StateChange, newState, _toggle.Device);
            _toggle.Close();
        }
    }

    // G7 sprint unlatch on the carrier status: death, mounting, swimming or the open panel.
    private void SyncSprintLatch(in CarrierStatus s)
    {
        if (_sprintLatch.Latched
            && (s.Life is LifeState.Dead or LifeState.DeadAway || s.Body is BodyContext.Dead or BodyContext.Mounted || s.Swimming || s.PanelOpen))
        {
            _sprintLatch.Unlatch();
            _held.RemoveAction(FrameAction.Sprint);
        }
    }

    // The held bits a flush pushes: F8 runs on the intent this flush pushes, Auto adds the sprint bit at |moveIntent| ≥ 0.9 while
    // no Auto-mode Sprint input is held, and the bit is masked wherever Sprint is not live (the latch itself survives, e.g. Pause).
    private ulong HeldBitsForFlush(short moveX, short moveY, bool fromPad)
    {
        if (_sprintLatch.Step(_ticks.ClientTick, moveX == 0 && moveY == 0))
        {
            _held.RemoveAction(FrameAction.Sprint);
        }

        ulong bits = _held.ActionBits;
        ulong sprint = FrameActions.Bit(FrameAction.Sprint);
        if (_autoWalkers == 0 && SprintMode.Of(Settings, fromPad) == HoldMode.Auto && SprintMode.ReachesAuto(moveX, moveY))
        {
            bits |= sprint;
        }

        if (_sprintDef is null || !_sprintDef.IsLive(_state))
        {
            bits &= ~sprint;
        }

        return bits;
    }
}
