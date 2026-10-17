using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The Mail manifest's RNG draw sequence, end to end (GDD mail-destinations Determinism; story 012; TR-mail-002/003).
/// Unconditionally, on the single <c>mail</c> stream for the shift: (1) one jitter per batch; (2) the Fisher-Yates
/// shuffle of the kind-ordered unit list; (3) the split by cumulative weight (no draws); (4) per batch one pair-rounding
/// draw, then per unit in manifest order its address draw and its Cursed roll. The total is exactly
/// <see cref="ExpectedDraws"/> = <c>2 nb + 2 N + max(0, N - 1)</c>. Data-only: the caller supplies every output span.
/// Assembling the <c>ShiftManifest</c>, PrepareShift, re-roll and lock belong to story 013. Never touches
/// <c>mail.special</c>: it receives only the one <see cref="RngStream"/>. 0 B, never throws.
/// </summary>
/// <example>
/// <code>
/// RngStream mail = view.Create(in RngNames.Mail, shift);
/// int nb = ManifestDraws.Run(mail, in plan, in knobs, cursedSharePm, addresses, kinds, ticks, counts, addrs, cursed);
/// </code>
/// </example>
public static class ManifestDraws
{
    /// <summary>The exact draw count K = 2 nb + 2 N + max(0, N - 1).</summary>
    /// <param name="batches">nb.</param>
    /// <param name="units">N.</param>
    /// <returns>K.</returns>
    public static long ExpectedDraws(int batches, int units)
        => (2L * batches) + (2L * units) + UnitShuffle.DrawCount(units);

    /// <summary>Performs draw steps (1) to (4).</summary>
    /// <param name="rng">The Mail-owned <c>mail</c> stream for the shift.</param>
    /// <param name="plan">The shift inputs (<see cref="BatchPlan.Units"/> is N).</param>
    /// <param name="k">The knobs.</param>
    /// <param name="cursedSharePm">The chance a unit is Cursed, in permille.</param>
    /// <param name="addresses">The unlocked addresses, ascending.</param>
    /// <param name="unitKinds">The kind-ordered unit list, shuffled in place (length at least N).</param>
    /// <param name="batchTicks">Receives t_b (length at least nb).</param>
    /// <param name="batchCounts">Receives c_b (length at least nb).</param>
    /// <param name="unitAddresses">Receives each unit's address in manifest order (length at least N).</param>
    /// <param name="unitCursed">Receives each unit's Cursed flag in manifest order (length at least N).</param>
    /// <returns>nb, or a negative <see cref="BatchScheduleStatus"/> / <see cref="DestinationDrawStatus"/> code. Range and span-length errors draw nothing; <see cref="BatchScheduleStatus.ZeroWeight"/> (every weight 0) is detected after the jitters are drawn.</returns>
    public static int Run(
        RngStream rng,
        in BatchPlan plan,
        in BatchKnobs k,
        int cursedSharePm,
        ReadOnlySpan<AddressId> addresses,
        Span<byte> unitKinds,
        Span<long> batchTicks,
        Span<int> batchCounts,
        Span<AddressId> unitAddresses,
        Span<bool> unitCursed)
    {
        int n = plan.Units;
        int nb = ValidatePlan(in plan, in k, addresses.Length);
        if (nb < 0)
        {
            return nb;
        }

        if (unitKinds.Length < n || unitAddresses.Length < n || unitCursed.Length < n)
        {
            return BatchScheduleStatus.OutputTooSmall;
        }

        if (batchTicks.Length < nb || batchCounts.Length < nb)
        {
            return BatchScheduleStatus.OutputTooSmall;
        }

        Span<int> jitter = stackalloc int[BatchSchedule.MaxBatches];
        int status = BatchSchedule.DrawJitters(rng, plan.DeliverySeconds, in k, jitter);
        if (status < 0)
        {
            return status;
        }

        UnitShuffle.Shuffle(rng, unitKinds[..n]);
        status = BatchSchedule.Layout(in plan, in k, jitter, batchTicks, batchCounts);
        return status < 0 ? status : DrawUnits(rng, nb, in k, cursedSharePm, addresses, batchCounts, unitAddresses, unitCursed);
    }

    private static int ValidatePlan(in BatchPlan plan, in BatchKnobs k, int addressCount)
    {
        int nb = BatchSchedule.BatchCount(plan.DeliverySeconds, in k);
        if (nb < 0)
        {
            return nb;
        }

        bool badRaid = plan.RaidWindowSeconds < 0 || plan.RaidWindowSeconds > plan.DeliverySeconds;
        bool noAddresses = plan.Units > 0 && addressCount == 0;
        return plan.Units < 0 || badRaid || noAddresses ? BatchScheduleStatus.InvalidInput : nb;
    }

    private static int DrawUnits(
        RngStream rng,
        int nb,
        in BatchKnobs k,
        int cursedSharePm,
        ReadOnlySpan<AddressId> addresses,
        ReadOnlySpan<int> counts,
        Span<AddressId> unitAddresses,
        Span<bool> unitCursed)
    {
        int offset = 0;
        for (int b = 0; b < nb; b++)
        {
            int m = counts[b];
            int pairs = DestinationDraw.DrawBatch(
                rng, addresses, m, k.StreetStreakRatioPm, cursedSharePm, unitAddresses[offset..], unitCursed[offset..]);
            if (pairs < 0)
            {
                return pairs;
            }

            offset += m;
        }

        return nb;
    }
}
