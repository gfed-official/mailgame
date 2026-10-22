using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/015 (ADR-0009 Implementation Guidelines; ADR-0006 0 B per tick): <see cref="IInventoryApi.ConsumeUnit"/>,
/// <see cref="IInventoryApi.DetachForThrow"/>, <see cref="IInventoryApi.TryInsert"/> and <see cref="IInventoryApi.CountItem"/> allocate
/// 0 B per call after warm-up, on their success and refusal paths. One cycle, inside a real tick (the rig's <c>MailStep</c> hook, a
/// delegate cached before the window): <c>TryInsert</c> a letter onto a resident stack of player A's inventory (the merge path) and
/// <c>ConsumeUnit</c> its oldest unit (the count stays 5); <c>TryInsert</c> a letter of another address into the empty hotbar (the
/// placement path), <c>DetachForThrow</c> it with a wrong count (<c>stale_entry</c>) then whole, and destroy the detached entry; the
/// refusals <c>TryInsert</c> into the Intake (<c>outfeed_only</c>), into a tool rack whose whitelist refuses letters and into a full
/// 1 x 1 grid (no room), and <c>DetachForThrow</c> of an inventory entry (<c>bad_request</c>); then <c>CountItem</c> over both panel
/// containers. The 0 B assertion runs in optimised builds only (TD-008 convention); its twin runs the same window in every build and
/// asserts the results. The rig's event capture is off in the window. Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class InventoryApiAllocationTests
{
    /// <summary>Cycles per measured window.</summary>
    private const int Cycles = 1_000;

    [Fact]
    [Trait("Id", "TR-inventory-010")]
    public void ConsumeDetachInsertAndCount_Over1000Cycles_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        Totals totals = default;

        // Act
        AllocAssert.Zero("TryInsert (merge, place, 3 refusals) + ConsumeUnit + DetachForThrow (ok, stale, bad) + CountItem x1000 cycles", () => totals = w.Run(Cycles));

        // Assert
        Workload.AssertTotals(in totals, Cycles);
    }

    [Fact]
    [Trait("Id", "TR-inventory-010")]
    public void ConsumeDetachInsertAndCount_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
    {
        // Arrange: the same workload as the 0 B test (which is skipped in unoptimised builds), run without measuring
        var w = new Workload();

        // Act
        Totals first = w.Run(Cycles);
        Totals second = w.Run(Cycles);

        // Assert
        Workload.AssertTotals(in first, Cycles);
        Assert.Equal(first, second);
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

    private record struct Totals(int Merged, int Consumed, int Placed, int Stale, int Detached, int Refused, int BadRequests, int Counted);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private const int CyclesPerTick = 50;
        private const uint Address4 = 4u;
        private const int Planks = 12;

        private readonly InvAutoPlaceRig _rig = new(content: InventoryApiTests.RefusingContent());
        private readonly InventorySystem _inventory;
        private readonly ushort _resident;
        private readonly ContainerId _inventoryA;
        private readonly ContainerId _hotbar;
        private readonly ContainerId _intake;
        private readonly ContainerId _rack;
        private readonly ContainerId _tiny;
        private readonly ushort _hotbarSlot;
        private readonly EntryId _panelEntry;
        private readonly PlayerSlot _a = PlayerSlot.FromValue(0);
        private readonly DefIndex _plank;
        private readonly DefIndex _letter;
        private readonly ContainerId[] _counted;
        private readonly InvPhaseAction _cycle;
        private uint _nextMail = 1_000_000;
        private int _toRun;
        private Totals _totals;

        public Workload()
        {
            _inventory = _rig.Inventory;
            _resident = _rig.PlaceMail(_rig.InventoryA, "letter", 0, 0, Address4, InvAutoPlaceRig.Ids(1, 5));
            ushort planks = _rig.PlaceItem(_rig.InventoryA, "plank", 1, 0, Planks);
            _panelEntry = _rig.Entries[planks].Id;
            ushort tiny = _rig.CreateGrid(1, 1);
            _ = _rig.PlaceMail(tiny, "letter", 0, 0, 5u, InvAutoPlaceRig.Ids(100, 20));
            _tiny = _rig.Header(tiny).Id;
            _rack = _rig.Header(_rig.CreateGrid(4, 2, InventoryApiTests.ToolRackDef)).Id;
            _inventoryA = _inventory.PlayerInventoryOf(_a);
            _hotbar = _inventory.HotbarOf(_a);
            _hotbarSlot = _rig.HotbarA;
            _intake = _inventory.IntakeContainer;
            _plank = _rig.ItemIndex("plank");
            _letter = _rig.KindIndex("letter");
            _counted = new[] { _inventoryA, _hotbar };
            _cycle = Cycle;
        }

        public int ResidentCount => _rig.Entries[_resident].Count;

        public static void AssertTotals(in Totals t, int cycles)
        {
            Assert.Equal(new Totals(cycles, cycles, cycles, cycles, cycles, 3 * cycles, cycles, cycles), t);
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
            DefIndex letter = _letter;
            for (int i = 0; i < CyclesPerTick && _toRun > 0; i++, _toRun--)
            {
                var merge = new UnitRef(new MailId(_nextMail++), default, letter, Address4, 0);
                _totals.Merged += _inventory.TryInsert(_inventoryA, in merge, ActorId.System) ? 1 : 0;
                _totals.Consumed += _inventory.ConsumeUnit(new EntryRef(_inventoryA, _rig.Entries[_resident].Id), ConsumeReason.Delivered, ActorId.System) ? 1 : 0;

                var place = new UnitRef(new MailId(_nextMail++), default, letter, 9u, 0);
                _totals.Placed += _inventory.TryInsert(_hotbar, in place, ActorId.System) ? 1 : 0;
                ushort thrown = _rig.EntryAt(_hotbarSlot, 1, 0);
                EntryId thrownId = _rig.Entries[thrown].Id;
                _totals.Stale += _inventory.DetachForThrow(_a, thrownId, 2).Reason == InvReason.StaleEntry ? 1 : 0;
                _totals.Detached += _inventory.DetachForThrow(_a, thrownId, 1).Reason == 0 ? 1 : 0;
                _inventory.Mutator.DestroyDetached(thrown);

                _totals.Refused += _inventory.TryInsert(_intake, in place, ActorId.System) ? 0 : 1;
                _totals.Refused += _inventory.TryInsert(_rack, in place, ActorId.System) ? 0 : 1;
                _totals.Refused += _inventory.TryInsert(_tiny, in place, ActorId.System) ? 0 : 1;
                _totals.BadRequests += _inventory.DetachForThrow(_a, _panelEntry, Planks).Reason == (InvReason)InvReasons.BadRequest ? 1 : 0;
                _totals.Counted += _inventory.CountItem(_counted, _plank) == Planks ? 1 : 0;
            }
        }
    }
}
