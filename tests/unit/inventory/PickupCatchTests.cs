using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/014 (GDD R8, R9 l.128-129, R12 l.137, edge cases l.362-363, H-R9-2, H-R12-1; ADR-0009 sections 1, 2 and 5;
/// TD-154 defaults, Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT; refuse vs fumble for an arena-refused catch,
/// TD-154 item 7, stays open to Evan; advisory fix, Mail Review PR #38 r1): the pickup core (<see cref="PickupCore"/>) and
/// <see cref="IInventoryApi.TryCatch"/> with its fumble. Pickups run both through a test-only <c>Pickup</c> handler
/// (<see cref="PickupTestHandler"/>, bound only in this rig: production binds Pickup in story 019) in the real phase-0 drain, and through
/// the core directly inside a tick phase; catches run inside a tick phase, as Throwing's flight resolution will. State is arranged
/// through real calls (the mutator-backed <c>PlaceMail</c> / <c>PlaceItem</c>, <c>CreateWorldItem</c>, <c>DetachForThrow</c>).
/// </summary>
public class PickupCatchTests
{
    private const uint Address = 4u;

    // ---------------------------------------------------------------- TargetOrder.PickupRow

    [Fact]
    public void PickupRow_WritesHotbarThenPlayerInventory_AndFitsTheLongestRow()
    {
        // Arrange
        Span<ushort> targets = stackalloc ushort[TargetOrder.MaxRowLength];

        // Act
        TargetOrder.PickupRow(7, 9, targets);

        // Assert: [hotbar, player_inventory], no backpack at M-1a
        Assert.Equal(7, targets[0]);
        Assert.Equal(9, targets[1]);
        Assert.Equal(2, TargetOrder.PickupRowLength);
        Assert.Equal(2, TargetOrder.MaxRowLength);
    }

    // ---------------------------------------------------------------- whole pickup

    [Fact]
    public void WholePickup_OfALetterWorldItem_IntoAnEmptyPanel_GoesToHotbarSlot1_RemovesTheItem_AndLeavesTheTakenTombstone()
    {
        // Arrange
        var p = new PickupRig();
        ushort letters = p.Letters(101, 5);
        EntryId entry = p.R.Entries[letters].Id;
        EntityId item = p.Drop(letters);
        int tombstones = p.World.Tombstones.Count;

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert: the entry itself goes into hotbar slot 1 (never 0), keeps its id and its 5 ids
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.True(reply.IsOk);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal(5, extra.RequestedUnits);
        Assert.Equal(5, extra.GotUnits);
        Assert.Equal(p.Hotbar(0), p.R.Entries[letters].ContainerSlot);
        Assert.Equal(1, p.R.Entries[letters].X);
        Assert.Equal(entry, p.R.Entries[letters].Id);
        Assert.Equal(PickupRig.Ids(101, 5), p.R.IdsOf(letters));
        Assert.False(p.World.Table.TryGetSlot(item, out _));
        Assert.Equal(tombstones + 1, p.World.Tombstones.Count);
        Assert.Equal((ushort)InvReason.ContainerGone, p.World.Resolve(item, out _, out uint by, out InvCause cause));
        Assert.Equal(p.A.Value, by);
        Assert.Equal(InvCause.Taken, cause);
        Assert.Equal(PickupVerdict.Taken, p.Handler.Last.Verdict);
    }

    [Fact]
    public void WholePickup_ThroughTheCoreInATickPhase_IsTheSameAsThroughTheHandler()
    {
        // Arrange
        var p = new PickupRig();
        ushort letters = p.Letters(101, 5);
        EntityId item = p.Drop(letters);

        // Act
        PickupResult r = p.Core(p.A, item, 0, out InvReplyExtra extra);

        // Assert
        Assert.Equal(new PickupResult(PickupVerdict.Taken, 0, 0, InvCause.None, 5, 5), r);
        Assert.Equal(5, extra.GotUnits);
        Assert.Equal(p.Hotbar(0), p.R.Entries[letters].ContainerSlot);
        Assert.False(p.World.Table.TryGetSlot(item, out _));
    }

    // ---------------------------------------------------------------- H-R12-1 (core form)

    [Fact]
    [Trait("Id", "H-R12-1")]
    public void HR121_OneSmallWorldItem_AAndBPickUpInOneTick_AFirst_AGainsIt_BGetsContainerGoneByA_Taken_OneCopy()
    {
        // Arrange: one Small parcel WorldItem; A and B have empty panels
        var p = new PickupRig();
        ushort small = p.R.DetachedMail("small", Address, new[] { 900u });
        EntityId item = p.Drop(small);

        // Act: both pickups in one tick, A's enqueued first
        EventEntry[] log = p.PickupBoth(p.A, item, p.B, item);

        // Assert: A gains it; B gets container_gone with byActorId = A, cause taken, from the tombstone A's removal wrote (age 0)
        Reply a = p.ReplyFor(log, p.A);
        Reply b = p.ReplyFor(log, p.B);
        Assert.True(a.IsOk);
        Assert.Equal((ushort)InvReason.ContainerGone, b.Reason);
        InvReplyExtra bx = b.ExtraAs<InvReplyExtra>();
        Assert.Equal(p.A.Value, bx.ByActorId);
        Assert.Equal(InvCause.Taken, bx.Cause);
        Assert.Equal(p.Hotbar(0), p.R.Entries[small].ContainerSlot);
        Assert.Equal(1, p.CopiesOf(900u));
        Assert.Equal(0, p.EntriesIn(p.Hotbar(1)) + p.EntriesIn(p.Inventory(1)));
        WorldItemDespawnedPayload gone = Assert.Single(WorldItemTests.Heads(log, InvEventKinds.WorldItemDespawned)).PayloadAs<WorldItemDespawnedPayload>();
        Assert.Equal(p.A.Value, gone.ByActorId);
        Assert.Equal((byte)InvCause.Taken, gone.Cause);
    }

    // ---------------------------------------------------------------- partial pickup (GDD R12 l.137, l.363)

    [Fact]
    public void PartialPickup_5Letters_RoomFor3_The3OldestMerge_TheItemKeepsIdEntryTimerPositionAndNode_With2Newest_OneEntriesChanged()
    {
        // Arrange: A's hotbar slot 1 holds 17 letters to #4, every other cell of A's panel is full
        var p = new PickupRig();
        ushort stack = p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 17));
        p.FillHotbar(0, from: 2);
        p.FillInventory(0);
        ushort letters = p.Letters(101, 5);
        EntryId entry = p.R.Entries[letters].Id;
        EntityId item = p.Drop(letters);
        Assert.True(p.World.Table.TryGetSlot(item, out ushort ws));
        WorldItemRecord before = p.World.Table[ws];
        uint hotbarV = p.R.Header(p.Hotbar(0)).Version;
        uint inventoryV = p.R.Header(p.Inventory(0)).Version;
        uint seq = p.World.WorldSeq;
        int tombstones = p.World.Tombstones.Count;

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert: ok, requested 5 / got 3; the 3 oldest (101-103) merged into the hotbar stack (20)
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.True(reply.IsOk);
        Assert.Equal(5, reply.ExtraAs<InvReplyExtra>().RequestedUnits);
        Assert.Equal(3, reply.ExtraAs<InvReplyExtra>().GotUnits);
        Assert.Equal(20, p.R.Entries[stack].Count);
        Assert.Equal(PickupRig.Ids(1, 17).Concat(new[] { 101u, 102u, 103u }), p.R.IdsOf(stack));

        // the WorldItem survives: same id, entry, expiresTick, position, FIFO, with the 2 newest
        Assert.True(p.World.Table.TryGetSlot(item, out ushort after));
        Assert.Equal(ws, after);
        WorldItemRecord now = p.World.Table[after];
        Assert.Equal(before.EntrySlot, now.EntrySlot);
        Assert.Equal(before.ExpiresTick, now.ExpiresTick);
        Assert.Equal(before.PosMm, now.PosMm);
        Assert.Equal(before.Fifo, now.Fifo);
        Assert.Equal(entry, p.R.Entries[letters].Id);
        Assert.Equal(new[] { 104u, 105u }, p.R.IdsOf(letters));
        Assert.Equal(EntryRecord.NoContainer, p.R.Entries[letters].ContainerSlot);
        Assert.True(p.World.Fifos.TryPeekHead(ExpiryFifo.MailWorldItem, p.World.Table, countVisits: false, out ushort head));
        Assert.Equal(after, head);

        // one WorldItemEntriesChanged {id, next worldSeq, entryId, 2}; no despawn, no tombstone; hotbar +1 only
        WorldItemEntriesChangedPayload changed = Assert.Single(WorldItemTests.Heads(log, InvEventKinds.WorldItemEntriesChanged)).PayloadAs<WorldItemEntriesChangedPayload>();
        Assert.Equal(item.Value, changed.WorldItemId);
        Assert.Equal(seq + 1, changed.WorldSeq);
        Assert.Equal(entry.Value, changed.EntryId);
        Assert.Equal(2, changed.Count);
        Assert.Empty(WorldItemTests.Heads(log, InvEventKinds.WorldItemDespawned));
        Assert.Equal(tombstones, p.World.Tombstones.Count);
        Assert.Equal(hotbarV + 1, p.R.Header(p.Hotbar(0)).Version);
        Assert.Equal(inventoryV, p.R.Header(p.Inventory(0)).Version);
        Assert.Equal(p.Hotbar(0), p.R.SlotOf(new ContainerId(Assert.Single(WorldItemTests.Heads(log, InvEventKinds.Version)).PayloadAs<InvVersionPayload>().Container)));
    }

    [Fact]
    public void PartialPickup_ThenBInTheSameTick_TakesThe2Left_WritesTheTakenTombstoneByB_NoMailIdDuplicated()
    {
        // Arrange: as the partial case; B's panel is empty
        var p = new PickupRig();
        ushort stack = p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 17));
        p.FillHotbar(0, from: 2);
        p.FillInventory(0);
        ushort letters = p.Letters(101, 5);
        EntityId item = p.Drop(letters);

        // Act: A then B in one tick
        EventEntry[] log = p.PickupBoth(p.A, item, p.B, item);

        // Assert: A got 3, B got the 2 newest (the entry itself, into B's hotbar slot 1); the tombstone names B, taken
        Reply a = p.ReplyFor(log, p.A);
        Reply b = p.ReplyFor(log, p.B);
        Assert.True(a.IsOk);
        Assert.True(b.IsOk);
        Assert.Equal(3, a.ExtraAs<InvReplyExtra>().GotUnits);
        Assert.Equal(2, b.ExtraAs<InvReplyExtra>().GotUnits);
        Assert.Equal(2, b.ExtraAs<InvReplyExtra>().RequestedUnits);
        Assert.Equal(p.Hotbar(1), p.R.Entries[letters].ContainerSlot);
        Assert.Equal(new[] { 104u, 105u }, p.R.IdsOf(letters));
        Assert.False(p.World.Table.TryGetSlot(item, out _));
        Assert.Equal((ushort)InvReason.ContainerGone, p.World.Resolve(item, out _, out uint by, out InvCause cause));
        Assert.Equal(p.B.Value, by);
        Assert.Equal(InvCause.Taken, cause);
        for (uint id = 101; id <= 105; id++)
        {
            Assert.Equal(1, p.CopiesOf(id));
        }

        Assert.Equal(20, p.R.Entries[stack].Count);
        Assert.Single(WorldItemTests.Heads(log, InvEventKinds.WorldItemEntriesChanged));
        Assert.Single(WorldItemTests.Heads(log, InvEventKinds.WorldItemDespawned));
    }

    // ---------------------------------------------------------------- H-R9-2 a-e (catch)

    [Fact]
    [Trait("Id", "H-R9-2")]
    public void HR92a_Caught5LettersTo4_HotbarSlot2Holds10To4_MergedIntoSlot2Making15_InventoryStackUntouched()
    {
        // Arrange
        var p = new PickupRig();
        ushort inv = p.R.PlaceMail(p.Inventory(0), "letter", 0, 0, Address, PickupRig.Ids(1, 3));
        ushort slot2 = p.R.PlaceMail(p.Hotbar(0), "letter", 2, 0, Address, PickupRig.Ids(10, 10));
        ushort flight = p.Letters(101, 5);

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert
        Assert.Equal(new CatchResult(5, 0, 0), r);
        Assert.Equal(15, p.R.Entries[slot2].Count);
        Assert.Equal(3, p.R.Entries[inv].Count);
        Assert.False(p.R.Entries[flight].IsLive); // merged away whole
        Assert.Equal(0, p.World.LiveCount);
    }

    [Fact]
    [Trait("Id", "H-R9-2")]
    public void HR92b_NoHotbarStackTo4_Slots1And3Free_ANewEntryInSlot1_NeverSlot0_KeepingItsId_InventoryStackUntouched()
    {
        // Arrange: slot 2 holds an axe, slots 1 and 3 are free
        var p = new PickupRig();
        ushort inv = p.R.PlaceMail(p.Inventory(0), "letter", 0, 0, Address, PickupRig.Ids(1, 3));
        p.R.PlaceItem(p.Hotbar(0), "axe", 2, 0, 1);
        ushort flight = p.Letters(101, 5);
        EntryId id = p.R.Entries[flight].Id;

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert
        Assert.Equal(new CatchResult(5, 0, 0), r);
        Assert.Equal(p.Hotbar(0), p.R.Entries[flight].ContainerSlot);
        Assert.Equal(1, p.R.Entries[flight].X);
        Assert.Equal(id, p.R.Entries[flight].Id);
        Assert.Equal(3, p.R.Entries[inv].Count);
    }

    [Fact]
    [Trait("Id", "H-R9-2")]
    public void HR92c_HotbarFullWithNoStackTo4_MergedIntoTheInventoryStackMaking8()
    {
        // Arrange
        var p = new PickupRig();
        ushort inv = p.R.PlaceMail(p.Inventory(0), "letter", 0, 0, Address, PickupRig.Ids(1, 3));
        p.FillHotbar(0);
        ushort flight = p.Letters(101, 5);

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert
        Assert.Equal(new CatchResult(5, 0, 0), r);
        Assert.Equal(8, p.R.Entries[inv].Count);
        Assert.Equal(PickupRig.Ids(1, 3).Concat(PickupRig.Ids(101, 5)), p.R.IdsOf(inv));
    }

    [Fact]
    [Trait("Id", "H-R9-2")]
    public void HR92d_HotbarAndInventoryFull_InventoryStack20Of20_CursorEmpty_OneThrownWorldItemAtTheFeet_Expires9000_CursorStaysEmpty_0And5()
    {
        // Arrange
        var p = new PickupRig();
        ushort inv = p.R.PlaceMail(p.Inventory(0), "letter", 0, 0, Address, PickupRig.Ids(1, 20));
        p.FillHotbar(0);
        p.FillInventory(0, from: 1);
        ushort flight = p.Letters(101, 5);
        EntryId id = p.R.Entries[flight].Id;
        uint hotbarV = p.R.Header(p.Hotbar(0)).Version;
        uint inventoryV = p.R.Header(p.Inventory(0)).Version;
        uint tick = p.W.Now;

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert: a fumble {0, 5}; the entry (same id, 5 ids) is one Thrown WorldItem at the feet, expiring at tick + 9000
        Assert.Equal(new CatchResult(0, 5, 0), r);
        ushort ws = p.WorldItemHolding(flight);
        WorldItemRecord w = p.World.Table[ws];
        Assert.Equal(WorldItemOrigin.Thrown, w.Origin);
        Assert.Equal(PickupRig.Feet, w.PosMm);
        Assert.Equal(tick, w.CreatedTick);
        Assert.Equal(tick + 9000, w.ExpiresTick);
        Assert.Equal(id, p.R.Entries[flight].Id);
        Assert.Equal(PickupRig.Ids(101, 5), p.R.IdsOf(flight));
        Assert.Equal(0, p.EntriesIn(p.Cursor(0)));
        Assert.False(p.Inv.TryGetCursorOrigin(PlayerSlot.FromValue(0), out _));
        Assert.Equal(20, p.R.Entries[inv].Count);
        Assert.Equal(hotbarV, p.R.Header(p.Hotbar(0)).Version);
        Assert.Equal(inventoryV, p.R.Header(p.Inventory(0)).Version);
        Assert.Single(WorldItemTests.Heads(p.R.Sink.Last, InvEventKinds.WorldItemSpawned));
        Assert.Empty(WorldItemTests.Heads(p.R.Sink.Last, InvEventKinds.Version));
    }

    [Fact]
    [Trait("Id", "H-R9-2")]
    public void HR92e_RoomFor3_OneHotbarStackAt17_The3OldestMerge_The2NewestBecomeTheWorldItem_3And2()
    {
        // Arrange
        var p = new PickupRig();
        ushort stack = p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 17));
        p.FillHotbar(0, from: 2);
        p.FillInventory(0);
        ushort flight = p.Letters(101, 5);
        EntryId id = p.R.Entries[flight].Id;

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert
        Assert.Equal(new CatchResult(3, 2, 0), r);
        Assert.Equal(20, p.R.Entries[stack].Count);
        Assert.Equal(PickupRig.Ids(1, 17).Concat(new[] { 101u, 102u, 103u }), p.R.IdsOf(stack));
        ushort ws = p.WorldItemHolding(flight);
        Assert.Equal(WorldItemOrigin.Thrown, p.World.Table[ws].Origin);
        Assert.Equal(id, p.R.Entries[flight].Id);
        Assert.Equal(new[] { 104u, 105u }, p.R.IdsOf(flight));
        for (uint m = 101; m <= 105; m++)
        {
            Assert.Equal(1, p.CopiesOf(m));
        }
    }

    // ---------------------------------------------------------------- Axe catch; mail pickup as a-c, no_room instead of d

    [Fact]
    [Trait("Id", "H-R9-2")]
    public void CaughtAxe_WithRoom_GoesToTheHotbarFirst_ThenTheInventoryWhenTheHotbarIsFull()
    {
        // Arrange: two axes in flight; A's hotbar has one free slot (1)
        var p = new PickupRig();
        p.FillHotbar(0, from: 2);
        ushort first = p.R.DetachedItem("axe", 1);
        ushort second = p.R.DetachedItem("axe", 1);

        // Act
        CatchResult r1 = p.Catch(0, first);
        CatchResult r2 = p.Catch(0, second);

        // Assert: category order (Tool: [hotbar, player_inventory], pass-first)
        Assert.Equal(new CatchResult(1, 0, 0), r1);
        Assert.Equal(new CatchResult(1, 0, 0), r2);
        Assert.Equal(p.Hotbar(0), p.R.Entries[first].ContainerSlot);
        Assert.Equal(1, p.R.Entries[first].X);
        Assert.Equal(p.Inventory(0), p.R.Entries[second].ContainerSlot);
        Assert.Equal(0, p.World.LiveCount);
    }

    [Theory]
    [InlineData('a')]
    [InlineData('b')]
    [InlineData('c')]
    [Trait("Id", "H-R9-2")]
    public void MailPickup_BehavesAsCatchAToC(char scenario)
    {
        // Arrange: the a-c panels; a 5-letter #4 WorldItem
        var p = new PickupRig();
        ushort inv = p.R.PlaceMail(p.Inventory(0), "letter", 0, 0, Address, PickupRig.Ids(1, 3));
        ushort slot2 = scenario == 'a' ? p.R.PlaceMail(p.Hotbar(0), "letter", 2, 0, Address, PickupRig.Ids(10, 10)) : (ushort)0;
        if (scenario == 'b')
        {
            p.R.PlaceItem(p.Hotbar(0), "axe", 2, 0, 1);
        }
        else if (scenario == 'c')
        {
            p.FillHotbar(0);
        }

        ushort letters = p.Letters(101, 5);
        EntityId item = p.Drop(letters);

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert: whole pickup, the WorldItem gone
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.True(reply.IsOk);
        Assert.Equal(5, reply.ExtraAs<InvReplyExtra>().GotUnits);
        Assert.False(p.World.Table.TryGetSlot(item, out _));
        switch (scenario)
        {
            case 'a':
                Assert.Equal(15, p.R.Entries[slot2].Count);
                Assert.Equal(3, p.R.Entries[inv].Count);
                break;
            case 'b':
                Assert.Equal(p.Hotbar(0), p.R.Entries[letters].ContainerSlot);
                Assert.Equal(1, p.R.Entries[letters].X);
                Assert.Equal(3, p.R.Entries[inv].Count);
                break;
            default:
                Assert.Equal(8, p.R.Entries[inv].Count);
                break;
        }
    }

    [Fact]
    [Trait("Id", "H-R9-2")]
    public void MailPickup_WithThePanelOfD_AnswersNoRoom_TheItemStays_NothingBumps()
    {
        // Arrange: hotbar and inventory full, the inventory #4 stack at 20/20
        var p = new PickupRig();
        p.R.PlaceMail(p.Inventory(0), "letter", 0, 0, Address, PickupRig.Ids(1, 20));
        p.FillHotbar(0);
        p.FillInventory(0, from: 1);
        ushort letters = p.Letters(101, 5);
        EntityId item = p.Drop(letters);
        Assert.True(p.World.Table.TryGetSlot(item, out ushort ws));
        uint hotbarV = p.R.Header(p.Hotbar(0)).Version;
        uint inventoryV = p.R.Header(p.Inventory(0)).Version;

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert
        Assert.Equal((ushort)InvReason.NoRoom, InvAutoPlaceRig.ReplyOf(log).Reason);
        Assert.True(p.World.Table.TryGetSlot(item, out ushort still));
        Assert.Equal(ws, still);
        Assert.Equal(5, p.R.Entries[letters].Count);
        Assert.Equal(hotbarV, p.R.Header(p.Hotbar(0)).Version);
        Assert.Equal(inventoryV, p.R.Header(p.Inventory(0)).Version);
    }

    // ---------------------------------------------------------------- catch refusals (TD-154 item 7)

    [Fact]
    public void Catch_TableFullAndUnitsWouldFumble_RefusedWhole_CapacityExhausted_0And0_NothingMutated_EntryStaysDetached_OneCheckRecord()
    {
        // Arrange: a 2-slot WorldItem table, both slots live; A's panel full (as d)
        var p = new PickupRig(tune: s => s with { WorldItemCapacity = 2 });
        p.Drop(p.R.DetachedItem("plank", 1));
        p.Drop(p.R.DetachedItem("plank", 1));
        p.R.PlaceMail(p.Inventory(0), "letter", 0, 0, Address, PickupRig.Ids(1, 20));
        p.FillHotbar(0);
        p.FillInventory(0, from: 1);
        ushort flight = p.Letters(101, 5);
        List<long> before = p.Storage();
        SimCheckRing ring = p.R.Host.World.CheckRing;
        int ringBefore = ring.Count;

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert
        Assert.Equal(new CatchResult(0, 0, InvReason.CapacityExhausted), r);
        Assert.Equal(before, p.Storage());
        Assert.True(p.R.Entries[flight].IsLive);
        Assert.Equal(EntryRecord.NoContainer, p.R.Entries[flight].ContainerSlot);
        Assert.False(p.World.OwnsEntry(flight));
        Assert.Equal(ringBefore + 1, ring.Count);
        Assert.Equal((ushort)CheckCode.InventoryWorldItemCapacity, p.LastCheck().Code);
        Assert.DoesNotContain(p.R.Sink.Last, e => e.Kind != EventKind.OpReply && e.Class != ReplicationClass.Internal);
    }

    [Fact]
    public void Catch_TableFull_ButEverythingFits_Catches()
    {
        // Arrange
        var p = new PickupRig(tune: s => s with { WorldItemCapacity = 1 });
        p.Drop(p.R.DetachedItem("plank", 1));
        ushort flight = p.Letters(101, 5);

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert
        Assert.Equal(new CatchResult(5, 0, 0), r);
        Assert.Equal(p.Hotbar(0), p.R.Entries[flight].ContainerSlot);
    }

    [Fact]
    public void Catch_ArenaRefusesTheMailMergeUnionBlock_RefusedWhole_CapacityExhausted_0And0_NothingMutated_EntryStaysDetached_OneArenaRecord()
    {
        // Arrange: a minimum arena; a 10-letter #4 stack on hotbar slot 1; the flight entry of 5; then the arena exhausted, and the
        // arena record's rate-limit window left behind so the catch's record is stored
        var p = new PickupRig(mailIdArenaUnits: MailIdArena.MinTotalUnits);
        ushort stack = p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 10));
        ushort flight = p.Letters(101, 5);
        EntryId id = p.R.Entries[flight].Id;
        p.ExhaustArena();
        SimCheckRing ring = p.R.Host.World.CheckRing;
        p.AdvanceTo(p.W.Now + (uint)ring.RateLimitTicks);
        List<long> before = p.Storage();
        uint hotbarV = p.R.Header(p.Hotbar(0)).Version;
        uint inventoryV = p.R.Header(p.Inventory(0)).Version;
        long writes = ring.Count + (long)ring.Overwritten;

        // Act
        CatchResult r = p.Catch(0, flight);
        uint tick = p.W.Now - 1;

        // Assert: ADR-0009 section 5, nothing mutated and an always-on record: no WorldItem, no worldSeq bump, no event, the entry
        // still live, detached, unowned, with its id and ids
        Assert.Equal(new CatchResult(0, 0, InvReason.CapacityExhausted), r);
        Assert.Equal(before, p.Storage());
        Assert.Equal(10, p.R.Entries[stack].Count);
        Assert.Equal(hotbarV, p.R.Header(p.Hotbar(0)).Version);
        Assert.Equal(inventoryV, p.R.Header(p.Inventory(0)).Version);
        Assert.True(p.R.Entries[flight].IsLive);
        Assert.Equal(EntryRecord.NoContainer, p.R.Entries[flight].ContainerSlot);
        Assert.False(p.World.OwnsEntry(flight));
        Assert.Equal(id, p.R.Entries[flight].Id);
        Assert.Equal(PickupRig.Ids(101, 5), p.R.IdsOf(flight));
        Assert.DoesNotContain(p.R.Sink.Last, PickupRig.IsInventoryRecord);
        Assert.Equal(writes + 1, ring.Count + (long)ring.Overwritten);
        SimCheckRecord record = p.LastCheck();
        Assert.Equal((ushort)CheckCode.InventoryArenaExhausted, record.Code);
        Assert.Equal(tick, record.Tick);
        Assert.Equal(15, record.A); // the union block: 10 + 5 units
        Assert.Equal(MailIdArena.ClassFor(15), record.B);
    }

    /// <summary>
    /// The arena refuses a partial merge plus a fumble (3 merge into a hotbar stack at 17/20, 2 would fumble), with the WorldItem
    /// table free or full: the arena check runs first, so the catch is <c>{0, 0}</c>, <c>capacity_exhausted</c>, nothing mutated, and
    /// the one record is the arena's, never the table's (advisory fix, Mail Review PR #34 r2 advisory 6 a, b).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Catch_ArenaRefusesAPartialMergeWithAFumble_TableFreeOrFull_ArenaWins_0And0_NothingMutated_OneArenaRecord(bool tableFull)
    {
        // Arrange: the H-R9-2 e panel; a 1-slot WorldItem table, live when tableFull; then the arena exhausted and its window left behind
        var p = new PickupRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, tune: s => s with { WorldItemCapacity = 1 });
        p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 17));
        p.FillHotbar(0, from: 2);
        p.FillInventory(0);
        ushort flight = p.Letters(101, 5);
        if (tableFull)
        {
            p.Drop(p.R.DetachedItem("plank", 1));
        }

        p.ExhaustArena();
        SimCheckRing ring = p.R.Host.World.CheckRing;
        p.AdvanceTo(p.W.Now + (uint)ring.RateLimitTicks);
        List<long> before = p.Storage();
        long writes = ring.Count + (long)ring.Overwritten;

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert
        Assert.Equal(new CatchResult(0, 0, InvReason.CapacityExhausted), r);
        Assert.Equal(before, p.Storage());
        Assert.Equal(EntryRecord.NoContainer, p.R.Entries[flight].ContainerSlot);
        Assert.DoesNotContain(p.R.Sink.Last, PickupRig.IsInventoryRecord);
        Assert.Equal(writes + 1, ring.Count + (long)ring.Overwritten);
        SimCheckRecord record = p.LastCheck();
        Assert.Equal((ushort)CheckCode.InventoryArenaExhausted, record.Code);
        Assert.Equal(20, record.A); // the union block: 17 + 3 units
    }

    /// <summary>
    /// With the arena exhausted, a catch that acquires no arena block still succeeds: a letter placed whole into a free cell keeps its
    /// own block, and a plank has none (advisory fix, Mail Review PR #34 r2 advisory 6 c: kills a refusal whenever the arena cannot
    /// reserve one unit).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Catch_ArenaExhausted_ButTheCatchNeedsNoNewBlock_Catches(bool mail)
    {
        // Arrange
        var p = new PickupRig(mailIdArenaUnits: MailIdArena.MinTotalUnits);
        ushort flight = mail ? p.Letters(101, 5) : p.R.DetachedItem("plank", 5);
        p.ExhaustArena();

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert
        Assert.Equal(new CatchResult(5, 0, 0), r);
        Assert.NotEqual(EntryRecord.NoContainer, p.R.Entries[flight].ContainerSlot);
    }

    [Fact]
    [Trait("Id", "H-R12-3")]
    public void Catch_FumbleAt512LivePlanks_EvictsExactlyOne_TheLowestExpiresTickPlank_By013Rules()
    {
        // Arrange: 512 plank WorldItems, one per tick; A's panel full (as d)
        var p = new PickupRig();
        EntityId[] planks = p.DropOnePerTick(10, 512, _ => p.R.DetachedItem("plank", 4));
        p.R.PlaceMail(p.Inventory(0), "letter", 0, 0, Address, PickupRig.Ids(1, 20));
        p.FillHotbar(0);
        p.FillInventory(0, from: 1);
        ushort flight = p.Letters(101, 5);

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert
        Assert.Equal(new CatchResult(0, 5, 0), r);
        Assert.Equal(512, p.World.LiveCount);
        Assert.False(p.World.Table.TryGetSlot(planks[0], out _));
        Assert.True(p.World.Table.TryGetSlot(planks[1], out _));
        Assert.Single(WorldItemTests.Heads(p.R.Sink.Last, InvEventKinds.ItemsLost));
        Assert.Single(WorldItemTests.Heads(p.R.Sink.Last, InvEventKinds.WorldItemDespawned));
        p.WorldItemHolding(flight);
    }

    [Fact]
    [Trait("Id", "H-R12-3")]
    public void Catch_FumbleAtTheCapWithAFullIntake_EvictsTheOldLetterToTheBacklog_NoPlankDestroyed()
    {
        // Arrange (story 021, Q3 ends): an old letter WorldItem + 511 planks, the Intake full, A's panel full
        var p = new PickupRig();
        ushort old = p.R.DetachedMail("letter", Address, new[] { 900u });
        EntityId[] letter = p.DropOnePerTick(10, 1, _ => old);
        EntityId[] planks = p.DropOnePerTick(11, 511, _ => p.R.DetachedItem("plank", 4));
        IntakeSpawnOutfeedTests.FillIntake(p.R, 320, 10_000);
        p.R.PlaceMail(p.Inventory(0), "letter", 0, 0, Address, PickupRig.Ids(1, 20));
        p.FillHotbar(0);
        p.FillInventory(0, from: 1);
        ushort flight = p.Letters(101, 5);

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert: the fumble's WorldItem took the count to 513; the qualifying letter went to the backlog tail, no plank was destroyed
        Assert.Equal(new CatchResult(0, 5, 0), r);
        Assert.Equal(512, p.World.LiveCount);
        Assert.False(p.World.Table.TryGetSlot(letter[0], out _));
        Assert.True(p.World.Table.TryGetSlot(planks[0], out _));
        Assert.Empty(WorldItemTests.Heads(p.R.Sink.Last, InvEventKinds.ItemsLost));
        Assert.Single(WorldItemTests.Heads(p.R.Sink.Last, InvEventKinds.BacklogUnits));
        Assert.True(p.R.Inventory.Backlog.Contains(old));
        p.WorldItemHolding(flight);
    }

    // The four malformed calls run in one test, each on a fresh rig; every failing call is reported in one message.
    [Fact]
    public void Catch_MalformedCall_IsBadRequest_NothingChanged()
    {
        string[] kinds = { "detached-in-world", "placed", "dead", "no-panel" };
        var failures = new List<string>();
        for (int i = 0; i < kinds.Length; i++)
        {
            string kind = kinds[i];
            try
            {
                // Arrange
                var p = new PickupRig();
                ushort entry = kind == "placed" ? p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 2)) : p.Letters(101, 2);
                if (kind == "detached-in-world")
                {
                    p.Drop(entry);
                }

                EntryId id = p.R.Entries[entry].Id;
                if (kind == "dead")
                {
                    p.R.FreeDetached(entry);
                }

                List<long> before = p.Storage();
                byte slot = kind == "no-panel" ? (byte)5 : (byte)0;

                // Act
                CatchResult r = p.InTick(() => p.Inv.TryCatch(PlayerSlot.FromValue(slot), new FlightPayload(id, PickupRig.Feet, false)));

                // Assert (collected)
                if (!r.Equals(new CatchResult(0, 0, (InvReason)InvReasons.BadRequest)))
                {
                    failures.Add($"row {i} ({kind}): expected bad_request {{0, 0}}, got {r}");
                }

                if (!before.SequenceEqual(p.Storage()))
                {
                    failures.Add($"row {i} ({kind}): Storage() changed");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"row {i} ({kind}): {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.Equal(4, kinds.Length);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---------------------------------------------------------------- floating item and entryId (M-1a form)

    [Fact]
    public void FloatingWorldItem_IsRefusedByTheCore_WithTheFloatingVerdict_NoReason_NothingMutated_NothingRaised()
    {
        // Arrange
        var p = new PickupRig();
        ushort letters = p.Letters(101, 5);
        EntityId item = p.Drop(letters, onWater: true);
        List<long> before = p.Storage();

        // Act
        PickupResult r = p.Core(p.A, item, 0, out InvReplyExtra extra);
        EventEntry[] log = p.R.Sink.Last;

        // Assert
        Assert.Equal(PickupVerdict.Floating, r.Verdict);
        Assert.Equal(0, r.Reason);
        Assert.Equal(0, r.Got);
        Assert.Equal(before, p.Storage());
        Assert.DoesNotContain(log, PickupRig.IsInventoryRecord);
        Assert.Equal(0, extra.GotUnits);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("own")]
    public void EntryId_ZeroOrTheWorldItemsOwnEntry_ThePickupProceeds(string form)
    {
        // Arrange
        var p = new PickupRig();
        ushort letters = p.Letters(101, 5);
        EntityId item = p.Drop(letters);
        uint entry = form == "own" ? p.R.Entries[letters].Id.Value : 0u;

        // Act
        EventEntry[] log = p.Pickup(p.A, item, entry);

        // Assert
        Assert.True(InvAutoPlaceRig.ReplyOf(log).IsOk);
        Assert.Equal(p.Hotbar(0), p.R.Entries[letters].ContainerSlot);
    }

    [Theory]
    [InlineData("live-elsewhere", (ushort)InvReason.StaleEntry)]
    [InlineData("dead", (ushort)InvReason.StaleEntry)]
    [InlineData("above-high-water", CoreReason.BadRequest)]
    [InlineData("other-slot-temp", CoreReason.BadRequest)]
    public void EntryId_NotTheWorldItemsEntry_StaleEntry_ButNamespaceAndHighWaterFailuresStayBadRequest_NothingMutated(string form, ushort expected)
    {
        // Arrange: a live entry in A's hotbar, a freed entry, the WorldItem
        var p = new PickupRig();
        ushort other = p.R.PlaceMail(p.Hotbar(0), "letter", 3, 0, Address, PickupRig.Ids(1, 2));
        ushort dead = p.Letters(50, 1);
        uint deadId = p.R.Entries[dead].Id.Value;
        p.R.FreeDetached(dead);
        ushort letters = p.Letters(101, 5);
        EntityId item = p.Drop(letters);
        uint entry = form switch
        {
            "live-elsewhere" => p.R.Entries[other].Id.Value,
            "dead" => deadId,
            "above-high-water" => p.R.Entries.LastId + 10,
            _ => EntryId.Temp(PlayerSlot.FromValue(1), 1).Value,
        };
        List<long> before = p.Storage();

        // Act
        EventEntry[] log = p.Pickup(p.A, item, entry);

        // Assert
        Assert.Equal(expected, InvAutoPlaceRig.ReplyOf(log).Reason);
        Assert.Equal(before, p.Storage());
        Assert.DoesNotContain(log, PickupRig.IsInventoryRecord);
    }

    // ---------------------------------------------------------------- tombstones (Review advisory A5)

    [Fact]
    public void Tombstone_LetterExpiredToTheIntakeAtT_AtTPlus900ContainerGoneBySystemExpired_AtTPlus901StaleEntry_NothingMutated()
    {
        // Arrange: a letter dropped at 100 expires to the Intake at T = 9100
        var p = new PickupRig();
        EntityId item = p.W.Create(100, p.Letters(101, 5));
        const uint T = 9_100;
        p.AdvanceTo(T + 1);
        Assert.False(p.World.Table.TryGetSlot(item, out _));

        // Act + Assert
        p.AssertTombstoneAnswers(item, T, ActorId.System.Value, InvCause.Expired);
    }

    [Fact]
    public void Tombstone_PlankDestroyedByTheWaterFloatAtT_AtTPlus900ContainerGoneBySystemDestroyed_AtTPlus901StaleEntry()
    {
        // Arrange: a plank landing on water at 100 is destroyed at T = 400
        var p = new PickupRig();
        EntityId item = p.W.Create(100, p.R.DetachedItem("plank", 3), WorldItemOrigin.Thrown, onWater: true);
        const uint T = 400;
        p.AdvanceTo(T + 1);
        Assert.False(p.World.Table.TryGetSlot(item, out _));

        // Act + Assert
        p.AssertTombstoneAnswers(item, T, ActorId.System.Value, InvCause.Destroyed);
    }

    [Fact]
    public void Tombstone_PickedUpByAAtT_BAtTPlus900ContainerGoneByATaken_AtTPlus901StaleEntry()
    {
        // Arrange
        var p = new PickupRig();
        EntityId item = p.Drop(p.Letters(101, 5));
        uint t = p.W.Now;
        Assert.True(InvAutoPlaceRig.ReplyOf(p.Pickup(p.A, item)).IsOk);

        // Act + Assert (B asks)
        p.AssertTombstoneAnswers(item, t, p.A.Value, InvCause.Taken, p.B);
    }

    // ---------------------------------------------------------------- rejects raise nothing (TD-105 item 5)

    [Theory]
    [InlineData("bad_request")]
    [InlineData("container_gone")]
    [InlineData("stale_entry")]
    [InlineData("floating")]
    [InlineData("no_room")]
    [InlineData("capacity_exhausted")]
    public void RefusedPickup_LeavesNoRecordAndNoSeal_VersionsUnchanged(string refusal)
    {
        // Arrange
        var p = new PickupRig(mailIdArenaUnits: refusal == "capacity_exhausted" ? MailIdArena.MinTotalUnits : null);
        EntityId item = refusal switch
        {
            "bad_request" => EntityId.Compose(EntityClass.WorldItem, 999),
            "container_gone" or "stale_entry" => p.Drop(p.Letters(101, 1)),
            "floating" => p.Drop(p.Letters(101, 1), onWater: true),
            _ => default,
        };
        if (refusal == "container_gone" || refusal == "stale_entry")
        {
            Assert.True(InvAutoPlaceRig.ReplyOf(p.Pickup(p.B, item)).IsOk);
            if (refusal == "stale_entry")
            {
                p.AdvanceTo(p.W.Now + Tombstones.LifetimeTicks + 1);
            }
        }
        else if (refusal == "no_room")
        {
            p.FillHotbar(0);
            p.FillInventory(0);
            item = p.Drop(p.Letters(101, 1));
        }
        else if (refusal == "capacity_exhausted")
        {
            p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 10));
            ushort letters = p.Letters(101, 5);
            item = p.Drop(letters);
            p.ExhaustArena();
        }

        List<long> before = p.Storage();
        long cursorV = p.R.Header(p.Cursor(0)).Version;

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.False(reply.IsOk);
        Assert.Equal(refusal == "floating" ? CoreReason.BadRequest : PickupRig.ReasonOf(refusal), reply.Reason); // TD-155 item 5
        Assert.Equal(refusal == "floating" ? PickupVerdict.Floating : PickupVerdict.Refused, p.Handler.Last.Verdict);
        Assert.Equal(PickupRig.ReasonOf(refusal), p.Handler.Last.Reason);
        Assert.DoesNotContain(log, PickupRig.IsInventoryRecord);
        Assert.Equal(before, p.Storage());
        Assert.Equal(cursorV, p.R.Header(p.Cursor(0)).Version);
    }

    [Fact]
    public void RefusedCatch_BadRequestOrTableFull_LeavesNoRecordAndNoSeal()
    {
        // Arrange: a 1-slot table, live; A's panel full
        var p = new PickupRig(tune: s => s with { WorldItemCapacity = 1 });
        p.Drop(p.R.DetachedItem("plank", 1));
        p.FillHotbar(0);
        p.FillInventory(0);
        ushort flight = p.Letters(101, 5);
        ushort placed = p.R.PlaceMail(p.Hotbar(1), "letter", 1, 0, Address, PickupRig.Ids(1, 2));

        // Act
        CatchResult full = p.Catch(0, flight);
        EventEntry[] fullLog = p.R.Sink.Last;
        CatchResult bad = p.Catch(0, placed);
        EventEntry[] badLog = p.R.Sink.Last;

        // Assert
        Assert.Equal(InvReason.CapacityExhausted, full.Reason);
        Assert.Equal((InvReason)InvReasons.BadRequest, bad.Reason);
        Assert.DoesNotContain(fullLog, PickupRig.IsInventoryRecord);
        Assert.DoesNotContain(badLog, PickupRig.IsInventoryRecord);
    }

    // ---------------------------------------------------------------- precedence (ADR-0009 Implementation Guidelines l.226)

    /// <summary>
    /// The core chain (TD-154 item 5, Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT; advisory fix, Mail Review
    /// PR #38 r1) answers the earliest step when a request violates it and every later step that can co-occur, on slots 0 and 1, through
    /// the test handler and through <see cref="PickupCore.Apply"/> directly (which also reaches the core's own namespace check, shadowed
    /// in the handler by <c>CheckRequest</c>). Every row but
    /// <c>capacity_exhausted</c> has the asker's panel full (<c>no_room</c>) and the arena exhausted (a refused plan reserves nothing, so
    /// <c>no_room</c> and <c>capacity_exhausted</c> cannot both answer; the exhausted arena kills a reservation checked before the plan):
    /// <c>bad_request</c> (the other slot's temp entry id on a live-tombstoned WorldItem); <c>container_gone</c> (a live tombstone, a
    /// foreign live <c>entryId</c>); <c>stale_entry</c> (a foreign live <c>entryId</c> on a floating WorldItem); floating; <c>no_room</c>;
    /// <c>capacity_exhausted</c> (a letter merge). The handler replies <c>bad_request</c> for the floating verdict (TD-155 item 5).
    /// </summary>
    [Theory]
    [InlineData("bad_request", 0)]
    [InlineData("bad_request", 1)]
    [InlineData("container_gone", 0)]
    [InlineData("container_gone", 1)]
    [InlineData("stale_entry", 0)]
    [InlineData("stale_entry", 1)]
    [InlineData("floating", 0)]
    [InlineData("floating", 1)]
    [InlineData("no_room", 0)]
    [InlineData("no_room", 1)]
    [InlineData("capacity_exhausted", 0)]
    [InlineData("capacity_exhausted", 1)]
    public void Pickup_Precedence_RequestViolatingThisStepAndEveryLaterOne_ReturnsThisStep(string row, int slot)
    {
        // Arrange
        var p = new PickupRig(mailIdArenaUnits: MailIdArena.MinTotalUnits);
        ActorId asker = slot == 0 ? p.A : p.B;
        ActorId other = slot == 0 ? p.B : p.A;
        EntityId item = p.Drop(p.Letters(101, 5), onWater: row is "stale_entry" or "floating");
        if (row is "bad_request" or "container_gone")
        {
            Assert.True(InvAutoPlaceRig.ReplyOf(p.Pickup(other, item)).IsOk);
        }

        uint foreign;
        if (row == "capacity_exhausted")
        {
            foreign = p.R.Entries[p.R.PlaceMail(p.Hotbar(slot), "letter", 1, 0, Address, PickupRig.Ids(1, 10))].Id.Value;
        }
        else
        {
            foreign = p.R.Entries[p.R.PlaceItem(p.Hotbar(slot), "axe", 1, 0, 1)].Id.Value;
            p.FillHotbar(slot, from: 2);
            p.FillInventory(slot);
        }

        p.ExhaustArena();
        uint entry = row switch
        {
            "bad_request" => EntryId.Temp(PlayerSlot.FromValue((byte)(1 - slot)), 1).Value,
            "container_gone" or "stale_entry" => foreign,
            _ => 0u,
        };
        PickupVerdict verdict = row == "floating" ? PickupVerdict.Floating : PickupVerdict.Refused;
        ushort reason = PickupRig.ReasonOf(row);
        List<long> before = p.Storage();

        // Act
        EventEntry[] log = p.Pickup(asker, item, entry);
        PickupResult handled = p.Handler.Last;
        PickupResult core = p.Core(asker, item, entry, out _);
        EventEntry[] coreLog = p.R.Sink.Last;

        // Assert
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.False(reply.IsOk);
        Assert.Equal(row == "floating" ? CoreReason.BadRequest : reason, reply.Reason);
        Assert.Equal(verdict, handled.Verdict);
        Assert.Equal(verdict, core.Verdict);

        // The floating row's reason is the constant 0 PickupCore writes for Floating (0 == 0), so the verdicts above pin that row
        // (advisory fix, Mail Review PR #34 r2 advisory 6 g)
        if (verdict == PickupVerdict.Refused)
        {
            Assert.Equal(reason, handled.Reason);
            Assert.Equal(reason, core.Reason);
        }
        if (row == "container_gone")
        {
            Assert.Equal(other.Value, reply.ExtraAs<InvReplyExtra>().ByActorId);
            Assert.Equal(other.Value, core.ByActorId);
        }

        Assert.DoesNotContain(log, PickupRig.IsInventoryRecord);
        Assert.DoesNotContain(coreLog, PickupRig.IsInventoryRecord);
        Assert.Equal(before, p.Storage());
    }

    // ---------------------------------------------------------------- Review r1 advisories (test gaps)

    /// <summary>
    /// TD-155 item 2 (Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT; advisory fix, Mail Review PR #38 r1): a
    /// pickup reply names the picker's hotbar as <c>ResultVersionA</c> and player_inventory as <c>ResultVersionB</c>. A pickup split
    /// across both (3 merge into a hotbar stack at 17/20, the hotbar otherwise full; 2 into a player_inventory stack), the two versions
    /// made different first: two seals, each +1, both before the reply.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SplitPickup_IntoHotbarAndPlayerInventory_TwoSealsBeforeTheReply_ResultVersionAIsTheHotbar_BThePlayerInventory(int slot)
    {
        // Arrange
        var p = new PickupRig();
        ActorId picker = slot == 0 ? p.A : p.B;
        ushort hotbar = p.Hotbar(slot);
        ushort inventory = p.Inventory(slot);
        ushort hotbarStack = p.R.PlaceMail(hotbar, "letter", 1, 0, Address, PickupRig.Ids(1, 17));
        p.FillHotbar(slot, from: 2);
        ushort inventoryStack = p.R.PlaceMail(inventory, "letter", 0, 0, Address, PickupRig.Ids(50, 3));
        EntityId item = p.Drop(p.Letters(101, 5));
        uint hotbarV = p.R.Header(hotbar).Version;
        uint inventoryV = p.R.Header(inventory).Version;
        Assert.NotEqual(hotbarV, inventoryV);

        // Act
        EventEntry[] log = p.Pickup(picker, item);

        // Assert
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.True(reply.IsOk);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((hotbarV + 1, inventoryV + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal((5, 5), ((int)extra.RequestedUnits, (int)extra.GotUnits));
        Assert.Equal((20, 5), ((int)p.R.Entries[hotbarStack].Count, (int)p.R.Entries[inventoryStack].Count));
        Assert.Equal(hotbarV + 1, p.R.Header(hotbar).Version);
        Assert.Equal(inventoryV + 1, p.R.Header(inventory).Version);
        foreach (ushort c in new[] { hotbar, inventory })
        {
            InvVersionPayload seal = PickupRig.Seal(log, p.R.IdOf(c));
            Assert.Equal(seal.VersionFrom + 1, seal.VersionTo);
            Assert.Equal(picker.Value, seal.ActorId);
            Assert.True(PickupRig.ReplyIndex(log) > PickupRig.SealIndex(log, p.R.IdOf(c)));
        }

        Assert.Equal(2, WorldItemTests.Heads(log, InvEventKinds.Version).Length);
    }

    /// <summary>
    /// The single-container and refused twins of the test above (advisory fix, Mail Review PR #34 r2 advisory 6 e). <c>TryCatch</c>
    /// returns a <see cref="CatchResult"/> with no result versions, so these are pickup replies: a pickup into the hotbar alone names
    /// the sealed hotbar as A and the untouched player_inventory as B; a <c>no_room</c> reply names both unchanged versions.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pickup_IntoTheHotbarAlone_OrRefused_ResultVersionAIsTheHotbar_BThePlayerInventory(bool refused)
    {
        // Arrange: the two versions made different first; for the refusal, a full panel
        var p = new PickupRig();
        ushort hotbar = p.Hotbar(0);
        ushort inventory = p.Inventory(0);
        p.R.PlaceMail(inventory, "letter", 0, 0, Address, PickupRig.Ids(1, 20));
        if (refused)
        {
            p.FillHotbar(0);
            p.FillInventory(0, from: 1);
        }

        EntityId item = p.Drop(p.Letters(101, 5));
        uint hotbarV = p.R.Header(hotbar).Version;
        uint inventoryV = p.R.Header(inventory).Version;
        Assert.NotEqual(hotbarV, inventoryV);

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.Equal(refused ? (ushort)InvReason.NoRoom : (ushort)0, reply.Reason);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((refused ? hotbarV : hotbarV + 1, inventoryV), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(refused ? 0 : 1, WorldItemTests.Heads(log, InvEventKinds.Version).Length);
    }

    [Fact]
    public void TryCatchAndPickup_OutsideATickPhase_AreRefused_CountMutatingApiOutOfPhase_NothingMutated()
    {
        // Arrange: between ticks, a detached flight entry and a WorldItem
        var p = new PickupRig();
        EntryId flight = p.R.Entries[p.Letters(101, 5)].Id;
        EntityId item = p.Drop(p.Letters(201, 2));
        List<long> before = p.Storage();
        long outOfPhase = p.R.Counters[CounterId.MutatingApiOutOfPhase];
        Func<CatchResult> doCatch = () => p.Inv.TryCatch(PlayerSlot.FromValue(0), new FlightPayload(flight, PickupRig.Feet, false));
        Func<EntityId, PickupResult> doPickup = id =>
        {
            var extra = new InvReplyExtra();
            return p.Inv.Pickup.Apply(p.A, id, 0, ref extra);
        };
        EntityId never = EntityId.Compose(EntityClass.WorldItem, 999); // refused in Validate, so only the core's own guard can fire

        // Act + Assert: SIM_ASSERT builds assert; Release refuses with bad_request
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.MutatingApiOutOfPhase, Assert.Throws<SimAssertException>(() => doCatch()).Code);
            Assert.Equal(SimAssertCode.MutatingApiOutOfPhase, Assert.Throws<SimAssertException>(() => doPickup(item)).Code);
            Assert.Equal(SimAssertCode.MutatingApiOutOfPhase, Assert.Throws<SimAssertException>(() => doPickup(never)).Code);
        }
        else
        {
            Assert.Equal(new CatchResult(0, 0, (InvReason)InvReasons.BadRequest), doCatch());
            Assert.Equal(PickupResult.Refuse(CoreReason.BadRequest), doPickup(item));
            Assert.Equal(PickupResult.Refuse(CoreReason.BadRequest), doPickup(never));
        }

        Assert.Equal(outOfPhase + 3, p.R.Counters[CounterId.MutatingApiOutOfPhase]);
        Assert.Equal(before, p.Storage());
    }

    [Fact]
    public void Catch_WholeFumbleOnWater_TheThrownWorldItemFloats_ExpiringAfterTheWaterFloat300Ticks()
    {
        // Arrange: A's panel full (as H-R9-2 d)
        var p = new PickupRig();
        p.R.PlaceMail(p.Inventory(0), "letter", 0, 0, Address, PickupRig.Ids(1, 20));
        p.FillHotbar(0);
        p.FillInventory(0, from: 1);
        ushort flight = p.Letters(101, 5);

        // Act
        CatchResult r = p.Catch(0, flight, onWater: true);
        uint tick = p.W.Now - 1;

        // Assert
        Assert.Equal(new CatchResult(0, 5, 0), r);
        ushort ws = p.WorldItemHolding(flight);
        Assert.True(p.World.Table[ws].OnWater);
        Assert.Equal(WorldItemOrigin.Thrown, p.World.Table[ws].Origin);
        Assert.Equal(tick + 300, p.World.Table[ws].ExpiresTick);
    }

    /// <summary>
    /// GDD R8 l.118 (TD-154 item 1): a non-mail pickup runs <see cref="TargetOrder.CursorRow"/> pass-first, so a plank (Material:
    /// <c>[player_inventory, hotbar]</c>) merges into a plank stack in the second target before it takes a free cell in the first; a
    /// target-first plan would put it in the empty player_inventory.
    /// </summary>
    [Fact]
    public void NonMailPickup_RunsPassFirst_MergesIntoTheSecondTargetsStack_BeforeAFreeCellOfTheFirst()
    {
        // Arrange
        var p = new PickupRig();
        ushort stack = p.R.PlaceItem(p.Hotbar(0), "plank", 1, 0, 5);
        EntityId item = p.Drop(p.R.DetachedItem("plank", 3));
        uint inventoryV = p.R.Header(p.Inventory(0)).Version;

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert
        Assert.True(InvAutoPlaceRig.ReplyOf(log).IsOk);
        Assert.Equal(8, p.R.Entries[stack].Count);
        Assert.Equal(0, p.EntriesIn(p.Inventory(0)));
        Assert.Equal(inventoryV, p.R.Header(p.Inventory(0)).Version);
    }

    /// <summary>The catch twin of the test above: a plank catch runs pass-first too (advisory fix, Mail Review PR #34 r2 advisory 6 d).</summary>
    [Fact]
    public void NonMailCatch_RunsPassFirst_MergesIntoTheSecondTargetsStack_BeforeAFreeCellOfTheFirst()
    {
        // Arrange
        var p = new PickupRig();
        ushort stack = p.R.PlaceItem(p.Hotbar(0), "plank", 1, 0, 5);
        ushort flight = p.R.DetachedItem("plank", 3);
        uint inventoryV = p.R.Header(p.Inventory(0)).Version;

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert
        Assert.Equal(new CatchResult(3, 0, 0), r);
        Assert.Equal(8, p.R.Entries[stack].Count);
        Assert.Equal(0, p.EntriesIn(p.Inventory(0)));
        Assert.Equal(inventoryV, p.R.Header(p.Inventory(0)).Version);
    }

    /// <summary>
    /// The negative-fit cache after a refusal decided outside Validate: a <c>no_room</c> pickup and a whole <c>{0, N}</c> fumble record
    /// the refused AutoPlace's no-fit fact at the unchanged version, so a repeat probes nothing. A Medium (2 x 2) letter against a
    /// player_inventory whose free cells are its odd columns only (no 2 x 2 hole) and a full hotbar.
    /// </summary>
    [Theory]
    [InlineData("pickup")]
    [InlineData("catch")]
    public void NoRoomPickup_AndWholeFumble_RecordTheNoFitFact_AtTheUnchangedVersion(string call)
    {
        // Arrange
        var p = new PickupRig();
        p.FillHotbar(0);
        ushort inventory = p.Inventory(0);
        for (int x = 0; x < p.R.Header(inventory).W; x += 2)
        {
            for (int y = 0; y < p.R.Header(inventory).H; y++)
            {
                p.R.PlaceItem(inventory, "plank", x, y, 20);
            }
        }

        ushort medium = p.R.DetachedMail("medium", Address, PickupRig.Ids(101, 1));
        EntityId item = call == "pickup" ? p.Drop(medium) : default;
        uint inventoryV = p.R.Header(inventory).Version;
        Assert.False(p.KnownNoFit(inventory, 2, 2));

        // Act
        if (call == "pickup")
        {
            Assert.Equal((ushort)InvReason.NoRoom, InvAutoPlaceRig.ReplyOf(p.Pickup(p.A, item)).Reason);
        }
        else
        {
            Assert.Equal(new CatchResult(0, 1, 0), p.Catch(0, medium));
        }

        // Assert
        Assert.Equal(inventoryV, p.R.Header(inventory).Version);
        Assert.True(p.KnownNoFit(inventory, 2, 2));
    }

    /// <summary>
    /// Every <c>TryCatch</c> is its own call scope (<c>CommitCatch</c>'s <c>BeginCall</c>), even when one in-tick caller makes two:
    /// each catch's panel record and seal share one <c>CallSeq</c>, and the two catches' differ.
    /// </summary>
    [Fact]
    public void TwoCatchesByOneInTickCaller_EachIsOneCallScope_ItsRecordAndSealShareIt()
    {
        // Arrange
        var p = new PickupRig();
        EntryId a = p.R.Entries[p.Letters(101, 5)].Id;
        EntryId b = p.R.Entries[p.Letters(201, 5)].Id;

        // Act
        (CatchResult ra, CatchResult rb) = p.InTick(() => (
            p.Inv.TryCatch(PlayerSlot.FromValue(0), new FlightPayload(a, PickupRig.Feet, false)),
            p.Inv.TryCatch(PlayerSlot.FromValue(1), new FlightPayload(b, PickupRig.Feet, false))));
        EventEntry[] log = p.R.Sink.Last;

        // Assert
        Assert.Equal(new CatchResult(5, 0, 0), ra);
        Assert.Equal(new CatchResult(5, 0, 0), rb);
        EventEntry[] inserted = WorldItemTests.Heads(log, InvEventKinds.Inserted);
        EventEntry[] seals = WorldItemTests.Heads(log, InvEventKinds.Version);
        Assert.Equal(2, inserted.Length);
        Assert.Equal(2, seals.Length);
        Assert.NotEqual(inserted[0].CallSeq, inserted[1].CallSeq);
        Assert.Equal(inserted[0].CallSeq, seals[0].CallSeq);
        Assert.Equal(inserted[1].CallSeq, seals[1].CallSeq);
    }

    /// <summary>GDD edge case l.363: a pickup that arrives on the WorldItem's expiry tick wins (the op drain, phase 0, runs before phase 1 <c>ItemsExpire</c>).</summary>
    [Fact]
    public void Pickup_OnTheItemsExpiryTick_Wins_TheLetterIsTaken_NotReturnedToTheIntake()
    {
        // Arrange: a letter dropped at 100 expires at 9100; the next tick to run is 9100
        var p = new PickupRig();
        ushort letters = p.Letters(101, 5);
        EntityId item = p.W.Create(100, letters);
        p.AdvanceTo(9_100);
        Assert.True(p.World.Table.TryGetSlot(item, out ushort ws));
        Assert.Equal(9_100u, p.World.Table[ws].ExpiresTick);

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert
        Assert.True(InvAutoPlaceRig.ReplyOf(log).IsOk);
        Assert.Equal(p.Hotbar(0), p.R.Entries[letters].ContainerSlot);
        Assert.Equal(PickupRig.Ids(101, 5), p.R.IdsOf(letters));
        Assert.Equal((ushort)InvReason.ContainerGone, p.World.Resolve(item, out _, out uint by, out InvCause cause));
        Assert.Equal((p.A.Value, InvCause.Taken), (by, cause));
    }

    // ---------------------------------------------------------------- versions, events, weight, indexes

    [Fact]
    public void WholePickup_IntoAnEmptyPanel_WorldRecordFirst_ThenInserted_ThenOneHotbarSeal_ActorThePicker_CauseNone_PRisesByClassPts()
    {
        // Arrange
        var p = new PickupRig();
        EntityId item = p.Drop(p.Letters(101, 5));
        uint hotbarV = p.R.Header(p.Hotbar(0)).Version;
        uint inventoryV = p.R.Header(p.Inventory(0)).Version;
        short weight = p.Inv.WeightPoints(PlayerSlot.FromValue(0));

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert: the reply is ok and follows the seal (ADR-0009 section 4 rule 5)
        Assert.True(InvAutoPlaceRig.ReplyOf(log).IsOk);
        Assert.True(PickupRig.ReplyIndex(log) > PickupRig.SealIndex(log, p.R.IdOf(p.Hotbar(0))));
        Assert.Equal(new[] { InvEventKinds.WorldItemDespawned, InvEventKinds.Inserted, InvEventKinds.Version }, PickupRig.InventoryKinds(log));
        InvVersionPayload seal = PickupRig.Seal(log, p.R.IdOf(p.Hotbar(0)));
        Assert.Equal(hotbarV, seal.VersionFrom);
        Assert.Equal(hotbarV + 1, seal.VersionTo);
        Assert.Equal(p.A.Value, seal.ActorId);
        Assert.Equal((byte)InvCause.None, seal.Cause);
        Assert.Equal(hotbarV + 1, p.R.Header(p.Hotbar(0)).Version);
        Assert.Equal(inventoryV, p.R.Header(p.Inventory(0)).Version);
        Assert.Equal(weight + p.Inv.Settings.WeightPointsLight, p.Inv.WeightPoints(PlayerSlot.FromValue(0)));
    }

    [Fact]
    public void PartialPickup_EntriesChangedFirst_ThenMerged_ThenTheHotbarSeal_PUnchanged()
    {
        // Arrange
        var p = new PickupRig();
        p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 17));
        p.FillHotbar(0, from: 2);
        p.FillInventory(0);
        EntityId item = p.Drop(p.Letters(101, 5));
        short weight = p.Inv.WeightPoints(PlayerSlot.FromValue(0));

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert: the reply is ok and follows the seal (ADR-0009 section 4 rule 5)
        Assert.True(InvAutoPlaceRig.ReplyOf(log).IsOk);
        Assert.True(PickupRig.ReplyIndex(log) > PickupRig.SealIndex(log, p.R.IdOf(p.Hotbar(0))));
        Assert.Equal(new[] { InvEventKinds.WorldItemEntriesChanged, InvEventKinds.Merged, InvEventKinds.Version }, PickupRig.InventoryKinds(log));
        Assert.Equal(weight, p.Inv.WeightPoints(PlayerSlot.FromValue(0)));
    }

    [Fact]
    public void FumblingCatch_PanelRecordsAndSealFirst_ThenWorldItemSpawnedInANestedCallScope_PUnchangedByMergeAndFumble()
    {
        // Arrange: the H-R9-2 e panel
        var p = new PickupRig();
        p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 17));
        p.FillHotbar(0, from: 2);
        p.FillInventory(0);
        ushort flight = p.Letters(101, 5);
        short weight = p.Inv.WeightPoints(PlayerSlot.FromValue(0));
        uint hotbarV = p.R.Header(p.Hotbar(0)).Version;

        // Act
        CatchResult r = p.Catch(0, flight);
        EventEntry[] log = p.R.Sink.Last;

        // Assert
        Assert.Equal(new CatchResult(3, 2, 0), r);
        Assert.Equal(new[] { InvEventKinds.Merged, InvEventKinds.Version, InvEventKinds.WorldItemSpawned }, PickupRig.InventoryKinds(log));
        EventEntry merged = Assert.Single(WorldItemTests.Heads(log, InvEventKinds.Merged));
        EventEntry spawned = Assert.Single(WorldItemTests.Heads(log, InvEventKinds.WorldItemSpawned));
        // Weak: a sibling call scope also passes != and >; a real nested-scope assert needs the capture to expose the parent scope
        // (TD-165; advisory fix, Mail Review PR #38 r1 advisory 6)
        Assert.NotEqual(merged.CallSeq, spawned.CallSeq);
        Assert.True(spawned.CallSeq > merged.CallSeq);
        InvVersionPayload seal = PickupRig.Seal(log, p.R.IdOf(p.Hotbar(0)));
        Assert.Equal(p.A.Value, seal.ActorId);
        Assert.Equal((byte)InvCause.None, seal.Cause);
        Assert.Equal(hotbarV + 1, p.R.Header(p.Hotbar(0)).Version);
        Assert.Equal(weight, p.Inv.WeightPoints(PlayerSlot.FromValue(0)));
    }

    [Fact]
    public void CatchPlacingANewEntry_RaisesInsertedAndOneSeal_PRisesByClassPts()
    {
        // Arrange
        var p = new PickupRig();
        ushort flight = p.Letters(101, 5);
        short weight = p.Inv.WeightPoints(PlayerSlot.FromValue(0));

        // Act
        CatchResult r = p.Catch(0, flight);
        EventEntry[] log = p.R.Sink.Last;

        // Assert
        Assert.Equal(new CatchResult(5, 0, 0), r);
        Assert.Equal(new[] { InvEventKinds.Inserted, InvEventKinds.Version }, PickupRig.InventoryKinds(log));
        Assert.Equal(weight + p.Inv.Settings.WeightPointsLight, p.Inv.WeightPoints(PlayerSlot.FromValue(0)));
    }

    [Fact]
    public void AxePickup_CountItemAndTheStackKeyIndexEqualARecount()
    {
        // Arrange: planks in A's inventory (a non-full stack in the index), an axe WorldItem, a mail stack to merge later
        var p = new PickupRig();
        p.R.PlaceItem(p.Inventory(0), "plank", 0, 0, 7);
        p.R.PlaceMail(p.Hotbar(0), "letter", 2, 0, Address, PickupRig.Ids(1, 4));
        EntityId axe = p.Drop(p.R.DetachedItem("axe", 1));
        EntityId letters = p.Drop(p.Letters(101, 3));

        // Act
        Assert.True(InvAutoPlaceRig.ReplyOf(p.Pickup(p.A, axe)).IsOk);
        Assert.True(InvAutoPlaceRig.ReplyOf(p.Pickup(p.A, letters)).IsOk);

        // Assert: CountItem over the panel equals a scan, and the index equals a rebuild
        ContainerId[] panel = { p.R.IdOf(p.Hotbar(0)), p.R.IdOf(p.Inventory(0)) };
        Assert.Equal(1, p.Inv.CountItem(panel, p.R.ItemIndex("axe")));
        Assert.Equal(p.ScanCount(p.R.ItemIndex("axe")), p.Inv.CountItem(panel, p.R.ItemIndex("axe")));
        Assert.Equal(p.ScanCount(p.R.ItemIndex("plank")), p.Inv.CountItem(panel, p.R.ItemIndex("plank")));
        List<ushort> before = p.IndexOf(p.Hotbar(0)).Concat(p.IndexOf(p.Inventory(0))).ToList();
        p.Inv.Mutator.RebuildIndex(p.Hotbar(0));
        p.Inv.Mutator.RebuildIndex(p.Inventory(0));
        Assert.Equal(p.IndexOf(p.Hotbar(0)).Concat(p.IndexOf(p.Inventory(0))), before);
        Assert.Equal(WeightRecountPropertyTests.Recount(p.R.Content, p.Inv, 0), p.Inv.WeightPoints(PlayerSlot.FromValue(0)));
    }

    [Fact]
    public void PickupHandler_IsBoundOnlyByThisRig_NotByInventorySystemBind()
    {
        // Arrange: a world with the production module only
        var rig = new InvAutoPlaceRig();
        var op = new Op { Kind = InvOpKinds.Pickup, ReqId = 5 };
        op.SetPayload(new PickupPayload { WorldItemId = 1 });

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, in op);

        // Assert: Pickup has no handler there (story 019 binds the production one), so the drain answers unknown_kind
        Assert.Equal(CoreReason.UnknownKind, InvAutoPlaceRig.ReplyOf(log).Reason);
    }
}

/// <summary>
/// TEST-ONLY <c>Pickup</c> handler (story grid-inventory/014): the shared request check (payload length, entry namespace), then
/// <see cref="PickupCore.Apply"/>; a refusal writes the core's reason, <c>byActorId</c> and cause into the reply extra. A floating
/// verdict replies <c>bad_request</c> here (story 019's anchor maps it to P13 <c>unavailable</c> before the core runs). Never bound in
/// <c>InventorySystem.Bind</c> or <c>InventoryWithStubOps</c>; production binds Pickup in story 019. The last outcome is
/// <see cref="Last"/>.
/// </summary>
internal sealed class PickupTestHandler : IOpHandler
{
    public InventorySystem System { get; set; } = null!;

    public OpKind Kind => InvOpKinds.Pickup;

    /// <summary>The last call's outcome.</summary>
    public PickupResult Last { get; private set; }

    public Reply Apply(in Op op, in TickContext ctx)
    {
        var extra = new InvReplyExtra();
        ushort reason = System.RequestCheck.CheckRequest(in op, System.ActorSlot(op.Actor));
        if (reason != 0)
        {
            Last = PickupResult.Refuse(reason);
            return Reply.Reject(in op, reason);
        }

        PickupPayload p = op.PayloadAs<PickupPayload>();
        Last = System.Pickup.Apply(op.Actor, new EntityId(p.WorldItemId), p.EntryId, ref extra);
        if (Last.IsTaken)
        {
            return Reply.Ok(in op, in extra);
        }

        extra.ByActorId = Last.ByActorId;
        extra.SetCause(Last.Cause, unchanged: true);
        Reply rejected = Reply.Reject(in op, Last.Verdict == PickupVerdict.Floating ? CoreReason.BadRequest : Last.Reason);
        rejected.SetExtra(in extra);
        return rejected;
    }
}

/// <summary>
/// The story 014 rig: <see cref="InvAutoPlaceRig"/> with players A (slot 0) and B (slot 1) and the <see cref="PickupTestHandler"/>
/// bound, plus the WorldItem helpers of <see cref="WorldItemRig"/>. Fill helpers leave no free cell: axes on hotbar slots, full plank
/// stacks (20/20) on the player_inventory's 16 cells.
/// </summary>
internal sealed class PickupRig
{
    private uint _reqId = 5000;

    public PickupRig(int? mailIdArenaUnits = null, Func<InventorySettings, InventorySettings>? tune = null)
    {
        var handler = new PickupTestHandler();
        Handler = handler;
        R = new InvAutoPlaceRig(
            mailIdArenaUnits: mailIdArenaUnits,
            tune: tune,
            joinPlayerB: true,
            bindExtra: (builder, inventory) =>
            {
                handler.System = inventory;
                builder.BindOp(InvOpDescriptors.Pickup, handler);
            });
        W = new WorldItemRig(R);
        R.CaptureEvents = true; // the arrange helpers read their reply; WorldItemRig turns capture off after each of its calls
    }

    public InvAutoPlaceRig R { get; }

    public WorldItemRig W { get; }

    public PickupTestHandler Handler { get; }

    public InventorySystem Inv => R.Inventory;

    public WorldItems World => Inv.WorldItems;

    public ActorId A => R.PlayerA;

    public ActorId B => R.PlayerB;

    public static uint[] Ids(uint first, int count) => InvAutoPlaceRig.Ids(first, count);

    public ushort Hotbar(int player) => R.HotbarOf(player);

    public ushort Inventory(int player) => R.InventoryOf(player);

    public ushort Cursor(int player) => R.CursorOf(player);

    /// <summary>A detached entry of <paramref name="count"/> letters to #4 with ids from <paramref name="first"/>.</summary>
    public ushort Letters(uint first, int count) => R.DetachedMail("letter", 4u, Ids(first, count));

    /// <summary>A WorldItem of the detached entry, created in the next tick.</summary>
    public EntityId Drop(ushort entry, bool onWater = false, WorldItemOrigin origin = WorldItemOrigin.Dropped)
    {
        EntityId id = W.Create(W.Now, entry, origin, onWater);
        R.CaptureEvents = true;
        return id;
    }

    /// <summary><see cref="WorldItemRig.CreateOnePerTick"/>, leaving capture on.</summary>
    public EntityId[] DropOnePerTick(uint firstTick, int n, Func<int, ushort> entry)
    {
        EntityId[] ids = W.CreateOnePerTick(firstTick, n, entry);
        R.CaptureEvents = true;
        return ids;
    }

    /// <summary>Runs <paramref name="call"/> in the next tick's <c>MailStep</c> (its log is <c>R.Sink.Last</c>), leaving capture on.</summary>
    public T InTick<T>(Func<T> call)
    {
        T result = W.At(W.Now, call);
        R.CaptureEvents = true;
        return result;
    }

    /// <summary>Axes on <paramref name="player"/>'s hotbar slots <paramref name="from"/> to 7.</summary>
    public void FillHotbar(int player, int from = 1)
    {
        for (int x = from; x < R.Header(Hotbar(player)).W; x++)
        {
            R.PlaceItem(Hotbar(player), "axe", x, 0, 1);
        }
    }

    /// <summary>A full plank stack (20) on every cell of <paramref name="player"/>'s player_inventory from cell <paramref name="from"/> (row-major).</summary>
    public void FillInventory(int player, int from = 0)
    {
        ref ContainerHeader h = ref R.Header(Inventory(player));
        int w = h.W;
        int cells = h.W * h.H;
        for (int i = from; i < cells; i++)
        {
            R.PlaceItem(Inventory(player), "plank", i % w, i / w, 20);
        }
    }

    public Op PickupOp(EntityId item, uint entry = 0)
    {
        var op = new Op { Kind = InvOpKinds.Pickup, ReqId = _reqId++ };
        op.SetPayload(new PickupPayload { WorldItemId = item.Value, EntryId = entry });
        return op;
    }

    /// <summary>One pickup op by <paramref name="actor"/> in the next tick; returns that tick's log.</summary>
    public EventEntry[] Pickup(ActorId actor, EntityId item, uint entry = 0)
    {
        Op op = PickupOp(item, entry);
        return R.Run(actor, in op);
    }

    /// <summary>Two pickup ops in one tick, <paramref name="first"/>'s enqueued first; returns that tick's log.</summary>
    public EventEntry[] PickupBoth(ActorId first, EntityId a, ActorId second, EntityId b)
    {
        Op opA = PickupOp(a);
        Op opB = PickupOp(b);
        int before = R.Sink.Ticks.Count;
        Assert.True(R.Host.World.EnqueueOp(first, in opA));
        Assert.True(R.Host.World.EnqueueOp(second, in opB));
        R.Host.World.Tick();
        Assert.Equal(before + 1, R.Sink.Ticks.Count); // capture was on: the log is this tick's (advisory fix, Mail Review PR #34 r2 advisory 7)
        return R.Sink.Last;
    }

    /// <summary>The core called directly inside the next tick's <c>MailStep</c>.</summary>
    public PickupResult Core(ActorId actor, EntityId item, uint entry, out InvReplyExtra extra)
    {
        InvReplyExtra x = default;
        PickupResult r = InTick(() => Inv.Pickup.Apply(actor, item, entry, ref x));
        extra = x;
        return r;
    }

    /// <summary>The reply sent to <paramref name="actor"/>'s slot in <paramref name="log"/>.</summary>
    public Reply ReplyFor(EventEntry[] log, ActorId actor)
    {
        byte bit = (byte)(1 << Inv.ActorSlot(actor).Value);
        return Assert.Single(log, e => e.Kind == EventKind.OpReply && (e.Audience & bit) != 0).PayloadAs<Reply>();
    }

    /// <summary>Live entries holding mail id <paramref name="mailId"/> (placed or detached).</summary>
    public int CopiesOf(uint mailId)
    {
        int n = 0;
        for (ushort s = 0; s < R.Entries.Capacity; s++)
        {
            if (R.Entries[s].IsLive && R.Entries[s].IsMail)
            {
                n += R.IdsOf(s).Count(id => id == mailId);
            }
        }

        return n;
    }

    /// <summary>The catcher's feet every rig catch uses, mm.</summary>
    public static Vec3Mm Feet => new(5_000, 0, 2_000);

    /// <summary><see cref="IInventoryApi.TryCatch"/> of the detached entry by <paramref name="player"/> inside the next tick (log: <c>R.Sink.Last</c>).</summary>
    public CatchResult Catch(int player, ushort entry, bool onWater = false)
    {
        EntryId id = R.Entries[entry].Id;
        return InTick(() => Inv.TryCatch(PlayerSlot.FromValue((byte)player), new FlightPayload(id, Feet, onWater)));
    }

    /// <summary>The table slot of the live WorldItem holding entry <paramref name="entry"/> (asserts one exists).</summary>
    public ushort WorldItemHolding(ushort entry)
    {
        for (ushort s = World.Table.FirstSlot; s != WorldItemTable.NoSlot; s = World.Table[s].NextSlot)
        {
            if (World.Table[s].EntrySlot == entry)
            {
                return s;
            }
        }

        Assert.Fail($"no WorldItem holds entry slot {entry}");
        return 0;
    }

    /// <summary>Spawns letters into the Intake until the arena refuses (story 011's helper, inside one tick).</summary>
    public void ExhaustArena()
    {
        DefIndex letter = R.KindIndex("letter");
        ContainerId intake = Inv.IntakeContainer;
        var one = new MailId[1];
        uint next = 1_000_000;
        int placed = InTick(() =>
        {
            int n = 0;
            while (true)
            {
                one[0] = new MailId(next++);
                if (Inv.Spawn(intake, letter, 9, 0, one, MergePolicy.NoMerge).Placed != 1)
                {
                    return n;
                }

                n++;
            }
        });
        Assert.True(placed > 0);
        Assert.False(Inv.MailIds.CanReserve(1));
    }

    /// <summary>The newest check-ring record.</summary>
    public SimCheckRecord LastCheck()
    {
        SimCheckRing ring = R.Host.World.CheckRing;
        return ring[ring.Count - 1];
    }

    /// <summary>Containers, occupancy, live entries with ids and counts, the WorldItem table and the arena totals: one comparable list.</summary>
    public List<long> Storage()
    {
        var values = new List<long>();
        for (ushort s = 0; s < Inv.Containers.Capacity; s++)
        {
            ContainerHeader h = Inv.Containers[s];
            if (!h.IsLive)
            {
                continue;
            }

            values.AddRange(new long[] { h.Id.Value, h.Version, h.EntryCount, h.FreeCells });
            for (int y = 0; y < h.H; y++)
            {
                values.Add((long)Inv.Occupancy.RowBits(s, y));
            }
        }

        for (ushort s = 0; s < R.Entries.Capacity; s++)
        {
            EntryRecord e = R.Entries[s];
            if (e.IsLive)
            {
                values.AddRange(new long[] { s, e.Id.Value, e.Count, e.ContainerSlot, e.X, e.Y, e.MailIds.Raw });
            }
        }

        values.Add(World.LiveCount);
        values.Add(World.WorldSeq);
        values.Add(World.Tombstones.Count);
        for (ushort s = World.Table.FirstSlot; s != WorldItemTable.NoSlot; s = World.Table[s].NextSlot)
        {
            values.AddRange(new long[] { World.Table[s].Id.Value, World.Table[s].EntrySlot, World.Table[s].ExpiresTick });
        }

        values.Add(Inv.MailIds.LiveBlocks);
        values.Add(Inv.MailIds.LiveUnits);
        values.Add(Inv.Mutator.Weight.Points(0));
        values.Add(Inv.Mutator.Weight.Points(1));
        return values;
    }

    /// <summary>Ticks (capture off) until <see cref="WorldItemRig.Now"/> is <paramref name="tick"/>, then turns capture back on.</summary>
    public void AdvanceTo(uint tick)
    {
        W.AdvanceTo(tick);
        R.CaptureEvents = true;
    }

    /// <summary>
    /// For a WorldItem removed at <paramref name="t"/>: a pickup by <paramref name="asker"/> (A by default) at <c>t + 900</c> gets
    /// <c>container_gone</c> with the tombstone's actor and cause, at <c>t + 901</c> <c>stale_entry</c>; nothing is mutated either time.
    /// </summary>
    public void AssertTombstoneAnswers(EntityId item, uint t, uint byActorId, InvCause cause, ActorId? asker = null)
    {
        ActorId who = asker ?? A;
        AdvanceTo(t + Tombstones.LifetimeTicks);
        List<long> before = Storage();
        EventEntry[] alive = Pickup(who, item);
        Reply gone = InvAutoPlaceRig.ReplyOf(alive);
        Assert.Equal((ushort)InvReason.ContainerGone, gone.Reason);
        Assert.Equal(byActorId, gone.ExtraAs<InvReplyExtra>().ByActorId);
        Assert.Equal(cause, gone.ExtraAs<InvReplyExtra>().Cause);
        Assert.Equal(before, Storage());
        Assert.DoesNotContain(alive, IsInventoryRecord);

        EventEntry[] dead = Pickup(who, item); // the next tick: t + 901
        Assert.Equal((ushort)InvReason.StaleEntry, InvAutoPlaceRig.ReplyOf(dead).Reason);
        Assert.Equal(before, Storage());
        Assert.DoesNotContain(dead, IsInventoryRecord);
    }

    /// <summary>True for an inventory record or seal (kinds 0x0080-0x008A).</summary>
    public static bool IsInventoryRecord(EventEntry e) => (ushort)e.Kind is >= 0x0080 and <= 0x008A;

    /// <summary>The inventory kinds of <paramref name="log"/> in order, continuations excluded.</summary>
    public static EventKind[] InventoryKinds(EventEntry[] log) =>
        log.Where(e => IsInventoryRecord(e) && (e.Flags & EventFlags.Continuation) == 0).Select(e => e.Kind).ToArray();

    /// <summary>The one seal of <paramref name="container"/> in <paramref name="log"/>.</summary>
    public static InvVersionPayload Seal(EventEntry[] log, ContainerId container) =>
        Assert.Single(WorldItemTests.Heads(log, InvEventKinds.Version), e => e.PayloadAs<InvVersionPayload>().Container == container.Value).PayloadAs<InvVersionPayload>();

    /// <summary>The index in <paramref name="log"/> of its one <c>OpReply</c>.</summary>
    public static int ReplyIndex(EventEntry[] log) => Array.FindIndex(log, e => e.Kind == EventKind.OpReply);

    /// <summary>The index in <paramref name="log"/> of the one seal of <paramref name="container"/>.</summary>
    public static int SealIndex(EventEntry[] log, ContainerId container)
    {
        Seal(log, container);
        return Array.FindIndex(
            log,
            e => e.Kind == InvEventKinds.Version && (e.Flags & EventFlags.Continuation) == 0 && e.PayloadAs<InvVersionPayload>().Container == container.Value);
    }

    /// <summary>The reason code a refusal name stands for.</summary>
    public static ushort ReasonOf(string refusal) => refusal switch
    {
        "bad_request" => CoreReason.BadRequest,
        "container_gone" => (ushort)InvReason.ContainerGone,
        "stale_entry" => (ushort)InvReason.StaleEntry,
        "no_room" => (ushort)InvReason.NoRoom,
        "capacity_exhausted" => (ushort)InvReason.CapacityExhausted,
        _ => 0,
    };

    /// <summary>The units of item <paramref name="item"/> in A's hotbar and player_inventory, by scanning the entry pool.</summary>
    public int ScanCount(DefIndex item)
    {
        int units = 0;
        for (ushort s = 0; s < R.Entries.Capacity; s++)
        {
            EntryRecord e = R.Entries[s];
            if (e.IsLive && !e.IsMail && e.ItemId == item.Value && (e.ContainerSlot == Hotbar(0) || e.ContainerSlot == Inventory(0)))
            {
                units += e.Count;
            }
        }

        return units;
    }

    /// <summary>The StackKey index lists of every key present in <paramref name="container"/>, in key-first-seen then list order.</summary>
    public List<ushort> IndexOf(ushort container)
    {
        var keys = new List<StackKey>();
        for (ushort s = 0; s < R.Entries.Capacity; s++)
        {
            if (R.Entries[s].IsLive && R.Entries[s].ContainerSlot == container && !keys.Contains(StackKey.Of(in R.Entries[s])))
            {
                keys.Add(StackKey.Of(in R.Entries[s]));
            }
        }

        var lists = new List<ushort>();
        foreach (StackKey k in keys)
        {
            for (ushort s = R.Index.Head(container, k); s != StackIndex.None; s = R.Index.Next(s))
            {
                lists.Add(s);
            }

            lists.Add(ushort.MaxValue);
        }

        return lists;
    }

    /// <summary>True when the negative-fit cache knows a <paramref name="w"/> x <paramref name="h"/> shape (either orientation) does not fit <paramref name="container"/> at its current version.</summary>
    public bool KnownNoFit(ushort container, byte w, byte h)
    {
        NegativeFitCache cache = Inv.AutoPlace.NoFitCache;
        return cache.IsNoFit(container, in R.Header(container), w, h, rotated: false) || cache.IsNoFit(container, in R.Header(container), w, h, rotated: true);
    }

    /// <summary>Live entries placed in <paramref name="container"/>.</summary>
    public int EntriesIn(ushort container) => R.Header(container).EntryCount;
}
