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
/// The <c>take_all</c> op (GDD R7 l.94, ADR-0009 sections 1-2 "batch ops"; WP-F, the client's Intake pickup). Op-level checks run before
/// any mutation, in this order: <c>bad_request</c> (<see cref="InvRequestCheck.CheckRequest"/>), <c>container_gone</c> (an issued id that
/// is no longer live), <c>not_open</c> (the container is not the actor's open external; the actor's own panel is never a take-all
/// source), <c>no_target</c> (no mail entry matches the filter). Then every matching entry, oldest first by lowest mailId, is planned
/// whole into the external quick-move row (mail: <c>[player_inventory, hotbar]</c>, R9 l.127, pass-first, merging) and committed at
/// once; an entry that does not fit is skipped and the batch continues (each entry is atomic, the batch is not). The whole call is one
/// <see cref="InventoryMutator.Begin"/> / <see cref="InventoryMutator.End"/> bracket, so each touched container is sealed once. A batch
/// that moves nothing replies <c>ok:false</c> with the first skip reason. The reply extra carries requested / got units and entries,
/// the skipped entries and the first skip reason. Allocation-free (the candidate list is on the stack).
/// </summary>
/// <example><code>builder.BindOp(InvOpDescriptors.TakeAll, new TakeAllHandler(inventory, content, stats));</code></example>
internal sealed class TakeAllHandler : IOpHandler
{
    // The largest grid a take-all reads (the Intake is 20 x 16 at M-1a; a 1x1 entry per cell is the worst case).
    private const int MaxCandidates = 512;

    private readonly InventorySystem _owner;
    private readonly ContentSet _content;
    private readonly IStatSheet _stats;

    /// <summary>Creates the handler.</summary>
    /// <param name="owner">The inventory.</param>
    /// <param name="content">The content set (item and mail-kind defs).</param>
    /// <param name="stats">The stat sheet (<c>effMax</c>).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public TakeAllHandler(InventorySystem owner, ContentSet content, IStatSheet stats)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        _owner = owner;
        _content = content;
        _stats = stats;
    }

    /// <inheritdoc />
    public OpKind Kind => InvOpKinds.TakeAll;

    /// <inheritdoc />
    public Reply Apply(in Op op, in TickContext ctx)
    {
        PlayerSlot actor = _owner.ActorSlot(op.Actor);
        ushort reason = _owner.RequestCheck.CheckRequest(in op, actor);
        TakeAllPayload p = op.PayloadAs<TakeAllPayload>();
        var from = new ContainerId(p.Container);
        ushort fromSlot = 0;
        if (reason == 0 && !_owner.Containers.TryGetSlot(from, out fromSlot))
        {
            reason = (ushort)InvReason.ContainerGone;
        }

        if (reason == 0 && (actor.IsNone || _owner.OpenSet.ExternalOf(actor) != from))
        {
            reason = (ushort)InvReason.NotOpen;
        }

        Span<ushort> candidates = stackalloc ushort[MaxCandidates];
        Span<uint> oldest = stackalloc uint[MaxCandidates];
        int n = reason == 0 ? Collect(fromSlot, (TakeAllFilter)p.FilterKind, p.FilterValue, candidates, oldest) : 0;
        if (reason == 0 && n == 0)
        {
            reason = (ushort)InvReason.NoTarget;
        }

        if (reason != 0)
        {
            return Reply.Reject(in op, reason);
        }

        var extra = new InvReplyExtra();
        Batch batch = Take(actor, op.Actor, fromSlot, candidates[..n], ref extra);
        extra.SetUnits(batch.RequestedUnits, batch.GotUnits);
        extra.SetEntries(n, batch.GotEntries, n - batch.GotEntries);
        extra.SkipReason = (byte)batch.FirstSkip;
        if (batch.GotEntries == 0)
        {
            Reply rejected = Reply.Reject(in op, batch.FirstSkip);
            rejected.SetExtra(in extra);
            return rejected;
        }

        return Reply.Ok(in op, in extra);
    }

    // The container's matching mail entries (one per anchor cell), sorted by lowest mailId; returns the count.
    private int Collect(ushort containerSlot, TakeAllFilter filter, uint value, Span<ushort> slots, Span<uint> keys)
    {
        ref ContainerHeader h = ref _owner.Containers[containerSlot];
        EntryPool entries = _owner.Entries;
        int n = 0;
        for (int y = 0; y < h.H; y++)
        {
            for (int x = 0; x < h.W && n < slots.Length; x++)
            {
                ushort owner = _owner.Occupancy.OwnerAt(containerSlot, h.W, x, y);
                if (owner == Occupancy.Free)
                {
                    continue;
                }

                ref EntryRecord e = ref entries[owner];
                if (e.X != x || e.Y != y || !e.IsMail || !Matches(filter, value, in e))
                {
                    continue;
                }

                ReadOnlySpan<uint> ids = _owner.MailIds.Read(in e.MailIds);
                slots[n] = owner;
                keys[n] = ids.Length > 0 ? ids[0] : uint.MaxValue;
                n++;
            }
        }

        // Insertion sort by (lowest mailId, entry slot): n is small and the order must be stable and allocation-free.
        for (int i = 1; i < n; i++)
        {
            ushort s = slots[i];
            uint k = keys[i];
            int j = i - 1;
            while (j >= 0 && (keys[j] > k || (keys[j] == k && slots[j] > s)))
            {
                slots[j + 1] = slots[j];
                keys[j + 1] = keys[j];
                j--;
            }

            slots[j + 1] = s;
            keys[j + 1] = k;
        }

        return n;
    }

    private static bool Matches(TakeAllFilter filter, uint value, in EntryRecord e)
    {
        var address = new AddressId(e.AddressId);
        return filter switch
        {
            TakeAllFilter.District => address.District == value,
            TakeAllFilter.Street => ((address.District << 8) | address.Street) == value,
            TakeAllFilter.Address => e.AddressId == value,
            TakeAllFilter.Kind => e.MailKind == value,
            _ => false,
        };
    }

    // Plans each candidate on its own plan against the state the earlier commits left, and commits the ones that fit into the call's plan.
    private Batch Take(PlayerSlot actor, ActorId actorId, ushort fromSlot, ReadOnlySpan<ushort> candidates, ref InvReplyExtra extra)
    {
        ContainerTable containers = _owner.Containers;
        EntryPool entries = _owner.Entries;
        _ = containers.TryGetSlot(_owner.HotbarOf(actor), out ushort hotbarSlot);
        _ = containers.TryGetSlot(_owner.PlayerInventoryOf(actor), out ushort inventorySlot);
        Span<ushort> row = stackalloc ushort[TargetOrder.MaxRowLength];
        var plan = new InvPlan();
        _ = plan.Touch(fromSlot);
        plan.SetPrimary(fromSlot, inventorySlot);
        Batch batch = default;
        InventoryMutator m = _owner.Mutator;
        m.Begin(ref plan, actorId, InvCause.None);
        foreach (ushort entrySlot in candidates)
        {
            ref readonly EntryRecord e = ref entries[entrySlot];
            int units = e.Count;
            batch.RequestedUnits += units;
            PlacementSubject subject = PlacementSubject.Of(_content, _stats, in e);
            TargetOrder.CursorRow(subject.Category, hotbarSlot, inventorySlot, row);
            var entryPlan = new InvPlan();
            ushort reason = _owner.AutoPlace.Plan(ref entryPlan, in subject, units, entrySlot, row, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);
            if (reason == 0 && ap.MovedUnits != units)
            {
                reason = (ushort)InvReason.NoRoom; // a part of a stack only merges: the entry would stay split across containers
            }

            reason = reason == 0 ? _owner.RequestCheck.CheckArenaReservation(in entryPlan) : reason;
            if (reason == 0 && !TouchAll(ref plan, in entryPlan))
            {
                reason = (ushort)InvReason.NoRoom;
            }

            if (reason != 0)
            {
                batch.FirstSkip = batch.FirstSkip == 0 ? reason : batch.FirstSkip;
                continue;
            }

            _owner.AutoPlace.Commit(ref plan, in ap);
            batch.GotUnits += units;
            batch.GotEntries++;
        }

        m.End(ref plan, ref extra);
        return batch;
    }

    // Adds the entry plan's touched containers to the call's plan; false when the call's touched set has no room for them.
    private static bool TouchAll(ref InvPlan plan, in InvPlan entryPlan)
    {
        for (int i = 0; i < entryPlan.TouchedCount; i++)
        {
            if (plan.Touch(entryPlan.TouchedSlot(i)) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private struct Batch
    {
        public int RequestedUnits;
        public int GotUnits;
        public int GotEntries;
        public ushort FirstSkip;
    }
}
