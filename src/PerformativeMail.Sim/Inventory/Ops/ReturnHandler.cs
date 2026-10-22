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
/// The production <c>Return</c> handler (story grid-inventory/010; GDD R7 <c>return {}</c> l.95, the cursor state table l.178, H-R7-9
/// l.706; ADR-0009 sections 1-3): puts the actor's cursor entry back "into its origin cells if they are still free (or mergeable), else
/// AutoPlace into the panel (R9 "put away"), else the cursor keeps it (<c>no_room</c>)", and never drops it to the world (l.95: no
/// WorldItem is created on any path). <see cref="Apply"/> is <see cref="Validate"/>, then on 0 <see cref="Commit"/>, then the reply. The
/// chain is <c>bad_request → cursor_empty → [origin leg] → [panel leg] → no_room → capacity_exhausted</c>; every code is ADR-0009's
/// (section 2 reason-code list, l.121):
/// <list type="number">
/// <item><c>bad_request</c> (Core's 8), the head (GDD l.103 "bad_request is returned for any malformed op"; ADR-0009 l.113, the placement
/// chain's head, which lists the <c>Return</c> target): <see cref="InvRequestCheck.CheckRequest"/>, a non-zero payload length. Through the
/// drain, Sim Core's descriptor-length check answers such an op <c>unknown_kind</c> before any handler runs, so this is the shared step 1
/// every inventory handler calls, reached only by a direct call;</item>
/// <item><c>cursor_empty</c> (70), after <c>bad_request</c> (Mail Lead default, TD-153): the actor's cursor holds no entry (the System
/// actor and a joined slot whose join found the container table full have no cursor, so theirs is empty too);</item>
/// <item>the origin leg, after <c>cursor_empty</c> (Mail Lead default, TD-153: GDD l.95 does not mention <c>cursor_empty</c>; the leg
/// itself and its order before the panel leg are GDD l.95, "into its origin cells if they are still free (or mergeable)"): it runs only
/// when an origin is recorded for the cursor's entry (<see cref="InventorySystem.TryGetCursorOrigin"/>) and its container is in the
/// actor's open set (<see cref="OpenSet.IsInOpenSet"/>, false for a dead id). It is the placement chain at the recorded cells
/// (<see cref="PlacementRules.Validate"/> with <c>wholeMove: false</c>; ADR-0009 l.113, the "<c>Return</c> target"): a free fit places the
/// whole entry; a mergeable stack takes <c>min(count, room)</c>, room <c>min(effMax, 255) − target.count</c> (the u8 cap, as
/// <see cref="MoveHandler"/>), unless the origin is an outfeed container (the Intake: R14 l.141, "every insertion into the Intake is
/// NoMerge"). Anything else — a reserved slot, out of bounds, not allowed, blocked, a full stack, an Intake stack, no origin, an origin
/// not in the open set — falls through to the panel leg and is never a reject (Mail Lead planning default pending Evan (TD-153), item
/// 2), so <c>not_open</c> is never answered and <c>blocked</c>/<c>stack_full</c> stay declared on <see cref="InvOpDescriptors.Return"/>
/// but unreachable;</item>
/// <item>the panel leg, after the origin leg (GDD l.95, "else AutoPlace into the panel (R9 "put away")"): <see cref="AutoPlace.Plan"/>
/// over <see cref="TargetOrder.CursorRow"/> (merge pass first, pass-first, as the leash put-away) for what the origin leg left. After a
/// partial origin merge it plans the remaining units in the same op (<see cref="AutoPlace.PlanRemainder"/>, skipping the origin stack;
/// Mail Lead planning default pending Evan (TD-153), item 3);</item>
/// <item><c>no_room</c> (77), after the panel leg (GDD l.95, "else the cursor keeps it (no_room)"; l.178): no unit moved on either leg.
/// When some moved, the rest stays on the cursor with its entryId (and so its origin) and the reply is <c>ok</c> with requested/got;</item>
/// <item><c>capacity_exhausted</c> (85), last (Mail Lead default, TD-153; as <see cref="MoveHandler"/> and <see cref="QuickMoveHandler"/>):
/// every arena block the commit acquires (each mail merge's union, at the origin and in the panel) must pass
/// <see cref="InvRequestCheck.CheckArenaReservation(in InvPlan)"/>. Refused, the entry stays on the cursor (Mail Lead planning default
/// pending Evan (TD-153); it follows the pattern of Evan's 2026-10-22 5:11 AM PT ruling, which covers only a leashed letter staying on
/// the cursor when the mail-id arena is full, even with a free cell).</item>
/// </list>
/// The commit, in one <c>Begin</c>/<c>End</c> bracket inside the drain's call scope, is one <see cref="InventoryMutator.Transfer"/> to the
/// origin, or an origin <see cref="InventoryMutator.MergeInto"/> and/or <see cref="AutoPlace.Commit(ref InvPlan, in AutoPlacePlan)"/>, so
/// the cursor and each container that received units bump exactly once (H-R7-9: an origin place is the origin +1 and the cursor +1).
/// Weight, the StackKey index and the item counts are kept by those primitives. <c>ResultVersionA</c> is the cursor; <c>ResultVersionB</c>
/// the first container that received units on a pass and the cursor row's first target on a <c>no_room</c> or <c>capacity_exhausted</c>
/// reject; a <c>bad_request</c> or <c>cursor_empty</c> reject resolves nothing (0, 0). A reject mutates and raises nothing (not even a
/// no-fit fact). Allocation-free; never throws. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>builder.BindOp(InvOpDescriptors.Return, new ReturnHandler(inventory, content, stats));</code></example>
internal sealed class ReturnHandler : IOpHandler
{
    private readonly InventorySystem _owner;
    private readonly ContentSet _content;
    private readonly IStatSheet _stats;

    /// <summary>Creates the handler over the module's storage, open set, cursor origins, AutoPlace and mutator.</summary>
    /// <param name="owner">The inventory module.</param>
    /// <param name="content">The content set (container defs; the entry's def for its footprint, category and <c>effMax</c>).</param>
    /// <param name="stats">The stat sheet (mail <c>effMax</c>, read at op time).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ReturnHandler(InventorySystem owner, ContentSet content, IStatSheet stats)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        _owner = owner;
        _content = content;
        _stats = stats;
    }

    /// <inheritdoc />
    public OpKind Kind => InvOpKinds.Return;

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

        Commit(ref plan, in ap, in decision, op.Actor, ref extra);
        return Reply.Ok(in op, in extra);
    }

    /// <summary>
    /// The pure half: walks the chain (see the class remarks) and on a pass writes the plan — the primaries, the touched set (cursor,
    /// then the origin, then each panel target the AutoPlace uses), the origin placement or merge and the arena blocks the commit
    /// acquires — the panel leg's AutoPlace result and the decision. Reads state only; allocates no id, draws no RNG, raises no event,
    /// touches no ledger, records no no-fit fact. O(footprint) for the origin leg plus at most one AutoPlace (H-PERF-6 probe counters,
    /// never reset).
    /// </summary>
    /// <param name="op">The drained op.</param>
    /// <param name="plan">The call's plan.</param>
    /// <param name="ap">The panel leg's AutoPlace result (default when the origin leg moved everything).</param>
    /// <param name="decision">The entry, the units requested and moved, and whether the panel leg commits.</param>
    /// <returns>0, or the earliest failing reason.</returns>
    internal ushort Validate(in Op op, ref InvPlan plan, out AutoPlacePlan ap, out Decision decision)
    {
        ap = default;
        decision = default;
        PlayerSlot actor = _owner.ActorSlot(op.Actor);
        InvRequestCheck check = _owner.RequestCheck;
        ushort reason = check.CheckRequest(in op, actor); // bad_request (8): the chain head (GDD l.103; ADR-0009 l.113)
        if (reason != 0)
        {
            return reason;
        }

        ContainerTable containers = _owner.Containers;
        if (!containers.TryGetSlot(_owner.CursorOf(actor), out ushort cursorSlot) || containers[cursorSlot].EntryCount == 0)
        {
            return (ushort)InvReason.CursorEmpty; // cursor_empty (70) after bad_request: Mail Lead default (TD-153)
        }

        // A panel's three containers are created together (OnPlayerJoined), so a live cursor has a live hotbar and player_inventory;
        // the cursor is one slot, so its entry is the owner of (0, 0).
        EntryPool entries = _owner.Entries;
        _ = containers.TryGetSlot(_owner.HotbarOf(actor), out ushort hotbarSlot);
        _ = containers.TryGetSlot(_owner.PlayerInventoryOf(actor), out ushort inventorySlot);
        ushort entrySlot = _owner.Occupancy.OwnerAt(cursorSlot, containers[cursorSlot].W, 0, 0);
        ref readonly EntryRecord e = ref entries[entrySlot];
        PlacementSubject subject = PlacementSubject.Of(_content, _stats, in e);
        Span<ushort> row = stackalloc ushort[TargetOrder.CursorRowLength];
        TargetOrder.CursorRow(subject.Category, hotbarSlot, inventorySlot, row);
        plan.SetPrimary(cursorSlot, row[0]);
        _ = plan.Touch(cursorSlot); // a fresh plan holds 4: the cursor, the origin and the two panel containers

        // The origin leg (GDD l.95): a free fit or a merge planned here; anything else falls through (TD-153 item 2).
        int originUnits = PlanOrigin(actor, entrySlot, in e, in subject, ref plan, out ushort receiver);
        bool panelLeg = originUnits < e.Count;
        if (panelLeg)
        {
            // The panel leg (GDD l.95, R9 cursor row; merge pass first, pass-first, as the leash put-away). Any AutoPlace refusal is
            // no_room (77) once nothing moved on either leg, the one code GDD l.95 gives this leg ("else the cursor keeps it
            // (no_room)"; l.178): in practice AutoPlace's own no_room, since the panel admits every v1 category (not_carryable is
            // dormant, TD-105 item 4).
            reason = originUnits == 0
                ? _owner.AutoPlace.Plan(ref plan, in subject, e.Count, entrySlot, row, MergePolicy.Merge, PlaceOrder.PassFirst, out ap)
                : _owner.AutoPlace.PlanRemainder(
                    ref plan, in subject, e.Count - originUnits, entrySlot, plan.Merge.TargetSlot, row, MergePolicy.Merge, PlaceOrder.PassFirst, out ap);
            if (reason != 0 && originUnits == 0)
            {
                return (ushort)InvReason.NoRoom;
            }

            receiver = originUnits > 0 ? receiver : ReceiverOf(in ap);
        }

        // capacity_exhausted (85) after no_room, last: Mail Lead default (TD-153), as MoveHandler and QuickMoveHandler; refused, the entry
        // stays on the cursor.
        int moved = originUnits + (reason == 0 ? ap.MovedUnits : 0);
        decision = new Decision(entrySlot, e.Count, moved, panelLeg && reason == 0);
        reason = check.CheckArenaReservation(in plan);
        if (reason == 0)
        {
            plan.SetPrimary(cursorSlot, receiver);
        }

        return reason;
    }

    /// <summary>
    /// The mutating half: applies the plan unchanged in one <c>Begin</c>/<c>End</c> bracket (cause <see cref="InvCause.None"/>) — the
    /// origin <see cref="InventoryMutator.Transfer"/>, or the origin <see cref="InventoryMutator.MergeInto"/> (the entry's oldest units)
    /// and then the panel leg's <see cref="AutoPlace.Commit(ref InvPlan, in AutoPlacePlan)"/> — then writes <c>requested</c>/<c>got</c>.
    /// Never fails, never recomputes, never creates a WorldItem.
    /// </summary>
    /// <param name="plan">The plan <see cref="Validate"/> passed.</param>
    /// <param name="ap">The panel leg's AutoPlace result.</param>
    /// <param name="decision">The entry, the units requested and moved, and whether the panel leg commits.</param>
    /// <param name="actor">The acting actor (written into every seal).</param>
    /// <param name="extra">The reply extra <see cref="InventoryMutator.End"/> fills.</param>
    internal void Commit(ref InvPlan plan, in AutoPlacePlan ap, in Decision decision, ActorId actor, ref InvReplyExtra extra)
    {
        InventoryMutator m = _owner.Mutator;
        m.Begin(ref plan, actor, InvCause.None);
        InvPlacement to = plan.Placement;
        if (to.IsSet)
        {
            m.Transfer(ref plan, to.EntrySlot, to.ContainerSlot, to.X, to.Y, to.Rotated);
        }
        else if (plan.Merge.IsSet)
        {
            InvMerge merge = plan.Merge;
            m.MergeInto(ref plan, merge.SourceSlot, merge.TargetSlot, merge.Units);
        }

        if (decision.PanelLeg)
        {
            _owner.AutoPlace.Commit(ref plan, in ap);
        }

        m.End(ref plan, ref extra);
        extra.SetUnits(decision.Requested, decision.Moved);
    }

    /// <summary>
    /// The origin leg (see the class remarks): plans a whole-entry placement or a merge at the recorded origin and returns the units it
    /// moves (0 when it falls through), touching the origin container and setting <paramref name="receiver"/> to it when it moves any.
    /// Pure.
    /// </summary>
    private int PlanOrigin(PlayerSlot actor, ushort entrySlot, in EntryRecord e, in PlacementSubject subject, ref InvPlan plan, out ushort receiver)
    {
        receiver = 0;
        ContainerTable containers = _owner.Containers;
        if (!_owner.TryGetCursorOrigin(actor, out InventorySystem.CursorOrigin origin)
            || !_owner.OpenSet.IsInOpenSet(actor, origin.Container)
            || !containers.TryGetSlot(origin.Container, out ushort originSlot))
        {
            return 0; // no origin, or its container is not open (or gone): the panel leg (TD-153 item 2)
        }

        ref readonly ContainerHeader h = ref containers[originSlot];
        PlacementVerdict v = PlacementRules.Validate(
            _owner.Occupancy, _owner.Entries, originSlot, in h, _content.Containers[h.DefIndex], in subject, origin.X, origin.Y, origin.Rotated,
            entrySlot, wholeMove: false);
        if (!v.IsOk)
        {
            return 0; // reserved_slot / out_of_bounds / not_allowed / blocked: the panel leg (GDD l.95; TD-153 item 2)
        }

        if (!v.IsMerge)
        {
            _ = plan.Touch(originSlot);
            plan.Placement = new InvPlacement(entrySlot, originSlot, origin.X, origin.Y, origin.Rotated); // a free fit: the whole entry
            receiver = originSlot;
            return e.Count;
        }

        // An item's maxStack may exceed 255, but an entry's count is a u8: room is capped there (as MoveHandler). An Intake stack is
        // never merged into (R14 l.141).
        int targetCount = _owner.Entries[v.TargetSlot].Count;
        int room = Math.Min(v.EffMax, byte.MaxValue) - targetCount;
        if (room <= 0 || _owner.Mutator.HasOutfeed(originSlot))
        {
            return 0; // stack_full, or an outfeed origin: the panel leg (GDD l.95; TD-153 item 2)
        }

        int units = Math.Min(e.Count, room);
        _ = plan.Touch(originSlot);
        plan.Merge = new InvMerge(entrySlot, v.TargetSlot, (byte)units);
        if (e.IsMail)
        {
            _ = plan.AddAcquiredBlock(targetCount + units); // the union block; one block never overflows a fresh plan
        }

        receiver = originSlot;
        return units;
    }

    /// <summary>The first container the panel leg's plan puts units into (its first merge's, else its placement's).</summary>
    private ushort ReceiverOf(in AutoPlacePlan ap) =>
        ap.MergeCount > 0 ? _owner.Entries[ap.Merge(0).TargetEntrySlot].ContainerSlot : ap.Placement.ContainerSlot;

    /// <summary>What <see cref="Validate"/> decided beside the plans: the entry, the units requested and moved, and whether the panel leg commits.</summary>
    internal readonly struct Decision
    {
        /// <summary>The cursor entry's pool slot.</summary>
        public readonly ushort EntrySlot;

        /// <summary>The units requested (the cursor entry's whole count).</summary>
        public readonly int Requested;

        /// <summary>The units the origin and panel legs move together.</summary>
        public readonly int Moved;

        /// <summary>True when the panel leg's AutoPlace plan commits.</summary>
        public readonly bool PanelLeg;

        /// <summary>Creates a decision.</summary>
        /// <param name="entrySlot">The cursor entry's pool slot.</param>
        /// <param name="requested">The units requested.</param>
        /// <param name="moved">The units moved.</param>
        /// <param name="panelLeg">True when the panel leg commits.</param>
        public Decision(ushort entrySlot, int requested, int moved, bool panelLeg)
        {
            EntrySlot = entrySlot;
            Requested = requested;
            Moved = moved;
            PanelLeg = panelLeg;
        }
    }
}
