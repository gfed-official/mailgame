namespace PerformativeMail.Client.Core.Input.Settings;

/// <summary>How a hold class takes its press (GDD R10, G7).</summary>
public enum HoldMode : byte
{
    /// <summary>The input is held for the hold time (or for as long as the state lasts).</summary>
    Hold = 0,

    /// <summary>One press starts or latches, a second press cancels or unlatches (G7).</summary>
    Toggle = 1,

    /// <summary>Sprint only: sprinting whenever the move magnitude is high enough (PC P14).</summary>
    Auto = 2,
}

/// <summary>Which glyph set the prompts show (GDD R9). Only <see cref="Auto"/> exists at M-1a; the overrides land at M1.</summary>
public enum GlyphStyle : byte
{
    /// <summary>Follow the last-used device.</summary>
    Auto = 0,
}

/// <summary>Every input setting, in <see cref="SettingsRanges"/> row order. Also the field id in a <see cref="LogKind.Config"/> entry.</summary>
public enum SettingsField : byte
{
    /// <summary>Hold time in ms (200-1000, step 50).</summary>
    HoldSettingMs,

    /// <summary>Minimum throw aim time in ms (GDD F11).</summary>
    ThrowMinAimMs,

    /// <summary>Throw preview settle time in ms (Throwing T3).</summary>
    PreviewSettleMs,

    /// <summary>Deliver and timed holds: Hold or Toggle.</summary>
    HoldModeDeliver,

    /// <summary>Throw aim: Hold or Toggle.</summary>
    HoldModeThrowAim,

    /// <summary>Sprint on keyboard and mouse: Hold, Toggle or Auto.</summary>
    HoldModeSprintKbm,

    /// <summary>Sprint on the pad: Hold, Toggle or Auto.</summary>
    HoldModeSprintPad,

    /// <summary>Mouse sensitivity in permille.</summary>
    MouseSensitivityPm,

    /// <summary>Stick X sensitivity in permille.</summary>
    StickXSensitivityPm,

    /// <summary>Stick Y sensitivity in permille.</summary>
    StickYSensitivityPm,

    /// <summary>Invert mouse X.</summary>
    InvertMouseX,

    /// <summary>Invert mouse Y.</summary>
    InvertMouseY,

    /// <summary>Invert stick X.</summary>
    InvertStickX,

    /// <summary>Invert stick Y.</summary>
    InvertStickY,

    /// <summary>Rumble strength in percent.</summary>
    RumblePct,

    /// <summary>Inner stick deadzone in permille (GDD F3).</summary>
    DzInnerPm,

    /// <summary>Outer stick deadzone in permille (GDD F3).</summary>
    DzOuterPm,

    /// <summary>Stick response curve exponent in permille (GDD F3).</summary>
    CurvePm,

    /// <summary>Trigger press threshold in permille (GDD F4).</summary>
    TriggerOnPm,

    /// <summary>Auto-repeat delay in ms (GDD F5).</summary>
    RepeatDelayMs,

    /// <summary>Auto-repeat interval in ms (GDD F5).</summary>
    RepeatIntervalMs,

    /// <summary>Aim steadiness in permille (GDD F10; M1 setting, parsed now).</summary>
    AimSteadinessPm,

    /// <summary>Glyph style (GDD R9).</summary>
    GlyphStyle,

    /// <summary>Auto-pause on focus loss or pad unplug in solo.</summary>
    AutoPauseSolo,

    /// <summary>Double-Back skip window in ms.</summary>
    BackSkipMs,

    /// <summary>Pending-commit timeout in ms.</summary>
    PendingCommitTimeoutMs,

    /// <summary>Pad edge collapse window in ms.</summary>
    PadEdgeCollapseMs,

    /// <summary>Catch-armed latch time in whole seconds (M0).</summary>
    CatchArmSeconds,
}

/// <summary>
/// The player's input settings in integer units (ADR-0012 sections 5 and 9, GDD R10): permille, ms, percent, whole seconds,
/// so NaN cannot occur. Immutable, with value equality: an untouched profile equals <see cref="Defaults"/>. Every value is
/// already normalized to its <see cref="SettingsRanges"/> row. Reads allocate 0 B.
/// </summary>
/// <example>
/// <code>
/// InputSettings s = InputSettingsCodec.Parse(bytes, out SettingsLoadReport report);
/// bool toggled = s.HoldModeDeliver == HoldMode.Toggle;
/// InputSettings slower = s.With(SettingsField.HoldSettingMs, 600);
/// </code>
/// </example>
public sealed class InputSettings : IEquatable<InputSettings>
{
    private readonly int[] _values;

    internal InputSettings(int[] values)
    {
        _values = values;
    }

    /// <summary>Every field at its <see cref="SettingsRange.Default"/>.</summary>
    public static InputSettings Defaults { get; } = new(SettingsRanges.CreateDefaultValues());

    // Mail Dev default, approved by Evan 2026-11-01 (recorded 1:48 PM PT) by card click (TD-205 item 4): Version is the schema this
    // object follows, always 1; the version a file declared is SettingsLoadReport.FileVersion.

    /// <summary>The settings schema version (<see cref="SettingsRanges.CurrentVersion"/>).</summary>
    public int Version => SettingsRanges.CurrentVersion;

    /// <summary>Hold time in ms, a multiple of 50 in 200-1000 (default 400).</summary>
    public int HoldSettingMs => _values[(int)SettingsField.HoldSettingMs];

    /// <summary>Minimum throw aim time in ms (default 250).</summary>
    public int ThrowMinAimMs => _values[(int)SettingsField.ThrowMinAimMs];

    /// <summary>Throw preview settle time in ms (default 0).</summary>
    public int PreviewSettleMs => _values[(int)SettingsField.PreviewSettleMs];

    /// <summary>Deliver and timed holds (default <see cref="HoldMode.Hold"/>).</summary>
    public HoldMode HoldModeDeliver => (HoldMode)_values[(int)SettingsField.HoldModeDeliver];

    /// <summary>Throw aim (default <see cref="HoldMode.Hold"/>).</summary>
    public HoldMode HoldModeThrowAim => (HoldMode)_values[(int)SettingsField.HoldModeThrowAim];

    /// <summary>Sprint on keyboard and mouse (default <see cref="HoldMode.Hold"/>).</summary>
    public HoldMode HoldModeSprintKbm => (HoldMode)_values[(int)SettingsField.HoldModeSprintKbm];

    /// <summary>Sprint on the pad (default <see cref="HoldMode.Toggle"/>).</summary>
    public HoldMode HoldModeSprintPad => (HoldMode)_values[(int)SettingsField.HoldModeSprintPad];

    /// <summary>Mouse sensitivity in permille.</summary>
    public int MouseSensitivityPm => _values[(int)SettingsField.MouseSensitivityPm];

    /// <summary>Stick X sensitivity in permille.</summary>
    public int StickXSensitivityPm => _values[(int)SettingsField.StickXSensitivityPm];

    /// <summary>Stick Y sensitivity in permille.</summary>
    public int StickYSensitivityPm => _values[(int)SettingsField.StickYSensitivityPm];

    /// <summary>Invert mouse X.</summary>
    public bool InvertMouseX => _values[(int)SettingsField.InvertMouseX] != 0;

    /// <summary>Invert mouse Y.</summary>
    public bool InvertMouseY => _values[(int)SettingsField.InvertMouseY] != 0;

    /// <summary>Invert stick X.</summary>
    public bool InvertStickX => _values[(int)SettingsField.InvertStickX] != 0;

    /// <summary>Invert stick Y.</summary>
    public bool InvertStickY => _values[(int)SettingsField.InvertStickY] != 0;

    /// <summary>Rumble strength in percent.</summary>
    public int RumblePct => _values[(int)SettingsField.RumblePct];

    /// <summary>Inner stick deadzone in permille (default 150).</summary>
    public int DzInnerPm => _values[(int)SettingsField.DzInnerPm];

    /// <summary>Outer stick deadzone in permille (default 950), at least 100‰ above <see cref="DzInnerPm"/>.</summary>
    public int DzOuterPm => _values[(int)SettingsField.DzOuterPm];

    /// <summary>Stick response curve exponent in permille (default 1500).</summary>
    public int CurvePm => _values[(int)SettingsField.CurvePm];

    /// <summary>Trigger press threshold in permille (default 500, floor 150).</summary>
    public int TriggerOnPm => _values[(int)SettingsField.TriggerOnPm];

    /// <summary>Auto-repeat delay in ms (default 300).</summary>
    public int RepeatDelayMs => _values[(int)SettingsField.RepeatDelayMs];

    /// <summary>Auto-repeat interval in ms (default 80, floor 40).</summary>
    public int RepeatIntervalMs => _values[(int)SettingsField.RepeatIntervalMs];

    /// <summary>Aim steadiness in permille (default 1000; no effect at M-1a).</summary>
    public int AimSteadinessPm => _values[(int)SettingsField.AimSteadinessPm];

    /// <summary>Glyph style (only <see cref="Settings.GlyphStyle.Auto"/> at M-1a).</summary>
    public GlyphStyle GlyphStyle => (GlyphStyle)_values[(int)SettingsField.GlyphStyle];

    /// <summary>Auto-pause on focus loss or pad unplug in solo (default on).</summary>
    public bool AutoPauseSolo => _values[(int)SettingsField.AutoPauseSolo] != 0;

    /// <summary>Double-Back skip window in ms (default 2000).</summary>
    public int BackSkipMs => _values[(int)SettingsField.BackSkipMs];

    /// <summary>Pending-commit timeout in ms (default 500).</summary>
    public int PendingCommitTimeoutMs => _values[(int)SettingsField.PendingCommitTimeoutMs];

    /// <summary>Pad edge collapse window in ms (default 20).</summary>
    public int PadEdgeCollapseMs => _values[(int)SettingsField.PadEdgeCollapseMs];

    /// <summary>Catch-armed latch time in whole seconds (default 3; M0).</summary>
    public int CatchArmSeconds => _values[(int)SettingsField.CatchArmSeconds];

    /// <summary>The stored integer of one field (bools as 0 / 1, enums as their value).</summary>
    /// <param name="field">The field.</param>
    /// <example><code>int dz = settings[SettingsField.DzInnerPm];</code></example>
    public int this[SettingsField field] => _values[(int)field];

    /// <summary>
    /// A copy with one field changed: the value is clamped and stepped by its <see cref="SettingsRanges"/> row, then the
    /// deadzone gap is enforced. Returns this instance when nothing changes.
    /// </summary>
    /// <param name="field">The field to change.</param>
    /// <param name="value">The new stored integer (bools as 0 / 1, enums as their value).</param>
    /// <returns>The changed settings.</returns>
    /// <example><code>InputSettings toggled = settings.With(SettingsField.HoldModeDeliver, (int)HoldMode.Toggle);</code></example>
    public InputSettings With(SettingsField field, int value)
    {
        int[] values = (int[])_values.Clone();
        values[(int)field] = SettingsRanges.Normalize(SettingsRanges.Get(field), value);
        SettingsRanges.EnforceDeadzoneGap(values);
        return values.AsSpan().SequenceEqual(_values) ? this : new InputSettings(values);
    }

    /// <inheritdoc />
    public bool Equals(InputSettings? other) =>
        other is not null && (ReferenceEquals(this, other) || _values.AsSpan().SequenceEqual(other._values));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as InputSettings);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (int value in _values)
            {
                hash = (hash * 31) + value;
            }

            return hash;
        }
    }
}
