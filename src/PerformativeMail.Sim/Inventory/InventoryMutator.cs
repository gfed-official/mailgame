using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// The one internal mutation layer of Grid Inventory (ADR-0009 Decision, sections 3 and 4; TR-inventory-002, TR-inventory-019; story
/// grid-inventory/005). Op handlers (their <c>Commit</c>) and <c>IInventoryApi</c> calls share it; nothing else writes container,
/// entry or occupancy state. One call is bracketed by <see cref="Begin"/> and <see cref="End"/> over the call's <see cref="InvPlan"/>:
/// <list type="number">
/// <item>the primitives (<see cref="Place"/>, <see cref="Remove"/>, <see cref="Transfer"/>, the count change <see cref="MergeInto"/>
/// (story 009); <c>ConsumeUnit</c>, <c>DetachForThrow</c>, <c>Spawn</c>, <c>Destroy</c> arrive with their stories) apply the change at
/// once and raise its <b>records</b> (<c>InvInserted</c> / <c>InvMerged</c> + mailId-run <c>Continuation</c> chunks, <c>InvRemoved</c>,
/// <c>InvMoved</c>), each with the container's audience resolved now, and mark the (already touched, see <see cref="InvPlan"/>)
/// container mutated in the plan. They never touch a version;</item>
/// <item><see cref="End"/> then bumps every mutated container exactly once (+1, in first-touch order) and raises its one
/// <c>InvVersion{c, from, to, actorId, cause}</c> seal, which therefore follows all of that container's records. A touched but
/// unchanged container (a no-op) raises nothing and keeps its version.</item>
/// </list>
/// Sim Core appends the <c>OpReply</c> after the handler returns, so every record and seal precedes it (ADR-0009 section 4 rule 5). An
/// <c>IInventoryApi</c> call opens its own call scope (<see cref="EventLogFacade.BeginCall"/>) around the bracket. Audience
/// (<see cref="AudienceOf"/>): a container with an owner slot (hotbar, player_inventory, cursor) goes to exactly that slot's bit; any
/// other container to its <see cref="ContainerSubscribers"/> mask at raise time. Records and seals carry no <c>reqId</c>. The ledger
/// hooks are story 017's. The primitives also maintain the <see cref="Placement.StackIndex"/> (story 009, ADR-0009 section 5: the
/// placed entries whose count is below their <c>effMax</c>, read at the primitive's time); <see cref="RebuildIndex"/> rebuilds a
/// container's lists and clears its negative-fit cache when its <c>statEpoch</c> bumps. A slots-layout container holds every entry
/// as a 1 x 1 unrotated footprint (GDD R1; story 009). Allocation-free; never throws on the tick path (preconditions assert under
/// <c>INVENTORY_ASSERT</c>). Not thread-safe: Sim tick thread only.
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
    private readonly ContentSet _content;
    private readonly IStatSheet _stats;
    private readonly StackIndex _index;
    private readonly NegativeFitCache _noFit;
    private readonly uint[] _movedIds = new uint[MailIdArena.MaxUnits];

    /// <summary>Creates the mutator over the module's storage.</summary>
    /// <param name="containers">The container table.</param>
    /// <param name="entries">The entry pool.</param>
    /// <param name="occupancy">The occupancy grids.</param>
    /// <param name="mailIds">The mail-ids arena (read for the Inserted runs).</param>
    /// <param name="subscribers">The subscriber sets.</param>
    /// <param name="events">The event-log writer.</param>
    /// <param name="content">The content set (an entry's def for its <c>effMax</c>).</param>
    /// <param name="stats">The stat sheet (mail <c>effMax</c> is <c>MailMaxStack</c>, read at the primitive's time).</param>
    /// <param name="index">The StackKey non-full index the primitives maintain.</param>
    /// <param name="noFit">The negative-fit cache <see cref="RebuildIndex"/> clears.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal InventoryMutator(
        ContainerTable containers,
        EntryPool entries,
        Occupancy occupancy,
        MailIdArena mailIds,
        ContainerSubscribers subscribers,
        EventLogFacade events,
        ContentSet content,
        IStatSheet stats,
        StackIndex index,
        NegativeFitCache noFit)
    {
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(occupancy);
        ArgumentNullException.ThrowIfNull(mailIds);
        ArgumentNullException.ThrowIfNull(subscribers);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(noFit);
        _containers = containers;
        _entries = entries;
        _occupancy = occupancy;
        _mailIds = mailIds;
        _subscribers = subscribers;
        _events = events;
        _content = content;
        _stats = stats;
        _index = index;
        _noFit = noFit;
    }

    /// <summary>The StackKey non-full index the primitives maintain (AutoPlace's PASS M reads it).</summary>
    internal StackIndex StackIndex => _index;

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
    /// cells <c>Validate</c> proved free, and raises <c>InvInserted</c> plus its mailId runs. A square footprint, and every entry of a
    /// slots layout (one 1 x 1 cell), is stored unrotated. Links the entry into the StackKey index when it is non-full. A free or
    /// already placed entry asserts under <c>INVENTORY_ASSERT</c>; Release returns with nothing changed.
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
        Canonicalise(in h, in e, ref rotated);
        FootprintIn(in h, in e, rotated, out int fw, out int fh);
        _occupancy.Place(containerSlot, h.W, h.H, x, y, fw, fh, entrySlot);
        e.ContainerSlot = containerSlot;
        e.X = x;
        e.Y = y;
        e.Rotated = rotated;
        h.EntryCount++;
        h.FreeCells = (ushort)Math.Max(0, h.FreeCells - (fw * fh));
        plan.MarkMutated(containerSlot);
        LinkIfNonFull(entrySlot);
        RaiseInserted(containerSlot, h.Id, in e);
    }

    /// <summary>Takes a placed entry out of its container (the entry stays allocated and detached; it leaves the StackKey index) and raises <c>InvRemoved{c, entry, count}</c>.</summary>
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
        FootprintIn(in h, in e, e.Rotated, out int fw, out int fh);
        _index.Unlink(entrySlot);
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

        ref ContainerHeader h = ref _containers[fromSlot];
        Canonicalise(in h, in e, ref rotated);
        if (e.X == x && e.Y == y && e.Rotated == rotated)
        {
            return; // onto its own cells: a no-op bumps nothing and raises nothing (ADR-0009 section 3)
        }

        if (!plan.EnsureTouched(fromSlot))
        {
            return;
        }

        FootprintIn(in h, in e, e.Rotated, out int oldW, out int oldH);
        FootprintIn(in h, in e, rotated, out int fw, out int fh);
        _index.Unlink(entrySlot);
        _occupancy.Clear(fromSlot, h.W, h.H, e.X, e.Y, oldW, oldH);
        _occupancy.Place(fromSlot, h.W, h.H, x, y, fw, fh, entrySlot);
        e.X = x;
        e.Y = y;
        e.Rotated = rotated;
        plan.MarkMutated(fromSlot);
        LinkIfNonFull(entrySlot);
        var moved = new InvMovedPayload { Container = h.Id.Value, EntryId = e.Id.Value, X = x, Y = y, Rotated = rotated ? (byte)1 : (byte)0 };
        _ = _events.TryRaise(InvEventKinds.Moved, AudienceOf(fromSlot), in moved);
    }

    /// <summary>
    /// The count-change primitive (story grid-inventory/009, Planner R1; reused by story 008's Merge op): moves the <paramref name="units"/>
    /// <b>oldest</b> units of the source entry into the placed target stack. For mail the oldest mailIds leave the source (arena take,
    /// R6) and are unioned into the target's run (one arena block acquired, which <c>Validate</c> reserved through
    /// <see cref="InvPlan.AddAcquiredBlock"/> with the union's size, <c>target.count + units</c>); both counts are set. Records, in order:
    /// for a placed source <c>InvRemoved{source container, source, units}</c> (take-k removes the k lowest, ADR-0009 section 4); then
    /// <c>InvMerged{target container, target, units}</c> plus the moved mailIds as runs in <c>Continuation</c> chunks. A source left at
    /// 0 is removed from its container (its <c>InvRemoved</c> carries the whole count) and freed; otherwise it keeps its entryId and
    /// position. A detached source (cursor-less in-flight entry, spawn) raises no source record. Both entries are re-linked in the
    /// StackKey index by their new counts. The source may be in the target's container (an in-container merge). The containers must
    /// already be touched (<see cref="InvPlan.EnsureTouched"/>). An invalid merge (free or unplaced target, a self-merge, different
    /// <c>StackKey</c>s, <paramref name="units"/> outside 1 to the source's count, a target over 255 or over its <c>effMax</c> read now
    /// (<see cref="EffMaxOf"/>, the value the index uses)) asserts
    /// <see cref="SimAssertCode.InventoryMergeInvalid"/> under <c>INVENTORY_ASSERT</c>; Release changes nothing. The ledger hook is story
    /// 017's (a Placed-to-Placed merge crosses no ledger state, ADR-0009 section 6).
    /// </summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="sourceSlot">The source entry's pool slot (placed or detached).</param>
    /// <param name="targetSlot">The placed target stack's pool slot.</param>
    /// <param name="units">How many units move, 1 to the source's count.</param>
    public void MergeInto(ref InvPlan plan, ushort sourceSlot, ushort targetSlot, int units)
    {
        ref EntryRecord src = ref _entries[sourceSlot];
        ref EntryRecord dst = ref _entries[targetSlot];
        bool valid = src.IsLive && dst.IsLive && sourceSlot != targetSlot && dst.ContainerSlot != EntryRecord.NoContainer
            && units >= 1 && units <= src.Count && dst.Count + units <= byte.MaxValue && dst.Count + units <= EffMaxOf(in dst)
            && StackKey.Of(in src) == StackKey.Of(in dst) && (!src.IsMail || src.MailIds.Count >= units);
        InventoryAssert.That(valid, SimAssertCode.InventoryMergeInvalid);
        if (!valid)
        {
            return;
        }

        ushort from = src.ContainerSlot;
        ushort to = dst.ContainerSlot;
        if ((from != EntryRecord.NoContainer && !plan.EnsureTouched(from)) || !plan.EnsureTouched(to))
        {
            return; // Release: Validate broke the touch contract and the set is full, so nothing is changed unsealed
        }

        Span<uint> moved = _movedIds.AsSpan(0, src.IsMail ? units : 0);
        if (src.IsMail)
        {
            _mailIds.Read(in src.MailIds)[..units].CopyTo(moved);
            if (!_mailIds.TryAppend(ref dst.MailIds, moved, 0))
            {
                return; // unreachable after Validate's reservation (ADR-0009 section 2); the arena wrote its exhaustion record
            }

            _ = _mailIds.Take(ref src.MailIds, units, moved, 0);
        }

        _index.Unlink(targetSlot);
        _entries.SetCount(targetSlot, (byte)(dst.Count + units));
        if (units == src.Count)
        {
            if (from != EntryRecord.NoContainer)
            {
                Remove(ref plan, sourceSlot); // InvRemoved carries the whole count, which is units
            }

            _entries.Free(sourceSlot);
        }
        else
        {
            _index.Unlink(sourceSlot);
            _entries.SetCount(sourceSlot, (byte)(src.Count - units));
            if (from != EntryRecord.NoContainer)
            {
                plan.MarkMutated(from);
                var removed = new InvRemovedPayload { Container = _containers[from].Id.Value, EntryId = src.Id.Value, Count = (byte)units };
                _ = _events.TryRaise(InvEventKinds.Removed, AudienceOf(from), in removed);
                LinkIfNonFull(sourceSlot);
            }
        }

        plan.MarkMutated(to);
        LinkIfNonFull(targetSlot);
        byte audience = AudienceOf(to);
        var merged = new InvMergedPayload
        {
            Container = _containers[to].Id.Value, TargetEntryId = dst.Id.Value, AddedCount = (byte)units, RunCount = (ushort)CountRuns(moved),
        };
        _ = _events.TryRaise(InvEventKinds.Merged, audience, in merged);
        RaiseRuns(InvEventKinds.Merged, audience, moved);
    }

    /// <summary>
    /// Rebuilds the container's StackKey lists from its entries (every placed entry whose count is below its <c>effMax</c> read now,
    /// row-major) and clears its negative-fit cache. Every entry linked under the container is unlinked first
    /// (<see cref="Placement.StackIndex.ClearContainer"/>), so a stale link whose cells were cleared outside the mutator does not
    /// survive. Called when the container's <c>statEpoch</c> bumps (a <c>MailMaxStack</c> or <c>InventoryRows</c> change, R15; nothing
    /// bumps it at M-1a, tests call it), after a load, and by tests that arranged entries outside the mutator. Changes no Sim state
    /// (both are derived memos): no record, no version. O(entry pool + cells); allocation-free.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    public void RebuildIndex(ushort containerSlot)
    {
        ref ContainerHeader h = ref _containers[containerSlot];
        _index.ClearContainer(containerSlot);
        for (int y = 0; y < h.H; y++)
        {
            if (_occupancy.RowBits(containerSlot, y) == 0UL)
            {
                continue;
            }

            for (int x = 0; x < h.W; x++)
            {
                ushort owner = _occupancy.OwnerAt(containerSlot, h.W, x, y);
                if (owner == Occupancy.Free)
                {
                    continue;
                }

                ref EntryRecord e = ref _entries[owner];
                if (e.ContainerSlot == containerSlot && e.X == x && e.Y == y)
                {
                    LinkIfNonFull(owner); // the entry's top-left cell
                }
            }
        }

        _noFit.Clear(containerSlot);
    }

    /// <summary>The entry's <c>effMax</c> now: <c>MailMaxStack</c> of its kind for mail, its def's <c>maxStack</c> for an item (story 006's <see cref="PlacementSubject"/>).</summary>
    /// <param name="entry">A live entry.</param>
    /// <returns>The effective max stack.</returns>
    internal int EffMaxOf(in EntryRecord entry) => PlacementSubject.Of(_content, _stats, in entry).EffMax;

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

    /// <summary>The footprint an entry covers in a container: 1 x 1 on a slots layout (one entry per slot whatever its size), else its effective <c>w x h</c>.</summary>
    private static void FootprintIn(in ContainerHeader h, in EntryRecord e, bool rotated, out int fw, out int fh)
    {
        if (h.Layout == ContainerLayout.Slots)
        {
            fw = 1;
            fh = 1;
            return;
        }

        Footprint.Effective(e.W, e.H, rotated, out fw, out fh);
    }

    /// <summary>A square footprint, and any entry on a slots layout, is stored unrotated.</summary>
    private static void Canonicalise(in ContainerHeader h, in EntryRecord e, ref bool rotated)
    {
        Footprint.Canonicalise(e.W, e.H, ref rotated);
        if (h.Layout == ContainerLayout.Slots)
        {
            rotated = false;
        }
    }

    private void LinkIfNonFull(ushort entrySlot)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        if (e.Count < EffMaxOf(in e))
        {
            _index.Link(entrySlot);
        }
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
