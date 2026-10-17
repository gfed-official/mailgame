using System.Collections.Immutable;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// A destination type (Mail R3, CD10). OPEN FLAG: <c>insertRate</c> is "2.0/s" in Mail R3 but "2000 milli/s" in
/// AC M-R3-1 and CD4 has no rate suffix row; it is stored as integer thousandths of a piece per second per AC M-R3-1
/// (<see cref="InsertRateMilliPerSec"/>) pending the Mail / Content re-review. The spec 08 field
/// <c>requiresVehicleZone</c> has no M-1a reader and is not modelled.
/// </summary>
/// <example>
/// <code>
/// var house = new DestinationTypeDef
/// {
///     Id = "house", Accepts = ImmutableArray.Create(letterIndex), InsertRateMilliPerSec = 2000,
///     ManualInsertHoldMs = 400, MaxAutomatedFeeders = 1,
///     CatchVolume = new CatchVolume(1000, 800, 1000, 1000, 0),
/// };
/// </code>
/// </example>
public sealed record class DestinationTypeDef : IDef
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>The <see cref="MailKindDef"/> indices this type takes (required).</summary>
    public required ImmutableArray<DefIndex> Accepts { get; init; }

    /// <summary>Automated feeder rate in thousandths of a piece per second (2.0/s = 2000; required).</summary>
    public required int InsertRateMilliPerSec { get; init; }

    /// <summary>Base deliver hold in milliseconds (from <c>manualInsertHoldSeconds</c>; 0 = no Deliver anchor; required).</summary>
    public required int ManualInsertHoldMs { get; init; }

    /// <summary>Placed feeders per destination (required).</summary>
    public required int MaxAutomatedFeeders { get; init; }

    /// <summary>True when a unit/box number is needed (default false).</summary>
    public bool RequiresUnit { get; init; }

    /// <summary>The landing volume (required).</summary>
    public required CatchVolume CatchVolume { get; init; }
}
