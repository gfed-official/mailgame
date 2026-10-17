using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Violation and valid-edge fixtures of the Player Character rules CV-PLR-01, 02, 04, 06, 08, 09, 10 and 11 (content-data
/// story 011). Each violation is <see cref="ContentFixtures.MinimalValid"/> plus one balance value that must produce exactly
/// one diagnostic of the rule's id; each edge sits exactly on the limit. The deferred rows (CV-PLR-03, 05, 07, 12, 13) have no
/// fixtures: the meta-test skips them. Appended to <see cref="RuleFixtureCatalogue.Real"/>.
/// </summary>
/// <example>
/// <code>
/// ValidationResult r = PlayerRuleFixtures.With("player.minHoldTicks", 5);   // one CV-PLR-04
/// </code>
/// </example>
internal static class PlayerRuleFixtures
{
    /// <summary>Validates the minimal set with <c>player.*</c> or <c>throw.*</c> key <paramref name="key"/> set to <paramref name="value"/>.</summary>
    public static ValidationResult With(string key, long value) => ContentFixtures.MinimalValid().WithBalance(key, value).Build().Validation;

    /// <summary>Validates the minimal set with <c>throw.catchRadiusMm</c> = <paramref name="radius"/> and <c>throw.catchHistoryMaxOffsetMm</c> = 0, so CV-THR-18 (radius + offset at most 3700) does not also fire for a large radius.</summary>
    public static ValidationResult WithCatchRadius(long radius) =>
        ContentFixtures.MinimalValid().WithBalance("throw.catchRadiusMm", radius).WithBalance("throw.catchHistoryMaxOffsetMm", 0).Build().Validation;

    private static RuleFixture Violation(string rule, string clause, Func<ValidationResult> produce) =>
        new(rule, clause, FixtureKind.Violation, produce, ImmutableArray<string>.Empty);

    private static RuleFixture Edge(string rule, string clause, Func<ValidationResult> produce) =>
        new(rule, clause, FixtureKind.ValidEdge, produce, ImmutableArray<string>.Empty);

    /// <summary>All the story 011 fixtures.</summary>
    public static ImmutableArray<RuleFixture> All() => ImmutableArray.Create(
        // CV-PLR-01: 2 500 + 500 = 3 000 passes, 2 501 + 500 errors
        Violation(PlayerRuleIds.RangeWithinLeash, "range_plus_slack_within_leash", () => With("player.rangeMm", 2501)),
        Edge(PlayerRuleIds.RangeWithinLeash, "range_plus_slack_within_leash", () => With("player.rangeMm", 2500)),
        // CV-PLR-02: swimExit = swimEnter - 100 passes, - 99 errors
        Violation(PlayerRuleIds.SwimHysteresis, "swim_exit_below_enter", () => With("player.swimExitMm", 801)),
        Edge(PlayerRuleIds.SwimHysteresis, "swim_exit_below_enter", () => With("player.swimExitMm", 800)),
        // CV-PLR-04: 6 passes, 5 errors
        Violation(PlayerRuleIds.MinHold, "min_hold_ticks", () => With("player.minHoldTicks", 5)),
        Edge(PlayerRuleIds.MinHold, "min_hold_ticks", () => With("player.minHoldTicks", 6)),
        // CV-PLR-06: catch radius = range errors, range - 1 passes (the catch history offset is 0 so CV-THR-18's reach limit stays quiet)
        Violation(PlayerRuleIds.CatchBelowRange, "catch_radius_below_range", () => WithCatchRadius(2500)),
        Edge(PlayerRuleIds.CatchBelowRange, "catch_radius_below_range", () => WithCatchRadius(2499)),
        // CV-PLR-08: haulOut = stepUp passes, below errors
        Violation(PlayerRuleIds.HaulOut, "haul_out_at_least_step_up", () => With("player.haulOutMm", 399)),
        Edge(PlayerRuleIds.HaulOut, "haul_out_at_least_step_up", () => With("player.haulOutMm", 400)),
        // CV-PLR-09: incumbentSlack + 100 = rangeSlack passes, +1 errors
        Violation(PlayerRuleIds.IncumbentSlack, "incumbent_slack_within_range_slack", () => With("player.incumbentSlackMm", 401)),
        Edge(PlayerRuleIds.IncumbentSlack, "incumbent_slack_within_range_slack", () => With("player.incumbentSlackMm", 400)),
        // CV-PLR-10 (the other fields and g's edges are asserted in PlayerRulesTests: the meta-test allows one fixture per kind and clause)
        Violation(PlayerRuleIds.DivisionSafety, "positive_fields", () => With("player.ticksAccel", 0)),
        Edge(PlayerRuleIds.DivisionSafety, "positive_fields", () => With("player.ticksAccel", 1)),
        Violation(PlayerRuleIds.DivisionSafety, "g_multiple_of_30", () => With("player.g", 24_001)),
        Edge(PlayerRuleIds.DivisionSafety, "g_multiple_of_30", () => With("player.g", 24_000)),
        // CV-PLR-11: 19 passes, 18 errors
        Violation(PlayerRuleIds.PositionStale, "position_stale_ticks", () => With("player.positionStaleTicks", 18)),
        Edge(PlayerRuleIds.PositionStale, "position_stale_ticks", () => With("player.positionStaleTicks", 19)));
}
