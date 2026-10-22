using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/008 AC "0 B" (ADR-0009 Implementation Guidelines; ADR-0006 0 B per tick): the <c>Move</c> split-and-place and
/// swap arms (<see cref="MoveHandler"/>, <see cref="InventoryMutator.SplitAndPlace"/>) and the <c>Merge</c> op
/// (<see cref="MergeHandler"/>) allocate 0 B per op after warm-up, through the real phase-0 drain. One cycle is eight ticks, one op
/// each, all by player B (slot 1, so a hard-coded slot 0 cannot pass) with an open chest: split 3 of the inventory's 10 letters
/// [1..10] to a free chest cell (a new entry, a k-unit arena block through
/// <see cref="InvRequestCheck.CheckArenaReservation(in InvPlan)"/>); merge that piece back onto the letters (the sorted union, the
/// piece removed); swap the cursor's planks with the Axe in hotbar slot 1, and swap back; a <c>blocked</c> merge of the letters onto
/// themselves; a <c>bad_count</c> split (count above the total); split 4 of the chest's 10 planks into the inventory; merge them back.
/// The two merges name the piece the split just made, by the pool's high-water, so their payloads are rewritten in the window (no
/// allocation). The cycle ends as it began. Every op's outcome is checked from the state between ticks (no reply capture). The 0 B
/// assertion runs in optimised builds only (TD-008 convention); its twin runs the same window in every build and asserts the results.
/// The rig's event capture is off in the window. Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class MoveSplitMergeAllocationTests
{
    /// <summary>Cycles per measured window.</summary>
    private const int Cycles = 500;

    [Fact]
    [Trait("Id", "TR-inventory-001")]
    public void SplitSwapMergeAndRejects_Over500Cycles_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        Totals totals = default;

        // Act
        AllocAssert.Zero("4 split/merge (mail and item) + 2 swaps + 1 blocked merge + 1 bad_count split x500 cycles", () => totals = w.Run(Cycles));

        // Assert
        Workload.AssertTotals(in totals, Cycles);
    }

    [Fact]
    [Trait("Id", "TR-inventory-001")]
    public void SplitSwapMergeAndRejects_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
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

    private record struct Totals(int MailSplits, int MailMerges, int Swaps, int MergeRejects, int SplitRejects, int ItemSplits, int ItemMerges, int CycleRestored);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private const uint Address3 = 3u;
        private const int OpsPerCycle = 8;

        private static readonly PlayerSlot _slotB = PlayerSlot.FromValue(1);
        private static readonly uint[] _letterIds = InvAutoPlaceRig.Ids(1, 10);

        private readonly InvAutoPlaceRig _rig = new(joinPlayerB: true);
        private readonly ActorId _playerB;
        private readonly ushort _chest;
        private readonly ushort _inventory;
        private readonly ushort _hotbar;
        private readonly ushort _cursor;
        private readonly ushort _letters;
        private readonly ushort _chestPlanks;
        private readonly ushort _axe;
        private readonly ushort _heldPlanks;
        private readonly short _points;
        private readonly Op[] _ops = new Op[OpsPerCycle];
        private Totals _totals;

        public Workload()
        {
            _playerB = _rig.PlayerB;
            _chest = _rig.CreateGrid(8, 4);
            Assert.True(_rig.Inventory.TryOpenExternal(_slotB, _rig.IdOf(_chest)));
            _inventory = _rig.InventoryOf(1);
            _hotbar = _rig.HotbarOf(1);
            _cursor = _rig.CursorOf(1);
            _letters = _rig.PlaceMail(_inventory, "letter", 0, 0, Address3, _letterIds);
            _chestPlanks = _rig.PlaceItem(_chest, "plank", 0, 0, 10);
            _axe = _rig.PlaceItem(_hotbar, "axe", 1, 0, 1);
            _heldPlanks = _rig.PlaceItem(_cursor, "plank", 0, 0, 3);
            _points = _rig.Inventory.WeightPoints(_slotB);
            _ops[0] = Move(_inventory, _letters, _chest, 5, 3, 3);
            _ops[1] = MergeOfPiece(_chest, _inventory, _letters);  // payload rewritten in the window with the piece's id
            _ops[2] = Move(_cursor, _heldPlanks, _hotbar, 1, 0, 0); // swap
            _ops[3] = Move(_cursor, _axe, _hotbar, 1, 0, 0);        // swap back
            _ops[4] = Merge(_inventory, _letters, _inventory, _letters); // blocked: onto itself
            _ops[5] = Move(_inventory, _letters, _chest, 5, 3, 11); // bad_count: above the total
            _ops[6] = Move(_chest, _chestPlanks, _inventory, 7, 1, 4);
            _ops[7] = MergeOfPiece(_inventory, _chest, _chestPlanks);
        }

        /// <summary>
        /// The cycle's start state: the letters 10 [1..10] at inventory (0,0), the chest planks 10 at (0,0), the Axe in hotbar slot 1, the
        /// planks on the cursor, chest (5,3) and inventory (7,1) free, P as arranged.
        /// </summary>
        public bool AtCycleStart =>
            At(_letters, _inventory, 0, 0) && _rig.Entries[_letters].Count == 10
            && _rig.Inventory.MailIds.Read(in _rig.Entries[_letters].MailIds).SequenceEqual(_letterIds)
            && At(_chestPlanks, _chest, 0, 0) && _rig.Entries[_chestPlanks].Count == 10
            && At(_axe, _hotbar, 1, 0) && At(_heldPlanks, _cursor, 0, 0)
            && _rig.EntryAt(_chest, 5, 3) == Occupancy.Free && _rig.EntryAt(_inventory, 7, 1) == Occupancy.Free
            && _rig.Inventory.WeightPoints(_slotB) == _points;

        public static void AssertTotals(in Totals t, int cycles)
        {
            Assert.Equal(new Totals(cycles, cycles, 2 * cycles, cycles, cycles, cycles, cycles, cycles), t);
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
                        if (i is 1 or 7)
                        {
                            RetargetPiece(ref _ops[i]);
                        }

                        uint versions = VersionSum();
                        uint lastId = _rig.Entries.LastId;
                        _ = _rig.Host.World.EnqueueOp(_playerB, in _ops[i]);
                        _rig.Host.World.Tick();
                        Tally(i, VersionSum() == versions, lastId);
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

        private void Tally(int op, bool unchanged, uint lastIdBefore)
        {
            bool newId = _rig.Entries.LastId == lastIdBefore + 1;
            switch (op)
            {
                case 0:
                    _totals.MailSplits += Units(_letters, _inventory) == 7 && PieceUnits(_chest, 5, 3) == 3 && newId && !unchanged ? 1 : 0;
                    break;
                case 1:
                    _totals.MailMerges += Units(_letters, _inventory) == 10 && _rig.EntryAt(_chest, 5, 3) == Occupancy.Free && !unchanged ? 1 : 0;
                    break;
                case 2:
                    _totals.Swaps += At(_heldPlanks, _hotbar, 1, 0) && At(_axe, _cursor, 0, 0) && !unchanged ? 1 : 0;
                    break;
                case 3:
                    _totals.Swaps += At(_axe, _hotbar, 1, 0) && At(_heldPlanks, _cursor, 0, 0) && !unchanged ? 1 : 0;
                    break;
                case 4:
                    _totals.MergeRejects += Units(_letters, _inventory) == 10 && unchanged ? 1 : 0;
                    break;
                case 5:
                    _totals.SplitRejects += Units(_letters, _inventory) == 10 && _rig.EntryAt(_chest, 5, 3) == Occupancy.Free && !newId && unchanged ? 1 : 0;
                    break;
                case 6:
                    _totals.ItemSplits += Units(_chestPlanks, _chest) == 6 && PieceUnits(_inventory, 7, 1) == 4 && newId && !unchanged ? 1 : 0;
                    break;
                default:
                    _totals.ItemMerges += Units(_chestPlanks, _chest) == 10 && _rig.EntryAt(_inventory, 7, 1) == Occupancy.Free && !unchanged ? 1 : 0;
                    break;
            }
        }

        // The piece the previous op's split made holds the pool's high-water id (ids are never reused, ADR-0004).
        private void RetargetPiece(ref Op op)
        {
            MergePayload p = op.PayloadAs<MergePayload>();
            p.EntryId = _rig.Entries.LastId;
            op.SetPayload(in p);
        }

        private uint VersionSum() =>
            _rig.Header(_inventory).Version + _rig.Header(_chest).Version + _rig.Header(_hotbar).Version + _rig.Header(_cursor).Version;

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

        private int PieceUnits(ushort container, int x, int y)
        {
            ushort piece = _rig.EntryAt(container, x, y);
            return piece == Occupancy.Free ? -1 : _rig.Entries[piece].Count;
        }

        private Op Move(ushort from, ushort entry, ushort to, short x, short y, byte count)
        {
            var op = new Op { Kind = InvOpKinds.Move, ReqId = 82 };
            op.SetPayload(new MovePayload { From = _rig.IdOf(from).Value, EntryId = _rig.Entries[entry].Id.Value, To = _rig.IdOf(to).Value, X = x, Y = y, Count = count });
            return op;
        }

        private Op Merge(ushort from, ushort entry, ushort to, ushort target)
        {
            var op = new Op { Kind = InvOpKinds.Merge, ReqId = 83 };
            op.SetPayload(new MergePayload { From = _rig.IdOf(from).Value, EntryId = _rig.Entries[entry].Id.Value, To = _rig.IdOf(to).Value, TargetEntryId = _rig.Entries[target].Id.Value });
            return op;
        }

        private Op MergeOfPiece(ushort from, ushort to, ushort target)
        {
            var op = new Op { Kind = InvOpKinds.Merge, ReqId = 83 };
            op.SetPayload(new MergePayload { From = _rig.IdOf(from).Value, To = _rig.IdOf(to).Value, TargetEntryId = _rig.Entries[target].Id.Value });
            return op;
        }
    }
}
