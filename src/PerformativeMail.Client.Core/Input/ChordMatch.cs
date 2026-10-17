namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The result of <see cref="ChordResolver.TryResolve"/>: the winning action, the modifiers its binding matched (so the router
/// can mark them consumed, story 007) and, when the action is Back, the Back-ladder step bound on the same input.
/// Immutable value type; thread-safe.
/// </summary>
/// <example>
/// <code>
/// if (map.Chords.TryResolve(held, PhysicalInput.PadB, stateId, out ChordMatch m))
/// {
///     ushort action = m.Action;                 // ActionId.Back
///     ushort step = m.LadderStepOwner;          // ActionId.CancelReturn in Holding-open
///     bool chord = m.Modifiers != ModifierMask.None;
/// }
/// </code>
/// </example>
public readonly struct ChordMatch
{
    /// <summary>Creates a match.</summary>
    /// <param name="action">The winning action id.</param>
    /// <param name="modifiers">The modifiers the winning binding needs (a subset of those held).</param>
    /// <param name="ladderStepOwner">The ladder-step owner, or <see cref="ActionId.None"/>.</param>
    public ChordMatch(ushort action, ModifierMask modifiers, ushort ladderStepOwner)
    {
        Action = action;
        Modifiers = modifiers;
        LadderStepOwner = ladderStepOwner;
    }

    /// <summary>The winning <see cref="ActionId"/>. Back-ladder steps resolve to <see cref="ActionId.Back"/>.</summary>
    public ushort Action { get; }

    /// <summary>The modifiers the winning binding matched: None for a plain binding, Shift for Shift+Enter.</summary>
    public ModifierMask Modifiers { get; }

    /// <summary>
    /// When <see cref="Action"/> is Back: the lowest-id live action whose Back-ladder step binding uses the same input and
    /// modifiers (Pause for Esc; Close panel for pad B in Panel; Cancel / return for pad B in Holding-open). Otherwise
    /// <see cref="ActionId.None"/>. Informational: Back's ordered R5 handlers still decide what the press does.
    /// </summary>
    public ushort LadderStepOwner { get; }

    /// <summary>True when a ladder step was found on the matched input.</summary>
    public bool HasLadderStep => LadderStepOwner != ActionId.None;
}
