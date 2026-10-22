using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/014 (GDD R8, R9 l.128-129, R12 l.137, edge cases l.362-363, H-R9-2, H-R12-1; ADR-0009 sections 1, 2 and 5;
/// TD-154 Mail Lead planning defaults pending Evan): the pickup core (<see cref="PickupCore"/>) and <see cref="IInventoryApi.TryCatch"/>
/// with its fumble. Pickups run both through a test-only <c>Pickup</c> handler (<see cref="PickupTestHandler"/>, bound only in this
/// rig: production binds Pickup in story 019) in the real phase-0 drain, and through the core directly inside a tick phase; catches run
/// inside a tick phase, as Throwing's flight resolution will. State is arranged through real calls (the mutator-backed
/// <c>PlaceMail</c> / <c>PlaceItem</c>, <c>CreateWorldItem</c>, <c>DetachForThrow</c>).
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
    public void Catch_ArenaRefusesTheMailMergeUnionBlock_TheWholeCatchFumbles_0AndN_CapacityExhausted_NoUnitLost()
    {
        // Arrange: a minimum arena; a 10-letter #4 stack on hotbar slot 1; the flight entry of 5; then the arena exhausted
        var p = new PickupRig(mailIdArenaUnits: MailIdArena.MinTotalUnits);
        ushort stack = p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 10));
        ushort flight = p.Letters(101, 5);
        EntryId id = p.R.Entries[flight].Id;
        p.ExhaustArena();
        uint hotbarV = p.R.Header(p.Hotbar(0)).Version;

        // Act
        CatchResult r = p.Catch(0, flight);

        // Assert: nothing went in, the entry (its own block, same id) is the fumble WorldItem
        Assert.Equal(new CatchResult(0, 5, InvReason.CapacityExhausted), r);
        Assert.Equal(10, p.R.Entries[stack].Count);
        Assert.Equal(hotbarV, p.R.Header(p.Hotbar(0)).Version);
        ushort ws = p.WorldItemHolding(flight);
        Assert.Equal(WorldItemOrigin.Thrown, p.World.Table[ws].Origin);
        Assert.Equal(id, p.R.Entries[flight].Id);
        Assert.Equal(PickupRig.Ids(101, 5), p.R.IdsOf(flight));
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

    [Theory]
    [InlineData("detached-in-world")]
    [InlineData("placed")]
    [InlineData("dead")]
    [InlineData("no-panel")]
    public void Catch_MalformedCall_IsBadRequest_NothingChanged(string kind)
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

        // Assert
        Assert.Equal(new CatchResult(0, 0, (InvReason)InvReasons.BadRequest), r);
        Assert.Equal(before, p.Storage());
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
        Assert.True(R.Host.World.EnqueueOp(first, in opA));
        Assert.True(R.Host.World.EnqueueOp(second, in opB));
        R.Host.World.Tick();
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

    /// <summary>Live entries placed in <paramref name="container"/>.</summary>
    public int EntriesIn(ushort container) => R.Header(container).EntryCount;
}
