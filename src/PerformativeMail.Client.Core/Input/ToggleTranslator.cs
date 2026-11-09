using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The one open Toggle-mode Interact hold (story 012; GDD G7; ADR-0012 section 3). A press on a presented Hold target in Toggle
/// mode opens it (a logical Press whose physical release emits nothing); it owns the one closing edge that press reserved, and
/// it is closed by a second press from any device (<c>Release(ToggleSecondPress)</c>), by Back (<c>Cancel(Back)</c>), by a state
/// change (<c>Cancel(StateChange)</c>), or by a plain <c>Release</c> once the Sim has ended the hold it started (complete or
/// cancelled), which is the edge a Hold-mode player gives by letting go. Never timed: "running" is read from the published
/// <see cref="InteractState"/>. 0 B per call.
/// </summary>
internal sealed class ToggleTranslator
{
    private bool _seenRunning;

    /// <summary>True while a toggled press owes its closing edge.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The device the toggled press came from (stamped on its closing edge).</summary>
    public DeviceClass Device { get; private set; }

    /// <summary>The states a second press closes: a hold or channel in progress, or the declared Toggle-armed state.</summary>
    /// <param name="state">The published state.</param>
    /// <returns>True when running.</returns>
    public static bool IsRunning(InteractState state) =>
        state is InteractState.Holding or InteractState.Channel or InteractState.ToggleArmed;

    /// <summary>Whether a fresh Interact press is toggled: Deliver class in Toggle mode and a presented Hold target.</summary>
    /// <param name="mode">The Deliver hold class mode.</param>
    /// <param name="hasPrompt">True when a prompt with a target was presented.</param>
    /// <param name="prompt">The presented prompt.</param>
    /// <returns>True to toggle; Tap and Channel targets and an empty prompt are never toggled.</returns>
    public static bool ShouldToggle(HoldMode mode, bool hasPrompt, in PresentedPrompt prompt) =>
        mode == HoldMode.Toggle && hasPrompt && prompt.Mode == InteractMode.Hold;

    /// <summary>Opens the toggled hold.</summary>
    /// <param name="device">The pressing device.</param>
    public void Open(DeviceClass device)
    {
        IsOpen = true;
        Device = device;
        _seenRunning = false;
    }

    /// <summary>Closes it (the caller queued the closing edge).</summary>
    public void Close()
    {
        IsOpen = false;
        _seenRunning = false;
    }

    /// <summary>Reads the published state; true when the Sim has ended the hold this toggle started, so the router releases it.</summary>
    /// <param name="state">The published state.</param>
    /// <returns>True when the open toggle should now be released.</returns>
    public bool HasEnded(InteractState state)
    {
        if (!IsOpen)
        {
            return false;
        }

        if (IsRunning(state))
        {
            _seenRunning = true;
            return false;
        }

        return _seenRunning;
    }
}
