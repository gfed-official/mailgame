using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// The one internal mutation layer of Grid Inventory (ADR-0009 Decision, sections 3 and 4; TR-inventory-002, TR-inventory-019; story
/// grid-inventory/005). Op handlers (their <c>Commit</c>) and <c>IInventoryApi</c> calls share it; nothing else writes container,
/// entry or occupancy state. One call is bracketed by <see cref="Begin"/> and <see cref="End"/> over the call's <see cref="InvPlan"/>:
/// <list type="number">
/// <item>the primitives (<see cref="Place"/>, <see cref="Remove"/>, <see cref="Transfer"/>; <c>ConsumeUnit</c>, <c>DetachForThrow</c>,
/// <c>Spawn</c>, <c>Destroy</c> arrive with their stories) apply the change at once and raise its <b>records</b> (<c>InvInserted</c> +
/// mailId-run <c>Continuation</c> chunks, <c>InvRemoved</c>, <c>InvMoved</c>), each with the container's audience resolved now, and mark
/// the (already touched, see <see cref="InvPlan"/>) container mutated in the plan. They never touch a version;</item>
/// <item><see cref="End"/> then bumps every mutated container exactly once (+1, in first-touch order) and raises its one
/// <c>InvVersion{c, from, to, actorId, cause}</c> seal, which therefore follows all of that container's records. A touched but
/// unchanged container (a no-op) raises nothing and keeps its version.</item>
/// </list>
/// Sim Core appends the <c>OpReply</c> after the handler returns, so every record and seal precedes it (ADR-0009 section 4 rule 5). An
/// <c>IInventoryApi</c> call opens its own call scope (<see cref="EventLogFacade.BeginCall"/>) around the bracket. Audience
/// (<see cref="AudienceOf"/>): a container with an owner slot (hotbar, player_inventory, cursor) goes to exactly that slot's bit; any
/// other container to its <see cref="ContainerSubscribers"/> mask at raise time. Records and seals carry no <c>reqId</c>. The ledger
/// hooks are story 017's. Allocation-free; never throws on the tick path (preconditions assert under <c>INVENTORY_ASSERT</c>). Not
/// thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// mutator.Begin(ref plan, op.Actor, InvCause.None);
/// mutator.Transfer(ref plan, entrySlot, toSlot, x, y, rotated);
/// var extra = new InvReplyExtra();
/// mutator.End(ref plan, ref extra);
/// return Reply.Ok(in op, in extra);
/// </code>
/// </example>
internal sealed class InventoryMutator
{
    private readonly ContainerTable _containers;
    private readonly EntryPool _entries;
    private readonly Occupancy _occupancy;
    private readonly MailIdArena _mailIds;
    private readonly ContainerSubscribers _subscribers;
    private readonly EventLogFacade _events;

    /// <summary>Creates the mutator over the module's storage.</summary>
    /// <param name="containers">The container table.</param>
    /// <param name="entries">The entry pool.</param>
    /// <param name="occupancy">The occupancy grids.</param>
    /// <param name="mailIds">The mail-ids arena (read for the Inserted runs).</param>
    /// <param name="subscribers">The subscriber sets.</param>
    /// <param name="events">The event-log writer.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal InventoryMutator(
        ContainerTable containers, EntryPool entries, Occupancy occupancy, MailIdArena mailIds, ContainerSubscribers subscribers, EventLogFacade events)
    {
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(occupancy);
        ArgumentNullException.ThrowIfNull(mailIds);
        ArgumentNullException.ThrowIfNull(subscribers);
        ArgumentNullException.ThrowIfNull(events);
        _containers = containers;
        _entries = entries;
        _occupancy = occupancy;
        _mailIds = mailIds;
        _subscribers = subscribers;
        _events = events;
    }

    /// <summary>The audience of a container's records and seals, resolved now: the owner's bit for a panel or cursor container, else the subscriber mask.</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <returns>The slot bitmask.</returns>
    public byte AudienceOf(ushort containerSlot)
    {
        byte owner = _containers[containerSlot].OwnerSlot;
        return owner < PlayerSlot.Count ? (byte)(1 << owner) : _subscribers.Mask(containerSlot);
    }

    /// <summary>Opens a call over <paramref name="plan"/>: records the actor and cause every seal and the reply carry.</summary>
    /// <param name="plan">The call's plan (its touched set may already hold the containers <c>Validate</c> planned).</param>
    /// <param name="actor">The acting actor.</param>
    /// <param name="cause">The cause.</param>
    public void Begin(ref InvPlan plan, ActorId actor, InvCause cause)
    {
        plan.Actor = actor;
        plan.Cause = cause;
    }

    /// <summary>
    /// Places a detached entry (<c>ContainerSlot == NoContainer</c>) at (<paramref name="x"/>, <paramref name="y"/>) of a container whose
    /// cells <c>Validate</c> proved free, and raises <c>InvInserted</c> plus its mailId runs. A square footprint is stored unrotated. A
    /// free or already placed entry asserts under <c>INVENTORY_ASSERT</c>; Release returns with nothing changed.
    /// </summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="entrySlot">The entry's pool slot.</param>
    /// <param name="containerSlot">The destination's table slot.</param>
    /// <param name="x">Top-left column.</param>
    /// <param name="y">Top-left row.</param>
    /// <param name="rotated">The rotation.</param>
    public void Place(ref InvPlan plan, ushort entrySlot, ushort containerSlot, byte x, byte y, bool rotated)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        InventoryAssert.That(e.IsLive, SimAssertCode.InventoryEntrySlotFree);
        InventoryAssert.That(e.ContainerSlot == EntryRecord.NoContainer, SimAssertCode.InventoryCellConflict);
        if (!e.IsLive || e.ContainerSlot != EntryRecord.NoContainer)
        {
            return; // Release: a free or already placed entry is not placed again (nothing changed, nothing raised)
        }

        if (!plan.EnsureTouched(containerSlot))
        {
            return; // Release: Validate broke the touch contract and the set is full, so nothing is changed unsealed
        }

        ref ContainerHeader h = ref _containers[containerSlot];
        Footprint.Canonicalise(e.W, e.H, ref rotated);
        Footprint.Effective(e.W, e.H, rotated, out int fw, out int fh);
        _occupancy.Place(containerSlot, h.W, h.H, x, y, fw, fh, entrySlot);
        e.ContainerSlot = containerSlot;
        e.X = x;
        e.Y = y;
        e.Rotated = rotated;
        h.EntryCount++;
        h.FreeCells = (ushort)Math.Max(0, h.FreeCells - (fw * fh));
        plan.MarkMutated(containerSlot);
        RaiseInserted(containerSlot, h.Id, in e);
    }

    /// <summary>Takes a placed entry out of its container (the entry stays allocated and detached) and raises <c>InvRemoved{c, entry, count}</c>.</summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="entrySlot">The entry's pool slot.</param>
    public void Remove(ref InvPlan plan, ushort entrySlot)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        InventoryAssert.That(e.IsLive && e.ContainerSlot != EntryRecord.NoContainer, SimAssertCode.InventoryEntrySlotFree);
        if (!e.IsLive || e.ContainerSlot == EntryRecord.NoContainer)
        {
            return;
        }

        ushort containerSlot = e.ContainerSlot;
        if (!plan.EnsureTouched(containerSlot))
        {
            return;
        }

        ref ContainerHeader h = ref _containers[containerSlot];
        Footprint.Effective(e.W, e.H, e.Rotated, out int fw, out int fh);
        _occupancy.Clear(containerSlot, h.W, h.H, e.X, e.Y, fw, fh);
        h.EntryCount = (ushort)Math.Max(0, h.EntryCount - 1);
        h.FreeCells = (ushort)Math.Min(h.W * h.H, h.FreeCells + (fw * fh));
        e.ContainerSlot = EntryRecord.NoContainer;
        plan.MarkMutated(containerSlot);
        var removed = new InvRemovedPayload { Container = h.Id.Value, EntryId = e.Id.Value, Count = e.Count };
        _ = _events.TryRaise(InvEventKinds.Removed, AudienceOf(containerSlot), in removed);
    }

    /// <summary>
    /// Moves a placed entry to (<paramref name="x"/>, <paramref name="y"/>, <paramref name="rotated"/>) of <paramref name="toSlot"/>. Onto
    /// its own cells with the same rotation it is a no-op (nothing raised, nothing marked); within one container it raises one
    /// <c>InvMoved</c>; across containers it is <see cref="Remove"/> then <see cref="Place"/> (source records, then destination records).
    /// </summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="entrySlot">The entry's pool slot.</param>
    /// <param name="toSlot">The destination's table slot.</param>
    /// <param name="x">Top-left column.</param>
    /// <param name="y">Top-left row.</param>
    /// <param name="rotated">The rotation.</param>
    public void Transfer(ref InvPlan plan, ushort entrySlot, ushort toSlot, byte x, byte y, bool rotated)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        InventoryAssert.That(e.IsLive && e.ContainerSlot != EntryRecord.NoContainer, SimAssertCode.InventoryEntrySlotFree);
        if (!e.IsLive || e.ContainerSlot == EntryRecord.NoContainer)
        {
            return;
        }

        Footprint.Canonicalise(e.W, e.H, ref rotated);
        ushort fromSlot = e.ContainerSlot;
        if (fromSlot != toSlot)
        {
            if (!plan.EnsureTouched(fromSlot) || !plan.EnsureTouched(toSlot))
            {
                return; // both checked before the Remove, so the entry is never left detached
            }

            Remove(ref plan, entrySlot);
            Place(ref plan, entrySlot, toSlot, x, y, rotated);
            return;
        }

        if (e.X == x && e.Y == y && e.Rotated == rotated)
        {
            return; // onto its own cells: a no-op bumps nothing and raises nothing (ADR-0009 section 3)
        }

        if (!plan.EnsureTouched(fromSlot))
        {
            return;
        }

        ref ContainerHeader h = ref _containers[fromSlot];
        Footprint.Effective(e.W, e.H, e.Rotated, out int oldW, out int oldH);
        Footprint.Effective(e.W, e.H, rotated, out int fw, out int fh);
        _occupancy.Clear(fromSlot, h.W, h.H, e.X, e.Y, oldW, oldH);
        _occupancy.Place(fromSlot, h.W, h.H, x, y, fw, fh, entrySlot);
        e.X = x;
        e.Y = y;
        e.Rotated = rotated;
        plan.MarkMutated(fromSlot);
        var moved = new InvMovedPayload { Container = h.Id.Value, EntryId = e.Id.Value, X = x, Y = y, Rotated = rotated ? (byte)1 : (byte)0 };
        _ = _events.TryRaise(InvEventKinds.Moved, AudienceOf(fromSlot), in moved);
    }

    /// <summary>
    /// Closes the call: bumps each mutated container of <paramref name="plan"/> by exactly 1 in first-touch order and raises its one
    /// <c>InvVersion</c> seal, then fills <paramref name="extra"/> (<see cref="FillVersions"/>, cause, and the <c>unchanged</c> flag when
    /// nothing was sealed). <c>ByActorId</c> is left as the caller set it (0): ADR-0009 means the <i>contested</i> actor (who took or
    /// destroyed the thing), set by stories 013/014; the acting actor is on every seal.
    /// </summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="extra">The reply extra to fill.</param>
    public void End(ref InvPlan plan, ref InvReplyExtra extra)
    {
        int sealedCount = 0;
        for (int i = 0; i < plan.TouchedCount; i++)
        {
            if (!plan.IsMutated(i))
            {
                continue;
            }

            ushort slot = plan.TouchedSlot(i);
            ref ContainerHeader h = ref _containers[slot];
            uint from = h.Version;
            uint to = unchecked(from + 1u); // ADR-0009 section 3: never wraps within a run
            h.Version = to;
            var seal = new InvVersionPayload
            {
                Container = h.Id.Value, VersionFrom = from, VersionTo = to, ActorId = plan.Actor.Value, Cause = (byte)plan.Cause,
            };
            _ = _events.TryRaise(InvEventKinds.Version, AudienceOf(slot), in seal);
            sealedCount++;
        }

        FillVersions(in plan, ref extra);
        extra.SetCause(plan.Cause, unchanged: sealedCount == 0);
    }

    /// <summary>Writes the current versions of the plan's primary source and destination into <c>ResultVersionA/B</c> (a reject's reply uses this alone).</summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="extra">The reply extra.</param>
    public void FillVersions(in InvPlan plan, ref InvReplyExtra extra)
    {
        if (plan.TryGetPrimarySource(out ushort a))
        {
            extra.ResultVersionA = _containers[a].Version;
        }

        if (plan.TryGetPrimaryDestination(out ushort b))
        {
            extra.ResultVersionB = _containers[b].Version;
        }
    }

    /// <summary>The number of ascending runs of consecutive ids in <paramref name="ids"/> (strictly ascending input).</summary>
    /// <param name="ids">The ids.</param>
    /// <returns>The run count.</returns>
    internal static int CountRuns(ReadOnlySpan<uint> ids)
    {
        int runs = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            if (i == 0 || ids[i] - ids[i - 1] != 1u)
            {
                runs++;
            }
        }

        return runs;
    }

    private void RaiseInserted(ushort containerSlot, ContainerId container, in EntryRecord e)
    {
        ReadOnlySpan<uint> ids = e.IsMail ? _mailIds.Read(in e.MailIds) : ReadOnlySpan<uint>.Empty;
        byte audience = AudienceOf(containerSlot);
        var inserted = new InvInsertedPayload
        {
            Container = container.Value,
            EntryId = e.Id.Value,
            AddressId = e.AddressId,
            ArrivalSeq = e.ArrivalSeq,
            MailKind = e.MailKind,
            ItemId = e.ItemId,
            RunCount = (ushort)CountRuns(ids),
            X = e.X,
            Y = e.Y,
            W = e.W,
            H = e.H,
            Rotated = e.Rotated ? (byte)1 : (byte)0,
            Count = e.Count,
            Flags = e.Flags,
            IsMail = e.IsMail ? (byte)1 : (byte)0,
        };
        _ = _events.TryRaise(InvEventKinds.Inserted, audience, in inserted);
        RaiseRuns(InvEventKinds.Inserted, audience, ids);
    }

    /// <summary>Raises <paramref name="ids"/> as ascending runs, 8 per <c>Continuation</c> chunk of <paramref name="kind"/> (same call, so same <c>CallSeq</c>).</summary>
    private void RaiseRuns(EventKind kind, byte audience, ReadOnlySpan<uint> ids)
    {
        InvMailIdRuns chunk = default;
        int n = 0;
        int i = 0;
        while (i < ids.Length)
        {
            int j = i + 1;
            while (j < ids.Length && ids[j] - ids[j - 1] == 1u && j - i < ushort.MaxValue)
            {
                j++;
            }

            chunk[n] = new InvMailIdRun { Start = ids[i], Length = (ushort)(j - i) };
            n++;
            if (n == InvMailIdRuns.Capacity)
            {
                _ = _events.TryRaiseContinuation(kind, audience, in chunk, n * InvMailIdRuns.RunBytes);
                chunk = default;
                n = 0;
            }

            i = j;
        }

        if (n > 0)
        {
            _ = _events.TryRaiseContinuation(kind, audience, in chunk, n * InvMailIdRuns.RunBytes);
        }
    }
}
