using PerformativeMail.Sim.Geometry;
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
/// What Grid Inventory provides to the systems above it (ADR-0009 Key Interfaces, as implemented 2026-10-21; ADR-0003 section 5,
/// TR-sim-019): synchronous calls made by in-tick callers inside their own phase. Each mutating call is one atomic mutation through
/// the shared <c>InventoryMutator</c>, with the same records and one <c>InvVersion</c> seal per changed container as an op, and no
/// reply. Mutating calls are legal only inside a tick phase (<c>TickGuard</c>); an illegal call is refused with nothing mutated and
/// no event. Story 013 adds <see cref="CreateWorldItem"/>; story 015 adds <c>ConsumeUnit</c>, <c>DetachForThrow</c>, <c>TryInsert</c> and <c>CountItem</c>; story 021 the backlog
/// members. Allocation-free; never throws on the tick path. Not thread-safe: Sim tick thread only.
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
    /// <c>INVENTORY_ASSERT</c>), an entry that is not live and detached or already in a WorldItem, or a full WorldItem table.
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
}
