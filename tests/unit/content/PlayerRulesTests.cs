using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using PlayerRules = PerformativeMail.Sim.Content.Rules.Player;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 011 (TR-content-022, M-1a subset; GDD PCM-F8-3, CDD-RULE-2): the Player Character rules
/// CV-PLR-01, 02, 04, 06, 08, 09, 10 and 11 through the production pipeline, the deferred ids, the alias
/// <c>Throwing rule 14</c>, the cross-owner skip, and the shipped content. The per-clause fixtures live in
/// <see cref="PlayerRuleFixtures"/> and are checked by the meta-test in <c>RuleRegistryTests</c>.
/// </summary>
public class PlayerRulesTests
{
    private static ContentDiagnostic[] Of(ValidationResult r, string id) => r.Diagnostics.Where(d => d.RuleId == id).ToArray();

    private static ValidationResult With(string key, long value) => PlayerRuleFixtures.With(key, value);

    private static ValidationResult With(string k1, long v1, string k2, long v2) =>
        ContentFixtures.MinimalValid().WithBalance(k1, v1).WithBalance(k2, v2).Build().Validation;

    private static void AssertOnlyOne(ValidationResult r, string id)
    {
        ContentDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal(id, d.RuleId);
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.False(r.CanBuild);
    }

    private static string[] PlrSkips(ValidationResult r) =>
        r.Skipped.Select(s => s.RuleId).Where(id => id.StartsWith("CV-PLR-", StringComparison.Ordinal)).ToArray();

    private static void AssertClean(ValidationResult r)
    {
        Assert.Empty(r.Diagnostics);
        Assert.True(r.CanBuild);
    }

    // ------------------------------------------------------------ the rows

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Rows_AreRegisteredWithTheCatalogueSeverityStageAndPcAlias()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();
        (string Id, string Alias, ValidationStage Stage)[] live =
        {
            (PlayerRuleIds.RangeWithinLeash, "PC rule 1", ValidationStage.WholeSet),
            (PlayerRuleIds.SwimHysteresis, "PC rule 2", ValidationStage.References),
            (PlayerRuleIds.MinHold, "PC rule 4", ValidationStage.References),
            (PlayerRuleIds.CatchBelowRange, "PC rule 6", ValidationStage.WholeSet),
            (PlayerRuleIds.HaulOut, "PC rule 8", ValidationStage.References),
            (PlayerRuleIds.IncumbentSlack, "PC rule 9", ValidationStage.References),
            (PlayerRuleIds.DivisionSafety, "PC rule 10", ValidationStage.References),
            (PlayerRuleIds.PositionStale, "PC rule 11", ValidationStage.References),
        };

        foreach ((string id, string alias, ValidationStage stage) in live)
        {
            RuleSpec rule = Assert.IsAssignableFrom<RuleSpec>(registry.Find(id));
            Assert.Equal(RuleSeverity.Error, rule.Severity);
            Assert.Equal(stage, rule.Stage);
            Assert.Equal("player-character-movement", rule.Owner);
            Assert.Equal(RuleStatus.Active, rule.Status);
            Assert.Equal(alias, rule.Alias);
            Assert.True(registry.TryResolve(alias, out RuleSpec? byAlias));
            Assert.Equal(id, byAlias!.Id);
        }

        Assert.Equal(13, PlayerRules.Rules.Length);
        Assert.Equal("CV-PLR-01 (PC rule 1)", registry.FormatId(PlayerRuleIds.RangeWithinLeash));
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void CrossOwnerRules_DeclareBothOwnerFiles_AtStage5()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();

        RuleSpec leash = registry.Find(PlayerRuleIds.RangeWithinLeash)!;
        Assert.Equal(new[] { ContentInputKeys.PlayerBalance, ContentInputKeys.InventoryBalance }, leash.Inputs.ToArray());
        RuleSpec catchRule = registry.Find(PlayerRuleIds.CatchBelowRange)!;
        Assert.Equal(new[] { ContentInputKeys.PlayerBalance, ContentInputKeys.ThrowBalance }, catchRule.Inputs.ToArray());
        Assert.Equal(ValidationStage.WholeSet, leash.Stage);
        Assert.Equal(ValidationStage.WholeSet, catchRule.Stage);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Plr06_IsReachableByThrowingRule14_AndTheRetiredThr14PointsAtIt()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();

        Assert.True(registry.TryResolve("Throwing rule 14", out RuleSpec? byAlias));
        Assert.Equal(PlayerRuleIds.CatchBelowRange, byAlias!.Id);
        Assert.True(registry.TryResolve("PC rule 6", out RuleSpec? byPc));
        Assert.Equal(PlayerRuleIds.CatchBelowRange, byPc!.Id);
        Assert.True(registry.TryResolve("THROW-CV-14", out RuleSpec? retired));
        Assert.Equal(RuleStatus.Retired, retired!.Status);
        Assert.Equal(PlayerRuleIds.CatchBelowRange, retired.RetiredInto);
        Assert.Empty(registry.UnresolvedRetirements());
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void DeferredRows_AreRegisteredWithAReason_NeverRun_AndHaveNoFixtureObligation()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();
        string[] deferred =
        {
            PlayerRuleIds.BuffCap, PlayerRuleIds.RespawnSet, PlayerRuleIds.PushCap, PlayerRuleIds.SupportSpeedCap, PlayerRuleIds.UnknownVerb,
        };

        foreach (string id in deferred)
        {
            RuleSpec rule = Assert.IsAssignableFrom<RuleSpec>(registry.Find(id));
            Assert.Equal(RuleStatus.Deferred, rule.Status);
            Assert.False(rule.HasCheck);
            Assert.StartsWith("inputs not present at M-1a", rule.DeferredReason, StringComparison.Ordinal);
        }

        // The fixture meta-test skips them: no problem is reported for the production registry with only the real fixtures.
        Assert.Empty(FixtureChecker.Check(registry, RuleFixtureCatalogue.Real(), RuleFixtureCatalogue.ForwardReferences));
        // A fixture that names a deferred rule is an orphan (it cannot trip anything).
        var orphan = new RuleFixture(PlayerRuleIds.PushCap, "main", FixtureKind.Violation, () => With("player.pushCapMm", 1), ImmutableArray<string>.Empty);
        Assert.Contains(FixtureChecker.Check(registry, ImmutableArray.Create(orphan)), p => p.Kind == FixtureProblemKind.UnknownRule);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Defer_WithABlankReason_IsRejected_AndAReasonedRowIsAccepted()
    {
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(
            RuleSpec.Defer("CV-AAA-01", "o", "", RuleSeverity.Error, ValidationStage.WholeSet, " "))));
        Assert.NotNull(RuleRegistry.Compose(ImmutableArray.Create(
            RuleSpec.Defer("CV-AAA-01", "o", "", RuleSeverity.Error, ValidationStage.WholeSet, "inputs not present"))).Find("CV-AAA-01"));
    }

    // ------------------------------------------------------------ CV-PLR-01

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData(2500, 0)]   // 2 500 + 500 = 3 000 passes
    [InlineData(2501, 1)]
    public void Plr01_RangePlusSlack_AgainstTheExternalLeash(int range, int errors)
    {
        ValidationResult r = With("player.rangeMm", range);

        Assert.Equal(errors, Of(r, PlayerRuleIds.RangeWithinLeash).Length);
        Assert.Equal(errors, r.ErrorCount);
    }

    [Fact]
    [Trait("Id", "PCM-F8-3")]
    public void Plr01_ReadsGridsLeashKey_SoRaisingItAdmitsALongerRange_AndLoweringItErrors()
    {
        AssertClean(With("player.rangeMm", 3500, "inventory.externalLeashMeters", 4000));   // 3 500 + 500 = 4 000
        AssertOnlyOne(With("inventory.externalLeashMeters", 2999), PlayerRuleIds.RangeWithinLeash);
        AssertClean(With("player.rangeSlackMm", 400, "player.incumbentSlackMm", 300));   // slack 400: 2 900 <= 3 000
    }

    // ------------------------------------------------------------ CV-PLR-02

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData(800, 0)]   // swimEnter 900 - 100
    [InlineData(801, 1)]   // swimEnter - 99
    [InlineData(900, 1)]
    public void Plr02_SwimExit_MustBeAtLeast100BelowEnter(int exit, int errors)
    {
        Assert.Equal(errors, Of(With("player.swimExitMm", exit), PlayerRuleIds.SwimHysteresis).Length);
    }

    [Fact]
    [Trait("Id", "PCM-F8-3")]
    public void Plr02_TracksTheEnterDepth()
    {
        AssertClean(With("player.swimEnterMm", 1000, "player.swimExitMm", 900));
        AssertOnlyOne(With("player.swimEnterMm", 1000, "player.swimExitMm", 901), PlayerRuleIds.SwimHysteresis);
    }

    // ------------------------------------------------------------ CV-PLR-04

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData(6, 0)]
    [InlineData(5, 1)]
    [InlineData(0, 1)]
    [InlineData(9, 0)]
    public void Plr04_MinHoldTicks_AtLeast6(int ticks, int errors)
    {
        Assert.Equal(errors, Of(With("player.minHoldTicks", ticks), PlayerRuleIds.MinHold).Length);
    }

    // ------------------------------------------------------------ CV-PLR-06

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData(2499, 0)]   // range - 1 passes
    [InlineData(2500, 1)]   // catch radius = range errors
    [InlineData(3000, 1)]
    public void Plr06_CatchRadius_MustBeBelowRange(int catchRadius, int errors)
    {
        ValidationResult r = PlayerRuleFixtures.WithCatchRadius(catchRadius);

        Assert.Equal(errors, Of(r, PlayerRuleIds.CatchBelowRange).Length);
        Assert.Equal(errors, r.ErrorCount);
    }

    [Fact]
    [Trait("Id", "PCM-F8-3")]
    public void Plr06_MessageNamesBothKeys_AndPrintsBothSpellings()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();
        ContentDiagnostic d = Assert.Single(Of(PlayerRuleFixtures.WithCatchRadius(2500), PlayerRuleIds.CatchBelowRange));

        Assert.Contains("throw.catchRadiusMm", d.Value, StringComparison.Ordinal);
        Assert.Contains("player.rangeMm", d.Value, StringComparison.Ordinal);
        Assert.Contains("CV-PLR-06 (PC rule 6)", d.Format(registry), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ CV-PLR-08

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData(400, 0)]   // haulOut = stepUp passes
    [InlineData(399, 1)]
    [InlineData(1200, 0)]
    public void Plr08_HaulOut_AtLeastStepUp(int haulOut, int errors)
    {
        Assert.Equal(errors, Of(With("player.haulOutMm", haulOut), PlayerRuleIds.HaulOut).Length);
    }

    // ------------------------------------------------------------ CV-PLR-09

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData(400, 0)]   // 400 + 100 = rangeSlack 500 passes
    [InlineData(401, 1)]
    [InlineData(0, 0)]
    public void Plr09_IncumbentSlackPlus100_AtMostRangeSlack(int incumbent, int errors)
    {
        Assert.Equal(errors, Of(With("player.incumbentSlackMm", incumbent), PlayerRuleIds.IncumbentSlack).Length);
    }

    // ------------------------------------------------------------ CV-PLR-10

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData("player.ticksAccel")]
    [InlineData("player.ticksDecel")]
    [InlineData("player.airControlPm")]
    public void Plr10_EachDivisorOfZero_Errors_AndOneIsTheEdge(string key)
    {
        ContentDiagnostic d = Assert.Single(Of(With(key, 0), PlayerRuleIds.DivisionSafety));
        Assert.Contains(key, d.Value, StringComparison.Ordinal);
        AssertClean(With(key, 1));
    }

    [Fact]
    [Trait("Id", "PCM-F8-3")]
    public void Plr10_RangeMmOfZero_Errors_AndAlsoTripsPlr06()
    {
        // rangeMm 0 is a divisor and also not above the catch radius (1 500): two reviewed findings, not one.
        ValidationResult r = With("player.rangeMm", 0);

        Assert.Single(Of(r, PlayerRuleIds.DivisionSafety));
        Assert.Single(Of(r, PlayerRuleIds.CatchBelowRange));
        Assert.Equal(2, r.ErrorCount);
        AssertClean(With("player.rangeMm", 1600, "throw.catchRadiusMm", 1500));
    }

    [Fact]
    [Trait("Id", "PCM-F8-3")]
    public void Plr10_SeveralBadFields_GiveOneFindingEach()
    {
        ValidationResult r = With("player.ticksAccel", 0, "player.ticksDecel", 0);

        Assert.Equal(2, Of(r, PlayerRuleIds.DivisionSafety).Length);
    }

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData(24_000, 0)]
    [InlineData(24_001, 1)]
    [InlineData(24_029, 1)]
    [InlineData(24_030, 0)]
    [InlineData(30, 0)]
    public void Plr10_GravityMultipleOf30(int g, int errors)
    {
        Assert.Equal(errors, Of(With("player.g", g), PlayerRuleIds.DivisionSafety).Length);
    }

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData(0)]
    [InlineData(-30)]
    public void Plr10_GravityOfZeroOrBelow_IsRefusedByTheBalanceHardRangeBeforeTheRuleRuns(int g)
    {
        // player.g has hard range 1..1 000 000 (CV-GEN-13), so the build stops at the balance table: the rule never sees it.
        Assert.Throws<InvalidOperationException>(() => With("player.g", g));
    }

    // ------------------------------------------------------------ CV-PLR-11

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData(19, 0)]
    [InlineData(18, 1)]
    [InlineData(0, 1)]
    [InlineData(30, 0)]
    public void Plr11_PositionStaleTicks_AtLeast19(int ticks, int errors)
    {
        Assert.Equal(errors, Of(With("player.positionStaleTicks", ticks), PlayerRuleIds.PositionStale).Length);
    }

    // ------------------------------------------------------------ skipping, severity, shipped content

    [Fact]
    [Trait("Id", "CDD-LOAD-3")]
    public void CrossOwnerRule_WithAFailedOwnerFile_IsSkippedAndCounted_WhileSingleOwnerRulesStillRun()
    {
        // Violates CV-PLR-01 (range 2 501) and CV-PLR-04 (hold 5). The inventory file failed: PLR-01 and PLR-06 are skipped, PLR-04 runs.
        ContentDraft draft = ContentFixtures.MinimalValid().WithBalance("player.rangeMm", 2501).WithBalance("player.minHoldTicks", 5);
        ContentValidationInput input = draft.Input(ImmutableArray.Create(
            new FailedInput(ContentInputKeys.InventoryBalance, DefRef.None, "balance/inventory.jsonc")));

        ValidationResult r = ContentValidationRunner.Run(RuleRegistry.CreateDefault(), input);

        Assert.Empty(Of(r, PlayerRuleIds.RangeWithinLeash));
        Assert.Single(Of(r, PlayerRuleIds.MinHold));
        Assert.Equal(new[] { PlayerRuleIds.RangeWithinLeash }, PlrSkips(r));
    }

    [Fact]
    [Trait("Id", "CDD-LOAD-3")]
    public void CatchRule_WithAFailedThrowFile_IsSkipped_AndAFailedPlayerFileSkipsEveryPlayerRuleThatReadsIt()
    {
        ContentValidationInput throwFailed = ContentFixtures.MinimalValid().WithBalance("throw.catchRadiusMm", 2500).Input(
            ImmutableArray.Create(new FailedInput(ContentInputKeys.ThrowBalance, DefRef.None, "balance/throw.jsonc")));
        ValidationResult a = ContentValidationRunner.Run(RuleRegistry.CreateDefault(), throwFailed);
        Assert.Empty(a.Diagnostics);
        Assert.Equal(new[] { PlayerRuleIds.CatchBelowRange }, PlrSkips(a));

        ContentValidationInput playerFailed = ContentFixtures.MinimalValid().WithBalance("player.minHoldTicks", 5).Input(
            ImmutableArray.Create(new FailedInput(ContentInputKeys.PlayerBalance, DefRef.None, "balance/player.jsonc")));
        ValidationResult b = ContentValidationRunner.Run(RuleRegistry.CreateDefault(), playerFailed);
        Assert.Empty(b.Diagnostics);
        Assert.Equal(8, PlrSkips(b).Length);   // the eight live rules all read the player file
    }

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData(PlayerRuleIds.MinHold)]       // a live Error rule
    [InlineData("PC rule 4")]                 // its alias is not an id, so it is refused too
    [InlineData(PlayerRuleIds.UnknownVerb)]   // a deferred rule
    public void Errors_AreNotSuppressible_ALivePlayerRuleOrADeferredIdNamedInSuppressIsRejected(string target)
    {
        ContentDraft draft = ContentFixtures.MinimalValid().WithBalance("player.minHoldTicks", 5);
        var suppress = ImmutableArray.Create(TestInput.Suppress(target, "intended"));
        var input = new ContentValidationInput(draft.Input().Candidate, default, default, suppress);

        ValidationResult r = ContentValidationRunner.Run(RuleRegistry.CreateDefault(), input);

        Assert.Single(Of(r, GeneralRuleIds.SuppressBadTarget));
        Assert.Single(Of(r, PlayerRuleIds.MinHold));   // the Error still reports
        Assert.Equal(0, r.SuppressedCount);
        Assert.False(r.CanBuild);
    }

    [Theory]
    [Trait("Id", "PCM-F8-3")]
    [InlineData("player.ticksAccel")]
    [InlineData("player.ticksDecel")]
    [InlineData("player.airControlPm")]
    [InlineData("player.rangeMm")]
    public void Plr10_NegativeDivisors_AreRefusedByTheBalanceHardRange_BeforeTheRuleRuns(string key)
    {
        // Hard ranges are 0..max (CV-GEN-13), so a negative value stops the build at the balance table; zero is the rule's case.
        Assert.Throws<InvalidOperationException>(() => With(key, -1));
    }

    [Fact]
    [Trait("Id", "PCM-F8-3")]
    public void CreateM1a_PassesEveryPlayerRule_WithNoDiagnosticsAndNoSkips()
    {
        ContentBuildResult r = BuiltInContent.BuildM1a(RuleRegistry.CreateDefault());

        Assert.Empty(r.Validation.Diagnostics);
        Assert.Equal(0, r.Validation.SkippedCount);
        Assert.NotNull(r.Set);
        Assert.NotNull(BuiltInContent.CreateM1a());
    }
}
