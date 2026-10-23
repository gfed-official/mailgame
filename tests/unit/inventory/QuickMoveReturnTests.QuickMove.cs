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
}
