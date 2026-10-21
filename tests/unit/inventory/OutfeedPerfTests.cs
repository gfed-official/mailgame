using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
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

    /// <summary>Which Intake stack a churn cycle merges onto, and from what.</summary>
    public enum ChurnTarget
    {
        /// <summary>A fresh detached letter onto the oldest non-full stack (its node is the head, so the next peek pops it).</summary>
        Oldest,

        /// <summary>A fresh detached letter onto a seeded random non-full stack (mid-queue tombstones).</summary>
        Random,

        /// <summary>One unit of a random Intake stack onto another (in-Intake merges; units stay level, tombstones pile up and compact).</summary>
        InIntake,
    }

    [Theory]
    [Trait("Id", "H-PERF-6a")]
    [InlineData(ChurnTarget.Oldest, 1, 2)]
    [InlineData(ChurnTarget.Random, 1, 2)]
    [InlineData(ChurnTarget.InIntake, 8, 1)]
    public void RepeatedMergesOntoIntakeStacks_InterleavedWithPeekAndTake_StayWithin4CandidatesPerCall_AndNeverExhaustTheNodePool(
        ChurnTarget target, int mergesPerCycle, int takesPerCycle)
    {
        // Arrange: 160 count-1 letters (ids 1-160, seq 1-160); a reference queue of the stacks in arrivalSeq order
        var rig = new InvAutoPlaceRig();
        IntakeSpawnOutfeedTests.FillIntake(rig, 160, 1);
        var queue = new List<(ushort Slot, List<uint> Ids)>();
        for (int i = 0; i < 160; i++)
        {
            queue.Add((rig.EntryAt(rig.Intake, i % 20, i / 20), new List<uint> { (uint)(i + 1) }));
        }

        // Act + Assert per cycle: the merges (each restamps its target, which moves to the queue's tail), then Peek + Take
        // takesPerCycle times and Spawn one; every served id must be the queue's oldest unit
        Churn r = RunMergeChurn(rig, queue, target, mergesPerCycle, takesPerCycle, cycles: 5_000, firstId: 1_000);

        // Assert: Peek/Take alone, and all candidates (merge-triggered compactions included) over all calls (merges included)
        double perPeekTake = (r.PeekCandidates + r.TakeCandidates) / (double)(r.Peeks + r.Takes);
        double perCall = (r.PeekCandidates + r.TakeCandidates + r.MergeCandidates) / (double)(r.Peeks + r.Takes + r.Merges);
        _output.WriteLine(
            $"H-PERF-6a merge churn ({target}, {mergesPerCycle} merge(s) + {takesPerCycle} peek/take per cycle): {perPeekTake:F3} candidates per " +
            $"Peek/Take call (PeekOutfeed {r.PeekCandidates / (double)r.Peeks:F3}, TakeUnit {r.TakeCandidates / (double)r.Takes:F3}); " +
            $"{perCall:F3} per call with merges (merge compactions {r.MergeCandidates} over {r.Merges} merges); " +
            $"max {r.MaxNodes} nodes, live {r.MinLive}-{r.MaxLive}, min {r.MinFree} free of {rig.Inventory.Mutator.Outfeed.Capacity}; bound {MaxCandidatesPerCall:F1}");
        Assert.True(r.OrderOk, r.FirstMismatch);
        Assert.True(r.Merges >= 5_000 * mergesPerCycle * 9 / 10, $"merges {r.Merges}");
        Assert.True(r.MinFree > 0, "the node pool ran out");
        Assert.True(r.BoundedOk, "a list held more than twice its live entries after a call");
        Assert.True(perPeekTake <= MaxCandidatesPerCall, $"Peek/Take: {perPeekTake:F3} candidates per call");
        Assert.True(perCall <= MaxCandidatesPerCall, $"with merges: {perCall:F3} candidates per call");
        if (target == ChurnTarget.InIntake)
        {
            Assert.True(r.MergeCandidates > 0, "restamp tombstones never triggered a compaction");
        }
    }

    [Fact]
    [Trait("Id", "H-PERF-6a")]
    public void OutfeedCandidates_CountOnePerFifoNodeExamined_ExactDeltasForPeekTakeCompactionAndRebuild()
    {
        // Arrange: six letters, ids 1-6 (seq 1-6), one node each
        var rig = new InvAutoPlaceRig();
        DefIndex letter = rig.KindIndex("letter");
        ContainerId intake = rig.Inventory.IntakeContainer;
        var fifo = rig.Inventory.Mutator.Outfeed;
        ushort[] e = new ushort[7];
        long mark = rig.Counters[CounterId.InventoryOutfeedCandidates];
        long Delta()
        {
            long now = rig.Counters[CounterId.InventoryOutfeedCandidates];
            long d = now - mark;
            mark = now;
            return d;
        }

        void Leave(ushort entry)
        {
            Assert.True(rig.Step(
                (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
                (ref InvPlan p) => rig.Inventory.Mutator.Remove(ref p, entry)).IsOk);
            rig.FreeDetached(entry);
        }

        bool Take(out UnitRef u)
        {
            UnitRef got = default;
            bool took = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.TakeUnit(intake, Selector.Fifo, rig.PlayerA, out got));
            u = got;
            return took;
        }

        SpawnResult spawned = IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.Spawn(intake, letter, 4u, 0, IntakeSpawnOutfeedTests.Mail(1, 2, 3, 4, 5, 6), MergePolicy.NoMerge));
        long spawnDelta = Delta();
        for (int x = 0; x < 6; x++)
        {
            e[x + 1] = rig.EntryAt(rig.Intake, x, 0);
        }

        // Act + Assert 1: a peek on a live head examines exactly that node
        Assert.Equal(6, spawned.Placed);
        Assert.Equal(0, spawnDelta); // appends examine no node; Spawn's cell probes are AutoPlace's counter, not this one
        Assert.True(rig.Inventory.PeekOutfeed(intake, Selector.Fifo, out UnitRef head));
        Assert.Equal(1u, head.Mail.Value);
        Assert.Equal(1, Delta());

        // Act + Assert 2: k = 2 stale head tombstones (2 tombstones, 4 live: no compaction, and leaving counts nothing); the peek is k + 1
        Leave(e[1]);
        Leave(e[2]);
        Assert.Equal(0, Delta());
        Assert.Equal(6, fifo.NodeCount(rig.Intake));
        Assert.True(rig.Inventory.PeekOutfeed(intake, Selector.Fifo, out head));
        Assert.Equal(3u, head.Mail.Value);
        Assert.Equal(3, Delta());
        Assert.Equal(4, fifo.NodeCount(rig.Intake));

        // Act + Assert 3: a take whose leave does not compact (1 tombstone, 3 live) = its peek of the live head = 1
        Assert.True(Take(out UnitRef u));
        Assert.Equal(3u, u.Mail.Value);
        Assert.Equal(1, Delta());
        Assert.Equal(4, fifo.NodeCount(rig.Intake));

        // Act + Assert 4: a compaction pass adds exactly the nodes it walks: id 5 leaves (2 tombstones, 2 live: none), then id 6
        // (3 tombstones > 1 live) compacts the 4-node list down to its one live node
        Leave(e[5]);
        Assert.Equal(0, Delta());
        Leave(e[6]);
        Assert.Equal(4, Delta());
        Assert.Equal(1, fifo.NodeCount(rig.Intake));

        // Act + Assert 5: a take that compacts = its peek (1) + the 1-node compaction its leave runs (1 tombstone > 0 live) = 2
        Assert.True(Take(out u));
        Assert.Equal(4u, u.Mail.Value);
        Assert.Equal(2, Delta());
        Assert.Equal(0, fifo.NodeCount(rig.Intake));

        // Act + Assert 6: a peek and a take on the empty outfeed examine nothing
        Assert.False(rig.Inventory.PeekOutfeed(intake, Selector.Fifo, out _));
        Assert.False(Take(out _));
        Assert.Equal(0, Delta());

        // Act + Assert 7: a rebuild counts each old node it walks (3: two live, one tombstone), not the entries it scans or the nodes it
        // appends; the rebuilt list serves the same order
        _ = IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.Spawn(intake, letter, 4u, 0, IntakeSpawnOutfeedTests.Mail(7, 8, 9), MergePolicy.NoMerge));
        Leave(rig.EntryAt(rig.Intake, 1, 0)); // id 8: 1 tombstone, 2 live
        Assert.Equal(0, Delta());
        Assert.Equal(3, fifo.NodeCount(rig.Intake));
        rig.Inventory.Mutator.RebuildOutfeed(rig.Intake);
        Assert.Equal(3, Delta());
        Assert.Equal(2, fifo.NodeCount(rig.Intake));
        Assert.True(rig.Inventory.PeekOutfeed(intake, Selector.Fifo, out head));
        Assert.Equal(7u, head.Mail.Value);
        Assert.Equal(1, Delta());
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

    /// <summary>
    /// <paramref name="cycles"/> cycles of: <paramref name="mergesPerCycle"/> merges onto Intake stacks chosen by
    /// <paramref name="target"/> (one tick through the fixture op), then, in one tick, Peek + Take <paramref name="takesPerCycle"/> times
    /// and Spawn one. <paramref name="queue"/> is the reference: the stacks in arrivalSeq order, each with its ascending ids.
    /// </summary>
    private static Churn RunMergeChurn(
        InvAutoPlaceRig rig, List<(ushort Slot, List<uint> Ids)> queue, ChurnTarget target, int mergesPerCycle, int takesPerCycle, int cycles, uint firstId)
    {
        DefIndex letter = rig.KindIndex("letter");
        var fifo = rig.Inventory.Mutator.Outfeed;
        int effMax = rig.Mail("letter", 4u).EffMax;
        var rng = new Pcg32(0x0012_C0DEUL, 7);
        uint nextId = firstId;
        var c = new Churn { OrderOk = true, MinFree = int.MaxValue, MinLive = int.MaxValue, BoundedOk = true, FirstMismatch = string.Empty };
        var merges = new List<(ushort Source, ushort Target)>();
        int Pick(int n) => (int)(rng.Next32() % (uint)n);
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            // plan the merges against the reference queue, updating it as the mutator will
            merges.Clear();
            for (int m = 0; m < mergesPerCycle; m++)
            {
                int source = -1;
                if (target == ChurnTarget.InIntake)
                {
                    var multi = Enumerable.Range(0, queue.Count).Where(q => queue[q].Ids.Count >= 2).ToList();
                    source = multi.Count > 0 ? multi[Pick(multi.Count)] : Pick(queue.Count);
                }

                var open = Enumerable.Range(0, queue.Count).Where(q => q != source && queue[q].Ids.Count < effMax).ToList();
                if (open.Count == 0)
                {
                    continue;
                }

                int at = target == ChurnTarget.Oldest ? open[0] : open[Pick(open.Count)];
                (ushort targetSlot, List<uint> ids) = queue[at];
                uint moved;
                ushort sourceSlot;
                if (source < 0)
                {
                    moved = nextId++;
                    sourceSlot = rig.DetachedMail("letter", 4u, new[] { moved });
                }
                else
                {
                    sourceSlot = queue[source].Slot;
                    moved = queue[source].Ids[0];
                    queue[source].Ids.RemoveAt(0);
                }

                int pos = ids.BinarySearch(moved);
                ids.Insert(pos < 0 ? ~pos : pos, moved);
                queue.RemoveAt(at);
                queue.Add((targetSlot, ids)); // the restamped stack leaves last
                if (source >= 0 && source != at && queue[source > at ? source - 1 : source].Ids.Count == 0)
                {
                    queue.RemoveAt(source > at ? source - 1 : source); // a drained source leaves the Intake
                }

                merges.Add((sourceSlot, targetSlot));
            }

            if (merges.Count > 0)
            {
                long m0 = rig.Counters[CounterId.InventoryOutfeedCandidates];
                Reply merged = rig.Step(
                    (ref InvPlan p) => p.Touch(rig.Intake) < 0 ? CoreReason.BadRequest : (ushort)0,
                    (ref InvPlan p) =>
                    {
                        foreach ((ushort source, ushort to) in merges)
                        {
                            rig.Inventory.Mutator.MergeInto(ref p, source, to, 1);
                        }
                    });
                Assert.True(merged.IsOk);
                c.MergeCandidates += rig.Counters[CounterId.InventoryOutfeedCandidates] - m0;
                c.Merges += merges.Count;
                Track(ref c);
            }

            // peek + take, spawn one
            uint spawnId = nextId++;
            var one = new[] { new MailId(spawnId) };
            uint[] peeked = new uint[takesPerCycle];
            uint[] took = new uint[takesPerCycle];
            IntakeSpawnOutfeedTests.InPhase(rig, () =>
            {
                for (int k = 0; k < takesPerCycle; k++)
                {
                    long c0 = rig.Counters[CounterId.InventoryOutfeedCandidates];
                    peeked[k] = rig.Inventory.PeekOutfeed(rig.Inventory.IntakeContainer, Selector.Fifo, out UnitRef p) ? p.Mail.Value : 0u;
                    long c1 = rig.Counters[CounterId.InventoryOutfeedCandidates];
                    took[k] = rig.Inventory.TakeUnit(rig.Inventory.IntakeContainer, Selector.Fifo, rig.PlayerA, out UnitRef u) ? u.Mail.Value : 0u;
                    c.PeekCandidates += c1 - c0;
                    c.TakeCandidates += rig.Counters[CounterId.InventoryOutfeedCandidates] - c1;
                }

                return rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, 4u, 0, one, MergePolicy.NoMerge);
            });
            c.Peeks += takesPerCycle;
            c.Takes += takesPerCycle;
            for (int k = 0; k < takesPerCycle; k++)
            {
                uint expected = queue[0].Ids[0];
                if (c.OrderOk && (peeked[k] != expected || took[k] != expected))
                {
                    c.OrderOk = false;
                    c.FirstMismatch = $"cycle {cycle}: peeked {peeked[k]}, took {took[k]}, expected {expected}";
                }

                queue[0].Ids.RemoveAt(0);
                if (queue[0].Ids.Count == 0)
                {
                    queue.RemoveAt(0);
                }
            }

            ushort spawned = Occupancy.Free;
            for (int s = 0; s < rig.Entries.Capacity && spawned == Occupancy.Free; s++)
            {
                EntryRecord e = rig.Entries[(ushort)s];
                if (e.IsLive && e.ContainerSlot == rig.Intake && e.Count == 1 && rig.IdsOf((ushort)s)[0] == spawnId)
                {
                    spawned = (ushort)s;
                }
            }

            Assert.NotEqual(Occupancy.Free, spawned);
            queue.Add((spawned, new List<uint> { spawnId }));
            Track(ref c);
        }

        return c;

        void Track(ref Churn t)
        {
            int nodes = fifo.NodeCount(rig.Intake);
            int live = rig.Header(rig.Intake).EntryCount;
            t.MaxNodes = Math.Max(t.MaxNodes, nodes);
            t.MaxLive = Math.Max(t.MaxLive, live);
            t.MinLive = Math.Min(t.MinLive, live);
            t.MinFree = Math.Min(t.MinFree, fifo.FreeCount);
            t.BoundedOk &= nodes <= 2 * live;
        }
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

    private record struct Churn(
        long PeekCandidates, long TakeCandidates, long MergeCandidates, int Peeks, int Takes, int Merges,
        int MaxNodes, int MaxLive, int MinLive, int MinFree, bool BoundedOk, bool OrderOk, string FirstMismatch);

    private readonly record struct Result(long PeekCandidates, long TakeCandidates, int Cycles, bool OrderOk, int EntriesAfter);
}
