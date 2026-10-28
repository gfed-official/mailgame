using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Parsing;

namespace PerformativeMail.Sim.Content.Defs;

/// <summary>
/// The <see cref="MailKindDef"/> field table (content-data CD10, story 018), in canonical encode order. Money is Cents,
/// <c>beltLengthMetres</c> Metres, <c>complaintOnMisdeliveryPoints</c> Points. <c>fallbackKind</c> is optional (absent = none).
/// <c>maxStack</c> allows 0 so CV-GRD-04 reports a stack below 1. Defaults are neutral, <c>carryByHand</c> included (false;
/// CD10 owner ruling 2026-10-21). Built fresh on each call.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;FieldSpec&gt; rows = MailKindDefFieldSpec.Rows;   // id, name, grid, ...
/// </code>
/// </example>
public static class MailKindDefFieldSpec
{
    /// <summary>The rows, in canonical order; a new array on each call.</summary>
    public static ImmutableArray<FieldSpec> Rows => ImmutableArray.Create(
        FieldSpec.Id(),
        FieldSpec.Text("name"),
        FieldSpec.IntList("grid", FieldUnit.Count, fixedLength: 2, min: 1, max: 255),
        FieldSpec.Number("maxStack", FieldUnit.Count, min: 0, max: 65_535),
        FieldSpec.Number("baseValue", FieldUnit.Cents, min: 0),
        FieldSpec.Enumeration("weightClass", "WeightClass"),
        FieldSpec.Bool("carryByHand", defaultValue: false),
        FieldSpec.Number("beltLanes", FieldUnit.Count, min: 0),
        FieldSpec.Number("beltLengthMetres", FieldUnit.Metres, min: 0),
        FieldSpec.Number("deadlineOffsetShifts", FieldUnit.Count, required: false, min: 0, defaultValue: 0),
        FieldSpec.RefList("acceptedBy", nameof(DestinationTypeDef)),
        FieldSpec.Number("complaintOnMisdeliveryPoints", FieldUnit.Points, min: 0),
        FieldSpec.Number("unlockShift", FieldUnit.Count, min: 0),
        FieldSpec.Reference("fallbackKind", nameof(MailKindDef), required: false));
}
