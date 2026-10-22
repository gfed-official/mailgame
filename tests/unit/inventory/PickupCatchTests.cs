using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/014 (GDD R8, R9 l.128-129, R12 l.137, edge cases l.362-363, H-R9-2, H-R12-1; ADR-0009 sections 1, 2 and 5;
/// TD-154 Mail Lead planning defaults pending Evan): the pickup core (<see cref="PickupCore"/>) and <see cref="IInventoryApi.TryCatch"/>
/// with its fumble. Pickups run both through a test-only <c>Pickup</c> handler (<see cref="PickupTestHandler"/>, bound only in this
/// rig: production binds Pickup in story 019) in the real phase-0 drain, and through the core directly inside a tick phase; catches run
/// inside a tick phase, as Throwing's flight resolution will. State is arranged through real calls (the mutator-backed
/// <c>PlaceMail</c> / <c>PlaceItem</c>, <c>CreateWorldItem</c>, <c>DetachForThrow</c>).
/// </summary>
public class PickupCatchTests
{
    private const uint Address = 4u;

    // ---------------------------------------------------------------- TargetOrder.PickupRow

    [Fact]
    public void PickupRow_WritesHotbarThenPlayerInventory_AndFitsTheLongestRow()
    {
        // Arrange
        Span<ushort> targets = stackalloc ushort[TargetOrder.MaxRowLength];

        // Act
        TargetOrder.PickupRow(7, 9, targets);

        // Assert: [hotbar, player_inventory], no backpack at M-1a
        Assert.Equal(7, targets[0]);
        Assert.Equal(9, targets[1]);
        Assert.Equal(2, TargetOrder.PickupRowLength);
        Assert.Equal(2, TargetOrder.MaxRowLength);
    }

    // ---------------------------------------------------------------- whole pickup

    [Fact]
    public void WholePickup_OfALetterWorldItem_IntoAnEmptyPanel_GoesToHotbarSlot1_RemovesTheItem_AndLeavesTheTakenTombstone()
    {
        // Arrange
        var p = new PickupRig();
        ushort letters = p.Letters(101, 5);
        EntryId entry = p.R.Entries[letters].Id;
        EntityId item = p.Drop(letters);
        int tombstones = p.World.Tombstones.Count;

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert: the entry itself goes into hotbar slot 1 (never 0), keeps its id and its 5 ids
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.True(reply.IsOk);
        InvReplyExtra extra = reply.ExtraAs<InvReplyExtra>();
        Assert.Equal(5, extra.RequestedUnits);
        Assert.Equal(5, extra.GotUnits);
        Assert.Equal(p.Hotbar(0), p.R.Entries[letters].ContainerSlot);
        Assert.Equal(1, p.R.Entries[letters].X);
        Assert.Equal(entry, p.R.Entries[letters].Id);
        Assert.Equal(PickupRig.Ids(101, 5), p.R.IdsOf(letters));
        Assert.False(p.World.Table.TryGetSlot(item, out _));
        Assert.Equal(tombstones + 1, p.World.Tombstones.Count);
        Assert.Equal((ushort)InvReason.ContainerGone, p.World.Resolve(item, out _, out uint by, out InvCause cause));
        Assert.Equal(p.A.Value, by);
        Assert.Equal(InvCause.Taken, cause);
        Assert.Equal(PickupVerdict.Taken, p.Handler.Last.Verdict);
    }

    [Fact]
    public void WholePickup_ThroughTheCoreInATickPhase_IsTheSameAsThroughTheHandler()
    {
        // Arrange
        var p = new PickupRig();
        ushort letters = p.Letters(101, 5);
        EntityId item = p.Drop(letters);

        // Act
        PickupResult r = p.Core(p.A, item, 0, out InvReplyExtra extra);

        // Assert
        Assert.Equal(new PickupResult(PickupVerdict.Taken, 0, 0, InvCause.None, 5, 5), r);
        Assert.Equal(5, extra.GotUnits);
        Assert.Equal(p.Hotbar(0), p.R.Entries[letters].ContainerSlot);
        Assert.False(p.World.Table.TryGetSlot(item, out _));
    }

    // ---------------------------------------------------------------- H-R12-1 (core form)

    [Fact]
    [Trait("Id", "H-R12-1")]
    public void HR121_OneSmallWorldItem_AAndBPickUpInOneTick_AFirst_AGainsIt_BGetsContainerGoneByA_Taken_OneCopy()
    {
        // Arrange: one Small parcel WorldItem; A and B have empty panels
        var p = new PickupRig();
        ushort small = p.R.DetachedMail("small", Address, new[] { 900u });
        EntityId item = p.Drop(small);

        // Act: both pickups in one tick, A's enqueued first
        EventEntry[] log = p.PickupBoth(p.A, item, p.B, item);

        // Assert: A gains it; B gets container_gone with byActorId = A, cause taken, from the tombstone A's removal wrote (age 0)
        Reply a = p.ReplyFor(log, p.A);
        Reply b = p.ReplyFor(log, p.B);
        Assert.True(a.IsOk);
        Assert.Equal((ushort)InvReason.ContainerGone, b.Reason);
        InvReplyExtra bx = b.ExtraAs<InvReplyExtra>();
        Assert.Equal(p.A.Value, bx.ByActorId);
        Assert.Equal(InvCause.Taken, bx.Cause);
        Assert.Equal(p.Hotbar(0), p.R.Entries[small].ContainerSlot);
        Assert.Equal(1, p.CopiesOf(900u));
        Assert.Equal(0, p.EntriesIn(p.Hotbar(1)) + p.EntriesIn(p.Inventory(1)));
        WorldItemDespawnedPayload gone = Assert.Single(WorldItemTests.Heads(log, InvEventKinds.WorldItemDespawned)).PayloadAs<WorldItemDespawnedPayload>();
        Assert.Equal(p.A.Value, gone.ByActorId);
        Assert.Equal((byte)InvCause.Taken, gone.Cause);
    }

    // ---------------------------------------------------------------- partial pickup (GDD R12 l.137, l.363)

    [Fact]
    public void PartialPickup_5Letters_RoomFor3_The3OldestMerge_TheItemKeepsIdEntryTimerPositionAndNode_With2Newest_OneEntriesChanged()
    {
        // Arrange: A's hotbar slot 1 holds 17 letters to #4, every other cell of A's panel is full
        var p = new PickupRig();
        ushort stack = p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 17));
        p.FillHotbar(0, from: 2);
        p.FillInventory(0);
        ushort letters = p.Letters(101, 5);
        EntryId entry = p.R.Entries[letters].Id;
        EntityId item = p.Drop(letters);
        Assert.True(p.World.Table.TryGetSlot(item, out ushort ws));
        WorldItemRecord before = p.World.Table[ws];
        uint hotbarV = p.R.Header(p.Hotbar(0)).Version;
        uint inventoryV = p.R.Header(p.Inventory(0)).Version;
        uint seq = p.World.WorldSeq;
        int tombstones = p.World.Tombstones.Count;

        // Act
        EventEntry[] log = p.Pickup(p.A, item);

        // Assert: ok, requested 5 / got 3; the 3 oldest (101-103) merged into the hotbar stack (20)
        Reply reply = InvAutoPlaceRig.ReplyOf(log);
        Assert.True(reply.IsOk);
        Assert.Equal(5, reply.ExtraAs<InvReplyExtra>().RequestedUnits);
        Assert.Equal(3, reply.ExtraAs<InvReplyExtra>().GotUnits);
        Assert.Equal(20, p.R.Entries[stack].Count);
        Assert.Equal(PickupRig.Ids(1, 17).Concat(new[] { 101u, 102u, 103u }), p.R.IdsOf(stack));

        // the WorldItem survives: same id, entry, expiresTick, position, FIFO, with the 2 newest
        Assert.True(p.World.Table.TryGetSlot(item, out ushort after));
        Assert.Equal(ws, after);
        WorldItemRecord now = p.World.Table[after];
        Assert.Equal(before.EntrySlot, now.EntrySlot);
        Assert.Equal(before.ExpiresTick, now.ExpiresTick);
        Assert.Equal(before.PosMm, now.PosMm);
        Assert.Equal(before.Fifo, now.Fifo);
        Assert.Equal(entry, p.R.Entries[letters].Id);
        Assert.Equal(new[] { 104u, 105u }, p.R.IdsOf(letters));
        Assert.Equal(EntryRecord.NoContainer, p.R.Entries[letters].ContainerSlot);
        Assert.True(p.World.Fifos.TryPeekHead(ExpiryFifo.MailWorldItem, p.World.Table, countVisits: false, out ushort head));
        Assert.Equal(after, head);

        // one WorldItemEntriesChanged {id, next worldSeq, entryId, 2}; no despawn, no tombstone; hotbar +1 only
        WorldItemEntriesChangedPayload changed = Assert.Single(WorldItemTests.Heads(log, InvEventKinds.WorldItemEntriesChanged)).PayloadAs<WorldItemEntriesChangedPayload>();
        Assert.Equal(item.Value, changed.WorldItemId);
        Assert.Equal(seq + 1, changed.WorldSeq);
        Assert.Equal(entry.Value, changed.EntryId);
        Assert.Equal(2, changed.Count);
        Assert.Empty(WorldItemTests.Heads(log, InvEventKinds.WorldItemDespawned));
        Assert.Equal(tombstones, p.World.Tombstones.Count);
        Assert.Equal(hotbarV + 1, p.R.Header(p.Hotbar(0)).Version);
        Assert.Equal(inventoryV, p.R.Header(p.Inventory(0)).Version);
        Assert.Equal(p.Hotbar(0), p.R.SlotOf(new ContainerId(Assert.Single(WorldItemTests.Heads(log, InvEventKinds.Version)).PayloadAs<InvVersionPayload>().Container)));
    }

    [Fact]
    public void PartialPickup_ThenBInTheSameTick_TakesThe2Left_WritesTheTakenTombstoneByB_NoMailIdDuplicated()
    {
        // Arrange: as the partial case; B's panel is empty
        var p = new PickupRig();
        ushort stack = p.R.PlaceMail(p.Hotbar(0), "letter", 1, 0, Address, PickupRig.Ids(1, 17));
        p.FillHotbar(0, from: 2);
        p.FillInventory(0);
        ushort letters = p.Letters(101, 5);
        EntityId item = p.Drop(letters);

        // Act: A then B in one tick
        EventEntry[] log = p.PickupBoth(p.A, item, p.B, item);

        // Assert: A got 3, B got the 2 newest (the entry itself, into B's hotbar slot 1); the tombstone names B, taken
        Reply a = p.ReplyFor(log, p.A);
        Reply b = p.ReplyFor(log, p.B);
        Assert.True(a.IsOk);
        Assert.True(b.IsOk);
        Assert.Equal(3, a.ExtraAs<InvReplyExtra>().GotUnits);
        Assert.Equal(2, b.ExtraAs<InvReplyExtra>().GotUnits);
        Assert.Equal(2, b.ExtraAs<InvReplyExtra>().RequestedUnits);
        Assert.Equal(p.Hotbar(1), p.R.Entries[letters].ContainerSlot);
        Assert.Equal(new[] { 104u, 105u }, p.R.IdsOf(letters));
        Assert.False(p.World.Table.TryGetSlot(item, out _));
        Assert.Equal((ushort)InvReason.ContainerGone, p.World.Resolve(item, out _, out uint by, out InvCause cause));
        Assert.Equal(p.B.Value, by);
        Assert.Equal(InvCause.Taken, cause);
        for (uint id = 101; id <= 105; id++)
        {
            Assert.Equal(1, p.CopiesOf(id));
        }

        Assert.Equal(20, p.R.Entries[stack].Count);
        Assert.Single(WorldItemTests.Heads(log, InvEventKinds.WorldItemEntriesChanged));
        Assert.Single(WorldItemTests.Heads(log, InvEventKinds.WorldItemDespawned));
    }

    [Fact]
    public void PickupHandler_IsBoundOnlyByThisRig_NotByInventorySystemBind()
    {
        // Arrange: a world with the production module only
        var rig = new InvAutoPlaceRig();
        var op = new Op { Kind = InvOpKinds.Pickup, ReqId = 5 };
        op.SetPayload(new PickupPayload { WorldItemId = 1 });

        // Act
        EventEntry[] log = rig.Run(rig.PlayerA, in op);

        // Assert: Pickup has no handler there (story 019 binds the production one), so the drain answers unknown_kind
        Assert.Equal(CoreReason.UnknownKind, InvAutoPlaceRig.ReplyOf(log).Reason);
    }
}

/// <summary>
/// TEST-ONLY <c>Pickup</c> handler (story grid-inventory/014): the shared request check (payload length, entry namespace), then
/// <see cref="PickupCore.Apply"/>; a refusal writes the core's reason, <c>byActorId</c> and cause into the reply extra. A floating
/// verdict replies <c>bad_request</c> here (story 019's anchor maps it to P13 <c>unavailable</c> before the core runs). Never bound in
/// <c>InventorySystem.Bind</c> or <c>InventoryWithStubOps</c>; production binds Pickup in story 019. The last outcome is
/// <see cref="Last"/>.
/// </summary>
internal sealed class PickupTestHandler : IOpHandler
{
    public InventorySystem System { get; set; } = null!;

    public OpKind Kind => InvOpKinds.Pickup;

    /// <summary>The last call's outcome.</summary>
    public PickupResult Last { get; private set; }

    public Reply Apply(in Op op, in TickContext ctx)
    {
        var extra = new InvReplyExtra();
        ushort reason = System.RequestCheck.CheckRequest(in op, System.ActorSlot(op.Actor));
        if (reason != 0)
        {
            Last = PickupResult.Refuse(reason);
            return Reply.Reject(in op, reason);
        }

        PickupPayload p = op.PayloadAs<PickupPayload>();
        Last = System.Pickup.Apply(op.Actor, new EntityId(p.WorldItemId), p.EntryId, ref extra);
        if (Last.IsTaken)
        {
            return Reply.Ok(in op, in extra);
        }

        extra.ByActorId = Last.ByActorId;
        extra.SetCause(Last.Cause, unchanged: true);
        Reply rejected = Reply.Reject(in op, Last.Verdict == PickupVerdict.Floating ? CoreReason.BadRequest : Last.Reason);
        rejected.SetExtra(in extra);
        return rejected;
    }
}

/// <summary>
/// The story 014 rig: <see cref="InvAutoPlaceRig"/> with players A (slot 0) and B (slot 1) and the <see cref="PickupTestHandler"/>
/// bound, plus the WorldItem helpers of <see cref="WorldItemRig"/>. Fill helpers leave no free cell: axes on hotbar slots, full plank
/// stacks (20/20) on the player_inventory's 16 cells.
/// </summary>
internal sealed class PickupRig
{
    private uint _reqId = 5000;

    public PickupRig(int? mailIdArenaUnits = null, Func<InventorySettings, InventorySettings>? tune = null)
    {
        var handler = new PickupTestHandler();
        Handler = handler;
        R = new InvAutoPlaceRig(
            mailIdArenaUnits: mailIdArenaUnits,
            tune: tune,
            joinPlayerB: true,
            bindExtra: (builder, inventory) =>
            {
                handler.System = inventory;
                builder.BindOp(InvOpDescriptors.Pickup, handler);
            });
        W = new WorldItemRig(R);
    }

    public InvAutoPlaceRig R { get; }

    public WorldItemRig W { get; }

    public PickupTestHandler Handler { get; }

    public InventorySystem Inv => R.Inventory;

    public WorldItems World => Inv.WorldItems;

    public ActorId A => R.PlayerA;

    public ActorId B => R.PlayerB;

    public static uint[] Ids(uint first, int count) => InvAutoPlaceRig.Ids(first, count);

    public ushort Hotbar(int player) => R.HotbarOf(player);

    public ushort Inventory(int player) => R.InventoryOf(player);

    public ushort Cursor(int player) => R.CursorOf(player);

    /// <summary>A detached entry of <paramref name="count"/> letters to #4 with ids from <paramref name="first"/>.</summary>
    public ushort Letters(uint first, int count) => R.DetachedMail("letter", 4u, Ids(first, count));

    /// <summary>A WorldItem of the detached entry, created in the next tick.</summary>
    public EntityId Drop(ushort entry, bool onWater = false, WorldItemOrigin origin = WorldItemOrigin.Dropped) => W.Create(W.Now, entry, origin, onWater);

    /// <summary>Axes on <paramref name="player"/>'s hotbar slots <paramref name="from"/> to 7.</summary>
    public void FillHotbar(int player, int from = 1)
    {
        for (int x = from; x < R.Header(Hotbar(player)).W; x++)
        {
            R.PlaceItem(Hotbar(player), "axe", x, 0, 1);
        }
    }

    /// <summary>A full plank stack (20) on every cell of <paramref name="player"/>'s player_inventory from cell <paramref name="from"/> (row-major).</summary>
    public void FillInventory(int player, int from = 0)
    {
        ref ContainerHeader h = ref R.Header(Inventory(player));
        int w = h.W;
        int cells = h.W * h.H;
        for (int i = from; i < cells; i++)
        {
            R.PlaceItem(Inventory(player), "plank", i % w, i / w, 20);
        }
    }

    public Op PickupOp(EntityId item, uint entry = 0)
    {
        var op = new Op { Kind = InvOpKinds.Pickup, ReqId = _reqId++ };
        op.SetPayload(new PickupPayload { WorldItemId = item.Value, EntryId = entry });
        return op;
    }

    /// <summary>One pickup op by <paramref name="actor"/> in the next tick; returns that tick's log.</summary>
    public EventEntry[] Pickup(ActorId actor, EntityId item, uint entry = 0)
    {
        Op op = PickupOp(item, entry);
        R.CaptureEvents = true;
        EventEntry[] log = R.Run(actor, in op);
        R.CaptureEvents = false;
        return log;
    }

    /// <summary>Two pickup ops in one tick, <paramref name="first"/>'s enqueued first; returns that tick's log.</summary>
    public EventEntry[] PickupBoth(ActorId first, EntityId a, ActorId second, EntityId b)
    {
        Op opA = PickupOp(a);
        Op opB = PickupOp(b);
        Assert.True(R.Host.World.EnqueueOp(first, in opA));
        Assert.True(R.Host.World.EnqueueOp(second, in opB));
        R.CaptureEvents = true;
        R.Host.World.Tick();
        R.CaptureEvents = false;
        return R.Sink.Last;
    }

    /// <summary>The core called directly inside the next tick's <c>MailStep</c>.</summary>
    public PickupResult Core(ActorId actor, EntityId item, uint entry, out InvReplyExtra extra)
    {
        InvReplyExtra x = default;
        PickupResult r = W.At(W.Now, () => Inv.Pickup.Apply(actor, item, entry, ref x));
        extra = x;
        return r;
    }

    /// <summary>The reply sent to <paramref name="actor"/>'s slot in <paramref name="log"/>.</summary>
    public Reply ReplyFor(EventEntry[] log, ActorId actor)
    {
        byte bit = (byte)(1 << Inv.ActorSlot(actor).Value);
        return Assert.Single(log, e => e.Kind == EventKind.OpReply && (e.Audience & bit) != 0).PayloadAs<Reply>();
    }

    /// <summary>Live entries holding mail id <paramref name="mailId"/> (placed or detached).</summary>
    public int CopiesOf(uint mailId)
    {
        int n = 0;
        for (ushort s = 0; s < R.Entries.Capacity; s++)
        {
            if (R.Entries[s].IsLive && R.Entries[s].IsMail)
            {
                n += R.IdsOf(s).Count(id => id == mailId);
            }
        }

        return n;
    }

    /// <summary>Live entries placed in <paramref name="container"/>.</summary>
    public int EntriesIn(ushort container) => R.Header(container).EntryCount;
}
