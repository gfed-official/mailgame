using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.World;

/// <summary>How a <see cref="PickupCore"/> call ended (story grid-inventory/014).</summary>
internal enum PickupVerdict : byte
{
    /// <summary>Units went into the panel (the whole entry, or the oldest part of it: <see cref="PickupResult.Got"/>).</summary>
    Taken = 0,

    /// <summary>Refused with <see cref="PickupResult.Reason"/>; nothing changed.</summary>
    Refused = 1,

    /// <summary>
    /// The WorldItem is floating (it landed on water): refused by the core with nothing changed and no <c>InvReason</c>. Story 019's
    /// range-gated handler maps it to Player Character P13's <c>unavailable</c> (ADR-0009 l.115) before the core runs (Mail Lead planning
    /// default pending Evan, TD-154 item 4).
    /// </summary>
    Floating = 2,
}

/// <summary>
/// The outcome of <see cref="PickupCore.Apply"/>: the verdict, the reason of a refusal (<c>bad_request</c> 8 or an <see cref="InvReason"/>),
/// for <c>container_gone</c> the tombstone's actor and cause, and the units asked for (the entry's count) and taken.
/// </summary>
/// <param name="Verdict">Taken, refused or floating.</param>
/// <param name="Reason">0 when taken, else the refusal (0 for <see cref="PickupVerdict.Floating"/>).</param>
/// <param name="ByActorId">For <c>container_gone</c>: who removed the WorldItem (raw <c>ActorId</c>); else 0.</param>
/// <param name="Cause">For <c>container_gone</c>: why (<c>Taken</c>, <c>Expired</c> or <c>Destroyed</c>); else none.</param>
/// <param name="Requested">The units asked for: the WorldItem entry's count (0 when refused before the entry was resolved).</param>
/// <param name="Got">The units that went into the panel (0 unless taken).</param>
internal readonly record struct PickupResult(PickupVerdict Verdict, ushort Reason, uint ByActorId, InvCause Cause, byte Requested, byte Got)
{
    /// <summary>True when units went into the panel.</summary>
    public bool IsTaken => Verdict == PickupVerdict.Taken;

    /// <summary>A refusal with <paramref name="reason"/>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>The result.</returns>
    public static PickupResult Refuse(ushort reason) => new(PickupVerdict.Refused, reason, 0, InvCause.None, 0, 0);
}

/// <summary>
/// What <see cref="PickupCore.Validate"/> decided and <see cref="PickupCore.Commit"/> applies unchanged (ADR-0009 section 2): the panel
/// AutoPlace of the WorldItem's entry, the WorldItem and its entry, and the picker.
/// </summary>
internal struct PickupPlan
{
    /// <summary>The AutoPlace of the WorldItem's (detached) entry into the picker's panel.</summary>
    public AutoPlacePlan Placement;

    /// <summary>The WorldItem.</summary>
    public EntityId WorldItem;

    /// <summary>Its table slot.</summary>
    public ushort WorldSlot;

    /// <summary>Its entry's pool slot.</summary>
    public ushort EntrySlot;

    /// <summary>The picker (the seal's actor and the tombstone's <c>byActorId</c>).</summary>
    public ActorId Actor;

    /// <summary>True when every unit goes in, so the WorldItem is removed (else the newest units stay on it).</summary>
    public readonly bool Whole => Placement.Unplaced == 0;
}

/// <summary>
/// What <c>InventorySystem.ValidateCatch</c> decided and <c>CommitCatch</c> applies unchanged (story 014; ADR-0009 section 2): the panel
/// AutoPlace (committed only when <see cref="Places"/>), the flight's entry, the units in and fumbled, and whether the catch was refused.
/// </summary>
internal struct CatchPlan
{
    /// <summary>The AutoPlace of the flight's entry into the catcher's panel.</summary>
    public AutoPlacePlan Placement;

    /// <summary>The flight entry's pool slot.</summary>
    public ushort EntrySlot;

    /// <summary>The catcher (the seals' actor).</summary>
    public ActorId Actor;

    /// <summary>True when <see cref="Placement"/> is committed (some units go in).</summary>
    public bool Places;

    /// <summary>True when the catch is refused whole: nothing is committed.</summary>
    public bool Refused;

    /// <summary>True when the refusal is a full WorldItem table (the caller writes the capacity record).</summary>
    public bool TableFull;
}

/// <summary>
/// The pickup core (story grid-inventory/014; GDD R9 l.128-129, R12 l.137, edge cases l.362-363, H-R9-2, H-R12-1; ADR-0009 sections 1,
/// 2 and 5; TR-inventory-007, TR-inventory-013): everything a <c>pickup {worldItemId, entryId}</c> does after Player Character's P13
/// range chain, which is story 019's range-gated <c>Pickup</c> handler (production binds no Pickup handler until then; tests bind a
/// test-only one). <see cref="Apply"/> is <see cref="Validate"/>, then on a pass <see cref="Commit"/>.
/// <para>
/// The chain (Mail Lead planning default pending Evan, TD-154 item 5; no GDD or ADR section gives the core's order, and
/// <c>container_gone</c> is decided in id resolution, ADR-0009 l.116):
/// <list type="number">
/// <item><c>bad_request</c>: an actor with no panel, an <paramref name="entryId"/> outside the actor's namespace or above the entry
/// high-water (<see cref="InvRequestCheck.CheckEntryNamespace"/>, 0 allowed), a WorldItem id that is not of class WorldItem or was
/// never allocated (<see cref="WorldItems.Resolve"/>);</item>
/// <item><c>container_gone</c>: a removed WorldItem whose tombstone is alive (30 s), with its <c>byActorId</c> and cause;</item>
/// <item><c>stale_entry</c>: a removed WorldItem whose tombstone is dead; or an <paramref name="entryId"/> other than 0 or the
/// WorldItem's own entry (at M-1a a WorldItem holds one entry; heap selection is M-1b's, TD-143 item 3; TD-154 item 3);</item>
/// <item>floating: the WorldItem landed on water (<see cref="PickupVerdict.Floating"/>, no reason; TD-154 item 4);</item>
/// <item><c>no_room</c>: AutoPlace of the entry's whole count into the panel moved nothing. Mail uses
/// <see cref="TargetOrder.PickupRow"/> <c>[hotbar, player_inventory]</c>, <see cref="PlaceOrder.TargetFirst"/>; non-mail
/// <see cref="TargetOrder.CursorRow"/>, <see cref="PlaceOrder.PassFirst"/>; both with <see cref="MergePolicy.Merge"/> (TD-154 item 1).
/// <c>not_allowed</c> / <c>not_carryable</c> are unreachable at M-1a and assert
/// <see cref="SimAssertCode.InventoryPickupNotAdmitted"/> (Release <c>no_room</c>);</item>
/// <item><c>capacity_exhausted</c>: the plan's mail merge union blocks fail <see cref="InvRequestCheck.CheckArenaReservation(in InvPlan)"/>
/// (last, as <c>MoveHandler</c>).</item>
/// </list>
/// </para>
/// <para>
/// The commit, in the caller's call scope (the op drain's, or the in-tick caller's phase): a <b>whole</b> pickup first removes the
/// WorldItem (<see cref="WorldItems.Remove"/>: <c>WorldItemDespawned</c> and the <c>taken</c> tombstone with the picker as
/// <c>byActorId</c>) and hands its entry back detached, before the AutoPlace commit, because <see cref="InventoryMutator.MergeInto"/>
/// frees a source it leaves at 0; a <b>partial</b> pickup (the panel took only the oldest units, R6) keeps the WorldItem, its id, entry,
/// <c>expiresTick</c>, position and FIFO node, and raises <see cref="WorldItems.RaiseEntriesChanged"/> with the count left (TD-154 item
/// 2). So the world record comes first, then the panel records and one <c>InvVersion</c> seal per changed panel container, actor the
/// picker, cause <see cref="InvCause.None"/> (TD-154 item 8). The cursor and its <c>CursorOrigin</c> are never touched (GDD R9 "never
/// the cursor"). A refusal changes nothing and raises nothing (TD-105 item 5); a refused AutoPlace's no-fit facts are recorded after the
/// refusal was decided, outside <see cref="Validate"/>, as <c>PutAwayCursor</c> does. Allocation-free; never throws on the tick path.
/// Not thread-safe: Sim tick thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var extra = new InvReplyExtra();
/// PickupResult r = inventory.Pickup.Apply(op.Actor, new EntityId(p.WorldItemId), p.EntryId, ref extra);
/// </code>
/// </example>
internal sealed class PickupCore
{
    private readonly InventorySystem _owner;
    private readonly ContentSet _content;
    private readonly IStatSheet _stats;
    private readonly ITickReader _tick;
    private readonly SimCounters _counters;

    /// <summary>Creates the core over the module.</summary>
    /// <param name="owner">The inventory module (storage, AutoPlace, mutator, WorldItems, panels).</param>
    /// <param name="content">The content set (the entry's def and <c>effMax</c>).</param>
    /// <param name="stats">The stat sheet (mail <c>effMax</c>, read at op time).</param>
    /// <param name="tick">The tick reader (the tick guard).</param>
    /// <param name="counters">The world's counters (the tick guard).</param>
    internal PickupCore(InventorySystem owner, ContentSet content, IStatSheet stats, ITickReader tick, SimCounters counters)
    {
        _owner = owner;
        _content = content;
        _stats = stats;
        _tick = tick;
        _counters = counters;
    }

    /// <summary>
    /// Picks up <paramref name="worldItemId"/> for <paramref name="actor"/>: the tick guard (<c>bad_request</c> outside a tick phase), then
    /// <see cref="Validate"/>, then on a pass <see cref="Commit"/>. On a refusal <paramref name="extra"/> gets the panel's current versions
    /// only; the caller writes the result's reason, <c>byActorId</c> and cause into its reply.
    /// </summary>
    /// <param name="actor">The picker (a player entity with a panel).</param>
    /// <param name="worldItemId">The WorldItem named by the op.</param>
    /// <param name="entryId">The op's raw entry id (0 = the WorldItem's entry).</param>
    /// <param name="extra">The reply extra (versions, units, cause).</param>
    /// <returns>The outcome.</returns>
    public PickupResult Apply(ActorId actor, EntityId worldItemId, uint entryId, ref InvReplyExtra extra)
    {
        if (!TickGuard.IsMutable(_tick, _counters))
        {
            return PickupResult.Refuse(CoreReason.BadRequest);
        }

        var plan = new InvPlan();
        PickupResult result = Validate(actor, worldItemId, entryId, ref plan, out PickupPlan pickup);
        if (!result.IsTaken)
        {
            if (result.Reason is (ushort)InvReason.NoRoom or (ushort)InvReason.CapacityExhausted)
            {
                _owner.AutoPlace.RecordNoFits(in pickup.Placement);
            }

            _owner.Mutator.FillVersions(in plan, ref extra);
            return result;
        }

        Commit(ref plan, in pickup, ref extra);
        return result;
    }

    /// <summary>
    /// The pure half: walks the chain (see the class remarks) and on a pass fills <paramref name="pickup"/> and <paramref name="plan"/>
    /// (primaries hotbar and player_inventory, the touched panel containers, the arena blocks). Reads state only; writes nothing but the
    /// two plans; allocates no id, raises no event, records no no-fit fact.
    /// </summary>
    /// <param name="actor">The picker.</param>
    /// <param name="worldItemId">The WorldItem.</param>
    /// <param name="entryId">The raw entry id (0 = the WorldItem's entry).</param>
    /// <param name="plan">The call's plan.</param>
    /// <param name="pickup">The pickup to commit (its placement also holds a refused AutoPlace's no-fit facts).</param>
    /// <returns>Taken with the units when the commit can run, else the refusal or the floating verdict.</returns>
    internal PickupResult Validate(ActorId actor, EntityId worldItemId, uint entryId, ref InvPlan plan, out PickupPlan pickup)
    {
        pickup = default;
        pickup.Actor = actor;
        PlayerSlot slot = _owner.ActorSlot(actor);
        if (!_owner.TryGetPanel(slot, out ushort hotbar, out ushort playerInventory))
        {
            return PickupResult.Refuse(CoreReason.BadRequest); // the System actor, or a slot with no panel
        }

        plan.SetPrimary(hotbar, playerInventory);
        EntryPool entries = _owner.Entries;
        ushort reason = InvRequestCheck.CheckEntryNamespace(entries, entryId, slot, allowNone: true);
        if (reason != 0)
        {
            return PickupResult.Refuse(reason);
        }

        WorldItems world = _owner.WorldItems;
        reason = world.Resolve(worldItemId, out ushort worldSlot, out uint byActorId, out InvCause cause);
        if (reason != 0)
        {
            return new PickupResult(PickupVerdict.Refused, reason, byActorId, cause, 0, 0); // bad_request, container_gone or stale_entry
        }

        ref readonly WorldItemRecord w = ref world.Table[worldSlot];
        ushort entrySlot = w.EntrySlot;
        ref readonly EntryRecord e = ref entries[entrySlot];
        if (entryId != 0u && entryId != e.Id.Value)
        {
            return PickupResult.Refuse((ushort)InvReason.StaleEntry); // not this WorldItem's entry (TD-154 item 3)
        }

        if (w.OnWater)
        {
            return new PickupResult(PickupVerdict.Floating, 0, 0, InvCause.None, e.Count, 0);
        }

        pickup.WorldItem = worldItemId;
        pickup.WorldSlot = worldSlot;
        pickup.EntrySlot = entrySlot;
        reason = PlanIntoPanel(ref plan, entrySlot, hotbar, playerInventory, out pickup.Placement);
        if (reason == 0)
        {
            reason = _owner.RequestCheck.CheckArenaReservation(in plan);
        }

        return reason != 0
            ? new PickupResult(PickupVerdict.Refused, reason, 0, InvCause.None, e.Count, 0)
            : new PickupResult(PickupVerdict.Taken, 0, 0, InvCause.None, e.Count, (byte)pickup.Placement.MovedUnits);
    }

    /// <summary>
    /// The mutating half: applies <paramref name="pickup"/> unchanged (see the class remarks for the order). Never fails, never
    /// recomputes. Writes the units into <paramref name="extra"/> (requested = the entry's count, got = the units moved).
    /// </summary>
    /// <param name="plan">The plan <see cref="Validate"/> passed.</param>
    /// <param name="pickup">The pickup <see cref="Validate"/> passed.</param>
    /// <param name="extra">The reply extra.</param>
    internal void Commit(ref InvPlan plan, in PickupPlan pickup, ref InvReplyExtra extra)
    {
        InventoryMutator m = _owner.Mutator;
        int requested = pickup.Placement.Requested;

        // story 017 ledger hook: the moved units cross WORLD -> PLACED.
        if (pickup.Whole)
        {
            _ = _owner.WorldItems.Remove(pickup.WorldItem, pickup.Actor, InvCause.Taken, out _); // the guard passed in Apply; the id is live
        }
        else
        {
            _owner.WorldItems.RaiseEntriesChanged(pickup.WorldSlot, (byte)pickup.Placement.Unplaced);
        }

        m.Begin(ref plan, pickup.Actor, InvCause.None);
        _owner.AutoPlace.Commit(ref plan, in pickup.Placement);
        m.End(ref plan, ref extra);
        extra.SetUnits(requested, pickup.Placement.MovedUnits);
    }

    /// <summary>
    /// Plans the whole count of the detached entry in <paramref name="entrySlot"/> into a panel (the pickup and catch rows, TD-154 item
    /// 1): mail by <see cref="TargetOrder.PickupRow"/> target-first, non-mail by <see cref="TargetOrder.CursorRow"/> pass-first, both
    /// with <see cref="MergePolicy.Merge"/>. Returns 0, <c>no_room</c> or (unreachable) <c>bad_request</c>; an unreachable
    /// <c>not_allowed</c> / <c>not_carryable</c> asserts <see cref="SimAssertCode.InventoryPickupNotAdmitted"/> and is <c>no_room</c>.
    /// Pure, as <see cref="AutoPlace.Plan"/>.
    /// </summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="entrySlot">The detached entry.</param>
    /// <param name="hotbar">The panel's hotbar table slot.</param>
    /// <param name="playerInventory">The panel's player_inventory table slot.</param>
    /// <param name="placement">The AutoPlace plan.</param>
    /// <returns>0, <c>no_room</c> or <c>bad_request</c>.</returns>
    internal ushort PlanIntoPanel(ref InvPlan plan, ushort entrySlot, ushort hotbar, ushort playerInventory, out AutoPlacePlan placement)
    {
        ref readonly EntryRecord e = ref _owner.Entries[entrySlot];
        PlacementSubject subject = PlacementSubject.Of(_content, _stats, in e);
        Span<ushort> targets = stackalloc ushort[TargetOrder.MaxRowLength];
        PlaceOrder order;
        if (e.IsMail)
        {
            TargetOrder.PickupRow(hotbar, playerInventory, targets);
            order = PlaceOrder.TargetFirst;
        }
        else
        {
            TargetOrder.CursorRow(subject.Category, hotbar, playerInventory, targets);
            order = PlaceOrder.PassFirst;
        }

        ushort reason = _owner.AutoPlace.Plan(ref plan, in subject, e.Count, entrySlot, targets, MergePolicy.Merge, order, out placement);
        bool notAdmitted = reason is (ushort)InvReason.NotAllowed or (ushort)InvReason.NotCarryable;
        InventoryAssert.That(!notAdmitted, SimAssertCode.InventoryPickupNotAdmitted);
        return notAdmitted ? (ushort)InvReason.NoRoom : reason;
    }
}
