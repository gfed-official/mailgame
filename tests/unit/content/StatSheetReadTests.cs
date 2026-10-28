using System.Collections.Immutable;
using System.Reflection;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 014 (TR-content-017, TR-content-018 transient half, TR-content-019, TR-content-028 Read half;
/// GDD CDD-STAT-8 numeric half, CDD-ID-4 Read half, CDD-PERF-2 structural clause): <see cref="StatSheet.Build"/>, the cached
/// cells, <see cref="IStatSheet.Read"/> and <see cref="IStatSheet.Explain"/>, and the <see cref="StatResolve.ResolveWithTransient"/>
/// entry point the transient read uses. Sources are synthetic (nothing writes sources at M-1a). No random, no time, no I/O.
/// The invalid-read table is checked in both builds: under SIM_ASSERT the call must throw the documented code, otherwise it
/// must return the documented deterministic value (pattern: <see cref="TestBuildExpectation.SimAssertsExpected"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class StatSheetReadTests
{
    private const ushort None = StatSheet.NoQualifier;

    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    // ------------------------------------------------------------ helpers

    private static PlayerSlot Slot(int n) => PlayerSlot.FromValue((byte)n);

    private static Modifier Team(Stat stat, ModifierOp op, int value, ushort qualifier = None, byte stack = 0, uint source = 0) =>
        new(SourceKind.Perk, DefIndex.FromValue((ushort)source), stack, PlayerSlot.None, stat, qualifier, op, value, ModifierLifetime.Run);

    private static Modifier Mine(int slot, Stat stat, ModifierOp op, int value, ushort qualifier = None, byte stack = 0, uint source = 0) =>
        new(SourceKind.Perk, DefIndex.FromValue((ushort)source), stack, Slot(slot), stat, qualifier, op, value, ModifierLifetime.Run);

    private static IStatSheet Build(params Modifier[] modifiers) => StatSheet.Build(_content, new StatSources(modifiers));

    private static IStatSheet Empty() => Build();

    private static ushort Container(string id) => Find(_content.Containers.AsSpan(), id, c => c.Id);

    private static ushort MailKind(string id) => Find(_content.MailKinds.AsSpan(), id, k => k.Id);

    private static ushort Find<T>(ReadOnlySpan<T> table, string id, Func<T, string> idOf)
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

    private static StatBounds Bounds(Stat stat) => _content.ResolvedStatBounds[(int)stat - 1];

    private static Modifier[] LongLegs(int slot) =>
        [Mine(slot, Stat.MoveSpeedAddPm, ModifierOp.Add, 120, stack: 0), Mine(slot, Stat.MoveSpeedAddPm, ModifierOp.Add, 120, stack: 1), Mine(slot, Stat.MoveSpeedAddPm, ModifierOp.Add, 120, stack: 2)];

    private static int BaseOf(StatDef def, int qualifier) => def.Base.Kind switch
    {
        BaseSourceKind.ContainerRows => _content.Containers[DefIndex.FromValue((ushort)qualifier)].GridH,
        BaseSourceKind.MailKindMaxStack => _content.MailKinds[DefIndex.FromValue((ushort)qualifier)].MaxStack,
        _ => def.ResolveBase(_content.Balance),
    };

    private static int QualifierCount(StatDef def) => def.Qualifier switch
    {
        QualifierKind.Container => _content.Containers.Count,
        QualifierKind.MailKind => _content.MailKinds.Count,
        _ => 1,
    };

    /// <summary>Under SIM_ASSERT the call throws <paramref name="code"/>; otherwise it returns <paramref name="releaseValue"/>.</summary>
    private static void AssertTripsOrReturns(Func<int> call, ushort code, int releaseValue)
    {
        SimAssertException? thrown = null;
        int value = 0;
        try
        {
            value = call();
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
            Assert.Equal(releaseValue, value);
        }
    }

    /// <summary>TD-008: allocation counts are meaningful only for an optimised Sim.</summary>
    private static void SkipUnlessOptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(StatSheet).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

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

        return all;
    }

    // ------------------------------------------------------------ AC-1: no sources

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void NoSources_EveryStatQualifierAndSlot_ReadsClampedBaseAndExplainsNoClamp()
    {
        // Arrange
        IStatSheet sheet = Empty();

        for (int s = 1; s <= StatDefs.Count; s++)
        {
            StatDef def = StatDefs.Get((Stat)s);
            StatBounds bounds = Bounds((Stat)s);
            for (int q = 0; q < QualifierCount(def); q++)
            {
                ushort qualifier = def.Qualifier == QualifierKind.None ? None : (ushort)q;
                int baseValue = BaseOf(def, q);
                int expected = Math.Clamp(baseValue, bounds.Min, bounds.Max);

                // Act / Assert: team view and every slot
                Assert.Equal(expected, sheet.Read((Stat)s, qualifier));
                StatExplain e = sheet.Explain((Stat)s, qualifier);
                Assert.Equal(expected, e.Resolved);
                Assert.Equal(baseValue, e.Unclamped);
                Assert.Equal(StatClamp.None, e.ClampedAt);
                Assert.Equal(0, e.Sources.Length);
                Assert.Equal(0, e.TransientAdd);
                for (int slot = 0; slot < PlayerSlot.Count; slot++)
                {
                    Assert.Equal(expected, sheet.Read((Stat)s, qualifier, Slot(slot)));
                }
            }
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void NoSources_NamedExamples_MatchTheGdd()
    {
        // Arrange
        IStatSheet sheet = Empty();

        // Act / Assert
        Assert.Equal(20, sheet.Read(Stat.MailMaxStack, MailKind("letter")));
        Assert.Equal(2, sheet.Read(Stat.InventoryRows, Container("player_inventory")));
        Assert.Equal(500, sheet.Read(Stat.LateValueRatioPm));
        Assert.Equal(StatClamp.None, sheet.Explain(Stat.MailMaxStack, MailKind("letter")).ClampedAt);
        Assert.Equal(StatClamp.None, sheet.Explain(Stat.InventoryRows, Container("player_inventory")).ClampedAt);
        Assert.Equal(StatClamp.None, sheet.Explain(Stat.LateValueRatioPm).ClampedAt);
    }

    // ------------------------------------------------------------ AC-2: Long Legs and the transient

    [Fact]
    [Trait("Id", "CDD-STAT-8")]
    public void LongLegs_WithTransient300_ReadsCappedAndExplainsUnclamped660()
    {
        // Arrange
        IStatSheet sheet = Build(LongLegs(2));

        // Act
        int read = sheet.Read(Stat.MoveSpeedAddPm, slot: Slot(2), transientAdd: 300);
        StatExplain e = sheet.Explain(Stat.MoveSpeedAddPm, slot: Slot(2), transientAdd: 300);

        // Assert
        Assert.Equal(400, read);
        Assert.Equal(400, e.Resolved);
        Assert.Equal(660, e.Unclamped);
        Assert.Equal(StatClamp.Max, e.ClampedAt);
        Assert.Equal(300, e.TransientAdd);
        Assert.Equal(3, e.Sources.Length);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-8")]
    public void LongLegs_WithoutTransient_Reads360AndExplainsNoClamp()
    {
        // Arrange
        IStatSheet sheet = Build(LongLegs(2));

        // Act
        StatExplain e = sheet.Explain(Stat.MoveSpeedAddPm, slot: Slot(2));

        // Assert: also the slot-less team view and another slot see base only
        Assert.Equal(360, sheet.Read(Stat.MoveSpeedAddPm, slot: Slot(2)));
        Assert.Equal(360, e.Resolved);
        Assert.Equal(360, e.Unclamped);
        Assert.Equal(StatClamp.None, e.ClampedAt);
        Assert.Equal(0, sheet.Read(Stat.MoveSpeedAddPm));
        Assert.Equal(0, sheet.Read(Stat.MoveSpeedAddPm, slot: Slot(3)));
    }

    [Fact]
    [Trait("Id", "TR-content-018")]
    public void Transient_IsNotStored_ASecondReadWithoutItSeesTheCachedValue()
    {
        // Arrange
        IStatSheet sheet = Build(LongLegs(0));

        // Act
        int with = sheet.Read(Stat.MoveSpeedAddPm, slot: Slot(0), transientAdd: 30);
        int without = sheet.Read(Stat.MoveSpeedAddPm, slot: Slot(0));

        // Assert: a read never mutates the sheet
        Assert.Equal(390, with);
        Assert.Equal(360, without);
    }

    // ------------------------------------------------------------ AC-3: scope

    [Fact]
    [Trait("Id", "CD15")]
    public void BothStat_PersonalRead_MergesTeamAndSlotModifiers()
    {
        // Arrange: base 2 rows; team Mul 1500 and slot 3 Add 1: N = 2*1500 + 1000*1 = 4000 -> 4
        ushort inv = Container("player_inventory");
        IStatSheet sheet = Build(
            Team(Stat.InventoryRows, ModifierOp.Mul, 1500, inv),
            Mine(3, Stat.InventoryRows, ModifierOp.Add, 1, inv));

        // Act / Assert
        Assert.Equal(4, sheet.Read(Stat.InventoryRows, inv, Slot(3)));
        Assert.Equal(3, sheet.Read(Stat.InventoryRows, inv));
        Assert.Equal(3, sheet.Read(Stat.InventoryRows, inv, Slot(4)));
        StatExplain e = sheet.Explain(Stat.InventoryRows, inv, Slot(3));
        Assert.Equal(2, e.Sources.Length);
        Assert.Equal(PlayerSlot.None, e.Sources[0].OwnerSlot);
        Assert.Equal(Slot(3), e.Sources[1].OwnerSlot);
    }

    [Fact]
    [Trait("Id", "CD15")]
    public void BothStat_SlotWithoutPlayerModifiers_ReadsTeamPlusBaseOnly()
    {
        // Arrange
        ushort inv = Container("player_inventory");
        IStatSheet sheet = Build(Team(Stat.InventoryRows, ModifierOp.Mul, 2000, inv), Mine(1, Stat.InventoryRows, ModifierOp.Add, 3, inv));

        // Act / Assert: slot 7 has no player and no modifiers
        Assert.Equal(4, sheet.Read(Stat.InventoryRows, inv, Slot(7)));
        Assert.Equal(4, sheet.Read(Stat.InventoryRows, inv));
        Assert.Equal(7, sheet.Read(Stat.InventoryRows, inv, Slot(1)));
    }

    [Fact]
    [Trait("Id", "CD15")]
    public void PersonalStat_IgnoresOtherSlotsAndReadsBaseOnTheTeamView()
    {
        // Arrange
        IStatSheet sheet = Build(Mine(2, Stat.ExtraJumps, ModifierOp.Add, 1));

        // Act / Assert
        Assert.Equal(1, sheet.Read(Stat.ExtraJumps, slot: Slot(2)));
        Assert.Equal(0, sheet.Read(Stat.ExtraJumps, slot: Slot(1)));
        Assert.Equal(0, sheet.Read(Stat.ExtraJumps));
    }

    [Fact]
    [Trait("Id", "CD15")]
    public void TeamOnlyStat_IgnoresTheSlot()
    {
        // Arrange: MailMaxStack letter 20, team Mul 1500 -> 30
        ushort letter = MailKind("letter");
        IStatSheet sheet = Build(Team(Stat.MailMaxStack, ModifierOp.Mul, 1500, letter));

        // Act / Assert
        Assert.Equal(30, sheet.Read(Stat.MailMaxStack, letter));
        for (int slot = 0; slot < PlayerSlot.Count; slot++)
        {
            Assert.Equal(30, sheet.Read(Stat.MailMaxStack, letter, Slot(slot)));
            Assert.Equal(30, sheet.Explain(Stat.MailMaxStack, letter, Slot(slot)).Resolved);
        }
    }

    [Fact]
    [Trait("Id", "CD11")]
    public void UnqualifiedModifier_OnAQualifiedStat_AppliesToEveryQualifier()
    {
        // Arrange
        IStatSheet baseline = Empty();
        IStatSheet sheet = Build(Team(Stat.InventoryRows, ModifierOp.Mul, 2000));
        StatBounds b = Bounds(Stat.InventoryRows);

        // Act / Assert: ceil(2 * rows), clamped
        for (int q = 0; q < _content.Containers.Count; q++)
        {
            int rows = baseline.Read(Stat.InventoryRows, (ushort)q);
            Assert.Equal(Math.Clamp(rows * 2, b.Min, b.Max), sheet.Read(Stat.InventoryRows, (ushort)q));
            Assert.Single(sheet.Explain(Stat.InventoryRows, (ushort)q).Sources.ToArray());
            Assert.Equal(None, sheet.Explain(Stat.InventoryRows, (ushort)q).Sources[0].Qualifier);
        }
    }

    [Fact]
    [Trait("Id", "CD11")]
    public void QualifiedModifier_AppliesOnlyToItsDefIndex()
    {
        // Arrange
        IStatSheet baseline = Empty();
        ushort inv = Container("player_inventory");
        IStatSheet sheet = Build(Team(Stat.InventoryRows, ModifierOp.Add, 1, inv));

        // Act / Assert
        for (int q = 0; q < _content.Containers.Count; q++)
        {
            int before = baseline.Read(Stat.InventoryRows, (ushort)q);
            int after = sheet.Read(Stat.InventoryRows, (ushort)q);
            Assert.Equal(q == inv ? before + 1 : before, after);
            Assert.Equal(q == inv ? 1 : 0, sheet.Explain(Stat.InventoryRows, (ushort)q).Sources.Length);
        }
    }

    [Fact]
    [Trait("Id", "CD11")]
    public void SetStat_AnyLiveSetWins_MaxWinsAndMinWins()
    {
        // Arrange: LateValueRatioPm is max-wins (team); RespawnMs is min-wins (personal)
        StatBounds respawn = Bounds(Stat.RespawnMs);
        int low = respawn.Min + 1;
        int high = respawn.Min + 2;
        IStatSheet sheet = Build(
            Team(Stat.LateValueRatioPm, ModifierOp.Set, 700),
            Team(Stat.LateValueRatioPm, ModifierOp.Set, 900, stack: 1),
            Mine(0, Stat.RespawnMs, ModifierOp.Set, high),
            Mine(0, Stat.RespawnMs, ModifierOp.Set, low, stack: 1));

        // Act / Assert
        Assert.Equal(900, sheet.Read(Stat.LateValueRatioPm));
        Assert.Equal(900, sheet.Explain(Stat.LateValueRatioPm).Unclamped);
        Assert.Equal(low, sheet.Read(Stat.RespawnMs, slot: Slot(0)));
        Assert.Equal(_content.Player.RespawnMs, sheet.Read(Stat.RespawnMs, slot: Slot(1)));
    }

    [Fact]
    [Trait("Id", "CD11")]
    public void SetBelowMin_ClampsAndExplainsMin()
    {
        // Arrange
        StatBounds respawn = Bounds(Stat.RespawnMs);
        IStatSheet sheet = Build(Mine(4, Stat.RespawnMs, ModifierOp.Set, respawn.Min - 100));

        // Act
        StatExplain e = sheet.Explain(Stat.RespawnMs, slot: Slot(4));

        // Assert
        Assert.Equal(respawn.Min, e.Resolved);
        Assert.Equal(respawn.Min - 100, e.Unclamped);
        Assert.Equal(StatClamp.Min, e.ClampedAt);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-5")]
    public void SourceOrder_DoesNotChangeAnyCell()
    {
        // Arrange
        ushort inv = Container("player_inventory");
        Modifier[] mods =
        [
            Team(Stat.InventoryRows, ModifierOp.Mul, 1500, inv),
            Team(Stat.InventoryRows, ModifierOp.Mul, 1200, None, stack: 1),
            Mine(2, Stat.InventoryRows, ModifierOp.Add, 1, inv),
            Mine(2, Stat.MoveSpeedAddPm, ModifierOp.Add, 50),
            Team(Stat.LateValueRatioPm, ModifierOp.Set, 800),
            Mine(5, Stat.WeightPenaltyPm, ModifierOp.Mul, 800),
            Team(Stat.MailValueMultPm, ModifierOp.Mul, 1100, MailKind("letter")),
        ];
        Modifier[] reversed = (Modifier[])mods.Clone();
        Array.Reverse(reversed);
        Modifier[] rotated = [mods[3], mods[4], mods[5], mods[6], mods[0], mods[1], mods[2]];

        // Act
        List<int> a = Sweep(Build(mods));
        List<int> b = Sweep(Build(reversed));
        List<int> c = Sweep(Build(rotated));

        // Assert
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    [Trait("Id", "TR-content-017")]
    public void Build_SameInputsTwice_GivesIdenticalSheets()
    {
        // Arrange
        Modifier[] mods = [.. LongLegs(1), Team(Stat.MisdeliveryPenaltyMultPm, ModifierOp.Mul, 900)];

        // Act / Assert
        Assert.Equal(Sweep(Build(mods)), Sweep(Build(mods)));
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => StatSheet.Build(null!, new StatSources([])));
        Assert.Throws<ArgumentNullException>(() => StatSheet.Build(_content, null!));
    }

    [Fact]
    public void Build_SixMuls_ResolvesWithK6()
    {
        // Arrange: MailValueMultPm base 1000, six Mul 1100 -> 1000 * 1.1^6 = 1771.561 -> 1772 (HalfUp), clamped by payMultMaxPm
        Modifier[] mods = new Modifier[6];
        for (int i = 0; i < mods.Length; i++)
        {
            mods[i] = Team(Stat.MailValueMultPm, ModifierOp.Mul, 1100, None, (byte)i);
        }

        // Act
        IStatSheet sheet = Build(mods);

        // Assert
        Assert.Equal(Math.Min(1772, Bounds(Stat.MailValueMultPm).Max), sheet.Read(Stat.MailValueMultPm, MailKind("letter")));
        Assert.Equal(6, sheet.Explain(Stat.MailValueMultPm, MailKind("letter")).Sources.Length);
    }

    // ------------------------------------------------------------ inapplicable modifiers at Build

    public static TheoryData<string> BadModifierCases() => new()
    {
        "unknown-stat",
        "team-mod-on-personal-stat",
        "slot-mod-on-team-stat",
        "op-not-accepted",
        "qualifier-on-unqualified-stat",
        "qualifier-out-of-range",
        "undefined-op",
    };

    private static Modifier BadModifier(string which) => which switch
    {
        "unknown-stat" => Team((Stat)99, ModifierOp.Add, 1),
        "team-mod-on-personal-stat" => Team(Stat.MoveSpeedAddPm, ModifierOp.Add, 100),
        "slot-mod-on-team-stat" => Mine(0, Stat.MisdeliveryPenaltyMultPm, ModifierOp.Mul, 900),
        "op-not-accepted" => Team(Stat.MailMaxStack, ModifierOp.Add, 5),
        "qualifier-on-unqualified-stat" => Mine(0, Stat.ExtraJumps, ModifierOp.Add, 1, qualifier: 3),
        "qualifier-out-of-range" => Team(Stat.MailMaxStack, ModifierOp.Mul, 2000, qualifier: (ushort)_content.MailKinds.Count),
        _ => Team(Stat.MailMaxStack, (ModifierOp)9, 2000),
    };

    [Theory]
    [MemberData(nameof(BadModifierCases))]
    [Trait("Id", "TR-content-017")]
    public void Build_InapplicableModifier_TripsAssertOrIsSkipped(string which)
    {
        // Arrange
        Modifier good = Team(Stat.MailMaxStack, ModifierOp.Mul, 1500, MailKind("letter"));
        Modifier bad = BadModifier(which);
        SimAssertException? thrown = null;
        IStatSheet? sheet = null;

        // Act
        try
        {
            sheet = Build(bad, good);
        }
        catch (SimAssertException e)
        {
            thrown = e;
        }

        // Assert: Release skips the bad one and keeps the good one
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(thrown);
            Assert.Equal(ContentAssertCode.StatSheetBadModifier, thrown.Code);
        }
        else
        {
            Assert.Null(thrown);
            Assert.NotNull(sheet);
            Assert.Equal(30, sheet.Read(Stat.MailMaxStack, MailKind("letter")));
            Assert.Equal(0, sheet.Read(Stat.MoveSpeedAddPm, slot: Slot(0)));
            Assert.Equal(0, sheet.Read(Stat.ExtraJumps, slot: Slot(0)));
            Assert.Equal(1000, sheet.Read(Stat.MisdeliveryPenaltyMultPm));
        }
    }

    // ------------------------------------------------------------ AC-4: 0 B and structure

    [Fact]
    [Trait("Id", "CDD-ID-4")]
    public void Read_OneMillionCalls_AllocateNothing_WithoutATransient()
    {
        SkipUnlessOptimised();

        // Arrange
        IStatSheet sheet = Build(LongLegs(1));
        ushort inv = Container("player_inventory");
        PlayerSlot slot = Slot(1);
        long sink = 0;

        // Act / Assert
        _ = AllocAssert.Zero("1,000,000 Read calls, no transient", () =>
        {
            long sum = 0;
            for (int i = 0; i < 1_000_000; i++)
            {
                sum += sheet.Read(Stat.MoveSpeedAddPm, None, slot);
                sum += sheet.Read(Stat.InventoryRows, inv, slot);
            }

            sink = sum;
        });
        Assert.Equal(1_000_000L * (360 + 2), sink);
    }

    [Fact]
    [Trait("Id", "CDD-ID-4")]
    public void Read_OneMillionCalls_AllocateNothing_WithATransient()
    {
        SkipUnlessOptimised();

        // Arrange
        IStatSheet sheet = Build(LongLegs(1));
        PlayerSlot slot = Slot(1);
        long sink = 0;

        // Act / Assert
        _ = AllocAssert.Zero("1,000,000 Read calls with a transient", () =>
        {
            long sum = 0;
            for (int i = 0; i < 1_000_000; i++)
            {
                sum += sheet.Read(Stat.MoveSpeedAddPm, None, slot, 300);
            }

            sink = sum;
        });
        Assert.Equal(1_000_000L * 400, sink);
    }

    [Fact]
    [Trait("Id", "TR-content-019")]
    public void Explain_ManyCalls_AllocateNothing()
    {
        SkipUnlessOptimised();

        // Arrange
        IStatSheet sheet = Build(LongLegs(1));
        PlayerSlot slot = Slot(1);
        long sink = 0;

        // Act / Assert
        _ = AllocAssert.Zero("10,000 Explain calls", () =>
        {
            long sum = 0;
            for (int i = 0; i < 10_000; i++)
            {
                StatExplain e = sheet.Explain(Stat.MoveSpeedAddPm, None, slot, 300);
                sum += e.Unclamped + e.Sources.Length;
            }

            sink = sum;
        });
        Assert.Equal(10_000L * 663, sink);
    }

    [Fact]
    [Trait("Id", "CDD-PERF-2")]
    public void ReadImplementation_HoldsNoReferenceToTheModifierPoolOrSources()
    {
        // Arrange: every type reachable through the fields of the sheet's implementation type
        Type implementation = StatSheet.Build(_content, new StatSources([])).GetType();
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(implementation);

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

        // Assert
        Assert.Equal("StatSheetCache", implementation.Name);
        Assert.DoesNotContain(typeof(Modifier), seen);
        Assert.DoesNotContain(typeof(StatSources), seen);
        Assert.DoesNotContain(typeof(ContentSet), seen);
        Assert.DoesNotContain(typeof(ImmutableArray<Modifier>), seen);
        Assert.DoesNotContain(typeof(List<Modifier>), seen);
        Assert.Contains(typeof(StatContribution), seen);
    }

    [Fact]
    [Trait("Id", "CDD-PERF-2")]
    public void StatSheetCache_HasNoStaticFields()
    {
        // Arrange / Act
        FieldInfo[] statics = typeof(StatSheetCache).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Concat(typeof(StatSheet).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(f => !f.IsLiteral)
            .ToArray();

        // Assert: Build is pure (no static state, ADR-0003 section 4)
        Assert.Empty(statics);
    }

    // ------------------------------------------------------------ AC-5: invalid reads (Debug asserts, Release deterministic)

    [Fact]
    [Trait("Id", "StatSheet-edge")]
    public void Read_QualifiedStatWithNoQualifier_AssertsOrReturnsClampMin()
    {
        // Arrange: sources present so the fallback cannot be a cell value
        IStatSheet sheet = Build(Team(Stat.InventoryRows, ModifierOp.Mul, 3000), Team(Stat.MailMaxStack, ModifierOp.Mul, 3000));

        // Act / Assert
        AssertTripsOrReturns(() => sheet.Read(Stat.InventoryRows), ContentAssertCode.StatSheetBadQualifier, Bounds(Stat.InventoryRows).Min);
        AssertTripsOrReturns(() => sheet.Read(Stat.InventoryRows, None, Slot(1)), ContentAssertCode.StatSheetBadQualifier, Bounds(Stat.InventoryRows).Min);
        AssertTripsOrReturns(() => sheet.Read(Stat.MailMaxStack, None), ContentAssertCode.StatSheetBadQualifier, Bounds(Stat.MailMaxStack).Min);
    }

    [Theory]
    [Trait("Id", "StatSheet-edge")]
    [InlineData(Stat.InventoryRows, 0)]
    [InlineData(Stat.MailMaxStack, 1)]
    [InlineData(Stat.MailValueMultPm, 2)]
    public void Read_QualifierFromTheWrongKindsRange_AssertsOrReturnsClampMin(Stat stat, int which)
    {
        // Arrange: the first ordinal past the kind's table, and a far-out one (a mail-kind ordinal can be too big for containers)
        IStatSheet sheet = Empty();
        ushort past = (ushort)(which == 0 ? _content.Containers.Count : _content.MailKinds.Count);
        int min = Bounds(stat).Min;

        // Act / Assert
        AssertTripsOrReturns(() => sheet.Read(stat, past), ContentAssertCode.StatSheetBadQualifier, min);
        AssertTripsOrReturns(() => sheet.Read(stat, 0xFFFE), ContentAssertCode.StatSheetBadQualifier, min);
    }

    [Fact]
    [Trait("Id", "StatSheet-edge")]
    public void Read_SlotNone_AssertsOrReturnsTheTeamOnlyValue()
    {
        // Arrange: slot 3 would read 4, team reads 3
        ushort inv = Container("player_inventory");
        IStatSheet sheet = Build(Team(Stat.InventoryRows, ModifierOp.Mul, 1500, inv), Mine(3, Stat.InventoryRows, ModifierOp.Add, 1, inv), Mine(0, Stat.ExtraJumps, ModifierOp.Add, 1));
        PlayerSlot none = PlayerSlot.None;

        // Act / Assert
        AssertTripsOrReturns(() => sheet.Read(Stat.InventoryRows, inv, none), ContentAssertCode.StatSheetBadSlot, 3);
        AssertTripsOrReturns(() => sheet.Read(Stat.ExtraJumps, None, none), ContentAssertCode.StatSheetBadSlot, 0);
        AssertTripsOrReturns(() => sheet.Read(Stat.MailMaxStack, MailKind("letter"), none), ContentAssertCode.StatSheetBadSlot, 20);
    }

    [Fact]
    [Trait("Id", "StatSheet-edge")]
    public void Read_NegativeTransient_AssertsOrTreatsItAsZero()
    {
        // Arrange
        IStatSheet sheet = Build(LongLegs(2));

        // Act / Assert
        AssertTripsOrReturns(() => sheet.Read(Stat.MoveSpeedAddPm, None, Slot(2), -50), ContentAssertCode.StatSheetNegativeTransient, 360);
        AssertTripsOrReturns(() => sheet.Read(Stat.MoveSpeedAddPm, None, Slot(2), int.MinValue), ContentAssertCode.StatSheetNegativeTransient, 360);
    }

    [Fact]
    [Trait("Id", "StatSheet-edge")]
    public void Read_TransientOnAStatWithoutAdd_AssertsOrTreatsItAsZero()
    {
        // Arrange: MailMaxStack is Mul-only, RespawnMs is Set-only, WeightPenaltyPm is Mul-only
        IStatSheet sheet = Build(Team(Stat.MailMaxStack, ModifierOp.Mul, 1500, MailKind("letter")), Mine(0, Stat.RespawnMs, ModifierOp.Set, Bounds(Stat.RespawnMs).Min + 5));

        // Act / Assert
        AssertTripsOrReturns(() => sheet.Read(Stat.MailMaxStack, MailKind("letter"), null, 10), ContentAssertCode.StatSheetTransientNotAdd, 30);
        AssertTripsOrReturns(() => sheet.Read(Stat.RespawnMs, None, Slot(0), 10), ContentAssertCode.StatSheetTransientNotAdd, Bounds(Stat.RespawnMs).Min + 5);
        AssertTripsOrReturns(() => sheet.Read(Stat.WeightPenaltyPm, None, Slot(0), 10), ContentAssertCode.StatSheetTransientNotAdd, 1000);
    }

    [Fact]
    [Trait("Id", "StatSheet-edge")]
    public void Read_UnknownStat_AssertsOrReturnsZero()
    {
        // Arrange
        IStatSheet sheet = Empty();

        // Act / Assert
        AssertTripsOrReturns(() => sheet.Read((Stat)0), ContentAssertCode.StatSheetUnknownStat, 0);
        AssertTripsOrReturns(() => sheet.Read((Stat)(StatDefs.Count + 1)), ContentAssertCode.StatSheetUnknownStat, 0);
    }

    [Fact]
    [Trait("Id", "StatSheet-edge")]
    public void Explain_InvalidArguments_AssertOrReturnTheSameFallbackWithNoSources()
    {
        // Arrange
        ushort inv = Container("player_inventory");
        IStatSheet sheet = Build(Team(Stat.InventoryRows, ModifierOp.Mul, 1500, inv), Mine(3, Stat.InventoryRows, ModifierOp.Add, 1, inv), LongLegs(3)[0]);

        // Act / Assert
        AssertTripsOrReturns(() => Expect(sheet.Explain(Stat.InventoryRows), 1, 0), ContentAssertCode.StatSheetBadQualifier, 1);
        AssertTripsOrReturns(() => Expect(sheet.Explain((Stat)77), 0, 0), ContentAssertCode.StatSheetUnknownStat, 0);
        AssertTripsOrReturns(() => Expect(sheet.Explain(Stat.InventoryRows, inv, PlayerSlot.None), 3, 1), ContentAssertCode.StatSheetBadSlot, 3);
        AssertTripsOrReturns(() => Expect(sheet.Explain(Stat.MoveSpeedAddPm, None, Slot(3), -1), 120, 1), ContentAssertCode.StatSheetNegativeTransient, 120);
        AssertTripsOrReturns(() => Expect(sheet.Explain(Stat.RespawnMs, None, Slot(3), 5), _content.Player.RespawnMs, 0), ContentAssertCode.StatSheetTransientNotAdd, _content.Player.RespawnMs);

        static int Expect(StatExplain e, int resolved, int sources)
        {
            Assert.Equal(sources, e.Sources.Length);
            Assert.Equal(0, e.TransientAdd);
            return e.Resolved == resolved ? e.Resolved : -1;
        }
    }

    [Fact]
    [Trait("Id", "StatSheet-edge")]
    public void Read_QualifierOnAnUnqualifiedStat_AssertsOrIsIgnored()
    {
        // Arrange
        IStatSheet sheet = Build(LongLegs(2));

        // Act / Assert: Release ignores the qualifier and reads the cell
        AssertTripsOrReturns(() => sheet.Read(Stat.MoveSpeedAddPm, 7, Slot(2)), ContentAssertCode.StatSheetQualifierOnUnqualified, 360);
        AssertTripsOrReturns(() => sheet.Explain(Stat.MoveSpeedAddPm, 7, Slot(2)).Resolved, ContentAssertCode.StatSheetQualifierOnUnqualified, 360);
        AssertTripsOrReturns(() => sheet.Read(Stat.MoveSpeedAddPm, 0, Slot(2), 300), ContentAssertCode.StatSheetQualifierOnUnqualified, 400);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Build_SevenMulFactorsOnOneCell_TripsTheMulAssertOrResolvesDeterministically()
    {
        // Arrange: seven neutral Muls on one cell, so a build without the assert still has a checkable value
        Modifier[] mods = new Modifier[7];
        for (int i = 0; i < mods.Length; i++)
        {
            mods[i] = Team(Stat.MailValueMultPm, ModifierOp.Mul, 1000, None, (byte)i);
        }

        // Act / Assert
        AssertTripsOrReturns(
            () => Build(mods).Read(Stat.MailValueMultPm, MailKind("letter")),
            ContentAssertCode.StatMulFactorsExceeded,
            Math.Min(1000, Bounds(Stat.MailValueMultPm).Max));
    }

    [Fact]
    [Trait("Id", "CD11")]
    public void SetWithAnAdd_OnOneStat_TheSetWinsAtSheetLevel()
    {
        // Arrange: no M-1a stat accepts both Set and Add, so the Add is refused at Build (asserted) and skipped otherwise;
        // either way no Add can reach a Set stat, and a transient on it is refused too. The Set-beats-Add arithmetic
        // itself is covered by ResolveWithTransient_MatchesResolveAndKeepsASetWinner.
        Modifier set = Team(Stat.LateValueRatioPm, ModifierOp.Set, 700);
        Modifier add = Team(Stat.LateValueRatioPm, ModifierOp.Add, 50, None, 1);

        // Act / Assert
        AssertTripsOrReturns(() => Build(set, add).Read(Stat.LateValueRatioPm), ContentAssertCode.StatSheetBadModifier, 700);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            IStatSheet sheet = Build(set, add);
            Assert.Equal(700, sheet.Read(Stat.LateValueRatioPm, None, null, 100));
            Assert.Equal(700, sheet.Explain(Stat.LateValueRatioPm, None, null, 100).Unclamped);
            Assert.Single(sheet.Explain(Stat.LateValueRatioPm).Sources.ToArray());
        }
    }

    // ------------------------------------------------------------ Explain

    [Fact]
    [Trait("Id", "TR-content-019")]
    public void Explain_ListsContributorsTeamFirstThenSlot_WithTheirIdentity()
    {
        // Arrange
        ushort inv = Container("player_inventory");
        var teamMod = new Modifier(SourceKind.Stamp, DefIndex.FromValue(4), 1, PlayerSlot.None, Stat.InventoryRows, None, ModifierOp.Mul, 1500, ModifierLifetime.Shift);
        var mineMod = new Modifier(SourceKind.RunEvent, DefIndex.FromValue(9), 2, Slot(6), Stat.InventoryRows, inv, ModifierOp.Add, 1, ModifierLifetime.Run);
        IStatSheet sheet = Build(mineMod, teamMod);

        // Act
        StatExplain e = sheet.Explain(Stat.InventoryRows, inv, Slot(6));

        // Assert: team first although it was given second
        Assert.Equal(2, e.Sources.Length);
        Assert.Equal(new StatContribution(SourceKind.Stamp, DefIndex.FromValue(4), 1, PlayerSlot.None, None, ModifierOp.Mul, 1500, ModifierLifetime.Shift), e.Sources[0]);
        Assert.Equal(new StatContribution(SourceKind.RunEvent, DefIndex.FromValue(9), 2, Slot(6), inv, ModifierOp.Add, 1, ModifierLifetime.Run), e.Sources[1]);
        Assert.Equal(4, e.Resolved);
        Assert.Equal(4, e.Unclamped);
    }

    [Fact]
    [Trait("Id", "TR-content-019")]
    public void Explain_UnclampedIsSaturatedWithTheSetValue_AndSourcesViewIsStable()
    {
        // Arrange
        IStatSheet sheet = Build(Team(Stat.LateValueRatioPm, ModifierOp.Set, 2000));

        // Act
        StatExplain first = sheet.Explain(Stat.LateValueRatioPm);
        StatExplain second = sheet.Explain(Stat.LateValueRatioPm);

        // Assert: Set 2000 above max 1000
        Assert.Equal(1000, first.Resolved);
        Assert.Equal(2000, first.Unclamped);
        Assert.Equal(StatClamp.Max, first.ClampedAt);
        Assert.Equal(first.Sources.ToArray(), second.Sources.ToArray());
        Assert.Equal(0, default(StatExplain).Sources.Length);
    }

    // ------------------------------------------------------------ transient evaluation is bit-identical to F1

    [Fact]
    [Trait("Id", "CDD-STAT-8")]
    public void TransientRead_EqualsAFullF1ResolveOverTheSameSources()
    {
        // Arrange: Add-only stats with positive, negative and capped sums
        int[][] addSets = [[], [120], [120, 120, 120], [-500, 40], [10_000, 10_000], [-1, -2, -3]];
        foreach (int[] adds in addSets)
        {
            Modifier[] mods = adds.Select((a, i) => Mine(0, Stat.ThrowRangePermille, ModifierOp.Add, a, None, (byte)i)).ToArray();
            IStatSheet sheet = Build(mods);
            StatDef def = StatDefs.Get(Stat.ThrowRangePermille);
            StatBounds b = Bounds(Stat.ThrowRangePermille);

            foreach (int transient in new[] { 0, 1, 37, 300, 999, 100_000, int.MaxValue })
            {
                // Act
                StatResolution reference = StatResolve.Resolve(def, b, 1000, adds, [], [], transient);
                StatExplain e = sheet.Explain(Stat.ThrowRangePermille, None, Slot(0), transient);

                // Assert
                Assert.Equal(reference.Value, sheet.Read(Stat.ThrowRangePermille, None, Slot(0), transient));
                Assert.Equal(reference.Value, e.Resolved);
                Assert.Equal(reference.Unclamped, e.Unclamped);
                Assert.Equal(reference.ClampedAt, e.ClampedAt);
            }
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-8")]
    public void TransientRead_OnMulAndAddCell_EqualsAFullF1Resolve()
    {
        // Arrange: InventoryRows (Ceil, Mul and Add): team Mul 1500 and 1100, slot Add 1, base 2
        ushort inv = Container("player_inventory");
        IStatSheet sheet = Build(
            Team(Stat.InventoryRows, ModifierOp.Mul, 1500, inv),
            Team(Stat.InventoryRows, ModifierOp.Mul, 1100, inv, 1),
            Mine(2, Stat.InventoryRows, ModifierOp.Add, 1, inv));
        StatDef def = StatDefs.Get(Stat.InventoryRows);

        foreach (int transient in new[] { 0, 1, 2, 3, 50 })
        {
            // Act
            StatResolution reference = StatResolve.Resolve(def, Bounds(Stat.InventoryRows), 2, [1], [1500, 1100], [], transient);

            // Assert
            Assert.Equal(reference.Value, sheet.Read(Stat.InventoryRows, inv, Slot(2), transient));
            Assert.Equal(reference.Unclamped, sheet.Explain(Stat.InventoryRows, inv, Slot(2), transient).Unclamped);
        }
    }

    [Fact]
    [Trait("Id", "TR-content-018")]
    public void ResolveWithTransient_MatchesResolveAndKeepsASetWinner()
    {
        // Arrange
        StatResolution cachedAdds = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 0, 400, 0, [120, 120, 120], [], [], 0);
        StatResolution cachedSet = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.MaxWins, 0, 1000, 500, [10], [2000], [700, 900], 0);

        // Act
        StatResolution capped = StatResolve.ResolveWithTransient(StatRounding.HalfUp, 0, 400, cachedAdds, 300);
        StatResolution stillSet = StatResolve.ResolveWithTransient(StatRounding.HalfUp, 0, 1000, cachedSet, 300);

        // Assert
        Assert.Equal(StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 0, 400, 0, [120, 120, 120], [], [], 300), capped);
        Assert.Equal(660, capped.Unclamped);
        Assert.Equal(StatClamp.Max, capped.ClampedAt);
        Assert.Equal(900, stillSet.Value);
        Assert.True(stillSet.HasSet);
        Assert.Equal(cachedSet.Value, stillSet.Value);
    }

    [Fact]
    [Trait("Id", "TR-content-018")]
    public void ResolveWithTransient_AddsToAnEarlierTransient()
    {
        // Arrange
        StatResolution cached = StatResolve.Resolve(StatRounding.Floor, SetResolve.NotApplicable, 0, 10_000, 100, [], [1500], [], 20);

        // Act
        StatResolution again = StatResolve.ResolveWithTransient(StatRounding.Floor, 0, 10_000, cached, 30);

        // Assert: 100 * 1.5 + 50 = 200
        Assert.Equal(200, again.Value);
        Assert.Equal(50, again.SumA);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void ResolveWithTransient_BadCachedFactorCountOrBounds_TripsAssertWhenAssertsEnabled()
    {
        // Arrange
        var sevenFactors = new StatResolution(0, 7, 0, false, 0, 0, StatClamp.None, 0);
        var ok = new StatResolution(0, 0, 0, false, 0, 0, StatClamp.None, 0);

        // Act / Assert
        AssertTripsOrReturns(() => StatResolve.ResolveWithTransient(StatRounding.HalfUp, 0, 10, sevenFactors, 0).Value, ContentAssertCode.StatMulFactorsExceeded, 0);
        AssertTripsOrReturns(() => StatResolve.ResolveWithTransient(StatRounding.HalfUp, 10, 0, ok, 5).Value, ContentAssertCode.StatBoundsInverted, 10);
    }

    // ------------------------------------------------------------ StatSources, Modifier, enums

    [Fact]
    [Trait("Id", "TR-content-017")]
    public void StatSources_SplitsByOwnerKeepingOrder_AndCopiesItsInput()
    {
        // Arrange
        Modifier a = Team(Stat.MailMaxStack, ModifierOp.Mul, 1100, None, 0);
        Modifier b = Mine(2, Stat.ExtraJumps, ModifierOp.Add, 1, None, 0);
        Modifier c = Team(Stat.MailMaxStack, ModifierOp.Mul, 1200, None, 1);
        Modifier d = Mine(2, Stat.ExtraJumps, ModifierOp.Add, 2, None, 1);
        Modifier e = Mine(7, Stat.ExtraJumps, ModifierOp.Add, 3, None, 0);
        Modifier[] input = [a, b, c, d, e];

        // Act
        var sources = new StatSources(input);
        input[0] = Team(Stat.MailMaxStack, ModifierOp.Mul, 9999);

        // Assert
        Assert.Equal(5, sources.Count);
        Assert.Equal(new[] { a, c }, sources.Team.ToArray());
        Assert.Equal(new[] { b, d }, sources.ForSlot(Slot(2)).ToArray());
        Assert.Equal(new[] { e }, sources.ForSlot(Slot(7)).ToArray());
        Assert.Equal(0, sources.ForSlot(Slot(0)).Length);
        Assert.Equal(0, sources.ForSlot(PlayerSlot.None).Length);
    }

    [Fact]
    [Trait("Id", "TR-content-017")]
    public void StatSources_Empty_HasNoModifiers()
    {
        // Arrange / Act
        var sources = new StatSources([]);

        // Assert
        Assert.Equal(0, sources.Count);
        Assert.Equal(0, sources.Team.Length);
        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            Assert.Equal(0, sources.ForSlot(Slot(s)).Length);
        }
    }

    [Fact]
    public void Modifier_HasStructuralEquality()
    {
        // Arrange
        Modifier a = Mine(1, Stat.ExtraJumps, ModifierOp.Add, 1);
        Modifier b = Mine(1, Stat.ExtraJumps, ModifierOp.Add, 1);

        // Assert
        Assert.Equal(a, b);
        Assert.NotEqual(a, Mine(2, Stat.ExtraJumps, ModifierOp.Add, 1));
        Assert.NotEqual(a, a with { Value = 2 });
    }

    [Fact]
    public void ModifierEnums_AreClosedAndFrozen()
    {
        Assert.Equal(new byte[] { 1, 2, 3 }, new[] { (byte)SourceKind.Perk, (byte)SourceKind.Stamp, (byte)SourceKind.RunEvent });
        Assert.Equal(new byte[] { 1, 2, 3 }, new[] { (byte)ModifierOp.Add, (byte)ModifierOp.Mul, (byte)ModifierOp.Set });
        Assert.Equal(new byte[] { 1, 2 }, new[] { (byte)ModifierLifetime.Run, (byte)ModifierLifetime.Shift });
        Assert.Equal(3, Enum.GetValues<SourceKind>().Length);
        Assert.Equal(StatSheet.NoQualifier, (ushort)0xFFFF);
        Assert.Equal(9, StatSheet.SheetCount);
    }

    [Fact]
    public void IStatSheet_DeclaresExactlyTheReadFace()
    {
        // Story 015 added Has(RuleFlag, slot) and Version to the story 014 pair (Read, Explain); nothing here writes.
        string[] members = typeof(IStatSheet).GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "Explain", "Has", "Read", "get_Version" }, members);
        Assert.DoesNotContain(typeof(IStatSheet).GetProperties(), p => p.CanWrite);
    }
}
