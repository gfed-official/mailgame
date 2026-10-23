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
}
