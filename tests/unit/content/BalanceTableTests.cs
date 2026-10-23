using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 004 (TR-content-013; GDD CD9, CD11, CDD-SCHEMA-5): the <c>BalanceKey</c> registry,
/// <see cref="BalanceTable.Build"/> with its CV-GEN-05 / 13 / 20 findings, the 0 B <see cref="BalanceTable.Get"/>,
/// and <see cref="SimSettings"/> composition. Story 008 registers the <c>player</c> rows, so the AC1 cases that name
/// <c>player.rangeMm</c> use a test-only registry that adds that row to the production <c>sim.*</c> rows. Everything
/// is a fixed fixture; nothing is random, timed or read from disk.
/// </summary>
public class BalanceTableTests
{
    private static readonly SourceLocation _fileA = new("balance/player.jsonc", 3, 5);
    private static readonly SourceLocation _fileB = new("balance/player_extra.jsonc", 9, 5);

    /// <summary>The sim.* rows only: these tests exercise the framework against Simulation Core's table (the full registry is in BuiltInContentTests).</summary>
    private static BalanceRegistry SimRegistry() => BalanceKeys.Compose(BalanceKeys.Sim.Rows);

    /// <summary>The production sim rows plus a test-only <c>player.rangeMm</c> row (hard 100..20000 mm).</summary>
    private static BalanceRegistry TestRegistry() => BalanceKeys.Compose(
        BalanceKeys.Sim.Rows,
        ImmutableArray.Create(new BalanceRow("player", "rangeMm", BalanceUnit.Mm, 100, 20_000)));

    private static BalanceEntry Player(long value, string file = "player", SourceLocation? where = null, BalanceSafeRange? safe = null) =>
        new(file, "player.rangeMm", value, where ?? _fileA, safe);

    private static ImmutableArray<BalanceEntry> SimPlus(params BalanceEntry[] extra) =>
        SimSettings.DefaultEntries().AddRange(extra);

    private static BalanceBuildResult BuildTest(params BalanceEntry[] extra) =>
        BalanceTable.Build(TestRegistry(), SimPlus(extra));

    private static List<ContentDiagnostic> Of(BalanceBuildResult r, string ruleId) =>
        r.Validation.Diagnostics.Where(d => d.RuleId == ruleId).ToList();

    private static BalanceEntry Sim(string key) =>
        SimSettings.DefaultEntries().Single(e => e.Name == "sim." + key);

    private static ImmutableArray<BalanceEntry> SimWith(string key, BalanceEntry replacement)
    {
        ImmutableArray<BalanceEntry>.Builder b = ImmutableArray.CreateBuilder<BalanceEntry>();
        foreach (BalanceEntry e in SimSettings.DefaultEntries())
        {
            b.Add(e.Name == "sim." + key ? replacement : e);
        }

        return b.ToImmutable();
    }

    // ------------------------------------------------------------ AC-1: CV-GEN-13

    [Fact]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_ValidSimAndPlayerEntries_BuildsWithNoFindings()
    {
        BalanceBuildResult r = BuildTest(Player(5_000));

        Assert.Empty(r.Validation.Diagnostics);
        Assert.NotNull(r.Table);
        Assert.Equal(5_000, r.Table!.Get(r.Table.Registry.Find("player.rangeMm")));
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_PlayerRangeMmFromTwoSources_ReportsExactlyOneGen13AtTheSecond()
    {
        BalanceBuildResult r = BuildTest(Player(5_000, where: _fileA), Player(6_000, file: "player", where: _fileB));

        List<ContentDiagnostic> gen13 = Of(r, GeneralRuleIds.BalanceKey);
        Assert.Single(gen13);
        Assert.Equal(RuleSeverity.Error, gen13[0].Severity);
        Assert.Equal(_fileB, gen13[0].Location);
        Assert.Equal("player.rangeMm", gen13[0].InputKey);
        Assert.Single(r.Validation.Diagnostics);
        Assert.Null(r.Table);
        Assert.False(r.Validation.CanBuild);
    }

    [Theory]
    [InlineData("player", "sim")]
    [InlineData("sim", "player")]
    [InlineData("sim", "throw")]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_KeySuppliedFromTwoDifferentFiles_ReportsExactlyOneGen13(string firstFile, string secondFile)
    {
        // One mistake, one finding: the duplicate is reported, and neither supply is also reported as a wrong-owner file.
        BalanceBuildResult r = BuildTest(Player(5_000, file: firstFile, where: _fileA), Player(6_000, file: secondFile, where: _fileB));

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal(GeneralRuleIds.BalanceKey, d.RuleId);
        Assert.Equal(_fileB, d.Location);
        Assert.Contains("twice", d.Value);
        Assert.Null(r.Table);
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_KeySuppliedThreeTimes_ReportsOneGen13PerExtraSupply()
    {
        BalanceBuildResult r = BuildTest(Player(5_000), Player(6_000, where: _fileB), Player(7_000, file: "sim", where: _fileB));

        Assert.Equal(2, Of(r, GeneralRuleIds.BalanceKey).Count);
        Assert.Equal(2, r.Validation.Diagnostics.Length);
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_UnregisteredKey_ReportsExactlyOneGen13()
    {
        var stray = new BalanceEntry("sim", "sim.noSuchKey", 1, SourceLocation.BuiltIn("builtin/balance/sim"));

        BalanceBuildResult r = BalanceTable.Build(SimRegistry(), SimSettings.DefaultEntries().Add(stray));

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal(GeneralRuleIds.BalanceKey, d.RuleId);
        Assert.Equal("sim.noSuchKey", d.InputKey);
        Assert.Null(r.Table);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(20_001)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_ValueOutsideHardRange_ReportsExactlyOneGen13(long value)
    {
        BalanceBuildResult r = BuildTest(Player(value));

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal(GeneralRuleIds.BalanceKey, d.RuleId);
        Assert.Equal("100..20000", d.Allowed);
        Assert.Null(r.Table);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(20_000)]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_ValueOnTheHardRangeEdge_IsAccepted(long value)
    {
        BalanceBuildResult r = BuildTest(Player(value));

        Assert.Empty(r.Validation.Diagnostics);
        Assert.NotNull(r.Table);
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_KeySuppliedUnderAnotherOwnersFile_ReportsExactlyOneGen13_AndNoMissingFinding()
    {
        // player.rangeMm in the "sim" file: registered, in range, but the file prefix is not its registry owner.
        BalanceBuildResult r = BuildTest(Player(5_000, file: "sim"));

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal(GeneralRuleIds.BalanceKey, d.RuleId);
        Assert.Contains("player.rangeMm", d.Value);
        Assert.Contains("sim", d.Value);
        Assert.Equal("owner file player", d.Allowed);
        Assert.Null(r.Table);
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Gen13_IsAnError_AndGen20IsAWarning_InTheProductionRegistry()
    {
        RuleRegistry rules = RuleRegistry.CreateDefault();

        Assert.Equal(RuleSeverity.Error, rules.Find("CV-GEN-13")!.Severity);
        Assert.Equal(RuleSeverity.Warning, rules.Find("CV-GEN-20")!.Severity);
    }

    // ------------------------------------------------------------ AC-2: CV-GEN-20

    [Theory]
    [InlineData(1_999)]
    [InlineData(10_001)]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_ValueOutsideSafeRange_ReportsGen20WarningAndStillBuilds(long value)
    {
        BalanceBuildResult r = BuildTest(Player(value, safe: new BalanceSafeRange(2_000, 10_000)));

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal(GeneralRuleIds.BalanceOutsideSafe, d.RuleId);
        Assert.Equal(RuleSeverity.Warning, d.Severity);
        Assert.Equal("2000..10000", d.Allowed);
        Assert.Equal(1, r.Validation.WarningCount);
        Assert.Equal(0, r.Validation.ErrorCount);
        Assert.True(r.Validation.CanBuild);
        Assert.NotNull(r.Table);
        Assert.Equal(value, r.Table!.Get(r.Table.Registry.Find("player.rangeMm")));
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(10_000)]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_ValueOnTheSafeRangeEdge_HasNoFinding(long value)
    {
        BalanceBuildResult r = BuildTest(Player(value, safe: new BalanceSafeRange(2_000, 10_000)));

        Assert.Empty(r.Validation.Diagnostics);
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void Build_ValueOutsideHardAndSafe_ReportsOnlyTheHardError()
    {
        BalanceBuildResult r = BuildTest(Player(50, safe: new BalanceSafeRange(2_000, 10_000)));

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal(GeneralRuleIds.BalanceKey, d.RuleId);
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-5")]
    public void SafeRange_WithMinAboveMax_IsRejected_AndAnEqualPairIsAccepted()
    {
        Assert.Throws<ArgumentException>(() => new BalanceSafeRange(10, 9));
        Assert.Equal(7, new BalanceSafeRange(7, 7).Min);
        Assert.Equal(0, default(BalanceSafeRange).Max);
    }

    // ------------------------------------------------------------ AC-3: CV-GEN-05

    [Fact]
    [Trait("Id", "CD9")]
    public void Build_RegisteredKeyWithNoValue_ReportsGen05NamingOwnerDotKey_AndBuildsNothing()
    {
        ImmutableArray<BalanceEntry> entries = SimSettings.DefaultEntries().RemoveAll(e => e.Name == "sim.warmupTicks");

        BalanceBuildResult r = BalanceTable.Build(SimRegistry(), entries);

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal(GeneralRuleIds.RequiredFieldMissing, d.RuleId);
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.Equal("sim.warmupTicks", d.InputKey);
        Assert.Contains("sim.warmupTicks", d.Value);
        Assert.Equal("balance/sim.jsonc:0:0", d.Location.ToString());
        Assert.Null(r.Table);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Build_NoEntriesAtAll_ReportsOneGen05PerRegisteredKey_InOrdinalOrderWithNoDefaults()
    {
        BalanceRegistry registry = SimRegistry();

        BalanceBuildResult r = BalanceTable.Build(registry, ImmutableArray<BalanceEntry>.Empty);

        Assert.Equal(registry.Count, r.Validation.Diagnostics.Length);
        Assert.All(r.Validation.Diagnostics, d => Assert.Equal(GeneralRuleIds.RequiredFieldMissing, d.RuleId));
        Assert.Equal(
            registry.Rows.Select(x => x.Name).ToHashSet(),
            r.Validation.Diagnostics.Select(d => d.InputKey!).ToHashSet());
        Assert.Null(r.Table);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Build_DefaultArrayOfEntries_IsTreatedAsEmpty()
    {
        BalanceBuildResult r = BalanceTable.Build(SimRegistry(), default);

        Assert.Equal(SimRegistry().Count, r.Validation.ErrorCount);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Build_SameInputTwice_GivesIdenticalFindings()
    {
        ImmutableArray<BalanceEntry> entries = SimPlus(Player(50), Player(60, where: _fileB));

        string a = string.Join("|", BalanceTable.Build(TestRegistry(), entries).Validation.Diagnostics.Select(d => d.ToString()));
        string b = string.Join("|", BalanceTable.Build(TestRegistry(), entries).Validation.Diagnostics.Select(d => d.ToString()));

        Assert.Equal(a, b);
    }

    // ------------------------------------------------------------ AC-4: Get and ordinal order

    [Fact]
    [Trait("Id", "CD9")]
    public void Get_ReturnsTheSuppliedValueForEachSimHandle()
    {
        BalanceTable table = BalanceTable.Build(SimRegistry(), SimSettings.DefaultEntries()).Table!;
        SimKeys k = BalanceKeys.Sim.Resolve(table.Registry);

        Assert.Equal(4096, table.Get(k.OpQueueCapacity));
        Assert.Equal(64, table.Get(k.SystemOpReserve));
        Assert.Equal(64, table.Get(k.MaxOpsDrainedPerTick));
        Assert.Equal(4096, table.Get(k.EventLogCapacity));
        Assert.Equal(256, table.Get(k.EventLogReserve));
        Assert.Equal(4, table.Get(k.MaxCatchUpTicksPerFrame));
        Assert.Equal(256, table.Get(k.CheckRingCapacity));
        Assert.Equal(300, table.Get(k.WarmupTicks));
        Assert.Equal(128, table.Get(k.ModifierPoolPerTeam));
        Assert.Equal(64, table.Get(k.ModifierPoolPerPlayer));
        Assert.Equal(64, table.Get(k.MailTablePages));
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Get_IsAnIndexedReadOfValues_ByRegistryPosition()
    {
        BalanceTable table = BalanceTable.Build(SimRegistry(), SimSettings.DefaultEntries()).Table!;

        for (int i = 0; i < table.Registry.Count; i++)
        {
            Assert.Equal(table.Values[i], table.Get(table.Registry.Find(table.Registry.Rows[i].Name)));
        }

        Assert.Equal(table.Registry.Count, table.Values.Length);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Get_WithNone_FailsLoudly()
    {
        BalanceTable table = BalanceTable.Build(SimRegistry(), SimSettings.DefaultEntries()).Table!;

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => table.Get(BalanceKey.None));
            Assert.Equal(ContentAssertCode.BalanceKeyNone, thrown.Code);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => table.Get(BalanceKey.None));
        }
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Get_WithAHandleFromAnotherRegistry_IsCaught_NotReadAsAnotherRow()
    {
        // The test registry sorts player.rangeMm at index 0 and the production registry sorts sim.checkRingCapacity at 0,
        // so without the stamp a production handle would silently read player.rangeMm from the test table.
        BalanceTable testTable = BuildTest(Player(5_000)).Table!;
        BalanceTable prodTable = BalanceTable.Build(SimRegistry(), SimSettings.DefaultEntries()).Table!;
        BalanceKey fromProduction = BalanceKeys.Sim.Resolve(prodTable.Registry).CheckRingCapacity;
        BalanceKey fromTest = BalanceKeys.Sim.Resolve(testTable.Registry).CheckRingCapacity;

        Assert.NotEqual(fromProduction.Stamp, fromTest.Stamp);
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException a = Assert.Throws<SimAssertException>(() => testTable.Get(fromProduction));
            SimAssertException b = Assert.Throws<SimAssertException>(() => prodTable.Get(fromTest));
            Assert.Equal(ContentAssertCode.BalanceKeyForeign, a.Code);
            Assert.Equal(ContentAssertCode.BalanceKeyForeign, b.Code);
        }
        else
        {
            Assert.Throws<ArgumentException>(() => testTable.Get(fromProduction));
            Assert.Throws<ArgumentException>(() => prodTable.Get(fromTest));
        }

        Assert.Equal(256, testTable.Get(fromTest));
        Assert.Throws<ArgumentException>(() => testTable.Registry[fromProduction]);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Registry_Stamp_IsDeterministicNonZeroAndFollowsTheKeyLayout()
    {
        BalanceRegistry a = SimRegistry();
        BalanceRegistry b = SimRegistry();
        BalanceRegistry withPlayer = TestRegistry();
        BalanceRegistry reordered = new(Enumerable.Reverse(BalanceKeys.Sim.Rows.ToArray()).ToImmutableArray());

        Assert.NotEqual(0, a.Stamp);
        Assert.Equal(a.Stamp, b.Stamp);
        Assert.Equal(a.Stamp, reordered.Stamp);   // input order does not matter, ordinal layout does
        Assert.NotEqual(a.Stamp, withPlayer.Stamp);
        Assert.False(a.Owns(BalanceKey.None));
        Assert.True(a.Owns(a.Find("sim.warmupTicks")));
        Assert.False(a.Owns(withPlayer.Find("sim.warmupTicks")));
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Registry_Find_Index_IsTheOrdinalPosition_AndShiftsWhenAnotherOwnerRegisters()
    {
        BalanceRegistry prod = SimRegistry();
        BalanceRegistry withPlayer = TestRegistry();

        // Ordinal order of the eleven sim keys: checkRingCapacity, eventLogCapacity, eventLogReserve, mailTablePages, maxCatchUpTicksPerFrame,
        // maxOpsDrainedPerTick, modifierPoolPerPlayer, modifierPoolPerTeam, opQueueCapacity, systemOpReserve, warmupTicks.
        Assert.Equal(0, prod.Find("sim.checkRingCapacity").Index);
        Assert.Equal(10, prod.Find("sim.warmupTicks").Index);
        Assert.Equal(0, withPlayer.Find("player.rangeMm").Index);
        Assert.Equal(1, withPlayer.Find("sim.checkRingCapacity").Index);
        Assert.Equal(11, withPlayer.Find("sim.warmupTicks").Index);
        Assert.Equal(withPlayer.Count, prod.Count + 1);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void SimKeys_Resolve_GivesHandlesToTheNamedRows_AndRejectsARegistryWithoutSimRows()
    {
        BalanceRegistry registry = TestRegistry();
        SimKeys k = BalanceKeys.Sim.Resolve(registry);

        Assert.Equal("sim.opQueueCapacity", registry[k.OpQueueCapacity].Name);
        Assert.Equal("sim.systemOpReserve", registry[k.SystemOpReserve].Name);
        Assert.Equal("sim.maxOpsDrainedPerTick", registry[k.MaxOpsDrainedPerTick].Name);
        Assert.Equal("sim.eventLogCapacity", registry[k.EventLogCapacity].Name);
        Assert.Equal("sim.eventLogReserve", registry[k.EventLogReserve].Name);
        Assert.Equal("sim.maxCatchUpTicksPerFrame", registry[k.MaxCatchUpTicksPerFrame].Name);
        Assert.Equal("sim.checkRingCapacity", registry[k.CheckRingCapacity].Name);
        Assert.Equal("sim.warmupTicks", registry[k.WarmupTicks].Name);
        Assert.Equal("sim.modifierPoolPerTeam", registry[k.ModifierPoolPerTeam].Name);
        Assert.Equal("sim.modifierPoolPerPlayer", registry[k.ModifierPoolPerPlayer].Name);
        Assert.Equal("sim.mailTablePages", registry[k.MailTablePages].Name);
        var playerOnly = new BalanceRegistry(ImmutableArray.Create(new BalanceRow("player", "rangeMm", BalanceUnit.Mm, 100, 20_000)));
        Assert.Throws<ArgumentException>(() => BalanceKeys.Sim.Resolve(playerOnly));
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void BalanceRow_Name_FollowsAWithCopy()
    {
        var row = new BalanceRow("sim", "a", BalanceUnit.Count, 0, 9);

        Assert.Equal("sim.a", row.Name);
        Assert.Equal("sim.b", (row with { Key = "b" }).Name);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void BalanceKey_DefaultIsNone_AndNeverMeansRowZero()
    {
        BalanceRegistry registry = SimRegistry();
        BalanceKey first = registry.Find(registry.Rows[0].Name);

        Assert.True(default(BalanceKey).IsNone);
        Assert.Equal(-1, BalanceKey.None.Index);
        Assert.False(first.IsNone);
        Assert.Equal(0, first.Index);
        Assert.NotEqual(first, default);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Registry_EnumeratesInOrdinalOwnerDotKeyOrder_WhateverTheInputOrder()
    {
        BalanceRow[] rows =
        {
            new("sim", "b", BalanceUnit.Count, 0, 9),
            new("throw", "throwSpeed.light", BalanceUnit.Mm, 0, 9),
            new("sim", "B", BalanceUnit.Count, 0, 9),
            new("player", "rangeMm", BalanceUnit.Mm, 0, 9),
            new("sim", "a", BalanceUnit.Count, 0, 9),
        };

        var forward = new BalanceRegistry(ImmutableArray.Create(rows));
        var reversed = new BalanceRegistry(ImmutableArray.Create(Enumerable.Reverse(rows).ToArray()));

        string[] expected = { "player.rangeMm", "sim.B", "sim.a", "sim.b", "throw.throwSpeed.light" };   // ordinal: 'B' (66) before 'a' (97)
        Assert.Equal(expected, forward.Rows.Select(r => r.Name).ToArray());
        Assert.Equal(expected, reversed.Rows.Select(r => r.Name).ToArray());
        Assert.Equal(2, forward.Find("sim.a").Index);   // handle index == position (player.rangeMm 0, sim.B 1, sim.a 2)
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Registry_KeysWithDots_AreRegisteredVerbatimUnderTheFirstDotOwner()
    {
        var registry = new BalanceRegistry(ImmutableArray.Create(new BalanceRow("throw", "throwSpeed.light", BalanceUnit.Mm, 0, 9)));

        Assert.True(registry.TryFind("throw.throwSpeed.light", out BalanceKey key));
        Assert.Equal("throw", registry[key].Owner);
        Assert.Equal("throwSpeed.light", registry[key].Key);
        Assert.False(registry.TryFind("throwSpeed.light", out _));
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Registry_RejectsDuplicateEmptyAndInvertedRows()
    {
        BalanceRow Ok() => new("sim", "a", BalanceUnit.Count, 0, 9);

        Assert.Throws<ArgumentException>(() => new BalanceRegistry(ImmutableArray.Create(Ok(), Ok())));
        Assert.Throws<ArgumentException>(() => new BalanceRegistry(ImmutableArray.Create(new BalanceRow("", "a", BalanceUnit.Count, 0, 9))));
        Assert.Throws<ArgumentException>(() => new BalanceRegistry(ImmutableArray.Create(new BalanceRow("a.b", "a", BalanceUnit.Count, 0, 9))));
        Assert.Throws<ArgumentException>(() => new BalanceRegistry(ImmutableArray.Create(new BalanceRow("sim", "", BalanceUnit.Count, 0, 9))));
        Assert.Throws<ArgumentException>(() => new BalanceRegistry(ImmutableArray.Create(new BalanceRow("sim", "a", BalanceUnit.Count, 9, 0))));
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void SimRegistry_HoldsOnlySimRows_WithValidHardRangesAndDefaultsInsideThem()
    {
        // The sim.* defaults are checked against the sim-only registry; the production registry is the sim rows plus the other owners.
        BalanceRegistry registry = SimRegistry();
        BalanceRegistry production = BalanceKeys.CreateRegistry();

        Assert.Equal(11, registry.Count);
        Assert.True(production.Count > registry.Count);
        Assert.Equal(
            new[] { "delivery", "inventory", "mail", "player", "sim", "throw" },
            production.Rows.Select(r => r.Owner).Distinct().ToArray());
        Assert.All(registry.Rows, r => Assert.Equal("sim", r.Owner));
        foreach (BalanceEntry e in SimSettings.DefaultEntries())
        {
            BalanceRow row = registry[registry.Find(e.Name)];
            Assert.InRange(e.Value, row.HardMin, row.HardMax);
            if (e.Safe is { } safe)
            {
                Assert.InRange(e.Value, safe.Min, safe.Max);
                Assert.InRange(safe.Min, row.HardMin, row.HardMax);
                Assert.InRange(safe.Max, row.HardMin, row.HardMax);
            }
        }
    }

    // ------------------------------------------------------------ AC-5: SimSettings

    [Fact]
    [Trait("Id", "CD9")]
    public void SimSettings_BuiltFromTheDefaultRows_EqualsTheGddDefaults()
    {
        SimSettings settings = SimSettings.CreateDefault();

        Assert.Equal(new SimCoreSettings(), settings.Core);   // 4096, 64, 64, 4096, 256, 8, 4, 256
        Assert.Equal(300, settings.WarmupTicks);
        Assert.Equal(new StatsSettings { ModifierPoolPerTeam = 128, ModifierPoolPerPlayer = 64 }, settings.Stats);
        Assert.Equal(64, settings.MailTablePages);
        Assert.Equal(30, SimSettings.TickRateHz);
        Assert.Equal(8, settings.Core.MaxEventReaders);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void SimSettings_PropertyDefaults_AreTheSameNumbersAsTheBuiltInEntries()
    {
        // One source: the SimSettings / StatsSettings property defaults and DefaultEntries read the same constants.
        var bare = new SimSettings();
        BalanceTable table = BalanceTable.Build(SimRegistry(), SimSettings.DefaultEntries()).Table!;

        Assert.Equal(bare.WarmupTicks, SimSettings.FromTable(table).WarmupTicks);
        Assert.Equal(bare.Stats, SimSettings.FromTable(table).Stats);
        Assert.Equal(bare.Core, SimSettings.FromTable(table).Core);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void SimSettings_FillsEachMemberFromItsOwnRow()
    {
        // Ten distinct values so a swapped pair of keys shows up.
        long[] values = { 8192, 96, 48, 8192, 512, 6, 128, 450, 200, 100 };
        string[] keys =
        {
            "opQueueCapacity", "systemOpReserve", "maxOpsDrainedPerTick", "eventLogCapacity", "eventLogReserve",
            "maxCatchUpTicksPerFrame", "checkRingCapacity", "warmupTicks", "modifierPoolPerTeam", "modifierPoolPerPlayer",
        };
        ImmutableArray<BalanceEntry> entries = SimSettings.DefaultEntries();
        for (int i = 0; i < keys.Length; i++)
        {
            string name = "sim." + keys[i];
            long v = values[i];
            entries = entries.Select(e => e.Name == name ? e with { Value = v, Safe = null } : e).ToImmutableArray();
        }

        BalanceBuildResult r = BalanceTable.Build(SimRegistry(), entries);
        Assert.Empty(r.Validation.Diagnostics);

        SimSettings s = SimSettings.FromTable(r.Table!);

        Assert.Equal(8192, s.Core.OpQueueCapacity);
        Assert.Equal(96, s.Core.SystemOpReserve);
        Assert.Equal(48, s.Core.MaxOpsDrainedPerTick);
        Assert.Equal(8192, s.Core.EventLogCapacity);
        Assert.Equal(512, s.Core.EventLogReserve);
        Assert.Equal(6, s.Core.MaxCatchUpTicksPerFrame);
        Assert.Equal(128, s.Core.CheckRingCapacity);
        Assert.Equal(450, s.WarmupTicks);
        Assert.Equal(200, s.Stats.ModifierPoolPerTeam);
        Assert.Equal(100, s.Stats.ModifierPoolPerPlayer);
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void SimSettings_FromATableWithoutSimRows_Throws()
    {
        var registry = new BalanceRegistry(ImmutableArray.Create(new BalanceRow("player", "rangeMm", BalanceUnit.Mm, 100, 20_000)));
        BalanceTable table = BalanceTable.Build(registry, ImmutableArray.Create(Player(5_000))).Table!;

        Assert.Throws<ArgumentException>(() => SimSettings.FromTable(table));
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void SimSettings_IsImmutable_AndHasValueEquality()
    {
        SimSettings a = SimSettings.CreateDefault();
        SimSettings b = SimSettings.CreateDefault();

        Assert.Equal(a, b);
        Assert.NotEqual(a, a with { WarmupTicks = 301 });
        foreach (Type t in new[] { typeof(SimSettings), typeof(StatsSettings) })
        {
            foreach (System.Reflection.PropertyInfo p in t.GetProperties())
            {
                if (p.Name == "EqualityContract")
                {
                    continue;
                }

                // init-only: the setter's return parameter carries the IsExternalInit modifier.
                Assert.True(
                    p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers()
                        .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit"),
                    t.Name + "." + p.Name + " must be init-only");
            }
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(1024, true)]
    [InlineData(0, false)]
    [InlineData(1025, false)]
    [Trait("Id", "CD9")]
    public void Build_MailTablePages_HardRangeIs1To1024(long value, bool accepted)
    {
        ImmutableArray<BalanceEntry> entries = SimWith("mailTablePages", Sim("mailTablePages") with { Value = value, Safe = null });

        BalanceBuildResult r = BalanceTable.Build(SimRegistry(), entries);

        if (accepted)
        {
            Assert.Empty(r.Validation.Diagnostics);
            Assert.Equal((int)value, SimSettings.FromTable(r.Table!).MailTablePages);
        }
        else
        {
            ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
            Assert.Equal(GeneralRuleIds.BalanceKey, d.RuleId);
            Assert.Null(r.Table);
        }
    }

    [Fact]
    [Trait("Id", "CD9")]
    public void Build_SimValueOutsideItsSafeRange_WarnsAndSettingsStillBuild()
    {
        // opQueueCapacity safe range is 4096..16384: 2048 warns (Warning), still inside the hard range 8..1048576.
        ImmutableArray<BalanceEntry> entries = SimWith("opQueueCapacity", Sim("opQueueCapacity") with { Value = 2048 });

        BalanceBuildResult r = BalanceTable.Build(SimRegistry(), entries);

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal(GeneralRuleIds.BalanceOutsideSafe, d.RuleId);
        Assert.Equal(2048, SimSettings.FromTable(r.Table!).Core.OpQueueCapacity);
    }
}

/// <summary>The 0 B guardrail for <see cref="BalanceTable.Get"/>; joins the non-parallel collection so nothing else allocates meanwhile.</summary>
[Collection(NonParallelCollection.Name)]
public class BalanceTableAllocationTests
{
    [Fact]
    [Trait("Id", "CD9")]
    public void Get_AllocatesNothing()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(BalanceTable).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        BalanceTable table = BalanceTable.Build(BalanceKeys.Compose(BalanceKeys.Sim.Rows), SimSettings.DefaultEntries()).Table!;
        SimKeys keys = BalanceKeys.Sim.Resolve(table.Registry);
        BalanceKey a = keys.OpQueueCapacity;
        BalanceKey b = keys.WarmupTicks;
        long sink = 0;

        _ = AllocAssert.Zero("2,000 BalanceTable.Get calls", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                sink += table.Get(a) + table.Get(b);
            }
        });

        Assert.Equal(0, sink % 1);   // keeps sink live
        Assert.NotEqual(0, sink);
    }
}
