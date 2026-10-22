using System.Collections.Immutable;
using System.Reflection;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/007 slice 1 (ACs 1-9): the production <c>Pick</c> and <c>Move</c> handlers (<see cref="PickHandler"/>,
/// <see cref="MoveHandler"/>) through the real phase-0 drain of a composed world with players A (slot 0) and B (slot 1); GDD R4, R7,
/// R10; H-R1-2, H-R4-1 a–f, H-R4-2 a/b, H-R4-3 a/b, H-R10-2 (move leg); TD-105 items 3 and 5; ADR-0009 sections 2-3. Every core case
/// runs from slot 0 and from slot 1, so a hard-coded slot index cannot pass. State is arranged only through real calls: the rig's
/// mutator-backed <c>PlaceMail</c> / <c>PlaceItem</c>, <c>TryOpenExternal</c>, <c>Spawn</c> and <c>DetachForThrow</c> in a tick phase,
/// and the real ops — except the documented <c>stale_layout</c> seam (<see cref="ContainerHeader.LastReplaceVersion"/> written between
/// ticks; only M1's sort sets it). Every reject asserts its reply, no record and no <c>InvVersion</c> seal in the tick log, the reply
/// alone in its call and sent to the actor alone, and <see cref="SideEffects"/> unchanged (containers with versions, occupancy, entries
/// and mail ids, the StackKey index, the arena, weight P, <c>CountItem</c>, the negative-fit cache, subscriber masks, externals, check
/// ring writes and the id allocators). The count, merge-fit and arena rules are the Mail Lead planning defaults pending Evan (TD-150).
/// AC 10 (indexes and weight) and AC 11 (0 B) are slice 2.
/// </summary>
public class PickMoveTests
{
    private const uint Address3 = 3u;
    private const uint Address99 = 99u;

    /// <summary>The fixture container def <see cref="PrecedenceContent"/> adds: an 8 x 4 hand-carried grid with <c>acceptsInsert: false</c>.</summary>
    private const string OutputOnlyDef = "chest_output_only";

    // ------------------------------------------------------------------ AC 1: H-R1-2

    [Theory]
    [Trait("Id", "H-R1-2")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR12_AxeOnTheCursor_MoveToHotbarSlot0_ReservedSlot_ThenSlot1_Ok(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort cursor = rig.CursorOf(slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort axe = rig.PlaceItem(cursor, "axe", 0, 0, 1);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] reserved = rig.Run(rig.Player(slot), MoveOp(rig, cursor, axe, hotbar, 0, 0));

        // Assert: reserved_slot; the Axe stays on the cursor, the hotbar version is unchanged
        AssertRejected(rig, reserved, InvReason.ReservedSlot, before, slot);
        Assert.Equal(cursor, rig.Entries[axe].ContainerSlot);

        // Act
        uint hotbarV = rig.Header(hotbar).Version;
        uint cursorV = rig.Header(cursor).Version;
        EventEntry[] placed = rig.Run(rig.Player(slot), MoveOp(rig, cursor, axe, hotbar, 1, 0));

        // Assert: ok, the Axe (same entry) at hotbar slot 1, the cursor empty; both containers +1
        AssertOk(placed);
        Assert.Equal(hotbar, rig.Entries[axe].ContainerSlot);
        Assert.Equal((1, 0), (rig.Entries[axe].X, rig.Entries[axe].Y));
        Assert.Equal(0, rig.Header(cursor).EntryCount);
        Assert.Equal(hotbarV + 1, rig.Header(hotbar).Version);
        Assert.Equal(cursorV + 1, rig.Header(cursor).Version);
    }

    // ------------------------------------------------------------------ AC 2: Pick

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Pick_EntryInTheActorsInventory_LiftsTheWholeEntryOntoTheEmptyCursor_KeepingItsId(int slot)
    {
        // Arrange: 5 letters to #3 at the actor's inventory (2,1)
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort letters = rig.PlaceMail(inventory, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(100, 5));
        EntryId id = rig.Entries[letters].Id;
        uint inventoryV = rig.Header(inventory).Version;
        uint cursorV = rig.Header(cursor).Version;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), PickOp(rig, inventory, letters));

        // Assert: the same entry (id, 5 units, its mail ids) on cursor slot 0; Removed then Inserted, then one seal per container
        Reply reply = AssertOk(log);
        Assert.Equal(id, rig.Entries[letters].Id);
        Assert.Equal(cursor, rig.Entries[letters].ContainerSlot);
        Assert.Equal((0, 0, (byte)5), (rig.Entries[letters].X, rig.Entries[letters].Y, rig.Entries[letters].Count));
        Assert.Equal(InvAutoPlaceRig.Ids(100, 5), rig.IdsOf(letters));
        Assert.Equal(0, rig.Header(inventory).EntryCount);
        Assert.Equal(inventoryV + 1, rig.Header(inventory).Version);
        Assert.Equal(cursorV + 1, rig.Header(cursor).Version);
        Assert.Equal(
            new[] { InvEventKinds.Removed, InvEventKinds.Inserted, InvEventKinds.Version, InvEventKinds.Version },
            RecordHeads(log).Select(e => e.Kind).ToArray());
        Assert.Equal(new[] { rig.IdOf(inventory).Value, rig.IdOf(cursor).Value }, Seals(log).Select(s => s.Container).ToArray());
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((inventoryV + 1, cursorV + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.False(extra.Unchanged);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Pick_FromTheActorsOpenChest_LiftsTheMediumOntoTheCursor_ChestAndCursorPlusOne(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort cursor = rig.CursorOf(slot);
        ushort medium = rig.PlaceMail(chest, "medium", 6, 2, Address99, InvAutoPlaceRig.Ids(500, 1));
        uint chestV = rig.Header(chest).Version;
        uint cursorV = rig.Header(cursor).Version;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), PickOp(rig, chest, medium));

        // Assert
        AssertOk(log);
        Assert.Equal(cursor, rig.Entries[medium].ContainerSlot);
        Assert.Equal(0, rig.Header(chest).EntryCount);
        Assert.Equal(rig.Header(chest).W * rig.Header(chest).H, rig.Header(chest).FreeCells);
        Assert.Equal(chestV + 1, rig.Header(chest).Version);
        Assert.Equal(cursorV + 1, rig.Header(cursor).Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Pick_WithABusyCursor_CursorBusy_TheCursorAndTheSourceUnchanged(int slot)
    {
        // Arrange: an Axe on the cursor, 5 letters in the inventory
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort axe = rig.PlaceItem(cursor, "axe", 0, 0, 1);
        ushort letters = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 5));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), PickOp(rig, inventory, letters));

        // Assert: the reply still names both containers' current versions
        Reply reply = AssertRejected(rig, log, InvReason.CursorBusy, before, slot);
        Assert.Equal(cursor, rig.Entries[axe].ContainerSlot);
        Assert.Equal(inventory, rig.Entries[letters].ContainerSlot);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((rig.Header(inventory).Version, rig.Header(cursor).Version), (extra.ResultVersionA, extra.ResultVersionB));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Pick_TheCursorsOwnEntry_CursorBusy_NothingChanges(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort cursor = rig.CursorOf(slot);
        ushort axe = rig.PlaceItem(cursor, "axe", 0, 0, 1);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), PickOp(rig, cursor, axe));

        // Assert
        AssertRejected(rig, log, InvReason.CursorBusy, before, slot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Pick_EntryInAChestOutsideTheActorsOpenSet_NotOpen_NothingChanges(int slot)
    {
        // Arrange: a chest the actor has not opened
        var rig = NewRig();
        ushort chest = rig.CreateGrid(8, 4);
        ushort medium = rig.PlaceMail(chest, "medium", 6, 2, Address99, InvAutoPlaceRig.Ids(500, 1));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), PickOp(rig, chest, medium));

        // Assert
        AssertRejected(rig, log, InvReason.NotOpen, before, slot);
        Assert.Equal(chest, rig.Entries[medium].ContainerSlot);
    }

    /// <summary>
    /// Pick's chain <c>bad_request → not_open → stale_entry → cursor_busy</c>: each row violates its step and every later one (the
    /// cursor is busy in every row); the earliest code must win.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-006")]
    [InlineData(CoreReason.BadRequest, 0)]
    [InlineData(CoreReason.BadRequest, 1)]
    [InlineData((ushort)InvReason.NotOpen, 0)]
    [InlineData((ushort)InvReason.NotOpen, 1)]
    [InlineData((ushort)InvReason.StaleEntry, 0)]
    [InlineData((ushort)InvReason.StaleEntry, 1)]
    [InlineData((ushort)InvReason.CursorBusy, 0)]
    [InlineData((ushort)InvReason.CursorBusy, 1)]
    public void Pick_Precedence_RequestViolatingThisStepAndEveryLaterOne_ReturnsThisStep(ushort step, int slot)
    {
        // Arrange: a busy cursor; a letter in a chest the actor has not opened; a letter in the actor's inventory
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        _ = rig.PlaceItem(rig.CursorOf(slot), "axe", 0, 0, 1);
        ushort closedChest = rig.CreateGrid(8, 4);
        ushort inChest = rig.PlaceMail(closedChest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 2));
        ushort inInventory = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(200, 2));
        uint closed = rig.IdOf(closedChest).Value;
        Op op = step switch
        {
            CoreReason.BadRequest => PickOpRaw(closed, rig.Entries.LastId + 1),                         // unissued id; closed; busy
            (ushort)InvReason.NotOpen => PickOpRaw(closed, rig.Entries[inInventory].Id.Value),         // live, not in from; busy
            (ushort)InvReason.StaleEntry => PickOpRaw(rig.IdOf(inventory).Value, rig.Entries[inChest].Id.Value), // busy
            _ => PickOp(rig, inventory, inInventory),
        };
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert
        AssertRejected(rig, log, (InvReason)step, before, slot);
    }

    // ------------------------------------------------------------------ AC 3: H-R4-1 a–f (op form)

    [Theory]
    [Trait("Id", "H-R4-1a")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR41a_TwoCursorLettersTo3_PlacedAt00_MergeIntoTheStack_Six_CursorEmpty_ChestPlusOne(int slot)
    {
        // Arrange
        var rig = NewRig();
        (ushort chest, _, ushort stack) = HR41Chest(rig, slot);
        ushort cursor = rig.CursorOf(slot);
        ushort two = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(10, 2));
        EntryId twoId = rig.Entries[two].Id;
        EntryId stackId = rig.Entries[stack].Id;
        uint chestV = rig.Header(chest).Version;
        uint cursorV = rig.Header(cursor).Version;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, two, chest, 0, 0));

        // Assert: the stack keeps its id and holds 6 ascending ids; the cursor entry is gone (freed); chest +1 (and the cursor +1)
        AssertOk(log);
        Assert.Equal(stackId, rig.Entries[stack].Id);
        Assert.Equal(6, rig.Entries[stack].Count);
        Assert.Equal(new uint[] { 10, 11, 100, 101, 102, 103 }, rig.IdsOf(stack));
        Assert.False(rig.Entries.TryGetSlot(twoId, out _));
        Assert.Equal(0, rig.Header(cursor).EntryCount);
        Assert.Equal(chestV + 1, rig.Header(chest).Version);
        Assert.Equal(cursorV + 1, rig.Header(cursor).Version);
        Assert.Equal(
            new[] { InvEventKinds.Removed, InvEventKinds.Merged, InvEventKinds.Version, InvEventKinds.Version },
            RecordHeads(log).Select(e => e.Kind).ToArray());
    }

    [Theory]
    [Trait("Id", "H-R4-1b")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR41b_CursorLargeAt70_OutOfBounds_ChestAndCursorUnchanged(int slot) => HR41Reject(slot, "large", 7, 0, false, InvReason.OutOfBounds);

    [Theory]
    [Trait("Id", "H-R4-1c")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR41c_CursorLargeAt50_OverlapsTheMedium_Blocked_ChestAndCursorUnchanged(int slot) => HR41Reject(slot, "large", 5, 0, false, InvReason.Blocked);

    [Theory]
    [Trait("Id", "H-R4-1d")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR41d_CursorLargeAt51_LeavesTheGridAndOverlaps_OutOfBounds_ChestAndCursorUnchanged(int slot) => HR41Reject(slot, "large", 5, 1, false, InvReason.OutOfBounds);

    [Theory]
    [Trait("Id", "H-R4-1e")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR41e_CursorSmallRotatedAt00_OverlapsTheLetterStack_Blocked_ChestAndCursorUnchanged(int slot) => HR41Reject(slot, "small", 0, 0, true, InvReason.Blocked);

    [Theory]
    [Trait("Id", "H-R4-1f")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR41f_CursorLogsAt21_PartiallyOverlapTheLogStackAt20_Blocked_ChestAndCursorUnchanged(int slot)
    {
        // Arrange: Log x3 at (2,0) (cells (2,0),(2,1)); the cursor holds Log x2
        var rig = NewRig();
        (ushort chest, _, _) = HR41Chest(rig, slot);
        ushort logs = rig.PlaceItem(chest, "log", 2, 0, 3);
        ushort cursor = rig.CursorOf(slot);
        ushort two = rig.PlaceItem(cursor, "log", 0, 0, 2);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, two, chest, 2, 1));

        // Assert
        AssertRejected(rig, log, InvReason.Blocked, before, slot);
        Assert.Equal(cursor, rig.Entries[two].ContainerSlot);
        Assert.Equal(3, rig.Entries[logs].Count);
    }

    // ------------------------------------------------------------------ AC 4: H-R4-2 a/b

    [Theory]
    [Trait("Id", "H-R4-2a")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR42a_LargeAt00_MovedTo10_OverItsOwnCells_Ok_ChestPlusOne_OneMovedRecord(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort large = rig.PlaceMail(chest, "large", 0, 0, Address99, InvAutoPlaceRig.Ids(500, 1));
        uint chestV = rig.Header(chest).Version;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, chest, large, chest, 1, 0));

        // Assert: x 1–2, y 0–3 now; column 0 free; one InvMoved and one seal
        AssertOk(log);
        Assert.Equal((1, 0), (rig.Entries[large].X, rig.Entries[large].Y));
        Assert.Equal(Occupancy.Free, rig.Inventory.Occupancy.OwnerAt(chest, 8, 0, 0));
        Assert.Equal(large, rig.Inventory.Occupancy.OwnerAt(chest, 8, 2, 3));
        Assert.Equal(chestV + 1, rig.Header(chest).Version);
        Assert.Equal(new[] { InvEventKinds.Moved, InvEventKinds.Version }, RecordHeads(log).Select(e => e.Kind).ToArray());
    }

    [Theory]
    [Trait("Id", "H-R4-2b")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR42b_ChestMediumToInventory60_WhereAnAxeCovers6061_Blocked_EntryUnmoved_BothVersionsUnchanged(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort medium = rig.PlaceMail(chest, "medium", 0, 0, Address99, InvAutoPlaceRig.Ids(500, 1));
        _ = rig.PlaceItem(inventory, "axe", 6, 0, 1);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, chest, medium, inventory, 6, 0));

        // Assert
        Reply reply = AssertRejected(rig, log, InvReason.Blocked, before, slot);
        Assert.Equal((chest, (byte)0, (byte)0), (rig.Entries[medium].ContainerSlot, rig.Entries[medium].X, rig.Entries[medium].Y));
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((rig.Header(chest).Version, rig.Header(inventory).Version), (extra.ResultVersionA, extra.ResultVersionB));
    }

    // ------------------------------------------------------------------ AC 5: H-R4-3 a/b

    [Theory]
    [Trait("Id", "H-R4-3a")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR43a_TwoCursorLettersTo3_PlacedOnFree42_NeverMerge_NewEntryAt42_AStillFive_ChestAndCursorPlusOneEach(int slot)
    {
        // Arrange: A = 5 letters to #3 at chest (0,0)
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort a = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 5));
        ushort cursor = rig.CursorOf(slot);
        ushort two = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(10, 2));
        EntryId twoId = rig.Entries[two].Id;
        uint chestV = rig.Header(chest).Version;
        uint cursorV = rig.Header(cursor).Version;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, two, chest, 4, 2));

        // Assert: the cursor entry, same id, at (4,2); A untouched; the chest bumps once and the cursor once (TD-150 (3))
        AssertOk(log);
        Assert.Equal(twoId, rig.Entries[two].Id);
        Assert.Equal((chest, (byte)4, (byte)2, (byte)2), (rig.Entries[two].ContainerSlot, rig.Entries[two].X, rig.Entries[two].Y, rig.Entries[two].Count));
        Assert.Equal(5, rig.Entries[a].Count);
        Assert.Equal(chestV + 1, rig.Header(chest).Version);
        Assert.Equal(cursorV + 1, rig.Header(cursor).Version);
        Assert.Equal(2, Seals(log).Length);
        Assert.DoesNotContain(log, e => e.Kind == InvEventKinds.Merged);
    }

    [Theory]
    [Trait("Id", "H-R4-3b")]
    [InlineData(0, false, 0)]
    [InlineData(1, false, 0)]
    [InlineData(0, true, 0)]
    [InlineData(1, true, 0)]
    [InlineData(0, false, 5)]
    [InlineData(1, false, 5)]
    public void HR43b_MoveAOntoItsOwnCells_SameCanonicalRotation_OkUnchanged_NoBump_NoEvent(int slot, bool rotated, byte count)
    {
        // Arrange: A = 5 letters (1 x 1, so rotated canonicalises to unrotated); count 0 and count == total are both whole moves
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort a = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 5));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, chest, a, chest, 0, 0, rotated, count));

        // Assert: ok with the unchanged flag; the reply is the tick's only inventory-side entry; nothing changed
        Reply reply = AssertOk(log);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.True(extra.Unchanged);
        Assert.Equal((rig.Header(chest).Version, rig.Header(chest).Version), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Empty(InventoryRecords(log));
        Assert.Equal(before, SideEffects(rig));
    }

    [Theory]
    [Trait("Id", "H-R4-3b")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR43b_LargeOntoItsOwnTopLeftWithTheRotationChanged_IsAMove_NotANoOp(int slot)
    {
        // Arrange: a Large (2 x 4) at (0,0); rotated it covers x 0–3, y 0–1, over its own cells
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort large = rig.PlaceMail(chest, "large", 0, 0, Address99, InvAutoPlaceRig.Ids(500, 1));
        uint chestV = rig.Header(chest).Version;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, chest, large, chest, 0, 0, rotated: true));

        // Assert
        Reply reply = AssertOk(log);
        Assert.False(reply.ExtraAs<InvReplyExtra>().Unchanged);
        Assert.True(rig.Entries[large].Rotated);
        Assert.Equal(chestV + 1, rig.Header(chest).Version);
        Assert.Equal(new[] { InvEventKinds.Moved, InvEventKinds.Version }, RecordHeads(log).Select(e => e.Kind).ToArray());
    }

    // ------------------------------------------------------------------ AC 6: the full placement precedence chain

    /// <summary>
    /// For each step of <c>bad_request → not_open → stale_entry → bad_count → stale_layout → reserved_slot → out_of_bounds →
    /// not_carryable → not_allowed → blocked / stack_full</c>, a <c>move</c> violating that step and every later one that can co-occur;
    /// the earliest code must win. Rows up to <c>stale_layout</c> aim at an output-only, hand-carried 8 x 4 chest (Medium at (6,2)) at
    /// (7,2) with a <c>carryByHand: false</c> 2 x 2 crate (also out of bounds, not carryable, not allowed and blocked), count 2 (above
    /// the crate's 1) and <c>baseVersion</c> 0 below the seam's <c>lastReplaceVersion</c> 1. <c>reserved_slot</c> runs on the hotbar
    /// (here <c>acceptsInsert: false</c>; plank in slot 0) with a non-cursor source, where <c>out_of_bounds</c> cannot co-occur.
    /// <c>blocked</c> and <c>stack_full</c> share the last step.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-006")]
    [InlineData(CoreReason.BadRequest, 0)]
    [InlineData(CoreReason.BadRequest, 1)]
    [InlineData((ushort)InvReason.NotOpen, 0)]
    [InlineData((ushort)InvReason.NotOpen, 1)]
    [InlineData((ushort)InvReason.StaleEntry, 0)]
    [InlineData((ushort)InvReason.StaleEntry, 1)]
    [InlineData((ushort)InvReason.BadCount, 0)]
    [InlineData((ushort)InvReason.BadCount, 1)]
    [InlineData((ushort)InvReason.StaleLayout, 0)]
    [InlineData((ushort)InvReason.StaleLayout, 1)]
    [InlineData((ushort)InvReason.ReservedSlot, 0)]
    [InlineData((ushort)InvReason.ReservedSlot, 1)]
    [InlineData((ushort)InvReason.OutOfBounds, 0)]
    [InlineData((ushort)InvReason.OutOfBounds, 1)]
    [InlineData((ushort)InvReason.NotCarryable, 0)]
    [InlineData((ushort)InvReason.NotCarryable, 1)]
    [InlineData((ushort)InvReason.NotAllowed, 0)]
    [InlineData((ushort)InvReason.NotAllowed, 1)]
    [InlineData((ushort)InvReason.Blocked, 0)]
    [InlineData((ushort)InvReason.Blocked, 1)]
    [InlineData((ushort)InvReason.StackFull, 0)]
    [InlineData((ushort)InvReason.StackFull, 1)]
    public void Move_Precedence_RequestViolatingThisStepAndEveryLaterOne_ReturnsThisStep(ushort step, int slot)
    {
        // Arrange
        var rig = NewRig(PrecedenceContent());
        ushort cursor = rig.CursorOf(slot);
        ushort inventory = rig.InventoryOf(slot);
        Op op;
        if (step == (ushort)InvReason.ReservedSlot)
        {
            // Crate in the actor's inventory -> hotbar slot 0: reserved, not carryable, not allowed (acceptsInsert false), blocked
            ushort hotbar = rig.HotbarOf(slot);
            _ = rig.PlaceItem(hotbar, "plank", 0, 0, 1);
            ushort crate = rig.PlaceItem(inventory, "crate", 0, 0, 1);
            op = MoveOp(rig, inventory, crate, hotbar, 0, 0);
        }
        else if (step <= (ushort)InvReason.StaleLayout || step == (ushort)InvReason.BadCount || step == CoreReason.BadRequest)
        {
            op = EverythingWrong(rig, slot, (InvReason)step, cursor, inventory);
        }
        else
        {
            op = RuleRow(rig, slot, (InvReason)step, cursor);
        }

        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert
        AssertRejected(rig, log, (InvReason)step, before, slot);
    }

    [Theory]
    [Trait("Id", "H-R7-5")]
    [InlineData(0)]
    [InlineData(1)]
    public void StaleLayoutSeam_BaseVersionBelowLastReplace_StaleLayout_AtLastReplace_EvaluatedNormally(int slot)
    {
        // Arrange: the seam (only M1's sort writes lastReplaceVersion): the chest's lastReplaceVersion = n = its version + 3
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort a = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 5));
        uint n = rig.Header(chest).Version + 3;
        rig.Header(chest).LastReplaceVersion = n;
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] stale = rig.Run(rig.Player(slot), MoveOp(rig, chest, a, chest, 3, 1, baseVersion: n - 1));

        // Assert
        AssertRejected(rig, stale, InvReason.StaleLayout, before, slot);

        // Act
        EventEntry[] normal = rig.Run(rig.Player(slot), MoveOp(rig, chest, a, chest, 3, 1, baseVersion: n));

        // Assert
        AssertOk(normal);
        Assert.Equal((3, 1), (rig.Entries[a].X, rig.Entries[a].Y));
    }

    // ------------------------------------------------------------------ AC 7: H-R10-2 (move leg)

    [Theory]
    [Trait("Id", "H-R10-2")]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void HR102_MoveFromAnotherPlayersInventory_ToTheActorsOwn_NotOpen_NothingChanges(int actorSlot, int ownerSlot)
    {
        // Arrange: the owner's inventory holds E
        var rig = NewRig();
        ushort ownerInventory = rig.InventoryOf(ownerSlot);
        ushort e = rig.PlaceMail(ownerInventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 3));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(actorSlot), MoveOp(rig, ownerInventory, e, rig.InventoryOf(actorSlot), 0, 0));

        // Assert
        AssertRejected(rig, log, InvReason.NotOpen, before, actorSlot);
    }

    [Theory]
    [Trait("Id", "H-R10-2")]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void HR102_PickFromAnotherPlayersInventory_NotOpen_NothingChanges(int actorSlot, int ownerSlot)
    {
        // Arrange
        var rig = NewRig();
        ushort ownerInventory = rig.InventoryOf(ownerSlot);
        ushort e = rig.PlaceMail(ownerInventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 3));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(actorSlot), PickOp(rig, ownerInventory, e));

        // Assert
        AssertRejected(rig, log, InvReason.NotOpen, before, actorSlot);
    }

    [Theory]
    [Trait("Id", "H-R10-2")]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void HR102_MoveOfTheActorsOwnEntry_IntoAnotherPlayersInventory_NotOpen_TheToSideIsChecked(int actorSlot, int otherSlot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(actorSlot);
        ushort e = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 3));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(actorSlot), MoveOp(rig, inventory, e, rig.InventoryOf(otherSlot), 4, 1));

        // Assert
        AssertRejected(rig, log, InvReason.NotOpen, before, actorSlot);
    }

    [Theory]
    [Trait("Id", "H-R10-2")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR102_TheOwnersOwnMove_PassesNotOpen_AndIsApplied(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort e = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 3));
        uint v = rig.Header(inventory).Version;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, e, inventory, 4, 1));

        // Assert
        AssertOk(log);
        Assert.Equal((4, 1), (rig.Entries[e].X, rig.Entries[e].Y));
        Assert.Equal(v + 1, rig.Header(inventory).Version);
    }

    [Theory]
    [Trait("Id", "H-R10-2")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR102_AChestOnlyTheOtherPlayerHasOpen_NotOpen_ForPickAndForMoveInAndOut(int slot)
    {
        // Arrange: the other player has the chest open; the actor has nothing open
        var rig = NewRig();
        int other = 1 - slot;
        ushort chest = OpenChest(rig, other);
        ushort inChest = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 3));
        ushort inventory = rig.InventoryOf(slot);
        ushort mine = rig.PlaceMail(inventory, "letter", 0, 0, Address99, InvAutoPlaceRig.Ids(200, 3));
        List<long> before = SideEffects(rig);

        // Act + Assert
        AssertRejected(rig, rig.Run(rig.Player(slot), PickOp(rig, chest, inChest)), InvReason.NotOpen, before, slot);
        AssertRejected(rig, rig.Run(rig.Player(slot), MoveOp(rig, chest, inChest, inventory, 4, 1)), InvReason.NotOpen, before, slot);
        AssertRejected(rig, rig.Run(rig.Player(slot), MoveOp(rig, inventory, mine, chest, 4, 1)), InvReason.NotOpen, before, slot);
    }

    [Fact]
    [Trait("Id", "H-R10-2")]
    public void HR102_TheSystemActorHasAnEmptyOpenSet_PickAndMove_NotOpen_NothingChanges()
    {
        // Arrange: E in A's inventory; A has a chest open
        var rig = NewRig();
        ushort chest = OpenChest(rig, 0);
        ushort inventory = rig.InventoryOf(0);
        ushort e = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 3));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] pick = rig.Run(ActorId.System, PickOp(rig, inventory, e));
        EventEntry[] move = rig.Run(ActorId.System, MoveOp(rig, inventory, e, chest, 0, 0));

        // Assert: no slot hears the System actor's reply
        foreach (EventEntry[] log in new[] { pick, move })
        {
            Reply reply = InvAutoPlaceRig.ReplyOf(log);
            Assert.Equal((ushort)InvReason.NotOpen, reply.Reason);
            Assert.Empty(InventoryRecords(log));
            Assert.Equal(0, Assert.Single(log, x => x.Kind == EventKind.OpReply).Audience);
        }

        Assert.Equal(before, SideEffects(rig));
    }

    // ------------------------------------------------------------------ AC 8: rejects raise nothing (TD-105 item 5)

    [Theory]
    [Trait("Id", "TD-105")]
    [InlineData("bad_request", 0)]
    [InlineData("bad_request", 1)]
    [InlineData("not_open", 0)]
    [InlineData("not_open", 1)]
    [InlineData("stale_entry", 0)]
    [InlineData("stale_entry", 1)]
    [InlineData("cursor_busy", 0)]
    [InlineData("cursor_busy", 1)]
    [InlineData("bad_count", 0)]
    [InlineData("bad_count", 1)]
    [InlineData("stale_layout", 0)]
    [InlineData("stale_layout", 1)]
    [InlineData("out_of_bounds", 0)]
    [InlineData("out_of_bounds", 1)]
    [InlineData("blocked", 0)]
    [InlineData("blocked", 1)]
    [InlineData("stack_full", 0)]
    [InlineData("stack_full", 1)]
    public void Reject_LeavesOnlyTheOpReply_AndTheOtherSubscriberOfTheChestReceivesNothing(string reason, int slot)
    {
        // Arrange: both players have the chest open (both subscribed); the chest holds A = 5 letters to #3 at (0,0), 20 letters to #99
        // at (1,0) and one #99 letter at (2,0); the actor's cursor holds an Axe; the other player's inventory holds a letter
        var rig = NewRig();
        int other = 1 - slot;
        ushort chest = OpenChest(rig, slot);
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)other), rig.IdOf(chest)));
        ushort a = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 5));
        ushort full = rig.PlaceMail(chest, "letter", 1, 0, Address99, InvAutoPlaceRig.Ids(200, 20));
        ushort one99 = rig.PlaceMail(chest, "letter", 2, 0, Address99, InvAutoPlaceRig.Ids(300, 1));
        ushort cursor = rig.CursorOf(slot);
        ushort axe = rig.PlaceItem(cursor, "axe", 0, 0, 1);
        ushort othersLetter = rig.PlaceMail(rig.InventoryOf(other), "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(400, 1));
        if (reason == "stale_layout")
        {
            rig.Header(chest).LastReplaceVersion = rig.Header(chest).Version + 1;
        }

        Op op = reason switch
        {
            "bad_request" => MoveOp(rig, chest, a, chest, 3, 0, pad0: 1),
            "not_open" => PickOp(rig, rig.InventoryOf(other), othersLetter),
            "stale_entry" => MoveOpRaw(rig.IdOf(cursor).Value, rig.Entries[a].Id.Value, rig.IdOf(chest).Value, 3, 0, false, 0, rig.Header(chest).Version),
            "cursor_busy" => PickOp(rig, chest, a),
            "bad_count" => MoveOp(rig, chest, a, chest, 3, 0, count: 6),
            "stale_layout" => MoveOp(rig, chest, a, chest, 3, 0, baseVersion: rig.Header(chest).Version),
            "out_of_bounds" => MoveOp(rig, cursor, axe, chest, 7, 3),
            "blocked" => MoveOp(rig, cursor, axe, chest, 0, 0),
            _ => MoveOp(rig, chest, one99, chest, 1, 0),
        };
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert: the reply alone in its call, sent to the actor only (AssertRejected); the other subscriber is sent nothing
        AssertRejected(rig, log, ReasonNamed(reason), before, slot);
        Assert.Empty(InvEventRig.DeliveredTo(log, other));
        Assert.Equal(20, rig.Entries[full].Count);
    }

    [Theory]
    [Trait("Id", "H-R7-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void ChestToInventory_TheOtherSubscriberOfTheChest_ReceivesOnlyTheChestsRecordAndSeal(int slot)
    {
        // Arrange: both players have the chest open; the chest holds E
        var rig = NewRig();
        int other = 1 - slot;
        ushort chest = OpenChest(rig, slot);
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)other), rig.IdOf(chest)));
        ushort inventory = rig.InventoryOf(slot);
        ushort e = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 2));

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, chest, e, inventory, 0, 0));

        // Assert: the other slot gets the chest's Removed and its seal (actorId = the mover), not the actor's inventory records; the
        // actor gets both containers' records and seals and the reply
        AssertOk(log);
        EventEntry[] toOther = InvEventRig.DeliveredTo(log, other);
        Assert.Equal(new[] { InvEventKinds.Removed, InvEventKinds.Version }, toOther.Select(x => x.Kind).ToArray());
        Assert.Equal(rig.IdOf(chest).Value, toOther[0].PayloadAs<InvRemovedPayload>().Container);
        InvVersionPayload seal = toOther[1].PayloadAs<InvVersionPayload>();
        Assert.Equal((rig.IdOf(chest).Value, rig.Player(slot).Value), (seal.Container, seal.ActorId));
        Assert.Equal(
            new[] { InvEventKinds.Removed, InvEventKinds.Inserted, InvEventKinds.Version, InvEventKinds.Version, EventKind.OpReply },
            InvEventRig.DeliveredTo(log, slot).Where(x => (x.Flags & EventFlags.Continuation) == 0).Select(x => x.Kind).ToArray());
    }

    // ------------------------------------------------------------------ AC 9: count (ADR-0009 section 1; TD-105 item 3, TD-150)

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    public void Count_ZeroOrTheTotal_IsAWholeMove_TheEntryKeepsItsId(int slot, byte count)
    {
        // Arrange: E = 5 letters in the actor's inventory, moved into the actor's open chest
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort e = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 5));
        EntryId id = rig.Entries[e].Id;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, e, chest, 4, 2, count: count));

        // Assert
        AssertOk(log);
        Assert.Equal(id, rig.Entries[e].Id);
        Assert.Equal((chest, (byte)5), (rig.Entries[e].ContainerSlot, rig.Entries[e].Count));
        Assert.Equal(InvAutoPlaceRig.Ids(100, 5), rig.IdsOf(e));
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 6)]
    [InlineData(0, 255)]
    [InlineData(1, 255)]
    public void Count_AboveTheTotal_BadCount_NothingChanges(int slot, byte count)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort e = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 5));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, e, chest, 4, 2, count: count));

        // Assert
        AssertRejected(rig, log, InvReason.BadCount, before, slot);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    public void Count_BelowTheTotal_BadCount_Until008_NothingChanges(int slot, byte count)
    {
        // Arrange: until 008 — story 008's split-and-place replaces this rejection
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort e = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 5));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, e, chest, 4, 2, count: count));

        // Assert
        AssertRejected(rig, log, InvReason.BadCount, before, slot);
    }

    // ------------------------------------------------------------------ implicit merge fit (TD-150 (2)) and the arena reservation

    /// <summary>
    /// Target T = 15 letters to #3 in the chest (room 5 under <c>effMax</c> 20), source S on the cursor: S = 5 (exactly the room) and
    /// S = 4 (room − 1) merge whole (S freed); S = 6 (room + 1) moves 5 and S keeps 1 unit, its id and its cursor cell. Both containers
    /// bump once either way.
    /// </summary>
    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(0, 6)]
    [InlineData(1, 6)]
    public void ImplicitMerge_MovesMinOfTheCountAndTheRoom_TheRemainderStaysAtTheSourceWithItsId(int slot, int sourceUnits)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort target = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(100, 15));
        ushort cursor = rig.CursorOf(slot);
        ushort source = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, sourceUnits));
        EntryId sourceId = rig.Entries[source].Id;
        uint chestV = rig.Header(chest).Version;
        uint cursorV = rig.Header(cursor).Version;
        int moved = Math.Min(sourceUnits, 5);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, source, chest, 2, 1));

        // Assert: the oldest units move; the target keeps its placement
        AssertOk(log);
        Assert.Equal(15 + moved, rig.Entries[target].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(1, moved).Concat(InvAutoPlaceRig.Ids(100, 15)).ToArray(), rig.IdsOf(target));
        Assert.Equal((2, 1), (rig.Entries[target].X, rig.Entries[target].Y));
        Assert.Equal(moved, RecordHeads(log).Single(x => x.Kind == InvEventKinds.Merged).PayloadAs<InvMergedPayload>().AddedCount);
        if (sourceUnits > 5)
        {
            Assert.Equal(sourceId, rig.Entries[source].Id);
            Assert.Equal((cursor, (byte)(sourceUnits - 5)), (rig.Entries[source].ContainerSlot, rig.Entries[source].Count));
            Assert.Equal(InvAutoPlaceRig.Ids(1 + 5, sourceUnits - 5), rig.IdsOf(source));
        }
        else
        {
            Assert.False(rig.Entries.TryGetSlot(sourceId, out _));
            Assert.Equal(0, rig.Header(cursor).EntryCount);
        }

        Assert.Equal(chestV + 1, rig.Header(chest).Version);
        Assert.Equal(cursorV + 1, rig.Header(cursor).Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ImplicitMerge_OntoAFullTarget_StackFull_NothingChanges(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        _ = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(100, 20));
        ushort cursor = rig.CursorOf(slot);
        ushort source = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 2));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, source, chest, 2, 1));

        // Assert
        AssertRejected(rig, log, InvReason.StackFull, before, slot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ImplicitMerge_InsideOneContainer_ItemPartialFit_OneContainerPlusOne_NoArenaChange(int slot)
    {
        // Arrange: planks 18 at (0,0) and 5 at (3,0) in the actor's inventory (plank maxStack 20)
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort target = rig.PlaceItem(inventory, "plank", 0, 0, 18);
        ushort source = rig.PlaceItem(inventory, "plank", 3, 0, 5);
        uint v = rig.Header(inventory).Version;
        int liveBlocks = rig.Inventory.MailIds.LiveBlocks;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, source, inventory, 0, 0));

        // Assert: 2 move, 3 stay at (3,0)
        AssertOk(log);
        Assert.Equal(20, rig.Entries[target].Count);
        Assert.Equal((inventory, (byte)3, (byte)0, (byte)3), (rig.Entries[source].ContainerSlot, rig.Entries[source].X, rig.Entries[source].Y, rig.Entries[source].Count));
        Assert.Equal(v + 1, rig.Header(inventory).Version);
        Assert.Single(Seals(log));
        Assert.Equal(liveBlocks, rig.Inventory.MailIds.LiveBlocks);
        Assert.Equal(23, rig.Inventory.CountItem(new[] { rig.IdOf(inventory) }, rig.ItemIndex("plank")));
    }

    /// <summary>An item def's <c>maxStack</c> may exceed 255 (no content rule caps it) but a count is a u8: the room is capped at 255.</summary>
    [Theory]
    [InlineData(0, 250, 5)]
    [InlineData(1, 250, 5)]
    [InlineData(0, 254, 1)]
    [InlineData(1, 254, 1)]
    public void ImplicitMerge_ItemMaxStackAbove255_MovesUpTo255(int slot, byte targetCount, int expectedMoved)
    {
        // Arrange: a bigstack (maxStack 300) target in the chest, 10 on the cursor
        var rig = NewRig(PrecedenceContent());
        ushort chest = OpenChest(rig, slot);
        ushort target = rig.PlaceItem(chest, "bigstack", 0, 0, targetCount);
        ushort cursor = rig.CursorOf(slot);
        ushort source = rig.PlaceItem(cursor, "bigstack", 0, 0, 10);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, source, chest, 0, 0));

        // Assert
        AssertOk(log);
        Assert.Equal(255, rig.Entries[target].Count);
        Assert.Equal(10 - expectedMoved, rig.Entries[source].Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ImplicitMerge_ItemMaxStackAbove255_OntoA255Target_StackFull_NothingChanges(int slot)
    {
        // Arrange
        var rig = NewRig(PrecedenceContent());
        ushort chest = OpenChest(rig, slot);
        _ = rig.PlaceItem(chest, "bigstack", 0, 0, 255);
        ushort cursor = rig.CursorOf(slot);
        ushort source = rig.PlaceItem(cursor, "bigstack", 0, 0, 10);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, source, chest, 0, 0));

        // Assert
        AssertRejected(rig, log, InvReason.StackFull, before, slot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ImplicitMerge_ArenaCannotReserveTheUnionBlock_CapacityExhausted_TheLetterStaysOnTheCursor_AFreeCellStillWorks(int slot)
    {
        // Arrange: a minimum-size arena; a 2-letter #3 stack in the chest and a #3 letter on the cursor, then letters spawned into the
        // Intake until Spawn is refused at its arena reservation, so the merge's 3-unit union block cannot be reserved
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        ushort chest = OpenChest(rig, slot);
        ushort stack = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 2));
        ushort cursor = rig.CursorOf(slot);
        ushort letter = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 1));
        ExhaustArenaThroughIntakeSpawns(rig);
        Assert.False(rig.Inventory.MailIds.CanReserve(3));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] merge = rig.Run(rig.Player(slot), MoveOp(rig, cursor, letter, chest, 0, 0));

        // Assert: refused at the reservation step (a dry run: SideEffects includes the check ring writes)
        AssertRejected(rig, merge, InvReason.CapacityExhausted, before, slot);
        Assert.Equal(cursor, rig.Entries[letter].ContainerSlot);
        Assert.Equal(2, rig.Entries[stack].Count);

        // Act: the same letter onto a free cell needs no block
        EventEntry[] free = rig.Run(rig.Player(slot), MoveOp(rig, cursor, letter, chest, 5, 3));

        // Assert
        AssertOk(free);
        Assert.Equal((chest, (byte)5, (byte)3), (rig.Entries[letter].ContainerSlot, rig.Entries[letter].X, rig.Entries[letter].Y));
    }

    // ------------------------------------------------------------------ the stub rig's moved Pick / Move expectations

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PickAndMove_AnAllocatedButUnplacedEntry_StaleEntry_NothingChanges(int slot)
    {
        // Arrange: a letter released from the actor's hotbar by DetachForThrow is live but in no container
        var rig = NewRig();
        ushort hotbar = rig.HotbarOf(slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort e = rig.PlaceMail(hotbar, "letter", 2, 0, Address3, InvAutoPlaceRig.Ids(100, 1));
        EntryId id = rig.Entries[e].Id;
        DetachResult detached = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.DetachForThrow(PlayerSlot.FromValue((byte)slot), id, 1));
        Assert.Equal((InvReason)0, detached.Reason);
        Assert.True(rig.Entries[e].IsLive);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[e].ContainerSlot);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] pick = rig.Run(rig.Player(slot), PickOpRaw(rig.IdOf(hotbar).Value, id.Value));
        EventEntry[] move = rig.Run(rig.Player(slot), MoveOpRaw(rig.IdOf(hotbar).Value, id.Value, rig.IdOf(inventory).Value, 0, 0, false, 0, 0));

        // Assert
        AssertRejected(rig, pick, InvReason.StaleEntry, before, slot);
        AssertRejected(rig, move, InvReason.StaleEntry, before, slot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PickAndMove_AnIdMergedAway_StaleEntry_TheActorsOwnUnmappedTempId_StaleEntry(int slot)
    {
        // Arrange: a cursor letter merged onto a chest stack by a real move (its entry is freed)
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort cursor = rig.CursorOf(slot);
        _ = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 2));
        ushort gone = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 1));
        uint goneId = rig.Entries[gone].Id.Value;
        AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, gone, chest, 0, 0)));
        uint temp = EntryId.Temp(PlayerSlot.FromValue((byte)slot), 5).Value;
        uint chestId = rig.IdOf(chest).Value;
        List<long> before = SideEffects(rig);

        // Act + Assert
        AssertRejected(rig, rig.Run(rig.Player(slot), PickOpRaw(rig.IdOf(cursor).Value, goneId)), InvReason.StaleEntry, before, slot);
        AssertRejected(rig, rig.Run(rig.Player(slot), MoveOpRaw(rig.IdOf(cursor).Value, goneId, chestId, 3, 0, false, 0, 0)), InvReason.StaleEntry, before, slot);
        AssertRejected(rig, rig.Run(rig.Player(slot), PickOpRaw(chestId, temp)), InvReason.StaleEntry, before, slot);
        AssertRejected(rig, rig.Run(rig.Player(slot), MoveOpRaw(chestId, temp, chestId, 3, 0, false, 0, 0)), InvReason.StaleEntry, before, slot);
    }

    // ------------------------------------------------------------------ helpers

    private static InvAutoPlaceRig NewRig(ContentSet? content = null) => new(content: content, joinPlayerB: true);

    /// <summary>A fresh 8 x 4 <paramref name="def"/> grid with no owner, opened as <paramref name="slot"/>'s external.</summary>
    private static ushort OpenChest(InvAutoPlaceRig rig, int slot, string def = "player_inventory")
    {
        ushort chest = rig.CreateGrid(8, 4, def);
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)slot), rig.IdOf(chest)));
        return chest;
    }

    /// <summary>The H-R4-1 arrangement: the slot's open 8 x 4 chest with a Medium at (6,2) and 4 letters to #3 at (0,0).</summary>
    private static (ushort Chest, ushort Medium, ushort Stack) HR41Chest(InvAutoPlaceRig rig, int slot)
    {
        ushort chest = OpenChest(rig, slot);
        ushort medium = rig.PlaceMail(chest, "medium", 6, 2, Address99, InvAutoPlaceRig.Ids(500, 1));
        ushort stack = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 4));
        return (chest, medium, stack);
    }

    /// <summary>H-R4-1 b–e: a one-unit <paramref name="kind"/> on the cursor placed at (x, y) of the arrangement; a reject that changes nothing.</summary>
    private static void HR41Reject(int slot, string kind, int x, int y, bool rotated, InvReason expected)
    {
        // Arrange
        var rig = NewRig();
        (ushort chest, ushort medium, ushort stack) = HR41Chest(rig, slot);
        ushort cursor = rig.CursorOf(slot);
        ushort held = rig.PlaceMail(cursor, kind, 0, 0, Address3, InvAutoPlaceRig.Ids(10, 1));
        uint chestV = rig.Header(chest).Version;
        uint cursorV = rig.Header(cursor).Version;
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, held, chest, x, y, rotated));

        // Assert: the chest version and the cursor unchanged (and everything else)
        AssertRejected(rig, log, expected, before, slot);
        Assert.Equal(chestV, rig.Header(chest).Version);
        Assert.Equal(cursorV, rig.Header(cursor).Version);
        Assert.Equal(cursor, rig.Entries[held].ContainerSlot);
        Assert.Equal((chest, chest, (byte)4), (rig.Entries[medium].ContainerSlot, rig.Entries[stack].ContainerSlot, rig.Entries[stack].Count));
    }

    /// <summary>
    /// The precedence rows up to <c>stale_layout</c>: a crate to the output-only chest at (7,2), count 2, baseVersion 0 against
    /// lastReplaceVersion 1 (the seam), with each earlier step broken as the row needs: an unissued entry id (<c>bad_request</c>), the
    /// chest not opened (<c>not_open</c>), the inventory's crate named from the cursor (<c>stale_entry</c>), the cursor's crate with
    /// count 2 (<c>bad_count</c>), then count 0 (<c>stale_layout</c>). Every row from <c>stale_entry</c> on has the chest open.
    /// </summary>
    private static Op EverythingWrong(InvAutoPlaceRig rig, int slot, InvReason step, ushort cursor, ushort inventory)
    {
        ushort chest = rig.CreateGrid(8, 4, OutputOnlyDef);
        _ = rig.PlaceMail(chest, "medium", 6, 2, Address99, InvAutoPlaceRig.Ids(500, 1));
        ushort crate = rig.PlaceItem(cursor, "crate", 0, 0, 1);
        ushort notOnCursor = rig.PlaceItem(inventory, "crate", 0, 0, 1); // live, but not in from
        rig.Header(chest).LastReplaceVersion = 1;
        bool open = (ushort)step >= (ushort)InvReason.StaleEntry && step != (InvReason)CoreReason.BadRequest;
        if (open)
        {
            Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)slot), rig.IdOf(chest)));
        }

        uint from = rig.IdOf(cursor).Value;
        uint to = rig.IdOf(chest).Value;
        return step switch
        {
            (InvReason)CoreReason.BadRequest => MoveOpRaw(from, rig.Entries.LastId + 1, to, 7, 2, false, 2, 0), // unissued id
            InvReason.NotOpen => MoveOpRaw(from, rig.Entries[notOnCursor].Id.Value, to, 7, 2, false, 2, 0),
            InvReason.StaleEntry => MoveOpRaw(from, rig.Entries[notOnCursor].Id.Value, to, 7, 2, false, 2, 0),
            InvReason.BadCount => MoveOpRaw(from, rig.Entries[crate].Id.Value, to, 7, 2, false, 2, 0),
            _ => MoveOpRaw(from, rig.Entries[crate].Id.Value, to, 7, 2, false, 0, 0),
        };
    }

    /// <summary>The rule-level precedence rows after <c>reserved_slot</c> (story 006's arrangements, driven through the op).</summary>
    private static Op RuleRow(InvAutoPlaceRig rig, int slot, InvReason step, ushort cursor)
    {
        bool outputOnly = step is InvReason.OutOfBounds or InvReason.NotCarryable or InvReason.NotAllowed;
        ushort chest = OpenChest(rig, slot, outputOnly ? OutputOnlyDef : "player_inventory");
        _ = rig.PlaceMail(chest, "medium", 6, 2, Address99, InvAutoPlaceRig.Ids(500, 1));
        _ = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 4));
        switch (step)
        {
            case InvReason.OutOfBounds:
            {
                // Crate (2 x 2) at (7,2): x 7–8 leaves the grid and (7,2),(7,3) are the Medium's
                ushort crate = rig.PlaceItem(cursor, "crate", 0, 0, 1);
                return MoveOp(rig, cursor, crate, chest, 7, 2);
            }

            case InvReason.NotCarryable:
            {
                // Crate exactly on the Medium's cells (blocked: different key), into an output-only hand-carried def
                ushort crate = rig.PlaceItem(cursor, "crate", 0, 0, 1);
                return MoveOp(rig, cursor, crate, chest, 6, 2);
            }

            case InvReason.NotAllowed:
            case InvReason.Blocked:
            {
                // A carryable plank on the letter stack: blocked, and not allowed into the output-only def
                ushort plank = rig.PlaceItem(cursor, "plank", 0, 0, 1);
                return MoveOp(rig, cursor, plank, chest, 0, 0);
            }

            default:
            {
                ushort full = rig.PlaceMail(chest, "letter", 1, 0, Address99, InvAutoPlaceRig.Ids(200, 20));
                Assert.Equal(20, rig.Entries[full].Count);
                ushort letter = rig.PlaceMail(cursor, "letter", 0, 0, Address99, InvAutoPlaceRig.Ids(1, 1));
                return MoveOp(rig, cursor, letter, chest, 1, 0);
            }
        }
    }

    /// <summary>
    /// The M-1a content plus <see cref="OutputOnlyDef"/>, a <c>crate</c> item (2 x 2, <c>carryByHand: false</c>, Light, so the weight
    /// memo counts it without the Bulk assert), a <c>bigstack</c> item (1 x 1, <c>maxStack</c> 300), and the hotbar def with
    /// <c>acceptsInsert: false</c> (the <c>reserved_slot</c> row's <c>not_allowed</c>).
    /// </summary>
    private static ContentSet PrecedenceContent()
    {
        ContentSet m1a = BuiltInContent.CreateM1a();
        ContainerDef inventoryDef = m1a.Containers[PlacementFixture.Index(m1a.Containers, "player_inventory")];
        ImmutableArray<ContainerDef> containers = m1a.Containers.AsSpan().ToArray()
            .Select(c => c.Id == "hotbar" ? c with { AcceptsInsert = false } : c)
            .Append(inventoryDef with { Id = OutputOnlyDef, GridW = 8, GridH = 4, AcceptsInsert = false })
            .ToImmutableArray();
        ItemDef plank = m1a.Items[PlacementFixture.Index(m1a.Items, "plank")];
        ImmutableArray<ItemDef> items = m1a.Items.AsSpan().ToArray()
            .Append(plank with { Id = "crate", Category = ItemCategory.Tool, GridW = 2, GridH = 2, MaxStack = 1, CarryByHand = false })
            .Append(plank with { Id = "bigstack", MaxStack = 300 })
            .ToImmutableArray();
        return new ContentSet(
            m1a.Balance, m1a.ResolvedStatBounds, new DefTable<ContainerDef>(containers), new DefTable<ItemDef>(items), m1a.MailKinds, m1a.DestinationTypes, m1a.MailMixes);
    }

    /// <summary>Spawns single letters into the Intake (one tick) until <see cref="IInventoryApi.Spawn"/> places none.</summary>
    private static void ExhaustArenaThroughIntakeSpawns(InvAutoPlaceRig rig)
    {
        DefIndex letter = rig.KindIndex("letter");
        ContainerId intake = rig.Inventory.IntakeContainer;
        var one = new MailId[1];
        uint next = 1_000_000;
        int placed = IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            int n = 0;
            while (true)
            {
                one[0] = new MailId(next++);
                if (rig.Inventory.Spawn(intake, letter, 9, 0, one, MergePolicy.NoMerge).Placed != 1)
                {
                    return n;
                }

                n++;
            }
        });
        Assert.True(placed > 0);
        Assert.True(rig.Header(rig.Intake).FreeCells > 0); // refused at the arena, not for room
        Assert.False(rig.Inventory.MailIds.CanReserve(1));
    }

    private static InvReason ReasonNamed(string name) => name == "bad_request"
        ? (InvReason)CoreReason.BadRequest
        : Enum.GetValues<InvReason>().Single(r => InvReasons.Name(r) == name);

    private static Op PickOp(InvAutoPlaceRig rig, ushort from, ushort entry) => PickOpRaw(rig.IdOf(from).Value, rig.Entries[entry].Id.Value);

    private static Op PickOpRaw(uint from, uint entryId)
    {
        var op = new Op { Kind = InvOpKinds.Pick, ReqId = 71 };
        op.SetPayload(new PickPayload { From = from, EntryId = entryId });
        return op;
    }

    /// <summary>A <c>move</c> of the entry at pool slot <paramref name="entry"/>; <c>baseVersion</c> defaults to the destination's current version.</summary>
    private static Op MoveOp(
        InvAutoPlaceRig rig, ushort from, ushort entry, ushort to, int x, int y, bool rotated = false, byte count = 0, uint? baseVersion = null, byte pad0 = 0) =>
        MoveOpRaw(rig.IdOf(from).Value, rig.Entries[entry].Id.Value, rig.IdOf(to).Value, x, y, rotated, count, baseVersion ?? rig.Header(to).Version, pad0);

    private static Op MoveOpRaw(uint from, uint entryId, uint to, int x, int y, bool rotated, byte count, uint baseVersion, byte pad0 = 0)
    {
        var op = new Op { Kind = InvOpKinds.Move, ReqId = 72 };
        op.SetPayload(new MovePayload
        {
            From = from, EntryId = entryId, To = to, BaseVersion = baseVersion, X = (short)x, Y = (short)y, Rotated = rotated ? (byte)1 : (byte)0,
            Count = count, Pad0 = pad0,
        });
        return op;
    }

    private static Reply AssertOk(EventEntry[] log)
    {
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.Equal(CoreReason.Ok, reply.Reason);
        return reply;
    }

    /// <summary>
    /// The reject contract (TD-105 item 5; ADR-0007 l.128; ADR-0009 section 3): the reason; no inventory record and no <c>InvVersion</c>
    /// seal anywhere in the tick; the reply alone in its (handler's) call scope and sent to the actor's slot alone; and every recorded
    /// side effect as before.
    /// </summary>
    private static Reply AssertRejected(InvAutoPlaceRig rig, EventEntry[] log, InvReason reason, List<long> before, int actorSlot)
    {
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.Equal(ReplyResult.Rejected, reply.Result);
        Assert.Equal((ushort)reason, reply.Reason);
        Assert.Empty(InventoryRecords(log));
        EventEntry entry = Assert.Single(log, e => e.Kind == EventKind.OpReply);
        Assert.NotEqual(0u, entry.CallSeq);
        Assert.Single(log, e => e.CallSeq == entry.CallSeq);
        Assert.Equal(1 << actorSlot, entry.Audience);
        Assert.Equal(before, SideEffects(rig));
        return reply;
    }

    private static EventEntry[] InventoryRecords(EventEntry[] log) =>
        log.Where(e => e.Kind is >= InvEventKinds.Inserted and <= InvEventKinds.ItemsLost).ToArray();

    /// <summary>The inventory records and seals that start a record (mail-id run continuations dropped), in log order.</summary>
    private static EventEntry[] RecordHeads(EventEntry[] log) =>
        InventoryRecords(log).Where(e => (e.Flags & EventFlags.Continuation) == 0).ToArray();

    private static InvVersionPayload[] Seals(EventEntry[] log) =>
        log.Where(e => e.Kind == InvEventKinds.Version).Select(e => e.PayloadAs<InvVersionPayload>()).ToArray();

    /// <summary>
    /// Everything a pick or move could change or record: every container header (version and lastReplaceVersion included), occupancy
    /// owner, subscriber mask and per-slot external; every live entry with its mail ids and its StackKey-index membership; the pool's
    /// high-water; the arena's units, blocks and free and peak blocks per class; weight P per slot; <c>CountItem</c> of every item def
    /// per container; the negative-fit cache's private arrays; the check ring's writes; and the entity and mail id allocators.
    /// </summary>
    private static List<long> SideEffects(InvAutoPlaceRig rig)
    {
        var values = new List<long>();
        ContainerTable containers = rig.Inventory.Containers;
        for (int s = 0; s < containers.LiveCount; s++)
        {
            ContainerHeader h = containers[(ushort)s];
            values.AddRange(new long[] { h.Id.Value, h.Version, h.LastReplaceVersion, h.StatEpoch, h.EntryCount, h.FreeCells, h.NextArrivalSeq });
            values.Add(rig.Inventory.Subscribers.Mask((ushort)s));
            for (int y = 0; y < h.H; y++)
            {
                for (int x = 0; x < h.W; x++)
                {
                    values.Add(rig.Inventory.Occupancy.OwnerAt((ushort)s, h.W, x, y));
                }
            }

            for (int i = 0; i < rig.Content.Items.Count; i++)
            {
                values.Add(rig.Inventory.CountItem(new[] { h.Id }, DefIndex.FromValue((ushort)i)));
            }
        }

        for (byte slot = 0; slot < PlayerSlot.Count; slot++)
        {
            values.Add(rig.Inventory.OpenSet.ExternalOf(PlayerSlot.FromValue(slot)).Value);
            values.Add(rig.Inventory.WeightPoints(PlayerSlot.FromValue(slot)));
        }

        values.Add(rig.Entries.LastId);
        values.Add(rig.Entries.LiveCount);
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            EntryRecord r = rig.Entries[(ushort)s];
            if (r.IsLive)
            {
                values.AddRange(new long[] { s, r.Id.Value, r.Count, r.ContainerSlot, r.X, r.Y, r.Rotated ? 1 : 0, r.ArrivalSeq, r.MailIds.Raw });
                values.Add(rig.Index.Contains((ushort)s) ? 1 : 0);
                values.AddRange(rig.IdsOf((ushort)s).Select(id => (long)id));
            }
        }

        MailIdArena arena = rig.Inventory.MailIds;
        values.Add(arena.LiveUnits);
        values.Add(arena.LiveBlocks);
        for (int c = 0; c < MailIdArena.ClassCount; c++)
        {
            values.Add(arena.FreeBlocks(c));
            values.Add(arena.PeakLiveBlocks(c));
        }

        object cache = rig.Inventory.AutoPlace.NoFitCache;
        foreach (FieldInfo f in cache.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Where(f => f.FieldType.IsArray))
        {
            foreach (object v in (Array)f.GetValue(cache)!)
            {
                values.Add(Convert.ToInt64(v));
            }
        }

        SimCheckRing ring = rig.Host.World.CheckRing;
        values.Add(ring.Count + (long)ring.Overwritten + ring.Suppressed(CheckCode.InventoryArenaExhausted));
        for (int c = 0; c < 256; c++)
        {
            values.Add(rig.Host.Services.EntityIds.Peek((EntityClass)c));
        }

        values.Add(rig.Host.Services.MailIds.Peek());
        return values;
    }
}
