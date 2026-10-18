namespace PerformativeMail.Sim.Players;

/// <summary>
/// The constants <see cref="Hold.HoldTicks(int, int, int, in HoldSettings)"/> reads (ADR-0014 section 4; Player Character
/// story 011): <c>holdDefaultMs</c> and <c>holdCeilMs</c> are Input &amp; Controls constants (PROPOSED in input-controls),
/// <c>maxInsertPm</c> is the Manual Delivery <c>DeliverInsertPm</c> clamp, and <c>minHoldTicks</c> comes from
/// <see cref="PlayerSettings.MinHoldTicks"/>. <c>minHoldTicks</c> below 6 is the content validator's error (CV-PLR-04) and is not
/// re-checked here. Immutable; the default constructor leaves every field 0, so build one with
/// <see cref="FromPlayerSettings"/>.
/// </summary>
/// <param name="HoldDefaultMs">The hold length, ms, at the default setting (400); the divisor that scales a setting.</param>
/// <param name="HoldCeilMs">The base length, ms, below which the upper clamp uses this floor (2000).</param>
/// <param name="MinHoldTicks">Floor on any hold, ticks (<c>player.minHoldTicks</c>, default 6).</param>
/// <param name="MaxInsertPm">Largest <c>insertPm</c> accepted, permille (750).</param>
/// <example>
/// <code>
/// HoldSettings hold = HoldSettings.FromPlayerSettings(playerSettings);
/// int ticks = Hold.HoldTicks(400, 0, 400, in hold);   // 12
/// </code>
/// </example>
public readonly record struct HoldSettings(int HoldDefaultMs, int HoldCeilMs, int MinHoldTicks, int MaxInsertPm)
{
    /// <summary>Default <c>holdDefaultMs</c>, ms.</summary>
    public const int DefaultHoldDefaultMs = 400;

    /// <summary>Default <c>holdCeilMs</c>, ms.</summary>
    public const int DefaultHoldCeilMs = 2000;

    /// <summary>Default <c>maxInsertPm</c>, permille.</summary>
    public const int DefaultMaxInsertPm = 750;

    /// <summary>Smallest player hold setting, ms.</summary>
    public const int MinSettingMs = 200;

    /// <summary>Largest player hold setting, ms.</summary>
    public const int MaxSettingMs = 1000;

    /// <summary>Granularity of the player hold setting, ms.</summary>
    public const int SettingStepMs = 50;

    /// <summary>Builds the settings from <paramref name="s"/> (<c>MinHoldTicks</c>) plus the Input constants.</summary>
    /// <param name="s">The players settings record; must not be null.</param>
    /// <returns>The hold settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is null.</exception>
    /// <example><code>HoldSettings hold = HoldSettings.FromPlayerSettings(settings);</code></example>
    public static HoldSettings FromPlayerSettings(PlayerSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new HoldSettings(DefaultHoldDefaultMs, DefaultHoldCeilMs, s.MinHoldTicks, DefaultMaxInsertPm);
    }
}
