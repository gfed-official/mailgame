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
/// Mail Lead planning defaults pending Evan (TD-153); the codes are ADR-0009's (section 2 reason-code list). Slice 2 adds <c>Return</c>.
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
