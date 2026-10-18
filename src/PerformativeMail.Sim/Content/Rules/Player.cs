using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Rules;

/// <summary>The ids of the Player Character (player-character-movement) rules, so rows, fixtures and tests name them without string literals.</summary>
/// <example>
/// <code>
/// bool isLeash = d.RuleId == PlayerRuleIds.RangeWithinLeash;
/// </code>
/// </example>
public static class PlayerRuleIds
{
    /// <summary>CV-PLR-01 (Error, stage 5): <c>rangeMm + rangeSlackMm</c> at most the external container leash.</summary>
    public const string RangeWithinLeash = "CV-PLR-01";

    /// <summary>CV-PLR-02 (Error, stage 2): <c>swimExitMm</c> at most <c>swimEnterMm - 100</c>.</summary>
    public const string SwimHysteresis = "CV-PLR-02";

    /// <summary>CV-PLR-03 (deferred): <c>buffCapPm</c> against <c>postalPm</c>, 1 000 and the animation clamp. Needs Postal Sprint (M1).</summary>
    public const string BuffCap = "CV-PLR-03";

    /// <summary>CV-PLR-04 (Error, stage 2): <c>minHoldTicks</c> at least 6.</summary>
    public const string MinHold = "CV-PLR-04";

    /// <summary>CV-PLR-05 (deferred): a min-wins Set on <c>RespawnMs</c> exceeds base. Needs PerkDef (M1).</summary>
    public const string RespawnSet = "CV-PLR-05";

    /// <summary>CV-PLR-06 (Error, stage 5): the catch range is below <c>rangeMm</c>. Also spelled <c>Throwing rule 14</c> (was CV-THR-14).</summary>
    public const string CatchBelowRange = "CV-PLR-06";

    /// <summary>CV-PLR-07 (deferred): <c>2 x pushCapMm</c> at most <c>capsuleRadiusMm</c>. M-1b.</summary>
    public const string PushCap = "CV-PLR-07";

    /// <summary>CV-PLR-08 (Error, stage 2): <c>haulOutMm</c> at least <c>stepUpMm</c>.</summary>
    public const string HaulOut = "CV-PLR-08";

    /// <summary>CV-PLR-09 (Error, stage 2): <c>incumbentSlackMm + 100</c> at most <c>rangeSlackMm</c>.</summary>
    public const string IncumbentSlack = "CV-PLR-09";

    /// <summary>CV-PLR-10 (Error, stage 2): <c>ticksAccel</c>, <c>ticksDecel</c>, <c>airControlPm</c>, <c>rangeMm</c> above 0 and <c>g</c> a multiple of 30.</summary>
    public const string DivisionSafety = "CV-PLR-10";

    /// <summary>CV-PLR-11 (Error, stage 2): <c>positionStaleTicks</c> at least 19.</summary>
    public const string PositionStale = "CV-PLR-11";

    /// <summary>CV-PLR-12 (deferred): <c>supportSpeedCapMm</c> below the mail truck's road speed. Needs vehicles.</summary>
    public const string SupportSpeedCap = "CV-PLR-12";

    /// <summary>
    /// CV-PLR-13 (Error, stage 2; alias PCM-P10-1, input-controls H-R4-5): a content definition names a verb outside the closed
    /// <c>Verb</c> catalogue, or declares other than exactly one verb for one interactable. Reads
    /// <c>ContentValidationInput.VerbDeclarations</c> (player-character story 012).
    /// </summary>
    public const string UnknownVerb = "CV-PLR-13";
}

/// <summary>
/// The Player Character (player-character-movement) rule table (CV-PLR, alias <c>PC rule N</c>), owner
/// <c>player-character-movement</c>: CV-PLR-01, 02, 04, 06, 08, 09, 10 and 11 are live (content-data story 011) and CV-PLR-13
/// (alias PCM-P10-1, input-controls H-R4-5) is live (player-character story 012; it reads the adapter's verb declarations, which
/// are empty at M-1a); CV-PLR-03, 05, 07 and 12 are registered as <see cref="RuleStatus.Deferred"/> ids ("inputs not present at
/// M-1a") so they are never renumbered or reused and the fixture meta-test skips them explicitly. Rows are explicit entries in
/// this list; there is no reflection discovery. Never renumber or reuse an id.
/// <para>
/// Cross-owner rules: CV-PLR-01 (<c>player</c> + <c>inventory</c>) and CV-PLR-06 (<c>player</c> + <c>throw</c>) are stage 5 and
/// declare both owner files, so a failed file skips the rule and counts it. CV-PLR-01 reads Grid's registered key
/// <c>inventory.externalLeashMeters</c>; PC's text calls it <c>leashMm</c> and <c>externalContainerLeashMeters</c> (naming
/// to reconcile at the PC/Grid re-review). Stage: the catalogue puts the single-owner rules at stage 2 but the runner executes
/// only stages 4 and 5, so they are registered at stage 4 (TD-063 item 1; story 018 moves them). CV-PLR-06 carries the extra
/// spelling <c>Throwing rule 14</c> (its retired predecessor CV-THR-14 is listed in <see cref="Throwing"/>).
/// </para>
/// </summary>
/// <example>
/// <code>
/// RuleRegistry.CreateDefault().TryResolve("Throwing rule 14", out RuleSpec? rule);   // rule.Id == "CV-PLR-06"
/// </code>
/// </example>
public static class Player
{
    private const string Owner = "player-character-movement";
    private const string NotAtM1a = "inputs not present at M-1a: ";

    /// <summary>The rows, in registration order. A property that builds the array on each call, so the class holds no static state (ADR-0003 section 4).</summary>
    public static ImmutableArray<RuleSpec> Rules => Table(new PlayerChecks());

    private static ImmutableArray<RuleSpec> Table(PlayerChecks checks) => ImmutableArray.Create(
        RuleSpec.Create<ContentValidationInput>(
            PlayerRuleIds.RangeWithinLeash, Owner, "PC rule 1", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.PlayerBalance, ContentInputKeys.InventoryBalance),
            checks.CheckRangePlusSlackWithinLeash,
            ImmutableArray.Create(new RuleClause("range_plus_slack_within_leash", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            PlayerRuleIds.SwimHysteresis, Owner, "PC rule 2", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.PlayerBalance),
            checks.CheckSwimHysteresis,
            ImmutableArray.Create(new RuleClause("swim_exit_below_enter", HasLimit: true))),
        RuleSpec.Defer(PlayerRuleIds.BuffCap, Owner, "PC rule 3", RuleSeverity.Error, ValidationStage.WholeSet,
            NotAtM1a + "gaitPlaybackMax (a presentation constant) and Postal Sprint are M1"),
        RuleSpec.Create<ContentValidationInput>(
            PlayerRuleIds.MinHold, Owner, "PC rule 4", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.PlayerBalance),
            checks.CheckMinHold,
            ImmutableArray.Create(new RuleClause("min_hold_ticks", HasLimit: true))),
        RuleSpec.Defer(PlayerRuleIds.RespawnSet, Owner, "PC rule 5", RuleSeverity.Error, ValidationStage.WholeSet,
            NotAtM1a + "secondWindMs is retired; restated as a min-wins Set above base, which needs PerkDef (M1)"),
        RuleSpec.Create<ContentValidationInput>(
            PlayerRuleIds.CatchBelowRange, Owner, "PC rule 6", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ContentInputKeys.PlayerBalance, ContentInputKeys.ThrowBalance),
            checks.CheckCatchRadiusBelowRange,
            ImmutableArray.Create(new RuleClause("catch_radius_below_range", HasLimit: true)),
            ImmutableArray.Create("Throwing rule 14")),
        RuleSpec.Defer(PlayerRuleIds.PushCap, Owner, "PC rule 7", RuleSeverity.Error, ValidationStage.References,
            NotAtM1a + "pushCapMm belongs to M-1b (player pushing)"),
        RuleSpec.Create<ContentValidationInput>(
            PlayerRuleIds.HaulOut, Owner, "PC rule 8", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.PlayerBalance),
            checks.CheckHaulOutAtLeastStepUp,
            ImmutableArray.Create(new RuleClause("haul_out_at_least_step_up", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            PlayerRuleIds.IncumbentSlack, Owner, "PC rule 9", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.PlayerBalance),
            checks.CheckIncumbentSlackWithinRangeSlack,
            ImmutableArray.Create(new RuleClause("incumbent_slack_within_range_slack", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            PlayerRuleIds.DivisionSafety, Owner, "PC rule 10", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.PlayerBalance),
            checks.CheckDivisionSafety,
            ImmutableArray.Create(new RuleClause("positive_fields", HasLimit: true), new RuleClause("g_multiple_of_30", HasLimit: true))),
        RuleSpec.Create<ContentValidationInput>(
            PlayerRuleIds.PositionStale, Owner, "PC rule 11", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.PlayerBalance),
            checks.CheckPositionStale,
            ImmutableArray.Create(new RuleClause("position_stale_ticks", HasLimit: true))),
        RuleSpec.Defer(PlayerRuleIds.SupportSpeedCap, Owner, "PC rule 12", RuleSeverity.Error, ValidationStage.WholeSet,
            NotAtM1a + "the mail truck's road speed (Vehicles) is a later milestone"),
        RuleSpec.Create<ContentValidationInput>(
            PlayerRuleIds.UnknownVerb, Owner, "PCM-P10-1", RuleSeverity.Error, ValidationStage.References,   // catalogue stage 2
            ImmutableArray.Create(ContentInputKeys.Verbs),
            checks.CheckVerbs,
            ImmutableArray.Create(new RuleClause("known_verb", HasLimit: false), new RuleClause("one_verb_per_interactable", HasLimit: true)),
            ImmutableArray.Create("H-R4-5")));
}
