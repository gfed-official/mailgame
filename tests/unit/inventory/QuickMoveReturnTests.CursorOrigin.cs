using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

public partial class QuickMoveReturnTests
{
    // ------------------------------------------------------------------ cursor origin

    /// <summary>A pick from a chest cell records the entry, the chest and its cells and rotation as the actor's origin; the other slot has none.</summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Origin_PickFromAChestCell_RecordsTheEntryChestCellsAndRotation(int slot, bool rotated)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort logs = rig.PlaceItem(chest, "log", 2, 1, 4, rotated);
        Assert.False(rig.Inventory.TryGetCursorOrigin(PlayerSlot.FromValue((byte)slot), out _));

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, logs)));

        // Assert
        Assert.True(rig.Inventory.TryGetCursorOrigin(PlayerSlot.FromValue((byte)slot), out InventorySystem.CursorOrigin o));
        Assert.Equal((rig.Entries[logs].Id, rig.IdOf(chest), (byte)2, (byte)1, rotated), (o.Entry, o.Container, o.X, o.Y, o.Rotated));
        Assert.False(rig.Inventory.TryGetCursorOrigin(PlayerSlot.FromValue((byte)(1 - slot)), out _));
    }

    /// <summary>A pick from hotbar slot 3 records the hotbar and the slot.</summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Origin_PickFromAHotbarSlot_RecordsTheHotbarAndTheSlot(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort hotbar = rig.HotbarOf(slot);
        ushort axe = rig.PlaceItem(hotbar, "axe", 3, 0, 1);

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, hotbar, axe)));

        // Assert
        Assert.True(rig.Inventory.TryGetCursorOrigin(PlayerSlot.FromValue((byte)slot), out InventorySystem.CursorOrigin o));
        Assert.Equal((rig.Entries[axe].Id, rig.IdOf(hotbar), (byte)3, (byte)0, false), (o.Entry, o.Container, o.X, o.Y, o.Rotated));
    }

    /// <summary>A hotbar swap from the cursor records the displaced entry's hotbar slot as the origin of the entry now on the cursor.</summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Origin_HotbarSwap_RecordsTheDisplacedEntrysHotbarSlot(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort axe = rig.PlaceItem(hotbar, "axe", 2, 0, 1);
        ushort letters = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
        EntryRecord displaced = rig.Entries[axe];

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, hotbar, 2, 0)));

        // Assert: every field is the displaced entry's record before the swap
        Assert.Equal(cursor, rig.Entries[axe].ContainerSlot);
        Assert.True(rig.Inventory.TryGetCursorOrigin(PlayerSlot.FromValue((byte)slot), out InventorySystem.CursorOrigin o));
        Assert.Equal(
            (displaced.Id, rig.IdOf(displaced.ContainerSlot), displaced.X, displaced.Y, displaced.Rotated),
            (o.Entry, o.Container, o.X, o.Y, o.Rotated));
        Assert.Equal((rig.IdOf(hotbar), (byte)2, (byte)0, false), (o.Container, o.X, o.Y, o.Rotated));
    }

    /// <summary>
    /// The origin is present only while the cursor holds its entry: a partial place (the entry keeps its id on the cursor) keeps it, a
    /// whole place clears it (the mutator's <c>Remove</c> from the cursor), and the next pick replaces it.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Origin_PartialPlaceKeepsIt_WholePlaceClearsIt_TheNextPickReplacesIt(int slot)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort letters = rig.PlaceMail(chest, "letter", 3, 2, Address3, InvAutoPlaceRig.Ids(1, 5));
        ushort axe = rig.PlaceItem(hotbar, "axe", 2, 0, 1);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, letters)));

        // Act: a partial place
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, inventory, 0, 0, count: 2)));

        // Assert
        Assert.True(rig.Inventory.TryGetCursorOrigin(ps, out InventorySystem.CursorOrigin kept));
        Assert.Equal((rig.Entries[letters].Id, rig.IdOf(chest), (byte)3, (byte)2), (kept.Entry, kept.Container, kept.X, kept.Y));

        // Act: the whole place
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, inventory, 1, 0)));

        // Assert
        Assert.Equal(0, rig.Header(cursor).EntryCount);
        Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));

        // Act: the next pick
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, hotbar, axe)));

        // Assert
        Assert.True(rig.Inventory.TryGetCursorOrigin(ps, out InventorySystem.CursorOrigin next));
        Assert.Equal((rig.Entries[axe].Id, rig.IdOf(hotbar), (byte)2, (byte)0), (next.Entry, next.Container, next.X, next.Y));
    }

    // ------------------------------------------------------------------ cursor origin: one choke point (Mail Review r1 B1)

    /// <summary>
    /// The review repro (GDD R7 l.95, "into its origin cells"): planks 5 picked from chest (2,1), placed whole at inventory (3,0), then
    /// moved from there back onto the cursor. The move records inventory (3,0) as the origin (the mutator's <c>Place</c> into the cursor;
    /// Mail Lead planning default pending Evan, TD-153), so <c>return</c> puts the planks back at inventory (3,0) with their id, not at the
    /// pick's chest (2,1); the chest is unchanged, the cursor and the inventory bump once each.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_PickPlaceThenMoveBackToTheCursor_GoesToTheInventoryCellItCameFrom_NotThePickOrigin(int slot)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort planks = rig.PlaceItem(chest, "plank", 2, 1, 5);
        EntryId id = rig.Entries[planks].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, planks)));
        AssertOrigin(rig, ps, id, rig.IdOf(chest), 2, 1, false);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, planks, inventory, 3, 0)));
        Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, inventory, planks, cursor, 0, 0)));
        AssertOrigin(rig, ps, id, rig.IdOf(inventory), 3, 0, false);
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[cursor], versions[inventory]);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal((id, inventory, (byte)3, (byte)0, (byte)5), (rig.Entries[planks].Id, rig.Entries[planks].ContainerSlot, rig.Entries[planks].X, rig.Entries[planks].Y, rig.Entries[planks].Count));
        Assert.Equal(Occupancy.Free, rig.EntryAt(chest, 2, 1));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[cursor] + 1, versions[inventory] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
    }

    /// <summary>
    /// The review's second repro: planks 5 picked from chest (2,1); the leash put-away (<c>CloseExternal(Leash)</c> in phase) puts them at
    /// inventory (0,0) and clears the origin; a real <c>move</c> takes them to inventory (4,1) and then back onto the cursor (origin
    /// inventory (4,1)); the chest is opened again, so the pick's stale chest (2,1) would be both free and open. <c>return</c> puts them
    /// at inventory (4,1) (the panel leg would have chosen (0,0)); the chest is unchanged.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_LeashPutAwayThenMoveBackToTheCursor_GoesWhereItCameFrom_NotThePickOrigin(int slot)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort planks = rig.PlaceItem(chest, "plank", 2, 1, 5);
        EntryId id = rig.Entries[planks].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, planks)));
        Leash(rig, ps);
        Assert.Equal((inventory, (byte)0, (byte)0), (rig.Entries[planks].ContainerSlot, rig.Entries[planks].X, rig.Entries[planks].Y));
        Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, inventory, planks, inventory, 4, 1)));
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, inventory, planks, cursor, 0, 0)));
        Assert.True(rig.Inventory.TryOpenExternal(ps, rig.IdOf(chest)));
        Assert.Equal(Occupancy.Free, rig.EntryAt(chest, 2, 1));
        AssertOrigin(rig, ps, id, rig.IdOf(inventory), 4, 1, false);
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp()));

        // Assert
        Assert.Equal((id, inventory, (byte)4, (byte)1), (rig.Entries[planks].Id, rig.Entries[planks].ContainerSlot, rig.Entries[planks].X, rig.Entries[planks].Y));
        Assert.Equal(Occupancy.Free, rig.EntryAt(chest, 2, 1));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
    }

    /// <summary>
    /// An origin recorded for one entry never serves another: planks X picked from chest (2,1) (origin X) and placed whole at inventory
    /// (3,0), which clears it; then a different entry Y (3 letters) reaches the cursor. Rows: <c>placed_on_cursor</c> (the rig's
    /// mutator-backed <c>PlaceMail</c> onto the cursor: an entry from no container, so no origin) → <c>return</c> takes the panel leg
    /// (inventory (0,0), the mail cursor row's first free cell), never X's chest (2,1); <c>moved_to_cursor</c> (a real <c>move</c> from
    /// hotbar slot 4: origin hotbar slot 4) → <c>return</c> puts Y back in slot 4. The chest stays unchanged either way.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "placed_on_cursor")]
    [InlineData(1, "placed_on_cursor")]
    [InlineData(0, "moved_to_cursor")]
    [InlineData(1, "moved_to_cursor")]
    public void Return_ADifferentEntryOnTheCursorThanTheOneTheOriginWasRecordedFor_NeverUsesThatOrigin(int slot, string row)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort x = rig.PlaceItem(chest, "plank", 2, 1, 5);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, x)));
        AssertOrigin(rig, ps, rig.Entries[x].Id, rig.IdOf(chest), 2, 1, false);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, x, inventory, 3, 0)));
        ushort y;
        if (row == "placed_on_cursor")
        {
            y = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
            Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
        }
        else
        {
            y = rig.PlaceMail(hotbar, "letter", 4, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
            MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, hotbar, y, cursor, 0, 0)));
            AssertOrigin(rig, ps, rig.Entries[y].Id, rig.IdOf(hotbar), 4, 0, false);
        }

        EntryId yId = rig.Entries[y].Id;
        Assert.NotEqual(rig.Entries[x].Id, yId);
        uint chestVersion = rig.Header(chest).Version;

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp()));

        // Assert
        (ushort to, byte tx) = row == "placed_on_cursor" ? (inventory, (byte)0) : (hotbar, (byte)4);
        Assert.Equal((yId, to, tx, (byte)0), (rig.Entries[y].Id, rig.Entries[y].ContainerSlot, rig.Entries[y].X, rig.Entries[y].Y));
        Assert.Equal((Occupancy.Free, chestVersion), (rig.EntryAt(chest, 2, 1), rig.Header(chest).Version));
        Assert.Equal((inventory, (byte)3), (rig.Entries[x].ContainerSlot, rig.Entries[x].X));
        Assert.Equal(0, (int)rig.Header(cursor).EntryCount);
        Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
    }

    /// <summary>
    /// A departure counts only within its own call (the mutator's <c>Begin</c> empties the departure ring): the Axe in hotbar slot 3 is
    /// detached by <see cref="IInventoryApi.DetachForThrow"/> (a <c>Remove</c> from the hotbar), then placed on the cursor in a later tick
    /// through the rig's mutator-backed <c>PlaceDetached</c>. It arrives from no container in that call, so it has no origin, and
    /// <c>return</c> takes the panel leg (a tool: hotbar first, the lowest free slot 1), not slot 3.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Origin_AnEntryPlacedOnTheCursorInALaterCall_GetsNoOriginFromItsEarlierDeparture(int slot)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort axe = rig.PlaceItem(hotbar, "axe", 3, 0, 1);
        EntryId id = rig.Entries[axe].Id;
        DetachResult detached = IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.DetachForThrow(ps, id, 1));
        Assert.Equal((id, EntryRecord.NoContainer), (detached.Entry, rig.Entries[axe].ContainerSlot));

        // Act
        rig.PlaceDetached(axe, cursor, 0, 0);

        // Assert
        Assert.Equal(cursor, rig.Entries[axe].ContainerSlot);
        Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp()));

        // Assert
        Assert.Equal((id, hotbar, (byte)1), (rig.Entries[axe].Id, rig.Entries[axe].ContainerSlot, rig.Entries[axe].X));
    }

    /// <summary>
    /// Both players hold origins at once and neither write touches the other's: the actor picks planks from chest (2,1), the other player
    /// letters from chest (5,3); each places whole into their own inventory ((3,0) and (6,1)) and moves them back onto their cursor, the
    /// actor first. Each origin is the player's own inventory cell. The actor's <c>return</c> puts the planks back at (3,0) and leaves
    /// the other player's cursor, origin and panel versions untouched; the other's <c>return</c> then puts the letters back at (6,1).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_BothPlayersHoldOrigins_EachReturnGoesToItsOwnOrigin_NeitherInterferes(int slot)
    {
        // Arrange
        var rig = NewRig();
        int other = 1 - slot;
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        PlayerSlot po = PlayerSlot.FromValue((byte)other);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        Assert.True(rig.Inventory.TryOpenExternal(po, rig.IdOf(chest)));
        ushort inventory = rig.InventoryOf(slot);
        ushort otherInventory = rig.InventoryOf(other);
        ushort cursor = rig.CursorOf(slot);
        ushort otherCursor = rig.CursorOf(other);
        ushort planks = rig.PlaceItem(chest, "plank", 2, 1, 5);
        ushort letters = rig.PlaceMail(chest, "letter", 5, 3, Address3, InvAutoPlaceRig.Ids(1, 4));
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, planks)));
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(other), PickOp(rig, chest, letters)));
        AssertOrigin(rig, ps, rig.Entries[planks].Id, rig.IdOf(chest), 2, 1, false);
        AssertOrigin(rig, po, rig.Entries[letters].Id, rig.IdOf(chest), 5, 3, false);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, planks, inventory, 3, 0)));
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(other), MoveOp(rig, otherCursor, letters, otherInventory, 6, 1)));
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, inventory, planks, cursor, 0, 0)));
        Assert.False(rig.Inventory.TryGetCursorOrigin(po, out _));
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(other), MoveOp(rig, otherInventory, letters, otherCursor, 0, 0)));
        AssertOrigin(rig, ps, rig.Entries[planks].Id, rig.IdOf(inventory), 3, 0, false);
        AssertOrigin(rig, po, rig.Entries[letters].Id, rig.IdOf(otherInventory), 6, 1, false);
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp()));

        // Assert
        Assert.Equal((inventory, (byte)3, (byte)0), (rig.Entries[planks].ContainerSlot, rig.Entries[planks].X, rig.Entries[planks].Y));
        Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
        Assert.Equal(otherCursor, rig.Entries[letters].ContainerSlot);
        AssertOrigin(rig, po, rig.Entries[letters].Id, rig.IdOf(otherInventory), 6, 1, false);
        Assert.Equal((versions[otherCursor], versions[otherInventory], versions[chest]), (rig.Header(otherCursor).Version, rig.Header(otherInventory).Version, rig.Header(chest).Version));

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(other), ReturnOp()));

        // Assert
        Assert.Equal((otherInventory, (byte)6, (byte)1), (rig.Entries[letters].ContainerSlot, rig.Entries[letters].X, rig.Entries[letters].Y));
        Assert.False(rig.Inventory.TryGetCursorOrigin(po, out _));
        Assert.Equal((inventory, (byte)3), (rig.Entries[planks].ContainerSlot, rig.Entries[planks].X));
        Assert.Equal(versions[chest], rig.Header(chest).Version);
    }

    /// <summary>
    /// The origin is cleared by every way an entry leaves the cursor in code today and kept while part of it stays (the entry keeps its id
    /// on the cursor). Letters X = [1..5] to #3 picked from chest (2,1). Cleared: <c>whole_place</c> (a <c>move</c> to inventory (3,0));
    /// <c>merge_out_move</c> (a whole <c>move</c> onto a same-key inventory stack of 3); <c>merge_op</c> (a <c>merge</c> op into that
    /// stack); <c>quick_move</c> (the cursor row); <c>return_origin</c>; <c>return_panel</c> (after a real <c>close</c> of the chest);
    /// <c>leash</c> (the put-away); <c>consume</c> (five <c>ConsumeUnit</c> calls, the last removes it); <c>swap</c> (a <c>move</c> onto
    /// the hotbar Axe's slot 2: the origin is now the Axe's, slot 2). Kept: <c>partial_place</c> (count 2), <c>partial_merge_out</c> (a
    /// whole move onto a stack of 18 with room 2), <c>partial_quick_move</c> (count 2), <c>partial_consume</c> (one unit).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "whole_place")]
    [InlineData(1, "whole_place")]
    [InlineData(0, "merge_out_move")]
    [InlineData(1, "merge_out_move")]
    [InlineData(0, "merge_op")]
    [InlineData(1, "merge_op")]
    [InlineData(0, "quick_move")]
    [InlineData(1, "quick_move")]
    [InlineData(0, "return_origin")]
    [InlineData(1, "return_origin")]
    [InlineData(0, "return_panel")]
    [InlineData(1, "return_panel")]
    [InlineData(0, "leash")]
    [InlineData(1, "leash")]
    [InlineData(0, "consume")]
    [InlineData(1, "consume")]
    [InlineData(0, "swap")]
    [InlineData(1, "swap")]
    [InlineData(0, "partial_place")]
    [InlineData(1, "partial_place")]
    [InlineData(0, "partial_merge_out")]
    [InlineData(1, "partial_merge_out")]
    [InlineData(0, "partial_quick_move")]
    [InlineData(1, "partial_quick_move")]
    [InlineData(0, "partial_consume")]
    [InlineData(1, "partial_consume")]
    public void Origin_ClearedByEveryWayTheEntryLeavesTheCursor_KeptWhilePartOfItStays(int slot, string way)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort x = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 5));
        ushort axe = rig.PlaceItem(hotbar, "axe", 2, 0, 1);
        ushort stack = way switch
        {
            "merge_out_move" or "merge_op" => rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 3)),
            "partial_merge_out" => rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 18)),
            _ => Occupancy.Free,
        };
        EntryId xId = rig.Entries[x].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, x)));
        AssertOrigin(rig, ps, xId, rig.IdOf(chest), 2, 1, false);

        // Act
        switch (way)
        {
            case "whole_place":
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, x, inventory, 3, 0)));
                break;
            case "merge_out_move":
            case "partial_merge_out":
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, x, inventory, 0, 0)));
                break;
            case "merge_op":
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MergeOp(rig, cursor, x, inventory, stack)));
                break;
            case "quick_move":
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, cursor, x)));
                break;
            case "partial_quick_move":
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, cursor, x, 2)));
                break;
            case "return_origin":
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp()));
                Assert.Equal(chest, rig.Entries[x].ContainerSlot);
                break;
            case "return_panel":
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), CloseOp(rig, chest)));
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp()));
                Assert.Equal(inventory, rig.Entries[x].ContainerSlot);
                break;
            case "leash":
                Leash(rig, ps);
                Assert.Equal(inventory, rig.Entries[x].ContainerSlot);
                break;
            case "consume":
                MoveSplitMergeTests.ConsumeUnits(rig, cursor, x, 5);
                break;
            case "partial_consume":
                var at = new EntryRef(rig.IdOf(cursor), xId);
                Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () => rig.Inventory.ConsumeUnit(at, ConsumeReason.Delivered, ActorId.System)));
                break;
            case "swap":
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, x, hotbar, 2, 0)));
                break;
            default:
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, x, inventory, 3, 0, count: 2)));
                break;
        }

        // Assert
        if (way.StartsWith("partial_", StringComparison.Ordinal))
        {
            Assert.Equal((xId, cursor), (rig.Entries[x].Id, rig.Entries[x].ContainerSlot));
            Assert.True(rig.Entries[x].Count < 5);
            AssertOrigin(rig, ps, xId, rig.IdOf(chest), 2, 1, false);
            return;
        }

        if (way == "swap")
        {
            Assert.Equal((hotbar, cursor), (rig.Entries[x].ContainerSlot, rig.Entries[axe].ContainerSlot));
            AssertOrigin(rig, ps, rig.Entries[axe].Id, rig.IdOf(hotbar), 2, 0, false);
            return;
        }

        Assert.Equal(0, (int)rig.Header(cursor).EntryCount);
        Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out InventorySystem.CursorOrigin cleared));
        Assert.Equal(default, cleared);
    }

    /// <summary>
    /// A move or a split onto the cursor records where the entry or its source was (Mail Lead planning default pending Evan, TD-153: no
    /// GDD or ADR line defines the origin of a move or split onto the cursor), and <c>return</c> uses it. Rows: <c>move_whole</c> (planks
    /// 5 moved from inventory (3,0): origin inventory (3,0), back there with the same id); <c>split_item</c> (planks 5 at chest (2,1),
    /// <c>move count 2</c> onto the cursor: the piece's new id with origin chest (2,1), the source's cells; <c>return</c> merges it back into
    /// the source, 5 planks, the piece retired); <c>split_mail</c> (letters [1..5] the same way: piece [1, 2] merges back, [1..5]
    /// ascending). The cursor and the receiver bump once each.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "move_whole")]
    [InlineData(1, "move_whole")]
    [InlineData(0, "split_item")]
    [InlineData(1, "split_item")]
    [InlineData(0, "split_mail")]
    [InlineData(1, "split_mail")]
    public void Origin_MoveOrSplitOntoTheCursor_RecordsTheSourceCells_ReturnPutsItBack(int slot, string row)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort from = row == "move_whole" ? inventory : chest;
        (byte fx, byte fy) = row == "move_whole" ? ((byte)3, (byte)0) : ((byte)2, (byte)1);
        ushort source = row == "split_mail"
            ? rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 5))
            : rig.PlaceItem(from, "plank", fx, fy, 5);
        EntryId sourceId = rig.Entries[source].Id;
        uint lastId = rig.Entries.LastId;

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, from, source, cursor, 0, 0, count: row == "move_whole" ? (byte)0 : (byte)2)));

        // Assert
        ushort held = rig.EntryAt(cursor, 0, 0);
        EntryId heldId = rig.Entries[held].Id;
        Assert.Equal(row == "move_whole" ? sourceId : EntryId.Real(lastId + 1), heldId);
        AssertOrigin(rig, ps, heldId, rig.IdOf(from), fx, fy, false);
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp()));

        // Assert
        Assert.Equal(0, (int)rig.Header(cursor).EntryCount);
        Assert.Equal((sourceId, from, fx, fy, (byte)5), (rig.Entries[source].Id, rig.Entries[source].ContainerSlot, rig.Entries[source].X, rig.Entries[source].Y, rig.Entries[source].Count));
        Assert.Equal(row == "move_whole", rig.Entries.TryGetSlot(heldId, out _));
        if (row == "split_mail")
        {
            Assert.Equal(InvAutoPlaceRig.Ids(1, 5), rig.IdsOf(source));
            MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
        }

        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[from]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
    }
}
