using System.Collections.Immutable;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Inventory.Ops;

/// <summary>
/// Grid Inventory's <see cref="OpKind"/> values (ADR-0007 section 4 inventory block <c>0x0080-0x00FF</c>; ADR-0009 section 1; story
/// grid-inventory/004). Values follow ADR-0009 section 1's table order, are append-only and never reused. The M-1a kinds have a
/// descriptor in <see cref="InvOpDescriptors"/>; <c>MoveGroup</c>, <c>QuickMoveGroup</c>, <c>Split</c>, <c>TakeAll</c> and <c>Sort</c> are
/// reserved here and registered at M1. <c>DeliverMatching</c> is Delivery's (block <c>0x0100-0x013F</c>, ADR-0009 erratum 2026-10-16);
/// the throw op is Throwing's <c>ThrowRelease</c>. Kept in the owning module (as <see cref="CorePlayerOps"/> keeps Sim Core's
/// descriptors) so Sim Core's enum never names a system's ops; <c>0x00F0</c> stays unassigned (Sim Core tests use it as "unregistered").
/// </summary>
/// <example><code>var op = new Op { Kind = InvOpKinds.Move, ReqId = 7 };</code></example>
public static class InvOpKinds
{
    /// <summary>Pick an entry onto the cursor.</summary>
    public const OpKind Pick = (OpKind)0x0080;

    /// <summary>Move (all or part of) an entry to a position.</summary>
    public const OpKind Move = (OpKind)0x0081;

    /// <summary>Reserved (M1): move a manifest group.</summary>
    public const OpKind MoveGroup = (OpKind)0x0082;

    /// <summary>Merge an entry onto a target stack.</summary>
    public const OpKind Merge = (OpKind)0x0083;

    /// <summary>Reserved (M1): split k units off an entry.</summary>
    public const OpKind Split = (OpKind)0x0084;

    /// <summary>Quick-move an entry along the panel chain.</summary>
    public const OpKind QuickMove = (OpKind)0x0085;

    /// <summary>Reserved (M1): quick-move a manifest group.</summary>
    public const OpKind QuickMoveGroup = (OpKind)0x0086;

    /// <summary>Reserved (M1): take everything matching a filter.</summary>
    public const OpKind TakeAll = (OpKind)0x0087;

    /// <summary>Return the cursor's entry (payload-free).</summary>
    public const OpKind Return = (OpKind)0x0088;

    /// <summary>Reserved (M1): sort a container.</summary>
    public const OpKind Sort = (OpKind)0x0089;

    /// <summary>Drop an entry into the world.</summary>
    public const OpKind Drop = (OpKind)0x008A;

    /// <summary>Pick up a world item (range-gated).</summary>
    public const OpKind Pickup = (OpKind)0x008B;

    /// <summary>Open a container (range-gated).</summary>
    public const OpKind Open = (OpKind)0x008C;

    /// <summary>Close a container.</summary>
    public const OpKind Close = (OpKind)0x008D;

    /// <summary>The reserved (not yet registered) M1 values.</summary>
    public static ImmutableArray<OpKind> Reserved { get; } = ImmutableArray.Create(MoveGroup, Split, QuickMoveGroup, TakeAll, Sort);
}

/// <summary>
/// The descriptors of the M-1a inventory kinds (ADR-0007 section 4; ADR-0009 sections 1-2). <see cref="InventorySystem"/> binds
/// <see cref="Pick"/> and <see cref="Move"/> (story 007) and <see cref="Close"/> (story 011); the others are not bound yet: a declared
/// kind without a handler fails <c>Freeze</c> (<c>MissingOpHandler</c>), and their handlers are stories 008-010 and 019. Wire codec ids
/// and sizes are 0 (unassigned) until the network-adapter-loopback epic assigns them. Reasons are the inventory codes each kind's ADR-0009
/// chain can return (the precedence chains themselves are the handler stories'); every block-acquiring kind (<c>Move</c>, <c>Merge</c>,
/// <c>QuickMove</c>, <c>Pickup</c>, <c>Return</c>) declares <c>(kind, 85) capacity_exhausted</c>. <c>bad_request</c> is Core's 8 and is
/// not declared.
/// </summary>
/// <example><code>foreach (OpDescriptor d in InvOpDescriptors.All) { builder.BindOp(d, handlerFor(d.Kind)); }</code></example>
public static class InvOpDescriptors
{
    /// <summary><see cref="InvOpKinds.Pick"/>: 8 B; not_open, stale_entry, cursor_busy, container_gone.</summary>
    public static OpDescriptor Pick { get; } = OpDescriptor.For<PickPayload>(
        InvOpKinds.Pick, false, 0, 0,
        R(InvReason.NotOpen), R(InvReason.StaleEntry), R(InvReason.CursorBusy), R(InvReason.ContainerGone));

    /// <summary><see cref="InvOpKinds.Move"/>: 24 B; the placement chain plus bad_count (count above the entry's total), container_gone and capacity_exhausted.</summary>
    public static OpDescriptor Move { get; } = OpDescriptor.For<MovePayload>(
        InvOpKinds.Move, false, 0, 0,
        R(InvReason.NotOpen), R(InvReason.StaleEntry), R(InvReason.StaleLayout), R(InvReason.ReservedSlot), R(InvReason.OutOfBounds),
        R(InvReason.NotCarryable), R(InvReason.NotAllowed), R(InvReason.Blocked), R(InvReason.StackFull), R(InvReason.BadCount),
        R(InvReason.ContainerGone), R(InvReason.CapacityExhausted));

    /// <summary><see cref="InvOpKinds.Merge"/>: 16 B; not_open, stale_entry, not_allowed, blocked (different StackKey, GDD H-R7-3), stack_full, container_gone, capacity_exhausted.</summary>
    public static OpDescriptor Merge { get; } = OpDescriptor.For<MergePayload>(
        InvOpKinds.Merge, false, 0, 0,
        R(InvReason.NotOpen), R(InvReason.StaleEntry), R(InvReason.NotAllowed), R(InvReason.Blocked), R(InvReason.StackFull),
        R(InvReason.ContainerGone), R(InvReason.CapacityExhausted));

    /// <summary><see cref="InvOpKinds.QuickMove"/>: 12 B; not_open, stale_entry, not_allowed, no_room, container_gone, capacity_exhausted.</summary>
    public static OpDescriptor QuickMove { get; } = OpDescriptor.For<QuickMovePayload>(
        InvOpKinds.QuickMove, false, 0, 0,
        R(InvReason.NotOpen), R(InvReason.StaleEntry), R(InvReason.NotAllowed), R(InvReason.NoRoom), R(InvReason.ContainerGone),
        R(InvReason.CapacityExhausted));

    /// <summary><see cref="InvOpKinds.Return"/>: payload-free (<see cref="OpDescriptor.Empty"/>); cursor_empty, blocked, stack_full, no_room, capacity_exhausted.</summary>
    public static OpDescriptor Return { get; } = OpDescriptor.Empty(
        InvOpKinds.Return, false, 0, 0,
        R(InvReason.CursorEmpty), R(InvReason.Blocked), R(InvReason.StackFull), R(InvReason.NoRoom), R(InvReason.CapacityExhausted));

    /// <summary><see cref="InvOpKinds.Drop"/>: 4 B; not_open, stale_entry, swimming (GDD H-R7-4), container_gone.</summary>
    public static OpDescriptor Drop { get; } = OpDescriptor.For<DropPayload>(
        InvOpKinds.Drop, false, 0, 0,
        R(InvReason.NotOpen), R(InvReason.StaleEntry), R(InvReason.Swimming), R(InvReason.ContainerGone));

    /// <summary><see cref="InvOpKinds.Pickup"/>: 20 B; the P13 range chain (no_target, out_of_range, position_stale), stale_entry, no_room, container_gone (a picked-up or expired WorldItem's tombstone, GDD R7), capacity_exhausted.</summary>
    public static OpDescriptor Pickup { get; } = OpDescriptor.For<PickupPayload>(
        InvOpKinds.Pickup, false, 0, 0,
        R(InvReason.OutOfRange), R(InvReason.PositionStale), R(InvReason.StaleEntry), R(InvReason.NoRoom), R(InvReason.NoTarget),
        R(InvReason.ContainerGone), R(InvReason.CapacityExhausted));

    /// <summary><see cref="InvOpKinds.Open"/>: 16 B; the P13 range chain (no_target, out_of_range, position_stale) and container_gone.</summary>
    public static OpDescriptor Open { get; } = OpDescriptor.For<OpenPayload>(
        InvOpKinds.Open, false, 0, 0,
        R(InvReason.OutOfRange), R(InvReason.PositionStale), R(InvReason.ContainerGone), R(InvReason.NoTarget));

    /// <summary><see cref="InvOpKinds.Close"/>: 4 B; not_open, container_gone.</summary>
    public static OpDescriptor Close { get; } = OpDescriptor.For<ClosePayload>(
        InvOpKinds.Close, false, 0, 0,
        R(InvReason.NotOpen), R(InvReason.ContainerGone));

    /// <summary>Every M-1a inventory descriptor, in ascending kind order.</summary>
    public static ImmutableArray<OpDescriptor> All { get; } = ImmutableArray.Create(Pick, Move, Merge, QuickMove, Return, Drop, Pickup, Open, Close);

    private static ReasonDef R(InvReason reason) => InvReasons.Def(reason);
}
