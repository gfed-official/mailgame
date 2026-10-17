using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using ThrowingRules = PerformativeMail.Sim.Content.Rules.Throwing;
using Fx = PerformativeMail.Sim.Tests.Unit.Content.ThrowingRuleFixtures;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 012 (TR-content-022, M-1a subset; GDD TC-CV-1, CDD-RULE-2, CDD-VAL-1, CDD-STAT-11, CDD-UNIT-3): the
/// Throwing rules CV-THR-01 to 06, 08 to 13, 15, 16 and 18 through the production pipeline, the deferred ids CV-THR-07 and 17, the
/// retired CV-THR-14, the F3 helper, the cross-owner skip, the id-plus-alias message and the shipped content. The per-clause
/// fixtures live in <see cref="ThrowingRuleFixtures"/> and are checked by the meta-test in <c>RuleRegistryTests</c>.
/// </summary>
public class ThrowingRulesTests
{
    private static readonly string[] _liveIds =
    {
        ThrowingRuleIds.SpeedOrdering, ThrowingRuleIds.ThrowRangeSources, ThrowingRuleIds.GravityPositive, ThrowingRuleIds.MaxFlightTicks,
        ThrowingRuleIds.RankPermutation, ThrowingRuleIds.CatchVolumes, ThrowingRuleIds.WeightClasses, ThrowingRuleIds.FwdOffClearsCapsule,
        ThrowingRuleIds.HandHeightInCapsule, ThrowingRuleIds.LoftWithinHostBound, ThrowingRuleIds.CatchWholeTicks,
        ThrowingRuleIds.CatchPredictWithinFlight, ThrowingRuleIds.DamagePerPoint, ThrowingRuleIds.CooldownAndBurst, ThrowingRuleIds.CatchReach,
    };

    private static ContentDiagnostic[] Of(ValidationResult r, string id) => r.Diagnostics.Where(d => d.RuleId == id).ToArray();

    private static ValidationResult With(string key, long value) => Fx.With(key, value);

    private static ValidationResult With(string k1, long v1, string k2, long v2) =>
        ContentFixtures.MinimalValid().WithBalance(k1, v1).WithBalance(k2, v2).Build().Validation;

    private static void AssertOnlyOne(ValidationResult r, string id)
    {
        ContentDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal(id, d.RuleId);
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.False(r.CanBuild);
    }

    private static void AssertClean(ValidationResult r)
    {
        Assert.Empty(r.Diagnostics);
        Assert.True(r.CanBuild);
    }

    private static RuleRegistry ThrowingOnly() => RuleRegistry.Compose(ThrowingRules.Rules);

    private static string Id(int n) => "CV-THR-" + n.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------ the rows

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Rows_AreRegisteredAsErrorsWithTheThrowCvAlias_AndTheCatalogueStages()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();
        var wholeSet = new HashSet<string>
        {
            ThrowingRuleIds.ThrowRangeSources, ThrowingRuleIds.FwdOffClearsCapsule, ThrowingRuleIds.HandHeightInCapsule,
        };

        foreach (string id in _liveIds)
        {
            RuleSpec rule = Assert.IsAssignableFrom<RuleSpec>(registry.Find(id));
            string alias = "THROW-CV-" + id.Substring("CV-THR-".Length);
            Assert.Equal(RuleSeverity.Error, rule.Severity);
            Assert.Equal(RuleStatus.Active, rule.Status);
            Assert.Equal("throwing-catching", rule.Owner);
            Assert.Equal(alias, rule.Alias);
            Assert.True(rule.HasCheck);
            Assert.Equal(wholeSet.Contains(id) ? ValidationStage.WholeSet : ValidationStage.References, rule.Stage);
            Assert.True(registry.TryResolve(alias, out RuleSpec? byAlias));
            Assert.Equal(id, byAlias!.Id);
            Assert.Contains(rule.Inputs, i => i == ContentInputKeys.ThrowBalance || i == ContentInputKeys.DestinationTypes || i == ContentInputKeys.MailKinds);
        }

        Assert.Equal(
            _liveIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            ThrowingRules.Rules.Where(r => r.Status == RuleStatus.Active).Select(r => r.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            Enumerable.Range(1, 18).Select(Id).ToArray(),
            ThrowingRules.Rules.Select(r => r.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void AliasesAreUnique_AndEveryAliasResolvesToItsOwnId()
    {
        ImmutableArray<RuleSpec> rows = ThrowingRules.Rules;

        Assert.Equal(rows.Length, rows.Select(r => r.Alias).Distinct(StringComparer.Ordinal).Count());
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ThrowingRules.Rules, ThrowingRules.Rules));   // a duplicate id/alias is refused
        RuleRegistry registry = RuleRegistry.CreateDefault();
        foreach (RuleSpec row in rows)
        {
            Assert.True(registry.TryResolve(row.Alias, out RuleSpec? found));
            Assert.Equal(row.Id, found!.Id);
        }
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void CrossOwnerRules_DeclareBothOwnerFiles_AtStage5()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();

        foreach (string id in new[] { ThrowingRuleIds.FwdOffClearsCapsule, ThrowingRuleIds.HandHeightInCapsule })
        {
            RuleSpec rule = registry.Find(id)!;
            Assert.Equal(new[] { ContentInputKeys.ThrowBalance, ContentInputKeys.PlayerBalance }, rule.Inputs.ToArray());
            Assert.Equal(ValidationStage.WholeSet, rule.Stage);
        }
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Thr14_StaysRetired_AndPointsAtPlr06()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();

        RuleSpec retired = Assert.IsAssignableFrom<RuleSpec>(registry.Find(ThrowingRuleIds.Retired14));
        Assert.Equal(RuleStatus.Retired, retired.Status);
        Assert.Equal(PlayerRuleIds.CatchBelowRange, retired.RetiredInto);
        Assert.False(retired.HasCheck);
        Assert.True(registry.TryResolve("THROW-CV-14", out RuleSpec? byAlias));
        Assert.Equal(ThrowingRuleIds.Retired14, byAlias!.Id);
        Assert.True(registry.TryResolve("Throwing rule 14", out RuleSpec? plr));
        Assert.Equal(PlayerRuleIds.CatchBelowRange, plr!.Id);
        Assert.Empty(registry.UnresolvedRetirements());
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Thr07AndThr17_AreDeferredWithAReason_NeverRun_AndHaveNoFixtureObligation()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();

        foreach (string id in new[] { ThrowingRuleIds.EnemyDimensions, ThrowingRuleIds.MapExtents })
        {
            RuleSpec rule = Assert.IsAssignableFrom<RuleSpec>(registry.Find(id));
            Assert.Equal(RuleStatus.Deferred, rule.Status);
            Assert.False(rule.HasCheck);
            Assert.StartsWith("inputs not present at M-1a", rule.DeferredReason, StringComparison.Ordinal);
        }

        Assert.Contains("EnemyDef", registry.Find(ThrowingRuleIds.EnemyDimensions)!.DeferredReason, StringComparison.Ordinal);
        Assert.Empty(FixtureChecker.Check(ThrowingOnly(), ThrowingRuleFixtures.All(), RuleFixtureCatalogue.ForwardReferences));
        Assert.DoesNotContain(ThrowingRuleFixtures.All(), f => f.RuleId is ThrowingRuleIds.EnemyDimensions or ThrowingRuleIds.MapExtents);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void StaleNames_AreNotRegistered()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();
        string[] stale = { "cooldownSlackTicks", "throwSpreadMrad" };

        foreach (RuleSpec rule in registry.Rules)
        {
            foreach (RuleClause clause in rule.Clauses)
            {
                Assert.DoesNotContain(clause.Name, stale);
            }
        }

        Assert.False(BalanceKeys.CreateRegistry().TryFind("throw.cooldownSlackTicks", out _));
        Assert.False(BalanceKeys.CreateRegistry().TryFind("throw.throwSpreadMrad", out _));
    }

    // ------------------------------------------------------------ fixtures and message

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void EveryLiveRule_HasViolationFixtures_ThatYieldExactlyOneDiagnosticOfTheirId_AndAnEdgeFixtureAtEachLimit()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();
        ImmutableArray<RuleFixture> fixtures = ThrowingRuleFixtures.All();

        foreach (RuleSpec rule in registry.Rules.Where(r => r.Status == RuleStatus.Active && r.Id.StartsWith("CV-THR-", StringComparison.Ordinal)))
        {
            foreach (RuleClause clause in rule.Clauses)
            {
                RuleFixture violation = Assert.Single(fixtures, f => f.RuleId == rule.Id && f.Clause == clause.Name && f.Kind == FixtureKind.Violation);
                ValidationResult r = violation.Produce();
                ContentDiagnostic d = Assert.Single(Of(r, rule.Id));
                Assert.Equal(RuleSeverity.Error, d.Severity);
                Assert.False(r.CanBuild);
                if (clause.HasLimit)
                {
                    RuleFixture edge = Assert.Single(fixtures, f => f.RuleId == rule.Id && f.Clause == clause.Name && f.Kind == FixtureKind.ValidEdge);
                    AssertClean(edge.Produce());
                }
            }
        }

        Assert.Empty(FixtureChecker.Check(ThrowingOnly(), fixtures, RuleFixtureCatalogue.ForwardReferences));
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Message_PrintsTheIdAndTheThrowCvAlias_ForEveryLiveRule()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();

        foreach (RuleFixture f in ThrowingRuleFixtures.All().Where(x => x.Kind == FixtureKind.Violation))
        {
            ContentDiagnostic d = Assert.Single(Of(f.Produce(), f.RuleId));
            string expected = f.RuleId + " (THROW-CV-" + f.RuleId.Substring("CV-THR-".Length) + ")";
            Assert.Equal(expected, registry.FormatId(f.RuleId));
            Assert.Contains("error " + expected, d.Format(registry), StringComparison.Ordinal);
        }

        Assert.Equal("CV-THR-12 (THROW-CV-12)", registry.FormatId("CV-THR-12"));
    }

    // ------------------------------------------------------------ F3

    [Theory]
    [Trait("Id", "CDD-UNIT-3")]
    [InlineData(500, true)]   // 15 000 mod 1 000 = 0, 15 ticks
    [InlineData(100, true)]   // 3 000, 3 ticks
    [InlineData(0, true)]
    [InlineData(86_400_000, true)]
    [InlineData(501, false)]  // 15 030 mod 1 000 = 30
    [InlineData(50, false)]   // 1 500 mod 1 000 = 500
    [InlineData(150, false)]  // Sim Core F1 would round 150 up to 5 ticks; F3 rejects it
    [InlineData(450, false)]
    public void ExactTickCheck_NormativeVectors(int ms, bool expected)
    {
        Assert.Equal(expected, ExactTickCheck.IsWholeTicks(ms));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-3")]
    public void ExactTickCheck_IsTrueExactlyForMultiplesOf100_OverTheWholeDomain()
    {
        for (int ms = 0; ms <= 86_400_000; ms++)
        {
            if (ExactTickCheck.IsWholeTicks(ms) != (ms % 100 == 0))
            {
                Assert.Fail("F3 disagrees with ms % 100 at " + ms);
            }
        }
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-3")]
    public void ExactTickCheck_UsesLongArithmetic_SoAMillisecondValueAboveTheInt32ProductDoesNotWrap()
    {
        // 71 582 788 ms x 30 exceeds int32; 71 582 800 is a multiple of 100 and must still be true, 71 582 801 false.
        Assert.True(ExactTickCheck.IsWholeTicks(71_582_800));
        Assert.False(ExactTickCheck.IsWholeTicks(71_582_801));
        Assert.True(ExactTickCheck.IsWholeTicks(int.MaxValue - (int.MaxValue % 100)));
        Assert.False(ExactTickCheck.IsWholeTicks(int.MaxValue));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-3")]
    public void ExactTickCheck_AValueAboveInt32MaxOver30_AndALongAboveInt32_DoNotWrap()
    {
        long justAbove = (int.MaxValue / 30) + 1;   // 71 582 789: ms x 30 no longer fits int32
        Assert.Equal(justAbove % 100 == 0, ExactTickCheck.IsWholeTicks(justAbove));
        Assert.True(ExactTickCheck.IsWholeTicks(71_582_800));
        Assert.False(ExactTickCheck.IsWholeTicks(71_582_850));
        Assert.True(ExactTickCheck.IsWholeTicks(3_000_000_000L));   // above int.MaxValue itself
        Assert.False(ExactTickCheck.IsWholeTicks(3_000_000_001L));
    }

    [Fact]
    [Trait("Id", "CDD-UNIT-3")]
    public void ExactTickCheck_NegativeValues_FollowTheSameModulo_AndTheCallerRangeChecks()
    {
        Assert.True(ExactTickCheck.IsWholeTicks(-100));
        Assert.True(ExactTickCheck.IsWholeTicks(-500));
        Assert.False(ExactTickCheck.IsWholeTicks(-150));
        Assert.False(ExactTickCheck.IsWholeTicks(-1));
    }

    // ------------------------------------------------------------ CV-THR-01

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(9999, 0)]
    [InlineData(10_000, 1)]   // heavy = medium
    [InlineData(10_001, 1)]
    public void Thr01_HeavyMustBeBelowMedium(int heavy, int errors)
    {
        Assert.Equal(errors, Of(With("throw.throwSpeed.heavy", heavy), ThrowingRuleIds.SpeedOrdering).Length);
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr01_MediumMustBeBelowLight_AndOneFindingNamesTheTriple()
    {
        ValidationResult r = With("throw.throwSpeed.medium", 14_000);   // medium = light

        ContentDiagnostic d = Assert.Single(Of(r, ThrowingRuleIds.SpeedOrdering));
        Assert.Contains("heavy 6000", d.Value, StringComparison.Ordinal);
        Assert.Contains("light 14000", d.Value, StringComparison.Ordinal);
        AssertOnlyOne(With("throw.throwSpeed.light", 9000), ThrowingRuleIds.SpeedOrdering);   // light below medium
    }

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData("throw.throwSpeed.heavy", 2999, 1)]
    [InlineData("throw.throwSpeed.heavy", 3000, 0)]
    [InlineData("throw.throwSpeed.light", 30_000, 0)]
    [InlineData("throw.throwSpeed.light", 30_001, 1)]
    public void Thr01_EachSpeedMustBeWithin3000To30000_ProvenWithTheHardRangeWidened(string key, int value, int errors)
    {
        ValidationResult r = Fx.WidenedWith(key, value);

        Assert.Equal(errors, Of(r, ThrowingRuleIds.SpeedOrdering).Length);
        Assert.Equal(errors, r.ErrorCount);
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr01_GddViolationLight2999_AlsoBreaksTheOrdering_SoTheFixtureUsesHeavy2999()
    {
        // The GDD's "light 2 999" cannot be a single-finding violation: light below medium also fails the ordering.
        ValidationResult r = Fx.WidenedWith("throw.throwSpeed.light", 2999);

        Assert.Equal(2, Of(r, ThrowingRuleIds.SpeedOrdering).Length);
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr01_HeavyZero_OnAWidenedRegistry_IsBothAnOrderingAndARangeFinding()
    {
        ValidationResult r = Fx.WidenedWith("throw.throwSpeed.heavy", 0);

        ContentDiagnostic[] found = Of(r, ThrowingRuleIds.SpeedOrdering);
        Assert.Equal(2, found.Length);
        Assert.Contains(found, d => d.Allowed.Contains("0 < heavy", StringComparison.Ordinal));
        Assert.Contains(found, d => d.Value.Contains("throwSpeed.heavy 0", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ CV-THR-02 (F5 single source)

    private static ValidationResult WithSources(params int[] adds) => ContentFixtures.MinimalValid().Build(Fx.RegistryWithRangeSource(adds)).Validation;

    [Theory]
    [Trait("Id", "CDD-STAT-11")]
    [InlineData(500, 0)]   // 1000 + 500 = 1500 passes
    [InlineData(501, 1)]   // 1501 errors
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    public void Thr02_AnAddSourceOnTheBase_StaysWithinTheRangeMax(int add, int errors)
    {
        ValidationResult r = WithSources(add);

        Assert.Equal(errors, Of(r, ThrowingRuleIds.ThrowRangeSources).Length);
        Assert.Equal(errors, r.ErrorCount);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void Thr02_AboveMaxMessageNamesTheSum_AndTheLowerBoundClauseIsSeparate()
    {
        ContentDiagnostic above = Assert.Single(Of(WithSources(501), ThrowingRuleIds.ThrowRangeSources));
        Assert.Contains("1000 + 501 = 1501", above.Value, StringComparison.Ordinal);
        Assert.Contains("rangeMaxPermille 1500", above.Allowed, StringComparison.Ordinal);

        ContentDiagnostic below = Assert.Single(Of(WithSources(-1), ThrowingRuleIds.ThrowRangeSources));
        Assert.Contains("1000 + -1 = 999", below.Value, StringComparison.Ordinal);
        Assert.Contains("rangeMinPermille 1000", below.Allowed, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void Thr02_SourcesAreCheckedOneAtATime_NotSummed_AndEachOffenderReportsOnce()
    {
        AssertClean(WithSources(300, 300, 500));   // single-source mode: the sum 1100 is the runtime clamp's business
        Assert.Equal(2, Of(WithSources(501, 500, 700), ThrowingRuleIds.ThrowRangeSources).Length);
        AssertClean(ContentFixtures.MinimalValid().Build(Fx.RegistryWithRangeSource()).Validation);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void Thr02_TracksTheBalanceRangeMax()
    {
        ValidationResult widerMax = ContentFixtures.MinimalValid().WithBalance("throw.rangeMaxPermille", 1600).Build(Fx.RegistryWithRangeSource(501)).Validation;
        ValidationResult narrowerMax = ContentFixtures.MinimalValid().WithBalance("throw.rangeMaxPermille", 1499).Build(Fx.RegistryWithRangeSource(500)).Validation;

        AssertClean(widerMax);
        AssertOnlyOne(narrowerMax, ThrowingRuleIds.ThrowRangeSources);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void Thr02_WithNoSources_PassesAtM1a_AndProductionRowsCarryNone()
    {
        AssertClean(With("throw.rangeMaxPermille", 1000));
        Assert.Equal(ValidationStage.WholeSet, RuleRegistry.CreateDefault().Find(ThrowingRuleIds.ThrowRangeSources)!.Stage);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Thr02_Fixture_ListsCvPrk05_AsAnUnregisteredForwardReference_AndTheMetaTestAccepts()
    {
        RuleFixture violation = Assert.Single(
            ThrowingRuleFixtures.All(), f => f.RuleId == ThrowingRuleIds.ThrowRangeSources && f.Clause == "range_above_max" && f.Kind == FixtureKind.Violation);
        RuleRegistry registry = RuleRegistry.CreateDefault();

        Assert.Contains("CV-PRK-05", violation.AlsoTrips);
        Assert.Null(registry.Find("CV-PRK-05"));   // M1: the perk rules are not registered yet
        Assert.Empty(FixtureChecker.Check(ThrowingOnly(), ThrowingRuleFixtures.All(), RuleFixtureCatalogue.ForwardReferences));

        // A registered rule that does not fire is still stale.
        var stale = new RuleFixture(
            ThrowingRuleIds.ThrowRangeSources, "range_above_max", FixtureKind.Violation, violation.Produce, ImmutableArray.Create("CV-PRK-05", ThrowingRuleIds.GravityPositive));
        ImmutableArray<FixtureProblem> problems = FixtureChecker.Check(
            ThrowingOnly(), forwardReferences: RuleFixtureCatalogue.ForwardReferences, fixtures: ThrowingRuleFixtures.All().RemoveAll(f => f.RuleId == ThrowingRuleIds.ThrowRangeSources && f.Clause == "range_above_max" && f.Kind == FixtureKind.Violation).Add(stale));
        FixtureProblem p = Assert.Single(problems);
        Assert.Equal(FixtureProblemKind.StaleAlsoTrips, p.Kind);
    }

    // ------------------------------------------------------------ CV-THR-03 and 04 (hard ranges shadow them)

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(9800, 0)]
    public void Thr03_GravityMustBeAboveZero_ProvenWithTheHardRangeWidened(int g, int errors)
    {
        Assert.Equal(errors, Of(Fx.WidenedWith("throw.g", g), ThrowingRuleIds.GravityPositive).Length);
    }

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(29, 1)]
    [InlineData(30, 0)]
    [InlineData(150, 0)]
    [InlineData(151, 1)]
    public void Thr04_MaxFlightTicksMustBeWithin30To150_ProvenWithTheHardRangeWidened(int ticks, int errors)
    {
        ValidationResult r = Fx.WidenedWith("throw.maxFlightTicks", ticks);

        Assert.Equal(errors, Of(r, ThrowingRuleIds.MaxFlightTicks).Length);
    }

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData("throw.g", 0)]
    [InlineData("throw.maxFlightTicks", 29)]
    [InlineData("throw.maxFlightTicks", 151)]
    [InlineData("throw.throwSpeed.heavy", 2999)]
    [InlineData("throw.throwSpeed.light", 30_001)]
    public void ShadowedViolations_AreRefusedByTheBalanceHardRange_BeforeTheThrowingRuleRuns(string key, int value)
    {
        // CV-GEN-13 stops the build at the balance table, so the CV-THR rule never sees the value: the rule is the defence if the hard range is relaxed.
        ImmutableArray<BalanceEntry> entries = ContentFixtures.MinimalValid().WithBalance(key, value).Balance;

        BalanceBuildResult b = BalanceTable.Build(BalanceKeys.CreateRegistry(), entries);

        Assert.Null(b.Table);
        Assert.Contains(b.Validation.Diagnostics, d => d.RuleId == GeneralRuleIds.BalanceKey);
        Assert.DoesNotContain(b.Validation.Diagnostics, d => d.RuleId.StartsWith("CV-THR-", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ CV-THR-05

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr05_ADuplicateRankErrors_APermutationOfDifferentOrderPasses()
    {
        ContentDiagnostic d = Assert.Single(Of(With("throw.rank.water", 0), ThrowingRuleIds.RankPermutation));
        Assert.Contains("rank 0 is used twice", d.Value, StringComparison.Ordinal);
        AssertClean(With("throw.rank.armedCatcher", 7, "throw.rank.water", 0));   // swapped: still a permutation
        AssertOnlyOne(With("throw.rank.mailbox", 0), ThrowingRuleIds.RankPermutation);   // one finding however many collide
    }

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(8)]
    [InlineData(-1)]
    public void Thr05_ARankOutsideZeroToSeven_IsRefusedByTheHardRange_AndIsOneFindingOnceItIsWidened(int rank)
    {
        // Shadowing pin: the balance hard range is 0..7, so CV-GEN-13 stops the build before CV-THR-05 can see the value.
        BalanceBuildResult b = BalanceTable.Build(BalanceKeys.CreateRegistry(), ContentFixtures.MinimalValid().WithBalance("throw.rank.water", rank).Balance);
        Assert.Null(b.Table);
        Assert.Contains(b.Validation.Diagnostics, d => d.RuleId == GeneralRuleIds.BalanceKey);

        ValidationResult r = Fx.Widened(ContentFixtures.MinimalValid().WithBalance("throw.rank.water", rank), null, "throw.rank.water");

        ContentDiagnostic d = Assert.Single(Of(r, ThrowingRuleIds.RankPermutation));
        Assert.Contains("outside 0..7", d.Value, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ CV-THR-06

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(0, 0)]
    [InlineData(90_000, 0)]
    [InlineData(270_000, 0)]
    [InlineData(-90_000, 0)]
    [InlineData(45_000, 1)]
    [InlineData(89_999, 1)]
    public void Thr06_YawMustBeAMultipleOf90Degrees(int yaw, int errors)
    {
        ValidationResult r = Fx.V(Fx.Volume(Fx.BaseVolume with { YawMilliDeg = yaw }));

        Assert.Equal(errors, Of(r, ThrowingRuleIds.CatchVolumes).Length);
    }

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(1, 1, 1, 0)]
    [InlineData(0, 800, 1000, 1)]
    [InlineData(1000, 0, 1000, 1)]
    [InlineData(1000, 800, 0, 1)]
    [InlineData(0, 0, 0, 1)]   // one finding per volume however many extents are zero
    [InlineData(-1, 800, 1000, 1)]
    public void Thr06_EveryExtentMustBeAboveZero(int width, int height, int depth, int errors)
    {
        ValidationResult r = Fx.V(Fx.Volume(Fx.BaseVolume with { WidthMm = width, HeightMm = height, DepthMm = depth }));

        Assert.Equal(errors, Of(r, ThrowingRuleIds.CatchVolumes).Count(d => d.Value.Contains("extents", StringComparison.Ordinal)));
    }

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(2000, 1000, 0)]   // bottom exactly 0
    [InlineData(2001, 1000, 1)]   // bottom -0.5, an odd height is exact
    [InlineData(2002, 1000, 1)]   // bottom -1
    [InlineData(800, 400, 0)]
    [InlineData(800, 399, 1)]
    public void Thr06_BottomMustBeAtOrAboveTheMailboxBase(int height, int centre, int errors)
    {
        ValidationResult r = Fx.V(Fx.Volume(Fx.BaseVolume with { HeightMm = height, CentreHeightMm = centre }));

        Assert.Equal(errors, Of(r, ThrowingRuleIds.CatchVolumes).Length);
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr06_AFindingAddressesTheDestinationType()
    {
        ValidationResult r = Fx.V(Fx.Volume(Fx.BaseVolume with { YawMilliDeg = 45_000 }));

        ContentDiagnostic d = Assert.Single(Of(r, ThrowingRuleIds.CatchVolumes));
        Assert.Equal(new DefRef(DefType.DestinationType, "house"), d.Def);
        Assert.Contains("destination_types/house", d.Location.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ CV-THR-08

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr08_AWeightClassOutsideTheEnum_IsOneFindingPerDef()
    {
        ContentDiagnostic d = Assert.Single(Of(Fx.V(Fx.Plank((WeightClass)0, true)), ThrowingRuleIds.WeightClasses));
        Assert.Equal(new DefRef(DefType.Item, "plank"), d.Def);
        Assert.Single(Of(Fx.V(Fx.Plank((WeightClass)9, true)), ThrowingRuleIds.WeightClasses));
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr08_ABulkItemMustNotBeHandCarried_AndEveryOtherClassMay()
    {
        AssertOnlyOne(Fx.V(Fx.Plank(WeightClass.Bulk, true)), ThrowingRuleIds.WeightClasses);
        AssertClean(Fx.V(Fx.Plank(WeightClass.Bulk, false)));
        foreach (WeightClass wc in new[] { WeightClass.Light, WeightClass.Medium, WeightClass.Heavy })
        {
            AssertClean(Fx.V(Fx.Plank(wc, true)));
        }
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr08_ABulkMailKindMustNotBeHandCarried()
    {
        // The cursor admits only materials, so CV-GRD-02 stays quiet and the mail kind is judged by CV-THR-08 alone.
        ContentDraft draft = ContentFixtures.MinimalValid()
            .Replace(ContentFixtures.Cursor with { HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Material) });

        ContentDiagnostic d = Assert.Single(Of(Fx.V(draft.Replace(ContentFixtures.Letter with { WeightClass = WeightClass.Bulk })), ThrowingRuleIds.WeightClasses));
        Assert.Equal(new DefRef(DefType.MailKind, "letter"), d.Def);
        AssertClean(Fx.V(draft.Replace(ContentFixtures.Letter with { WeightClass = WeightClass.Bulk, CarryByHand = false })));
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr08_CurrentBehaviour_OnlyAFailedMailKindTableSkipsTheRule_PendingCddSchema3()
    {
        // Pins the CURRENT behaviour, not the spec'd one: CDD-SCHEMA-3 says a failed item table skips CV-THR-08, but the item table is
        // read and not declared (TD-059, as CV-GRD-02/04; see TD-069(2)), so only a failed mail-kind table skips the rule today.
        // When CDD-SCHEMA-3 is implemented, flip the first assertion.
        ContentValidationInput itemsFailed = Fx.Plank(WeightClass.Bulk, true).Input(
            ImmutableArray.Create(new FailedInput(ContentInputKeys.Items, DefRef.None, "items/")));
        Assert.Single(Of(ContentValidationRunner.Run(RuleRegistry.CreateDefault(), itemsFailed), ThrowingRuleIds.WeightClasses));

        ContentValidationInput input = Fx.Plank(WeightClass.Bulk, true).Input(
            ImmutableArray.Create(new FailedInput(ContentInputKeys.MailKinds, DefRef.None, "mail_kinds/")));

        ValidationResult r = ContentValidationRunner.Run(RuleRegistry.CreateDefault(), input);

        Assert.Empty(Of(r, ThrowingRuleIds.WeightClasses));
        Assert.Contains(r.Skipped, s => s.RuleId == ThrowingRuleIds.WeightClasses);
    }

    // ------------------------------------------------------------ CV-THR-09 and 10 (cross-owner)

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(299, 1)]
    [InlineData(300, 0)]
    [InlineData(301, 0)]
    [InlineData(0, 1)]
    public void Thr09_FwdOffMustClearTheCapsuleRadius(int fwd, int errors)
    {
        Assert.Equal(errors, Of(With("throw.fwdOffMm", fwd), ThrowingRuleIds.FwdOffClearsCapsule).Length);
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr09_TracksThePlayersCapsuleRadius()
    {
        AssertClean(With("player.capsuleRadiusMm", 250));   // fwdOff 300 is now ample
        AssertOnlyOne(With("player.capsuleRadiusMm", 301), ThrowingRuleIds.FwdOffClearsCapsule);
    }

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(1, 0)]
    [InlineData(1400, 0)]
    [InlineData(1401, 1)]
    [InlineData(0, 1)]   // not above 0 (the hard range admits 0)
    public void Thr10_HandHeightMustBeAboveZeroAndWithinTheCapsule(int hand, int errors)
    {
        Assert.Equal(errors, Of(With("throw.handHeightMm", hand), ThrowingRuleIds.HandHeightInCapsule).Length);
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr10_TracksThePlayersCapsuleHeight()
    {
        AssertClean(With("throw.handHeightMm", 1500, "player.capsuleHeightMm", 1500));
        AssertOnlyOne(With("player.capsuleHeightMm", 1199), ThrowingRuleIds.HandHeightInCapsule);
    }

    [Fact]
    [Trait("Id", "CDD-LOAD-3")]
    public void CrossOwnerRules_WithAFailedOwnerFile_AreSkippedAndCounted_NotReported()
    {
        // Both files carry a violation, but the file that failed stage 3 is not trusted: the rule is skipped and counted.
        ContentDraft draft = ContentFixtures.MinimalValid().WithBalance("throw.fwdOffMm", 299).WithBalance("throw.handHeightMm", 1401);

        ValidationResult throwFailed = ContentValidationRunner.Run(RuleRegistry.CreateDefault(),
            draft.Input(ImmutableArray.Create(new FailedInput(ContentInputKeys.ThrowBalance, DefRef.None, "balance/throw.jsonc"))));
        ValidationResult playerFailed = ContentValidationRunner.Run(RuleRegistry.CreateDefault(),
            draft.Input(ImmutableArray.Create(new FailedInput(ContentInputKeys.PlayerBalance, DefRef.None, "balance/player.jsonc"))));
        ValidationResult healthy = ContentValidationRunner.Run(RuleRegistry.CreateDefault(), draft.Input());

        Assert.Empty(throwFailed.Diagnostics);
        Assert.Empty(playerFailed.Diagnostics);
        Assert.Equal(2, healthy.ErrorCount);
        string[] cross = { ThrowingRuleIds.FwdOffClearsCapsule, ThrowingRuleIds.HandHeightInCapsule };
        Assert.Equal(cross, playerFailed.Skipped.Select(s => s.RuleId).Where(id => id.StartsWith("CV-THR-", StringComparison.Ordinal)).ToArray());
        string[] throwSkips = throwFailed.Skipped.Select(s => s.RuleId).Where(id => id.StartsWith("CV-THR-", StringComparison.Ordinal)).ToArray();
        Assert.Contains(cross[0], throwSkips);
        Assert.Contains(cross[1], throwSkips);
        Assert.DoesNotContain(ThrowingRuleIds.CatchVolumes, throwSkips);   // reads destination types, not the throw file
        // Every live rule that declares the throw file is skipped; the two that read other tables (06 destinations, 08 mail kinds) are not.
        string[] declaringThrow = RuleRegistry.CreateDefault().Rules
            .Where(r => r.Status == RuleStatus.Active && r.Id.StartsWith("CV-THR-", StringComparison.Ordinal) && r.Inputs.Contains(ContentInputKeys.ThrowBalance))
            .Select(r => r.Id).ToArray();
        Assert.Equal(declaringThrow.OrderBy(x => x, StringComparer.Ordinal).ToArray(), throwSkips.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(_liveIds.Length - 2, throwSkips.Length);
        Assert.Equal(0, healthy.Skipped.Count(s => s.RuleId.StartsWith("CV-THR-", StringComparison.Ordinal)));
    }

    // ------------------------------------------------------------ CV-THR-11

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(8000, 0)]   // 70 + 8 = 78
    [InlineData(8001, 1)]
    [InlineData(9000, 1)]
    [InlineData(0, 0)]
    [InlineData(6000, 0)]   // the default
    public void Thr11_SeventyDegreesPlusLoft_AtMost78Degrees(int loft, int errors)
    {
        Assert.Equal(errors, Of(With("throw.loftDeg", loft), ThrowingRuleIds.LoftWithinHostBound).Length);
    }

    // ------------------------------------------------------------ CV-THR-12 (F3)

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(500, 0)]   // the normative edges
    [InlineData(100, 0)]
    [InlineData(501, 1)]
    [InlineData(450, 1)]
    [InlineData(150, 1)]
    [InlineData(0, 0)]
    public void Thr12_CatchTapMs_MustBeWholeTicks(int tap, int errors)
    {
        ValidationResult r = With("throw.catchTapMs", tap);

        Assert.Equal(errors, Of(r, ThrowingRuleIds.CatchWholeTicks).Length);
        Assert.Equal(errors, r.ErrorCount);
    }

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(500, 0)]
    [InlineData(100, 0)]
    [InlineData(450, 1)]
    [InlineData(501, 1)]
    public void Thr12_CatchPredictMs_MustBeWholeTicks(int predict, int errors)
    {
        Assert.Equal(errors, Of(With("throw.catchPredictMs", predict), ThrowingRuleIds.CatchWholeTicks).Length);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Thr12_BothFieldsBad_ReportsOneFindingEach()
    {
        ValidationResult r = With("throw.catchTapMs", 501, "throw.catchPredictMs", 450);

        Assert.Equal(2, Of(r, ThrowingRuleIds.CatchWholeTicks).Length);
    }

    // ------------------------------------------------------------ CV-THR-13

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(3000, 0)]   // 90 ticks x 1000 / 30
    [InlineData(3100, 1)]
    [InlineData(3001, 1)]
    public void Thr13_PredictMs_AtMostTheFlightCap(int predict, int errors)
    {
        Assert.Equal(errors, Of(With("throw.catchPredictMs", predict), ThrowingRuleIds.CatchPredictWithinFlight).Length);
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr13_TracksMaxFlightTicks()
    {
        AssertClean(With("throw.maxFlightTicks", 30, "throw.catchPredictMs", 1000));   // 30 ticks = 1000 ms
        AssertOnlyOne(With("throw.maxFlightTicks", 30, "throw.catchPredictMs", 1100), ThrowingRuleIds.CatchPredictWithinFlight);
    }

    // ------------------------------------------------------------ CV-THR-15 and 16

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(5, 0)]
    public void Thr15_DamagePerPoint_AtLeastOne(int damage, int errors)
    {
        Assert.Equal(errors, Of(With("throw.damagePerPoint", damage), ThrowingRuleIds.DamagePerPoint).Length);
    }

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData("throw.throwCooldownTicks", 0, 1)]
    [InlineData("throw.throwCooldownTicks", 1, 0)]
    [InlineData("throw.throwBurstTicks", 0, 1)]
    [InlineData("throw.throwBurstTicks", 1, 0)]
    public void Thr16_CooldownAndBurst_AtLeastOneTick(string key, int value, int errors)
    {
        ContentDiagnostic[] found = Of(With(key, value), ThrowingRuleIds.CooldownAndBurst);

        Assert.Equal(errors, found.Length);
        if (errors == 1)
        {
            Assert.Contains(key, found[0].Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr16_BothZero_ReportsOneFindingPerKey()
    {
        Assert.Equal(2, Of(With("throw.throwCooldownTicks", 0, "throw.throwBurstTicks", 0), ThrowingRuleIds.CooldownAndBurst).Length);
    }

    // ------------------------------------------------------------ CV-THR-18 and the CV-PLR-06 interplay

    [Theory]
    [Trait("Id", "TC-CV-1")]
    [InlineData(2200, 0)]   // 1 500 + 2 200 = 3 700 passes
    [InlineData(2201, 1)]   // 3 701 errors
    [InlineData(0, 0)]
    public void Thr18_CatchReach_AtMost3700(int offset, int errors)
    {
        ValidationResult r = With("throw.catchHistoryMaxOffsetMm", offset);

        Assert.Equal(errors, Of(r, ThrowingRuleIds.CatchReach).Length);
        Assert.Equal(errors, r.ErrorCount);
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void Thr18_GddExample_Radius2200WithOffset1600_Errors()
    {
        AssertOnlyOne(With("throw.catchRadiusMm", 2200, "throw.catchHistoryMaxOffsetMm", 1600), ThrowingRuleIds.CatchReach);   // 3 800
        AssertClean(With("throw.catchRadiusMm", 2200, "throw.catchHistoryMaxOffsetMm", 1500));   // 3 700 passes
    }

    [Fact]
    [Trait("Id", "PCM-F8-3")]
    public void Plr06Fixtures_StillYieldExactlyOneDiagnostic_NowThatThr18Exists()
    {
        // TD-064(5): a catch radius of 2 500 with the default 1 500 history offset would also exceed 3 700. The fixture zeroes the offset.
        ContentDiagnostic d = Assert.Single(PlayerRuleFixtures.WithCatchRadius(2500).Diagnostics);
        Assert.Equal(PlayerRuleIds.CatchBelowRange, d.RuleId);
        AssertClean(PlayerRuleFixtures.WithCatchRadius(2499));
        // With the default offset the same radius trips both, which is the interplay the fixture avoids.
        Assert.Equal(2, With("throw.catchRadiusMm", 2500).Diagnostics.Length);
    }

    // ------------------------------------------------------------ shipped content

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void CreateM1a_PassesEveryThrowingRule_WithNoDiagnosticsAndNoSkips()
    {
        ContentBuildResult r = BuiltInContent.BuildM1a(RuleRegistry.CreateDefault());

        Assert.Empty(r.Validation.Diagnostics);
        Assert.Equal(0, r.Validation.SkippedCount);
        Assert.NotNull(r.Set);
        Assert.NotNull(BuiltInContent.CreateM1a());
    }

    [Fact]
    [Trait("Id", "TC-CV-1")]
    public void MinimalValid_PassesEveryThrowingRule()
    {
        AssertClean(Fx.V(ContentFixtures.MinimalValid()));
    }
}
