using PerformativeMail.Sim.Content;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The batch-timing and street knobs of F2/F4 (GDD mail-destinations sections F2 and F4; story 012). Immutable, 0 B.
/// The source is <see cref="MailMixDef"/> (millisecond fields are converted to whole seconds); none of these have
/// <c>mail.*</c> balance keys, and <see cref="Default"/> carries the GDD defaults.
/// </summary>
/// <param name="BatchIntervalSeconds">Spacing between batches (GDD default 15).</param>
/// <param name="BatchJitterSeconds">Jitter half-range in seconds (GDD default 3, i.e. +/-90 ticks).</param>
/// <param name="TailGuardSeconds">No batch fires within this many seconds of Delivery's end (GDD default 9).</param>
/// <param name="RaidSpawnRateMultPm">Batch weight in the raid window, permille (GDD default 500).</param>
/// <param name="OpeningRampBatches">Opening batches that carry half weight (GDD default 0).</param>
/// <param name="StreetStreakRatioPm">Share of a batch's units that share a street, permille (GDD default 300).</param>
/// <example>
/// <code>
/// BatchKnobs k = BatchKnobs.Default;                 // 15 / 3 / 9 / 500 / 0 / 300
/// BatchKnobs fromContent = BatchKnobs.From(mixDef);
/// </code>
/// </example>
public readonly record struct BatchKnobs(
    int BatchIntervalSeconds,
    int BatchJitterSeconds,
    int TailGuardSeconds,
    int RaidSpawnRateMultPm,
    int OpeningRampBatches,
    int StreetStreakRatioPm)
{
    /// <summary>The GDD defaults: 15 / 3 / 9 / 500 / 0 / 300.</summary>
    public static BatchKnobs Default => new(15, 3, 9, 500, 0, 300);

    /// <summary>Builds the knobs from a mail mix (milliseconds are divided down to whole seconds).</summary>
    /// <param name="mix">The mail mix def.</param>
    /// <returns>The knobs.</returns>
    public static BatchKnobs From(MailMixDef mix)
    {
        ArgumentNullException.ThrowIfNull(mix);
        return new BatchKnobs(
            mix.BatchIntervalMs / 1000,
            mix.BatchJitterMs / 1000,
            mix.TailGuardMs / 1000,
            mix.RaidSpawnRateRatioPm,
            mix.OpeningRampBatches,
            mix.StreetStreakRatioPm);
    }
}
