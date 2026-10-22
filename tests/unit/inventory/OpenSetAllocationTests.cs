using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/011 AC 7 (ADR-0009 Implementation Guidelines; ADR-0006 0 B per tick): <see cref="InventorySystem.TryOpenExternal"/>,
/// <see cref="InventorySystem.CloseExternal"/> with <see cref="CloseCause.Leash"/> on an empty cursor and with a cursor entry (the put-away:
/// <c>TargetOrder</c>, <c>AutoPlace.Plan</c>, the reservation and one bracket), and the production <c>Close</c> op allocate 0 B per call
/// after warm-up. One cycle per tick: the <c>Close</c> op (enqueued before the tick) closes player A's Intake external in
/// <c>Inputs.Apply</c>; then inside <c>MailStep</c> (the rig's hook, a delegate cached before the window) A re-opens it, a leash closes it
/// with the cursor empty, A re-opens it, the axe is moved from hotbar slot 1 back onto the cursor (one bracket, test code), a leash closes
/// it and puts the axe away into hotbar slot 1, and A re-opens it for the next tick's op. The 0 B assertion runs in optimised builds only
/// (TD-008 convention); its twin runs the same window in every build and asserts the results. The rig's event capture is off in the
/// window. Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class OpenSetAllocationTests
{
    /// <summary>Cycles (ticks) per measured window.</summary>
    private const int Cycles = 1_000;

    [Fact]
    [Trait("Id", "TR-inventory-001")]
    public void TryOpenExternalCloseExternalAndTheCloseOp_Over1000Cycles_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        Totals totals = default;

        // Act
        AllocAssert.Zero("Close op + 3 TryOpenExternal + CloseExternal(Leash) empty cursor + CloseExternal(Leash) with put-away x1000 cycles", () => totals = w.Run(Cycles));

        // Assert
        Workload.AssertTotals(in totals, Cycles);
    }

    [Fact]
    [Trait("Id", "TR-inventory-001")]
    public void TryOpenExternalCloseExternalAndTheCloseOp_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
    {
        // Arrange: the same workload as the 0 B test (which is skipped in unoptimised builds), run without measuring
        var w = new Workload();

        // Act
        Totals first = w.Run(Cycles);
        Totals second = w.Run(Cycles);

        // Assert
        Workload.AssertTotals(in first, Cycles);
        Assert.Equal(first, second);
        Assert.True(w.AxeInHotbarSlot1);
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(InventorySystem).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    private record struct Totals(int ClosedByOp, int Opened, int LeashedEmpty, int Picked, int PutAway);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private static readonly PlayerSlot _slotA = PlayerSlot.FromValue(0);

        private readonly InvAutoPlaceRig _rig = new();
        private readonly InventorySystem _inventory;
        private readonly EventLogFacade _events;
        private readonly ContainerId _intake;
        private readonly ushort _hotbar;
        private readonly ushort _cursor;
        private readonly ushort _axe;
        private readonly ActorId _playerA;
        private readonly Op _close;
        private readonly InvPhaseAction _cycle;
        private Totals _totals;

        public Workload()
        {
            _inventory = _rig.Inventory;
            _events = _rig.Host.Services.Events;
            _intake = _inventory.IntakeContainer;
            _hotbar = _rig.HotbarA;
            _cursor = _rig.SlotOf(_inventory.CursorOf(_slotA));
            _playerA = _rig.PlayerA;
            _axe = _rig.PlaceItem(_hotbar, "axe", 1, 0, 1);
            _close = new Op { Kind = InvOpKinds.Close, ReqId = 77 };
            _close.SetPayload(new ClosePayload { Container = _intake.Value });
            Assert.True(_inventory.TryOpenExternal(_slotA, _intake));
            _cycle = Cycle;
        }

        public bool AxeInHotbarSlot1 => _rig.Entries[_axe].ContainerSlot == _hotbar && _rig.Entries[_axe].X == 1;

        public static void AssertTotals(in Totals t, int cycles)
        {
            Assert.Equal(new Totals(cycles, 3 * cycles, cycles, cycles, cycles), t);
        }

        public Totals Run(int cycles)
        {
            _totals = default;
            _rig.CaptureEvents = false;
            _rig.Hook.Action = _cycle;
            try
            {
                for (int i = 0; i < cycles; i++)
                {
                    _ = _rig.Host.World.EnqueueOp(_playerA, in _close);
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
            _totals.ClosedByOp += _inventory.OpenSet.ExternalOf(_slotA).IsNone ? 1 : 0;
            _totals.Opened += _inventory.TryOpenExternal(_slotA, _intake) ? 1 : 0;
            _inventory.CloseExternal(_slotA, CloseCause.Leash);
            _totals.LeashedEmpty += _inventory.OpenSet.ExternalOf(_slotA).IsNone && AxeInHotbarSlot1 ? 1 : 0;
            _totals.Opened += _inventory.TryOpenExternal(_slotA, _intake) ? 1 : 0;
            _totals.Picked += PickAxe() ? 1 : 0;
            _inventory.CloseExternal(_slotA, CloseCause.Leash);
            _totals.PutAway += _inventory.OpenSet.ExternalOf(_slotA).IsNone && AxeInHotbarSlot1 ? 1 : 0;
            _totals.Opened += _inventory.TryOpenExternal(_slotA, _intake) ? 1 : 0;
        }

        /// <summary>Moves the axe from the hotbar onto the cursor (one call scope and bracket, like story 007's <c>pick</c>).</summary>
        private bool PickAxe()
        {
            var plan = new InvPlan();
            _ = plan.Touch(_hotbar);
            _ = plan.Touch(_cursor);
            var extra = new InvReplyExtra();
            InventoryMutator m = _inventory.Mutator;
            using (EventCallScope scope = _events.BeginCall())
            {
                m.Begin(ref plan, _playerA, InvCause.None);
                m.Transfer(ref plan, _axe, _cursor, 0, 0, false);
                m.End(ref plan, ref extra);
            }

            return _rig.Entries[_axe].ContainerSlot == _cursor;
        }
    }
}
