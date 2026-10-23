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
    // ------------------------------------------------------------------ not_open, stale_entry, capacity_exhausted

    /// <summary>
    /// <c>not_open</c>: <c>from</c> is a chest the actor has not opened (versions not yet resolved: 0, 0). The other player's inventory is
    /// <see cref="Precedence_RequestViolatingThisStepAndEveryLaterOne_ReturnsThisStep"/>'s <c>not_open</c> row.
    /// </summary>
    [Theory]
    [Trait("Id", "H-R10-2")]
    [InlineData(0)]
    [InlineData(1)]
    public void NotOpen_FromOutsideTheActorsOpenSet_NotOpen_NothingChanges(int slot)
    {
        // Arrange
        var rig = NewRig();
        ushort from = rig.CreateGrid(8, 4);
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
            Assert.NotEqual(rig.Header(chest).Version, rig.Header(inventory).Version);
        }
        else
        {
            a = rig.PlaceMail(chest, "letter", 2, 1, Address3, InvAutoPlaceRig.Ids(1, 5));
            if (row == "whole_merge")
            {
                _ = rig.PlaceMail(inventory, "letter", 0, 0, Address3, new uint[] { 10, 11 });
            }

            InvOpTestHelpers.ExhaustArenaThroughIntakeSpawns(rig);
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
        InvOpTestHelpers.AssertOk(whole);
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
        InvOpTestHelpers.ExhaustArenaThroughIntakeSpawns(rig);
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

        InvReplyExtra extra = InvOpTestHelpers.AssertOk(log).ExtraAs<InvReplyExtra>();
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
            InvReplyExtra extra = InvOpTestHelpers.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, 2))).ExtraAs<InvReplyExtra>();

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
        InvOpTestHelpers.AssertOk(whole);
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
                InvOpTestHelpers.ExhaustArenaThroughIntakeSpawns(rig);
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
                InvOpTestHelpers.ExhaustArenaThroughIntakeSpawns(rig);
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
            InvOpTestHelpers.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, chest, axe, chest, x, 0)));
        }

        _ = rig.PlaceItem(inventory, "fiber", 7, 1, 1);
        uint chestV = rig.Header(chest).Version;
        uint hotbarV = rig.Header(hotbar).Version;
        uint inventoryV = rig.Header(inventory).Version;
        Assert.Equal(3, new[] { chestV, hotbarV, inventoryV }.Distinct().Count());

        // Act
        InvReplyExtra ok = InvOpTestHelpers.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, axe))).ExtraAs<InvReplyExtra>();

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
        InvOpTestHelpers.AssertOk(rig.Run(rig.Player(slot), QuickMoveOp(rig, chest, a, count)));

        // Assert
        ushort moved = rig.EntryAt(inventory, 0, 0);
        Assert.NotEqual(Occupancy.Free, moved);
        Assert.Equal(count == 0 ? 10 : count, (int)rig.Entries[moved].Count);
        Assert.Equal(mine + InvOpTestHelpers.ClassPts(rig, moved), rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)slot)));
        Assert.Equal(theirs, rig.Inventory.WeightPoints(PlayerSlot.FromValue((byte)(1 - slot))));
        MoveSplitMergeTests.AssertWeightAndCountsMatchRecount(rig);
        MoveSplitMergeTests.AssertIndexesMatchARecount(rig);
    }
}
