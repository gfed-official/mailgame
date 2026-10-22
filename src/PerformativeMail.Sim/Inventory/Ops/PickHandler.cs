using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Ops;

/// <summary>
/// The production <c>Pick</c> handler (story grid-inventory/007; GDD R7 <c>pick {from, entryId}</c>; ADR-0009 section 2): lifts the
/// whole entry onto the actor's <b>empty</b> cursor. <see cref="Apply"/> is <see cref="Validate"/>, then on 0 <see cref="Commit"/>, then
/// the reply. The chain is <c>bad_request → not_open → stale_entry → cursor_busy</c>:
/// <list type="number">
/// <item><c>bad_request</c>: <see cref="InvRequestCheck.CheckRequest"/> with the actor's slot (from <see cref="Op.Actor"/>, never the payload);</item>
/// <item><c>not_open</c>: <c>from</c> or the actor's cursor is not in the actor's open set (<see cref="OpenSet.IsInOpenSet"/>; the
/// System actor has an empty set, and so has a joined actor without a panel);</item>
/// <item><c>stale_entry</c>: <see cref="InvRequestCheck.ResolveEntries"/>, then a live entry that is not in <c>from</c> (another
/// container's, or held by none);</item>
/// <item><c>cursor_busy</c>: the cursor holds an entry (so picking the cursor's own entry is <c>cursor_busy</c> too).</item>
/// </list>
/// <c>container_gone</c> stays declared but is unreachable at M-1a: no container is freed, and a dead id fails <c>not_open</c> first
/// (the container tombstones are M-1b, TD-136). A <c>carryByHand: false</c> pick is dormant at M-1a and undeclared (TD-105 item 4).
/// The commit is one <see cref="InventoryMutator.Transfer"/> to cursor slot 0 in one <c>Begin</c>/<c>End</c> bracket inside the
/// drain's call scope, so the source and the cursor bump once each. A reject mutates and raises nothing; once the containers are
/// resolved its reply carries their current versions. Allocation-free; never throws. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>builder.BindOp(InvOpDescriptors.Pick, new PickHandler(inventory));</code></example>
internal sealed class PickHandler : IOpHandler
{
    private readonly InventorySystem _owner;

    /// <summary>Creates the handler over the module's storage, open set and mutator.</summary>
    /// <param name="owner">The inventory module.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
    public PickHandler(InventorySystem owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <inheritdoc />
    public OpKind Kind => InvOpKinds.Pick;

    /// <inheritdoc />
    public Reply Apply(in Op op, in TickContext ctx)
    {
        var plan = new InvPlan();
        var extra = new InvReplyExtra();
        ushort reason = Validate(in op, ref plan);
        if (reason != 0)
        {
            _owner.Mutator.FillVersions(in plan, ref extra);
            Reply rejected = Reply.Reject(in op, reason);
            rejected.SetExtra(in extra);
            return rejected;
        }

        Commit(ref plan, op.Actor, ref extra);
        return Reply.Ok(in op, in extra);
    }

    /// <summary>
    /// The pure half: walks the chain (see the class remarks) and on a pass writes the plan — the primaries (source, cursor), the
    /// touched set (source, then cursor) and the placement (the entry at cursor slot 0). Reads state only; writes nothing but
    /// <paramref name="plan"/>; allocates no id, draws no RNG, raises no event, touches no ledger.
    /// </summary>
    /// <param name="op">The drained op.</param>
    /// <param name="plan">The call's plan.</param>
    /// <returns>0, or the earliest failing reason.</returns>
    internal ushort Validate(in Op op, ref InvPlan plan)
    {
        PlayerSlot actor = _owner.ActorSlot(op.Actor);
        InvRequestCheck check = _owner.RequestCheck;
        ushort reason = check.CheckRequest(in op, actor);
        if (reason != 0)
        {
            return reason;
        }

        PickPayload p = op.PayloadAs<PickPayload>();
        var from = new ContainerId(p.From);
        ContainerId cursor = _owner.CursorOf(actor);
        if (!_owner.OpenSet.IsInOpenSet(actor, from) || !_owner.OpenSet.IsInOpenSet(actor, cursor))
        {
            return (ushort)InvReason.NotOpen;
        }

        // IsInOpenSet is false for a dead id, so both are live here.
        ContainerTable containers = _owner.Containers;
        _ = containers.TryGetSlot(from, out ushort fromSlot);
        _ = containers.TryGetSlot(cursor, out ushort cursorSlot);
        plan.SetPrimary(fromSlot, cursorSlot);
        reason = check.ResolveEntries(in op);
        if (reason != 0)
        {
            return reason;
        }

        // ResolveEntries passed, so the id is a live real id.
        EntryPool entries = _owner.Entries;
        _ = entries.TryGetSlot(EntryId.Real(p.EntryId), out ushort entrySlot);
        if (entries[entrySlot].ContainerSlot != fromSlot)
        {
            return (ushort)InvReason.StaleEntry;
        }

        if (containers[cursorSlot].EntryCount > 0)
        {
            return (ushort)InvReason.CursorBusy;
        }

        // A fresh plan holds InvPlan.MaxTouched (4) containers, so these two touches always succeed.
        _ = plan.Touch(fromSlot);
        _ = plan.Touch(cursorSlot);
        plan.Placement = new InvPlacement(entrySlot, cursorSlot, 0, 0, false);
        return 0;
    }

    /// <summary>
    /// The mutating half: applies <paramref name="plan"/>'s placement unchanged through <see cref="InventoryMutator.Transfer"/> in one
    /// <c>Begin</c>/<c>End</c> bracket (cause <see cref="InvCause.None"/>). Never fails, never recomputes.
    /// </summary>
    /// <param name="plan">The plan <see cref="Validate"/> passed.</param>
    /// <param name="actor">The acting actor (written into every seal).</param>
    /// <param name="extra">The reply extra <see cref="InventoryMutator.End"/> fills.</param>
    internal void Commit(ref InvPlan plan, ActorId actor, ref InvReplyExtra extra)
    {
        InventoryMutator m = _owner.Mutator;
        InvPlacement to = plan.Placement;
        m.Begin(ref plan, actor, InvCause.None);
        m.Transfer(ref plan, to.EntrySlot, to.ContainerSlot, to.X, to.Y, to.Rotated);
        m.End(ref plan, ref extra);
    }
}
