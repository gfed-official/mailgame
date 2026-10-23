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
/// Story grid-inventory/010, slice 1: the cursor origin (<see cref="InventorySystem.TryGetCursorOrigin"/>, recorded by the mutator's
/// <c>Place</c> into a cursor and cleared by its <c>Remove</c> from one; Mail Review r1 B1), the R9 quick_move rows (<see cref="TargetOrder.QuickMoveRow"/>; GDD l.120-128)
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
public partial class QuickMoveReturnTests
{
    private const uint Address3 = 3u;
    private const uint Address5 = 5u;

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
        InvOpTestHelpers.AssertOk(rig.Run(rig.Player(slot), MoveOp(rig, inventory, gone, inventory, 6, 1)));
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
        InvOpTestHelpers.AssertOk(rig.Run(rig.Player(other), MoveOp(rig, otherInventory, fiber, chest, x, y)));
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

    /// <summary>The leash put-away: <c>CloseExternal(Leash)</c> for <paramref name="ps"/> inside a tick phase (story 011).</summary>
    private static void Leash(InvAutoPlaceRig rig, PlayerSlot ps) =>
        Assert.True(IntakeSpawnOutfeedTests.InPhase(rig, () =>
        {
            rig.Inventory.CloseExternal(ps, CloseCause.Leash);
            return true;
        }));

    private static Op MergeOp(InvAutoPlaceRig rig, ushort from, ushort entry, ushort to, ushort target)
    {
        var op = new Op { Kind = InvOpKinds.Merge, ReqId = 83 };
        op.SetPayload(new MergePayload
        {
            From = rig.IdOf(from).Value, EntryId = rig.Entries[entry].Id.Value, To = rig.IdOf(to).Value, TargetEntryId = rig.Entries[target].Id.Value,
        });
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
