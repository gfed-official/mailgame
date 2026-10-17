using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// F1 final carrier speed (GDD <c>player-character-movement.md</c> F1; ADR-0013 section 2; TR-movement-002, TR-movement-020).
/// <c>speedMmS = base x weightPm x buffPm x wadePm / 10^9</c> in <see cref="long"/> with exactly one division (truncation).
/// </summary>
/// <example>
/// <code>
/// // sprint, weight 800, Long Legs x2 + Postal 300 = 540 (capped to 400): 8400 mm/s
/// int add = CarrierSpeed.BuffAddPm(2, 3, 120, true, 300);
/// int v = CarrierSpeed.Final(7500, 800, add, 400, 1000, ref sink);
/// </code>
/// </example>
public static class CarrierSpeed
{
    /// <summary>One in permille.</summary>
    public const int Unit = 1000;

    /// <summary>
    /// Overflow guard only: the summed buff is clamped here before use so the <see cref="long"/> product in <see cref="Final"/>
    /// (largest base x 1000 x buffPm x 1000) cannot overflow whatever the settings or StatSheet hand in. It equals the
    /// <c>player.buffCapPm</c> knob's validated maximum, so no valid cap is affected.
    /// </summary>
    public const int MaxAddPm = 100_000;

    /// <summary>
    /// Builds the summed buff permille from the StatSheet inputs, clamping them first (GDD F1 Degenerate): stacks to
    /// <c>0..maxStacks</c>, negative per-stack or Postal values to 0. Story 020 builds the real <c>BuffAddPm</c> with the same rule;
    /// this is the defensive copy so a bad StatSheet read cannot break F1's 1200 mm/s minimum.
    /// </summary>
    /// <param name="longLegsStacks">Stacks held (clamped to <paramref name="maxStacks"/>).</param>
    /// <param name="maxStacks">Perk stack limit (3).</param>
    /// <param name="longLegsPm">Permille per stack (120).</param>
    /// <param name="postalActive">Whether Postal Sprint is running.</param>
    /// <param name="postalPm">Postal Sprint permille (300).</param>
    /// <returns>The sum, never negative.</returns>
    public static int BuffAddPm(int longLegsStacks, int maxStacks, int longLegsPm, bool postalActive, int postalPm)
    {
        long stacks = Math.Clamp(longLegsStacks, 0, Math.Max(0, maxStacks));
        long sum = (stacks * Math.Max(0, longLegsPm)) + (postalActive ? Math.Max(0, (long)postalPm) : 0L);
        return (int)Math.Min(sum, MaxAddPm);
    }

    /// <summary><c>1000 + min(max(addPm, 0), buffCapPm)</c>.</summary>
    /// <param name="addPm">Summed buffs (a negative value is treated as 0).</param>
    /// <param name="buffCapPm">The cap (400).</param>
    /// <returns>The buff multiplier in permille.</returns>
    public static int BuffPm(int addPm, int buffCapPm) => Unit + (int)Math.Min(Math.Min(Math.Max((long)addPm, 0L), Math.Max((long)buffCapPm, 0L)), MaxAddPm);

    /// <summary>F1: the final speed in mm/s, one division.</summary>
    /// <param name="baseMmS">Walk, sprint or swim base.</param>
    /// <param name="weightPm">Grid Inventory D1 <c>speedPermille</c> (clamped to 0..1000; the caller passes 1000 when mounted).</param>
    /// <param name="addPm">Summed buffs (clamped as <see cref="BuffPm"/>).</param>
    /// <param name="buffCapPm">The cap.</param>
    /// <param name="wadePm">Wading slow (1000 when dry; clamped to 0..1000).</param>
    /// <param name="sink">Arithmetic counters.</param>
    /// <returns>The speed in mm/s.</returns>
    public static int Final(int baseMmS, int weightPm, int addPm, int buffCapPm, int wadePm, ref SimCounterSink sink)
    {
        long product = (long)baseMmS * Math.Clamp(weightPm, 0, Unit) * BuffPm(addPm, buffCapPm) * Math.Clamp(wadePm, 0, Unit);
        long speed = Fix.MulDiv(product, 1, 1_000_000_000L, ref sink);
        return (int)Math.Min(speed, int.MaxValue);
    }
}
