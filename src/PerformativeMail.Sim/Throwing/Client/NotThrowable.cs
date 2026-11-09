namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Why nothing can be thrown right now (GDD T1 "What you throw" and the carrier table; throwing-catching story 004, TC-T1-1 b). Published
/// every tick, aiming or not, so the input router can refuse the aim start; the player-facing strings are story 019's.
/// </summary>
/// <example><code>bool refuse = state.NotThrowable != NotThrowable.None;</code></example>
public enum NotThrowable : byte
{
    /// <summary>The selected entry can be thrown.</summary>
    None = 0,

    /// <summary>The hands slot (hotbar index 0) is selected.</summary>
    HandsSlot = 1,

    /// <summary>The selected hotbar slot is empty.</summary>
    EmptySlot = 2,

    /// <summary>The carrier is swimming.</summary>
    Swimming = 3,
}
