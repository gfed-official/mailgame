namespace PerformativeMail.Sim.Players;

/// <summary>
/// The eight carrier verbs of the PC GDD P14 table (rows of <see cref="VerbContextTable"/>). Explicit, append-only values. Not
/// the interaction <see cref="Verb"/> catalogue: "Interact" here is the one press that resolves to the focused anchor's verb.
/// </summary>
/// <example><code>CellState c = VerbContextTable.Cell(ContextVerb.Interact, InputContext.Mounted);   // Live (Dismount only)</code></example>
public enum ContextVerb : byte
{
    /// <summary>Move (camera-relative).</summary>
    Move = 0,

    /// <summary>Look.</summary>
    Look = 1,

    /// <summary>Sprint (Hold / Toggle / Auto modes).</summary>
    Sprint = 2,

    /// <summary>Jump.</summary>
    Jump = 3,

    /// <summary>Interact (tap / hold / channel by target).</summary>
    Interact = 4,

    /// <summary>Shoulder swap.</summary>
    ShoulderSwap = 5,

    /// <summary>The comms wheel: pings, Say, emotes.</summary>
    CommsWheel = 6,

    /// <summary>Respawn: automatic, never a press; bound in no context.</summary>
    Respawn = 7,
}

/// <summary>
/// One cell of the verb x context table (story 013 encoding, 2026-10-20). A GDD '—' cell is <see cref="Absent"/>; only a cell the
/// GDD writes as "disabled" is <see cref="Disabled"/>; "emotes greyed" is <see cref="Greyed"/>; cells the gate or router narrow
/// (Dismount only, vehicle's controls, orbit, Look suspended in UI) are <see cref="Live"/>.
/// </summary>
/// <example><code>bool usable = VerbContextTable.Cell(ContextVerb.Sprint, InputContext.Panel) == CellState.Live;   // false</code></example>
public enum CellState : byte
{
    /// <summary>The verb is live in the context.</summary>
    Live = 0,

    /// <summary>The verb is bound but disabled (shown, refused).</summary>
    Disabled = 1,

    /// <summary>The verb does not exist in the context (GDD '—').</summary>
    Absent = 2,

    /// <summary>The verb is live with part of its set greyed (the comms wheel's emotes while Dead).</summary>
    Greyed = 3,
}

/// <summary>The press kinds a <see cref="ContextVerb"/> can carry (for the P14 "no tap and hold on one button" rule).</summary>
/// <example><code>bool both = VerbContextTable.Presses(ContextVerb.Interact) == (VerbPress.Tap | VerbPress.Hold);   // true</code></example>
[Flags]
public enum VerbPress : byte
{
    /// <summary>No press (an axis, or never pressed).</summary>
    None = 0,

    /// <summary>A tap verb.</summary>
    Tap = 1,

    /// <summary>A hold verb (including channel and hold-to-open).</summary>
    Hold = 2,
}

/// <summary>
/// The P14 verb x context table (PC GDD P14; PCM-P14-1), the data Input &amp; Controls' action map validates against. Rows are
/// <see cref="ContextVerb"/>, columns <see cref="InputContext"/>; the matrix is a <c>ReadOnlySpan&lt;byte&gt;</c> literal in the
/// assembly's data section (no allocation). Input &amp; Controls owns bindings, remap, thresholds and the validator.
/// </summary>
/// <example><code>CellState c = VerbContextTable.Cell(ContextVerb.CommsWheel, InputContext.Dead);   // Greyed</code></example>
public static class VerbContextTable
{
    /// <summary>Number of rows (verbs).</summary>
    public const int VerbCount = 8;

    /// <summary>Number of columns (contexts).</summary>
    public const int ContextCount = 5;

    private const byte L = (byte)CellState.Live;
    private const byte D = (byte)CellState.Disabled;
    private const byte A = (byte)CellState.Absent;
    private const byte G = (byte)CellState.Greyed;

    /// <summary>The 8 x 5 matrix, row-major: <c>Cells[(int)verb * ContextCount + (int)context]</c>.</summary>
    /// <example><code>byte raw = VerbContextTable.Cells[0];   // Move in World: Live</code></example>
    public static ReadOnlySpan<byte> Cells =>
    [
        // World Panel Holding Mounted Dead
        L, L, L, L, A, // Move (Mounted: the vehicle's controls)
        L, L, L, L, A, // Look (Panel: suspended while the cursor is in UI; Mounted: orbit; Dead: camera auto-orbits)
        L, A, L, A, A, // Sprint (Panel: walk, Shift is quick-move; Mounted: Vehicles may reuse, Absent at M-1a)
        L, D, L, A, A, // Jump
        L, D, L, L, A, // Interact (Holding: panel closed; Mounted: Dismount only, narrowed by InteractionGate)
        L, A, L, L, A, // Shoulder swap
        L, A, L, L, G, // Comms wheel (Dead: pings anchor at the pile, Say; emotes greyed)
        A, A, A, A, A, // Respawn: automatic, never a press
    ];

    /// <summary>The press kinds of each verb, indexed by <see cref="ContextVerb"/>.</summary>
    private static ReadOnlySpan<byte> PressKinds =>
    [
        (byte)VerbPress.None, // Move: axis
        (byte)VerbPress.None, // Look: axis
        (byte)VerbPress.Hold, // Sprint: hold (toggle and auto are Input & Controls modes of the same action)
        (byte)VerbPress.Tap, // Jump
        (byte)(VerbPress.Tap | VerbPress.Hold), // Interact: mode from the focused anchor (P10), the one allowed exception
        (byte)VerbPress.Tap, // Shoulder swap
        (byte)VerbPress.Hold, // Comms wheel: hold to open the wheel
        (byte)VerbPress.None, // Respawn: never a press
    ];

    /// <summary>The cell for a verb in a context; <see cref="CellState.Absent"/> for an out-of-range verb or context.</summary>
    /// <param name="verb">The row.</param>
    /// <param name="context">The column.</param>
    /// <returns>The cell state.</returns>
    /// <example><code>CellState c = VerbContextTable.Cell(ContextVerb.Respawn, InputContext.World);   // Absent</code></example>
    public static CellState Cell(ContextVerb verb, InputContext context)
    {
        if ((uint)verb >= VerbCount || (uint)context >= ContextCount)
        {
            return CellState.Absent;
        }

        return (CellState)Cells[((int)verb * ContextCount) + (int)context];
    }

    /// <summary>The press kinds a verb carries; <see cref="VerbPress.None"/> for an out-of-range verb.</summary>
    /// <param name="verb">The verb.</param>
    /// <returns>The press kinds.</returns>
    /// <example><code>VerbPress p = VerbContextTable.Presses(ContextVerb.Jump);   // Tap</code></example>
    public static VerbPress Presses(ContextVerb verb) => (uint)verb < VerbCount ? (VerbPress)PressKinds[(int)verb] : VerbPress.None;
}
