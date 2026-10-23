using System.Reflection;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Integration.Content;

/// <summary>
/// Content-data story 015 (TR-content-016, TR-content-017 rebuild-on-source-change, TR-content-018 RuleFlag half; GDD CD14 to
/// CD18, CDD-STAT-4 M-1a form, CDD-STAT-5, CDD-STAT-6/7/8 sheet halves, CDD-STAT-13): <see cref="IStatWriter"/> source identity
/// and versioning, every refusal of Push and Remove, the fixed pools, the three build paths, lifetimes as tags, stacks and
/// rule flags, the <c>profileId</c> to slot remap (TD-082 item 1) and the per-world composition. Sources are synthetic: no
/// Perk or Stamp system exists at M-1a. No random, no clock, no I/O.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class StatSheetWriterTests
{
    private const ushort None = StatSheet.NoQualifier;

    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    // ------------------------------------------------------------ helpers

    private sealed record Entry(ModifierSource Src, Modifier[] Mods);

    private sealed class Rig
    {
        public Rig(StatsSettings? settings = null)
        {
            Counters = new SimCounters();
            Writer = settings is null ? new StatSheetWriter(_content, Counters) : new StatSheetWriter(_content, settings, Counters);
        }

        public SimCounters Counters { get; }

        public StatSheetWriter Writer { get; }

        public IStatSheet Sheet => Writer.Sheet;

        public long Refused => Counters[CounterId.StatWriteRefused];

        public long PoolFull => Counters[CounterId.StatPoolFull];
    }

    private sealed class ProbeSystem : ISimSystem
    {
        public void Bind(SimWorldBuilder builder)
        {
        }
    }

    private static PlayerSlot Slot(int n) => PlayerSlot.FromValue((byte)n);

    private static ModifierOwner Mine(int slot) => ModifierOwner.ForSlot(Slot(slot));

    private static ModifierSource Src(ModifierOwner owner, ushort id = 1, byte stack = 0, byte max = 3, SourceKind kind = SourceKind.Perk, ModifierLifetime life = ModifierLifetime.Run, RuleFlagSet flags = default) =>
        new(kind, DefIndex.FromValue(id), stack, owner, max, life, flags);

    private static Modifier M(in ModifierSource s, Stat stat, ModifierOp op, int value, ushort qualifier = None) =>
        new(s.Kind, s.Id, s.StackIndex, s.Owner.Slot, stat, qualifier, op, value, s.Lifetime);

    private static Entry E(ModifierSource src, params Modifier[] mods) => new(src, mods);

    private static ushort Container(string id) => Index(_content.Containers.AsSpan(), c => c.Id, id);

    private static ushort MailKind(string id) => Index(_content.MailKinds.AsSpan(), k => k.Id, id);

    private static ushort Index<T>(ReadOnlySpan<T> table, Func<T, string> idOf, string id)
    {
        for (int i = 0; i < table.Length; i++)
        {
            if (idOf(table[i]) == id)
            {
                return (ushort)i;
            }
        }

        throw new InvalidOperationException("no def " + id);
    }

    private static bool Push(Rig rig, Entry e)
    {
        ModifierSource src = e.Src;
        return rig.Writer.Push(in src, e.Mods);
    }

    private static IStatSheet Pure(params Entry[] entries) => StatSheet.Build(_content, PureSources(entries));

    private static StatSources PureSources(IEnumerable<Entry> entries)
    {
        var mods = new List<Modifier>();
        var grants = new List<RuleFlagGrant>();
        foreach (Entry e in entries)
        {
            mods.AddRange(e.Mods);
            foreach (RuleFlag f in Enum.GetValues<RuleFlag>().Where(f => e.Src.Flags.Contains(f)))
            {
                grants.Add(new RuleFlagGrant(e.Src.Owner, f));
            }
        }

        return new StatSources(mods.ToArray(), grants.ToArray());
    }

    private static int QualifierCount(StatDef def) => def.Qualifier switch
    {
        QualifierKind.Container => _content.Containers.Count,
        QualifierKind.MailKind => _content.MailKinds.Count,
        _ => 1,
    };

    /// <summary>Every (stat, qualifier, team/slot) read plus every (flag, team/slot) Has, as one flat list.</summary>
    private static List<int> Sweep(IStatSheet sheet)
    {
        var all = new List<int>();
        for (int s = 1; s <= StatDefs.Count; s++)
        {
            StatDef def = StatDefs.Get((Stat)s);
            for (int q = 0; q < QualifierCount(def); q++)
            {
                ushort qualifier = def.Qualifier == QualifierKind.None ? None : (ushort)q;
                all.Add(sheet.Read((Stat)s, qualifier));
                for (int slot = 0; slot < PlayerSlot.Count; slot++)
                {
                    all.Add(sheet.Read((Stat)s, qualifier, Slot(slot)));
                }
            }
        }

        foreach (RuleFlag f in Enum.GetValues<RuleFlag>())
        {
            all.Add(sheet.Has(f) ? 1 : 0);
            for (int slot = 0; slot < PlayerSlot.Count; slot++)
            {
                all.Add(sheet.Has(f, Slot(slot)) ? 1 : 0);
            }
        }

        return all;
    }

    private static void RunTripsOrCompletes(Action call, ushort code)
    {
        SimAssertException? thrown = null;
        try
        {
            call();
        }
        catch (SimAssertException e)
        {
            thrown = e;
        }

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(thrown);
            Assert.Equal(code, thrown.Code);
        }
        else
        {
            Assert.Null(thrown);
        }
    }

    private static void SkipUnlessOptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(StatSheet).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    private static List<Entry> MixedWorld()
    {
        ushort letter = MailKind("letter");
        ushort inv = Container("player_inventory");
        ModifierSource legs0 = Src(Mine(3), id: 4, stack: 0);
        ModifierSource legs1 = Src(Mine(3), id: 4, stack: 1);
        ModifierSource legs2 = Src(Mine(3), id: 4, stack: 2);
        ModifierSource rush = Src(Mine(3), id: 5, flags: RuleFlagSet.Of(RuleFlag.RushHour));
        ModifierSource bag = Src(ModifierOwner.Team, id: 3, kind: SourceKind.Stamp);
        ModifierSource jump = Src(Mine(6), id: 6, flags: RuleFlagSet.Of(RuleFlag.PriorityMail));
        ModifierSource stack = Src(ModifierOwner.Team, id: 1, flags: RuleFlagSet.Of(RuleFlag.PriorityMail));
        ModifierSource sale = Src(ModifierOwner.Team, id: 2, kind: SourceKind.RunEvent, life: ModifierLifetime.Shift);
        return
        [
            E(stack, M(stack, Stat.MailMaxStack, ModifierOp.Mul, 1500, letter)),
            E(sale, M(sale, Stat.MailValueMultPm, ModifierOp.Mul, 900)),
            E(bag, M(bag, Stat.InventoryRows, ModifierOp.Mul, 1500, inv)),
            E(legs0, M(legs0, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)),
            E(legs1, M(legs1, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)),
            E(legs2, M(legs2, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)),
            E(rush, M(rush, Stat.InventoryRows, ModifierOp.Add, 1, inv)),
            E(jump, M(jump, Stat.ExtraJumps, ModifierOp.Add, 1)),
        ];
    }

    // ------------------------------------------------------------ AC-1: identity and versioning

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void Push_Success_RaisesTheVersionByExactlyOne_AndTheSheetReadsTheModifier()
    {
        // Arrange
        var rig = new Rig();
        ModifierSource src = Src(Mine(1), id: 7);

        // Act
        bool ok = rig.Writer.Push(in src, [M(src, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)]);

        // Assert
        Assert.True(ok);
        Assert.Equal(1u, rig.Sheet.Version);
        Assert.Equal(120, rig.Sheet.Read(Stat.MoveSpeedAddPm, None, Slot(1)));
        Assert.Equal(0, rig.Sheet.Read(Stat.MoveSpeedAddPm, None, Slot(2)));
        Assert.Equal(0L, rig.Refused + rig.PoolFull);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void Push_DuplicateIdentity_ReturnsFalse_AndLeavesTheSheetAndVersion()
    {
        // Arrange: the second push differs in modifiers, maxStacks, lifetime and flags, which are not part of the identity
        var rig = new Rig();
        ModifierSource src = Src(Mine(1), id: 7);
        ModifierSource again = Src(Mine(1), id: 7, max: 9, life: ModifierLifetime.Shift, flags: RuleFlagSet.Of(RuleFlag.RushHour));
        Assert.True(rig.Writer.Push(in src, [M(src, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)]));
        List<int> before = Sweep(rig.Sheet);

        // Act
        bool ok = rig.Writer.Push(in again, [M(again, Stat.MoveSpeedAddPm, ModifierOp.Add, 300)]);

        // Assert
        Assert.False(ok);
        Assert.Equal(1u, rig.Sheet.Version);
        Assert.Equal(before, Sweep(rig.Sheet));
        Assert.Equal(1L, rig.Refused);
        Assert.Equal(1, rig.Writer.SourceCount(Mine(1)));
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(5, 3)]
    [InlineData(0, 0)]
    [InlineData(255, 255)]
    [Trait("Id", "CDD-STAT-5")]
    public void Push_StackIndexAtOrAboveMaxStacks_ReturnsFalse_AndLeavesTheSheetAndVersion(int stack, int max)
    {
        // Arrange
        var rig = new Rig();
        ModifierSource src = Src(Mine(1), stack: (byte)stack, max: (byte)max);

        // Act
        bool ok = rig.Writer.Push(in src, [M(src, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)]);

        // Assert
        Assert.False(ok);
        Assert.Equal(0u, rig.Sheet.Version);
        Assert.Equal(0, rig.Sheet.Read(Stat.MoveSpeedAddPm, None, Slot(1)));
        Assert.Equal(1L, rig.Refused);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void Push_DifferingInKindIdStackOrOwner_IsADistinctIdentity()
    {
        // Arrange
        var rig = new Rig();
        ModifierSource baseSrc = Src(Mine(1), id: 7, stack: 0);
        ModifierSource[] distinct =
        [
            baseSrc,
            baseSrc with { Kind = SourceKind.Stamp },
            baseSrc with { Id = DefIndex.FromValue(8) },
            baseSrc with { StackIndex = 1 },
            baseSrc with { Owner = Mine(2) },
            baseSrc with { Owner = ModifierOwner.Team },
        ];

        // Act / Assert: every one is accepted, the version counts them
        for (int i = 0; i < distinct.Length; i++)
        {
            Assert.True(rig.Writer.Push(in distinct[i], []), "push " + i);
            Assert.Equal((uint)(i + 1), rig.Sheet.Version);
        }

        Assert.Equal(0L, rig.Refused);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void Remove_NeverPushed_ReturnsFalse_AndLeavesTheVersion()
    {
        // Arrange
        var rig = new Rig();
        ModifierSource pushed = Src(Mine(1), id: 7);
        ModifierSource other = Src(Mine(1), id: 8);
        Assert.True(rig.Writer.Push(in pushed, [M(pushed, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)]));

        // Act / Assert: wrong id, wrong stack, wrong owner, wrong kind
        Assert.False(rig.Writer.Remove(in other));
        Assert.False(rig.Writer.Remove(pushed with { StackIndex = 1 }));
        Assert.False(rig.Writer.Remove(pushed with { Owner = Mine(2) }));
        Assert.False(rig.Writer.Remove(pushed with { Kind = SourceKind.Stamp }));
        Assert.False(rig.Writer.Remove(src: default));
        Assert.Equal(1u, rig.Sheet.Version);
        Assert.Equal(120, rig.Sheet.Read(Stat.MoveSpeedAddPm, None, Slot(1)));
        Assert.Equal(5L, rig.Refused);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void Remove_Pushed_RaisesTheVersionByOne_AndTheReadsRevert_AndASecondRemoveIsRefused()
    {
        // Arrange
        var rig = new Rig();
        ModifierSource src = Src(Mine(1));
        List<int> empty = Sweep(rig.Sheet);
        Assert.True(rig.Writer.Push(in src, [M(src, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)]));

        // Act
        bool first = rig.Writer.Remove(in src);
        bool second = rig.Writer.Remove(in src);

        // Assert
        Assert.True(first);
        Assert.False(second);
        Assert.Equal(2u, rig.Sheet.Version);
        Assert.Equal(empty, Sweep(rig.Sheet));
        Assert.Equal(0, rig.Writer.ModifierCount(Mine(1)));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void Remove_UsesTheIdentityOnly_NotMaxStacksLifetimeOrFlags()
    {
        // Arrange
        var rig = new Rig();
        ModifierSource src = Src(Mine(1), max: 3, life: ModifierLifetime.Shift, flags: RuleFlagSet.Of(RuleFlag.RushHour));
        Assert.True(rig.Writer.Push(in src, [M(src, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)]));
        ModifierSource byIdentity = src with { MaxStacks = 0, Lifetime = ModifierLifetime.Run, Flags = default };

        // Act / Assert
        Assert.True(rig.Writer.Remove(in byIdentity));
        Assert.False(rig.Sheet.Has(RuleFlag.RushHour, Slot(1)));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void Reads_NeverChangeTheVersion()
    {
        // Arrange
        var rig = new Rig();
        foreach (Entry e in MixedWorld())
        {
            Assert.True(Push(rig, e));
        }

        uint version = rig.Sheet.Version;

        // Act
        _ = Sweep(rig.Sheet);
        _ = rig.Sheet.Explain(Stat.MoveSpeedAddPm, None, Slot(3), 50);
        _ = rig.Sheet.Read(Stat.MoveSpeedAddPm, None, Slot(3), 50);

        // Assert
        Assert.Equal(8u, version);
        Assert.Equal(version, rig.Sheet.Version);
    }

    // ------------------------------------------------------------ refusals (every branch of Push and Remove)

    public static TheoryData<string> RefusalCases() => new()
    {
        "unowned-owner", "bad-kind", "kind-zero", "bad-lifetime", "bad-flag-bits", "personal-stat-on-team-source",
        "team-stat-on-slot-source", "op-not-accepted", "qualifier-out-of-range", "qualifier-on-unqualified", "unknown-stat",
    };

    [Theory]
    [MemberData(nameof(RefusalCases))]
    [Trait("Id", "CDD-STAT-5")]
    public void Push_InvalidSourceOrModifier_IsRefusedWhole_CountedAndLeavesEverythingUnchanged(string which)
    {
        // Arrange: one valid source is live; the refused push must not disturb it
        var rig = new Rig();
        ModifierSource live = Src(Mine(2), id: 90);
        Assert.True(rig.Writer.Push(in live, [M(live, Stat.MoveSpeedAddPm, ModifierOp.Add, 50)]));
        List<int> before = Sweep(rig.Sheet);
        ModifierSource good = Src(Mine(1), id: 91);
        Modifier ok = M(good, Stat.MoveSpeedAddPm, ModifierOp.Add, 120);

        // Act
        bool accepted = which switch
        {
            "unowned-owner" => PushOne(rig, good with { Owner = default }, ok),
            "bad-kind" => PushOne(rig, good with { Kind = (SourceKind)9 }, ok),
            "kind-zero" => PushOne(rig, good with { Kind = default }, ok),
            "bad-lifetime" => PushOne(rig, good with { Lifetime = (ModifierLifetime)7 }, ok),
            "bad-flag-bits" => PushOne(rig, good with { Flags = new RuleFlagSet(1u << 20) }, ok),
            "personal-stat-on-team-source" => PushOne(rig, good with { Owner = ModifierOwner.Team }, ok),
            "team-stat-on-slot-source" => PushOne(rig, good, M(good, Stat.MailValueMultPm, ModifierOp.Mul, 900)),
            "op-not-accepted" => PushOne(rig, good, M(good, Stat.MoveSpeedAddPm, ModifierOp.Mul, 1100)),
            "qualifier-out-of-range" => PushOne(rig, good with { Owner = ModifierOwner.Team }, M(good, Stat.MailMaxStack, ModifierOp.Mul, 1100, 60000)),
            "qualifier-on-unqualified" => PushOne(rig, good, M(good, Stat.MoveSpeedAddPm, ModifierOp.Add, 120, 0)),
            "unknown-stat" => PushOne(rig, good, M(good, (Stat)200, ModifierOp.Add, 120)),
            _ => throw new InvalidOperationException(which),
        };

        // Assert: refused for the other reason than capacity, nothing changed, the good modifier next to a bad one was not stored
        Assert.False(accepted);
        Assert.Equal(1u, rig.Sheet.Version);
        Assert.Equal(before, Sweep(rig.Sheet));
        Assert.Equal(1L, rig.Refused);
        Assert.Equal(0L, rig.PoolFull);
        Assert.Equal(0, rig.Writer.SourceCount(Mine(1)));
        Assert.Equal(0, rig.Writer.SourceCount(ModifierOwner.Team));
    }

    private static bool PushOne(Rig rig, ModifierSource src, Modifier m) => rig.Writer.Push(in src, [m]);

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void Push_OneBadModifierAmongGoodOnes_RefusesTheWholeSource()
    {
        // Arrange
        var rig = new Rig();
        ModifierSource src = Src(Mine(1));

        // Act
        bool ok = rig.Writer.Push(in src, [M(src, Stat.MoveSpeedAddPm, ModifierOp.Add, 120), M(src, Stat.MailValueMultPm, ModifierOp.Mul, 900)]);

        // Assert
        Assert.False(ok);
        Assert.Equal(0, rig.Sheet.Read(Stat.MoveSpeedAddPm, None, Slot(1)));
        Assert.Equal(0u, rig.Sheet.Version);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void Remove_UnownedOwner_IsRefusedAndCounted()
    {
        var rig = new Rig();
        ModifierSource src = Src(default);

        Assert.False(rig.Writer.Remove(in src));
        Assert.Equal(1L, rig.Refused);
        Assert.Equal(0u, rig.Sheet.Version);
    }

    [Fact]
    [Trait("Id", "TD-082")]
    public void DefaultOwner_IsNotTheTeam()
    {
        // Arrange / Act
        ModifierOwner none = default;

        // Assert: explicit team ownership; default and PlayerSlot.None never become the team
        Assert.False(none.IsValid);
        Assert.False(none.IsTeam);
        Assert.NotEqual(ModifierOwner.Team, none);
        Assert.False(ModifierOwner.ForSlot(PlayerSlot.None).IsValid);
        Assert.True(ModifierOwner.Team.IsTeam);
        Assert.Equal(PlayerSlot.None, ModifierOwner.Team.Slot);
        Assert.Equal(Slot(5), Mine(5).Slot);
        Assert.Equal(0, new Rig().Writer.ModifierCapacity(none));
        Assert.Equal(0, new Rig().Writer.ModifierCount(none));
    }

    // ------------------------------------------------------------ pools

    [Fact]
    [Trait("Id", "TR-content-017")]
    public void Pools_AreSizedFromSimSettings_AndTheTeamPoolRefusesTheOneBeyondCapacity()
    {
        // Arrange
        var rig = new Rig();
        StatsSettings stats = _content.Settings.Stats;
        int cap = rig.Writer.ModifierCapacity(ModifierOwner.Team);
        ModifierSource template = Src(ModifierOwner.Team, max: 1);

        // Act: fill the team pool exactly, then one more
        for (int i = 0; i < cap; i++)
        {
            ModifierSource s = template with { Id = DefIndex.FromValue((ushort)i) };
            Assert.True(rig.Writer.Push(in s, [M(s, Stat.LateValueRatioPm, ModifierOp.Set, i % 1000)]), "push " + i);
        }

        ModifierSource extra = template with { Id = DefIndex.FromValue((ushort)cap) };
        bool beyond = rig.Writer.Push(in extra, [M(extra, Stat.LateValueRatioPm, ModifierOp.Set, 1)]);

        // Assert
        Assert.Equal(stats.ModifierPoolPerTeam, cap);
        Assert.Equal(stats.ModifierPoolPerPlayer, rig.Writer.ModifierCapacity(Mine(0)));
        Assert.Equal(stats.ModifierPoolPerPlayer, rig.Writer.ModifierCapacity(Mine(7)));
        Assert.False(beyond);
        Assert.Equal((uint)cap, rig.Sheet.Version);
        Assert.Equal(cap, rig.Writer.ModifierCount(ModifierOwner.Team));
        Assert.Equal(1L, rig.PoolFull);
        Assert.Equal(0L, rig.Refused);
    }

    [Fact]
    [Trait("Id", "TR-content-017")]
    public void Push_ModifiersThatDoNotFit_AreRefusedWhole_AndOtherOwnersPoolsAreIndependent()
    {
        // Arrange: team 2, per player 3
        var rig = new Rig(new StatsSettings { ModifierPoolPerTeam = 2, ModifierPoolPerPlayer = 3 });
        ModifierSource a = Src(Mine(1), id: 1);
        ModifierSource b = Src(Mine(1), id: 2);
        Assert.True(rig.Writer.Push(in a, [M(a, Stat.MoveSpeedAddPm, ModifierOp.Add, 10), M(a, Stat.ExtraJumps, ModifierOp.Add, 1)]));
        List<int> before = Sweep(rig.Sheet);

        // Act: two more modifiers do not fit in the 1 left; a slot-2 push of three does
        bool overflow = rig.Writer.Push(in b, [M(b, Stat.MoveSpeedAddPm, ModifierOp.Add, 10), M(b, Stat.MoveSpeedAddPm, ModifierOp.Add, 10)]);
        ModifierSource c = Src(Mine(2), id: 3);
        bool other = rig.Writer.Push(in c, [M(c, Stat.MoveSpeedAddPm, ModifierOp.Add, 1), M(c, Stat.MoveSpeedAddPm, ModifierOp.Add, 1), M(c, Stat.MoveSpeedAddPm, ModifierOp.Add, 1)]);

        // Assert
        Assert.False(overflow);
        Assert.True(other);
        Assert.Equal(1L, rig.PoolFull);
        Assert.Equal(2u, rig.Sheet.Version);
        Assert.Equal(2, rig.Writer.ModifierCount(Mine(1)));
        Assert.Equal(3, rig.Writer.ModifierCount(Mine(2)));
        Assert.NotEqual(before, Sweep(rig.Sheet));
        Assert.Equal(10, rig.Sheet.Read(Stat.MoveSpeedAddPm, None, Slot(1)));
    }

    [Fact]
    [Trait("Id", "TR-content-017")]
    public void Push_SourceTableFull_RefusesEvenAModifierlessSource_AndRemoveFreesRoom()
    {
        // Arrange: a flag-only source holds no modifier but still takes a source entry
        var rig = new Rig(new StatsSettings { ModifierPoolPerTeam = 2, ModifierPoolPerPlayer = 2 });
        ModifierSource a = Src(ModifierOwner.Team, id: 1, flags: RuleFlagSet.Of(RuleFlag.RushHour));
        ModifierSource b = Src(ModifierOwner.Team, id: 2, flags: RuleFlagSet.Of(RuleFlag.RushHour));
        ModifierSource c = Src(ModifierOwner.Team, id: 3, flags: RuleFlagSet.Of(RuleFlag.RushHour));
        Assert.True(rig.Writer.Push(in a, []));
        Assert.True(rig.Writer.Push(in b, []));

        // Act / Assert
        Assert.False(rig.Writer.Push(in c, []));
        Assert.Equal(1L, rig.PoolFull);
        Assert.True(rig.Writer.Remove(in a));
        Assert.True(rig.Writer.Push(in c, []));
        Assert.Equal(2, rig.Writer.SourceCount(ModifierOwner.Team));
        Assert.Equal(4u, rig.Sheet.Version);
    }

    [Fact]
    [Trait("Id", "TR-content-017")]
    public void ZeroCapacityPools_RefuseEveryPush_AndNegativeCapacityIsRejected()
    {
        // Arrange
        var rig = new Rig(new StatsSettings { ModifierPoolPerTeam = 0, ModifierPoolPerPlayer = 0 });
        ModifierSource team = Src(ModifierOwner.Team);
        ModifierSource mine = Src(Mine(0));

        // Act / Assert
        Assert.False(rig.Writer.Push(in team, []));
        Assert.False(rig.Writer.Push(in mine, []));
        Assert.Equal(2L, rig.PoolFull);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rig(new StatsSettings { ModifierPoolPerTeam = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rig(new StatsSettings { ModifierPoolPerPlayer = -1 }));
        Assert.Throws<ArgumentNullException>(() => new StatSheetWriter(null!, new SimCounters()));
        Assert.Throws<ArgumentNullException>(() => new StatSheetWriter(_content, null!));
    }

    [Fact]
    [Trait("Id", "TR-content-017")]
    public void Pools_NeverGrow_TheBackingArraysAreTheSameInstancesAfterWarmUp()
    {
        // Arrange
        var rig = new Rig(new StatsSettings { ModifierPoolPerTeam = 4, ModifierPoolPerPlayer = 4 });
        Modifier[][] before = BackingArrays(rig.Writer);

        // Act: fill past capacity on every list, remove, refill
        for (int round = 0; round < 3; round++)
        {
            for (int owner = 0; owner < PlayerSlot.Count + 1; owner++)
            {
                ModifierOwner o = owner == 0 ? ModifierOwner.Team : Mine(owner - 1);
                for (ushort i = 0; i < 6; i++)
                {
                    ModifierSource s = Src(o, id: i, max: 1);
                    _ = rig.Writer.Push(in s, []);
                }

                for (ushort i = 0; i < 6; i += 2)
                {
                    ModifierSource s = Src(o, id: i, max: 1);
                    _ = rig.Writer.Remove(in s);
                }
            }
        }

        // Assert
        Modifier[][] after = BackingArrays(rig.Writer);
        for (int i = 0; i < before.Length; i++)
        {
            Assert.Same(before[i], after[i]);
            Assert.Equal(4, after[i].Length);
        }
    }

    private static Modifier[][] BackingArrays(StatSheetWriter writer)
    {
        FieldInfo lists = typeof(StatSheetWriter).GetField("_lists", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var array = (Array)lists.GetValue(writer)!;
        var result = new Modifier[array.Length][];
        for (int i = 0; i < array.Length; i++)
        {
            object list = array.GetValue(i)!;
            result[i] = (Modifier[])list.GetType().GetField("Mods")!.GetValue(list)!;
        }

        return result;
    }

    // ------------------------------------------------------------ AC-2: three build paths agree

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void ThreeBuildPaths_ForIdenticalSourceLists_GiveEqualReadsAndFlagsForEveryCell()
    {
        // Arrange
        List<Entry> world = MixedWorld();
        IStatSheet scratch = Pure(world.ToArray());

        var incremental = new Rig();
        foreach (Entry e in world)
        {
            Assert.True(Push(incremental, e));
        }

        var churned = new Rig();
        ModifierSource noise = Src(Mine(3), id: 200);
        for (int i = world.Count - 1; i >= 0; i--)
        {
            Assert.True(Push(churned, world[i]));
            Assert.True(churned.Writer.Push(in noise, [M(noise, Stat.MoveSpeedAddPm, ModifierOp.Add, 77)]));
            Assert.True(churned.Writer.Remove(in noise));
        }

        SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, _content, 5));
        foreach (Entry e in world)
        {
            ModifierSource src = e.Src;
            Assert.True(host.Services.Stats.Push(in src, e.Mods));
        }

        // Act
        List<int> expected = Sweep(scratch);

        // Assert
        Assert.Contains(900, expected);
        Assert.Equal(expected, Sweep(incremental.Sheet));
        Assert.Equal(expected, Sweep(churned.Sheet));
        Assert.Equal(expected, Sweep(host.StatSheet));
        Assert.Equal((uint)world.Count, incremental.Sheet.Version);
        Assert.Equal((uint)((world.Count * 3)), churned.Sheet.Version);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void RemovingSourcesInAnyOrder_KeepsTheSheetEqualToAFreshBuildOfTheRest()
    {
        // Arrange: sources of 2, 1, 3 and 1 modifiers in one slot, so compaction moves ranges
        var rig = new Rig();
        ModifierSource a = Src(Mine(1), id: 1);
        ModifierSource b = Src(Mine(1), id: 2);
        ModifierSource c = Src(Mine(1), id: 3);
        ModifierSource d = Src(Mine(1), id: 4);
        Entry ea = E(a, M(a, Stat.MoveSpeedAddPm, ModifierOp.Add, 1), M(a, Stat.ExtraJumps, ModifierOp.Add, 1));
        Entry eb = E(b, M(b, Stat.MoveSpeedAddPm, ModifierOp.Add, 10));
        Entry ec = E(c, M(c, Stat.MoveSpeedAddPm, ModifierOp.Add, 100), M(c, Stat.ThrowRangePermille, ModifierOp.Add, 5), M(c, Stat.ExtraJumps, ModifierOp.Add, 1));
        Entry ed = E(d, M(d, Stat.MoveSpeedAddPm, ModifierOp.Add, 1000));
        foreach (Entry e in new[] { ea, eb, ec, ed })
        {
            Assert.True(Push(rig, e));
        }

        // Act / Assert: remove middle, last, first, then the remaining one; compare after each step
        Assert.True(rig.Writer.Remove(in b));
        Assert.Equal(Sweep(Pure(ea, ec, ed)), Sweep(rig.Sheet));
        Assert.True(rig.Writer.Remove(in d));
        Assert.Equal(Sweep(Pure(ea, ec)), Sweep(rig.Sheet));
        Assert.True(rig.Writer.Remove(in a));
        Assert.Equal(Sweep(Pure(ec)), Sweep(rig.Sheet));
        Assert.Equal(3, rig.Writer.ModifierCount(Mine(1)));
        Assert.True(rig.Writer.Remove(in c));
        Assert.Equal(Sweep(Pure()), Sweep(rig.Sheet));
        Assert.Equal(0, rig.Writer.ModifierCount(Mine(1)));
        Assert.Equal(8u, rig.Sheet.Version);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void Push_StampsTheSourceIdentityAndLifetimeOntoEveryStoredModifier()
    {
        // Arrange: the modifier carries a different identity; the source is authoritative
        var rig = new Rig();
        ModifierSource src = Src(ModifierOwner.Team, id: 11, stack: 1, kind: SourceKind.RunEvent, life: ModifierLifetime.Shift);
        Modifier wrong = new(SourceKind.Perk, DefIndex.FromValue(99), 0, Slot(4), Stat.MailValueMultPm, None, ModifierOp.Mul, 900, ModifierLifetime.Run);

        // Act
        Assert.True(rig.Writer.Push(in src, [wrong]));
        StatExplain e = rig.Sheet.Explain(Stat.MailValueMultPm, MailKind("letter"));

        // Assert
        Assert.Equal(900, e.Resolved);
        StatContribution c = Assert.Single(e.Sources.ToArray());
        Assert.Equal(SourceKind.RunEvent, c.SourceKind);
        Assert.Equal(DefIndex.FromValue(11), c.SourceId);
        Assert.Equal((byte)1, c.StackIndex);
        Assert.Equal(PlayerSlot.None, c.OwnerSlot);
        Assert.Equal(ModifierLifetime.Shift, c.Lifetime);
    }

    // ------------------------------------------------------------ AC-3: Shift-lifetime source, no timers

    [Fact]
    [Trait("Id", "CDD-STAT-6")]
    public void ShiftLifetimeSource_Reads900UntilRemoved_EvenTenThousandTicksLater_Then1000()
    {
        // Arrange
        SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, _content, 9));
        IStatSheet sheet = host.StatSheet;
        ushort letter = MailKind("letter");
        ModifierSource src = Src(ModifierOwner.Team, id: 2, kind: SourceKind.RunEvent, life: ModifierLifetime.Shift);
        Assert.Equal(1000, sheet.Read(Stat.MailValueMultPm, letter));

        // Act
        Assert.True(host.Services.Stats.Push(in src, [M(src, Stat.MailValueMultPm, ModifierOp.Mul, 900)]));
        int before = sheet.Read(Stat.MailValueMultPm, letter);
        for (int i = 0; i < 10_000; i++)
        {
            host.World.Tick();
        }

        int later = sheet.Read(Stat.MailValueMultPm, letter);
        uint versionLater = sheet.Version;
        bool removed = host.Services.Stats.Remove(in src);
        int after = sheet.Read(Stat.MailValueMultPm, letter);

        // Assert: the sheet has no timers, only a Remove changes the value
        Assert.Equal(900, before);
        Assert.Equal(900, later);
        Assert.Equal(1u, versionLater);
        Assert.True(removed);
        Assert.Equal(1000, after);
        Assert.Equal(2u, sheet.Version);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-6")]
    public void RunAndShiftLifetimes_AreTagsOnly_BothBehaveTheSameInTheSheet()
    {
        // Arrange
        var run = new Rig();
        var shift = new Rig();
        ModifierSource r = Src(ModifierOwner.Team, life: ModifierLifetime.Run);
        ModifierSource s = Src(ModifierOwner.Team, life: ModifierLifetime.Shift);

        // Act
        Assert.True(run.Writer.Push(in r, [M(r, Stat.MailValueMultPm, ModifierOp.Mul, 900)]));
        Assert.True(shift.Writer.Push(in s, [M(s, Stat.MailValueMultPm, ModifierOp.Mul, 900)]));

        // Assert
        Assert.Equal(Sweep(run.Sheet), Sweep(shift.Sheet));
    }

    // ------------------------------------------------------------ AC-4: stacks and rule flags

    [Fact]
    [Trait("Id", "CDD-STAT-7")]
    public void ThreeStacksOfAPersonalAdd120_Read360()
    {
        // Arrange
        var rig = new Rig();

        // Act
        for (byte stack = 0; stack < 3; stack++)
        {
            ModifierSource s = Src(Mine(2), id: 4, stack: stack, max: 3);
            Assert.True(rig.Writer.Push(in s, [M(s, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)]));
        }

        ModifierSource fourth = Src(Mine(2), id: 4, stack: 3, max: 3);

        // Assert
        Assert.Equal(360, rig.Sheet.Read(Stat.MoveSpeedAddPm, None, Slot(2)));
        Assert.False(rig.Writer.Push(in fourth, [M(fourth, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)]));
        Assert.Equal(360, rig.Sheet.Read(Stat.MoveSpeedAddPm, None, Slot(2)));
        Assert.Equal(0, rig.Sheet.Read(Stat.MoveSpeedAddPm, None, Slot(1)));
        Assert.Equal(3u, rig.Sheet.Version);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-8")]
    public void RuleFlagHeldByTwoSources_StaysOnAfterOneIsRemoved_AndTurnsOffAfterBoth()
    {
        // Arrange
        var rig = new Rig();
        RuleFlagSet flag = RuleFlagSet.Of(RuleFlag.PriorityMail);
        ModifierSource a = Src(ModifierOwner.Team, id: 1, flags: flag);
        ModifierSource b = Src(ModifierOwner.Team, id: 2, kind: SourceKind.Stamp, flags: flag);
        Assert.False(rig.Sheet.Has(RuleFlag.PriorityMail));

        // Act / Assert
        Assert.True(rig.Writer.Push(in a, []));
        Assert.True(rig.Sheet.Has(RuleFlag.PriorityMail));
        Assert.True(rig.Writer.Push(in b, []));
        Assert.True(rig.Writer.Remove(in a));
        Assert.True(rig.Sheet.Has(RuleFlag.PriorityMail));
        Assert.True(rig.Writer.Remove(in b));
        Assert.False(rig.Sheet.Has(RuleFlag.PriorityMail));
        Assert.Equal(4u, rig.Sheet.Version);
    }

    [Fact]
    [Trait("Id", "TR-content-018")]
    public void RuleFlags_AreCountedPerSheet_TeamFlagsReachEverySlot_PersonalFlagsOnlyTheirOwn()
    {
        // Arrange
        var rig = new Rig();
        ModifierSource team = Src(ModifierOwner.Team, id: 1, flags: RuleFlagSet.Of(RuleFlag.PriorityMail));
        ModifierSource mine = Src(Mine(4), id: 2, flags: RuleFlagSet.Of(RuleFlag.RushHour).With(RuleFlag.PostalSprint));

        // Act
        Assert.True(rig.Writer.Push(in team, []));
        Assert.True(rig.Writer.Push(in mine, []));

        // Assert
        Assert.True(rig.Sheet.Has(RuleFlag.PriorityMail));
        Assert.True(rig.Sheet.Has(RuleFlag.PriorityMail, Slot(0)));
        Assert.True(rig.Sheet.Has(RuleFlag.PriorityMail, Slot(4)));
        Assert.False(rig.Sheet.Has(RuleFlag.RushHour));
        Assert.False(rig.Sheet.Has(RuleFlag.RushHour, Slot(3)));
        Assert.True(rig.Sheet.Has(RuleFlag.RushHour, Slot(4)));
        Assert.True(rig.Sheet.Has(RuleFlag.PostalSprint, Slot(4)));
        Assert.False(rig.Sheet.Has(RuleFlag.PresortOutfeed, Slot(4)));
        Assert.True(rig.Writer.Remove(in mine));
        Assert.False(rig.Sheet.Has(RuleFlag.RushHour, Slot(4)));
        Assert.True(rig.Sheet.Has(RuleFlag.PriorityMail, Slot(4)));
    }

    [Fact]
    [Trait("Id", "TR-content-018")]
    public void Has_UndefinedFlag_IsFalse_AndSlotNoneAssertsOrReadsTheTeamSheet()
    {
        // Arrange
        var rig = new Rig();
        ModifierSource team = Src(ModifierOwner.Team, flags: RuleFlagSet.Of(RuleFlag.RushHour));
        Assert.True(rig.Writer.Push(in team, []));
        bool undefined = true;
        bool noneSlot = false;

        // Act
        RunTripsOrCompletes(() => noneSlot = rig.Sheet.Has(RuleFlag.RushHour, PlayerSlot.None), ContentAssertCode.StatSheetBadSlot);
        undefined = rig.Sheet.Has((RuleFlag)0) || rig.Sheet.Has((RuleFlag)200);

        // Assert
        Assert.False(undefined);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.True(noneSlot);
        }
    }

    [Fact]
    [Trait("Id", "TR-content-018")]
    public void RuleFlagSet_BuildsAndTestsBits_AndIgnoresOutOfRangeFlags()
    {
        // Arrange / Act
        RuleFlagSet set = RuleFlagSet.Of(RuleFlag.KeepHotbarOnDeath).With(RuleFlag.RushHour);

        // Assert
        Assert.Equal(1u | (1u << 13), set.Bits);
        Assert.True(set.Contains(RuleFlag.RushHour));
        Assert.False(set.Contains(RuleFlag.PriorityMail));
        Assert.False(set.Contains((RuleFlag)0));
        Assert.False(set.Contains((RuleFlag)255));
        Assert.Equal(set, set.With((RuleFlag)0).With((RuleFlag)255));
        Assert.Equal(default, RuleFlagSet.Of((RuleFlag)0));
        Assert.Equal(RuleFlagSet.DefinedWidth, Enum.GetValues<RuleFlag>().Max(f => (int)f));
    }

    [Fact]
    [Trait("Id", "TR-content-018")]
    public void Build_InvalidFlagGrant_AssertsOrIsSkipped()
    {
        // Arrange
        var sources = new StatSources([], [new RuleFlagGrant(default, RuleFlag.RushHour), new RuleFlagGrant(ModifierOwner.Team, (RuleFlag)200), new RuleFlagGrant(ModifierOwner.Team, RuleFlag.PriorityMail)]);
        IStatSheet? sheet = null;

        // Act
        RunTripsOrCompletes(() => sheet = StatSheet.Build(_content, sources), ContentAssertCode.StatSheetBadFlagGrant);

        // Assert: only the valid grant counts
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(sheet);
            Assert.True(sheet.Has(RuleFlag.PriorityMail));
            Assert.False(sheet.Has(RuleFlag.RushHour));
        }
    }

    // ------------------------------------------------------------ AC-5: profile remap (CDD-STAT-13, TD-082 item 1)

    private static Modifier PersonalLegs(PlayerSlot ignoredOwner, byte stack = 0) =>
        new(SourceKind.Perk, DefIndex.FromValue(4), stack, ignoredOwner, Stat.MoveSpeedAddPm, None, ModifierOp.Add, 120, ModifierLifetime.Run);

    private static ProfilePick[] LegsPicks(ProfileId p, PlayerSlot stale) =>
        [new(p, PersonalLegs(stale, 0)), new(p, PersonalLegs(stale, 1)), new(p, PersonalLegs(stale, 2))];

    [Fact]
    [Trait("Id", "CDD-STAT-13")]
    public void PersonalPick_KeyedByProfile_AppliesToTheNewSlotAfterTheMapChanges_AndTheOldSlotReadsBase()
    {
        // Arrange
        var p = new ProfileId(0xA11CE);
        ProfilePick[] picks = LegsPicks(p, Slot(7));

        // Act
        IStatSheet atSlot2 = StatSheet.Build(_content, StatSources.FromProfilePicks([], picks, [new ProfileSlotEntry(p, Slot(2))]));
        IStatSheet atSlot5 = StatSheet.Build(_content, StatSources.FromProfilePicks([], picks, [new ProfileSlotEntry(p, Slot(5))]));

        // Assert: the stale owner slot inside the pick (7) never matters
        Assert.Equal(360, atSlot2.Read(Stat.MoveSpeedAddPm, None, Slot(2)));
        Assert.Equal(0, atSlot2.Read(Stat.MoveSpeedAddPm, None, Slot(5)));
        Assert.Equal(360, atSlot5.Read(Stat.MoveSpeedAddPm, None, Slot(5)));
        Assert.Equal(0, atSlot5.Read(Stat.MoveSpeedAddPm, None, Slot(2)));
        Assert.Equal(0, atSlot5.Read(Stat.MoveSpeedAddPm, None, Slot(7)));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-13")]
    public void OldSlot_ReadsTeamPlusBaseOnly_AfterTheRemap()
    {
        // Arrange: a team Mul and a personal Add on the both-stat InventoryRows
        ushort inv = Container("player_inventory");
        var p = new ProfileId(1);
        Modifier team = new(SourceKind.Stamp, DefIndex.FromValue(3), 0, PlayerSlot.None, Stat.InventoryRows, inv, ModifierOp.Mul, 1500, ModifierLifetime.Run);
        Modifier pick = new(SourceKind.Perk, DefIndex.FromValue(5), 0, PlayerSlot.None, Stat.InventoryRows, inv, ModifierOp.Add, 1, ModifierLifetime.Run);

        // Act
        IStatSheet before = StatSheet.Build(_content, StatSources.FromProfilePicks([team], [new ProfilePick(p, pick)], [new ProfileSlotEntry(p, Slot(1))]));
        IStatSheet after = StatSheet.Build(_content, StatSources.FromProfilePicks([team], [new ProfilePick(p, pick)], [new ProfileSlotEntry(p, Slot(6))]));

        // Assert: 2 rows * 1.5 = 3 (team + base), 2 * 1.5 + 1 = 4 with the personal Add
        Assert.Equal(4, before.Read(Stat.InventoryRows, inv, Slot(1)));
        Assert.Equal(3, before.Read(Stat.InventoryRows, inv, Slot(6)));
        Assert.Equal(3, after.Read(Stat.InventoryRows, inv, Slot(1)));
        Assert.Equal(4, after.Read(Stat.InventoryRows, inv, Slot(6)));
        Assert.Equal(3, after.Read(Stat.InventoryRows, inv));
    }

    [Fact]
    [Trait("Id", "TD-082")]
    public void PicksOfAnAbsentProfile_ApplyToNobody_NotToTheTeam()
    {
        // Arrange: a both-stat pick would show on the team sheet if it leaked to PlayerSlot.None
        ushort inv = Container("player_inventory");
        var absent = new ProfileId(7);
        var noSlot = new ProfileId(8);
        var present = new ProfileId(9);
        Modifier pick = new(SourceKind.Perk, DefIndex.FromValue(5), 0, Slot(0), Stat.InventoryRows, inv, ModifierOp.Add, 2, ModifierLifetime.Run);
        Modifier legs = PersonalLegs(PlayerSlot.None);
        ProfileSlotEntry[] map = [new(noSlot, PlayerSlot.None), new(present, Slot(3))];

        // Act: absent has no entry; noSlot has an entry without a slot; PlayerSlot.None as an owner would mean team
        StatSources sources = StatSources.FromProfilePicks(
            [], [new ProfilePick(absent, pick), new ProfilePick(noSlot, pick), new ProfilePick(absent, legs), new ProfilePick(present, legs)], map,
            personalFlags: [new ProfileRuleFlag(absent, RuleFlag.RushHour), new ProfileRuleFlag(noSlot, RuleFlag.RushHour)]);
        IStatSheet sheet = StatSheet.Build(_content, sources);

        // Assert: only the present profile's pick survived
        Assert.Equal(1, sources.Count);
        Assert.Equal(0, sources.Team.Length);
        Assert.Equal(0, sources.Grants.Length);
        Assert.Equal(2, sheet.Read(Stat.InventoryRows, inv));
        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            Assert.Equal(2, sheet.Read(Stat.InventoryRows, inv, Slot(s)));
            Assert.False(sheet.Has(RuleFlag.RushHour, Slot(s)));
            Assert.Equal(s == 3 ? 120 : 0, sheet.Read(Stat.MoveSpeedAddPm, None, Slot(s)));
        }

        Assert.False(sheet.Has(RuleFlag.RushHour));
    }

    [Fact]
    [Trait("Id", "TD-082")]
    public void TeamModifiersAndFlags_StayTeam_AndPersonalFlagsFollowTheProfile()
    {
        // Arrange
        var p = new ProfileId(3);
        Modifier team = new(SourceKind.Perk, DefIndex.FromValue(1), 0, Slot(5), Stat.MailValueMultPm, None, ModifierOp.Mul, 900, ModifierLifetime.Run);

        // Act: the owner slot inside a team modifier is forced to team
        StatSources sources = StatSources.FromProfilePicks(
            [team], [], [new ProfileSlotEntry(p, Slot(4))],
            teamFlags: [RuleFlag.PriorityMail],
            personalFlags: [new ProfileRuleFlag(p, RuleFlag.RushHour)]);
        IStatSheet sheet = StatSheet.Build(_content, sources);

        // Assert
        Assert.Equal(900, sheet.Read(Stat.MailValueMultPm, MailKind("letter")));
        Assert.Equal(1, sources.Team.Length);
        Assert.True(sheet.Has(RuleFlag.PriorityMail, Slot(0)));
        Assert.True(sheet.Has(RuleFlag.RushHour, Slot(4)));
        Assert.False(sheet.Has(RuleFlag.RushHour, Slot(5)));
        Assert.False(sheet.Has(RuleFlag.RushHour));
    }

    [Fact]
    [Trait("Id", "TD-082")]
    public void Remap_FirstEntryWithASlotWins_AndTheResultDoesNotDependOnPickOrder()
    {
        // Arrange
        var p = new ProfileId(3);
        ProfilePick[] picks = LegsPicks(p, PlayerSlot.None);
        ProfileSlotEntry[] map = [new(p, PlayerSlot.None), new(p, Slot(6)), new(p, Slot(1))];

        // Act
        IStatSheet forward = StatSheet.Build(_content, StatSources.FromProfilePicks([], picks, map));
        IStatSheet backward = StatSheet.Build(_content, StatSources.FromProfilePicks([], Enumerable.Reverse(picks).ToArray(), map));

        // Assert
        Assert.Equal(360, forward.Read(Stat.MoveSpeedAddPm, None, Slot(6)));
        Assert.Equal(0, forward.Read(Stat.MoveSpeedAddPm, None, Slot(1)));
        Assert.Equal(Sweep(forward), Sweep(backward));
    }

    // ------------------------------------------------------------ AC-6: composition

    [Fact]
    [Trait("Id", "CDD-STAT-4")]
    public void Build_CreatesOneSheetPerWorld_SizedFromSimSettings_AndEveryStatReadsItsBase()
    {
        // Arrange / Act
        SimHost a = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, _content, 1));
        SimHost b = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, _content, 1));

        // Assert: one instance per world, stable for its life, never shared
        Assert.NotNull(a.StatSheet);
        Assert.Same(a.StatSheet, a.Services.Stats.Sheet);
        Assert.NotSame(a.StatSheet, b.StatSheet);
        Assert.NotSame(a.Services.Stats, b.Services.Stats);
        Assert.Equal(_content.Settings.Stats.ModifierPoolPerTeam, a.Services.Stats.ModifierCapacity(ModifierOwner.Team));
        Assert.Equal(_content.Settings.Stats.ModifierPoolPerPlayer, a.Services.Stats.ModifierCapacity(Mine(3)));
        Assert.Equal(0u, a.StatSheet.Version);
        Assert.Equal(Sweep(Pure()), Sweep(a.StatSheet));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-4")]
    public void WorldsDoNotShareSources_APushInOneIsInvisibleInTheOther()
    {
        // Arrange
        SimHost a = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, _content, 1));
        SimHost b = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, _content, 1));
        ModifierSource src = Src(Mine(0));

        // Act
        Assert.True(a.Services.Stats.Push(in src, [M(src, Stat.MoveSpeedAddPm, ModifierOp.Add, 120)]));

        // Assert
        Assert.Equal(120, a.StatSheet.Read(Stat.MoveSpeedAddPm, None, Slot(0)));
        Assert.Equal(0, b.StatSheet.Read(Stat.MoveSpeedAddPm, None, Slot(0)));
        Assert.Equal(0u, b.StatSheet.Version);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-4")]
    public void Refusals_AreCountedInTheWorldsOwnSimCounters()
    {
        // Arrange
        SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, _content, 1));
        ModifierSource src = Src(Mine(0));

        // Act
        bool removed = host.Services.Stats.Remove(in src);

        // Assert
        Assert.False(removed);
        Assert.Equal(1L, host.Services.Counters[CounterId.StatWriteRefused]);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-4")]
    public void Composition_PassesTheSheetToAnySystem_AndTheWriterOnlyToPerksAndStamps()
    {
        // Arrange
        IStatSheet? grid = null;
        IStatSheet? perksSheet = null;
        IStatWriter? perksWriter = null;
        IStatWriter? stampsWriter = null;
        SimProfile profile = SimProfile.ForTest(
            "stats",
            null,
            new SimSystemEntry(SystemId.GridInventory, ctx =>
            {
                grid = ctx.StatSheet;
                return new ProbeSystem();
            }),
            new SimSystemEntry(SystemId.Perks, ctx =>
            {
                perksSheet = ctx.StatSheet;
                perksWriter = ctx.StatWriter;
                return new ProbeSystem();
            }),
            new SimSystemEntry(SystemId.StampsRunEvents, ctx =>
            {
                stampsWriter = ctx.StatWriter;
                return new ProbeSystem();
            }));

        // Act
        SimHost host = SimComposition.Build(new SimBuildArgs(profile, _content, 1));

        // Assert: the same per-world instances
        Assert.Same(host.StatSheet, grid);
        Assert.Same(host.StatSheet, perksSheet);
        Assert.Same(host.Services.Stats, perksWriter);
        Assert.Same(host.Services.Stats, stampsWriter);
    }

    [Theory]
    [InlineData(SystemId.GridInventory)]
    [InlineData(SystemId.PlayerCharacter)]
    [InlineData(SystemId.MailDestinations)]
    [InlineData(SystemId.Economy)]
    [InlineData(SystemId.RunStructure)]
    [Trait("Id", "CDD-STAT-4")]
    public void Composition_RefusesTheWriterToEverySystemExceptPerksAndStamps(SystemId id)
    {
        // Arrange
        SimProfile profile = SimProfile.ForTest("deny", null, new SimSystemEntry(id, ctx =>
        {
            _ = ctx.StatWriter;
            return new ProbeSystem();
        }));

        // Act
        SimBuildException e = Assert.Throws<SimBuildException>(() => SimComposition.Build(new SimBuildArgs(profile, _content, 1)));

        // Assert
        Assert.Equal(SimBuildErrorCode.UndeclaredDependency, e.Code);
        Assert.Contains("IStatWriter", e.Message);
    }

    // ------------------------------------------------------------ structure and allocation

    [Fact]
    [Trait("Id", "CDD-PERF-2")]
    public void TheLiveSheet_HoldsOnlyTheImmutableCache_NeverThePoolsTheSourcesOrTheWriter()
    {
        // Arrange
        Type live = new Rig().Sheet.GetType();
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(live);

        // Act
        while (pending.Count > 0)
        {
            Type t = pending.Pop();
            if (!seen.Add(t))
            {
                continue;
            }

            if (t.HasElementType)
            {
                pending.Push(t.GetElementType()!);
            }

            if (t.IsGenericType)
            {
                foreach (Type arg in t.GetGenericArguments())
                {
                    pending.Push(arg);
                }
            }

            if (t.Assembly != typeof(Modifier).Assembly)
            {
                continue;
            }

            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                pending.Push(f.FieldType);
            }
        }

        // Assert: the writer owns the pools and holds the sheet, not the reverse
        Assert.Equal("LiveStatSheet", live.Name);
        Assert.Contains(typeof(StatSheetCache), seen);
        Assert.DoesNotContain(typeof(Modifier), seen);
        Assert.DoesNotContain(typeof(StatSources), seen);
        Assert.DoesNotContain(typeof(StatSheetWriter), seen);
        Assert.DoesNotContain(typeof(ContentSet), seen);
    }

    [Fact]
    [Trait("Id", "CDD-PERF-2")]
    public void Writer_AndLiveSheet_HaveNoStaticFields()
    {
        Type[] types = [typeof(StatSheetWriter), typeof(StatSheetWriter).Assembly.GetType("PerformativeMail.Sim.Content.Stats.LiveStatSheet")!, typeof(StatSources), typeof(RuleFlagSet), typeof(ModifierOwner)];

        FieldInfo[] statics = types
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(f => !f.IsLiteral)
            .ToArray();

        Assert.Empty(statics);
    }

    [Fact]
    [Trait("Id", "CDD-ID-4")]
    public void LiveSheet_ReadAndHas_AllocateNothing_AfterManyRebuilds()
    {
        SkipUnlessOptimised();

        // Arrange
        var rig = new Rig();
        foreach (Entry e in MixedWorld())
        {
            Assert.True(Push(rig, e));
        }

        IStatSheet sheet = rig.Sheet;
        PlayerSlot slot = Slot(3);
        long sink = 0;

        // Act / Assert
        _ = AllocAssert.Zero("1,000,000 live-sheet reads and Has calls", () =>
        {
            long sum = 0;
            for (int i = 0; i < 1_000_000; i++)
            {
                sum += sheet.Read(Stat.MoveSpeedAddPm, None, slot);
                sum += sheet.Has(RuleFlag.RushHour, slot) ? 1 : 0;
            }

            sink = sum;
        });
        Assert.Equal(1_000_000L * 361, sink);
    }
}
