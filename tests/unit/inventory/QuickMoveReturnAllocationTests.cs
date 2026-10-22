using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/010 AC "0 B" (ADR-0009 Implementation Guidelines; ADR-0006 0 B per tick), slice 1: the <c>QuickMove</c> op
/// (<see cref="QuickMoveHandler"/>) allocates 0 B per op after warm-up, through the real phase-0 drain. One cycle is six ticks, one op
/// each, by the player in the theory's slot (0 and 1, so a hard-coded slot cannot pass) with an open chest: a partial quick_move of 3 of
/// the inventory's 10 letters [1..10] into the chest (the R9 panel → external row; <see cref="PerformativeMail.Sim.Inventory.Placement.AutoPlace.CommitSplit"/>: a new
/// entry and its arena block); a whole quick_move of that piece back (the external → [inventory, hotbar] row; it merges into the letters,
/// the sorted union, and the piece is removed); a <c>bad_count</c> reject (count above the total; Mail Lead planning default pending
/// Evan, TD-153); a whole quick_move of the hotbar Axe into the chest and back to hotbar slot 1; a <c>not_open</c> reject (the other
/// player's inventory). The piece is named by the pool's high-water, so its payload is rewritten in the window (no allocation). The
/// cycle ends as it began. Each op's outcome is checked from the state between ticks; the twin also runs one cycle with capture on and
/// checks every reply's reason. The 0 B assertion runs in optimised builds only (TD-008 convention); its twin runs the same window in
/// every build. The rig's event capture is off in the window. Slice 2 adds <c>return</c> to the cycle. Joins the non-parallel
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
    public void QuickMoveWholePartialAndRejects_Over500Cycles_AllocateNothing_InOptimisedBuilds(int slot)
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload(slot);
        Totals totals = default;

        // Act
        AllocAssert.Zero("1 partial + 3 whole quick_moves + 1 bad_count + 1 not_open x500 cycles", () => totals = w.Run(Cycles));

        // Assert
        Workload.AssertTotals(in totals, Cycles);
    }

    [Theory]
    [Trait("Id", "TR-inventory-001")]
    [InlineData(0)]
    [InlineData(1)]
    public void QuickMoveWholePartialAndRejects_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild(int slot)
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

    private record struct Totals(int Partials, int MergesBack, int CountRejects, int AxeOut, int AxeBack, int OpenRejects, int CycleRestored);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private const uint Address3 = 3u;
        private const int OpsPerCycle = 6;

        private static readonly uint[] _letterIds = InvAutoPlaceRig.Ids(1, 10);

        private readonly InvAutoPlaceRig _rig = new(joinPlayerB: true);
        private readonly PlayerSlot _slot;
        private readonly ActorId _actor;
        private readonly ushort _chest;
        private readonly ushort _inventory;
        private readonly ushort _hotbar;
        private readonly ushort _letters;
        private readonly ushort _axe;
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
            _letters = _rig.PlaceMail(_inventory, "letter", 0, 0, Address3, _letterIds);
            _axe = _rig.PlaceItem(_hotbar, "axe", 1, 0, 1);
            _points = _rig.Inventory.WeightPoints(_slot);
            _ops[0] = QuickMove(_inventory, _rig.Entries[_letters].Id.Value, 3);
            _ops[1] = QuickMove(_chest, 0u, 0); // the piece: payload rewritten in the window with the pool's high-water
            _ops[2] = QuickMove(_inventory, _rig.Entries[_letters].Id.Value, 11); // bad_count: above the total
            _ops[3] = QuickMove(_hotbar, _rig.Entries[_axe].Id.Value, 0);
            _ops[4] = QuickMove(_chest, _rig.Entries[_axe].Id.Value, 0);
            _ops[5] = QuickMove(_rig.InventoryOf(1 - slot), _rig.Entries[_letters].Id.Value, 0); // not_open: the other player's inventory
        }

        /// <summary>Each op's reply reason in cycle order: 0 for ok, else the reject (<c>bad_count</c> and <c>not_open</c>, ADR-0009 section 2 codes).</summary>
        public static ushort[] ExpectedReasons { get; } = { 0, 0, (ushort)InvReason.BadCount, 0, 0, (ushort)InvReason.NotOpen };

        /// <summary>The cycle's start state: the letters 10 [1..10] at inventory (0,0), the Axe in hotbar slot 1, the chest empty, P as arranged.</summary>
        public bool AtCycleStart =>
            At(_letters, _inventory, 0, 0) && _rig.Entries[_letters].Count == 10
            && _rig.Inventory.MailIds.Read(in _rig.Entries[_letters].MailIds).SequenceEqual(_letterIds)
            && At(_axe, _hotbar, 1, 0) && _rig.Header(_chest).EntryCount == 0
            && _rig.Inventory.WeightPoints(_slot) == _points;

        public static void AssertTotals(in Totals t, int cycles)
        {
            Assert.Equal(new Totals(cycles, cycles, cycles, cycles, cycles, cycles, cycles), t);
        }

        /// <summary>Runs one more cycle with event capture on (outside any measured window) and returns each op's reply reason.</summary>
        public ushort[] OneCycleReasons()
        {
            var reasons = new ushort[OpsPerCycle];
            for (int i = 0; i < OpsPerCycle; i++)
            {
                if (i == 1)
                {
                    RetargetPiece(ref _ops[i]);
                }

                reasons[i] = InvAutoPlaceRig.ReplyOf(_rig.Run(_actor, in _ops[i])).Reason;
            }

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
                        if (i == 1)
                        {
                            RetargetPiece(ref _ops[i]);
                        }

                        uint versions = VersionSum();
                        uint lastId = _rig.Entries.LastId;
                        _ = _rig.Host.World.EnqueueOp(_actor, in _ops[i]);
                        _rig.Host.World.Tick();
                        Tally(i, VersionSum() == versions, _rig.Entries.LastId == lastId + 1);
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
                default:
                    _totals.OpenRejects += Units(_letters, _inventory) == 10 && !newId && unchanged ? 1 : 0;
                    break;
            }
        }

        // The piece the previous op's split made holds the pool's high-water id (ids are never reused, ADR-0004).
        private void RetargetPiece(ref Op op)
        {
            QuickMovePayload p = op.PayloadAs<QuickMovePayload>();
            p.EntryId = _rig.Entries.LastId;
            op.SetPayload(in p);
        }

        private uint VersionSum() => _rig.Header(_inventory).Version + _rig.Header(_chest).Version + _rig.Header(_hotbar).Version;

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

        private Op QuickMove(ushort from, uint entry, byte count)
        {
            var op = new Op { Kind = InvOpKinds.QuickMove, ReqId = 85 };
            op.SetPayload(new QuickMovePayload { From = _rig.IdOf(from).Value, EntryId = entry, Count = count });
            return op;
        }
    }
}
