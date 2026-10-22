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

/// <summary>
/// Story grid-inventory/010, slice 1: the cursor origin (<see cref="InventorySystem.TryGetCursorOrigin"/>, written by the
/// <c>Pick</c> commit and the <c>Move</c> swap commit), the R9 quick_move rows (<see cref="TargetOrder.QuickMoveRow"/>; GDD l.120-128)
/// and the <c>QuickMove</c> op, entry form (<see cref="QuickMoveHandler"/>; GDD R7 l.93, R8, R9, H-R9-1, H-R6-1 d; ADR-0009 sections
/// 1-3), through the real phase-0 drain of a composed world with players A (slot 0) and B (slot 1). Every world case runs from slot 0
/// and from slot 1. State is arranged only through real calls: the rig's mutator-backed <c>PlaceMail</c> / <c>PlaceItem</c>,
/// <c>TryOpenExternal</c>, Intake <c>Spawn</c>, <c>ConsumeUnit</c>, the rig's documented settings overrides and the real ops. Every
/// reject asserts the reason, <c>ResultVersionA/B</c>, that the tick log holds the <c>OpReply</c> alone (no record, no seal; TD-105 item
/// 5) and <see cref="MoveSplitMergeTests.SideEffects"/> unchanged (headers with versions, occupancy row bitmaps, entries with mail ids and
/// StackKey-index membership, the arena, the negative-fit cache, weight P, <c>CountItem</c>, the id allocators and the WorldItem
/// count). The QuickMove chain order, <c>count &gt; total</c> → <c>bad_count</c> and an arena refusal leaving the entry where it is are
/// Mail Lead planning defaults pending Evan (TD-153); the codes are ADR-0009's (section 2 reason-code list). Slice 2: the <c>Return</c>
/// op (<see cref="ReturnHandler"/>; GDD R7 l.95, H-R7-9 l.706): the origin leg, the panel leg (<see cref="TargetOrder.CursorRow"/>),
/// <c>no_room</c>, <c>capacity_exhausted</c>, <c>cursor_empty</c> and the chain, with the same reject contract; a <c>return</c> never
/// creates a WorldItem. Return's fall-through, the partial-merge continuation and its chain order beyond the GDD's legs are Mail Lead
/// planning defaults pending Evan (TD-153).
/// </summary>
public class QuickMoveReturnTests
{
    private const uint Address3 = 3u;
    private const uint Address5 = 5u;

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
    /// The origin is valid only while the cursor holds its entry: a partial place (the entry keeps its id on the cursor) keeps it, a
    /// whole place makes it inert with no clearing hook, and the next pick replaces it.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Origin_PartialPlaceKeepsIt_WholePlaceMakesItInert_TheNextPickReplacesIt(int slot)
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

    // ------------------------------------------------------------------ R9 quick_move rows (pure)

    /// <summary>
    /// Every M-1a R9 quick_move row (GDD l.124-128; backpack skipped): a panel source with an external open → the external only; the
    /// inventory with none → the hotbar; the hotbar with none → the inventory; the external and the cursor → the category order
    /// (tool/weapon/consumable hotbar first, else, mail included, inventory first), whether or not the external flag is set.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R9-1")]
    [InlineData("Hotbar", "Tool", true, "E")]
    [InlineData("Hotbar", "Mail", true, "E")]
    [InlineData("Inventory", "Tool", true, "E")]
    [InlineData("Inventory", "Mail", true, "E")]
    [InlineData("Inventory", "Material", false, "H")]
    [InlineData("Inventory", "Tool", false, "H")]
    [InlineData("Hotbar", "Tool", false, "I")]
    [InlineData("Hotbar", "Mail", false, "I")]
    [InlineData("External", "Tool", true, "HI")]
    [InlineData("External", "Weapon", true, "HI")]
    [InlineData("External", "Consumable", true, "HI")]
    [InlineData("External", "Material", true, "IH")]
    [InlineData("External", "Mail", true, "IH")]
    [InlineData("External", "Mail", false, "IH")]
    [InlineData("Cursor", "Tool", true, "HI")]
    [InlineData("Cursor", "Mail", false, "IH")]
    [InlineData("Cursor", "Material", true, "IH")]
    public void QuickMoveRow_EveryM1aRow_WritesTheR9Targets(string sourceName, string categoryName, bool externalOpen, string expected)
    {
        // Arrange
        QuickMoveSource source = Enum.Parse<QuickMoveSource>(sourceName);
        ItemCategory category = Enum.Parse<ItemCategory>(categoryName);
        const ushort Hotbar = 11;
        const ushort Inventory = 12;
        const ushort External = 13;
        Span<ushort> targets = stackalloc ushort[TargetOrder.MaxRowLength];
        targets.Fill(ushort.MaxValue);

        // Act
        int n = TargetOrder.QuickMoveRow(source, category, externalOpen, Hotbar, Inventory, External, targets);

        // Assert
        ushort[] want = expected.Select(c => c switch { 'H' => Hotbar, 'I' => Inventory, _ => External }).ToArray();
        Assert.Equal(want, targets[..n].ToArray());
        Assert.True(n <= TargetOrder.MaxRowLength);
    }

    // ------------------------------------------------------------------ H-R9-1

    /// <summary>
    /// H-R9-1 (M-1a rows, every container has space): the entry lands where R9 lists, whole, keeping its id, and nowhere else — exactly
    /// <c>from</c> and the target bump +1, every other container's version and entry count unchanged. The chest is an ownerless 8 x 4
    /// <c>player_inventory</c>-def grid. Rows: inventory with the chest open → chest; inventory, no external → hotbar; hotbar, no
    /// external → inventory; from the chest: Axe → hotbar, Plank → inventory, letter → inventory; from the cursor (the cursor row): Axe →
    /// hotbar, letter → inventory. The reply carries <c>from</c> (A) and the target (B) and requested = got = the count.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R9-1")]
    [InlineData(0, "inventory_chest_open")]
    [InlineData(1, "inventory_chest_open")]
    [InlineData(0, "inventory_no_external")]
    [InlineData(1, "inventory_no_external")]
    [InlineData(0, "hotbar_no_external")]
    [InlineData(1, "hotbar_no_external")]
    [InlineData(0, "chest_axe")]
    [InlineData(1, "chest_axe")]
    [InlineData(0, "chest_plank")]
    [InlineData(1, "chest_plank")]
    [InlineData(0, "chest_letter")]
    [InlineData(1, "chest_letter")]
    [InlineData(0, "cursor_axe")]
    [InlineData(1, "cursor_axe")]
    [InlineData(0, "cursor_letter")]
    [InlineData(1, "cursor_letter")]
    public void HR91_EveryRow_LandsWhereListed_AndNowhereElse(int slot, string row)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = row is "hotbar_no_external" or "inventory_no_external" ? rig.CreateGrid(8, 4) : MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        (ushort from, ushort entry, ushort to, int x) = row switch
        {
            "inventory_chest_open" => (inventory, rig.PlaceItem(inventory, "axe", 0, 0, 1), chest, 0),
            "inventory_no_external" => (inventory, rig.PlaceItem(inventory, "plank", 0, 0, 5), hotbar, 1),
            "hotbar_no_external" => (hotbar, rig.PlaceItem(hotbar, "axe", 1, 0, 1), inventory, 0),
            "chest_axe" => (chest, rig.PlaceItem(chest, "axe", 3, 1, 1), hotbar, 1),
            "chest_plank" => (chest, rig.PlaceItem(chest, "plank", 3, 1, 5), inventory, 0),
            "chest_letter" => (chest, rig.PlaceMail(chest, "letter", 3, 1, Address3, InvAutoPlaceRig.Ids(1, 4)), inventory, 0),
            "cursor_axe" => (cursor, rig.PlaceItem(cursor, "axe", 0, 0, 1), hotbar, 1),
            _ => (cursor, rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 4)), inventory, 0),
        };
        EntryId id = rig.Entries[entry].Id;
        byte count = rig.Entries[entry].Count;
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        int[] entryCounts = EntryCounts(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, from, entry));

        // Assert: where R9 lists, whole, same id
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal((id, to, (byte)x, (byte)0, count), (rig.Entries[entry].Id, rig.Entries[entry].ContainerSlot, rig.Entries[entry].X, rig.Entries[entry].Y, rig.Entries[entry].Count));

        // Assert: nowhere else
        uint[] expectedVersions = (uint[])versions.Clone();
        expectedVersions[from]++;
        expectedVersions[to]++;
        Assert.Equal(expectedVersions, MoveSplitMergeTests.Versions(rig));
        int[] expectedCounts = (int[])entryCounts.Clone();
        expectedCounts[from]--;
        expectedCounts[to]++;
        Assert.Equal(expectedCounts, EntryCounts(rig));
        Assert.Equal((versions[from] + 1, versions[to] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(((ushort)count, (ushort)count), (extra.RequestedUnits, extra.GotUnits));
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
    }

    /// <summary>
    /// H-R9-1, Intake row: a letter from the inventory with the Intake open goes to the Intake, inserted NoMerge (R14 l.141) beside a
    /// resident letter of the same StackKey, whole with its id, and stamped with a new <c>arrivalSeq</c> above the resident's, so it
    /// leaves last; only the inventory and the Intake bump.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R9-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR91_LetterFromTheInventoryWithTheIntakeOpen_GoesToTheIntake_NoMerge_NewArrivalSeq(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        DefIndex letter = rig.KindIndex("letter");
        Assert.Equal(1, IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address3, 0, new[] { new MailId(500) }, MergePolicy.NoMerge).Placed));
        ushort resident = rig.EntryAt(rig.Intake, 0, 0);
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)slot), rig.Inventory.IntakeContainer));
        ushort letters = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
        Assert.Equal(StackKey.Of(in rig.Entries[resident]), StackKey.Of(in rig.Entries[letters]));
        EntryId id = rig.Entries[letters].Id;
        uint residentSeq = rig.Entries[resident].ArrivalSeq;
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, inventory, letters));

        // Assert
        MoveSplitMergeTests.AssertOk(log);
        Assert.Equal((id, rig.Intake, (byte)3), (rig.Entries[letters].Id, rig.Entries[letters].ContainerSlot, rig.Entries[letters].Count));
        Assert.Equal((2, (byte)1), ((int)rig.Header(rig.Intake).EntryCount, rig.Entries[resident].Count));
        Assert.True(rig.Entries[letters].ArrivalSeq > residentSeq);
        Assert.DoesNotContain(MoveSplitMergeTests.RecordHeads(log), e => e.Kind == InvEventKinds.Merged);
        uint[] expected = (uint[])versions.Clone();
        expected[inventory]++;
        expected[rig.Intake]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
    }

    /// <summary>H-R9-1: an Axe from the inventory with the Intake open → <c>not_allowed</c> (the only target rejects tools), no fallback to the hotbar.</summary>
    [Theory]
    [Trait("Id", "H-R9-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR91_AxeFromTheInventoryWithTheIntakeOpen_NotAllowed_NoFallback(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)slot), rig.Inventory.IntakeContainer));
        ushort axe = rig.PlaceItem(inventory, "axe", 0, 0, 1);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, inventory, axe));

        // Assert
        AssertRejected(rig, log, InvReason.NotAllowed, before, slot, (rig.Header(inventory).Version, rig.Header(rig.Intake).Version));
    }

    /// <summary>H-R9-1: a plank from the inventory with the external open but full → <c>no_room</c>, no fallback (the hotbar has room).</summary>
    [Theory]
    [Trait("Id", "H-R9-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR91_InventoryWithTheExternalOpenButFull_NoRoom_NoFallback(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort full = OpenGrid(rig, slot, 1, 1);
        _ = rig.PlaceItem(full, "fiber", 0, 0, 20);
        ushort planks = rig.PlaceItem(inventory, "plank", 0, 0, 5);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, inventory, planks));

        // Assert
        AssertRejected(rig, log, InvReason.NoRoom, before, slot, (rig.Header(inventory).Version, rig.Header(full).Version));
    }

    // ------------------------------------------------------------------ H-R6-1 d

    /// <summary>
    /// H-R6-1 d: A = letters [4, 9, 15, 21, 30] to #5 in the open chest; the inventory is full and the only room in the panel is one
    /// unit in a hotbar stack to #5 (19/20). <c>quick_move {A, count 0}</c> moves exactly one unit, mailId 4 (the lowest), into the
    /// hotbar stack (19 → 20, ascending, no duplicate); A keeps its entryId and cells with [9, 15, 21, 30]; reply ok, requested 5 / got 1;
    /// chest +1 and hotbar +1, inventory and cursor unchanged. A second <c>quick_move {A}</c> → <c>no_room</c>, nothing changes.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R6-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void HR61d_OnlyOneUnitOfRoom_QuickMoveMovesExactlyMailId4_AKeepsItsIdAndCells_AgainNoRoom(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        for (int i = 0; i < 16; i++)
        {
            _ = rig.PlaceItem(inventory, "plank", i % 8, i / 8, 1);
        }

        for (int s = 1; s < 7; s++)
        {
            _ = rig.PlaceItem(hotbar, "fiber", s, 0, 1);
        }

        ushort stack = rig.PlaceMail(hotbar, "letter", 7, 0, Address5, InvAutoPlaceRig.Ids(100, 19));
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address5, new uint[] { 4, 9, 15, 21, 30 });
        EntryId aId = rig.Entries[a].Id;
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a));

        // Assert
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal(new uint[] { 4 }.Concat(InvAutoPlaceRig.Ids(100, 19)).ToArray(), rig.IdsOf(stack));
        Assert.Equal((byte)20, rig.Entries[stack].Count);
        Assert.Equal((aId, chest, (byte)2, (byte)1), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y));
        Assert.Equal(new uint[] { 9, 15, 21, 30 }, rig.IdsOf(a));
        Assert.Equal(((ushort)5, (ushort)1), (extra.RequestedUnits, extra.GotUnits));
        uint[] expected = (uint[])versions.Clone();
        expected[chest]++;
        expected[hotbar]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[inventory], versions[cursor]), (rig.Header(inventory).Version, rig.Header(cursor).Version));
        Assert.Equal((versions[chest] + 1, versions[hotbar] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] again = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a));

        // Assert: B is the row's first target (mail from the chest: the inventory)
        AssertRejected(rig, again, InvReason.NoRoom, before, slot, (rig.Header(chest).Version, rig.Header(inventory).Version));
    }

    // ------------------------------------------------------------------ count

    /// <summary>
    /// QuickMove count (Mail Lead planning default pending Evan, TD-153 item 1): A = 10 letters [1..10] to #3 at chest (2,1), the
    /// inventory empty (the row is [inventory, hotbar]). <c>count</c> 0 or 10 moves A whole to inventory (0,0) with its id; 1 and 9
    /// (total − 1) split the count oldest ids into a new entry (the next id) at inventory (0,0) while A keeps its id, cells and the
    /// newest ids, with records <c>InvRemoved</c> + <c>InvSplitFrom</c> (chest), <c>InvInserted</c> (inventory), then the chest's and
    /// the inventory's seals; 11 (total + 1) is <c>bad_count</c> and nothing changes. Ok replies carry requested = got = the units, chest
    /// +1 and inventory +1 (A, B), and P rises by the letter's classPts.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 9)]
    [InlineData(1, 9)]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    [InlineData(0, 11)]
    [InlineData(1, 11)]
    public void Count_ZeroOrTotalIsWhole_OneToTotalMinusOneSplitsTheOldest_AboveTheTotalBadCount(int slot, byte count)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        _ = rig.PlaceItem(chest, "plank", 7, 3, 1); // a second chest entry, so the chest keeps entries in every row
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        EntryId aId = rig.Entries[a].Id;
        uint lastId = rig.Entries.LastId;
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        short points = rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)slot));
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, count));

        // Assert
        if (count > 10)
        {
            AssertRejected(rig, log, InvReason.BadCount, before, slot, (versions[chest], versions[inventory]));
            return;
        }

        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        int units = count is 0 or 10 ? 10 : count;
        Assert.Equal(((ushort)units, (ushort)units), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal((versions[chest] + 1, versions[inventory] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        ushort placed = rig.EntryAt(inventory, 0, 0);
        Assert.Equal(InvAutoPlaceRig.Ids(1, units), rig.IdsOf(placed));
        if (units == 10)
        {
            Assert.Equal((a, aId, lastId), (placed, rig.Entries[a].Id, rig.Entries.LastId));
        }
        else
        {
            Assert.Equal((lastId + 1, (byte)units), (rig.Entries[placed].Id.Value, rig.Entries[placed].Count));
            Assert.Equal((aId, chest, (byte)2, (byte)1), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y));
            Assert.Equal(InvAutoPlaceRig.Ids((uint)(1 + units), 10 - units), rig.IdsOf(a));
            Assert.Equal(
                new[] { InvEventKinds.Removed, InvEventKinds.SplitFrom, InvEventKinds.Inserted, InvEventKinds.Version, InvEventKinds.Version },
                MoveSplitMergeTests.RecordHeads(log).Select(e => e.Kind).ToArray());
        }

        uint[] expected = (uint[])versions.Clone();
        expected[chest]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal(points + MoveSplitMergeTests.ClassPts(rig, placed), rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)slot)));
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
    }

    /// <summary>
    /// A partial quick_move merges first, then places the rest as one new entry, oldest ids first (R6, R8): chest A = 10 letters
    /// [1..10] to #3, inventory stack S = 18 letters [100..117] to #3 at (0,0); <c>count 3</c> → S takes [1, 2] (20), a new entry holds
    /// [3] at inventory (1,0), A keeps [4..10] with its id. With S at 15 instead (room 5), all 3 merge: no new entry, no id spent, no
    /// <c>InvSplitFrom</c>.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, 18)]
    [InlineData(1, 18)]
    [InlineData(0, 15)]
    [InlineData(1, 15)]
    public void Partial_MergesFirstThenSplitsTheRestAsOneNewEntry_OldestIdsFirst(int slot, int stackCount)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort s = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, stackCount));
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        uint lastId = rig.Entries.LastId;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, 3));

        // Assert
        MoveSplitMergeTests.AssertOk(log);
        Assert.Equal(InvAutoPlaceRig.Ids(4, 7), rig.IdsOf(a));
        if (stackCount == 18)
        {
            Assert.Equal(new uint[] { 1, 2 }.Concat(InvAutoPlaceRig.Ids(100, 18)).ToArray(), rig.IdsOf(s));
            ushort piece = rig.EntryAt(inventory, 1, 0);
            Assert.Equal(lastId + 1, rig.Entries[piece].Id.Value);
            Assert.Equal(new uint[] { 3 }, rig.IdsOf(piece));
        }
        else
        {
            Assert.Equal(new uint[] { 1, 2, 3 }.Concat(InvAutoPlaceRig.Ids(100, 15)).ToArray(), rig.IdsOf(s));
            Assert.Equal((lastId, 1), (rig.Entries.LastId, (int)rig.Header(inventory).EntryCount));
            Assert.DoesNotContain(MoveSplitMergeTests.RecordHeads(log), e => e.Kind == InvEventKinds.SplitFrom);
        }

        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
    }

    /// <summary>
    /// The 255 cap (content with <c>letter.maxStack</c> 255): inventory stack 250 letters to #3, chest A = 10 letters. A partial
    /// <c>count 5</c> merges all 5 (255, no new entry); <c>count 6</c> merges 5 and places the 6th as a new entry; A keeps the rest.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(0, 6)]
    [InlineData(1, 6)]
    public void Partial_MailAtThe255Cap_MergesUpToTheCap_TheRestBecomesANewEntry(int slot, byte count)
    {
        // Arrange
        var rig = NewRig(MoveSplitMergeTests.MailStack255Content());
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort s = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1001, 250));
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        uint lastId = rig.Entries.LastId;

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, count))).ExtraAs<InvReplyExtra>();

        // Assert
        Assert.Equal(((ushort)count, (ushort)count), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal(InvAutoPlaceRig.Ids(1, 5).Concat(InvAutoPlaceRig.Ids(1001, 250)).ToArray(), rig.IdsOf(s));
        Assert.Equal((byte)255, rig.Entries[s].Count);
        Assert.Equal(InvAutoPlaceRig.Ids((uint)(1 + count), 10 - count), rig.IdsOf(a));
        Assert.Equal(count == 6 ? lastId + 1 : lastId, rig.Entries.LastId);
        Assert.Equal(count == 6 ? 2 : 1, (int)rig.Header(inventory).EntryCount);
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
    }

    /// <summary>
    /// Non-stackables are all-or-nothing (GDD R7 l.93, R8): a Large letter (2 x 4, stored rotated) in the inventory with an open 3 x 3
    /// external that can hold neither orientation → <c>no_room</c>, nothing moved, no bump.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R9-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void Whole_LargeWithNoRoomInAnyTarget_NoRoom_NothingMoves(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort small = OpenGrid(rig, slot, 3, 3);
        ushort large = rig.PlaceMail(inventory, "large", 0, 0, Address3, new uint[] { 7 }, rotated: true);
        Assert.Equal((byte)1, rig.Entries[large].Count);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, inventory, large));

        // Assert
        AssertRejected(rig, log, InvReason.NoRoom, before, slot, (rig.Header(inventory).Version, rig.Header(small).Version));
    }

    // ------------------------------------------------------------------ not_open, stale_entry, capacity_exhausted

    /// <summary><c>not_open</c>: <c>from</c> is the other player's inventory, or a chest the actor has not opened (versions not yet resolved: 0, 0).</summary>
    [Theory]
    [Trait("Id", "H-R10-2")]
    [InlineData(0, "other_inventory")]
    [InlineData(1, "other_inventory")]
    [InlineData(0, "unopened_chest")]
    [InlineData(1, "unopened_chest")]
    public void NotOpen_FromOutsideTheActorsOpenSet_NotOpen_NothingChanges(int slot, string which)
    {
        // Arrange
        var rig = NewRig();
        ushort from = which == "other_inventory" ? rig.InventoryOf(1 - slot) : rig.CreateGrid(8, 4);
        ushort planks = rig.PlaceItem(from, "plank", 0, 0, 5);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, from, planks));

        // Assert
        AssertRejected(rig, log, InvReason.NotOpen, before, slot, (0u, 0u));
    }

    /// <summary>
    /// <c>not_open</c> for an actor with no panel (as <see cref="PickHandler"/>'s cursor clause): a 6-container table holds the Intake and
    /// player A's panel, so player B's <c>PlayerJoin</c> finds it too full for a panel (the join records
    /// <c>InventoryContainerCapacity</c>); B can still open an ownerless chest (<c>TryOpenExternal</c>), whose letters have no R9 target.
    /// Slot 1 only: the rig always joins A (slot 0) first, so slot 0 cannot be the panel-less one.
    /// </summary>
    [Fact]
    [Trait("Id", "H-R10-2")]
    public void NotOpen_AnActorWithNoPanel_NotOpen_NothingChanges()
    {
        // Arrange
        var rig = new InvAutoPlaceRig(tune: s => s with { ContainerCapacity = 6 }, joinPlayerB: true);
        PlayerSlot b = PlayerSlot.FromValue(1);
        Assert.True(rig.Inventory.HotbarOf(b).IsNone);
        ushort chest = rig.CreateGrid(8, 4);
        Assert.True(rig.Inventory.TryOpenExternal(b, rig.IdOf(chest)));
        ushort letters = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 3));
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(1), QuickMoveOp(rig, chest, letters));

        // Assert
        AssertRejected(rig, log, InvReason.NotOpen, before, 1, (0u, 0u));
    }

    /// <summary>
    /// <c>stale_entry</c>: an id retired by a real merge (the letters merged away by a whole <c>move</c>), or a live entry that is not in
    /// <c>from</c> (it is in the hotbar). Nothing changes; the entry is not resolved, so the versions are (0, 0).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-4")]
    [InlineData(0, "retired")]
    [InlineData(1, "retired")]
    [InlineData(0, "not_in_from")]
    [InlineData(1, "not_in_from")]
    public void StaleEntry_ARetiredIdOrALiveEntryNotInFrom_StaleEntry_NothingChanges(int slot, string which)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        uint id = which == "retired" ? RetiredId(rig, slot) : rig.Entries[rig.PlaceItem(rig.HotbarOf(slot), "axe", 1, 0, 1)].Id.Value;
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveRaw(rig.IdOf(inventory).Value, id, 0));

        // Assert
        AssertRejected(rig, log, InvReason.StaleEntry, before, slot, (0u, 0u));
    }

    /// <summary>
    /// The InvMalformedOpTests stub rig's valid row, moved here now that the real handler is bound: its live entries are allocated but
    /// placed in no container, so a well-formed <c>quick_move</c> on one is <c>stale_entry</c>, from either slot.
    /// </summary>
    [Theory]
    [Trait("Id", "H-NET-5-sim")]
    [InlineData(0)]
    [InlineData(1)]
    public void QuickMove_OnTheStubRigsUnplacedLiveEntry_IsStaleEntry(int slot)
    {
        // Arrange
        var rig = new InvOpRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        var op = new Op { Kind = InvOpKinds.QuickMove, ReqId = 11 };
        op.SetPayload(new QuickMovePayload { From = rig.Inventory.PlayerInventoryOf(ps).Value, EntryId = rig.LiveIds[0], Count = 1 });

        // Act + Assert
        rig.AssertSingleReply(op, slot == 0 ? rig.PlayerA : rig.PlayerB, (ushort)InvReason.StaleEntry);
    }

    /// <summary>
    /// <c>capacity_exhausted</c> (code 85, ADR-0009 section 2), checked last, leaves the entry where it is (Mail Lead planning default
    /// pending Evan, TD-153; it follows the pattern of Evan's 2026-10-22 5:11 AM PT ruling, which covers only a leashed letter staying on
    /// the cursor when the mail-id arena is full, even with a free cell). Rows: a whole quick_move whose merge needs a union block, with
    /// free cells in the inventory, under an exhausted minimum arena; a partial quick_move to free cells whose new block cannot be
    /// reserved; a partial item split with the entry pool full. The whole move to free cells (no block, no entry) then succeeds for the
    /// last two.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, "whole_merge")]
    [InlineData(1, "whole_merge")]
    [InlineData(0, "partial_arena")]
    [InlineData(1, "partial_arena")]
    [InlineData(0, "partial_pool")]
    [InlineData(1, "partial_pool")]
    public void CapacityExhausted_ArenaOrPoolRefusal_LeavesTheEntryWhereItIs(int slot, string row)
    {
        // Arrange
        var rig = row == "partial_pool"
            ? new InvAutoPlaceRig(entryPoolCapacity: 3, joinPlayerB: true)
            : new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort a;
        if (row == "partial_pool")
        {
            a = rig.PlaceItem(chest, "plank", 2, 1, 5);
            _ = rig.PlaceItem(rig.HotbarOf(slot), "fiber", 2, 0, 1);
            _ = rig.PlaceItem(rig.InventoryOf(1 - slot), "fiber", 0, 0, 1);
            Assert.Equal(rig.Entries.Capacity, rig.Entries.LiveCount);
        }
        else
        {
            a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 5));
            if (row == "whole_merge")
            {
                _ = rig.PlaceMail(inventory, "letter", 0, 0, Address3, new uint[] { 10, 11 });
            }

            MoveSplitMergeTests.ExhaustArenaThroughIntakeSpawns(rig);
        }

        Assert.True(rig.Header(inventory).FreeCells > 0);
        EntryId id = rig.Entries[a].Id;
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, row == "whole_merge" ? (byte)0 : (byte)2));

        // Assert
        AssertRejected(rig, log, InvReason.CapacityExhausted, before, slot, (rig.Header(chest).Version, rig.Header(inventory).Version));
        Assert.Equal((id, chest, (byte)5), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
        if (row == "whole_merge")
        {
            return;
        }

        // Act
        EventEntry[] whole = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a));

        // Assert
        MoveSplitMergeTests.AssertOk(whole);
        Assert.Equal((id, inventory, (byte)5), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
    }

    /// <summary>
    /// A partial quick_move that acquires two arena blocks of different size classes: the merge's union and the new entry's block
    /// (QuickMoveHandler's reservation, last; <c>capacity_exhausted</c> is code 85 of ADR-0009 section 2, and its position and "the entry
    /// stays where it is" are Mail Lead planning defaults pending Evan, TD-153). Chest A = 10 letters [1..10] to #3; inventory stack S =
    /// 18 letters [100..117] to #3 at (0,0); <c>count 3</c> → [1, 2] merge into S (union 20, class 5) and [3] becomes a new entry
    /// (1 unit, class 0). A minimum arena is exhausted through Intake spawns, then spare entries in the other player's inventory are
    /// consumed through <see cref="IInventoryApi.ConsumeUnit"/> to free exactly: <c>exact</c> one class-5 and one class-0 block → ok;
    /// <c>union_class_short</c> one class-4 block (one size class below the union's) and one class-0 block → <c>capacity_exhausted</c>;
    /// <c>new_block_missing</c> one class-5 block only (the union fits, the new entry's block does not) → <c>capacity_exhausted</c>. A
    /// refusal changes nothing.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, "exact")]
    [InlineData(1, "exact")]
    [InlineData(0, "union_class_short")]
    [InlineData(1, "union_class_short")]
    [InlineData(0, "new_block_missing")]
    [InlineData(1, "new_block_missing")]
    public void CapacityExhausted_PartialNeedsAUnionAndANewBlock_ExactArenaOk_OneClassOrOneBlockShortRefused(int slot, string row)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort otherInventory = rig.InventoryOf(1 - slot);
        ushort s = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 18));
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        ushort spare17 = rig.PlaceMail(otherInventory, "letter", 0, 0, Address5, InvAutoPlaceRig.Ids(500, 17));
        ushort spare16 = rig.PlaceMail(otherInventory, "letter", 1, 0, Address5, InvAutoPlaceRig.Ids(600, 16));
        ushort spare1 = rig.PlaceMail(otherInventory, "letter", 2, 0, Address5, new uint[] { 700 });
        _ = rig.PlaceItem(inventory, "fiber", 0, 1, 1); // a second inventory bump: the reply's two versions differ
        Assert.Equal((5, 0, 5, 4, 0), (MailIdArena.ClassFor(20), MailIdArena.ClassFor(1), MailIdArena.ClassFor(17), MailIdArena.ClassFor(16), MailIdArena.ClassFor(1)));
        MoveSplitMergeTests.ExhaustArenaThroughIntakeSpawns(rig);
        if (row != "union_class_short")
        {
            MoveSplitMergeTests.ConsumeUnits(rig, otherInventory, spare17, 17);
        }
        else
        {
            MoveSplitMergeTests.ConsumeUnits(rig, otherInventory, spare16, 16);
        }

        if (row != "new_block_missing")
        {
            MoveSplitMergeTests.ConsumeUnits(rig, otherInventory, spare1, 1);
        }

        int[] free = Enumerable.Range(0, MailIdArena.ClassCount).Select(c => rig.Inventory.MailIds.FreeBlocks(c)).ToArray();
        int[] expectedFree = new int[MailIdArena.ClassCount];
        expectedFree[row == "union_class_short" ? 4 : 5] = 1;
        expectedFree[0] = row == "new_block_missing" ? 0 : 1;
        Assert.Equal(expectedFree, free);
        EntryId aId = rig.Entries[a].Id;
        uint lastId = rig.Entries.LastId;
        Assert.NotEqual(rig.Header(chest).Version, rig.Header(inventory).Version);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, 3));

        // Assert
        if (row != "exact")
        {
            AssertRejected(rig, log, InvReason.CapacityExhausted, before, slot, (rig.Header(chest).Version, rig.Header(inventory).Version));
            Assert.Equal((aId, chest, (byte)10), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
            return;
        }

        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal(((ushort)3, (ushort)3), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal(new uint[] { 1, 2 }.Concat(InvAutoPlaceRig.Ids(100, 18)).ToArray(), rig.IdsOf(s));
        Assert.Equal(5, rig.Entries[s].MailIds.ClassIndex);
        ushort piece = rig.EntryAt(inventory, 1, 0);
        Assert.Equal((lastId + 1, 0), (rig.Entries[piece].Id.Value, rig.Entries[piece].MailIds.ClassIndex));
        Assert.Equal(new uint[] { 3 }, rig.IdsOf(piece));
        Assert.Equal((aId, chest), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot));
        Assert.Equal(InvAutoPlaceRig.Ids(4, 7), rig.IdsOf(a));
        Assert.Equal(0, rig.Inventory.MailIds.FreeBlocks(0));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
    }

    /// <summary>
    /// The entry-id pre-check of a partial quick_move onto free cells (QuickMoveHandler: <c>LastId</c> at <see cref="EntryPool.MaxRealId"/>
    /// is <c>capacity_exhausted</c>, code 85 of ADR-0009 section 2; Mail Lead planning default pending Evan, TD-153, as MoveHandler's split
    /// pre-check). Chest A = 5 planks at (2,1), a fiber in the actor's hotbar and one in the other player's inventory, the ids set through
    /// <see cref="InventorySettings.EntryInitialLastId"/>: <c>last_issued</c> (the three take the last ids, <c>LastId</c> = MaxRealId) →
    /// <c>count 2</c> is refused and nothing changes, and the whole quick_move (no new id) still works; <c>one_left</c> (<c>LastId</c> =
    /// MaxRealId − 1) → <c>count 2</c> moves 2 planks to inventory (0,0) as a new entry with id MaxRealId (now the last id issued), A
    /// keeps 3 with its id.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0, "last_issued")]
    [InlineData(1, "last_issued")]
    [InlineData(0, "one_left")]
    [InlineData(1, "one_left")]
    public void CapacityExhausted_PartialWithTheLastRealIdIssued_Refused_OneIdLeft_TheSplitTakesIt(int slot, string row)
    {
        // Arrange
        uint initial = EntryPool.MaxRealId - (row == "last_issued" ? 3u : 4u);
        var rig = new InvAutoPlaceRig(tune: st => st with { EntryInitialLastId = initial }, joinPlayerB: true);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort a = rig.PlaceItem(chest, "plank", 2, 1, 5);
        _ = rig.PlaceItem(rig.HotbarOf(slot), "fiber", 2, 0, 1);
        _ = rig.PlaceItem(rig.InventoryOf(1 - slot), "fiber", 0, 0, 1);
        Assert.Equal(row == "last_issued" ? EntryPool.MaxRealId : EntryPool.MaxRealId - 1, rig.Entries.LastId);
        EntryId id = rig.Entries[a].Id;

        if (row == "one_left")
        {
            // Act
            InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, 2))).ExtraAs<InvReplyExtra>();

            // Assert
            Assert.Equal(((ushort)2, (ushort)2), (extra.RequestedUnits, extra.GotUnits));
            ushort piece = rig.EntryAt(inventory, 0, 0);
            Assert.Equal((EntryPool.MaxRealId, (byte)2), (rig.Entries[piece].Id.Value, rig.Entries[piece].Count));
            Assert.Equal((id, chest, (byte)3), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
            Assert.Equal(EntryPool.MaxRealId, rig.Entries.LastId);
            MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
            return;
        }

        Assert.NotEqual(rig.Header(chest).Version, rig.Header(inventory).Version);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] refused = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, 2));

        // Assert
        AssertRejected(rig, refused, InvReason.CapacityExhausted, before, slot, (rig.Header(chest).Version, rig.Header(inventory).Version));
        Assert.Equal((id, chest, (byte)5), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));

        // Act
        EventEntry[] whole = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a));

        // Assert
        MoveSplitMergeTests.AssertOk(whole);
        Assert.Equal((id, inventory, (byte)5), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
    }

    // ------------------------------------------------------------------ precedence

    /// <summary>
    /// The QuickMove chain (Mail Lead planning default pending Evan, TD-153; codes from ADR-0009 section 2): <c>bad_request → not_open →
    /// stale_entry → bad_count → not_carryable → not_allowed → no_room → capacity_exhausted</c>. Each row violates its step and every
    /// later one that can co-occur, and the earliest code must win: <c>bad_request</c> (a non-zero pad, plus another player's inventory,
    /// a retired id and count 255); <c>not_open</c> (another player's inventory, a retired id, count 255); <c>stale_entry</c> (a retired
    /// id; a live hotbar Axe named in the inventory, count 2, the Intake open); <c>bad_count</c> (an inventory Axe, count 2, the Intake
    /// open: not_allowed next); <c>not_allowed</c> (the same with count 0); <c>no_room</c> (letters into a full 1 x 1 external under an
    /// exhausted arena); <c>capacity_exhausted</c> (a chest letter merge under an exhausted arena). <c>not_carryable</c> is dormant at
    /// M-1a (no v1 item is <c>carryByHand: false</c>; TD-105 item 4), so it has no row; <c>no_room</c> and <c>capacity_exhausted</c>
    /// cannot co-occur with <c>not_allowed</c> (nothing is planned). Rejects before the entry is resolved carry versions (0, 0); later
    /// ones <c>from</c>'s and the row's first target's.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R9-1")]
    [InlineData("bad_request", 0)]
    [InlineData("bad_request", 1)]
    [InlineData("not_open", 0)]
    [InlineData("not_open", 1)]
    [InlineData("stale_entry_retired", 0)]
    [InlineData("stale_entry_retired", 1)]
    [InlineData("stale_entry_not_in_from", 0)]
    [InlineData("stale_entry_not_in_from", 1)]
    [InlineData("bad_count", 0)]
    [InlineData("bad_count", 1)]
    [InlineData("not_allowed", 0)]
    [InlineData("not_allowed", 1)]
    [InlineData("no_room", 0)]
    [InlineData("no_room", 1)]
    [InlineData("capacity_exhausted", 0)]
    [InlineData("capacity_exhausted", 1)]
    public void Precedence_RequestViolatingThisStepAndEveryLaterOne_ReturnsThisStep(string row, int slot)
    {
        // Arrange
        var rig = row is "no_room" or "capacity_exhausted"
            ? new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true)
            : NewRig();
        ushort inventory = rig.InventoryOf(slot);
        uint otherInventory = rig.IdOf(rig.InventoryOf(1 - slot)).Value;
        Op op;
        InvReason reason;
        Func<(uint, uint)> versions = () => (0u, 0u);
        switch (row)
        {
            case "bad_request":
            {
                op = QuickMoveRaw(otherInventory, RetiredId(rig, slot), 255);
                QuickMovePayload p = op.PayloadAs<QuickMovePayload>();
                p.Pad1 = 1;
                op.SetPayload(in p);
                reason = (InvReason)CoreReason.BadRequest;
                break;
            }

            case "not_open":
                op = QuickMoveRaw(otherInventory, RetiredId(rig, slot), 255);
                reason = InvReason.NotOpen;
                break;
            case "stale_entry_retired":
                op = QuickMoveRaw(rig.IdOf(inventory).Value, RetiredId(rig, slot), 255);
                reason = InvReason.StaleEntry;
                break;
            case "stale_entry_not_in_from":
            {
                OpenIntake(rig, slot);
                ushort axe = rig.PlaceItem(rig.HotbarOf(slot), "axe", 1, 0, 1);
                op = QuickMoveOp(rig, inventory, axe, 2);
                reason = InvReason.StaleEntry;
                break;
            }

            case "bad_count":
            case "not_allowed":
            {
                OpenIntake(rig, slot);
                ushort axe = rig.PlaceItem(inventory, "axe", 0, 0, 1);
                op = QuickMoveOp(rig, inventory, axe, row == "bad_count" ? (byte)2 : (byte)0);
                reason = row == "bad_count" ? InvReason.BadCount : InvReason.NotAllowed;
                versions = () => (rig.Header(inventory).Version, rig.Header(rig.Intake).Version);
                break;
            }

            case "no_room":
            {
                ushort full = OpenGrid(rig, slot, 1, 1);
                _ = rig.PlaceItem(full, "fiber", 0, 0, 20);
                ushort letters = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
                MoveSplitMergeTests.ExhaustArenaThroughIntakeSpawns(rig);
                op = QuickMoveOp(rig, inventory, letters);
                reason = InvReason.NoRoom;
                versions = () => (rig.Header(inventory).Version, rig.Header(full).Version);
                break;
            }

            default:
            {
                ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
                ushort letters = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 3));
                _ = rig.PlaceMail(inventory, "letter", 0, 0, Address3, new uint[] { 10, 11 });
                MoveSplitMergeTests.ExhaustArenaThroughIntakeSpawns(rig);
                op = QuickMoveOp(rig, chest, letters);
                reason = InvReason.CapacityExhausted;
                versions = () => (rig.Header(chest).Version, rig.Header(inventory).Version);
                break;
            }
        }

        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert
        AssertRejected(rig, log, reason, before, slot, versions());
    }

    // ------------------------------------------------------------------ versions, weight, indexes

    /// <summary>
    /// <c>ResultVersionA</c> is <c>from</c> and <c>ResultVersionB</c> the container that received units (ok) or the row's first target
    /// (reject), with the chest's and the panel's versions made different first by real moves inside the chest: an Axe chest → hotbar
    /// (A = chest, B = hotbar), then a <c>bad_count</c> plank (A = chest, B = inventory, unchanged).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R9-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void Reply_ResultVersionAIsFrom_BIsTheReceiverOrTheFirstTarget_WithDifferentVersions(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort planks = rig.PlaceItem(chest, "plank", 7, 3, 5);
        ushort axe = rig.PlaceItem(chest, "axe", 0, 0, 1);
        for (int x = 1; x <= 3; x++)
        {
            MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, chest, axe, chest, x, 0)));
        }

        _ = rig.PlaceItem(inventory, "fiber", 7, 1, 1);
        uint chestV = rig.Header(chest).Version;
        uint hotbarV = rig.Header(hotbar).Version;
        uint inventoryV = rig.Header(inventory).Version;
        Assert.Equal(3, new[] { chestV, hotbarV, inventoryV }.Distinct().Count());

        // Act
        InvReplyExtra ok = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, axe))).ExtraAs<InvReplyExtra>();

        // Assert
        Assert.Equal((chestV + 1, hotbarV + 1), (ok.ResultVersionA, ok.ResultVersionB));
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] rejected = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, planks, 6));

        // Assert
        AssertRejected(rig, rejected, InvReason.BadCount, before, slot, (chestV + 1, inventoryV));
    }

    /// <summary>
    /// A chest → panel quick_move raises the owner's P by the moved entry's classPts (whole: the entry; partial: the new entry), leaves
    /// the other player's P alone, and <c>CountItem</c> and the StackKey index equal a recount. Rows: planks 10 whole and count 4, letters
    /// whole and count 4.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R16-1")]
    [InlineData(0, "plank", 0)]
    [InlineData(1, "plank", 0)]
    [InlineData(0, "plank", 4)]
    [InlineData(1, "plank", 4)]
    [InlineData(0, "letter", 0)]
    [InlineData(1, "letter", 0)]
    [InlineData(0, "letter", 4)]
    [InlineData(1, "letter", 4)]
    public void Weight_ChestToPanel_PRisesByTheMovedEntrysClassPts_CountItemAndIndexMatchARecount(int slot, string kind, byte count)
    {
        // Arrange
        var rig = NewRig();
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort a = kind == "plank" ? rig.PlaceItem(chest, "plank", 2, 1, 10) : rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        short mine = rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)slot));
        short theirs = rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)(1 - slot)));

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, count)));

        // Assert
        ushort moved = rig.EntryAt(inventory, 0, 0);
        Assert.NotEqual(Occupancy.Free, moved);
        Assert.Equal(count == 0 ? 10 : count, (int)rig.Entries[moved].Count);
        Assert.Equal(mine + MoveSplitMergeTests.ClassPts(rig, moved), rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)slot)));
        Assert.Equal(theirs, rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)(1 - slot))));
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
    }

    // ------------------------------------------------------------------ Return: H-R7-9

    /// <summary>
    /// H-R7-9, the three legs (GDD l.95, l.706), for an item (planks 5), mail (letters [1..4] to #3) and a 1 x 2 log picked rotated, each
    /// picked from chest (2,1): <c>origin_free</c> → E back at (2,1) with its id and rotation, chest +1 and cursor +1, nothing else;
    /// <c>origin_filled</c> (the other player moved fiber onto (2,1) meanwhile) → E AutoPlaced whole into the panel (the cursor row's
    /// first target, inventory (0,0)), inventory +1 and cursor +1, the chest unchanged; <c>panel_full</c> (the same, with every panel cell
    /// holding fiber) → <c>no_room</c>, E stays on the cursor with its origin, nothing changes. <c>ResultVersionA</c> is the cursor and
    /// <c>ResultVersionB</c> the receiver (ok) or the row's first target (reject), with the two made different first. No path creates a
    /// WorldItem.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "plank", "origin_free")]
    [InlineData(1, "plank", "origin_free")]
    [InlineData(0, "plank", "origin_filled")]
    [InlineData(1, "plank", "origin_filled")]
    [InlineData(0, "plank", "panel_full")]
    [InlineData(1, "plank", "panel_full")]
    [InlineData(0, "letter", "origin_free")]
    [InlineData(1, "letter", "origin_free")]
    [InlineData(0, "letter", "origin_filled")]
    [InlineData(1, "letter", "origin_filled")]
    [InlineData(0, "letter", "panel_full")]
    [InlineData(1, "letter", "panel_full")]
    [InlineData(0, "log", "origin_free")]
    [InlineData(1, "log", "origin_free")]
    [InlineData(0, "log", "origin_filled")]
    [InlineData(1, "log", "origin_filled")]
    [InlineData(0, "log", "panel_full")]
    [InlineData(1, "log", "panel_full")]
    public void HR79_Return_OriginFree_OriginFilled_PanelFull(int slot, string kind, string leg)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        bool rotated = kind == "log";
        ushort e = kind switch
        {
            "plank" => rig.PlaceItem(chest, "plank", 2, 1, 5),
            "letter" => rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 4)),
            _ => rig.PlaceItem(chest, "log", 2, 1, 1, rotated: true),
        };
        EntryId id = rig.Entries[e].Id;
        byte count = rig.Entries[e].Count;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, e)));
        if (leg != "origin_free")
        {
            FillOriginByTheOtherPlayer(rig, slot, chest, 2, 1);
        }

        if (leg == "panel_full")
        {
            FillPanelWithFiber(rig, slot);
        }

        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[cursor], versions[chest]);
        Assert.NotEqual(versions[cursor], versions[inventory]);
        int worldItems = rig.Inventory.WorldItems.LiveCount;
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        if (leg == "panel_full")
        {
            AssertRejected(rig, log, InvReason.NoRoom, before, slot, (versions[cursor], versions[inventory]));
            Assert.Equal((id, cursor, count), (rig.Entries[e].Id, rig.Entries[e].ContainerSlot, rig.Entries[e].Count));
            AssertOrigin(rig, ps, id, rig.IdOf(chest), 2, 1, rotated);
            return;
        }

        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        ushort to = leg == "origin_free" ? chest : inventory;
        (byte x, byte y) = leg == "origin_free" ? ((byte)2, (byte)1) : ((byte)0, (byte)0);
        Assert.Equal((id, to, x, y, count), (rig.Entries[e].Id, rig.Entries[e].ContainerSlot, rig.Entries[e].X, rig.Entries[e].Y, rig.Entries[e].Count));
        if (leg == "origin_free")
        {
            Assert.Equal(rotated, rig.Entries[e].Rotated);
        }

        Assert.Equal(0, (int)rig.Header(cursor).EntryCount);
        Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[to]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[cursor] + 1, versions[to] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(((ushort)count, (ushort)count), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal(worldItems, rig.Inventory.WorldItems.LiveCount);
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
    }

    // ------------------------------------------------------------------ Return fallbacks

    /// <summary>
    /// The origin leg runs only for a recorded origin whose container is in the actor's open set (Mail Lead planning default pending Evan
    /// (TD-153), item 2); otherwise the panel leg runs, never <c>not_open</c>. The origin cells are free in every row: <c>closed</c> (a
    /// real <c>close</c> of the chest), <c>other_external</c> (a second chest opened, which closes the first), <c>no_origin</c> (the
    /// planks were placed straight on the cursor, never picked). Planks 5 land whole at inventory (0,0) with their id; the chest is
    /// unchanged; cursor +1 and inventory +1.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "closed")]
    [InlineData(1, "closed")]
    [InlineData(0, "other_external")]
    [InlineData(1, "other_external")]
    [InlineData(0, "no_origin")]
    [InlineData(1, "no_origin")]
    public void Return_OriginNotInTheOpenSetOrNotRecorded_GoesToThePanel_NeverNotOpen(int slot, string row)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort planks;
        if (row == "no_origin")
        {
            planks = rig.PlaceItem(cursor, "plank", 0, 0, 5);
            Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
        }
        else
        {
            planks = rig.PlaceItem(chest, "plank", 2, 1, 5);
            MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, planks)));
            if (row == "closed")
            {
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), CloseOp(rig, chest)));
            }
            else
            {
                _ = MoveSplitMergeTests.OpenChest(rig, slot);
            }

            Assert.False(rig.Inventory.OpenSet.IsInOpenSet(ps, rig.IdOf(chest)));
            AssertOrigin(rig, ps, rig.Entries[planks].Id, rig.IdOf(chest), 2, 1, false);
        }

        EntryId id = rig.Entries[planks].Id;
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        int worldItems = rig.Inventory.WorldItems.LiveCount;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal((id, inventory, (byte)0, (byte)0, (byte)5), (rig.Entries[planks].Id, rig.Entries[planks].ContainerSlot, rig.Entries[planks].X, rig.Entries[planks].Y, rig.Entries[planks].Count));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[cursor] + 1, versions[inventory] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(worldItems, rig.Inventory.WorldItems.LiveCount);
    }

    /// <summary>
    /// An origin in the Intake is never merged into (R14 l.141, "every insertion into the Intake is NoMerge"): a letter picked from the
    /// open Intake whose cell now holds a same-StackKey letter goes to the panel (inventory (0,0)); the Intake is unchanged.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_OriginInTheIntakeHoldingASameKeyLetter_NoMerge_GoesToThePanel(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        DefIndex letter = rig.KindIndex("letter");
        Assert.Equal(1, IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address3, 0, new[] { new MailId(500) }, MergePolicy.NoMerge).Placed));
        ushort picked = rig.EntryAt(rig.Intake, 0, 0);
        OpenIntake(rig, slot);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, rig.Intake, picked)));
        ushort resident = rig.PlaceMail(rig.Intake, "letter", 0, 0, Address3, new uint[] { 600 });
        Assert.Equal(StackKey.Of(in rig.Entries[resident]), StackKey.Of(in rig.Entries[picked]));
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        MoveSplitMergeTests.AssertOk(log);
        Assert.Equal(inventory, rig.Entries[picked].ContainerSlot);
        Assert.Equal(new uint[] { 500 }, rig.IdsOf(picked));
        Assert.Equal(new uint[] { 600 }, rig.IdsOf(resident));
        Assert.DoesNotContain(MoveSplitMergeTests.RecordHeads(log), r => r.Kind == InvEventKinds.Merged);
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
    }

    /// <summary>
    /// A free Intake origin (GDD l.95, "into its origin cells if they are still free"): a letter picked from the open Intake whose cell is
    /// still free goes back there whole with its id and mail ids, inserted NoMerge (R14 l.141, "every insertion into the Intake is
    /// NoMerge") beside a same-StackKey resident that is left unchanged, and stamped with a new <c>arrivalSeq</c> above every other Intake
    /// entry's, so it leaves last (GDD l.400, "the inserted entry gets a new arrivalSeq, so it leaves last"). Only the cursor and the
    /// Intake bump; <c>ResultVersionA</c> is the cursor, <c>ResultVersionB</c> the Intake.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_ToAFreeIntakeOrigin_BackThereNoMerge_NewArrivalSeq_LeavesLast(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort cursor = rig.CursorOf(slot);
        DefIndex letter = rig.KindIndex("letter");
        Assert.Equal(2, IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address3, 0, new[] { new MailId(500), new MailId(501) }, MergePolicy.NoMerge).Placed));
        ushort picked = rig.EntryAt(rig.Intake, 0, 0);
        ushort resident = rig.EntryAt(rig.Intake, 1, 0);
        Assert.NotEqual(picked, resident);
        Assert.Equal(StackKey.Of(in rig.Entries[resident]), StackKey.Of(in rig.Entries[picked]));
        uint pickedSeq = rig.Entries[picked].ArrivalSeq;
        Assert.True(pickedSeq < rig.Entries[resident].ArrivalSeq);
        EntryId id = rig.Entries[picked].Id;
        uint[] mailIds = rig.IdsOf(picked);
        uint residentSeq = rig.Entries[resident].ArrivalSeq;
        OpenIntake(rig, slot);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, rig.Intake, picked)));
        AssertOrigin(rig, PlayerSlot.FromValue((byte)slot), id, rig.Inventory.IntakeContainer, 0, 0, false);
        Assert.Equal(Occupancy.Free, rig.EntryAt(rig.Intake, 0, 0));
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[cursor], versions[rig.Intake]);
        int worldItems = rig.Inventory.WorldItems.LiveCount;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal(picked, rig.EntryAt(rig.Intake, 0, 0));
        Assert.Equal((id, rig.Intake, (byte)1), (rig.Entries[picked].Id, rig.Entries[picked].ContainerSlot, rig.Entries[picked].Count));
        Assert.Equal(mailIds, rig.IdsOf(picked));
        Assert.Equal(((byte)1, residentSeq, 2), (rig.Entries[resident].Count, rig.Entries[resident].ArrivalSeq, (int)rig.Header(rig.Intake).EntryCount));
        Assert.DoesNotContain(MoveSplitMergeTests.RecordHeads(log), r => r.Kind == InvEventKinds.Merged);
        uint newSeq = rig.Entries[picked].ArrivalSeq;
        Assert.True(newSeq > pickedSeq);
        for (int s = 0; s < rig.Entries.Capacity; s++)
        {
            EntryRecord r = rig.Entries[(ushort)s];
            if (r.IsLive && r.ContainerSlot == rig.Intake && s != picked)
            {
                Assert.True(newSeq > r.ArrivalSeq);
            }
        }

        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[rig.Intake]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[cursor] + 1, versions[rig.Intake] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(worldItems, rig.Inventory.WorldItems.LiveCount);
    }

    /// <summary>
    /// A mergeable origin takes <c>min(count, room)</c> of the oldest units and the rest goes on to the panel leg in the same op (Mail
    /// Lead planning default pending Evan (TD-153), item 3). A = 10 letters [1..10] to #3 picked from chest (2,1), which then holds a
    /// same-key stack; the inventory holds a same-key stack of 15 [200..214] at (0,0). Rows: <c>whole_at_origin</c> (origin stack 5,
    /// room 15: all 10 merge there, A is retired, the inventory unchanged); <c>remainder_on_cursor</c> (origin stack 18 [100..117], room 2:
    /// [1, 2] merge there, [3..7] into the inventory stack, every other panel cell holds fiber, so A keeps [8, 9, 10] on the cursor with
    /// its id and its origin, requested 10 / got 7; a second <c>return</c> is <c>no_room</c>); <c>rest_to_free_cell</c> (the same with one
    /// free inventory cell: A moves there with [8, 9, 10] and its id). The receivers and the cursor bump once each.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "whole_at_origin")]
    [InlineData(1, "whole_at_origin")]
    [InlineData(0, "remainder_on_cursor")]
    [InlineData(1, "remainder_on_cursor")]
    [InlineData(0, "rest_to_free_cell")]
    [InlineData(1, "rest_to_free_cell")]
    public void Return_PartialOriginMerge_ThenThePanelLeg_ThenTheRemainderStaysOnTheCursor(int slot, string row)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort hotbar = rig.HotbarOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        EntryId aId = rig.Entries[a].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        int originCount = row == "whole_at_origin" ? 5 : 18;
        ushort origin = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(100, originCount));
        ushort stack = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(200, 15));
        if (row != "whole_at_origin")
        {
            FillPanelWithFiber(rig, slot, keepFree: row == "rest_to_free_cell" ? 1 : 0);
        }

        uint[] versions = MoveSplitMergeTests.Versions(rig);
        int worldItems = rig.Inventory.WorldItems.LiveCount;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(log).ExtraAs<InvReplyExtra>();
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[chest]++;
        Assert.Equal((versions[cursor] + 1, versions[chest] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        if (row == "whole_at_origin")
        {
            Assert.Equal(InvAutoPlaceRig.Ids(1, 10).Concat(InvAutoPlaceRig.Ids(100, 5)).ToArray(), rig.IdsOf(origin));
            Assert.False(rig.Entries.TryGetSlot(aId, out _));
            Assert.Equal(InvAutoPlaceRig.Ids(200, 15), rig.IdsOf(stack));
            Assert.Equal(((ushort)10, (ushort)10), (extra.RequestedUnits, extra.GotUnits));
            Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
            return;
        }

        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal(versions[hotbar], rig.Header(hotbar).Version);
        Assert.Equal(new uint[] { 1, 2 }.Concat(InvAutoPlaceRig.Ids(100, 18)).ToArray(), rig.IdsOf(origin));
        Assert.Equal(InvAutoPlaceRig.Ids(3, 5).Concat(InvAutoPlaceRig.Ids(200, 15)).ToArray(), rig.IdsOf(stack));
        Assert.Equal(new uint[] { 8, 9, 10 }, rig.IdsOf(a));
        Assert.Equal(aId, rig.Entries[a].Id);
        Assert.Equal(worldItems, rig.Inventory.WorldItems.LiveCount);
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
        if (row == "rest_to_free_cell")
        {
            Assert.Equal(((ushort)10, (ushort)10), (extra.RequestedUnits, extra.GotUnits));
            Assert.Equal((inventory, 0), (rig.Entries[a].ContainerSlot, (int)rig.Header(cursor).EntryCount));
            Assert.False(rig.Inventory.TryGetCursorOrigin(ps, out _));
            return;
        }

        Assert.Equal(((ushort)10, (ushort)7), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal(cursor, rig.Entries[a].ContainerSlot);
        AssertOrigin(rig, ps, aId, rig.IdOf(chest), 2, 1, false);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act: again, with the origin full and no room in the panel
        EventEntry[] again = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        AssertRejected(rig, again, InvReason.NoRoom, before, slot, (rig.Header(cursor).Version, rig.Header(inventory).Version));
    }

    /// <summary>
    /// A partial origin merge with no room in the panel is still <c>ok</c> (Mail Lead planning default pending Evan (TD-153), item 3: ok
    /// if any unit moved): A = 10 letters [1..10] picked from chest (2,1), which then holds a same-key stack of 18; every panel cell holds
    /// fiber. [1, 2] merge at the origin, A keeps [3..10], its id and its origin on the cursor; requested 10 / got 2; chest +1 and cursor
    /// +1 (A = cursor, B = chest), the panel unchanged.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_PartialOriginMerge_NoRoomInThePanel_OkWithTheOriginUnitsOnly(int slot)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        EntryId aId = rig.Entries[a].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        ushort origin = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(100, 18));
        FillPanelWithFiber(rig, slot);
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[cursor], versions[chest]);

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp())).ExtraAs<InvReplyExtra>();

        // Assert
        Assert.Equal(new uint[] { 1, 2 }.Concat(InvAutoPlaceRig.Ids(100, 18)).ToArray(), rig.IdsOf(origin));
        Assert.Equal((aId, cursor), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot));
        Assert.Equal(InvAutoPlaceRig.Ids(3, 8), rig.IdsOf(a));
        AssertOrigin(rig, ps, aId, rig.IdOf(chest), 2, 1, false);
        Assert.Equal(((ushort)10, (ushort)2), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal((versions[cursor] + 1, versions[chest] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[chest]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
    }

    /// <summary>
    /// The origin merge's 255 cap (content with <c>letter.maxStack</c> 255; room <c>min(effMax, 255) − count</c>, as Move): A = n letters
    /// [1..n] picked from chest (2,1), which then holds a same-key stack of 250 [1001..1250] (or 255). n = 4 → 254 and n = 5 → 255, A
    /// retired, the inventory unchanged; n = 6 → [1..5] merge (255) and A keeps [6] and its id, moved whole to inventory (0,0); a target
    /// already at 255 takes nothing and A (4) goes whole to inventory (0,0), the chest unchanged.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0, 4, 250)]
    [InlineData(1, 4, 250)]
    [InlineData(0, 5, 250)]
    [InlineData(1, 5, 250)]
    [InlineData(0, 6, 250)]
    [InlineData(1, 6, 250)]
    [InlineData(0, 4, 255)]
    [InlineData(1, 4, 255)]
    public void Return_OriginMergeAtThe255Cap_TakesUpToTheCap_TheRestGoesToThePanel(int slot, int n, int originCount)
    {
        // Arrange
        var rig = NewRig(MoveSplitMergeTests.MailStack255Content());
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, n));
        EntryId aId = rig.Entries[a].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        ushort origin = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1001, originCount));
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp())).ExtraAs<InvReplyExtra>();

        // Assert
        int merged = Math.Min(n, 255 - originCount);
        Assert.Equal(((ushort)n, (ushort)n), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal(InvAutoPlaceRig.Ids(1, merged).Concat(InvAutoPlaceRig.Ids(1001, originCount)).ToArray(), rig.IdsOf(origin));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[chest] += merged > 0 ? 1u : 0u;
        if (merged == n)
        {
            Assert.False(rig.Entries.TryGetSlot(aId, out _));
        }
        else
        {
            Assert.Equal((aId, inventory, (byte)0, (byte)0), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y));
            Assert.Equal(InvAutoPlaceRig.Ids((uint)(merged + 1), n - merged), rig.IdsOf(a));
            expected[inventory]++;
        }

        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
    }

    /// <summary>
    /// The origin room is capped at the u8 count, not the item's <c>maxStack</c> (as Move): a <c>bigstack</c> item (<c>maxStack</c> 300,
    /// <see cref="MoveSplitMergeTests.MergeContent"/>) of 10 picked from chest (2,1), which then holds a bigstack of 250: the origin takes
    /// 5 (255) and the other 5 go, with the entry's id, to inventory (0,0).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_ItemOriginMergeWithMaxStackAbove255_TheRoomIsCappedAt255(int slot)
    {
        // Arrange
        var rig = NewRig(MoveSplitMergeTests.MergeContent());
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort a = rig.PlaceItem(chest, "bigstack", 2, 1, 10);
        EntryId aId = rig.Entries[a].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        ushort origin = rig.PlaceItem(chest, "bigstack", 2, 1, 250);
        Assert.True(rig.SubjectOf(origin).EffMax > byte.MaxValue);

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp())).ExtraAs<InvReplyExtra>();

        // Assert
        Assert.Equal((byte)255, rig.Entries[origin].Count);
        Assert.Equal((aId, inventory, (byte)0, (byte)0, (byte)5), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y, rig.Entries[a].Count));
        Assert.Equal(((ushort)10, (ushort)10), (extra.RequestedUnits, extra.GotUnits));
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
    }

    /// <summary>
    /// A partial merge at an origin inside the panel (the inventory, which the cursor row also targets) is not planned twice: A = 10
    /// letters [1..10] picked from inventory (0,0), which then holds a same-key stack of 18 [100..117] (room 2). [1, 2] merge at the
    /// origin (20); the panel leg skips that stack and moves A, with [3..10] and its id, to the first free cell, inventory (1,0).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_PartialMergeAtAnOriginInThePanel_TheOriginStackIsNotPlannedTwice(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 10));
        EntryId aId = rig.Entries[a].Id;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, inventory, a)));
        ushort origin = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(100, 18));
        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp())).ExtraAs<InvReplyExtra>();

        // Assert
        Assert.Equal(new uint[] { 1, 2 }.Concat(InvAutoPlaceRig.Ids(100, 18)).ToArray(), rig.IdsOf(origin));
        Assert.Equal((aId, inventory, (byte)1, (byte)0), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y));
        Assert.Equal(InvAutoPlaceRig.Ids(3, 8), rig.IdsOf(a));
        Assert.Equal(((ushort)10, (ushort)10), (extra.RequestedUnits, extra.GotUnits));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
    }

    /// <summary>
    /// An origin recorded by a hotbar swap (story 008's swap commit): letters on the cursor are placed onto hotbar slot 2's Axe, which
    /// comes onto the cursor with origin hotbar slot 2. <c>slot_free</c> (the letters moved on to the inventory) → the Axe goes back to
    /// slot 2; <c>slot_taken</c> → the panel leg puts it in the first free usable hotbar slot, 1 (a tool: hotbar first, R9). Hotbar +1 and
    /// cursor +1.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "slot_free")]
    [InlineData(1, "slot_free")]
    [InlineData(0, "slot_taken")]
    [InlineData(1, "slot_taken")]
    public void Return_OriginFromAHotbarSwap_BackToTheSlotOrThePanel(int slot, string row)
    {
        // Arrange
        var rig = NewRig();
        ushort hotbar = rig.HotbarOf(slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort axe = rig.PlaceItem(hotbar, "axe", 2, 0, 1);
        ushort letters = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, cursor, letters, hotbar, 2, 0)));
        Assert.Equal(cursor, rig.Entries[axe].ContainerSlot);
        if (row == "slot_free")
        {
            MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, hotbar, letters, inventory, 0, 0)));
        }

        uint[] versions = MoveSplitMergeTests.Versions(rig);

        // Act
        InvReplyExtra extra = MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp())).ExtraAs<InvReplyExtra>();

        // Assert
        byte x = row == "slot_free" ? (byte)2 : (byte)1;
        Assert.Equal((hotbar, x, (byte)0), (rig.Entries[axe].ContainerSlot, rig.Entries[axe].X, rig.Entries[axe].Y));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[hotbar]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[cursor] + 1, versions[hotbar] + 1), (extra.ResultVersionA, extra.ResultVersionB));
    }

    // ------------------------------------------------------------------ Return: capacity_exhausted, cursor_empty, precedence

    /// <summary>
    /// <c>capacity_exhausted</c> (code 85, ADR-0009 section 2), checked last, leaves the entry on the cursor with its origin (Mail Lead
    /// planning default pending Evan (TD-153); it follows the pattern of Evan's 2026-10-22 5:11 AM PT ruling, which covers only a leashed
    /// letter staying on the cursor when the mail-id arena is full, even with a free cell). Rows, each with free inventory cells under an
    /// exhausted minimum arena: <c>origin_merge</c> (letters picked from chest (2,1), which then holds a same-key stack, so the origin
    /// merge needs a union block); <c>panel_merge</c> (letters placed straight on the cursor, no origin, with a same-key stack in the
    /// inventory).
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "origin_merge")]
    [InlineData(1, "origin_merge")]
    [InlineData(0, "panel_merge")]
    [InlineData(1, "panel_merge")]
    public void Return_ArenaRefusal_CapacityExhausted_TheEntryStaysOnTheCursor(int slot, string row)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a;
        if (row == "origin_merge")
        {
            a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 3));
            MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
            _ = rig.PlaceMail(chest, "letter", 2, 1, Address3, new uint[] { 10, 11 });
        }
        else
        {
            a = rig.PlaceMail(cursor, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 3));
            _ = rig.PlaceMail(inventory, "letter", 0, 0, Address3, new uint[] { 10, 11 });
        }

        MoveSplitMergeTests.ExhaustArenaThroughIntakeSpawns(rig);
        Assert.True(rig.Header(inventory).FreeCells > 0);
        EntryId id = rig.Entries[a].Id;
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        AssertRejected(rig, log, InvReason.CapacityExhausted, before, slot, (rig.Header(cursor).Version, rig.Header(inventory).Version));
        Assert.Equal((id, cursor, (byte)3), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
        Assert.Equal(row == "origin_merge", rig.Inventory.TryGetCursorOrigin(ps, out _));
    }

    /// <summary>
    /// The InvMalformedOpTests stub rig's valid row, moved here now that the real handler is bound: the rig's cursors are empty, so a
    /// well-formed <c>return</c> is <c>cursor_empty</c>, from either slot (the stub answered ok).
    /// </summary>
    [Theory]
    [Trait("Id", "H-NET-5-sim")]
    [InlineData(0)]
    [InlineData(1)]
    public void Return_OnTheStubRigsEmptyCursor_IsCursorEmpty(int slot)
    {
        // Arrange
        var rig = new InvOpRig();
        Assert.True(rig.Inventory.TryGetHeader(rig.Inventory.CursorOf(PlayerSlot.FromValue((byte)slot)), out ContainerHeader cursor));
        Assert.Equal(0, (int)cursor.EntryCount);

        // Act + Assert
        rig.AssertSingleReply(ReturnOp(), slot == 0 ? rig.PlayerA : rig.PlayerB, (ushort)InvReason.CursorEmpty);
    }

    /// <summary>
    /// <c>cursor_empty</c> for an actor with no panel (so no cursor): a 6-container table holds the Intake and player A's panel, so player
    /// B's join finds no room for one. Slot 1 only: the rig always joins A (slot 0) first. Nothing changes; versions (0, 0).
    /// </summary>
    [Fact]
    [Trait("Id", "H-R7-9")]
    public void Return_AnActorWithNoPanel_CursorEmpty_NothingChanges()
    {
        // Arrange
        var rig = new InvAutoPlaceRig(tune: s => s with { ContainerCapacity = 6 }, joinPlayerB: true);
        Assert.True(rig.Inventory.CursorOf(PlayerSlot.FromValue(1)).IsNone);
        _ = rig.PlaceItem(rig.InventoryOf(0), "plank", 0, 0, 5);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(1), ReturnOp());

        // Assert
        AssertRejected(rig, log, InvReason.CursorEmpty, before, 1, (0u, 0u));
    }

    /// <summary>
    /// The Return chain, <c>bad_request → cursor_empty → [origin leg] → [panel leg] → no_room → capacity_exhausted</c> (codes from
    /// ADR-0009 section 2; the head from GDD l.103 and ADR-0009 l.113, the legs and <c>no_room</c> from GDD l.95, the rest Mail Lead
    /// defaults, TD-153). Each row violates its step and every later one that can co-occur, under an exhausted minimum arena, and the
    /// earliest code must win: <c>wrong_length</c> (a 4-byte payload on an empty cursor: Sim Core's descriptor-length check answers
    /// <c>unknown_kind</c>, 3, before any handler runs, so the handler's <c>bad_request</c> head — <see cref="InvRequestCheck.CheckRequest"/>,
    /// pinned by <c>InvMalformedOpTests.CheckRequest_ReturnWithANonZeroPayloadLength_IsBadRequest</c> — is never reached through the
    /// drain); <c>cursor_empty</c> (an empty cursor);
    /// <c>no_room</c> (letters whose origin holds fiber, every panel cell fiber); <c>capacity_exhausted</c> (letters whose origin holds a
    /// same-key stack). <c>no_room</c> and <c>capacity_exhausted</c> cannot co-occur (nothing moved means no block is planned); the origin
    /// leg's own verdicts never reject. Rejects before the cursor's entry is resolved carry (0, 0); later ones the cursor's and the cursor
    /// row's first target's versions.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData("wrong_length", 0)]
    [InlineData("wrong_length", 1)]
    [InlineData("cursor_empty", 0)]
    [InlineData("cursor_empty", 1)]
    [InlineData("no_room", 0)]
    [InlineData("no_room", 1)]
    [InlineData("capacity_exhausted", 0)]
    [InlineData("capacity_exhausted", 1)]
    public void Return_Precedence_RequestViolatingThisStepAndEveryLaterOne_ReturnsThisStep(string row, int slot)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        Op op = ReturnOp();
        InvReason reason;
        (uint, uint) versions = (0u, 0u);
        switch (row)
        {
            case "wrong_length":
                op.SetPayload(new DropPayload { EntryId = 1 });
                Assert.Equal(4, op.PayloadLength);
                reason = (InvReason)CoreReason.UnknownKind;
                break;
            case "cursor_empty":
                reason = InvReason.CursorEmpty;
                break;
            default:
            {
                ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 3));
                MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
                if (row == "no_room")
                {
                    _ = rig.PlaceItem(chest, "fiber", 2, 1, 3);
                    FillPanelWithFiber(rig, slot);
                    reason = InvReason.NoRoom;
                }
                else
                {
                    _ = rig.PlaceMail(chest, "letter", 2, 1, Address3, new uint[] { 10, 11 });
                    reason = InvReason.CapacityExhausted;
                }

                break;
            }
        }

        MoveSplitMergeTests.ExhaustArenaThroughIntakeSpawns(rig);
        if (row is "no_room" or "capacity_exhausted")
        {
            versions = (rig.Header(cursor).Version, rig.Header(inventory).Version);
        }

        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), op);

        // Assert
        AssertRejected(rig, log, reason, before, slot, versions);
    }

    // ------------------------------------------------------------------ Return: weight, indexes

    /// <summary>
    /// Weight and indexes after a <c>return</c>: planks 10 or letters [1..10] picked from chest (2,1); to the origin, the owner's P is
    /// back to its value before the pick; to the panel (the chest closed), P is that value plus the entry's classPts; the other player's
    /// P never changes; <c>CountItem</c> and the StackKey index equal a recount.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R16-1")]
    [InlineData(0, "plank", "origin")]
    [InlineData(1, "plank", "origin")]
    [InlineData(0, "plank", "panel")]
    [InlineData(1, "plank", "panel")]
    [InlineData(0, "letter", "origin")]
    [InlineData(1, "letter", "origin")]
    [InlineData(0, "letter", "panel")]
    [InlineData(1, "letter", "panel")]
    public void Return_Weight_POfTheOriginOrThePanel_CountItemAndIndexMatchARecount(int slot, string kind, string leg)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        PlayerSlot other = PlayerSlot.FromValue((byte)(1 - slot));
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort a = kind == "plank" ? rig.PlaceItem(chest, "plank", 2, 1, 10) : rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        short beforePick = rig.Inventory.WeightPoints(ps);
        short theirs = rig.Inventory.WeightPoints(other);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        if (leg == "panel")
        {
            MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), CloseOp(rig, chest)));
        }

        // Act
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), ReturnOp()));

        // Assert
        Assert.Equal(leg == "origin" ? chest : rig.InventoryOf(slot), rig.Entries[a].ContainerSlot);
        Assert.Equal(beforePick + (leg == "origin" ? 0 : MoveSplitMergeTests.ClassPts(rig, a)), rig.Inventory.WeightPoints(ps));
        Assert.Equal(theirs, rig.Inventory.WeightPoints(other));
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
    }

    // ------------------------------------------------------------------ helpers

    private static InvAutoPlaceRig NewRig(ContentSet? content = null) => new(content: content, joinPlayerB: true);

    /// <summary>A fresh <paramref name="w"/> x <paramref name="h"/> ownerless grid, opened as <paramref name="slot"/>'s external.</summary>
    private static ushort OpenGrid(InvAutoPlaceRig rig, int slot, int w, int h)
    {
        ushort grid = rig.CreateGrid(w, h);
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)slot), rig.IdOf(grid)));
        return grid;
    }

    private static void OpenIntake(InvAutoPlaceRig rig, int slot) =>
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)slot), rig.Inventory.IntakeContainer));

    /// <summary>An id retired by a real whole <c>move</c> that merges 2 letters onto a same-key stack in the actor's inventory.</summary>
    private static uint RetiredId(InvAutoPlaceRig rig, int slot)
    {
        ushort inventory = rig.InventoryOf(slot);
        _ = rig.PlaceMail(inventory, "letter", 6, 1, Address5, InvAutoPlaceRig.Ids(900, 3));
        ushort gone = rig.PlaceMail(inventory, "letter", 7, 1, Address5, InvAutoPlaceRig.Ids(910, 2));
        uint id = rig.Entries[gone].Id.Value;
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, inventory, gone, inventory, 6, 1)));
        Assert.False(rig.Entries.TryGetSlot(EntryId.Real(id), out _));
        return id;
    }

    /// <summary>The live entry count of every container, by table slot.</summary>
    private static int[] EntryCounts(InvAutoPlaceRig rig)
    {
        ContainerTable containers = rig.Inventory.Containers;
        var counts = new int[containers.LiveCount];
        for (int s = 0; s < counts.Length; s++)
        {
            counts[s] = containers[(ushort)s].EntryCount;
        }

        return counts;
    }

    internal static Op QuickMoveOp(InvAutoPlaceRig rig, ushort from, ushort entry, byte count = 0) =>
        QuickMoveRaw(rig.IdOf(from).Value, rig.Entries[entry].Id.Value, count);

    internal static Op QuickMoveRaw(uint from, uint entry, byte count)
    {
        var op = new Op { Kind = InvOpKinds.QuickMove, ReqId = 85 };
        op.SetPayload(new QuickMovePayload { From = from, EntryId = entry, Count = count });
        return op;
    }

    private static Op ReturnOp() => new() { Kind = InvOpKinds.Return, ReqId = 86 };

    private static Op CloseOp(InvAutoPlaceRig rig, ushort container)
    {
        var op = new Op { Kind = InvOpKinds.Close, ReqId = 87 };
        op.SetPayload(new ClosePayload { Container = rig.IdOf(container).Value });
        return op;
    }

    /// <summary>The other player opens <paramref name="chest"/> and moves 3 fiber from their inventory onto (x, y) with a real <c>move</c>.</summary>
    private static void FillOriginByTheOtherPlayer(InvAutoPlaceRig rig, int slot, ushort chest, int x, int y)
    {
        int other = 1 - slot;
        ushort otherInventory = rig.InventoryOf(other);
        Assert.True(rig.Inventory.TryOpenExternal(PlayerSlot.FromValue((byte)other), rig.IdOf(chest)));
        ushort fiber = rig.PlaceItem(otherInventory, "fiber", 0, 0, 3);
        MoveSplitMergeTests.AssertOk(rig.Run(rig.Player(other), MoveOp(rig, otherInventory, fiber, chest, x, y)));
        Assert.Equal(fiber, rig.EntryAt(chest, x, y));
    }

    /// <summary>
    /// Places one fiber (1 x 1; no returned entry here is fiber, so nothing merges into it) on every free usable hotbar slot and then every
    /// free inventory cell, in that order, leaving the last <paramref name="keepFree"/> free inventory cells empty.
    /// </summary>
    private static void FillPanelWithFiber(InvAutoPlaceRig rig, int slot, int keepFree = 0)
    {
        var free = new List<(ushort Container, int X, int Y)>();
        foreach (ushort c in new[] { rig.HotbarOf(slot), rig.InventoryOf(slot) })
        {
            ContainerHeader h = rig.Header(c);
            bool slots = h.Layout == ContainerLayout.Slots;
            for (int y = 0; y < h.H; y++)
            {
                for (int x = slots && y == 0 ? 1 : 0; x < h.W; x++)
                {
                    if (rig.Inventory.Occupancy.OwnerAt(c, h.W, x, y) == Occupancy.Free)
                    {
                        free.Add((c, x, y));
                    }
                }
            }
        }

        Assert.True(free.Count > keepFree);
        for (int i = 0; i < free.Count - keepFree; i++)
        {
            _ = rig.PlaceItem(free[i].Container, "fiber", free[i].X, free[i].Y, 1);
        }
    }

    /// <summary>The actor's cursor origin is recorded for <paramref name="entry"/> at the given container, cells and rotation.</summary>
    private static void AssertOrigin(InvAutoPlaceRig rig, PlayerSlot ps, EntryId entry, ContainerId container, byte x, byte y, bool rotated)
    {
        Assert.True(rig.Inventory.TryGetCursorOrigin(ps, out InventorySystem.CursorOrigin o));
        Assert.Equal((entry, container, x, y, rotated), (o.Entry, o.Container, o.X, o.Y, o.Rotated));
    }

    private static Op PickOp(InvAutoPlaceRig rig, ushort from, ushort entry)
    {
        var op = new Op { Kind = InvOpKinds.Pick, ReqId = 80 };
        op.SetPayload(new PickPayload { From = rig.IdOf(from).Value, EntryId = rig.Entries[entry].Id.Value });
        return op;
    }

    private static Op MoveOp(InvAutoPlaceRig rig, ushort from, ushort entry, ushort to, int x, int y, byte count = 0)
    {
        var op = new Op { Kind = InvOpKinds.Move, ReqId = 81 };
        op.SetPayload(new MovePayload
        {
            From = rig.IdOf(from).Value, EntryId = rig.Entries[entry].Id.Value, To = rig.IdOf(to).Value, BaseVersion = rig.Header(to).Version,
            X = (short)x, Y = (short)y, Count = count,
        });
        return op;
    }

    /// <summary>
    /// The reject contract (TD-105 item 5; ADR-0009 section 3): the reason; the reply's <c>(ResultVersionA, ResultVersionB)</c>; the
    /// <c>OpReply</c> is the only entry in the tick log (no record, no seal), sent to the actor's slot alone; every recorded side effect
    /// is as before.
    /// </summary>
    private static void AssertRejected(InvAutoPlaceRig rig, EventEntry[] log, InvReason reason, List<long> before, int actorSlot, (uint A, uint B) versions)
    {
        EventEntry entry = Assert.Single(log);
        Assert.Equal(EventKind.OpReply, entry.Kind);
        Assert.Equal(1 << actorSlot, entry.Audience);
        Reply reply = entry.PayloadAs<Reply>();
        Assert.Equal(ReplyResult.Rejected, reply.Result);
        Assert.Equal((ushort)reason, reply.Reason);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal(versions, (extra.ResultVersionA, extra.ResultVersionB));
        Assert.Equal(before, MoveSplitMergeTests.SideEffects(rig));
    }
}
