using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Rules;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The spec table's self-check (TR-content-006, TD-090): a table defect otherwise surfaces as a content error on every token
/// (a missing unit is CV-GEN-06 on each value), so the loader and a test run it over every table. It flags a numeric row with no
/// unit; an <see cref="FieldSpec.IsDuration"/> row whose unit is not Seconds or Ms; Min above Max; a default outside [Min, Max];
/// an enum row with no set; a reference row with no target; a default on a required row; an object row with no children;
/// duplicate names; a variant rule with no enum discriminator; and a fixed length on a non-list. Nested tables are checked with a
/// dotted path. Stateless and thread-safe.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;FieldSpecIssue&gt; issues = FieldSpecTable.SelfCheck(new[] { FieldSpec.Number("grid", default) });
/// // one issue: ("grid", "CV-GEN-06", MissingUnit)
/// </code>
/// </example>
public static class FieldSpecTable
{
    /// <summary>Checks one def type's table (and every nested table) for the defects listed on the type.</summary>
    /// <param name="specs">The rows of one def type's table.</param>
    /// <returns>The issues in table order (a row's own issues before its children's); empty when the table is clean.</returns>
    /// <example><code>bool clean = FieldSpecTable.SelfCheck(rows).IsEmpty;</code></example>
    public static ImmutableArray<FieldSpecIssue> SelfCheck(IReadOnlyList<FieldSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        ImmutableArray<FieldSpecIssue>.Builder issues = ImmutableArray.CreateBuilder<FieldSpecIssue>();
        CheckTable(specs, string.Empty, issues);
        return issues.ToImmutable();
    }

    private static void CheckTable(IReadOnlyList<FieldSpec> specs, string prefix, ImmutableArray<FieldSpecIssue>.Builder issues)
    {
        for (int i = 0; i < specs.Count; i++)
        {
            FieldSpec spec = specs[i];
            string path = prefix + spec.Name;
            CheckRow(spec, path, issues);
            CheckDuplicate(specs, i, path, issues);
            CheckVariant(specs, spec, path, issues);
            if (!spec.Children.IsDefaultOrEmpty)
            {
                CheckTable(spec.Children, path + ".", issues);
            }
        }
    }

    private static void CheckRow(FieldSpec spec, string path, ImmutableArray<FieldSpecIssue>.Builder issues)
    {
        if (UsesNumbers(spec.Kind))
        {
            CheckNumber(spec, path, issues);
        }

        if ((spec.Kind is FieldKind.Enum or FieldKind.EnumList) && string.IsNullOrEmpty(spec.EnumSet))
        {
            issues.Add(new FieldSpecIssue(path, GeneralRuleIds.EnumSpelling, FieldSpecIssueKind.MissingEnumSet));
        }

        if ((spec.Kind is FieldKind.Reference or FieldKind.RefList or FieldKind.RefMap) && string.IsNullOrEmpty(spec.RefTarget))
        {
            issues.Add(new FieldSpecIssue(path, GeneralRuleIds.IdsAndReferences, FieldSpecIssueKind.MissingRefTarget));
        }

        CheckShape(spec, path, issues);
    }

    private static void CheckShape(FieldSpec spec, string path, ImmutableArray<FieldSpecIssue>.Builder issues)
    {
        if ((spec.Kind is FieldKind.Object or FieldKind.ObjectList) && spec.Children.IsDefaultOrEmpty)
        {
            issues.Add(new FieldSpecIssue(path, GeneralRuleIds.UnknownField, FieldSpecIssueKind.MissingChildren));
        }

        if (spec.Required && (spec.Default is not null || spec.DefaultSpelling is not null))
        {
            issues.Add(new FieldSpecIssue(path, GeneralRuleIds.RequiredFieldMissing, FieldSpecIssueKind.DefaultOnRequired));
        }

        bool isList = spec.Kind is FieldKind.IntList or FieldKind.TextList or FieldKind.EnumList or FieldKind.RefList or FieldKind.ObjectList;
        if (spec.FixedLength < 0 || (spec.FixedLength > 0 && !isList))
        {
            issues.Add(new FieldSpecIssue(path, GeneralRuleIds.ValueOutOfRange, FieldSpecIssueKind.FixedLengthMisuse));
        }
    }

    private static void CheckNumber(FieldSpec spec, string path, ImmutableArray<FieldSpecIssue>.Builder issues)
    {
        if (!FieldUnits.IsDeclared(spec.Unit))
        {
            issues.Add(new FieldSpecIssue(path, GeneralRuleIds.NumberFormat, FieldSpecIssueKind.MissingUnit));
            return;
        }

        if (spec.IsDuration && spec.Unit != FieldUnit.Seconds && spec.Unit != FieldUnit.Ms)
        {
            issues.Add(new FieldSpecIssue(path, GeneralRuleIds.NumberFormat, FieldSpecIssueKind.DurationUnit));
        }

        if (spec.Min > spec.Max)
        {
            issues.Add(new FieldSpecIssue(path, GeneralRuleIds.ValueOutOfRange, FieldSpecIssueKind.RangeInverted));
        }
        else if (spec.Default is long d && (d < spec.Min || d > spec.Max))
        {
            issues.Add(new FieldSpecIssue(path, GeneralRuleIds.ValueOutOfRange, FieldSpecIssueKind.DefaultOutOfRange));
        }
    }

    private static void CheckDuplicate(IReadOnlyList<FieldSpec> specs, int index, string path, ImmutableArray<FieldSpecIssue>.Builder issues)
    {
        for (int j = 0; j < index; j++)
        {
            if (string.Equals(specs[j].Name, specs[index].Name, StringComparison.Ordinal))
            {
                issues.Add(new FieldSpecIssue(path, GeneralRuleIds.DuplicateKey, FieldSpecIssueKind.DuplicateName));
                return;
            }
        }
    }

    private static void CheckVariant(IReadOnlyList<FieldSpec> specs, FieldSpec spec, string path, ImmutableArray<FieldSpecIssue>.Builder issues)
    {
        if (spec.Variant is null)
        {
            return;
        }

        bool discriminatorOk = false;
        for (int j = 0; j < specs.Count && !discriminatorOk; j++)
        {
            discriminatorOk = specs[j].Kind == FieldKind.Enum && string.Equals(specs[j].Name, spec.Variant.On, StringComparison.Ordinal);
        }

        if (!discriminatorOk || spec.Required)
        {
            issues.Add(new FieldSpecIssue(path, GeneralRuleIds.VariantField, FieldSpecIssueKind.VariantTarget));
        }
    }

    private static bool UsesNumbers(FieldKind kind) => kind is FieldKind.Number or FieldKind.IntList or FieldKind.RefMap;
}
