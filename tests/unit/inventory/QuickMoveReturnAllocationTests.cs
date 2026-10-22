using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/010 AC "0 B" (ADR-0009 Implementation Guidelines; ADR-0006 0 B per tick): the <c>QuickMove</c> op
/// (<see cref="QuickMoveHandler"/>) and the <c>Return</c> op (<see cref="ReturnHandler"/>) allocate 0 B per op after warm-up, through
/// the real phase-0 drain. One cycle is fourteen ticks, one op each, by the player in the theory's slot (0 and 1, so a hard-coded slot cannot pass) with an open chest: a partial quick_move of 3 of
/// the inventory's 10 letters [1..10] into the chest (the R9 panel → external row; <see cref="PerformativeMail.Sim.Inventory.Placement.AutoPlace.CommitSplit"/>: a new
/// entry and its arena block); a whole quick_move of that piece back (the external → [inventory, hotbar] row; it merges into the letters,
/// the sorted union, and the piece is removed); a <c>bad_count</c> reject (count above the total; Mail Lead planning default pending
/// Evan, TD-153); a whole quick_move of the hotbar Axe into the chest and back to hotbar slot 1; a <c>not_open</c> reject (the other
/// player's inventory); then the return ops: a <c>pick</c> of the Axe from hotbar slot 1 and a <c>return</c> to that origin (the origin
/// leg); a <c>cursor_empty</c> reject (a <c>return</c> on the empty cursor); a <c>pick</c> of the Axe again, a <c>move</c> of the letters
/// onto its hotbar slot, and a <c>return</c> that finds the origin blocked and puts the Axe in hotbar slot 2 (the panel leg, the cursor
/// row); a <c>move</c> of the letters back to inventory (0,0) and of the Axe back to slot 1. Then (Mail Review r1): a <c>pick</c> of the
/// letters from inventory (0,0) (origin inventory (0,0)), a <c>move</c> of the same-key stack S (18 letters [100..117], kept in hotbar
/// slot 5) onto that cell, and a <c>return</c> whose origin leg merges the 2 oldest into S (room 2) and whose panel leg places the other
/// 8 at inventory (1,0) (<see cref="PerformativeMail.Sim.Inventory.Placement.AutoPlace.PlanRemainder"/>); a <c>move count 2</c> of S's
/// two oldest ([1, 2]) back onto the letters, a <c>move</c> of S back to hotbar slot 5 and of the letters back to (0,0); the Intake
/// opened as the external (<c>TryOpenExternal</c>, between ticks); a partial quick_move of 1 letter into the Intake (NoMerge: a new
/// entry with a new <c>arrivalSeq</c>, which fills the entry pool); a second one, refused <c>capacity_exhausted</c> (the pool is full;
/// Mail Lead planning default pending Evan, TD-153); the Intake piece quick_moved back (it merges into the letters); a 1 x 1 grid
/// holding a fiber opened as the external and a whole quick_move of the letters into it, refused <c>no_room</c>; then the chest opened
/// again. The pool holds exactly one free slot at the cycle start. The pieces are named by the pool's high-water,
/// so their payloads are rewritten in the window (no allocation). The cycle ends as it began. Each op's outcome is checked from the state between ticks; the twin also runs one cycle with capture on and
/// checks every reply's reason. The 0 B assertion runs in optimised builds only (TD-008 convention); its twin runs the same window in
/// every build. The rig's event capture is off in the window. Joins the non-parallel
/// collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class QuickMoveReturnAllocationTests
{
    /// <summary>Cycles per measured window.</summary>
    private const int Cycles = 500;

    [Theory]
    [Trait("Id", "TR-inventory-001")]
    [InlineData(0)]
    [InlineData(1)]
    public void QuickMoveReturnAndRejects_Over500Cycles_AllocateNothing_InOptimisedBuilds(int slot)
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload(slot);
        Totals totals = default;

        // Act
        AllocAssert.Zero(
            "2 partial + 4 whole quick_moves + bad_count, not_open, capacity_exhausted, no_room + 3 picks + origin, panel and partial-origin returns + 1 cursor_empty + 7 moves + 3 external switches x500 cycles",
            () => totals = w.Run(Cycles));

        // Assert
        Workload.AssertTotals(in totals, Cycles);
    }

    [Theory]
    [Trait("Id", "TR-inventory-001")]
    [InlineData(0)]
    [InlineData(1)]
    public void QuickMoveReturnAndRejects_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild(int slot)
    {
        // Arrange: the same workload as the 0 B test (which is skipped in unoptimised builds), run without measuring
        var w = new Workload(slot);

        // Act
        Totals first = w.Run(Cycles);
        Totals second = w.Run(Cycles);

        // Assert
        Workload.AssertTotals(in first, Cycles);
        Assert.Equal(first, second);
        Assert.True(w.AtCycleStart);
        Assert.Equal(Workload.ExpectedReasons, w.OneCycleReasons());
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
        int Partials, int MergesBack, int CountRejects, int AxeOut, int AxeBack, int OpenRejects, int Picks, int OriginReturns, int EmptyRejects,
        int LettersOnSlot, int PanelReturns, int LettersBack, int AxeHome, int CycleRestored, int LetterPicks, int StackOnOrigin, int PartialOriginReturns,
        int OldestBack, int StackHome, int LettersHome, int IntakeOut, int PoolRejects, int IntakeBack, int RoomRejects);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private const uint Address3 = 3u;
        private const int OpsPerCycle = 24;

        // The ops that run with another external open: the Intake from IntakeFirstOp, the 1 x 1 grid at TinyOp; the chest otherwise.
        private const int IntakeFirstOp = 20;
        private const int TinyOp = 23;

        // Live entries at the cycle start: the letters, the Axe, S and the tiny grid's fiber; one pool slot stays free.
        private const int SteadyEntries = 4;

        private static readonly uint[] _letterIds = InvAutoPlaceRig.Ids(1, 10);
        private static readonly uint[] _stackIds = InvAutoPlaceRig.Ids(100, 18);

        private readonly InvAutoPlaceRig _rig = new(entryPoolCapacity: SteadyEntries + 1, joinPlayerB: true);
        private readonly PlayerSlot _slot;
        private readonly ActorId _actor;
        private readonly ushort _chest;
        private readonly ushort _inventory;
        private readonly ushort _hotbar;
        private readonly ushort _cursor;
        private readonly ushort _letters;
        private readonly ushort _axe;
        private readonly ushort _stack;
        private readonly ushort _tiny;
        private readonly short _points;
        private readonly Op[] _ops = new Op[OpsPerCycle];
        private Totals _totals;

        public Workload(int slot)
        {
            _slot = PlayerSlot.FromValue((byte)slot);
            _actor = _rig.Player(slot);
            _chest = _rig.CreateGrid(8, 4);
            Assert.True(_rig.Inventory.TryOpenExternal(_slot, _rig.IdOf(_chest)));
            _inventory = _rig.InventoryOf(slot);
            _hotbar = _rig.HotbarOf(slot);
            _cursor = _rig.CursorOf(slot);
            _letters = _rig.PlaceMail(_inventory, "letter", 0, 0, Address3, _letterIds);
            _axe = _rig.PlaceItem(_hotbar, "axe", 1, 0, 1);
            _stack = _rig.PlaceMail(_hotbar, "letter", 5, 0, Address3, _stackIds);
            _tiny = _rig.CreateGrid(1, 1);
            _ = _rig.PlaceItem(_tiny, "fiber", 0, 0, 1);
            Assert.Equal(SteadyEntries, _rig.Entries.LiveCount);
            _points = _rig.Inventory.WeightPoints(_slot);
            _ops[0] = QuickMove(_inventory, _rig.Entries[_letters].Id.Value, 3);
            _ops[1] = QuickMove(_chest, 0u, 0); // the piece: payload rewritten in the window with the pool's high-water
            _ops[2] = QuickMove(_inventory, _rig.Entries[_letters].Id.Value, 11); // bad_count: above the total
            _ops[3] = QuickMove(_hotbar, _rig.Entries[_axe].Id.Value, 0);
            _ops[4] = QuickMove(_chest, _rig.Entries[_axe].Id.Value, 0);
            _ops[5] = QuickMove(_rig.InventoryOf(1 - slot), _rig.Entries[_letters].Id.Value, 0); // not_open: the other player's inventory
            _ops[6] = Pick(_hotbar, _axe); // origin: hotbar slot 1
            _ops[7] = new Op { Kind = InvOpKinds.Return, ReqId = 86 }; // the origin leg: back to slot 1
            _ops[8] = _ops[7]; // cursor_empty
            _ops[9] = _ops[6];
            _ops[10] = Move(_inventory, _letters, _hotbar, 1); // the origin slot is now blocked
            _ops[11] = _ops[7]; // the panel leg: hotbar slot 2 (a tool: hotbar first)
            _ops[12] = Move(_hotbar, _letters, _inventory, 0);
            _ops[13] = Move(_hotbar, _axe, _hotbar, 1);
            _ops[14] = Pick(_inventory, _letters); // origin: inventory (0,0)
            _ops[15] = Move(_hotbar, _stack, _inventory, 0);
            _ops[16] = _ops[7]; // the origin leg merges 2 into S (room 2), the panel leg places the other 8 at inventory (1,0)
            _ops[17] = Move(_inventory, _stack, _inventory, 1, 0, 2); // S's 2 oldest, [1, 2], merge back into the letters
            _ops[18] = Move(_inventory, _stack, _hotbar, 5);
            _ops[19] = Move(_inventory, _letters, _inventory, 0);
            _ops[IntakeFirstOp] = QuickMove(_inventory, _rig.Entries[_letters].Id.Value, 1); // into the Intake, NoMerge: a new entry
            _ops[21] = _ops[IntakeFirstOp]; // capacity_exhausted: the pool is full
            _ops[22] = QuickMove(_rig.Intake, 0u, 0); // the Intake piece: payload rewritten in the window with the pool's high-water
            _ops[TinyOp] = QuickMove(_inventory, _rig.Entries[_letters].Id.Value, 0); // no_room: the 1 x 1 grid holds a fiber
        }

        /// <summary>Each op's reply reason in cycle order: 0 for ok, else the reject (<c>bad_count</c> and <c>not_open</c>, ADR-0009 section 2 codes).</summary>
        public static ushort[] ExpectedReasons { get; } =
        {
            0, 0, (ushort)InvReason.BadCount, 0, 0, (ushort)InvReason.NotOpen, 0, 0, (ushort)InvReason.CursorEmpty, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, (ushort)InvReason.CapacityExhausted, 0, (ushort)InvReason.NoRoom,
        };

        /// <summary>The cycle's start state: the letters 10 [1..10] at inventory (0,0), the Axe in hotbar slot 1, the chest empty, P as arranged.</summary>
        public bool AtCycleStart =>
            At(_letters, _inventory, 0, 0) && _rig.Entries[_letters].Count == 10
            && _rig.Inventory.MailIds.Read(in _rig.Entries[_letters].MailIds).SequenceEqual(_letterIds)
            && At(_axe, _hotbar, 1, 0) && _rig.Header(_chest).EntryCount == 0 && _rig.Header(_cursor).EntryCount == 0
            && At(_stack, _hotbar, 5, 0) && _rig.Inventory.MailIds.Read(in _rig.Entries[_stack].MailIds).SequenceEqual(_stackIds)
            && _rig.Header(_rig.Intake).EntryCount == 0 && _rig.Entries.LiveCount == SteadyEntries
            && _rig.Inventory.OpenSet.ExternalOf(_slot) == _rig.IdOf(_chest)
            && _rig.Inventory.WeightPoints(_slot) == _points;

        public static void AssertTotals(in Totals t, int cycles)
        {
            Assert.Equal(
                new Totals(
                    cycles, cycles, cycles, cycles, cycles, cycles, 2 * cycles, cycles, cycles, cycles, cycles, cycles, cycles, cycles,
                    cycles, cycles, cycles, cycles, cycles, cycles, cycles, cycles, cycles, cycles),
                t);
        }

        /// <summary>Runs one more cycle with event capture on (outside any measured window) and returns each op's reply reason.</summary>
        public ushort[] OneCycleReasons()
        {
            var reasons = new ushort[OpsPerCycle];
            for (int i = 0; i < OpsPerCycle; i++)
            {
                BeforeOp(i);
                reasons[i] = InvAutoPlaceRig.ReplyOf(_rig.Run(_actor, in _ops[i])).Reason;
            }

            SwitchExternal(_chest);
            return reasons;
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
                        BeforeOp(i);
                        uint versions = VersionSum();
                        uint lastId = _rig.Entries.LastId;
                        _ = _rig.Host.World.EnqueueOp(_actor, in _ops[i]);
                        _rig.Host.World.Tick();
                        Tally(i, VersionSum() == versions, _rig.Entries.LastId == lastId + 1);
                    }

                    SwitchExternal(_chest);
                    _totals.CycleRestored += AtCycleStart ? 1 : 0;
                }
            }
            finally
            {
                _rig.CaptureEvents = true;
            }

            return _totals;
        }

        private void Tally(int op, bool unchanged, bool newId)
        {
            switch (op)
            {
                case 0:
                    _totals.Partials += Units(_letters, _inventory) == 7 && PieceUnits(_chest, 0, 0) == 3 && newId && !unchanged ? 1 : 0;
                    break;
                case 1:
                    _totals.MergesBack += Units(_letters, _inventory) == 10 && _rig.Header(_chest).EntryCount == 0 && !newId && !unchanged ? 1 : 0;
                    break;
                case 2:
                    _totals.CountRejects += Units(_letters, _inventory) == 10 && !newId && unchanged ? 1 : 0;
                    break;
                case 3:
                    _totals.AxeOut += At(_axe, _chest, 0, 0) && !unchanged ? 1 : 0;
                    break;
                case 4:
                    _totals.AxeBack += At(_axe, _hotbar, 1, 0) && !unchanged ? 1 : 0;
                    break;
                case 5:
                    _totals.OpenRejects += Units(_letters, _inventory) == 10 && !newId && unchanged ? 1 : 0;
                    break;
                case 6:
                case 9:
                    _totals.Picks += At(_axe, _cursor, 0, 0) && !unchanged ? 1 : 0;
                    break;
                case 7:
                    _totals.OriginReturns += At(_axe, _hotbar, 1, 0) && _rig.Header(_cursor).EntryCount == 0 && !unchanged ? 1 : 0;
                    break;
                case 8:
                    _totals.EmptyRejects += At(_axe, _hotbar, 1, 0) && !newId && unchanged ? 1 : 0;
                    break;
                case 10:
                    _totals.LettersOnSlot += At(_letters, _hotbar, 1, 0) ? 1 : 0;
                    break;
                case 11:
                    _totals.PanelReturns += At(_axe, _hotbar, 2, 0) && _rig.Header(_cursor).EntryCount == 0 && !unchanged ? 1 : 0;
                    break;
                case 12:
                    _totals.LettersBack += At(_letters, _inventory, 0, 0) ? 1 : 0;
                    break;
                case 13:
                    _totals.AxeHome += At(_axe, _hotbar, 1, 0) ? 1 : 0;
                    break;
                case 14:
                    _totals.LetterPicks += At(_letters, _cursor, 0, 0) && !unchanged ? 1 : 0;
                    break;
                case 15:
                    _totals.StackOnOrigin += At(_stack, _inventory, 0, 0) ? 1 : 0;
                    break;
                case 16:
                    _totals.PartialOriginReturns += Units(_stack, _inventory) == 20 && At(_letters, _inventory, 1, 0) && Units(_letters, _inventory) == 8
                        && _rig.Header(_cursor).EntryCount == 0 && !newId && !unchanged ? 1 : 0;
                    break;
                case 17:
                    _totals.OldestBack += Units(_letters, _inventory) == 10 && Units(_stack, _inventory) == 18 && !newId ? 1 : 0;
                    break;
                case 18:
                    _totals.StackHome += At(_stack, _hotbar, 5, 0) ? 1 : 0;
                    break;
                case 19:
                    _totals.LettersHome += At(_letters, _inventory, 0, 0) ? 1 : 0;
                    break;
                case IntakeFirstOp:
                    _totals.IntakeOut += Units(_letters, _inventory) == 9 && PieceUnits(_rig.Intake, 0, 0) == 1
                        && _rig.Entries[_rig.EntryAt(_rig.Intake, 0, 0)].ArrivalSeq != 0 && newId && !unchanged ? 1 : 0;
                    break;
                case 21:
                    _totals.PoolRejects += Units(_letters, _inventory) == 9 && _rig.Entries.LiveCount == _rig.Entries.Capacity && !newId && unchanged ? 1 : 0;
                    break;
                case 22:
                    _totals.IntakeBack += Units(_letters, _inventory) == 10 && _rig.Header(_rig.Intake).EntryCount == 0 && !newId && !unchanged ? 1 : 0;
                    break;
                default:
                    _totals.RoomRejects += At(_letters, _inventory, 0, 0) && Units(_letters, _inventory) == 10 && !newId && unchanged ? 1 : 0;
                    break;
            }
        }

        // Rewrites a piece's payload (ops 1 and 22) and opens the external the op runs with (the Intake at IntakeFirstOp, the 1 x 1
        // grid at TinyOp; the chest comes back at the cycle end).
        private void BeforeOp(int op)
        {
            if (op is 1 or 22)
            {
                RetargetPiece(ref _ops[op]);
            }

            if (op == IntakeFirstOp)
            {
                SwitchExternal(_rig.Intake);
            }
            else if (op == TinyOp)
            {
                SwitchExternal(_tiny);
            }
        }

        private void SwitchExternal(ushort container)
        {
            ContainerId id = _rig.IdOf(container);
            if (_rig.Inventory.OpenSet.ExternalOf(_slot) != id)
            {
                _ = _rig.Inventory.TryOpenExternal(_slot, id);
            }
        }

        // The piece the previous op's split made holds the pool's high-water id (ids are never reused, ADR-0004).
        private void RetargetPiece(ref Op op)
        {
            QuickMovePayload p = op.PayloadAs<QuickMovePayload>();
            p.EntryId = _rig.Entries.LastId;
            op.SetPayload(in p);
        }

        private uint VersionSum() =>
            _rig.Header(_inventory).Version + _rig.Header(_chest).Version + _rig.Header(_hotbar).Version + _rig.Header(_cursor).Version
            + _rig.Header(_rig.Intake).Version + _rig.Header(_tiny).Version;

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

        private Op Pick(ushort from, ushort entry)
        {
            var op = new Op { Kind = InvOpKinds.Pick, ReqId = 80 };
            op.SetPayload(new PickPayload { From = _rig.IdOf(from).Value, EntryId = _rig.Entries[entry].Id.Value });
            return op;
        }

        // baseVersion 0: no sort runs, so every lastReplaceVersion stays 0 and the payload stays valid.
        private Op Move(ushort from, ushort entry, ushort to, int x, int y = 0, byte count = 0)
        {
            var op = new Op { Kind = InvOpKinds.Move, ReqId = 81 };
            op.SetPayload(new MovePayload
            {
                From = _rig.IdOf(from).Value, EntryId = _rig.Entries[entry].Id.Value, To = _rig.IdOf(to).Value, X = (short)x, Y = (short)y, Count = count,
            });
            return op;
        }

        private Op QuickMove(ushort from, uint entry, byte count)
        {
            var op = new Op { Kind = InvOpKinds.QuickMove, ReqId = 85 };
            op.SetPayload(new QuickMovePayload { From = _rig.IdOf(from).Value, EntryId = entry, Count = count });
            return op;
        }
    }
}
