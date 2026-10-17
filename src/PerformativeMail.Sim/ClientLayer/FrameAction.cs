namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The Sim-visible per-frame actions (ADR-0012 section 4). The value is the action's bit index in
/// <see cref="InputFrame.HeldBits"/>: <c>HeldBits</c> bit <c>n</c> is <c>(FrameAction)n</c>. Values are explicit and
/// append-only; never renumber or reuse one. The range is 0 to <see cref="FrameActions.MaxCount"/> - 1 (63), because
/// the held state is a single <see cref="ulong"/>. Actions that are not <c>Frame</c> delivery never get a value here.
/// </summary>
/// <example><code>bool aiming = (frame.HeldBits &amp; FrameActions.Bit(FrameAction.Aim)) != 0;</code></example>
public enum FrameAction : byte
{
    /// <summary>Interact (pick up, open, deliver); Press/Release grammar, never coalesced.</summary>
    Interact = 0,

    /// <summary>Jump.</summary>
    Jump = 1,

    /// <summary>Sprint.</summary>
    Sprint = 2,

    /// <summary>Aim (raise the throw aim); Press/Release grammar, never coalesced.</summary>
    Aim = 3,

    /// <summary>Hotbar step (wheel, D-pad left / right): a stepped action; the step count travels in the frame's step fields.</summary>
    HotbarStep = 4,
}

/// <summary>Pure helpers over <see cref="FrameAction"/> and the held-bit mapping.</summary>
/// <example><code>ulong held = FrameActions.Bit(FrameAction.Sprint) | FrameActions.Bit(FrameAction.Aim);</code></example>
public static class FrameActions
{
    /// <summary>Number of representable actions: <see cref="InputFrame.HeldBits"/> is 64 bits wide, so action ids are 0 to 63.</summary>
    public const int MaxCount = 64;

    /// <summary>The <see cref="InputFrame.HeldBits"/> bit for <paramref name="action"/>; 0 for an id of 64 or more (no bit exists).</summary>
    /// <param name="action">The action.</param>
    /// <returns><c>1UL &lt;&lt; action</c>, or 0 when <paramref name="action"/> is outside 0 to 63.</returns>
    /// <example><code>ulong jump = FrameActions.Bit(FrameAction.Jump);   // 0b10</code></example>
    public static ulong Bit(FrameAction action)
    {
        return (byte)action < MaxCount ? 1UL << (byte)action : 0UL;
    }
}
