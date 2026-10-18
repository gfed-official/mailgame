using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// One row of a def type's code-side field table (content-data CD4, TR-content-006): name, kind, unit, required or default,
/// stored range, and the enum set or reference target. Story 018 builds the per-def tables from this shape; the list, object
/// and map kinds carry the nested M-1a fields (<c>grid</c>, <c>tags</c>, <c>catchVolume</c>, <c>shifts</c>, <c>shares</c>).
/// Immutable, so thread-safe.
/// </summary>
/// <example>
/// <code>
/// FieldSpec price = FieldSpec.Number("price", FieldUnit.Cents, required: true, min: 0, max: 1_000_000);
/// ScaledStatus s = price.ReadNumber("400"u8, out long cents);   // Ok, 400
/// </code>
/// </example>
public sealed record FieldSpec
{
    /// <summary>The reserved name of every def's key field; the schema walker applies the id lint to it.</summary>
    public const string IdName = "id";

    /// <summary>The JSON field name.</summary>
    public required string Name { get; init; }

    /// <summary>The value kind.</summary>
    public required FieldKind Kind { get; init; }

    /// <summary>The declared unit (a number, a number list or a map's values); 0 (undeclared) is only legal on other kinds.</summary>
    public FieldUnit Unit { get; init; }

    /// <summary>True when the field must be present (CV-GEN-05 when it is not).</summary>
    public bool Required { get; init; }

    /// <summary>
    /// The stored default when the field is absent and not required; <c>null</c> for none (an absent optional field then means
    /// "not present", for example no <c>buyPrice</c>). A bool default is 1 for true and 0 for false.
    /// </summary>
    public long? Default { get; init; }

    /// <summary>The data spelling of an enum row's default when absent (for example <c>none</c>); <c>null</c> for none.</summary>
    public string? DefaultSpelling { get; init; }

    /// <summary>The smallest stored value (numbers, list elements, map values).</summary>
    public long Min { get; init; } = int.MinValue;

    /// <summary>The largest stored value (numbers, list elements, map values).</summary>
    public long Max { get; init; } = int.MaxValue;

    /// <summary>
    /// True when the number is a duration: its range is [max(<see cref="Min"/>, 0), min(<see cref="Max"/>, 86 400 000)] ms
    /// (CV-GEN-07), and the unit must be Seconds or Ms.
    /// </summary>
    public bool IsDuration { get; init; }

    /// <summary>The closed-enum set name, for <see cref="FieldKind.Enum"/> and <see cref="FieldKind.EnumList"/>.</summary>
    public string? EnumSet { get; init; }

    /// <summary>The referenced def type name, for <see cref="FieldKind.Reference"/>, <see cref="FieldKind.RefList"/> and <see cref="FieldKind.RefMap"/>.</summary>
    public string? RefTarget { get; init; }

    /// <summary>The nested rows, for <see cref="FieldKind.Object"/> and <see cref="FieldKind.ObjectList"/> (itself self-checked); empty otherwise.</summary>
    public ImmutableArray<FieldSpec> Children { get; init; } = ImmutableArray<FieldSpec>.Empty;

    /// <summary>The required element count of a list row (2 for <c>grid</c>); 0 means any length.</summary>
    public int FixedLength { get; init; }

    /// <summary>The presence rule that depends on a sibling enum row (CV-GEN-14), or <c>null</c> when the row has none.</summary>
    public FieldVariant? Variant { get; init; }

    /// <summary>Creates a number row. <paramref name="unit"/> may be left undeclared; the table self-check then reports it.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="unit">The declared unit.</param>
    /// <param name="required">True when the field must be present.</param>
    /// <param name="min">The smallest stored value.</param>
    /// <param name="max">The largest stored value.</param>
    /// <param name="defaultValue">The stored default when absent.</param>
    /// <param name="isDuration">True for a duration field (Seconds or Ms).</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec s = FieldSpec.Number("loftDeg", FieldUnit.Deg, true, 0, 90_000);</code></example>
    public static FieldSpec Number(
        string name, FieldUnit unit, bool required = true, long min = int.MinValue, long max = int.MaxValue,
        long? defaultValue = null, bool isDuration = false) =>
        new() { Name = name, Kind = FieldKind.Number, Unit = unit, Required = required, Min = min, Max = max, Default = defaultValue, IsDuration = isDuration };

    /// <summary>Creates a row of a non-number kind.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="kind">The kind (not <see cref="FieldKind.Number"/>).</param>
    /// <param name="required">True when the field must be present.</param>
    /// <param name="enumSet">The enum set name, for an enum field.</param>
    /// <param name="refTarget">The referenced def type, for a reference field.</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec s = FieldSpec.Other("category", FieldKind.Enum, enumSet: "ItemCategory");</code></example>
    public static FieldSpec Other(string name, FieldKind kind, bool required = true, string? enumSet = null, string? refTarget = null) =>
        new() { Name = name, Kind = kind, Required = required, EnumSet = enumSet, RefTarget = refTarget };

    /// <summary>Creates the required <c>id</c> row (a string the schema walker lints).</summary>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec id = FieldSpec.Id();   // Name == "id", Required</code></example>
    public static FieldSpec Id() => Other(IdName, FieldKind.Text);

    /// <summary>Creates a string row (a localisation key or free text).</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="required">True when the field must be present.</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec name = FieldSpec.Text("name");</code></example>
    public static FieldSpec Text(string name, bool required = true) => Other(name, FieldKind.Text, required);

    /// <summary>Creates a bool row; a bool is never absent-meaningful, so an optional one carries its default.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="required">True when the field must be present (then it must not carry a default).</param>
    /// <param name="defaultValue">The value when absent; ignored when <paramref name="required"/> is true.</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec s = FieldSpec.Bool("handCarried", required: false, defaultValue: false);</code></example>
    public static FieldSpec Bool(string name, bool required = false, bool defaultValue = false) =>
        new() { Name = name, Kind = FieldKind.Bool, Required = required, Default = required ? null : (defaultValue ? 1 : 0) };

    /// <summary>Creates a closed-enum row.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="enumSet">The enum set name.</param>
    /// <param name="required">True when the field must be present.</param>
    /// <param name="defaultSpelling">The data spelling used when absent; must be <c>null</c> for a required row.</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec s = FieldSpec.Enumeration("equipSlot", "EquipSlot", required: false, defaultSpelling: "none");</code></example>
    public static FieldSpec Enumeration(string name, string enumSet, bool required = true, string? defaultSpelling = null) =>
        new() { Name = name, Kind = FieldKind.Enum, Required = required, EnumSet = enumSet, DefaultSpelling = defaultSpelling };

    /// <summary>Creates a single def-reference row.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="refTarget">The referenced def type name.</param>
    /// <param name="required">True when the field must be present; an absent optional reference means "none".</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec s = FieldSpec.Reference("fallbackKind", "MailKindDef", required: false);</code></example>
    public static FieldSpec Reference(string name, string refTarget, bool required = true) =>
        Other(name, FieldKind.Reference, required, refTarget: refTarget);

    /// <summary>Creates a list-of-numbers row; the unit, <paramref name="min"/> and <paramref name="max"/> apply to each element.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="unit">The element unit.</param>
    /// <param name="fixedLength">The required element count; 0 for any length.</param>
    /// <param name="required">True when the field must be present.</param>
    /// <param name="min">The smallest stored element.</param>
    /// <param name="max">The largest stored element.</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec grid = FieldSpec.IntList("grid", FieldUnit.Count, fixedLength: 2, min: 1, max: 255);</code></example>
    public static FieldSpec IntList(
        string name, FieldUnit unit, int fixedLength = 0, bool required = true, long min = int.MinValue, long max = int.MaxValue) =>
        new() { Name = name, Kind = FieldKind.IntList, Unit = unit, Required = required, Min = min, Max = max, FixedLength = fixedLength };

    /// <summary>Creates a list-of-strings row.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="required">True when the field must be present.</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec tags = FieldSpec.TextList("tags", required: false);</code></example>
    public static FieldSpec TextList(string name, bool required = true) => Other(name, FieldKind.TextList, required);

    /// <summary>Creates a list-of-enum-spellings row.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="enumSet">The enum set name of the elements.</param>
    /// <param name="required">True when the field must be present; optional and absent means "any".</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec cats = FieldSpec.EnumList("allowedCategories", "ItemCategory", required: false);</code></example>
    public static FieldSpec EnumList(string name, string enumSet, bool required = true) =>
        Other(name, FieldKind.EnumList, required, enumSet: enumSet);

    /// <summary>Creates a list-of-def-references row.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="refTarget">The referenced def type name of the elements.</param>
    /// <param name="required">True when the field must be present.</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec s = FieldSpec.RefList("acceptedBy", "DestinationTypeDef");</code></example>
    public static FieldSpec RefList(string name, string refTarget, bool required = true) =>
        Other(name, FieldKind.RefList, required, refTarget: refTarget);

    /// <summary>Creates a nested-object row described by <paramref name="children"/>.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="children">The nested rows, in canonical order.</param>
    /// <param name="required">True when the field must be present.</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec s = FieldSpec.Object("catchVolume", ImmutableArray.Create(FieldSpec.Number("yawDeg", FieldUnit.Deg)));</code></example>
    public static FieldSpec Object(string name, ImmutableArray<FieldSpec> children, bool required = true) =>
        new() { Name = name, Kind = FieldKind.Object, Required = required, Children = children };

    /// <summary>Creates a list-of-objects row; each element is described by <paramref name="children"/>.</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="children">The element's rows, in canonical order.</param>
    /// <param name="required">True when the field must be present.</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec s = FieldSpec.ObjectList("shifts", ImmutableArray.Create(FieldSpec.Number("shift", FieldUnit.Count)));</code></example>
    public static FieldSpec ObjectList(string name, ImmutableArray<FieldSpec> children, bool required = true) =>
        new() { Name = name, Kind = FieldKind.ObjectList, Required = required, Children = children };

    /// <summary>Creates an id-keyed map row whose values are numbers (<c>shares</c>: kind id to Ratio).</summary>
    /// <param name="name">The JSON field name.</param>
    /// <param name="refTarget">The def type the keys name.</param>
    /// <param name="unit">The value unit.</param>
    /// <param name="required">True when the field must be present.</param>
    /// <param name="min">The smallest stored value.</param>
    /// <param name="max">The largest stored value.</param>
    /// <returns>The row.</returns>
    /// <example><code>FieldSpec s = FieldSpec.RefMap("shares", "MailKindDef", FieldUnit.Ratio, min: 0, max: 1000);</code></example>
    public static FieldSpec RefMap(
        string name, string refTarget, FieldUnit unit, bool required = true, long min = int.MinValue, long max = int.MaxValue) =>
        new() { Name = name, Kind = FieldKind.RefMap, Unit = unit, Required = required, RefTarget = refTarget, Min = min, Max = max };

    /// <summary>Returns a copy of this row with a variant rule attached.</summary>
    /// <param name="on">The sibling enum row that decides presence.</param>
    /// <param name="requiredFor">Discriminator spellings for which the field must be present.</param>
    /// <param name="forbiddenFor">Discriminator spellings for which the field must be absent.</param>
    /// <returns>The row with <see cref="Variant"/> set.</returns>
    /// <example><code>FieldSpec g = FieldSpec.IntList("grid", FieldUnit.Count, 2, false).WithVariant("layout", new[] { "grid" }, new[] { "pile" });</code></example>
    public FieldSpec WithVariant(string on, string[] requiredFor, string[] forbiddenFor) =>
        this with { Variant = new FieldVariant(on, ImmutableArray.Create(requiredFor), ImmutableArray.Create(forbiddenFor)) };

    /// <summary>Reads a number token under this row's unit and range (duration rows use the duration cap clamped by the row's own range).</summary>
    /// <param name="token">The raw number token bytes.</param>
    /// <param name="value">The stored value; 0 unless the result is <see cref="ScaledStatus.Ok"/>.</param>
    /// <returns>The verdict; map it with <see cref="DecimalToScaled.RuleIdOf"/>.</returns>
    /// <example><code>ScaledStatus s = spec.ReadNumber("1.5"u8, out long mm);</code></example>
    public ScaledStatus ReadNumber(ReadOnlySpan<byte> token, out long value) =>
        IsDuration
            ? DecimalToScaled.ReadDuration(token, Unit, Min, Max, out value)
            : DecimalToScaled.Read(token, Unit, Min, Max, out value);
}
