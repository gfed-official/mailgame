using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ops;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Inventory;

/// <summary>
/// Story grid-inventory/014 AC 0 B (ADR-0009 Implementation Guidelines; ADR-0006 0 B per tick): <see cref="PickupCore"/> (whole,
/// partial, <c>container_gone</c>, <c>stale_entry</c>) and <see cref="IInventoryApi.TryCatch"/> (merge with a fumble, place) allocate 0 B
/// per call after warm-up. One cycle is one tick whose <c>MailStep</c> hook runs, for players A (slot 0: hotbar slot 1 a 17-letter #4
/// stack X, every other panel cell full) and B (slot 1: hotbar slot 1 a 5-letter #4 entry Y): B detaches Y and A catches it ({3, 2}:
/// 3 merge into X, 2 fumble as a WorldItem); B picks that WorldItem up whole; B names it again (<c>container_gone</c>) and names a
/// long-dead WorldItem (<c>stale_entry</c>); X gives 3 units back to Y (the mutator's <c>MergeInto</c>, arrange); B detaches Y and drops
/// it; A picks it up partly (3 in, 2 stay); B picks the rest up whole; X gives 3 back again; B detaches Y and catches it into its own
/// hotbar (a place). The cycle ends as it began. The 0 B assertion runs in optimised builds only (TD-008 convention); its twin runs the
/// same window in every build and asserts the results. Event capture is off in the window. Joins the non-parallel collection (see
/// <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class PickupCatchAllocationTests
{
    /// <summary>Cycles per measured window.</summary>
    private const int Cycles = 500;

    [Fact]
    [Trait("Id", "TR-inventory-013")]
    public void PickupAndCatch_Over500Cycles_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        Totals totals = default;

        // Act
        AllocAssert.Zero("catch {3,2} + whole + container_gone + stale_entry + partial + whole pickup + catch place x500 cycles", () => totals = w.Run(Cycles));

        // Assert
        Workload.AssertTotals(in totals, Cycles);
    }

    [Fact]
    [Trait("Id", "TR-inventory-013")]
    public void PickupAndCatch_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
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
        int CatchFumbles, int WholePickups, int Gone, int Stale, int PartialPickups, int CatchPlaces, int CycleRestored);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private static readonly PlayerSlot _slotA = PlayerSlot.FromValue(0);
        private static readonly PlayerSlot _slotB = PlayerSlot.FromValue(1);
        private static readonly Vec3Mm _feet = new(5_000, 0, 2_000);

        private readonly PickupRig _p = new();
        private readonly InventorySystem _inv;
        private readonly ushort _hotbarA;
        private readonly ushort _hotbarB;
        private readonly ushort _x;
        private readonly ushort _y;
        private readonly EntryId _yId;
        private readonly EntityId _dead;
        private readonly InvPhaseAction _cycle;
        private Totals _totals;

        public Workload()
        {
            _inv = _p.Inv;
            _hotbarA = _p.Hotbar(0);
            _hotbarB = _p.Hotbar(1);
            _x = _p.R.PlaceMail(_hotbarA, "letter", 1, 0, 4u, PickupRig.Ids(1, 17));
            _p.FillHotbar(0, from: 2);
            _p.FillInventory(0);
            _y = _p.R.PlaceMail(_hotbarB, "letter", 1, 0, 4u, PickupRig.Ids(101, 5));
            _yId = _p.R.Entries[_y].Id;

            // A long-dead WorldItem id: a plank B picked up (into B's inventory), its tombstone then aged out
            _dead = _p.Drop(_p.R.DetachedItem("plank", 1));
            Assert.True(InvAutoPlaceRig.ReplyOf(_p.Pickup(_p.B, _dead)).IsOk);
            _p.AdvanceTo(_p.W.Now + Tombstones.LifetimeTicks + 1);
            _cycle = Cycle;
        }

        /// <summary>The cycle's start state: X 17 on A's hotbar slot 1, Y 5 on B's hotbar slot 1, no WorldItem.</summary>
        public bool AtCycleStart =>
            Units(_x, _hotbarA) == 17 && Units(_y, _hotbarB) == 5 && _p.R.Entries[_y].X == 1 && _inv.WorldItems.LiveCount == 0;

        public static void AssertTotals(in Totals t, int cycles)
        {
            Assert.Equal(new Totals(cycles, 2 * cycles, cycles, cycles, cycles, cycles, cycles), t);
        }

        public Totals Run(int cycles)
        {
            _totals = default;
            _p.R.CaptureEvents = false;
            _p.R.Hook.Action = _cycle;
            try
            {
                for (int c = 0; c < cycles; c++)
                {
                    _p.R.Host.World.Tick();
                    _totals.CycleRestored += AtCycleStart ? 1 : 0;
                }
            }
            finally
            {
                _p.R.Hook.Action = null;
                _p.R.CaptureEvents = true;
            }

            return _totals;
        }

        private void Cycle(in TickContext ctx)
        {
            var extra = default(InvReplyExtra);

            // Catch with a merge and a fumble: {3, 2}
            DetachY();
            CatchResult caught = _inv.TryCatch(_slotA, new FlightPayload(_yId, _feet, false));
            EntityId fumble = WorldItemOf(_y);
            _totals.CatchFumbles += caught == new CatchResult(3, 2, 0) && !fumble.IsNone ? 1 : 0;

            // B picks the fumble up whole; then container_gone, then stale_entry
            PickupResult whole = _inv.Pickup.Apply(_p.B, fumble, 0, ref extra);
            _totals.WholePickups += whole.IsTaken && whole.Got == 2 && Units(_y, _hotbarB) == 2 ? 1 : 0;
            PickupResult gone = _inv.Pickup.Apply(_p.B, fumble, 0, ref extra);
            _totals.Gone += gone.Reason == (ushort)InvReason.ContainerGone && gone.ByActorId == _p.B.Value ? 1 : 0;
            PickupResult stale = _inv.Pickup.Apply(_p.B, _dead, 0, ref extra);
            _totals.Stale += stale.Reason == (ushort)InvReason.StaleEntry ? 1 : 0;
            GiveBack(ref extra);

            // A partial pickup (3 of 5), then B takes the 2 left whole
            DetachY();
            EntityId dropped = _inv.CreateWorldItem(_yId, _feet, WorldItemOrigin.Dropped, false, ctx.Tick);
            PickupResult partial = _inv.Pickup.Apply(_p.A, dropped, 0, ref extra);
            _totals.PartialPickups += partial.IsTaken && partial.Got == 3 && Units(_y, EntryRecord.NoContainer) == 2 ? 1 : 0;
            PickupResult rest = _inv.Pickup.Apply(_p.B, dropped, _yId.Value, ref extra);
            _totals.WholePickups += rest.IsTaken && rest.Got == 2 ? 1 : 0;
            GiveBack(ref extra);

            // A catch that places the entry itself
            DetachY();
            CatchResult placed = _inv.TryCatch(_slotB, new FlightPayload(_yId, _feet, false));
            _totals.CatchPlaces += placed == new CatchResult(5, 0, 0) && Units(_y, _hotbarB) == 5 ? 1 : 0;
        }

        private void DetachY() => _ = _inv.DetachForThrow(_slotB, _yId, _p.R.Entries[_y].Count);

        /// <summary>Arrange inside the cycle: the 3 oldest units of X back onto Y through the shared mutator primitive.</summary>
        private void GiveBack(ref InvReplyExtra extra)
        {
            var plan = new InvPlan();
            _ = plan.Touch(_hotbarA);
            _ = plan.Touch(_hotbarB);
            InventoryMutator m = _inv.Mutator;
            m.Begin(ref plan, ActorId.System, InvCause.None);
            m.MergeInto(ref plan, _x, _y, 3);
            m.End(ref plan, ref extra);
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

        private int Units(ushort entry, ushort container)
        {
            EntryRecord e = _p.R.Entries[entry];
            return e.IsLive && e.ContainerSlot == container ? e.Count : -1;
        }
    }
}
