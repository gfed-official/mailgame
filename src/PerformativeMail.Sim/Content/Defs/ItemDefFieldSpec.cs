using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Parsing;

namespace PerformativeMail.Sim.Content.Defs;

/// <summary>
/// The <see cref="ItemDef"/> field table (content-data CD10, story 018): the JSON fields in canonical encode order
/// (<c>DefCanonical.WriteCanonical(ItemDef)</c>). Built fresh on each call, so the class holds no static state.
/// Defaults are neutral (0, false, none) except <c>carryByHand</c>, which defaults to true by the GDD (CD10 Grid R5).
/// <c>buyPrice</c> has no default: absent means the item has no buy price. <c>maxStack</c> allows 0 so CV-GRD-04 (not CV-GEN-12)
/// reports a stack below 1.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;FieldSpec&gt; rows = ItemDefFieldSpec.Rows;   // id, name, category, grid, maxStack, ...
/// </code>
/// </example>
public static class ItemDefFieldSpec
{
    /// <summary>The rows, in canonical order; a new array on each call.</summary>
    public static ImmutableArray<FieldSpec> Rows => ImmutableArray.Create(
        FieldSpec.Id(),
        FieldSpec.Text("name"),
        FieldSpec.Enumeration("category", "ItemCategory"),
        FieldSpec.IntList("grid", FieldUnit.Count, fixedLength: 2, min: 1, max: 255),
        FieldSpec.Number("maxStack", FieldUnit.Count, min: 0, max: 65_535),
        FieldSpec.Enumeration("weightClass", "WeightClass"),
        FieldSpec.Number("sellPrice", FieldUnit.Cents, required: false, min: 0, defaultValue: 0),
        FieldSpec.Number("buyPrice", FieldUnit.Cents, required: false, min: 0),
        FieldSpec.Bool("carryByHand", defaultValue: true), // GDD exception to neutral defaults: carryByHand defaults to true (CD10, Grid R5).
        FieldSpec.Enumeration("equipSlot", "EquipSlot", required: false, defaultSpelling: "none"),
        FieldSpec.TextList("tags", required: false));
}
