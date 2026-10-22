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

    /// <summary>True when a merge into <paramref name="targetEntrySlot"/> is already planned (PASS M's INVENTORY_ASSERT that no stack is planned twice).</summary>
    internal readonly bool HasMergeInto(ushort targetEntrySlot)
    {
        for (int i = 0; i < _mergeCount; i++)
        {
            if (_mergeSlots[i] == targetEntrySlot)
            {
                return true;
            }
        }

        return false;
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
/// <see cref="PlacementRules.CanMerge"/>; each stack takes <c>min(remaining, effMax − count)</c> of the oldest units and is planned at
/// most once per plan. Never a container scan;</item>
/// <item><b>PASS U</b>: per target, the first free top-left anchor (rows outer, columns inner) of the unrotated footprint, each anchor
/// tested against all <c>fh</c> of its footprint's occupancy row masks (no early exit), so it examines <c>fw × fh</c> cells; a slots
/// layout takes the lowest free usable slot ≥ 1 (a reserved slot is skipped, never returned, never slot 0), one cell per slot visited;
/// a shape the negative-fit cache knows does not fit at the container's version is skipped with 0 probes;</item>
/// <item><b>PASS R</b> (only when <c>w ≠ h</c>, never on a slots layout): the same scan rotated.</item>
/// </list>
/// <see cref="PlaceOrder.PassFirst"/> runs each pass over all targets; <see cref="PlaceOrder.TargetFirst"/> runs M → U → R per target.
/// A target the <see cref="InsertionWhitelist"/> rejects (checked once per target) is skipped in every pass. Stackables may place
/// partially (oldest ids first, the newest stay on the source); a non-stackable (<c>effMax == 1</c>) is all-or-nothing.
/// <para>
/// Replies: nothing moved and no target admitted → <c>not_carryable</c> if a target was skipped for carryability, else
/// <c>not_allowed</c> (ADR-0009 section 2 chain order); nothing moved otherwise → <c>no_room</c>; something moved → 0 (ok, with
/// <see cref="AutoPlacePlan.Unplaced"/>). A handler-limit refusal is <c>bad_request</c>: units outside 1 to 255 (or above the source's
/// count), no or too many targets, a container named twice in the target list, a dead container or source, or a container the plan
/// cannot touch (<see cref="InvPlan.Touch"/> -1).
/// Every container the commit may change (the placed source's and each merge or placement target) is touched; each mail merge adds its
/// union's block to <see cref="InvPlan.AddAcquiredBlock"/> (the handler runs the reservation step, reason 85, last).
/// </para>
/// <para>
/// <see cref="Commit(ref InvPlan, in AutoPlacePlan)"/> applies the plan through <see cref="InventoryMutator.MergeInto"/> then
/// <see cref="InventoryMutator.Place"/> (detached source) or <see cref="InventoryMutator.Transfer"/> (placed source), never recomputing,
/// and writes the plan's no-fit facts to the <see cref="NegativeFitCache"/>; <see cref="Plan"/> only reads it (ADR-0009 section 2). The
/// probe counters <see cref="CounterId.InventoryMergeCandidates"/> and <see cref="CounterId.InventoryPlacementProbes"/> are incremented
/// once per list node and once per cell examined (GDD H-PERF-6, per cell per Evan 2026-10-21): a grid anchor adds its footprint area
/// <c>fw × fh</c>, a slot adds 1, so a scan costs (anchors visited) × area, at most Σ (W − fw + 1)(H − fh + 1) × fw × fh over the
/// shapes scanned (6,684 for the six v1 oriented shapes on the 20 × 16 Intake, inside GDD D3's 8,320 and the 8,500 gate), in every
/// build. Allocation-free; never throws on the tick path. Not thread-safe: Sim tick thread only.
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
        out AutoPlacePlan result) =>
        PlanCore(ref plan, in subject, units, sourceSlot, remainder: false, NoSource, targets, policy, order, out result);

    /// <summary>
    /// <see cref="Plan"/> for the last <paramref name="units"/> units of the placed source <paramref name="sourceSlot"/>, after the
    /// caller's own merge in the same plan takes the source's other (oldest) units into <paramref name="skipStack"/> (story
    /// grid-inventory/010: <c>return</c>'s partial merge at the origin, then the panel leg, GDD R7 l.95). The units are planned as if they
    /// were the source's whole count, so a free-cell placement moves the source entry itself with what the merges leave; PASS M skips
    /// <paramref name="skipStack"/>, whose count here is the pre-plan one and which the caller's merge fills. Commit with
    /// <see cref="Commit(ref InvPlan, in AutoPlacePlan)"/> after the caller's <see cref="InventoryMutator.MergeInto"/>. Pure, as
    /// <see cref="Plan"/>.
    /// </summary>
    /// <param name="plan">The call's plan (holding the caller's merge and its touches).</param>
    /// <param name="subject">What is placed, with <c>effMax</c> read at op time.</param>
    /// <param name="units">The units left after the caller's merge, 1 to the source's count - 1.</param>
    /// <param name="sourceSlot">The placed source entry's pool slot.</param>
    /// <param name="skipStack">The stack the caller's merge fills (never planned here).</param>
    /// <param name="targets">The target containers' table slots in order.</param>
    /// <param name="policy">Whether PASS M runs.</param>
    /// <param name="order">Pass-first or target-first.</param>
    /// <param name="result">The plan to commit.</param>
    /// <returns>As <see cref="Plan"/>.</returns>
    public ushort PlanRemainder(
        ref InvPlan plan,
        in PlacementSubject subject,
        int units,
        ushort sourceSlot,
        ushort skipStack,
        ReadOnlySpan<ushort> targets,
        MergePolicy policy,
        PlaceOrder order,
        out AutoPlacePlan result) =>
        PlanCore(ref plan, in subject, units, sourceSlot, remainder: true, skipStack, targets, policy, order, out result);

    private ushort PlanCore(
        ref InvPlan plan,
        in PlacementSubject subject,
        int units,
        ushort sourceSlot,
        bool remainder,
        ushort skipStack,
        ReadOnlySpan<ushort> targets,
        MergePolicy policy,
        PlaceOrder order,
        out AutoPlacePlan result)
    {
        result = default;
        result.SourceSlot = sourceSlot;
        result.Requested = units;
        if (!IsWellFormed(units, sourceSlot, targets))
        {
            return CoreReason.BadRequest;
        }

        Span<bool> admitted = stackalloc bool[AutoPlacePlan.MaxTargets];
        ushort refusal = Admit(in subject, targets, admitted);
        if (refusal != Pass)
        {
            return refusal;
        }

        // The placed source's container changes whatever moves, so it is touched first (its records precede the targets').
        if (sourceSlot != NoSource && _entries[sourceSlot].ContainerSlot != EntryRecord.NoContainer && plan.Touch(_entries[sourceSlot].ContainerSlot) < 0)
        {
            return CoreReason.BadRequest;
        }

        int sourceCount = sourceSlot == NoSource ? 0 : remainder ? units : _entries[sourceSlot].Count;
        var pass = new PassState(subject, units, sourceSlot, skipStack, sourceCount, policy == MergePolicy.Merge && subject.EffMax > 1);
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
    /// Applies a <see cref="NoSource"/> plan of <c>k</c> units (<paramref name="result"/>, planned with <c>units = k</c>) as a split of
    /// the k <b>oldest</b> units of the placed entry <paramref name="sourceSlot"/> (story grid-inventory/010: a partial <c>quick_move</c>,
    /// GDD R7 l.93 "partial allowed for stackables"; R6 oldest first). Records the no-fit facts; each merge takes its units straight from
    /// the source through <see cref="InventoryMutator.MergeInto"/> (oldest first, the source keeps its id and cells); then the free-cell
    /// placement, if any, is story 008's split primitive <see cref="InventoryMutator.SplitAndPlace"/> of
    /// <see cref="AutoPlacePlan.PlacementUnits"/> (the new entry's id is allocated there, in <c>Commit</c>). The units each target gets
    /// are the ones a split-off piece AutoPlaced in the same order would give it, but a split whose units all merge allocates no entry id
    /// and raises no <c>InvSplitFrom</c> for a piece that would never be placed. Precondition (the caller's <c>Validate</c>): the plan's
    /// units are below the source's count, so the split leaves a remainder; with a placement the pool has a free slot and id, and a mail
    /// split's block is in the plan's reservation. Never recomputes and never fails.
    /// </summary>
    /// <param name="plan">The call's plan (between <see cref="InventoryMutator.Begin"/> and <see cref="InventoryMutator.End"/>; the
    /// source's container touched first).</param>
    /// <param name="result">The <see cref="NoSource"/> plan <see cref="Plan"/> returned 0 for.</param>
    /// <param name="sourceSlot">The placed source entry's pool slot.</param>
    /// <param name="tick">The current tick (the split's check records).</param>
    public void CommitSplit(ref InvPlan plan, in AutoPlacePlan result, ushort sourceSlot, uint tick)
    {
        RecordNoFits(in result);
        for (int i = 0; i < result.MergeCount; i++)
        {
            AutoPlaceMerge m = result.Merge(i);
            _mutator.MergeInto(ref plan, sourceSlot, m.TargetEntrySlot, m.Units);
        }

        InvPlacement p = result.Placement;
        if (p.IsSet)
        {
            _ = _mutator.SplitAndPlace(ref plan, sourceSlot, result.PlacementUnits, p.ContainerSlot, p.X, p.Y, p.Rotated, tick);
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

    /// <summary>
    /// Writes <paramref name="result"/>'s no-fit facts at each container's <b>current</b> version instead of the scanned one: for a
    /// caller that only filled the scanned containers further after the scan and then sealed them (<see cref="InventorySystem.Spawn"/>:
    /// the failing id is scanned after every earlier id's placement, and <c>End</c>'s version bump is the only later change). Filling
    /// cells can only remove anchors, so a shape that found none still finds none. Outside <c>Validate</c>, like <see cref="RecordNoFits"/>.
    /// </summary>
    /// <param name="result">A refused plan from <see cref="Plan"/>.</param>
    public void RecordNoFitsAtCurrentVersion(in AutoPlacePlan result)
    {
        for (int i = 0; i < result.NoFitCount; i++)
        {
            AutoPlaceNoFit f = result.NoFit(i);
            ref readonly ContainerHeader h = ref _containers[f.ContainerSlot];
            _noFit.RecordNoFit(f.ContainerSlot, in h, f.W, f.H, f.Rotated, h.Version);
        }
    }

    /// <summary>
    /// The handler-limit checks (<c>bad_request</c> when false): units 1 to 255, 1 to <see cref="AutoPlacePlan.MaxTargets"/> live
    /// targets with no container named twice (GDD R8/R9 target lists are sets of distinct containers; a repeat would let PASS M plan
    /// the same stack twice), and a live source holding at least <paramref name="units"/>.
    /// </summary>
    private bool IsWellFormed(int units, ushort sourceSlot, ReadOnlySpan<ushort> targets)
    {
        if (units < 1 || units > byte.MaxValue || targets.Length == 0 || targets.Length > AutoPlacePlan.MaxTargets)
        {
            return false;
        }

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] >= _containers.Capacity || !_containers[targets[i]].IsLive || targets[..i].Contains(targets[i]))
            {
                return false;
            }
        }

        return sourceSlot == NoSource
            || (sourceSlot < _entries.Capacity && _entries[sourceSlot].IsLive && units <= _entries[sourceSlot].Count);
    }

    /// <summary>The insertion whitelist per target (once each): 0 when any admits, else <c>not_carryable</c> before <c>not_allowed</c>.</summary>
    private ushort Admit(in PlacementSubject subject, ReadOnlySpan<ushort> targets, Span<bool> admitted)
    {
        bool anyAdmitted = false;
        bool anyNotCarryable = false;
        for (int i = 0; i < targets.Length; i++)
        {
            InvReason verdict = InsertionWhitelist.Check(_defs[_containers[targets[i]].DefIndex], in subject);
            admitted[i] = verdict == 0;
            anyAdmitted |= admitted[i];
            anyNotCarryable |= verdict == InvReason.NotCarryable;
        }

        if (anyAdmitted)
        {
            return Pass;
        }

        return (ushort)(anyNotCarryable ? InvReason.NotCarryable : InvReason.NotAllowed);
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
            if (s == pass.SourceSlot || s == pass.SkipStack || stack.Count >= subject.EffMax
                || !PlacementRules.CanMerge(subject.Key, StackKey.Of(in stack), subject.EffMax, def, subject.Category))
            {
                continue;
            }

            // Unreachable: targets are distinct containers (IsWellFormed), each list is walked once and a stack is in one container's
            // list only. A repeat would overfill the stack, since a planned stack keeps its pre-plan count here; Release skips the
            // stack (no second merge planned).
            InventoryAssert.That(!result.HasMergeInto(s), SimAssertCode.InventoryAutoPlaceStackPlannedTwice);
            if (result.HasMergeInto(s))
            {
                continue;
            }

            ushort reason = AddMerge(ref plan, ref result, ref pass, containerSlot, s, mail);
            if (reason != Pass)
            {
                return reason;
            }
        }

        return Pass;
    }

    /// <summary>Plans one merge of <c>min(remaining, effMax − count)</c> units into <paramref name="stackSlot"/>.</summary>
    private ushort AddMerge(ref InvPlan plan, ref AutoPlacePlan result, ref PassState pass, ushort containerSlot, ushort stackSlot, bool mail)
    {
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

        int count = _entries[stackSlot].Count;
        int take = Math.Min(pass.Remaining, pass.Subject.EffMax - count);
        result.AddMerge(stackSlot, (byte)take); // 1 <= take <= remaining <= 255
        if (mail)
        {
            _ = plan.AddAcquiredBlock(count + take); // the union's block: target count + moved units
        }

        pass.Remaining -= take;
        return Pass;
    }

    /// <summary>PASS U (or PASS R when <paramref name="rotated"/>) on one target: the first free anchor, one probe per cell examined.</summary>
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
        int area = fw * fh;
        for (int y = 0; y <= h.H - fh; y++)
        {
            for (int x = 0; x <= h.W - fw; x++)
            {
                // Every footprint row is tested (no early exit), so each anchor examines exactly fw x fh cells and counts that many.
                _counters.Add(CounterId.InventoryPlacementProbes, area);
                ulong shifted = mask << x;
                ulong blocked = 0UL;
                for (int r = 0; r < fh; r++)
                {
                    blocked |= _occupancy.RowBits(containerSlot, y + r) & shifted;
                }

                if (blocked == 0UL)
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

            _counters.Add(CounterId.InventoryPlacementProbes); // one slot is one cell
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
        public readonly ushort SkipStack;
        public int Remaining;
        public bool Merging;
        private readonly int _units;
        private readonly int _sourceCount;

        public PassState(in PlacementSubject subject, int units, ushort sourceSlot, ushort skipStack, int sourceCount, bool merging)
        {
            Subject = subject;
            SourceSlot = sourceSlot;
            SkipStack = skipStack;
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
