using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/007 AC 11 (ADR-0009 Implementation Guidelines; ADR-0006 0 B per tick): the production <c>Pick</c> and
/// <c>Move</c> ops (<see cref="PickHandler"/>, <see cref="MoveHandler"/>) allocate 0 B per op after warm-up, through the real phase-0
/// drain. One cycle is eleven ticks, one op each, all by player B (slot 1, so a hard-coded slot 0 cannot pass) with an open chest: pick
/// the chest's Medium onto the empty cursor; a <c>cursor_busy</c> pick; place the Medium from the cursor into the inventory
/// (cross-container); move it inside the inventory; move it onto its own cells (the no-op); an <c>out_of_bounds</c> move to the chest;
/// move it back to its chest cell (cross-container); then two partial-fit implicit merges of planks and two of letters (the mail
/// merge's union block goes through <see cref="InvRequestCheck.CheckArenaReservation(in InvPlan)"/>), chest → inventory and back, so
/// the stacks alternate 5 / 20 and 20 / 5 and end the cycle as they began. Every op's outcome is checked from the state between ticks
/// (no reply capture). The 0 B assertion runs in optimised builds only (TD-008 convention); its twin runs the same window in every
/// build and asserts the results. The rig's event capture is off in the window. Joins the non-parallel collection (see
/// <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class PickMoveAllocationTests
{
    /// <summary>Cycles per measured window.</summary>
    private const int Cycles = 500;

    [Fact]
    [Trait("Id", "TR-inventory-001")]
    public void PickMoveNoOpAndRejects_Over500Cycles_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        Totals totals = default;

        // Act
        AllocAssert.Zero("2 Pick (1 cursor_busy) + 9 Move (1 no-op, 1 out_of_bounds, 4 partial merges) x500 cycles", () => totals = w.Run(Cycles));

        // Assert
        Workload.AssertTotals(in totals, Cycles);
    }

    [Fact]
    [Trait("Id", "TR-inventory-001")]
    public void PickMoveNoOpAndRejects_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
    {
        // Arrange: the same workload as the 0 B test (which is skipped in unoptimised builds), run without measuring
        var w = new Workload();

        // Act
        Totals first = w.Run(Cycles);
        Totals second = w.Run(Cycles);

        // Assert
        Workload.AssertTotals(in first, Cycles);
        Assert.Equal(first, second);
        Assert.True(w.AtCycleStart);
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(InventorySystem).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    private record struct Totals(
        int Picked, int BusyRejected, int Placed, int MovedInside, int NoOps, int BoundsRejected, int Returned, int ItemMerges, int MailMerges, int CycleRestored);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private const uint Address3 = 3u;
        private const int OpsPerCycle = 11;

        private static readonly PlayerSlot _slotB = PlayerSlot.FromValue(1);

        private readonly InvAutoPlaceRig _rig = new(joinPlayerB: true);
        private readonly ActorId _playerB;
        private readonly ushort _chest;
        private readonly ushort _inventory;
        private readonly ushort _cursor;
        private readonly ushort _medium;
        private readonly ushort _invPlanks;
        private readonly ushort _chestPlanks;
        private readonly ushort _invLetters;
        private readonly ushort _chestLetters;
        private readonly short _points;
        private readonly Op[] _ops = new Op[OpsPerCycle];
        private Totals _totals;

        public Workload()
        {
            _playerB = _rig.PlayerB;
            _chest = _rig.CreateGrid(8, 4);
            Assert.True(_rig.Inventory.TryOpenExternal(_slotB, _rig.IdOf(_chest)));
            _inventory = _rig.InventoryOf(1);
            _cursor = _rig.CursorOf(1);
            _medium = _rig.PlaceMail(_chest, "medium", 6, 2, 99u, InvAutoPlaceRig.Ids(500, 1));
            _chestPlanks = _rig.PlaceItem(_chest, "plank", 0, 0, 20);
            _chestLetters = _rig.PlaceMail(_chest, "letter", 1, 0, Address3, InvAutoPlaceRig.Ids(100, 20));
            _invPlanks = _rig.PlaceItem(_inventory, "plank", 0, 0, 5);
            _invLetters = _rig.PlaceMail(_inventory, "letter", 1, 0, Address3, InvAutoPlaceRig.Ids(200, 5));
            _points = _rig.Inventory.WeightPoints(_slotB);
            _ops[0] = Pick(_chest, _medium);
            _ops[1] = Pick(_chest, _chestPlanks);                 // cursor_busy: the Medium is on the cursor
            _ops[2] = Move(_cursor, _medium, _inventory, 4, 0);
            _ops[3] = Move(_inventory, _medium, _inventory, 6, 0);
            _ops[4] = Move(_inventory, _medium, _inventory, 6, 0); // the no-op
            _ops[5] = Move(_inventory, _medium, _chest, 7, 3);     // out_of_bounds: a 2 x 2 at (7,3) of an 8 x 4
            _ops[6] = Move(_inventory, _medium, _chest, 6, 2);
            _ops[7] = Move(_chest, _chestPlanks, _inventory, 0, 0); // 20 onto 5: 15 move, 5 stay
            _ops[8] = Move(_inventory, _invPlanks, _chest, 0, 0);   // 20 onto 5: 15 move back
            _ops[9] = Move(_chest, _chestLetters, _inventory, 1, 0);
            _ops[10] = Move(_inventory, _invLetters, _chest, 1, 0);
        }

        /// <summary>The cycle's start state: the Medium at chest (6,2), the cursor empty, planks and letters 5 in the inventory and 20 in the chest, P as arranged.</summary>
        public bool AtCycleStart =>
            At(_medium, _chest, 6, 2) && _rig.Header(_cursor).EntryCount == 0
            && Units(_invPlanks, _inventory) == 5 && Units(_chestPlanks, _chest) == 20
            && Units(_invLetters, _inventory) == 5 && Units(_chestLetters, _chest) == 20
            && _rig.Inventory.WeightPoints(_slotB) == _points;

        public static void AssertTotals(in Totals t, int cycles)
        {
            Assert.Equal(new Totals(cycles, cycles, cycles, cycles, cycles, cycles, cycles, 2 * cycles, 2 * cycles, cycles), t);
        }

        public Totals Run(int cycles)
        {
            _totals = default;
            _rig.CaptureEvents = false;
            try
            {
                for (int c = 0; c < cycles; c++)
                {
                    for (int i = 0; i < OpsPerCycle; i++)
                    {
                        uint inventoryV = _rig.Header(_inventory).Version;
                        uint chestV = _rig.Header(_chest).Version;
                        uint cursorV = _rig.Header(_cursor).Version;
                        _ = _rig.Host.World.EnqueueOp(_playerB, in _ops[i]);
                        _rig.Host.World.Tick();
                        bool unchanged = _rig.Header(_inventory).Version == inventoryV && _rig.Header(_chest).Version == chestV
                            && _rig.Header(_cursor).Version == cursorV;
                        Tally(i, unchanged);
                    }

                    _totals.CycleRestored += AtCycleStart ? 1 : 0;
                }
            }
            finally
            {
                _rig.CaptureEvents = true;
            }

            return _totals;
        }

        private void Tally(int op, bool unchanged)
        {
            switch (op)
            {
                case 0:
                    _totals.Picked += At(_medium, _cursor, 0, 0) && !unchanged ? 1 : 0;
                    break;
                case 1:
                    _totals.BusyRejected += At(_medium, _cursor, 0, 0) && At(_chestPlanks, _chest, 0, 0) && unchanged ? 1 : 0;
                    break;
                case 2:
                    _totals.Placed += At(_medium, _inventory, 4, 0) && _rig.Header(_cursor).EntryCount == 0 && !unchanged ? 1 : 0;
                    break;
                case 3:
                    _totals.MovedInside += At(_medium, _inventory, 6, 0) && !unchanged ? 1 : 0;
                    break;
                case 4:
                    _totals.NoOps += At(_medium, _inventory, 6, 0) && unchanged ? 1 : 0;
                    break;
                case 5:
                    _totals.BoundsRejected += At(_medium, _inventory, 6, 0) && unchanged ? 1 : 0;
                    break;
                case 6:
                    _totals.Returned += At(_medium, _chest, 6, 2) && !unchanged ? 1 : 0;
                    break;
                case 7:
                    _totals.ItemMerges += Units(_invPlanks, _inventory) == 20 && Units(_chestPlanks, _chest) == 5 && !unchanged ? 1 : 0;
                    break;
                case 8:
                    _totals.ItemMerges += Units(_invPlanks, _inventory) == 5 && Units(_chestPlanks, _chest) == 20 && !unchanged ? 1 : 0;
                    break;
                case 9:
                    _totals.MailMerges += Units(_invLetters, _inventory) == 20 && Units(_chestLetters, _chest) == 5 && !unchanged ? 1 : 0;
                    break;
                default:
                    _totals.MailMerges += Units(_invLetters, _inventory) == 5 && Units(_chestLetters, _chest) == 20 && !unchanged ? 1 : 0;
                    break;
            }
        }

        private bool At(ushort entry, ushort container, int x, int y)
        {
            EntryRecord e = _rig.Entries[entry];
            return e.IsLive && e.ContainerSlot == container && e.X == x && e.Y == y;
        }

        private int Units(ushort entry, ushort container)
        {
            EntryRecord e = _rig.Entries[entry];
            return e.IsLive && e.ContainerSlot == container ? e.Count : -1;
        }

        private Op Pick(ushort from, ushort entry)
        {
            var op = new Op { Kind = InvOpKinds.Pick, ReqId = 81 };
            op.SetPayload(new PickPayload { From = _rig.IdOf(from).Value, EntryId = _rig.Entries[entry].Id.Value });
            return op;
        }

        private Op Move(ushort from, ushort entry, ushort to, short x, short y)
        {
            var op = new Op { Kind = InvOpKinds.Move, ReqId = 82 };
            op.SetPayload(new MovePayload { From = _rig.IdOf(from).Value, EntryId = _rig.Entries[entry].Id.Value, To = _rig.IdOf(to).Value, X = x, Y = y });
            return op;
        }
    }
}
