using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Parsing;

namespace PerformativeMail.Sim.Content.Defs;

/// <summary>
/// The <see cref="ContainerDef"/> field table (content-data CD10, story 018), in canonical encode order. <c>grid</c> is a
/// variant of <c>layout</c> (required for <c>grid</c> and <c>slots</c>, forbidden for <c>pile</c>: CV-GEN-14) and
/// <c>reservedSlots</c> is for the <c>slots</c> layout only. <c>allowedCategories</c> omitted means any; present and empty is
/// CV-GRD-03. <c>grid</c> elements allow 0 so CV-GRD-04 (not CV-GEN-12) reports a dimension below 1. Defaults are neutral except
/// the GDD-stated <c>acceptsInsert</c> true and <c>view</c> auto (CD10). Built fresh on each call.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;FieldSpec&gt; rows = ContainerDefFieldSpec.Rows;   // id, layout, grid, view, ...
/// </code>
/// </example>
public static class ContainerDefFieldSpec
{
    /// <summary>The rows, in canonical order; a new array on each call.</summary>
    public static ImmutableArray<FieldSpec> Rows => ImmutableArray.Create(
        FieldSpec.Id(),
        FieldSpec.Enumeration("layout", "ContainerLayout"),
        FieldSpec.IntList("grid", FieldUnit.Count, fixedLength: 2, required: false, min: 0, max: 255)
            .WithVariant("layout", new[] { "grid", "slots" }, new[] { "pile" }),
        FieldSpec.Enumeration("view", "ContainerView", required: false, defaultSpelling: "auto"), // GDD default (CD10): auto.
        FieldSpec.Enumeration("beltAccess", "BeltAccess", required: false, defaultSpelling: "none"),
        FieldSpec.EnumList("allowedCategories", "ItemCategory", required: false),
        FieldSpec.Bool("handCarried", defaultValue: false),
        FieldSpec.Bool("acceptsInsert", defaultValue: true), // GDD exception to neutral defaults: acceptsInsert defaults to true (CD10).
        FieldSpec.IntList("reservedSlots", FieldUnit.Count, required: false, min: 0, max: 65_535)
            .WithVariant("layout", Array.Empty<string>(), new[] { "grid", "pile" }));
}
