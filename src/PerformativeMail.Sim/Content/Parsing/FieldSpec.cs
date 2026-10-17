using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Rules;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>The value kind of a <see cref="FieldSpec"/> row.</summary>
public enum FieldKind : byte
{
    /// <summary>A number; it must declare a <see cref="FieldUnit"/>.</summary>
    Number = 1,

    /// <summary>A string.</summary>
    Text = 2,

    /// <summary>A boolean.</summary>
    Bool = 3,

    /// <summary>A closed-enum spelling; <see cref="FieldSpec.EnumSet"/> names the set.</summary>
    Enum = 4,

    /// <summary>A reference to another def; <see cref="FieldSpec.RefTarget"/> names the def type.</summary>
    Reference = 5,
}

/// <summary>
/// One row of a def type's code-side field table (content-data CD4, TR-content-006): name, kind, unit, required or default,
/// stored range, and the enum set or reference target. Story 018 builds the per-def tables from this shape. Immutable, so
/// thread-safe.
/// </summary>
/// <example>
/// <code>
/// FieldSpec price = FieldSpec.Number("price", FieldUnit.Cents, required: true, min: 0, max: 1_000_000);
/// ScaledStatus s = price.ReadNumber("400"u8, out long cents);   // Ok, 400
/// </code>
/// </example>
public sealed record FieldSpec
{
    /// <summary>The JSON field name.</summary>
    public required string Name { get; init; }

    /// <summary>The value kind.</summary>
    public required FieldKind Kind { get; init; }

    /// <summary>The declared unit; 0 (undeclared) is only legal on a non-number kind.</summary>
    public FieldUnit Unit { get; init; }

    /// <summary>True when the field must be present (CV-GEN-05 when it is not).</summary>
    public bool Required { get; init; }

    /// <summary>The stored default when the field is absent and not required; <c>null</c> for none.</summary>
    public long? Default { get; init; }

    /// <summary>The smallest stored value (numbers).</summary>
    public long Min { get; init; } = int.MinValue;

    /// <summary>The largest stored value (numbers).</summary>
    public long Max { get; init; } = int.MaxValue;

    /// <summary>True when the number is a duration, capped at 0 to 86 400 000 ms (CV-GEN-07).</summary>
    public bool IsDuration { get; init; }

    /// <summary>The closed-enum set name, for <see cref="FieldKind.Enum"/>.</summary>
    public string? EnumSet { get; init; }

    /// <summary>The referenced def type name, for <see cref="FieldKind.Reference"/>.</summary>
    public string? RefTarget { get; init; }

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

    /// <summary>Reads a number token under this row's unit and range (duration rows use the duration cap).</summary>
    /// <param name="token">The raw number token bytes.</param>
    /// <param name="value">The stored value; 0 unless the result is <see cref="ScaledStatus.Ok"/>.</param>
    /// <returns>The verdict; map it with <see cref="DecimalToScaled.RuleIdOf"/>.</returns>
    /// <example><code>ScaledStatus s = spec.ReadNumber("1.5"u8, out long mm);</code></example>
    public ScaledStatus ReadNumber(ReadOnlySpan<byte> token, out long value) =>
        IsDuration
            ? DecimalToScaled.ReadDuration(token, Unit, out value)
            : DecimalToScaled.Read(token, Unit, Min, Max, out value);
}

/// <summary>A spec-table self-check finding: the field and the rule it breaks.</summary>
/// <param name="FieldName">The offending row's name.</param>
/// <param name="RuleId">The catalogue rule id (CV-GEN-06).</param>
public readonly record struct FieldSpecIssue(string FieldName, string RuleId);

/// <summary>
/// The spec table's self-check (TR-content-006): every number row must declare a unit.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;FieldSpecIssue&gt; issues = FieldSpecTable.SelfCheck(new[] { FieldSpec.Number("grid", default) });
/// // one issue: ("grid", "CV-GEN-06")
/// </code>
/// </example>
public static class FieldSpecTable
{
    /// <summary>Finds each number row with no declared unit (CV-GEN-06).</summary>
    /// <param name="specs">The rows of one def type's table.</param>
    /// <returns>One issue per offending row, in table order; empty when the table is clean.</returns>
    /// <example><code>bool clean = FieldSpecTable.SelfCheck(rows).IsEmpty;</code></example>
    public static ImmutableArray<FieldSpecIssue> SelfCheck(IReadOnlyList<FieldSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        ImmutableArray<FieldSpecIssue>.Builder issues = ImmutableArray.CreateBuilder<FieldSpecIssue>();
        for (int i = 0; i < specs.Count; i++)
        {
            FieldSpec spec = specs[i];
            if (spec.Kind == FieldKind.Number && !FieldUnits.IsDeclared(spec.Unit))
            {
                issues.Add(new FieldSpecIssue(spec.Name, GeneralRuleIds.NumberFormat));
            }
        }

        return issues.ToImmutable();
    }
}
