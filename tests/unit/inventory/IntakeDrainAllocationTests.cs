using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/021 (ADR-0009 Implementation Guidelines; ADR-0006 0 B per tick): <see cref="IInventoryApi.AppendBacklog"/> and
/// the <c>IntakeDrain</c> pass allocate 0 B after warm-up. The Intake starts full; each tick, inside the rig's <c>MailStep</c> hook (a
/// delegate cached before the window), four letters are appended to the backlog and four units are taken from the Intake, so the next
/// tick's phase 2 pass (its Intake version changed) drains four entries into the freed cells: 1,200 appends and 300 non-idle passes per
/// window. The 0 B assertion runs in optimised builds only (TD-008 convention); its twin runs the same window in every build and
/// asserts the results. Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class IntakeDrainAllocationTests
{
    private const int Ticks = 300;
    private const int PerTick = 4;

    [Fact]
    [Trait("Id", "TR-inventory-011")]
    public void AppendBacklogAndDrainPasses_Over300Ticks_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        _ = w.Run(Ticks); // warm-up
        Totals totals = default;

        // Act
        AllocAssert.Zero("AppendBacklog x4 + TakeUnit x4 + IntakeDrain pass x300 ticks", () => totals = w.Run(Ticks));

        // Assert
        Workload.AssertSteadyState(in totals);
    }

    [Fact]
    [Trait("Id", "TR-inventory-011")]
    public void AppendBacklogAndDrainPasses_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
    {
        // Arrange: the same workload as the 0 B test, run without measuring
        var w = new Workload();

        // Act
        Totals first = w.Run(Ticks);
        Totals second = w.Run(Ticks);
        Totals third = w.Run(Ticks);

        // Assert: the first window's first pass had nothing queued yet; then 4 per pass and one seal per pass; a steady backlog of 4
        Assert.Equal(PerTick * Ticks, first.Appended);
        Assert.Equal(PerTick * (Ticks - 1), first.Drained);
        Workload.AssertSteadyState(in second);
        Assert.Equal(second, third);
        Assert.Equal(PerTick, w.BacklogEntries);
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(InventorySystem).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    private record struct Totals(int Appended, int Taken, int Drained, uint Seals);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private readonly InvAutoPlaceRig _rig = new();
        private readonly InventorySystem _inventory;
        private readonly ContainerId _intake;
        private readonly DefIndex _letter;
        private readonly MailId[] _one = new MailId[1];
        private readonly InvPhaseAction _step;
        private uint _nextMail = 100_000;
        private int _toRun;
        private Totals _totals;

        public Workload()
        {
            IntakeSpawnOutfeedTests.FillIntake(_rig, 320, 1);
            _inventory = _rig.Inventory;
            _intake = _inventory.IntakeContainer;
            _letter = _rig.KindIndex("letter");
            _step = Step;
        }

        public int BacklogEntries => _inventory.BacklogEntries;

        public static void AssertSteadyState(in Totals t)
        {
            Assert.Equal(new Totals(PerTick * Ticks, PerTick * Ticks, PerTick * Ticks, (uint)((PerTick + 1) * Ticks)), t);
        }

        public Totals Run(int ticks)
        {
            _totals = default;
            _toRun = ticks;
            _rig.CaptureEvents = false;
            _rig.Hook.Action = _step;
            uint arrivalsBefore = _rig.Header(_rig.Intake).NextArrivalSeq;
            uint versionBefore = _rig.Header(_rig.Intake).Version;
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
            }

            _totals.Drained = (int)(_rig.Header(_rig.Intake).NextArrivalSeq - arrivalsBefore); // every Intake insertion is a drained entry
            _totals.Seals = _rig.Header(_rig.Intake).Version - versionBefore; // one per TakeUnit and one per non-idle drain pass
            return _totals;
        }

        private void Step(in TickContext ctx)
        {
            _toRun--;
            for (int i = 0; i < PerTick; i++)
            {
                _one[0] = new MailId(_nextMail++);
                _totals.Appended += _inventory.AppendBacklog(new BacklogEntry(_letter, 4u, 0, _one)) ? 1 : 0;
            }

            for (int i = 0; i < PerTick; i++)
            {
                _totals.Taken += _inventory.TakeUnit(_intake, Selector.Fifo, ActorId.System, out _) ? 1 : 0;
            }
        }
    }
}
