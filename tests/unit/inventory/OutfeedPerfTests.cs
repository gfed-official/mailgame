using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/012, H-PERF-6a (GDD R14; ADR-0009 section 5 and Risks "probe accounting": a candidate is one FIFO node
/// inspected, tombstone pops included; Planner R7): <see cref="IInventoryApi.PeekOutfeed"/> and <see cref="IInventoryApi.TakeUnit"/>
/// visit at most 4 candidates per call amortised over 10,000 balanced take/spawn cycles (n stays constant), at n = 320 (the M-1a
/// <c>po_intake</c>, 20 x 16) and n = 6,400 (a 64 x 100 test container; the GDD's 80 x 80 cannot be built: <c>Occupancy.BitmapWidth</c>
/// is 64). The count is <see cref="CounterId.InventoryOutfeedCandidates"/>, compiled into every build, so these run (and must pass) in
/// Debug and Release alike: no Release-only skip, no twin needed. Each cycle also checks the FIFO order, so a wrong-but-cheap
/// outfeed fails too. The calls run inside real ticks (the rig's <c>MailStep</c> hook), 50 cycles per tick. Each case writes its
/// measured mean candidates per call to the test output, so the margin to the bound is visible.
/// </summary>
public class OutfeedPerfTests
{
    private readonly ITestOutputHelper _output;

    public OutfeedPerfTests(ITestOutputHelper output) => _output = output;

    /// <summary>The H-PERF-6a bound: candidates per Peek/Take call, amortised.</summary>
    private const double MaxCandidatesPerCall = 4.0;

    private const int Cycles = 10_000;

    private const int CyclesPerTick = 50;

    /// <summary>The 64 x 100 fixture def's id (test content only; <c>BuiltInContent</c> is not edited, content-data 018 owns it).</summary>
    private const string WideDef = "test_outfeed_64";

    [Fact]
    [Trait("Id", "H-PERF-6a")]
    public void PeekAndTake_AtN320OnPoIntake_VisitAtMost4CandidatesPerCall_Amortised()
    {
        // Arrange: po_intake full (320 count-1 letters)
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1);

        // Act
        Result r = RunBalanced(rig, rig.Inventory.IntakeContainer, firstId: 1, n: 320);

        // Assert
        AssertBound(in r, 320, "n = 320 (po_intake 20 x 16)");
    }

    [Fact]
    [Trait("Id", "H-PERF-6a")]
    public void PeekAndTake_AtN6400OnA64x100Container_VisitAtMost4CandidatesPerCall_Amortised()
    {
        // Arrange: the 64 x 100 outfeed container, full (6,400 count-1 letters)
        var rig = new InvAutoPlaceRig(content: WideContent());
        ContainerId wide = CreateWide(rig);
        Fill(rig, wide, 6_400, 1);

        // Act
        Result r = RunBalanced(rig, wide, firstId: 1, n: 6_400);

        // Assert
        AssertBound(in r, 6_400, "n = 6,400 (64 x 100)");
    }

    [Fact]
    [Trait("Id", "H-PERF-6a")]
    public void DrainingAnIntakeWithHalfItsEntriesRemovedMidQueue_StaysWithin4CandidatesPerCall_Amortised()
    {
        // Arrange: 320 letters; every second one (seq 2, 4, ...) leaves through the mutator, leaving 160 mid-queue tombstones
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 320, 1);
        for (int y = 0; y < 16; y++)
        {
            int row = y;
            Reply removed = rig.Step(
                (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) =>
                {
                    for (int x = 1; x < 20; x += 2)
                    {
                        rig.Inventory.Mutator.Remove(ref p, rig.EntryAt(rig.Intake, x, row));
                    }
                });
            Assert.True(removed.IsOk);
        }

        long before = rig.Counters[CounterId.InventoryOutfeedCandidates];
        var taken = new List<uint>();
        int calls = 0;

        // Act: drain the 160 live entries (peek, then take, per entry) until a peek finds the outfeed empty
        rig.Hook.Action = (in TickContext ctx) =>
        {
            for (int i = 0; i < CyclesPerTick; i++)
            {
                calls++;
                if (!rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out _))
                {
                    break;
                }

                calls++;
                bool took = rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out UnitRef u);
                taken.Add(took ? u.Mail.Value : 0u);
            }
        };
        for (int t = 0; t < 4; t++)
        {
            rig.Host.World.Tick();
        }

        rig.Hook.Action = null;
        long candidates = rig.Counters[CounterId.InventoryOutfeedCandidates] - before;

        // Assert: the odd ids in order (the even ones left mid-queue); at least 160 peeks + 160 takes + 1 empty peek
        Assert.Equal(Enumerable.Range(0, 160).Select(i => (uint)(1 + (2 * i))), taken);
        Assert.False(rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out _));
        _output.WriteLine(
            $"H-PERF-6a drain with 160 mid-queue tombstones: {candidates / (double)calls:F3} candidates/call ({candidates} over {calls} calls; bound {MaxCandidatesPerCall:F1})");
        Assert.True(calls >= 321);
        Assert.True(candidates / (double)calls <= MaxCandidatesPerCall, $"{candidates} candidates over {calls} calls");
    }

    /// <summary>The M-1a content plus a 64-wide, mail-only <c>OutfeedOnly</c> grid def; <c>inventory.maxRows</c> 100, arena 65,536 units.</summary>
    internal static ContentSet WideContent()
    {
        var overrides = new Dictionary<string, long>
        {
            ["inventory." + BalanceKeys.Inventory.MaxRowsKey] = 100,
            ["inventory." + BalanceKeys.Inventory.MailIdArenaUnitsKey] = 65_536,
        };
        ImmutableArray<BalanceEntry> entries = BalanceKeys.CreateDefaultEntries()
            .Select(e => overrides.TryGetValue(e.Name, out long v) ? e with { Value = v, Safe = null } : e)
            .ToImmutableArray();
        RuleRegistry rules = RuleRegistry.CreateDefault();
        BalanceBuildResult balance = BalanceTable.Build(BalanceKeys.CreateRegistry(), entries, rules);
        Assert.NotNull(balance.Table);
        ImmutableArray<ContainerDef> containers = BuiltInContent.M1aContainers().Add(new ContainerDef
        {
            Id = WideDef,
            Layout = ContainerLayout.Grid,
            HasGrid = true,
            GridW = 64,
            GridH = 100,
            View = ContainerView.Manifest,
            BeltAccess = BeltAccess.OutfeedOnly,
            AcceptsInsert = true,
            HasAllowedCategories = true,
            AllowedCategories = ImmutableArray.Create(ItemCategory.Mail),
        });
        ContentBuildResult built = BuiltInContent.Build(
            balance.Table!, containers, BuiltInContent.M1aItems(), BuiltInContent.M1aMailKinds(), BuiltInContent.M1aDestinationTypes(), rules);
        Assert.True(built.Set is not null, BuiltInContent.Describe(built.Validation, rules));
        Assert.Equal(100, built.Set!.Inventory.MaxRows);
        Assert.Equal(65_536, built.Set.Inventory.MailIdArenaUnits);
        return built.Set;
    }

    /// <summary>The 64 x 100 container, created through <see cref="InventorySystem.TryCreateContainer"/>.</summary>
    internal static ContainerId CreateWide(InvAutoPlaceRig rig)
    {
        DefIndex def = PlacementFixture.Index(rig.Content.Containers, WideDef);
        Assert.True(rig.Inventory.TryCreateContainer(def, 64, 100, ContainerLayout.Grid, ContainerHeader.NoOwner, out ContainerId id));
        return id;
    }

    /// <summary>Spawns <paramref name="count"/> letters into <paramref name="c"/>, 255 per tick.</summary>
    internal static void Fill(InvAutoPlaceRig rig, ContainerId c, int count, uint firstId)
    {
        DefIndex letter = rig.KindIndex("letter");
        for (int done = 0; done < count;)
        {
            int n = Math.Min(byte.MaxValue, count - done);
            MailId[] ids = InvAutoPlaceRig.Ids(firstId + (uint)done, n).Select(i => new MailId(i)).ToArray();
            SpawnResult r = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.Spawn(c, letter, 4u, 0, ids, MergePolicy.NoMerge));
            Assert.Equal(n, r.Placed);
            done += n;
        }
    }

    /// <summary>
    /// <see cref="Cycles"/> cycles of Peek, Take and Spawn-one (n stays constant); every take must return the id the peek showed, which
    /// must be the oldest unit (ids are spawned ascending, so the expected id rises by 1 per cycle).
    /// </summary>
    private static Result RunBalanced(InvAutoPlaceRig rig, ContainerId c, uint firstId, int n)
    {
        DefIndex letter = rig.KindIndex("letter");
        uint expected = firstId;
        uint nextSpawn = firstId + (uint)n;
        long peekCandidates = 0;
        long takeCandidates = 0;
        int cycles = 0;
        bool orderOk = true;
        var one = new MailId[1];
        rig.CaptureEvents = false;
        rig.Hook.Action = (in TickContext ctx) =>
        {
            for (int i = 0; i < CyclesPerTick && cycles < Cycles; i++, cycles++)
            {
                long c0 = rig.Counters[CounterId.InventoryOutfeedCandidates];
                bool peeked = rig.Inventory.PeekOutfeed(c, Selector.Fifo, out UnitRef p);
                long c1 = rig.Counters[CounterId.InventoryOutfeedCandidates];
                bool took = rig.Inventory.TakeUnit(c, Selector.Fifo, rig.PlayerA, out UnitRef u);
                long c2 = rig.Counters[CounterId.InventoryOutfeedCandidates];
                one[0] = new MailId(nextSpawn++);
                SpawnResult s = rig.Inventory.Spawn(c, letter, 4u, 0, one, MergePolicy.NoMerge);
                peekCandidates += c1 - c0;
                takeCandidates += c2 - c1;
                orderOk &= peeked && took && p.Mail.Value == expected && u.Mail.Value == expected && s.Placed == 1;
                expected++;
            }
        };
        try
        {
            while (cycles < Cycles)
            {
                rig.Host.World.Tick();
            }
        }
        finally
        {
            rig.Hook.Action = null;
            rig.CaptureEvents = true;
        }

        _ = rig.Inventory.TryGetHeader(c, out ContainerHeader h);
        return new Result(peekCandidates, takeCandidates, cycles, orderOk, h.EntryCount);
    }

    private void AssertBound(in Result r, int n, string label)
    {
        Assert.Equal(Cycles, r.Cycles);
        Assert.True(r.OrderOk, "a peek or take returned something other than the oldest unit, or a spawn did not place");
        Assert.Equal(n, r.EntriesAfter);
        double perPeek = r.PeekCandidates / (double)r.Cycles;
        double perTake = r.TakeCandidates / (double)r.Cycles;
        double perCall = (r.PeekCandidates + r.TakeCandidates) / (2.0 * r.Cycles);
        _output.WriteLine(
            $"H-PERF-6a {label}: {perCall:F3} candidates/call (PeekOutfeed {perPeek:F3}, TakeUnit {perTake:F3}; {r.Cycles} cycles; bound {MaxCandidatesPerCall:F1})");
        Assert.True(perPeek <= MaxCandidatesPerCall, $"PeekOutfeed: {perPeek:F3} candidates per call at n = {n}");
        Assert.True(perTake <= MaxCandidatesPerCall, $"TakeUnit: {perTake:F3} candidates per call at n = {n}");
        Assert.True(perCall <= MaxCandidatesPerCall, $"Peek + Take: {perCall:F3} candidates per call at n = {n}");
        Assert.True(r.PeekCandidates >= r.Cycles, "every peek inspects at least the live head");
    }

    private readonly record struct Result(long PeekCandidates, long TakeCandidates, int Cycles, bool OrderOk, int EntriesAfter);
}
