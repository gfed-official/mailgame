using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Ops;

/// <summary>
/// The production <c>Move</c> handler (stories grid-inventory/007 and 008; GDD R7 <c>move {from, entryId, to, x, y, rotated,
/// count?}</c>; ADR-0009 sections 2-3): a whole-entry move to a position, within a container or across two, all or nothing; a
/// split-and-place for <c>1 ≤ count &lt; total</c> (story 008); and, from the cursor onto an occupied hotbar slot, a swap (story 008).
/// "Place" is a <c>move</c> from the cursor; a <c>move</c> to the cursor is an ordinary slots destination. <see cref="Apply"/> is
/// <see cref="Validate"/>, then on 0 <see cref="Commit"/>, then the reply. The chain is the placement chain (GDD l.103; ADR-0009
/// section 2 "Placement ops (Move, ..., swap)") with <c>bad_count</c> after <c>stale_entry</c>: <c>bad_request → not_open →
/// stale_entry → bad_count → stale_layout → reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full</c>,
/// then <c>capacity_exhausted</c> for a block or an entry the commit acquires:
/// <list type="number">
/// <item><c>bad_request</c>: <see cref="InvRequestCheck.CheckRequest"/> with the actor's slot (from <see cref="Op.Actor"/>);</item>
/// <item><c>not_open</c>: <c>from</c> or <c>to</c> is not in the actor's open set (<see cref="OpenSet.IsInOpenSet"/>; the System actor
/// has an empty set);</item>
/// <item><c>stale_entry</c>: <see cref="InvRequestCheck.ResolveEntries"/>, then a live entry that is not in <c>from</c>;</item>
/// <item><c>bad_count</c>: <c>count</c> above the entry's total (GDD l.103 "&gt; total for <c>move</c>"; story 008 AC
/// Split-and-place). <c>count</c> 0 or the total is a whole move that keeps the entry id (ADR-0009 section 1, "0 = whole"; Mail Lead
/// planning default pending Evan, TD-105 item 3, TD-150); <c>1 ≤ count &lt; total</c> is a split-and-place (GDD R7 move row, l.90);</item>
/// <item><c>stale_layout</c>: <c>baseVersion</c> below the destination's <see cref="ContainerHeader.LastReplaceVersion"/> (only
/// <c>sort</c> sets it, M1);</item>
/// <item><c>reserved_slot</c> to <c>blocked / stack_full</c>: <see cref="PlacementRules.Validate"/> with the entry as the source. A
/// whole move treats its own cells as free (H-R4-2 a), so a move onto them with the same rotation is a free placement; a split does
/// not, since the remainder still occupies them (GDD R4 l.79, l.365; H-R4-2 c), so a split onto or across its own cells is
/// <c>blocked</c>. A <c>blocked</c> verdict becomes a swap (no reason) when the move is whole, <c>from</c> is the actor's cursor, the
/// destination has the slots layout (the hotbar), one entry holds the cell (<see cref="Occupancy.OwnerAt"/>) and that entry passes
/// the cursor's whitelist (GDD R4 l.79 "a move from the cursor onto an occupied, non-mergeable slot swaps"; H-R4-2 d; story 008
/// Implementation Notes); otherwise it stays <c>blocked</c> (a grid destination, a split, a non-cursor source). A same-key full slot
/// is <c>stack_full</c> before this (R4 "onto a full same-StackKey slot → stack_full, no swap");</item>
/// <item><c>stack_full</c>, then <c>capacity_exhausted</c> on a merge: a placement onto a mergeable stack is an implicit merge of
/// <c>min(count, room)</c> units, room <c>min(effMax, 255) − target.count</c> (the remainder stays at the source with its id; a
/// split onto a stack is the same partial merge with no new entry; Mail Lead planning default pending Evan, TD-150), whose mail
/// union block <c>target.count + units</c> is planned with <see cref="InvPlan.AddAcquiredBlock"/> and must pass
/// <see cref="InvRequestCheck.CheckArenaReservation(in InvPlan)"/> (ADR-0009 sections 2 and 5);</item>
/// <item><c>capacity_exhausted</c> on a split onto free cells: the entry pool must have a free slot and an unissued id
/// (<see cref="EntryPool.LiveCount"/> below <see cref="EntryPool.Capacity"/>, <see cref="EntryPool.LastId"/> below
/// <see cref="EntryPool.MaxRealId"/>; Mail Lead planning default pending Evan), then a mail split's k-unit block must pass the
/// reservation. The code is ADR-0009's (section 2 reason codes, 85); refused, the entry stays where it is, on the cursor for a place
/// (Mail Lead planning default pending Evan, TD-152, for both halves; it follows the pattern of Evan's 2026-10-22 5:11 AM PT
/// ruling, which covers only a leashed letter staying on the cursor when the mail-id arena is full).</item>
/// </list>
/// <c>container_gone</c> stays declared but is unreachable at M-1a (no container is freed; a dead id fails <c>not_open</c> first;
/// TD-136). The commit, in one <c>Begin</c>/<c>End</c> bracket inside the drain's call scope, is one of: one
/// <see cref="InventoryMutator.Transfer"/> (across containers: <c>Remove</c> then <c>Place</c>; within one: the same-container path,
/// which raises nothing and marks nothing for a move onto the entry's own cells with the same rotation, so that no-op replies
/// <c>ok</c> with no bump and no event); one <see cref="InventoryMutator.MergeInto"/>; one <see cref="InventoryMutator.SplitAndPlace"/>
/// (the new entry's id is allocated there); or the swap's <c>Remove(displaced)</c>, <c>Transfer(cursor entry → slot)</c>,
/// <c>Place(displaced → cursor slot 0)</c>, in that order (<see cref="Occupancy"/> asserts on an occupied cell, so the slot is
/// cleared first). Every changed container bumps exactly once (a cursor → chest move bumps both; a swap bumps the cursor and the
/// hotbar once each: Mail Lead planning default pending Evan, the TD-150 item 3 reading of "one version bump"). Weight, the StackKey
/// index and the item counts are kept by those primitives. A reject mutates and raises nothing; once the containers are resolved its
/// reply carries their current versions. Allocation-free; never throws. Not thread-safe: Sim tick thread only.
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

        Commit(ref plan, op.Actor, ctx.Tick, ref extra);
        return Reply.Ok(in op, in extra);
    }

    /// <summary>
    /// The pure half: walks the chain (see the class remarks) and on a pass writes the plan — the primaries (source, destination), the
    /// touched set (source, then destination) and either the placement (with the split or the swap that goes with it) or the merge, with
    /// the arena blocks the commit acquires. Reads state only; writes nothing but <paramref name="plan"/>; allocates no id, draws no
    /// RNG, raises no event, touches no ledger. O(footprint cells).
    /// </summary>
    /// <param name="op">The drained op.</param>
    /// <param name="plan">The call's plan.</param>
    /// <returns>0, or the earliest failing reason.</returns>
    internal ushort Validate(in Op op, ref InvPlan plan)
    {
        PlayerSlot actor = _owner.ActorSlot(op.Actor);
        InvRequestCheck check = _owner.RequestCheck;
        ushort reason = check.CheckRequest(in op, actor); // bad_request: GDD l.103 chain head
        if (reason != 0)
        {
            return reason;
        }

        MovePayload p = op.PayloadAs<MovePayload>();
        var from = new ContainerId(p.From);
        var to = new ContainerId(p.To);
        if (!_owner.OpenSet.IsInOpenSet(actor, from) || !_owner.OpenSet.IsInOpenSet(actor, to))
        {
            return (ushort)InvReason.NotOpen; // GDD l.103: not_open after bad_request
        }

        // IsInOpenSet is false for a dead id, so both are live here.
        ContainerTable containers = _owner.Containers;
        _ = containers.TryGetSlot(from, out ushort fromSlot);
        _ = containers.TryGetSlot(to, out ushort toSlot);
        plan.SetPrimary(fromSlot, toSlot);
        reason = check.ResolveEntries(in op); // stale_entry: GDD l.103, after not_open
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
            return (ushort)InvReason.StaleEntry; // GDD l.103 stale_entry: a live entry not in from
        }

        if (p.Count > e.Count)
        {
            return (ushort)InvReason.BadCount; // GDD l.103 "> total for move"; story 008 AC Split-and-place; after stale_entry (story 007)
        }

        bool whole = p.Count == 0 || p.Count == e.Count; // ADR-0009 section 1 "count u8 (0 = whole)"; TD-150
        int requested = whole ? e.Count : p.Count;
        ref readonly ContainerHeader h = ref containers[toSlot];
        if (p.BaseVersion < h.LastReplaceVersion)
        {
            return (ushort)InvReason.StaleLayout; // GDD l.103: stale_layout after stale_entry
        }

        // reserved_slot → out_of_bounds → not_carryable → not_allowed → blocked / stack_full (GDD l.103); a split keeps its own cells (GDD l.79)
        bool rotated = p.Rotated != 0;
        PlacementSubject subject = PlacementSubject.Of(_content, _stats, in e);
        PlacementVerdict v = PlacementRules.Validate(
            _owner.Occupancy, entries, toSlot, in h, _content.Containers[h.DefIndex], in subject, p.X, p.Y, rotated, entrySlot, wholeMove: whole);
        if (!v.IsOk)
        {
            return v.Reason == InvReason.Blocked && whole && TryPlanSwap(actor, from, fromSlot, toSlot, in h, entrySlot, p.X, p.Y, ref plan)
                ? (ushort)0
                : (ushort)v.Reason;
        }

        // A fresh plan holds InvPlan.MaxTouched (4) containers, so these two touches always succeed.
        _ = plan.Touch(fromSlot);
        _ = plan.Touch(toSlot);
        if (!v.IsMerge)
        {
            // PlacementRules proved (x, y) inside the destination, whose width and rows fit a byte.
            plan.Placement = new InvPlacement(entrySlot, toSlot, (byte)p.X, (byte)p.Y, rotated);
            if (whole)
            {
                return 0;
            }

            // A split onto free cells: Commit allocates one entry and, for mail, one k-unit block, so both are checked here (story 008
            // AC Split-and-place; Mail Lead planning default pending Evan for the pool; ADR-0009 section 2 for the reservation).
            plan.Split = new InvSplit((byte)p.Count);
            if (entries.LiveCount >= entries.Capacity || entries.LastId >= EntryPool.MaxRealId)
            {
                return (ushort)InvReason.CapacityExhausted;
            }

            if (e.IsMail)
            {
                _ = plan.AddAcquiredBlock(p.Count); // the new entry's block; one block never overflows a fresh plan
            }

            return check.CheckArenaReservation(in plan);
        }

        // An item's maxStack may exceed 255 (no content rule caps it), but an entry's count is a u8: room is capped there.
        int targetCount = entries[v.TargetSlot].Count;
        int room = Math.Min(v.EffMax, byte.MaxValue) - targetCount;
        if (room <= 0)
        {
            return (ushort)InvReason.StackFull; // GDD l.103 stack_full: a mergeable target with no room under the 255 cap
        }

        int units = Math.Min(requested, room); // a split onto a stack is a partial merge of min(count, room) (story 008 AC Split-and-place)
        plan.Merge = new InvMerge(entrySlot, v.TargetSlot, (byte)units);
        if (e.IsMail)
        {
            _ = plan.AddAcquiredBlock(targetCount + units); // the union block; one block never overflows a fresh plan
        }

        return check.CheckArenaReservation(in plan); // capacity_exhausted (code 85, ADR-0009 section 2 reason codes; arena exhaustion, section 5) checked last: Mail Lead planning default pending Evan (TD-152)
    }

    /// <summary>
    /// The mutating half: applies <paramref name="plan"/>'s merge (<see cref="InventoryMutator.MergeInto"/>), split
    /// (<see cref="InventoryMutator.SplitAndPlace"/>), swap (<see cref="InventoryMutator.Remove"/> of the displaced entry,
    /// <see cref="InventoryMutator.Transfer"/> of the cursor entry, <see cref="InventoryMutator.Place"/> of the displaced entry on cursor
    /// slot 0) or placement (<see cref="InventoryMutator.Transfer"/>) unchanged in one <c>Begin</c>/<c>End</c> bracket (cause
    /// <see cref="InvCause.None"/>). Never fails, never recomputes; a no-op transfer seals nothing and <see cref="InventoryMutator.End"/>
    /// sets <c>unchanged</c>.
    /// </summary>
    /// <param name="plan">The plan <see cref="Validate"/> passed.</param>
    /// <param name="actor">The acting actor (written into every seal).</param>
    /// <param name="tick">The current tick (a split's check records).</param>
    /// <param name="extra">The reply extra <see cref="InventoryMutator.End"/> fills.</param>
    internal void Commit(ref InvPlan plan, ActorId actor, uint tick, ref InvReplyExtra extra)
    {
        InventoryMutator m = _owner.Mutator;
        m.Begin(ref plan, actor, InvCause.None);
        InvPlacement to = plan.Placement;
        if (plan.Merge.IsSet)
        {
            InvMerge merge = plan.Merge;
            m.MergeInto(ref plan, merge.SourceSlot, merge.TargetSlot, merge.Units);
        }
        else if (plan.Split.IsSet)
        {
            _ = m.SplitAndPlace(ref plan, to.EntrySlot, plan.Split.Units, to.ContainerSlot, to.X, to.Y, to.Rotated, tick);
        }
        else if (plan.Swap.IsSet)
        {
            InvSwap swap = plan.Swap;
            m.Remove(ref plan, swap.DisplacedSlot);
            m.Transfer(ref plan, to.EntrySlot, to.ContainerSlot, to.X, to.Y, to.Rotated);
            m.Place(ref plan, swap.DisplacedSlot, swap.CursorSlot, 0, 0, false);
        }
        else
        {
            m.Transfer(ref plan, to.EntrySlot, to.ContainerSlot, to.X, to.Y, to.Rotated);
        }

        m.End(ref plan, ref extra);
    }

    /// <summary>
    /// The swap step of a <c>blocked</c> whole move (see the class remarks): when <paramref name="from"/> is the actor's cursor, the
    /// destination has the slots layout (so one other entry holds the slot), and that entry passes the cursor's whitelist
    /// (<see cref="InsertionWhitelist.Check(ContainerDef, in PlacementSubject)"/>), writes the plan — both containers touched (cursor,
    /// then hotbar), the cursor entry's placement on the slot and the swap — and returns true. Otherwise writes nothing and returns
    /// false, so the move stays <c>blocked</c>. The cursor is one slot holding only the moving entry, so its slot 0 is free once that
    /// entry has left. Pure.
    /// </summary>
    private bool TryPlanSwap(
        PlayerSlot actor, ContainerId from, ushort fromSlot, ushort toSlot, in ContainerHeader h, ushort entrySlot, int x, int y, ref InvPlan plan)
    {
        if (h.Layout != ContainerLayout.Slots || from != _owner.CursorOf(actor))
        {
            return false; // no grid swap in v1, and only the cursor swaps (GDD R4 l.79; H-R4-2 d)
        }

        // reserved_slot and out_of_bounds came first, so (x, y) is an in-range slot; a slot is one 1 x 1 cell and the mover is in the
        // cursor, so a blocked verdict there means exactly one other entry holds it (PlacementRules' overlap scan).
        ushort displaced = _owner.Occupancy.OwnerAt(toSlot, h.W, x, y);
        ContainerDef cursorDef = _content.Containers[_owner.Containers[fromSlot].DefIndex];
        PlacementSubject displacedSubject = PlacementSubject.Of(_content, _stats, in _owner.Entries[displaced]);
        if (InsertionWhitelist.Check(cursorDef, in displacedSubject) != 0)
        {
            return false; // the displaced entry may not go to the cursor: the move stays blocked (story 008 Implementation Notes)
        }

        _ = plan.Touch(fromSlot);
        _ = plan.Touch(toSlot);
        plan.Placement = new InvPlacement(entrySlot, toSlot, (byte)x, (byte)y, false);
        plan.Swap = new InvSwap(displaced, fromSlot);
        return true;
    }
}
