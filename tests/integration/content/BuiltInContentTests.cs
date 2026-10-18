using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.Content;

namespace PerformativeMail.Sim.Tests.Integration.Content;

/// <summary>
/// Content-data story 008: <c>BuiltInContent.CreateM1a</c> and the immutable <c>ContentSet</c>. A snapshot of the M-1a
/// tables (ids, ordinal DefIndex order, authored values, owner balance records), reference resolution, the stage 4 to 5
/// runner over the factory set with every registered rule, the TD-037 candidate/adapter release path, immutability by
/// reflection, determinism by a field-walking comparer, and the <c>ContentFixtures</c> overlay helper. The
/// <c>ReferenceEquals</c> / <c>ContentBuildCount</c> criterion (CDD-LOAD-4, M-1a form) waits for simulation-core
/// story 025 (<c>SimComposition.Build</c>) and is not tested here.
/// </summary>
public class BuiltInContentTests
{
    private static readonly string[] _kindIds = { "large", "letter", "medium", "postcard", "small" };

    private static string[] Ids<T>(DefTable<T> t)
        where T : IDef => t.AsSpan().ToArray().Select(d => d.Id).ToArray();

    private static T ById<T>(DefTable<T> t, string id)
        where T : IDef => t.AsSpan().ToArray().Single(d => d.Id == id);

    // ------------------------------------------------------------ id sets and ordinal order

    [Fact]
    [Trait("Id", "CDD-LOAD-4")]
    [Trait("Id", "CD21")]
    public void CreateM1a_EveryTable_HasTheExactIdSet_InOrdinalDefIndexOrder()
    {
        ContentSet c = BuiltInContent.CreateM1a();

        Assert.Equal(new[] { "cursor", "hotbar", "player_inventory", "po_intake" }, Ids(c.Containers));
        Assert.Equal(new[] { "axe", "fiber", "log", "plank" }, Ids(c.Items));
        Assert.Equal(_kindIds, Ids(c.MailKinds));
        Assert.Equal(new[] { "house" }, Ids(c.DestinationTypes));
        Assert.Single(Ids(c.MailMixes));
    }

    [Fact]
    [Trait("Id", "CD21")]
    public void CreateM1a_EveryTableIsSortedByOrdinalId_AndIndexerAgreesWithPosition()
    {
        ContentSet c = BuiltInContent.CreateM1a();

        AssertSorted(c.Containers);
        AssertSorted(c.Items);
        AssertSorted(c.MailKinds);
        AssertSorted(c.DestinationTypes);
        AssertSorted(c.MailMixes);
        Assert.Equal("letter", c.MailKinds[DefIndex.FromValue(1)].Id);
    }

    private static void AssertSorted<T>(DefTable<T> t)
        where T : IDef
    {
        string[] ids = Ids(t);
        Assert.Equal(ids.OrderBy(x => x, StringComparer.Ordinal).ToArray(), ids);
        for (int i = 0; i < ids.Length; i++)
        {
            Assert.Equal(ids[i], t[DefIndex.FromValue((ushort)i)].Id);
        }
    }

    // ------------------------------------------------------------ authored values

    [Fact]
    [Trait("Id", "CD21")]
    public void CreateM1a_Containers_HaveTheGridInventoryR1Values()
    {
        ContentSet c = BuiltInContent.CreateM1a();

        ContainerDef hotbar = ById(c.Containers, "hotbar");
        Assert.Equal((ContainerLayout.Slots, true, 8, 1, ContainerView.Grid, BeltAccess.None, true), (hotbar.Layout, hotbar.HasGrid, hotbar.GridW, hotbar.GridH, hotbar.View, hotbar.BeltAccess, hotbar.HandCarried));
        Assert.Equal(new[] { 0 }, hotbar.ReservedSlots);
        Assert.False(hotbar.HasAllowedCategories);

        ContainerDef inv = ById(c.Containers, "player_inventory");
        Assert.Equal((ContainerLayout.Grid, 8, 2, ContainerView.Grid, BeltAccess.None, true), (inv.Layout, inv.GridW, inv.GridH, inv.View, inv.BeltAccess, inv.HandCarried));
        Assert.Empty(inv.ReservedSlots);

        ContainerDef intake = ById(c.Containers, "po_intake");
        Assert.Equal((ContainerLayout.Grid, 20, 16, ContainerView.Manifest, BeltAccess.OutfeedOnly, false), (intake.Layout, intake.GridW, intake.GridH, intake.View, intake.BeltAccess, intake.HandCarried));
        Assert.True(intake.HasAllowedCategories);
        Assert.Equal(new[] { ItemCategory.Mail }, intake.AllowedCategories);
        Assert.Equal(320, intake.GridW * intake.GridH);

        ContainerDef cursor = ById(c.Containers, "cursor");
        Assert.Equal((ContainerLayout.Slots, 1, 1), (cursor.Layout, cursor.GridW, cursor.GridH));
        Assert.True(cursor.HandCarried);   // GDD R1: "any hand-carryable" (the story table showed "-")
    }

    [Fact]
    [Trait("Id", "CD21")]
    public void CreateM1a_Items_HaveTheFixtureTableValues()
    {
        ContentSet c = BuiltInContent.CreateM1a();

        (string Id, ItemCategory Cat, int W, int H, int Stack)[] expected =
        {
            ("axe", ItemCategory.Tool, 1, 2, 1), ("fiber", ItemCategory.Material, 1, 1, 20),
            ("log", ItemCategory.Material, 1, 2, 10), ("plank", ItemCategory.Material, 1, 1, 20),
        };
        foreach (var e in expected)
        {
            ItemDef d = ById(c.Items, e.Id);
            Assert.Equal((e.Cat, e.W, e.H, e.Stack, WeightClass.Light), (d.Category, d.GridW, d.GridH, d.MaxStack, d.WeightClass));
            Assert.False(d.HasBuyPrice);
            Assert.Equal(0, d.BuyPriceCents);
            Assert.True(d.CarryByHand);
            Assert.Equal(EquipSlot.None, d.EquipSlot);
        }
    }

    [Fact]
    [Trait("Id", "CD21")]
    public void CreateM1a_MailKinds_HaveTheMailR1Values()
    {
        ContentSet c = BuiltInContent.CreateM1a();

        (string Id, int W, int H, int Stack, long Cents, WeightClass Weight, int Complaint, int Unlock)[] expected =
        {
            ("postcard", 1, 1, 40, 4, WeightClass.Light, 3_000, 2),
            ("letter", 1, 1, 20, 8, WeightClass.Light, 5_000, 1),
            ("small", 1, 2, 1, 30, WeightClass.Light, 8_000, 1),
            ("medium", 2, 2, 1, 70, WeightClass.Medium, 12_000, 1),
            ("large", 2, 4, 1, 160, WeightClass.Heavy, 16_000, 2),
        };
        foreach (var e in expected)
        {
            MailKindDef k = ById(c.MailKinds, e.Id);
            Assert.Equal((e.W, e.H, e.Stack, e.Cents, e.Weight, e.Complaint, e.Unlock), (k.GridW, k.GridH, k.MaxStack, k.BaseValueCents, k.WeightClass, k.ComplaintOnMisdeliveryMilliPoints, k.UnlockShift));
            Assert.Equal(0, k.DeadlineOffsetShifts);
            Assert.Equal(new[] { DefIndex.FromValue(0) }, k.AcceptedBy);   // house
            Assert.Equal((1, 500), (k.BeltLanes, k.BeltLengthMm));
            Assert.True(k.FallbackKind.IsNone);
        }

        Assert.DoesNotContain("cargo", Ids(c.MailKinds));   // deferred: M0
    }

    [Fact]
    [Trait("Id", "CD10")]
    public void CreateM1a_House_AcceptsEveryKind_WithTheMailValues()
    {
        DestinationTypeDef house = BuiltInContent.CreateM1a().DestinationTypes[DefIndex.FromValue(0)];

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, house.Accepts.Select(i => (int)i.Value));
        Assert.Equal((400, 2_000, 1, false), (house.ManualInsertHoldMs, house.InsertRateMilliPerSec, house.MaxAutomatedFeeders, house.RequiresUnit));
        Assert.Equal(new CatchVolume(1_000, 800, 1_000, 1_000, 0), house.CatchVolume);
    }

    [Fact]
    [Trait("Id", "CD10")]
    public void CreateM1a_MailMix_HasTheShiftSharesAndTiming()
    {
        ContentSet c = BuiltInContent.CreateM1a();
        MailMixDef mix = c.MailMixes[DefIndex.FromValue(0)];

        Assert.Equal((15_000, 3_000, 9_000, 0), (mix.BatchIntervalMs, mix.BatchJitterMs, mix.TailGuardMs, mix.OpeningRampBatches));
        Assert.Equal((300, 500, 250, 100, 1_600), (mix.StreetStreakRatioPm, mix.RaidSpawnRateRatioPm, mix.DistanceStepRatioPm, mix.ShiftStepRatioPm, mix.SpawnOverheadPm));
        Assert.Equal(new[] { 1, 2 }, mix.Shifts.Select(s => s.Shift));
        Assert.Equal(
            new[] { "letter:600", "small:300", "medium:100" },
            mix.Shifts[0].Shares.Select(s => c.MailKinds[s.Kind].Id + ":" + s.SharePm));
        Assert.Equal(
            new[] { "postcard:100", "letter:400", "small:300", "medium:150", "large:50" },
            mix.Shifts[1].Shares.Select(s => c.MailKinds[s.Kind].Id + ":" + s.SharePm));
        Assert.All(mix.Shifts, s => Assert.Equal(1_000, s.Shares.Sum(x => x.SharePm)));
    }

    // ------------------------------------------------------------ references

    [Fact]
    [Trait("Id", "CD10")]
    public void CreateM1a_EveryReference_ResolvesToADefIndexInItsTable()
    {
        ContentSet c = BuiltInContent.CreateM1a();

        foreach (MailKindDef k in c.MailKinds.AsSpan())
        {
            Assert.All(k.AcceptedBy, i => Assert.True(!i.IsNone && i.Value < c.DestinationTypes.Count));
            Assert.Equal("house", c.DestinationTypes[k.AcceptedBy[0]].Id);
        }

        foreach (DestinationTypeDef d in c.DestinationTypes.AsSpan())
        {
            Assert.All(d.Accepts, i => Assert.True(!i.IsNone && i.Value < c.MailKinds.Count));
        }

        foreach (MailMixDef m in c.MailMixes.AsSpan())
        {
            foreach (MailShare s in m.Shifts.SelectMany(x => x.Shares))
            {
                Assert.True(!s.Kind.IsNone && s.Kind.Value < c.MailKinds.Count);
            }
        }
    }

    // ------------------------------------------------------------ stage 4-5 runner

    [Fact]
    [Trait("Id", "CDD-LOAD-4")]
    public void BuildM1a_RunsTheStage4To5Runner_WithEveryRegisteredRule_AndReturnsZeroErrors()
    {
        RuleRegistry rules = RuleRegistry.CreateDefault();   // composes every rule table, so stories 009 to 012 are covered when they register

        // A dropped owner table must not pass vacuously: the registry holds the General rows and every owner table's rows.
        Assert.NotEmpty(rules.Rules);
        Assert.Contains(rules.Rules, x => x.Id.StartsWith("CV-GEN-", StringComparison.Ordinal));
        (string Owner, ImmutableArray<RuleSpec> Table)[] ownerTables =
        {
            ("General", General.Rules), ("Grid", PerformativeMail.Sim.Content.Rules.Grid.Rules),
            ("Mail", PerformativeMail.Sim.Content.Rules.Mail.Rules), ("Player", PerformativeMail.Sim.Content.Rules.Player.Rules),
            ("Throwing", Throwing.Rules),
        };
        foreach ((string owner, ImmutableArray<RuleSpec> table) in ownerTables)
        {
            Assert.All(table, x => Assert.NotNull(rules.Find(x.Id)));   // every row of owner table 'owner' is registered
            Assert.True(rules.Rules.Length >= table.Length, owner);
        }

        Assert.Equal(ownerTables.Sum(t => t.Table.Length), rules.Rules.Length);

        ContentBuildResult r = BuiltInContent.BuildM1a(rules);

        Assert.Equal(0, r.Validation.ErrorCount);
        Assert.True(r.Validation.CanBuild);
        Assert.NotNull(r.Set);
        Assert.Equal(0, r.Validation.WarningCount);
    }

    [Fact]
    [Trait("Id", "CDD-LOAD-4")]
    public void ContentValidationInput_PresentsEveryDefOfTheCandidate_AndABuiltInLocation()
    {
        ContentValidationInput input = ContentFixtures.MinimalValid().Input();

        Assert.Equal(5, input.Defs.Length);
        Assert.Equal(
            new[] { "Container:cursor", "Item:plank", "MailKind:letter", "DestinationType:house", "MailMix:default" },
            input.Defs.Select(d => d.Def.ToString()));
        Assert.All(input.Defs, d => Assert.True(d.Location.IsBuiltIn));
        Assert.Equal("builtin/mail_kinds/letter", input.Defs[2].Location.Path);
        Assert.Empty(input.PriorFailures);
        Assert.Equal(1, input.Candidate.MailKinds.Count);
    }

    [Fact]
    [Trait("Id", "CDD-LOAD-4")]
    public void Build_ARuleOverTheAdapterThatReportsAnError_WithholdsTheSet()
    {
        RuleSpec rule = RuleSpec.Create<ContentValidationInput>(
            "CV-TST-91", "test-only", "TST-RULE-91", RuleSeverity.Error, ValidationStage.References,
            ImmutableArray.Create("tst.kinds"),
            (input, ctx) =>
            {
                foreach (MailKindDef k in input.Candidate.MailKinds.AsSpan())
                {
                    ctx.Report(new DefRef(DefType.MailKind, k.Id), SourceLocation.BuiltIn("builtin/mail_kinds/" + k.Id), k.Id, "none", "tst.kinds");
                }
            });
        RuleRegistry rules = RuleRegistry.Compose(General.Rules, ImmutableArray.Create(rule));

        ContentBuildResult r = ContentFixtures.MinimalValid().Build(rules);

        Assert.Null(r.Set);
        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal("CV-TST-91", d.RuleId);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void Build_InvertedStatBounds_ReportsCvGen15_AndWithholdsTheSet()
    {
        ContentBuildResult r = ContentFixtures.MinimalValid().WithBalance("throw.rangeMinPermille", 1_501).Build();

        Assert.Null(r.Set);
        Assert.Contains(r.Validation.Diagnostics, d => d.RuleId == GeneralRuleIds.StatBoundsInverted);
    }

    [Fact]
    [Trait("Id", "CD21")]
    public void Build_DuplicateIds_AreReportedAsCvGen01_NotThrown_AndEmptyIdsStillThrow()
    {
        // Duplicates are CV-GEN-01 findings (story 009); the build is refused (no set), nothing throws.
        ContentBuildResult duplicate = ContentFixtures.MinimalValid().Add(ContentFixtures.Letter).Build();
        Assert.Null(duplicate.Set);
        Assert.Contains(duplicate.Validation.Diagnostics, d => d.RuleId == GeneralRuleIds.IdsAndReferences);

        // Empty ids are CV-GEN-02, a loader schema rule (story 018): the loader reports them before Build, which still rejects them.
        // DefIdAssigner asserts first (SimAssertException when asserts are on); without asserts the pipeline reports ArgumentException.
        Action empty = () => ContentFixtures.MinimalValid().Add(ContentFixtures.Plank with { Id = string.Empty }).Build();
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Throws<SimAssertException>(empty);
        }
        else
        {
            Assert.Throws<ArgumentException>(empty);
        }

        Assert.Throws<ArgumentNullException>(() => ContentPipeline.Build(null!, default, default, default, default, default, RuleRegistry.CreateDefault()));
        Assert.Throws<ArgumentNullException>(() => BuiltInContent.BuildM1a(null!));
    }

    // ------------------------------------------------------------ balance defaults (TD-047)

    [Fact]
    [Trait("Id", "CD9")]
    public void Registry_WithTheDefaultEntries_BuildsWithNoDiagnostics_AndEveryRowHasExactlyOneEntry()
    {
        BalanceRegistry registry = BalanceKeys.CreateRegistry();
        ImmutableArray<BalanceEntry> defaults = BalanceKeys.CreateDefaultEntries();

        BalanceBuildResult r = BalanceTable.Build(registry, defaults);

        Assert.Empty(r.Validation.Diagnostics);   // no CV-GEN-05 (missing), CV-GEN-13 (range) or CV-GEN-20 (outside safe)
        Assert.NotNull(r.Table);
        Assert.Equal(registry.Count, defaults.Length);
        Assert.Equal(registry.Names.ToHashSet(), defaults.Select(e => e.Name).ToHashSet());
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void DefaultBalance_ResolvesEveryStatBound_AndRespawnIsAtLeastItsFloor()
    {
        BalanceTable table = BalanceTable.Build(BalanceKeys.CreateRegistry(), BalanceKeys.CreateDefaultEntries()).Table!;

        StatBoundsResult bounds = StatDefs.ResolveBounds(table);

        Assert.True(bounds.Validation.CanBuild);
        BalanceKey respawn = table.Registry.Find("player.respawnMs");
        BalanceKey floor = table.Registry.Find("player.respawnMinMs");
        Assert.True(table.Get(respawn) >= table.Get(floor));
        Assert.Equal(new StatBounds(Stat.ThrowRangePermille, 1000, 1500), bounds.Get(Stat.ThrowRangePermille));
        Assert.Equal(16, bounds.Get(Stat.InventoryRows).Max);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void EveryRegisteredRow_FitsAnInt_SoTheOwnerRecordsCanNarrowWithoutOverflow()
    {
        BalanceRegistry registry = BalanceKeys.CreateRegistry();

        Assert.All(registry.Rows, r =>
        {
            Assert.InRange(r.HardMin, int.MinValue, int.MaxValue);
            Assert.InRange(r.HardMax, int.MinValue, int.MaxValue);
        });
    }

    [Fact]
    [Trait("Id", "CD4")]
    public void SecondsKeys_HoldMilliseconds()
    {
        BalanceRegistry registry = BalanceKeys.CreateRegistry();

        BalanceRow[] seconds = registry.Rows.Where(r => r.Key.EndsWith("Seconds", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(seconds);
        Assert.All(seconds, r => Assert.Equal(BalanceUnit.Ms, r.Unit));
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void ResolveHandles_ComeFromTheRegistry_AndAnOwnerResolveWithoutItsRowsThrows()
    {
        BalanceRegistry registry = BalanceKeys.CreateRegistry();

        PlayerKeys keys = BalanceKeys.Player.Resolve(registry);

        Assert.Equal(registry.Find("player.ticksAccel"), keys.TicksAccel);
        Assert.Equal(registry.Find("throw.rank.water"), BalanceKeys.Throw.Resolve(registry).RankWater);
        BalanceRegistry simOnly = BalanceKeys.Compose(BalanceKeys.Sim.Rows);
        Assert.Throws<ArgumentException>(() => BalanceKeys.Player.Resolve(simOnly));
        Assert.Throws<ArgumentNullException>(() => BalanceKeys.Inventory.Resolve(null!));
        Assert.Throws<ArgumentNullException>(() => PlayerBalance.FromTable(null!));
    }

    // ------------------------------------------------------------ pinned balance defaults

    // Every registered key with its unit and M-1a default (already-scaled integers, CD4; ...Seconds keys hold ms). The values come
    // from the owner GDDs' Tuning Knobs tables (story 008 Implementation Notes): Player Character, Grid Inventory, Mail and
    // Destinations, Throwing and Catching, Manual Delivery (CD12) and Simulation Core. Editing a default means editing this table
    // on purpose, with the owner GDD.
    private static readonly (string Key, BalanceUnit Unit, long Value)[] _pinnedDefaults =
    {
        ("delivery.insertPmMax", BalanceUnit.Permille, 750),
        ("inventory.weightPoints.light", BalanceUnit.Count, 1),
        ("inventory.weightPoints.medium", BalanceUnit.Count, 3),
        ("inventory.weightPoints.heavy", BalanceUnit.Count, 8),
        ("inventory.ptsPermille", BalanceUnit.Permille, 10),
        ("inventory.weightSpeedFloor", BalanceUnit.Permille, 600),
        ("inventory.maxRows", BalanceUnit.Count, 16),
        ("inventory.worldItemDespawnSeconds", BalanceUnit.Ms, 300_000),
        ("inventory.worldItemCap", BalanceUnit.Count, 512),
        ("inventory.capEvictMinAgeSeconds", BalanceUnit.Ms, 10_000),
        ("inventory.waterReturnSeconds", BalanceUnit.Ms, 10_000),
        ("inventory.externalLeashMeters", BalanceUnit.Mm, 3000),
        ("inventory.backlogDrainPerTick", BalanceUnit.Count, 32),
        ("inventory.manifestCellThreshold", BalanceUnit.Count, 160),
        ("inventory.containerCapacity", BalanceUnit.Count, 64),
        ("inventory.entryPoolCapacity", BalanceUnit.Count, 8192),
        ("inventory.worldItemCapacity", BalanceUnit.Count, 1024),
        ("inventory.mailIdArenaUnits", BalanceUnit.Count, 32_768),
        ("mail.payMultMaxPm", BalanceUnit.Permille, 5000),
        ("mail.penaltyMultMaxPm", BalanceUnit.Permille, 2000),
        ("mail.lateValueRatio", BalanceUnit.Permille, 500),
        ("mail.misdeliveryPenaltyRatio", BalanceUnit.Permille, 500),
        ("mail.deadLetterShifts", BalanceUnit.Count, 2),
        ("mail.minPenaltyCents", BalanceUnit.Cents, 1),
        ("mail.doorComplaintCap", BalanceUnit.MilliPoints, 20_000),
        ("mail.doorComplaintWindowSeconds", BalanceUnit.Ms, 30_000),
        ("mail.lateComplaint", BalanceUnit.MilliPoints, 2000),
        ("mail.lateComplaintWindowSeconds", BalanceUnit.Ms, 30_000),
        ("mail.teamComplaintCap", BalanceUnit.MilliPoints, 40_000),
        ("mail.closeGraceTicks", BalanceUnit.Ticks, 90),
        ("mail.distanceStepPm", BalanceUnit.Permille, 250),
        ("mail.shiftStepPm", BalanceUnit.Permille, 100),
        ("player.walkMmPerSec", BalanceUnit.MmPerSec, 5000),
        ("player.sprintMmPerSec", BalanceUnit.MmPerSec, 7500),
        ("player.swimMmPerSec", BalanceUnit.MmPerSec, 2000),
        ("player.ticksAccel", BalanceUnit.Ticks, 4),
        ("player.ticksDecel", BalanceUnit.Ticks, 3),
        ("player.airControlPm", BalanceUnit.Permille, 500),
        ("player.turnDegPerSec", BalanceUnit.DegPerSec, 720),
        ("player.g", BalanceUnit.MmPerSecSq, 24_000),
        ("player.jumpHeightMm", BalanceUnit.Mm, 1200),
        ("player.extraJumpPm", BalanceUnit.Permille, 900),
        ("player.coyoteTicks", BalanceUnit.Ticks, 4),
        ("player.jumpBufferTicks", BalanceUnit.Ticks, 4),
        ("player.stepUpMm", BalanceUnit.Mm, 400),
        ("player.capsuleRadiusMm", BalanceUnit.Mm, 300),
        ("player.capsuleHeightMm", BalanceUnit.Mm, 1400),
        ("player.swimEnterMm", BalanceUnit.Mm, 900),
        ("player.swimExitMm", BalanceUnit.Mm, 700),
        ("player.maxWadeSlowPm", BalanceUnit.Permille, 200),
        ("player.haulOutMm", BalanceUnit.Mm, 1200),
        ("player.haulOutTicks", BalanceUnit.Ticks, 15),
        ("player.rangeMm", BalanceUnit.Mm, 2500),
        ("player.incumbentSlackMm", BalanceUnit.Mm, 400),
        ("player.focusConePm", BalanceUnit.Permille, 900),
        ("player.nearMm", BalanceUnit.Mm, 800),
        ("player.nearAxisMm", BalanceUnit.Mm, 150),
        ("player.wDist", BalanceUnit.Count, 400),
        ("player.wAng", BalanceUnit.Count, 600),
        ("player.stickyPm", BalanceUnit.Permille, 60),
        ("player.focusDwellTicks", BalanceUnit.Ticks, 4),
        ("player.minHoldTicks", BalanceUnit.Ticks, 6),
        ("player.rangeSlackMm", BalanceUnit.Mm, 500),
        ("player.rangeHistoryTicks", BalanceUnit.Ticks, 9),
        ("player.positionStaleTicks", BalanceUnit.Ticks, 30),
        ("player.claimToleranceMm", BalanceUnit.Mm, 1500),
        ("player.supportSpeedCapMm", BalanceUnit.MmPerSec, 12_000),
        ("player.pushCapMm", BalanceUnit.Mm, 60),
        ("player.pushSharePm", BalanceUnit.Permille, 500),
        ("player.pushDeadMm", BalanceUnit.Mm, 10),
        ("player.ghostAfterTicks", BalanceUnit.Ticks, 45),
        ("player.buffCapPm", BalanceUnit.Permille, 400),
        ("player.maxExtraJumps", BalanceUnit.Count, 1),
        ("player.respawnMinMs", BalanceUnit.Ms, 1000),
        ("player.respawnMs", BalanceUnit.Ms, 10_000),
        ("player.terminalMmS", BalanceUnit.MmPerSec, 20_000),
        ("player.slopeMaxPm", BalanceUnit.Permille, 700),
        ("player.subStepMm", BalanceUnit.Mm, 300),
        ("player.maxSubStepsPerTick", BalanceUnit.Count, 4),
        ("player.slideIterations", BalanceUnit.Count, 3),
        ("player.chestMm", BalanceUnit.Mm, 1000),
        ("player.maxAnchors", BalanceUnit.Count, 4096),
        ("player.maxCarrierCandidates", BalanceUnit.Count, 64),
        ("throw.throwSpeed.light", BalanceUnit.MmPerSec, 14_000),
        ("throw.throwSpeed.medium", BalanceUnit.MmPerSec, 10_000),
        ("throw.throwSpeed.heavy", BalanceUnit.MmPerSec, 6000),
        ("throw.g", BalanceUnit.MmPerSecSq, 9800),
        ("throw.loftDeg", BalanceUnit.MilliDeg, 6000),
        ("throw.maxFlightTicks", BalanceUnit.Ticks, 90),
        ("throw.throwCooldownTicks", BalanceUnit.Ticks, 5),
        ("throw.throwBurstTicks", BalanceUnit.Ticks, 5),
        ("throw.handHeightMm", BalanceUnit.Mm, 1200),
        ("throw.fwdOffMm", BalanceUnit.Mm, 300),
        ("throw.sideOffMm", BalanceUnit.Mm, 250),
        ("throw.damagePerPoint", BalanceUnit.Count, 5),
        ("throw.dropPushbackMm", BalanceUnit.Mm, 500),
        ("throw.enemyHitSlackMm", BalanceUnit.Mm, 300),
        ("throw.previewSettleMs", BalanceUnit.Ms, 0),
        ("throw.rangeMinPermille", BalanceUnit.Permille, 1000),
        ("throw.rangeMaxPermille", BalanceUnit.Permille, 1500),
        ("throw.catchRadiusMm", BalanceUnit.Mm, 1500),
        ("throw.catchPredictMs", BalanceUnit.Ms, 500),
        ("throw.catchTapMs", BalanceUnit.Ms, 500),
        ("throw.catchHistoryMaxOffsetMm", BalanceUnit.Mm, 1500),
        ("throw.bonkRadiusMm", BalanceUnit.Mm, 300),
        ("throw.relayWindowTicks", BalanceUnit.Ticks, 150),
        ("throw.rank.armedCatcher", BalanceUnit.Count, 0),
        ("throw.rank.mailbox", BalanceUnit.Count, 1),
        ("throw.rank.goon", BalanceUnit.Count, 2),
        ("throw.rank.teammate", BalanceUnit.Count, 3),
        ("throw.rank.belt", BalanceUnit.Count, 4),
        ("throw.rank.solid", BalanceUnit.Count, 5),
        ("throw.rank.ground", BalanceUnit.Count, 6),
        ("throw.rank.water", BalanceUnit.Count, 7),
        ("sim.opQueueCapacity", BalanceUnit.Count, 4096),
        ("sim.systemOpReserve", BalanceUnit.Count, 64),
        ("sim.maxOpsDrainedPerTick", BalanceUnit.Count, 64),
        ("sim.eventLogCapacity", BalanceUnit.Count, 4096),
        ("sim.eventLogReserve", BalanceUnit.Count, 256),
        ("sim.maxCatchUpTicksPerFrame", BalanceUnit.Count, 4),
        ("sim.checkRingCapacity", BalanceUnit.Count, 256),
        ("sim.warmupTicks", BalanceUnit.Ticks, 300),
        ("sim.modifierPoolPerTeam", BalanceUnit.Count, 128),
        ("sim.modifierPoolPerPlayer", BalanceUnit.Count, 64),
        ("sim.mailTablePages", BalanceUnit.Count, 64),
    };

    [Fact]
    [Trait("Id", "CD9")]
    public void DefaultEntries_EveryKeyOfEveryOwner_HasItsPinnedUnitAndValue()
    {
        BalanceRegistry registry = BalanceKeys.CreateRegistry();
        ImmutableArray<BalanceEntry> defaults = BalanceKeys.CreateDefaultEntries();

        Assert.Equal(_pinnedDefaults.Length, registry.Count);
        Assert.Equal(_pinnedDefaults.Select(d => d.Key).OrderBy(k => k, StringComparer.Ordinal), registry.Names);
        foreach ((string key, BalanceUnit unit, long value) in _pinnedDefaults)
        {
            Assert.Equal(unit, registry[registry.Find(key)].Unit);
            Assert.Equal(value, defaults.Single(e => e.Name == key).Value);
        }

        Assert.All(new[] { "delivery", "inventory", "mail", "player", "sim", "throw" }, o => Assert.Contains(_pinnedDefaults, d => d.Key.StartsWith(o + ".", StringComparison.Ordinal)));
        Assert.Contains(_pinnedDefaults, d => d is ("delivery.insertPmMax", BalanceUnit.Permille, 750));
        Assert.Contains(_pinnedDefaults, d => d is ("mail.doorComplaintCap", BalanceUnit.MilliPoints, 20_000));
        Assert.Contains(_pinnedDefaults, d => d is ("mail.doorComplaintWindowSeconds", BalanceUnit.Ms, 30_000));
        Assert.Contains(_pinnedDefaults, d => d is ("mail.lateComplaint", BalanceUnit.MilliPoints, 2_000));
        Assert.Contains(_pinnedDefaults, d => d is ("mail.lateComplaintWindowSeconds", BalanceUnit.Ms, 30_000));
    }

    // ------------------------------------------------------------ owner records

    [Fact]
    [Trait("Id", "CD9")]
    public void OwnerRecords_HoldTheStoryValues()
    {
        ContentSet c = BuiltInContent.CreateM1a();

        PlayerBalance p = c.Player;
        Assert.Equal((4, 3, 500, 720, 24_000, 1_200), (p.TicksAccel, p.TicksDecel, p.AirControlPm, p.TurnDegPerSec, p.G, p.JumpHeightMm));
        Assert.Equal((5_000, 7_500, 2_000), (p.WalkMmPerSec, p.SprintMmPerSec, p.SwimMmPerSec));
        Assert.Equal((2_500, 6, 500, 30), (p.RangeMm, p.MinHoldTicks, p.RangeSlackMm, p.PositionStaleTicks));
        Assert.Equal((400, 1, 10_000, 1_000), (p.BuffCapPm, p.MaxExtraJumps, p.RespawnMs, p.RespawnMinMs));

        InventoryBalance i = c.Inventory;
        Assert.Equal((1, 3, 8, 10, 600, 16), (i.WeightPointsLight, i.WeightPointsMedium, i.WeightPointsHeavy, i.PtsPermille, i.WeightSpeedFloor, i.MaxRows));
        Assert.Equal((300_000, 512, 10_000, 10_000, 3_000, 32, 160), (i.WorldItemDespawnSeconds, i.WorldItemCap, i.CapEvictMinAgeSeconds, i.WaterReturnSeconds, i.ExternalLeashMeters, i.BacklogDrainPerTick, i.ManifestCellThreshold));

        MailBalance m = c.Mail;
        Assert.Equal((500, 500, 2, 1, 90, 250, 100), (m.LateValueRatio, m.MisdeliveryPenaltyRatio, m.DeadLetterShifts, m.MinPenaltyCents, m.CloseGraceTicks, m.DistanceStepPm, m.ShiftStepPm));
        Assert.Equal((5_000, 2_000), (m.PayMultMaxPm, m.PenaltyMultMaxPm));

        ThrowBalance t = c.Throw;
        Assert.Equal((14_000, 10_000, 6_000, 9_800, 6_000, 90), (t.ThrowSpeedLight, t.ThrowSpeedMedium, t.ThrowSpeedHeavy, t.G, t.LoftDeg, t.MaxFlightTicks));
        Assert.Equal((5, 5, 1_200, 300, 250, 5, 500, 300, 0), (t.ThrowCooldownTicks, t.ThrowBurstTicks, t.HandHeightMm, t.FwdOffMm, t.SideOffMm, t.DamagePerPoint, t.DropPushbackMm, t.EnemyHitSlackMm, t.PreviewSettleMs));
        Assert.Equal((1_000, 1_500, 1_500, 500, 500, 1_500, 300, 150), (t.RangeMinPermille, t.RangeMaxPermille, t.CatchRadiusMm, t.CatchPredictMs, t.CatchTapMs, t.CatchHistoryMaxOffsetMm, t.BonkRadiusMm, t.RelayWindowTicks));
        Assert.NotEqual(p.G, t.G);   // player.g and throw.g stay distinct keys
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void OwnerRecords_SatisfyTheOwnerValidatorInvariants()
    {
        ContentSet c = BuiltInContent.CreateM1a();
        ThrowBalance t = c.Throw;

        Assert.True(0 < t.ThrowSpeedHeavy && t.ThrowSpeedHeavy < t.ThrowSpeedMedium && t.ThrowSpeedMedium < t.ThrowSpeedLight);
        Assert.True(t.FwdOffMm >= c.Player.CapsuleRadiusMm);
        Assert.True(c.Player.RangeMm + c.Player.RangeSlackMm <= c.Inventory.ExternalLeashMeters);   // Player Character rule 1: an open never succeeds outside the leash
        int[] ranks =
        {
            t.RankArmedCatcher, t.RankMailbox, t.RankGoon, t.RankTeammate, t.RankBelt, t.RankSolid, t.RankGround, t.RankWater,
        };
        Assert.Equal(Enumerable.Range(0, 8), ranks.OrderBy(x => x));   // the receiver rank table is a permutation of the 8 kinds
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, ranks);
        Assert.True(c.Player.RespawnMs >= c.Player.RespawnMinMs);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Settings_AreTheSimSettingsDefaults_AndTheOwnerRecordsEqualTheirFromTable()
    {
        ContentSet c = BuiltInContent.CreateM1a();

        Assert.Equal(SimSettings.CreateDefault(), c.Settings);
        Assert.Equal(PlayerBalance.FromTable(c.Balance), c.Player);
        Assert.Equal(InventoryBalance.FromTable(c.Balance), c.Inventory);
        Assert.Equal(MailBalance.FromTable(c.Balance), c.Mail);
        Assert.Equal(ThrowBalance.FromTable(c.Balance), c.Throw);
        Assert.Equal(StatDefs.Count, c.ResolvedStatBounds.Length);
    }

    // ------------------------------------------------------------ TD-044 item 5 obligations

    [Fact]
    [Trait("Id", "CD21")]
    public void Defs_HonourTheLoaderObligations_NoDefaultArraysConsistentFlagsNoEmptyVolume()
    {
        ContentSet c = BuiltInContent.CreateM1a();

        foreach (ContainerDef d in c.Containers.AsSpan())
        {
            Assert.False(d.AllowedCategories.IsDefault || d.ReservedSlots.IsDefault);
            Assert.Equal(d.HasAllowedCategories, !d.AllowedCategories.IsEmpty);
            Assert.True(d.HasGrid == (d.GridW > 0 && d.GridH > 0));
            Assert.False(string.IsNullOrEmpty(d.Id));
        }

        foreach (ItemDef d in c.Items.AsSpan())
        {
            Assert.False(d.Tags.IsDefault);
            Assert.Equal(d.HasBuyPrice, d.BuyPriceCents != 0);
            Assert.False(string.IsNullOrEmpty(d.Id));
        }

        Assert.All(c.MailKinds.AsSpan().ToArray(), k => Assert.False(k.AcceptedBy.IsDefault));
        foreach (DestinationTypeDef d in c.DestinationTypes.AsSpan())
        {
            Assert.False(d.Accepts.IsDefault);
            Assert.NotEqual(default, d.CatchVolume);
            Assert.True(d.CatchVolume.WidthMm > 0 && d.CatchVolume.HeightMm > 0 && d.CatchVolume.DepthMm > 0);
        }

        foreach (MailMixDef m in c.MailMixes.AsSpan())
        {
            Assert.False(m.Shifts.IsDefault);
            Assert.All(m.Shifts, s => Assert.False(s.Shares.IsDefault));
        }
    }

    // ------------------------------------------------------------ immutability

    [Fact]
    [Trait("Id", "CD21")]
    public void ContentSet_AndEverythingReachableFromIt_IsImmutable()
    {
        var seen = new HashSet<Type>();
        var violations = new List<string>();

        Visit(typeof(ContentSet), seen, violations);

        Assert.Empty(violations);
        Assert.Contains(typeof(MailKindDef), seen);   // the scan really reached the defs
        Assert.Contains(typeof(ShiftMix), seen);
        Assert.Contains(typeof(PlayerBalance), seen);
        Assert.Contains(typeof(SimCoreSettings), seen);
    }

    [Fact]
    [Trait("Id", "CD21")]
    public void ImmutabilityScan_FlagsAMutableShape()
    {
        var violations = new List<string>();

        Visit(typeof(BadShape), new HashSet<Type>(), violations);

        Assert.Equal(5, violations.Count);
    }

    private sealed class BadShape
    {
        public int Setter { get; set; }

        public List<int> Items { get; init; } = new();

        public int[] Array { get; init; } = System.Array.Empty<int>();

#pragma warning disable CS0649 // the fields are only read by reflection
        public int Field;
        public readonly List<int> ReadOnlyFieldHoldingAList = new();
#pragma warning restore CS0649
    }

    private static void Visit(Type t, HashSet<Type> seen, List<string> violations)
    {
        if (!seen.Add(t))
        {
            return;
        }

        foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!f.IsInitOnly)
            {
                violations.Add(t.Name + "." + f.Name + ": public mutable field");
            }

            if (IsMutableCollection(f.FieldType))
            {
                violations.Add(t.Name + "." + f.Name + ": mutable collection " + f.FieldType.Name);
            }

            foreach (Type next in Expand(f.FieldType))
            {
                Visit(next, seen, violations);
            }
        }

        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            MethodInfo? set = p.SetMethod;
            if (set is { IsPublic: true } && !set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
            {
                violations.Add(t.Name + "." + p.Name + ": public setter");
            }

            if (IsMutableCollection(p.PropertyType))
            {
                violations.Add(t.Name + "." + p.Name + ": mutable collection " + p.PropertyType.Name);
            }

            foreach (Type next in Expand(p.PropertyType))
            {
                Visit(next, seen, violations);
            }
        }
    }

    private static IEnumerable<Type> Expand(Type t)
    {
        if (t.IsArray || t.IsByRef)
        {
            yield break;
        }

        if (t.IsGenericType)
        {
            foreach (Type a in t.GetGenericArguments().SelectMany(Expand))
            {
                yield return a;
            }

            if (!t.Namespace!.StartsWith("PerformativeMail", StringComparison.Ordinal))
            {
                yield break;
            }
        }

        if (t.Namespace is { } ns && ns.StartsWith("PerformativeMail", StringComparison.Ordinal) && !t.IsEnum)
        {
            yield return t;
        }
    }

    private static bool IsMutableCollection(Type t)
    {
        if (t.IsArray)
        {
            return true;
        }

        if (t == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(t))
        {
            return false;
        }

        // ImmutableArray<T> and the read-only interfaces are allowed; any other enumerable is only allowed when it cannot be written through.
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
        {
            return false;
        }

        return t.GetInterfaces().Concat(new[] { t }).Any(i =>
            i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(ICollection<>) || i.GetGenericTypeDefinition() == typeof(IDictionary<,>)));
    }

    // ------------------------------------------------------------ determinism

    [Fact]
    [Trait("Id", "CDD-LOAD-4")]
    public void CreateM1a_CalledTwice_GivesFieldForFieldEqualSets_NotTheSameInstance()
    {
        ContentSet a = BuiltInContent.CreateM1a();
        ContentSet b = BuiltInContent.CreateM1a();

        Assert.NotSame(a, b);
        Assert.Empty(ContentWalk.Diff(a, b));
        Assert.True(ContentWalk.CountLeaves(a) > 300);   // the walk really covers the tables and every balance value
    }

    [Fact]
    [Trait("Id", "CDD-LOAD-4")]
    public void ContentWalk_ReportsEachKindOfDifference()
    {
        ContentDraft baseDraft = ContentFixtures.MinimalValid();
        ContentSet a = baseDraft.Build().Set!;

        ContentSet defChanged = baseDraft.Replace(ContentFixtures.Letter with { MaxStack = 21 }).Build().Set!;
        ContentSet balanceChanged = baseDraft.WithBalance("player.ticksAccel", 5).Build().Set!;
        ContentSet listChanged = baseDraft.Replace(ContentFixtures.Letter with { AcceptedBy = ImmutableArray.Create(DefIndex.FromValue(0), DefIndex.FromValue(0)) }).Build().Set!;
        ContentSet longer = baseDraft.Add(ContentFixtures.Letter with { Id = "postcard", AcceptedBy = ImmutableArray<DefIndex>.Empty }).Build().Set!;

        Assert.Contains("$.MailKinds[0].MaxStack: 20 vs 21", ContentWalk.Diff(a, defChanged));
        Assert.Contains(ContentWalk.Diff(a, balanceChanged), x => x.StartsWith("$.Balance.player.ticksAccel: 4 vs 5", StringComparison.Ordinal));
        Assert.Contains(ContentWalk.Diff(a, balanceChanged), x => x.StartsWith("$.Player.TicksAccel", StringComparison.Ordinal));
        Assert.Contains(ContentWalk.Diff(a, listChanged), x => x.Contains("AcceptedBy: length 1 vs 2", StringComparison.Ordinal));
        Assert.Contains(ContentWalk.Diff(a, longer), x => x.Contains("MailKinds: length 1 vs 2", StringComparison.Ordinal));
        Assert.Single(ContentWalk.Diff(a, new object()));   // a type mismatch is reported, not walked
    }

    // ------------------------------------------------------------ fixtures and overlay (stories 009 to 012)

    [Fact]
    [Trait("Id", "CD21")]
    public void MinimalValid_BuildsWithZeroDiagnostics_UnderEveryRegisteredRule()
    {
        ContentBuildResult r = ContentFixtures.MinimalValid().Build();

        Assert.Empty(r.Validation.Diagnostics);
        ContentSet set = r.Set!;
        Assert.Equal(new[] { "cursor" }, Ids(set.Containers));
        Assert.Equal(new[] { "plank" }, Ids(set.Items));
        Assert.Equal(new[] { "letter" }, Ids(set.MailKinds));
        Assert.Equal(new[] { "house" }, Ids(set.DestinationTypes));
        Assert.Equal(new[] { "default" }, Ids(set.MailMixes));
    }

    [Fact]
    [Trait("Id", "CD21")]
    public void Overlay_ReplacesOneDefById_AndLeavesTheBaseDraftUntouched()
    {
        ContentDraft baseDraft = ContentFixtures.MinimalValid();

        ContentDraft edited = baseDraft
            .Replace(ContentFixtures.Letter with { MaxStack = 99 })
            .Replace(ContentFixtures.Plank with { MaxStack = 7 })
            .Replace(ContentFixtures.Cursor with { GridW = 2 })
            .Replace(ContentFixtures.House with { ManualInsertHoldMs = 1 })
            .Replace(ContentFixtures.Mix with { BatchJitterMs = 0 });

        ContentSet set = edited.Build().Set!;
        Assert.Equal(99, set.MailKinds[DefIndex.FromValue(0)].MaxStack);
        Assert.Equal(7, set.Items[DefIndex.FromValue(0)].MaxStack);
        Assert.Equal(2, set.Containers[DefIndex.FromValue(0)].GridW);
        Assert.Equal(1, set.DestinationTypes[DefIndex.FromValue(0)].ManualInsertHoldMs);
        Assert.Equal(0, set.MailMixes[DefIndex.FromValue(0)].BatchJitterMs);
        Assert.Equal(20, baseDraft.Build().Set!.MailKinds[DefIndex.FromValue(0)].MaxStack);
        Assert.Throws<ArgumentException>(() => baseDraft.Replace(ContentFixtures.Letter with { Id = "nope" }));
        Assert.Throws<ArgumentException>(() => baseDraft.Replace(ContentFixtures.Plank with { Id = "nope" }));
    }

    [Fact]
    [Trait("Id", "CD21")]
    public void Overlay_ReplacesOneBalanceValueByKey_AndAddsAndRemovesDefs()
    {
        ContentDraft baseDraft = ContentFixtures.MinimalValid();

        ContentSet set = baseDraft.WithBalance("player.ticksAccel", 7).Build().Set!;

        Assert.Equal(7, set.Player.TicksAccel);
        Assert.Equal(4, baseDraft.Build().Set!.Player.TicksAccel);
        Assert.Throws<ArgumentException>(() => baseDraft.WithBalance("player.noSuchKey", 1));
        Assert.Throws<InvalidOperationException>(() => baseDraft.WithBalance("player.ticksAccel", -1).Build());

        ContentDraft two = baseDraft.Add(ContentFixtures.Letter with { Id = "postcard", AcceptedBy = ImmutableArray<DefIndex>.Empty }).Add(ContentFixtures.Plank with { Id = "axe" });
        Assert.Equal(new[] { "letter", "postcard" }, Ids(two.Build().Set!.MailKinds).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(new[] { "axe", "plank" }, Ids(two.Build().Set!.Items));
        Assert.Equal(new[] { "postcard" }, Ids(two.RemoveMailKind("letter").Replace(ContentFixtures.Letter with { Id = "postcard" }).Build().Set!.MailKinds));
        Assert.Throws<ArgumentException>(() => baseDraft.RemoveMailKind("nope"));
    }
}
