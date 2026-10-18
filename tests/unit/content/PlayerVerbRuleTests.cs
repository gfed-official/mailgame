using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers player-character story 012 AC 2 (PCM-P10-1, input-controls H-R4-5): CV-PLR-13 errors when a content definition names
/// a verb outside the closed <see cref="Verb"/> catalogue or declares other than exactly one verb for one interactable, a single
/// known verb passes, and <c>BuiltInContent.CreateM1a()</c> passes with no declarations. The rule reads
/// <see cref="ContentValidationInput.VerbDeclarations"/> (no def type carries a verb field at M-1a), so every case here is a
/// fixture declaration on the <c>house</c> destination type. The per-clause fixtures live in <see cref="PlayerRuleFixtures"/>.
/// </summary>
public class PlayerVerbRuleTests
{
    private static ContentDiagnostic[] Of(ValidationResult r) => r.Diagnostics.Where(d => d.RuleId == PlayerRuleIds.UnknownVerb).ToArray();

    private static ValidationResult WithVerbs(params string[] verbs) => PlayerRuleFixtures.WithVerbs(verbs);

    // ------------------------------------------------------------ the row

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    [Trait("Id", "H-R4-5")]
    public void Row_IsActive_WithTwoClauses_TheVerbsInput_AndTheHR45Alias()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();

        RuleSpec rule = Assert.IsAssignableFrom<RuleSpec>(registry.Find(PlayerRuleIds.UnknownVerb));

        Assert.Equal(RuleStatus.Active, rule.Status);
        Assert.True(rule.HasCheck);
        Assert.Equal(RuleSeverity.Error, rule.Severity);
        Assert.Equal(ValidationStage.References, rule.Stage);
        Assert.Equal("PCM-P10-1", rule.Alias);
        Assert.Equal(new[] { ContentInputKeys.Verbs }, rule.Inputs);
        Assert.Equal(new[] { "known_verb", "one_verb_per_interactable" }, rule.Clauses.Select(c => c.Name));
        Assert.True(registry.TryResolve("PCM-P10-1", out RuleSpec? byAlias));
        Assert.Equal(PlayerRuleIds.UnknownVerb, byAlias!.Id);
        Assert.True(registry.TryResolve("H-R4-5", out RuleSpec? byInputControls));
        Assert.Equal(PlayerRuleIds.UnknownVerb, byInputControls!.Id);
    }

    // ------------------------------------------------------------ known_verb

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void UnknownVerb_Revive_IsOneError_NamingTheSpelling_TheDef_AndTheVerbsInput()
    {
        ValidationResult r = WithVerbs("revive");

        ContentDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal(PlayerRuleIds.UnknownVerb, d.RuleId);
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.Equal("revive", d.Value);
        Assert.Equal(ContentInputKeys.Verbs, d.InputKey);
        Assert.Equal(new DefRef(DefType.DestinationType, "house"), d.Def);
        Assert.Equal(ContentValidationInput.LocationOf("destination_types", "house"), d.Location);
        Assert.Contains("deliver", d.Allowed, StringComparison.Ordinal);
        Assert.Contains("deconstruct", d.Allowed, StringComparison.Ordinal);
        Assert.False(r.CanBuild);
    }

    [Theory]
    [Trait("Id", "PCM-P10-1")]
    [InlineData("Deliver")]      // spellings are snake_case and compared byte for byte: no case fallback
    [InlineData("pickup")]       // the catalogue spelling is pick_up
    [InlineData("")]             // an empty spelling is not a verb
    public void UnknownVerb_NearMisses_AreRefused(string spelling)
    {
        ValidationResult r = WithVerbs(spelling);

        ContentDiagnostic d = Assert.Single(Of(r));
        Assert.Equal(spelling, d.Value);
    }

    [Theory]
    [Trait("Id", "PCM-P10-1")]
    [InlineData("deliver")]
    [InlineData("open")]
    [InlineData("pick_up")]
    [InlineData("mount")]
    [InlineData("operate")]
    [InlineData("configure")]
    [InlineData("shop")]
    [InlineData("announce")]
    [InlineData("dismount")]
    [InlineData("catch")]
    [InlineData("push")]
    [InlineData("rebuild")]
    [InlineData("deconstruct")]
    public void SingleKnownVerb_EveryCatalogueSpelling_Passes(string spelling)
    {
        Assert.True(VerbSpelling.TryParse(System.Text.Encoding.UTF8.GetBytes(spelling), out _));   // the theory covers the whole catalogue

        ValidationResult r = WithVerbs(spelling);

        Assert.Empty(r.Diagnostics);
        Assert.True(r.CanBuild);
    }

    // ------------------------------------------------------------ one_verb_per_interactable

    [Fact]
    [Trait("Id", "H-R4-5")]
    public void TwoKnownVerbs_OnOneInteractable_IsOneError_ForThePair_AndNoKnownVerbFinding()
    {
        ValidationResult r = WithVerbs("deliver", "open");

        ContentDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal(PlayerRuleIds.UnknownVerb, d.RuleId);
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.Equal("2 verbs: deliver, open", d.Value);
        Assert.Equal("exactly one verb per interactable", d.Allowed);
        Assert.Equal(ContentInputKeys.Verbs, d.InputKey);
        Assert.False(r.CanBuild);
    }

    [Fact]
    [Trait("Id", "H-R4-5")]
    public void ZeroVerbsDeclared_IsOneError_OfTheOneVerbClause()
    {
        ValidationResult r = WithVerbs();

        ContentDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal(PlayerRuleIds.UnknownVerb, d.RuleId);
        Assert.Equal("no verb", d.Value);
        Assert.Equal("exactly one verb per interactable", d.Allowed);
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    [Trait("Id", "H-R4-5")]
    public void TwoVerbs_OneUnknown_ReportsBothClauses_AsTwoFindings()
    {
        ValidationResult r = WithVerbs("deliver", "revive");

        ContentDiagnostic[] own = Of(r);
        Assert.Equal(2, own.Length);
        Assert.Single(own, d => d.Value == "2 verbs: deliver, revive");
        Assert.Single(own, d => d.Value == "revive");
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void TwoDeclarations_AreCheckedIndependently()
    {
        ValidationResult r = ContentFixtures.MinimalValid().WithVerbs("house", "deliver").WithVerbs("house", "revive").Build().Validation;

        ContentDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal("revive", d.Value);
    }

    // ------------------------------------------------------------ the rule body, called directly

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void RuleBody_OverTheAdapter_ReadsTheDeclarations_AndIsSkippedWhenTheVerbsInputFailed()
    {
        ContentValidationInput input = ContentFixtures.MinimalValid().WithVerbs("house", "revive").Input();
        Assert.Single(input.VerbDeclarations);

        ValidationResult direct = ContentValidationRunner.Run(RuleRegistry.CreateDefault(), input);
        Assert.Single(Of(direct));

        ContentValidationInput failed = ContentFixtures.MinimalValid().WithVerbs("house", "revive").Input(
            ImmutableArray.Create(new FailedInput(ContentInputKeys.Verbs, DefRef.None, "destination_types/house.jsonc")));
        ValidationResult skipped = ContentValidationRunner.Run(RuleRegistry.CreateDefault(), failed);
        Assert.Empty(Of(skipped));
        Assert.Contains(skipped.Skipped, s => s.RuleId == PlayerRuleIds.UnknownVerb);
    }

    // ------------------------------------------------------------ shipped content

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void CreateM1a_PassesCvPlr13_WithNoDeclarations_AndNoSkip()
    {
        ContentBuildResult r = BuiltInContent.BuildM1a(RuleRegistry.CreateDefault());
        var input = new ContentValidationInput(BuiltInContent.CreateM1a());

        Assert.Empty(Of(r.Validation));
        Assert.DoesNotContain(r.Validation.Skipped, s => s.RuleId == PlayerRuleIds.UnknownVerb);
        Assert.NotNull(r.Set);
        Assert.Empty(input.VerbDeclarations);
    }

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Adapter_DefaultDeclarations_NormaliseToEmpty()
    {
        var input = new ContentValidationInput(ContentFixtures.MinimalValid().Input().Candidate, default, default, default, default);

        Assert.False(input.VerbDeclarations.IsDefault);
        Assert.Empty(input.VerbDeclarations);
    }
}
