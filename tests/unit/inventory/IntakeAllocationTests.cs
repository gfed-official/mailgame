using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/012 (ADR-0009 Implementation Guidelines; ADR-0006 0 B per tick): <see cref="IInventoryApi.Spawn"/>,
/// <see cref="IInventoryApi.PeekOutfeed"/>, <see cref="IInventoryApi.TakeUnit"/> and <see cref="InventorySystem.ReturnToIntake"/>, and
/// with them the <c>OutfeedFifo</c> (append, head pops, compaction) and <see cref="InventoryMutator.TakeOldest"/>, allocate 0 B per call
/// after warm-up. One cycle, inside a real tick (the rig's <c>MailStep</c> hook, a delegate cached before the window): spawn one letter,
/// peek, take the oldest unit, return a detached 3-letter entry to the Intake, then remove it again (so it can be returned next cycle;
/// its tombstones drive the FIFO compaction). The Intake keeps 160 entries. The 0 B assertion runs in optimised builds only (TD-008
/// convention); its twin runs the same window in every build and asserts the results. The rig's event capture is off in the window.
/// Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class IntakeAllocationTests
{
    /// <summary>Cycles per measured window.</summary>
    private const int Cycles = 1_000;

    [Fact]
    [Trait("Id", "TR-inventory-010")]
    public void SpawnPeekTakeAndReturn_Over1000Cycles_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        Totals totals = default;

        // Act
        AllocAssert.Zero("Spawn + PeekOutfeed + TakeUnit + ReturnToIntake x1000 cycles", () => totals = w.Run(Cycles));

        // Assert
        Workload.AssertTotals(in totals, Cycles);
    }

    [Fact]
    [Trait("Id", "TR-inventory-010")]
    public void SpawnPeekTakeAndReturn_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
    {
        // Arrange: the same workload as the 0 B test (which is skipped in unoptimised builds), run without measuring
        var w = new Workload();

        // Act
        Totals first = w.Run(Cycles);
        Totals second = w.Run(Cycles);

        // Assert
        Workload.AssertTotals(in first, Cycles);
        Assert.Equal(first, second);
        Assert.Equal(160, w.IntakeEntries);
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(InventorySystem).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    private record struct Totals(int Spawned, int Peeked, int Taken, int InOrder, int Returned, int Removed);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private const int CyclesPerTick = 50;

        private readonly InvAutoPlaceRig _rig = new();
        private readonly InventorySystem _inventory;
        private readonly EventLogFacade _events;
        private readonly ContainerId _intake;
        private readonly ushort _intakeSlot;
        private readonly ushort _returning;
        private readonly DefIndex _letter;
        private readonly MailId[] _one = new MailId[1];
        private readonly InvPhaseAction _cycle;
        private uint _nextSpawn;
        private uint _expectedTake;
        private int _toRun;
        private Totals _totals;

        public Workload()
        {
            IntakeSpawnOutfeedTests.FillIntake(_rig, 160, 1);
            _inventory = _rig.Inventory;
            _events = _rig.Host.Services.Events;
            _intake = _inventory.IntakeContainer;
            _intakeSlot = _rig.Intake;
            _letter = _rig.KindIndex("letter");
            _returning = _rig.DetachedMail("letter", 9u, InvAutoPlaceRig.Ids(900_000_000, 3));
            _nextSpawn = 161;
            _expectedTake = 1;
            _cycle = Cycle;
        }

        public int IntakeEntries => _rig.Header(_intakeSlot).EntryCount;

        public static void AssertTotals(in Totals t, int cycles)
        {
            Assert.Equal(new Totals(cycles, cycles, cycles, cycles, cycles, cycles), t);
        }

        public Totals Run(int cycles)
        {
            _totals = default;
            _toRun = cycles;
            _rig.CaptureEvents = false;
            _rig.Hook.Action = _cycle;
            try
            {
                while (_toRun > 0)
                {
                    _rig.Host.World.Tick();
                }
            }
            finally
            {
                _rig.Hook.Action = null;
                _rig.CaptureEvents = true;
            }

            return _totals;
        }

        private void Cycle(in TickContext ctx)
        {
            for (int i = 0; i < CyclesPerTick && _toRun > 0; i++, _toRun--)
            {
                _one[0] = new MailId(_nextSpawn++);
                _totals.Spawned += _inventory.Spawn(_intake, _letter, 4u, 0, _one, MergePolicy.NoMerge).Placed;
                _totals.Peeked += _inventory.PeekOutfeed(_intake, Selector.Fifo, out UnitRef peeked) ? 1 : 0;
                bool took = _inventory.TakeUnit(_intake, Selector.Fifo, ActorId.System, out UnitRef taken);
                _totals.Taken += took ? 1 : 0;
                _totals.InOrder += took && taken.Mail.Value == _expectedTake && peeked.Mail.Value == taken.Mail.Value && peeked.Entry.Value == taken.Entry.Value ? 1 : 0;
                _expectedTake++;
                _totals.Returned += _inventory.ReturnToIntake(_returning, ActorId.System, InvCause.Expired) ? 1 : 0;
                _totals.Removed += Detach() ? 1 : 0;
            }
        }

        /// <summary>Takes the returned entry out of the Intake again (one call scope and bracket, like an API call).</summary>
        private bool Detach()
        {
            var plan = new InvPlan();
            _ = plan.Touch(_intakeSlot);
            var extra = new InvReplyExtra();
            InventoryMutator m = _inventory.Mutator;
            using (EventCallScope scope = _events.BeginCall())
            {
                m.Begin(ref plan, ActorId.System, InvCause.None);
                m.Remove(ref plan, _returning);
                m.End(ref plan, ref extra);
            }

            return plan.MutatedCount == 1;
        }
    }
}
