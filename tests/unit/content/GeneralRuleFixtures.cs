using System.Collections.Immutable;
using System.Globalization;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Violation and valid-edge fixtures of the General set rules CV-GEN-01, 09, 17 and 18 (content-data story 009). Each
/// violation is <see cref="ContentFixtures.MinimalValid"/> plus one overlay that must produce exactly one diagnostic of the
/// rule's id; other ids a fixture trips are listed in its reviewed <c>alsoTrips</c>. Appended to
/// <see cref="RuleFixtureCatalogue.Real"/>.
/// </summary>
/// <example>
/// <code>
/// ValidationResult r = GeneralRuleFixtures.TooManyContainers(65_535).Build().Validation;   // one CV-GEN-09
/// </code>
/// </example>
internal static class GeneralRuleFixtures
{
    /// <summary>The pending by-id reference of <see cref="ContentFixtures.House"/> to <paramref name="id"/> as a mail kind.</summary>
    public static ImmutableArray<PendingReference> HouseAcceptsPending(string id) => ImmutableArray.Create(new PendingReference(
        new DefRef(DefType.DestinationType, "house"), ContentValidationInput.LocationOf("destination_types", "house"),
        "accepts[0]", DefType.MailKind, id));

    /// <summary>The minimal set with <paramref name="count"/> containers (generated ids <c>c00000</c> and up) instead of the cursor.</summary>
    public static ContentDraft TooManyContainers(int count)
    {
        var list = ImmutableArray.CreateBuilder<ContainerDef>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ContentFixtures.Cursor with { Id = "c" + i.ToString("D5", CultureInfo.InvariantCulture) });
        }

        return ContentFixtures.MinimalValid() with { Containers = list.MoveToImmutable() };
    }

    private static RuleFixture Violation(string rule, string clause, Func<ValidationResult> produce, params string[] alsoTrips) =>
        new(rule, clause, FixtureKind.Violation, produce, ImmutableArray.Create(alsoTrips));

    private static RuleFixture Edge(string rule, string clause, Func<ValidationResult> produce) =>
        new(rule, clause, FixtureKind.ValidEdge, produce, ImmutableArray<string>.Empty);

    /// <summary>All the story 009 fixtures.</summary>
    public static ImmutableArray<RuleFixture> All() => ImmutableArray.Create(
        // CV-GEN-01
        Violation(GeneralRuleIds.IdsAndReferences, "duplicate_id",
            () => ContentFixtures.MinimalValid().Add(ContentFixtures.Plank).Build().Validation),
        Violation(GeneralRuleIds.IdsAndReferences, "unresolved_reference",
            () => ContentFixtures.MinimalValid().Build(references: HouseAcceptsPending("nope")).Validation),
        Violation(GeneralRuleIds.IdsAndReferences, "wrong_type_reference",
            () => ContentFixtures.MinimalValid().Build(references: HouseAcceptsPending("plank")).Validation),
        Violation(GeneralRuleIds.IdsAndReferences, "unresolved_index",
            () => ContentFixtures.MinimalValid()
                .Replace(ContentFixtures.House with { Accepts = ImmutableArray.Create(DefIndex.FromValue(5)) }).Build().Validation,
            // the letter lists a house that no longer accepts it: Mail's mirror and acceptor rules (story 010) follow
            MailRuleIds.Acceptance, MailRuleIds.Acceptors),
        // CV-GEN-09 (edge: exactly 65 534 defs)
        Violation(GeneralRuleIds.TooManyDefs, "too_many_defs", () => TooManyContainers(65_535).Build().Validation),
        Edge(GeneralRuleIds.TooManyDefs, "too_many_defs", () => TooManyContainers(65_534).Build().Validation),
        // CV-GEN-17 (edges: exactly at the bound)
        Violation(GeneralRuleIds.StatBaseOutsideBounds, "container_rows",
            () => ContentFixtures.MinimalValid().Replace(ContentFixtures.Cursor with { GridH = 17 }).Build().Validation),
        Edge(GeneralRuleIds.StatBaseOutsideBounds, "container_rows",
            () => ContentFixtures.MinimalValid().Replace(ContentFixtures.Cursor with { GridH = 16 }).Build().Validation),
        Violation(GeneralRuleIds.StatBaseOutsideBounds, "mail_kind_max_stack",
            () => ContentFixtures.MinimalValid().Replace(ContentFixtures.Letter with { MaxStack = 256 }).Build().Validation,
            GridRuleIds.MaxStackWorstCase),   // 256 x no source also exceeds 255 (story 010)
        Edge(GeneralRuleIds.StatBaseOutsideBounds, "mail_kind_max_stack",
            () => ContentFixtures.MinimalValid().Replace(ContentFixtures.Letter with { MaxStack = 255 }).Build().Validation),
        Violation(GeneralRuleIds.StatBaseOutsideBounds, "stat_base",
            () => ContentFixtures.MinimalValid().WithBalance("throw.rangeMinPermille", 1_001).Build().Validation),
        Edge(GeneralRuleIds.StatBaseOutsideBounds, "stat_base",
            () => ContentFixtures.MinimalValid().WithBalance("throw.rangeMinPermille", 1_000).Build().Validation),
        // CV-GEN-18 (edge: an empty ItemDef table is valid)
        Violation(GeneralRuleIds.EmptyRequiredTable, "empty_table",
            () => ContentFixtures.MinimalValid().WithoutContainers().Build().Validation),
        Edge(GeneralRuleIds.EmptyRequiredTable, "empty_table",
            () => ContentFixtures.MinimalValid().WithoutItems().Build().Validation));
}
