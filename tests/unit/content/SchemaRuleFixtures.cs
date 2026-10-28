using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Parsing;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Violation and valid-edge fixtures of the stage 2 schema rows of content-data story 018 (CV-GEN-02, 03, 05
/// <c>required_field</c>, 06, 07, 08, 12, 14). Each fixture walks a one-def file through stages 1 and 2 (<see cref="SchemaJson"/>)
/// and must produce exactly one diagnostic of the rule's id; a valid edge produces none. Appended to the fixture catalogue.
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;RuleFixture&gt; fixtures = SchemaRuleFixtures.All();
/// </code>
/// </example>
internal static class SchemaRuleFixtures
{
    /// <summary>An item file whose <c>tags</c> list has <paramref name="count"/> elements.</summary>
    public static string Tags(int count) => SchemaJson.File(SchemaJson.Plank(("tags", "[" + string.Join(",", Enumerable.Repeat("\"t\"", count)) + "]")));

    /// <summary>An item file whose <c>name</c> is <paramref name="bytes"/> ASCII bytes long.</summary>
    public static string Name(int bytes) => SchemaJson.File(SchemaJson.Plank(("name", "\"" + new string('a', bytes) + "\"")));

    private static ValidationResult Item(params (string Key, string? Value)[] edits) =>
        Of(SchemaJson.Walk(DefType.Item, SchemaJson.File(SchemaJson.Plank(edits))));

    private static ValidationResult Kind(params (string Key, string? Value)[] edits) =>
        Of(SchemaJson.Walk(DefType.MailKind, SchemaJson.File(SchemaJson.Kind(edits))));

    private static ValidationResult Mix(params (string Key, string? Value)[] edits) =>
        Of(SchemaJson.Walk(DefType.MailMix, SchemaJson.File(SchemaJson.Mix(edits))));

    private static ValidationResult Of(SchemaResult r) => ValidationResult.FromDiagnostics(r.Diagnostics);

    // A numeric row with no declared unit cannot exist in a shipped table (the loader's self-check rejects it), so no_unit walks an ad-hoc table.
    private static ValidationResult NoUnit() =>
        Of(SchemaJson.Walk(new SchemaTable(DefType.Item, "Fixture", ImmutableArray.Create(FieldSpec.Id(), FieldSpec.Number("n", default))), "[{\"id\": \"a\", \"n\": 1}]"));

    private static RuleFixture Violation(string rule, string clause, Func<ValidationResult> produce) =>
        new(rule, clause, FixtureKind.Violation, produce, ImmutableArray<string>.Empty);

    private static RuleFixture Edge(string rule, string clause, Func<ValidationResult> produce) =>
        new(rule, clause, FixtureKind.ValidEdge, produce, ImmutableArray<string>.Empty);

    /// <summary>All the story 018 schema fixtures.</summary>
    public static ImmutableArray<RuleFixture> All() => ImmutableArray.Create(
        Violation(GeneralRuleIds.IdFormat, "id_regex", () => Item(("id", "\"Letter\""))),
        Violation(GeneralRuleIds.IdFormat, "id_escape", () => Item(("id", "\"\\u0061b\""))),
        Violation(GeneralRuleIds.UnknownField, "unknown_field", () => Item(("maxStak", "20"))),
        Violation(GeneralRuleIds.RequiredFieldMissing, "required_field", () => Item(("weightClass", null))),
        Violation(GeneralRuleIds.NumberFormat, "integer_dot", () => Item(("maxStack", "1.0"))),
        Violation(GeneralRuleIds.NumberFormat, "fraction_digits", () => Kind(("beltLengthMetres", "0.1234"))),
        Violation(GeneralRuleIds.NumberFormat, "exponent", () => Kind(("beltLengthMetres", "1e3"))),
        Violation(GeneralRuleIds.NumberFormat, "negative_zero", () => Kind(("beltLengthMetres", "-0.00"))),
        Violation(GeneralRuleIds.NumberFormat, "no_unit", NoUnit),
        Violation(GeneralRuleIds.DurationOverCap, "over_cap", () => Mix(("batchIntervalSeconds", "86400.001"))),
        Edge(GeneralRuleIds.DurationOverCap, "over_cap", () => Mix(("batchIntervalSeconds", "86400"))),
        Violation(GeneralRuleIds.EnumSpelling, "unknown_spelling", () => Item(("weightClass", "\"Light\""))),
        Violation(GeneralRuleIds.ValueOutOfRange, "storage_width", () => Item(("maxStack", "65536"))),
        Edge(GeneralRuleIds.ValueOutOfRange, "storage_width", () => Item(("maxStack", "65535"))),
        Violation(GeneralRuleIds.ValueOutOfRange, "list_too_long", () => Of(SchemaJson.Walk(DefType.Item, Tags(SchemaWalker.MaxListLength + 1)))),
        Edge(GeneralRuleIds.ValueOutOfRange, "list_too_long", () => Of(SchemaJson.Walk(DefType.Item, Tags(SchemaWalker.MaxListLength)))),
        Violation(GeneralRuleIds.ValueOutOfRange, "string_too_long", () => Of(SchemaJson.Walk(DefType.Item, Name(SchemaWalker.MaxStringBytes + 1)))),
        Edge(GeneralRuleIds.ValueOutOfRange, "string_too_long", () => Of(SchemaJson.Walk(DefType.Item, Name(SchemaWalker.MaxStringBytes)))),
        Violation(GeneralRuleIds.VariantField, "forbidden_for_variant",
            () => Of(SchemaJson.Walk(DefType.Container, SchemaJson.File(SchemaJson.Cursor(("layout", "\"pile\"")))))));
}
