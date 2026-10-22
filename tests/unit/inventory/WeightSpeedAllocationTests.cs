using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/016 (ADR-0006 0 B per tick; TR-inventory-015): <see cref="IInventoryApi.WeightPoints"/>,
/// <see cref="IInventoryApi.SpeedPermille"/> (the stat-sheet read of <c>WeightPenaltyPm</c> and D1's one <c>Fix.MulDiv</c>) and a
/// <see cref="InventoryMutator.Place"/> / <see cref="InventoryMutator.Remove"/> cycle (the weight hooks) allocate 0 B after warm-up.
/// Player A holds a letter in the hotbar and a Medium in the inventory (P = 4) and a synthetic Strong Back (<c>WeightPenaltyPm</c>
/// Mul 500). Per cycle: a detached Large is placed into hotbar slot 1 in one real tick (P = 12, 940‰) and removed in the next
/// (P = 4, 980‰), each followed by reads for slot A, an unjoined slot and the none slot. The fixture op's validate and commit steps are
/// delegates cached before the window, and the rig's event capture is off in it. The 0 B assertion runs in optimised builds only
/// (TD-008 convention); its twin runs the same window in every build and asserts the results. Joins the non-parallel collection (see
/// <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class WeightSpeedAllocationTests
{
    /// <summary>Cycles per measured window (2 ticks each).</summary>
    private const int Cycles = 1_000;

    [Fact]
    [Trait("Id", "TR-inventory-015")]
    public void WeightPointsSpeedPermilleAndAPlaceRemoveCycle_Over1000Cycles_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        Totals totals = default;

        // Act
        AllocAssert.Zero("Place + Remove (weight hooks) + WeightPoints x3 + SpeedPermille x3 per tick x1000 cycles", () => totals = w.Run(Cycles));

        // Assert
        Workload.AssertTotals(in totals, Cycles);
    }

    [Fact]
    [Trait("Id", "TR-inventory-015")]
    public void WeightPointsSpeedPermilleAndAPlaceRemoveCycle_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
    {
        // Arrange: the same workload as the 0 B test (which is skipped in unoptimised builds), run without measuring
        var w = new Workload();

        // Act
        Totals first = w.Run(Cycles);
        Totals second = w.Run(Cycles);

        // Assert
        Workload.AssertTotals(in first, Cycles);
        Assert.Equal(first, second);
        Assert.Equal(EntryRecord.NoContainer, w.LargeContainer);
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(InventorySystem).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    /// <summary>Per-cycle tallies: committed ticks, and reads that gave the expected points and speeds.</summary>
    private record struct Totals(int Ok, int Placed12, int Placed940, int Removed4, int Removed980, int OtherSlotReads);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private const uint Address4 = 4u;

        private readonly InvAutoPlaceRig _rig = new();
        private readonly InventorySystem _inventory;
        private readonly ushort _large;
        private readonly PlayerSlot _a = PlayerSlot.FromValue(0);
        private readonly PlayerSlot _unjoined = PlayerSlot.FromValue(5);
        private readonly InvPlanStep _touchHotbar;
        private readonly InvCommitStep _place;
        private readonly InvCommitStep _remove;

        public Workload()
        {
            _inventory = _rig.Inventory;
            _ = _rig.PlaceMail(_rig.HotbarA, "letter", 2, 0, Address4, [1]);
            _ = _rig.PlaceMail(_rig.InventoryA, "medium", 0, 0, Address4, [2]);
            Assert.True(WeightSpeedTests.PushStrongBack(_rig.Host, _a, 500));
            _large = _rig.DetachedMail("large", Address4, [3]);
            ushort hotbar = _rig.HotbarA;
            ushort large = _large;
            InventoryMutator mutator = _inventory.Mutator;
            _touchHotbar = (ref InvPlan p) => p.Touch(hotbar) < 0 ? CoreReason.BadRequest : (ushort)0;
            _place = (ref InvPlan p) => mutator.Place(ref p, large, hotbar, 1, 0, false);
            _remove = (ref InvPlan p) => mutator.Remove(ref p, large);
        }

        public ushort LargeContainer => _rig.Entries[_large].ContainerSlot;

        public static void AssertTotals(in Totals t, int cycles)
        {
            Assert.Equal(new Totals(2 * cycles, cycles, cycles, cycles, cycles, 2 * cycles), t);
        }

        public Totals Run(int cycles)
        {
            Totals t = default;
            InvFixtureActionHandler handler = _rig.Handler;
            _rig.CaptureEvents = false;
            try
            {
                for (int i = 0; i < cycles; i++)
                {
                    handler.ValidateStep = _touchHotbar;
                    handler.CommitStep = _place;
                    _rig.TickOnce();
                    t.Ok += handler.LastReason == 0 ? 1 : 0;
                    t.Placed12 += _inventory.WeightPoints(_a) == 12 ? 1 : 0;
                    t.Placed940 += _inventory.SpeedPermille(_a) == 940 ? 1 : 0;
                    t.OtherSlotReads += Unloaded() ? 1 : 0;

                    handler.CommitStep = _remove;
                    _rig.TickOnce();
                    t.Ok += handler.LastReason == 0 ? 1 : 0;
                    t.Removed4 += _inventory.WeightPoints(_a) == 4 ? 1 : 0;
                    t.Removed980 += _inventory.SpeedPermille(_a) == 980 ? 1 : 0;
                    t.OtherSlotReads += Unloaded() ? 1 : 0;
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

        /// <summary>An unjoined slot and the none slot read P 0 and 1000‰.</summary>
        private bool Unloaded() =>
            _inventory.WeightPoints(_unjoined) == 0 && _inventory.SpeedPermille(_unjoined) == 1000
            && _inventory.WeightPoints(PlayerSlot.None) == 0 && _inventory.SpeedPermille(PlayerSlot.None) == 1000;
    }
}
