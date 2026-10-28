using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 009 (TR-content-022, M-1a subset; GDD CDD-ID-1 duplicate clause, CDD-ID-3, CV-GEN-17, CV-GEN-18):
/// the General set-level rules, through the production pipeline (<see cref="ContentDraft.Build"/>). The per-clause
/// violation and valid-edge fixtures live in <see cref="GeneralRuleFixtures"/> and are checked by the meta-test in
/// <c>RuleRegistryTests</c>; these tests add the exact-content assertions the fixtures do not make.
/// </summary>
public class GeneralRulesTests
{
    private static ContentDiagnostic[] Of(ContentBuildResult r, string id) =>
        r.Validation.Diagnostics.Where(d => d.RuleId == id).ToArray();

    // ------------------------------------------------------------ CV-GEN-01 (CDD-ID-1)

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void DuplicateIdInOneType_IsOneErrorNamingBothLocations()
    {
        ContentBuildResult r = ContentFixtures.MinimalValid().Add(ContentFixtures.Plank).Build();

        ContentDiagnostic d = Assert.Single(Of(r, GeneralRuleIds.IdsAndReferences));
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.Contains("builtin/items/plank#0", d.Value, StringComparison.Ordinal);
        Assert.Contains("builtin/items/plank#1", d.Value, StringComparison.Ordinal);
        Assert.Equal(new DefRef(DefType.Item, "plank"), d.Def);
        Assert.Null(r.Set);
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void DuplicateIdThreeTimes_NamesEveryLocation_AndStillOneError()
    {
        ContentBuildResult r = ContentFixtures.MinimalValid().Add(ContentFixtures.Plank).Add(ContentFixtures.Plank).Build();

        ContentDiagnostic d = Assert.Single(Of(r, GeneralRuleIds.IdsAndReferences));
        Assert.Contains("#2", d.Value, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void DuplicateId_NeitherDefIsKept_AndOtherDefsAre()
    {
        var found = new List<ContentDiagnostic>();
        var failed = new List<FailedInput>();
        ItemDef a = ContentFixtures.Plank with { Id = "a" };
        ItemDef b = ContentFixtures.Plank with { Id = "b" };

        DefTable<ItemDef> table = ContentIdChecks.Prepare(
            DefType.Item, "items", ContentInputKeys.Items, ImmutableArray.Create(a, b, a), RuleSeverity.Error, RuleSeverity.Error, found, failed);

        Assert.Equal(1, table.Count);
        Assert.Equal("b", table[DefIndex.FromValue(0)].Id);
        Assert.Single(found);
        Assert.Equal(ContentInputKeys.Items, Assert.Single(failed).Input);   // the type is a failed input: its dependents are skipped
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void SameIdInTwoDifferentTypes_Passes()
    {
        ContentBuildResult r = ContentFixtures.MinimalValid().Add(ContentFixtures.Plank with { Id = "letter" }).Build();

        Assert.Empty(r.Validation.Diagnostics);
        Assert.NotNull(r.Set);
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void ReferenceToAMissingId_IsAnError_ReferenceToAnExistingIdOfTheRightType_Passes()
    {
        ContentBuildResult missing = ContentFixtures.MinimalValid().Build(references: GeneralRuleFixtures.HouseAcceptsPending("nope"));
        ContentBuildResult ok = ContentFixtures.MinimalValid().Build(references: GeneralRuleFixtures.HouseAcceptsPending("letter"));

        ContentDiagnostic d = Assert.Single(Of(missing, GeneralRuleIds.IdsAndReferences));
        Assert.Contains("nope", d.Value, StringComparison.Ordinal);
        Assert.Equal(new DefRef(DefType.DestinationType, "house"), d.Def);
        Assert.Null(missing.Set);
        Assert.Empty(ok.Validation.Diagnostics);
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void ReferenceToAnIdOfTheWrongType_IsUnresolved_AndNamesTheTypeItIsFoundIn()
    {
        ContentBuildResult r = ContentFixtures.MinimalValid().Build(references: GeneralRuleFixtures.HouseAcceptsPending("plank"));

        ContentDiagnostic d = Assert.Single(Of(r, GeneralRuleIds.IdsAndReferences));
        Assert.Contains("exists only as Item", d.Allowed, StringComparison.Ordinal);
        Assert.Null(r.Set);
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void IndexReferences_OutOfRangeOrNone_AreUnresolved_ButNoFallbackKindIsFine()
    {
        MailKindDef noAcceptor = ContentFixtures.Letter with { AcceptedBy = ImmutableArray.Create(DefIndex.None) };
        MailKindDef badFallback = ContentFixtures.Letter with { FallbackKind = DefIndex.FromValue(9) };
        MailMixDef badShare = ContentFixtures.Mix with
        {
            Shifts = ImmutableArray.Create(new ShiftMix(1, ImmutableArray.Create(new MailShare(DefIndex.FromValue(3), 1_000)))),
        };

        Assert.Single(Of(ContentFixtures.MinimalValid().Replace(noAcceptor).Build(), GeneralRuleIds.IdsAndReferences));
        Assert.Single(Of(ContentFixtures.MinimalValid().Replace(badFallback).Build(), GeneralRuleIds.IdsAndReferences));
        Assert.Single(Of(ContentFixtures.MinimalValid().Replace(badShare).Build(), GeneralRuleIds.IdsAndReferences));
        Assert.Empty(Of(ContentFixtures.MinimalValid().Build(), GeneralRuleIds.IdsAndReferences));   // FallbackKind defaults to None
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void BuiltInContent_ValidatesWithZeroErrorsAndZeroWarnings()
    {
        ContentBuildResult r = BuiltInContent.BuildM1a(RuleRegistry.CreateDefault());

        Assert.Equal(0, r.Validation.ErrorCount);
        Assert.Equal(0, r.Validation.WarningCount);
        Assert.NotNull(r.Set);
    }

    // ------------------------------------------------------------ CV-GEN-09 (CDD-ID-3)

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void SixtyFiveThousandFiveHundredThirtyFiveDefs_IsOneStage3Error_AndTheTypesDependentRulesAreSkippedAndCounted()
    {
        ContentBuildResult r = GeneralRuleFixtures.TooManyContainers(65_535).Build();

        ContentDiagnostic d = Assert.Single(Of(r, GeneralRuleIds.TooManyDefs));
        Assert.Equal("65535 defs", d.Value);
        Assert.Null(r.Set);
        Assert.Empty(Of(r, GeneralRuleIds.EmptyRequiredTable));   // the emptied table is not reported as empty: CV-GEN-18 was skipped
        Assert.Contains(r.Validation.Skipped, s => s.RuleId == GeneralRuleIds.EmptyRequiredTable && s.BlockingInput == ContentInputKeys.Containers);
        Assert.Contains(r.Validation.Skipped, s => s.RuleId == GeneralRuleIds.StatBaseOutsideBounds && s.BlockingInput == ContentInputKeys.Containers);
        Assert.Equal(ValidationStage.Build, RuleRegistry.CreateDefault().Find(GeneralRuleIds.TooManyDefs)!.Stage);
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void SixtyFiveThousandFiveHundredThirtyFourDefs_PassWithMaxIndex65533_AndNothingIsSkipped()
    {
        ContentBuildResult r = GeneralRuleFixtures.TooManyContainers(65_534).Build();

        Assert.Empty(r.Validation.Diagnostics);
        Assert.Empty(r.Validation.Skipped);
        ContentSet set = Assert.IsType<ContentSet>(r.Set);
        Assert.Equal(65_534, set.Containers.Count);
        Assert.Equal("c65533", set.Containers[DefIndex.FromValue(65_533)].Id);
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void TooManyDefsWithADuplicate_ReportsBoth()
    {
        ContentDraft draft = GeneralRuleFixtures.TooManyContainers(65_535);
        ContentBuildResult r = (draft with { Containers = draft.Containers.Add(ContentFixtures.Cursor with { Id = "c00000" }) }).Build();

        Assert.Single(Of(r, GeneralRuleIds.TooManyDefs));
        Assert.Single(Of(r, GeneralRuleIds.IdsAndReferences));
    }

    // ------------------------------------------------------------ CV-GEN-17

    [Fact]
    [Trait("Id", "CDD-STAT-17")]
    public void ContainerRowsAboveMaxRows_IsAnErrorOnThatContainer_AndRowsEqualToMaxRowsPasses()
    {
        ContentBuildResult over = ContentFixtures.MinimalValid().Replace(ContentFixtures.Cursor with { GridH = 17 }).Build();
        ContentBuildResult at = ContentFixtures.MinimalValid().Replace(ContentFixtures.Cursor with { GridH = 16 }).Build();

        ContentDiagnostic d = Assert.Single(Of(over, GeneralRuleIds.StatBaseOutsideBounds));
        Assert.Equal(new DefRef(DefType.Container, "cursor"), d.Def);
        Assert.Equal("1..16", d.Allowed);
        Assert.Contains("inventory_rows", d.Value, StringComparison.Ordinal);
        Assert.Empty(at.Validation.Diagnostics);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-17")]
    public void ContainerRows_UseTheResolvedKey_SoALowerMaxRowsMakesTheSameContainerFail()
    {
        ContentBuildResult r = ContentFixtures.MinimalValid().Replace(ContentFixtures.Cursor with { GridH = 4 }).WithBalance("inventory.maxRows", 3).Build();

        Assert.Equal("1..3", Assert.Single(Of(r, GeneralRuleIds.StatBaseOutsideBounds)).Allowed);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-17")]
    public void ContainerWithoutAGrid_HasNoRows_AndIsNotChecked()
    {
        ContentBuildResult r = ContentFixtures.MinimalValid().Replace(ContentFixtures.Cursor with { HasGrid = false, GridH = 99 }).Build();

        Assert.Empty(Of(r, GeneralRuleIds.StatBaseOutsideBounds));
    }

    [Theory]
    [Trait("Id", "CDD-STAT-17")]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(255, false)]
    [InlineData(256, true)]
    public void MailKindMaxStack_MustLieInOneToTwoFiftyFive(int maxStack, bool trips)
    {
        ContentBuildResult r = ContentFixtures.MinimalValid().Replace(ContentFixtures.Letter with { MaxStack = maxStack }).Build();

        Assert.Equal(trips ? 1 : 0, Of(r, GeneralRuleIds.StatBaseOutsideBounds).Length);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-17")]
    public void ConstantBaseOutsideBalanceBounds_IsAnErrorOnTheStat_AndEqualityPasses()
    {
        ContentBuildResult over = ContentFixtures.MinimalValid().WithBalance("throw.rangeMinPermille", 1_200).Build();
        ContentBuildResult at = ContentFixtures.MinimalValid().WithBalance("throw.rangeMinPermille", 1_000).Build();

        ContentDiagnostic d = Assert.Single(Of(over, GeneralRuleIds.StatBaseOutsideBounds));
        Assert.True(d.Def.IsNone);
        Assert.Contains("throw_range_permille", d.Value, StringComparison.Ordinal);
        Assert.Equal("1200..1500", d.Allowed);
        Assert.Empty(at.Validation.Diagnostics);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-17")]
    public void StatWithInvertedBounds_IsCvGen15Only_AndSkippedByCvGen17()
    {
        // min 1501 > max 1500 (CV-GEN-15); the base 1000 is below min but the stat is skipped.
        ContentBuildResult r = ContentFixtures.MinimalValid().WithBalance("throw.rangeMinPermille", 1_501).Build();

        Assert.Single(Of(r, GeneralRuleIds.StatBoundsInverted));
        Assert.Empty(Of(r, GeneralRuleIds.StatBaseOutsideBounds));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-17")]
    public void RuleBody_WithoutPrecomputedBounds_ResolvesThemFromTheBalanceTable()
    {
        RuleSpec rule = RuleRegistry.CreateDefault().Find(GeneralRuleIds.StatBaseOutsideBounds)!;
        ContentValidationInput input = ContentFixtures.MinimalValid().WithBalance("throw.rangeMinPermille", 1_200).Input();   // Input() carries no ResolvedStatBounds
        var sink = new List<ContentDiagnostic>();

        new GeneralSetChecks().CheckStatBases(input, new RuleContext(rule, sink, new List<DefRef>()));

        Assert.Single(sink);
        Assert.Equal(GeneralRuleIds.StatBaseOutsideBounds, sink[0].RuleId);
    }

    // ------------------------------------------------------------ CV-GEN-18

    [Theory]
    [Trait("Id", "CDD-LOAD-18")]
    [InlineData("mail_kinds")]
    [InlineData("mail_mixes")]
    [InlineData("destination_types")]
    [InlineData("containers")]
    public void EachEmptyRequiredTable_IsOneErrorForThatTable(string folder)
    {
        // Emptying the mail kind or destination table also strands the references into it (CV-GEN-01), a reviewed cascade.
        ContentDraft draft = folder switch
        {
            "mail_kinds" => ContentFixtures.MinimalValid().WithoutMailKinds(),
            "mail_mixes" => ContentFixtures.MinimalValid().WithoutMailMixes(),
            "destination_types" => ContentFixtures.MinimalValid().WithoutDestinationTypes(),
            _ => ContentFixtures.MinimalValid().WithoutContainers(),
        };

        ContentBuildResult r = draft.Build();

        ContentDiagnostic d = Assert.Single(Of(r, GeneralRuleIds.EmptyRequiredTable));
        Assert.Equal("builtin/" + folder, d.Location.Path);
        Assert.Null(r.Set);
    }

    [Fact]
    [Trait("Id", "CDD-LOAD-18")]
    public void AllFourTablesEmpty_IsFourErrors_AndAnEmptyItemTablePasses()
    {
        ContentDraft none = ContentFixtures.MinimalValid().WithoutMailKinds().WithoutMailMixes().WithoutDestinationTypes().WithoutContainers();
        ContentBuildResult r = none.Build();
        ContentBuildResult noItems = ContentFixtures.MinimalValid().WithoutItems().Build();

        Assert.Equal(4, Of(r, GeneralRuleIds.EmptyRequiredTable).Length);
        Assert.Empty(noItems.Validation.Diagnostics);
        Assert.NotNull(noItems.Set);
    }

    // ------------------------------------------------------------ per-type (all five def types)

    private static ImmutableArray<T> Generate<T>(int count, Func<string, T> make)
    {
        var list = ImmutableArray.CreateBuilder<T>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(make("g" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture)));
        }

        return list.MoveToImmutable();
    }

    private static ContentDraft WithDuplicate(string folder)
    {
        ContentDraft d = ContentFixtures.MinimalValid();
        return folder switch
        {
            "containers" => d.Add(ContentFixtures.Cursor),
            "items" => d.Add(ContentFixtures.Plank),
            "mail_kinds" => d.Add(ContentFixtures.Letter),
            "destination_types" => d with { DestinationTypes = d.DestinationTypes.Add(ContentFixtures.House) },
            _ => d with { MailMixes = d.MailMixes.Add(ContentFixtures.Mix) },
        };
    }

    private static ContentDraft WithTooMany(string folder)
    {
        const int Count = DefIdAssigner.MaxDefsPerType + 1;
        ContentDraft d = ContentFixtures.MinimalValid();
        return folder switch
        {
            "containers" => d with { Containers = Generate(Count, id => ContentFixtures.Cursor with { Id = id }) },
            "items" => d with { Items = Generate(Count, id => ContentFixtures.Plank with { Id = id }) },
            "mail_kinds" => d with { MailKinds = Generate(Count, id => ContentFixtures.Letter with { Id = id }) },
            "destination_types" => d with { DestinationTypes = Generate(Count, id => ContentFixtures.House with { Id = id }) },
            _ => d with { MailMixes = Generate(Count, id => ContentFixtures.Mix with { Id = id }) },
        };
    }

    private static DefType TypeOf(string folder) => folder switch
    {
        "containers" => DefType.Container,
        "items" => DefType.Item,
        "mail_kinds" => DefType.MailKind,
        "destination_types" => DefType.DestinationType,
        _ => DefType.MailMix,
    };

    [Theory]
    [Trait("Id", "CDD-ID-1")]
    [InlineData("containers", ContentInputKeys.Containers)]
    [InlineData("items", ContentInputKeys.Items)]
    [InlineData("mail_kinds", ContentInputKeys.MailKinds)]
    [InlineData("destination_types", ContentInputKeys.DestinationTypes)]
    [InlineData("mail_mixes", ContentInputKeys.MailMixes)]
    public void DuplicateInAnyDefType_IsExactlyOneCvGen01_AndDependentRulesAreSkippedNotCascaded(string folder, string key)
    {
        ContentBuildResult r = WithDuplicate(folder).Build();

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);   // no shifted-index CV-GEN-01, no CV-GEN-17 or CV-GEN-18 follow-ups
        Assert.Equal(GeneralRuleIds.IdsAndReferences, d.RuleId);
        Assert.Equal(TypeOf(folder), d.Def.Type);
        Assert.StartsWith("builtin/" + folder + "/", d.Location.Path, StringComparison.Ordinal);
        Assert.Null(r.Set);
        if (folder == "items")
        {
            Assert.Empty(r.Validation.Skipped);   // no rule declares the item table as an input (CV-GRD-02 and 04 read it but tolerate a partial table)
        }
        else
        {
            Assert.Contains(r.Validation.Skipped, s => s.BlockingInput == key && s.BlockingFile == "builtin/" + folder);
        }
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void DuplicateMailKind_SkipsTheRulesThatReadTheKindTable()
    {
        ContentBuildResult r = WithDuplicate("mail_kinds").Build();

        Assert.Contains(r.Validation.Skipped, s => s.RuleId == GeneralRuleIds.IdsAndReferences);
        Assert.Contains(r.Validation.Skipped, s => s.RuleId == GeneralRuleIds.EmptyRequiredTable);
        Assert.Contains(r.Validation.Skipped, s => s.RuleId == GeneralRuleIds.StatBaseOutsideBounds);
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void SeveralDuplicatedIds_AreEachOneError_AndTheOutputIsSortedByDef()
    {
        ItemDef a = ContentFixtures.Plank with { Id = "a" };
        ItemDef b = ContentFixtures.Plank with { Id = "b" };
        ContentDraft draft = ContentFixtures.MinimalValid() with { Items = ImmutableArray.Create(b, a, b, a) };

        ContentDiagnostic[] found = Of(draft.Build(), GeneralRuleIds.IdsAndReferences);

        Assert.Equal(new[] { "a", "b" }, found.Select(x => x.Def.Id).ToArray());
    }

    [Theory]
    [Trait("Id", "CDD-ID-3")]
    [InlineData("containers", ContentInputKeys.Containers)]
    [InlineData("items", ContentInputKeys.Items)]
    [InlineData("mail_kinds", ContentInputKeys.MailKinds)]
    [InlineData("destination_types", ContentInputKeys.DestinationTypes)]
    [InlineData("mail_mixes", ContentInputKeys.MailMixes)]
    public void TooManyDefsInAnyType_IsExactlyOneCvGen09_AndTheTypesDependentsAreSkippedWithItsKey(string folder, string key)
    {
        ContentBuildResult r = WithTooMany(folder).Build();

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal(GeneralRuleIds.TooManyDefs, d.RuleId);
        Assert.Equal("builtin/" + folder, d.Location.Path);
        Assert.Null(r.Set);
        if (folder == "items")
        {
            Assert.Empty(r.Validation.Skipped);
        }
        else
        {
            Assert.All(r.Validation.Skipped, s => Assert.Equal(key, s.BlockingInput));
            Assert.NotEmpty(r.Validation.Skipped);
        }
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void PendingReferenceToATypeThatFailedStage3_IsSilentlySkipped_NotReportedAsMissing()
    {
        // The container table is emptied by CV-GEN-09, so "c00000" cannot be looked up: no false CV-GEN-01.
        ImmutableArray<PendingReference> toContainer = ImmutableArray.Create(new PendingReference(
            new DefRef(DefType.DestinationType, "house"), ContentValidationInput.LocationOf("destination_types", "house"),
            "dropAt", DefType.Container, "c00000"));

        ContentBuildResult r = GeneralRuleFixtures.TooManyContainers(65_535).Build(references: toContainer);

        Assert.Empty(Of(r, GeneralRuleIds.IdsAndReferences));
        Assert.Single(Of(r, GeneralRuleIds.TooManyDefs));
    }

    // ------------------------------------------------------------ CV-GEN-17 more cases

    [Fact]
    [Trait("Id", "CDD-STAT-17")]
    public void BalanceBase_OutsideItsStatsBounds_IsAnErrorNamingTheBalanceKey()
    {
        // No shipped stat can be driven out of range through the balance hard ranges, so the body is run on a synthetic
        // row: LateValueRatioPm reads its base from mail.lateValueRatio (default 500); its max is moved down to 400.
        StatDef row = StatDefs.Get(Stat.LateValueRatioPm) with { Max = StatBound.Constant(400) };
        RuleSpec rule = RuleRegistry.CreateDefault().Find(GeneralRuleIds.StatBaseOutsideBounds)!;
        ContentSet set = ContentFixtures.MinimalValid().Input().Candidate;
        var sink = new List<ContentDiagnostic>();

        new GeneralSetChecks().CheckStat(row, row.ResolveBounds(set.Balance), set, new RuleContext(rule, sink, new List<DefRef>()));

        ContentDiagnostic d = Assert.Single(sink);
        Assert.Contains("mail.lateValueRatio", d.Value, StringComparison.Ordinal);
        Assert.Equal("0..400", d.Allowed);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-17")]
    public void RuleBodies_ToleratePlaceholderDefaultArrays_WithoutThrowing()
    {
        MailKindDef kind = ContentFixtures.Letter with { AcceptedBy = default };
        DestinationTypeDef house = ContentFixtures.House with { Accepts = default };
        MailMixDef mix = ContentFixtures.Mix with { Shifts = default };
        MailMixDef mix2 = ContentFixtures.Mix with { Id = "second", Shifts = ImmutableArray.Create(new ShiftMix(1, default)) };
        ContentDraft draft = (ContentFixtures.MinimalValid().Replace(kind).Replace(house).Replace(mix)) with { };
        draft = draft with { MailMixes = draft.MailMixes.Add(mix2) };
        RuleSpec rule = RuleRegistry.CreateDefault().Find(GeneralRuleIds.IdsAndReferences)!;
        var sink = new List<ContentDiagnostic>();

        new GeneralSetChecks().CheckReferences(draft.Input(), new RuleContext(rule, sink, new List<DefRef>()));

        Assert.Empty(sink);
    }

    // ------------------------------------------------------------ registration

    [Theory]
    [Trait("Id", "CDD-RULE-2")]
    [InlineData(GeneralRuleIds.IdsAndReferences, ValidationStage.References)]
    [InlineData(GeneralRuleIds.TooManyDefs, ValidationStage.Build)]
    [InlineData(GeneralRuleIds.StatBaseOutsideBounds, ValidationStage.WholeSet)]
    [InlineData(GeneralRuleIds.EmptyRequiredTable, ValidationStage.WholeSet)]
    public void TheFourRules_AreRegisteredAsErrors_AtTheirStage(string id, ValidationStage stage)
    {
        RuleSpec rule = RuleRegistry.CreateDefault().Find(id)!;

        Assert.NotNull(rule);
        Assert.Equal(RuleSeverity.Error, rule.Severity);
        Assert.Equal(stage, rule.Stage);
        Assert.Equal(RuleStatus.Active, rule.Status);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Build_WithARegistryLackingTheNewRows_ThrowsNamingTheRow()
    {
        RuleRegistry old = RuleRegistry.Compose(General.Rules.RemoveAll(r => r.Id == GeneralRuleIds.TooManyDefs));
        ContentDraft draft = ContentFixtures.MinimalValid();

        ArgumentException e = Assert.Throws<ArgumentException>(() => ContentPipeline.Build(
            draft.BuildBalance(), draft.Containers, draft.Items, draft.MailKinds, draft.DestinationTypes, draft.MailMixes, old));
        Assert.Contains(GeneralRuleIds.TooManyDefs, e.Message, StringComparison.Ordinal);
    }
}
