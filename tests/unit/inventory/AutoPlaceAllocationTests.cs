using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/009 (ADR-0009 section 5 and Performance Implications; ADR-0006 0 B per tick): the StackKey index (link, unlink,
/// head lookup, list walk), the negative-fit cache (read, record, clear) and AutoPlace planning (<see cref="AutoPlace.Plan"/> in both
/// orders and policies, merges, free-cell placement, <c>no_room</c>, <see cref="AutoPlace.RecordNoFits"/>) allocate 0 B per call after
/// warm-up. The world is arranged before the measured window; the window leaves the index and cache as it found them, so every
/// repetition does the same work. The 0 B assertion runs in optimised builds only (TD-008 convention); its counterpart runs the same
/// window in every build and asserts the results, so the workload itself is never untested. The mutator path (AutoPlace's
/// <c>Commit</c> of a merge plan through <see cref="InventoryMutator.MergeInto"/>, and a <see cref="InventoryMutator.Place"/> /
/// <see cref="InventoryMutator.Remove"/> cycle: LinkIfNonFull → EffMaxOf → <see cref="PlacementSubject.Of"/>) has its own window and
/// twin; it runs inside real ticks, with the rig's event capture switched off. Joins the non-parallel collection (see
/// <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class AutoPlaceAllocationTests
{
    private const int Calls = 10_000;

    /// <summary>Mutator-window cycles (4 ticks each).</summary>
    private const int MutatorCycles = 1_000;

    [Fact]
    [Trait("Id", "TR-inventory-007")]
    public void IndexCacheAndAutoPlacePlan_Over10000Calls_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        Totals totals = default;

        // Act
        AllocAssert.Zero("StackIndex + NegativeFitCache + AutoPlace.Plan x10000", () => totals = w.Run(Calls));

        // Assert
        w.AssertTotals(in totals, Calls);
    }

    [Fact]
    [Trait("Id", "TR-inventory-007")]
    public void IndexCacheAndAutoPlacePlan_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
    {
        // Arrange: the same workload as the 0 B test (which is skipped in unoptimised builds), run without measuring
        var w = new Workload();

        // Act
        Totals first = w.Run(Calls);
        Totals second = w.Run(Calls);

        // Assert
        w.AssertTotals(in first, Calls);
        Assert.Equal(first, second);
    }

    [Fact]
    [Trait("Id", "TR-inventory-007")]
    public void MergeCommitAndPlaceRemove_Over1000Cycles_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new MutatorWorkload();
        MutatorTotals totals = default;

        // Act
        AllocAssert.Zero("AutoPlace.Commit via MergeInto + Place/Remove x1000 cycles", () => totals = w.Run(MutatorCycles));

        // Assert
        w.AssertTotals(in totals, MutatorCycles);
    }

    [Fact]
    [Trait("Id", "TR-inventory-007")]
    public void MergeCommitAndPlaceRemove_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
    {
        // Arrange: the same workload as the 0 B test (which is skipped in unoptimised builds), run without measuring
        var w = new MutatorWorkload();

        // Act
        MutatorTotals first = w.Run(MutatorCycles);
        MutatorTotals second = w.Run(MutatorCycles);

        // Assert
        w.AssertTotals(in first, MutatorCycles);
        Assert.Equal(first, second);
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(AutoPlace).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    private record struct Totals(int Ok, int NoRoom, int Merges, int Placed, int Links, int CacheHits);

    private record struct MutatorTotals(int Ok, int Merges, int MergedUnits, int LinkedAfterPlace, int UnlinkedAfterRemove);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private readonly InvAutoPlaceRig _rig = new();
        private readonly ushort[] _targets;
        private readonly ushort[] _full;
        private readonly ushort[] _stacks;
        private readonly PlacementSubject _letter;
        private readonly PlacementSubject _large;
        private readonly PlacementSubject _plank;
        private readonly StackKey _letterKey;

        public Workload()
        {
            // Inventory: three letter stacks to #2 (non-full), a plank stack; a full 8 x 2 grid (Large never fits); the hotbar
            ushort full = _rig.CreateGrid(8, 2);
            AutoPlaceTests.FillWithMediums(_rig, full, (0, 0), (2, 0), (4, 0), (6, 0));
            _stacks = new[]
            {
                _rig.PlaceMail(_rig.InventoryA, "letter", 0, 0, 2u, InvAutoPlaceRig.Ids(1, 5)),
                _rig.PlaceMail(_rig.InventoryA, "letter", 3, 0, 2u, InvAutoPlaceRig.Ids(100, 18)),
                _rig.PlaceMail(_rig.InventoryA, "letter", 1, 1, 2u, InvAutoPlaceRig.Ids(200, 12)),
            };
            _ = _rig.PlaceItem(_rig.InventoryA, "plank", 5, 1, 7);
            _targets = new[] { _rig.HotbarA, _rig.InventoryA };
            _full = new[] { full };
            _letter = _rig.Mail("letter", 2u);
            _large = _rig.Mail("large", 2u);
            _plank = _rig.Item("plank");
            _letterKey = _letter.Key;
        }

        public Totals Run(int calls)
        {
            Totals t = default;
            AutoPlace autoPlace = _rig.Inventory.AutoPlace;
            NegativeFitCache cache = autoPlace.NoFitCache;
            StackIndex index = _rig.Index;
            ushort fullSlot = _full[0];
            for (int i = 0; i < calls; i++)
            {
                // Index: unlink and relink the middle stack (row-major insert), then walk the list
                index.Unlink(_stacks[1]);
                index.Link(_stacks[1]);
                for (ushort s = index.Head(_rig.InventoryA, _letterKey); s != StackIndex.None; s = index.Next(s))
                {
                    t.Links++;
                }

                // AutoPlace planning: merges (pass-first), a target-first free slot, a plank merge (no-merge -> free cells), no_room
                var plan = new InvPlan();
                ushort a = autoPlace.Plan(ref plan, in _letter, 30, AutoPlace.NoSource, _targets, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan pa);
                plan = new InvPlan();
                ushort b = autoPlace.Plan(ref plan, in _letter, 3, AutoPlace.NoSource, _targets, MergePolicy.Merge, PlaceOrder.TargetFirst, out AutoPlacePlan pb);
                plan = new InvPlan();
                ushort c = autoPlace.Plan(ref plan, in _plank, 4, AutoPlace.NoSource, _targets, MergePolicy.NoMerge, PlaceOrder.PassFirst, out AutoPlacePlan pc);
                plan = new InvPlan();
                ushort d = autoPlace.Plan(ref plan, in _large, 1, AutoPlace.NoSource, _full, MergePolicy.Merge, PlaceOrder.PassFirst, out AutoPlacePlan pd);

                // Cache: record the no-fit facts, read them back, clear (so the next repetition scans again)
                autoPlace.RecordNoFits(in pd);
                t.CacheHits += cache.IsNoFit(fullSlot, in _rig.Header(fullSlot), _large.W, _large.H, rotated: true) ? 1 : 0;
                cache.Clear(fullSlot);

                t.Ok += (a == 0 ? 1 : 0) + (b == 0 ? 1 : 0) + (c == 0 ? 1 : 0);
                t.NoRoom += d == (ushort)InvReason.NoRoom ? 1 : 0;
                t.Merges += pa.MergeCount + pb.MergeCount + pc.MergeCount;
                t.Placed += (pa.Placement.IsSet ? 1 : 0) + (pb.Placement.IsSet ? 1 : 0) + (pc.Placement.IsSet ? 1 : 0);
            }

            return t;
        }

        public void AssertTotals(in Totals t, int calls)
        {
            Assert.Equal(3 * calls, t.Ok);
            Assert.Equal(calls, t.NoRoom);
            Assert.Equal(3 * calls, t.Merges);      // pass-first: 15 + 2 + 8 of 30 into the three letter stacks
            Assert.Equal(3 * calls, t.Placed);      // pass-first remainder, target-first hotbar slot, the no-merge plank
            Assert.Equal(3 * calls, t.Links);
            Assert.Equal(calls, t.CacheHits);
        }
    }

    /// <summary>
    /// The mutator window: per cycle, AutoPlace 3 of stack A into stack B (a partial source, so merges only) and 3 of B back into A,
    /// each committed through <see cref="InventoryMutator.MergeInto"/>; then a plank placed and removed through the mutator. A cycle
    /// restores every count, id run and cell, so every repetition does the same work.
    /// </summary>
    private sealed class MutatorWorkload
    {
        private readonly InvAutoPlaceRig _rig = new();
        private readonly ushort _grid;
        private readonly ushort _a;
        private readonly ushort _b;
        private readonly ushort _plank;
        private readonly PlacementSubject _letter;
        private readonly InvPlanStep _touchGrid;
        private readonly InvCommitStep _place;
        private readonly InvCommitStep _remove;

        public MutatorWorkload()
        {
            // A fixture 8 x 4 grid: letters to #2 at (0,0) ids 1..10 and at (2,0) ids 100..109; a detached 5-plank
            _grid = _rig.CreateGrid(8, 4);
            _a = _rig.PlaceMail(_grid, "letter", 0, 0, 2u, InvAutoPlaceRig.Ids(1, 10));
            _b = _rig.PlaceMail(_grid, "letter", 2, 0, 2u, InvAutoPlaceRig.Ids(100, 10));
            _plank = _rig.DetachedItem("plank", 5);
            _letter = _rig.SubjectOf(_a);
            ushort grid = _grid;
            ushort plank = _plank;
            InventoryMutator mutator = _rig.Inventory.Mutator;
            _touchGrid = (ref InvPlan p) => p.Touch(grid) < 0 ? CoreReason.BadRequest : (ushort)0;
            _place = (ref InvPlan p) => mutator.Place(ref p, plank, grid, 5, 3, false);
            _remove = (ref InvPlan p) => mutator.Remove(ref p, plank);
        }

        public MutatorTotals Run(int cycles)
        {
            MutatorTotals t = default;
            InvFixtureActionHandler handler = _rig.Handler;
            StackIndex index = _rig.Index;
            _rig.CaptureEvents = false;
            try
            {
                for (int i = 0; i < cycles; i++)
                {
                    // AutoPlace commits: 3 of A into B, then 3 of B (its oldest: A's former 1..3) back into A
                    MergeTick(_a, ref t);
                    MergeTick(_b, ref t);

                    // Place / Remove through the mutator (the plank is non-full, so it is linked, then unlinked)
                    handler.ValidateStep = _touchGrid;
                    handler.CommitStep = _place;
                    _rig.TickOnce();
                    t.Ok += handler.LastReason == 0 ? 1 : 0;
                    t.LinkedAfterPlace += index.Contains(_plank) ? 1 : 0;
                    handler.CommitStep = _remove;
                    _rig.TickOnce();
                    t.Ok += handler.LastReason == 0 ? 1 : 0;
                    t.UnlinkedAfterRemove += index.Contains(_plank) ? 0 : 1;
                    handler.ValidateStep = null;
                    handler.CommitStep = null;
                }
            }
            finally
            {
                _rig.CaptureEvents = true;
                handler.ValidateStep = null;
                handler.CommitStep = null;
            }

            return t;
        }

        public void AssertTotals(in MutatorTotals t, int cycles)
        {
            Assert.Equal(4 * cycles, t.Ok);
            Assert.Equal(2 * cycles, t.Merges);
            Assert.Equal(6 * cycles, t.MergedUnits);
            Assert.Equal(cycles, t.LinkedAfterPlace);
            Assert.Equal(cycles, t.UnlinkedAfterRemove);

            // Every cycle restores the arranged state
            Assert.Equal(InvAutoPlaceRig.Ids(1, 10), _rig.IdsOf(_a));
            Assert.Equal(InvAutoPlaceRig.Ids(100, 10), _rig.IdsOf(_b));
            Assert.Equal(EntryRecord.NoContainer, _rig.Entries[_plank].ContainerSlot);
            Assert.True(_rig.Index.Contains(_a));
            Assert.True(_rig.Index.Contains(_b));
        }

        private void MergeTick(ushort source, ref MutatorTotals t)
        {
            InvFixtureActionHandler handler = _rig.Handler;
            handler.Subject = _letter;
            handler.Units = 3;
            handler.Source = source;
            handler.Targets[0] = _grid;
            handler.TargetCount = 1;
            handler.Policy = MergePolicy.Merge;
            handler.Order = PlaceOrder.PassFirst;
            _rig.TickOnce();
            t.Ok += handler.LastReason == 0 ? 1 : 0;
            t.Merges += handler.LastPlan.MergeCount;
            t.MergedUnits += handler.LastPlan.MergedUnits;
        }
    }
}
