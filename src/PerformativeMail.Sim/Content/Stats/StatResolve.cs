using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>Which clamp bound (if any) changed the unclamped value in <see cref="StatResolve"/> (CD19 <c>Explain</c>'s <c>clampedAt</c>).</summary>
/// <example>
/// <code>
/// StatResolution r = StatResolve.Resolve(def, bounds, 0, [120, 120, 120], [], [], 300);
/// bool capped = r.ClampedAt == StatClamp.Max;   // true: unclamped 660, max 400
/// </code>
/// </example>
public enum StatClamp : byte
{
    /// <summary>The unclamped value was already within [min, max].</summary>
    None = 0,

    /// <summary>The unclamped value was below min and was raised to it.</summary>
    Min = 1,

    /// <summary>The unclamped value was above max and was lowered to it.</summary>
    Max = 2,
}

/// <summary>
/// The result of one F1 evaluation, with the components the StatSheet caches for <c>Explain</c> (story 014). Immutable;
/// a value type with structural equality, so order-independence can be asserted with <c>==</c>.
/// </summary>
/// <param name="BaseTimesP">The Int128 product <c>base x PM_i</c> (permille^k scale); with <see cref="K"/> and <see cref="SumA"/> it reconstructs N and D.</param>
/// <param name="K">The Mul factor count (0 to <see cref="StatResolve.MaxMulFactors"/>); D = 1000^K.</param>
/// <param name="SumA">The sum of every Add plus the transient Add (in the stat's unit).</param>
/// <param name="HasSet">True when at least one Set was live; then <see cref="SetValue"/> is X and Adds/Muls were discarded.</param>
/// <param name="SetValue">The winning Set value (min-wins or max-wins); 0 when <paramref name="HasSet"/> is false.</param>
/// <param name="Unclamped">X before the clamp, saturated to int64.</param>
/// <param name="ClampedAt">Which bound the clamp applied, if any.</param>
/// <param name="Value">The resolved, clamped stat value.</param>
/// <example>
/// <code>
/// StatResolution r = StatResolve.Resolve(def, bounds, 10, [], [1500], [], 0);
/// int rows = r.Value;   // 15 (InventoryRows, Ceil)
/// </code>
/// </example>
public readonly record struct StatResolution(
    Int128 BaseTimesP,
    int K,
    long SumA,
    bool HasSet,
    int SetValue,
    long Unclamped,
    StatClamp ClampedAt,
    int Value);

/// <summary>
/// F1 StatResolve (content-data CD13, ADR-0010 section 7): <c>clamp(X, min, max)</c> where <c>X = S</c> if any Set is live,
/// otherwise <c>X = R_r(N, D)</c> with <c>N = base x P + 1000^k x (sum A + transientAdd)</c>, <c>D = 1000^k</c>,
/// <c>P = product of M_i</c> (permille). One rounding (HalfUp symmetric on magnitude, true Floor, true Ceil) through the
/// <see cref="Fix"/> Int128 helpers; the clamp happens in Int128 before narrowing to <see cref="int"/>. No input order
/// affects the result (Add and Mul are commutative; Set is a min or max). Pure, allocation-free, thread-safe.
/// <para>
/// Range contract (the Int128 proof): <see cref="MaxMulFactors"/> or fewer Mul factors, each in 0 to
/// <see cref="MaxMulPermille"/>, at most <see cref="MaxModifiers"/> Adds, and min at most max. Each violation trips
/// <c>SIM_ASSERT</c> (<see cref="ContentAssertCode.StatMulFactorsExceeded"/>, <see cref="ContentAssertCode.StatMulOutOfRange"/>,
/// <see cref="ContentAssertCode.StatAddsTooMany"/>, <see cref="ContentAssertCode.StatBoundsInverted"/>); builds without it compute
/// the result unchecked, which is only meaningful while the values stay inside the proof (the validator rules CV-PRK-05,
/// CV-PRK-10 and CV-GEN-15 keep data inside the premise).
/// </para>
/// </summary>
/// <example>
/// <code>
/// // QuotaMultPm-shaped: base 1000, muls 900 and 700, HalfUp: N = 630 000 000, D = 10^6 -> 630.
/// StatResolution r = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 0, 1000,
///     1000, [], [900, 700], [], 0);
/// </code>
/// </example>
public static class StatResolve
{
    /// <summary>The F1 Mul factor limit, <c>stats.maxMulFactors</c> (the premise of the Int128 bound).</summary>
    public const int MaxMulFactors = 6;

    /// <summary>The largest Mul value in permille (CV-PRK-05); part of the Int128 proof's range contract.</summary>
    public const int MaxMulPermille = 10_000;

    /// <summary>The most live modifiers (and so Adds) the Int128 proof allows.</summary>
    public const int MaxModifiers = 65_535;

    /// <summary>
    /// Resolves one stat using the rounding and Set rule of <paramref name="def"/> and its resolved
    /// <paramref name="bounds"/> (see <see cref="StatDef.ResolveBounds"/>).
    /// </summary>
    /// <param name="def">The stat's row (rounding and Set resolve are read from it).</param>
    /// <param name="bounds">The resolved clamp; must satisfy <see cref="StatBounds.IsValid"/>.</param>
    /// <param name="baseValue">The stat's base value.</param>
    /// <param name="adds">Add values, any order.</param>
    /// <param name="muls">Mul values in permille, any order; at most <see cref="MaxMulFactors"/>.</param>
    /// <param name="sets">Set values, any order.</param>
    /// <param name="transientAdd">The live timed Add (CD18); 0 if none.</param>
    /// <returns>The resolution with its explain components.</returns>
    /// <exception cref="SimAssertException">SIM_ASSERT builds only: more than <see cref="MaxMulFactors"/> Muls, or a Set on a stat without Set resolve.</exception>
    /// <example>
    /// <code>
    /// StatDef def = StatDefs.Get(Stat.MoveSpeedAddPm);
    /// StatResolution r = StatResolve.Resolve(def, new StatBounds(def.Stat, 0, 400), 0, [120, 120, 120], [], [], 300);
    /// // r.Unclamped == 660, r.ClampedAt == StatClamp.Max, r.Value == 400
    /// </code>
    /// </example>
    public static StatResolution Resolve(
        StatDef def,
        StatBounds bounds,
        int baseValue,
        ReadOnlySpan<int> adds,
        ReadOnlySpan<int> muls,
        ReadOnlySpan<int> sets,
        int transientAdd)
    {
        return Resolve(def.Rounding, def.SetResolve, bounds.Min, bounds.Max, baseValue, adds, muls, sets, transientAdd);
    }

    /// <summary>The primitive form of <see cref="Resolve(StatDef, StatBounds, int, ReadOnlySpan{int}, ReadOnlySpan{int}, ReadOnlySpan{int}, int)"/>.</summary>
    /// <param name="rounding">The single rounding mode.</param>
    /// <param name="setResolve">How several Sets combine.</param>
    /// <param name="min">Lower clamp bound (inclusive).</param>
    /// <param name="max">Upper clamp bound (inclusive).</param>
    /// <param name="baseValue">The stat's base value.</param>
    /// <param name="adds">Add values, any order.</param>
    /// <param name="muls">Mul values in permille, any order; at most <see cref="MaxMulFactors"/>.</param>
    /// <param name="sets">Set values, any order.</param>
    /// <param name="transientAdd">The live timed Add (CD18); 0 if none.</param>
    /// <returns>The resolution with its explain components.</returns>
    /// <exception cref="SimAssertException">SIM_ASSERT builds only: see the StatDef overload.</exception>
    /// <example>
    /// <code>
    /// StatResolution r = StatResolve.Resolve(StatRounding.Floor, SetResolve.NotApplicable, 1, 255, 20, [], [2000], [], 0);
    /// // r.Value == 40
    /// </code>
    /// </example>
    public static StatResolution Resolve(
        StatRounding rounding,
        SetResolve setResolve,
        int min,
        int max,
        int baseValue,
        ReadOnlySpan<int> adds,
        ReadOnlySpan<int> muls,
        ReadOnlySpan<int> sets,
        int transientAdd)
    {
        SimAssert.That(muls.Length <= MaxMulFactors, ContentAssertCode.StatMulFactorsExceeded);
        SimAssert.That(adds.Length <= MaxModifiers, ContentAssertCode.StatAddsTooMany);
        SimAssert.That(min <= max, ContentAssertCode.StatBoundsInverted);

        // Add enters before the Set check and the clamp; a long holds 65 535 int32 terms with ~16 bits to spare.
        long sumA = transientAdd;
        for (int i = 0; i < adds.Length; i++)
        {
            sumA += adds[i];
        }

        Int128 product = 1;
        for (int i = 0; i < muls.Length; i++)
        {
            int m = muls[i];
            SimAssert.That(m >= 0 && m <= MaxMulPermille, ContentAssertCode.StatMulOutOfRange);
            product *= m;
        }

        // Computed on the Set path too, on purpose: it feeds Explain's "overridden by a Set" components (story 014).
        Int128 baseTimesP = (Int128)baseValue * product;
        bool hasSet = sets.Length > 0;
        int setValue = hasSet ? ResolveSet(setResolve, sets) : 0;
        return Finish(rounding, min, max, baseTimesP, muls.Length, sumA, hasSet, setValue);
    }

    /// <summary>
    /// Re-runs the F1 tail from the components of an earlier <see cref="Resolve(StatRounding, SetResolve, int, int, int, ReadOnlySpan{int}, ReadOnlySpan{int}, ReadOnlySpan{int}, int)"/>
    /// with one more Add term: <c>sumA' = cached.SumA + transientAdd</c>, then the same single rounding and Int128 clamp. The
    /// result is bit-identical to calling <c>Resolve</c> again with the original lists plus <paramref name="transientAdd"/>
    /// (the StatSheet's transient <c>Read</c> uses this and never touches a modifier list, story 014). Pure, allocation-free.
    /// </summary>
    /// <param name="rounding">The single rounding mode.</param>
    /// <param name="min">Lower clamp bound (inclusive).</param>
    /// <param name="max">Upper clamp bound (inclusive).</param>
    /// <param name="cached">The components of the earlier resolution (its transient, if any, is kept and added to).</param>
    /// <param name="transientAdd">The extra live timed Add (CD18).</param>
    /// <returns>The new resolution.</returns>
    /// <exception cref="SimAssertException">SIM_ASSERT builds only: the cached factor count exceeds <see cref="MaxMulFactors"/>, or min is above max.</exception>
    /// <example>
    /// <code>
    /// StatResolution baseOnly = StatResolve.Resolve(def, bounds, 0, [120, 120, 120], [], [], 0);   // 360
    /// StatResolution withTransient = StatResolve.ResolveWithTransient(def.Rounding, 0, 400, baseOnly, 300);   // unclamped 660, 400
    /// </code>
    /// </example>
    public static StatResolution ResolveWithTransient(StatRounding rounding, int min, int max, in StatResolution cached, int transientAdd)
    {
        SimAssert.That(cached.K >= 0 && cached.K <= MaxMulFactors, ContentAssertCode.StatMulFactorsExceeded);
        SimAssert.That(min <= max, ContentAssertCode.StatBoundsInverted);
        return Finish(rounding, min, max, cached.BaseTimesP, cached.K, cached.SumA + transientAdd, cached.HasSet, cached.SetValue);
    }

    /// <summary>The shared F1 tail: Set short-circuit or one rounding, then the Int128 clamp.</summary>
    private static StatResolution Finish(
        StatRounding rounding, int min, int max, Int128 baseTimesP, int k, long sumA, bool hasSet, int setValue)
    {
        Int128 d = k == 0 ? Int128.One : Pow1000(k);
        Int128 x = hasSet ? setValue : Round(rounding, baseTimesP + (d * sumA), d);
        int value = ApplyClamp(x, min, max, out StatClamp clamp);
        return new StatResolution(baseTimesP, k, sumA, hasSet, setValue, Saturate(x), clamp, value);
    }

    /// <summary>Picks the winning Set: the smallest (min-wins) or largest (max-wins). <paramref name="sets"/> is non-empty.</summary>
    private static int ResolveSet(SetResolve setResolve, ReadOnlySpan<int> sets)
    {
        SimAssert.That(setResolve != SetResolve.NotApplicable, ContentAssertCode.StatSetWithoutResolve);
        int winner = sets[0];
        bool maxWins = setResolve == SetResolve.MaxWins;
        for (int i = 1; i < sets.Length; i++)
        {
            int s = sets[i];
            if (maxWins ? s > winner : s < winner)
            {
                winner = s;
            }
        }

        return winner;
    }

    /// <summary>Clamps in Int128 and only then narrows to int; min wins ties of an inverted pair (below min first).</summary>
    private static int ApplyClamp(Int128 x, int min, int max, out StatClamp clamp)
    {
        if (x < min)
        {
            clamp = StatClamp.Min;
            return min;
        }

        if (x > max)
        {
            clamp = StatClamp.Max;
            return max;
        }

        clamp = StatClamp.None;
        return (int)x;
    }

    /// <summary>The single F1/F5 rounding step <c>R_r(N, D)</c> over Int128 (<paramref name="d"/> &gt; 0).</summary>
    /// <param name="rounding">The mode.</param>
    /// <param name="n">Numerator.</param>
    /// <param name="d">Denominator, <c>1000^k</c>.</param>
    /// <returns>The rounded quotient.</returns>
    /// <example>
    /// <code>
    /// Int128 a = StatResolve.Round(StatRounding.Ceil, -1, 1000);   // 0
    /// </code>
    /// </example>
    /// <exception cref="SimAssertException">SIM_ASSERT builds only: <paramref name="rounding"/> is not a defined mode (other builds round HalfUp).</exception>
    public static Int128 Round(StatRounding rounding, Int128 n, Int128 d)
    {
        switch (rounding)
        {
            case StatRounding.HalfUp:
                return Fix.DivRoundHalfAway(n, d);
            case StatRounding.Floor:
                return Fix.FloorDiv(n, d);
            case StatRounding.Ceil:
                return Fix.CeilDiv(n, d);
            default:
                SimAssert.Fail(ContentAssertCode.StatRoundingUnknown);
                return Fix.DivRoundHalfAway(n, d);
        }
    }

    /// <summary><c>1000^k</c> for k from 0 to 6 as an Int128 (k = 7 and above only in builds that skipped the assert).</summary>
    /// <param name="k">The exponent.</param>
    /// <returns>The power.</returns>
    /// <example>
    /// <code>Int128 d = StatResolve.Pow1000(2); // 1 000 000</code>
    /// </example>
    public static Int128 Pow1000(int k)
    {
        Int128 d = 1;
        for (int i = 0; i < k; i++)
        {
            d *= 1000;
        }

        return d;
    }

    /// <summary>Saturates an Int128 into the int64 range (not symmetric: the low end is <see cref="long.MinValue"/>).</summary>
    /// <param name="v">The value.</param>
    /// <returns><paramref name="v"/> limited to int64.</returns>
    /// <example>
    /// <code>long s = StatResolve.Saturate(Int128.MaxValue); // long.MaxValue</code>
    /// </example>
    public static long Saturate(Int128 v)
    {
        if (v > long.MaxValue)
        {
            return long.MaxValue;
        }

        return v < long.MinValue ? long.MinValue : (long)v;
    }
}
