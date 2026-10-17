using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// The mail mix and batch timing (spec 08 section 2.3, CD10). Times are milliseconds and every ratio is permille.
/// ASSUMPTION (flagged): <c>spawnOverhead</c> has no settled unit-suffixed name; it is modelled as
/// <see cref="SpawnOverheadPm"/> (spec value 1.6 = 1600) until Mail's re-review renames it. The spec fields
/// <c>lateValueRatio</c> and <c>misdeliveryPenaltyRatio</c> are stats (CD11), not mix fields.
/// </summary>
/// <example>
/// <code>
/// var mix = new MailMixDef
/// {
///     Id = "default", Shifts = ImmutableArray.Create(shift1), BatchIntervalMs = 15000, BatchJitterMs = 3000,
///     TailGuardMs = 5000, SpawnOverheadPm = 1600, StreetStreakRatioPm = 300, RaidSpawnRateRatioPm = 500,
///     DistanceStepRatioPm = 250, ShiftStepRatioPm = 100,
/// };
/// </code>
/// </example>
public sealed record class MailMixDef : IDef
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>The per-shift spawn mixes (required).</summary>
    public required ImmutableArray<ShiftMix> Shifts { get; init; }

    /// <summary>Time between batches in milliseconds (required).</summary>
    public required int BatchIntervalMs { get; init; }

    /// <summary>Batch jitter in milliseconds (required).</summary>
    public required int BatchJitterMs { get; init; }

    /// <summary>No batch fires within this many milliseconds of the phase end (from <c>tailGuardSeconds</c>; required).</summary>
    public required int TailGuardMs { get; init; }

    /// <summary>Opening batches that spawn at half weight (default 0).</summary>
    public int OpeningRampBatches { get; init; }

    /// <summary>Spawn overhead multiplier in permille (spec <c>spawnOverhead</c> 1.6 = 1600; name assumed, see the type remarks; required).</summary>
    public required int SpawnOverheadPm { get; init; }

    /// <summary>Street streak ratio in permille (required).</summary>
    public required int StreetStreakRatioPm { get; init; }

    /// <summary>Raid-window spawn rate ratio in permille (required).</summary>
    public required int RaidSpawnRateRatioPm { get; init; }

    /// <summary>Per-district distance step ratio in permille (required).</summary>
    public required int DistanceStepRatioPm { get; init; }

    /// <summary>Per-shift step ratio in permille (required).</summary>
    public required int ShiftStepRatioPm { get; init; }
}
