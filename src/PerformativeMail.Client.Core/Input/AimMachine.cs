using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input;

/// <summary>Why the router ended a throw aim without a throw (story 014; GDD G2, F11). Maps onto <see cref="EdgeReason"/>.</summary>
internal enum AimCancelReason : byte
{
    /// <summary>Released (or, in Toggle mode, never committed) before <c>throwMinAimMs</c>: the "Hold to aim" cancel.</summary>
    EarlyRelease = 0,

    /// <summary>The Back ladder (story 016).</summary>
    Back = 1,

    /// <summary>A state change made Throw non-live, or cleared <c>aiming</c>.</summary>
    StateChange = 2,

    /// <summary>The window lost focus (story 018).</summary>
    FocusLost = 3,

    /// <summary>The aiming device was lost (story 018).</summary>
    DeviceLost = 4,

    /// <summary><see cref="IThrowAimView.EntryStale"/> became true: the entry is gone or stale.</summary>
    EntryStale = 5,

    /// <summary>A commit from another input became pending (story 015).</summary>
    Busy = 6,
}

/// <summary>
/// The one throw aim (story 014; ADR-0012 section 3, GDD F11 and G7): the press time on the ms clock, whether it is a Toggle aim,
/// the press machine that holds it (Hold mode) and the last <see cref="IThrowAimView.EntryStale"/> it saw. The aim owns the Aim
/// held bit, the closing edge and the reserved commit immediate; the router queues them. Never timed: F11 compares arrival
/// stamps when the aim ends. 0 B per call.
/// </summary>
internal sealed class AimMachine
{
    /// <summary>True while aiming.</summary>
    public bool Active { get; private set; }

    /// <summary>True for a Toggle-mode aim: the first press's release emits nothing and a second press commits.</summary>
    public bool Toggle { get; private set; }

    /// <summary>The press machine index of a Hold-mode aim; -1 for a Toggle aim.</summary>
    public int Slot { get; private set; } = -1;

    /// <summary>The device the aim started on (stamped on its closing edge).</summary>
    public DeviceClass Device { get; private set; }

    /// <summary>The arrival time of the press that started the aim.</summary>
    public long PressMs { get; private set; }

    /// <summary>The <see cref="IThrowAimView.EntryStale"/> value last seen; only a change to true cancels.</summary>
    public bool LastStale { get; set; }

    /// <summary>F11: <c>aimHeld ≥ throwMinAimMs</c>, with <c>aimHeld</c> the ms between the two arrival stamps. Never scaled.</summary>
    /// <param name="pressMs">The arrival time of the press that started the aim.</param>
    /// <param name="endMs">The arrival time of the release (Toggle mode: the second press).</param>
    /// <param name="minAimMs">The <c>throwMinAimMs</c> setting.</param>
    /// <returns>True when the aim commits.</returns>
    public static bool Commits(long pressMs, long endMs, int minAimMs) => endMs - pressMs >= minAimMs;

    /// <summary>The edge reason a cancel carries: the early-release cancel is a plain <see cref="EdgeKind.Cancel"/> (its cue says why).</summary>
    /// <param name="reason">The cancel reason.</param>
    /// <returns>The edge reason.</returns>
    public static EdgeReason EdgeReasonOf(AimCancelReason reason) => reason switch
    {
        AimCancelReason.Back => EdgeReason.Back,
        AimCancelReason.StateChange => EdgeReason.StateChange,
        AimCancelReason.FocusLost => EdgeReason.FocusLost,
        AimCancelReason.DeviceLost => EdgeReason.DeviceLost,
        AimCancelReason.EntryStale => EdgeReason.EntryStale,
        AimCancelReason.Busy => EdgeReason.Busy,
        _ => EdgeReason.None,
    };

    /// <summary>Starts the aim.</summary>
    /// <param name="slot">The press machine index (Hold mode), or -1 (Toggle mode).</param>
    /// <param name="device">The pressing device.</param>
    /// <param name="pressMs">The press arrival time.</param>
    /// <param name="stale">The current <see cref="IThrowAimView.EntryStale"/> (a flag left over from an earlier aim never cancels).</param>
    public void Start(int slot, DeviceClass device, long pressMs, bool stale)
    {
        Active = true;
        Toggle = slot < 0;
        Slot = slot;
        Device = device;
        PressMs = pressMs;
        LastStale = stale;
    }

    /// <summary>Ends the aim (the caller queued its closing edge and immediate).</summary>
    public void Clear()
    {
        Active = false;
        Toggle = false;
        Slot = -1;
        LastStale = false;
    }
}
