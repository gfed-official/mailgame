namespace PerformativeMail.Sim.Content.Stats;

/// <summary>Whether a <see cref="WorstCase"/> evaluation produced a value.</summary>
/// <example>
/// <code>
/// WorstCaseResult r = WorstCase.High(StatRounding.Floor, 40, [], [2000], [], 0);
/// bool ok = r.Status == WorstCaseStatus.Ok;   // r.Value == 80
/// </code>
/// </example>
public enum WorstCaseStatus : byte
{
    /// <summary>The value was computed.</summary>
    Ok = 0,

    /// <summary>More than <see cref="StatResolve.MaxMulFactors"/> Mul units can be live together (CV-PRK-10); the value was skipped.</summary>
    TooManyFactors = 1,
}

/// <summary>The result of an F5 evaluation: a status, the unclamped worst-case value and the factor counts.</summary>
/// <param name="Status">Whether <paramref name="Value"/> is meaningful.</param>
/// <param name="Value">The unclamped worst case, saturated to int64; 0 when <paramref name="Status"/> is <see cref="WorstCaseStatus.TooManyFactors"/>.</param>
/// <param name="KWorst">All Mul units supplied, the count CV-PRK-10 checks against <see cref="StatResolve.MaxMulFactors"/>.</param>
/// <param name="KSelected">The Mul units that entered the product (raising or lowering ones only).</param>
/// <example>
/// <code>
/// WorstCaseResult r = WorstCase.High(StatRounding.HalfUp, 0, [120, 120, 120], [], [], 300);
/// long worst = r.Value;   // 660
/// </code>
/// </example>
public readonly record struct WorstCaseResult(WorstCaseStatus Status, long Value, int KWorst, int KSelected);

/// <summary>
/// F5 WorstCaseStat over an already-selected unit list (content-data F5; the personal-cap and <c>excludes</c> selection
/// that needs PerkDef is M1, CV-PRK-08/10). One F1 evaluation without the clamp over the raising units
/// (<see cref="High"/>: Mul &gt; 1000, Add &gt; 0, plus the largest transient term) or the lowering
/// units (<see cref="Low"/>: Mul &lt; 1000, Add &lt; 0). Sets are folded in exactly as the formula reads:
/// <c>max(R(N+), every S_j)</c> for High and <c>min(R(N-), every S_j)</c> for Low, so a Set "counts only when it raises"
/// (or lowers) the rounded value, wherever the base sits (also for a negative base). Pure,
/// allocation-free, thread-safe. With zero units the value is the rounded base (CV-GRD-01 at M-1a).
/// </summary>
/// <example>
/// <code>
/// // CV-GRD-01 postcard: maxStack 40 x Bulk Mailer 2000 (Floor) = 80, within 255.
/// WorstCaseResult r = WorstCase.High(StatRounding.Floor, 40, [], [2000], [], 0);
/// </code>
/// </example>
public static class WorstCase
{
    /// <summary>F5 high: the largest value the units can produce, unclamped.</summary>
    /// <param name="rounding">The stat's rounding mode.</param>
    /// <param name="baseValue">The base for the qualifier.</param>
    /// <param name="adds">Every Add unit that can be live (the raising ones are selected here).</param>
    /// <param name="muls">Every Mul unit that can be live together (k_worst is their count).</param>
    /// <param name="sets">Every Set value that can be live.</param>
    /// <param name="transientMax">T_s, the largest transient term the owner can pass; 0 if none.</param>
    /// <returns>The status and value; <see cref="WorstCaseStatus.TooManyFactors"/> skips the value.</returns>
    /// <example>
    /// <code>
    /// WorstCaseResult r = WorstCase.High(StatRounding.Floor, 200, [], [2000], [], 0);   // 400
    /// </code>
    /// </example>
    public static WorstCaseResult High(
        StatRounding rounding, int baseValue, ReadOnlySpan<int> adds, ReadOnlySpan<int> muls, ReadOnlySpan<int> sets, int transientMax)
    {
        return Evaluate(true, rounding, baseValue, adds, muls, sets, transientMax);
    }

    /// <summary>F5 low: the smallest value the units can produce, unclamped (CV-PRK-08 floor).</summary>
    /// <param name="rounding">The stat's rounding mode.</param>
    /// <param name="baseValue">The base for the qualifier.</param>
    /// <param name="adds">Every Add unit that can be live (the lowering ones are selected here).</param>
    /// <param name="muls">Every Mul unit that can be live together (k_worst is their count).</param>
    /// <param name="sets">Every Set value that can be live.</param>
    /// <returns>The status and value; <see cref="WorstCaseStatus.TooManyFactors"/> skips the value.</returns>
    /// <example>
    /// <code>
    /// WorstCaseResult r = WorstCase.Low(StatRounding.HalfUp, 1000, [], [900, 700, 450], []);   // 284
    /// </code>
    /// </example>
    public static WorstCaseResult Low(
        StatRounding rounding, int baseValue, ReadOnlySpan<int> adds, ReadOnlySpan<int> muls, ReadOnlySpan<int> sets)
    {
        return Evaluate(false, rounding, baseValue, adds, muls, sets, 0);
    }

    private static WorstCaseResult Evaluate(
        bool high, StatRounding rounding, int baseValue, ReadOnlySpan<int> adds, ReadOnlySpan<int> muls, ReadOnlySpan<int> sets, int transient)
    {
        if (muls.Length > StatResolve.MaxMulFactors)
        {
            return new WorstCaseResult(WorstCaseStatus.TooManyFactors, 0, muls.Length, 0);
        }

        Int128 product = 1;
        int k = 0;
        for (int i = 0; i < muls.Length; i++)
        {
            int m = muls[i];
            if (high ? m > 1000 : m < 1000)
            {
                product *= m;
                k++;
            }
        }

        long sumA = high ? transient : 0;
        for (int i = 0; i < adds.Length; i++)
        {
            int a = adds[i];
            if (high ? a > 0 : a < 0)
            {
                sumA += a;
            }
        }

        Int128 d = StatResolve.Pow1000(k);
        Int128 best = StatResolve.Round(rounding, (Int128)baseValue * product + d * sumA, d);
        for (int i = 0; i < sets.Length; i++)
        {
            int s = sets[i];
            if (high ? s > best : s < best)
            {
                best = s;
            }
        }

        return new WorstCaseResult(WorstCaseStatus.Ok, StatResolve.Saturate(best), muls.Length, k);
    }
}
