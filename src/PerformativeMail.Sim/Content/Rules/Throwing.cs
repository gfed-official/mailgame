using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Rules;

/// <summary>The ids of the Throwing (throwing-catching) rules, so rows, fixtures and tests name them without string literals.</summary>
/// <example>
/// <code>
/// bool isTap = d.RuleId == ThrowingRuleIds.CatchWholeTicks;
/// </code>
/// </example>
public static class ThrowingRuleIds
{
    /// <summary>CV-THR-01 (Error, stage 2): <c>0 &lt; heavy &lt; medium &lt; light</c> throw speeds, each within 3000 to 30000 mm/s.</summary>
    public const string SpeedOrdering = "CV-THR-01";

    /// <summary>CV-THR-02 (Error, stage 5): the F5 single-source worst case of <c>ThrowRangePermille</c> stays within the range clamp.</summary>
    public const string ThrowRangeSources = "CV-THR-02";

    /// <summary>CV-THR-03 (Error, stage 2): <c>g</c> above 0.</summary>
    public const string GravityPositive = "CV-THR-03";

    /// <summary>CV-THR-04 (Error, stage 2): <c>maxFlightTicks</c> within 30 to 150.</summary>
    public const string MaxFlightTicks = "CV-THR-04";

    /// <summary>CV-THR-05 (Error, stage 2): the receiver rank table is a permutation of the 8 kinds.</summary>
    public const string RankPermutation = "CV-THR-05";

    /// <summary>CV-THR-06 (Error, stage 4): every catch volume is axis-aligned, has extents above 0 and its bottom at or above the mailbox base.</summary>
    public const string CatchVolumes = "CV-THR-06";

    /// <summary>CV-THR-07 (deferred): every EnemyDef has radius at least 100 and height at least 500. Needs EnemyDef (Enemy epic).</summary>
    public const string EnemyDimensions = "CV-THR-07";

    /// <summary>CV-THR-08 (Error, stage 4): every ItemDef and MailKindDef has a weight class, and a bulk one is not hand-carried (so never on the hotbar).</summary>
    public const string WeightClasses = "CV-THR-08";

    /// <summary>CV-THR-09 (Error, stage 5): <c>fwdOffMm</c> at least <c>player.capsuleRadiusMm</c>.</summary>
    public const string FwdOffClearsCapsule = "CV-THR-09";

    /// <summary>CV-THR-10 (Error, stage 5): <c>0 &lt; handHeightMm</c> at most <c>player.capsuleHeightMm</c>.</summary>
    public const string HandHeightInCapsule = "CV-THR-10";

    /// <summary>CV-THR-11 (Error, stage 2): 70 degrees plus <c>loftDeg</c> at most 78 degrees.</summary>
    public const string LoftWithinHostBound = "CV-THR-11";

    /// <summary>CV-THR-12 (Error, stage 2): <c>catchTapMs</c> and <c>catchPredictMs</c> are whole ticks (F3).</summary>
    public const string CatchWholeTicks = "CV-THR-12";

    /// <summary>CV-THR-13 (Error, stage 2): <c>catchPredictMs</c> at most <c>maxFlightTicks x 1000 / 30</c>.</summary>
    public const string CatchPredictWithinFlight = "CV-THR-13";

    /// <summary>CV-THR-14 (retired): became CV-PLR-06.</summary>
    public const string Retired14 = "CV-THR-14";

    /// <summary>CV-THR-15 (Error, stage 2): <c>damagePerPoint</c> at least 1.</summary>
    public const string DamagePerPoint = "CV-THR-15";

    /// <summary>CV-THR-16 (Error, stage 2): <c>throwCooldownTicks</c> and <c>throwBurstTicks</c> at least 1.</summary>
    public const string CooldownAndBurst = "CV-THR-16";

    /// <summary>CV-THR-17 (deferred): map extents at most 1 000 000 mm. The extents are world-generation output, not a content input at M-1a.</summary>
    public const string MapExtents = "CV-THR-17";

    /// <summary>CV-THR-18 (Error, stage 2): <c>catchRadiusMm + catchHistoryMaxOffsetMm</c> at most 3700.</summary>
    public const string CatchReach = "CV-THR-18";
}

/// <summary>
/// The Throwing (throwing-catching) rule table (CV-THR, alias <c>THROW-CV-nn</c>), owner <c>throwing-catching</c>: CV-THR-01 to
/// 06, 08 to 13, 15, 16 and 18 are live (content-data story 012). CV-THR-07 (EnemyDef) and CV-THR-17 (map extents) are
/// <see cref="RuleStatus.Deferred"/> ("inputs not present at M-1a"), and CV-THR-14 stays listed as retired: its text lives on as
/// CV-PLR-06 (also spelled "Throwing rule 14"; story 011 registered it in <see cref="Player"/>). Never renumber or reuse an id.
/// <para>
/// Stage: the catalogue puts the balance-only rules at stage 2, but the runner executes only stages 4 and 5, so they are
/// registered at stage 4 (<see cref="ValidationStage.References"/>; TD-063 item 1, story 018 moves them). The cross-owner rules
/// CV-THR-09 and 10 (<c>player</c> capsule keys) and CV-THR-02 (F5) are stage 5 and declare every owner file they read, so a
/// failed file skips the rule and counts it. Stale names are not registered: <c>cooldownSlackTicks</c> became rule 16's
/// <c>throwCooldownTicks</c> / <c>throwBurstTicks</c>, and <c>throwSpreadMrad</c> is retired (Throwing OQ1).
/// </para>
/// </summary>
/// <example>
/// <code>
/// RuleRegistry.CreateDefault().Find("CV-THR-14")!.Status;   // RuleStatus.Retired
/// string label = RuleRegistry.CreateDefault().FormatId("CV-THR-12");   // "CV-THR-12 (THROW-CV-12)"
/// </code>
/// </example>
public static class Throwing
{
    private const string Owner = "throwing-catching";
    private const string NotAtM1a = "inputs not present at M-1a: ";

    /// <summary>The rows, in registration order. A property that builds the array on each call, so the class holds no static state (ADR-0003 section 4).</summary>
    public static ImmutableArray<RuleSpec> Rules => Table(new ThrowingChecks(default));

    /// <summary>
    /// The rows with a synthetic list of single <c>ThrowRangePermille</c> Add sources for CV-THR-02. At M-1a no perk adds range
    /// (Cannon Arm is M1), so a fixture supplies the source (for example +500, or +501 to violate) to prove the F5 single-source path.
    /// </summary>
    /// <param name="throwRangeAdds">Each Add unit (permille), checked on its own against the range clamp.</param>
    internal static ImmutableArray<RuleSpec> RulesWithSources(ImmutableArray<int> throwRangeAdds) => Table(new ThrowingChecks(throwRangeAdds));

    private static ImmutableArray<RuleSpec> Table(ThrowingChecks c) => ImmutableArray.Create(
        // Catalogue stage 2 rows run at stage 4 (the runner executes 4 and 5 only; TD-063 item 1).
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.SpeedOrdering, Owner, "THROW-CV-01", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckSpeeds,
            ImmutableArray.Create(new RuleClause("speed_order", HasLimit: true), new RuleClause("speed_range", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.ThrowRangeSources, Owner, "THROW-CV-02", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckThrowRangeSources,
            ImmutableArray.Create(new RuleClause("range_above_max", HasLimit: true), new RuleClause("range_below_min", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.GravityPositive, Owner, "THROW-CV-03", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckGravity,
            ImmutableArray.Create(new RuleClause("g_positive", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.MaxFlightTicks, Owner, "THROW-CV-04", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckMaxFlightTicks,
            ImmutableArray.Create(new RuleClause("max_flight_ticks_range", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.RankPermutation, Owner, "THROW-CV-05", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckRankPermutation,
            ImmutableArray.Create(new RuleClause("rank_permutation"))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.CatchVolumes, Owner, "THROW-CV-06", RuleSeverity.Error, ValidationStage.References,
            ImmutableArray.Create(ContentInputKeys.DestinationTypes),
            c.CheckCatchVolumes,
            ImmutableArray.Create(
                new RuleClause("volume_axis_aligned"),
                new RuleClause("volume_extents_positive", HasLimit: true),
                new RuleClause("volume_bottom_at_base", HasLimit: true))),
        RuleSpec.Defer(ThrowingRuleIds.EnemyDimensions, Owner, "THROW-CV-07", RuleSeverity.Error, ValidationStage.References,
            NotAtM1a + "EnemyDef (radius at least 100, height at least 500) arrives with the Enemy epic; there are no enemies at M-1a"),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.WeightClasses, Owner, "THROW-CV-08", RuleSeverity.Error, ValidationStage.References,
            // CDD-SCHEMA-3 (story 018, TD-069(2)): an ItemDef without weightClass fails stage 2, its type is not built, and the rule
            // is skipped and counted, so the item table is declared. Approved by Evan 2026-10-28 6:09 AM PT (TD-176): declaring `items` (not a
            // field-level key) also skips the rule's mail-kind half when the item table failed (TD-059(1) granularity).
            ImmutableArray.Create(ContentInputKeys.Items, ContentInputKeys.MailKinds),
            c.CheckWeightClasses,
            ImmutableArray.Create(new RuleClause("weight_class_present"), new RuleClause("bulk_not_hand_carried"))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.FwdOffClearsCapsule, Owner, "THROW-CV-09", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.ThrowBalance, ContentInputKeys.PlayerBalance),
            c.CheckFwdOffClearsCapsule,
            ImmutableArray.Create(new RuleClause("fwd_off_at_least_capsule_radius", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.HandHeightInCapsule, Owner, "THROW-CV-10", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.ThrowBalance, ContentInputKeys.PlayerBalance),
            c.CheckHandHeightInCapsule,
            ImmutableArray.Create(new RuleClause("hand_height_within_capsule", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.LoftWithinHostBound, Owner, "THROW-CV-11", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckLoft,
            ImmutableArray.Create(new RuleClause("loft_within_host_bound", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.CatchWholeTicks, Owner, "THROW-CV-12", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckCatchWholeTicks,
            ImmutableArray.Create(new RuleClause("catch_tap_whole_ticks", HasLimit: true), new RuleClause("catch_predict_whole_ticks", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.CatchPredictWithinFlight, Owner, "THROW-CV-13", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckCatchPredictWithinFlight,
            ImmutableArray.Create(new RuleClause("catch_predict_within_flight", HasLimit: true))),
        RuleSpec.Retire(ThrowingRuleIds.Retired14, Owner, "THROW-CV-14", "CV-PLR-06"),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.DamagePerPoint, Owner, "THROW-CV-15", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckDamagePerPoint,
            ImmutableArray.Create(new RuleClause("damage_per_point", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.CooldownAndBurst, Owner, "THROW-CV-16", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckCooldownAndBurst,
            ImmutableArray.Create(new RuleClause("throw_cooldown_ticks", HasLimit: true), new RuleClause("throw_burst_ticks", HasLimit: true))),
        RuleSpec.Defer(ThrowingRuleIds.MapExtents, Owner, "THROW-CV-17", RuleSeverity.Error, ValidationStage.WholeSet,
            NotAtM1a + "map extents are world-generation output (owner world-generation) under the ADR-0026 Bounds contract (|coord| at most 1 000 000 mm), not a content file"),
        RuleSpec.Create<ContentValidationInput>(
            ThrowingRuleIds.CatchReach, Owner, "THROW-CV-18", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.ThrowBalance),
            c.CheckCatchReach,
            ImmutableArray.Create(new RuleClause("catch_reach", HasLimit: true))));
}
