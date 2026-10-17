using System.Collections.Immutable;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// A mail kind (spec 08 section 2.2, Mail R1, CD10). Lengths are millimetres, times are shifts, the misdelivery
/// complaint is milli-points (authored 3/5/8/12/16, stored 3000...16000). References are <see cref="DefIndex"/>
/// values into the matching def table.
/// </summary>
/// <example>
/// <code>
/// var letter = new MailKindDef
/// {
///     Id = "letter", Name = "mail.letter", GridW = 1, GridH = 1, MaxStack = 20, BaseValueCents = 8,
///     WeightClass = WeightClass.Light, BeltLanes = 1, BeltLengthMm = 500,
///     AcceptedBy = ImmutableArray.Create(houseIndex), ComplaintOnMisdeliveryMilliPoints = 5000, UnlockShift = 1,
/// };
/// </code>
/// </example>
public sealed record class MailKindDef : IDef
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Localisation key.</summary>
    public required string Name { get; init; }

    /// <summary>Footprint width in cells (required).</summary>
    public required int GridW { get; init; }

    /// <summary>Footprint height in cells (required).</summary>
    public required int GridH { get; init; }

    /// <summary>Most pieces per stack (required).</summary>
    public required int MaxStack { get; init; }

    /// <summary>Base value in cents (required).</summary>
    public required long BaseValueCents { get; init; }

    /// <summary>Throw/weight class. Required with no default (CD7).</summary>
    public required WeightClass WeightClass { get; init; }

    /// <summary>True when the kind can be carried in the hands (default true; false for cargo).</summary>
    public bool CarryByHand { get; init; } = true;

    /// <summary>Belt lanes occupied, 1 or 2 (required).</summary>
    public required int BeltLanes { get; init; }

    /// <summary>Length of lane occupied in millimetres (from <c>beltLengthMetres</c>; required).</summary>
    public required int BeltLengthMm { get; init; }

    /// <summary>Shifts added to the spawn shift to give the deadline (default 0).</summary>
    public int DeadlineOffsetShifts { get; init; }

    /// <summary>The <see cref="DestinationTypeDef"/> indices that accept this kind (required; mirrors <see cref="DestinationTypeDef.Accepts"/>, CV-MAIL-01).</summary>
    public required ImmutableArray<DefIndex> AcceptedBy { get; init; }

    /// <summary>Complaint on misdelivery in milli-points (from <c>complaintOnMisdeliveryPoints</c>; required).</summary>
    public required int ComplaintOnMisdeliveryMilliPoints { get; init; }

    /// <summary>First shift in which the kind spawns (required).</summary>
    public required int UnlockShift { get; init; }

    /// <summary>The <see cref="MailKindDef"/> that takes this kind's spawn share when no accepting destination is unlocked (default <see cref="DefIndex.None"/>).</summary>
    public DefIndex FallbackKind { get; init; } = DefIndex.None;
}
