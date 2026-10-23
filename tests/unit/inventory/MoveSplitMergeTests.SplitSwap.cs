using System.Collections.Immutable;
using System.Reflection;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

public partial class MoveSplitMergeTests
{
    // ------------------------------------------------------------------ H-R4-2 c: a split keeps its own cells

    /// <summary>A = 10 letters at the chest's (0,0): <c>move {A, count 4 → (0,0)}</c> is <c>blocked</c> (the remainder still occupies the cell).</summary>
    [Theory]
    [Trait("Id", "H-R4-2")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR42c_TenLettersAt00_SplitFourOntoItsOwnCells_Blocked_NothingChanges(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort a = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 10));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, chest, a, chest, 0, 0, count: 4));

        // Assert
        AssertRejected(rig, log, InvReason.Blocked, before, slot, chest, chest);
        Assert.Equal((chest, (byte)10), (rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
    }

    /// <summary>A 1 x 2 log stack (10) at (0,0): a split onto (0,1) overlaps half of its own footprint and is <c>blocked</c>; the whole move there is not.</summary>
    [Theory]
    [Trait("Id", "H-R4-2")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR42c_LogsSplitAcrossHalfTheirOwnFootprint_Blocked_TheWholeMoveThereIsOk(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort logs = rig.PlaceItem(chest, "log", 0, 0, 10);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] split = rig.Run(rig.Player(slot), MoveOp(rig, chest, logs, chest, 0, 1, count: 3));

        // Assert
        AssertRejected(rig, split, InvReason.Blocked, before, slot, chest, chest);

        // Act
        EventEntry[] whole = rig.Run(rig.Player(slot), MoveOp(rig, chest, logs, chest, 0, 1));

        // Assert
        AssertOk(whole);
        Assert.Equal(((byte)0, (byte)1, (byte)10), (rig.Entries[logs].X, rig.Entries[logs].Y, rig.Entries[logs].Count));
    }

    // ------------------------------------------------------------------ H-R4-2 d: the hotbar swap

    /// <summary>
    /// Hotbar slot 1 holds X, the cursor holds Y (whole move): the slot takes Y and the cursor takes X, both keeping their ids, counts
    /// and mail ids. Rows: 3 letters onto the Axe (the GDD case), the Axe onto 4 letters to #5, 3 letters to #3 onto 4 letters to #5
    /// (a StackKey mismatch). The cursor and the hotbar each bump once and no other container does; records Removed (hotbar),
    /// Removed (cursor), Inserted (hotbar), Inserted (cursor), then the cursor's and the hotbar's seals; the reply carries the cursor
    /// (A) then the hotbar (B).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R4-2")]
    [InlineData(0, "letters3", "axe")]
    [InlineData(1, "letters3", "axe")]
    [InlineData(0, "axe", "letters5")]
    [InlineData(1, "axe", "letters5")]
    [InlineData(0, "letters3", "letters5")]
    [InlineData(1, "letters3", "letters5")]
    public void HR42d_CursorEntryOntoAnOccupiedNonMergeableHotbarSlot_Swaps_EachContainerPlusOne(int slot, string held, string inSlot)
    {
        // Arrange
        var rig = NewRig();
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort displaced = PlaceNamed(rig, hotbar, inSlot, 1, 0, firstId: 100);
        ushort mover = PlaceNamed(rig, cursor, held, 0, 0, firstId: 1);
        EntryRecord moverBefore = rig.Entries[mover];
        EntryRecord displacedBefore = rig.Entries[displaced];
        uint[] moverIds = rig.IdsOf(mover);
        uint[] displacedIds = rig.IdsOf(displaced);
        uint[] versions = Versions(rig);
        uint lastId = rig.Entries.LastId;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, mover, hotbar, 1, 0));

        // Assert: the swap — same ids, counts and mail ids, exchanged places
        Reply reply = AssertOk(log);
        Assert.Equal((moverBefore.Id, hotbar, (byte)1, (byte)0, moverBefore.Count),
            (rig.Entries[mover].Id, rig.Entries[mover].ContainerSlot, rig.Entries[mover].X, rig.Entries[mover].Y, rig.Entries[mover].Count));
        Assert.Equal((displacedBefore.Id, cursor, (byte)0, (byte)0, displacedBefore.Count),
            (rig.Entries[displaced].Id, rig.Entries[displaced].ContainerSlot, rig.Entries[displaced].X, rig.Entries[displaced].Y, rig.Entries[displaced].Count));
        Assert.Equal(moverIds, rig.IdsOf(mover));
        Assert.Equal(displacedIds, rig.IdsOf(displaced));
        Assert.Equal(lastId, rig.Entries.LastId);
        Assert.Equal((1, 1), (rig.Header(hotbar).EntryCount, rig.Header(cursor).EntryCount));

        // Assert: occupancy — hotbar slot 1 is owned by the mover, cursor slot 0 by the displaced entry, and the row bitmaps hold
        // exactly those cells
        Occupancy occupancy = rig.Inventory.Occupancy;
        Assert.Equal(mover, occupancy.OwnerAt(hotbar, rig.Header(hotbar).W, 1, 0));
        Assert.Equal(displaced, occupancy.OwnerAt(cursor, rig.Header(cursor).W, 0, 0));
        Assert.Equal((1UL << 1, 1UL), (occupancy.RowBits(hotbar, 0), occupancy.RowBits(cursor, 0)));

        // Assert: exactly the cursor and the hotbar bump, once each
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[hotbar]++;
        Assert.Equal(expected, Versions(rig));
        Assert.Equal(
            new[] { InvEventKinds.Removed, InvEventKinds.Removed, InvEventKinds.Inserted, InvEventKinds.Inserted, InvEventKinds.Version, InvEventKinds.Version },
            RecordHeads(log).Select(e => e.Kind).ToArray());
        EventEntry[] heads = RecordHeads(log);
        Assert.Equal((rig.IdOf(hotbar).Value, displacedBefore.Id.Value), Removed(heads[0]));
        Assert.Equal((rig.IdOf(cursor).Value, moverBefore.Id.Value), Removed(heads[1]));
        Assert.Equal((rig.IdOf(hotbar).Value, moverBefore.Id.Value), Inserted(heads[2]));
        Assert.Equal((rig.IdOf(cursor).Value, displacedBefore.Id.Value), Inserted(heads[3]));
        Assert.Equal(new[] { rig.IdOf(cursor).Value, rig.IdOf(hotbar).Value }, Seals(log).Select(s => s.Container).ToArray());
        Assert.All(InventoryRecords(log), e => Assert.Equal(1 << slot, e.Audience));
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((versions[cursor] + 1, versions[hotbar] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.False(extra.Unchanged);
        AssertIndexesMatchARecount(rig);
    }

    /// <summary>
    /// The swap with <c>count</c> equal to the cursor entry's total is the whole move (story 008 AC Split-and-place: <c>count</c> 0 or
    /// <c>== total</c> stays the whole move), so it swaps exactly as <c>count</c> 0 does: in two identical worlds, one sending each
    /// count, both reply ok with the same versions and leave the same records and the same side effects. Rows: 3 letters onto the Axe
    /// (count 3), the Axe onto 4 letters to #5 (count 1).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R4-2")]
    [InlineData(0, "letters3", "axe")]
    [InlineData(1, "letters3", "axe")]
    [InlineData(0, "axe", "letters5")]
    [InlineData(1, "axe", "letters5")]
    public void HR42d_CursorSwapWithCountEqualToTheTotal_IsTheWholeMove_SwapsExactlyAsCountZero(int slot, string held, string inSlot)
    {
        // Arrange: two identical worlds
        var rigs = new[] { NewRig(), NewRig() };
        var movers = new ushort[2];
        var displaced = new ushort[2];
        for (int i = 0; i < 2; i++)
        {
            displaced[i] = PlaceNamed(rigs[i], rigs[i].HotbarOf(slot), inSlot, 1, 0, firstId: 100);
            movers[i] = PlaceNamed(rigs[i], rigs[i].CursorOf(slot), held, 0, 0, firstId: 1);
        }

        Assert.Equal(SideEffects(rigs[0]), SideEffects(rigs[1]));
        byte total = rigs[0].Entries[movers[0]].Count;
        ushort hotbar = rigs[0].HotbarOf(slot);
        ushort cursor = rigs[0].CursorOf(slot);

        // Act
        EventEntry[] whole = rigs[0].Run(rigs[0].Player(slot), MoveOp(rigs[0], cursor, movers[0], hotbar, 1, 0, count: total));
        EventEntry[] zero = rigs[1].Run(rigs[1].Player(slot), MoveOp(rigs[1], cursor, movers[1], hotbar, 1, 0));

        // Assert
        InvReplyExtra wholeExtra = AssertOk(whole).ExtraAs<InvReplyExtra>();
        InvReplyExtra zeroExtra = AssertOk(zero).ExtraAs<InvReplyExtra>();
        Assert.Equal((zeroExtra.ResultVersionA, zeroExtra.ResultVersionB), (wholeExtra.ResultVersionA, wholeExtra.ResultVersionB));
        Assert.Equal((hotbar, cursor), (rigs[0].Entries[movers[0]].ContainerSlot, rigs[0].Entries[displaced[0]].ContainerSlot));
        Assert.Equal(total, rigs[0].Entries[movers[0]].Count);
        Assert.Equal(RecordHeads(zero).Select(e => (e.Kind, e.Audience)), RecordHeads(whole).Select(e => (e.Kind, e.Audience)));
        Assert.Equal(SideEffects(rigs[1]), SideEffects(rigs[0]));
    }

    /// <summary>The same move onto an occupied <b>grid</b> cell (the actor's inventory) is <c>blocked</c>: no grid swap in v1.</summary>
    [Theory]
    [Trait("Id", "H-R4-2")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR42d_CursorLettersOntoTheAxeInAGridCell_Blocked_NoGridSwap(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        _ = rig.PlaceItem(inventory, "axe", 3, 0, 1);
        ushort letters = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, inventory, 3, 0));

        // Assert
        AssertRejected(rig, log, InvReason.Blocked, before, slot, cursor, inventory);
        Assert.Equal(cursor, rig.Entries[letters].ContainerSlot);
    }

    /// <summary>Slot 1 = 20 letters to #3 (full), the cursor 2 letters to #3: <c>stack_full</c>, no swap.</summary>
    [Theory]
    [Trait("Id", "H-R4-2")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR42d_SlotHoldsTwentyLettersTo3_CursorTwoTo3_StackFull_NoSwap(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort full = rig.PlaceMail(hotbar, "letter", 1, 0, Address3, InvAutoPlaceRig.Ids(100, 20));
        ushort letters = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 2));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, hotbar, 1, 0));

        // Assert
        AssertRejected(rig, log, InvReason.StackFull, before, slot, cursor, hotbar);
        Assert.Equal((hotbar, cursor), (rig.Entries[full].ContainerSlot, rig.Entries[letters].ContainerSlot));
    }

    /// <summary>
    /// What never swaps (GDD R4; story 008 Implementation Notes): a split from the cursor onto the occupied slot (<c>blocked</c>: the
    /// remainder keeps the cursor), a whole move from a non-cursor source (the inventory) onto it (<c>blocked</c>), and a move onto
    /// reserved slot 0 (<c>reserved_slot</c>, before anything else).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R4-2")]
    [InlineData(0, "split")]
    [InlineData(1, "split")]
    [InlineData(0, "inventory")]
    [InlineData(1, "inventory")]
    [InlineData(0, "slot0")]
    [InlineData(1, "slot0")]
    public void Swap_SplitOrNonCursorSourceOrReservedSlot_NoSwap_NothingChanges(int slot, string row)
    {
        // Arrange: the Axe in hotbar slot 1; 3 letters on the cursor and 3 in the inventory
        var rig = NewRig();
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort inventory = rig.InventoryOf(slot);
        _ = rig.PlaceItem(hotbar, "axe", 1, 0, 1);
        ushort onCursor = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
        ushort inInventory = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(10, 3));
        List<long> before = SideEffects(rig);
        (Op op, ushort from, InvReason expected) = row switch
        {
            "split" => (MoveOp(rig, cursor, onCursor, hotbar, 1, 0, count: 2), cursor, InvReason.Blocked),
            "inventory" => (MoveOp(rig, inventory, inInventory, hotbar, 1, 0), inventory, InvReason.Blocked),
            _ => (MoveOp(rig, cursor, onCursor, hotbar, 0, 0), cursor, InvReason.ReservedSlot),
        };

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert
        AssertRejected(rig, log, expected, before, slot, from, hotbar);
    }

    /// <summary>A cursor def admitting only mail: the displaced Axe may not go to the cursor, so the move stays <c>blocked</c>.</summary>
    [Theory]
    [Trait("Id", "H-R4-2")]
    [InlineData(0)]
    [InlineData(1)]
    public void Swap_DisplacedEntryFailsTheCursorWhitelist_StaysBlocked_NothingChanges(int slot)
    {
        // Arrange
        var rig = NewRig(MailOnlyCursorContent());
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort axe = rig.PlaceItem(hotbar, "axe", 1, 0, 1);
        ushort letters = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, hotbar, 1, 0));

        // Assert
        AssertRejected(rig, log, InvReason.Blocked, before, slot, cursor, hotbar);
        Assert.Equal((hotbar, cursor), (rig.Entries[axe].ContainerSlot, rig.Entries[letters].ContainerSlot));
    }

    // ------------------------------------------------------------------ Split-and-place

    /// <summary>
    /// A = 7 letters [1..7]; <c>move {A, count 3 → free cell}</c>: a new entry with the next entryId holds [1,2,3] at the cell, A keeps
    /// its id, position and [4..7]. Rows: inside the inventory, inventory → chest, cursor → chest (a place). Records Removed{from, A, 3},
    /// SplitFrom{from, A, new, 3}, Inserted{to, new, 3} (+ runs), then one seal per touched container, the source's first; each touched
    /// container bumps once and no other; the reply carries the source (A) then the destination (B).
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, "inventory", "inventory")]
    [InlineData(1, "inventory", "inventory")]
    [InlineData(0, "inventory", "chest")]
    [InlineData(1, "inventory", "chest")]
    [InlineData(0, "cursor", "chest")]
    [InlineData(1, "cursor", "chest")]
    public void Split_SevenLettersCountThree_NewEntryHolds123_AKeepsItsIdWith4To7_OneBumpPerTouchedContainer(int slot, string fromWhere, string toWhere)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort from = Where(rig, slot, fromWhere, chest);
        ushort to = Where(rig, slot, toWhere, chest);
        ushort a = rig.PlaceMail(from, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 7));
        EntryId aId = rig.Entries[a].Id;
        uint lastId = rig.Entries.LastId;
        uint[] versions = Versions(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, from, a, to, 5, 1, count: 3));

        // Assert: the new piece, with the next (never reused) id
        Reply reply = AssertOk(log);
        ushort piece = rig.EntryAt(to, 5, 1);
        Assert.NotEqual(Occupancy.Free, piece);
        Assert.NotEqual(a, piece);
        Assert.Equal(EntryId.Real(lastId + 1), rig.Entries[piece].Id);
        Assert.Equal(lastId + 1, rig.Entries.LastId);
        Assert.Equal((to, (byte)3, true, rig.KindIndex("letter").Value, Address3),
            (rig.Entries[piece].ContainerSlot, rig.Entries[piece].Count, rig.Entries[piece].IsMail, rig.Entries[piece].MailKind, rig.Entries[piece].AddressId));
        Assert.Equal(new uint[] { 1, 2, 3 }, rig.IdsOf(piece));

        // Assert: A keeps its id, cell and the newer ids
        Assert.Equal(aId, rig.Entries[a].Id);
        Assert.Equal((from, (byte)0, (byte)0, (byte)4), (rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y, rig.Entries[a].Count));
        Assert.Equal(new uint[] { 4, 5, 6, 7 }, rig.IdsOf(a));

        // Assert: records, seals, versions, reply
        EventEntry[] heads = RecordHeads(log);
        bool cross = from != to;
        Assert.Equal(
            cross
                ? new[] { InvEventKinds.Removed, InvEventKinds.SplitFrom, InvEventKinds.Inserted, InvEventKinds.Version, InvEventKinds.Version }
                : new[] { InvEventKinds.Removed, InvEventKinds.SplitFrom, InvEventKinds.Inserted, InvEventKinds.Version },
            heads.Select(e => e.Kind).ToArray());
        InvRemovedPayload removed = heads[0].PayloadAs<InvRemovedPayload>();
        Assert.Equal((rig.IdOf(from).Value, aId.Value, (byte)3), (removed.Container, removed.EntryId, removed.Count));
        InvSplitFromPayload split = heads[1].PayloadAs<InvSplitFromPayload>();
        Assert.Equal((rig.IdOf(from).Value, aId.Value, lastId + 1, (byte)3), (split.Container, split.EntryId, split.NewEntryId, split.Count));
        InvInsertedPayload inserted = heads[2].PayloadAs<InvInsertedPayload>();
        Assert.Equal((rig.IdOf(to).Value, lastId + 1, (byte)3, (byte)5, (byte)1), (inserted.Container, inserted.EntryId, inserted.Count, inserted.X, inserted.Y));
        Assert.All(InventoryRecords(log), e => Assert.Equal(1 << slot, e.Audience));
        Assert.Equal(cross ? new[] { rig.IdOf(from).Value, rig.IdOf(to).Value } : new[] { rig.IdOf(from).Value }, Seals(log).Select(s => s.Container).ToArray());
        uint[] expected = (uint[])versions.Clone();
        expected[from]++;
        expected[to] = versions[to] + 1;
        Assert.Equal(expected, Versions(rig));
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((versions[from] + 1, versions[to] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        AssertIndexesMatchARecount(rig);
    }

    /// <summary>
    /// The count boundaries on A = 7 letters [1..7] (inventory → chest): 1 and total − 1 (6) split off the 1 or 6 oldest ids into a new
    /// entry, the remainder keeping its id.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 6)]
    [InlineData(1, 6)]
    public void Split_CountOneAndTotalMinusOne_SplitsOffTheOldestIds_TheRemainderKeepsItsId(int slot, byte count)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort a = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 7));
        EntryId aId = rig.Entries[a].Id;

        // Act
        AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, inventory, a, chest, 4, 2, count: count)));

        // Assert
        ushort piece = rig.EntryAt(chest, 4, 2);
        Assert.NotEqual(Occupancy.Free, piece);
        Assert.NotEqual(aId, rig.Entries[piece].Id);
        Assert.Equal(InvAutoPlaceRig.Ids(1, count), rig.IdsOf(piece));
        Assert.Equal((aId, inventory, (byte)(7 - count)), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
        Assert.Equal(InvAutoPlaceRig.Ids(1u + count, 7 - count), rig.IdsOf(a));
        AssertIndexesMatchARecount(rig);
    }

    /// <summary>
    /// The other boundaries on A = 7 letters: the total (7) and 0 are story 007's whole move (A moves, keeping its id; no new entry);
    /// total + 1 (8) is <c>bad_count</c> and changes nothing.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, 7)]
    [InlineData(1, 7)]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 8)]
    [InlineData(1, 8)]
    public void Split_CountAtTheTotalOrZero_IsTheWholeMove_AboveTheTotal_BadCount(int slot, byte count)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort a = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 7));
        EntryId aId = rig.Entries[a].Id;
        uint lastId = rig.Entries.LastId;
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, a, chest, 4, 2, count: count));

        // Assert
        if (count > 7)
        {
            AssertRejected(rig, log, InvReason.BadCount, before, slot, inventory, chest);
            return;
        }

        AssertOk(log);
        Assert.Equal((aId, chest, (byte)4, (byte)2, (byte)7), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y, rig.Entries[a].Count));
        Assert.Equal(InvAutoPlaceRig.Ids(1, 7), rig.IdsOf(a));
        Assert.Equal(lastId, rig.Entries.LastId);
        Assert.DoesNotContain(RecordHeads(log), e => e.Kind == InvEventKinds.SplitFrom);
    }

    /// <summary>An item split (10 planks, count 4, inside the inventory): the new entry is a plank stack of 4, A keeps 6; no arena block.</summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0)]
    [InlineData(1)]
    public void Split_ItemStack_NewPlankEntryOfFour_AKeepsSix_NoArenaChange(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort planks = rig.PlaceItem(inventory, "plank", 0, 0, 10);
        int liveBlocks = rig.Inventory.MailIds.LiveBlocks;

        // Act
        AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, inventory, planks, inventory, 3, 1, count: 4)));

        // Assert
        ushort piece = rig.EntryAt(inventory, 3, 1);
        Assert.NotEqual(Occupancy.Free, piece);
        Assert.Equal((false, rig.ItemIndex("plank").Value, (byte)4), (rig.Entries[piece].IsMail, rig.Entries[piece].ItemId, rig.Entries[piece].Count));
        Assert.Equal((byte)6, rig.Entries[planks].Count);
        Assert.Equal(liveBlocks, rig.Inventory.MailIds.LiveBlocks);
        Assert.Equal(10, rig.Inventory.CountItem(new[] { rig.IdOf(inventory) }, rig.ItemIndex("plank")));
        AssertIndexesMatchARecount(rig);
    }

    /// <summary>
    /// A split onto a mergeable stack is a partial merge of <c>min(count, room)</c> with no new entry: T = 18 letters to #3 in the chest
    /// (room 2), A = 7 letters to #3 [1..7] in the inventory. count 1 (room − 1) and 2 (the room) move all of it, count 3 (room + 1)
    /// moves 2; A keeps the rest, its id and the newer ids; no entry id is spent; both containers +1.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    public void Split_OntoAMergeableStack_PartialMergeOfMinCountAndRoom_NoNewEntry(int slot, byte count)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort t = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(100, 18));
        ushort a = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 7));
        EntryId aId = rig.Entries[a].Id;
        uint lastId = rig.Entries.LastId;
        uint chestV = rig.Header(chest).Version;
        uint inventoryV = rig.Header(inventory).Version;
        int moved = Math.Min((int)count, 2);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, a, chest, 2, 1, count: count));

        // Assert
        Reply reply = AssertOk(log);
        Assert.Equal(18 + moved, rig.Entries[t].Count);
        Assert.Equal(InvAutoPlaceRig.Ids(1, moved).Concat(InvAutoPlaceRig.Ids(100, 18)).ToArray(), rig.IdsOf(t));
        Assert.Equal((aId, inventory, (byte)(7 - moved)), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
        Assert.Equal(InvAutoPlaceRig.Ids(1u + (uint)moved, 7 - moved), rig.IdsOf(a));
        Assert.Equal(lastId, rig.Entries.LastId);
        Assert.Equal(new[] { InvEventKinds.Removed, InvEventKinds.Merged, InvEventKinds.Version, InvEventKinds.Version }, RecordHeads(log).Select(e => e.Kind).ToArray());
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((inventoryV + 1, chestV + 1), (extra.ResultVersionA, extra.ResultVersionB));
        AssertIndexesMatchARecount(rig);
    }

    /// <summary>A split onto a full mergeable stack (T = 20 letters to #3) is <c>stack_full</c> and changes nothing.</summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0)]
    [InlineData(1)]
    public void Split_OntoAFullMergeableStack_StackFull_NothingChanges(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        _ = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(100, 20));
        ushort a = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 7));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, a, chest, 2, 1, count: 3));

        // Assert
        AssertRejected(rig, log, InvReason.StackFull, before, slot, inventory, chest);
    }

    /// <summary>
    /// The mail-ids arena cannot reserve the new entry's block (a minimum arena, the Intake filled with single letters until
    /// <c>Spawn</c> is refused at its reservation): a split from the cursor to a free chest cell is <c>capacity_exhausted</c> and the
    /// letters stay on the cursor (Mail Lead planning default pending Evan, following the pattern of Evan's 5:11 AM PT leash ruling; the
    /// code is ADR-0009's). The whole move to the same cell needs no block and succeeds.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0)]
    [InlineData(1)]
    public void Split_ArenaCannotReserveTheNewBlock_CapacityExhausted_TheLettersStayOnTheCursor_TheWholeMoveStillWorks(int slot)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        ushort chest = OpenChest(rig, slot);
        ushort cursor = rig.CursorOf(slot);
        ushort letters = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
        ExhaustArenaThroughIntakeSpawns(rig);
        Assert.False(rig.Inventory.MailIds.CanReserve(2));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] split = rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, chest, 5, 3, count: 2));

        // Assert
        AssertRejected(rig, split, InvReason.CapacityExhausted, before, slot, cursor, chest);
        Assert.Equal((cursor, (byte)3), (rig.Entries[letters].ContainerSlot, rig.Entries[letters].Count));

        // Act
        EventEntry[] whole = rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, chest, 5, 3));

        // Assert
        AssertOk(whole);
        Assert.Equal((chest, (byte)3), (rig.Entries[letters].ContainerSlot, rig.Entries[letters].Count));
    }

    /// <summary>
    /// The entry pool cannot give the new entry a slot (capacity 3, all live) or an id (the last id issued is
    /// <see cref="EntryPool.MaxRealId"/>, through <see cref="InventorySettings.EntryInitialLastId"/>): a split from the cursor (letters)
    /// or the inventory (planks) to a free cell is <c>capacity_exhausted</c>, the entry stays where it is (Mail Lead planning default
    /// pending Evan; the code is ADR-0009's). The whole move needs no entry and succeeds.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, "pool", "cursor")]
    [InlineData(1, "pool", "cursor")]
    [InlineData(0, "ids", "cursor")]
    [InlineData(1, "ids", "cursor")]
    [InlineData(0, "pool", "inventory")]
    [InlineData(1, "pool", "inventory")]
    [InlineData(0, "ids", "inventory")]
    [InlineData(1, "ids", "inventory")]
    public void Split_EntryPoolFullOrOutOfIds_CapacityExhausted_TheEntryStaysPut_TheWholeMoveStillWorks(int slot, string limit, string fromWhere)
    {
        // Arrange: the source and two more entries, which fill a 3-slot pool, or which take the last 3 ids
        var rig = limit == "pool"
            ? new InvAutoPlaceRig(entryPoolCapacity: 3, joinPlayerB: true)
            : new InvAutoPlaceRig(tune: s => s with { EntryInitialLastId = EntryPool.MaxRealId - 3 }, joinPlayerB: true);
        ushort chest = OpenChest(rig, slot);
        ushort from = Where(rig, slot, fromWhere, chest);
        ushort source = fromWhere == "cursor"
            ? rig.PlaceMail(from, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3))
            : rig.PlaceItem(from, "plank", 0, 0, 3);
        _ = rig.PlaceItem(rig.HotbarOf(slot), "fiber", 2, 0, 1);
        _ = rig.PlaceItem(rig.InventoryOf(1 - slot), "fiber", 0, 0, 1);
        Assert.True(limit == "pool" ? rig.Entries.LiveCount == rig.Entries.Capacity : rig.Entries.LastId == EntryPool.MaxRealId);
        EntryId id = rig.Entries[source].Id;
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] split = rig.Run(rig.Player(slot), MoveOp(rig, from, source, chest, 5, 3, count: 2));

        // Assert
        AssertRejected(rig, split, InvReason.CapacityExhausted, before, slot, from, chest);
        Assert.Equal((id, from, (byte)3), (rig.Entries[source].Id, rig.Entries[source].ContainerSlot, rig.Entries[source].Count));

        // Act
        EventEntry[] whole = rig.Run(rig.Player(slot), MoveOp(rig, from, source, chest, 5, 3));

        // Assert
        AssertOk(whole);
        Assert.Equal((id, chest, (byte)3), (rig.Entries[source].Id, rig.Entries[source].ContainerSlot, rig.Entries[source].Count));
    }

    /// <summary>
    /// Exactly one new entry left: a 4-slot pool with 3 live (one free slot), or the last id issued one below
    /// <see cref="EntryPool.MaxRealId"/> (through <see cref="InventorySettings.EntryInitialLastId"/>). A split of 1 from the cursor
    /// (letters) or the inventory (planks) to a free chest cell takes it (the piece's id is <see cref="EntryPool.MaxRealId"/> in the id
    /// rows); the next split of 1 is <c>capacity_exhausted</c> (ADR-0009 section 2's code; MoveHandler's pool-and-id check) and the
    /// entry stays where it is (Mail Lead planning default pending Evan, TD-152).
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, "pool", "cursor")]
    [InlineData(1, "pool", "cursor")]
    [InlineData(0, "ids", "cursor")]
    [InlineData(1, "ids", "cursor")]
    [InlineData(0, "pool", "inventory")]
    [InlineData(1, "pool", "inventory")]
    [InlineData(0, "ids", "inventory")]
    [InlineData(1, "ids", "inventory")]
    public void Split_ExactlyOneEntrySlotOrIdLeft_TheSplitTakesIt_TheNextSplitIsCapacityExhausted(int slot, string limit, string fromWhere)
    {
        // Arrange: the source and two more entries, which leave one free slot of a 4-slot pool, or one id below MaxRealId
        var rig = limit == "pool"
            ? new InvAutoPlaceRig(entryPoolCapacity: 4, joinPlayerB: true)
            : new InvAutoPlaceRig(tune: s => s with { EntryInitialLastId = EntryPool.MaxRealId - 4 }, joinPlayerB: true);
        ushort chest = OpenChest(rig, slot);
        ushort from = Where(rig, slot, fromWhere, chest);
        ushort source = fromWhere == "cursor"
            ? rig.PlaceMail(from, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3))
            : rig.PlaceItem(from, "plank", 0, 0, 3);
        _ = rig.PlaceItem(rig.HotbarOf(slot), "fiber", 2, 0, 1);
        _ = rig.PlaceItem(rig.InventoryOf(1 - slot), "fiber", 0, 0, 1);
        Assert.True(limit == "pool" ? rig.Entries.LiveCount == rig.Entries.Capacity - 1 : rig.Entries.LastId == EntryPool.MaxRealId - 1);
        EntryId id = rig.Entries[source].Id;

        // Act
        EventEntry[] first = rig.Run(rig.Player(slot), MoveOp(rig, from, source, chest, 5, 3, count: 1));

        // Assert
        AssertOk(first);
        ushort piece = rig.EntryAt(chest, 5, 3);
        Assert.NotEqual(Occupancy.Free, piece);
        Assert.Equal((byte)1, rig.Entries[piece].Count);
        Assert.Equal((id, from, (byte)2), (rig.Entries[source].Id, rig.Entries[source].ContainerSlot, rig.Entries[source].Count));
        Assert.True(limit == "pool" ? rig.Entries.LiveCount == rig.Entries.Capacity : rig.Entries[piece].Id.Value == EntryPool.MaxRealId);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] second = rig.Run(rig.Player(slot), MoveOp(rig, from, source, chest, 7, 3, count: 1));

        // Assert
        AssertRejected(rig, second, InvReason.CapacityExhausted, before, slot, from, chest);
        Assert.Equal((id, from, (byte)2), (rig.Entries[source].Id, rig.Entries[source].ContainerSlot, rig.Entries[source].Count));
    }

    /// <summary>
    /// The mail-ids arena has room for exactly one unit: a minimum arena filled with single Intake letters until <c>Spawn</c> is refused
    /// at its reservation, then a spare single letter in the actor's inventory consumed through <see cref="IInventoryApi.ConsumeUnit"/>
    /// (story 015), which frees its class-0 block and nothing else. A split of 2 of the cursor's 3 letters to a free chest cell needs a
    /// 2-unit block and is <c>capacity_exhausted</c> (ADR-0009 section 2; MoveHandler's arena reservation after the pool check), the
    /// letters staying on the cursor (Mail Lead planning default pending Evan, TD-152); a split of 1 takes the one block: a new entry
    /// holding [1], the letters keep their id with [2, 3].
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0)]
    [InlineData(1)]
    public void Split_ArenaHasRoomForOneUnit_SplitOfTwoCapacityExhausted_SplitOfOneTakesIt(int slot)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        ushort chest = OpenChest(rig, slot);
        ushort cursor = rig.CursorOf(slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort letters = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
        ushort spare = rig.PlaceMail(inventory, "letter", 0, 0, Address5, InvAutoPlaceRig.Ids(500, 1));
        ExhaustArenaThroughIntakeSpawns(rig);
        ConsumeUnits(rig, inventory, spare, 1);
        Assert.True(rig.Inventory.MailIds.CanReserve(1));
        Assert.False(rig.Inventory.MailIds.CanReserve(2));
        EntryId id = rig.Entries[letters].Id;
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] two = rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, chest, 5, 3, count: 2));

        // Assert
        AssertRejected(rig, two, InvReason.CapacityExhausted, before, slot, cursor, chest);
        Assert.Equal((id, cursor, (byte)3), (rig.Entries[letters].Id, rig.Entries[letters].ContainerSlot, rig.Entries[letters].Count));

        // Act
        EventEntry[] one = rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, chest, 5, 3, count: 1));

        // Assert
        AssertOk(one);
        ushort piece = rig.EntryAt(chest, 5, 3);
        Assert.NotEqual(Occupancy.Free, piece);
        Assert.Equal(new uint[] { 1 }, rig.IdsOf(piece));
        Assert.Equal((id, cursor), (rig.Entries[letters].Id, rig.Entries[letters].ContainerSlot));
        Assert.Equal(new uint[] { 2, 3 }, rig.IdsOf(letters));
        Assert.False(rig.Inventory.MailIds.CanReserve(1));
    }

    /// <summary>
    /// <see cref="InventorySettings.EntryInitialLastId"/> is checked when set: <see cref="EntryPool.MaxRealId"/> is accepted (a world
    /// built with it has issued every real id), <see cref="EntryPool.MaxRealId"/> + 1 throws <see cref="SimBuildException"/> with
    /// <c>InventorySettingsInvalid</c> (control manifest l.109: build errors throw <c>SimBuildException { Code }</c>), so
    /// <see cref="EntryPool"/>'s constructor never sees it.
    /// </summary>
    [Fact]
    [Trait("Id", "TR-inventory-005")]
    public void EntryInitialLastId_MaxRealIdIsAccepted_OneAboveThrowsInventorySettingsInvalid()
    {
        // Arrange
        InventorySettings settings = InventorySettings.FromBalance(BuiltInContent.CreateM1a().Inventory);

        // Act
        var rig = new InvAutoPlaceRig(tune: s => s with { EntryInitialLastId = EntryPool.MaxRealId });
        SimBuildException e = Assert.Throws<SimBuildException>(() => settings with { EntryInitialLastId = EntryPool.MaxRealId + 1 });

        // Assert
        Assert.Equal(EntryPool.MaxRealId, rig.Entries.LastId);
        Assert.Equal(SimBuildErrorCode.InventorySettingsInvalid, e.Code);
        Assert.Equal(0u, settings.EntryInitialLastId);
    }

    // ------------------------------------------------------------------ Rejects raise nothing (TD-105 item 5)

    /// <summary>
    /// Every rejected split and swap leaves the <c>OpReply</c> alone in the tick log and changes nothing — P (both slots) and every
    /// <c>CountItem</c> as before and equal to a recount, the StackKey index equal to a recount, and every other side effect unchanged.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R4-2")]
    [Trait("Id", "TR-inventory-005")]
    [InlineData("split_own_cells", 0)]
    [InlineData("split_own_cells", 1)]
    [InlineData("split_onto_other", 0)]
    [InlineData("split_onto_other", 1)]
    [InlineData("split_full_stack", 0)]
    [InlineData("split_full_stack", 1)]
    [InlineData("split_bad_count", 0)]
    [InlineData("split_bad_count", 1)]
    [InlineData("swap_grid", 0)]
    [InlineData("swap_grid", 1)]
    [InlineData("swap_split", 0)]
    [InlineData("swap_split", 1)]
    [InlineData("swap_non_cursor", 0)]
    [InlineData("swap_non_cursor", 1)]
    [InlineData("swap_full_slot", 0)]
    [InlineData("swap_full_slot", 1)]
    [InlineData("swap_reserved", 0)]
    [InlineData("swap_reserved", 1)]
    public void SplitAndSwap_Rejects_LeaveOnlyTheOpReply_PCountItemAndIndexesUnchanged(string row, int slot)
    {
        // Arrange: chest planks 10 at (0,0) and a full plank stack at (2,0); inventory Axe at (6,0) and planks 4 at (0,0); hotbar Axe in
        // slot 1 and 20 planks in slot 2; cursor 3 planks; the other player's inventory a log stack
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort chestPlanks = rig.PlaceItem(chest, "plank", 0, 0, 10);
        _ = rig.PlaceItem(chest, "plank", 2, 0, 20);
        _ = rig.PlaceItem(inventory, "axe", 6, 0, 1);
        ushort planks = rig.PlaceItem(inventory, "plank", 0, 0, 4);
        _ = rig.PlaceItem(hotbar, "axe", 1, 0, 1);
        _ = rig.PlaceItem(hotbar, "plank", 2, 0, 20);
        ushort held = rig.PlaceItem(cursor, "plank", 0, 0, 3);
        _ = rig.PlaceItem(rig.InventoryOf(1 - slot), "log", 0, 0, 2);
        (Op op, ushort from, ushort to, InvReason expected) = row switch
        {
            "split_own_cells" => (MoveOp(rig, chest, chestPlanks, chest, 0, 0, count: 4), chest, chest, InvReason.Blocked),
            "split_onto_other" => (MoveOp(rig, chest, chestPlanks, inventory, 6, 0, count: 4), chest, inventory, InvReason.Blocked),
            "split_full_stack" => (MoveOp(rig, inventory, planks, chest, 2, 0, count: 2), inventory, chest, InvReason.StackFull),
            "split_bad_count" => (MoveOp(rig, inventory, planks, chest, 5, 2, count: 5), inventory, chest, InvReason.BadCount),
            "swap_grid" => (MoveOp(rig, cursor, held, inventory, 6, 0), cursor, inventory, InvReason.Blocked),
            "swap_split" => (MoveOp(rig, cursor, held, hotbar, 1, 0, count: 2), cursor, hotbar, InvReason.Blocked),
            "swap_non_cursor" => (MoveOp(rig, inventory, planks, hotbar, 1, 0), inventory, hotbar, InvReason.Blocked),
            "swap_full_slot" => (MoveOp(rig, cursor, held, hotbar, 2, 0), cursor, hotbar, InvReason.StackFull),
            _ => (MoveOp(rig, cursor, held, hotbar, 0, 0), cursor, hotbar, InvReason.ReservedSlot),
        };
        (int p0, int other0) = Points(rig, slot);
        int[] counts = AllItemCounts(rig);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert
        AssertRejected(rig, log, expected, before, slot, from, to);
        Assert.Equal((p0, other0), Points(rig, slot));
        Assert.Equal(counts, AllItemCounts(rig));
        AssertIndexesMatchARecount(rig);
    }

    // ------------------------------------------------------------------ Indexes and weight

    /// <summary>
    /// A split creates an entry, so it raises the destination owner's P by the new entry's <c>classPts</c> (PerEntry), even inside one
    /// container; a new entry in the uncounted chest changes nothing; the other slot's P never moves. Letters (Light), planks and a
    /// 1 x 2 log stack. <c>CountItem</c> and the StackKey index equal a recount.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-015")]
    [InlineData(0, "letter", "inventory", "inventory", 1)]
    [InlineData(1, "letter", "inventory", "inventory", 1)]
    [InlineData(0, "plank", "inventory", "inventory", 1)]
    [InlineData(1, "plank", "inventory", "inventory", 1)]
    [InlineData(0, "letter", "chest", "inventory", 1)]
    [InlineData(1, "letter", "chest", "inventory", 1)]
    [InlineData(0, "log", "chest", "hotbar", 1)]
    [InlineData(1, "log", "chest", "hotbar", 1)]
    [InlineData(0, "letter", "cursor", "inventory", 1)]
    [InlineData(1, "letter", "cursor", "inventory", 1)]
    [InlineData(0, "letter", "inventory", "chest", 0)]
    [InlineData(1, "letter", "inventory", "chest", 0)]
    [InlineData(0, "plank", "chest", "chest", 0)]
    [InlineData(1, "plank", "chest", "chest", 0)]
    public void Weight_Split_RaisesTheDestinationOwnersPByTheNewEntrysClassPts_CountItemAndIndexMatchARecount(
        int slot, string kind, string fromWhere, string toWhere, int entriesGained)
    {
        // Arrange: 5 units at (0,0) of the source; the piece of 2 goes to (3,0) (a free hotbar slot, or a free grid cell)
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort from = Where(rig, slot, fromWhere, chest);
        ushort to = Where(rig, slot, toWhere, chest);
        ushort source = PlaceKind(rig, from, kind, 0, 0, 5);
        int pts = ClassPts(rig, source);
        Assert.True(pts > 0);
        (int p0, int other0) = Points(rig, slot);

        // Act
        AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, from, source, to, 3, 0, count: 2)));

        // Assert
        Assert.Equal((byte)3, rig.Entries[source].Count);
        Assert.Equal((byte)2, rig.Entries[rig.EntryAt(to, 3, 0)].Count);
        Assert.Equal((p0 + (entriesGained * pts), other0), Points(rig, slot));
        AssertWeightAndCountsMatchRecount(rig);
        AssertIndexesMatchARecount(rig);
    }

    /// <summary>A swap moves one entry each way between two counted containers of the same player: P nets 0; the counts and the index match a recount.</summary>
    [Theory]
    [Trait("Id", "TR-inventory-015")]
    [InlineData(0, "planks", "axe")]
    [InlineData(1, "planks", "axe")]
    [InlineData(0, "axe", "letters5")]
    [InlineData(1, "axe", "letters5")]
    public void Weight_Swap_NetsZero_CountItemAndIndexMatchARecount(int slot, string held, string inSlot)
    {
        // Arrange
        var rig = NewRig();
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        _ = PlaceNamed(rig, hotbar, inSlot, 1, 0, firstId: 100);
        ushort mover = PlaceNamed(rig, cursor, held, 0, 0, firstId: 1);
        (int p0, int other0) = Points(rig, slot);
        int[] counts = AllItemCounts(rig);

        // Act
        AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, mover, hotbar, 1, 0)));

        // Assert: the per-container counts moved, the per-player totals did not
        Assert.Equal(hotbar, rig.Entries[mover].ContainerSlot);
        Assert.Equal((p0, other0), Points(rig, slot));
        Assert.Equal(counts.Sum(), AllItemCounts(rig).Sum());
        AssertWeightAndCountsMatchRecount(rig);
        AssertIndexesMatchARecount(rig);
    }
}
