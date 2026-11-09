namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Router constants that are not player settings (GDD Formulas preamble: data, never literals in the formulas). The player
/// settings (<c>dzInnerPm</c>, <c>dzOuterPm</c>, <c>curvePm</c>, <c>trigOnPm</c>, sensitivity, invert) are not repeated here:
/// their defaults are <see cref="Settings.InputSettings.Defaults"/> (<see cref="Settings.SettingsRanges"/>). Values marked
/// PROPOSED are the story's or Mail Dev's starting points, not tuned.
/// </summary>
internal static class InputDefaults
{
    /// <summary>F4 release gap below the press point, permille (GDD F4 <c>trigHyst</c>, PROPOSED).</summary>
    public const int TrigHystPm = 150;

    /// <summary>F4 lowest release point, permille (GDD F4).</summary>
    public const int TrigFloorPm = 50;

    /// <summary>The F3 response exponent of the move stick, permille (GDD F3: move stick fixed at 1.0).</summary>
    public const int MoveCurvePm = 1000;

    /// <summary>Mouse counts per full turn at sensitivity 1000 ‰, so one count is 0.1 degree (PROPOSED).</summary>
    public const int MouseCountsPerTurn = 3600;

    /// <summary>Right-stick look rate at full deflection and sensitivity 1000 ‰, BAM per second: 180 degrees per second (PROPOSED).</summary>
    public const int StickLookBamPerSec = 32_768;

    /// <summary>The longest rendered frame the stick look integrates, ms; a longer hitch integrates this much (PROPOSED).</summary>
    public const int StickLookMaxDtMs = 1000;

    /// <summary>GDD F8 <c>sprintUnlatchSeconds</c> in ms (0.25 s; a constant, not a setting).</summary>
    public const int SprintUnlatchMs = 250;

    /// <summary>Sprint Auto threshold: <c>|moveIntent| ≥ 0.9</c> in Q15 (Player Character P14, PROPOSED).</summary>
    public const int SprintAutoMinQ15 = 29_491;
}
