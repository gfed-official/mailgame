using System.Reflection;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/002 (TR-inventory-003; GDD R2 tests H-R2-1 and H-R2-2 in primitive form): the entry pool, entry identity,
/// the footprint rules and the per-container occupancy bitmaps. Assert-vs-tolerant behaviour is chosen by
/// <see cref="TestBuildExpectation.SimAssertsExpected"/> (INVENTORY_ASSERT is defined together with SIM_ASSERT).
/// </summary>
public class EntryStorageTests
{
    private const int Rows = 16;

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

    /// <summary>The caller's R2 rule in primitive form: a decrement that reaches 0 frees the entry in the same call.</summary>
    private static void Decrement(EntryPool pool, ushort slot)
    {
        byte count = pool[slot].Count;
        if (count <= 1)
        {
            pool.Free(slot);
        }
        else
        {
            pool.SetCount(slot, (byte)(count - 1));
        }
    }

    // ---------------------------------------------------------------- AC-1: H-R2-1 (primitive form)

    [Fact]
    [Trait("Id", "H-R2-1")]
    public void HR21_LastId41_CreateMoveDrainCreate_Id42TravelsThenDiesAndNextIs43_NeverReissuedAcross10000()
    {
        // Arrange: the allocator's last id is 41; two containers (table slots 0 and 1) of 8x2
        var ring = new SimCheckRing();
        var pool = new EntryPool(16_384, ring, initialLastId: 41);
        var occ = new Occupancy(2, Rows);
        occ.Reset(0, 8, 2);
        occ.Reset(1, 8, 2);

        // Act 1: create -> 42, place in container A
        Assert.True(pool.TryAllocate(1, out ushort slot, out EntryId id));
        ref EntryRecord e = ref pool[slot];
        e.ContainerSlot = 0;
        e.X = 2;
        e.Y = 0;
        e.W = 1;
        e.H = 1;
        pool.SetCount(slot, 2);
        occ.Place(0, 8, 2, e.X, e.Y, 1, 1, slot);

        // Act 2: move to container B via the record and the occupancy primitives
        occ.Clear(0, 8, 2, e.X, e.Y, 1, 1);
        e.ContainerSlot = 1;
        e.X = 5;
        e.Y = 1;
        occ.Place(1, 8, 2, e.X, e.Y, 1, 1, slot);

        // Assert: 42 travelled with the entry
        Assert.Equal(42u, id.Value);
        Assert.False(id.IsTemp);
        Assert.Equal(id, pool[slot].Id);
        Assert.Equal(1, pool[slot].ContainerSlot);
        Assert.Equal(Occupancy.Free, occ.OwnerAt(0, 8, 2, 0));
        Assert.Equal(slot, occ.OwnerAt(1, 8, 5, 1));
        Assert.True(pool.TryGetSlot(id, out ushort found) && found == slot);

        // Act 3: count driven to 0 is removed in the same call (2 -> 1 -> freed); a literal 0 write is an assert trip
        occ.Clear(1, 8, 2, e.X, e.Y, 1, 1);
        Decrement(pool, slot);
        Assert.Equal(1, pool[slot].Count);
        Assert.Equal(1, pool.LiveCount);
        Decrement(pool, slot);

        // Assert: no 0-count entry exists after any call; the id left the map
        Assert.Equal(0, pool.LiveCount);
        Assert.False(pool[slot].IsLive);
        Assert.False(pool.TryGetSlot(id, out _));
        Assert.Equal(42u, pool.LastId);

        // Act 4: the next entry is 43 and reuses the freed slot, the id is not reused
        Assert.True(pool.TryAllocate(2, out ushort slot2, out EntryId id2));
        Assert.Equal(43u, id2.Value);
        Assert.Equal(slot, slot2);
        Assert.Equal(1, pool[slot2].Count);
        Assert.Equal(EntryRecord.NoContainer, pool[slot2].ContainerSlot);

        // Act 5: 10 000 further allocations (freeing every third) never yield 42, and LastId only rises
        uint last = pool.LastId;
        for (int i = 0; i < 10_000; i++)
        {
            Assert.True(pool.TryAllocate(3, out ushort s, out EntryId next));
            Assert.NotEqual(42u, next.Value);
            Assert.True(next.Value > last);
            last = next.Value;
            if (i % 3 == 0)
            {
                pool.Free(s);
            }
        }

        Assert.Equal(43u + 10_000u, pool.LastId);
        Assert.False(pool.TryGetSlot(EntryId.Real(42), out _));
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void Free_OfAFreeSlot_AssertsUnderInventoryAssert_OrIsANoOpInRelease()
    {
        // Arrange
        var pool = new EntryPool(4, new SimCheckRing());
        Assert.True(pool.TryAllocate(0, out ushort slot, out _));
        pool.Free(slot);
        int liveAfterFirstFree = pool.LiveCount;

        // Act + Assert: a second Free of the same slot never double-pushes the free stack
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => pool.Free(slot));
            Assert.Equal(SimAssertCode.InventoryEntrySlotFree, thrown.Code);
            thrown = Assert.Throws<SimAssertException>(() => pool.SetCount(slot, 3));
            Assert.Equal(SimAssertCode.InventoryEntrySlotFree, thrown.Code);
        }
        else
        {
            pool.Free(slot);
        }

        Assert.Equal(0, liveAfterFirstFree);
        Assert.Equal(0, pool.LiveCount);
        for (int i = 0; i < 4; i++)
        {
            Assert.True(pool.TryAllocate(0, out _, out _));
        }

        Assert.Equal(4, pool.LiveCount);
        Assert.False(pool.TryAllocate(0, out _, out _));
    }

    [Fact]
    public void Place_OnAnOccupiedOrOutOfBoundsFootprint_AssertsUnderInventoryAssert()
    {
        // Arrange: 8x2 grid with a 2x1 at (0,0)
        var occ = new Occupancy(1, 2, 8);
        occ.Reset(0, 8, 2);
        occ.Place(0, 8, 2, 0, 0, 2, 1, 7);
        Assert.SkipUnless(TestBuildExpectation.SimAssertsExpected, "INVENTORY_ASSERT is compiled out of this build.");

        // Act + Assert
        SimAssertException conflict = Assert.Throws<SimAssertException>(() => occ.Place(0, 8, 2, 1, 0, 1, 1, 9));
        Assert.Equal(SimAssertCode.InventoryCellConflict, conflict.Code);
        SimAssertException freeOwner = Assert.Throws<SimAssertException>(() => occ.Place(0, 8, 2, 4, 0, 1, 1, Occupancy.Free));
        Assert.Equal(SimAssertCode.InventoryCellConflict, freeOwner.Code);
        SimAssertException outOfBounds = Assert.Throws<SimAssertException>(() => occ.Place(0, 8, 2, 7, 1, 2, 1, 9));
        Assert.Equal(SimAssertCode.InventoryFootprintOutOfBounds, outOfBounds.Code);
        SimAssertException clearFree = Assert.Throws<SimAssertException>(() => occ.Clear(0, 8, 2, 2, 0, 2, 1));
        Assert.Equal(SimAssertCode.InventoryCellConflict, clearFree.Code);
        SimAssertException clearOutOfBounds = Assert.Throws<SimAssertException>(() => occ.Clear(0, 8, 2, 0, 1, 1, 2));
        Assert.Equal(SimAssertCode.InventoryFootprintOutOfBounds, clearOutOfBounds.Code);
        Assert.Equal(7, occ.OwnerAt(0, 8, 0, 0));
        Assert.Equal(0b11UL, occ.RowBits(0, 0));
    }

    [Fact]
    [Trait("Id", "H-R2-1")]
    public void SetCount_Zero_AssertsUnderInventoryAssert_OrWritesInRelease()
    {
        // Arrange
        var pool = new EntryPool(4, new SimCheckRing());
        Assert.True(pool.TryAllocate(0, out ushort slot, out _));
        pool.SetCount(slot, 255);
        Assert.Equal(255, pool[slot].Count);

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => pool.SetCount(slot, 0));
            Assert.Equal(SimAssertCode.InventoryCountZero, thrown.Code);
            Assert.Equal(255, pool[slot].Count);
        }
        else
        {
            pool.SetCount(slot, 0);
            Assert.Equal(0, pool[slot].Count);
        }
    }

    [Fact]
    [Trait("Id", "H-R2-1")]
    public void Free_RemovesTheIdFromTheMap_AndTheSlotIsReusedLifo()
    {
        // Arrange
        var pool = new EntryPool(8, new SimCheckRing());
        var slots = new ushort[3];
        var ids = new EntryId[3];
        for (int i = 0; i < 3; i++)
        {
            Assert.True(pool.TryAllocate(0, out slots[i], out ids[i]));
        }

        // Act
        pool.Free(slots[1]);
        pool.Free(slots[0]);

        // Assert
        Assert.Equal(1, pool.LiveCount);
        Assert.False(pool.TryGetSlot(ids[0], out _));
        Assert.False(pool.TryGetSlot(ids[1], out _));
        Assert.True(pool.TryGetSlot(ids[2], out ushort s2) && s2 == slots[2]);
        Assert.True(pool.TryAllocate(0, out ushort reused, out EntryId fresh));
        Assert.Equal(slots[0], reused); // last freed, first reused
        Assert.Equal(4u, fresh.Value);
        Assert.False(pool.TryGetSlot(EntryId.None, out _));
        Assert.False(pool.TryGetSlot(EntryId.Temp(PlayerSlot.FromValue(0), 1), out _));
    }

    // ---------------------------------------------------------------- AC-2: H-R2-2

    [Fact]
    [Trait("Id", "H-R2-2")]
    public void HR22_SquareFootprintPlacedRotated_CanonicalisesToUnrotated_AndCoversTheFourCells()
    {
        // Arrange
        var occ = new Occupancy(1, Rows);
        occ.Reset(0, 8, 4);
        bool rotated = true;

        // Act
        Footprint.Canonicalise(2, 2, ref rotated);
        Footprint.Effective(2, 2, rotated, out int fw, out int fh);
        Assert.True(occ.Fits(0, 8, 4, 0, 0, fw, fh));
        occ.Place(0, 8, 4, 0, 0, fw, fh, 7);

        // Assert
        Assert.False(rotated);
        Assert.Equal((2, 2), (fw, fh));
        Assert.Equal(0b11UL, occ.RowBits(0, 0));
        Assert.Equal(0b11UL, occ.RowBits(0, 1));
        Assert.Equal(0UL, occ.RowBits(0, 2));
        Assert.Equal(7, occ.OwnerAt(0, 8, 0, 0));
        Assert.Equal(7, occ.OwnerAt(0, 8, 1, 0));
        Assert.Equal(7, occ.OwnerAt(0, 8, 0, 1));
        Assert.Equal(7, occ.OwnerAt(0, 8, 1, 1));
        Assert.Equal(Occupancy.Free, occ.OwnerAt(0, 8, 2, 0));
        Assert.Equal(Occupancy.Free, occ.OwnerAt(0, 8, 0, 2));
    }

    [Fact]
    [Trait("Id", "H-R2-2")]
    public void HR22_OneByTwoPlacedRotated_OccupiesTwoByOne()
    {
        // Arrange
        var occ = new Occupancy(1, Rows);
        occ.Reset(0, 8, 4);
        bool rotated = true;

        // Act
        Footprint.Canonicalise(1, 2, ref rotated);
        Footprint.Effective(1, 2, rotated, out int fw, out int fh);
        occ.Place(0, 8, 4, 3, 2, fw, fh, 9);

        // Assert
        Assert.True(rotated);
        Assert.Equal((2, 1), (fw, fh));
        Assert.Equal(0b11UL << 3, occ.RowBits(0, 2));
        Assert.Equal(0UL, occ.RowBits(0, 3));
        Assert.Equal(9, occ.OwnerAt(0, 8, 3, 2));
        Assert.Equal(9, occ.OwnerAt(0, 8, 4, 2));
        Assert.Equal(Occupancy.Free, occ.OwnerAt(0, 8, 3, 3));
        Assert.False(occ.Fits(0, 8, 4, 4, 2, 1, 1));
        Assert.True(occ.Fits(0, 8, 4, 5, 2, 1, 1));
    }

    // ---------------------------------------------------------------- AC-3: footprint test, cellOwner, property test

    [Theory]
    [InlineData(8, 2)]
    [InlineData(8, 4)]
    [InlineData(20, 16)]
    public void Fits_OutOfBoundsAndPartialOverlap_AreRejected(int w, int h)
    {
        // Arrange: a 2x1 at (w-2, 0)
        var occ = new Occupancy(1, Rows);
        occ.Reset(0, w, h);
        occ.Place(0, w, h, w - 2, 0, 2, 1, 1);

        // Act + Assert: bounds
        Assert.False(occ.Fits(0, w, h, -1, 0, 1, 1));
        Assert.False(occ.Fits(0, w, h, 0, -1, 1, 1));
        Assert.False(occ.Fits(0, w, h, w, 0, 1, 1));
        Assert.False(occ.Fits(0, w, h, 0, h, 1, 1));
        Assert.False(occ.Fits(0, w, h, w - 1, 1, 2, 1));
        Assert.False(occ.Fits(0, w, h, 0, h - 1, 1, 2));
        Assert.False(occ.Fits(0, w, h, 0, 0, 0, 1));
        Assert.False(occ.Fits(0, w, h, 0, 0, 1, 0));
        Assert.True(occ.Fits(0, w, h, 0, 1, w, h - 1));

        // partial overlap: a 2x2 at (w-3, 0) covers (w-2, 0)
        Assert.False(occ.Fits(0, w, h, w - 3, 0, 2, 2));
        Assert.True(occ.Fits(0, w, h, w - 4, 0, 2, 2));
        Assert.True(occ.Fits(0, w, h, w - 3, 1, 3, 1));
        Assert.Equal(Occupancy.Free, occ.OwnerAt(0, w, w, 0));
        Assert.Equal(Occupancy.Free, occ.OwnerAt(0, w, -1, 0));
        Assert.Equal(1, occ.OwnerAt(0, w, w - 1, 0));
    }

    [Theory]
    [InlineData(8, 2, 0x1234_5678u)]
    [InlineData(8, 4, 0x0BAD_CAFEu)]
    [InlineData(20, 16, 0xDEAD_BEEFu)]
    public void PropertyTest_10000RandomPlaceRemoveSteps_BitmapsOwnersAndBruteForceGridAgree(int w, int h, uint seed)
    {
        // Arrange: three containers in one occupancy; this grid lives in slot 1 so slot indexing is exercised
        const ushort Slot = 1;
        var occ = new Occupancy(3, Rows);
        occ.Reset(0, 20, 16);
        occ.Reset(Slot, w, h);
        occ.Reset(2, 8, 2);
        var grid = new ushort[w, h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                grid[x, y] = Occupancy.Free;
            }
        }

        var placed = new List<(ushort Entry, int X, int Y, int Fw, int Fh)>();
        var rng = new XorShift32(seed);
        ushort nextEntry = 0;
        int placements = 0;
        int rejections = 0;

        // Act + Assert, after every step
        for (int step = 0; step < 10_000; step++)
        {
            bool remove = placed.Count > 0 && rng.Next(3) == 0;
            if (remove)
            {
                int k = rng.Next(placed.Count);
                (ushort entry, int x, int y, int fw, int fh) = placed[k];
                placed[k] = placed[^1];
                placed.RemoveAt(placed.Count - 1);
                occ.Clear(Slot, w, h, x, y, fw, fh);
                for (int yy = y; yy < y + fh; yy++)
                {
                    for (int xx = x; xx < x + fw; xx++)
                    {
                        Assert.Equal(entry, grid[xx, yy]);
                        grid[xx, yy] = Occupancy.Free;
                    }
                }
            }
            else
            {
                int fw = 1 + rng.Next(Math.Min(4, w));
                int fh = 1 + rng.Next(Math.Min(4, h));
                int x = rng.Next(w + 2) - 1; // -1 to w, so out-of-bounds requests are drawn too
                int y = rng.Next(h + 2) - 1;
                bool expected = x >= 0 && y >= 0 && x + fw <= w && y + fh <= h;
                for (int yy = y; expected && yy < y + fh; yy++)
                {
                    for (int xx = x; xx < x + fw; xx++)
                    {
                        if (grid[xx, yy] != Occupancy.Free)
                        {
                            expected = false;
                            break;
                        }
                    }
                }

                Assert.Equal(expected, occ.Fits(Slot, w, h, x, y, fw, fh));
                if (expected)
                {
                    ushort entry = nextEntry;
                    nextEntry = (ushort)((nextEntry + 1) % Occupancy.Free);
                    occ.Place(Slot, w, h, x, y, fw, fh, entry);
                    placed.Add((entry, x, y, fw, fh));
                    for (int yy = y; yy < y + fh; yy++)
                    {
                        for (int xx = x; xx < x + fw; xx++)
                        {
                            grid[xx, yy] = entry;
                        }
                    }

                    placements++;
                }
                else
                {
                    rejections++;
                }
            }

            for (int y = 0; y < h; y++)
            {
                ulong expectedBits = 0UL;
                for (int x = 0; x < w; x++)
                {
                    Assert.Equal(grid[x, y], occ.OwnerAt(Slot, w, x, y));
                    if (grid[x, y] != Occupancy.Free)
                    {
                        expectedBits |= 1UL << x;
                    }
                }

                Assert.Equal(expectedBits, occ.RowBits(Slot, y));
            }
        }

        // the neighbours were never touched
        Assert.Equal(0UL, occ.RowBits(0, 0));
        Assert.Equal(0UL, occ.RowBits(2, 0));
        Assert.True(placements > 100, $"only {placements} placements");
        Assert.True(rejections > 100, $"only {rejections} rejections");
    }

    [Fact]
    public void Reset_GridLargerThanThePreallocation_Throws()
    {
        // Arrange
        var occ = new Occupancy(2, 4, 20);

        // Act + Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => occ.Reset(0, 21, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => occ.Reset(0, 20, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => occ.Reset(2, 8, 2));
        occ.Reset(1, 20, 4);
        occ.Place(1, 20, 4, 19, 3, 1, 1, 5);
        occ.Reset(1, 20, 4);
        Assert.Equal(Occupancy.Free, occ.OwnerAt(1, 20, 19, 3));
        Assert.Equal(0UL, occ.RowBits(1, 3));
    }

    [Fact]
    public void Fits_FullWidth64_UsesTheFullMask()
    {
        // Arrange
        var occ = new Occupancy(1, 2, 64);
        occ.Reset(0, 64, 2);

        // Act
        occ.Place(0, 64, 2, 0, 0, 64, 1, 3);

        // Assert
        Assert.Equal(ulong.MaxValue, occ.RowBits(0, 0));
        Assert.False(occ.Fits(0, 64, 2, 63, 0, 1, 1));
        Assert.True(occ.Fits(0, 64, 2, 0, 1, 64, 1));
        Assert.Equal(3, occ.OwnerAt(0, 64, 63, 0));
    }

    // ---------------------------------------------------------------- AC-4: EntryRecord layout, IdMap, exhaustion

    [Fact]
    public void EntryRecord_IsAtMost40Bytes_AndHoldsEveryR2Field()
    {
        // Act
        int size = Unsafe.SizeOf<EntryRecord>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (FieldInfo f in typeof(EntryRecord).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            names.Add(f.Name);
        }

        // Assert
        Assert.True(size <= 40, $"EntryRecord is {size} B");
        foreach (string required in new[]
                 {
                     "Id", "ContainerSlot", "X", "Y", "W", "H", "Rotated", "Count", "IsMail", "MailKind", "AddressId", "Flags", "ItemId",
                     "ArrivalSeq", "MailIds",
                 })
        {
            Assert.Contains(required, names);
        }

        Assert.Equal(typeof(byte), typeof(EntryRecord).GetField("Count")!.FieldType);
        Assert.Equal(typeof(EntryId), typeof(EntryRecord).GetField("Id")!.FieldType);
        Assert.Equal(typeof(uint), typeof(EntryRecord).GetField("ArrivalSeq")!.FieldType);
        Assert.Equal(typeof(MailIdsHandle), typeof(EntryRecord).GetField("MailIds")!.FieldType);
        Assert.Equal(4, Unsafe.SizeOf<MailIdsHandle>());
        Assert.False(default(EntryRecord).IsLive);
    }

    [Fact]
    public void Footprint_Canonicalise_OnlySquaresLoseRotation()
    {
        // Arrange
        bool square = true;
        bool tall = true;
        bool wide = false;

        // Act
        Footprint.Canonicalise(3, 3, ref square);
        Footprint.Canonicalise(1, 3, ref tall);
        Footprint.Canonicalise(3, 1, ref wide);
        Footprint.Effective(3, 1, false, out int fw, out int fh);

        // Assert
        Assert.False(square);
        Assert.True(tall);
        Assert.False(wide);
        Assert.Equal((3, 1), (fw, fh));
    }

    [Fact]
    public void IdMap_AddGetRemove_BackwardShiftKeepsTheRestOfTheProbeRunFindable()
    {
        // Arrange: a tiny table (size 4, mask 3) so every key collides into one run
        var map = new IdMap(2);
        Assert.Equal(4, map.TableSize);
        uint[] keys = { 1, 2, 3 };
        for (int i = 0; i < keys.Length; i++)
        {
            Assert.True(map.TryAdd(keys[i], (ushort)(100 + i)));
        }

        Assert.Equal(3, map.Count);
        Assert.False(map.TryAdd(2, 9)); // duplicate
        Assert.False(map.TryAdd(0, 9)); // key 0 is the empty marker

        // Act: remove each key in turn (middle first) and check the others survive the shift
        Assert.True(map.Remove(2));
        Assert.False(map.Remove(2));

        // Assert
        Assert.Equal(2, map.Count);
        Assert.False(map.TryGet(2, out _));
        Assert.True(map.TryGet(1, out ushort v1) && v1 == 100);
        Assert.True(map.TryGet(3, out ushort v3) && v3 == 102);
        Assert.True(map.TryAdd(2, 7));
        Assert.True(map.TryGet(2, out ushort v2) && v2 == 7);
        Assert.False(map.TryAdd(4, 1)); // table full (3 of 4 used; the 4th add would leave no empty marker)
        Assert.False(map.TryGet(4, out _)); // a miss on the full table still terminates
        Assert.True(map.Remove(1) && map.Remove(3) && map.Remove(2));
        Assert.Equal(0, map.Count);
        Assert.False(map.TryGet(1, out _));
    }

    [Fact]
    public void IdMap_ManyCollidingKeys_RandomRemovalOrder_StaysConsistent()
    {
        // Arrange: 1000 keys, remove 500 in a shuffled order, then every survivor must still resolve
        var map = new IdMap(1000);
        var alive = new bool[1001];
        for (uint k = 1; k <= 1000; k++)
        {
            Assert.True(map.TryAdd(k, (ushort)k));
            alive[k] = true;
        }

        var rng = new XorShift32(77);

        // Act
        for (int i = 0; i < 500; i++)
        {
            uint k = (uint)(1 + rng.Next(1000));
            Assert.Equal(alive[k], map.Remove(k));
            alive[k] = false;
        }

        // Assert
        int count = 0;
        for (uint k = 1; k <= 1000; k++)
        {
            Assert.Equal(alive[k], map.TryGet(k, out ushort v));
            if (alive[k])
            {
                Assert.Equal((ushort)k, v);
                count++;
            }
        }

        Assert.Equal(count, map.Count);
    }

    [Fact]
    public void TryAllocate_AtMaxRealId_RecordsIdExhausted_AndMutatesNothing()
    {
        // Arrange: one id left
        var ring = new SimCheckRing();
        var pool = new EntryPool(4, ring, initialLastId: 0x7FFF_FFFEu);
        Assert.True(pool.TryAllocate(5, out _, out EntryId last));
        Assert.Equal(EntryPool.MaxRealId, last.Value);
        Assert.False(last.IsTemp);

        // Act
        bool ok;
        ushort slot = 0;
        EntryId id = default;
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => pool.TryAllocate(6, out _, out _));
            Assert.Equal(SimAssertCode.IdExhausted, thrown.Code);
            ok = false;
        }
        else
        {
            ok = pool.TryAllocate(6, out slot, out id);
        }

        // Assert: the record was written before the assert; the counter never wraps
        Assert.False(ok);
        Assert.Equal(0, slot);
        Assert.True(id.IsNone);
        Assert.Equal(1, ring.Count);
        Assert.Equal(CheckCode.IdExhausted, ring[0].Check);
        Assert.Equal(6u, ring[0].Tick);
        Assert.Equal(0L, ring[0].A);
        Assert.Equal((long)EntryPool.MaxRealId, ring[0].B);
        Assert.Equal(EntryPool.MaxRealId, pool.LastId);
        Assert.Equal(1, pool.LiveCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new EntryPool(4, ring, initialLastId: 0x8000_0000u));
    }

    [Fact]
    public void TryAllocate_FullPool_RecordsInventoryEntryPoolExhausted_AndMutatesNothing()
    {
        // Arrange
        var ring = new SimCheckRing();
        var pool = new EntryPool(3, ring);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(pool.TryAllocate(1, out _, out _));
        }

        // Act
        bool ok = pool.TryAllocate(9, out ushort slot, out EntryId id);

        // Assert
        Assert.False(ok);
        Assert.Equal(0, slot);
        Assert.True(id.IsNone);
        Assert.Equal(3u, pool.LastId);
        Assert.Equal(3, pool.LiveCount);
        Assert.Equal(3, pool.Capacity);
        Assert.Equal(1, ring.Count);
        Assert.Equal(CheckCode.InventoryEntryPoolExhausted, ring[0].Check);
        Assert.Equal(9u, ring[0].Tick);
        Assert.Equal(3L, ring[0].A);
        Assert.Equal(3L, ring[0].B);
    }
}

/// <summary>The 0 B guardrail over the story 002 primitives (ADR-0006: 0 B per tick after warm-up).</summary>
[Collection(NonParallelCollection.Name)]
public class EntryStorageAllocationTests
{
    [Fact]
    public void AllocatePlaceClearFreeLoops_AfterWarmUp_Allocate0Bytes()
    {
        // Arrange: the 0 B guardrail applies to optimised builds only (TD-008 pattern, RecorderTests.SkipWhenUnoptimised)
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(EntryPool).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(debuggable is { IsJITOptimizerDisabled: true }, "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
        var pool = new EntryPool(1024, new SimCheckRing());
        var occ = new Occupancy(2, 16);
        occ.Reset(0, 20, 16);
        occ.Reset(1, 8, 2);
        var slots = new ushort[256];
        uint tick = 0;
        void Window()
        {
            for (int round = 0; round < 10; round++)
            {
                tick++;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (!pool.TryAllocate(tick, out slots[i], out EntryId id) || id.IsNone)
                    {
                        throw new InvalidOperationException("allocation refused");
                    }

                    ref EntryRecord e = ref pool[slots[i]];
                    e.ContainerSlot = 0;
                    e.X = (byte)(i % 20);
                    e.Y = (byte)(i / 20);
                    e.W = 1;
                    e.H = 1;
                    pool.SetCount(slots[i], (byte)(1 + (i & 7)));
                    if (!occ.Fits(0, 20, 16, e.X, e.Y, 1, 1))
                    {
                        throw new InvalidOperationException("footprint refused");
                    }

                    occ.Place(0, 20, 16, e.X, e.Y, 1, 1, slots[i]);
                }

                for (int i = 0; i < slots.Length; i++)
                {
                    ref EntryRecord e = ref pool[slots[i]];
                    if (occ.OwnerAt(0, 20, e.X, e.Y) != slots[i] || !pool.TryGetSlot(e.Id, out _))
                    {
                        throw new InvalidOperationException("owner mismatch");
                    }

                    occ.Clear(0, 20, 16, e.X, e.Y, 1, 1);
                    pool.Free(slots[i]);
                }
            }
        }

        // Act + Assert
        AllocAssert.Zero("10 rounds of 256 allocate/place/clear/free", Window);
        Assert.Equal(0, pool.LiveCount);
    }
}
