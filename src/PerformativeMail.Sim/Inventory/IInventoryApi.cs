using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>The outfeed selector of <see cref="IInventoryApi.PeekOutfeed"/> and <see cref="IInventoryApi.TakeUnit"/> (ADR-0009 Key Interfaces). <c>Street</c> (Presort) arrives with M-1b.</summary>
public enum Selector : byte
{
    /// <summary>The Intake outfeed: the lowest mailId of the live entry with the lowest <c>arrivalSeq</c> (GDD R14).</summary>
    Fifo = 0,
}

/// <summary>
/// The result of <see cref="IInventoryApi.Spawn"/>: how many ids were placed, in span order, before the first id that found no cell
/// or that the entry pool or arena refused (nothing was mutated for that id). The unplaced remainder is <c>ids[Placed..]</c> in order.
/// </summary>
/// <param name="Placed">Ids placed, one entry each.</param>
/// <param name="Unplaced">Ids not placed (<c>ids.Length - Placed</c>).</param>
/// <example><code>SpawnResult r = inventory.Spawn(intake, kind, address, flags, ids, MergePolicy.NoMerge); ReadOnlySpan&lt;MailId&gt; rest = ids[r.Placed..];</code></example>
public readonly record struct SpawnResult(ushort Placed, ushort Unplaced);

/// <summary>
/// One mail unit taken from (or peeked at in) an outfeed: its id, the entry it came from and the entry's StackKey, so a belt can
/// re-insert it without asking Mail (ADR-0017 section 5: Inventory never reads Mail's table).
/// </summary>
/// <param name="Mail">The unit's mail id.</param>
/// <param name="Entry">The entry the unit was (or is) in.</param>
/// <param name="MailKind">The entry's mail kind.</param>
/// <param name="AddressId">The entry's packed address.</param>
/// <param name="Flags">The entry's mail flags.</param>
/// <example><code>if (inventory.TakeUnit(intake, Selector.Fifo, actor, out UnitRef u)) { MailId taken = u.Mail; }</code></example>
public readonly record struct UnitRef(MailId Mail, EntryId Entry, DefIndex MailKind, uint AddressId, byte Flags);

/// <summary>
/// An entry named by its container and id (story 015; ADR-0009 Key Interfaces name, shape a planning default): the target of
/// <see cref="IInventoryApi.ConsumeUnit"/>. The ref is stale when the entry is not live or is not in <see cref="Container"/>.
/// </summary>
/// <param name="Container">The container the caller believes holds the entry.</param>
/// <param name="Entry">The entry.</param>
/// <example><code>bool ok = inventory.ConsumeUnit(new EntryRef(hotbar, entry), ConsumeReason.Delivered, actor);</code></example>
public readonly record struct EntryRef(ContainerId Container, EntryId Entry);

/// <summary>
/// The result of <see cref="IInventoryApi.DetachForThrow"/> (story 015; ADR-0009 Key Interfaces name, shape a planning default): the refusal reason (none,
/// 0, on success), the detached entry and its units.
/// </summary>
/// <param name="Reason">0 on success, else the refusal (<c>bad_request</c> as <c>(InvReason)</c><see cref="InvReasons.BadRequest"/>, or <see cref="InvReason.StaleEntry"/>).</param>
/// <param name="Entry">The detached entry (none when refused).</param>
/// <param name="Units">The detached entry's units (0 when refused).</param>
/// <example><code>DetachResult r = inventory.DetachForThrow(slot, entry, units); bool detached = r.Reason == 0;</code></example>
public readonly record struct DetachResult(InvReason Reason, EntryId Entry, byte Units);

/// <summary>
/// What a flight hands <see cref="IInventoryApi.TryCatch"/> (story 014; ADR-0009 Key Interfaces name, l.206, which the ADR never defines:
/// shape Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT, TD-154 item 6; advisory fix, Mail Review PR #38 r1): the
/// flight's entry, which is the live detached entry <see cref="IInventoryApi.DetachForThrow"/> left (its mailIds still in the arena, so
/// the payload carries no ids or units), and where a fumble lands, the catcher's feet.
/// </summary>
/// <param name="Entry">The flight's live detached entry.</param>
/// <param name="CatcherPosMm">The catcher's feet, mm (a fumble's WorldItem position).</param>
/// <param name="OnWater">True when the catcher stands on a non-ground tile (a fumble floats).</param>
/// <example><code>CatchResult r = inventory.TryCatch(catcher, new FlightPayload(entry, feetMm, false));</code></example>
public readonly record struct FlightPayload(EntryId Entry, Vec3Mm CatcherPosMm, bool OnWater);

/// <summary>
/// The result of <see cref="IInventoryApi.TryCatch"/> (story 014; ADR-0009 Key Interfaces <c>{unitsIn, unitsFumbled}</c>, l.206; shape
/// Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT, TD-154 item 6; advisory fix, Mail Review PR #38 r1), on the
/// <see cref="DetachResult"/> pattern: the units that went into the catcher's panel (the oldest, R6), the units that became the fumble
/// WorldItem (the newest), and the reason. Exactly two forms: reason 0 with <c>UnitsIn + UnitsFumbled</c> = the entry's count is a catch
/// that mutated (<c>UnitsIn = 0</c> is a whole fumble, the entry now a WorldItem); <c>{0, 0}</c> with a non-zero reason is a refusal
/// (nothing changed, the entry is still live, detached and the caller's).
/// </summary>
/// <param name="UnitsIn">Units placed or merged into the panel.</param>
/// <param name="UnitsFumbled">Units in the fumble WorldItem.</param>
/// <param name="Reason">0 on a catch, partial or whole fumble included; on a refusal, <c>capacity_exhausted</c> (the arena refused the
/// merge, or a fumble met a full WorldItem table) or <c>bad_request</c> (as <c>(InvReason)</c><see cref="InvReasons.BadRequest"/>).</param>
/// <example><code>CatchResult r = inventory.TryCatch(slot, in flight); bool fumbled = r.UnitsIn == 0 &amp;&amp; r.UnitsFumbled &gt; 0;</code></example>
public readonly record struct CatchResult(byte UnitsIn, byte UnitsFumbled, InvReason Reason);

/// <summary>
/// Why <see cref="IInventoryApi.ConsumeUnit"/> consumed a unit (story 015; ADR-0009 Key Interfaces name). Carried to the
/// <c>UnitLedger</c>'s PLACED to CONSUMED crossing (story 017); the inventory records and seal are the same for every reason.
/// </summary>
public enum ConsumeReason : byte
{
    /// <summary>Delivered to its destination.</summary>
    Delivered = 0,

    /// <summary>Delivered to the wrong destination (the Misdeliver path).</summary>
    Misdelivered = 1,

    /// <summary>Returned to its sender.</summary>
    ReturnedToSender = 2,
}

/// <summary>
/// One whole backlog entry for <see cref="IInventoryApi.AppendBacklog"/> (story grid-inventory/021; ADR-0017 section 6 names the call,
/// the shape is Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT, TD-157 item 2; advisory fix, Mail Review PR #38
/// r1): units of one StackKey. A spawn remainder (the ids <see cref="IInventoryApi.Spawn"/> did not place) is appended one unit per call,
/// GDD R14 "spawns are one unit per entry" and mail-destinations story 014; multi-unit backlog entries come from Inventory's own returns
/// (an expiry, water float or cap eviction of a multi-unit entry). The call accepts 1 to the kind's effective maxStack ids.
/// </summary>
/// <example><code>foreach (MailId id in ids[r.Placed..]) { inventory.AppendBacklog(new BacklogEntry(letter, address, 0, new ReadOnlySpan<MailId>(in id))); }</code></example>
public readonly ref struct BacklogEntry
{
    /// <summary>Creates the entry.</summary>
    /// <param name="mailKind">The units' mail kind.</param>
    /// <param name="addressId">The units' packed address.</param>
    /// <param name="flags">The units' mail flags.</param>
    /// <param name="ids">1 to 255 (and at most the kind's effective maxStack) strictly ascending mail ids.</param>
    public BacklogEntry(DefIndex mailKind, uint addressId, byte flags, ReadOnlySpan<MailId> ids)
    {
        MailKind = mailKind;
        AddressId = addressId;
        Flags = flags;
        Ids = ids;
    }

    /// <summary>The units' mail kind.</summary>
    public DefIndex MailKind { get; }

    /// <summary>The units' packed address.</summary>
    public uint AddressId { get; }

    /// <summary>The units' mail flags.</summary>
    public byte Flags { get; }

    /// <summary>1 to 255 strictly ascending mail ids.</summary>
    public ReadOnlySpan<MailId> Ids { get; }
}

/// <summary>
/// What Grid Inventory provides to the systems above it (ADR-0009 Key Interfaces, as implemented 2026-10-21; ADR-0003 section 5,
/// TR-sim-019): synchronous calls made by in-tick callers inside their own phase. Each mutating call is one atomic mutation through
/// the shared <c>InventoryMutator</c>, with the same records and one <c>InvVersion</c> seal per changed container as an op, and no
/// reply. Mutating calls are legal only inside a tick phase (<c>TickGuard</c>); an illegal call is refused with nothing mutated and
/// no event. Story 013 adds <see cref="CreateWorldItem"/>; story 015 adds <see cref="ConsumeUnit"/>, <see cref="DetachForThrow"/>,
/// <see cref="TryInsert"/> (mail form) and <see cref="CountItem"/>; story 014 <see cref="TryCatch"/>; story 016 <see cref="WeightPoints"/> and <see cref="SpeedPermille"/>
/// (not in the ADR-0009 sketch: TD-148); story 021 <see cref="AppendBacklog"/>, <see cref="BacklogUnits"/> and <see cref="BacklogEntries"/>. Allocation-free; never throws on the tick path. Not
/// thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// SpawnResult r = inventory.Spawn(intake, letter, address, 0, ids, MergePolicy.NoMerge);   // Mail, phase 8
/// if (inventory.PeekOutfeed(intake, Selector.Fifo, out UnitRef next)) { /* next.Mail is the oldest unit */ }
/// </code>
/// </example>
public interface IInventoryApi
{
    /// <summary>
    /// Spawns mail units of one StackKey into <paramref name="c"/>, one entry of count 1 per id, first-fit (GDD R14): per id, AutoPlace
    /// (no source; <see cref="MergePolicy.NoMerge"/> on an outfeed container whatever <paramref name="p"/> says), the arena
    /// reservation check, a new entry and its one-unit block, then the commit. Stops at the first id that does not fit (no cell, entry
    /// pool or arena refusal), with nothing mutated for it. One call scope and one seal per call; actor <c>System</c>, cause
    /// <c>System</c>. A malformed call (no or more than 255 ids, ids not strictly ascending or none, an unknown mail kind, a container
    /// that is not live) and a call outside a tick phase place nothing and mutate nothing.
    /// </summary>
    /// <param name="c">The container (the Intake at M-1a).</param>
    /// <param name="mailKind">The units' mail kind.</param>
    /// <param name="addressId">The units' packed address.</param>
    /// <param name="flags">The units' mail flags.</param>
    /// <param name="ids">1 to 255 strictly ascending mail ids.</param>
    /// <param name="p">The merge policy (ignored for an outfeed container: every Intake insertion is NoMerge).</param>
    /// <returns>How many ids were placed and how many were not.</returns>
    SpawnResult Spawn(ContainerId c, DefIndex mailKind, uint addressId, byte flags, ReadOnlySpan<MailId> ids, MergePolicy p);

    /// <summary>
    /// The unit <see cref="TakeUnit"/> would take now, without taking it: no call scope, no seal, no version change. It may pop stale
    /// head tombstones, rewriting the undumped <c>OutfeedHead</c>/<c>OutfeedTail</c> memo and counting
    /// <c>CounterId.InventoryOutfeedCandidates</c> (44), outside the tick guard; this is deterministic because the memo and the counters
    /// are not in the canonical dump. False on a container with no outfeed, an unknown selector, or an empty outfeed.
    /// </summary>
    /// <param name="c">The container.</param>
    /// <param name="s">The selector.</param>
    /// <param name="u">The unit when found.</param>
    /// <returns>True when a unit is available.</returns>
    bool PeekOutfeed(ContainerId c, Selector s, out UnitRef u);

    /// <summary>
    /// Takes the lowest mailId of the outfeed's head entry (the live entry with the lowest <c>arrivalSeq</c>): one call scope, one seal
    /// with <paramref name="actor"/> and cause <c>None</c>; an entry left at 0 is removed and freed. False, mutating nothing, on a
    /// container with no outfeed, an unknown selector, an empty outfeed, or a call outside a tick phase.
    /// </summary>
    /// <param name="c">The container.</param>
    /// <param name="s">The selector.</param>
    /// <param name="actor">The acting actor (written into the seal).</param>
    /// <param name="u">The unit taken.</param>
    /// <returns>True when a unit was taken.</returns>
    bool TakeUnit(ContainerId c, Selector s, ActorId actor, out UnitRef u);

    /// <summary>
    /// Creates a WorldItem holding the detached entry <paramref name="entry"/> (story 013; GDD R12): the caller has already taken the entry
    /// out of every container (Drop, story 020; Throwing's landing) and resolved the landing — this call only stores
    /// <paramref name="posMm"/>, <paramref name="onWater"/> and <paramref name="createdTick"/> (the landing tick for a throw, which
    /// starts the timer: <c>expiresTick = createdTick + seconds x 30</c>). Mail on ground expires after
    /// <c>worldItemDespawnSeconds</c> back to the Intake, non-mail is then destroyed; on water both float <c>waterReturnSeconds</c> first.
    /// <see cref="WorldItemOrigin.Loot"/> is on no expiry FIFO and never a cap candidate. Raises <c>WorldItemSpawned</c> (replicated, with
    /// the next <c>worldSeq</c>) and, if the live count goes over <c>worldItemCap</c>, evicts one other WorldItem in the same call scope.
    /// Returns the new id (class <c>WorldItem</c>, never reused), or none with nothing changed outside a tick phase, for a reserved
    /// origin (<see cref="WorldItemOrigin.BeltLeak"/>, <see cref="WorldItemOrigin.Spill"/>: M-1b), a <paramref name="createdTick"/> after
    /// the current tick, a <paramref name="createdTick"/> before the last creation's on the same expiry FIFO (Loot, on no FIFO, neither
    /// checks nor moves that order; a programming error, which also trips <c>SimAssertCode.InventoryWorldItemCreatedOutOfOrder</c> under
    /// <c>INVENTORY_ASSERT</c>), an entry that is not live and detached, already in a WorldItem or queued on the Intake backlog, or a
    /// full WorldItem table.
    /// <para>
    /// Callers must handle <see cref="EntityId.None"/>: no WorldItem was created, the entry stays detached and is still the caller's
    /// responsibility.
    /// </para>
    /// </summary>
    /// <param name="entry">A live detached entry (in no container).</param>
    /// <param name="posMm">The landing position, mm.</param>
    /// <param name="origin">The origin.</param>
    /// <param name="onWater">True when it landed on a non-ground tile (water float).</param>
    /// <param name="createdTick">The creation or landing tick (at most the current tick).</param>
    /// <returns>The WorldItem id, or <see cref="EntityId.None"/>.</returns>
    EntityId CreateWorldItem(EntryId entry, Vec3Mm posMm, WorldItemOrigin origin, bool onWater, uint createdTick);

    /// <summary>
    /// Consumes the oldest unit of the entry (story 015; GDD R6 oldest-first, H-R6-1; Mail and Throwing call it per unit): for mail the
    /// lowest mailId. One call scope, one <c>InvRemoved{c, entry, 1}</c> and one <c>InvVersion</c> seal with <paramref name="actor"/>
    /// and cause <c>Consumed</c>; consuming the last unit removes and frees the entry in the same call. <paramref name="reason"/> is the
    /// ledger's (story 017); no other record is raised. False, mutating nothing, on a stale ref (a dead container, an entry that is
    /// not live or not in that container), an empty entry, an undefined reason, or a call outside a tick phase.
    /// </summary>
    /// <param name="entry">The container and entry.</param>
    /// <param name="reason">Why the unit is consumed.</param>
    /// <param name="actor">The acting actor (written into the seal).</param>
    /// <returns>True when a unit was consumed.</returns>
    bool ConsumeUnit(EntryRef entry, ConsumeReason reason, ActorId actor);

    /// <summary>
    /// Detaches a whole hotbar entry for a throw (story 015; ADR-0009 section 1, ADR-0015 section 1: Throwing's <c>ThrowRelease</c>
    /// handler calls it after its own checks, including the selected-slot check, which is not read here). One call scope, one
    /// <c>InvRemoved{hotbar, entry, units}</c> and one <c>InvVersion</c> seal with the slot's player as actor and cause <c>None</c>; the
    /// entry stays live and detached (in no container and in no WorldItem) with its mailIds, and becomes the caller's (the flight's).
    /// Refused with nothing changed and no record, checked in this order: <c>bad_request</c> outside a tick phase or for a slot without
    /// a hotbar; then the id as an op's is classified, with <paramref name="slot"/> as the temp namespace: <c>bad_request</c> for an id
    /// that is none, a real id above the allocator's high-water, a temp id with reserved bits set or a temp id of another slot's
    /// namespace; <c>stale_entry</c> for a temp id of <paramref name="slot"/>'s own namespace (never mapped: no temp id is mapped yet)
    /// or a real id once allocated that is no longer live (consumed, merged away, retired or freed; ADR-0009 section 1, GDD H-R7-4);
    /// <c>bad_request</c> for a live entry not in <paramref name="slot"/>'s hotbar (the cursor's entry, a panel or another slot's
    /// entry, an already detached entry); <c>stale_entry</c> when <paramref name="units"/> is not the entry's count.
    /// </summary>
    /// <param name="slot">The thrower's slot.</param>
    /// <param name="entryId">The hotbar entry.</param>
    /// <param name="units">The entry's whole count, as the client saw it.</param>
    /// <returns>Reason 0 with the entry and its units, or the refusal.</returns>
    DetachResult DetachForThrow(PlayerSlot slot, EntryId entryId, byte units);

    /// <summary>
    /// Catches a flight's entry into <paramref name="slot"/>'s panel (story 014; GDD R9 l.129, edge case l.362, H-R9-2, H-X-2; ADR-0009
    /// Key Interfaces): Throwing's flight resolution calls it with the live detached entry <see cref="DetachForThrow"/> left. The entry's
    /// whole count is AutoPlaced into the panel with the entry as source and <c>Merge</c>: mail by <c>TargetOrder.PickupRow</c>
    /// <c>[hotbar, player_inventory]</c> target-first, non-mail by <c>TargetOrder.CursorRow</c> pass-first (TD-154 item 1). The oldest
    /// units go in; the newest that fit nowhere stay on the entry, which becomes one WorldItem (<see cref="WorldItemOrigin.Thrown"/>, at
    /// <see cref="FlightPayload.CatcherPosMm"/>, created at the current tick so <c>expiresTick = now + 9000</c>, keeping its entry id).
    /// Never the cursor. One call scope; the panel records and one <c>InvVersion</c> seal per changed panel container (actor the slot's
    /// player, cause <c>None</c>) come first, then <c>WorldItemSpawned</c> in <see cref="CreateWorldItem"/>'s nested scope, which applies
    /// story 013's cap eviction (TD-154 item 8). Refusals, each <c>{0, 0}</c> with nothing changed and nothing raised, the entry still
    /// live, detached and the caller's: a mail merge whose union block the arena refuses (<c>capacity_exhausted</c> and one
    /// <c>CheckCode.InventoryArenaExhausted</c> record, ADR-0009 section 5 l.157); a catch that would fumble units while the WorldItem
    /// table is full (<c>capacity_exhausted</c> and one <c>CheckCode.InventoryWorldItemCapacity</c> record; Approved by Evan 2026-10-22
    /// 2:24 PM PT, confirmed in scope 3:00 PM PT, TD-154 item 7; refuse vs fumble for an arena-refused catch stays open to Evan; advisory
    /// fix, Mail Review PR #38 r1); <c>bad_request</c> outside a tick phase, for a slot with no panel, or an entry that is not live and
    /// detached, is held by a WorldItem or is queued on the Intake backlog (story 021). No unit is ever lost.
    /// </summary>
    /// <param name="slot">The catcher's slot.</param>
    /// <param name="flight">The flight's entry and the catcher's feet.</param>
    /// <returns>The units in and fumbled, and the reason.</returns>
    CatchResult TryCatch(PlayerSlot slot, in FlightPayload flight);

    /// <summary>
    /// Inserts one mail unit (story 015, the ADR-0009 sketch's mail form; story 012's <see cref="UnitRef"/>: its id and StackKey) into
    /// <paramref name="c"/> by AutoPlace, merge pass first (GDD R5, H-R5-1, H-X-3): onto a matching non-full stack (one
    /// <c>InvMerged</c>, no entry created), else into the first free cell as a new entry (one <c>InvInserted</c>). One call scope and
    /// one <c>InvVersion</c> seal with <paramref name="actor"/> and cause <c>None</c>. False, with nothing changed (no record, no entry,
    /// no arena block, no version), for a container with an outfeed (<c>outfeed_only</c>: the Intake), a container that is not live, a
    /// whitelist refusal, no room, an arena or entry pool refusal, a none id or unknown mail kind, or a call outside a tick phase. The
    /// item form (a Plank) is not here (Belt Logistics).
    /// </summary>
    /// <param name="c">The container.</param>
    /// <param name="u">The unit (its <see cref="UnitRef.Entry"/> is ignored).</param>
    /// <param name="actor">The acting actor (written into the seal).</param>
    /// <returns>True when the unit was inserted.</returns>
    bool TryInsert(ContainerId c, in UnitRef u, ActorId actor);

    /// <summary>
    /// The units of item def <paramref name="itemId"/> over the containers in <paramref name="cs"/> (story 015; GDD R17): a read of each
    /// container's <c>itemId → units</c> index, never a scan, so its probes do not grow with the containers' entry counts. Mail is
    /// never counted. A dead container contributes 0; a container listed twice is counted twice. Read-only: legal between ticks, no
    /// call scope, no version change; it counts <c>CounterId.InventoryItemCountProbes</c> (49), which is not in the canonical dump.
    /// </summary>
    /// <param name="cs">The containers.</param>
    /// <param name="itemId">The item def.</param>
    /// <returns>The total units (0 for a none def).</returns>
    int CountItem(ReadOnlySpan<ContainerId> cs, DefIndex itemId);

    /// <summary>
    /// The weight points <c>P</c> of <paramref name="slot"/>'s counted containers (story 016; GDD R16, D1): the class points of every
    /// entry in a container the slot owns (hotbar, player_inventory, cursor), once per entry whatever its count. Maintained by the
    /// mutator in O(1) per mutation; a detached entry does not count. Saturates at <see cref="short.MaxValue"/> (unreachable at the
    /// shipped knobs), which asserts <c>SimAssertCode.InventoryWeightPointsSaturated</c> (79) under <c>INVENTORY_ASSERT</c>. Read-only:
    /// legal between ticks. A derived memo, not in the canonical dump.
    /// </summary>
    /// <param name="slot">The player slot.</param>
    /// <returns>The points (0 for a none slot).</returns>
    short WeightPoints(PlayerSlot slot);

    /// <summary>
    /// D1's carrier speed share of <paramref name="slot"/> in permille (story 016): <c>1000 - min(ptsPermille x P, 1000 - floor) x
    /// WeightPenaltyPm / 1000</c>, one truncating division, with <c>WeightPenaltyPm</c> read through the stat sheet for the slot (Strong
    /// Back). Player Character applies it (<c>CarrierSpeed.Final</c>). Read-only: legal between ticks.
    /// </summary>
    /// <param name="slot">The player slot.</param>
    /// <returns>The speed share, the floor to 1000 (1000 for a none slot).</returns>
    int SpeedPermille(PlayerSlot slot);

    /// <summary>
    /// Appends one whole mail entry to the tail of the Intake backlog (story 021; ADR-0017 section 6; GDD R14): Mail calls it in phase 8
    /// for the ids <see cref="Spawn"/> left unplaced, one unit per call (GDD R14). A new detached entry of <c>Ids.Length</c> units and
    /// its mailIds block are taken (as <see cref="Spawn"/> takes them) and queued; the <c>IntakeDrain</c> phase places it later, whole,
    /// NoMerge, with a fresh <c>arrivalSeq</c>. One call scope raising only <c>BacklogUnits</c>; no container changes, no seal. False
    /// with nothing mutated and nothing raised for a malformed entry (a none or unknown kind; 0 or more than 255 ids; more ids than the
    /// kind's effective maxStack, which the whole-entry drain could never place; ids none or not strictly ascending), a call outside a
    /// tick phase (counted by <c>TickGuard</c>), or an entry-pool or arena refusal, which writes the existing always-on record
    /// (<c>InventoryEntryPoolExhausted</c> / <c>InventoryArenaExhausted</c>); the units stay Mail's.
    /// </summary>
    /// <param name="e">The entry.</param>
    /// <returns>True when queued.</returns>
    bool AppendBacklog(in BacklogEntry e);

    /// <summary>The units in the Intake backlog (story 021): an O(1) memo, read-only, legal between ticks.</summary>
    int BacklogUnits { get; }

    /// <summary>The entries in the Intake backlog (story 021): an O(1) memo, read-only, legal between ticks. While it is not 0 nothing overtakes the backlog: <see cref="Spawn"/> into the Intake places nothing.</summary>
    int BacklogEntries { get; }
}
