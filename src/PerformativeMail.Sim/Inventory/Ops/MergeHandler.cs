using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Ops;

/// <summary>
/// The production <c>Merge</c> handler (story grid-inventory/008; GDD R7 <c>merge {from, entryId, to, targetEntryId}</c>, l.90; R6; ADR-0009
/// sections 2-4): an explicit entry → entry merge of <c>min(count, room)</c> units, room <c>min(effMax, 255) − target.count</c>; the
/// remainder stays in place with its id; a source left at 0 is removed. <see cref="Apply"/> is <see cref="Validate"/>, then on 0
/// <see cref="Commit"/>, then the reply. Neither the GDD nor ADR-0009 fixes a merge chain; this one is the Mail Lead planning default
/// pending Evan (story 008 AC "Merge precedence"; GDD l.103 errata note (2); TD-152), with <c>container_gone</c> ahead of <c>not_open</c>
/// (GDD l.379, H-X-4 l.817: a viewer's next op on a destroyed container is <c>container_gone</c>):
/// <list type="number">
/// <item><c>bad_request</c>: <see cref="InvRequestCheck.CheckRequest"/> with the actor's slot (from <see cref="Op.Actor"/>);</item>
/// <item><c>container_gone</c>: <c>from</c> or <c>to</c> is an issued id (below the high-water) that is no longer live;</item>
/// <item><c>not_open</c>: <c>from</c> or <c>to</c> is not in the actor's open set (<see cref="OpenSet.IsInOpenSet"/>; the System
/// actor has an empty set);</item>
/// <item><c>stale_entry</c>: <see cref="InvRequestCheck.ResolveEntries"/> (the entry, then the target), then a live entry that is not
/// in <c>from</c>, then a live target that is not in <c>to</c>;</item>
/// <item><c>not_allowed</c>: <c>to</c>'s def has <c>acceptsInsert: false</c> or its <c>allowedCategories</c> omit the entry's category
/// (GDD R1, R5; <see cref="InsertionWhitelist"/>);</item>
/// <item><c>blocked</c>: the entry is the target (H-R4-3 c), the StackKeys differ (H-R7-3), or <c>effMax</c> is 1 (R3,
/// <see cref="PlacementRules.CanMerge"/>);</item>
/// <item><c>stack_full</c>: no room under <c>min(effMax, 255)</c> (an item's <c>maxStack</c> may exceed 255, but a count is a u8);</item>
/// <item><c>capacity_exhausted</c>: for mail, the union block <c>target.count + units</c>, planned with
/// <see cref="InvPlan.AddAcquiredBlock"/>, fails <see cref="InvRequestCheck.CheckArenaReservation(in InvPlan)"/> (the code is ADR-0009's,
/// section 2 and 5; refused, nothing moves).</item>
/// </list>
/// The commit is one <see cref="InventoryMutator.MergeInto"/> in one <c>Begin</c>/<c>End</c> bracket inside the drain's call scope
/// (records: the source's <c>InvRemoved</c>, then <c>InvMerged</c> with the union's runs, then one seal per touched container). Both
/// containers bump once; a same-container merge bumps once (<see cref="InvPlan.Touch"/> dedups). Weight, the StackKey index and the
/// item counts are kept by <see cref="InventoryMutator.MergeInto"/>. A reject mutates and raises nothing; once both containers have
/// passed <c>not_open</c> its reply carries their current versions (<c>from</c> in A, <c>to</c> in B). Allocation-free; never throws.
/// Not thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>builder.BindOp(InvOpDescriptors.Merge, new MergeHandler(inventory, content, stats));</code></example>
internal sealed class MergeHandler : IOpHandler
{
    private readonly InventorySystem _owner;
    private readonly ContentSet _content;
    private readonly IStatSheet _stats;

    /// <summary>Creates the handler over the module's storage, open set and mutator.</summary>
    /// <param name="owner">The inventory module.</param>
    /// <param name="content">The content set (container defs; the entry's def for its key and <c>effMax</c>).</param>
    /// <param name="stats">The stat sheet (mail <c>effMax</c>, read at op time).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public MergeHandler(InventorySystem owner, ContentSet content, IStatSheet stats)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        _owner = owner;
        _content = content;
        _stats = stats;
    }

    /// <inheritdoc />
    public OpKind Kind => InvOpKinds.Merge;

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
    /// The pure half: walks the chain (see the class remarks) and on a pass writes the plan — the primaries (<c>from</c>, <c>to</c>), the
    /// touched set (<c>from</c>, then <c>to</c>), the merge (source, target, units) and, for mail, the union block the commit acquires.
    /// Reads state only; writes nothing but <paramref name="plan"/>; allocates no id, draws no RNG, raises no event, touches no ledger. O(1).
    /// </summary>
    /// <param name="op">The drained op.</param>
    /// <param name="plan">The call's plan.</param>
    /// <returns>0, or the earliest failing reason.</returns>
    internal ushort Validate(in Op op, ref InvPlan plan)
    {
        PlayerSlot actor = _owner.ActorSlot(op.Actor);
        InvRequestCheck check = _owner.RequestCheck;
        ushort reason = check.CheckRequest(in op, actor); // bad_request: GDD l.103 chain head; story 008 AC Merge precedence
        if (reason != 0)
        {
            return reason;
        }

        MergePayload p = op.PayloadAs<MergePayload>();
        var from = new ContainerId(p.From);
        var to = new ContainerId(p.To);
        ContainerTable containers = _owner.Containers;
        if (!containers.TryGetSlot(from, out ushort fromSlot) || !containers.TryGetSlot(to, out ushort toSlot))
        {
            return (ushort)InvReason.ContainerGone; // GDD l.379, H-X-4 l.817: an issued id no longer live, before not_open (GDD l.103 errata (2))
        }

        if (!_owner.OpenSet.IsInOpenSet(actor, from) || !_owner.OpenSet.IsInOpenSet(actor, to))
        {
            return (ushort)InvReason.NotOpen; // GDD l.103: not_open after bad_request; story 008 AC Merge precedence "from and to"
        }

        plan.SetPrimary(fromSlot, toSlot);
        reason = check.ResolveEntries(in op); // stale_entry, the entry then the target: GDD l.103; story 008 AC Merge precedence
        if (reason != 0)
        {
            return reason;
        }

        // ResolveEntries passed, so both ids are live real ids.
        EntryPool entries = _owner.Entries;
        _ = entries.TryGetSlot(EntryId.Real(p.EntryId), out ushort entrySlot);
        _ = entries.TryGetSlot(EntryId.Real(p.TargetEntryId), out ushort targetSlot);
        ref readonly EntryRecord e = ref entries[entrySlot];
        ref readonly EntryRecord t = ref entries[targetSlot];
        if (e.ContainerSlot != fromSlot || t.ContainerSlot != toSlot)
        {
            return (ushort)InvReason.StaleEntry; // story 008 AC Merge precedence: an entry not in from, then a target not in to
        }

        PlacementSubject subject = PlacementSubject.Of(_content, _stats, in e);
        ContainerDef toDef = _content.Containers[containers[toSlot].DefIndex];
        if (!toDef.AcceptsInsert || !InsertionWhitelist.Admits(toDef, subject.Category))
        {
            return (ushort)InvReason.NotAllowed; // GDD R1 acceptsInsert, R5; story 008 AC Merge precedence: after stale_entry
        }

        if (entrySlot == targetSlot || !PlacementRules.CanMerge(subject.Key, StackKey.Of(in t), subject.EffMax, toDef, subject.Category))
        {
            return (ushort)InvReason.Blocked; // GDD l.90 "key mismatch or source = target → blocked"; R3 effMax 1; story 008 AC
        }

        // An item's maxStack may exceed 255 (no content rule caps it), but an entry's count is a u8: room is capped there (as MoveHandler).
        int room = Math.Min(subject.EffMax, byte.MaxValue) - t.Count;
        if (room <= 0)
        {
            return (ushort)InvReason.StackFull; // GDD l.90 "full target → stack_full"; story 008 AC Merge precedence
        }

        int units = Math.Min(e.Count, room); // GDD l.90: moves min(count, effMax − target.count)

        // A fresh plan holds InvPlan.MaxTouched (4) containers, so these two touches always succeed (one when from == to).
        _ = plan.Touch(fromSlot);
        _ = plan.Touch(toSlot);
        plan.Merge = new InvMerge(entrySlot, targetSlot, (byte)units);
        if (e.IsMail)
        {
            _ = plan.AddAcquiredBlock(t.Count + units); // the union block; one block never overflows a fresh plan
        }

        return check.CheckArenaReservation(in plan); // capacity_exhausted last (ADR-0009 sections 2 and 5; story 008 AC Merge precedence)
    }

    /// <summary>
    /// The mutating half: applies <paramref name="plan"/>'s merge unchanged through <see cref="InventoryMutator.MergeInto"/> in one
    /// <c>Begin</c>/<c>End</c> bracket (cause <see cref="InvCause.None"/>). Never fails, never recomputes.
    /// </summary>
    /// <param name="plan">The plan <see cref="Validate"/> passed.</param>
    /// <param name="actor">The acting actor (written into every seal).</param>
    /// <param name="extra">The reply extra <see cref="InventoryMutator.End"/> fills.</param>
    internal void Commit(ref InvPlan plan, ActorId actor, ref InvReplyExtra extra)
    {
        InventoryMutator m = _owner.Mutator;
        InvMerge merge = plan.Merge;
        m.Begin(ref plan, actor, InvCause.None);
        m.MergeInto(ref plan, merge.SourceSlot, merge.TargetSlot, merge.Units);
        m.End(ref plan, ref extra);
    }
}
