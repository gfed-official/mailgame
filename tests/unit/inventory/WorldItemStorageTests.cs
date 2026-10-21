using System.Buffers;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/013, slice 1 (ADR-0009 section 5): the WorldItem storage on its own — <see cref="WorldItemTable"/> (slots
/// reused, ids never; ascending-id walk), <see cref="ExpiryFifos"/> (append order, lazy tombstones, the head memo and the H-PERF-6d
/// visit counter, compaction on a full ring) and <see cref="Tombstones"/> (the 256-entry ring, 30 s inclusive lifetime, overwrite of
/// the oldest). The behaviour through <c>CreateWorldItem</c> and the <c>ItemsExpire</c> phase is in <c>WorldItemTests</c>.
/// </summary>
public class WorldItemStorageTests
{
    private static EntityId Wi(uint counter) => EntityId.Compose(EntityClass.WorldItem, counter);

    // ---------------------------------------------------------------- WorldItemTable

    [Fact]
    public void Table_AddRemoveAdd_ReusesTheSlot_NeverTheId_AndWalksLiveItemsInAscendingIdOrder()
    {
        // Arrange
        var table = new WorldItemTable(4);
        Assert.True(table.TryAdd(Wi(1), out ushort s1));
        Assert.True(table.TryAdd(Wi(2), out ushort s2));
        Assert.True(table.TryAdd(Wi(3), out ushort s3));

        // Act: remove the middle one, then add id 4 (which takes the freed slot)
        table.Remove(s2);
        Assert.True(table.TryAdd(Wi(4), out ushort s4));

        // Assert
        Assert.Equal(s2, s4);
        Assert.Equal(3, table.LiveCount);
        Assert.False(table.TryGetSlot(Wi(2), out _));
        Assert.True(table.TryGetSlot(Wi(4), out ushort found));
        Assert.Equal(s4, found);
        Assert.False(table.IsLiveAt(s2, Wi(2).Value)); // a FIFO node naming id 2 at that slot is now stale
        Assert.Equal(new uint[] { Wi(1).Value, Wi(3).Value, Wi(4).Value }, Walk(table));
        Assert.Equal(s1, table.FirstSlot);
        _ = s3;
    }

    [Fact]
    public void Table_Full_RefusesAnAdd_AndADuplicateOrNoneIdIsRefused()
    {
        // Arrange
        var table = new WorldItemTable(2);
        Assert.True(table.TryAdd(Wi(1), out _));

        // Act + Assert
        Assert.False(table.TryAdd(Wi(1), out _));
        Assert.False(table.TryAdd(EntityId.None, out _));
        Assert.True(table.TryAdd(Wi(2), out _));
        Assert.False(table.TryAdd(Wi(3), out ushort none));
        Assert.Equal(WorldItemTable.NoSlot, none);
        Assert.Equal(2, table.LiveCount);
    }

    [Fact]
    public void Table_RemovingHeadAndTail_KeepsTheIdOrderedList()
    {
        // Arrange
        var table = new WorldItemTable(8);
        var slots = new ushort[5];
        for (uint i = 0; i < 5; i++)
        {
            Assert.True(table.TryAdd(Wi(i + 1), out slots[i]));
        }

        // Act
        table.Remove(slots[0]);
        table.Remove(slots[4]);
        table.Remove(slots[4]); // a free slot is ignored
        Assert.True(table.TryAdd(Wi(6), out _));

        // Assert
        Assert.Equal(new uint[] { Wi(2).Value, Wi(3).Value, Wi(4).Value, Wi(6).Value }, Walk(table));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65_536)]
    public void Table_RejectsACapacityOutsideTheUshortSlotRange(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldItemTable(capacity));
    }

    // ---------------------------------------------------------------- ExpiryFifos

    [Fact]
    public void Fifo_HeadIsTheFirstAppended_TombstonesArePoppedLazily_AndEachInspectedNodeCountsOnTheEvictionPath()
    {
        // Arrange: 3 items appended in id order to one FIFO; the first two are then removed (picked up)
        var counters = new SimCounters();
        var table = new WorldItemTable(8);
        var fifos = new ExpiryFifos(8, counters);
        var slots = new ushort[3];
        for (uint i = 0; i < 3; i++)
        {
            Assert.True(table.TryAdd(Wi(i + 1), out slots[i]));
            Assert.True(fifos.Append(ExpiryFifo.NonMailWorldItem, table, Wi(i + 1).Value, slots[i], 10_000 + i));
        }

        table.Remove(slots[0]);
        fifos.OnRemoved(ExpiryFifo.NonMailWorldItem, Wi(1).Value);
        table.Remove(slots[1]);
        fifos.OnRemoved(ExpiryFifo.NonMailWorldItem, Wi(2).Value);

        // Act 1: an eviction-path peek pops the two tombstones and verifies the third
        bool found = fifos.TryPeekHead(ExpiryFifo.NonMailWorldItem, table, countVisits: true, out ushort head);
        long afterFirst = counters[CounterId.InventoryWorldItemCapCandidates];

        // Act 2: the verified head is returned again without a visit
        bool again = fifos.TryPeekHead(ExpiryFifo.NonMailWorldItem, table, countVisits: true, out ushort head2);

        // Assert
        Assert.True(found);
        Assert.True(again);
        Assert.Equal(slots[2], head);
        Assert.Equal(slots[2], head2);
        Assert.Equal(10_002u, fifos.HeadExpiresTick(ExpiryFifo.NonMailWorldItem));
        Assert.Equal(3, afterFirst);
        Assert.Equal(3, counters[CounterId.InventoryWorldItemCapCandidates]);
        Assert.Equal(1, fifos.NodeCount(ExpiryFifo.NonMailWorldItem));
    }

    [Fact]
    public void Fifo_ExpiryPathPeek_CountsNothing_AndAHeadItVerifiedIsFreeForTheEvictionPath()
    {
        // Arrange
        var counters = new SimCounters();
        var table = new WorldItemTable(4);
        var fifos = new ExpiryFifos(4, counters);
        Assert.True(table.TryAdd(Wi(1), out ushort s));
        Assert.True(fifos.Append(ExpiryFifo.MailWorldItem, table, Wi(1).Value, s, 9_000));

        // Act
        Assert.True(fifos.TryPeekHead(ExpiryFifo.MailWorldItem, table, countVisits: false, out _));
        Assert.True(fifos.TryPeekHead(ExpiryFifo.MailWorldItem, table, countVisits: true, out _));

        // Assert
        Assert.Equal(0, counters[CounterId.InventoryWorldItemCapCandidates]);
    }

    [Fact]
    public void Fifo_RemovingTheVerifiedHead_DropsTheMemo_SoTheNextPeekInspectsAgain()
    {
        // Arrange
        var counters = new SimCounters();
        var table = new WorldItemTable(4);
        var fifos = new ExpiryFifos(4, counters);
        Assert.True(table.TryAdd(Wi(1), out ushort a));
        Assert.True(table.TryAdd(Wi(2), out ushort b));
        Assert.True(fifos.Append(ExpiryFifo.MailWorldItem, table, Wi(1).Value, a, 9_000));
        Assert.True(fifos.Append(ExpiryFifo.MailWorldItem, table, Wi(2).Value, b, 9_001));
        Assert.True(fifos.TryPeekHead(ExpiryFifo.MailWorldItem, table, countVisits: true, out _));

        // Act: the head is picked up; a slot reuse by a new id must not make its stale node look live
        table.Remove(a);
        fifos.OnRemoved(ExpiryFifo.MailWorldItem, Wi(1).Value);
        Assert.True(table.TryAdd(Wi(3), out ushort reused));
        bool found = fifos.TryPeekHead(ExpiryFifo.MailWorldItem, table, countVisits: true, out ushort head);

        // Assert: tombstone popped (visit 2), id 2 verified (visit 3)
        Assert.Equal(a, reused);
        Assert.True(found);
        Assert.Equal(b, head);
        Assert.Equal(3, counters[CounterId.InventoryWorldItemCapCandidates]);
    }

    [Fact]
    public void Fifo_FullRing_IsCompactedOnAppend_KeepingLiveOrder()
    {
        // Arrange: capacity 4, four items appended, the 2nd and 3rd removed
        var counters = new SimCounters();
        var table = new WorldItemTable(4);
        var fifos = new ExpiryFifos(4, counters);
        var slots = new ushort[4];
        for (uint i = 0; i < 4; i++)
        {
            Assert.True(table.TryAdd(Wi(i + 1), out slots[i]));
            Assert.True(fifos.Append(ExpiryFifo.NonMailWorldItem, table, Wi(i + 1).Value, slots[i], 100 + i));
        }

        table.Remove(slots[1]);
        table.Remove(slots[2]);
        Assert.True(table.TryAdd(Wi(5), out ushort s5));

        // Act
        bool appended = fifos.Append(ExpiryFifo.NonMailWorldItem, table, Wi(5).Value, s5, 200);

        // Assert
        Assert.True(appended);
        Assert.Equal(3, fifos.NodeCount(ExpiryFifo.NonMailWorldItem));
        Assert.Equal(new[] { Wi(1).Value, Wi(4).Value, Wi(5).Value }, Enumerable.Range(0, 3).Select(i => fifos.IdAt(ExpiryFifo.NonMailWorldItem, i)).ToArray());
        Assert.Equal(0, counters[CounterId.InventoryWorldItemCapCandidates]);
    }

    [Fact]
    public void Fifo_EmptyPeek_ReturnsFalse_AndCountsNothing()
    {
        var counters = new SimCounters();
        var fifos = new ExpiryFifos(2, counters);

        Assert.False(fifos.TryPeekHead(ExpiryFifo.Bag, new WorldItemTable(2), countVisits: true, out ushort slot));
        Assert.Equal(WorldItemTable.NoSlot, slot);
        Assert.Equal(0, counters[CounterId.InventoryWorldItemCapCandidates]);
    }

    // ---------------------------------------------------------------- Tombstones

    [Theory]
    [InlineData(0u, true)]
    [InlineData(899u, true)]
    [InlineData(900u, true)]
    [InlineData(901u, false)]
    public void Tombstone_IsAliveFor30SecondsInclusive(uint age, bool alive)
    {
        // Arrange
        var t = new Tombstones();
        t.Add(Wi(7).Value, 0x0100_0001u, InvCause.Taken, 12, 1_000);

        // Act
        bool found = t.TryResolve(Wi(7).Value, 1_000 + age, out Tombstone stone);

        // Assert
        Assert.Equal(alive, found);
        if (alive)
        {
            Assert.Equal(new Tombstone(Wi(7).Value, 0x0100_0001u, InvCause.Taken, 12, 1_000), stone);
        }

        Assert.Equal(900u, Tombstones.LifetimeTicks);
    }

    [Fact]
    public void Tombstone_Ring_OverwritesTheOldest_After256()
    {
        // Arrange
        var t = new Tombstones();

        // Act
        for (uint i = 1; i <= 257; i++)
        {
            t.Add(Wi(i).Value, ActorId.System.Value, InvCause.Expired, i, 5);
        }

        // Assert: id 1 was overwritten by id 257; 2 and 257 are held
        Assert.Equal(Tombstones.Capacity, t.Count);
        Assert.False(t.TryResolve(Wi(1).Value, 5, out _));
        Assert.True(t.TryResolve(Wi(2).Value, 5, out _));
        Assert.True(t.TryResolve(Wi(257).Value, 5, out Tombstone last));
        Assert.Equal(257u, last.WorldSeq);
    }

    [Fact]
    public void Tombstone_Dump_WritesOldestFirst()
    {
        // Arrange
        var t = new Tombstones();
        t.Add(Wi(1).Value, 9, InvCause.Destroyed, 3, 40);
        t.Add(Wi(2).Value, 8, InvCause.Taken, 4, 41);

        // Act
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());
        w.BeginSection(SectionIds.Inventory);
        t.Write(ref w);
        w.EndSection();
        byte[] bytes = output.WrittenSpan.ToArray();

        // Assert: the record is the last 4 + 2 x 17 bytes of the section body
        int body = bytes.Length - (4 + (2 * 17));
        Assert.Equal(2u, BitConverter.ToUInt32(bytes, body));
        Assert.Equal(Wi(1).Value, BitConverter.ToUInt32(bytes, body + 4));
        Assert.Equal((byte)InvCause.Destroyed, bytes[body + 12]);
        Assert.Equal(Wi(2).Value, BitConverter.ToUInt32(bytes, body + 4 + 17));
    }

    private static uint[] Walk(WorldItemTable table)
    {
        var ids = new List<uint>();
        for (ushort s = table.FirstSlot; s != WorldItemTable.NoSlot; s = table[s].NextSlot)
        {
            ids.Add(table[s].Id.Value);
        }

        return ids.ToArray();
    }
}
