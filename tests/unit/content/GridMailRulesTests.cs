using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 010 (TR-content-022, M-1a subset; GDD CDD-VAL-1 normative edges, CDD-RULE-1 mechanism half):
/// the Grid rules CV-GRD-01 to 04, 06, 07 and the Mail rules CV-MAIL-01 to 06, through the production pipeline
/// (<see cref="ContentDraft.Build"/>). The per-clause violation and valid-edge fixtures live in
/// <see cref="GridMailRuleFixtures"/> and are checked by the meta-test in <c>RuleRegistryTests</c>; these tests add the
/// exact-content assertions, the extra edges and the shipped-content check.
/// </summary>
public class GridMailRulesTests
{
    private static ContentDiagnostic[] Of(ValidationResult r, string id) => r.Diagnostics.Where(d => d.RuleId == id).ToArray();

    private static ValidationResult Run(ContentDraft draft) => draft.Build().Validation;

    private static ContentDraft Shares(params (int Kind, int Pm)[] shares) =>
        GridMailRuleFixtures.WithKinds(Array.Empty<MailKindDef>(), shares);

    private static RuleRegistry WithSources(params int[] muls) => RuleRegistry.Compose(
        General.Rules, Grid.RulesWithSources(ImmutableArray.Create(muls)), Mail.Rules, Player.Rules, Throwing.Rules);

    private static ValidationResult RunWith(ContentDraft draft, RuleRegistry rules) => draft.Build(rules).Validation;

    // ------------------------------------------------------------ the rows

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Rows_AreRegisteredWithTheCatalogueSeverityAndStage()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();
        (string Id, RuleSeverity Severity, ValidationStage Stage, string Owner)[] expected =
        {
            (GridRuleIds.MaxStackWorstCase, RuleSeverity.Error, ValidationStage.WholeSet, "grid-inventory"),
            (GridRuleIds.HandCarriedBulk, RuleSeverity.Error, ValidationStage.WholeSet, "grid-inventory"),
            (GridRuleIds.EmptyAllowedCategories, RuleSeverity.Error, ValidationStage.References, "grid-inventory"),
            (GridRuleIds.Dimensions, RuleSeverity.Error, ValidationStage.References, "grid-inventory"),
            (GridRuleIds.EvictAgeAtOrAboveDespawn, RuleSeverity.Warning, ValidationStage.WholeSet, "grid-inventory"),
            (GridRuleIds.DespawnTooShort, RuleSeverity.Warning, ValidationStage.References, "grid-inventory"),
            (MailRuleIds.Acceptance, RuleSeverity.Error, ValidationStage.References, "mail-destinations"),
            (MailRuleIds.Shares, RuleSeverity.Error, ValidationStage.WholeSet, "mail-destinations"),
            (MailRuleIds.DeadLetterShifts, RuleSeverity.Error, ValidationStage.References, "mail-destinations"),
            (MailRuleIds.Acceptors, RuleSeverity.Error, ValidationStage.WholeSet, "mail-destinations"),
            (MailRuleIds.Jitter, RuleSeverity.Error, ValidationStage.WholeSet, "mail-destinations"),
            (MailRuleIds.TailGuard, RuleSeverity.Error, ValidationStage.WholeSet, "mail-destinations"),
        };

        foreach ((string id, RuleSeverity severity, ValidationStage stage, string owner) in expected)
        {
            RuleSpec rule = Assert.IsAssignableFrom<RuleSpec>(registry.Find(id));
            Assert.Equal(severity, rule.Severity);
            Assert.Equal(stage, rule.Stage);
            Assert.Equal(owner, rule.Owner);
            Assert.Equal(RuleStatus.Active, rule.Status);
            Assert.NotEmpty(rule.Alias);
        }

        Assert.Equal(6, Grid.Rules.Length);
        Assert.Equal(6, Mail.Rules.Length);
        Assert.Null(registry.Find("CV-GRD-05"));   // M-1b, with the death bag
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void OwnerAliases_ResolveToTheirRows()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();

        Assert.True(registry.TryResolve("Grid (R3)", out RuleSpec? grd01));
        Assert.Equal(GridRuleIds.MaxStackWorstCase, grd01!.Id);
        Assert.True(registry.TryResolve("Grid (R5/D1)", out RuleSpec? grd02));
        Assert.Equal(GridRuleIds.HandCarriedBulk, grd02!.Id);
        Assert.True(registry.TryResolve("Mail (M-R1-1c) shares", out RuleSpec? mail02));
        Assert.Equal(MailRuleIds.Shares, mail02!.Id);
        Assert.Equal("CV-GRD-01 (Grid (R3))", registry.FormatId(GridRuleIds.MaxStackWorstCase));
    }

    // ------------------------------------------------------------ CV-GRD-01 (F5)

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd01_127TimesDouble_Is254_AndPasses()
    {
        ValidationResult r = GridMailRuleFixtures.WithSyntheticStackSource(GridMailRuleFixtures.Kind(127));

        Assert.Empty(Of(r, GridRuleIds.MaxStackWorstCase));
        Assert.Equal(0, r.ErrorCount);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd01_128TimesDouble_Is256_AndErrorsNamingTheKind()
    {
        ValidationResult r = GridMailRuleFixtures.WithSyntheticStackSource(GridMailRuleFixtures.Kind(128));

        ContentDiagnostic d = Assert.Single(Of(r, GridRuleIds.MaxStackWorstCase));
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.Equal(new DefRef(DefType.MailKind, "letter"), d.Def);
        Assert.Contains("256", d.Value, StringComparison.Ordinal);
        Assert.Contains("255", d.Allowed, StringComparison.Ordinal);
        Assert.False(r.CanBuild);
    }

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(255, 0)]
    [InlineData(256, 1)]
    public void Grd01_WithNoSources_TheWorstCaseIsTheBase_255Passes_256Errors(int maxStack, int expected)
    {
        ValidationResult r = Run(GridMailRuleFixtures.Kind(maxStack));

        Assert.Equal(expected, Of(r, GridRuleIds.MaxStackWorstCase).Length);
        if (maxStack == 256)
        {
            Assert.Single(Of(r, GeneralRuleIds.StatBaseOutsideBounds));   // the stat's own clamp, reported once by CV-GEN-17
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void Grd01_PostcardAndLetterUnderBulkMailer_Pass_AndHypotheticalMaxStack200Errors()
    {
        ContentDraft postcardAndLetter = GridMailRuleFixtures.Kind(40);   // 40 x 2.0 = 80
        Assert.Empty(Of(RunWith(postcardAndLetter, WithSources(2_000)), GridRuleIds.MaxStackWorstCase));
        Assert.Empty(Of(RunWith(GridMailRuleFixtures.Kind(20), WithSources(2_000)), GridRuleIds.MaxStackWorstCase));
        Assert.Single(Of(RunWith(GridMailRuleFixtures.Kind(200), WithSources(2_000)), GridRuleIds.MaxStackWorstCase));   // 400 > 255
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void Grd01_PassesTheFullCoLiveMulList_SoTwoDoublersMultiply()
    {
        // 64 x 2.0 = 128 passes; 64 x 2.0 x 2.0 = 256 errors: k_worst is the whole list, not the largest source.
        Assert.Empty(Of(RunWith(GridMailRuleFixtures.Kind(64), WithSources(2_000)), GridRuleIds.MaxStackWorstCase));
        Assert.Single(Of(RunWith(GridMailRuleFixtures.Kind(64), WithSources(2_000, 2_000)), GridRuleIds.MaxStackWorstCase));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void Grd01_LoweringAndNeutralMuls_DoNotRaiseTheWorstCase()
    {
        // F5 high takes the raising units only: 500 (halves) and 1000 (neutral) cannot push 255 over.
        Assert.Empty(Of(RunWith(GridMailRuleFixtures.Kind(255), WithSources(500, 1_000)), GridRuleIds.MaxStackWorstCase));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void Grd01_TooManyFactors_IsNotAResult_TheRuleDoesNotReadValueZero()
    {
        // Seven live Muls exceed the six F5 allows: Status is TooManyFactors and Value 0 must not be compared (TD-050 item 4).
        // CV-PRK-10 owns that finding; CV-GRD-01 reports nothing for it, not even for a base that is itself too large.
        ValidationResult r = RunWith(GridMailRuleFixtures.Kind(255), WithSources(2_000, 2_000, 2_000, 2_000, 2_000, 2_000, 2_000));

        Assert.Empty(Of(r, GridRuleIds.MaxStackWorstCase));
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd01_ReportsOnlyTheOffendingKind()
    {
        ContentDraft draft = GridMailRuleFixtures.Kind(128).Add(GridMailRuleFixtures.Pallet);   // pallet maxStack 1

        ContentDiagnostic d = Assert.Single(Of(RunWith(draft, WithSources(2_000)), GridRuleIds.MaxStackWorstCase));

        Assert.Equal("letter", d.Def.Id);
    }

    // ------------------------------------------------------------ CV-GRD-02

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd02_HandCarriedContainerThatAdmitsABulkItem_Errors_NamingBothDefs()
    {
        ContentDraft draft = GridMailRuleFixtures.Cursor(ContentFixtures.Cursor).Add(
            ContentFixtures.Plank with { Id = "anvil", Name = "item.anvil", Category = ItemCategory.Tool, WeightClass = WeightClass.Bulk });

        ContentDiagnostic d = Assert.Single(Of(Run(draft), GridRuleIds.HandCarriedBulk));

        Assert.Equal(new DefRef(DefType.Container, "cursor"), d.Def);
        Assert.Contains("anvil", d.Value, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd02_ANonHandCarriedContainer_MayAdmitBulk()
    {
        ContentDraft draft = GridMailRuleFixtures.Cursor(ContentFixtures.Cursor with { HandCarried = false }).Add(
            ContentFixtures.Plank with { Id = "anvil", Name = "item.anvil", WeightClass = WeightClass.Bulk });

        Assert.Empty(Of(Run(draft), GridRuleIds.HandCarriedBulk));
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd02_ACategoryListThatExcludesTheBulkCategory_Passes_OneThatIncludesItErrors()
    {
        ItemDef bulkMaterial = ContentFixtures.Plank with { Id = "anvil", Name = "item.anvil", Category = ItemCategory.Material, WeightClass = WeightClass.Bulk };
        ContainerDef toolsOnly = ContentFixtures.Cursor with { HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Tool) };
        ContainerDef materials = ContentFixtures.Cursor with { HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Tool, ItemCategory.Material) };

        Assert.Empty(Of(Run(GridMailRuleFixtures.Cursor(toolsOnly).Add(bulkMaterial)), GridRuleIds.HandCarriedBulk));
        Assert.Single(Of(Run(GridMailRuleFixtures.Cursor(materials).Add(bulkMaterial)), GridRuleIds.HandCarriedBulk));
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd02_ABulkMailKind_IsAdmittedByAnyCategoryContainer_AndByAMailContainer_ButNotByAToolsOnlyOne()
    {
        MailKindDef bulkLetter = ContentFixtures.Letter with { WeightClass = WeightClass.Bulk };
        ContainerDef mailOnly = ContentFixtures.Cursor with { HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Mail) };
        ContainerDef toolsOnly = ContentFixtures.Cursor with { HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Tool) };

        Assert.Single(Of(Run(ContentFixtures.MinimalValid().Replace(bulkLetter)), GridRuleIds.HandCarriedBulk));
        Assert.Single(Of(Run(GridMailRuleFixtures.Cursor(mailOnly).Replace(bulkLetter)), GridRuleIds.HandCarriedBulk));
        Assert.Empty(Of(Run(GridMailRuleFixtures.Cursor(toolsOnly).Replace(bulkLetter)), GridRuleIds.HandCarriedBulk));
    }

    // ------------------------------------------------------------ CV-GRD-03

    [Fact]
    [Trait("Id", "CDD-SCHEMA-3")]
    public void Grd03_PresentButEmptyAllowedCategories_Errors_OmittedPasses_OneCategoryPasses()
    {
        ValidationResult empty = Run(GridMailRuleFixtures.Cursor(ContentFixtures.Cursor with { HasAllowedCategories = true }));
        ValidationResult omitted = Run(ContentFixtures.MinimalValid());
        ValidationResult one = Run(GridMailRuleFixtures.Cursor(ContentFixtures.Cursor with
        {
            HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Mail),
        }));

        ContentDiagnostic d = Assert.Single(Of(empty, GridRuleIds.EmptyAllowedCategories));
        Assert.Equal(new DefRef(DefType.Container, "cursor"), d.Def);
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.Empty(Of(omitted, GridRuleIds.EmptyAllowedCategories));
        Assert.Empty(Of(one, GridRuleIds.EmptyAllowedCategories));
    }

    // ------------------------------------------------------------ CV-GRD-04

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(ContainerLayout.Grid)]
    [InlineData(ContainerLayout.Slots)]
    public void Grd04_WidthBelowOne_ErrorsForGridAndSlots(ContainerLayout layout)
    {
        ValidationResult r = Run(GridMailRuleFixtures.Cursor(ContentFixtures.Cursor with { Layout = layout, GridW = 0 }));

        ContentDiagnostic d = Assert.Single(Of(r, GridRuleIds.Dimensions));
        Assert.Equal(new DefRef(DefType.Container, "cursor"), d.Def);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd04_HeightBelowOne_Errors_AndAlsoTripsTheRowsStatBound()
    {
        ValidationResult r = Run(GridMailRuleFixtures.Cursor(ContentFixtures.Cursor with { GridH = 0 }));

        Assert.Single(Of(r, GridRuleIds.Dimensions));
        Assert.Single(Of(r, GeneralRuleIds.StatBaseOutsideBounds));   // InventoryRows clamp 1..16: a reviewed second finding
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd04_AOneByOneGrid_Passes_AndAPileHasNoGridToCheck()
    {
        Assert.Empty(Of(Run(ContentFixtures.MinimalValid()), GridRuleIds.Dimensions));
        Assert.Empty(Of(Run(GridMailRuleFixtures.Cursor(ContentFixtures.Cursor with { Layout = ContainerLayout.Pile, HasGrid = false, GridW = 0, GridH = 0 })), GridRuleIds.Dimensions));
    }

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(0)]
    [InlineData(-3)]
    public void Grd04_ItemMaxStackBelowOne_Errors(int maxStack)
    {
        ValidationResult r = Run(ContentFixtures.MinimalValid().Replace(ContentFixtures.Plank with { MaxStack = maxStack }));

        ContentDiagnostic d = Assert.Single(Of(r, GridRuleIds.Dimensions));
        Assert.Equal(new DefRef(DefType.Item, "plank"), d.Def);
        Assert.Empty(Of(r, GeneralRuleIds.StatBaseOutsideBounds));   // items have no stat clamp: only CV-GRD-04 reports
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd04_MailKindMaxStackBelowOne_Errors_ListingCvGen17AsAlsoTripped_AndOneStaysValid()
    {
        ValidationResult zero = Run(GridMailRuleFixtures.Kind(0));
        ValidationResult one = Run(GridMailRuleFixtures.Kind(1));

        Assert.Equal(new DefRef(DefType.MailKind, "letter"), Assert.Single(Of(zero, GridRuleIds.Dimensions)).Def);
        Assert.Single(Of(zero, GeneralRuleIds.StatBaseOutsideBounds));
        Assert.Empty(Of(one, GridRuleIds.Dimensions));
    }

    // ------------------------------------------------------------ CV-GRD-06 and CV-GRD-07 (Warnings)

    [Theory]
    [Trait("Id", "CDD-RULE-1")]
    [InlineData(29_000, 1)]
    [InlineData(30_000, 0)]
    [InlineData(0, 1)]
    public void Grd07_Under30Seconds_Warns_30SecondsPasses(int despawnMs, int warnings)
    {
        ValidationResult r = Run(ContentFixtures.MinimalValid().WithBalance("inventory.worldItemDespawnSeconds", despawnMs));

        Assert.Equal(warnings, Of(r, GridRuleIds.DespawnTooShort).Length);
        Assert.Equal(0, r.ErrorCount);
        Assert.True(r.CanBuild);   // a Warning does not stop the build
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Grd07_TheWarningIsAWarning_ItCarriesNoDef_AndNamesTheKey()
    {
        ValidationResult r = Run(ContentFixtures.MinimalValid().WithBalance("inventory.worldItemDespawnSeconds", 29_000));

        ContentDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal(GridRuleIds.DespawnTooShort, d.RuleId);
        Assert.Equal(RuleSeverity.Warning, d.Severity);
        Assert.True(d.Def.IsNone);
        Assert.Contains("worldItemDespawnSeconds", d.Value, StringComparison.Ordinal);
        Assert.Equal(1, r.WarningCount);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Grd07_ASuppressWithWhy_SilencesTheWarning_TheRunProceeds()
    {
        ValidationResult r = RunWithSetSuppress(29_000, new SuppressEntry(GridRuleIds.DespawnTooShort, "short litter timer for the prototype map", ContentValidationInput.SetLocation));

        Assert.Empty(Of(r, GridRuleIds.DespawnTooShort));
        Assert.Equal(0, r.WarningCount);
        Assert.Equal(0, r.ErrorCount);
        Assert.Equal(1, r.SuppressedCount);
        Assert.True(r.CanBuild);
    }

    [Theory]
    [Trait("Id", "CDD-RULE-1")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Grd07_ASuppressWithoutWhy_IsCvGen05_AndDoesNotSilence(string? why)
    {
        ValidationResult r = RunWithSetSuppress(29_000, new SuppressEntry(GridRuleIds.DespawnTooShort, why, ContentValidationInput.SetLocation));

        Assert.Single(Of(r, GridRuleIds.DespawnTooShort));
        Assert.Single(Of(r, GeneralRuleIds.SuppressWithoutWhy));
        Assert.Equal(0, r.SuppressedCount);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Grd07_ASuppressNamingAnErrorRule_IsAnError_AndItsRuleStillFires()
    {
        ValidationResult r = RunWithSetSuppress(29_000, new SuppressEntry(GridRuleIds.Dimensions, "wishful", ContentValidationInput.SetLocation));

        Assert.Single(Of(r, GeneralRuleIds.SuppressBadTarget));
        Assert.Single(Of(r, GridRuleIds.DespawnTooShort));
        Assert.False(r.CanBuild);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Grd07_ASuppressForAWarningThatDoesNotFire_IsCountedAsUnmatched()
    {
        ValidationResult r = RunWithSetSuppress(30_000, new SuppressEntry(GridRuleIds.DespawnTooShort, "not needed", ContentValidationInput.SetLocation));

        Assert.Empty(r.Diagnostics);
        Assert.Equal(1, r.UnmatchedSuppressCount);
    }

    private static ValidationResult RunWithSetSuppress(int despawnMs, SuppressEntry entry)
    {
        ContentValidationInput built = ContentFixtures.MinimalValid().WithBalance("inventory.worldItemDespawnSeconds", despawnMs).Input();
        var input = new ContentValidationInput(built.Candidate, setSuppress: ImmutableArray.Create(entry));
        return ContentValidationRunner.Run(RuleRegistry.CreateDefault(), input);
    }

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(300_000, 1)]
    [InlineData(300_001, 1)]
    [InlineData(299_999, 0)]
    public void Grd06_EvictAgeAtOrAboveTheDespawn_Warns(int evictMs, int warnings)
    {
        ValidationResult r = Run(ContentFixtures.MinimalValid().WithBalance("inventory.capEvictMinAgeSeconds", evictMs));

        Assert.Equal(warnings, Of(r, GridRuleIds.EvictAgeAtOrAboveDespawn).Length);
        Assert.Equal(0, r.ErrorCount);
        Assert.True(r.CanBuild);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Grd06_And07_AreIndependent_ADespawnOf29SecondsWithTheDefaultEvictAgeWarnsOnlyGrd07()
    {
        ValidationResult r = Run(ContentFixtures.MinimalValid().WithBalance("inventory.worldItemDespawnSeconds", 29_000));

        Assert.Single(Of(r, GridRuleIds.DespawnTooShort));
        Assert.Empty(Of(r, GridRuleIds.EvictAgeAtOrAboveDespawn));
    }

    // ------------------------------------------------------------ CV-MAIL-01

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail01_AcceptedByAnUnknownDestination_Errors_AndAlsoTripsCvGen01()
    {
        ValidationResult r = Run(ContentFixtures.MinimalValid().Replace(ContentFixtures.Letter with
        {
            AcceptedBy = ImmutableArray.Create(DefIndex.FromValue(0), DefIndex.FromValue(5)),
        }));

        ContentDiagnostic d = Assert.Single(Of(r, MailRuleIds.Acceptance));
        Assert.Equal(new DefRef(DefType.MailKind, "letter"), d.Def);
        Assert.Contains("5", d.Value, StringComparison.Ordinal);
        Assert.Single(Of(r, GeneralRuleIds.IdsAndReferences));
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail01_AKindListsADestinationThatDoesNotAcceptIt_ErrorsOnTheKind()
    {
        ValidationResult r = Run(ContentFixtures.MinimalValid().Replace(ContentFixtures.House with { Accepts = ImmutableArray<DefIndex>.Empty }));

        ContentDiagnostic d = Assert.Single(Of(r, MailRuleIds.Acceptance));
        Assert.Equal(new DefRef(DefType.MailKind, "letter"), d.Def);
        Assert.Contains("house", d.Value, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail01_ADestinationAcceptsAKindThatDoesNotListIt_ErrorsOnTheDestination()
    {
        ValidationResult r = Run(ContentFixtures.MinimalValid().Replace(ContentFixtures.Letter with { AcceptedBy = ImmutableArray<DefIndex>.Empty }));

        ContentDiagnostic d = Assert.Single(Of(r, MailRuleIds.Acceptance));
        Assert.Equal(new DefRef(DefType.DestinationType, "house"), d.Def);
        Assert.Contains("letter", d.Value, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail01_MirroredLists_Pass_AMirroredKindWithNoAcceptorIsNotMail01sBusiness()
    {
        // pallet is listed nowhere and listed by no destination: consistent, so CV-MAIL-01 is silent.
        ValidationResult r = Run(GridMailRuleFixtures.WithKinds(new[] { GridMailRuleFixtures.Pallet }, (0, 600), (0, 400), (1, 0)));

        Assert.Empty(Of(r, MailRuleIds.Acceptance));
    }

    // ------------------------------------------------------------ CV-MAIL-02

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(600, 400, 0)]
    [InlineData(600, 399, 1)]
    [InlineData(600, 401, 1)]
    public void Mail02_TheShiftSumMustBeExactly1000(int a, int b, int errors)
    {
        ValidationResult r = Run(Shares((0, a), (0, b)));

        Assert.Equal(errors, Of(r, MailRuleIds.Shares).Length);
    }

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(601, 399, 1)]
    [InlineData(600, 400, 0)]
    [InlineData(0, 600, 1)]   // sum 600: the sum clause, not the range clause
    public void Mail02_OneShareOver600_Errors_600Passes(int a, int b, int errors)
    {
        ValidationResult r = Run(Shares((0, a), (0, b)));

        Assert.Equal(errors, Of(r, MailRuleIds.Shares).Length);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail02_AShareOf601_NamesTheShift_AndANegativeShareErrors()
    {
        ContentDiagnostic over = Assert.Single(Of(Run(Shares((0, 601), (0, 399))), MailRuleIds.Shares));
        Assert.Equal(new DefRef(DefType.MailMix, "default"), over.Def);
        Assert.Contains("shift 1", over.Value, StringComparison.Ordinal);
        Assert.Contains("601", over.Value, StringComparison.Ordinal);

        // -1 breaks the range (one finding); 600 + 400 + (-1) also breaks the sum (a second finding).
        Assert.Equal(2, Of(Run(Shares((0, 600), (0, 400), (0, -1))), MailRuleIds.Shares).Length);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail02_EachShiftIsCheckedOnItsOwn_AndAnEmptyShiftSumsToZero()
    {
        ImmutableArray<MailShare> good = ImmutableArray.Create(new MailShare(DefIndex.FromValue(0), 600), new MailShare(DefIndex.FromValue(0), 400));
        MailMixDef mix = ContentFixtures.Mix with
        {
            Shifts = ImmutableArray.Create(
                new ShiftMix(1, good),
                new ShiftMix(2, ImmutableArray.Create(new MailShare(DefIndex.FromValue(0), 500), new MailShare(DefIndex.FromValue(0), 499))),
                new ShiftMix(3, ImmutableArray<MailShare>.Empty)),
        };

        ContentDiagnostic[] found = Of(Run(ContentFixtures.MinimalValid().Replace(mix)), MailRuleIds.Shares);

        Assert.Equal(2, found.Length);
        Assert.Contains(found, d => d.Value.Contains("shift 2", StringComparison.Ordinal));
        Assert.Contains(found, d => d.Value.Contains("shift 3", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail02_PassesVacuouslyOverAnEmptyMixTable_WhereCvGen18Fails()
    {
        ValidationResult r = Run(ContentFixtures.MinimalValid().WithoutMailMixes());

        Assert.Empty(Of(r, MailRuleIds.Shares));
        Assert.Single(Of(r, GeneralRuleIds.EmptyRequiredTable));
    }

    // ------------------------------------------------------------ CV-MAIL-03

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    public void Mail03_DeadLetterShiftsBelowOne_Errors(int shifts, int errors)
    {
        ValidationResult r = GridMailRuleFixtures.DeadLetterShifts(shifts);

        Assert.Equal(errors, Of(r, MailRuleIds.DeadLetterShifts).Length);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail03_TheShippedRegistryRefusesZeroAlready_AtCvGen13_SoTheRuleIsTheSecondGuard()
    {
        // mail.deadLetterShifts has hard range 1..16 today: the balance table does not even build with 0.
        Assert.Throws<InvalidOperationException>(() => ContentFixtures.MinimalValid().WithBalance("mail.deadLetterShifts", 0).Build());
    }

    // ------------------------------------------------------------ CV-MAIL-04

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail04_AKindWithAShareButNoAcceptorAndNoFallback_Errors_NamingTheKind()
    {
        ValidationResult r = Run(GridMailRuleFixtures.WithKinds(new[] { GridMailRuleFixtures.Pallet }, (0, 600), (1, 400)));

        ContentDiagnostic d = Assert.Single(Of(r, MailRuleIds.Acceptors));
        Assert.Equal(new DefRef(DefType.MailKind, "pallet"), d.Def);
        Assert.Equal(1, r.ErrorCount);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail04_AZeroShareKindNeedsNoAcceptor()
    {
        ValidationResult r = Run(GridMailRuleFixtures.WithKinds(new[] { GridMailRuleFixtures.Pallet }, (0, 600), (0, 400), (1, 0)));

        Assert.Empty(Of(r, MailRuleIds.Acceptors));
        Assert.Equal(0, r.ErrorCount);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail04_AShareInAnyShiftCounts()
    {
        MailMixDef mix = ContentFixtures.Mix with
        {
            Shifts = ImmutableArray.Create(
                new ShiftMix(1, ImmutableArray.Create(new MailShare(DefIndex.FromValue(0), 600), new MailShare(DefIndex.FromValue(0), 400))),
                new ShiftMix(2, ImmutableArray.Create(new MailShare(DefIndex.FromValue(0), 600), new MailShare(DefIndex.FromValue(1), 400)))),
        };

        ValidationResult r = Run(ContentFixtures.MinimalValid().Add(GridMailRuleFixtures.Pallet).Replace(mix));

        Assert.Equal("pallet", Assert.Single(Of(r, MailRuleIds.Acceptors)).Def.Id);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail04_AFallbackToAKindWithAnAcceptor_Resolves_AndTheChainMayBeLong()
    {
        // pallet -> parcel -> letter (letter is accepted by the house).
        MailKindDef pallet = GridMailRuleFixtures.Pallet with { FallbackKind = DefIndex.FromValue(2) };
        MailKindDef parcel = GridMailRuleFixtures.Parcel with { FallbackKind = DefIndex.FromValue(0) };

        ValidationResult r = Run(GridMailRuleFixtures.WithKinds(new[] { pallet, parcel }, (0, 600), (1, 400)));

        Assert.Empty(Of(r, MailRuleIds.Acceptors));
        Assert.Equal(0, r.ErrorCount);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail04_AFallbackChainThatEndsWithoutAnAcceptor_Errors()
    {
        MailKindDef pallet = GridMailRuleFixtures.Pallet with { FallbackKind = DefIndex.FromValue(2) };

        ValidationResult r = Run(GridMailRuleFixtures.WithKinds(new[] { pallet, GridMailRuleFixtures.Parcel }, (0, 600), (1, 400)));

        ContentDiagnostic d = Assert.Single(Of(r, MailRuleIds.Acceptors));
        Assert.Equal("pallet", d.Def.Id);
        Assert.Contains("ends", d.Value, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail04_AFallbackCycle_Errors_Once()
    {
        // pallet -> parcel -> pallet; parcel carries no share, so only pallet is checked.
        MailKindDef pallet = GridMailRuleFixtures.Pallet with { FallbackKind = DefIndex.FromValue(2) };
        MailKindDef parcel = GridMailRuleFixtures.Parcel with { FallbackKind = DefIndex.FromValue(1) };

        ValidationResult r = Run(GridMailRuleFixtures.WithKinds(new[] { pallet, parcel }, (0, 600), (1, 400)));

        ContentDiagnostic d = Assert.Single(Of(r, MailRuleIds.Acceptors));
        Assert.Contains("cycles", d.Value, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail04_ASelfLoopIsACycle()
    {
        MailKindDef pallet = GridMailRuleFixtures.Pallet with { FallbackKind = DefIndex.FromValue(1) };

        ValidationResult r = Run(GridMailRuleFixtures.WithKinds(new[] { pallet }, (0, 600), (1, 400)));

        Assert.Contains("cycles", Assert.Single(Of(r, MailRuleIds.Acceptors)).Value, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ CV-MAIL-05 and CV-MAIL-06

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(15_000, 7_499, 0)]
    [InlineData(15_000, 7_500, 1)]
    [InlineData(15_000, 8_000, 1)]
    [InlineData(15_000, 3_000, 0)]
    public void Mail05_JitterMustBeBelowHalfTheInterval(int interval, int jitter, int errors)
    {
        // The tail guard is kept inside interval - jitter (or below it) so only CV-MAIL-05 can fire.
        ValidationResult r = Run(ContentFixtures.MinimalValid().Replace(
            ContentFixtures.Mix with { BatchIntervalMs = interval, BatchJitterMs = jitter, TailGuardMs = 5_000 }));

        Assert.Equal(errors, Of(r, MailRuleIds.Jitter).Length);
        Assert.Empty(Of(r, MailRuleIds.TailGuard));
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void Mail05_AnOddIntervalIsComparedWithoutTruncation()
    {
        // interval 15 001: half is 7 500.5, so jitter 7 500 is below it and 7 501 is not (integer 2 x jitter < interval).
        Assert.Empty(Of(Run(Timing(15_001, 7_500, 5_000)), MailRuleIds.Jitter));
        Assert.Single(Of(Run(Timing(15_001, 7_501, 5_000)), MailRuleIds.Jitter));
    }

    private static ContentDraft Timing(int interval, int jitter, int tail) =>
        ContentFixtures.MinimalValid().Replace(ContentFixtures.Mix with { BatchIntervalMs = interval, BatchJitterMs = jitter, TailGuardMs = tail });

    [Theory]
    [Trait("Id", "CDD-VAL-1")]
    [InlineData(12_000, 0)]
    [InlineData(12_001, 1)]
    [InlineData(0, 0)]
    public void Mail06_TailGuardMustFitInsideIntervalMinusJitter(int tail, int errors)
    {
        ValidationResult r = Run(Timing(15_000, 3_000, tail));

        Assert.Equal(errors, Of(r, MailRuleIds.TailGuard).Length);
        Assert.Empty(Of(r, MailRuleIds.Jitter));
        if (errors == 1)
        {
            Assert.Equal(new DefRef(DefType.MailMix, "default"), Of(r, MailRuleIds.TailGuard)[0].Def);
        }
    }

    // ------------------------------------------------------------ shipped content and determinism

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void CreateM1a_ReportsZeroErrorsAndZeroUnsuppressedWarnings_UnderEveryGridAndMailRule()
    {
        ContentBuildResult r = BuiltInContent.BuildM1a(RuleRegistry.CreateDefault());

        Assert.Equal(0, r.Validation.ErrorCount);
        Assert.Equal(0, r.Validation.WarningCount);
        Assert.Empty(r.Validation.Diagnostics);
        Assert.NotNull(r.Set);
        Assert.NotNull(BuiltInContent.CreateM1a());   // does not throw
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void TheSameCandidateGivesTheSameDiagnostics_InTheSameOrder()
    {
        ContentDraft draft = Timing(15_000, 8_000, 14_000).WithBalance("inventory.worldItemDespawnSeconds", 1_000);

        string[] first = Run(draft).Diagnostics.Select(d => d.RuleId + "|" + d.Def + "|" + d.Value).ToArray();
        string[] second = Run(draft).Diagnostics.Select(d => d.RuleId + "|" + d.Def + "|" + d.Value).ToArray();

        Assert.True(first.Length >= 3);
        Assert.Equal(first, second);
    }
}
