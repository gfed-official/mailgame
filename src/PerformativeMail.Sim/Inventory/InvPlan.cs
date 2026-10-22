using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// The placement result a handler's <c>Validate</c> decided and its <c>Commit</c> applies unchanged (ADR-0009 section 2: commit never
/// recomputes). A plain value; <see cref="IsSet"/> false means "no placement".
/// </summary>
/// <example><code>plan.Placement = new InvPlacement(entrySlot, toSlot, 3, 0, false);</code></example>
public struct InvPlacement
{
    /// <summary>The entry's pool slot.</summary>
    public ushort EntrySlot;

    /// <summary>The destination container's table slot.</summary>
    public ushort ContainerSlot;

    /// <summary>Top-left column.</summary>
    public byte X;

    /// <summary>Top-left row.</summary>
    public byte Y;

    /// <summary>The placement's rotation.</summary>
    public bool Rotated;

    /// <summary>True when a placement was decided.</summary>
    public bool IsSet;

    /// <summary>Creates a decided placement.</summary>
    /// <param name="entrySlot">The entry's pool slot.</param>
    /// <param name="containerSlot">The destination container's table slot.</param>
    /// <param name="x">Top-left column.</param>
    /// <param name="y">Top-left row.</param>
    /// <param name="rotated">The rotation.</param>
    public InvPlacement(ushort entrySlot, ushort containerSlot, byte x, byte y, bool rotated)
    {
        EntrySlot = entrySlot;
        ContainerSlot = containerSlot;
        X = x;
        Y = y;
        Rotated = rotated;
        IsSet = true;
    }
}

/// <summary>
/// The merge a handler's <c>Validate</c> decided and its <c>Commit</c> applies unchanged (story grid-inventory/007: <c>move</c> onto a
/// mergeable stack, GDD R7 merge row): <see cref="Units"/> of the source entry go onto the placed target stack. A plain value;
/// <see cref="IsSet"/> false means "no merge".
/// </summary>
/// <example><code>plan.Merge = new InvMerge(entrySlot, targetSlot, units);</code></example>
public struct InvMerge
{
    /// <summary>The source entry's pool slot.</summary>
    public ushort SourceSlot;

    /// <summary>The placed target stack's pool slot.</summary>
    public ushort TargetSlot;

    /// <summary>How many units move: <c>min(count, effMax − target.count)</c>, at least 1.</summary>
    public byte Units;

    /// <summary>True when a merge was decided.</summary>
    public bool IsSet;

    /// <summary>Creates a decided merge.</summary>
    /// <param name="sourceSlot">The source entry's pool slot.</param>
    /// <param name="targetSlot">The placed target stack's pool slot.</param>
    /// <param name="units">How many units move.</param>
    public InvMerge(ushort sourceSlot, ushort targetSlot, byte units)
    {
        SourceSlot = sourceSlot;
        TargetSlot = targetSlot;
        Units = units;
        IsSet = true;
    }
}

/// <summary>Up to <see cref="InvPlan.MaxTouched"/> container table slots.</summary>
[InlineArray(InvPlan.MaxTouched)]
internal struct InvSlotBuffer
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private ushort _e0;
#pragma warning restore CS0169
}

/// <summary>Up to <see cref="InvPlan.MaxAcquiredBlocks"/> block sizes (<c>ushort</c>: a block size may exceed 255, see <see cref="InvPlan.AddAcquiredBlock"/>).</summary>
[InlineArray(InvPlan.MaxAcquiredBlocks)]
internal struct InvBlockBuffer
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private ushort _e0;
#pragma warning restore CS0169
}

/// <summary>
/// The stack-allocated plan of one inventory call (ADR-0009 sections 2 and 3; story grid-inventory/005): a fixed-size struct with
/// <c>[InlineArray]</c> buffers, so a handler's <c>Validate</c> writes only it and nothing is allocated. It holds:
/// <list type="bullet">
/// <item>the <b>touched set</b>: up to <see cref="MaxTouched"/> container table slots in first-touch order (the seal order), each with a
/// "mutated" bit. Contract: <c>Validate</c> <see cref="Touch"/>es <b>every</b> container <c>Commit</c> may change and refuses the op
/// (<c>bad_request</c>, a handler-limit refusal) before any mutation when a touch returns -1, so <c>Commit</c> never changes a
/// container the plan cannot seal. The <see cref="InventoryMutator"/> primitives then mark a touched container mutated when they
/// really change it (<see cref="EnsureTouched"/> asserts it was touched, before anything changes); only mutated containers are sealed and bumped, so a no-op
/// (a move onto its own cells) seals nothing even though it was touched;</item>
/// <item>the <b>acquired blocks</b>: one element per mail-ids arena block the commit acquires, each sized at the moment it is
/// acquired (the input of <see cref="Ops.InvRequestCheck.CheckArenaReservation(in InvPlan)"/>);</item>
/// <item>the <see cref="Placement"/> result (or the <see cref="Merge"/> a placement onto a mergeable stack became, story 007) and
/// the reply's primary source/destination containers (<c>ResultVersionA/B</c>);</item>
/// <item>the call's actor and cause, set by <see cref="InventoryMutator.Begin"/> and written into every seal.</item>
/// </list>
/// A default value is an empty plan. Not thread-safe (plain value).
/// </summary>
/// <example>
/// <code>
/// var plan = new InvPlan();
/// plan.Touch(fromSlot); plan.Touch(toSlot); plan.SetPrimary(fromSlot, toSlot);
/// plan.AddAcquiredBlock(units);
/// ushort reason = check.CheckArenaReservation(in plan);
/// </code>
/// </example>
public struct InvPlan
{
    /// <summary>The most containers one call may touch (ADR-0009 section 2).</summary>
    public const int MaxTouched = 4;

    /// <summary>The arena blocks a handler may plan for itself beside one AutoPlace's merges (story 009, TD-116).</summary>
    public const int HandlerBlockHeadroom = 8;

    /// <summary>
    /// The most arena blocks one call may plan to acquire; more makes the reservation fail (conservative). 263 = one AutoPlace's exact
    /// merge bound (<see cref="PerformativeMail.Sim.Inventory.Placement.AutoPlacePlan.MaxMerges"/>, 255: one union per mail merge) plus
    /// <see cref="HandlerBlockHeadroom"/> for the handler's own acquisitions (story 009, TD-116 / Planner R4: the cap is raised so a
    /// legal PASS M never stops early, GDD R8). A handler that plans at most <see cref="HandlerBlockHeadroom"/> blocks of its own
    /// therefore never asserts or rejects 85 for plan capacity, whatever the geometry.
    /// </summary>
    public const int MaxAcquiredBlocks = PerformativeMail.Sim.Inventory.Placement.AutoPlacePlan.MaxMerges + HandlerBlockHeadroom;

    private InvSlotBuffer _touched;
    private InvBlockBuffer _blocks;
    private byte _touchedCount;
    private ushort _blockCount;
    private int _primaryA; // slot + 1; 0 = none
    private int _primaryB;
    private byte _mutatedMask;
    private bool _blocksOverflowed;

    /// <summary>The placement decided by <c>Validate</c>.</summary>
    public InvPlacement Placement;

    /// <summary>The merge decided by <c>Validate</c> instead of a placement (story 007).</summary>
    public InvMerge Merge;

    /// <summary>The call's actor (written into every seal; the reply's <c>ByActorId</c> is the contested actor, stories 013/014).</summary>
    public ActorId Actor;

    /// <summary>The call's cause (written into every seal and the reply's cause bits).</summary>
    public InvCause Cause;

    /// <summary>The number of touched containers.</summary>
    public readonly int TouchedCount => _touchedCount;

    /// <summary>The number of touched containers that were really changed (and will be sealed).</summary>
    public readonly int MutatedCount => System.Numerics.BitOperations.PopCount(_mutatedMask);

    /// <summary>The number of planned block acquisitions.</summary>
    public readonly int AcquiredBlockCount => _blockCount;

    /// <summary>True when more than <see cref="MaxAcquiredBlocks"/> acquisitions were planned (the reservation then fails).</summary>
    public readonly bool AcquiredBlocksOverflowed => _blocksOverflowed;

    /// <summary>The planned block sizes, in commit order.</summary>
    [UnscopedRef]
    public readonly ReadOnlySpan<ushort> AcquiredBlocks => ((ReadOnlySpan<ushort>)_blocks).Slice(0, _blockCount);

    /// <summary>The table slot of the <paramref name="index"/>-th touched container (first-touch order).</summary>
    /// <param name="index">0 to <see cref="TouchedCount"/> - 1.</param>
    /// <returns>The slot.</returns>
    public readonly ushort TouchedSlot(int index) => _touched[index];

    /// <summary>True when the <paramref name="index"/>-th touched container was changed.</summary>
    /// <param name="index">0 to <see cref="TouchedCount"/> - 1.</param>
    /// <returns>True when mutated.</returns>
    public readonly bool IsMutated(int index) => (_mutatedMask & (1 << index)) != 0;

    /// <summary>
    /// Adds <paramref name="containerSlot"/> to the touched set (deduplicated) and returns its index. <c>Validate</c> calls it for every
    /// container the commit may change; -1 (the set already holds <see cref="MaxTouched"/> others) is a legitimate refusal the handler
    /// turns into a reject before any mutation, so it does not assert.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <returns>The index, or -1 when the set is full.</returns>
    public int Touch(ushort containerSlot)
    {
        int found = IndexOf(containerSlot);
        if (found >= 0)
        {
            return found;
        }

        if (_touchedCount >= MaxTouched)
        {
            return -1;
        }

        _touched[_touchedCount] = containerSlot;
        return _touchedCount++;
    }

    /// <summary>
    /// The mutator primitives' check, made <b>before</b> they change anything: the container must already be in the touched set
    /// (asserts <see cref="SimAssertCode.InventoryPlanContainerNotTouched"/> under <c>INVENTORY_ASSERT</c>). Release falls back to
    /// <see cref="Touch"/>, so the change is still sealed when the set has room; when it is full the primitive changes nothing, so a
    /// change is never left unsealed.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <returns>True when the container is (now) in the touched set.</returns>
    public bool EnsureTouched(ushort containerSlot)
    {
        int index = IndexOf(containerSlot);
        InventoryAssert.That(index >= 0, SimAssertCode.InventoryPlanContainerNotTouched);
        return index >= 0 || Touch(containerSlot) >= 0;
    }

    /// <summary>Marks a touched container changed (the mutator primitives call this after <see cref="EnsureTouched"/>); an untouched slot is ignored.</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    public void MarkMutated(ushort containerSlot)
    {
        int index = IndexOf(containerSlot);
        if (index >= 0)
        {
            _mutatedMask |= (byte)(1 << index);
        }
    }

    /// <summary>Names the reply's primary source (A) and destination (B) containers.</summary>
    /// <param name="sourceSlot">Table slot of the source.</param>
    /// <param name="destinationSlot">Table slot of the destination.</param>
    public void SetPrimary(ushort sourceSlot, ushort destinationSlot)
    {
        _primaryA = sourceSlot + 1;
        _primaryB = destinationSlot + 1;
    }

    /// <summary>The primary source container's slot.</summary>
    /// <param name="slot">The slot when set.</param>
    /// <returns>True when set.</returns>
    public readonly bool TryGetPrimarySource(out ushort slot)
    {
        slot = (ushort)Math.Max(0, _primaryA - 1);
        return _primaryA != 0;
    }

    /// <summary>The primary destination container's slot.</summary>
    /// <param name="slot">The slot when set.</param>
    /// <returns>True when set.</returns>
    public readonly bool TryGetPrimaryDestination(out ushort slot)
    {
        slot = (ushort)Math.Max(0, _primaryB - 1);
        return _primaryB != 0;
    }

    /// <summary>
    /// Records one block the commit will acquire, sized at the moment it is acquired (a union with a none input and a <c>TryAppend</c> on a
    /// none handle still acquire one; a chained union needs <c>|a|+|b|</c> and then <c>|a|+|b|+|c|</c>). Beyond
    /// <see cref="MaxAcquiredBlocks"/> is a handler programming error, not genuine exhaustion: it asserts
    /// <see cref="SimAssertCode.InventoryPlanCapacityExceeded"/> under <c>INVENTORY_ASSERT</c>; Release marks the plan overflowed and the
    /// reservation step conservatively rejects with <c>capacity_exhausted</c> (85), never a false accept. The size is stored as a
    /// <c>ushort</c> clamped to 0 to 65,535: at or below 0 is ignored by the reservation and anything over
    /// <see cref="Storage.MailIdArena.MaxUnits"/> (256) still fails it, as before.
    /// </summary>
    /// <param name="units">The block's unit count.</param>
    /// <returns>False when the plan overflowed.</returns>
    public bool AddAcquiredBlock(int units)
    {
        InventoryAssert.That(_blockCount < MaxAcquiredBlocks, SimAssertCode.InventoryPlanCapacityExceeded);
        if (_blockCount >= MaxAcquiredBlocks)
        {
            _blocksOverflowed = true;
            return false;
        }

        _blocks[_blockCount++] = (ushort)Math.Clamp(units, 0, ushort.MaxValue);
        return true;
    }

    private readonly int IndexOf(ushort containerSlot)
    {
        for (int i = 0; i < _touchedCount; i++)
        {
            if (_touched[i] == containerSlot)
            {
                return i;
            }
        }

        return -1;
    }
}
