using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Ops;

/// <summary>
/// The shared steps of every inventory handler's <c>Validate</c> (ADR-0009 section 2; TR-inventory-025, TR-inventory-006; GDD H-NET-5 and
/// H-R7-4; story grid-inventory/004), split so the ADR chain order holds for every kind:
/// <list type="number">
/// <item><see cref="CheckRequest"/> first: the <c>bad_request</c> judgments only the Sim can make (returns 0 or 8, never 67);</item>
/// <item>the handler's own <c>not_open</c> step (placement and entry ops), or Player Character P13's chain for range-gated kinds
/// (<c>Open</c>, <c>Pickup</c>) — stories 007-010;</item>
/// <item><see cref="ResolveEntries"/>: <c>stale_entry</c> for the named entry ids (returns 0 or 67);</item>
/// <item>the rest of the kind's chain (<c>stale_layout</c>, <c>reserved_slot</c>, ...), which builds the <see cref="InvPlan"/>;</item>
/// <item><see cref="CheckArenaReservation(in InvPlan)"/> last, once the plan is built (story 005; ADR-0009 sections 2 and 5, TD-102): the
/// plan lists every mail-ids arena block the commit will acquire, each sized at the moment it is acquired (a union with a none input
/// and a <c>TryAppend</c> on a none handle each acquire one; chained unions need <c>|a|+|b|</c> and then <c>|a|+|b|+|c|</c>; omitting
/// one is a false accept), and the step returns <c>capacity_exhausted</c> (85) when <see cref="MailIdArena.CanReserve(ReadOnlySpan{int})"/>
/// is false, so Commit never fails to acquire. It is a dry run: it writes no check record (the arena's
/// <c>InventoryArenaExhausted</c> record comes only from a real acquire failure).</item>
/// </list>
/// <para><b>bad_request</b> (<see cref="CheckRequest"/>, all fields in payload order):</para>
/// <list type="bullet">
/// <item>a payload length other than the kind's descriptor length, or a kind that is not a registered inventory kind;</item>
/// <item>a non-zero pad byte;</item>
/// <item>a container id that is 0 or at/above the container allocator's high-water (<see cref="ContainerTable.NextId"/>);</item>
/// <item>an entry id that is 0 (except <c>Pickup</c>'s "default entry"), a real id above the entry pool's high-water
/// (<see cref="EntryPool.LastId"/>), a temp id with any of bits 23-30 set, or a temp id outside the actor's own slot namespace
/// (<c>0x8000_0000 | slot &lt;&lt; 20 | ctr</c>; the System actor has no temp namespace);</item>
/// <item><c>Move.Rotated</c> above 1, or 1 on a live slots-layout destination;</item>
/// <item><c>Pickup.WorldItemId</c> 0 (the WorldItem high-water arrives with its table).</item>
/// </list>
/// <para><b>stale_entry</b> (<see cref="ResolveEntries"/>): a real id at or below the high-water that is no longer live, or a temp id of the
/// actor's own namespace (no temp id is mapped before the mutator story, so it was "never mapped": ADR-0009 section 1). Tombstones and
/// <c>container_gone</c> are later stories; a container id below the high-water that is not live passes and is the handler's (none exists
/// yet: containers are never freed).</para>
/// <para>
/// Narrow payload fields (<c>count u8</c>, <c>x</c>/<c>y i16</c>) cannot hold an out-of-range decoded value, so the range checks are the
/// widened-<c>int</c> statics <see cref="CheckCount"/> and <see cref="CheckCoordinate"/>, for the decode boundary (the adapter codec,
/// network-adapter-loopback epic); <see cref="CheckRequest"/> does not repeat them on the narrowed fields. Pure: reads state only,
/// allocates nothing, draws no RNG, raises no event, allocates no id, never throws or asserts on any payload. Not thread-safe: Sim tick
/// thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// ushort reason = check.CheckRequest(in op, actorSlot);                // bad_request
/// if (reason == 0) { reason = NotOpen(in op); }                       // the handler's own step
/// if (reason == 0) { reason = check.ResolveEntries(in op); }          // stale_entry
/// if (reason != 0) { return Reply.Reject(in op, reason); }
/// </code>
/// </example>
public sealed class InvRequestCheck
{
    private const uint TempReservedMask = 0x7F80_0000u;
    private const ushort Pass = 0;
    private const ushort Bad = CoreReason.BadRequest;
    private const ushort Stale = (ushort)InvReason.StaleEntry;
    private const ushort Exhausted = (ushort)InvReason.CapacityExhausted;

    private readonly ContainerTable _containers;
    private readonly EntryPool _entries;
    private readonly MailIdArena _mailIds;

    /// <summary>Creates the check over the module's container table, entry pool and mail-ids arena.</summary>
    /// <param name="containers">The container table.</param>
    /// <param name="entries">The entry pool.</param>
    /// <param name="mailIds">The mail-ids arena (the reservation step).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal InvRequestCheck(ContainerTable containers, EntryPool entries, MailIdArena mailIds)
    {
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(mailIds);
        _containers = containers;
        _entries = entries;
        _mailIds = mailIds;
    }

    /// <summary>
    /// The shared arena-reservation step (last step of every handler's <c>Validate</c>; see the class remarks): 0 when every block in
    /// <paramref name="acquiredBlockUnits"/> can be acquired at once, else <c>capacity_exhausted</c> (85). Pure dry run: reserves,
    /// mutates and records nothing; allocation-free.
    /// </summary>
    /// <param name="acquiredBlockUnits">One element per block the commit acquires, each sized at the moment it is acquired.</param>
    /// <returns>0 or <see cref="InvReason.CapacityExhausted"/>.</returns>
    public ushort CheckArenaReservation(ReadOnlySpan<int> acquiredBlockUnits) => _mailIds.CanReserve(acquiredBlockUnits) ? Pass : Exhausted;

    /// <summary>
    /// <see cref="CheckArenaReservation(ReadOnlySpan{int})"/> over the plan's <see cref="InvPlan.AcquiredBlocks"/>; a plan that planned
    /// more than <see cref="InvPlan.MaxAcquiredBlocks"/> acquisitions (a handler programming error that asserted at
    /// <see cref="InvPlan.AddAcquiredBlock"/> under <c>INVENTORY_ASSERT</c>) is <c>capacity_exhausted</c> in Release (conservative: never a false accept).
    /// </summary>
    /// <param name="plan">The built plan.</param>
    /// <returns>0 or <see cref="InvReason.CapacityExhausted"/>.</returns>
    public ushort CheckArenaReservation(in InvPlan plan) =>
        plan.AcquiredBlocksOverflowed ? Exhausted : CheckArenaReservation(plan.AcquiredBlocks);

    /// <summary>Range check of a decoded count (a <c>u8</c> field, widened): 0 to 255 passes, anything else is <c>bad_request</c>.</summary>
    /// <param name="decoded">The count as decoded (for example -1).</param>
    /// <returns>0 or <see cref="CoreReason.BadRequest"/>.</returns>
    public static ushort CheckCount(int decoded) => decoded is >= 0 and <= byte.MaxValue ? Pass : Bad;

    /// <summary>Range check of a decoded grid coordinate (an <c>i16</c> field, widened): outside int16 is <c>bad_request</c>.</summary>
    /// <param name="decoded">The coordinate as decoded (for example 70,000).</param>
    /// <returns>0 or <see cref="CoreReason.BadRequest"/>.</returns>
    /// <remarks>A representable coordinate outside the container is <c>out_of_bounds</c>, judged later in the placement chain.</remarks>
    public static ushort CheckCoordinate(int decoded) => decoded is >= short.MinValue and <= short.MaxValue ? Pass : Bad;

    /// <summary>
    /// Step 1 of every inventory handler's <c>Validate</c>: the <c>bad_request</c> judgments only the Sim can make, over every payload field
    /// in payload order. Never returns <c>stale_entry</c>: a removed id at or below the high-water passes here and is decided by
    /// <see cref="ResolveEntries"/> after the handler's <c>not_open</c> (or P13) step.
    /// </summary>
    /// <param name="op">The drained op.</param>
    /// <param name="actorSlot">The issuing actor's slot (from <see cref="Op.Actor"/>, never the payload); none for the System actor.</param>
    /// <returns>0 when the request is well-formed, otherwise <see cref="CoreReason.BadRequest"/>. A non-inventory kind, a reserved kind, or a
    /// payload length other than the kind's descriptor length (including a non-zero length on <c>Return</c>) is <c>bad_request</c>.</returns>
    public ushort CheckRequest(in Op op, PlayerSlot actorSlot)
    {
        if (!LengthMatches(in op))
        {
            return Bad;
        }

        return op.Kind switch
        {
            InvOpKinds.Return => Pass,
            InvOpKinds.Pick => CheckPick(op.PayloadAs<PickPayload>(), actorSlot),
            InvOpKinds.Move => CheckMove(op.PayloadAs<MovePayload>(), actorSlot),
            InvOpKinds.Merge => CheckMerge(op.PayloadAs<MergePayload>(), actorSlot),
            InvOpKinds.QuickMove => CheckQuickMove(op.PayloadAs<QuickMovePayload>(), actorSlot),
            InvOpKinds.Drop => CheckEntryNamespace(op.PayloadAs<DropPayload>().EntryId, actorSlot, false),
            InvOpKinds.Pickup => CheckPickup(op.PayloadAs<PickupPayload>(), actorSlot),
            InvOpKinds.Open => CheckContainer(op.PayloadAs<OpenPayload>().Container),
            InvOpKinds.Close => CheckContainer(op.PayloadAs<ClosePayload>().Container),
            _ => Bad,
        };
    }

    private ushort CheckPick(in PickPayload p, PlayerSlot actorSlot) =>
        Or(CheckContainer(p.From), CheckEntryNamespace(p.EntryId, actorSlot, false));

    // X, Y (i16) and Count (u8) cannot hold an out-of-range decoded value: CheckCoordinate / CheckCount are the decode boundary's.
    private ushort CheckMove(in MovePayload p, PlayerSlot actorSlot)
    {
        ushort r = (p.Pad0 | p.Pad1) != 0 ? Bad : Pass;
        r = Or(r, CheckContainer(p.From));
        r = Or(r, CheckEntryNamespace(p.EntryId, actorSlot, false));
        r = Or(r, CheckContainer(p.To));
        return Or(r, CheckRotated(p.To, p.Rotated));
    }

    private ushort CheckMerge(in MergePayload p, PlayerSlot actorSlot)
    {
        ushort r = CheckContainer(p.From);
        r = Or(r, CheckEntryNamespace(p.EntryId, actorSlot, false));
        r = Or(r, CheckContainer(p.To));
        return Or(r, CheckEntryNamespace(p.TargetEntryId, actorSlot, false));
    }

    private ushort CheckQuickMove(in QuickMovePayload p, PlayerSlot actorSlot)
    {
        ushort r = (p.Pad0 | p.Pad1 | p.Pad2) != 0 ? Bad : Pass;
        r = Or(r, CheckContainer(p.From));
        return Or(r, CheckEntryNamespace(p.EntryId, actorSlot, false));
    }

    // The WorldItem table (and its high-water) is a later story: only 0 is judged here. Claimed positions are any i32 (P13 range).
    private ushort CheckPickup(in PickupPayload p, PlayerSlot actorSlot) =>
        Or(p.WorldItemId == 0u ? Bad : Pass, CheckEntryNamespace(p.EntryId, actorSlot, true));

    /// <summary>
    /// The <c>stale_entry</c> step: resolves every entry id the op names (<c>Merge</c>: <c>EntryId</c> then <c>TargetEntryId</c>;
    /// <c>Pickup</c>'s <c>EntryId</c> 0 is the default entry and passes). A real id that is no longer live, or a temp id of the actor's own
    /// namespace (none is mapped before the mutator story: "never mapped", ADR-0009 section 1), is <c>stale_entry</c>. The handler calls it
    /// after its <c>not_open</c> / P13 step. Precondition: <see cref="CheckRequest"/> returned 0 for this op (an op that would not pass it
    /// gets <c>bad_request</c> here only when its length is wrong; otherwise the result is unspecified).
    /// </summary>
    /// <param name="op">The drained op.</param>
    /// <returns>0 when every named entry is live (or the kind names none), otherwise <see cref="InvReason.StaleEntry"/>.</returns>
    public ushort ResolveEntries(in Op op)
    {
        if (!LengthMatches(in op))
        {
            return Bad;
        }

        switch (op.Kind)
        {
            case InvOpKinds.Pick:
                return ResolveEntry(op.PayloadAs<PickPayload>().EntryId);
            case InvOpKinds.Move:
                return ResolveEntry(op.PayloadAs<MovePayload>().EntryId);
            case InvOpKinds.Merge:
            {
                MergePayload p = op.PayloadAs<MergePayload>();
                return Or(ResolveEntry(p.EntryId), ResolveEntry(p.TargetEntryId));
            }

            case InvOpKinds.QuickMove:
                return ResolveEntry(op.PayloadAs<QuickMovePayload>().EntryId);
            case InvOpKinds.Drop:
                return ResolveEntry(op.PayloadAs<DropPayload>().EntryId);
            case InvOpKinds.Pickup:
            {
                uint entry = op.PayloadAs<PickupPayload>().EntryId;
                return entry == 0u ? Pass : ResolveEntry(entry);
            }

            default:
                return Pass; // Return, Open, Close name no entry
        }
    }

    // Earliest non-zero reason wins (the chain order is the call order).
    private static ushort Or(ushort first, ushort next) => first != Pass ? first : next;

    // Lengths come from the descriptors, so the check cannot drift from the declared PayloadLength.
    private static bool LengthMatches(in Op op)
    {
        int expected = op.Kind switch
        {
            InvOpKinds.Pick => InvOpDescriptors.Pick.PayloadLength,
            InvOpKinds.Move => InvOpDescriptors.Move.PayloadLength,
            InvOpKinds.Merge => InvOpDescriptors.Merge.PayloadLength,
            InvOpKinds.QuickMove => InvOpDescriptors.QuickMove.PayloadLength,
            InvOpKinds.Drop => InvOpDescriptors.Drop.PayloadLength,
            InvOpKinds.Pickup => InvOpDescriptors.Pickup.PayloadLength,
            InvOpKinds.Open => InvOpDescriptors.Open.PayloadLength,
            InvOpKinds.Close => InvOpDescriptors.Close.PayloadLength,
            InvOpKinds.Return => InvOpDescriptors.Return.PayloadLength,
            _ => -1,
        };
        return expected == op.PayloadLength;
    }

    private ushort CheckContainer(uint raw) => raw != 0u && raw < _containers.NextId ? Pass : Bad;

    private ushort CheckRotated(uint destination, byte rotated)
    {
        if (rotated > 1)
        {
            return Bad;
        }

        if (rotated == 0 || !_containers.TryGetSlot(new ContainerId(destination), out ushort slot))
        {
            return Pass;
        }

        return _containers[slot].Layout == ContainerLayout.Slots ? Bad : Pass;
    }

    // Raw-bit checks only: EntryId.FromValue / Real assert on malformed input, and a malformed request must never assert.
    private ushort CheckEntryNamespace(uint raw, PlayerSlot actorSlot, bool allowNone)
    {
        if (raw == 0u)
        {
            return allowNone ? Pass : Bad;
        }

        if ((raw & EntryId.TempFlag) == 0u)
        {
            return raw <= _entries.LastId ? Pass : Bad;
        }

        if ((raw & TempReservedMask) != 0u || actorSlot.IsNone)
        {
            return Bad;
        }

        return ((raw >> 20) & 0x7u) == actorSlot.Value ? Pass : Bad;
    }

    // Called only after CheckEntryNamespace passed: a real id at/below the high-water or a temp id of the actor's own namespace.
    private ushort ResolveEntry(uint raw)
    {
        if ((raw & EntryId.TempFlag) != 0u)
        {
            return Stale; // no temp id is mapped yet (the mapping arrives with the mutator story)
        }

        return _entries.TryGetSlot(EntryId.Real(raw), out _) ? Pass : Stale;
    }
}
