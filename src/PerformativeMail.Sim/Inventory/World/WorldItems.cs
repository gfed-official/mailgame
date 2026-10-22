using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.World;

/// <summary>
/// Grid Inventory's WorldItems (GDD R12, R7 tombstones, D6; ADR-0009 section 5; TR-inventory-013, TR-inventory-014; story
/// grid-inventory/013): the <see cref="WorldItemTable"/>, the <see cref="ExpiryFifos"/>, the <see cref="Tombstones"/> and the global
/// <c>worldSeq</c>. It creates WorldItems (<see cref="Create"/>, behind <c>IInventoryApi.CreateWorldItem</c>), runs phase 1
/// <c>ItemsExpire</c> (<see cref="Expire"/>), evicts at the soft cap and leaves a tombstone for every removal. Every record goes through
/// the <see cref="InventoryMutator"/> (the world kinds to the connected-slot mask, <see cref="ReplicationClass.Replicated"/>).
/// <list type="bullet">
/// <item><b>Timers</b> are absolute ticks: <c>expiresTick = createdTick + seconds x 30</c> (D6) from the caller's <c>createdTick</c> (the
/// landing tick for a throw), so a pause that stops ticks moves nothing. Durations: <c>mailWorldItem</c> and <c>nonMailWorldItem</c>
/// <c>inventory.worldItemDespawnSeconds</c> (9000 ticks), <c>waterFloat</c> <c>inventory.waterReturnSeconds</c> (300), <c>bag</c> 900
/// (30 s, M-1b's <c>deathBagDespawnSeconds</c> has no key yet). Balance values are ms; ticks = <c>ms x 30 / 1000</c>, rounded down.
/// Loot (<see cref="WorldItemOrigin.Loot"/>) is on no FIFO: it never expires here and is never a cap candidate.</item>
/// <item><b>Expiry</b> (phase 1, after <c>InputsApply</c>, so a pickup on the expiry tick wins): the FIFO heads with
/// <c>expiresTick &lt;= now</c> are popped in ascending <c>(expiresTick, worldItemId)</c> across all FIFOs, one call scope each. Mail
/// returns to the Intake whole (<c>InventorySystem</c>'s return: NoMerge, a new <c>arrivalSeq</c>, back of the outfeed queue; seal
/// cause <c>Expired</c>); non-mail is destroyed with <c>ItemsLost{itemDef, count}</c>. Water floats are the same rule at 300 ticks.</item>
/// <item><b>Cap</b> (<c>inventory.worldItemCap</c>, 512; a WorldItem counts once): a creation that takes the live count over the cap
/// evicts once, in the creation's call scope: the <c>mailWorldItem</c> head if at least <c>capEvictMinAgeSeconds</c> (300 ticks) old
/// ("older than 10 s" is read as "10 s or more have elapsed": a letter created at tick c qualifies from tick c + 300), returned to the
/// Intake losslessly; otherwise the lower <c>(expiresTick, id)</c> of the two non-mail heads (<c>nonMailWorldItem</c>,
/// <c>waterFloat</c> non-mail), destroyed with <c>ItemsLost</c>. The new item is never the candidate. If nothing qualifies the count
/// stays over the cap: <see cref="CounterId.InventoryWorldItemCapExceeded"/> counts the creation, one
/// <see cref="CheckCode.InventoryWorldItemCapExceeded"/> record is written when the count first goes over (latched until it is back at
/// or under the cap; the repo's always-on counter + check-record convention for runtime warnings), and each <c>ItemsExpire</c> re-runs
/// the eviction while over, so the first item to qualify (e.g. the first young letter to reach 10 s) is evicted on that tick.
/// <see cref="CounterId.InventoryWorldItemCapCandidates"/> counts the eviction path's candidate visits (see <see cref="ExpiryFifos"/>).</item>
/// <item><b>Interim rule until story 021 (backlog):</b> when the Intake cannot take a returning mail entry (full, or not accepting),
/// the mail WorldItem is <b>not</b> removed and nothing is lost: on expiry it stays at its FIFO head (the FIFO is skipped for the rest
/// of that pass, so its order is kept) and is retried on the next <c>ItemsExpire</c>; as a cap candidate it is not evicted and the
/// non-mail rule applies, else the cap is exceeded as above. Each failed return counts
/// <see cref="CounterId.InventoryWorldItemReturnRetries"/>; a repeat of the same FIFO's head against the same Intake version in the
/// same tick is not attempted (or counted) again. Story 021 replaces this with the backlog append.</item>
/// <item><b>Tombstones</b>: every removal (picked up <c>Taken</c>, mail returned on expiry or eviction <c>Expired</c>, non-mail
/// destroyed <c>Destroyed</c>) raises <c>WorldItemDespawned</c> and leaves <c>(id, byActorId, cause, worldSeq, tick)</c>;
/// <see cref="Resolve"/> answers <c>container_gone</c> with them for 30 s, then <c>stale_entry</c>.</item>
/// </list>
/// Allocation-free after construction; never throws on the tick path. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// EntityId w = worldItems.Create(entryId, pos, WorldItemOrigin.Dropped, onWater: false, createdTick: tick);
/// worldItems.Expire(tick);   // phase 1
/// </code>
/// </example>
internal sealed class WorldItems
{
    private readonly InventorySystem _system;
    private readonly InventoryMutator _mutator;
    private readonly EntryPool _entries;
    private readonly EventLogFacade _events;
    private readonly ITickReader _tick;
    private readonly EntityIds? _ids;
    private readonly SimCheckRing _checkRing;
    private readonly SimCounters _counters;
    private readonly WorldItemTable _table;
    private readonly ExpiryFifos _fifos;
    private readonly Tombstones _tombstones = new();
    private readonly ushort[] _worldSlotOfEntry;
    private readonly uint[] _durationTicks = new uint[ExpiryFifos.Count];
    private readonly int _cap;
    private readonly uint _minAgeTicks;

    // Per FIFO: the last creation's createdTick. TODO(load story): not dumped (nor was the global tick it replaces); a load must
    // restore it, or rebuild it from each FIFO's tail.
    private readonly uint[] _lastCreatedTick = new uint[ExpiryFifos.Count];

    // Per FIFO: the failed-return memo (tick, Intake version, entry slot), interim until story 021.
    private readonly uint[] _blockedTick = new uint[ExpiryFifos.Count];
    private readonly uint[] _blockedIntakeVersion = new uint[ExpiryFifos.Count];
    private readonly ushort[] _blockedEntry = new ushort[ExpiryFifos.Count];
    private uint _worldSeq;
    private bool _overCapLogged;

    /// <summary>Creates the WorldItem state.</summary>
    /// <param name="system">The owning module (the Intake return and its version).</param>
    /// <param name="mutator">The mutation layer.</param>
    /// <param name="entries">The entry pool.</param>
    /// <param name="balance">The inventory balance (durations and the cap, read once here).</param>
    /// <param name="capacity">WorldItem slots (<c>inventory.worldItemCapacity</c>).</param>
    /// <param name="events">The event-log writer (call scopes).</param>
    /// <param name="tick">The tick reader.</param>
    /// <param name="ids">The world's entity id allocator; null creates no WorldItem (a module built without one).</param>
    /// <param name="checkRing">The check ring.</param>
    /// <param name="counters">The world's counters.</param>
    internal WorldItems(
        InventorySystem system,
        InventoryMutator mutator,
        EntryPool entries,
        InventoryBalance balance,
        int capacity,
        EventLogFacade events,
        ITickReader tick,
        EntityIds? ids,
        SimCheckRing checkRing,
        SimCounters counters)
    {
        _system = system;
        _mutator = mutator;
        _entries = entries;
        _events = events;
        _tick = tick;
        _ids = ids;
        _checkRing = checkRing;
        _counters = counters;
        _table = new WorldItemTable(capacity);
        _fifos = new ExpiryFifos(capacity, counters);
        _worldSlotOfEntry = new ushort[entries.Capacity];
        Array.Fill(_worldSlotOfEntry, WorldItemTable.NoSlot);
        uint ground = MsToTicks(balance.WorldItemDespawnSeconds);
        uint water = MsToTicks(balance.WaterReturnSeconds);
        _durationTicks[(int)ExpiryFifo.MailWorldItem] = ground;
        _durationTicks[(int)ExpiryFifo.NonMailWorldItem] = ground;
        _durationTicks[(int)ExpiryFifo.WaterFloatMail] = water;
        _durationTicks[(int)ExpiryFifo.WaterFloatNonMail] = water;
        _durationTicks[(int)ExpiryFifo.Bag] = BagDespawnTicks;
        _minAgeTicks = MsToTicks(balance.CapEvictMinAgeSeconds);
        _cap = balance.WorldItemCap;
        Array.Fill(_blockedTick, uint.MaxValue);
        Array.Fill(_blockedEntry, WorldItemTable.NoSlot);
    }

    /// <summary>The bag duration (30 s; M-1b adds <c>deathBagDespawnSeconds</c>).</summary>
    public const uint BagDespawnTicks = 30u * SimSettings.TickRateHz;

    /// <summary>The table (dump, tests).</summary>
    internal WorldItemTable Table => _table;

    /// <summary>The FIFOs (dump, tests).</summary>
    internal ExpiryFifos Fifos => _fifos;

    /// <summary>The tombstones (dump, tests).</summary>
    internal Tombstones Tombstones => _tombstones;

    /// <summary>The global <c>worldSeq</c>: the value carried by the last WorldItem record (0 before the first).</summary>
    public uint WorldSeq => _worldSeq;

    /// <summary>The live WorldItem count (what the cap counts).</summary>
    public int LiveCount => _table.LiveCount;

    /// <summary>The soft cap.</summary>
    public int Cap => _cap;

    /// <summary>The duration of <paramref name="fifo"/>, ticks.</summary>
    /// <param name="fifo">The FIFO.</param>
    /// <returns>Ticks.</returns>
    public uint DurationTicks(ExpiryFifo fifo) => _durationTicks[(int)fifo];

    /// <summary>True when the entry in <paramref name="entrySlot"/> is held by a live WorldItem.</summary>
    /// <param name="entrySlot">The entry's pool slot.</param>
    /// <returns>True when owned.</returns>
    public bool OwnsEntry(ushort entrySlot) => entrySlot < _worldSlotOfEntry.Length && _worldSlotOfEntry[entrySlot] != WorldItemTable.NoSlot;

    /// <summary>
    /// Creates a WorldItem holding the detached entry <paramref name="entry"/> (see <c>IInventoryApi.CreateWorldItem</c>). None, with
    /// nothing changed, outside a tick phase, without an id allocator, for a reserved origin, a <paramref name="createdTick"/> after
    /// now, an entry that is not live and detached or already in a WorldItem, a <paramref name="createdTick"/> before the last
    /// creation's on the same FIFO (<see cref="SimAssertCode.InventoryWorldItemCreatedOutOfOrder"/> under <c>INVENTORY_ASSERT</c>; Loot,
    /// on no FIFO, neither checks nor moves it), a full table (one <see cref="CheckCode.InventoryWorldItemCapacity"/> record) or an id
    /// refusal.
    /// </summary>
    public EntityId Create(EntryId entry, Vec3Mm posMm, WorldItemOrigin origin, bool onWater, uint createdTick)
    {
        if (!TickGuard.IsMutable(_tick, _counters) || _ids is null)
        {
            return EntityId.None;
        }

        uint now = _tick.CurrentTick;
        if ((origin != WorldItemOrigin.Dropped && origin != WorldItemOrigin.Thrown && origin != WorldItemOrigin.Loot)
            || createdTick > now
            || !_entries.TryGetSlot(entry, out ushort entrySlot) || _entries[entrySlot].ContainerSlot != EntryRecord.NoContainer
            || OwnsEntry(entrySlot))
        {
            return EntityId.None;
        }

        bool isMail = _entries[entrySlot].IsMail;
        ExpiryFifo fifo = origin == WorldItemOrigin.Loot ? ExpiryFifo.None
            : onWater ? (isMail ? ExpiryFifo.WaterFloatMail : ExpiryFifo.WaterFloatNonMail)
            : isMail ? ExpiryFifo.MailWorldItem : ExpiryFifo.NonMailWorldItem;
        if (fifo != ExpiryFifo.None && createdTick < _lastCreatedTick[(int)fifo])
        {
            InventoryAssert.That(false, SimAssertCode.InventoryWorldItemCreatedOutOfOrder);
            return EntityId.None; // would break the FIFO's expiresTick order
        }

        if (_table.LiveCount == _table.Capacity)
        {
            _checkRing.Record(CheckCode.InventoryWorldItemCapacity, now, _table.Capacity, _table.LiveCount);
            return EntityId.None;
        }

        EntityId id = _ids.Allocate(EntityClass.WorldItem);
        if (id.IsNone || !_table.TryAdd(id, out ushort slot))
        {
            return EntityId.None; // the allocator wrote its record; TryAdd cannot fail after the capacity check
        }

        ref WorldItemRecord w = ref _table[slot];
        w.EntrySlot = entrySlot;
        w.PosMm = posMm;
        w.CreatedTick = createdTick;
        w.ExpiresTick = fifo == ExpiryFifo.None ? 0u : createdTick + _durationTicks[(int)fifo];
        w.Origin = origin;
        w.OnWater = onWater;
        w.IsMail = isMail;
        w.Fifo = fifo;
        _worldSlotOfEntry[entrySlot] = slot;
        if (fifo != ExpiryFifo.None)
        {
            _lastCreatedTick[(int)fifo] = createdTick;
            if (!_fifos.Append(fifo, _table, id.Value, slot, w.ExpiresTick))
            {
                InventoryAssert.That(false, SimAssertCode.InventoryWorldItemFifoFull);
                w.Fifo = ExpiryFifo.None; // Release: unreachable (each ring holds the table's capacity); the item lives on no FIFO
            }
        }

        using (EventCallScope scope = _events.BeginCall())
        {
            RaiseSpawned(slot);
            if (_table.LiveCount > _cap)
            {
                _ = TryEvictOne(now, slot);
            }

            if (_table.LiveCount > _cap)
            {
                _counters.Add(CounterId.InventoryWorldItemCapExceeded); // nothing qualified: over the cap until an item ages
            }
        }

        UpdateOverCapLog(now);
        return id;
    }

    /// <summary>
    /// Phase 1 <c>ItemsExpire</c> at <paramref name="now"/>: pops every due head in ascending <c>(expiresTick, id)</c> across the FIFOs
    /// (one call scope per item; a mail head the Intake cannot take blocks its FIFO for this pass, interim until story 021), then, while
    /// over the cap, evicts one qualifying candidate per call scope until none qualifies.
    /// </summary>
    /// <param name="now">The tick being run.</param>
    public void Expire(uint now)
    {
        int blocked = 0; // bit f: FIFO f's mail head could not return this pass
        while (true)
        {
            int best = -1;
            ushort bestSlot = WorldItemTable.NoSlot;
            for (int f = 0; f < ExpiryFifos.Count; f++)
            {
                if ((blocked & (1 << f)) != 0 || !_fifos.TryPeekHead((ExpiryFifo)f, _table, countVisits: false, out ushort s))
                {
                    continue;
                }

                ref WorldItemRecord r = ref _table[s];
                if (r.ExpiresTick > now)
                {
                    continue;
                }

                if (best < 0 || r.ExpiresTick < _table[bestSlot].ExpiresTick
                    || (r.ExpiresTick == _table[bestSlot].ExpiresTick && r.Id.Value < _table[bestSlot].Id.Value))
                {
                    best = f;
                    bestSlot = s;
                }
            }

            if (best < 0)
            {
                break;
            }

            using EventCallScope scope = _events.BeginCall();
            if (_table[bestSlot].IsMail)
            {
                if (!TryReturnMail(bestSlot, (ExpiryFifo)best, now))
                {
                    blocked |= 1 << best; // interim until story 021: stays at its head, retried next ItemsExpire
                }
            }
            else
            {
                DestroyNonMail(bestSlot, (ExpiryFifo)best, now);
            }
        }

        while (_table.LiveCount > _cap)
        {
            using EventCallScope scope = _events.BeginCall();
            if (!TryEvictOne(now, WorldItemTable.NoSlot))
            {
                break;
            }
        }

        UpdateOverCapLog(now);
    }

    /// <summary>
    /// Removes a live WorldItem and hands its entry back detached (the story 014 pickup hook): raises <c>WorldItemDespawned</c> with
    /// <paramref name="actor"/> and <paramref name="cause"/> and leaves the tombstone. Runs in the caller's call scope (the pickup
    /// handler's), opening none. False, changing nothing, outside a tick phase or for an id that is not live.
    /// </summary>
    /// <param name="id">The WorldItem.</param>
    /// <param name="actor">The actor credited (the picker).</param>
    /// <param name="cause">The cause (<see cref="InvCause.Taken"/> for a pickup).</param>
    /// <param name="entrySlot">The entry's pool slot, detached and now the caller's.</param>
    /// <returns>True when removed.</returns>
    public bool Remove(EntityId id, ActorId actor, InvCause cause, out ushort entrySlot)
    {
        entrySlot = 0;
        if (!TickGuard.IsMutable(_tick, _counters) || !_table.TryGetSlot(id, out ushort slot))
        {
            return false;
        }

        entrySlot = _table[slot].EntrySlot;
        RemoveCore(slot, actor, cause, _tick.CurrentTick);
        UpdateOverCapLog(_tick.CurrentTick);
        return true;
    }

    /// <summary>
    /// Resolves a WorldItem id named by an op (the step a later pickup op's <c>Validate</c> calls; pure): 0 and its slot when live;
    /// <c>bad_request</c> (8) for an id that is not of class WorldItem or was never allocated; <c>container_gone</c> (82) with the
    /// tombstone's <paramref name="byActorId"/> and <paramref name="cause"/> within 30 s of its removal; else <c>stale_entry</c> (67).
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="slot">The table slot when live.</param>
    /// <param name="byActorId">Who removed it (container_gone only).</param>
    /// <param name="cause">Why (container_gone only).</param>
    /// <returns>The reason, 0 when live.</returns>
    public ushort Resolve(EntityId id, out ushort slot, out uint byActorId, out InvCause cause)
    {
        byActorId = 0;
        cause = InvCause.None;
        if (_table.TryGetSlot(id, out slot))
        {
            return 0;
        }

        if (id.Class != EntityClass.WorldItem || id.Counter == 0u || _ids is null || id.Counter > _ids.Peek(EntityClass.WorldItem))
        {
            return CoreReason.BadRequest;
        }

        if (_tombstones.TryResolve(id.Value, _tick.CurrentTick, out Tombstone t))
        {
            byActorId = t.ByActorId;
            cause = t.Cause;
            return (ushort)InvReason.ContainerGone;
        }

        return (ushort)InvReason.StaleEntry;
    }

    /// <summary>
    /// Writes the WorldItem state into the inventory dump section: <c>worldSeq</c>, the live count, each live item in ascending id
    /// (id, entryId, x, y, z, createdTick, expiresTick, origin u8, fifo u8, onWater u8), then for each FIFO its live count and ids in
    /// queue order, then the tombstones (<see cref="Tombstones.Write"/>).
    /// </summary>
    /// <param name="w">The dump writer.</param>
    public void Write(ref CanonicalWriter w)
    {
        w.WriteU32(_worldSeq);
        w.WriteU32((uint)_table.LiveCount);
        for (ushort s = _table.FirstSlot; s != WorldItemTable.NoSlot; s = _table[s].NextSlot)
        {
            ref readonly WorldItemRecord r = ref _table[s];
            w.WriteU32(r.Id.Value);
            w.WriteU32(_entries[r.EntrySlot].Id.Value);
            w.WriteU32(unchecked((uint)r.PosMm.X));
            w.WriteU32(unchecked((uint)r.PosMm.Y));
            w.WriteU32(unchecked((uint)r.PosMm.Z));
            w.WriteU32(r.CreatedTick);
            w.WriteU32(r.ExpiresTick);
            w.WriteU8((byte)r.Origin);
            w.WriteU8((byte)r.Fifo);
            w.WriteU8(r.OnWater ? (byte)1 : (byte)0);
        }

        for (int f = 0; f < ExpiryFifos.Count; f++)
        {
            w.WriteU32((uint)_fifos.LiveCount((ExpiryFifo)f, _table));
            _fifos.WriteLiveIds((ExpiryFifo)f, _table, ref w);
        }

        _tombstones.Write(ref w);
    }

    /// <summary>Balance ms to ticks at 30 Hz, rounded down.</summary>
    private static uint MsToTicks(int ms) => (uint)((long)Math.Max(0, ms) * SimSettings.TickRateHz / 1000L);

    /// <summary>
    /// One cap eviction (in the caller's call scope): the mail head if old enough and the Intake takes it, else the lower
    /// <c>(expiresTick, id)</c> non-mail head; never <paramref name="exclude"/> (the item just created). False when nothing qualifies.
    /// </summary>
    private bool TryEvictOne(uint now, ushort exclude)
    {
        if (_fifos.TryPeekHead(ExpiryFifo.MailWorldItem, _table, countVisits: true, out ushort mail) && mail != exclude
            && now - _table[mail].CreatedTick >= _minAgeTicks && TryReturnMail(mail, ExpiryFifo.MailWorldItem, now))
        {
            return true; // lossless: back of the Intake queue
        }

        bool hasA = _fifos.TryPeekHead(ExpiryFifo.NonMailWorldItem, _table, countVisits: true, out ushort a) && a != exclude;
        bool hasB = _fifos.TryPeekHead(ExpiryFifo.WaterFloatNonMail, _table, countVisits: true, out ushort b) && b != exclude;
        if (!hasA && !hasB)
        {
            return false;
        }

        bool pickA = hasA && (!hasB || _table[a].ExpiresTick < _table[b].ExpiresTick
            || (_table[a].ExpiresTick == _table[b].ExpiresTick && _table[a].Id.Value < _table[b].Id.Value));
        DestroyNonMail(pickA ? a : b, pickA ? ExpiryFifo.NonMailWorldItem : ExpiryFifo.WaterFloatNonMail, now);
        return true;
    }

    /// <summary>
    /// Returns the mail WorldItem at the head of <paramref name="fifo"/> to the Intake (cause <c>Expired</c>) in the caller's call
    /// scope, then removes it and pops its node. False, changing nothing, when the Intake cannot take it (interim until story 021: the
    /// item stays; one <see cref="CounterId.InventoryWorldItemReturnRetries"/>, not repeated against the same Intake version this tick;
    /// the memo is per FIFO, so each blocked mail head is tried at most once per tick and Intake version).
    /// </summary>
    private bool TryReturnMail(ushort slot, ExpiryFifo fifo, uint now)
    {
        int f = (int)fifo;
        ushort entrySlot = _table[slot].EntrySlot;
        uint intakeVersion = _system.IntakeVersion;
        if (_blockedTick[f] == now && _blockedIntakeVersion[f] == intakeVersion && _blockedEntry[f] == entrySlot)
        {
            return false;
        }

        var plan = new InvPlan();
        if (!_system.TryPlanReturnToIntake(entrySlot, ref plan, out AutoPlacePlan ap))
        {
            _counters.Add(CounterId.InventoryWorldItemReturnRetries);
            _blockedTick[f] = now;
            _blockedIntakeVersion[f] = intakeVersion;
            _blockedEntry[f] = entrySlot;
            return false;
        }

        RemoveCore(slot, ActorId.System, InvCause.Expired, now);
        _fifos.Pop(fifo);
        _system.CommitReturnToIntake(ref plan, in ap, ActorId.System, InvCause.Expired);
        return true;
    }

    /// <summary>Destroys the non-mail WorldItem at the head of <paramref name="fifo"/>: <c>WorldItemDespawned</c> (Destroyed), <c>ItemsLost</c>, the entry freed.</summary>
    private void DestroyNonMail(ushort slot, ExpiryFifo fifo, uint now)
    {
        uint id = _table[slot].Id.Value;
        ushort entrySlot = _table[slot].EntrySlot;
        ref EntryRecord e = ref _entries[entrySlot];
        var lost = new ItemsLostPayload { WorldItemId = id, ItemId = e.ItemId, Count = e.Count, Cause = (byte)InvCause.Destroyed };

        // TODO(story-017): the conservation ledger hook for destroyed non-mail items.
        RemoveCore(slot, ActorId.System, InvCause.Destroyed, now);
        _fifos.Pop(fifo);
        _mutator.RaiseWorld(InvEventKinds.ItemsLost, in lost);
        _mutator.DestroyDetached(entrySlot);
    }

    /// <summary>Raises <c>WorldItemDespawned</c> with the next <c>worldSeq</c>, leaves the tombstone and frees the table slot (the entry stays allocated).</summary>
    private void RemoveCore(ushort slot, ActorId actor, InvCause cause, uint now)
    {
        ref WorldItemRecord r = ref _table[slot];
        uint id = r.Id.Value;
        _worldSeq++;
        var despawned = new WorldItemDespawnedPayload { WorldItemId = id, WorldSeq = _worldSeq, ByActorId = actor.Value, Cause = (byte)cause };
        _mutator.RaiseWorld(InvEventKinds.WorldItemDespawned, in despawned);
        _tombstones.Add(id, actor.Value, cause, _worldSeq, now);
        _fifos.OnRemoved(r.Fifo, id);
        _worldSlotOfEntry[r.EntrySlot] = WorldItemTable.NoSlot;
        _table.Remove(slot);
    }

    private void RaiseSpawned(ushort slot)
    {
        ref WorldItemRecord r = ref _table[slot];
        ref EntryRecord e = ref _entries[r.EntrySlot];
        _worldSeq++;
        var spawned = new WorldItemSpawnedPayload
        {
            WorldItemId = r.Id.Value,
            WorldSeq = _worldSeq,
            EntryId = e.Id.Value,
            AddressId = e.AddressId,
            X = r.PosMm.X,
            Y = r.PosMm.Y,
            Z = r.PosMm.Z,
            CreatedTick = r.CreatedTick,
            ExpiresTick = r.ExpiresTick,
            MailKind = e.MailKind,
            ItemId = e.ItemId,
            Count = e.Count,
            Origin = (byte)r.Origin,
            MailFlags = e.Flags,
            Bits = (byte)((e.IsMail ? WorldItemSpawnedPayload.IsMailBit : 0) | (r.OnWater ? WorldItemSpawnedPayload.OnWaterBit : 0)),
        };
        _mutator.RaiseWorld(InvEventKinds.WorldItemSpawned, in spawned);
    }

    /// <summary>One <see cref="CheckCode.InventoryWorldItemCapExceeded"/> record (a = live, b = cap) when the count first goes over the cap; re-armed once it is back at or under.</summary>
    private void UpdateOverCapLog(uint now)
    {
        if (_table.LiveCount <= _cap)
        {
            _overCapLogged = false;
            return;
        }

        if (!_overCapLogged)
        {
            _checkRing.Record(CheckCode.InventoryWorldItemCapExceeded, now, _table.LiveCount, _cap);
            _overCapLogged = true;
        }
    }
}
