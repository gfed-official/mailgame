using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>Negative result codes of <see cref="KindCounts.Compute(long, ReadOnlySpan{KindShare}, in MeanDist, in MailValueKnobs, int, Span{int})"/>; a non-negative result is the unit total N.</summary>
public static class KindCountsStatus
{
    /// <summary>No unlocked houses: the manifest cannot be built (GDD M-F3-2e: a content error for PrepareShift, never an exception in a tick).</summary>
    public const int ZeroHouses = -1;

    /// <summary>
    /// An input is out of range: V negative or above <see cref="KindCounts.MaxV"/>; more than <see cref="MeanDist.MaxHouses"/> houses;
    /// a share outside [0, 1000]; a base value outside [0, <see cref="KindCounts.MaxBaseValue"/>]; a fallback index outside [-1, count);
    /// or an accepting kind whose E_k is not positive.
    /// </summary>
    public const int InvalidInput = -2;

    /// <summary>The output span is shorter than the shares span.</summary>
    public const int OutputTooSmall = -3;

    /// <summary>More than <see cref="KindCounts.MaxKinds"/> kinds.</summary>
    public const int TooManyKinds = -4;
}

/// <summary>
/// F3, units per kind (GDD mail-destinations section F3; TR-mail-001/002; ADR-0017 section 10). Pure, deterministic,
/// RNG-free, integer-only, 0 B (stackalloc scratch only), thread-safe, never throws.
/// <para>
/// <c>E_k = baseValue_k x shiftPm x meanDistPm / 10^6</c>; <c>V_k = V x share_k / 1000</c> (exact, not truncated; see the
/// discrepancy note below) plus the shares of kinds that have no unlocked accepting destination and whose
/// <c>fallbackKind</c> chain ends at k; kinds are processed in descending <c>baseValue</c> (ties by lower index) with a
/// carry r starting at 0: <c>n_k = max(0, floorDiv(2 (V_k + r) + E_k, 2 E_k))</c>, <c>r = V_k + r - n_k E_k</c>,
/// <c>N = SUM n_k</c>.
/// </para>
/// <para>
/// Exactness: V_k, E_k and r are rationals with the GDD's common denominator D = H x 10^6 (<see cref="MeanDist.Denominator"/>).
/// SUM(V x share) is scaled by <see cref="MeanDist.ShareScale"/> (H x 10^3), E_k is <see cref="MeanDist.ExpectedNumerator"/>,
/// and the recurrence runs on those integer numerators in <see cref="Int128"/> (needed: V x D reaches 10^9 x 3.2 x 10^4 x 10^10
/// at the bounds). Bounds: V at most <see cref="MaxV"/> (10^9 cents), H at most 10^4, at most 32 kinds, base value at most
/// <see cref="MaxBaseValue"/> (2^20) keep every numerator below 2^110; |r| is at most half the previous E.
/// </para>
/// <para>
/// DISCREPANCY (flagged for game-designer): the GDD F3 variable table says V x share / 1000 "truncates", but the golden
/// M-F3-2 (FX-I50, shift 5, V = 24,704: Postcard 288) is only reached with the EXACT V_k (truncating gives 287). The exact
/// value is implemented; the GDD sentence should drop "truncates". The V = 960 and V = 672 goldens are unaffected.
/// </para>
/// <para>
/// A kind with no accepting destination gets n = 0 and does not disturb the carry; if its fallback chain dead-ends (or
/// cycles) its share is dropped (the content validator CV-MAIL-04 rejects that content).
/// </para>
/// </summary>
/// <example>
/// <code>
/// MeanDist md = MeanDist.Create(districts, knobs.DistanceStepPm);
/// int n = KindCounts.Compute(960, shares, in md, in knobs, 1, counts);   // N, or a KindCountsStatus code
/// </code>
/// </example>
public static class KindCounts
{
    /// <summary>The most kinds supported (the processed-kinds bitmask is 32 bits).</summary>
    public const int MaxKinds = 32;

    /// <summary>The largest supported V in cents.</summary>
    public const long MaxV = 1_000_000_000L;

    /// <summary>The largest supported <see cref="KindShare.BaseValueCents"/>.</summary>
    public const long MaxBaseValue = 1L << 20;

    /// <summary>Computes F3 counts only.</summary>
    /// <param name="v">The shift spawn budget V in cents.</param>
    /// <param name="shares">One entry per kind; <paramref name="nOut"/> is indexed identically.</param>
    /// <param name="md">The exact mean distance multiplier.</param>
    /// <param name="k">The knobs (<see cref="MailValueKnobs.ShiftStepPm"/> is used).</param>
    /// <param name="spawnShift">The spawn shift (1-based; below 1 is treated as 1).</param>
    /// <param name="nOut">Receives n_k for each kind (length at least <c>shares.Length</c>).</param>
    /// <returns>N = SUM n_k, or a negative <see cref="KindCountsStatus"/> code (with <paramref name="nOut"/> zeroed).</returns>
    public static int Compute(long v, ReadOnlySpan<KindShare> shares, in MeanDist md, in MailValueKnobs k, int spawnShift, Span<int> nOut)
        => Compute(v, shares, in md, in k, spawnShift, nOut, default);

    /// <summary>Computes F3 counts and the carry after each kind.</summary>
    /// <param name="v">The shift spawn budget V in cents.</param>
    /// <param name="shares">One entry per kind; the outputs are indexed identically.</param>
    /// <param name="md">The exact mean distance multiplier.</param>
    /// <param name="k">The knobs (<see cref="MailValueKnobs.ShiftStepPm"/> is used).</param>
    /// <param name="spawnShift">The spawn shift (1-based; below 1 is treated as 1).</param>
    /// <param name="nOut">Receives n_k for each kind (length at least <c>shares.Length</c>).</param>
    /// <param name="carryNumerators">Optional (may be empty): receives the carry r after each kind as a numerator over <see cref="MeanDist.Denominator"/>; skipped kinds repeat the incoming carry. Otherwise length at least <c>shares.Length</c>.</param>
    /// <returns>N = SUM n_k, or a negative <see cref="KindCountsStatus"/> code (with the outputs zeroed).</returns>
    public static int Compute(
        long v, ReadOnlySpan<KindShare> shares, in MeanDist md, in MailValueKnobs k, int spawnShift, Span<int> nOut, Span<Int128> carryNumerators)
    {
        nOut.Clear();
        carryNumerators.Clear();
        int status = Validate(v, shares, in md, nOut.Length, carryNumerators.Length);
        if (status != 0)
        {
            return status;
        }

        Span<long> vk = stackalloc long[MaxKinds];   // SUM(V x sharePm) per target kind, exact (cents x permille)
        vk.Clear();
        FoldShares(v, shares, vk);
        long shiftPm = 1000L + ((long)k.ShiftStepPm * (Math.Max(spawnShift, 1) - 1L));
        int total = Allocate(shares, vk, in md, shiftPm, nOut, carryNumerators);
        if (total < 0)
        {
            nOut.Clear();
            carryNumerators.Clear();
        }

        return total;
    }

    /// <summary>
    /// One F3 step on exact numerators (all over one common denominator):
    /// <c>n = max(0, floorDiv(2 (vk + r) + e, 2 e))</c> and <c>rNew = vk + r - n e</c>. Requires <paramref name="eNumerator"/> &gt; 0.
    /// </summary>
    /// <param name="vkNumerator">V_k scaled to the common denominator.</param>
    /// <param name="rNumerator">The incoming carry on the same denominator.</param>
    /// <param name="eNumerator">E_k on the same denominator.</param>
    /// <param name="rNew">The outgoing carry.</param>
    /// <returns>n, at least 0 (saturated to <see cref="int.MaxValue"/>).</returns>
    internal static int Step(Int128 vkNumerator, Int128 rNumerator, Int128 eNumerator, out Int128 rNew)
    {
        Int128 sum = vkNumerator + rNumerator;
        Int128 n = Fix.FloorDiv((2 * sum) + eNumerator, 2 * eNumerator);
        n = Int128.Clamp(n, 0, int.MaxValue);
        rNew = sum - (n * eNumerator);
        return (int)n;
    }

    /// <summary>Checks the header inputs, then every entry (all entries are validated before any fallback index is followed).</summary>
    private static int Validate(long v, ReadOnlySpan<KindShare> shares, in MeanDist md, int nLength, int carryLength)
    {
        if (md.HouseCount <= 0)
        {
            return KindCountsStatus.ZeroHouses;
        }

        if (shares.Length > MaxKinds)
        {
            return KindCountsStatus.TooManyKinds;
        }

        if (nLength < shares.Length || (carryLength != 0 && carryLength < shares.Length))
        {
            return KindCountsStatus.OutputTooSmall;
        }

        if (v < 0 || v > MaxV || !md.IsWithinLimit)
        {
            return KindCountsStatus.InvalidInput;
        }

        for (int i = 0; i < shares.Length; i++)
        {
            if (!IsEntryValid(shares[i], shares.Length))
            {
                return KindCountsStatus.InvalidInput;
            }
        }

        return 0;
    }

    private static bool IsEntryValid(in KindShare s, int count)
        => s.SharePm >= 0 && s.SharePm <= 1000
            && s.BaseValueCents >= 0 && s.BaseValueCents <= MaxBaseValue
            && s.FallbackIndex >= -1 && s.FallbackIndex < count;

    /// <summary>Adds each kind's exact <c>V x share</c> to its fallback target (own index when accepting; dropped on a dead end or cycle).</summary>
    private static void FoldShares(long v, ReadOnlySpan<KindShare> shares, Span<long> vk)
    {
        for (int i = 0; i < shares.Length; i++)
        {
            int target = ResolveTarget(shares, i);
            if (target >= 0)
            {
                vk[target] += v * shares[i].SharePm;
            }
        }
    }

    /// <summary>Follows the fallback chain from <paramref name="start"/> (at most <c>count</c> hops); -1 when it dead-ends or cycles. Entries are already validated.</summary>
    private static int ResolveTarget(ReadOnlySpan<KindShare> shares, int start)
    {
        int target = start;
        for (int hops = 0; hops <= shares.Length; hops++)
        {
            if (shares[target].HasAcceptingDestination)
            {
                return target;
            }

            target = shares[target].FallbackIndex;
            if (target < 0)
            {
                return -1;
            }
        }

        return -1;
    }

    /// <summary>The unprocessed kind with the greatest base value (ties to the lower index).</summary>
    private static int PickNext(ReadOnlySpan<KindShare> shares, uint done)
    {
        int pick = -1;
        for (int i = 0; i < shares.Length; i++)
        {
            if ((done & (1u << i)) == 0 && (pick < 0 || shares[i].BaseValueCents > shares[pick].BaseValueCents))
            {
                pick = i;
            }
        }

        return pick;
    }

    /// <summary>The carry recurrence over the kinds in descending base value; returns N or <see cref="KindCountsStatus.InvalidInput"/>.</summary>
    private static int Allocate(
        ReadOnlySpan<KindShare> shares, ReadOnlySpan<long> vk, in MeanDist md, long shiftPm, Span<int> nOut, Span<Int128> carryOut)
    {
        Int128 carry = Int128.Zero;
        long total = 0;
        uint done = 0;
        for (int step = 0; step < shares.Length; step++)
        {
            int pick = PickNext(shares, done);
            done |= 1u << pick;
            if (shares[pick].HasAcceptingDestination)
            {
                Int128 e = md.ExpectedNumerator(shares[pick].BaseValueCents, shiftPm);
                if (e <= 0)
                {
                    return KindCountsStatus.InvalidInput;
                }

                nOut[pick] = Step((Int128)vk[pick] * md.ShareScale, carry, e, out carry);
                total += nOut[pick];
            }

            if (carryOut.Length != 0)
            {
                carryOut[pick] = carry;
            }
        }

        return total > int.MaxValue ? KindCountsStatus.InvalidInput : (int)total;
    }
}
