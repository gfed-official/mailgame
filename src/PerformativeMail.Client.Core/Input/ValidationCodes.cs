namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The stable codes a <see cref="ValidationEntry"/> carries (input-controls story 006). Logs and the M1 remap screen key on
/// these strings, so they never change; new codes are appended.
/// </summary>
/// <example><code>bool conflict = report.Errors.Any(e => e.Code == ValidationCodes.ConflictColive);</code></example>
public static class ValidationCodes
{
    /// <summary>A field check of one definition (story 005, H-R1-1).</summary>
    public const string Field = "FIELD";

    /// <summary>One physical input (with its modifiers, after most-specific-chord resolution) reaches two live actions in one reachable state, including an input used both as an action and as another action's chord modifier (GDD R4.1, H-R4-1).</summary>
    public const string ConflictColive = "CONFLICT_COLIVE";

    /// <summary>
    /// A Tap action and a different Hold action share an input in one reachable state, across modifier buckets too (GDD R10:
    /// "a tap-grammar action and a hold-grammar action may never share an input in overlapping states"; Interact is one
    /// action and so the exception, R4.2).
    /// </summary>
    public const string TapHoldShare = "TAP_HOLD_SHARE";

    /// <summary>A chord that is not "one keyboard modifier plus one non-modifier input" (a pad chord, two non-modifiers, a modifier as the pressed input).</summary>
    public const string ChordShape = "CHORD_SHAPE";

    /// <summary>A reserved input on the wrong action: Esc on anything but Back and Pause, Menu on anything but Pause (R10).</summary>
    public const string ReservedInput = "RESERVED_INPUT";

    /// <summary>Movement and the grid cursor share a physical input (WASD and arrows never share a key).</summary>
    public const string MoveCursorShare = "MOVE_CURSOR_SHARE";

    /// <summary>A Back-ladder step binding that is not one of Back's own inputs on that device (R5).</summary>
    public const string LadderStep = "LADDER_STEP";

    /// <summary>An interactable target type without exactly one verb with one allowed grammar (R4.2, H-R4-5).</summary>
    public const string GrammarTarget = "GRAMMAR_TARGET";

    /// <summary>A registered action with no pad binding, no declared pad path and no exemption (H-FX-3a).</summary>
    public const string PadPath = "PAD_PATH";

    /// <summary>Info: one input on actions that are never live together ("Also Pick up in Panel — they never overlap").</summary>
    public const string SharedDisjoint = "SHARED_DISJOINT";
}
