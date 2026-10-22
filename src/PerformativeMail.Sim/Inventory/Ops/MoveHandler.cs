using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Ops;

/// <summary>
/// The production <c>Move</c> handler (story grid-inventory/007; GDD R7 <c>move {from, entryId, to, x, y, rotated, count?}</c>; ADR-0009
/// sections 2-3): a whole-entry move to a position, within a container or across two, all or nothing. "Place" is a <c>move</c> from
/// the cursor; a <c>move</c> to the cursor is an ordinary slots destination. <see cref="Apply"/> is <see cref="Validate"/>, then on 0
/// <see cref="Commit"/>, then the reply. The chain is the placement chain with <c>bad_count</c> after <c>stale_entry</c>:
/// <c>bad_request → not_open → stale_entry → bad_count → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed →
/// blocked / stack_full</c>, then <c>capacity_exhausted</c> for a mail merge's union block:
/// <list type="number">
/// <item><c>bad_request</c>: <see cref="InvRequestCheck.CheckRequest"/> with the actor's slot (from <see cref="Op.Actor"/>);</item>
/// <item><c>not_open</c>: <c>from</c> or <c>to</c> is not in the actor's open set (<see cref="OpenSet.IsInOpenSet"/>; the System actor
/// has an empty set);</item>
/// <item><c>stale_entry</c>: <see cref="InvRequestCheck.ResolveEntries"/>, then a live entry that is not in <c>from</c>;</item>
/// <item><c>bad_count</c>: <c>count</c> 0 or the entry's total is a whole move that keeps the entry id (ADR-0009 section 1, "0 =
/// whole"; Mail Lead planning default pending Evan, TD-105 item 3, TD-150); any other count is <c>bad_count</c> — above the total for
/// good, below it until story 008 adds split-and-place;</item>
/// <item><c>stale_layout</c>: <c>baseVersion</c> below the destination's <see cref="ContainerHeader.LastReplaceVersion"/> (only
/// <c>sort</c> sets it, M1);</item>
/// <item><c>reserved_slot</c> to <c>blocked / stack_full</c>: <see cref="PlacementRules.Validate"/> with the entry as a whole-move
/// source, so its own cells are free to it (H-R4-2 a) and a move onto them with the same rotation is a free placement;</item>
/// <item><c>capacity_exhausted</c>: a placement onto a mergeable stack is an implicit merge of <c>min(count, effMax − target.count)</c>
/// units (the remainder stays at the source with its id; Mail Lead planning default pending Evan, TD-150), whose mail union block
/// <c>target.count + units</c> is planned with <see cref="InvPlan.AddAcquiredBlock"/> and must pass
/// <see cref="InvRequestCheck.CheckArenaReservation(in InvPlan)"/>; refused, the entry stays where it is (on the cursor for a place).</item>
/// </list>
/// <c>container_gone</c> stays declared but is unreachable at M-1a (no container is freed; a dead id fails <c>not_open</c> first;
/// TD-136). The commit is one <see cref="InventoryMutator.Transfer"/> (across containers: <c>Remove</c> then <c>Place</c>; within one:
/// the same-container path, which raises nothing and marks nothing for a move onto the entry's own cells with the same rotation, so
/// that no-op replies <c>ok</c> with no bump and no event) or one <see cref="InventoryMutator.MergeInto"/>, in one
/// <c>Begin</c>/<c>End</c> bracket inside the drain's call scope: every changed container bumps exactly once (a cursor → chest move
/// bumps both, TD-150). Weight, the StackKey index and the item counts are kept by those primitives. A reject mutates and raises
/// nothing; once the containers are resolved its reply carries their current versions. Allocation-free; never throws. Not
/// thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>builder.BindOp(InvOpDescriptors.Move, new MoveHandler(inventory, content, stats));</code></example>
internal sealed class MoveHandler : IOpHandler
{
    private readonly InventorySystem _owner;
    private readonly ContentSet _content;
    private readonly IStatSheet _stats;

    /// <summary>Creates the handler over the module's storage, open set and mutator.</summary>
    /// <param name="owner">The inventory module.</param>
    /// <param name="content">The content set (container defs; the entry's def for its footprint, traits and <c>effMax</c>).</param>
    /// <param name="stats">The stat sheet (mail <c>effMax</c>, read at op time).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MoveHandler(InventorySystem owner, ContentSet content, IStatSheet stats)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        _owner = owner;
        _content = content;
        _stats = stats;
    }

    /// <inheritdoc />
    public OpKind Kind => InvOpKinds.Move;

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
    /// The pure half: walks the chain (see the class remarks) and on a pass writes the plan — the primaries (source, destination), the
    /// touched set (source, then destination) and either the placement or the merge with its planned arena block. Reads state only;
    /// writes nothing but <paramref name="plan"/>; allocates no id, draws no RNG, raises no event, touches no ledger. O(footprint cells).
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

        MovePayload p = op.PayloadAs<MovePayload>();
        var from = new ContainerId(p.From);
        var to = new ContainerId(p.To);
        if (!_owner.OpenSet.IsInOpenSet(actor, from) || !_owner.OpenSet.IsInOpenSet(actor, to))
        {
            return (ushort)InvReason.NotOpen;
        }

        // IsInOpenSet is false for a dead id, so both are live here.
        ContainerTable containers = _owner.Containers;
        _ = containers.TryGetSlot(from, out ushort fromSlot);
        _ = containers.TryGetSlot(to, out ushort toSlot);
        plan.SetPrimary(fromSlot, toSlot);
        reason = check.ResolveEntries(in op);
        if (reason != 0)
        {
            return reason;
        }

        // ResolveEntries passed, so the id is a live real id.
        EntryPool entries = _owner.Entries;
        _ = entries.TryGetSlot(EntryId.Real(p.EntryId), out ushort entrySlot);
        ref readonly EntryRecord e = ref entries[entrySlot];
        if (e.ContainerSlot != fromSlot)
        {
            return (ushort)InvReason.StaleEntry;
        }

        if (p.Count != 0 && p.Count != e.Count)
        {
            return (ushort)InvReason.BadCount;
        }

        ref readonly ContainerHeader h = ref containers[toSlot];
        if (p.BaseVersion < h.LastReplaceVersion)
        {
            return (ushort)InvReason.StaleLayout;
        }

        bool rotated = p.Rotated != 0;
        PlacementSubject subject = PlacementSubject.Of(_content, _stats, in e);
        PlacementVerdict v = PlacementRules.Validate(
            _owner.Occupancy, entries, toSlot, in h, _content.Containers[h.DefIndex], in subject, p.X, p.Y, rotated, entrySlot, wholeMove: true);
        if (!v.IsOk)
        {
            return (ushort)v.Reason;
        }

        // A fresh plan holds InvPlan.MaxTouched (4) containers, so these two touches always succeed.
        _ = plan.Touch(fromSlot);
        _ = plan.Touch(toSlot);
        if (!v.IsMerge)
        {
            // PlacementRules proved (x, y) inside the destination, whose width and rows fit a byte.
            plan.Placement = new InvPlacement(entrySlot, toSlot, (byte)p.X, (byte)p.Y, rotated);
            return 0;
        }

        // An item's maxStack may exceed 255 (no content rule caps it), but an entry's count is a u8: room is capped there.
        int targetCount = entries[v.TargetSlot].Count;
        int room = Math.Min(v.EffMax, byte.MaxValue) - targetCount;
        if (room <= 0)
        {
            return (ushort)InvReason.StackFull;
        }

        int units = Math.Min(e.Count, room);
        plan.Merge = new InvMerge(entrySlot, v.TargetSlot, (byte)units);
        if (e.IsMail)
        {
            _ = plan.AddAcquiredBlock(targetCount + units); // the union block; one block never overflows a fresh plan
        }

        return check.CheckArenaReservation(in plan);
    }

    /// <summary>
    /// The mutating half: applies <paramref name="plan"/>'s merge (<see cref="InventoryMutator.MergeInto"/>) or placement
    /// (<see cref="InventoryMutator.Transfer"/>) unchanged in one <c>Begin</c>/<c>End</c> bracket (cause <see cref="InvCause.None"/>).
    /// Never fails, never recomputes; a no-op transfer seals nothing and <see cref="InventoryMutator.End"/> sets <c>unchanged</c>.
    /// </summary>
    /// <param name="plan">The plan <see cref="Validate"/> passed.</param>
    /// <param name="actor">The acting actor (written into every seal).</param>
    /// <param name="extra">The reply extra <see cref="InventoryMutator.End"/> fills.</param>
    internal void Commit(ref InvPlan plan, ActorId actor, ref InvReplyExtra extra)
    {
        InventoryMutator m = _owner.Mutator;
        m.Begin(ref plan, actor, InvCause.None);
        if (plan.Merge.IsSet)
        {
            InvMerge merge = plan.Merge;
            m.MergeInto(ref plan, merge.SourceSlot, merge.TargetSlot, merge.Units);
        }
        else
        {
            InvPlacement to = plan.Placement;
            m.Transfer(ref plan, to.EntrySlot, to.ContainerSlot, to.X, to.Y, to.Rotated);
        }

        m.End(ref plan, ref extra);
    }
}
