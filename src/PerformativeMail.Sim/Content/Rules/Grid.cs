using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Rules;

/// <summary>The ids of the Grid (grid-inventory) rules, so rows, fixtures and tests name them without string literals.</summary>
/// <example>
/// <code>
/// bool isMaxStack = d.RuleId == GridRuleIds.MaxStackWorstCase;
/// </code>
/// </example>
public static class GridRuleIds
{
    /// <summary>CV-GRD-01 (Error, stage 5): <c>maxStack</c> times the worst-case <c>MailMaxStack</c> Mul (F5) is at most 255.</summary>
    public const string MaxStackWorstCase = "CV-GRD-01";

    /// <summary>CV-GRD-02 (Error, stage 5): a <c>handCarried</c> container could admit a <c>bulk</c>-class item.</summary>
    public const string HandCarriedBulk = "CV-GRD-02";

    /// <summary>CV-GRD-03 (Error, stage 2): <c>allowedCategories</c> present but empty.</summary>
    /// <remarks>Catalogue stage 2; runs at stage 4 through the runner (TD-063(1)).</remarks>
    public const string EmptyAllowedCategories = "CV-GRD-03";

    /// <summary>CV-GRD-04 (Error, stage 2): a grid or slots container with W or H below 1, or any <c>maxStack</c> below 1.</summary>
    /// <remarks>Catalogue stage 2; runs at stage 4 through the runner (TD-063(1)).</remarks>
    public const string Dimensions = "CV-GRD-04";

    /// <summary>CV-GRD-06 (Warning, stage 5): <c>capEvictMinAgeSeconds</c> not at least 2 ticks below <c>worldItemDespawnSeconds</c> (no eviction window).</summary>
    public const string EvictAgeAtOrAboveDespawn = "CV-GRD-06";

    /// <summary>CV-GRD-07 (Warning, stage 2): <c>worldItemDespawnSeconds</c> below 30 s.</summary>
    /// <remarks>Catalogue stage 2; runs at stage 4 through the runner (TD-063(1)).</remarks>
    public const string DespawnTooShort = "CV-GRD-07";
}

/// <summary>
/// The Grid (grid-inventory) rule table (CV-GRD), owner <c>grid-inventory</c>: CV-GRD-01 to 04, 06 and 07 (content-data
/// story 010). CV-GRD-05 (<c>deathBagDespawnSeconds</c> against <c>player.respawnMs</c>) is M-1b with the death bag.
/// Rows are explicit entries in this list; there is no reflection discovery. Never renumber or reuse an id.
/// <para>
/// Stage: the catalogue puts CV-GRD-03, 04 and 07 at stage 2 (schema), but the stage 2 rule bodies of M-1a read the built
/// set and the runner executes only stages 4 and 5, so they are registered at stage 4 (<see cref="ValidationStage.References"/>),
/// ahead of every stage 5 rule; story 018 moves them if its schema walker takes them over. Aliases are unique per row
/// (the registry rejects shared ones), so the two R12 rows carry a key suffix.
/// </para>
/// </summary>
/// <example>
/// <code>
/// RuleSpec grd07 = RuleRegistry.CreateDefault().Find("CV-GRD-07")!;   // Warning, "Grid (R12) worldItemDespawn"
/// </code>
/// </example>
public static class Grid
{
    private const string Owner = "grid-inventory";

    /// <summary>The rows, in registration order. A property that builds the array on each call, so the class holds no static state (ADR-0003 section 4).</summary>
    public static ImmutableArray<RuleSpec> Rules => Table(new GridChecks(default));

    /// <summary>
    /// The rows with a synthetic list of live <c>MailMaxStack</c> Mul sources (permille) for CV-GRD-01. At M-1a no perk can
    /// raise the stack cap, so a fixture supplies the source (for example 2000 for a x2.0 Bulk Mailer) to prove the F5 path.
    /// </summary>
    /// <param name="mailMaxStackMuls">The Mul units live together.</param>
    internal static ImmutableArray<RuleSpec> RulesWithSources(ImmutableArray<int> mailMaxStackMuls) => Table(new GridChecks(mailMaxStackMuls));

    private static ImmutableArray<RuleSpec> Table(GridChecks checks) => ImmutableArray.Create(
        // F5: the Mul list is every unit that can be live together; at M-1a it is empty, so the worst case is the base.
        RuleSpec.Create<ContentValidationInput>(
            GridRuleIds.MaxStackWorstCase, Owner, "Grid (R3)", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.MailKinds),
            checks.CheckMaxStackWorstCase,
            ImmutableArray.Create(new RuleClause("max_stack_worst_case", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            GridRuleIds.HandCarriedBulk, Owner, "Grid (R5/D1)", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.Containers, ContentInputKeys.MailKinds),   // the item table is read but not declared: a failed item table must not skip the rule (TD-059)
            checks.CheckHandCarriedBulk,
            ImmutableArray.Create(new RuleClause("hand_carried_admits_bulk"))),
        RuleSpec.Create<ContentValidationInput>(
            GridRuleIds.EmptyAllowedCategories, Owner, "Grid (Dependencies)", RuleSeverity.Error, ValidationStage.References,
            ImmutableArray.Create(ContentInputKeys.Containers),
            checks.CheckAllowedCategories,
            ImmutableArray.Create(new RuleClause("empty_allowed_categories", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            GridRuleIds.Dimensions, Owner, "Grid (edge cases)", RuleSeverity.Error, ValidationStage.References,
            ImmutableArray.Create(ContentInputKeys.Containers, ContentInputKeys.MailKinds),   // the item table is read but not declared: a failed item table must not skip the rule (TD-059)
            checks.CheckDimensions,
            ImmutableArray.Create(new RuleClause("grid_dimensions", HasLimit: true), new RuleClause("max_stack", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            GridRuleIds.EvictAgeAtOrAboveDespawn, Owner, "Grid (R12) capEvictMinAge", RuleSeverity.Warning, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.InventoryBalance),
            checks.CheckEvictAgeBelowDespawn,
            ImmutableArray.Create(new RuleClause("evict_age_below_despawn", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            GridRuleIds.DespawnTooShort, Owner, "Grid (R12) worldItemDespawn", RuleSeverity.Warning, ValidationStage.References,
            ImmutableArray.Create(ContentInputKeys.InventoryBalance),
            checks.CheckDespawnAtLeast30Seconds,
            ImmutableArray.Create(new RuleClause("despawn_at_least_30_seconds", HasLimit: true))));
}
