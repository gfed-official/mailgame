using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>Negative result codes of <see cref="BatchSchedule"/>; a non-negative result is the batch count nb.</summary>
public static class BatchScheduleStatus
{
    /// <summary>An input is out of range (D, interval, jitter, tail guard, raid window, weights, unit count).</summary>
    public const int InvalidInput = -1;

    /// <summary>nb exceeds <see cref="BatchSchedule.MaxBatches"/>.</summary>
    public const int TooManyBatches = -2;

    /// <summary>An output span is shorter than nb.</summary>
    public const int OutputTooSmall = -3;

    /// <summary>The total weight W is not positive, so units cannot be split.</summary>
    public const int ZeroWeight = -4;
}

/// <summary>The per-shift inputs of F2: where Delivery starts, how long it runs, the raid window and the unit count N.</summary>
/// <param name="DeliveryStartTick">The tick Delivery starts.</param>
/// <param name="DeliverySeconds">D, the Delivery length in seconds.</param>
/// <param name="RaidWindowSeconds">The raid overlap at the end of Delivery (0 on shift 1).</param>
/// <param name="Units">N, the units in the shift (F3).</param>
public readonly record struct BatchPlan(long DeliveryStartTick, int DeliverySeconds, int RaidWindowSeconds, int Units);

/// <summary>
/// F2, the batch schedule (GDD mail-destinations section F2 and Determinism step 1; story 012; TR-mail-002/003).
/// <c>nb = ceil(D / interval)</c>; <c>t_b = start + 30 x interval x b + j_b</c> clamped to
/// <c>start + 30 x (D - tailGuard)</c>; <c>w_b</c> is the raid multiplier when <c>t_b</c> is in the raid window, else 1000,
/// halved for <c>b &lt; openingRampBatches</c>; <c>c_b = floor(N cumW_b / W) - floor(N cumW_(b-1) / W)</c> in <c>long</c>.
/// <para>
/// Split in two so tests can force jitter: <see cref="DrawJitters"/> is the only RNG part (one draw per batch,
/// batch 0 in [0, +J], others [-J, +J], J = jitterSeconds x 30); <see cref="Layout"/> is pure. 0 B, never throws.
/// </para>
/// <para>
/// AMBIGUITY (flagged): a batch both in the raid window and below the ramp is not specified; the ramp halves the
/// weight (raid x 500 / 1000) rather than overriding it. Unreachable with the default knobs.
/// </para>
/// </summary>
/// <example>
/// <code>
/// int nb = BatchSchedule.DrawJitters(stream, d, in knobs, jitter);
/// BatchSchedule.Layout(in plan, in knobs, jitter, ticks, counts);   // counts sum to plan.Units
/// </code>
/// </example>
public static class BatchSchedule
{
    /// <summary>The most batches supported (scratch buffers are sized to it).</summary>
    public const int MaxBatches = 128;

    private const int TicksPerSecond = SimSettings.TickRateHz;
    private const int FullWeightPm = 1000;
    private const int RampWeightPm = 500;

    /// <summary>nb = ceil(D / batchIntervalSeconds).</summary>
    /// <param name="deliverySeconds">D.</param>
    /// <param name="k">The knobs.</param>
    /// <returns>nb, or <see cref="BatchScheduleStatus.InvalidInput"/> / <see cref="BatchScheduleStatus.TooManyBatches"/>.</returns>
    public static int BatchCount(int deliverySeconds, in BatchKnobs k)
    {
        if (deliverySeconds <= 0 || k.BatchIntervalSeconds <= 0 || k.BatchJitterSeconds < 0
            || k.TailGuardSeconds < 0 || k.TailGuardSeconds >= deliverySeconds
            || k.RaidSpawnRateMultPm < 0 || k.OpeningRampBatches < 0)
        {
            return BatchScheduleStatus.InvalidInput;
        }

        int nb = (deliverySeconds + k.BatchIntervalSeconds - 1) / k.BatchIntervalSeconds;
        return nb > MaxBatches ? BatchScheduleStatus.TooManyBatches : nb;
    }

    /// <summary>Draws one jitter per batch: batch 0 uniform in [0, J], the others in [-J, +J] (ticks); always one draw each, even when J = 0.</summary>
    /// <param name="rng">The Mail-owned <c>mail</c> stream for the shift.</param>
    /// <param name="deliverySeconds">D.</param>
    /// <param name="k">The knobs.</param>
    /// <param name="jitterOut">Receives j_b in ticks (length at least nb).</param>
    /// <returns>nb (the number of draws made), or a negative <see cref="BatchScheduleStatus"/> code with nothing drawn.</returns>
    public static int DrawJitters(RngStream rng, int deliverySeconds, in BatchKnobs k, Span<int> jitterOut)
    {
        int nb = BatchCount(deliverySeconds, in k);
        if (nb < 0)
        {
            return nb;
        }

        if (jitterOut.Length < nb)
        {
            return BatchScheduleStatus.OutputTooSmall;
        }

        uint j = (uint)(k.BatchJitterSeconds * TicksPerSecond);
        jitterOut[0] = (int)rng.NextInt(j + 1);
        for (int b = 1; b < nb; b++)
        {
            jitterOut[b] = (int)rng.NextInt((2 * j) + 1) - (int)j;
        }

        return nb;
    }

    /// <summary>Computes batch ticks and unit counts from given jitters (no RNG).</summary>
    /// <param name="plan">The shift inputs.</param>
    /// <param name="k">The knobs.</param>
    /// <param name="jitterTicks">j_b per batch (length at least nb).</param>
    /// <param name="ticksOut">Receives t_b (length at least nb).</param>
    /// <param name="countsOut">Receives c_b (length at least nb); sums to <see cref="BatchPlan.Units"/>.</param>
    /// <returns>nb, or a negative <see cref="BatchScheduleStatus"/> code.</returns>
    public static int Layout(in BatchPlan plan, in BatchKnobs k, ReadOnlySpan<int> jitterTicks, Span<long> ticksOut, Span<int> countsOut)
    {
        int nb = BatchCount(plan.DeliverySeconds, in k);
        if (nb < 0)
        {
            return nb;
        }

        if (plan.Units < 0 || plan.RaidWindowSeconds < 0 || plan.RaidWindowSeconds > plan.DeliverySeconds)
        {
            return BatchScheduleStatus.InvalidInput;
        }

        if (jitterTicks.Length < nb || ticksOut.Length < nb || countsOut.Length < nb)
        {
            return BatchScheduleStatus.OutputTooSmall;
        }

        long total = PlaceBatches(in plan, in k, nb, jitterTicks, ticksOut);
        return total <= 0 ? BatchScheduleStatus.ZeroWeight : SplitUnits(in plan, in k, nb, total, ticksOut, countsOut);
    }

    /// <summary>The weight w_b of batch <paramref name="b"/> firing at <paramref name="tick"/>, in permille.</summary>
    /// <param name="plan">The shift inputs.</param>
    /// <param name="k">The knobs.</param>
    /// <param name="b">The batch index.</param>
    /// <param name="tick">The batch's (clamped, jittered) tick.</param>
    /// <returns>The weight.</returns>
    public static long Weight(in BatchPlan plan, in BatchKnobs k, int b, long tick)
    {
        long raidStart = plan.DeliveryStartTick + ((long)TicksPerSecond * (plan.DeliverySeconds - plan.RaidWindowSeconds));
        long w = plan.RaidWindowSeconds > 0 && tick >= raidStart ? k.RaidSpawnRateMultPm : FullWeightPm;
        return b < k.OpeningRampBatches ? w * RampWeightPm / FullWeightPm : w;
    }

    private static long PlaceBatches(in BatchPlan plan, in BatchKnobs k, int nb, ReadOnlySpan<int> jitterTicks, Span<long> ticksOut)
    {
        long step = (long)TicksPerSecond * k.BatchIntervalSeconds;
        long last = plan.DeliveryStartTick + ((long)TicksPerSecond * (plan.DeliverySeconds - k.TailGuardSeconds));
        long total = 0;
        for (int b = 0; b < nb; b++)
        {
            long t = plan.DeliveryStartTick + (step * b) + jitterTicks[b];
            ticksOut[b] = t > last ? last : t;
            total += Weight(in plan, in k, b, ticksOut[b]);
        }

        return total;
    }

    private static int SplitUnits(in BatchPlan plan, in BatchKnobs k, int nb, long total, ReadOnlySpan<long> ticks, Span<int> countsOut)
    {
        long cum = 0;
        long prev = 0;
        for (int b = 0; b < nb; b++)
        {
            cum += Weight(in plan, in k, b, ticks[b]);
            long cur = plan.Units * cum / total;
            countsOut[b] = (int)(cur - prev);
            prev = cur;
        }

        return nb;
    }
}
