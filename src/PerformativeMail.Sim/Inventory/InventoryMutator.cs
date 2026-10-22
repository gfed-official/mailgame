using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Intake;
using PerformativeMail.Sim.Inventory.Ledger;
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
/// <item>the primitives (<see cref="Place"/>, <see cref="Remove"/>, <see cref="Transfer"/>, the count changes <see cref="MergeInto"/>
/// (story 009), <see cref="TakeOldest"/> (story 012, which story 015's <c>ConsumeUnit</c> reuses), <see cref="MergeUnitInto"/>
/// (story 015's <c>TryInsert</c>) and <see cref="SplitAndPlace"/> (story 008's split-and-place, raising <c>InvSplitFrom</c>), and
/// <see cref="DestroyDetached"/> (story 013)) apply the change at
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
/// other container to its <see cref="ContainerSubscribers"/> mask at raise time. Records and seals carry no <c>reqId</c>. The
/// primitives are also the one writer of the conservation <see cref="UnitLedger"/> (story 017; ADR-0009 section 6): <see cref="Place"/>
/// adds an entry's units to the Cursor state (a player's cursor, <see cref="IsCursor"/>) or the Placed state of its destination and
/// <see cref="Remove"/> subtracts them, so a cross-container <see cref="Transfer"/> moves them and a move inside one container moves
/// nothing; <see cref="MergeInto"/>, <see cref="MergeUnitInto"/> and <see cref="TakeOldest"/>'s partial path move the units they
/// count; <see cref="SplitAndPlace"/> follows from <see cref="TakeOldest"/> and <see cref="Place"/>; <see cref="DestroyDetached"/>
/// crosses to Destroyed and <see cref="ConsumeOldest"/> to Consumed by reason; and the holder states (World, Flight, Belt, Backlog)
/// are entered and left only through <see cref="EnterHolder(LedgerState, ushort)"/> / <see cref="LeaveHolder(ushort, int)"/> and their
/// unit forms, which the <c>IInventoryApi</c> calls, <c>WorldItems</c> and <c>PickupCore</c> call at their holder crossings, and
/// <see cref="CountSpawned"/> counts a unit's first entry into custody (Mail Lead planning defaults pending Evan, TD-160 items 1-4). A
/// detached source's holder state is the source state of the primitive that consumes it: <see cref="Place"/> takes the entry's units
/// out of the holder it is in (Flight for a catch or a flying return, Backlog for the drain, none after a WorldItem's removal) and
/// <see cref="MergeInto"/> the merged units (World for a partial pickup, Flight for a partial catch), so no caller leaves a holder
/// before a placement and a partial merge is never counted twice. The primitives also maintain the <see cref="Placement.StackIndex"/> (story 009, ADR-0009 section 5: the
/// placed entries whose count is below their <c>effMax</c>, read at the primitive's time); <see cref="RebuildIndex"/> rebuilds a
/// container's lists and clears its negative-fit cache when its <c>statEpoch</c> bumps. They maintain the
/// <see cref="Placement.ItemCountIndex"/> too (story 015, ADR-0009 section 5: a container's <c>itemId → units</c>, which
/// <c>CountItem</c> reads): every unit of an item entry entering a container is added, every unit leaving it subtracted, here and
/// nowhere else. A slots-layout container holds every entry
/// as a 1 x 1 unrotated footprint (GDD R1; story 009). A container whose def has an outfeed (<c>BeltAccess.OutfeedOnly</c>, the
/// Intake) stamps every entry it receives through <see cref="Place"/> or <see cref="Transfer"/> with the next <c>arrivalSeq</c>
/// (<see cref="ContainerHeader.NextArrivalSeq"/>, before <c>InvInserted</c>, which carries it) and appends it to the
/// <see cref="Outfeed"/> FIFO; a stack that units are merged onto (<see cref="MergeInto"/>, AutoPlace's PASS M) is restamped the same
/// way, before <c>InvMerged</c>, so it leaves last (owner ruling Evan 2026-10-21 12:28 PT; GDD R14 "no Intake entry's arrivalSeq ever
/// decreases"), and its old node becomes a lazy tombstone; a move inside the container keeps the seq. An entry leaving it
/// (<see cref="Remove"/>, a cross-container <see cref="Transfer"/>) gets <c>arrivalSeq</c> 0 and leaves a lazy tombstone (story 012,
/// GDD R14). It is also the one writer of each slot's <b>cursor origin</b> (story grid-inventory/010, Mail Review r1 B1; GDD R7
/// <c>return</c> l.95, "into its origin cells"; read through <see cref="TryGetCursorOrigin"/>): <see cref="Place"/> into a cursor (a
/// container of the cursor def with an owner slot) records where the entry came from in this call, and <see cref="Remove"/> from a
/// cursor clears it, so an origin is present exactly while the cursor holds the entry it was recorded for. Every path onto or off a
/// cursor (pick, move, split, swap, leash put-away, quick_move, return, merge, consume) ends in those two primitives, which are
/// the only cursor occupancy writes (an in-container <see cref="Transfer"/> on the 1 x 1 cursor is a no-op), so no handler records or
/// clears an origin itself. "Where it came from" is the container, cells and rotation the entry was <see cref="Remove"/>d from earlier
/// in the same <see cref="Begin"/>/<see cref="End"/> call, or for a piece <see cref="SplitAndPlace"/> splits off, its source entry's
/// container, cells and rotation; an entry that arrives with neither (a detached entry from no container this call) gets no origin.
/// The origin of an entry moved or split onto the cursor is a Mail Lead planning default pending Evan (TD-153): GDD l.95 and the l.178
/// cursor table name only "its origin cells", and ADR-0009 does not define them. Allocation-free; never throws on the tick path
/// (preconditions assert under <c>INVENTORY_ASSERT</c>). Not thread-safe: Sim tick thread only.
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
    private readonly ItemCountIndex _itemCounts;
    private readonly NegativeFitCache _noFit;
    private readonly OutfeedFifo _outfeed;
    private readonly ISlotView? _slots;
    private readonly Weight _weight;
    private readonly UnitLedger _ledger;
    private readonly uint[] _movedIds = new uint[MailIdArena.MaxUnits];
    private readonly uint[] _splitIds = new uint[MailIdArena.MaxUnits];
    private readonly DefIndex _cursorDef;

    // Story 010 (Mail Review r1 B1): each slot's cursor origin, written only by Place and Remove (see the class remarks).
    private readonly InventorySystem.CursorOrigin[] _cursorOrigins = new InventorySystem.CursorOrigin[PlayerSlot.Count];

    // Where entries left this call: a ring of the latest departures (Remove, and SplitAndPlace's piece), emptied by Begin (every
    // bracket opens with it). A departure overwritten by a later one is lost, so a Place onto a cursor that finds none records no
    // origin, never a stale one.
    private readonly InventorySystem.CursorOrigin[] _departures = new InventorySystem.CursorOrigin[DepartureCapacity];
    private int _departureCount;

    private const int DepartureCapacity = 4;

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
    /// <param name="itemCounts">The <c>itemId → units</c> index the primitives maintain (story 015).</param>
    /// <param name="noFit">The negative-fit cache <see cref="RebuildIndex"/> clears.</param>
    /// <param name="outfeed">The outfeed FIFO memo the primitives maintain (story 012).</param>
    /// <param name="weight">The per-slot weight points memo the primitives maintain (story 016).</param>
    /// <param name="cursorDef">The cursor container def (story 010: <see cref="Place"/> and <see cref="Remove"/> keep the cursor origins).</param>
    /// <param name="ledger">The conservation ledger the primitives write (story 017; the only writer).</param>
    /// <param name="slots">The slot table (the connected-slot audience of the world kinds, story 013); null gives audience 0.</param>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="slots"/> is null.</exception>
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
        ItemCountIndex itemCounts,
        NegativeFitCache noFit,
        OutfeedFifo outfeed,
        Weight weight,
        DefIndex cursorDef,
        UnitLedger ledger,
        ISlotView? slots = null)
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
        ArgumentNullException.ThrowIfNull(itemCounts);
        ArgumentNullException.ThrowIfNull(noFit);
        ArgumentNullException.ThrowIfNull(outfeed);
        ArgumentNullException.ThrowIfNull(weight);
        ArgumentNullException.ThrowIfNull(ledger);
        _containers = containers;
        _entries = entries;
        _occupancy = occupancy;
        _mailIds = mailIds;
        _subscribers = subscribers;
        _events = events;
        _content = content;
        _stats = stats;
        _index = index;
        _itemCounts = itemCounts;
        _noFit = noFit;
        _outfeed = outfeed;
        _weight = weight;
        _cursorDef = cursorDef;
        _ledger = ledger;
        _slots = slots;
    }

    /// <summary>The StackKey non-full index the primitives maintain (AutoPlace's PASS M reads it).</summary>
    internal StackIndex StackIndex => _index;

    /// <summary>The <c>itemId → units</c> index the primitives maintain (story 015; <c>CountItem</c> reads it).</summary>
    internal ItemCountIndex ItemCounts => _itemCounts;

    /// <summary>The outfeed FIFO memo the primitives maintain (story 012; <c>PeekOutfeed</c> / <c>TakeUnit</c> read it).</summary>
    internal OutfeedFifo Outfeed => _outfeed;

    /// <summary>The per-slot weight points memo the primitives maintain (story 016; <c>WeightPoints</c> / <c>SpeedPermille</c> read it).</summary>
    internal Weight Weight => _weight;

    /// <summary>The conservation ledger the primitives write (story 017; the tick-end check and the dump read it).</summary>
    internal UnitLedger Ledger => _ledger;

    /// <summary>True for a player's cursor container (story 017: the ledger's Cursor state; <see cref="UnitLedger.Recount"/> reads it).</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <returns>True when units there are in the Cursor state.</returns>
    internal bool IsCursorSlot(ushort containerSlot) => IsCursor(in _containers[containerSlot]);

    /// <summary>True when the container's def has an outfeed (<c>BeltAccess.OutfeedOnly</c>: the Intake at M-1a).</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <returns>True when insertions are stamped and queued.</returns>
    internal bool HasOutfeed(ushort containerSlot) => _content.Containers[_containers[containerSlot].DefIndex].BeltAccess == BeltAccess.OutfeedOnly;

    /// <summary>The audience of a container's records and seals, resolved now: the owner's bit for a panel or cursor container, else the subscriber mask.</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <returns>The slot bitmask.</returns>
    public byte AudienceOf(ushort containerSlot)
    {
        byte owner = _containers[containerSlot].OwnerSlot;
        return owner < PlayerSlot.Count ? (byte)(1 << owner) : _subscribers.Mask(containerSlot);
    }

    /// <summary>The audience of the world kinds (<c>WorldItem…</c>, <c>ItemsLost</c>; <see cref="ReplicationClass.Replicated"/>): every connected slot, resolved now.</summary>
    public byte WorldAudience => _slots?.ConnectedMask ?? 0;

    /// <summary>Raises a world record (story 013: <c>WorldItem{Spawned, Despawned, EntriesChanged}</c>, <c>ItemsLost</c>) to <see cref="WorldAudience"/>. Touches no container and no version.</summary>
    /// <typeparam name="T">The kind's payload.</typeparam>
    /// <param name="kind">The world kind.</param>
    /// <param name="payload">The payload.</param>
    public void RaiseWorld<T>(EventKind kind, in T payload)
        where T : unmanaged
        => _ = _events.TryRaise(kind, WorldAudience, in payload);

    /// <summary>
    /// The destroy primitive for a detached entry (story 013: a non-mail WorldItem that expired, floated or was evicted; ADR-0009
    /// section 6 lists <c>Destroy</c> among the lowest primitives): frees its mailIds block (if any) and its pool slot; the id is gone for
    /// ever. The entry is in no container, so no <see cref="Placement.ItemCountIndex"/> total changes. Raises nothing (the caller
    /// raises <c>ItemsLost</c>). Ledger (story 017, TD-135): the entry leaves any holder state it is still in and its units cross to
    /// Destroyed. A free or placed entry asserts <see cref="SimAssertCode.InventoryEntrySlotFree"/> under <c>INVENTORY_ASSERT</c>; Release changes nothing.
    /// </summary>
    /// <param name="entrySlot">The detached entry's pool slot.</param>
    public void DestroyDetached(ushort entrySlot)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        bool valid = e.IsLive && e.ContainerSlot == EntryRecord.NoContainer;
        InventoryAssert.That(valid, SimAssertCode.InventoryEntrySlotFree);
        if (!valid)
        {
            return;
        }

        LeaveHolder(entrySlot, e.Count);
        _ledger.AddDestroyed(UnitLedger.RowOf(in e), e.Count);
        if (e.IsMail)
        {
            _mailIds.Free(in e.MailIds);
        }

        _entries.Free(entrySlot);
    }

    /// <summary>
    /// The holder-entry primitive (story 017; Mail Lead planning default pending Evan, TD-160 item 1): the live detached entry in
    /// <paramref name="entrySlot"/> and all its units enter <paramref name="holder"/> (<see cref="LedgerState.World"/>,
    /// <see cref="LedgerState.Flight"/> or <see cref="LedgerState.Backlog"/>), leaving the holder state it was in first, if any (a
    /// flying entry returned to the backlog crosses Flight to Backlog). Called by <c>DetachForThrow</c> (Flight), <c>WorldItems</c>'s
    /// creation (World) and the backlog append (Backlog). Touches no container, version or record. A free or placed entry asserts
    /// <see cref="SimAssertCode.InventoryEntrySlotFree"/> under <c>INVENTORY_ASSERT</c>; Release, and a state that is not an entry
    /// holder, change nothing. O(1).
    /// </summary>
    /// <param name="holder">The holder state.</param>
    /// <param name="entrySlot">The detached entry's pool slot.</param>
    public void EnterHolder(LedgerState holder, ushort entrySlot)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        bool valid = e.IsLive && e.ContainerSlot == EntryRecord.NoContainer;
        InventoryAssert.That(valid, SimAssertCode.InventoryEntrySlotFree);
        if (!valid || (holder != LedgerState.World && holder != LedgerState.Flight && holder != LedgerState.Backlog))
        {
            return;
        }

        LeaveHolder(entrySlot, e.Count);
        _ledger.AddLive(UnitLedger.RowOf(in e), holder, e.Count);
        _ledger.SetHolder(entrySlot, (byte)holder);
    }

    /// <summary>
    /// The holder-exit primitive (story 017, TD-160 item 1): <paramref name="units"/> of the live detached entry in
    /// <paramref name="entrySlot"/> leave the holder state it is in (all of them when <paramref name="units"/> is at least its count,
    /// after which it is in no state until a primitive places it or enters it into a holder). An entry in no holder state (detached
    /// earlier in this call, e.g. by a WorldItem's removal) changes nothing. <see cref="Place"/>, <see cref="MergeInto"/> (a detached
    /// source), <see cref="EnterHolder(LedgerState, ushort)"/> and <see cref="DestroyDetached"/> call it for their source, and
    /// <c>WorldItems</c> calls it when a WorldItem is removed (its entry is then in no state until its caller moves it). Touches no
    /// container, version or record. O(1).
    /// </summary>
    /// <param name="entrySlot">The detached entry's pool slot.</param>
    /// <param name="units">The units that leave (1 to its count).</param>
    public void LeaveHolder(ushort entrySlot, int units)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        byte holder = _ledger.HolderOf(entrySlot);
        if (!e.IsLive || e.ContainerSlot != EntryRecord.NoContainer || holder == UnitLedger.NoHolder)
        {
            return;
        }

        _ledger.AddLive(UnitLedger.RowOf(in e), (LedgerState)holder, -units);
        if (units >= e.Count)
        {
            _ledger.SetHolder(entrySlot, UnitLedger.NoHolder);
        }
    }

    /// <summary>
    /// The holder-entry primitive for a unit no entry holds (story 017, TD-160 item 1): one mail unit enters
    /// <paramref name="holder"/>, which is <see cref="LedgerState.Belt"/> at M-1a (<c>TakeUnit</c>; a <see cref="UnitRef"/> is mail);
    /// any other state changes nothing. O(1).
    /// </summary>
    /// <param name="holder">The holder state (<see cref="LedgerState.Belt"/>).</param>
    /// <param name="unit">The unit (mail).</param>
    public void EnterHolder(LedgerState holder, in UnitRef unit)
    {
        if (holder == LedgerState.Belt && !unit.Mail.IsNone)
        {
            _ledger.AddLive(UnitLedger.MailRow, LedgerState.Belt, 1);
        }
    }

    /// <summary>
    /// The holder-exit primitive for a unit no entry holds (story 017, TD-160 items 1, 7): one mail unit leaves
    /// <paramref name="holder"/> (<see cref="LedgerState.Belt"/>; <c>TryInsert</c>, on true only). Counters cannot refuse a unit that
    /// was never taken: such an insert drives Belt negative, which the tick-end check reports (TD-145, narrowed). O(1).
    /// </summary>
    /// <param name="holder">The holder state (<see cref="LedgerState.Belt"/>).</param>
    /// <param name="unit">The unit (mail).</param>
    public void LeaveHolder(LedgerState holder, in UnitRef unit)
    {
        if (holder == LedgerState.Belt && !unit.Mail.IsNone)
        {
            _ledger.AddLive(UnitLedger.MailRow, LedgerState.Belt, -1);
        }
    }

    /// <summary>
    /// Counts the units of the live entry in <paramref name="entrySlot"/> as entering Inventory custody (story 017; ADR-0017 section 6
    /// l.137; TD-160 item 2): <c>spawned</c> += its count. <c>Spawn</c> calls it per placed unit before its placement commits, and
    /// <c>AppendBacklog</c> once its append succeeded; units a spawn leaves unplaced are Mail's and never counted. O(1).
    /// </summary>
    /// <param name="entrySlot">The new entry's pool slot.</param>
    public void CountSpawned(ushort entrySlot)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        if (e.IsLive)
        {
            _ledger.AddSpawned(UnitLedger.RowOf(in e), e.Count);
        }
    }

    /// <summary>Opens a call over <paramref name="plan"/>: records the actor and cause every seal and the reply carry.</summary>
    /// <param name="plan">The call's plan (its touched set may already hold the containers <c>Validate</c> planned).</param>
    /// <param name="actor">The acting actor.</param>
    /// <param name="cause">The cause.</param>
    public void Begin(ref InvPlan plan, ActorId actor, InvCause cause)
    {
        plan.Actor = actor;
        plan.Cause = cause;
        _departureCount = 0; // a departure counts only within its own call (see the class remarks)
    }

    /// <summary>
    /// Places a detached entry (<c>ContainerSlot == NoContainer</c>) at (<paramref name="x"/>, <paramref name="y"/>) of a container whose
    /// cells <c>Validate</c> proved free, and raises <c>InvInserted</c> plus its mailId runs. A square footprint, and every entry of a
    /// slots layout (one 1 x 1 cell), is stored unrotated. Links the entry into the StackKey index when it is non-full and adds its class
    /// points to the owner slot's weight (story 016; a container with no owner adds nothing). Into a container
    /// with an outfeed it first stamps the entry with the next <c>arrivalSeq</c> and appends its FIFO node (story 012). Its units leave
    /// the holder state the entry is in, if any, and enter the ledger's Cursor or Placed state (story 017). A free or
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
        int weight = _weight.PointsOf(h.OwnerSlot, in e); // before any change, so a Bulk assert leaves nothing half-placed
        LeaveHolder(entrySlot, e.Count); // story 017: Flight, Backlog or World -count (none after a WorldItem's removal)
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
        CountItemUnits(containerSlot, in e, e.Count);
        _ledger.AddLive(UnitLedger.RowOf(in e), StateOf(in h), e.Count);
        _weight.Add(h.OwnerSlot, weight);
        if (HasOutfeed(containerSlot))
        {
            StampArrival(containerSlot, ref h, entrySlot, ref e);
        }

        if (IsCursor(in h))
        {
            _cursorOrigins[h.OwnerSlot] = DepartureOf(e.Id); // the entry's origin, or none (story 010; see the class remarks)
        }

        RaiseInserted(containerSlot, h.Id, in e);
    }

    /// <summary>
    /// Takes a placed entry out of its container (the entry stays allocated and detached; it leaves the StackKey index) and raises
    /// <c>InvRemoved{c, entry, count}</c>. Its class points leave the owner slot's weight (story 016) and its units the ledger's Cursor
    /// or Placed state (story 017; the entry is in no state until a primitive places it or enters it into a holder). An outfeed entry's <c>arrivalSeq</c> is reset to 0 and its FIFO node becomes a lazy
    /// tombstone (the list is compacted when its tombstones exceed its live entries).
    /// </summary>
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
        int weight = _weight.PointsOf(h.OwnerSlot, in e);
        LogDeparture(e.Id, h.Id, e.X, e.Y, e.Rotated);
        if (IsCursor(in h))
        {
            _cursorOrigins[h.OwnerSlot] = default; // the entry leaves the cursor, so its origin goes with it (story 010)
        }

        FootprintIn(in h, in e, e.Rotated, out int fw, out int fh);
        _index.Unlink(entrySlot);
        CountItemUnits(containerSlot, in e, -e.Count);
        _ledger.AddLive(UnitLedger.RowOf(in e), StateOf(in h), -e.Count);
        _weight.Add(h.OwnerSlot, -weight);
        _occupancy.Clear(containerSlot, h.W, h.H, e.X, e.Y, fw, fh);
        h.EntryCount = (ushort)Math.Max(0, h.EntryCount - 1);
        h.FreeCells = (ushort)Math.Min(h.W * h.H, h.FreeCells + (fw * fh));
        e.ContainerSlot = EntryRecord.NoContainer;
        if (e.ArrivalSeq != 0)
        {
            e.ArrivalSeq = 0;
            _outfeed.OnTombstoned(containerSlot, ref h, h.EntryCount);
        }

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
    /// StackKey index by their new counts. A target in a container with an outfeed (the Intake) is restamped with the next
    /// <c>arrivalSeq</c> and re-enqueued at the FIFO tail, so the merged stack leaves last (owner ruling Evan 2026-10-21 12:28 PT; GDD
    /// R14, H-R14-3); its old node becomes a lazy tombstone, counted by the compaction rule like a leave's. The stamp is written before
    /// <c>InvMerged</c> is raised, as <see cref="Place"/> stamps before <c>InvInserted</c>, so the record follows the entry's final state
    /// (<c>InvMerged</c> has no <c>arrivalSeq</c> field). A partly merged source keeps its seq. The source may be in the target's
    /// container (an in-container merge). The containers must
    /// already be touched (<see cref="InvPlan.EnsureTouched"/>). An invalid merge (free or unplaced target, a self-merge, different
    /// <c>StackKey</c>s, <paramref name="units"/> outside 1 to the source's count, a target over 255 or over its <c>effMax</c> read now
    /// (<see cref="EffMaxOf"/>, the value the index uses)) asserts
    /// <see cref="SimAssertCode.InventoryMergeInvalid"/> under <c>INVENTORY_ASSERT</c>; Release changes nothing. Ledger (story 017): the
    /// units leave a placed source's state (Cursor or Placed; the whole-source path through <see cref="Remove"/>) and enter the
    /// target's, so a Placed-to-Placed merge nets nothing; a detached source's merged units leave the holder state it is in, if any
    /// (<see cref="LeaveHolder(ushort, int)"/>; the remainder stays there).
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

        if (from == EntryRecord.NoContainer)
        {
            LeaveHolder(sourceSlot, units); // story 017: before the count changes, so a whole source leaves its holder entirely
        }

        _index.Unlink(targetSlot);
        _entries.SetCount(targetSlot, (byte)(dst.Count + units));
        CountItemUnits(to, in dst, units);
        _ledger.AddLive(UnitLedger.RowOf(in dst), StateOf(in _containers[to]), units);
        if (units == src.Count)
        {
            if (from != EntryRecord.NoContainer)
            {
                Remove(ref plan, sourceSlot); // InvRemoved carries the whole count, which is units
            }

            _ledger.SetHolder(sourceSlot, UnitLedger.NoHolder); // the slot is free: no later entry inherits a holder
            _entries.Free(sourceSlot);
        }
        else
        {
            _index.Unlink(sourceSlot);
            _entries.SetCount(sourceSlot, (byte)(src.Count - units));
            if (from != EntryRecord.NoContainer)
            {
                CountItemUnits(from, in src, -units);
                _ledger.AddLive(UnitLedger.RowOf(in src), StateOf(in _containers[from]), -units);
                plan.MarkMutated(from);
                var removed = new InvRemovedPayload { Container = _containers[from].Id.Value, EntryId = src.Id.Value, Count = (byte)units };
                _ = _events.TryRaise(InvEventKinds.Removed, AudienceOf(from), in removed);
                LinkIfNonFull(sourceSlot);
            }
        }

        RaiseMerged(ref plan, targetSlot, to, units, moved);
    }

    /// <summary>
    /// The count-change primitive for one mail unit that has no entry (story 015: <c>IInventoryApi.TryInsert</c>'s AutoPlace merge
    /// pass): unions <paramref name="mail"/> into the placed target mail stack's run (one arena block acquired, which the caller
    /// reserved through <see cref="InvPlan.AddAcquiredBlock"/> with the union's size, <c>target.count + 1</c>), sets its count and
    /// raises <c>InvMerged{c, target, 1}</c> plus the one-id run, as <see cref="MergeInto"/> does for its target, with the same
    /// StackKey re-link and outfeed restamp. No source entry is created, so no entry id is spent. The container must already be
    /// touched. An invalid merge (a free, unplaced or non-mail target, a none id, a <paramref name="key"/> other than the target's, a
    /// target at 255 or at its <c>effMax</c> read now) asserts <see cref="SimAssertCode.InventoryMergeInvalid"/> under
    /// <c>INVENTORY_ASSERT</c>; Release changes nothing. Ledger (story 017): the unit enters the target's state (Cursor or Placed); the
    /// caller makes its source crossing (<c>TryInsert</c>: Belt, through <see cref="LeaveHolder(LedgerState, in UnitRef)"/>).
    /// </summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="targetSlot">The placed target stack's pool slot.</param>
    /// <param name="key">The unit's StackKey (the one the plan matched).</param>
    /// <param name="mail">The unit's mail id.</param>
    /// <returns>True when the unit was merged.</returns>
    public bool MergeUnitInto(ref InvPlan plan, ushort targetSlot, StackKey key, MailId mail)
    {
        ref EntryRecord dst = ref _entries[targetSlot];
        bool valid = dst.IsLive && dst.IsMail && dst.ContainerSlot != EntryRecord.NoContainer && !mail.IsNone && dst.Count < byte.MaxValue
            && dst.Count + 1 <= EffMaxOf(in dst) && StackKey.Of(in dst) == key;
        InventoryAssert.That(valid, SimAssertCode.InventoryMergeInvalid);
        if (!valid)
        {
            return false; // returns before MarkMutated (in RaiseMerged), as do the two below: a refused merge bumps no version, End seals nothing
        }

        ushort to = dst.ContainerSlot;
        if (!plan.EnsureTouched(to))
        {
            return false; // Release: the set is full, so nothing is changed unsealed
        }

        Span<uint> moved = _movedIds.AsSpan(0, 1);
        moved[0] = mail.Value;
        if (!_mailIds.TryAppend(ref dst.MailIds, moved, 0))
        {
            return false; // unreachable after the caller's reservation; the arena wrote its exhaustion record
        }

        _index.Unlink(targetSlot);
        _entries.SetCount(targetSlot, (byte)(dst.Count + 1));
        _ledger.AddLive(UnitLedger.MailRow, StateOf(in _containers[to]), 1);
        RaiseMerged(ref plan, targetSlot, to, 1, moved);
        return true;
    }

    /// <summary>
    /// The take count-change primitive (story grid-inventory/012, Planner R5; story 015's <c>ConsumeUnit</c> reuses it): takes the
    /// <paramref name="k"/> <b>oldest</b> units of a placed entry. For mail the k lowest mailIds leave the arena run into
    /// <paramref name="taken"/> (an arena take never fails). An entry left at 0 is <see cref="Remove"/>d (its <c>InvRemoved</c> carries
    /// the whole count, which is <paramref name="k"/>) and freed; otherwise its count is set, it is re-linked in the StackKey index and
    /// <c>InvRemoved{c, entry, k}</c> is raised (take-k removes the k lowest, ADR-0009 section 4). The container must already be touched.
    /// An invalid take (a free or unplaced entry, <paramref name="k"/> outside 1 to the count, a short <paramref name="taken"/> for mail)
    /// asserts <see cref="SimAssertCode.InventoryTakeInvalid"/> under <c>INVENTORY_ASSERT</c>; Release changes nothing. Ledger (story
    /// 017): the k units leave the entry's state (Cursor or Placed; the whole-entry path through <see cref="Remove"/>, so they are not
    /// counted twice) and are in no state: the caller makes their target crossing (<see cref="ConsumeOldest"/>, <c>TakeUnit</c>'s Belt,
    /// <see cref="SplitAndPlace"/>'s <see cref="Place"/>).
    /// </summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="entrySlot">The placed entry's pool slot.</param>
    /// <param name="k">How many units, 1 to the entry's count.</param>
    /// <param name="taken">Receives the k taken mailIds, ascending (mail only; may be empty for an item).</param>
    /// <returns>The units taken (0 when refused).</returns>
    public int TakeOldest(ref InvPlan plan, ushort entrySlot, int k, Span<uint> taken)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        bool valid = e.IsLive && e.ContainerSlot != EntryRecord.NoContainer && k >= 1 && k <= e.Count
            && (!e.IsMail || (taken.Length >= k && e.MailIds.Count >= k));
        InventoryAssert.That(valid, SimAssertCode.InventoryTakeInvalid);
        if (!valid)
        {
            return 0;
        }

        ushort containerSlot = e.ContainerSlot;
        if (!plan.EnsureTouched(containerSlot))
        {
            return 0; // Release: the set is full, so nothing is changed unsealed
        }

        if (e.IsMail)
        {
            _ = _mailIds.Take(ref e.MailIds, k, taken, 0);
        }

        if (k == e.Count)
        {
            Remove(ref plan, entrySlot);
            _entries.Free(entrySlot);
            return k;
        }

        _index.Unlink(entrySlot);
        _entries.SetCount(entrySlot, (byte)(e.Count - k));
        CountItemUnits(containerSlot, in e, -k);
        _ledger.AddLive(UnitLedger.RowOf(in e), StateOf(in _containers[containerSlot]), -k);
        plan.MarkMutated(containerSlot);
        var removed = new InvRemovedPayload { Container = _containers[containerSlot].Id.Value, EntryId = e.Id.Value, Count = (byte)k };
        _ = _events.TryRaise(InvEventKinds.Removed, AudienceOf(containerSlot), in removed);
        LinkIfNonFull(entrySlot);
        return k;
    }

    /// <summary>
    /// The consume primitive (story 015's <c>ConsumeUnit</c>, story 017's ledger crossing): <see cref="TakeOldest"/> of 1 (its records,
    /// the last-unit removal and the source state's −1), then the unit crosses to Consumed for <paramref name="reason"/>. The container
    /// must already be touched. A refused take changes nothing (as <see cref="TakeOldest"/>).
    /// </summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="entrySlot">The placed entry's pool slot.</param>
    /// <param name="reason">Why the unit is consumed.</param>
    /// <param name="taken">Receives the taken mailId (mail only).</param>
    /// <returns>The units taken: 1, or 0 when refused.</returns>
    public int ConsumeOldest(ref InvPlan plan, ushort entrySlot, ConsumeReason reason, Span<uint> taken)
    {
        int row = UnitLedger.RowOf(in _entries[entrySlot]); // read first: a last unit frees the entry
        int took = TakeOldest(ref plan, entrySlot, 1, taken);
        if (took == 1)
        {
            _ledger.AddConsumed(row, reason, 1);
        }

        return took;
    }

    /// <summary>
    /// The split primitive (story grid-inventory/008: a <c>move</c> with <c>1 ≤ count &lt; total</c>, GDD R7 move row "atomic
    /// split-and-place (new piece gets a new entryId; remainder keeps the old)"): splits the <paramref name="k"/> <b>oldest</b> units
    /// of a placed entry off into a new entry placed at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="rotated"/>) of
    /// <paramref name="toSlot"/>. In order: the new entry is allocated (<see cref="EntryPool.TryAllocate"/>: the id is allocated here,
    /// in <c>Commit</c>, never in <c>Validate</c>, ADR-0009 section 2) with the source's key and footprint; for mail its block is
    /// created from the k lowest mailIds (<see cref="MailIdArena.TryCreate"/>) <b>before</b> they are taken, the order
    /// <see cref="MergeInto"/> uses, so the take's re-home cannot claim the class <c>Validate</c> reserved; <see cref="TakeOldest"/>
    /// takes them (mail and items; <c>InvRemoved{source container, source, k}</c>, R6); <c>InvSplitFrom{source container, source,
    /// new, k}</c> is raised to the source container's audience; then <see cref="Place"/> places the new entry (<c>InvInserted</c>
    /// plus its runs). The remainder keeps its id, position and <c>arrivalSeq</c>. Weight, the StackKey index and the item counts
    /// follow from <see cref="TakeOldest"/> and <see cref="Place"/>: the new entry adds its class points to the destination owner's P
    /// (PerEntry, even inside one container). Both containers must already be touched. Validate pre-checks the entry pool and the
    /// arena reservation, so the two acquisitions cannot fail; if one does (unreachable), the pool or the arena has written its check
    /// record and this returns false with nothing placed (an entry allocated before an arena refusal is freed; its id stays spent,
    /// ADR-0004). An invalid split (a free or unplaced source, <paramref name="k"/> outside 1 to the count − 1, a mail run shorter than
    /// <paramref name="k"/>) asserts <see cref="SimAssertCode.InventoryTakeInvalid"/> under <c>INVENTORY_ASSERT</c>; Release changes
    /// nothing. Allocation-free (a preallocated id buffer). The ledger crossing (story 017) follows from <see cref="TakeOldest"/> and
    /// <see cref="Place"/>: the k units leave the source's state and enter the destination's.
    /// </summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="sourceSlot">The placed source entry's pool slot.</param>
    /// <param name="k">How many units are split off, 1 to the source's count − 1.</param>
    /// <param name="toSlot">The destination's table slot (may be the source's container).</param>
    /// <param name="x">Top-left column.</param>
    /// <param name="y">Top-left row.</param>
    /// <param name="rotated">The rotation.</param>
    /// <param name="tick">The current tick (for the pool's and the arena's check records).</param>
    /// <returns>True when the new entry was placed.</returns>
    public bool SplitAndPlace(ref InvPlan plan, ushort sourceSlot, int k, ushort toSlot, byte x, byte y, bool rotated, uint tick)
    {
        ref EntryRecord src = ref _entries[sourceSlot];
        bool valid = src.IsLive && src.ContainerSlot != EntryRecord.NoContainer && k >= 1 && k < src.Count
            && (!src.IsMail || src.MailIds.Count >= k);
        InventoryAssert.That(valid, SimAssertCode.InventoryTakeInvalid);
        if (!valid)
        {
            return false;
        }

        ushort from = src.ContainerSlot;
        if (!plan.EnsureTouched(from) || !plan.EnsureTouched(toSlot))
        {
            return false; // Release: Validate broke the touch contract and the set is full, so nothing is changed unsealed
        }

        if (!_entries.TryAllocate(tick, out ushort pieceSlot, out EntryId pieceId))
        {
            return false; // unreachable after Validate's pool pre-check; the pool wrote its check record
        }

        ref EntryRecord piece = ref _entries[pieceSlot];
        Span<uint> ids = _splitIds.AsSpan(0, src.IsMail ? k : 0);
        if (src.IsMail)
        {
            _mailIds.Read(in src.MailIds)[..k].CopyTo(ids);
            if (!_mailIds.TryCreate(ids, tick, out piece.MailIds))
            {
                _entries.Free(pieceSlot); // unreachable after Validate's reservation; the arena wrote its exhaustion record
                return false;
            }
        }

        piece.IsMail = src.IsMail;
        piece.MailKind = src.MailKind;
        piece.ItemId = src.ItemId;
        piece.AddressId = src.AddressId;
        piece.Flags = src.Flags;
        piece.W = src.W;
        piece.H = src.H;
        _entries.SetCount(pieceSlot, (byte)k);
        _ = TakeOldest(ref plan, sourceSlot, k, ids); // k < count: the source stays, with its id and position
        var split = new InvSplitFromPayload { Container = _containers[from].Id.Value, EntryId = src.Id.Value, NewEntryId = pieceId.Value, Count = (byte)k };
        _ = _events.TryRaise(InvEventKinds.SplitFrom, AudienceOf(from), in split);
        LogDeparture(pieceId, _containers[from].Id, src.X, src.Y, src.Rotated); // the piece's origin is its source's cells (TD-153)
        Place(ref plan, pieceSlot, toSlot, x, y, rotated);
        return true;
    }

    /// <summary>
    /// Rebuilds the container's outfeed FIFO from its live entries, ascending by <c>arrivalSeq</c> (after a load; the M1 Intake sort).
    /// Changes no Sim state (a derived memo): no record, no version.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    public void RebuildOutfeed(ushort containerSlot) => _outfeed.Rebuild(containerSlot, ref _containers[containerSlot]);

    /// <summary>
    /// Rebuilds the container's StackKey lists from its entries (every placed entry whose count is below its <c>effMax</c> read now,
    /// row-major) and its <c>itemId → units</c> totals (story 015), and clears its negative-fit cache. Every entry linked under the
    /// container is unlinked first (<see cref="Placement.StackIndex.ClearContainer"/>) and its item totals deleted
    /// (<see cref="Placement.ItemCountIndex.ClearContainer"/>), so a stale link or total whose cells were cleared outside the mutator
    /// does not survive. Called when the container's <c>statEpoch</c> bumps (a <c>MailMaxStack</c> or <c>InventoryRows</c> change, R15; nothing
    /// bumps it at M-1a, tests call it), after a load, and by tests that arranged entries outside the mutator. Changes no Sim state
    /// (all three are derived memos): no record, no version. O(entry pool + item table + cells); allocation-free.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    public void RebuildIndex(ushort containerSlot)
    {
        ref ContainerHeader h = ref _containers[containerSlot];
        _index.ClearContainer(containerSlot);
        _itemCounts.ClearContainer(containerSlot);
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
                    CountItemUnits(containerSlot, in e, e.Count);
                }
            }
        }

        _noFit.Clear(containerSlot);
    }

    /// <summary>
    /// Recounts <paramref name="slot"/>'s weight points from the entry pool (story 016: the load path, beside <see cref="RebuildIndex"/>;
    /// TD-146). Changes no Sim state (a derived memo): no record, no version. O(entry pool); allocation-free.
    /// </summary>
    /// <param name="slot">The player slot.</param>
    public void RebuildWeight(PlayerSlot slot)
    {
        if (!slot.IsNone && slot.Value < PlayerSlot.Count)
        {
            _weight.Rebuild(slot.Value, _containers, _entries);
        }
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

    /// <summary>
    /// The cursor origin of player slot <paramref name="slot"/> (story 010; see the class remarks): present exactly while that slot's
    /// cursor holds the entry it was recorded for, because <see cref="Place"/> into the cursor records it and <see cref="Remove"/> from the
    /// cursor clears it. Pure; allocation-free.
    /// </summary>
    /// <param name="slot">The player slot value (below <see cref="PlayerSlot.Count"/>).</param>
    /// <param name="origin">The origin when present; default otherwise.</param>
    /// <returns>True when an origin is recorded.</returns>
    internal bool TryGetCursorOrigin(byte slot, out InventorySystem.CursorOrigin origin)
    {
        origin = _cursorOrigins[slot];
        return !origin.Entry.IsNone;
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

    /// <summary>True for a player's cursor: a container of the cursor def with an owner slot.</summary>
    private bool IsCursor(in ContainerHeader h) => h.DefIndex == _cursorDef && h.OwnerSlot < PlayerSlot.Count;

    /// <summary>The ledger state of units placed in the container: <see cref="LedgerState.Cursor"/> on a player's cursor, else <see cref="LedgerState.Placed"/>.</summary>
    private LedgerState StateOf(in ContainerHeader h) => IsCursor(in h) ? LedgerState.Cursor : LedgerState.Placed;

    /// <summary>Appends where <paramref name="entry"/> left from in this call to the departure ring (the oldest is overwritten).</summary>
    private void LogDeparture(EntryId entry, ContainerId container, byte x, byte y, bool rotated)
    {
        _departures[_departureCount % DepartureCapacity] = new InventorySystem.CursorOrigin
        {
            Entry = entry, Container = container, X = x, Y = y, Rotated = rotated,
        };
        _departureCount++;
    }

    /// <summary>The latest departure of <paramref name="entry"/> in this call, or default (no origin) when it left nothing this call.</summary>
    private InventorySystem.CursorOrigin DepartureOf(EntryId entry)
    {
        int oldest = Math.Max(0, _departureCount - DepartureCapacity);
        for (int i = _departureCount - 1; i >= oldest; i--)
        {
            ref readonly InventorySystem.CursorOrigin d = ref _departures[i % DepartureCapacity];
            if (d.Entry == entry)
            {
                return d;
            }
        }

        return default;
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

    /// <summary>
    /// Stamps a placed entry of an outfeed container with the container's next <c>arrivalSeq</c> and appends its FIFO node at the tail
    /// (an insertion, or a merge onto the entry; a node the entry held before is stale from now on).
    /// </summary>
    private void StampArrival(ushort containerSlot, ref ContainerHeader h, ushort entrySlot, ref EntryRecord e)
    {
        e.ArrivalSeq = h.NextArrivalSeq;
        h.NextArrivalSeq = unchecked(h.NextArrivalSeq + 1u); // u32 per container; never wraps within a run
        _ = _outfeed.Append(containerSlot, ref h, e.ArrivalSeq, entrySlot, e.Id.Value);
    }

    /// <summary>Adds <paramref name="delta"/> units of an item entry's def to the container's <c>itemId → units</c> total; mail counts nothing.</summary>
    private void CountItemUnits(ushort containerSlot, in EntryRecord e, int delta)
    {
        if (!e.IsMail)
        {
            _itemCounts.Add(containerSlot, e.ItemId, delta);
        }
    }

    /// <summary>
    /// The target half of a merge whose units are already in the target's count and run: marks the container mutated, re-links the
    /// stack, restamps it on an outfeed container, then raises <c>InvMerged</c> and the moved ids' runs.
    /// </summary>
    private void RaiseMerged(ref InvPlan plan, ushort targetSlot, ushort to, int units, ReadOnlySpan<uint> moved)
    {
        ref EntryRecord dst = ref _entries[targetSlot];
        plan.MarkMutated(to);
        LinkIfNonFull(targetSlot);
        if (HasOutfeed(to))
        {
            ref ContainerHeader th = ref _containers[to];
            StampArrival(to, ref th, targetSlot, ref dst); // its old node is now stale: a tombstone
            _outfeed.OnTombstoned(to, ref th, th.EntryCount);
        }

        byte audience = AudienceOf(to);
        var merged = new InvMergedPayload
        {
            Container = _containers[to].Id.Value, TargetEntryId = dst.Id.Value, AddedCount = (byte)units, RunCount = (ushort)CountRuns(moved),
        };
        _ = _events.TryRaise(InvEventKinds.Merged, audience, in merged);
        RaiseRuns(InvEventKinds.Merged, audience, moved);
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
