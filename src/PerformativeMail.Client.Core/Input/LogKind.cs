namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// What a <see cref="UiEventEntry"/> records (ADR-0012 section 9). The meaning of <see cref="UiEventEntry.Reason"/> and the
/// three ids depends on the kind and is owned by the writing system. Values are stable and append-only; the debug overlay
/// and HUD format by id, so there are no strings in the ring.
/// </summary>
/// <example><code>if (entry.Kind == LogKind.GateNotLive) { /* a press was dropped because its action is not live */ }</code></example>
public enum LogKind : byte
{
    /// <summary>An unwritten slot (never returned by the reader).</summary>
    None = 0,

    /// <summary>A raw input record arrived (also the coalesced mouse-motion entry; see <see cref="UiEventLog.LogMotion"/>).</summary>
    RawInput = 1,

    /// <summary>A press passed its gate and fired an action.</summary>
    GateFired = 2,

    /// <summary>A press was gated out because its action is not live in the router state.</summary>
    GateNotLive = 3,

    /// <summary>A press was gated out because its action is muted until release.</summary>
    GateMuted = 4,

    /// <summary>A press was swallowed (for example the click that refocuses the window).</summary>
    GateSwallowed = 5,

    /// <summary>The router state changed.</summary>
    Transition = 6,

    /// <summary>A hold started.</summary>
    HoldStart = 7,

    /// <summary>A hold completed.</summary>
    HoldComplete = 8,

    /// <summary>A hold was cancelled; the reason says why.</summary>
    HoldCancel = 9,

    /// <summary>A UI cue was raised (for example "Hold to aim").</summary>
    Cue = 10,

    /// <summary>An input was dropped and counted (overflow, no sink).</summary>
    Dropped = 11,

    /// <summary>A configuration load or change was applied or rejected.</summary>
    Config = 12,
}
