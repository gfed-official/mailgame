using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Ops;

/// <summary>
/// The production <c>QuickMove</c> handler, entry form (story grid-inventory/010; GDD R7 <c>quick_move {from, entryId, count?}</c>
/// l.93, R8 AutoPlace, R9 target order l.120-128; ADR-0009 sections 1-3): AutoPlaces the entry, or its <c>count</c> oldest units, into
/// the R9 row for <c>from</c> (<see cref="TargetOrder.QuickMoveRow"/>; <c>from</c> = the cursor is the cursor row). Stackables may go
/// partly (the newest units stay at the source with its id and cells); a non-stackable is all-or-nothing (R8). <see cref="Apply"/> is
/// <see cref="Validate"/>, then on 0 <see cref="Commit"/>, then the reply. The whole chain, <c>bad_request → not_open → stale_entry →
/// bad_count → not_carryable → not_allowed → no_room → capacity_exhausted</c>, is the Mail Lead planning default pending Evan
/// (TD-153). No source states a QuickMove precedence: ADR-0009 section 2's placement chain (l.113) lists Move, MoveGroup, Return target
/// and swap, not QuickMove, and the GDD's precedence (R7 l.103) is "for any placement". The <c>bad_request → not_open → stale_entry</c>
/// and <c>not_carryable → not_allowed</c> positions are therefore Mail Lead default (TD-153), by analogy with the placement chain (GDD
/// l.103; ADR-0009 l.113 lists Move, MoveGroup, Return target, swap); only <c>not_allowed</c> before <c>no_room</c> is stated for
/// AutoPlace (R8 l.115), and the other positions are Mail Lead default (TD-153). Every code is ADR-0009's (section 2 reason-code list,
/// l.121):
/// <list type="number">
/// <item><c>bad_request</c> (Core's 8), the head (Mail Lead default (TD-153), by analogy with the placement chain):
/// <see cref="InvRequestCheck.CheckRequest"/> with the actor's slot (from <see cref="Op.Actor"/>, never the payload);</item>
/// <item><c>not_open</c> (64), after <c>bad_request</c> (Mail Lead default (TD-153), by analogy with the placement chain): <c>from</c> is
/// not in the actor's open set (<see cref="OpenSet.IsInOpenSet"/>; the System actor has an empty set), or the actor's hotbar is not (a
/// joined slot whose join found the container table full has no panel, which every R9 row but the panel → external one targets; as
/// <see cref="PickHandler"/>'s cursor clause);</item>
/// <item><c>stale_entry</c> (67), after <c>not_open</c> (Mail Lead default (TD-153), by analogy with the placement chain):
/// <see cref="InvRequestCheck.ResolveEntries"/>, then a live entry that is not in <c>from</c>;</item>
/// <item><c>bad_count</c> (78), after <c>stale_entry</c> (Mail Lead default, TD-153): <c>count</c> above the entry's total. <c>count</c> 0
/// or the total is the whole entry (ADR-0009 l.347, the QuickMove payload erratum, "count u8 (0 = whole)"), <c>1 ≤ count &lt; total</c>
/// splits the count oldest units off (R6; Mail Lead planning default pending Evan (TD-153), item 1, mirroring Move, TD-150);</item>
/// <item><c>not_carryable</c> (74), after <c>bad_count</c> (Mail Lead default, TD-153), then <c>not_allowed</c> (73) (Mail Lead default
/// (TD-153), by analogy with the placement chain), then <c>no_room</c> (77) (R8 l.115 "Nothing moved: if no target admitted the entry's
/// category → not_allowed; else → no_room"): one <see cref="AutoPlace.Plan"/> over the row, which answers <c>not_carryable</c> before
/// <c>not_allowed</c> when no target admits the entry and <c>no_room</c> when nothing moved. <c>not_carryable</c> is dormant at M-1a: no v1
/// item is <c>carryByHand: false</c>, so the handler has no clause of its own for it and the code is undeclared on
/// <see cref="InvOpDescriptors.QuickMove"/> (TD-105 item 4). An outfeed target (the Intake) is planned <see cref="MergePolicy.NoMerge"/>
/// (R14 l.141, "every insertion into the Intake is NoMerge"), and the placement stamps a new <c>arrivalSeq</c>;</item>
/// <item><c>capacity_exhausted</c> (85), after <c>no_room</c> (Mail Lead default, TD-153; last, as in <see cref="MoveHandler"/>): a split
/// onto free cells needs a free pool slot and an unissued id, and every arena block the commit acquires (each mail merge's union, a mail
/// split's k-unit block) must pass <see cref="InvRequestCheck.CheckArenaReservation(in InvPlan)"/>. Refused, the entry stays where it is
/// (Mail Lead planning default pending Evan (TD-153); it follows the pattern of Evan's 2026-10-22 5:11 AM PT ruling, which covers only a
/// leashed letter staying on the cursor when the mail-id arena is full, even with a free cell).</item>
/// </list>
/// <c>container_gone</c> stays declared but is unreachable at M-1a (no container is freed; a dead id fails <c>not_open</c> first, Move's
/// order, TD-152; TD-136). The commit, in one <c>Begin</c>/<c>End</c> bracket inside the drain's call scope, is
/// <see cref="AutoPlace.Commit(ref InvPlan, in AutoPlacePlan)"/> for a whole entry (merges, then a <c>Transfer</c> of the rest) or
/// <see cref="AutoPlace.CommitSplit"/> for a part (merges from the source, then <see cref="InventoryMutator.SplitAndPlace"/>, which
/// allocates the new entry id). Every changed container bumps exactly once; weight, the StackKey index and the item counts are kept by
/// those primitives. The reply carries <c>requested</c>/<c>got</c> units; <c>ResultVersionA</c> is <c>from</c>, <c>ResultVersionB</c>
/// the row's first target on a reject (once the entry is resolved) and the first container that received units on a pass. A reject
/// mutates and raises nothing (not even a no-fit fact). Allocation-free; never throws. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>builder.BindOp(InvOpDescriptors.QuickMove, new QuickMoveHandler(inventory, content, stats));</code></example>
internal sealed class QuickMoveHandler : IOpHandler
{
    private readonly InventorySystem _owner;
    private readonly ContentSet _content;
    private readonly IStatSheet _stats;

    /// <summary>Creates the handler over the module's storage, open set, AutoPlace and mutator.</summary>
    /// <param name="owner">The inventory module.</param>
    /// <param name="content">The content set (the entry's def for its footprint, category and <c>effMax</c>).</param>
    /// <param name="stats">The stat sheet (mail <c>effMax</c>, read at op time).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public QuickMoveHandler(InventorySystem owner, ContentSet content, IStatSheet stats)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        _owner = owner;
        _content = content;
        _stats = stats;
    }

    /// <inheritdoc />
    public OpKind Kind => InvOpKinds.QuickMove;

    /// <inheritdoc />
    public Reply Apply(in Op op, in TickContext ctx)
    {
        var plan = new InvPlan();
        var extra = new InvReplyExtra();
        ushort reason = Validate(in op, ref plan, out AutoPlacePlan ap, out Decision decision);
        if (reason != 0)
        {
            _owner.Mutator.FillVersions(in plan, ref extra);
            Reply rejected = Reply.Reject(in op, reason);
            rejected.SetExtra(in extra);
            return rejected;
        }

        Commit(ref plan, in ap, in decision, op.Actor, ctx.Tick, ref extra);
        return Reply.Ok(in op, in extra);
    }

    /// <summary>
    /// The pure half: walks the chain (see the class remarks) and on a pass writes the plan — the primaries, the touched set (source,
    /// then each target the AutoPlace uses) and the arena blocks the commit acquires — the AutoPlace result and the decision (entry, whole
    /// or part, units requested). Reads state only; allocates no id, draws no RNG, raises no event, touches no ledger, records no no-fit
    /// fact. One <see cref="AutoPlace.Plan"/> (H-PERF-6 probe counters, never reset).
    /// </summary>
    /// <param name="op">The drained op.</param>
    /// <param name="plan">The call's plan.</param>
    /// <param name="ap">The AutoPlace result <see cref="Commit"/> applies.</param>
    /// <param name="decision">The entry and the units requested.</param>
    /// <returns>0, or the earliest failing reason.</returns>
    internal ushort Validate(in Op op, ref InvPlan plan, out AutoPlacePlan ap, out Decision decision)
    {
        ap = default;
        decision = default;
        PlayerSlot actor = _owner.ActorSlot(op.Actor);
        InvRequestCheck check = _owner.RequestCheck;
        ushort reason = check.CheckRequest(in op, actor); // bad_request (8): the chain head (Mail Lead default, TD-153; by analogy with the placement chain)
        if (reason != 0)
        {
            return reason;
        }

        QuickMovePayload p = op.PayloadAs<QuickMovePayload>();
        var from = new ContainerId(p.From);
        ContainerId hotbar = _owner.HotbarOf(actor);
        if (!_owner.OpenSet.IsInOpenSet(actor, from) || !_owner.OpenSet.IsInOpenSet(actor, hotbar))
        {
            return (ushort)InvReason.NotOpen; // not_open (64) after bad_request (Mail Lead default, TD-153; by analogy with the placement chain); before container_gone, Move's order (TD-152)
        }

        reason = check.ResolveEntries(in op); // stale_entry (67) after not_open (Mail Lead default, TD-153; by analogy with the placement chain)
        if (reason != 0)
        {
            return reason;
        }

        // IsInOpenSet is false for a dead id, and a panel's three containers are created together, so every lookup below succeeds.
        ContainerTable containers = _owner.Containers;
        EntryPool entries = _owner.Entries;
        _ = containers.TryGetSlot(from, out ushort fromSlot);
        _ = containers.TryGetSlot(hotbar, out ushort hotbarSlot);
        _ = containers.TryGetSlot(_owner.PlayerInventoryOf(actor), out ushort inventorySlot);
        _ = entries.TryGetSlot(EntryId.Real(p.EntryId), out ushort entrySlot);
        ref readonly EntryRecord e = ref entries[entrySlot];
        if (e.ContainerSlot != fromSlot)
        {
            return (ushort)InvReason.StaleEntry; // stale_entry (67): a live entry not in from (Mail Lead default, TD-153; by analogy with the placement chain)
        }

        PlacementSubject subject = PlacementSubject.Of(_content, _stats, in e);
        Span<ushort> targets = stackalloc ushort[TargetOrder.MaxRowLength];
        int rowLength = Row(actor, from, fromSlot, hotbar, hotbarSlot, inventorySlot, subject.Category, targets);
        ReadOnlySpan<ushort> row = targets[..rowLength];
        plan.SetPrimary(fromSlot, row[0]);
        if (p.Count > e.Count)
        {
            return (ushort)InvReason.BadCount; // bad_count (78) after stale_entry: Mail Lead default (TD-153); the code is TD-153 item 1
        }

        bool whole = p.Count == 0 || p.Count == e.Count; // ADR-0009 l.347 (QuickMove payload erratum) "count u8 (0 = whole)"; TD-153 item 1
        decision = new Decision(entrySlot, !whole, whole ? e.Count : p.Count);
        MergePolicy policy = HasOutfeedTarget(row) ? MergePolicy.NoMerge : MergePolicy.Merge; // R14 l.141: every Intake insertion is NoMerge
        if (!whole)
        {
            _ = plan.Touch(fromSlot); // the split's source changes first; a NoSource plan does not touch it (a fresh plan holds 4)
        }

        // not_carryable (74) after bad_count: Mail Lead default (TD-153); not_allowed (73) after not_carryable: Mail Lead default
        // (TD-153), by analogy with the placement chain (GDD l.103; ADR-0009 l.113 lists Move, MoveGroup, Return target, swap); no_room
        // (77) after not_allowed (R8 l.115). A part is a NoSource plan of count units (AutoPlace places a source entry
        // only whole).
        reason = _owner.AutoPlace.Plan(
            ref plan, in subject, decision.Requested, whole ? entrySlot : AutoPlace.NoSource, row, policy, PlaceOrder.PassFirst, out ap);
        if (reason != 0)
        {
            return reason;
        }

        plan.SetPrimary(fromSlot, ap.MergeCount > 0 ? entries[ap.Merge(0).TargetEntrySlot].ContainerSlot : ap.Placement.ContainerSlot);
        if (!whole && ap.Placement.IsSet)
        {
            // The split onto free cells allocates one entry and, for mail, one block: both are capacity_exhausted (85), after no_room
            // with the reservation (Mail Lead default, TD-153; MoveHandler's split pre-check).
            if (entries.LiveCount >= entries.Capacity || entries.LastId >= EntryPool.MaxRealId)
            {
                return (ushort)InvReason.CapacityExhausted;
            }

            if (e.IsMail)
            {
                _ = plan.AddAcquiredBlock(ap.PlacementUnits); // the new entry's block, after the merges' unions (commit order)
            }
        }

        return check.CheckArenaReservation(in plan); // capacity_exhausted (85) after no_room, last: Mail Lead default (TD-153), as MoveHandler (TD-152)
    }

    /// <summary>
    /// The mutating half: applies <paramref name="ap"/> unchanged in one <c>Begin</c>/<c>End</c> bracket (cause
    /// <see cref="InvCause.None"/>) — <see cref="AutoPlace.Commit(ref InvPlan, in AutoPlacePlan)"/> for a whole entry,
    /// <see cref="AutoPlace.CommitSplit"/> for a part — then writes <c>requested</c>/<c>got</c>. Never fails, never recomputes.
    /// </summary>
    /// <param name="plan">The plan <see cref="Validate"/> passed.</param>
    /// <param name="ap">The AutoPlace result <see cref="Validate"/> planned.</param>
    /// <param name="decision">The entry and the units requested.</param>
    /// <param name="actor">The acting actor (written into every seal).</param>
    /// <param name="tick">The current tick (a split's check records).</param>
    /// <param name="extra">The reply extra <see cref="InventoryMutator.End"/> fills.</param>
    internal void Commit(ref InvPlan plan, in AutoPlacePlan ap, in Decision decision, ActorId actor, uint tick, ref InvReplyExtra extra)
    {
        InventoryMutator m = _owner.Mutator;
        m.Begin(ref plan, actor, InvCause.None);
        if (decision.Partial)
        {
            _owner.AutoPlace.CommitSplit(ref plan, in ap, decision.EntrySlot, tick);
        }
        else
        {
            _owner.AutoPlace.Commit(ref plan, in ap);
        }

        m.End(ref plan, ref extra);
        extra.SetUnits(decision.Requested, ap.MovedUnits);
    }

    /// <summary>The R9 row for <paramref name="from"/> (its role relative to the actor, then <see cref="TargetOrder.QuickMoveRow"/>). Pure.</summary>
    private int Row(
        PlayerSlot actor, ContainerId from, ushort fromSlot, ContainerId hotbar, ushort hotbarSlot, ushort inventorySlot, ItemCategory category, Span<ushort> targets)
    {
        QuickMoveSource source = from == hotbar ? QuickMoveSource.Hotbar
            : from == _owner.PlayerInventoryOf(actor) ? QuickMoveSource.Inventory
            : from == _owner.CursorOf(actor) ? QuickMoveSource.Cursor
            : QuickMoveSource.External; // in the open set and not the actor's panel or cursor: ownerless, the actor's external
        bool externalOpen = _owner.Containers.TryGetSlot(_owner.OpenSet.ExternalOf(actor), out ushort externalSlot);
        return TargetOrder.QuickMoveRow(source, category, externalOpen, hotbarSlot, inventorySlot, externalOpen ? externalSlot : fromSlot, targets);
    }

    private bool HasOutfeedTarget(ReadOnlySpan<ushort> row)
    {
        for (int i = 0; i < row.Length; i++)
        {
            if (_owner.Mutator.HasOutfeed(row[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What <see cref="Validate"/> decided beside the plans: the entry, whether only part of it moves, and the units requested.</summary>
    internal readonly struct Decision
    {
        /// <summary>The entry's pool slot.</summary>
        public readonly ushort EntrySlot;

        /// <summary>True for <c>1 ≤ count &lt; total</c> (a split).</summary>
        public readonly bool Partial;

        /// <summary>The units requested (the whole count, or <c>count</c>).</summary>
        public readonly int Requested;

        /// <summary>Creates a decision.</summary>
        /// <param name="entrySlot">The entry's pool slot.</param>
        /// <param name="partial">True for a split.</param>
        /// <param name="requested">The units requested.</param>
        public Decision(ushort entrySlot, bool partial, int requested)
        {
            EntrySlot = entrySlot;
            Partial = partial;
            Requested = requested;
        }
    }
}
