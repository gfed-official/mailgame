using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/003 (TR-inventory-003, TR-inventory-005; GDD R6 test H-R6-1 in arena form; ADR-0009 sections 2 and 5): the
/// mail-ids size-class arena — oldest-first take, sorted-union merge, re-homing hysteresis, borrow-up, exhaustion and the multi-block
/// dry-run reservation. The arena reports exhaustion as <c>CanReserve == false</c> and, for an API caller, <c>false</c> plus one
/// <see cref="CheckCode.InventoryArenaExhausted"/> record; mapping that to reason <c>capacity_exhausted</c> (85) in the validator is
/// story 004 and is not tested here. Assert-vs-tolerant behaviour is chosen by <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// </summary>
public class MailIdArenaTests
{
    /// <summary>The smallest budget: 113 units per class (c0 113, c1 56, c2 28, c3 14, c4 7, c5..c8 4 blocks).</summary>
    private const int MinBudget = MailIdArena.MinTotalUnits;

    /// <summary>A fixed-seed xorshift32 (ADR-0006 rule 6: no <c>System.Random</c>); deterministic across runs.</summary>
    private struct XorShift32
    {
        private uint _state;

        public XorShift32(uint seed)
        {
            _state = seed == 0u ? 0x9E37_79B9u : seed;
        }

        public uint Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>A value in 0 to <paramref name="bound"/> - 1.</summary>
        public int Next(int bound) => (int)(Next() % (uint)bound);
    }

    /// <summary>Hands out consecutive ascending id batches, never repeating an id.</summary>
    private sealed class IdSource
    {
        private uint _next;

        public IdSource(uint first = 1000u)
        {
            _next = first;
        }

        public uint[] Next(int n)
        {
            var ids = new uint[n];
            for (int i = 0; i < n; i++)
            {
                ids[i] = _next++;
            }

            return ids;
        }
    }

    /// <summary>A deep copy of every private array of the arena plus its counters (for "nothing mutated" assertions).</summary>
    private sealed class ArenaState
    {
        private static readonly string[] _jaggedFields = { "_slabs", "_freeStacks", "_live" };
        private static readonly string[] _flatFields = { "_freeCount", "_peakLive" };

        private readonly Array[][] _jagged = new Array[_jaggedFields.Length][];
        private readonly Array[] _flat = new Array[_flatFields.Length];
        private readonly int _liveBlocks;
        private readonly int _liveUnits;

        public ArenaState(MailIdArena arena)
        {
            for (int f = 0; f < _jaggedFields.Length; f++)
            {
                var outer = (Array)Field(_jaggedFields[f]).GetValue(arena)!;
                _jagged[f] = new Array[outer.Length];
                for (int c = 0; c < outer.Length; c++)
                {
                    _jagged[f][c] = (Array)((Array)outer.GetValue(c)!).Clone();
                }
            }

            for (int f = 0; f < _flatFields.Length; f++)
            {
                _flat[f] = (Array)((Array)Field(_flatFields[f]).GetValue(arena)!).Clone();
            }

            _liveBlocks = arena.LiveBlocks;
            _liveUnits = arena.LiveUnits;
        }

        public void AssertEqual(ArenaState other)
        {
            Assert.Equal(_liveBlocks, other._liveBlocks);
            Assert.Equal(_liveUnits, other._liveUnits);
            for (int f = 0; f < _jaggedFields.Length; f++)
            {
                for (int c = 0; c < _jagged[f].Length; c++)
                {
                    Assert.True(SameElements(_jagged[f][c], other._jagged[f][c]), $"{_jaggedFields[f]}[{c}] changed");
                }
            }

            for (int f = 0; f < _flatFields.Length; f++)
            {
                Assert.True(SameElements(_flat[f], other._flat[f]), $"{_flatFields[f]} changed");
            }
        }

        private static FieldInfo Field(string name) =>
            typeof(MailIdArena).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"MailIdArena.{name} not found");

        private static bool SameElements(Array a, Array b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (!Equals(a.GetValue(i), b.GetValue(i)))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static SimCheckRing NewRing() => new SimCheckRing(capacity: 64, rateLimitTicks: 0);

    /// <summary>Records written so far (rate limiting is off in these tests; overwritten records still count).</summary>
    private static long Records(SimCheckRing ring) => ring.Count + (long)ring.Overwritten;

    private static int[] FreeVector(MailIdArena arena)
    {
        var free = new int[MailIdArena.ClassCount];
        for (int c = 0; c < free.Length; c++)
        {
            free[c] = arena.FreeBlocks(c);
        }

        return free;
    }

    private static MailIdsHandle Create(MailIdArena arena, uint[] ids)
    {
        Assert.True(arena.TryCreate(ids, 0, out MailIdsHandle h));
        return h;
    }

    /// <summary>Creates runs of exactly the class size until the class has <paramref name="leave"/> free blocks.</summary>
    private static void DrainTo(MailIdArena arena, IdSource ids, int cls, int leave, List<MailIdsHandle> held)
    {
        while (arena.FreeBlocks(cls) > leave)
        {
            MailIdsHandle h = Create(arena, ids.Next(MailIdArena.ClassSize(cls)));
            Assert.Equal(cls, h.ClassIndex);
            held.Add(h);
        }
    }

    private static uint[] Union(uint[] a, uint[] b)
    {
        var all = new uint[a.Length + b.Length];
        a.CopyTo(all, 0);
        b.CopyTo(all, a.Length);
        Array.Sort(all);
        return all;
    }

    private static void AssertStrictlyAscending(ReadOnlySpan<uint> ids)
    {
        for (int i = 1; i < ids.Length; i++)
        {
            Assert.True(ids[i] > ids[i - 1], $"ids[{i}] = {ids[i]} is not above {ids[i - 1]}");
        }
    }

    private static uint[] SlabRegion(MailIdArena arena, int cls, int block)
    {
        var slabs = (uint[][])typeof(MailIdArena).GetField("_slabs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(arena)!;
        return slabs[cls].AsSpan(block << cls, 1 << cls).ToArray();
    }

    /// <summary>The leak check counters cannot net out: no block is flagged live and each free stack holds every block once.</summary>
    private static void AssertNoBlockLiveAndFreeStacksArePermutations(MailIdArena arena)
    {
        const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var live = (byte[][])typeof(MailIdArena).GetField("_live", Flags)!.GetValue(arena)!;
        var stacks = (ushort[][])typeof(MailIdArena).GetField("_freeStacks", Flags)!.GetValue(arena)!;
        for (int c = 0; c < MailIdArena.ClassCount; c++)
        {
            Assert.All(live[c], b => Assert.Equal(0, b));
            var seen = new bool[arena.BlocksPerClass(c)];
            foreach (ushort block in stacks[c])
            {
                Assert.False(seen[block], $"class {c}: block {block} is on the free stack twice");
                seen[block] = true;
            }
        }
    }

    // ---------------------------------------------------------------- AC-1: H-R6-1 a/b (arena form)

    [Fact]
    [Trait("Id", "H-R6-1")]
    public void HR61a_TakeTwoOfA_ReturnsFourAndNine_AndALeaves15To30()
    {
        // Arrange: GDD H-R6-1 fixtures are independent, each starting from A = [4, 9, 15, 21, 30]
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        MailIdsHandle a = Create(arena, new uint[] { 4, 9, 15, 21, 30 });
        var taken = new uint[2];

        // Act
        int got = arena.Take(ref a, 2, taken, 1);

        // Assert
        Assert.Equal(2, got);
        Assert.Equal(new uint[] { 4, 9 }, taken);
        Assert.Equal(new uint[] { 15, 21, 30 }, arena.Read(in a).ToArray());
        AssertStrictlyAscending(taken);
        AssertStrictlyAscending(arena.Read(in a));
        Assert.Equal(3, arena.LiveUnits);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    [Trait("Id", "H-R6-1")]
    public void HR61b_UnionOfAWith2And50_IsTheSortedUnion_AndBothInputsAreFreed()
    {
        // Arrange
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        MailIdsHandle a = Create(arena, new uint[] { 4, 9, 15, 21, 30 });
        MailIdsHandle b = Create(arena, new uint[] { 2, 50 });

        // Act
        Assert.True(arena.TryUnion(in a, in b, 2, out MailIdsHandle u));

        // Assert
        Assert.Equal(new uint[] { 2, 4, 9, 15, 21, 30, 50 }, arena.Read(in u).ToArray());
        AssertStrictlyAscending(arena.Read(in u));
        Assert.Equal(1, arena.LiveBlocks);
        Assert.Equal(7, arena.LiveUnits);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    [Trait("Id", "H-R6-1")]
    public void HR61_TakeThenUnionChained_EveryResultStrictlyAscending()
    {
        // Arrange
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        MailIdsHandle a = Create(arena, new uint[] { 4, 9, 15, 21, 30 });
        MailIdsHandle b = Create(arena, new uint[] { 2, 50 });
        var taken = new uint[2];

        // Act
        arena.Take(ref a, 2, taken, 1);
        Assert.True(arena.TryUnion(in a, in b, 2, out MailIdsHandle u));

        // Assert
        Assert.Equal(new uint[] { 2, 15, 21, 30, 50 }, arena.Read(in u).ToArray());
        AssertStrictlyAscending(arena.Read(in u));
        Assert.Equal(0, ring.Count);
    }

    // ---------------------------------------------------------------- AC-2: take O(1), union one block, re-home hysteresis

    [Fact]
    [Trait("Id", "TR-inventory-005")]
    public void AC2_TakeK_AdvancesStartInPlace_NoCopyNoBlockChangeNoRecord()
    {
        // Arrange: a 10-id run in class 4 (16 units); 10 > 4, so no re-home is due
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        uint[] ids = new IdSource().Next(10);
        MailIdsHandle h = Create(arena, ids);
        MailIdsHandle before = h;
        uint[] slabBefore = SlabRegion(arena, h.ClassIndex, h.Block);
        int[] freeBefore = FreeVector(arena);
        var taken = new uint[3];

        // Act
        int got = arena.Take(ref h, 3, taken, 5);

        // Assert
        Assert.Equal(3, got);
        Assert.Equal(ids[..3], taken);
        Assert.Equal(before.ClassIndex, h.ClassIndex);
        Assert.Equal(before.Block, h.Block);
        Assert.Equal(before.Start + 3, h.Start);
        Assert.Equal(before.Count - 3, h.Count);
        Assert.Equal(slabBefore, SlabRegion(arena, h.ClassIndex, h.Block)); // no copy, no move
        Assert.Equal(ids[3..], arena.Read(in h).ToArray());
        Assert.Equal(freeBefore, FreeVector(arena));
        Assert.Equal(7, arena.LiveUnits);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void AC2_Union_AllocatesOneBlockOfResultClass_AndFreesBothInputs()
    {
        // Arrange: a = 3 ids (class 2), b = 2 ids (class 1); the 5-id result is class 3
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        MailIdsHandle a = Create(arena, new uint[] { 10, 30, 50 });
        MailIdsHandle b = Create(arena, new uint[] { 20, 40 });
        Assert.Equal(2, a.ClassIndex);
        Assert.Equal(1, b.ClassIndex);
        int[] freeBefore = FreeVector(arena);

        // Act
        Assert.True(arena.TryUnion(in a, in b, 1, out MailIdsHandle u));

        // Assert: result class -1, each input class +1, every other class unchanged
        Assert.Equal(MailIdArena.ClassFor(5), u.ClassIndex);
        Assert.Equal(0, u.Start);
        Assert.Equal(5, u.Count);
        int[] expected = (int[])freeBefore.Clone();
        expected[3]--;
        expected[2]++;
        expected[1]++;
        Assert.Equal(expected, FreeVector(arena));
        Assert.Equal(new uint[] { 10, 20, 30, 40, 50 }, arena.Read(in u).ToArray());
        Assert.Equal(1, arena.LiveBlocks);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void AC2_Append_ReplacesTheRunWithOneBlockOfResultClass_AndFreesTheOldBlock()
    {
        // Arrange: h = 3 ids (class 2); appending 6 gives 9 (class 4)
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        MailIdsHandle h = Create(arena, new uint[] { 1, 5, 9 });
        int[] freeBefore = FreeVector(arena);

        // Act
        Assert.True(arena.TryAppend(ref h, new uint[] { 2, 3, 4, 6, 7, 8 }, 1));

        // Assert
        Assert.Equal(4, h.ClassIndex);
        Assert.Equal(9, h.Count);
        int[] expected = (int[])freeBefore.Clone();
        expected[4]--;
        expected[2]++;
        Assert.Equal(expected, FreeVector(arena));
        Assert.Equal(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, arena.Read(in h).ToArray());
        Assert.Equal(1, arena.LiveBlocks);
        Assert.Equal(9, arena.LiveUnits);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void AC2_ReHome_TriggersOnlyWhenPreTakeCountIsAtMostAQuarterOfTheClass()
    {
        // Arrange: a class 4 (16-unit) run brought down to 5 by one take of 11 (16 > 4: no re-home)
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        uint[] ids = new IdSource().Next(16);
        MailIdsHandle h = Create(arena, ids);
        int block = h.Block;
        var taken = new uint[16];
        Assert.Equal(11, arena.Take(ref h, 11, taken, 1));
        Assert.Equal(4, h.ClassIndex);
        Assert.Equal(block, h.Block);
        Assert.Equal(5, h.Count);

        // Act 1: pre-take count 5 > 16 / 4: stays in its block
        Assert.Equal(1, arena.Take(ref h, 1, taken, 2));

        // Assert 1
        Assert.Equal(4, h.ClassIndex);
        Assert.Equal(block, h.Block);
        Assert.Equal(12, h.Start);
        Assert.Equal(4, h.Count);

        // Act 2: pre-take count 4 <= 16 / 4: re-homed to the class of the 3 remaining ids
        int free4 = arena.FreeBlocks(4);
        Assert.Equal(1, arena.Take(ref h, 1, taken, 3));

        // Assert 2
        Assert.Equal(MailIdArena.ClassFor(3), h.ClassIndex);
        Assert.Equal(0, h.Start);
        Assert.Equal(3, h.Count);
        Assert.Equal(ids[13..], arena.Read(in h).ToArray());
        Assert.Equal(free4 + 1, arena.FreeBlocks(4));
        Assert.Equal(arena.BlocksPerClass(4), arena.FreeBlocks(4));
        Assert.Equal(1, arena.LiveBlocks);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void AC2_ReHome_NeverWhilePreTakeCountIsAboveAQuarter_ThenOnceAtTheQuarter()
    {
        // Arrange: a full class 8 (256-unit) run
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        uint[] ids = new IdSource().Next(MailIdArena.MaxUnits);
        MailIdsHandle h = Create(arena, ids);
        int block = h.Block;
        int quarter = MailIdArena.MaxUnits / 4;
        var taken = new uint[1];

        // Act + Assert: 192 single takes with a pre-take count of 256 down to 65 never move the run
        while (h.Count > quarter)
        {
            Assert.Equal(1, arena.Take(ref h, 1, taken, 1));
            Assert.Equal(8, h.ClassIndex);
            Assert.Equal(block, h.Block);
        }

        Assert.Equal(quarter, h.Count);
        Assert.Equal(MailIdArena.MaxUnits - quarter, h.Start);

        // Act: the take at a pre-take count of exactly a quarter re-homes
        Assert.Equal(1, arena.Take(ref h, 1, taken, 2));

        // Assert
        Assert.Equal(MailIdArena.ClassFor(quarter - 1), h.ClassIndex);
        Assert.Equal(0, h.Start);
        Assert.Equal(ids[(MailIdArena.MaxUnits - quarter + 1)..], arena.Read(in h).ToArray());
        Assert.Equal(arena.BlocksPerClass(8), arena.FreeBlocks(8));
        Assert.Equal(0, ring.Count);
    }

    [Theory]
    [InlineData(66)]
    [InlineData(65)]
    [InlineData(64)]
    public void AC2_OscillationAroundTheQuarter_ReHomesAtMostOnce_AndSettles(int startCount)
    {
        // Arrange: a class 8 run brought to a count around its quarter (64) by one big take (pre-take 256: no re-home)
        const int Cycles = 100;
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        var source = new IdSource();
        uint[] ids = source.Next(MailIdArena.MaxUnits);
        MailIdsHandle h = Create(arena, ids);
        var taken = new uint[MailIdArena.MaxUnits];
        Assert.Equal(MailIdArena.MaxUnits - startCount, arena.Take(ref h, MailIdArena.MaxUnits - startCount, taken, 0));
        Assert.Equal(8, h.ClassIndex);

        // the 128-unit class is drained, so an append back to 65 or 66 borrows a 256-unit block and the run sits on the quarter line
        // of class 8 every cycle (otherwise the union would re-fit it into class 7 on cycle 0 and mask the take's re-home rule)
        DrainTo(arena, source, 7, 0, new List<MailIdsHandle>());
        var reference = new List<uint>(ids[(MailIdArena.MaxUnits - startCount)..]);
        int takeReHomes = 0;
        int settledClass = -1;
        int settledSince = 0;

        // Act: Take(1) / TryAppend(+1 fresh id), 100 cycles; a re-home is a take that changed the class or block
        for (int cycle = 0; cycle < Cycles; cycle++)
        {
            MailIdsHandle beforeTake = h;
            Assert.Equal(1, arena.Take(ref h, 1, taken, (uint)cycle));
            reference.RemoveAt(0);
            if (h.ClassIndex != beforeTake.ClassIndex || h.Block != beforeTake.Block)
            {
                takeReHomes++;
            }

            uint[] fresh = source.Next(1);
            Assert.True(arena.TryAppend(ref h, fresh, (uint)cycle));
            reference.Add(fresh[0]);
            Assert.Equal(reference.ToArray(), arena.Read(in h).ToArray());
            if (h.ClassIndex != settledClass)
            {
                settledClass = h.ClassIndex;
                settledSince = cycle;
            }
        }

        // Assert: exactly one re-home when the first take starts at the quarter (64), none above it; the class is stable after
        Assert.Equal(startCount <= MailIdArena.MaxUnits / 4 ? 1 : 0, takeReHomes);
        Assert.True(settledSince <= 1, $"class still changing at cycle {settledSince}");
        Assert.Equal(startCount, h.Count);
        Assert.Equal(startCount <= MailIdArena.MaxUnits / 4 ? 6 : 8, h.ClassIndex);
        Assert.Equal(1 + arena.BlocksPerClass(7), arena.LiveBlocks);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void AC2_DrainBySingleTakes_ReHomesOncePerQuarterCrossing_NeverThrashes()
    {
        // Arrange: a full class 8 run; a re-homed run lands above half of its new class (hysteresis)
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        uint[] ids = new IdSource().Next(MailIdArena.MaxUnits);
        MailIdsHandle h = Create(arena, ids);
        var taken = new uint[1];
        var reHomeAtPreTakeCount = new List<int>();

        // Act: 255 single takes; a re-home is a take that changed the class or block
        while (h.Count > 1)
        {
            MailIdsHandle before = h;
            Assert.Equal(1, arena.Take(ref h, 1, taken, 1));
            if (h.ClassIndex != before.ClassIndex || h.Block != before.Block)
            {
                reHomeAtPreTakeCount.Add(before.Count);
                Assert.True(h.Count > MailIdArena.ClassSize(h.ClassIndex) / 2, $"re-homed {h.Count} into class {h.ClassIndex}");
            }
        }

        // Assert: one re-home per crossing of a quarter (64 of 256, 16 of 64, 4 of 16), not one per take below it
        Assert.Equal(new[] { 64, 16, 4 }, reHomeAtPreTakeCount.ToArray());
        Assert.Equal(new[] { ids[MailIdArena.MaxUnits - 1] }, arena.Read(in h).ToArray());
        Assert.Equal(1, arena.LiveBlocks);
        Assert.Equal(0, ring.Count);
    }

    // ---------------------------------------------------------------- AC-3: borrow-up, then exhaustion with nothing mutated

    [Fact]
    public void AC3_FittingClassEmpty_CreateUnionAppendBorrowFromTheNextLargerClass()
    {
        // Arrange: inputs first, then class 2 (4 units) exhausted
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        var source = new IdSource();
        MailIdsHandle pairA = Create(arena, source.Next(2));
        MailIdsHandle pairB = Create(arena, source.Next(2));
        MailIdsHandle single = Create(arena, source.Next(1));
        var held = new List<MailIdsHandle>();
        DrainTo(arena, source, 2, 0, held);
        int free3 = arena.FreeBlocks(3);

        // Act
        Assert.True(arena.CanReserve(3));
        Assert.True(arena.TryCreate(source.Next(3), 1, out MailIdsHandle created));
        Assert.True(arena.TryUnion(in pairA, in pairB, 1, out MailIdsHandle union));
        Assert.True(arena.TryAppend(ref single, source.Next(2), 1));

        // Assert: each landed in class 3, the next larger class
        Assert.Equal(3, created.ClassIndex);
        Assert.Equal(3, union.ClassIndex);
        Assert.Equal(3, single.ClassIndex);
        Assert.Equal(free3 - 3, arena.FreeBlocks(3));
        Assert.Equal(0, arena.FreeBlocks(2));
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void AC3_EveryLargerClassExhausted_ApiReturnsFalseWithOneRecord_AndNothingIsMutated()
    {
        // Arrange: inputs in classes 0 and 1, then classes 2..8 drained to 0 free blocks
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        var source = new IdSource();
        MailIdsHandle one = Create(arena, source.Next(1));
        MailIdsHandle two = Create(arena, source.Next(2));
        MailIdsHandle appendTarget = Create(arena, source.Next(2));
        var held = new List<MailIdsHandle> { one, two, appendTarget };
        for (int c = 2; c < MailIdArena.ClassCount; c++)
        {
            DrainTo(arena, source, c, 0, held);
        }

        Assert.True(arena.FreeBlocks(0) > 0 && arena.FreeBlocks(1) > 0);
        var before = new ArenaState(arena);
        uint[][] contents = held.Select(h => arena.Read(in h).ToArray()).ToArray();

        // Act + Assert: the dry runs are false and record nothing (validation would map this to reason 85: story 004)
        Assert.False(arena.CanReserve(3));
        Assert.False(arena.CanReserve(new[] { 3 }));
        Assert.True(arena.CanReserve(2));
        Assert.Equal(0, ring.Count);
        new ArenaState(arena).AssertEqual(before);

        // Act + Assert: TryCreate
        Assert.False(arena.TryCreate(source.Next(3), 7, out MailIdsHandle created));
        Assert.True(created.IsNone);
        Assert.Equal(1, ring.Count);
        Assert.Equal(CheckCode.InventoryArenaExhausted, ring[0].Check);
        Assert.Equal(7u, ring[0].Tick);
        Assert.Equal(3L, ring[0].A);
        Assert.Equal(2L, ring[0].B);
        new ArenaState(arena).AssertEqual(before);

        // Act + Assert: TryUnion (1 + 2 = 3 units)
        Assert.False(arena.TryUnion(in one, in two, 8, out MailIdsHandle union));
        Assert.True(union.IsNone);
        Assert.Equal(2, ring.Count);
        Assert.Equal(CheckCode.InventoryArenaExhausted, ring[1].Check);
        new ArenaState(arena).AssertEqual(before);

        // Act + Assert: TryAppend (2 + 1 = 3 units); the handle passed by ref is untouched
        MailIdsHandle targetBefore = appendTarget;
        Assert.False(arena.TryAppend(ref appendTarget, source.Next(1), 9));
        Assert.Equal(targetBefore, appendTarget);
        Assert.Equal(3, ring.Count);
        Assert.Equal(CheckCode.InventoryArenaExhausted, ring[2].Check);
        new ArenaState(arena).AssertEqual(before);

        // Act + Assert: a create over MaxUnits is refused the same way
        Assert.False(arena.TryCreate(source.Next(MailIdArena.MaxUnits + 1), 10, out _));
        Assert.Equal(4, ring.Count);
        Assert.Equal(CheckCode.InventoryArenaExhausted, ring[3].Check);
        new ArenaState(arena).AssertEqual(before);

        // Assert: every live run still reads the same
        for (int i = 0; i < held.Count; i++)
        {
            MailIdsHandle h = held[i];
            Assert.Equal(contents[i], arena.Read(in h).ToArray());
        }
    }

    // ---------------------------------------------------------------- AC (a): multi-block reservation

    /// <summary>
    /// Builds the ACa state: inputs for three acquisitions with results in classes 2, 3 and 3 (4, 6 and 7 units), then classes 5..8
    /// drained to 0, class 4 to <paramref name="free4"/>, class 3 to 1 free block.
    /// </summary>
    private static MailIdArena BuildMultiUnionState(
        SimCheckRing ring, int free4, out MailIdsHandle[] inputs, out uint[] appendIds, out List<MailIdsHandle> held)
    {
        var arena = new MailIdArena(MinBudget, ring);
        var source = new IdSource();
        inputs = new[]
        {
            Create(arena, new uint[] { 1, 3 }),         // union 1: 2 + 2 = 4 (class 2)
            Create(arena, new uint[] { 2, 4 }),
            Create(arena, new uint[] { 10, 12, 14 }),   // union 2: 3 + 3 = 6 (class 3)
            Create(arena, new uint[] { 11, 13, 15 }),
            Create(arena, new uint[] { 20, 22, 24, 26 }), // append: 4 + 3 = 7 (class 3)
        };
        appendIds = new uint[] { 21, 23, 25 };
        held = new List<MailIdsHandle>();
        for (int c = 5; c < MailIdArena.ClassCount; c++)
        {
            DrainTo(arena, source, c, 0, held);
        }

        DrainTo(arena, source, 4, free4, held);
        DrainTo(arena, source, 3, 1, held);
        Assert.True(arena.FreeBlocks(2) >= 1);
        return arena;
    }

    [Fact]
    public void ACa_ThreeUnionsAcrossClassesWithOneBorrow_ReservationPasses_AndEveryCommitSucceeds()
    {
        // Arrange: class 3 has 1 free, class 4 has 1 free, nothing larger; results need classes 2, 3, 3 (one must borrow class 4)
        var ring = NewRing();
        MailIdArena arena = BuildMultiUnionState(ring, free4: 1, out MailIdsHandle[] inputs, out uint[] appendIds, out _);
        MailIdsHandle appendTarget = inputs[4];

        // Act
        bool reserved = arena.CanReserve(new[] { 4, 6, 7 });
        bool u1 = arena.TryUnion(in inputs[0], in inputs[1], 1, out MailIdsHandle r1);
        bool u2 = arena.TryUnion(in inputs[2], in inputs[3], 1, out MailIdsHandle r2);
        bool u3 = arena.TryAppend(ref appendTarget, appendIds, 1);

        // Assert
        Assert.True(reserved);
        Assert.True(u1 && u2 && u3);
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, arena.Read(in r1).ToArray());
        Assert.Equal(new uint[] { 10, 11, 12, 13, 14, 15 }, arena.Read(in r2).ToArray());
        Assert.Equal(new uint[] { 20, 21, 22, 23, 24, 25, 26 }, arena.Read(in appendTarget).ToArray());
        Assert.Equal(2, r1.ClassIndex);
        Assert.Equal(3, r2.ClassIndex);
        Assert.Equal(4, appendTarget.ClassIndex); // borrowed
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void ACa_OneBlockShort_ReservationFails_AndNothingIsMutated()
    {
        // Arrange: the same state with class 4's last free block consumed
        var ring = NewRing();
        MailIdArena arena = BuildMultiUnionState(ring, free4: 0, out _, out _, out _);
        var before = new ArenaState(arena);

        // Act
        bool reserved = arena.CanReserve(new[] { 4, 6, 7 });

        // Assert
        Assert.False(reserved);
        Assert.Equal(0, ring.Count);
        new ArenaState(arena).AssertEqual(before);
    }

    [Fact]
    public void ACa_SingleBlockChecksEachPass_ButTheCombinedReservationFails_AsTheCommitWould()
    {
        // Arrange: one block short (the defect the multi-block check closes: per-request checks all pass)
        var ring = NewRing();
        MailIdArena arena = BuildMultiUnionState(ring, free4: 0, out MailIdsHandle[] inputs, out uint[] appendIds, out _);
        MailIdsHandle appendTarget = inputs[4];

        // Act
        bool single4 = arena.CanReserve(4);
        bool single6 = arena.CanReserve(6);
        bool single7 = arena.CanReserve(7);
        bool combined = arena.CanReserve(new[] { 4, 6, 7 });
        bool u1 = arena.TryUnion(in inputs[0], in inputs[1], 1, out _);
        bool u2 = arena.TryUnion(in inputs[2], in inputs[3], 1, out _);
        bool u3 = arena.TryAppend(ref appendTarget, appendIds, 1);

        // Assert: the third acquisition of the commit is the one that would have failed
        Assert.True(single4 && single6 && single7);
        Assert.False(combined);
        Assert.True(u1 && u2);
        Assert.False(u3);
        Assert.Equal(1, ring.Count);
        Assert.Equal(CheckCode.InventoryArenaExhausted, ring[0].Check);
    }

    [Fact]
    public void ACa_RunOverMaxUnitsFails_NonPositiveEntriesAreIgnored()
    {
        // Arrange: a fully exhausted arena (every class drained)
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        var fresh = new MailIdArena(MinBudget, ring);
        var source = new IdSource();
        var held = new List<MailIdsHandle>();
        for (int c = 0; c < MailIdArena.ClassCount; c++)
        {
            DrainTo(arena, source, c, 0, held);
        }

        // Act + Assert
        Assert.True(arena.CanReserve(ReadOnlySpan<int>.Empty));
        Assert.True(arena.CanReserve(new[] { 0, -1, int.MinValue }));
        Assert.False(arena.CanReserve(new[] { 0, 1 }));
        Assert.False(fresh.CanReserve(new[] { 4, MailIdArena.MaxUnits + 1 }));
        Assert.True(fresh.CanReserve(new[] { 0, MailIdArena.MaxUnits, -3 }));
        Assert.Equal(fresh.CanReserve(3), fresh.CanReserve(new[] { 0, -1, 3 }));
        Assert.Equal(0, ring.Count);
    }

    // ---------------------------------------------------------------- AC (b): best-effort re-homing

    /// <summary>A class 4 run at 4 ids (start 12) with classes 1..3 drained to <paramref name="free3"/> (class 3) and 0 (1, 2).</summary>
    private static MailIdArena BuildQuarterRun(SimCheckRing ring, int free3, out MailIdsHandle h, out uint[] ids)
    {
        var arena = new MailIdArena(MinBudget, ring);
        var source = new IdSource();
        ids = source.Next(16);
        h = Create(arena, ids);
        Assert.Equal(12, arena.Take(ref h, 12, new uint[12], 0));
        var held = new List<MailIdsHandle>();
        DrainTo(arena, source, 1, 0, held);
        DrainTo(arena, source, 2, 0, held);
        DrainTo(arena, source, 3, free3, held);
        return arena;
    }

    [Fact]
    public void ACb_NoSmallerBlockFree_TakeKeepsTheBlock_AndSucceeds()
    {
        // Arrange: pre-take count 4 <= 16 / 4, but classes [ClassFor(3), 4) = {2, 3} have no free block
        var ring = NewRing();
        MailIdArena arena = BuildQuarterRun(ring, 0, out MailIdsHandle h, out uint[] ids);
        MailIdsHandle before = h;
        var taken = new uint[1];

        // Act
        int got = arena.Take(ref h, 1, taken, 3);

        // Assert
        Assert.Equal(1, got);
        Assert.Equal(ids[12], taken[0]);
        Assert.Equal(before.ClassIndex, h.ClassIndex);
        Assert.Equal(before.Block, h.Block);
        Assert.Equal(before.Start + 1, h.Start);
        Assert.Equal(3, h.Count);
        Assert.Equal(ids[13..], arena.Read(in h).ToArray());
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void ACb_Control_SmallerBlockFreeInRange_TakeReHomes()
    {
        // Arrange: the same, with one class 3 block left free (inside [ClassFor(3), 4))
        var ring = NewRing();
        MailIdArena arena = BuildQuarterRun(ring, 1, out MailIdsHandle h, out uint[] ids);
        var taken = new uint[1];

        // Act
        int got = arena.Take(ref h, 1, taken, 3);

        // Assert: re-homed into the class 3 block, the class 4 block freed
        Assert.Equal(1, got);
        Assert.Equal(3, h.ClassIndex);
        Assert.Equal(0, h.Start);
        Assert.Equal(ids[13..], arena.Read(in h).ToArray());
        Assert.Equal(0, arena.FreeBlocks(3));
        Assert.Equal(arena.BlocksPerClass(4), arena.FreeBlocks(4));
        Assert.Equal(0, ring.Count);
    }

    // ---------------------------------------------------------------- AC-4: seeded property test

    [Theory]
    [InlineData(0x0000_C0DEu)]
    [InlineData(0x1234_5678u)]
    [InlineData(0xDEAD_BEEFu)]
    public void AC4_SeededRandomTakeUnionAppendFree_MatchesReferenceSortedLists_AndLeaksNothing(uint seed)
    {
        // Arrange: the minimum budget so large runs exhaust classes 5..8 (4 blocks each) some of the time; the loop runs until
        // 10 000 take/union/append ops reached the arena (creates, batches and frees come on top)
        const int Ops = 10_000;
        const int StepCap = 40_000;
        const int Slots = 16;
        const int MaxRun = 255;
        var stopwatch = Stopwatch.StartNew();
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        var rng = new XorShift32(seed);
        var used = new HashSet<uint>();
        var handles = new MailIdsHandle[Slots];
        var reference = new List<uint>[Slots];
        for (int s = 0; s < Slots; s++)
        {
            reference[s] = new List<uint>();
        }

        var taken = new uint[MailIdArena.MaxUnits];
        int refused = 0;
        int overflowAttempts = 0;
        int acquisitions = 0;
        int ops = 0;
        int peakRun = 0;
        int takeReHomes = 0;
        int batches = 0;
        int batchRefusals = 0;

        uint[] FreshIds(int n)
        {
            var ids = new uint[n];
            for (int i = 0; i < n; i++)
            {
                uint id;
                do
                {
                    id = rng.Next();
                }
                while (id == 0u || !used.Add(id));

                ids[i] = id;
            }

            Array.Sort(ids);
            return ids;
        }

        int RandomRun(int max) => 1 + rng.Next(1 + rng.Next(max)); // biased towards short runs

        void AssertSlot(int s)
        {
            MailIdsHandle h = handles[s];
            Assert.Equal(reference[s].Count, h.Count);
            Assert.True(arena.Read(in h).SequenceEqual(CollectionsMarshal.AsSpan(reference[s])), $"seed {seed:X8}: slot {s} differs");
        }

        void AssertAll()
        {
            int units = 0;
            int blocks = 0;
            for (int s = 0; s < Slots; s++)
            {
                AssertSlot(s);
                units += reference[s].Count;
                blocks += handles[s].IsNone ? 0 : 1;
            }

            Assert.Equal(units, arena.LiveUnits);
            Assert.Equal(blocks, arena.LiveBlocks);
        }

        // An acquiring op: the dry run must agree with the outcome; a refusal records once and mutates nothing
        void Acquire(bool predicted, Func<bool> op, uint tick)
        {
            var before = predicted ? null : new ArenaState(arena);
            MailIdsHandle[] handlesBefore = predicted ? Array.Empty<MailIdsHandle>() : (MailIdsHandle[])handles.Clone();
            long recordsBefore = Records(ring);
            bool ok = op();
            Assert.True(ok == predicted, $"seed {seed:X8} tick {tick}: CanReserve said {predicted}, the op returned {ok}");
            acquisitions++;
            if (ok)
            {
                Assert.Equal(recordsBefore, Records(ring));
                return;
            }

            refused++;
            Assert.Equal(recordsBefore + 1, Records(ring));
            new ArenaState(arena).AssertEqual(before!);
            Assert.Equal(handlesBefore, handles);
        }

        // A multi-block op (ACa): 2-3 creates into empty slots; the dry run must be exact: all succeed when it passes (with a take,
        // which may re-home, between the first two), and one fails when it does not (then the created runs are released)
        void Batch(uint tick)
        {
            int[] slots = new int[3];
            int n = 0;
            for (int i = rng.Next(Slots), seen = 0; seen < Slots && n < 2 + rng.Next(2); i = (i + 1) % Slots, seen++)
            {
                if (handles[i].IsNone)
                {
                    slots[n++] = i;
                }
            }

            if (n < 2)
            {
                return;
            }

            var sizes = new int[n];
            var batchIds = new uint[n][];
            for (int i = 0; i < n; i++)
            {
                batchIds[i] = FreshIds(RandomRun(MaxRun));
                sizes[i] = batchIds[i].Length;
            }

            bool predicted = arena.CanReserve(sizes);
            batches++;
            bool all = true;
            for (int i = 0; i < n; i++)
            {
                if (i == 1 && predicted)
                {
                    TakeOne(tick); // a take never lowers the free count at or above any class, so it cannot break a passed reservation
                }

                if (arena.TryCreate(batchIds[i], tick, out handles[slots[i]]))
                {
                    reference[slots[i]].AddRange(batchIds[i]);
                }
                else
                {
                    all = false;
                }
            }

            Assert.True(all == predicted, $"seed {seed:X8} tick {tick}: batch CanReserve said {predicted}, the commit gave {all}");
            if (!predicted)
            {
                batchRefusals++;
                for (int i = 0; i < n; i++)
                {
                    arena.Free(in handles[slots[i]]);
                    handles[slots[i]] = default;
                    reference[slots[i]].Clear();
                }
            }
        }

        // Takes one id from the first live slot at or after a random one (no-op when every slot is empty)
        void TakeOne(uint tick)
        {
            for (int i = rng.Next(Slots), seen = 0; seen < Slots; i = (i + 1) % Slots, seen++)
            {
                if (!handles[i].IsNone)
                {
                    Assert.Equal(1, arena.Take(ref handles[i], 1, taken, tick));
                    reference[i].RemoveAt(0);
                    return;
                }
            }
        }

        // Act + Assert
        int step = 0;
        for (; ops < Ops && step < StepCap; step++)
        {
            uint tick = (uint)step;
            int s = rng.Next(Slots);
            int roll = rng.Next(100);
            int touchedB = -1;
            if (handles[s].IsNone)
            {
                // create (the only op an empty slot takes)
                uint[] ids = FreshIds(RandomRun(MaxRun));
                Acquire(arena.CanReserve(ids.Length), () =>
                {
                    bool ok = arena.TryCreate(ids, tick, out handles[s]);
                    if (ok)
                    {
                        reference[s].AddRange(ids);
                    }

                    return ok;
                }, tick);
            }
            else if (roll < 35)
            {
                // take k oldest
                int k = 1 + rng.Next(handles[s].Count);
                MailIdsHandle beforeTake = handles[s];
                Assert.Equal(k, arena.Take(ref handles[s], k, taken, tick));
                ops++;
                if (!handles[s].IsNone && (handles[s].ClassIndex != beforeTake.ClassIndex || handles[s].Block != beforeTake.Block))
                {
                    takeReHomes++;
                }

                Assert.True(taken.AsSpan(0, k).SequenceEqual(CollectionsMarshal.AsSpan(reference[s])[..k]));
                reference[s].RemoveRange(0, k);
                Assert.Equal(reference[s].Count == 0, handles[s].IsNone);
            }
            else if (roll < 55)
            {
                // union with another live slot; the result goes to s, the other slot empties
                int b = rng.Next(Slots);
                if (b == s || handles[b].IsNone)
                {
                    continue;
                }

                int total = handles[s].Count + handles[b].Count;
                bool overflow = total > MailIdArena.MaxUnits;
                if (total > MaxRun && !overflow)
                {
                    continue;
                }

                overflowAttempts += overflow ? 1 : 0;
                touchedB = b;
                ops++;
                Acquire(arena.CanReserve(new[] { total }), () =>
                {
                    bool ok = arena.TryUnion(in handles[s], in handles[b], tick, out MailIdsHandle merged);
                    if (ok)
                    {
                        handles[s] = merged;
                        handles[b] = default;
                        reference[s].AddRange(reference[b]);
                        reference[s].Sort();
                        reference[b].Clear();
                    }

                    return ok;
                }, tick);
            }
            else if (roll < 85)
            {
                // append fresh ids (now and then one that would overflow 256)
                int count = handles[s].Count;
                int n;
                if (roll < 58)
                {
                    n = MailIdArena.MaxUnits + 1 - count + rng.Next(8);
                    overflowAttempts++;
                }
                else
                {
                    if (count >= MaxRun)
                    {
                        continue;
                    }

                    n = RandomRun(MaxRun - count);
                }

                uint[] ids = FreshIds(n);
                ops++;
                Acquire(arena.CanReserve(count + n), () =>
                {
                    MailIdsHandle h = handles[s];
                    bool ok = arena.TryAppend(ref h, ids, tick);
                    handles[s] = h;
                    if (ok)
                    {
                        reference[s].AddRange(ids);
                        reference[s].Sort();
                    }

                    return ok;
                }, tick);
            }
            else if (roll < 92)
            {
                Batch(tick);
                AssertAll();
            }
            else
            {
                // free
                arena.Free(in handles[s]);
                handles[s] = default;
                reference[s].Clear();
            }

            AssertSlot(s);
            if (touchedB >= 0)
            {
                AssertSlot(touchedB);
            }

            int liveUnits = 0;
            int liveBlocks = 0;
            for (int i = 0; i < Slots; i++)
            {
                liveUnits += reference[i].Count;
                liveBlocks += handles[i].IsNone ? 0 : 1;
                peakRun = Math.Max(peakRun, reference[i].Count);
            }

            Assert.Equal(liveUnits, arena.LiveUnits);
            Assert.Equal(liveBlocks, arena.LiveBlocks);
            if (step % 100 == 99)
            {
                AssertAll();
            }
        }

        AssertAll();

        // Act: release everything
        for (int s = 0; s < Slots; s++)
        {
            arena.Free(in handles[s]);
            handles[s] = default;
        }

        // Assert: no leak; exhaustion happened sometimes but not always
        for (int c = 0; c < MailIdArena.ClassCount; c++)
        {
            Assert.Equal(arena.BlocksPerClass(c), arena.FreeBlocks(c));
        }

        Assert.Equal(0, arena.LiveBlocks);
        Assert.Equal(0, arena.LiveUnits);
        AssertNoBlockLiveAndFreeStacksArePermutations(arena);

        // Assert: the run really covered what AC-4 names (counters guard against a later change making it trivial)
        Assert.True(ops >= Ops, $"seed {seed:X8}: only {ops} take/union/append ops in {step} steps");
        Assert.True(peakRun >= 200, $"seed {seed:X8}: the longest run was {peakRun}");
        Assert.True(takeReHomes > 0, $"seed {seed:X8}: no take re-homed");
        Assert.True(batches > 0 && batchRefusals > 0 && batchRefusals < batches, $"seed {seed:X8}: {batchRefusals} of {batches} batches refused");
        int exhausted = refused - overflowAttempts;
        Assert.True(exhausted > 0, $"seed {seed:X8}: no capacity refusal in {acquisitions} acquisitions");
        Assert.True(refused < acquisitions / 2, $"seed {seed:X8}: {refused} of {acquisitions} acquisitions refused");
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"AC4 seed=0x{seed:X8}: {ops} ops in {step} steps, {acquisitions} acquisitions, {exhausted} capacity refusals, {overflowAttempts} over-256 refusals, "
            + $"{batchRefusals}/{batches} batches refused, {takeReHomes} take re-homes, peak run {peakRun}, {stopwatch.ElapsedMilliseconds} ms");
    }

    // ---------------------------------------------------------------- assert-vs-Release

    [Fact]
    public void Take_KOverCountOrNoneHandle_AssertsUnderInventoryAssert_OrClampsInRelease()
    {
        // Arrange
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        MailIdsHandle h = Create(arena, new uint[] { 5, 6, 7 });
        MailIdsHandle none = default;
        var taken = new uint[8];

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException overCount = Assert.Throws<SimAssertException>(() => arena.Take(ref h, 4, taken, 0));
            Assert.Equal(SimAssertCode.InventoryArenaHandleInvalid, overCount.Code);
            SimAssertException onNone = Assert.Throws<SimAssertException>(() => arena.Take(ref none, 1, taken, 0));
            Assert.Equal(SimAssertCode.InventoryArenaHandleInvalid, onNone.Code);
            Assert.Equal(new uint[] { 5, 6, 7 }, arena.Read(in h).ToArray());
            Assert.Equal(3, arena.LiveUnits);
        }
        else
        {
            Assert.Equal(0, arena.Take(ref none, 1, taken, 0));
            Assert.Equal(3, arena.Take(ref h, 4, taken, 0)); // clamped to the count: the whole run, block freed
            Assert.True(h.IsNone);
            Assert.Equal(0, arena.LiveBlocks);
            Assert.Equal(0, arena.LiveUnits);
        }

        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void TryUnion_SameHandleTwice_AssertsUnderInventoryAssert_OrReturnsFalseWithoutRecordInRelease()
    {
        // Arrange
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        MailIdsHandle a = Create(arena, new uint[] { 1, 2 });
        var before = new ArenaState(arena);

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => arena.TryUnion(in a, in a, 0, out _));
            Assert.Equal(SimAssertCode.InventoryArenaHandleInvalid, thrown.Code);
        }
        else
        {
            Assert.False(arena.TryUnion(in a, in a, 0, out MailIdsHandle u));
            Assert.True(u.IsNone);
        }

        Assert.Equal(0, ring.Count);
        new ArenaState(arena).AssertEqual(before);
    }

    [Fact]
    public void NonAscendingOrOverlappingIds_AssertUnderInventoryAssert_OrAreStoredAsGivenInRelease()
    {
        // Arrange
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        MailIdsHandle a = Create(arena, new uint[] { 4, 9 });
        MailIdsHandle b = Create(arena, new uint[] { 9, 12 });

        // Act + Assert: unsorted create, duplicate append, overlapping union
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => arena.TryCreate(new uint[] { 5, 3 }, 1, out _));
            Assert.Equal(SimAssertCode.InventoryMailIdsNotAscending, thrown.Code);
            thrown = Assert.Throws<SimAssertException>(() => arena.TryAppend(ref a, new uint[] { 7, 7 }, 1));
            Assert.Equal(SimAssertCode.InventoryMailIdsNotAscending, thrown.Code);
            thrown = Assert.Throws<SimAssertException>(() => arena.TryUnion(in a, in b, 1, out _));
            Assert.Equal(SimAssertCode.InventoryMailIdsNotAscending, thrown.Code);
        }
        else
        {
            Assert.True(arena.TryCreate(new uint[] { 5, 3 }, 1, out MailIdsHandle unsorted));
            Assert.Equal(new uint[] { 5, 3 }, arena.Read(in unsorted).ToArray());
            Assert.True(arena.TryUnion(in a, in b, 1, out MailIdsHandle u));
            Assert.Equal(new uint[] { 4, 9, 9, 12 }, arena.Read(in u).ToArray()); // the duplicate is kept, adjacent
        }

        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void ACa_ReservationIgnoresBlocksTheCommitFrees_ConservativeByDesign()
    {
        // Arrange: a and b are 4-unit runs (class 2); class 2 is then drained, class 3 keeps one free block, classes 4 to 8 none
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);
        var source = new IdSource();
        var held = new List<MailIdsHandle>();
        MailIdsHandle a = Create(arena, source.Next(4));
        MailIdsHandle b = Create(arena, source.Next(4));
        DrainTo(arena, source, 2, 0, held);
        DrainTo(arena, source, 3, 1, held);
        for (int c = 4; c < MailIdArena.ClassCount; c++)
        {
            DrainTo(arena, source, c, 0, held);
        }

        // Act: the op is union(a, b) -> 8 units, then a create of 3 units
        bool reserved = arena.CanReserve(new[] { 8, 3 });
        bool union = arena.TryUnion(in a, in b, 1, out _);
        bool create = arena.TryCreate(source.Next(3), 1, out _);

        // Assert: the commit would have fitted (the union freed two class 2 blocks), but the dry run refuses it; a false negative
        // is the documented price of never accepting what a commit cannot do
        Assert.False(reserved);
        Assert.True(union);
        Assert.True(create);
    }

    [Fact]
    public void EdgePaths_EmptySpansNoneInputsAndPeak_BehaveAsDocumented()
    {
        // Arrange
        var ring = NewRing();
        var arena = new MailIdArena(MinBudget, ring);

        // Act + Assert: an empty create yields none and false without a record
        Assert.False(arena.TryCreate(ReadOnlySpan<uint>.Empty, 1, out MailIdsHandle none));
        Assert.True(none.IsNone);
        Assert.Equal(0, arena.LiveBlocks);

        // an append of nothing is a no-op that returns true; an append to none is a create
        MailIdsHandle h = default;
        Assert.True(arena.TryAppend(ref h, ReadOnlySpan<uint>.Empty, 1));
        Assert.True(h.IsNone);
        Assert.True(arena.TryAppend(ref h, new uint[] { 3, 7 }, 1));
        Assert.Equal(new uint[] { 3, 7 }, arena.Read(in h).ToArray());
        Assert.Equal(1, arena.PeakLiveBlocks(1));

        // a union with a none input copies the other run into a new block and frees it
        int free1 = arena.FreeBlocks(1);
        Assert.True(arena.TryUnion(in h, in none, 2, out MailIdsHandle copy));
        Assert.Equal(new uint[] { 3, 7 }, arena.Read(in copy).ToArray());
        Assert.Equal(free1, arena.FreeBlocks(1));
        Assert.Equal(2, arena.PeakLiveBlocks(1)); // the peak counts the moment both blocks were live, and never falls

        // an over-256 union records the total against ClassCount and mutates nothing
        MailIdsHandle big = Create(arena, new IdSource().Next(MailIdArena.MaxUnits));
        var before = new ArenaState(arena);
        Assert.False(arena.TryUnion(in big, in copy, 3, out MailIdsHandle over));
        Assert.True(over.IsNone);
        Assert.Equal(1, ring.Count);
        Assert.Equal(CheckCode.InventoryArenaExhausted, ring[0].Check);
        Assert.Equal(3u, ring[0].Tick);
        Assert.Equal((long)MailIdArena.MaxUnits + 2, ring[0].A);
        Assert.Equal((long)MailIdArena.ClassCount, ring[0].B);
        new ArenaState(arena).AssertEqual(before);

        // a double free asserts under INVENTORY_ASSERT and is ignored in Release
        arena.Free(in copy);
        arena.Free(in big);
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => arena.Free(in copy));
            Assert.Equal(SimAssertCode.InventoryArenaHandleInvalid, thrown.Code);
        }
        else
        {
            arena.Free(in copy);
        }

        arena.Free(in none);
        Assert.Equal(0, arena.LiveBlocks);
        Assert.Equal(0, arena.LiveUnits);
        AssertNoBlockLiveAndFreeStacksArePermutations(arena);
        Assert.Equal(2, arena.PeakLiveBlocks(1));
    }
}

/// <summary>The 0 B guardrail over the story 003 arena (ADR-0006: 0 B per tick after warm-up).</summary>
[Collection(NonParallelCollection.Name)]
public class MailIdArenaAllocationTests
{
    [Fact]
    public void CreateTakeUnionAppendFreeCanReserve_AfterWarmUp_Allocate0Bytes()
    {
        // Arrange: the 0 B guardrail applies to optimised builds only (TD-008 pattern, RecorderTests.SkipWhenUnoptimised)
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(MailIdArena).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(debuggable is { IsJITOptimizerDisabled: true }, "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
        var arena = new MailIdArena(MailIdArena.MinTotalUnits, new SimCheckRing());
        var odd = new uint[40];
        var even = new uint[40];
        for (int i = 0; i < odd.Length; i++)
        {
            odd[i] = (uint)(2 * i + 1);
            even[i] = (uint)(2 * i + 2);
        }

        var extra = new uint[] { 1000, 1001, 1002 };
        var reservation = new[] { 4, 6, 7, 80, 0 };
        var taken = new uint[MailIdArena.MaxUnits];
        uint tick = 0;
        void Window()
        {
            for (int round = 0; round < 50; round++)
            {
                tick++;
                if (!arena.CanReserve(reservation)
                    || !arena.TryCreate(odd, tick, out MailIdsHandle a)
                    || !arena.TryCreate(even, tick, out MailIdsHandle b)
                    || !arena.TryUnion(in a, in b, tick, out MailIdsHandle u)
                    || !arena.TryAppend(ref u, extra, tick))
                {
                    throw new InvalidOperationException("arena refused");
                }

                while (u.Count > 2)
                {
                    arena.Take(ref u, 3, taken, tick); // crosses the quarter: re-homes along the way
                }

                arena.Free(in u);
            }
        }

        // Act + Assert
        AllocAssert.Zero("50 rounds of reserve/create/union/append/take/free", Window);
        Assert.Equal(0, arena.LiveBlocks);
        Assert.Equal(0, arena.LiveUnits);
    }
}
