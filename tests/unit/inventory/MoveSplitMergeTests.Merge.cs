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
using static PerformativeMail.Sim.Tests.Unit.Inventory.InvOpTestHelpers;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

public partial class MoveSplitMergeTests
{
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
}
