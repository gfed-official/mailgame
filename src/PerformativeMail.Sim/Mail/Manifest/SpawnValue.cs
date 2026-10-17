using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// F1, the spawn value of one mail unit (GDD mail-destinations section F1; TR-mail-001; ADR-0006 section 2):
/// <c>spawnValue = RH( baseValue x distPm x shiftPm x cursedPm x foundPm / 10^12 )</c> with exactly one rounding, at the
/// end (half away from zero, <see cref="Fix.DivRoundHalfAway"/>). Pure, integer-only, allocation-free, thread-safe.
/// <para>
/// Factors: <c>distPm = 1000 + distanceStepPm x (district - 1)</c>, <c>shiftPm = 1000 + shiftStepPm x (spawnShift - 1)</c>,
/// <c>cursedPm</c> = <see cref="MailValueKnobs.CursedValueMultPm"/> when Cursed else 1000, <c>foundPm</c> =
/// <see cref="MailValueKnobs.FoundValueMultPm"/> when Found else 1000.
/// </para>
/// <para>
/// Overflow policy: the product is formed in <see cref="Int128"/>. <c>baseValue</c> and each permille factor are first
/// clamped to [0, 2^20]; five factors of at most 2^20 give at most 2^100, so the product never overflows. With the default
/// knobs and the v1 kinds the product is at most 160 x 2000 x 1400 x 1500 x 3000 = 2.016e15 (fits <see cref="long"/> even
/// before widening). The rounded result is saturated to <see cref="ushort.MaxValue"/> (the GDD's output range is
/// 4..1,344, so saturation is unreachable with shipped content; it exists so no input can wrap).
/// </para>
/// </summary>
/// <example>
/// <code>
/// ushort v = SpawnValue.Compute(70, 4, 3, cursed: true, found: false, MailValueKnobs.Default); // 221
/// </code>
/// </example>
public static class SpawnValue
{
    private const long FactorMax = 1L << 20;

    private const long Ten12 = 1_000_000_000_000L;

    /// <summary>Computes F1.</summary>
    /// <param name="baseValue">The kind's base value in cents (clamped to [0, 2^20]).</param>
    /// <param name="district">The destination house's district (1-based; below 1 is treated as 1).</param>
    /// <param name="spawnShift">The spawn shift (1-based; below 1 is treated as 1).</param>
    /// <param name="cursed">True when the unit is Cursed.</param>
    /// <param name="found">True when the unit is Found.</param>
    /// <param name="k">The permille knobs.</param>
    /// <returns>The spawn value in cents, saturated to <see cref="ushort.MaxValue"/>.</returns>
    public static ushort Compute(int baseValue, int district, int spawnShift, bool cursed, bool found, in MailValueKnobs k)
    {
        long distPm = Clamp(1000L + (long)k.DistanceStepPm * (Math.Max(district, 1) - 1L));
        long shiftPm = Clamp(1000L + (long)k.ShiftStepPm * (Math.Max(spawnShift, 1) - 1L));
        long cursedPm = cursed ? Clamp(k.CursedValueMultPm) : 1000L;
        long foundPm = found ? Clamp(k.FoundValueMultPm) : 1000L;
        Int128 product = (Int128)Clamp(baseValue) * distPm * shiftPm * cursedPm * foundPm;
        Int128 rounded = Fix.DivRoundHalfAway(product, Ten12);
        return rounded > ushort.MaxValue ? ushort.MaxValue : (ushort)rounded;
    }

    private static long Clamp(long x) => x < 0 ? 0 : (x > FactorMax ? FactorMax : x);
}
