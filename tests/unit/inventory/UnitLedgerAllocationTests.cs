using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Intake;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/017 AC "0 B, cost, determinism" (ADR-0009 section 6; ADR-0006 0 B per tick): every ledger crossing and the
/// tick-end <see cref="InventoryConservationCheck"/> allocate 0 B after warm-up. Two worlds, every tick ending in the check:
/// <list type="bullet">
/// <item><see cref="Workload"/>, each cycle inside the rig's <c>MailStep</c> hook (a delegate cached before the window): <c>Spawn</c>
/// of 2 letters into the Intake (spawned, Placed), 2 <c>TakeUnit</c>s (Placed → Belt), a merging <c>TryInsert</c> into A's inventory
/// (Belt → Placed) and a <c>ConsumeUnit</c> of it (Placed → Consumed), a placing <c>TryInsert</c> into A's hotbar,
/// <c>DetachForThrow</c> of it (Placed → Flight), <c>CreateWorldItem</c> (Flight → World), the WorldItem's removal (World → none)
/// and <c>DestroyDetached</c> (→ Destroyed).</item>
/// <item><see cref="CustodyWorkload"/>, the rest of the crossing table, one cycle of two ticks through the real phases: the water
/// return to the Intake and to the backlog (phase 1, World → Placed and World → Backlog), the <c>IntakeDrain</c> drain (phase 2,
/// Backlog → Placed), <c>AppendBacklog</c> (spawned, Backlog), the player ops pick, place, a move inside one container, a split onto
/// the cursor and a merge from it (phase 0, Placed ↔ Cursor), <c>TryCatch</c> with a fumble (Flight → Placed and World), the whole
/// and the partial pickup (World → Placed) and the leash put-away (<c>PutAwayCursor</c>, Cursor → Placed).</item>
/// </list>
/// The non-mail destroy on cap eviction (<c>DestroyNonMail</c> → <c>DestroyDetached</c>, World → Destroyed) is not repeated here:
/// <see cref="WorldItemAllocationTests"/> destroys a plank by cap eviction every tick of its window, with the ledger live. The 0 B
/// assertion runs in optimised builds only (TD-008
/// convention); its twins run the same windows in every build (in Debug with the <c>INVENTORY_ASSERT</c> scan every tick) and assert
/// the results and a balanced ledger. Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class UnitLedgerAllocationTests
{
    /// <summary>Cycles per measured window.</summary>
    private const int Cycles = 600;

    /// <summary>Two-tick cycles per measured custody window.</summary>
    private const int CustodyCycles = 300;

    [Fact]
    [Trait("Id", "TR-inventory-012")]
    public void EveryCrossingAndTheTickEndCheck_Over600Cycles_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        var c = new CustodyWorkload();
        Totals totals = default;
        CustodyTotals custody = default;

        // Act
        AllocAssert.Zero("Spawn + TakeUnit x2 + TryInsert x2 + ConsumeUnit + DetachForThrow + CreateWorldItem + Remove + DestroyDetached + the tick-end check x600 cycles", () => totals = w.Run(Cycles));
        AllocAssert.Zero(
            "water returns to the Intake and the backlog + drain + AppendBacklog + pick/place/move/split/merge ops + TryCatch fumble + whole and partial pickups + leash put-away + the tick-end check x300 two-tick cycles",
            () => custody = c.Run(CustodyCycles));

        // Assert
        Assert.Equal(Workload.Expected(Cycles), totals);
        Assert.Equal(CustodyWorkload.Expected(CustodyCycles), custody);
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

    [Fact]
    [Trait("Id", "TR-inventory-012")]
    public void TheCustodyCrossings_TheMeasuredWindow_GiveTheExpectedResults_AndABalancedLedger_InEveryBuild()
    {
        // Arrange: the same custody workload as the 0 B test (skipped in unoptimised builds), run without measuring
        var c = new CustodyWorkload();
        UnitLedger ledger = c.Ledger;
        long[] before = CustodyWorkload.Counters(ledger);

        // Act
        CustodyTotals first = c.Run(CustodyCycles);
        CustodyTotals second = c.Run(CustodyCycles);

        // Assert: every crossing ran every cycle and the cycle ends as it began; per cycle the mail row spawned one (the append) and
        // consumed one, every other counter back where it started; no mismatch counted, in Debug no scan finding
        Assert.Equal(CustodyWorkload.Expected(CustodyCycles), first);
        Assert.Equal(first, second);
        Assert.Equal(0, c.Mismatches);
        Assert.Equal(ScanFindingKind.None, c.ScanFinding);
        long[] after = CustodyWorkload.Counters(ledger);
        for (int i = 0; i < after.Length; i++)
        {
            long moved = i is CustodyWorkload.MailSpawned or CustodyWorkload.MailDelivered ? 2 * CustodyCycles : 0;
            Assert.True(after[i] - before[i] == moved, $"counter {i} moved {after[i] - before[i]}, expected {moved}");
        }

        for (int row = 0; row < ledger.Rows; row++)
        {
            Assert.Equal(ledger.Expected(row), ledger.LiveSum(row));
        }
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

    private record struct CustodyTotals(
        int Returned, int Queued, int Drained, int Appended, int WaterThrown, int Inserted, int Consumed, int Ops, int Caught,
        int WholePickups, int PartialPickups, int PutAway, int CycleRestored);

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

    /// <summary>
    /// The custody crossings' world (players A and B, <see cref="PickupRig"/>): the Intake holds 319 letters (one free cell) and a water
    /// pipeline holds two letters landed on water per even tick over one float duration. A cycle is two ticks, through the real phases.
    /// Even tick: phase 0 runs A's ops pick P (10 planks at inventory (4,0)) onto the cursor, place it at (5,0), move it back to (4,0)
    /// and split 4 of it onto the cursor; phase 1 returns the pair landed 300 ticks earlier, the first into the Intake's free cell and
    /// the second, the Intake now full, to the backlog; the hook appends one letter to the backlog, takes three units from the Intake
    /// (<c>TakeUnit</c>), places each of the first two into A's hotbar as a new single-letter entry (<c>TryInsert</c>, no matching stack
    /// there) and throws it onto water (<c>DetachForThrow</c>, <c>CreateWorldItem</c>: the pair 300 ticks on), merges the third into
    /// R (10 letters, #4, A's inventory (2,1)) and consumes one of R. Odd tick: phase 0 merges the cursor piece into Q (12 planks at inventory
    /// (0,1)); phase 2 drains the two queued entries into the freed cells (one stays free); the hook has A throw Y (5 letters, #7, hotbar
    /// slot 2) to B, whose full panel takes 3 into X (17 letters, #7) and fumbles 2, which A picks up whole; A throws Y again as a
    /// WorldItem that B picks up partly (3) and A whole (2); X's 3 go back to Y and Q's 4 to P after each (the mutator's
    /// <c>MergeInto</c>, arrange: Placed → Placed); then A opens a chest, picks the axe in hotbar slot 1 onto the cursor and the leash
    /// closes the chest (the put-away, back to slot 1). The cycle ends as it began.
    /// </summary>
    private sealed class CustodyWorkload
    {
        /// <summary>The mail row's Consumed[Delivered] in <see cref="Counters"/>.</summary>
        public const int MailDelivered = UnitLedger.LiveStateCount + (int)ConsumeReason.Delivered;

        /// <summary>The mail row's spawned in <see cref="Counters"/>.</summary>
        public const int MailSpawned = UnitLedger.LiveStateCount + UnitLedger.ConsumeReasonCount + 1;

        private const int CountersPerRow = UnitLedger.LiveStateCount + UnitLedger.ConsumeReasonCount + 3;
        private const uint Address4 = 4u;
        private const uint Address7 = 7u;

        private static readonly PlayerSlot _slotA = PlayerSlot.FromValue(0);
        private static readonly PlayerSlot _slotB = PlayerSlot.FromValue(1);
        private static readonly Vec3Mm _pos = new(1_000, 0, 1_000);

        private readonly PickupRig _p = new();
        private readonly InvAutoPlaceRig _r;
        private readonly InventorySystem _inv;
        private readonly EventLogFacade _events;
        private readonly ContainerId _intake;
        private readonly ContainerId _hotbarAId;
        private readonly ContainerId _chest;
        private readonly ushort _hotbarA;
        private readonly ushort _inventoryA;
        private readonly ushort _cursorA;
        private readonly ushort _hotbarB;
        private readonly ushort _axe;
        private readonly ushort _r10;
        private readonly ushort _y;
        private readonly ushort _x;
        private readonly ushort _planksP;
        private readonly ushort _planksQ;
        private readonly ContainerId _inventoryAId;
        private readonly EntryId _yId;
        private readonly DefIndex _letter;
        private readonly int _water;
        private readonly MailId[] _one = new MailId[1];
        private readonly Op[] _evenOps = new Op[4];
        private readonly InvPhaseAction _cycle;
        private Op _merge;
        private uint _nextMail = 500_000;
        private CustodyTotals _totals;

        public CustodyWorkload()
        {
            _r = _p.R;
            _inv = _p.Inv;
            _events = _r.Host.Services.Events;
            _intake = _inv.IntakeContainer;
            _hotbarA = _p.Hotbar(0);
            _inventoryA = _p.Inventory(0);
            _cursorA = _r.CursorA;
            _hotbarB = _p.Hotbar(1);
            _hotbarAId = _r.IdOf(_hotbarA);
            _axe = _r.PlaceItem(_hotbarA, "axe", 1, 0, 1);
            _y = _r.PlaceMail(_hotbarA, "letter", 2, 0, Address7, InvAutoPlaceRig.Ids(2_000, 5));
            _r10 = _r.PlaceMail(_inventoryA, "letter", 2, 1, Address4, InvAutoPlaceRig.Ids(1_000, 10));
            _inventoryAId = _r.IdOf(_inventoryA);
            _yId = _r.Entries[_y].Id;
            _planksP = _r.PlaceItem(_inventoryA, "plank", 4, 0, 10);
            _planksQ = _r.PlaceItem(_inventoryA, "plank", 0, 1, 12);
            _x = _r.PlaceMail(_hotbarB, "letter", 1, 0, Address7, InvAutoPlaceRig.Ids(3_000, 17));
            _p.FillHotbar(1, from: 2);
            _p.FillInventory(1);
            _chest = _r.IdOf(_r.CreateGrid(2, 2));
            _letter = _r.KindIndex("letter");
            IntakeSpawnOutfeedTests.FillIntake(_r, 319, 10_000);

            // The water pipeline: two letters landed on water on every even tick of one float duration, the first returning on the
            // first measured tick
            uint water = _inv.WorldItems.DurationTicks(ExpiryFifo.WaterFloatMail);
            uint first = (_p.W.Now + 1) & ~1u;
            for (uint t = first; t < first + water; t += 2)
            {
                EntryId a = _r.Entries[_r.DetachedMail("letter", Address4, new[] { _nextMail++ })].Id;
                EntryId b = _r.Entries[_r.DetachedMail("letter", Address4, new[] { _nextMail++ })].Id;
                uint tick = t;
                Assert.True(_p.W.At(tick, () =>
                    !_inv.CreateWorldItem(a, _pos, WorldItemOrigin.Thrown, true, tick).IsNone
                    && !_inv.CreateWorldItem(b, _pos, WorldItemOrigin.Thrown, true, tick).IsNone));
            }

            _p.W.AdvanceTo(first + water);
            _water = (int)water;

            _evenOps[0] = PickOp(_inventoryA, _planksP);
            _evenOps[1] = MoveOp(_cursorA, _planksP, _inventoryA, 5, 0);
            _evenOps[2] = MoveOp(_inventoryA, _planksP, _inventoryA, 4, 0);
            _evenOps[3] = MoveOp(_inventoryA, _planksP, _cursorA, 0, 0, 4);
            _merge = new Op { Kind = InvOpKinds.Merge, ReqId = 83 };
            _cycle = Cycle;
            Assert.True(AtCycleStart);
        }

        public UnitLedger Ledger => _inv.Ledger;

        public long Mismatches => _r.Counters[CounterId.InventoryConservationMismatches];

        public ScanFindingKind ScanFinding => _inv.ConservationCheck.ScanFinding.Kind;

        /// <summary>
        /// The cycle's start state: P 10 at inventory (4,0), Q 12 at (0,1), R 10 at (2,1), the cursor empty, the axe and Y 5 in A's
        /// hotbar slots 1-2 (slot 3 free), X 17 in B's hotbar slot 1, the backlog empty, one free Intake cell, the water pipeline's letters the only WorldItems.
        /// </summary>
        public bool AtCycleStart =>
            At(_planksP, _inventoryA, 4, 0) && Units(_planksP, _inventoryA) == 10 && At(_planksQ, _inventoryA, 0, 1)
            && Units(_planksQ, _inventoryA) == 12 && _r.Header(_cursorA).EntryCount == 0 && At(_axe, _hotbarA, 1, 0)
            && At(_r10, _inventoryA, 2, 1) && Units(_r10, _inventoryA) == 10 && At(_y, _hotbarA, 2, 0) && Units(_y, _hotbarA) == 5
            && _r.EntryAt(_hotbarA, 3, 0) == Occupancy.Free
            && At(_x, _hotbarB, 1, 0) && Units(_x, _hotbarB) == 17 && _inv.BacklogEntries == 0 && _r.Header(_r.Intake).FreeCells == 1
            && _inv.WorldItems.LiveCount == _water;

        public static CustodyTotals Expected(int cycles) =>
            new(cycles, cycles, cycles, cycles, 2 * cycles, 3 * cycles, cycles, cycles, cycles, 2 * cycles, cycles, cycles, cycles);

        /// <summary>Every counter of every ledger row: the live states, Consumed per reason, Destroyed, spawned, granted.</summary>
        public static long[] Counters(UnitLedger ledger)
        {
            var v = new long[ledger.Rows * CountersPerRow];
            for (int row = 0; row < ledger.Rows; row++)
            {
                int b = row * CountersPerRow;
                for (int s = 0; s < UnitLedger.LiveStateCount; s++)
                {
                    v[b + s] = ledger.Live(row, (LedgerState)s);
                }

                for (int c = 0; c < UnitLedger.ConsumeReasonCount; c++)
                {
                    v[b + UnitLedger.LiveStateCount + c] = ledger.Consumed(row, (ConsumeReason)c);
                }

                v[b + MailSpawned - 1] = ledger.Destroyed(row);
                v[b + MailSpawned] = ledger.Spawned(row);
                v[b + MailSpawned + 1] = ledger.Granted(row);
            }

            return v;
        }

        public CustodyTotals Run(int cycles)
        {
            _totals = default;
            _r.CaptureEvents = false;
            _r.Hook.Action = _cycle;
            try
            {
                for (int c = 0; c < cycles; c++)
                {
                    // Even tick: the ops, the pair's return (one Intake arrival) and the second's queueing, the hook
                    uint arrivals = _r.Header(_r.Intake).NextArrivalSeq;
                    for (int i = 0; i < _evenOps.Length; i++)
                    {
                        _ = _r.Host.World.EnqueueOp(_r.PlayerA, in _evenOps[i]);
                    }

                    _r.Host.World.Tick();
                    _totals.Ops += At(_planksP, _inventoryA, 4, 0) && Units(_planksP, _inventoryA) == 6 && PieceOnCursor() == 4 ? 1 : 0;
                    _totals.Returned += _r.Header(_r.Intake).NextArrivalSeq - arrivals == 1 ? 1 : 0;
                    _totals.Queued += _inv.BacklogEntries == 2 ? 1 : 0;

                    // Odd tick: the merge of the piece (its id rewritten in the window), the drain (two Intake arrivals), the hook
                    arrivals = _r.Header(_r.Intake).NextArrivalSeq;
                    RetargetMerge();
                    _ = _r.Host.World.EnqueueOp(_r.PlayerA, in _merge);
                    _r.Host.World.Tick();
                    _totals.Drained += _r.Header(_r.Intake).NextArrivalSeq - arrivals == 2 && _inv.BacklogEntries == 0 ? 1 : 0;
                    _totals.CycleRestored += AtCycleStart ? 1 : 0;
                }
            }
            finally
            {
                _r.Hook.Action = null;
                _r.CaptureEvents = true;
            }

            return _totals;
        }

        private void Cycle(in TickContext ctx)
        {
            if ((ctx.Tick & 1) == 0)
            {
                Even(ctx.Tick);
            }
            else
            {
                Odd(ctx.Tick);
            }
        }

        private void Even(uint tick)
        {
            _one[0] = new MailId(_nextMail++);
            _totals.Appended += _inv.AppendBacklog(new BacklogEntry(_letter, Address4, 0, _one)) ? 1 : 0;
            for (int i = 0; i < 2; i++)
            {
                // The placing TryInsert allocated the newest entry id (as the first workload); it is thrown whole
                bool placed = _inv.TakeUnit(_intake, Selector.Fifo, ActorId.System, out UnitRef u) && _inv.TryInsert(_hotbarAId, in u, ActorId.System);
                _totals.Inserted += placed ? 1 : 0;
                EntryId single = EntryId.Real(_r.Entries.LastId);
                _totals.WaterThrown += placed && _inv.DetachForThrow(_slotA, single, 1).Reason == 0
                    && !_inv.CreateWorldItem(single, _pos, WorldItemOrigin.Thrown, true, tick).IsNone ? 1 : 0;
            }

            _totals.Inserted += _inv.TakeUnit(_intake, Selector.Fifo, ActorId.System, out UnitRef merge) && _inv.TryInsert(_inventoryAId, in merge, ActorId.System) ? 1 : 0;
            _totals.Consumed += _inv.ConsumeUnit(new EntryRef(_inventoryAId, _r.Entries[_r10].Id), ConsumeReason.Delivered, ActorId.System) ? 1 : 0;
        }

        private void Odd(uint tick)
        {
            var extra = default(InvReplyExtra);

            // TryCatch {3, 2}: 3 merge into X, 2 fumble; A picks the fumble up whole
            DetachY();
            CatchResult caught = _inv.TryCatch(_slotB, new FlightPayload(_yId, PickupRig.Feet, false));
            EntityId fumble = WorldItemOf(_y);
            _totals.Caught += caught == new CatchResult(3, 2, 0) && !fumble.IsNone ? 1 : 0;
            PickupResult whole = _inv.Pickup.Apply(_r.PlayerA, fumble, 0, ref extra);
            _totals.WholePickups += whole.IsTaken && whole.Got == 2 && Units(_y, _hotbarA) == 2 ? 1 : 0;
            GiveBack(_x, _y, 3);

            // A partial pickup: B takes 3 (X's room), A the 2 left whole
            DetachY();
            EntityId item = _inv.CreateWorldItem(_yId, _pos, WorldItemOrigin.Thrown, false, tick);
            PickupResult partial = _inv.Pickup.Apply(_r.PlayerB, item, 0, ref extra);
            _totals.PartialPickups += partial.IsTaken && partial.Got == 3 && Units(_y, EntryRecord.NoContainer) == 2 ? 1 : 0;
            PickupResult rest = _inv.Pickup.Apply(_r.PlayerA, item, _yId.Value, ref extra);
            _totals.WholePickups += rest.IsTaken && rest.Got == 2 && Units(_y, _hotbarA) == 2 ? 1 : 0;
            GiveBack(_x, _y, 3);
            GiveBack(_planksQ, _planksP, 4);

            // The leash put-away of the axe
            bool opened = _inv.TryOpenExternal(_slotA, _chest);
            bool picked = PickAxe();
            _inv.CloseExternal(_slotA, CloseCause.Leash);
            _totals.PutAway += opened && picked && At(_axe, _hotbarA, 1, 0) && _r.Header(_cursorA).EntryCount == 0 ? 1 : 0;
        }

        private void DetachY() => _ = _inv.DetachForThrow(_slotA, _yId, _r.Entries[_y].Count);

        /// <summary>Arrange inside the cycle: <paramref name="units"/> oldest units of one placed stack onto another (Placed → Placed).</summary>
        private void GiveBack(ushort from, ushort to, int units)
        {
            var plan = new InvPlan();
            _ = plan.Touch(_r.Entries[from].ContainerSlot);
            _ = plan.Touch(_r.Entries[to].ContainerSlot);
            var extra = default(InvReplyExtra);
            InventoryMutator m = _inv.Mutator;
            m.Begin(ref plan, ActorId.System, InvCause.None);
            m.MergeInto(ref plan, from, to, units);
            m.End(ref plan, ref extra);
        }

        /// <summary>Moves the axe from hotbar slot 1 onto A's cursor (one call scope and bracket, like story 007's <c>pick</c>).</summary>
        private bool PickAxe()
        {
            var plan = new InvPlan();
            _ = plan.Touch(_hotbarA);
            _ = plan.Touch(_cursorA);
            var extra = new InvReplyExtra();
            InventoryMutator m = _inv.Mutator;
            using (EventCallScope scope = _events.BeginCall())
            {
                m.Begin(ref plan, _r.PlayerA, InvCause.None);
                m.Transfer(ref plan, _axe, _cursorA, 0, 0, false);
                m.End(ref plan, ref extra);
            }

            return _r.Entries[_axe].ContainerSlot == _cursorA;
        }

        private void RetargetMerge()
        {
            ushort piece = _r.EntryAt(_cursorA, 0, 0);
            _merge.SetPayload(new MergePayload
            {
                From = _r.IdOf(_cursorA).Value,
                EntryId = piece == Occupancy.Free ? 0u : _r.Entries[piece].Id.Value,
                To = _r.IdOf(_inventoryA).Value,
                TargetEntryId = _r.Entries[_planksQ].Id.Value,
            });
        }

        private int PieceOnCursor()
        {
            ushort piece = _r.EntryAt(_cursorA, 0, 0);
            return piece == Occupancy.Free ? 0 : _r.Entries[piece].Count;
        }

        private EntityId WorldItemOf(ushort entry)
        {
            WorldItemTable table = _inv.WorldItems.Table;
            for (ushort s = table.FirstSlot; s != WorldItemTable.NoSlot; s = table[s].NextSlot)
            {
                if (table[s].EntrySlot == entry)
                {
                    return table[s].Id;
                }
            }

            return EntityId.None;
        }

        private bool At(ushort entry, ushort container, int x, int y)
        {
            EntryRecord e = _r.Entries[entry];
            return e.IsLive && e.ContainerSlot == container && e.X == x && e.Y == y;
        }

        private int Units(ushort entry, ushort container)
        {
            EntryRecord e = _r.Entries[entry];
            return e.IsLive && e.ContainerSlot == container ? e.Count : -1;
        }

        private Op PickOp(ushort from, ushort entry)
        {
            var op = new Op { Kind = InvOpKinds.Pick, ReqId = 80 };
            op.SetPayload(new PickPayload { From = _r.IdOf(from).Value, EntryId = _r.Entries[entry].Id.Value });
            return op;
        }

        private Op MoveOp(ushort from, ushort entry, ushort to, int x, int y, byte count = 0)
        {
            var op = new Op { Kind = InvOpKinds.Move, ReqId = 81 };
            op.SetPayload(new MovePayload
            {
                From = _r.IdOf(from).Value, EntryId = _r.Entries[entry].Id.Value, To = _r.IdOf(to).Value, BaseVersion = _r.Header(to).Version,
                X = (short)x, Y = (short)y, Count = count,
            });
            return op;
        }
    }
}
