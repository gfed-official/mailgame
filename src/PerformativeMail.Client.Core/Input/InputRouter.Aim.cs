using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Throw aim-release (story 014; ADR-0012 sections 3 and 6; GDD R3, F11, G1, G2, G7). A live Throw press that
/// <see cref="IThrowAimView"/> does not refuse enters the router-owned <c>aiming</c> layer, sets the Aim held bit and queues an
/// Aim Press; it reserves the closing edge and the commit immediate, so neither can be dropped. The aim ends once: a release with
/// <c>aimHeld ≥ throwMinAimMs</c> on the ms clock (never scaled by <c>holdSetting</c>) queues an Aim Release and the Throw
/// commit immediate (<see cref="EdgeKind.Release"/>, dispatched by <see cref="DispatchImmediates"/> before
/// <see cref="FlushToLatch"/>); an earlier release, including a press and release stamped in one frame, queues a Cancel and a
/// <see cref="CueId.HoldToAim"/> cue; Back, a state change, focus or device loss, a stale entry or a pending commit cancel it with
/// their reason, and a release still owed is swallowed. In Toggle mode the first press's release emits nothing and a second
/// press commits, or is ignored before <c>throwMinAimMs</c>. Holding nothing live while aiming keeps the selected entry fixed:
/// the router never knows the slot, and the commit carries none.
/// </summary>
public sealed partial class InputRouter
{
    // Transition cause of an aim ended by the router itself (Back, focus or device loss, a stale entry, a pending commit).
    private const byte CauseAimCancelled = 3;

    private readonly IThrowAimView _aimView;
    private readonly AimMachine _aim = new();
    private ushort _aimAction;

    /// <summary>
    /// Back ladder step (story 016): while aiming, cancels the aim with <c>Cancel(Back)</c> and no throw, and returns true so the
    /// Back press is consumed; otherwise false.
    /// </summary>
    /// <returns>True when an aim was cancelled.</returns>
    internal bool TryCancelAim() => CancelAim(AimCancelReason.Back);

    /// <summary>
    /// A router-origin aim cancel (G2; stories 015, 016 and 018 call it): queues <c>Cancel(reason)</c> and no throw, mutes a
    /// Hold-mode press so its release is swallowed, and clears <c>aiming</c>.
    /// </summary>
    /// <param name="reason">Why: Back, FocusLost, DeviceLost, EntryStale or Busy.</param>
    /// <returns>True when an aim was cancelled; false when not aiming.</returns>
    internal bool CancelAim(AimCancelReason reason)
    {
        if (!_aim.Active)
        {
            return false;
        }

        EndAim(EdgeKind.Cancel, reason, _clock.NowMs, CauseAimCancelled);
        return true;
    }

    // G2 "entry gone or stale": a change of EntryStale to true while aiming cancels the aim.
    private void SyncAim(long timeMs)
    {
        if (!_aim.Active)
        {
            return;
        }

        bool stale = _aimView.EntryStale;
        bool rose = stale && !_aim.LastStale;
        _aim.LastStale = stale;
        if (rose)
        {
            EndAim(EdgeKind.Cancel, AimCancelReason.EntryStale, timeMs, CauseAimCancelled);
        }
    }

    // A live press of a commit-on-release Aim-release action (Throw). While aiming, a Toggle aim's second press commits (or is
    // ignored before throwMinAimMs) and any other press is muted; otherwise a refusal mutes the press with a cue, and a press that
    // can reserve its closing edge and commit immediate enters aiming.
    private void PressAim(int index, ref PressMachine m, in RawInputRecord r)
    {
        if (_aim.Active)
        {
            if (!_aim.Toggle)
            {
                m.Phase = PressPhase.Muted;
                Log(r.ArrivalMs, LogKind.GateMuted, in m, r.Input, _state);
                return;
            }

            m.Phase = PressPhase.Fired;
            Log(r.ArrivalMs, LogKind.GateFired, in m, r.Input, _state);
            if (AimMachine.Commits(_aim.PressMs, r.ArrivalMs, Settings.ThrowMinAimMs))
            {
                EndAim(EdgeKind.Release, AimCancelReason.EarlyRelease, r.ArrivalMs, CauseAction);
            }

            return;
        }

        m.Phase = PressPhase.Muted;
        byte notThrowable = _aimView.NotThrowable;
        if (notThrowable != 0)
        {
            _log.Write(r.ArrivalMs, LogKind.Cue, (byte)CueId.NotThrowable, m.Match.Action, (ushort)WordingOf(m.Device), _state, notThrowable);
            return;
        }

        Log(r.ArrivalMs, LogKind.GateFired, in m, r.Input, _state);
        if (!_immediates.TryReserve())
        {
            _log.Write(r.ArrivalMs, LogKind.Dropped, (byte)EdgeKind.Press, m.Match.Action, (ushort)r.Input, _state, (byte)DropCause.ImmediateQueueFull);
            return;
        }

        if (!_edges.TryPushPress(m.Bit, m.StateId, m.Device, opensClosingEdge: true))
        {
            _immediates.Unreserve();
            _log.Write(r.ArrivalMs, LogKind.Dropped, (byte)EdgeKind.Press, (ushort)m.Bit, (ushort)r.Input, _state, (byte)DropCause.EdgeRingFull);
            return;
        }

        bool toggle = Settings.HoldModeThrowAim == HoldMode.Toggle;
        _held.AddAction(m.Bit);
        _aim.Start(toggle ? -1 : index, m.Device, r.ArrivalMs, _aimView.EntryStale);
        _aimAction = m.Match.Action;
        m.Phase = toggle ? PressPhase.Toggled : PressPhase.Aiming;
        SetAiming(true, r.ArrivalMs, CauseAction, _aimAction);
    }

    // The release of a Hold-mode aim's press (its machine is already reset): F11 decides commit or "Hold to aim" cancel.
    private void ReleaseAim(long timeMs)
    {
        bool commit = AimMachine.Commits(_aim.PressMs, timeMs, Settings.ThrowMinAimMs);
        EndAim(commit ? EdgeKind.Release : EdgeKind.Cancel, AimCancelReason.EarlyRelease, timeMs, CauseAction);
    }

    // G3 for the aim, after the press machines: an aim that a state change left non-live or not aiming (a Toggle aim, whose press
    // may be up, or a test hook clearing aiming) is cancelled. A Hold-mode aim made non-live was closed with its machine.
    private void OnAimStateChanged(ushort newState)
    {
        if (_aim.Active && (!RouterStateInfo.IsAiming(newState) || !_map.Get(_aimAction)!.IsLive(newState)))
        {
            int slot = _aim.Slot;
            CloseAim(EdgeKind.Cancel, AimCancelReason.StateChange, newState, _clock.NowMs);
            MuteAimMachine(slot, newState);
        }
    }

    // Ends the aim in the current state: queues its closing edge (and commit or cue), mutes a press still down, clears aiming.
    private void EndAim(EdgeKind kind, AimCancelReason reason, long timeMs, byte cause)
    {
        int slot = _aim.Slot;
        CloseAim(kind, reason, _state, timeMs);
        MuteAimMachine(slot, _state);
        SetAiming(false, timeMs, cause, _aimAction);
    }

    // Clears the Aim bit and queues the closing edge the aim press reserved: Release plus the reserved commit immediate, or
    // Cancel(reason) with the reservation given back (and the "Hold to aim" cue for an early release).
    private void CloseAim(EdgeKind kind, AimCancelReason reason, ushort stateId, long timeMs)
    {
        DeviceClass device = _aim.Device;
        _aim.Clear();
        _held.RemoveAction(FrameAction.Aim);
        if (kind == EdgeKind.Release)
        {
            _edges.PushClosing(FrameAction.Aim, EdgeKind.Release, EdgeReason.None, stateId, device);
            ushort seq = _presented.TryGet(out PresentedPrompt prompt) ? prompt.Seq : (ushort)0;
            _immediates.PushClosing(new ImmediateAction(_aimAction, EdgeKind.Release, timeMs, stateId, seq, ImmediateFlags.None));
            return;
        }

        _edges.PushClosing(FrameAction.Aim, EdgeKind.Cancel, AimMachine.EdgeReasonOf(reason), stateId, device);
        _immediates.Unreserve();
        if (reason == AimCancelReason.EarlyRelease)
        {
            _log.Write(timeMs, LogKind.Cue, (byte)CueId.HoldToAim, _aimAction, (ushort)WordingOf(device), stateId);
        }
    }

    // A Hold-mode aim's press that is still down is muted, so its release is swallowed.
    private void MuteAimMachine(int slot, ushort stateId)
    {
        if (slot < 0 || _machines[slot].Phase != PressPhase.Aiming)
        {
            return;
        }

        ref PressMachine m = ref _machines[slot];
        m.Phase = PressPhase.Muted;
        Log(_clock.NowMs, LogKind.GateMuted, in m, DeviceSlots.InputOf(slot), stateId);
    }

    // Sets or clears the router-owned aiming layer as a transition.
    private void SetAiming(bool aiming, long timeMs, byte cause, ushort trigger)
    {
        _ = RouterStateInfo.Unpack(_state, out CaptureLayer c, out OverlayLayer o, out BodyLayer b, out bool h, out _, out SubmenuLayer s);
        ChangeState(Compose(c, o, b, h, aiming, s), timeMs, cause, trigger);
    }

    private static CueWording WordingOf(DeviceClass device) => device == DeviceClass.Pad ? CueWording.Pad : CueWording.Kbm;
}
