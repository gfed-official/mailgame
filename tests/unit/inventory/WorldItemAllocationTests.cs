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
/// Story grid-inventory/013 (ADR-0009 Implementation Guidelines; ADR-0006 0 B per tick): the WorldItem tick paths allocate 0 B after
/// warm-up — <see cref="IInventoryApi.CreateWorldItem"/> with a cap eviction (a plank destroyed with <c>ItemsLost</c>), the
/// <c>ItemsExpire</c> phase returning water-landed letters to the Intake, the pickup hook (<see cref="WorldItems.Remove"/>), the
/// tombstone resolution and the FIFO pops and compactions behind them. Each tick, inside the rig's <c>MailStep</c> hook (a delegate
/// cached before the window): two planks and one water letter are created at the cap (512 old planks to start), one of the planks is
/// picked up at once and resolved (<c>container_gone</c>), and one unit is taken from the Intake so the returns keep finding room. The
/// 0 B assertion runs in optimised builds only (TD-008 convention); its twin runs the same window in every build and asserts the
/// results. Joins the non-parallel collection (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class WorldItemAllocationTests
{
    /// <summary>Ticks per measured window (past the 300-tick water float, so returns run in the window).</summary>
    private const int Ticks = 400;

    [Fact]
    [Trait("Id", "TR-inventory-013")]
    public void CreateEvictExpireReturnPickupAndResolve_Over400Ticks_AllocateNothing_InOptimisedBuilds()
    {
        // Arrange
        SkipWhenUnoptimised();
        var w = new Workload();
        Totals totals = default;

        // Act
        AllocAssert.Zero("CreateWorldItem x3 + cap eviction + ItemsExpire water returns + Remove + Resolve + TakeUnit x400 ticks", () => totals = w.Run(Ticks));

        // Assert
        Workload.AssertSteadyState(in totals, Ticks);
    }

    [Fact]
    [Trait("Id", "TR-inventory-013")]
    public void CreateEvictExpireReturnPickupAndResolve_TheMeasuredWindow_GivesTheExpectedResults_InEveryBuild()
    {
        // Arrange: the same workload as the 0 B test (skipped in unoptimised builds), run without measuring; the first window warms
        // the water float up (no return before tick 300 of it)
        var w = new Workload();

        // Act
        Totals first = w.Run(Ticks);
        Totals second = w.Run(Ticks);
        Totals third = w.Run(Ticks);

        // Assert
        Assert.Equal(3 * Ticks, first.Created);
        Assert.Equal(Ticks - 300, first.Returned);
        Assert.Equal(Ticks - 300, first.Taken);
        Workload.AssertSteadyState(in second, Ticks);
        Assert.Equal(second, third);
        Assert.Equal(511, w.LiveCount); // each tick: three creations at the cap (each evicting to 512), then the pickup leaves 511
    }

    private static void SkipWhenUnoptimised()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(InventorySystem).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
    }

    private record struct Totals(int Created, int PickedUp, int Gone, int Returned, int Taken);

    /// <summary>The arranged world and the measured window.</summary>
    private sealed class Workload
    {
        private static readonly Vec3Mm _pos = new(1_000, 0, 1_000);

        private readonly WorldItemRig _w = new();
        private readonly InventorySystem _inventory;
        private readonly WorldItems _world;
        private readonly EntryPool _entries;
        private readonly MailIdArena _mailIds;
        private readonly ContainerId _intake;
        private readonly ushort _plankDef;
        private readonly byte _plankW;
        private readonly byte _plankH;
        private readonly ushort _letterKind;
        private readonly byte _letterW;
        private readonly byte _letterH;
        private readonly uint[] _one = new uint[1];
        private readonly InvPhaseAction _step;
        private uint _nextMail = 100_000;
        private int _toRun;
        private Totals _totals;

        public Workload()
        {
            _ = _w.CreateOnePerTick(10, 512, _ => _w.R.DetachedItem("plank", 1));
            _inventory = _w.Inv;
            _world = _w.World;
            _entries = _w.R.Entries;
            _mailIds = _inventory.MailIds;
            _intake = _inventory.IntakeContainer;
            DefIndex plank = _w.R.ItemIndex("plank");
            _plankDef = plank.Value;
            _plankW = (byte)_w.R.Content.Items[plank].GridW;
            _plankH = (byte)_w.R.Content.Items[plank].GridH;
            DefIndex letter = _w.R.KindIndex("letter");
            _letterKind = letter.Value;
            _letterW = (byte)_w.R.Content.MailKinds[letter].GridW;
            _letterH = (byte)_w.R.Content.MailKinds[letter].GridH;
            _w.AdvanceTo(600); // every plank older than 10 s
            _step = Step;
        }

        public int LiveCount => _world.LiveCount;

        public static void AssertSteadyState(in Totals t, int ticks)
        {
            Assert.Equal(new Totals(3 * ticks, ticks, ticks, ticks, ticks), t);
        }

        public Totals Run(int ticks)
        {
            _totals = default;
            _toRun = ticks;
            _w.R.CaptureEvents = false;
            _w.R.Hook.Action = _step;
            uint arrivalsBefore = _w.R.Header(_w.R.Intake).NextArrivalSeq;
            try
            {
                while (_toRun > 0)
                {
                    _w.R.Host.World.Tick();
                }
            }
            finally
            {
                _w.R.Hook.Action = null;
            }

            _totals.Returned = (int)(_w.R.Header(_w.R.Intake).NextArrivalSeq - arrivalsBefore); // every Intake insertion is a return here
            return _totals;
        }

        private void Step(in TickContext ctx)
        {
            _toRun--;
            EntityId kept = _inventory.CreateWorldItem(NewPlank(ctx.Tick), _pos, WorldItemOrigin.Dropped, false, ctx.Tick);
            EntityId taken = _inventory.CreateWorldItem(NewPlank(ctx.Tick), _pos, WorldItemOrigin.Thrown, false, ctx.Tick);
            EntityId floating = _inventory.CreateWorldItem(NewLetter(ctx.Tick), _pos, WorldItemOrigin.Thrown, true, ctx.Tick);
            _totals.Created += (kept.IsNone ? 0 : 1) + (taken.IsNone ? 0 : 1) + (floating.IsNone ? 0 : 1);
            if (_world.Remove(taken, ActorId.System, InvCause.Taken, out ushort entry))
            {
                _totals.PickedUp++;
                _inventory.Mutator.DestroyDetached(entry); // the fixture's "picker" drops it on the floor of nowhere
            }

            _totals.Gone += _world.Resolve(taken, out _, out _, out InvCause cause) == (ushort)InvReason.ContainerGone && cause == InvCause.Taken ? 1 : 0;
            _totals.Taken += _inventory.TakeUnit(_intake, Selector.Fifo, ActorId.System, out _) ? 1 : 0; // the letter returned this tick
        }

        private EntryId NewPlank(uint tick)
        {
            if (!_entries.TryAllocate(tick, out ushort slot, out EntryId id))
            {
                return default;
            }

            ref EntryRecord e = ref _entries[slot];
            e.ItemId = _plankDef;
            e.W = _plankW;
            e.H = _plankH;
            return id;
        }

        private EntryId NewLetter(uint tick)
        {
            if (!_entries.TryAllocate(tick, out ushort slot, out EntryId id))
            {
                return default;
            }

            ref EntryRecord e = ref _entries[slot];
            _one[0] = _nextMail++;
            _ = _mailIds.TryCreate(_one, tick, out e.MailIds);
            e.IsMail = true;
            e.MailKind = _letterKind;
            e.AddressId = 4u;
            e.W = _letterW;
            e.H = _letterH;
            return id;
        }
    }
}
