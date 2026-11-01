using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input.Settings;

/// <summary>How a <see cref="SettingsRange"/> value is written in <c>user://input.json</c>.</summary>
public enum SettingsValueKind : byte
{
    /// <summary>A JSON integer, clamped to the row's range and rounded to its step.</summary>
    Int = 0,

    /// <summary>A JSON <c>true</c> / <c>false</c>, stored as 1 / 0.</summary>
    Bool = 1,

    /// <summary>A <see cref="Settings.HoldMode"/> name as a JSON string, stored as the enum value.</summary>
    HoldMode = 2,

    /// <summary>A <see cref="Settings.GlyphStyle"/> name as a JSON string, stored as the enum value.</summary>
    GlyphStyle = 3,
}

/// <summary>
/// One <see cref="SettingsRanges"/> row: the JSON key, how it is written, its bounds, default and step. Bool and enum rows use
/// the stored integer (0 / 1, or the enum value) for <see cref="Min"/>, <see cref="Max"/> and <see cref="Default"/>.
/// </summary>
/// <param name="Field">The field this row describes.</param>
/// <param name="Key">The JSON key (ordinal, case-sensitive).</param>
/// <param name="Kind">How the value is written.</param>
/// <param name="Min">The lowest stored value.</param>
/// <param name="Max">The highest stored value.</param>
/// <param name="Default">The value of <see cref="InputSettings.Defaults"/>.</param>
/// <param name="Step">Stored values are multiples of this (1 for every field except the hold time).</param>
/// <example><code>SettingsRange row = SettingsRanges.Get(SettingsField.HoldSettingMs); // 200..1000 step 50, default 400</code></example>
public readonly record struct SettingsRange(
    SettingsField Field, string Key, SettingsValueKind Kind, int Min, int Max, int Default, int Step = 1);

/// <summary>
/// The one table of every input setting (ADR-0012 sections 5 and 9, GDD R10 and Tuning Knobs): key, bounds, default and step.
/// It drives <see cref="InputSettings.Defaults"/>, the clamp in <see cref="InputSettingsCodec.Parse(ReadOnlySpan{byte}, out SettingsLoadReport)"/>
/// and <see cref="InputSettings.With"/>, and the codec boundary test. Row order is <see cref="SettingsField"/> order.
/// </summary>
/// <example><code>int clamped = SettingsRanges.Normalize(SettingsRanges.Get(SettingsField.DzInnerPm), 900); // 400</code></example>
public static class SettingsRanges
{
    /// <summary>The schema version this parser reads; a file without <c>version</c> is this version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The smallest gap between the outer and inner deadzone, in permille (GDD F3).</summary>
    public const int MinDeadzoneGapPm = 100;

    // Mail Dev default pending Evan (TD-205 item 1): JSON keys are flat camelCase, one key per row, using ADR-0012 section 5's
    // integer-unit names where it gives one (holdSettingMs, dzInnerPm, trigOnPm, ...) and the InputSettings property name
    // otherwise; the GDD R10 nested groups (holdMode{}, sensitivity{}, invert{}, deadzone{}, repeat{}) are not used.
    // Mail Dev default pending Evan (TD-205 item 11): faceLayout and swapSticks (GDD M4) have no row yet; in a file they are
    // unknown keys, dropped and logged.
    private static readonly SettingsRange[] _rows =
    [
        new(SettingsField.HoldSettingMs, "holdSettingMs", SettingsValueKind.Int, 200, 1000, DefaultClientInputSettings.DefaultHoldSettingMs, Step: 50),
        new(SettingsField.ThrowMinAimMs, "throwMinAimMs", SettingsValueKind.Int, 150, 400, 250),
        new(SettingsField.PreviewSettleMs, "previewSettleMs", SettingsValueKind.Int, 0, 250, 0),
        new(SettingsField.HoldModeDeliver, "holdModeDeliver", SettingsValueKind.HoldMode, (int)HoldMode.Hold, (int)HoldMode.Toggle, (int)HoldMode.Hold),
        new(SettingsField.HoldModeThrowAim, "holdModeThrowAim", SettingsValueKind.HoldMode, (int)HoldMode.Hold, (int)HoldMode.Toggle, (int)HoldMode.Hold),
        new(SettingsField.HoldModeSprintKbm, "holdModeSprintKbm", SettingsValueKind.HoldMode, (int)HoldMode.Hold, (int)HoldMode.Auto, (int)HoldMode.Hold),
        new(SettingsField.HoldModeSprintPad, "holdModeSprintPad", SettingsValueKind.HoldMode, (int)HoldMode.Hold, (int)HoldMode.Auto, (int)HoldMode.Toggle),

        // Approved by Evan 2026-11-01 (TD-204 item 1): sensitivity permille default 1000 clamped 100-5000, the four
        // invert flags off, rumble percent default 100 clamped 0-100 (the GDD Tuning Knobs table lists no values).
        new(SettingsField.MouseSensitivityPm, "mouseSensitivityPm", SettingsValueKind.Int, 100, 5000, 1000),
        new(SettingsField.StickXSensitivityPm, "stickXSensitivityPm", SettingsValueKind.Int, 100, 5000, 1000),
        new(SettingsField.StickYSensitivityPm, "stickYSensitivityPm", SettingsValueKind.Int, 100, 5000, 1000),
        new(SettingsField.InvertMouseX, "invertMouseX", SettingsValueKind.Bool, 0, 1, 0),
        new(SettingsField.InvertMouseY, "invertMouseY", SettingsValueKind.Bool, 0, 1, 0),
        new(SettingsField.InvertStickX, "invertStickX", SettingsValueKind.Bool, 0, 1, 0),
        new(SettingsField.InvertStickY, "invertStickY", SettingsValueKind.Bool, 0, 1, 0),
        new(SettingsField.RumblePct, "rumblePct", SettingsValueKind.Int, 0, 100, 100),

        new(SettingsField.DzInnerPm, "dzInnerPm", SettingsValueKind.Int, 50, 400, 150),
        new(SettingsField.DzOuterPm, "dzOuterPm", SettingsValueKind.Int, 700, 1000, 950),
        new(SettingsField.CurvePm, "curvePm", SettingsValueKind.Int, 1000, 3000, 1500),
        new(SettingsField.TriggerOnPm, "trigOnPm", SettingsValueKind.Int, 150, 900, 500),
        new(SettingsField.RepeatDelayMs, "repeatDelayMs", SettingsValueKind.Int, 150, 600, 300),
        new(SettingsField.RepeatIntervalMs, "repeatIntervalMs", SettingsValueKind.Int, 40, 200, 80),
        new(SettingsField.AimSteadinessPm, "aimSteadinessPm", SettingsValueKind.Int, 250, 1000, 1000),
        new(SettingsField.GlyphStyle, "glyphStyle", SettingsValueKind.GlyphStyle, (int)GlyphStyle.Auto, (int)GlyphStyle.Auto, (int)GlyphStyle.Auto),
        new(SettingsField.AutoPauseSolo, "autoPauseSolo", SettingsValueKind.Bool, 0, 1, 1),
        new(SettingsField.BackSkipMs, "backSkipMs", SettingsValueKind.Int, 1500, 4000, 2000),
        new(SettingsField.PendingCommitTimeoutMs, "pendingCommitTimeoutMs", SettingsValueKind.Int, 300, 1000, 500),
        new(SettingsField.PadEdgeCollapseMs, "padEdgeCollapseMs", SettingsValueKind.Int, 10, 50, 20),

        // Mail Dev default pending Evan (TD-205 item 2): catchArmSeconds is clamped 1-3; the GDD gives only the default 3,
        // which is also the 90-tick cap Throwing T7 puts on any arm.
        new(SettingsField.CatchArmSeconds, "catchArmSeconds", SettingsValueKind.Int, 1, 3, 3),
    ];

    static SettingsRanges()
    {
        for (int i = 0; i < _rows.Length; i++)
        {
            if ((int)_rows[i].Field != i)
            {
                throw new InvalidOperationException("SettingsRanges row " + i + " is " + _rows[i].Field + "; rows must be in SettingsField order.");
            }
        }
    }

    /// <summary>Every row, in <see cref="SettingsField"/> order.</summary>
    /// <example><code>foreach (SettingsRange row in SettingsRanges.All) { /* row.Key, row.Min, row.Max */ }</code></example>
    public static ReadOnlySpan<SettingsRange> All => _rows;

    /// <summary>The row of one field.</summary>
    /// <param name="field">The field.</param>
    /// <returns>Its row.</returns>
    /// <example><code>int max = SettingsRanges.Get(SettingsField.RumblePct).Max; // 100</code></example>
    public static SettingsRange Get(SettingsField field) => _rows[(int)field];

    /// <summary>
    /// The stored form of a value: clamped to the nearest bound, then rounded to the nearest step (half up). Bounds are
    /// multiples of the step, so the result stays in range. An enum value outside its row falls back to the row default.
    /// </summary>
    /// <param name="row">The field's row.</param>
    /// <param name="value">The value read or set.</param>
    /// <returns>The value to store.</returns>
    /// <example><code>SettingsRanges.Normalize(SettingsRanges.Get(SettingsField.HoldSettingMs), 430); // 450</code></example>
    public static int Normalize(in SettingsRange row, long value)
    {
        if (row.Kind is SettingsValueKind.HoldMode or SettingsValueKind.GlyphStyle)
        {
            return value >= row.Min && value <= row.Max ? (int)value : row.Default;
        }

        // Mail Dev default pending Evan (TD-205 item 3): clamp first, then round half up to the step (425 -> 450).
        int clamped = (int)Math.Clamp(value, row.Min, row.Max);
        return (clamped + (row.Step / 2)) / row.Step * row.Step;
    }

    /// <summary>
    /// Enforces <c>dzOuter - dzInner &gt;= 100‰</c> by raising <c>dzOuter</c> (GDD F3). With today's ranges (inner at most
    /// 400, outer at least 700) the gap always holds; the rule is kept so a wider range cannot break it.
    /// </summary>
    /// <param name="values">Stored values indexed by <see cref="SettingsField"/>.</param>
    /// <returns>True when <c>dzOuter</c> was raised.</returns>
    internal static bool EnforceDeadzoneGap(int[] values)
    {
        int inner = values[(int)SettingsField.DzInnerPm];
        if (values[(int)SettingsField.DzOuterPm] - inner >= MinDeadzoneGapPm)
        {
            return false;
        }

        values[(int)SettingsField.DzOuterPm] = Math.Min(inner + MinDeadzoneGapPm, Get(SettingsField.DzOuterPm).Max);
        return true;
    }

    /// <summary>A new array holding every row's default, indexed by <see cref="SettingsField"/>.</summary>
    internal static int[] CreateDefaultValues()
    {
        var values = new int[_rows.Length];
        foreach (SettingsRange row in _rows)
        {
            values[(int)row.Field] = row.Default;
        }

        return values;
    }
}
