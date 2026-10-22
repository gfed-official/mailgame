using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Violation and valid-edge fixtures of the Grid and Mail rules CV-GRD-01 to 04, 06, 07 and CV-MAIL-01 to 06 (content-data
/// story 010). Each violation is <see cref="ContentFixtures.MinimalValid"/> plus one overlay that must produce exactly one
/// diagnostic of the rule's id; other ids a fixture trips are listed in its reviewed <c>alsoTrips</c>. Appended to
/// <see cref="RuleFixtureCatalogue.Real"/>. The fixture-only kinds <c>pallet</c> and <c>parcel</c> stand in for the deferred
/// cargo kind (they sort after <c>letter</c>, so the minimal set's indices stay valid); <c>cargo</c> is not in <c>BuiltInContent</c>.
/// </summary>
/// <example>
/// <code>
/// ValidationResult r = GridMailRuleFixtures.WithSyntheticStackSource(ContentFixtures.MinimalValid()
///     .Replace(ContentFixtures.Letter with { MaxStack = 128 }));   // one CV-GRD-01: 128 x 2.0 = 256
/// </code>
/// </example>
internal static class GridMailRuleFixtures
{
    /// <summary>A synthetic x2.0 <c>MailMaxStack</c> source (permille), standing in for Bulk Mailer, which M-1a does not have.</summary>
    public const int DoubleStackPm = 2_000;

    /// <summary>A kind no destination accepts (index 1 after <c>letter</c>); fixture-only, stands in for cargo.</summary>
    public static MailKindDef Pallet => ContentFixtures.Letter with
    {
        Id = "pallet", Name = "mail.pallet", MaxStack = 1, WeightClass = WeightClass.Medium,
        AcceptedBy = ImmutableArray<DefIndex>.Empty,
    };

    /// <summary>A second kind no destination accepts (index 2); fixture-only.</summary>
    public static MailKindDef Parcel => Pallet with { Id = "parcel", Name = "mail.parcel" };

    /// <summary>The production rule tables with CV-GRD-01 reading <see cref="DoubleStackPm"/> as a live <c>MailMaxStack</c> Mul.</summary>
    public static RuleRegistry RegistryWithStackSource() => RuleRegistry.Compose(
        General.Rules, Grid.RulesWithSources(ImmutableArray.Create(DoubleStackPm)), Mail.Rules, Player.Rules, Throwing.Rules);

    /// <summary>Validates <paramref name="draft"/> with the synthetic x2.0 stack source live.</summary>
    public static ValidationResult WithSyntheticStackSource(ContentDraft draft) => draft.Build(RegistryWithStackSource()).Validation;

    /// <summary>The minimal set plus the kinds in <paramref name="extra"/>, with the mix shares given as (kind ordinal, permille).</summary>
    public static ContentDraft WithKinds(MailKindDef[] extra, params (int Kind, int Pm)[] shares)
    {
        ContentDraft draft = ContentFixtures.MinimalValid();
        foreach (MailKindDef kind in extra)
        {
            draft = draft.Add(kind);
        }

        return draft.Replace(ContentFixtures.Mix with
        {
            Shifts = ImmutableArray.Create(new ShiftMix(1, shares
                .Select(s => new MailShare(DefIndex.FromValue((ushort)s.Kind), s.Pm)).ToImmutableArray())),
        });
    }

    /// <summary>The minimal set with one shift whose shares are given as (letter, permille) entries.</summary>
    public static ContentDraft WithShares(params int[] letterShares) =>
        WithKinds(Array.Empty<MailKindDef>(), letterShares.Select(p => (0, p)).ToArray());

    /// <summary>
    /// Builds the set with <c>mail.deadLetterShifts</c> = <paramref name="value"/> against a registry whose hard minimum for
    /// that key is widened to 0. The shipped hard range is 1 to 16, so CV-GEN-13 would refuse 0 before CV-MAIL-03 could see
    /// it; the widened registry proves the rule on its own (it is the defence if the hard range is ever relaxed).
    /// </summary>
    public static ValidationResult DeadLetterShifts(long value)
    {
        ImmutableArray<BalanceRow> rows = BalanceKeys.CreateRegistry().Rows
            .Select(r => r.Name == "mail.deadLetterShifts" ? r with { HardMin = 0 } : r).ToImmutableArray();
        ImmutableArray<BalanceEntry> entries = ContentFixtures.MinimalValid().WithBalance("mail.deadLetterShifts", value).Balance;
        BalanceTable table = BalanceTable.Build(new BalanceRegistry(rows), entries).Table
            ?? throw new InvalidOperationException("The widened balance did not validate.");
        ContentDraft d = ContentFixtures.MinimalValid();
        return ContentPipeline.Build(table, d.Containers, d.Items, d.MailKinds, d.DestinationTypes, d.MailMixes, RuleRegistry.CreateDefault()).Validation;
    }

    public static ValidationResult V(ContentDraft draft) => draft.Build().Validation;

    private static RuleFixture Violation(string rule, string clause, Func<ValidationResult> produce, params string[] alsoTrips) =>
        new(rule, clause, FixtureKind.Violation, produce, ImmutableArray.Create(alsoTrips));

    private static RuleFixture Edge(string rule, string clause, Func<ValidationResult> produce) =>
        new(rule, clause, FixtureKind.ValidEdge, produce, ImmutableArray<string>.Empty);

    public static ContentDraft Kind(int maxStack) => ContentFixtures.MinimalValid().Replace(ContentFixtures.Letter with { MaxStack = maxStack });

    public static ContentDraft Cursor(ContainerDef cursor) => ContentFixtures.MinimalValid().Replace(cursor);

    private static ContentDraft BulkItemWith(ContainerDef cursor) =>
        Cursor(cursor).Add(ContentFixtures.Plank with { Id = "anvil", Name = "item.anvil", Category = ItemCategory.Tool, WeightClass = WeightClass.Bulk, CarryByHand = false });   // not hand-carried, so CV-THR-08 stays quiet

    private static ContentDraft Inventory(string key, long value) => ContentFixtures.MinimalValid().WithBalance("inventory." + key, value);

    private static ContentDraft Timing(int intervalMs, int jitterMs, int tailMs) => ContentFixtures.MinimalValid().Replace(
        ContentFixtures.Mix with { BatchIntervalMs = intervalMs, BatchJitterMs = jitterMs, TailGuardMs = tailMs });

    private static MailKindDef[] Pallets => new[] { Pallet };

    private static MailKindDef[] PalletsWithFallback(int pallet, int parcel) => new[]
    {
        Pallet with { FallbackKind = DefIndex.FromValue((ushort)pallet) }, Parcel with { FallbackKind = DefIndex.FromValue((ushort)parcel) },
    };

    /// <summary>All the story 010 fixtures.</summary>
    public static ImmutableArray<RuleFixture> All() => GridFixtures().AddRange(MailFixtures());

    private static ImmutableArray<RuleFixture> GridFixtures() => ImmutableArray.Create(
        // CV-GRD-01 (edges: 127 x 2.0 = 254 passes, 128 -> 256 errors; no source: 255 passes, 256 errors)
        Violation(GridRuleIds.MaxStackWorstCase, "max_stack_worst_case", () => WithSyntheticStackSource(Kind(128))),
        Edge(GridRuleIds.MaxStackWorstCase, "max_stack_worst_case", () => WithSyntheticStackSource(Kind(127))),
        // (the no-source edges 255 and 256 are asserted in GridMailRulesTests: the meta-test allows one fixture per kind and clause)
        // CV-GRD-02 (a bulk tool; the edge narrows the hand-carried cursor to mail, which does not admit it)
        Violation(GridRuleIds.HandCarriedBulk, "hand_carried_admits_bulk", () => V(BulkItemWith(ContentFixtures.Cursor))),
        Edge(GridRuleIds.HandCarriedBulk, "hand_carried_admits_bulk", () => V(BulkItemWith(ContentFixtures.Cursor with
        {
            HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Mail),
        }))),
        // CV-GRD-03 (edge: one category)
        Violation(GridRuleIds.EmptyAllowedCategories, "empty_allowed_categories", () => V(Cursor(ContentFixtures.Cursor with
        {
            HasAllowedCategories = true, AllowedCategories = ImmutableArray<ItemCategory>.Empty,
        }))),
        Edge(GridRuleIds.EmptyAllowedCategories, "empty_allowed_categories", () => V(Cursor(ContentFixtures.Cursor with
        {
            HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Mail),
        }))),
        // CV-GRD-04 (edges: 1 x 1 and maxStack 1; a pile has no grid)
        Violation(GridRuleIds.Dimensions, "grid_dimensions", () => V(Cursor(ContentFixtures.Cursor with { GridW = 0 }))),
        Edge(GridRuleIds.Dimensions, "grid_dimensions", () => V(Cursor(ContentFixtures.Cursor with
        {
            Layout = ContainerLayout.Pile, HasGrid = false, GridW = 0, GridH = 0,
        }))),
        Violation(GridRuleIds.Dimensions, "max_stack", () => V(ContentFixtures.MinimalValid().Replace(ContentFixtures.Plank with { MaxStack = 0 }))),
        Edge(GridRuleIds.Dimensions, "max_stack", () => V(ContentFixtures.MinimalValid().Replace(ContentFixtures.Plank with { MaxStack = 1 }))),
        // CV-GRD-06 (Warning; the keys hold ms, compared in ticks; despawn 300 000 ms = 9000 ticks: 299 967 ms = 8999 ticks leaves
        // no eviction window (8999 + 1 >= 9000), the edge 299 966 ms = 8998 ticks is the largest that leaves one)
        Violation(GridRuleIds.EvictAgeAtOrAboveDespawn, "evict_age_below_despawn", () => V(Inventory("capEvictMinAgeSeconds", 299_967))),
        Edge(GridRuleIds.EvictAgeAtOrAboveDespawn, "evict_age_below_despawn", () => V(Inventory("capEvictMinAgeSeconds", 299_966))),
        // CV-GRD-07 (Warning; edge: exactly 30 s passes, 29 s warns)
        Violation(GridRuleIds.DespawnTooShort, "despawn_at_least_30_seconds", () => V(Inventory("worldItemDespawnSeconds", 29_000))),
        Edge(GridRuleIds.DespawnTooShort, "despawn_at_least_30_seconds", () => V(Inventory("worldItemDespawnSeconds", 30_000))));

    private static ImmutableArray<RuleFixture> MailFixtures() => ImmutableArray.Create(
        // CV-MAIL-01: an unknown destination index is also CV-GEN-01; an unmirrored list leaves the kind without an acceptor (CV-MAIL-04)
        Violation(MailRuleIds.Acceptance, "accepted_by_subset", () => V(ContentFixtures.MinimalValid().Replace(ContentFixtures.Letter with
        {
            AcceptedBy = ImmutableArray.Create(DefIndex.FromValue(0), DefIndex.FromValue(5)),
        })), GeneralRuleIds.IdsAndReferences),
        Violation(MailRuleIds.Acceptance, "accepted_by_mirror", () => V(ContentFixtures.MinimalValid().Replace(ContentFixtures.House with
        {
            Accepts = ImmutableArray<DefIndex>.Empty,
        })), MailRuleIds.Acceptors),
        Violation(MailRuleIds.Acceptance, "accepts_mirror", () => V(ContentFixtures.MinimalValid().Replace(ContentFixtures.Letter with
        {
            AcceptedBy = ImmutableArray<DefIndex>.Empty,
        }))),
        // CV-MAIL-02 (edges: sum 1 000 passes, 999 errors; a share of 600 or 0 passes, 601 errors)
        Violation(MailRuleIds.Shares, "share_sum", () => V(WithShares(600, 399))),
        Edge(MailRuleIds.Shares, "share_sum", () => V(WithShares(600, 400))),
        Violation(MailRuleIds.Shares, "share_range", () => V(WithShares(601, 399))),
        Edge(MailRuleIds.Shares, "share_range", () => V(WithShares(600, 400, 0))),
        // CV-MAIL-03 (a widened hard range lets 0 reach the rule; edge: 1 on the shipped registry)
        Violation(MailRuleIds.DeadLetterShifts, "dead_letter_shifts", () => DeadLetterShifts(0)),
        Edge(MailRuleIds.DeadLetterShifts, "dead_letter_shifts", () => V(ContentFixtures.MinimalValid().WithBalance("mail.deadLetterShifts", 1))),
        // CV-MAIL-04 (edge: a kind with no acceptor passes while its share is 0)
        Violation(MailRuleIds.Acceptors, "no_acceptor", () => V(WithKinds(Pallets, (0, 600), (1, 400)))),
        Edge(MailRuleIds.Acceptors, "no_acceptor", () => V(WithKinds(Pallets, (0, 600), (0, 400), (1, 0)))),
        Violation(MailRuleIds.Acceptors, "fallback_unresolved", () => V(WithKinds(
            new[] { Pallet with { FallbackKind = DefIndex.FromValue(2) }, Parcel }, (0, 600), (1, 400)))),
        Edge(MailRuleIds.Acceptors, "fallback_unresolved", () => V(WithKinds(
            new[] { Pallet with { FallbackKind = DefIndex.FromValue(0) } }, (0, 600), (1, 400)))),
        Violation(MailRuleIds.Acceptors, "fallback_cycle", () => V(WithKinds(PalletsWithFallback(2, 1), (0, 600), (1, 400)))),
        // CV-MAIL-05 (interval 15 s: jitter 7 500 ms is not below half; 7 499 ms is; tail 5 000 ms is inside both)
        Violation(MailRuleIds.Jitter, "jitter_below_half_interval", () => V(Timing(15_000, 7_500, 5_000))),
        Edge(MailRuleIds.Jitter, "jitter_below_half_interval", () => V(Timing(15_000, 7_499, 5_000))),
        // CV-MAIL-06 (interval 15 s, jitter 3 s: the tail guard may be 12 000 ms)
        Violation(MailRuleIds.TailGuard, "tail_guard_within_interval", () => V(Timing(15_000, 3_000, 12_001))),
        Edge(MailRuleIds.TailGuard, "tail_guard_within_interval", () => V(Timing(15_000, 3_000, 12_000))));
}
