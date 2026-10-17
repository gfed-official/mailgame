using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using GeneralRules = PerformativeMail.Sim.Content.Rules.General;
using GridRules = PerformativeMail.Sim.Content.Rules.Grid;
using MailRules = PerformativeMail.Sim.Content.Rules.Mail;
using PlayerRules = PerformativeMail.Sim.Content.Rules.Player;
using ThrowingRules = PerformativeMail.Sim.Content.Rules.Throwing;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Violation and valid-edge fixtures of the Throwing rules CV-THR-01 to 06, 08 to 13, 15, 16 and 18 (content-data story 012).
/// Each violation is <see cref="ContentFixtures.MinimalValid"/> plus one overlay that must produce exactly one diagnostic of
/// the rule's id; each edge sits exactly on the limit. CV-THR-07 and 17 are deferred and have no fixtures. Appended to
/// <see cref="RuleFixtureCatalogue.Real"/>.
/// <para>
/// Shadowing: CV-GEN-13 rejects a value outside a balance key's hard range before CV-THR can see it. Where a hard range equals
/// the rule's range (speeds 3000 to 30000, <c>g</c> at least 1, <c>maxFlightTicks</c> 30 to 150) the fixture runs against a
/// registry whose hard range for that key is widened to -1 000 000 to 10 000 000 (<see cref="Widened"/>), proving the rule on its own;
/// <c>ThrowingRulesTests</c> pins the shadowing. CV-THR-02's sources are synthetic (<see cref="RegistryWithRangeSource"/>).
/// </para>
/// </summary>
/// <example>
/// <code>
/// ValidationResult r = ThrowingRuleFixtures.With("throw.catchTapMs", 501);   // one CV-THR-12
/// </code>
/// </example>
internal static class ThrowingRuleFixtures
{
    /// <summary>Validates the minimal set with balance key <paramref name="key"/> set to <paramref name="value"/>.</summary>
    public static ValidationResult With(string key, long value) => ContentFixtures.MinimalValid().WithBalance(key, value).Build().Validation;

    /// <summary>Validates <paramref name="draft"/> through the default rules.</summary>
    public static ValidationResult V(ContentDraft draft) => draft.Build().Validation;

    /// <summary>The production tables with CV-THR-02 reading <paramref name="add"/> as a live single <c>ThrowRangePermille</c> Add source.</summary>
    public static RuleRegistry RegistryWithRangeSource(params int[] add) => RuleRegistry.Compose(
        GeneralRules.Rules, GridRules.Rules, MailRules.Rules, PlayerRules.Rules, ThrowingRules.RulesWithSources(ImmutableArray.Create(add)));

    /// <summary>
    /// Validates <paramref name="draft"/> against <paramref name="rules"/> (default: every registered rule) with the hard range of
    /// each key in <paramref name="keys"/> widened to -1 000 000 to 10 000 000, so CV-GEN-13 does not shadow the CV-THR rule under test.
    /// </summary>
    public static ValidationResult Widened(ContentDraft draft, RuleRegistry? rules, params string[] keys)
    {
        ImmutableArray<BalanceRow> rows = BalanceKeys.CreateRegistry().Rows
            .Select(r => keys.Contains(r.Name) ? r with { HardMin = -1_000_000, HardMax = 10_000_000 } : r).ToImmutableArray();
        BalanceTable table = BalanceTable.Build(new BalanceRegistry(rows), draft.Balance).Table
            ?? throw new InvalidOperationException("The widened balance did not validate.");
        return ContentPipeline.Build(
            table, draft.Containers, draft.Items, draft.MailKinds, draft.DestinationTypes, draft.MailMixes, rules ?? RuleRegistry.CreateDefault()).Validation;
    }

    /// <summary>The minimal set with <paramref name="key"/> = <paramref name="value"/>, validated with that key's hard range widened.</summary>
    public static ValidationResult WidenedWith(string key, long value) =>
        Widened(ContentFixtures.MinimalValid().WithBalance(key, value), null, key);

    /// <summary>The minimal set with the house's catch volume replaced.</summary>
    public static ContentDraft Volume(CatchVolume volume) =>
        ContentFixtures.MinimalValid().Replace(ContentFixtures.House with { CatchVolume = volume });

    /// <summary>The house's catch volume with one field changed.</summary>
    public static CatchVolume BaseVolume => ContentFixtures.House.CatchVolume;

    /// <summary>The minimal set with the plank item's weight class and hand-carry flag replaced, and the cursor narrowed to mail so CV-GRD-02 stays quiet.</summary>
    public static ContentDraft Plank(WeightClass weight, bool carryByHand) => ContentFixtures.MinimalValid()
        .Replace(ContentFixtures.Cursor with { HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Mail) })
        .Replace(ContentFixtures.Plank with { WeightClass = weight, CarryByHand = carryByHand });

    private static RuleFixture Violation(string rule, string clause, Func<ValidationResult> produce, params string[] alsoTrips) =>
        new(rule, clause, FixtureKind.Violation, produce, ImmutableArray.Create(alsoTrips));

    private static RuleFixture Edge(string rule, string clause, Func<ValidationResult> produce) =>
        new(rule, clause, FixtureKind.ValidEdge, produce, ImmutableArray<string>.Empty);

    private static ValidationResult WithRangeSource(int add, long? rangeMin = null)
    {
        ContentDraft draft = ContentFixtures.MinimalValid();
        if (rangeMin is not null)
        {
            draft = draft.WithBalance("throw.rangeMinPermille", rangeMin.Value);
        }

        return draft.Build(RegistryWithRangeSource(add)).Validation;
    }

    /// <summary>All the story 012 fixtures.</summary>
    public static ImmutableArray<RuleFixture> All() => ImmutableArray.Create(
        // CV-THR-01 (order: heavy = medium errors, heavy 9999 passes; range: heavy 2999 errors, 3000 passes, both with the hard range widened)
        Violation(ThrowingRuleIds.SpeedOrdering, "speed_order", () => With("throw.throwSpeed.heavy", 10_000)),
        Edge(ThrowingRuleIds.SpeedOrdering, "speed_order", () => With("throw.throwSpeed.heavy", 9999)),
        Violation(ThrowingRuleIds.SpeedOrdering, "speed_range", () => WidenedWith("throw.throwSpeed.heavy", 2999)),
        Edge(ThrowingRuleIds.SpeedOrdering, "speed_range", () => WidenedWith("throw.throwSpeed.heavy", 3000)),
        // CV-THR-02 (F5 single source: base 1000 + 500 = 1500 passes, + 501 errors; CV-PRK-05 (|A| > max - min) is M1 and unregistered, so it cannot fire yet)
        Violation(ThrowingRuleIds.ThrowRangeSources, "range_above_max", () => WithRangeSource(501), "CV-PRK-05"),
        Edge(ThrowingRuleIds.ThrowRangeSources, "range_above_max", () => WithRangeSource(500)),
        Violation(ThrowingRuleIds.ThrowRangeSources, "range_below_min", () => WithRangeSource(-1)),
        Edge(ThrowingRuleIds.ThrowRangeSources, "range_below_min", () => WithRangeSource(-1, rangeMin: 999)),
        // CV-THR-03 (g 0 errors, 1 passes; the hard range is widened)
        Violation(ThrowingRuleIds.GravityPositive, "g_positive", () => WidenedWith("throw.g", 0)),
        Edge(ThrowingRuleIds.GravityPositive, "g_positive", () => With("throw.g", 1)),
        // CV-THR-04 (29 errors, 30 passes; the hard range is widened)
        Violation(ThrowingRuleIds.MaxFlightTicks, "max_flight_ticks_range", () => WidenedWith("throw.maxFlightTicks", 29)),
        Edge(ThrowingRuleIds.MaxFlightTicks, "max_flight_ticks_range", () => With("throw.maxFlightTicks", 30)),
        // CV-THR-05 (water takes armedCatcher's rank 0; no limit, the permutation edge is asserted in ThrowingRulesTests)
        Violation(ThrowingRuleIds.RankPermutation, "rank_permutation", () => With("throw.rank.water", 0)),
        // CV-THR-06 (yaw 45 degrees; width 0 / 1; height 2002 puts the bottom at -1, 2000 at 0)
        Violation(ThrowingRuleIds.CatchVolumes, "volume_axis_aligned", () => V(Volume(BaseVolume with { YawMilliDeg = 45_000 }))),
        Violation(ThrowingRuleIds.CatchVolumes, "volume_extents_positive", () => V(Volume(BaseVolume with { WidthMm = 0 }))),
        Edge(ThrowingRuleIds.CatchVolumes, "volume_extents_positive", () => V(Volume(BaseVolume with { WidthMm = 1 }))),
        Violation(ThrowingRuleIds.CatchVolumes, "volume_bottom_at_base", () => V(Volume(BaseVolume with { HeightMm = 2002 }))),
        Edge(ThrowingRuleIds.CatchVolumes, "volume_bottom_at_base", () => V(Volume(BaseVolume with { HeightMm = 2000 }))),
        // CV-THR-08 (a weight class outside the closed enum; a bulk item that is hand-carried)
        Violation(ThrowingRuleIds.WeightClasses, "weight_class_present", () => V(Plank((WeightClass)0, carryByHand: true))),
        Violation(ThrowingRuleIds.WeightClasses, "bulk_not_hand_carried", () => V(Plank(WeightClass.Bulk, carryByHand: true))),
        // CV-THR-09 (capsule radius 300: 299 errors, 300 passes)
        Violation(ThrowingRuleIds.FwdOffClearsCapsule, "fwd_off_at_least_capsule_radius", () => With("throw.fwdOffMm", 299)),
        Edge(ThrowingRuleIds.FwdOffClearsCapsule, "fwd_off_at_least_capsule_radius", () => With("throw.fwdOffMm", 300)),
        // CV-THR-10 (capsule height 1400: 1401 errors, 1400 passes; 0 is asserted in ThrowingRulesTests)
        Violation(ThrowingRuleIds.HandHeightInCapsule, "hand_height_within_capsule", () => With("throw.handHeightMm", 1401)),
        Edge(ThrowingRuleIds.HandHeightInCapsule, "hand_height_within_capsule", () => With("throw.handHeightMm", 1400)),
        // CV-THR-11 (70 deg + 9 deg errors; + 8 deg passes; milli-degrees)
        Violation(ThrowingRuleIds.LoftWithinHostBound, "loft_within_host_bound", () => With("throw.loftDeg", 9000)),
        Edge(ThrowingRuleIds.LoftWithinHostBound, "loft_within_host_bound", () => With("throw.loftDeg", 8000)),
        // CV-THR-12 (F3: tap 501 errors, 100 passes; predict 450 errors, 100 passes; the 500 default is asserted in ThrowingRulesTests)
        Violation(ThrowingRuleIds.CatchWholeTicks, "catch_tap_whole_ticks", () => With("throw.catchTapMs", 501)),
        Edge(ThrowingRuleIds.CatchWholeTicks, "catch_tap_whole_ticks", () => With("throw.catchTapMs", 100)),
        Violation(ThrowingRuleIds.CatchWholeTicks, "catch_predict_whole_ticks", () => With("throw.catchPredictMs", 450)),
        Edge(ThrowingRuleIds.CatchWholeTicks, "catch_predict_whole_ticks", () => With("throw.catchPredictMs", 100)),
        // CV-THR-13 (90 ticks = 3000 ms: 3100 errors, 3000 passes)
        Violation(ThrowingRuleIds.CatchPredictWithinFlight, "catch_predict_within_flight", () => With("throw.catchPredictMs", 3100)),
        Edge(ThrowingRuleIds.CatchPredictWithinFlight, "catch_predict_within_flight", () => With("throw.catchPredictMs", 3000)),
        // CV-THR-15 (0 errors, 1 passes)
        Violation(ThrowingRuleIds.DamagePerPoint, "damage_per_point", () => With("throw.damagePerPoint", 0)),
        Edge(ThrowingRuleIds.DamagePerPoint, "damage_per_point", () => With("throw.damagePerPoint", 1)),
        // CV-THR-16 (0 errors, 1 passes, for each key)
        Violation(ThrowingRuleIds.CooldownAndBurst, "throw_cooldown_ticks", () => With("throw.throwCooldownTicks", 0)),
        Edge(ThrowingRuleIds.CooldownAndBurst, "throw_cooldown_ticks", () => With("throw.throwCooldownTicks", 1)),
        Violation(ThrowingRuleIds.CooldownAndBurst, "throw_burst_ticks", () => With("throw.throwBurstTicks", 0)),
        Edge(ThrowingRuleIds.CooldownAndBurst, "throw_burst_ticks", () => With("throw.throwBurstTicks", 1)),
        // CV-THR-18 (radius 1500: offset 2200 gives 3700 and passes, 2201 gives 3701 and errors)
        Violation(ThrowingRuleIds.CatchReach, "catch_reach", () => With("throw.catchHistoryMaxOffsetMm", 2201)),
        Edge(ThrowingRuleIds.CatchReach, "catch_reach", () => With("throw.catchHistoryMaxOffsetMm", 2200)));
}
