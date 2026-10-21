using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Placement;

/// <summary>Whether AutoPlace runs PASS M (ADR-0009 Key Interfaces; GDD R8). Spawn and Intake insertion use <see cref="NoMerge"/> (R14).</summary>
public enum MergePolicy : byte
{
    /// <summary>Run PASS M (merge into non-full stacks of the same StackKey) before the free-cell passes.</summary>
    Merge = 0,

    /// <summary>Skip PASS M: only free cells are used.</summary>
    NoMerge = 1,
}

/// <summary>How AutoPlace orders its passes over the targets (GDD R8).</summary>
public enum PlaceOrder : byte
{
    /// <summary>The default: PASS M over all targets, then PASS U over all targets, then PASS R over all targets (rotation is a last resort across the whole list).</summary>
    PassFirst = 0,

    /// <summary>Mail catch and WorldItem pickup (R9): M, U, R on each target before the next target (the hotbar fills first).</summary>
    TargetFirst = 1,
}

/// <summary>One planned PASS M merge: <see cref="Units"/> of the subject's oldest units into the stack in <see cref="TargetEntrySlot"/>.</summary>
/// <example><code>AutoPlaceMerge m = plan.Merge(0); int room = m.Units;</code></example>
public readonly struct AutoPlaceMerge
{
    /// <summary>The target stack's pool slot.</summary>
    public readonly ushort TargetEntrySlot;

    /// <summary>The units merged into it (1 to its room, at most 255).</summary>
    public readonly byte Units;

    /// <summary>Creates a merge.</summary>
    /// <param name="targetEntrySlot">The target stack's pool slot.</param>
    /// <param name="units">The units merged, 1 to 255.</param>
    public AutoPlaceMerge(ushort targetEntrySlot, byte units)
    {
        TargetEntrySlot = targetEntrySlot;
        Units = units;
    }
}

/// <summary>A PASS U / PASS R scan that found no free anchor: the negative-fit fact AutoPlace's <c>Commit</c> (or an API caller) records.</summary>
/// <example><code>AutoPlaceNoFit f = plan.NoFit(0); bool rotatedPass = f.Rotated;</code></example>
public readonly struct AutoPlaceNoFit
{
    /// <summary>The container's table slot.</summary>
    public readonly ushort ContainerSlot;

    /// <summary>The unrotated footprint width scanned (1 on a slots layout).</summary>
    public readonly byte W;

    /// <summary>The unrotated footprint height scanned (1 on a slots layout).</summary>
    public readonly byte H;

    /// <summary>The pass's rotation (PASS R).</summary>
    public readonly bool Rotated;

    /// <summary>The container's version when the scan found no fit.</summary>
    public readonly uint Version;

    /// <summary>Creates a no-fit fact.</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="w">The unrotated width.</param>
    /// <param name="h">The unrotated height.</param>
    /// <param name="rotated">The rotation.</param>
    /// <param name="version">The version scanned.</param>
    public AutoPlaceNoFit(ushort containerSlot, byte w, byte h, bool rotated, uint version)
    {
        ContainerSlot = containerSlot;
        W = w;
        H = h;
        Rotated = rotated;
        Version = version;
    }
}

/// <summary>The target slots of up to <see cref="AutoPlacePlan.MaxMerges"/> merges.</summary>
[InlineArray(AutoPlacePlan.MaxMerges)]
internal struct AutoPlaceMergeSlotBuffer
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private ushort _e0;
#pragma warning restore CS0169
}

/// <summary>The unit counts of up to <see cref="AutoPlacePlan.MaxMerges"/> merges (a parallel buffer, so no padding per merge).</summary>
[InlineArray(AutoPlacePlan.MaxMerges)]
internal struct AutoPlaceMergeUnitsBuffer
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private byte _e0;
#pragma warning restore CS0169
}

/// <summary>Up to <see cref="AutoPlacePlan.MaxNoFits"/> no-fit facts.</summary>
[InlineArray(AutoPlacePlan.MaxNoFits)]
internal struct AutoPlaceNoFitBuffer
{
#pragma warning disable CS0169 // The single element the InlineArray repeats; never referenced by name.
    private AutoPlaceNoFit _e0;
#pragma warning restore CS0169
}

/// <summary>
/// The fixed-size result of one AutoPlace planning call (story grid-inventory/009, Planner R3): the PASS M merges in pass order, at most
/// one free-cell <see cref="Placement"/> for the remainder, the unplaced count, and the negative-fit facts the scans found. It sits beside
/// the call's <see cref="InvPlan"/> (which it does not widen) and is applied unchanged by <see cref="AutoPlace.Commit(ref InvPlan, in AutoPlacePlan)"/>
/// (ADR-0009 section 2: commit never recomputes). A plain stack value with <c>[InlineArray]</c> buffers; a default value is an empty plan.
/// </summary>
/// <example>
/// <code>
/// ushort reason = autoPlace.Plan(ref plan, in subject, units, sourceSlot, targets, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);
/// if (reason == 0) { autoPlace.Commit(ref plan, in ap); }
/// </code>
/// </example>
public struct AutoPlacePlan
{
    /// <summary>
    /// The most merges one AutoPlace can plan, an exact bound whatever the geometry (TD-116, Planner R4): one call moves at most 255
    /// units (<c>units</c> is 1 to 255) and every merge takes at least one, so a legal PASS M never stops early (GDD R8). Each mail
    /// merge acquires one arena block, so <see cref="InvPlan.MaxAcquiredBlocks"/> holds a full merge list plus
    /// <see cref="InvPlan.HandlerBlockHeadroom"/>. Reaching it with units left is unreachable and asserts
    /// <see cref="SimAssertCode.InventoryAutoPlaceMergeBoundExceeded"/> (Release: <c>bad_request</c>, nothing mutated).
    /// </summary>
    public const int MaxMerges = byte.MaxValue;

    /// <summary>The most targets one AutoPlace takes (R9 rows have at most 3); more is a <c>bad_request</c> handler-limit refusal.</summary>
    public const int MaxTargets = 8;

    /// <summary>The most no-fit facts one plan holds (PASS U and PASS R on every target).</summary>
    public const int MaxNoFits = MaxTargets * 2;

    private AutoPlaceMergeSlotBuffer _mergeSlots;
    private AutoPlaceMergeUnitsBuffer _mergeUnits;
    private AutoPlaceNoFitBuffer _noFits;
    private byte _mergeCount;
    private byte _noFitCount;
    private byte _mergedUnits;

    /// <summary>The free-cell placement of the remainder (<see cref="InvPlacement.EntrySlot"/> is the source, or <see cref="AutoPlace.NoSource"/>); unset when none.</summary>
    public InvPlacement Placement;

    /// <summary>The units the free-cell placement places (0 when <see cref="Placement"/> is unset).</summary>
    public int PlacementUnits;

    /// <summary>The units asked for.</summary>
    public int Requested;

    /// <summary>The units neither merged nor placed (they stay on the source; the newest ids, R6).</summary>
    public int Unplaced;

    /// <summary>The source entry's pool slot, or <see cref="AutoPlace.NoSource"/> (a spawn whose entry <c>Commit</c> creates).</summary>
    public ushort SourceSlot;

    /// <summary>The number of planned merges.</summary>
    public readonly int MergeCount => _mergeCount;

    /// <summary>The units the merges move in total.</summary>
    public readonly int MergedUnits => _mergedUnits;

    /// <summary>The number of no-fit facts.</summary>
    public readonly int NoFitCount => _noFitCount;

    /// <summary>The units merged or placed.</summary>
    public readonly int MovedUnits => _mergedUnits + PlacementUnits;

    /// <summary>The <paramref name="index"/>-th merge, in PASS M order.</summary>
    /// <param name="index">0 to <see cref="MergeCount"/> - 1.</param>
    /// <returns>The merge.</returns>
    public readonly AutoPlaceMerge Merge(int index) => new(_mergeSlots[index], _mergeUnits[index]);

    /// <summary>The <paramref name="index"/>-th no-fit fact, in scan order.</summary>
    /// <param name="index">0 to <see cref="NoFitCount"/> - 1.</param>
    /// <returns>The fact.</returns>
    public readonly AutoPlaceNoFit NoFit(int index) => _noFits[index];

    /// <summary>Adds a merge; the caller keeps <see cref="MergeCount"/> below <see cref="MaxMerges"/> and the merged total at most 255.</summary>
    internal void AddMerge(ushort targetEntrySlot, byte units)
    {
        _mergeSlots[_mergeCount] = targetEntrySlot;
        _mergeUnits[_mergeCount] = units;
        _mergeCount++;
        _mergedUnits += units;
    }

    internal void AddNoFit(in AutoPlaceNoFit fact)
    {
        if (_noFitCount < MaxNoFits)
        {
            _noFits[_noFitCount++] = fact;
        }
    }
}

/// <summary>
/// AutoPlace (GDD R8; ADR-0009 sections 2 and 5; TR-inventory-007; story grid-inventory/009), shared by quick_move, spawn, return,
/// inserter, belt, pickup, catch and grant. <see cref="Plan"/> is pure (the <c>Validate</c> half): it reads state, writes only the
/// <see cref="InvPlan"/> and the <see cref="AutoPlacePlan"/>, and runs
/// <list type="bullet">
/// <item><b>PASS M</b> (skipped for <see cref="MergePolicy.NoMerge"/> or <c>effMax == 1</c>): for each target, the target's StackKey
/// non-full list (<see cref="StackIndex"/>, row-major; slot order on a slots layout), re-checking <c>count &lt; effMax</c> at op time and
/// <see cref="PlacementRules.CanMerge"/>; each stack takes <c>min(remaining, effMax − count)</c> of the oldest units. Never a container
/// scan;</item>
/// <item><b>PASS U</b>: per target, the first free top-left anchor (rows outer, columns inner) of the unrotated footprint, each anchor
/// one probe of the occupancy row masks; a slots layout takes the lowest free usable slot ≥ 1 (a reserved slot is skipped, never
/// returned, never slot 0); a shape the negative-fit cache knows does not fit at the container's version is skipped with 0 probes;</item>
/// <item><b>PASS R</b> (only when <c>w ≠ h</c>, never on a slots layout): the same scan rotated.</item>
/// </list>
/// <see cref="PlaceOrder.PassFirst"/> runs each pass over all targets; <see cref="PlaceOrder.TargetFirst"/> runs M → U → R per target.
/// A target the <see cref="InsertionWhitelist"/> rejects (checked once per target) is skipped in every pass. Stackables may place
/// partially (oldest ids first, the newest stay on the source); a non-stackable (<c>effMax == 1</c>) is all-or-nothing.
/// <para>
/// Replies: nothing moved and no target admitted → <c>not_carryable</c> if a target was skipped for carryability, else
/// <c>not_allowed</c> (ADR-0009 section 2 chain order); nothing moved otherwise → <c>no_room</c>; something moved → 0 (ok, with
/// <see cref="AutoPlacePlan.Unplaced"/>). A handler-limit refusal is <c>bad_request</c>: units outside 1 to 255 (or above the source's
/// count), no or too many targets, a dead container or source, or a container the plan cannot touch (<see cref="InvPlan.Touch"/> -1).
/// Every container the commit may change (the placed source's and each merge or placement target) is touched; each mail merge adds its
/// union's block to <see cref="InvPlan.AddAcquiredBlock"/> (the handler runs the reservation step, reason 85, last).
/// </para>
/// <para>
/// <see cref="Commit(ref InvPlan, in AutoPlacePlan)"/> applies the plan through <see cref="InventoryMutator.MergeInto"/> then
/// <see cref="InventoryMutator.Place"/> (detached source) or <see cref="InventoryMutator.Transfer"/> (placed source), never recomputing,
/// and writes the plan's no-fit facts to the <see cref="NegativeFitCache"/>; <see cref="Plan"/> only reads it (ADR-0009 section 2). The
/// probe counters <see cref="CounterId.InventoryMergeCandidates"/> and <see cref="CounterId.InventoryPlacementProbes"/> are incremented
/// once per list node and once per anchor, in every build. Allocation-free; never throws on the tick path. Not thread-safe: Sim tick
/// thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// PlacementSubject s = PlacementSubject.Of(content, stats, in entries[cursorEntry]);
/// ReadOnlySpan&lt;ushort&gt; targets = stackalloc ushort[] { inventorySlot, backpackSlot };
/// ushort reason = autoPlace.Plan(ref plan, in s, entries[cursorEntry].Count, cursorEntry, targets, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan ap);
/// if (reason == 0) { reason = requestCheck.CheckArenaReservation(in plan); }
/// if (reason == 0) { mutator.Begin(ref plan, actor, InvCause.None); autoPlace.Commit(ref plan, in ap); mutator.End(ref plan, ref extra); }
/// </code>
/// </example>
internal sealed class AutoPlace
{
    /// <summary>The source slot value meaning "no source entry yet" (a spawn: its <c>Commit</c> creates the entry).</summary>
    public const ushort NoSource = EntryRecord.NoContainer;

    private const ushort Pass = 0;

    private readonly ContainerTable _containers;
    private readonly DefTable<ContainerDef> _defs;
    private readonly EntryPool _entries;
    private readonly Occupancy _occupancy;
    private readonly InventoryMutator _mutator;
    private readonly NegativeFitCache _noFit;
    private readonly SimCounters _counters;

    /// <summary>Creates AutoPlace over the module's storage.</summary>
    /// <param name="containers">The container table.</param>
    /// <param name="defs">The container defs (whitelist, reserved slots).</param>
    /// <param name="entries">The entry pool.</param>
    /// <param name="occupancy">The occupancy grids.</param>
    /// <param name="mutator">The mutation layer (its <see cref="InventoryMutator.StackIndex"/> drives PASS M; its primitives commit).</param>
    /// <param name="noFit">The negative-fit cache.</param>
    /// <param name="counters">The world's counters (the probe counters).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public AutoPlace(
        ContainerTable containers, DefTable<ContainerDef> defs, EntryPool entries, Occupancy occupancy, InventoryMutator mutator, NegativeFitCache noFit, SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(defs);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(occupancy);
        ArgumentNullException.ThrowIfNull(mutator);
        ArgumentNullException.ThrowIfNull(noFit);
        ArgumentNullException.ThrowIfNull(counters);
        _containers = containers;
        _defs = defs;
        _entries = entries;
        _occupancy = occupancy;
        _mutator = mutator;
        _noFit = noFit;
        _counters = counters;
    }

    /// <summary>The negative-fit cache (tests disable it for the identical-run property).</summary>
    public NegativeFitCache NoFitCache => _noFit;

    /// <summary>
    /// Plans AutoPlacing <paramref name="units"/> of <paramref name="subject"/> into <paramref name="targets"/> (see the class summary).
    /// Pure: writes only <paramref name="plan"/> (touches, acquired blocks) and <paramref name="result"/>.
    /// </summary>
    /// <param name="plan">The call's plan.</param>
    /// <param name="subject">What is placed, with <c>effMax</c> read at op time.</param>
    /// <param name="units">How many units, 1 to 255; with a source, at most its count. The free-cell placement moves the source entry
    /// itself, so it is planned only when <paramref name="units"/> is the source's whole count (a part of a stack goes only into merges;
    /// splitting first is story 008's).</param>
    /// <param name="sourceSlot">The source entry's pool slot (placed or detached), or <see cref="NoSource"/>.</param>
    /// <param name="targets">The target containers' table slots in order (1 to <see cref="AutoPlacePlan.MaxTargets"/>).</param>
    /// <param name="policy">Whether PASS M runs.</param>
    /// <param name="order">Pass-first or target-first.</param>
    /// <param name="result">The plan to commit (or to record no-fits from).</param>
    /// <returns>0 (ok), <c>bad_request</c>, <c>not_carryable</c>, <c>not_allowed</c> or <c>no_room</c>.</returns>
    public ushort Plan(
        ref InvPlan plan,
        in PlacementSubject subject,
        int units,
        ushort sourceSlot,
        ReadOnlySpan<ushort> targets,
        MergePolicy policy,
        PlaceOrder order,
        out AutoPlacePlan result)
    {
        result = default;
        result.SourceSlot = sourceSlot;
        result.Requested = units;
        if (units < 1 || units > byte.MaxValue || targets.Length == 0 || targets.Length > AutoPlacePlan.MaxTargets)
        {
            return CoreReason.BadRequest;
        }

        int sourceCount = 0;
        if (sourceSlot != NoSource)
        {
            if (sourceSlot >= _entries.Capacity || !_entries[sourceSlot].IsLive || units > _entries[sourceSlot].Count)
            {
                return CoreReason.BadRequest;
            }

            sourceCount = _entries[sourceSlot].Count;
        }

        Span<bool> admitted = stackalloc bool[AutoPlacePlan.MaxTargets];
        bool anyAdmitted = false;
        bool anyNotCarryable = false;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] >= _containers.Capacity || !_containers[targets[i]].IsLive)
            {
                return CoreReason.BadRequest;
            }

            InvReason verdict = InsertionWhitelist.Check(_defs[_containers[targets[i]].DefIndex], in subject);
            admitted[i] = verdict == 0;
            anyAdmitted |= admitted[i];
            anyNotCarryable |= verdict == InvReason.NotCarryable;
        }

        if (!anyAdmitted)
        {
            return (ushort)(anyNotCarryable ? InvReason.NotCarryable : InvReason.NotAllowed);
        }

        // The placed source's container changes whatever moves, so it is touched first (its records precede the targets').
        if (sourceSlot != NoSource && _entries[sourceSlot].ContainerSlot != EntryRecord.NoContainer && plan.Touch(_entries[sourceSlot].ContainerSlot) < 0)
        {
            return CoreReason.BadRequest;
        }

        var pass = new PassState(subject, units, sourceSlot, sourceCount, policy == MergePolicy.Merge && subject.EffMax > 1);
        ushort reason = order == PlaceOrder.TargetFirst
            ? RunTargetFirst(ref plan, ref result, ref pass, targets, admitted)
            : RunPassFirst(ref plan, ref result, ref pass, targets, admitted);
        if (reason != Pass)
        {
            return reason;
        }

        result.Unplaced = units - result.MovedUnits;
        return result.MovedUnits == 0 ? (ushort)InvReason.NoRoom : Pass;
    }

    /// <summary>Applies <paramref name="result"/> with its own <see cref="AutoPlacePlan.SourceSlot"/> (see <see cref="Commit(ref InvPlan, in AutoPlacePlan, ushort)"/>).</summary>
    /// <param name="plan">The call's plan (between <see cref="InventoryMutator.Begin"/> and <see cref="InventoryMutator.End"/>).</param>
    /// <param name="result">The plan <see cref="Plan"/> returned 0 for.</param>
    public void Commit(ref InvPlan plan, in AutoPlacePlan result) => Commit(ref plan, in result, result.SourceSlot);

    /// <summary>
    /// Applies <paramref name="result"/> unchanged (ADR-0009 section 2): records its no-fit facts, then each merge through
    /// <see cref="InventoryMutator.MergeInto"/> (the oldest units leave <paramref name="sourceSlot"/> first) and the free-cell placement
    /// of what remains through <see cref="InventoryMutator.Place"/> (a detached source) or <see cref="InventoryMutator.Transfer"/> (a
    /// placed source). Never recomputes and never fails.
    /// </summary>
    /// <param name="plan">The call's plan (between <see cref="InventoryMutator.Begin"/> and <see cref="InventoryMutator.End"/>).</param>
    /// <param name="result">The plan <see cref="Plan"/> returned 0 for.</param>
    /// <param name="sourceSlot">The source entry: the planned one, or for a <see cref="NoSource"/> plan the entry the caller just created holding the planned units.</param>
    public void Commit(ref InvPlan plan, in AutoPlacePlan result, ushort sourceSlot)
    {
        RecordNoFits(in result);
        for (int i = 0; i < result.MergeCount; i++)
        {
            AutoPlaceMerge m = result.Merge(i);
            _mutator.MergeInto(ref plan, sourceSlot, m.TargetEntrySlot, m.Units);
        }

        InvPlacement p = result.Placement;
        if (!p.IsSet)
        {
            return;
        }

        if (_entries[sourceSlot].ContainerSlot == EntryRecord.NoContainer)
        {
            _mutator.Place(ref plan, sourceSlot, p.ContainerSlot, p.X, p.Y, p.Rotated);
        }
        else
        {
            _mutator.Transfer(ref plan, sourceSlot, p.ContainerSlot, p.X, p.Y, p.Rotated);
        }
    }

    /// <summary>
    /// Writes <paramref name="result"/>'s no-fit facts to the negative-fit cache, each at the version it was scanned at. The
    /// <c>Commit</c> path calls it; an <c>IInventoryApi</c>-style caller may call it after a rejected plan (outside <c>Validate</c>,
    /// ADR-0009 section 2), so a repeat at the same version probes nothing.
    /// </summary>
    /// <param name="result">A plan from <see cref="Plan"/>.</param>
    public void RecordNoFits(in AutoPlacePlan result)
    {
        for (int i = 0; i < result.NoFitCount; i++)
        {
            AutoPlaceNoFit f = result.NoFit(i);
            _noFit.RecordNoFit(f.ContainerSlot, in _containers[f.ContainerSlot], f.W, f.H, f.Rotated, f.Version);
        }
    }

    // fw == 64 would shift by a masked 0 in C#, so the full mask is special-cased.
    private static ulong RowMask(int fw) => fw >= Occupancy.BitmapWidth ? ulong.MaxValue : (1UL << fw) - 1UL;

    private ushort RunPassFirst(ref InvPlan plan, ref AutoPlacePlan result, ref PassState pass, ReadOnlySpan<ushort> targets, ReadOnlySpan<bool> admitted)
    {
        for (int i = 0; i < targets.Length && pass.Merging && pass.Remaining > 0; i++)
        {
            ushort reason = admitted[i] ? PassM(ref plan, ref result, ref pass, targets[i]) : Pass;
            if (reason != Pass)
            {
                return reason;
            }
        }

        for (int r = 0; r < 2; r++)
        {
            for (int i = 0; i < targets.Length && pass.CanPlaceFree(in result); i++)
            {
                ushort reason = admitted[i] ? PassFree(ref plan, ref result, ref pass, targets[i], rotated: r == 1) : Pass;
                if (reason != Pass)
                {
                    return reason;
                }
            }
        }

        return Pass;
    }

    private ushort RunTargetFirst(ref InvPlan plan, ref AutoPlacePlan result, ref PassState pass, ReadOnlySpan<ushort> targets, ReadOnlySpan<bool> admitted)
    {
        for (int i = 0; i < targets.Length && pass.Remaining > 0 && !result.Placement.IsSet; i++)
        {
            if (!admitted[i])
            {
                continue;
            }

            ushort reason = pass.Merging ? PassM(ref plan, ref result, ref pass, targets[i]) : Pass;
            for (int r = 0; r < 2 && reason == Pass && pass.CanPlaceFree(in result); r++)
            {
                reason = PassFree(ref plan, ref result, ref pass, targets[i], rotated: r == 1);
            }

            if (reason != Pass)
            {
                return reason;
            }
        }

        return Pass;
    }

    /// <summary>PASS M on one target: walk its StackKey list (one probe per node), merging into each mergeable non-full stack.</summary>
    private ushort PassM(ref InvPlan plan, ref AutoPlacePlan result, ref PassState pass, ushort containerSlot)
    {
        ContainerDef def = _defs[_containers[containerSlot].DefIndex];
        StackIndex index = _mutator.StackIndex;
        ref readonly PlacementSubject subject = ref pass.Subject;
        bool mail = !subject.Key.IsItem;
        for (ushort s = index.Head(containerSlot, subject.Key); s != StackIndex.None && pass.Remaining > 0; s = index.Next(s))
        {
            _counters.Add(CounterId.InventoryMergeCandidates);
            ref EntryRecord stack = ref _entries[s];
            if (s == pass.SourceSlot || stack.Count >= subject.EffMax
                || !PlacementRules.CanMerge(subject.Key, StackKey.Of(in stack), subject.EffMax, def, subject.Category))
            {
                continue;
            }

            // Unreachable (TD-116): units <= 255 and every merge takes >= 1, so MaxMerges (255) merges leave nothing remaining.
            bool withinBound = result.MergeCount < AutoPlacePlan.MaxMerges;
            InventoryAssert.That(withinBound, SimAssertCode.InventoryAutoPlaceMergeBoundExceeded);
            if (!withinBound)
            {
                return CoreReason.BadRequest; // Release: a handler-limit refusal before any mutation (Plan is pure)
            }

            if (plan.Touch(containerSlot) < 0)
            {
                return CoreReason.BadRequest;
            }

            int take = Math.Min(pass.Remaining, subject.EffMax - stack.Count);
            result.AddMerge(s, (byte)take); // 1 <= take <= remaining <= 255
            if (mail)
            {
                _ = plan.AddAcquiredBlock(stack.Count + take); // the union's block: target count + moved units
            }

            pass.Remaining -= take;
        }

        return Pass;
    }

    /// <summary>PASS U (or PASS R when <paramref name="rotated"/>) on one target: the first free anchor, one probe each.</summary>
    private ushort PassFree(ref InvPlan plan, ref AutoPlacePlan result, ref PassState pass, ushort containerSlot, bool rotated)
    {
        ref ContainerHeader h = ref _containers[containerSlot];
        bool slots = h.Layout == ContainerLayout.Slots;
        byte w = slots ? (byte)1 : pass.Subject.W;
        byte hh = slots ? (byte)1 : pass.Subject.H;
        if (rotated && w == hh)
        {
            return Pass; // PASS R runs only for w != h (never on a slots layout)
        }

        if (_noFit.IsNoFit(containerSlot, in h, w, hh, rotated))
        {
            return Pass;
        }

        bool found = slots
            ? ScanSlots(containerSlot, in h, out int x, out int y)
            : ScanGrid(containerSlot, in h, rotated ? hh : w, rotated ? w : hh, out x, out y);
        if (!found)
        {
            result.AddNoFit(new AutoPlaceNoFit(containerSlot, w, hh, rotated, h.Version));
            return Pass;
        }

        if (plan.Touch(containerSlot) < 0)
        {
            return CoreReason.BadRequest;
        }

        result.Placement = new InvPlacement(pass.SourceSlot, containerSlot, (byte)x, (byte)y, rotated);
        result.PlacementUnits = pass.PlacementUnits;
        pass.Remaining -= pass.PlacementUnits;
        return Pass;
    }

    private bool ScanGrid(ushort containerSlot, in ContainerHeader h, int fw, int fh, out int foundX, out int foundY)
    {
        ulong mask = RowMask(fw);
        for (int y = 0; y <= h.H - fh; y++)
        {
            for (int x = 0; x <= h.W - fw; x++)
            {
                _counters.Add(CounterId.InventoryPlacementProbes);
                ulong shifted = mask << x;
                int r = 0;
                while (r < fh && (_occupancy.RowBits(containerSlot, y + r) & shifted) == 0UL)
                {
                    r++;
                }

                if (r == fh)
                {
                    foundX = x;
                    foundY = y;
                    return true;
                }
            }
        }

        foundX = 0;
        foundY = 0;
        return false;
    }

    private bool ScanSlots(ushort containerSlot, in ContainerHeader h, out int foundX, out int foundY)
    {
        ContainerDef def = _defs[h.DefIndex];
        int slotCount = h.W * h.H;
        for (int index = 1; index < slotCount; index++)
        {
            int x = index % h.W;
            int y = index / h.W;
            if (PlacementRules.IsReservedSlot(def, h.W, h.H, x, y))
            {
                continue; // reserved_slot is skipped, never returned
            }

            _counters.Add(CounterId.InventoryPlacementProbes);
            if ((_occupancy.RowBits(containerSlot, y) & (1UL << x)) == 0UL)
            {
                foundX = x;
                foundY = y;
                return true;
            }
        }

        foundX = 0;
        foundY = 0;
        return false;
    }

    /// <summary>The per-call pass state: the subject, the remaining units and whether PASS M still runs.</summary>
    private struct PassState
    {
        public readonly PlacementSubject Subject;
        public readonly ushort SourceSlot;
        public int Remaining;
        public bool Merging;
        private readonly int _units;
        private readonly int _sourceCount;

        public PassState(in PlacementSubject subject, int units, ushort sourceSlot, int sourceCount, bool merging)
        {
            Subject = subject;
            SourceSlot = sourceSlot;
            Remaining = units;
            Merging = merging;
            _units = units;
            _sourceCount = sourceCount;
        }

        /// <summary>
        /// The units a free-cell placement would place now: everything remaining for a spawn (at most <c>effMax</c>, one entry); for a
        /// source entry, its whole remainder, and only when the call places the source's whole count (the entry itself moves).
        /// </summary>
        public readonly int PlacementUnits =>
            SourceSlot == AutoPlace.NoSource
                ? Math.Min(Remaining, Subject.EffMax)
                : _units == _sourceCount && Remaining <= Math.Max(1, Subject.EffMax) ? Remaining : 0;

        /// <summary>True while units remain, no free cell is chosen yet, and a placement could move some.</summary>
        public readonly bool CanPlaceFree(in AutoPlacePlan result) => Remaining > 0 && !result.Placement.IsSet && PlacementUnits > 0;
    }
}
