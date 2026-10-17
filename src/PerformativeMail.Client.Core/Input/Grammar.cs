namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// How an action is triggered (GDD R3). An <see cref="ActionDef"/> has exactly one grammar by construction (a single enum
/// value); <c>0</c> is "missing" and an undefined value is rejected by the validator. Values are append-only.
/// </summary>
/// <example>
/// <code>
/// Grammar g = Grammar.AimRelease;
/// bool defined = Enum.IsDefined(g);   // true
/// </code>
/// </example>
public enum Grammar : byte
{
    /// <summary>Missing. Never valid.</summary>
    None = 0,

    /// <summary>A press-and-release is one activation.</summary>
    Tap = 1,

    /// <summary>Press starts, release ends; what it does can depend on the target (see <see cref="ActionDef.TargetDriven"/>).</summary>
    Hold = 2,

    /// <summary>Press raises an aim, release commits or cancels it (Throw).</summary>
    AimRelease = 3,

    /// <summary>A level the Sim reads each tick (Sprint).</summary>
    HeldState = 4,

    /// <summary>A click or a drag with the pointer (Pick up / place).</summary>
    ClickOrDrag = 5,

    /// <summary>A continuous axis (Move, Look); travels in dedicated frame fields.</summary>
    Axis = 6,

    /// <summary>Discrete steps with repeat (Hotbar step, Grid cursor).</summary>
    Stepped = 7,
}
