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
    // ------------------------------------------------------------------ Mail Review r1 advisories: boundaries

    /// <summary>
    /// Return's origin-merge union block is planned at the union's size, <c>target.count + units</c> (ReturnHandler's origin leg; the
    /// reservation and its last position are Mail Lead planning defaults pending Evan, TD-153; code 85 is ADR-0009 section 2's). A = 3
    /// letters [1..3] picked from chest (2,1), which then holds a same-key stack O of 14 [100..113] (class 4); the union is 17 (class 5,
    /// one class above O's block). A minimum arena is exhausted through Intake spawns, then exactly one spare block is freed through
    /// <see cref="IInventoryApi.ConsumeUnit"/>: <c>exact</c> a class-5 block → ok, O = [1..3, 100..113] in a class-5 block, A retired,
    /// cursor +1 and chest +1; <c>one_class_short</c> a class-4 block (enough for O's own class, not the union's) →
    /// <c>capacity_exhausted</c>, nothing changes, A stays on the cursor with its origin.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-9")]
    [InlineData(0, "exact")]
    [InlineData(1, "exact")]
    [InlineData(0, "one_class_short")]
    [InlineData(1, "one_class_short")]
    public void Return_OriginMergeUnionNeedsALargerClass_ThatClassFreeOk_OneClassShortCapacityExhausted(int slot, string row)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(mailIdArenaUnits: MailIdArena.MinTotalUnits, joinPlayerB: true);
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort otherInventory = rig.InventoryOf(1 - slot);
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 3));
        EntryId aId = rig.Entries[a].Id;
        InvOpTestHelpers.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        ushort o = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(100, 14));
        ushort spare17 = rig.PlaceMail(otherInventory, "letter", 0, 0, Address5, InvAutoPlaceRig.Ids(500, 17));
        ushort spare16 = rig.PlaceMail(otherInventory, "letter", 1, 0, Address5, InvAutoPlaceRig.Ids(600, 16));
        Assert.Equal((4, 5, 5, 4), (rig.Entries[o].MailIds.ClassIndex, MailIdArena.ClassFor(17), MailIdArena.ClassFor(17), MailIdArena.ClassFor(16)));
        InvOpTestHelpers.ExhaustArenaThroughIntakeSpawns(rig);
        MoveSplitMergeTests.ConsumeUnits(rig, otherInventory, row == "exact" ? spare17 : spare16, row == "exact" ? 17 : 16);
        int[] expectedFree = new int[MailIdArena.ClassCount];
        expectedFree[row == "exact" ? 5 : 4] = 1;
        Assert.Equal(expectedFree, Enumerable.Range(0, MailIdArena.ClassCount).Select(c => rig.Inventory.MailIds.FreeBlocks(c)).ToArray());
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[cursor], versions[inventory]);
        Assert.NotEqual(versions[cursor], versions[chest]);
        List<long> before = MoveSplitMergeTests.SideEffects(rig);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), ReturnOp());

        // Assert
        if (row == "one_class_short")
        {
            AssertRejected(rig, log, InvReason.CapacityExhausted, before, slot, (versions[cursor], versions[inventory]));
            Assert.Equal((aId, cursor, (byte)3), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
            AssertOrigin(rig, ps, aId, rig.IdOf(chest), 2, 1, false);
            return;
        }

        InvReplyExtra extra = InvOpTestHelpers.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal(InvAutoPlaceRig.Ids(1, 3).Concat(InvAutoPlaceRig.Ids(100, 14)).ToArray(), rig.IdsOf(o));
        Assert.Equal(5, rig.Entries[o].MailIds.ClassIndex);
        Assert.False(rig.Entries.TryGetSlot(aId, out _));
        Assert.Equal(((ushort)3, (ushort)3), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal((versions[cursor] + 1, versions[chest] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[chest]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
    }

    /// <summary>
    /// The entry-pool boundary of a partial quick_move onto a free cell (QuickMoveHandler's pool pre-check; <c>capacity_exhausted</c> is
    /// ADR-0009 section 2's code 85, its position and "the entry stays where it is" Mail Lead planning defaults pending Evan, TD-153).
    /// Chest A = 5 planks at (2,1), a fiber in the actor's hotbar and one in the other player's inventory: three live entries. With pool
    /// capacity 4 (one slot left), <c>count 2</c> splits 2 planks to inventory (0,0) as a new entry and fills the pool. No slot left
    /// (capacity 3) is <see cref="CapacityExhausted_ArenaOrPoolRefusal_LeavesTheEntryWhereItIs"/>'s <c>partial_pool</c> row.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0)]
    [InlineData(1)]
    public void CapacityExhausted_PartialOntoAFreeCell_OnePoolSlotLeft_SplitsAndFillsThePool(int slot)
    {
        // Arrange
        var rig = new InvAutoPlaceRig(entryPoolCapacity: 4, joinPlayerB: true);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort a = rig.PlaceItem(chest, "plank", 2, 1, 5);
        _ = rig.PlaceItem(rig.HotbarOf(slot), "fiber", 2, 0, 1);
        _ = rig.PlaceItem(rig.InventoryOf(1 - slot), "fiber", 0, 0, 1);
        Assert.Equal(1, rig.Entries.Capacity - rig.Entries.LiveCount);
        EntryId id = rig.Entries[a].Id;
        uint lastId = rig.Entries.LastId;

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, 2));

        // Assert
        InvReplyExtra extra = InvOpTestHelpers.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal(((ushort)2, (ushort)2), (extra.RequestedUnits, extra.GotUnits));
        ushort piece = rig.EntryAt(inventory, 0, 0);
        Assert.Equal((lastId + 1, (byte)2), (rig.Entries[piece].Id.Value, rig.Entries[piece].Count));
        Assert.Equal((id, chest, (byte)3), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].Count));
        Assert.Equal(rig.Entries.Capacity, rig.Entries.LiveCount);
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
    }

    /// <summary>
    /// A partial quick_move from the cursor (the cursor row, GDD R9 l.128): letters A = [1..10] to #3 picked from chest (2,1);
    /// <c>count 3</c> splits [1, 2, 3] off as a new entry at inventory (0,0) (the mail cursor row's first target, no same-key stack);
    /// A keeps [4..10], its id and its origin on the cursor; requested 3 / got 3; cursor +1 and inventory +1 (A = cursor, B = inventory),
    /// every other container unchanged.
    /// </summary>
    [Theory]
    [Trait("Id", "TR-inventory-005")]
    [InlineData(0)]
    [InlineData(1)]
    public void Partial_FromTheCursor_SplitsTheOldestIntoThePanel_TheRestKeepsItsIdAndOrigin(int slot)
    {
        // Arrange
        var rig = NewRig();
        PlayerSlot ps = PlayerSlot.FromValue((byte)slot);
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        ushort cursor = rig.CursorOf(slot);
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 10));
        EntryId aId = rig.Entries[a].Id;
        InvOpTestHelpers.AssertOk(rig.Run(rig.Player(slot), PickOp(rig, chest, a)));
        uint lastId = rig.Entries.LastId;
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[cursor], versions[inventory]);

        // Act
        InvReplyExtra extra = InvOpTestHelpers.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, cursor, a, 3))).ExtraAs<InvReplyExtra>();

        // Assert
        Assert.Equal(((ushort)3, (ushort)3), (extra.RequestedUnits, extra.GotUnits));
        ushort piece = rig.EntryAt(inventory, 0, 0);
        Assert.Equal(lastId + 1, rig.Entries[piece].Id.Value);
        Assert.Equal(new uint[] { 1, 2, 3 }, rig.IdsOf(piece));
        Assert.Equal((aId, cursor), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot));
        Assert.Equal(InvAutoPlaceRig.Ids(4, 7), rig.IdsOf(a));
        AssertOrigin(rig, ps, aId, rig.IdOf(chest), 2, 1, false);
        uint[] expected = (uint[])versions.Clone();
        expected[cursor]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[cursor] + 1, versions[inventory] + 1), (extra.ResultVersionA, extra.ResultVersionB));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
    }

    /// <summary>
    /// A partial quick_move into the Intake (H-R9-1's Intake row; GDD R14 l.141, every Intake insertion is NoMerge): the inventory's
    /// letters A = [1..5] to #3 with the Intake open and a same-key Intake resident; <c>count 2</c> → [1, 2] become a new Intake entry
    /// (never merged into the resident) stamped with a new <c>arrivalSeq</c> above the resident's; A keeps [3, 4, 5] and its id;
    /// inventory +1 and Intake +1, no <c>InvMerged</c>.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R9-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void Partial_IntoTheIntake_NoMerge_ANewEntryWithANewArrivalSeq(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort inventory = rig.InventoryOf(slot);
        DefIndex letter = rig.KindIndex("letter");
        Assert.Equal(1, IntakeSpawnOutfeedTests.InPhase(
            rig, () => rig.Inventory.Spawn(rig.Inventory.IntakeContainer, letter, Address3, 0, new[] { new MailId(500) }, MergePolicy.NoMerge).Placed));
        ushort resident = rig.EntryAt(rig.Intake, 0, 0);
        OpenIntake(rig, slot);
        ushort a = rig.PlaceMail(inventory, "letter", 0, 0, Address3, InvAutoPlaceRig.Ids(1, 5));
        _ = rig.PlaceItem(inventory, "fiber", 7, 1, 1); // a second inventory bump: the two versions differ
        Assert.Equal(StackKey.Of(in rig.Entries[resident]), StackKey.Of(in rig.Entries[a]));
        EntryId aId = rig.Entries[a].Id;
        uint lastId = rig.Entries.LastId;
        uint residentSeq = rig.Entries[resident].ArrivalSeq;
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[inventory], versions[rig.Intake]);

        // Act
        EventEntry[] log = rig.Run(rig.Player(slot), QuickMoveOp(rig, inventory, a, 2));

        // Assert
        InvReplyExtra extra = InvOpTestHelpers.AssertOk(log).ExtraAs<InvReplyExtra>();
        Assert.Equal(((ushort)2, (ushort)2), (extra.RequestedUnits, extra.GotUnits));
        Assert.Equal(2, (int)rig.Header(rig.Intake).EntryCount);
        Assert.True(rig.Entries.TryGetSlot(EntryId.Real(lastId + 1), out ushort piece));
        Assert.Equal((rig.Intake, (byte)2), (rig.Entries[piece].ContainerSlot, rig.Entries[piece].Count));
        Assert.Equal(new uint[] { 1, 2 }, rig.IdsOf(piece));
        Assert.True(rig.Entries[piece].ArrivalSeq > residentSeq);
        Assert.Equal(new uint[] { 500 }, rig.IdsOf(resident));
        Assert.Equal((aId, inventory), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot));
        Assert.Equal(new uint[] { 3, 4, 5 }, rig.IdsOf(a));
        Assert.DoesNotContain(InvOpTestHelpers.RecordHeads(log), e => e.Kind == InvEventKinds.Merged);
        uint[] expected = (uint[])versions.Clone();
        expected[inventory]++;
        expected[rig.Intake]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        Assert.Equal((versions[inventory] + 1, versions[rig.Intake] + 1), (extra.ResultVersionA, extra.ResultVersionB));
    }

    /// <summary>
    /// A whole quick_move merging at the 255 cap (content with <c>letter.maxStack</c> 255; GDD R2 count ≤ 255): inventory stack S = 250
    /// letters [1001..1250] to #3 at (0,0), chest A = n letters [1..n]. <c>n</c> 4 → all merge (254), A retired; 5 → all merge (255), A
    /// retired; 6 → the 5 oldest merge (255) and A keeps [6] and its id at inventory (1,0); <c>at255</c> (S already 255, A = 3) → S is
    /// skipped and A lands whole at inventory (1,0) with its id. Requested n / got n; chest +1 and inventory +1.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R7-3")]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(0, 6)]
    [InlineData(1, 6)]
    [InlineData(0, 255)]
    [InlineData(1, 255)]
    public void Whole_MailMergeAtThe255Cap_MergesUpToTheCap_TheRestKeepsTheEntryId(int slot, int n)
    {
        // Arrange
        var rig = NewRig(MoveSplitMergeTests.MailStack255Content());
        ushort chest = MoveSplitMergeTests.OpenChest(rig, slot);
        ushort inventory = rig.InventoryOf(slot);
        bool at255 = n == 255;
        int units = at255 ? 3 : n;
        uint[] sIds = InvAutoPlaceRig.Ids(1001, at255 ? 255 : 250);
        ushort s = rig.PlaceMail(inventory, "letter", 0, 0, Address3, sIds);
        ushort a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, units));
        _ = rig.PlaceItem(inventory, "fiber", 7, 1, 1); // a second inventory bump: the two versions differ
        EntryId aId = rig.Entries[a].Id;
        uint lastId = rig.Entries.LastId;
        uint[] versions = MoveSplitMergeTests.Versions(rig);
        Assert.NotEqual(versions[chest], versions[inventory]);

        // Act
        InvReplyExtra extra = InvOpTestHelpers.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a))).ExtraAs<InvReplyExtra>();

        // Assert
        Assert.Equal(((ushort)units, (ushort)units), (extra.RequestedUnits, extra.GotUnits));
        int merged = at255 ? 0 : Math.Min(units, 5);
        Assert.Equal(InvAutoPlaceRig.Ids(1, merged).Concat(sIds).ToArray(), rig.IdsOf(s));
        Assert.Equal(lastId, rig.Entries.LastId);
        if (units - merged == 0)
        {
            Assert.False(rig.Entries.TryGetSlot(aId, out _));
            Assert.Equal(2, (int)rig.Header(inventory).EntryCount);
        }
        else
        {
            Assert.Equal((aId, inventory, (byte)1, (byte)0), (rig.Entries[a].Id, rig.Entries[a].ContainerSlot, rig.Entries[a].X, rig.Entries[a].Y));
            Assert.Equal(InvAutoPlaceRig.Ids((uint)(merged + 1), units - merged), rig.IdsOf(a));
        }

        Assert.Equal(0, (int)rig.Header(chest).EntryCount);
        uint[] expected = (uint[])versions.Clone();
        expected[chest]++;
        expected[inventory]++;
        Assert.Equal(expected, MoveSplitMergeTests.Versions(rig));
        MoveSplitMergeTests.AssertEveryIdListAscendingNoDuplicate(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
    }
}
