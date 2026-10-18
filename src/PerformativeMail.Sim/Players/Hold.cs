using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The single shared <c>holdEff</c> function (ADR-0014 section 4; Player Character GDD F4 and input-controls F1; TR-movement-012,
/// TR-input-011). Manual Delivery, Input &amp; Controls and the HUD hold ring all call <see cref="HoldTicks(int, int, int, in HoldSettings)"/>;
/// none may re-create it. Pure, integer-only (no float or double), allocation-free, never throws, no mutable statics.
/// </summary>
/// <example>
/// <code>
/// int ticks = Hold.HoldTicks(400, 500, 600, in settings);   // 9
/// int pm = Hold.ProgressPm(5, 12);                           // 416
/// </code>
/// </example>
public static class Hold
{
    /// <summary>Ticks per second (<see cref="TickRate.Hz"/>), the 30 in the ADR formula; never a literal.</summary>
    private const long TicksPerSecond = TickRate.Hz;

    /// <summary>
    /// Hold length in Sim ticks. <c>settingMs = ClampSettingMs(holdSettingMs)</c>; <c>insertPm</c> is clamped to
    /// 0 to <see cref="HoldSettings.MaxInsertPm"/>; a negative <paramref name="holdBaseMs"/> is treated as 0;
    /// <c>raw = ceil(holdBaseMs * (1000 - insertPm) * settingMs * 30 / (1000 * holdDefaultMs * 1000))</c>;
    /// <c>ceil = ceil(max(holdBaseMs, holdCeilMs) * 30 / 1000)</c>; the result is <c>clamp(raw, minHoldTicks, ceil)</c>.
    /// This overload counts nothing; see the overload taking <see cref="SimCounters"/>.
    /// </summary>
    /// <param name="holdBaseMs">The interactable's base hold length, ms.</param>
    /// <param name="insertPm">Quick Hands style reduction, permille (0 to maxInsertPm).</param>
    /// <param name="holdSettingMs">The player's hold setting, ms; see <see cref="ClampSettingMs"/>.</param>
    /// <param name="s">The hold constants.</param>
    /// <returns>The hold length in ticks, at least <see cref="HoldSettings.MinHoldTicks"/> (when that does not exceed <c>ceil</c>).</returns>
    /// <example><code>int ticks = Hold.HoldTicks(400, 0, 400, in settings);   // 12</code></example>
    public static int HoldTicks(int holdBaseMs, int insertPm, int holdSettingMs, in HoldSettings s)
    {
        return Compute(holdBaseMs, insertPm, holdSettingMs, in s, out _);
    }

    /// <summary>
    /// Same result as <see cref="HoldTicks(int, int, int, in HoldSettings)"/>, and adds <see cref="CounterId.HoldInputClamped"/> once
    /// when <paramref name="insertPm"/> was outside 0 to <see cref="HoldSettings.MaxInsertPm"/> (a content or caller bug). A
    /// <paramref name="holdSettingMs"/> clamp or rounding is NOT counted: it is a player setting, not a content defect.
    /// </summary>
    /// <param name="holdBaseMs">The interactable's base hold length, ms.</param>
    /// <param name="insertPm">Quick Hands style reduction, permille (0 to maxInsertPm).</param>
    /// <param name="holdSettingMs">The player's hold setting, ms; see <see cref="ClampSettingMs"/>.</param>
    /// <param name="s">The hold constants.</param>
    /// <param name="counters">Receives the clamp count; <see langword="null"/> counts nothing.</param>
    /// <returns>The hold length in ticks.</returns>
    /// <example><code>int ticks = Hold.HoldTicks(400, 900, 400, in settings, counters);   // 6, holdInputClamped + 1</code></example>
    public static int HoldTicks(int holdBaseMs, int insertPm, int holdSettingMs, in HoldSettings s, SimCounters? counters)
    {
        int ticks = Compute(holdBaseMs, insertPm, holdSettingMs, in s, out bool insertClamped);
        if (insertClamped)
        {
            counters?.Add(CounterId.HoldInputClamped);
        }

        return ticks;
    }

    /// <summary>
    /// Clamps a player hold setting to <see cref="HoldSettings.MinSettingMs"/> to <see cref="HoldSettings.MaxSettingMs"/> (200 to
    /// 1000), then rounds to the nearest multiple of <see cref="HoldSettings.SettingStepMs"/> (50). Ties (a value ending in 25 or 75)
    /// round UP: 430 gives 450, 420 gives 400, 425 gives 450, 150 gives 200, 1200 gives 1000.
    /// </summary>
    /// <param name="holdSettingMs">The setting, ms; any int.</param>
    /// <returns>A multiple of 50 from 200 to 1000.</returns>
    /// <example><code>int ms = Hold.ClampSettingMs(430);   // 450</code></example>
    public static int ClampSettingMs(int holdSettingMs)
    {
        int clamped = Math.Min(HoldSettings.MaxSettingMs, Math.Max(HoldSettings.MinSettingMs, holdSettingMs));
        int half = HoldSettings.SettingStepMs / 2;
        return ((clamped + half) / HoldSettings.SettingStepMs) * HoldSettings.SettingStepMs;
    }

    /// <summary>Hold progress in permille, <c>floor(1000 * heldTicks / holdTicks)</c> clamped to 0 to 1000.</summary>
    /// <param name="heldTicks">Ticks held so far.</param>
    /// <param name="holdTicks">The hold length from <see cref="HoldTicks(int, int, int, in HoldSettings)"/>; 0 or less gives 0.</param>
    /// <returns>Progress from 0 to 1000.</returns>
    /// <example><code>int pm = Hold.ProgressPm(5, 12);   // 416</code></example>
    public static int ProgressPm(int heldTicks, int holdTicks)
    {
        if (holdTicks <= 0 || heldTicks <= 0)
        {
            return 0;
        }

        long pm = 1000L * heldTicks / holdTicks;
        return (int)Math.Min(1000L, pm);
    }

    private static int Compute(int holdBaseMs, int insertPm, int holdSettingMs, in HoldSettings s, out bool insertClamped)
    {
        int maxInsert = Math.Max(0, s.MaxInsertPm);
        int insert = Math.Min(maxInsert, Math.Max(0, insertPm));
        insertClamped = insert != insertPm;
        long settingMs = ClampSettingMs(holdSettingMs);
        long baseMs = Math.Max(0, holdBaseMs);

        // The product reaches int.MaxValue * 1000 * 1000 * TickRate.Hz (> long.MaxValue) for an absurd base, so it is held in Int128;
        // for every in-range base (up to ~3e8 ms) it equals the ADR's long arithmetic.
        Int128 num = (Int128)baseMs * (1000 - insert) * settingMs * TicksPerSecond;
        Int128 den = (Int128)1000 * s.HoldDefaultMs * 1000;
        long raw = (long)Fix.CeilDiv(num, den);
        long ceil = (Math.Max(baseMs, s.HoldCeilMs) * TicksPerSecond + 999) / 1000;
        return (int)Math.Min(ceil, Math.Max(s.MinHoldTicks, raw));
    }
}
