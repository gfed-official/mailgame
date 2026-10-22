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

/// <summary>
/// Story grid-inventory/008: the <c>Move</c> handler's split-and-place and hotbar swap arms (<see cref="MoveHandler"/>,
/// <see cref="InventoryMutator.SplitAndPlace"/>) and the <c>Merge</c> op (<see cref="MergeHandler"/>) through the real phase-0 drain of
/// a composed world with players A (slot 0) and B (slot 1); GDD R4 (l.79), R6, R7 (l.90, l.103), l.379 and H-X-4 (l.817); H-R4-2 c/d,
/// H-R4-3 c, H-R6-1 b, H-R7-3; ADR-0009 sections 1-4; ADR-0004 (ids never reused). Every case runs from slot 0 and from slot 1. State is
/// arranged only through real calls: the rig's mutator-backed <c>PlaceMail</c> / <c>PlaceItem</c>, <c>TryOpenExternal</c>, Intake
/// <c>Spawn</c> and <c>DetachForThrow</c> in a tick phase (the arena exhaustion, an unplaced entry), the rig's documented settings
/// overrides (<c>entryPoolCapacity</c>, and <see cref="InventorySettings.EntryInitialLastId"/> through <c>tune</c>), story 011's
/// issued-but-dead container id seam (<see cref="SkipOneContainerId"/>) and the real ops. Every
/// reject asserts the reply (reason and <c>ResultVersionA/B</c>), that the tick log holds the <c>OpReply</c> alone (no record, no
/// seal), and <see cref="SideEffects"/> unchanged (container headers with versions, occupancy row bitmaps, entries with their mail
/// ids and StackKey-index membership, the pool's high-water and live count, the arena, the negative-fit cache, weight P,
/// <c>CountItem</c>, subscriber masks, externals, check ring writes and the id allocators). The entry-pool refusal, the swap's
/// per-container bump, the merge chain and a refused split leaving the entry where it is (arena or pool) are Mail Lead planning
/// defaults pending Evan (TD-152); Evan's 2026-10-22 5:11 AM PT ruling covers only a leashed letter staying on the cursor when the
/// mail-id arena is full (the code, <c>capacity_exhausted</c>, is ADR-0009's).
/// </summary>
public class MoveSplitMergeTests
{
    private const uint Address3 = 3u;
    private const uint Address5 = 5u;

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

    // ------------------------------------------------------------------ H-R7-3: merge {S → T}

    /// <summary>
    /// T = 18 letters to #5 [100..117] in the chest, S = 5 letters to #5 [1..5] (in the actor's inventory, or in the chest too):
    /// <c>merge {S → T}</c> moves the room, 2: T = 20 holding [1, 2, 100..117], S = 3 in place with its id and [3, 4, 5]; no entry id is
    /// spent; records Removed{from, S, 2}, Merged{to, T, 2}, then one seal per touched container, the source's first; across two
    /// containers both bump +1, inside one it bumps +1 once; the reply carries <c>from</c> then <c>to</c>. The same merge again is
    /// <c>stack_full</c> and changes nothing.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0, "inventory")]
    [InlineData(1, "inventory")]
    [InlineData(0, "chest")]
    [InlineData(1, "chest")]
    public void HR73_EighteenAndFiveTo5_MergeSOntoT_T20S3InPlace_EachTouchedContainerPlusOne_AgainStackFull(int slot, string sourceWhere)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort from = Where(rig, slot, sourceWhere, chest);
        ushort t = rig.PlaceMail(chest, "letter", 4, 2, Address5, InvAutoPlaceRig.Ids(100, 18));
        ushort s = rig.PlaceMail(from, "letter", 1, 1, Address5, InvAutoPlaceRig.Ids(1, 5));
        EntryId sId = rig.Entries[s].Id;
        EntryId tId = rig.Entries[t].Id;
        uint lastId = rig.Entries.LastId;
        uint[] versions = Versions(rig);
        bool cross = from != chest;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MergeOp(rig, from, s, chest, t));

        // Assert: T 20, S 3 in place, ids moved oldest-first, no id spent
        Reply reply = AssertOk(log);
        Assert.Equal((tId, chest, (byte)4, (byte)2, (byte)20), (rig.Entries[t].Id, rig.Entries[t].ContainerSlot, rig.Entries[t].X, rig.Entries[t].Y, rig.Entries[t].Count));
        Assert.Equal(InvAutoPlaceRig.Ids(1, 2).Concat(InvAutoPlaceRig.Ids(100, 18)).ToArray(), rig.IdsOf(t));
        Assert.Equal((sId, from, (byte)1, (byte)1, (byte)3), (rig.Entries[s].Id, rig.Entries[s].ContainerSlot, rig.Entries[s].X, rig.Entries[s].Y, rig.Entries[s].Count));
        Assert.Equal(new uint[] { 3, 4, 5 }, rig.IdsOf(s));
        Assert.Equal(lastId, rig.Entries.LastId);

        // Assert: records, seals, versions, reply
        EventEntry[] heads = RecordHeads(log);
        Assert.Equal(
            cross
                ? new[] { InvEventKinds.Removed, InvEventKinds.Merged, InvEventKinds.Version, InvEventKinds.Version }
                : new[] { InvEventKinds.Removed, InvEventKinds.Merged, InvEventKinds.Version },
            heads.Select(e => e.Kind).ToArray());
        InvRemovedPayload removed = heads[0].PayloadAs<InvRemovedPayload>();
        Assert.Equal((rig.IdOf(from).Value, sId.Value, (byte)2), (removed.Container, removed.EntryId, removed.Count));
        InvMergedPayload merged = heads[1].PayloadAs<InvMergedPayload>();
        Assert.Equal((rig.IdOf(chest).Value, tId.Value, (byte)2), (merged.Container, merged.TargetEntryId, merged.AddedCount));
        Assert.All(InventoryRecords(log), e => Assert.Equal(1 << slot, e.Audience));
        Assert.Equal(cross ? new[] { rig.IdOf(from).Value, rig.IdOf(chest).Value } : new[] { rig.IdOf(chest).Value }, Seals(log).Select(v => v.Container).ToArray());
        uint[] expected = (uint[])versions.Clone();
        expected[from]++;
        expected[chest] = versions[chest] + 1;
        Assert.Equal(expected, Versions(rig));
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((versions[from] + 1, versions[chest] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.False(extra.Unchanged);
        AssertIndexesMatchARecount(rig);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] again = rig.Run(rig.Player(slot), MergeOp(rig, from, s, chest, t));

        // Assert
        AssertRejected(rig, again, InvReason.StackFull, before, slot, from, chest);
    }

    /// <summary>T = 18 letters to #5 in the chest, S' = 5 letters to #6 in the actor's inventory: <c>merge {S' → T}</c> is <c>blocked</c> (StackKey mismatch).</summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR73_FiveTo6OntoEighteenTo5_Blocked_NothingChanges(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort t = rig.PlaceMail(chest, "letter", 4, 2, Address5, InvAutoPlaceRig.Ids(100, 18));
        ushort s = rig.PlaceMail(inventory, "letter", 1, 1, 6u, InvAutoPlaceRig.Ids(1, 5));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MergeOp(rig, inventory, s, chest, t));

        // Assert
        AssertRejected(rig, log, InvReason.Blocked, before, slot, inventory, chest);
    }

    // ------------------------------------------------------------------ H-R4-3 c: merge {A → A}

    /// <summary>H-R4-3's fixture (chest 8 x 4, A = 5 letters to #3 at (0,0)): <c>merge {A → A}</c> is <c>blocked</c>; A and the version are unchanged.</summary>
    [Theory]
    [Trait("Id", "H-R4-3")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR43c_MergeAOntoItself_Blocked_AAndTheVersionUnchanged(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort a = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 5));
        uint version = rig.Header(chest).Version;
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MergeOp(rig, chest, a, chest, a));

        // Assert
        AssertRejected(rig, log, InvReason.Blocked, before, slot, chest, chest);
        Assert.Equal(version, rig.Header(chest).Version);
        Assert.Equal((chest, (byte)5), (rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
        Assert.Equal(InvAutoPlaceRig.Ids(1, 5), rig.IdsOf(a));
    }

    // ------------------------------------------------------------------ H-R6-1 b: the sorted union

    /// <summary>
    /// <c>merge {A [4,9,15,21,30] → B [2,50]}</c> (letters to #5; A in the actor's inventory or in the chest with B): B holds
    /// [2,4,9,15,21,30,50], A is removed (its id no longer resolves, its cell is free), and every live entry's id list is ascending with
    /// no id held twice.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R6-1")]
    [InlineData(0, "inventory")]
    [InlineData(1, "inventory")]
    [InlineData(0, "chest")]
    [InlineData(1, "chest")]
    public void HR61b_MergeA4_9_15_21_30OntoB2_50_BIsTheSortedUnion_AIsRemoved(int slot, string sourceWhere)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort from = Where(rig, slot, sourceWhere, chest);
        ushort b = rig.PlaceMail(chest, "letter", 6, 3, Address5, new uint[] { 2, 50 });
        ushort a = rig.PlaceMail(from, "letter", 1, 0, Address5, new uint[] { 4, 9, 15, 21, 30 });
        EntryId aId = rig.Entries[a].Id;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MergeOp(rig, from, a, chest, b));

        // Assert
        AssertOk(log);
        Assert.Equal(new uint[] { 2, 4, 9, 15, 21, 30, 50 }, rig.IdsOf(b));
        Assert.Equal((byte)7, rig.Entries[b].Count);
        Assert.False(rig.Entries.TryGetSlot(aId, out _));
        Assert.Equal(Occupancy.Free, rig.Inventory.Occupancy.OwnerAt(from, rig.Header(from).W, 1, 0));
        InvRemovedPayload removed = RecordHeads(log)[0].PayloadAs<InvRemovedPayload>();
        Assert.Equal((rig.IdOf(from).Value, aId.Value, (byte)5), (removed.Container, removed.EntryId, removed.Count));
        AssertEveryIdListAscendingNoDuplicate(rig);
        AssertIndexesMatchARecount(rig);
    }

    /// <summary>
    /// A merge whose union lands in another arena size class than either input (a block is <c>2^c</c> ids): target t letters, source s
    /// letters (interleaved ids: the target's even, the source's odd) → the target's block is <see cref="MailIdArena.ClassFor"/>(t + s),
    /// its list is the sorted union with no duplicate, the source is removed and the arena holds the same units.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R6-1")]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(0, 2, 3)]
    [InlineData(1, 2, 3)]
    [InlineData(0, 4, 4)]
    [InlineData(1, 4, 4)]
    [InlineData(0, 8, 1)]
    [InlineData(1, 8, 1)]
    [InlineData(0, 16, 4)]
    [InlineData(1, 16, 4)]
    public void Merge_UnionCrossesArenaSizeClasses_TheTargetsBlockIsTheUnionsClass_TheSortedUnion(int slot, int targetCount, int sourceCount)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        uint[] targetIds = Enumerable.Range(1, targetCount).Select(i => (uint)(2 * i)).ToArray();
        uint[] sourceIds = Enumerable.Range(0, sourceCount).Select(i => (uint)((2 * i) + 1)).ToArray();
        ushort t = rig.PlaceMail(chest, "letter", 2, 2, Address3, targetIds);
        ushort s = rig.PlaceMail(inventory, "letter", 0, 1, Address3, sourceIds);
        EntryId sId = rig.Entries[s].Id;
        int union = targetCount + sourceCount;
        Assert.NotEqual(MailIdArena.ClassFor(targetCount), MailIdArena.ClassFor(union));
        Assert.NotEqual(MailIdArena.ClassFor(sourceCount), MailIdArena.ClassFor(union));
        int liveUnits = rig.Inventory.MailIds.LiveUnits;

        // Act
        AssertOk(rig.Run(rig.Player(slot), MergeOp(rig, inventory, s, chest, t)));

        // Assert
        Assert.Equal(targetIds.Concat(sourceIds).OrderBy(id => id).ToArray(), rig.IdsOf(t));
        Assert.Equal(MailIdArena.ClassFor(union), rig.Entries[t].MailIds.ClassIndex);
        Assert.Equal(union, rig.Entries[t].Count);
        Assert.False(rig.Entries.TryGetSlot(sId, out _));
        Assert.Equal(liveUnits, rig.Inventory.MailIds.LiveUnits);
        AssertEveryIdListAscendingNoDuplicate(rig);
    }

    /// <summary>
    /// Merge's union block (<c>target.count + units</c>) needs a larger arena size class than either input: target 2 letters [2, 4] and
    /// source 2 letters [1, 3], class 1 each, need one 4-unit block, class 2. A minimum arena filled with single Intake letters, then a
    /// spare 2-letter entry consumed through <see cref="IInventoryApi.ConsumeUnit"/> (its class-1 block freed): only the smaller class
    /// fits and the merge is <c>capacity_exhausted</c> (ADR-0009 section 2, last in the merge chain), nothing changes. A spare 3-letter
    /// entry consumed the same way frees a class-2 block, and the same merge is ok: the target holds [1..4] in a class-2 block and the
    /// source is removed.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R6-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void Merge_UnionNeedsALargerClassThanEitherInput_CapacityExhaustedWhenOnlyTheSmallerFits_OkWhenItFits(int slot)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort t = rig.PlaceMail(chest, "letter", 2, 2, Address3, new uint[] { 2, 4 });
        ushort s = rig.PlaceMail(inventory, "letter", 0, 1, Address3, new uint[] { 1, 3 });
        ushort spare2 = rig.PlaceMail(inventory, "letter", 4, 0, Address5, InvAutoPlaceRig.Ids(500, 2));
        ushort spare3 = rig.PlaceMail(inventory, "letter", 6, 0, Address5, InvAutoPlaceRig.Ids(600, 3));
        EntryId sId = rig.Entries[s].Id;
        Assert.Equal((1, 1, 2), (rig.Entries[t].MailIds.ClassIndex, rig.Entries[s].MailIds.ClassIndex, MailIdArena.ClassFor(4)));
        ExhaustArenaThroughIntakeSpawns(rig);
        ConsumeUnits(rig, inventory, spare2, 2);
        Assert.True(rig.Inventory.MailIds.CanReserve(2));
        Assert.False(rig.Inventory.MailIds.CanReserve(3));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] refused = rig.Run(rig.Player(slot), MergeOp(rig, inventory, s, chest, t));

        // Assert
        AssertRejected(rig, refused, InvReason.CapacityExhausted, before, slot, inventory, chest);

        // Arrange
        ConsumeUnits(rig, inventory, spare3, 3);
        Assert.True(rig.Inventory.MailIds.CanReserve(4));

        // Act
        EventEntry[] merged = rig.Run(rig.Player(slot), MergeOp(rig, inventory, s, chest, t));

        // Assert
        AssertOk(merged);
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, rig.IdsOf(t));
        Assert.Equal(2, rig.Entries[t].MailIds.ClassIndex);
        Assert.False(rig.Entries.TryGetSlot(sId, out _));
        AssertEveryIdListAscendingNoDuplicate(rig);
    }

    // ------------------------------------------------------------------ room under the 255 cap

    /// <summary>
    /// A <c>bigstack</c> item (<c>maxStack</c> 300, above the u8 count): target 250 in the chest, source 4, 5 or 6 in the inventory. Room
    /// is <c>min(300, 255) − 250 = 5</c>: 4 and 5 move whole (the source is removed), 6 moves 5 and leaves 1 in place with its id.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0, 4, 4)]
    [InlineData(1, 4, 4)]
    [InlineData(0, 5, 5)]
    [InlineData(1, 5, 5)]
    [InlineData(0, 6, 5)]
    [InlineData(1, 6, 5)]
    public void Merge_ItemMaxStackAbove255_Target250_Source4Or5Or6_MovesUpToThe255Cap(int slot, byte sourceCount, int expectedMoved)
    {
        // Arrange
        var rig = NewRig(MergeContent());
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort t = rig.PlaceItem(chest, "bigstack", 0, 0, 250);
        ushort s = rig.PlaceItem(inventory, "bigstack", 2, 1, sourceCount);
        EntryId sId = rig.Entries[s].Id;
        DefIndex bigstack = rig.ItemIndex("bigstack");

        // Act
        AssertOk(rig.Run(rig.Player(slot), MergeOp(rig, inventory, s, chest, t)));

        // Assert
        Assert.Equal(250 + expectedMoved, rig.Entries[t].Count);
        int left = sourceCount - expectedMoved;
        Assert.Equal(left > 0, rig.Entries.TryGetSlot(sId, out _));
        if (left > 0)
        {
            Assert.Equal((sId, inventory, (byte)2, (byte)1, (byte)left), (rig.Entries[s].Id, rig.Entries[s].ContainerSlot, rig.Entries[s].X, rig.Entries[s].Y, rig.Entries[s].Count));
        }

        Assert.Equal(250 + expectedMoved, rig.Inventory.CountItem(new[] { rig.IdOf(chest) }, bigstack));
        Assert.Equal(left, rig.Inventory.CountItem(new[] { rig.IdOf(inventory) }, bigstack));
        AssertWeightAndCountsMatchRecount(rig);
    }

    /// <summary>A <c>bigstack</c> target already at 255 (below its <c>maxStack</c> 300, at the u8 cap): <c>merge</c> is <c>stack_full</c> and changes nothing.</summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0)]
    [InlineData(1)]
    public void Merge_ItemMaxStackAbove255_OntoA255Target_StackFull_NothingChanges(int slot)
    {
        // Arrange
        var rig = NewRig(MergeContent());
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort t = rig.PlaceItem(chest, "bigstack", 0, 0, 255);
        ushort s = rig.PlaceItem(inventory, "bigstack", 2, 1, 1);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MergeOp(rig, inventory, s, chest, t));

        // Assert
        AssertRejected(rig, log, InvReason.StackFull, before, slot, inventory, chest);
    }

    /// <summary>
    /// The <c>Move</c> split arm against the 255 cap: a <c>bigstack</c> target (<c>maxStack</c> 300) at 250 in the chest's (0,0), a source
    /// of 10 in the inventory, <c>move {source, count k → (0,0)}</c>. Room is <c>min(300, 255) − 250 = 5</c>: k 4 and 5 move whole, k 6
    /// moves 5 (a partial merge of <c>min(count, room)</c>); the source keeps its id and cell, no entry is created, and the inventory
    /// and the chest each bump once. A target already at 255 with k 1 is <c>stack_full</c> and changes nothing.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, 250, 4)]
    [InlineData(1, 250, 4)]
    [InlineData(0, 250, 5)]
    [InlineData(1, 250, 5)]
    [InlineData(0, 250, 6)]
    [InlineData(1, 250, 6)]
    [InlineData(0, 255, 1)]
    [InlineData(1, 255, 1)]
    public void Split_ItemMaxStackAbove255_OntoTarget250Or255_MovesMinCountAndRoomUnderThe255Cap(int slot, byte targetCount, byte count)
    {
        // Arrange
        var rig = NewRig(MergeContent());
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort t = rig.PlaceItem(chest, "bigstack", 0, 0, targetCount);
        ushort s = rig.PlaceItem(inventory, "bigstack", 2, 1, 10);
        EntryId tId = rig.Entries[t].Id;
        EntryId sId = rig.Entries[s].Id;
        DefIndex bigstack = rig.ItemIndex("bigstack");
        int liveCount = rig.Entries.LiveCount;
        uint lastId = rig.Entries.LastId;
        uint chestV = rig.Header(chest).Version;
        uint inventoryV = rig.Header(inventory).Version;
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, s, chest, 0, 0, count: count));

        // Assert
        if (targetCount == byte.MaxValue)
        {
            AssertRejected(rig, log, InvReason.StackFull, before, slot, inventory, chest);
            return;
        }

        int moved = Math.Min((int)count, byte.MaxValue - targetCount);
        Reply reply = AssertOk(log);
        Assert.Equal((tId, chest, targetCount + moved), (rig.Entries[t].Id, rig.Entries[t].ContainerSlot, (int)rig.Entries[t].Count));
        Assert.Equal((sId, inventory, (byte)2, (byte)1, 10 - moved), (rig.Entries[s].Id, rig.Entries[s].ContainerSlot, rig.Entries[s].X, rig.Entries[s].Y, (int)rig.Entries[s].Count));
        Assert.Equal((liveCount, lastId), (rig.Entries.LiveCount, rig.Entries.LastId));
        Assert.DoesNotContain(RecordHeads(log), e => e.Kind == InvEventKinds.SplitFrom);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((inventoryV + 1, chestV + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal((inventoryV + 1, chestV + 1), (rig.Header(inventory).Version, rig.Header(chest).Version));
        Assert.Equal(targetCount + moved, rig.Inventory.CountItem(new[] { rig.IdOf(chest) }, bigstack));
        Assert.Equal(10 - moved, rig.Inventory.CountItem(new[] { rig.IdOf(inventory) }, bigstack));
        AssertWeightAndCountsMatchRecount(rig);
        AssertIndexesMatchARecount(rig);
    }

    /// <summary>
    /// The whole move (<c>count</c> 0, and <c>count</c> equal to the total, the same move) of a <c>bigstack</c> source of 10 in the
    /// inventory onto a <c>bigstack</c> target of 250 at the chest's (0,0): room is <c>min(300, 255) − 250 = 5</c>, so it is a partial
    /// merge of the room (MoveHandler's <c>min(requested, room)</c>, the total requested): the target holds 255, the source keeps 5 with
    /// its id and cell, no entry is created, and the inventory and the chest each bump once.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    public void WholeMove_ItemMaxStackAbove255_OntoTarget250_PartialMergeOfTheRoom_TheSourceKeepsTheRest(int slot, byte count)
    {
        // Arrange
        var rig = NewRig(MergeContent());
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort t = rig.PlaceItem(chest, "bigstack", 0, 0, 250);
        ushort s = rig.PlaceItem(inventory, "bigstack", 2, 1, 10);
        EntryId tId = rig.Entries[t].Id;
        EntryId sId = rig.Entries[s].Id;
        DefIndex bigstack = rig.ItemIndex("bigstack");
        int liveCount = rig.Entries.LiveCount;
        uint lastId = rig.Entries.LastId;
        uint chestV = rig.Header(chest).Version;
        uint inventoryV = rig.Header(inventory).Version;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, s, chest, 0, 0, count: count));

        // Assert
        Reply reply = AssertOk(log);
        Assert.Equal((tId, chest, 255), (rig.Entries[t].Id, rig.Entries[t].ContainerSlot, (int)rig.Entries[t].Count));
        Assert.Equal((sId, inventory, (byte)2, (byte)1, 5), (rig.Entries[s].Id, rig.Entries[s].ContainerSlot, rig.Entries[s].X, rig.Entries[s].Y, (int)rig.Entries[s].Count));
        Assert.Equal((liveCount, lastId), (rig.Entries.LiveCount, rig.Entries.LastId));
        Assert.DoesNotContain(RecordHeads(log), e => e.Kind == InvEventKinds.SplitFrom);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal((inventoryV + 1, chestV + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal((inventoryV + 1, chestV + 1), (rig.Header(inventory).Version, rig.Header(chest).Version));
        Assert.Equal(255, rig.Inventory.CountItem(new[] { rig.IdOf(chest) }, bigstack));
        Assert.Equal(5, rig.Inventory.CountItem(new[] { rig.IdOf(inventory) }, bigstack));
        AssertWeightAndCountsMatchRecount(rig);
        AssertIndexesMatchARecount(rig);
    }

    /// <summary>
    /// Mail at the 255 cap. The built-in kinds stop at 40 (<c>postcard</c>; <c>letter</c> 20), so <see cref="MailStack255Content"/>
    /// raises <c>letter</c>'s <c>maxStack</c> to 255 (the <c>MailMaxStack</c> stat's upper bound and CV-GRD-01's limit). Target 250 letters
    /// [1..250] in the chest, source 10 letters [1001..1010] in the inventory: <c>merge</c> moves the room, 5, which are the source's 5
    /// lowest ids (TR-inventory-005), so the target holds 255 in a class-8 block and the source keeps [1006..1010] in place with its id;
    /// again → <c>stack_full</c> (GDD l.103), nothing changes.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0)]
    [InlineData(1)]
    public void Merge_MailAtThe255Cap_Target250_Source10_MovesFive_AgainStackFull(int slot)
    {
        // Arrange
        var rig = NewRig(MailStack255Content());
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort t = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 250));
        ushort s = rig.PlaceMail(inventory, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1001, 10));
        EntryId sId = rig.Entries[s].Id;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MergeOp(rig, inventory, s, chest, t));

        // Assert
        AssertOk(log);
        Assert.Equal(InvAutoPlaceRig.Ids(1, 250).Concat(InvAutoPlaceRig.Ids(1001, 5)).ToArray(), rig.IdsOf(t));
        Assert.Equal((byte)255, rig.Entries[t].Count);
        Assert.Equal(MailIdArena.ClassCount - 1, rig.Entries[t].MailIds.ClassIndex);
        Assert.Equal((sId, inventory, (byte)2, (byte)1), (rig.Entries[s].Id, rig.Entries[s].ContainerSlot, rig.Entries[s].X, rig.Entries[s].Y));
        Assert.Equal(InvAutoPlaceRig.Ids(1006, 5), rig.IdsOf(s));
        AssertEveryIdListAscendingNoDuplicate(rig);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] again = rig.Run(rig.Player(slot), MergeOp(rig, inventory, s, chest, t));

        // Assert
        AssertRejected(rig, again, InvReason.StackFull, before, slot, inventory, chest);
    }

    /// <summary>
    /// A whole <c>move</c> onto a mergeable stack is an implicit merge whose union block (<c>target.count + units</c>,
    /// <see cref="MoveHandler"/>'s <see cref="InvPlan.AddAcquiredBlock"/>) needs a larger arena size class than the target alone: target 2
    /// letters [2, 4] (class 1) in the chest, source 2 letters [1, 3] in the inventory, union 4 (class 2). With a minimum arena filled
    /// with single Intake letters and a spare 2-letter entry consumed (one class-1 block free), only the target's class fits: the move is
    /// <c>capacity_exhausted</c> (code 85, ADR-0009 section 2) and nothing changes. A spare 3-letter entry consumed frees a class-2 block
    /// and the same move is ok: the target holds [1..4] in a class-2 block and the source is gone. PR #32 Mail Review follow-up 2: this
    /// kills the <c>targetCount + units</c> → <c>targetCount</c> mutant of the union block.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R6-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void Move_ImplicitMergeUnionNeedsALargerClassThanTheTarget_CapacityExhaustedWhenOnlyTheTargetsClassFits_OkWhenItFits(int slot)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort t = rig.PlaceMail(chest, "letter", 2, 2, Address3, new uint[] { 2, 4 });
        ushort s = rig.PlaceMail(inventory, "letter", 0, 1, Address3, new uint[] { 1, 3 });
        ushort spare2 = rig.PlaceMail(inventory, "letter", 4, 0, Address5, InvAutoPlaceRig.Ids(500, 2));
        ushort spare3 = rig.PlaceMail(inventory, "letter", 6, 0, Address5, InvAutoPlaceRig.Ids(600, 3));
        EntryId sId = rig.Entries[s].Id;
        Assert.Equal((1, 2), (MailIdArena.ClassFor(2), MailIdArena.ClassFor(4)));
        ExhaustArenaThroughIntakeSpawns(rig);
        ConsumeUnits(rig, inventory, spare2, 2);
        Assert.True(rig.Inventory.MailIds.CanReserve(2));
        Assert.False(rig.Inventory.MailIds.CanReserve(4));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] refused = rig.Run(rig.Player(slot), MoveOp(rig, inventory, s, chest, 2, 2));

        // Assert
        AssertRejected(rig, refused, InvReason.CapacityExhausted, before, slot, inventory, chest);

        // Arrange
        ConsumeUnits(rig, inventory, spare3, 3);
        Assert.True(rig.Inventory.MailIds.CanReserve(4));

        // Act
        EventEntry[] merged = rig.Run(rig.Player(slot), MoveOp(rig, inventory, s, chest, 2, 2));

        // Assert
        AssertOk(merged);
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, rig.IdsOf(t));
        Assert.Equal(2, rig.Entries[t].MailIds.ClassIndex);
        Assert.False(rig.Entries.TryGetSlot(sId, out _));
        AssertEveryIdListAscendingNoDuplicate(rig);
    }

    /// <summary>
    /// A whole <c>move</c> of letters (content with <c>letter.maxStack</c> 255, mail's cap) onto a 250-letter stack in the chest: room is
    /// <c>255 − 250 = 5</c>. 4 and 5 merge whole (the source is removed); 6 merges 5 (ids 1001-1005) and leaves 1 (1006) at the source
    /// with its id and cells (TD-150 item 2). The union lands in the 256-unit class.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0, 4, 4)]
    [InlineData(1, 4, 4)]
    [InlineData(0, 5, 5)]
    [InlineData(1, 5, 5)]
    [InlineData(0, 6, 5)]
    [InlineData(1, 6, 5)]
    public void Move_MailAtThe255Cap_Target250_Source4Or5Or6_MergesUpToThe255Cap(int slot, byte sourceCount, int expectedMoved)
    {
        // Arrange
        var rig = NewRig(MailStack255Content());
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort t = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 250));
        ushort s = rig.PlaceMail(inventory, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1001, sourceCount));
        EntryId sId = rig.Entries[s].Id;
        uint[] versions = Versions(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, s, chest, 0, 0));

        // Assert
        InvReplyExtra extra = AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal(InvAutoPlaceRig.Ids(1, 250).Concat(InvAutoPlaceRig.Ids(1001, expectedMoved)).ToArray(), rig.IdsOf(t));
        Assert.Equal((byte)(250 + expectedMoved), rig.Entries[t].Count);
        Assert.Equal(MailIdArena.ClassCount - 1, rig.Entries[t].MailIds.ClassIndex);
        Assert.Equal((versions[inventory] + 1, versions[chest] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        if (sourceCount > expectedMoved)
        {
            Assert.Equal((sId, inventory, (byte)2, (byte)1), (rig.Entries[s].Id, rig.Entries[s].ContainerSlot, rig.Entries[s].X, rig.Entries[s].Y));
            Assert.Equal(InvAutoPlaceRig.Ids(1001u + (uint)expectedMoved, sourceCount - expectedMoved), rig.IdsOf(s));
        }
        else
        {
            Assert.False(rig.Entries.TryGetSlot(sId, out _));
        }

        AssertEveryIdListAscendingNoDuplicate(rig);
        if (rig.Entries[t].Count < 255 || sourceCount > expectedMoved)
        {
            AssertIndexesMatchARecount(rig); // the recount needs one non-full stack; source 5 leaves only the full 255
        }
    }

    /// <summary>A whole <c>move</c> of letters onto a 255-letter stack (mail's cap) is <c>stack_full</c> and nothing changes.</summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0)]
    [InlineData(1)]
    public void Move_MailAtThe255Cap_OntoA255Target_StackFull_NothingChanges(int slot)
    {
        // Arrange
        var rig = NewRig(MailStack255Content());
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        _ = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 255));
        ushort s = rig.PlaceMail(inventory, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1001, 4));
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MoveOp(rig, inventory, s, chest, 0, 0));

        // Assert
        AssertRejected(rig, log, InvReason.StackFull, before, slot, inventory, chest);
    }

    // ------------------------------------------------------------------ Merge precedence

    /// <summary>
    /// The merge chain (story 008 AC "Merge precedence", Mail Lead planning default pending Evan; GDD l.103 errata (2); TD-152), with
    /// <c>container_gone</c> before <c>not_open</c> (GDD l.379, H-X-4 l.817): <c>bad_request → container_gone → not_open → stale_entry →
    /// not_allowed → blocked → stack_full → capacity_exhausted</c>. Each row violates its step and every later one that can co-occur, and
    /// the earliest code must win. The world: a minimum arena exhausted through Intake spawns after the fixture is placed (so every
    /// mail union is refused), an output-only external O (<c>acceptsInsert: false</c>) holding a full plank stack, the actor's inventory
    /// I with letters to #3 of 5, 20 (full) and 19, 20 letters to #5 and two Axes (<c>effMax</c> 1), a removed id (a cursor letter merged
    /// away by a real <c>move</c>), the other player's inventory X (not in the actor's open set) and an issued id G that is no longer live
    /// (the story 011 seam). A reject before <c>stale_entry</c> carries no versions; from <c>stale_entry</c> on, <c>from</c>'s then
    /// <c>to</c>'s.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData("bad_request", 0)]
    [InlineData("bad_request", 1)]
    [InlineData("container_gone_from", 0)]
    [InlineData("container_gone_from", 1)]
    [InlineData("container_gone_to", 0)]
    [InlineData("container_gone_to", 1)]
    [InlineData("not_open_from", 0)]
    [InlineData("not_open_from", 1)]
    [InlineData("not_open_to", 0)]
    [InlineData("not_open_to", 1)]
    [InlineData("stale_entry_removed", 0)]
    [InlineData("stale_entry_removed", 1)]
    [InlineData("stale_entry_target_removed", 0)]
    [InlineData("stale_entry_target_removed", 1)]
    [InlineData("stale_entry_not_in_from", 0)]
    [InlineData("stale_entry_not_in_from", 1)]
    [InlineData("stale_entry_target_not_in_to", 0)]
    [InlineData("stale_entry_target_not_in_to", 1)]
    [InlineData("not_allowed", 0)]
    [InlineData("not_allowed", 1)]
    [InlineData("blocked_self", 0)]
    [InlineData("blocked_self", 1)]
    [InlineData("blocked_key", 0)]
    [InlineData("blocked_key", 1)]
    [InlineData("blocked_effmax1", 0)]
    [InlineData("blocked_effmax1", 1)]
    [InlineData("stack_full", 0)]
    [InlineData("stack_full", 1)]
    [InlineData("capacity_exhausted", 0)]
    [InlineData("capacity_exhausted", 1)]
    public void Merge_Precedence_RequestViolatingThisStepAndEveryLaterOne_ReturnsThisStep(string row, int slot)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, content: MergeContent(), joinPlayerB: true);
        ushort o = rig.CreateGrid(8, 4, OutputOnlyDef);
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)slot), rig.IdOf(o)));
        ushort i = rig.InventoryOf(slot);
        ushort h = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        uint x = rig.IdOf(rig.InventoryOf(1 - slot)).Value;
        ushort oPlanksFull = rig.PlaceItem(o, "plank", 0, 0, 20);
        ushort iLetters = rig.PlaceMail(i, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 5));
        ushort iFull = rig.PlaceMail(i, "letter", 1, 0, Address3, InvAutoPlaceRig.Ids(100, 20));
        ushort iLetters19 = rig.PlaceMail(i, "letter", 2, 0, Address3, InvAutoPlaceRig.Ids(200, 19));
        ushort iFull5 = rig.PlaceMail(i, "letter", 3, 0, Address5, InvAutoPlaceRig.Ids(300, 20));
        ushort iAxe = rig.PlaceItem(i, "axe", 4, 0, 1);
        ushort iAxe2 = rig.PlaceItem(i, "axe", 5, 0, 1);
        _ = rig.PlaceMail(h, "letter", 2, 0, Address3, InvAutoPlaceRig.Ids(500, 2));
        ushort mergedAway = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(502, 1));
        uint gone = rig.Entries[mergedAway].Id.Value;
        AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, mergedAway, h, 2, 0)));
        Assert.False(rig.Entries.TryGetSlot(EntryId.Real(gone), out _));
        ExhaustArenaThroughIntakeSpawns(rig);
        uint g = SkipOneContainerId(rig);
        uint iId = rig.IdOf(i).Value;
        uint oId = rig.IdOf(o).Value;
        uint hId = rig.IdOf(h).Value;
        uint E(ushort entry) => rig.Entries[entry].Id.Value;
        (uint from, uint entry, uint to, uint target, InvReason expected) = row switch
        {
            "bad_request" => (g, E(iFull), 0u, E(iFull), (InvReason)CoreReason.BadRequest),
            "container_gone_from" => (g, E(iFull), iId, E(iFull), InvReason.ContainerGone),
            "container_gone_to" => (iId, E(iFull), g, E(iFull), InvReason.ContainerGone),
            "not_open_from" => (x, E(iFull), oId, E(iFull), InvReason.NotOpen),
            "not_open_to" => (iId, E(iFull), x, E(iFull), InvReason.NotOpen),
            "stale_entry_removed" => (iId, gone, oId, gone, InvReason.StaleEntry),
            "stale_entry_target_removed" => (iId, E(iFull), oId, gone, InvReason.StaleEntry),
            "stale_entry_not_in_from" => (hId, E(iFull), oId, E(oPlanksFull), InvReason.StaleEntry),
            "stale_entry_target_not_in_to" => (iId, E(iFull), oId, E(iFull), InvReason.StaleEntry),
            "not_allowed" => (iId, E(iFull), oId, E(oPlanksFull), InvReason.NotAllowed),
            "blocked_self" => (iId, E(iFull), iId, E(iFull), InvReason.Blocked),
            "blocked_key" => (iId, E(iFull), iId, E(iFull5), InvReason.Blocked),
            "blocked_effmax1" => (iId, E(iAxe), iId, E(iAxe2), InvReason.Blocked),
            "stack_full" => (iId, E(iLetters), iId, E(iFull), InvReason.StackFull),
            "capacity_exhausted" => (iId, E(iLetters), iId, E(iLetters19), InvReason.CapacityExhausted),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
        };
        bool resolved = expected is not ((InvReason)CoreReason.BadRequest or InvReason.ContainerGone or InvReason.NotOpen);
        (uint, uint) versions = resolved
            ? (rig.Header(rig.SlotOf(new ContainerId(from))).Version, rig.Header(rig.SlotOf(new ContainerId(to))).Version)
            : (0u, 0u);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), MergeOpRaw(from, entry, to, target));

        // Assert
        AssertRejectedWithVersions(rig, log, expected, before, slot, versions);
    }

    /// <summary>
    /// The real handler on an allocated but unplaced entry (a letter released from the actor's hotbar by <c>DetachForThrow</c>, live in
    /// no container), as the entry or as the target: <c>stale_entry</c>, nothing changes (the stub rig's former <c>ValidCases</c> "Merge"
    /// row, InvMalformedOpTests).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0, "entry")]
    [InlineData(1, "entry")]
    [InlineData(0, "target")]
    [InlineData(1, "target")]
    public void Merge_AnAllocatedButUnplacedEntryOrTarget_StaleEntry_NothingChanges(int slot, string which)
    {
        // Arrange
        var rig = NewRig();
        ushort hotbar = rig.HotbarOf(slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort placed = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
        ushort e = rig.PlaceMail(hotbar, "letter", 2, 0, Address3, InvAutoPlaceRig.Ids(100, 1));
        EntryId id = rig.Entries[e].Id;
        DetachResult detached = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.DetachForThrow(PlayerSlot.FromValue((byte)slot), id, 1));
        Assert.Equal((InvReason)0, detached.Reason);
        Assert.True(rig.Entries[e].IsLive);
        Assert.Equal(EntryRecord.NoContainer, rig.Entries[e].ContainerSlot);
        uint placedId = rig.Entries[placed].Id.Value;
        List<long> before = SideEffects(rig);
        Op op = which == "entry"
            ? MergeOpRaw(rig.IdOf(hotbar).Value, id.Value, rig.IdOf(inventory).Value, placedId)
            : MergeOpRaw(rig.IdOf(inventory).Value, placedId, rig.IdOf(hotbar).Value, id.Value);
        (ushort from, ushort to) = which == "entry" ? (hotbar, inventory) : (inventory, hotbar);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert
        AssertRejected(rig, log, InvReason.StaleEntry, before, slot, from, to);
    }

    // ------------------------------------------------------------------ Rejects raise nothing (merge)

    /// <summary>
    /// Every rejected merge leaves the <c>OpReply</c> alone in the tick log and changes nothing — P (both slots) and every
    /// <c>CountItem</c> as before and equal to a recount, the StackKey index equal to a recount, and every other side effect unchanged.
    /// Item and mail rows.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [Trait("Id", "H-R4-3")]
    [InlineData("self", 0)]
    [InlineData("self", 1)]
    [InlineData("key", 0)]
    [InlineData("key", 1)]
    [InlineData("effmax1", 0)]
    [InlineData("effmax1", 1)]
    [InlineData("item_full", 0)]
    [InlineData("item_full", 1)]
    [InlineData("mail_full", 0)]
    [InlineData("mail_full", 1)]
    [InlineData("not_open", 0)]
    [InlineData("not_open", 1)]
    [InlineData("not_in_from", 0)]
    [InlineData("not_in_from", 1)]
    public void Merge_Rejects_LeaveOnlyTheOpReply_PCountItemAndIndexesUnchanged(string row, int slot)
    {
        // Arrange: chest planks 20 (full) at (0,0) and 5 letters to #3 at (2,0); inventory planks 4, logs 3, two Axes and 20 letters
        // to #3; the other player's inventory planks 6
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort other = rig.InventoryOf(1 - slot);
        ushort chestPlanks = rig.PlaceItem(chest, "plank", 0, 0, 20);
        ushort chestLetters = rig.PlaceMail(chest, "letter", 2, 0, Address3, InvAutoPlaceRig.Ids(1, 5));
        ushort planks = rig.PlaceItem(inventory, "plank", 0, 0, 4);
        ushort logs = rig.PlaceItem(inventory, "log", 1, 0, 3);
        ushort axe = rig.PlaceItem(inventory, "axe", 2, 0, 1);
        ushort axe2 = rig.PlaceItem(inventory, "axe", 3, 0, 1);
        ushort letters = rig.PlaceMail(inventory, "letter", 4, 0, Address3, InvAutoPlaceRig.Ids(100, 20));
        ushort otherPlanks = rig.PlaceItem(other, "plank", 0, 0, 6);
        (Op op, ushort from, ushort to, InvReason expected) = row switch
        {
            "self" => (MergeOp(rig, inventory, planks, inventory, planks), inventory, inventory, InvReason.Blocked),
            "key" => (MergeOp(rig, inventory, planks, inventory, logs), inventory, inventory, InvReason.Blocked),
            "effmax1" => (MergeOp(rig, inventory, axe, inventory, axe2), inventory, inventory, InvReason.Blocked),
            "item_full" => (MergeOp(rig, inventory, planks, chest, chestPlanks), inventory, chest, InvReason.StackFull),
            "mail_full" => (MergeOp(rig, chest, chestLetters, inventory, letters), chest, inventory, InvReason.StackFull),
            "not_open" => (MergeOp(rig, other, otherPlanks, inventory, planks), other, inventory, InvReason.NotOpen),
            _ => (MergeOp(rig, chest, planks, inventory, planks), chest, inventory, InvReason.StaleEntry),
        };
        (int p0, int other0) = Points(rig, slot);
        int[] counts = AllItemCounts(rig);
        List<long> before = SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert
        if (expected == InvReason.NotOpen)
        {
            AssertRejectedWithVersions(rig, log, expected, before, slot, (0u, 0u));
        }
        else
        {
            AssertRejected(rig, log, expected, before, slot, from, to);
        }

        Assert.Equal((p0, other0), Points(rig, slot));
        Assert.Equal(counts, AllItemCounts(rig));
        AssertWeightAndCountsMatchRecount(rig);
        AssertIndexesMatchARecount(rig);
    }

    // ------------------------------------------------------------------ Indexes and weight (merge)

    /// <summary>
    /// A merge that empties its source removes an entry, so it lowers the source owner's P by the entry's <c>classPts</c> (PerEntry,
    /// even inside one container or across two of the same player's counted containers); a partial merge removes no entry and leaves P;
    /// a source drained out of the uncounted chest changes no P; the other slot's P never moves. <c>CountItem</c> and the StackKey index
    /// equal a recount.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-015")]
    [InlineData(0, "letter", "inventory", "inventory", 10, -1)]
    [InlineData(1, "letter", "inventory", "inventory", 10, -1)]
    [InlineData(0, "plank", "inventory", "inventory", 18, 0)]
    [InlineData(1, "plank", "inventory", "inventory", 18, 0)]
    [InlineData(0, "letter", "hotbar", "inventory", 10, -1)]
    [InlineData(1, "letter", "hotbar", "inventory", 10, -1)]
    [InlineData(0, "plank", "inventory", "chest", 10, -1)]
    [InlineData(1, "plank", "inventory", "chest", 10, -1)]
    [InlineData(0, "letter", "chest", "inventory", 10, 0)]
    [InlineData(1, "letter", "chest", "inventory", 10, 0)]
    [InlineData(0, "letter", "inventory", "chest", 18, 0)]
    [InlineData(1, "letter", "inventory", "chest", 18, 0)]
    public void Weight_Merge_DrainingLowersTheSourceOwnersPByClassPts_PartialDoesNot_CountItemAndIndexMatchARecount(
        int slot, string kind, string fromWhere, string toWhere, byte targetCount, int entriesGained)
    {
        // Arrange: source 5 units at (1,0) of its container, target at (3,0) of its own
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort from = Where(rig, slot, fromWhere, chest);
        ushort to = Where(rig, slot, toWhere, chest);
        ushort s = PlaceUnits(rig, from, kind, 1, 0, 5, firstId: 1);
        ushort t = PlaceUnits(rig, to, kind, 3, 0, targetCount, firstId: 100);
        int pts = ClassPts(rig, s);
        Assert.True(pts > 0);
        (int p0, int other0) = Points(rig, slot);
        int moved = Math.Min(5, 20 - targetCount);

        // Act
        AssertOk(rig.Run(rig.Player(slot), MergeOp(rig, from, s, to, t)));

        // Assert
        Assert.Equal(targetCount + moved, rig.Entries[t].Count);
        Assert.Equal(moved < 5, rig.Entries[s].IsLive && rig.Entries[s].ContainerSlot == from);
        Assert.Equal((p0 + (entriesGained * pts), other0), Points(rig, slot));
        AssertWeightAndCountsMatchRecount(rig);
        AssertIndexesMatchARecount(rig);
    }

    // ------------------------------------------------------------------ reply ResultVersionA / ResultVersionB order

    /// <summary>
    /// The reply's <c>ResultVersionA</c> is the source container's version and <c>ResultVersionB</c> the destination's, on an ok and on
    /// a reject, for a split, a swap and a merge: the two containers' versions are made distinct first (extra placements), so an
    /// exchanged pair cannot pass.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-002")]
    [InlineData(0, "split")]
    [InlineData(1, "split")]
    [InlineData(0, "swap")]
    [InlineData(1, "swap")]
    [InlineData(0, "merge")]
    [InlineData(1, "merge")]
    [InlineData(0, "merge_reject")]
    [InlineData(1, "merge_reject")]
    [InlineData(0, "split_reject")]
    [InlineData(1, "split_reject")]
    public void Reply_ResultVersionAIsTheSourceAndBTheDestination_OnOkAndReject(int slot, string row)
    {
        // Arrange: the chest is bumped 3 times more than the actor's inventory; the hotbar 2 times more than the cursor
        var rig = NewRig();
        ushort chest = OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort chestT = rig.PlaceMail(chest, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 18));
        _ = rig.PlaceItem(chest, "plank", 7, 3, 1);
        _ = rig.PlaceItem(chest, "plank", 6, 3, 1);
        _ = rig.PlaceItem(chest, "plank", 5, 3, 1);
        ushort invS = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 5));
        _ = rig.PlaceItem(hotbar, "fiber", 3, 0, 1);
        _ = rig.PlaceItem(hotbar, "axe", 1, 0, 1);
        _ = rig.PlaceItem(hotbar, "fiber", 4, 0, 1);
        ushort held = rig.PlaceItem(cursor, "plank", 0, 0, 3);
        (Op op, ushort from, ushort to, bool ok) = row switch
        {
            "split" => (MoveOp(rig, inventory, invS, chest, 4, 2, count: 2), inventory, chest, true),
            "swap" => (MoveOp(rig, cursor, held, hotbar, 1, 0), cursor, hotbar, true),
            "merge" => (MergeOp(rig, inventory, invS, chest, chestT), inventory, chest, true),
            "merge_reject" => (MergeOp(rig, inventory, invS, chest, invS), inventory, chest, false),
            _ => (MoveOp(rig, inventory, invS, chest, 7, 3, count: 2), inventory, chest, false),
        };
        uint fromV = rig.Header(from).Version;
        uint toV = rig.Header(to).Version;
        Assert.NotEqual(fromV, toV);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.Equal(ok ? ReplyResult.Ok : ReplyResult.Rejected, reply.Result);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        uint bump = ok ? 1u : 0u;
        Assert.Equal((fromV + bump, toV + bump), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal((rig.Header(from).Version, rig.Header(to).Version), (extra.ResultVersionA, extra.ResultVersionB));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>The fixture container def <see cref="MergeContent"/> adds: an 8 x 4 grid with <c>acceptsInsert: false</c>.</summary>
    private const string OutputOnlyDef = "output_only";

    /// <summary>The M-1a content plus <see cref="OutputOnlyDef"/> and a <c>bigstack</c> item (1 x 1, <c>maxStack</c> 300, above the u8 count).</summary>
    internal static ContentSet MergeContent()
    {
        ContentSet m1a = BuiltInContent.CreateM1a();
        ContainerDef inventoryDef = m1a.Containers[PlacementFixture.Index(m1a.Containers, "player_inventory")];
        ImmutableArray<ContainerDef> containers = m1a.Containers.AsSpan().ToArray()
            .Append(inventoryDef with { Id = OutputOnlyDef, GridW = 8, GridH = 4, AcceptsInsert = false })
            .ToImmutableArray();
        ItemDef plank = m1a.Items[PlacementFixture.Index(m1a.Items, "plank")];
        ImmutableArray<ItemDef> items = m1a.Items.AsSpan().ToArray().Append(plank with { Id = "bigstack", MaxStack = 300 }).ToImmutableArray();
        return new ContentSet(
            m1a.Balance, m1a.ResolvedStatBounds, new DefTable<ContainerDef>(containers), new DefTable<ItemDef>(items), m1a.MailKinds, m1a.DestinationTypes, m1a.MailMixes);
    }

    /// <summary>The M-1a content with <c>letter</c>'s <c>maxStack</c> raised from 20 to 255 (mail's cap: the u8 count and the <c>MailMaxStack</c> bound).</summary>
    internal static ContentSet MailStack255Content()
    {
        ContentSet m1a = BuiltInContent.CreateM1a();
        ImmutableArray<MailKindDef> kinds = m1a.MailKinds.AsSpan().ToArray()
            .Select(k => k.Id == "letter" ? k with { MaxStack = 255 } : k)
            .ToImmutableArray();
        return new ContentSet(
            m1a.Balance, m1a.ResolvedStatBounds, m1a.Containers, m1a.Items, new DefTable<MailKindDef>(kinds), m1a.DestinationTypes, m1a.MailMixes);
    }

    /// <summary>
    /// The story 011 seam for an issued-but-dead container id (no container is destroyed before M-1b): advances the container table's
    /// private id counter by one through reflection, which must find the field and a non-zero high-water, so the skipped id is below
    /// the high-water but not live. The map and every live container are untouched.
    /// </summary>
    internal static uint SkipOneContainerId(InvAutoPlaceRig rig)
    {
        ContainerTable table = rig.Inventory.Containers;
        uint skipped = table.NextId;
        Assert.True(skipped > 0u);
        FieldInfo? field = typeof(ContainerTable).GetField("_nextId", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(skipped, Convert.ToUInt32(field.GetValue(table)));
        field.SetValue(table, skipped + 1);
        Assert.Equal(skipped + 1, table.NextId);
        Assert.False(table.TryGetSlot(new ContainerId(skipped), out _));
        return skipped;
    }

    /// <summary>Places <paramref name="units"/> of <paramref name="kind"/> (an item def, else letters of that mail kind to #3 with ids from <paramref name="firstId"/>).</summary>
    private static ushort PlaceUnits(InvAutoPlaceRig rig, ushort container, string kind, int x, int y, byte units, uint firstId) =>
        kind is "plank" or "fiber" or "log" or "axe"
            ? rig.PlaceItem(container, kind, x, y, units)
            : rig.PlaceMail(container, kind, x, y, Address3, InvAutoPlaceRig.Ids(firstId, units));

    /// <summary>Every live mail entry's id list is strictly ascending, and no id is held by two entries (R6).</summary>
    internal static void AssertEveryIdListAscendingNoDuplicate(InvAutoPlaceRig rig)
    {
        var seen = new HashSet<uint>();
        int lists = 0;
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            EntryRecord e = rig.Entries[(ushort)s];
            if (!e.IsLive || !e.IsMail)
            {
                continue;
            }

            uint[] ids = rig.IdsOf((ushort)s);
            Assert.Equal(e.Count, ids.Length);
            for (int i = 1; i < ids.Length; i++)
            {
                Assert.True(ids[i - 1] < ids[i], $"entry {s}: ids not ascending at {i}");
            }

            Assert.All(ids, id => Assert.True(seen.Add(id), $"id {id} held twice"));
            lists++;
        }

        Assert.True(lists > 0);
    }

    private static Op MergeOp(InvAutoPlaceRig rig, ushort from, ushort entry, ushort to, ushort target) =>
        MergeOpRaw(rig.IdOf(from).Value, rig.Entries[entry].Id.Value, rig.IdOf(to).Value, rig.Entries[target].Id.Value);

    private static Op MergeOpRaw(uint from, uint entry, uint to, uint target)
    {
        var op = new Op { Kind = InvOpKinds.Merge, ReqId = 83 };
        op.SetPayload(new MergePayload { From = from, EntryId = entry, To = to, TargetEntryId = target });
        return op;
    }

    private static InvAutoPlaceRig NewRig(ContentSet? content = null) => new(content: content, joinPlayerB: true);

    /// <summary>A fresh 8 x 4 <c>player_inventory</c>-def grid with no owner, opened as <paramref name="slot"/>'s external.</summary>
    internal static ushort OpenChest(InvAutoPlaceRig rig, int slot)
    {
        ushort chest = rig.CreateGrid(8, 4);
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)slot), rig.IdOf(chest)));
        return chest;
    }

    /// <summary>The container named <paramref name="where"/>: <c>chest</c>, or the slot's <c>inventory</c>, <c>hotbar</c> or <c>cursor</c>.</summary>
    private static ushort Where(InvAutoPlaceRig rig, int slot, string where, ushort chest) => where switch
    {
        "chest" => chest,
        "inventory" => rig.InventoryOf(slot),
        "hotbar" => rig.HotbarOf(slot),
        "cursor" => rig.CursorOf(slot),
        _ => throw new ArgumentOutOfRangeException(nameof(where), where, null),
    };

    /// <summary>The swap fixtures: <c>axe</c>, <c>planks</c> (3), <c>letters3</c> (3 letters to #3) or <c>letters5</c> (4 letters to #5), ids from <paramref name="firstId"/>.</summary>
    private static ushort PlaceNamed(InvAutoPlaceRig rig, ushort container, string name, int x, int y, uint firstId) => name switch
    {
        "axe" => rig.PlaceItem(container, "axe", x, y, 1),
        "planks" => rig.PlaceItem(container, "plank", x, y, 3),
        "letters3" => rig.PlaceMail(container, "letter", x, y, Address3, InvAutoPlaceRig.Ids(firstId, 3)),
        "letters5" => rig.PlaceMail(container, "letter", x, y, Address5, InvAutoPlaceRig.Ids(firstId, 4)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>Places <paramref name="units"/> of <paramref name="kind"/> (an item def, else a mail kind to #3 with ids from 1).</summary>
    private static ushort PlaceKind(InvAutoPlaceRig rig, ushort container, string kind, int x, int y, byte units)
    {
        bool isItem = false;
        for (int i = 0; i < rig.Content.Items.Count; i++)
        {
            isItem |= rig.Content.Items[DefIndex.FromValue((ushort)i)].Id == kind;
        }

        return isItem
            ? rig.PlaceItem(container, kind, x, y, units)
            : rig.PlaceMail(container, kind, x, y, Address3, InvAutoPlaceRig.Ids(1, units));
    }

    /// <summary>The M-1a content with the cursor def admitting mail only (a whitelist the displaced Axe fails).</summary>
    private static ContentSet MailOnlyCursorContent()
    {
        ContentSet m1a = BuiltInContent.CreateM1a();
        ImmutableArray<ContainerDef> containers = m1a.Containers.AsSpan().ToArray()
            .Select(c => c.Id == "cursor" ? c with { HasAllowedCategories = true, AllowedCategories = ImmutableArray.Create(ItemCategory.Mail) } : c)
            .ToImmutableArray();
        return new ContentSet(
            m1a.Balance, m1a.ResolvedStatBounds, new DefTable<ContainerDef>(containers), m1a.Items, m1a.MailKinds, m1a.DestinationTypes, m1a.MailMixes);
    }

    /// <summary>Spawns single letters into the Intake (one tick) until <see cref="IInventoryApi.Spawn"/> places none.</summary>
    internal static void ExhaustArenaThroughIntakeSpawns(InvAutoPlaceRig rig)
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

    /// <summary>
    /// Consumes <paramref name="units"/> of the entry through <see cref="IInventoryApi.ConsumeUnit"/> (story 015's real API), one per
    /// tick; the last unit's removal frees its arena block.
    /// </summary>
    internal static void ConsumeUnits(InvAutoPlaceRig rig, ushort container, ushort entry, int units)
    {
        var at = new EntryRef(rig.IdOf(container), rig.Entries[entry].Id);
        for (int i = 0; i < units; i++)
        {
            Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ConsumeUnit(at, ConsumeReason.Delivered, ActorId.System)));
        }

        Assert.False(rig.Entries.TryGetSlot(at.Entry, out _));
    }

    /// <summary>D1's <c>classPts</c> of the entry's def, from the balance knobs (not from the module under test).</summary>
    internal static int ClassPts(InvAutoPlaceRig rig, ushort entry)
    {
        EntryRecord e = rig.Entries[entry];
        WeightClass c = e.IsMail
            ? rig.Content.MailKinds[DefIndex.FromValue(e.MailKind)].WeightClass
            : rig.Content.Items[DefIndex.FromValue(e.ItemId)].WeightClass;
        InventoryBalance k = rig.Content.Inventory;
        return c switch
        {
            WeightClass.Light => k.WeightPointsLight,
            WeightClass.Medium => k.WeightPointsMedium,
            WeightClass.Heavy => k.WeightPointsHeavy,
            _ => throw new InvalidOperationException($"entry {entry}: class {c}"),
        };
    }

    /// <summary><see cref="IInventoryApi.WeightPoints"/> of <paramref name="slot"/> and of the other joined slot.</summary>
    private static (int Actor, int Other) Points(InvAutoPlaceRig rig, int slot) =>
        (rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)slot)), rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)(1 - slot))));

    /// <summary>The version of every live container, by table slot.</summary>
    internal static uint[] Versions(InvAutoPlaceRig rig)
    {
        ContainerTable containers = rig.Inventory.Containers;
        var versions = new uint[containers.LiveCount];
        for (int s = 0; s < versions.Length; s++)
        {
            versions[s] = containers[(ushort)s].Version;
        }

        return versions;
    }

    /// <summary><c>CountItem</c> of every item def in every live container, in table order.</summary>
    private static int[] AllItemCounts(InvAutoPlaceRig rig)
    {
        ContainerTable containers = rig.Inventory.Containers;
        var counts = new List<int>();
        for (int s = 0; s < containers.LiveCount; s++)
        {
            for (int i = 0; i < rig.Content.Items.Count; i++)
            {
                counts.Add(rig.Inventory.CountItem(new[] { containers[(ushort)s].Id }, DefIndex.FromValue((ushort)i)));
            }
        }

        return counts.ToArray();
    }

    /// <summary>
    /// <see cref="IInventoryApi.WeightPoints"/> of every slot equals the brute-force recount (<see cref="WeightRecountPropertyTests.Recount"/>),
    /// and <c>CountItem</c> of every item def equals the sum of the live entries' units in every container.
    /// </summary>
    internal static void AssertWeightAndCountsMatchRecount(InvAutoPlaceRig rig)
    {
        for (int s = 0; s < PlayerSlot.Count; s++)
        {
            Assert.Equal(WeightRecountPropertyTests.Recount(rig.Content, rig.Inventory, s), rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)s)));
        }

        ContainerTable containers = rig.Inventory.Containers;
        for (int i = 0; i < rig.Content.Items.Count; i++)
        {
            DefIndex item = DefIndex.FromValue((ushort)i);
            for (int c = 0; c < containers.LiveCount; c++)
            {
                int units = 0;
                for (int s = 0; s < rig.Entries.Capacity; s++)
                {
                    EntryRecord e = rig.Entries[(ushort)s];
                    if (e.IsLive && !e.IsMail && e.ContainerSlot == c && e.ItemId == item.Value)
                    {
                        units += e.Count;
                    }
                }

                Assert.Equal(units, rig.Inventory.CountItem(new[] { containers[(ushort)c].Id }, item));
            }
        }
    }

    /// <summary>The StackKey index equals a recount: a live entry is linked iff it is placed and its count is below its <c>effMax</c> read now.</summary>
    internal static void AssertIndexesMatchARecount(InvAutoPlaceRig rig)
    {
        int linked = 0;
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            EntryRecord e = rig.Entries[(ushort)s];
            if (!e.IsLive)
            {
                continue;
            }

            bool expected = e.ContainerSlot != EntryRecord.NoContainer && e.Count < rig.SubjectOf((ushort)s).EffMax;
            Assert.Equal(expected, rig.Index.Contains((ushort)s));
            linked += expected ? 1 : 0;
        }

        Assert.True(linked > 0);
    }

    private static Op MoveOp(InvAutoPlaceRig rig, ushort from, ushort entry, ushort to, int x, int y, byte count = 0)
    {
        var op = new Op { Kind = InvOpKinds.Move, ReqId = 82 };
        op.SetPayload(new MovePayload
        {
            From = rig.IdOf(from).Value, EntryId = rig.Entries[entry].Id.Value, To = rig.IdOf(to).Value, BaseVersion = rig.Header(to).Version,
            X = (short)x, Y = (short)y, Count = count,
        });
        return op;
    }

    internal static Reply AssertOk(EventEntry[] log)
    {
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.Equal(CoreReason.Ok, reply.Reason);
        return reply;
    }

    /// <summary>
    /// The reject contract (TD-105 item 5; ADR-0007 l.128; ADR-0009 section 3): the reason; the reply's <c>ResultVersionA/B</c> are the
    /// source's and the destination's current versions; the <c>OpReply</c> is the only entry in the tick log (no record, no seal) and
    /// is sent to the actor's slot alone; and every recorded side effect is as before.
    /// </summary>
    private static void AssertRejected(InvAutoPlaceRig rig, EventEntry[] log, InvReason reason, List<long> before, int actorSlot, ushort from, ushort to) =>
        AssertRejectedWithVersions(rig, log, reason, before, actorSlot, (rig.Header(from).Version, rig.Header(to).Version));

    /// <summary>
    /// <see cref="AssertRejected"/> with the expected <c>(ResultVersionA, ResultVersionB)</c> given: (0, 0) for a reject before the
    /// containers are resolved (<c>bad_request</c>, <c>container_gone</c>, <c>not_open</c>).
    /// </summary>
    private static void AssertRejectedWithVersions(
        InvAutoPlaceRig rig, EventEntry[] log, InvReason reason, List<long> before, int actorSlot, (uint A, uint B) versions)
    {
        EventEntry entry = Assert.Single(log);
        Assert.Equal(EventKind.OpReply, entry.Kind);
        Assert.Equal(1 << actorSlot, entry.Audience);
        Reply reply = entry.PayloadAs<Reply>();
        Assert.Equal(ReplyResult.Rejected, reply.Result);
        Assert.Equal((ushort)reason, reply.Reason);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal(versions, (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(before, SideEffects(rig));
    }

    internal static (uint Container, uint Entry) Removed(EventEntry e)
    {
        InvRemovedPayload p = e.PayloadAs<InvRemovedPayload>();
        return (p.Container, p.EntryId);
    }

    internal static (uint Container, uint Entry) Inserted(EventEntry e)
    {
        InvInsertedPayload p = e.PayloadAs<InvInsertedPayload>();
        return (p.Container, p.EntryId);
    }

    internal static EventEntry[] InventoryRecords(EventEntry[] log) =>
        log.Where(e => e.Kind is >= InvEventKinds.Inserted and <= InvEventKinds.ItemsLost).ToArray();

    /// <summary>The inventory records and seals that start a record (mail-id run continuations dropped), in log order.</summary>
    internal static EventEntry[] RecordHeads(EventEntry[] log) =>
        InventoryRecords(log).Where(e => (e.Flags & EventFlags.Continuation) == 0).ToArray();

    internal static InvVersionPayload[] Seals(EventEntry[] log) =>
        log.Where(e => e.Kind == InvEventKinds.Version).Select(e => e.PayloadAs<InvVersionPayload>()).ToArray();

    /// <summary>
    /// Everything a split or swap could change or record: every container header (version and lastReplaceVersion included), its
    /// occupancy <b>row bitmaps</b>, subscriber mask and <c>CountItem</c> of every item def; every slot's external and weight P; every
    /// live entry with its position, count, arrival seq, mail ids and StackKey-index membership; the pool's high-water and live count;
    /// the arena's units, blocks and free and peak blocks per class; the negative-fit cache's private arrays (read by reflection, which
    /// must find at least one non-empty array); the check ring's writes; the entity and mail id allocators; and the live WorldItem count
    /// (story 010). Shared with <see cref="QuickMoveReturnTests"/>.
    /// </summary>
    internal static List<long> SideEffects(InvAutoPlaceRig rig)
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
                values.Add(unchecked((long)rig.Inventory.Occupancy.RowBits((ushort)s, y)));
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
        FieldInfo[] arrays = cache.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Where(f => f.FieldType.IsArray).ToArray();
        Assert.NotEmpty(arrays);
        int cells = 0;
        foreach (FieldInfo f in arrays)
        {
            var array = (Array?)f.GetValue(cache);
            Assert.NotNull(array);
            foreach (object v in array)
            {
                values.Add(Convert.ToInt64(v));
                cells++;
            }
        }

        Assert.True(cells > 0);
        SimCheckRing ring = rig.Host.World.CheckRing;
        values.Add(ring.Count + (long)ring.Overwritten + ring.Suppressed(CheckCode.InventoryArenaExhausted));
        for (int c = 0; c < 256; c++)
        {
            values.Add(rig.Host.Services.EntityIds.Peek((EntityClass)c));
        }

        values.Add(rig.Host.Services.MailIds.Peek());
        values.Add(rig.Inventory.WorldItems.LiveCount); // story 010: a reject (and return) never creates a WorldItem
        return values;
    }
}
