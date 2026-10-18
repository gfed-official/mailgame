using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// PHASE A PLACEHOLDER fixtures for the stage 2 schema rows registered by content-data story 018 (CV-GEN-02, 03, 05
/// <c>required_field</c>, 06, 07, 08, 12, 14). The rows are emitted by the schema walker, which is phase B; until it exists
/// a "violation" fixture can only hand-build the one diagnostic the walker will raise, and a "valid edge" is an empty result.
/// Phase B REPLACES every fixture here with one that walks a real file through the schema stage, and deletes the hand-built
/// diagnostics. (The mechanism the phase A brief named, <c>RuleFixtureCatalogue.ForwardReferences</c>, only exempts an
/// <c>alsoTrips</c> id from the registered-rule check; it does not waive a missing clause fixture, so placeholders are needed.)
/// </summary>
/// <example>
/// <code>
/// ImmutableArray&lt;RuleFixture&gt; fixtures = SchemaRuleFixtures.All();
/// </code>
/// </example>
internal static class SchemaRuleFixtures
{
    private static ValidationResult One(string rule) =>
        ValidationResult.FromDiagnostics(ImmutableArray.Create(new ContentDiagnostic(
            rule, RuleSeverity.Error, new SourceLocation("defs/placeholder.jsonc", 1, 1), DefRef.None, "placeholder", "phase B replaces this fixture")));

    private static RuleFixture Violation(string rule, string clause) =>
        new(rule, clause, FixtureKind.Violation, () => One(rule), ImmutableArray<string>.Empty);

    private static RuleFixture Edge(string rule, string clause) =>
        new(rule, clause, FixtureKind.ValidEdge, () => ValidationResult.FromDiagnostics(ImmutableArray<ContentDiagnostic>.Empty), ImmutableArray<string>.Empty);

    /// <summary>The placeholder fixtures; phase B replaces them.</summary>
    public static ImmutableArray<RuleFixture> All() => ImmutableArray.Create(
        Violation(GeneralRuleIds.IdFormat, "id_regex"),
        Violation(GeneralRuleIds.IdFormat, "id_escape"),
        Violation(GeneralRuleIds.UnknownField, "unknown_field"),
        Violation(GeneralRuleIds.RequiredFieldMissing, "required_field"),
        Violation(GeneralRuleIds.NumberFormat, "integer_dot"),
        Violation(GeneralRuleIds.NumberFormat, "fraction_digits"),
        Violation(GeneralRuleIds.NumberFormat, "exponent"),
        Violation(GeneralRuleIds.NumberFormat, "negative_zero"),
        Violation(GeneralRuleIds.NumberFormat, "no_unit"),
        Violation(GeneralRuleIds.DurationOverCap, "over_cap"),
        Edge(GeneralRuleIds.DurationOverCap, "over_cap"),
        Violation(GeneralRuleIds.EnumSpelling, "unknown_spelling"),
        Violation(GeneralRuleIds.ValueOutOfRange, "storage_width"),
        Edge(GeneralRuleIds.ValueOutOfRange, "storage_width"),
        Violation(GeneralRuleIds.ValueOutOfRange, "list_too_long"),
        Edge(GeneralRuleIds.ValueOutOfRange, "list_too_long"),
        Violation(GeneralRuleIds.ValueOutOfRange, "string_too_long"),
        Edge(GeneralRuleIds.ValueOutOfRange, "string_too_long"),
        Violation(GeneralRuleIds.VariantField, "forbidden_for_variant"));
}
