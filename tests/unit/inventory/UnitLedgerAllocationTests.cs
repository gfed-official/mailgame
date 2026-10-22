using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Intake;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/017 AC "0 B, cost, determinism" (ADR-0009 section 6; ADR-0006 0 B per tick): every ledger crossing and the
/// tick-end <see cref="InventoryConservationCheck"/> allocate 0 B after warm-up. Each cycle, inside the rig's <c>MailStep</c> hook (a
/// delegate cached before the window): <c>Spawn</c> of 2 letters into the Intake (spawned, Placed), 2 <c>TakeUnit</c>s (Placed → Belt),
/// a merging <c>TryInsert</c> into A's inventory (Belt → Placed) and a <c>ConsumeUnit</c> of it (Placed → Consumed), a placing
/// <c>TryInsert</c> into A's hotbar, <c>DetachForThrow</c> of it (Placed → Flight), <c>CreateWorldItem</c> (Flight → World), the
/// WorldItem's removal (World → none) and <c>DestroyDetached</c> (→ Destroyed); every tick ends in the check. The 0 B assertion runs
/// in optimised builds only (TD-008 convention); its twin runs the same window in every build (in Debug with the
/// <c>INVENTORY_ASSERT</c> scan every tick) and asserts the results and a balanced ledger. Joins the non-parallel collection (see
/// <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class UnitLedgerAllocationTests
{
    /// <summary>Cycles per measured window.</summary>
    private const int Cycles = 600;

    [Fact]
    [Trait("Id", "TR-inventory-012")]
    public void EveryCrossingAndTheTickEndCheck_Over600Cycles_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        Totals totals = default;

        // Act
        AllocAssert.Zero("Spawn + TakeUnit x2 + TryInsert x2 + ConsumeUnit + DetachForThrow + CreateWorldItem + Remove + DestroyDetached + the tick-end check x600 cycles", () => totals = w.Run(Cycles));

        // Assert
        Assert.Equal(Workload.Expected(Cycles), totals);
    }

    [Fact]
    [Trait("Id", "TR-inventory-012")]
    public void EveryCrossingAndTheTickEndCheck_TheMeasuredWindow_GivesTheExpectedResults_AndABalancedLedger_InEveryBuild()
    {
        // Arrange: the same workload as the 0 B test (skipped in unoptimised builds), run without measuring
        var w = new Workload();
        UnitLedger ledger = w.Ledger;
        long consumed = ledger.Consumed(UnitLedger.MailRow, ConsumeReason.Delivered);
        long destroyed = ledger.Destroyed(UnitLedger.MailRow);

        // Act
        Totals first = w.Run(Cycles);
        Totals second = w.Run(Cycles);

        // Assert: every cycle crossed; the ledger balances after every tick (no mismatch counted, in Debug no scan finding)
        Assert.Equal(Workload.Expected(Cycles), first);
        Assert.Equal(first, second);
        Assert.Equal(0, w.Mismatches);
        Assert.Equal(ScanFindingKind.None, w.ScanFinding);
        Assert.Equal(ledger.Expected(UnitLedger.MailRow), ledger.LiveSum(UnitLedger.MailRow));
        Assert.Equal(2 * Cycles, ledger.Consumed(UnitLedger.MailRow, ConsumeReason.Delivered) - consumed);
        Assert.Equal(2 * Cycles, ledger.Destroyed(UnitLedger.MailRow) - destroyed);
        Assert.Equal(0, ledger.Live(UnitLedger.MailRow, LedgerState.Belt));
        Assert.Equal(0, ledger.Live(UnitLedger.MailRow, LedgerState.Flight));
        Assert.Equal(0, ledger.Live(UnitLedger.MailRow, LedgerState.World));
        Assert.Equal(5, w.ResidentCount);
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(InventorySystem).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    private record struct Totals(int Spawned, int Taken, int Merged, int Consumed, int Placed, int Detached, int Created, int Removed);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private const int CyclesPerTick = 10;
        private const uint Address4 = 4u;

        private static readonly Vec3Mm _pos = new(1_000, 0, 1_000);

        private readonly InvAutoPlaceRig _rig = new();
        private readonly InventorySystem _inventory;
        private readonly WorldItems _world;
        private readonly ushort _resident;
        private readonly ContainerId _inventoryA;
        private readonly ContainerId _hotbar;
        private readonly ContainerId _intake;
        private readonly ushort _hotbarSlot;
        private readonly PlayerSlot _a = PlayerSlot.FromValue(0);
        private readonly DefIndex _letter;
        private readonly MailId[] _spawn = new MailId[2];
        private readonly InvPhaseAction _cycle;
        private uint _nextMail = 1_000_000;
        private int _toRun;
        private Totals _totals;

        public Workload()
        {
            _inventory = _rig.Inventory;
            _world = _inventory.WorldItems;
            _resident = _rig.PlaceMail(_rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(1, 5));
            _inventoryA = _inventory.PlayerInventoryOf(_a);
            _hotbar = _inventory.HotbarOf(_a);
            _hotbarSlot = _rig.HotbarA;
            _intake = _inventory.IntakeContainer;
            _letter = _rig.KindIndex("letter");
            _cycle = Cycle;
        }

        public UnitLedger Ledger => _inventory.Ledger;

        public int ResidentCount => _rig.Entries[_resident].Count;

        public long Mismatches => _rig.Counters[CounterId.InventoryConservationMismatches];

        public ScanFindingKind ScanFinding => _inventory.ConservationCheck.ScanFinding.Kind;

        public static Totals Expected(int cycles) => new(2 * cycles, 2 * cycles, cycles, cycles, cycles, cycles, cycles, cycles);

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
                _spawn[0] = new MailId(_nextMail++);
                _spawn[1] = new MailId(_nextMail++);
                _totals.Spawned += _inventory.Spawn(_intake, _letter, Address4, 0, _spawn, MergePolicy.NoMerge).Placed;
                bool first = _inventory.TakeUnit(_intake, Selector.Fifo, ActorId.System, out UnitRef merge);
                bool second = _inventory.TakeUnit(_intake, Selector.Fifo, ActorId.System, out UnitRef place);
                _totals.Taken += (first ? 1 : 0) + (second ? 1 : 0);

                _totals.Merged += first && _inventory.TryInsert(_inventoryA, in merge, ActorId.System) ? 1 : 0;
                _totals.Consumed += _inventory.ConsumeUnit(new EntryRef(_inventoryA, _rig.Entries[_resident].Id), ConsumeReason.Delivered, ActorId.System) ? 1 : 0;

                // The empty hotbar has no matching stack, so the placing TryInsert allocated the newest entry id (as InventoryApiAllocationTests)
                bool placed = second && _inventory.TryInsert(_hotbar, in place, ActorId.System);
                _totals.Placed += placed ? 1 : 0;
                EntryId thrownId = EntryId.Real(_rig.Entries.LastId);
                if (!placed || !_rig.Entries.TryGetSlot(thrownId, out ushort thrown) || _rig.Entries[thrown].ContainerSlot != _hotbarSlot)
                {
                    continue;
                }

                _totals.Detached += _inventory.DetachForThrow(_a, thrownId, 1).Reason == 0 ? 1 : 0;
                EntityId item = _inventory.CreateWorldItem(thrownId, _pos, WorldItemOrigin.Thrown, false, ctx.Tick);
                _totals.Created += item.IsNone ? 0 : 1;
                if (_world.Remove(item, ActorId.System, InvCause.Taken, out ushort landed))
                {
                    _totals.Removed++;
                    _inventory.Mutator.DestroyDetached(landed);
                }
            }
        }
    }
}
