using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Ledger;
using PerformativeMail.Sim.Inventory.Placement;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Integration.MailDestinations;
using PerformativeMail.Sim.Tests.Unit.ThrowingCatching;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.ThrowingCatching;

/// <summary>
/// throwing-catching story 012 (ADR-0015 section 5 <c>MAILBOX_*</c> rows and F8; ADR-0017 section 5 throw row; GDD T6 mailbox rows, T11):
/// the shipped <see cref="MailboxLanding"/> in a real world (<see cref="ThrowWorldRig"/> on the M-1a street) with the real Mail system and
/// the FX-W recording wallet. Flights carry real mail rows and are launched through the real <c>DetachForThrow</c>. Mail's own rules
/// (the closing-grace boundary T+90, the floor-stop rate limit, hand → landing phase order) are asserted at Mail's layer
/// (<c>ThrownLandingTests</c>, <c>ConsumeTickOrderTests</c>); here: the terminal, the bounce WorldItem, conservation and event order.
/// Greybox (simplify table, tc-012): F8 vector a only (<see cref="DropPointTests"/>), no walkable snap.
/// </summary>
public class MailboxLandingTests
{
    private const int Door = 0;          // registry row 0 (lot 1), the right house of every stack below
    private const int WrongHouse = 1;    // registry row 1 (lot 2)
    private const ushort Spawn = 12;     // a 12 ¢ letter: pays 12, misdelivers for 6
    private static readonly PlayerSlot _thrower = ThrowWorldRig.Slot(0);

    // ---------------------------------------------------------------- TC-X-2 a/b, TC-E-3 a, TC-T6-2 b; conservation; event order

    [Theory]
    [Trait("Id", "TC-X-2")]
    [Trait("Id", "TC-E-3")]
    [Trait("Id", "TC-T6-2b")]
    [InlineData("accept", 0, Door, "open", true, Terminal.MailboxAccept, 20, 240, 20)]
    [InlineData("misdeliver to the floor", -440, WrongHouse, "open", true, Terminal.MailboxMisdeliver, 10, -500, 11)]
    [InlineData("misdeliver at the floor", -500, WrongHouse, "open", true, Terminal.MailboxMisdeliver, 0, -500, 1)]
    [InlineData("reject: closed", 0, Door, "closed", true, Terminal.MailboxReject, 0, 0, 0)]
    [InlineData("reject: locked", 0, Door, "locked", true, Terminal.MailboxReject, 0, 0, 0)]
    [InlineData("reject: non-mail", 0, Door, "open", false, Terminal.MailboxReject, 0, 0, 0)]
    public void MailboxLanding_EndsInMailsTerminal_TheRestBouncesAsOneThrownWorldItemOutsideTheVolume_UnitsConserved_FlightResolvedFirst(
        string row, long wallet, int address, string door, bool mail, Terminal terminal, int consumed, long walletAfter, int walletCalls)
    {
        // Arrange: FX-L20 (or one axe) dropped onto the top of door 0's catch volume, moving −Z
        _ = row;
        var rig = new MailRig(wallet);
        rig.Door(door);
        int units = mail ? 20 : 1;
        AabbMm box = rig.Box(Door);
        EntryId entry = rig.Fly(mail ? address : -1, units, Above(box), new Vec3Mm(0, 0, -3_000));

        // Act
        EventEntry[] log = rig.Land();

        // Assert: one terminal, Mail's; receiver = the destination, catch-only fields 0
        FlightResolved r = Assert.Single(log, e => e.Kind == ThrowEventKinds.FlightResolved).PayloadAs<FlightResolved>();
        Assert.Equal((terminal, rig.Throw.Mail.DestinationAt(Door).Value, (byte)0, (byte)0), (r.Terminal, r.Receiver, r.UnitsIn, r.UnitsFumbled));
        Assert.Equal((walletAfter, walletCalls), (rig.Wallet.Balance, rig.Wallet.Calls));

        // The rest is one Thrown WorldItem created on the landing tick, on the surface, at least 600 mm outside the volume centre on
        // the side the flight came from; nothing for an Accept.
        int bounced = units - consumed;
        WorldItemSpawnedPayload[] spawned = log.Where(e => e.Kind == InvEventKinds.WorldItemSpawned).Select(e => e.PayloadAs<WorldItemSpawnedPayload>()).ToArray();
        Assert.Equal(bounced > 0 ? 1 : 0, spawned.Length);
        if (bounced > 0)
        {
            WorldItemSpawnedPayload w = spawned[0];
            Assert.True(rig.Throw.Inventory.WorldItems.Table.TryGetSlot(new EntityId(w.WorldItemId), out ushort slot));
            Assert.Equal(WorldItemOrigin.Thrown, rig.Throw.Inventory.WorldItems.Table[slot].Origin);
            Assert.Equal((r.Tick, entry.Value, r.ContactX), (w.CreatedTick, w.EntryId, w.X));
            Assert.Equal(rig.Throw.World.TerrainHeightMm(w.X, w.Z), w.Y);
            Assert.InRange(w.Z - ((box.Min.Z + box.Max.Z) / 2), DropPoint.ClearMm, DropPoint.ClearMm + DropPoint.StepMm - 1);
            Assert.Equal(r.Terminal == Terminal.MailboxReject ? w.WorldItemId : 0u, r.WorldItem);
        }

        // A Reject touches neither the wallet nor the complaint buckets; no landing is ever cursed.
        if (terminal == Terminal.MailboxReject)
        {
            Assert.DoesNotContain(log, e => e.Kind == MailEventKinds.ComplaintRaised || e.Kind == MailEventKinds.DeliveryOutcome);
        }

        Assert.All(log.Where(e => e.Kind == MailEventKinds.DeliveryOutcome).Select(e => e.PayloadAs<DeliveryOutcomePayload>()), o =>
        {
            Assert.Equal((ushort)consumed, o.Units);
            Assert.Equal((byte)0, o.Actor);
            Assert.Equal(0, o.Flags & DeliveryOutcomePayload.FlagCursed);
        });

        // Conservation: consumed + in the WorldItem = N; the Flight column is back to 0; both tick-end checks stay green.
        int row0 = mail ? UnitLedger.MailRow : rig.Throw.ItemOf("axe").Value + 1;
        UnitLedger ledger = rig.Throw.Inventory.Ledger;
        Assert.Equal((0L, (long)bounced, (long)consumed), (ledger.Live(row0, LedgerState.Flight), ledger.Live(row0, LedgerState.World), ledger.Consumed(row0)));
        rig.AssertChecksGreen();

        // Event order: FlightResolved first, then every event the landing causes, as one contiguous run of the log.
        AssertOneRunFromFlightResolved(log);
    }

    // ---------------------------------------------------------------- TC-E-3 b: two Misdelivers into one wallet in one tick

    [Fact]
    [Trait("Id", "TC-E-3")]
    public void TwoMisdeliversIntoOneWalletInOneTick_ResolveInCreationOrder_TheSecondConsumesNothing_AndBouncesWhole()
    {
        // Arrange: at −494 a 1-letter stack (−6 ¢) reaches the floor; the 3-letter stack launched after it lands on the same tick.
        var rig = new MailRig(-494);
        rig.Door("open");
        Vec3Mm above = Above(rig.Box(Door));
        var vel = new Vec3Mm(0, 0, -3_000);
        (EntryId first, EntryId second) = rig.FlyTwo((WrongHouse, 1, above, vel), (WrongHouse, 3, above, vel));

        // Act
        EventEntry[] log = rig.Land();

        // Assert
        FlightResolved[] resolved = log.Where(e => e.Kind == ThrowEventKinds.FlightResolved).Select(e => e.PayloadAs<FlightResolved>()).ToArray();
        Assert.Equal(new[] { Terminal.MailboxMisdeliver, Terminal.MailboxMisdeliver }, resolved.Select(r => r.Terminal));
        Assert.True(resolved[0].FlightId != resolved[1].FlightId);
        Assert.Equal(-500, rig.Wallet.Balance);
        Assert.False(rig.Throw.Inventory.TryReadEntry(new EntryRef(ContainerId.None, first), out _));   // consumed whole
        WorldItemSpawnedPayload w = Assert.Single(log, e => e.Kind == InvEventKinds.WorldItemSpawned).PayloadAs<WorldItemSpawnedPayload>();
        Assert.Equal(second.Value, w.EntryId);
        Assert.Equal(3L, rig.Throw.Inventory.Ledger.Live(UnitLedger.MailRow, LedgerState.World));
        int firstResolved = Array.FindIndex(log, e => e.Kind == ThrowEventKinds.FlightResolved);
        int secondResolved = Array.FindLastIndex(log, e => e.Kind == ThrowEventKinds.FlightResolved);
        Assert.True(Array.FindIndex(log, e => e.Kind == MailEventKinds.MailConsumed) < secondResolved);
        Assert.True(firstResolved < secondResolved);
        rig.AssertChecksGreen();
    }

    // ---------------------------------------------------------------- TC-X-2 c / TC-T11-1: judged by releaseTick

    [Theory]
    [Trait("Id", "TC-X-2")]
    [Trait("Id", "TC-T11-1")]
    [InlineData(-1, Terminal.MailboxAccept)]   // released at T − 1, lands after T: OPEN
    [InlineData(0, Terminal.MailboxReject)]    // released at T: closed
    public void MailboxLanding_AfterDeliveriesClose_IsJudgedByTheFlightsReleaseTick_NeverItsLandingTick(int releaseOffset, Terminal terminal)
    {
        // Arrange: one right-house letter launched on T − 1; deliveries close at T, before it lands. The release tick is injected.
        var rig = new MailRig(0);
        rig.Door("open");
        uint t = rig.Throw.Sim.CurrentTick + 1;
        rig.Fly(Door, 1, Above(rig.Box(Door)), new Vec3Mm(0, 0, -3_000), releaseTick: (uint)(t + releaseOffset));
        rig.Throw.Mail.Registry.Deliveries.Set(false, t, DeliveriesState.DefaultUnlockedMask);

        // Act
        EventEntry[] log = rig.Land();

        // Assert
        FlightResolved r = Assert.Single(log, e => e.Kind == ThrowEventKinds.FlightResolved).PayloadAs<FlightResolved>();
        Assert.True(r.Tick > t);
        Assert.Equal(terminal, r.Terminal);
    }

    // ---------------------------------------------------------------- Descending only, end to end

    [Fact]
    [Trait("Id", "TR-throwing-006")]
    public void FlatLobOverANearerMailbox_RisesThroughIt_AndLandsInTheFartherOne()
    {
        // Arrange: lot 1 and lot 3 face the street side by side (registry rows 0 and 2). Launched just before lot 1's volume at its
        // height, rising, the lob comes back to that height at lot 3's volume centre.
        var rig = new MailRig(0);
        AabbMm near = rig.Box(0);
        AabbMm far = rig.Box(2);
        var origin = new Vec3Mm(near.Min.X - 200, near.Max.Y - 100, (near.Min.Z + near.Max.Z) / 2);
        const int Vx = 12_000;
        int ticks = (int)Throw.RH(((far.Min.X + far.Max.X) / 2 - origin.X) * 30L, Vx);
        var vel = new Vec3Mm(Vx, (int)Throw.RH((long)rig.Throw.Throwing.Settings.G * ticks, 60), 0);
        var launch = new Launch(origin, vel, rig.Throw.Throwing.Settings.G);
        Assert.True(Ballistic.PositionAt(in launch, 4).X > near.Max.X && Ballistic.PositionAt(in launch, 4).Y > origin.Y);   // rose through it
        rig.Fly(Door, 1, origin, vel);

        // Act
        EventEntry[] log = rig.Land();

        // Assert
        FlightResolved r = Assert.Single(log, e => e.Kind == ThrowEventKinds.FlightResolved).PayloadAs<FlightResolved>();
        Assert.Equal(rig.Throw.Mail.DestinationAt(2).Value, r.Receiver);
        Assert.InRange(r.ContactX, far.Min.X, far.Max.X);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>300 mm above the volume's top, 300 mm toward +Z of its centre: a −Z flight at 3 m/s drops onto its top face.</summary>
    private static Vec3Mm Above(in AabbMm box) => new((box.Min.X + box.Max.X) / 2, box.Max.Y + 300, ((box.Min.Z + box.Max.Z) / 2) + 300);

    private static void AssertOneRunFromFlightResolved(EventEntry[] log)
    {
        EventKind[] caused = { MailEventKinds.MailConsumed, MailEventKinds.ComplaintRaised, MailEventKinds.DeliveryOutcome, InvEventKinds.WorldItemSpawned };
        int start = Array.FindIndex(log, e => e.Kind == ThrowEventKinds.FlightResolved);
        int run = log.Skip(start + 1).TakeWhile(e => caused.Contains(e.Kind)).Count();
        Assert.Equal(log.Count(e => caused.Contains(e.Kind)), run);
        Assert.True(log.Take(start).All(e => !caused.Contains(e.Kind)));
    }

    /// <summary>A <see cref="ThrowWorldRig"/> on the M-1a street with the FX-W wallet, launching flights that hold real mail.</summary>
    private sealed class MailRig
    {
        public MailRig(long balance)
        {
            Wallet = new RecordingWallet { Balance = balance };
            var b = new WorldTablesBuilder();
            M1aStreet.Author(b);
            Throw = new ThrowWorldRig(b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm), wallet: Wallet);
        }

        public ThrowWorldRig Throw { get; }

        public RecordingWallet Wallet { get; }

        public AabbMm Box(int ordinal) => Throw.Mail.CatchVolumeAt(ordinal);

        public void Door(string state) => Throw.Mail.Registry.Deliveries.Set(state != "closed", 0, state == "locked" ? 0UL : DeliveriesState.DefaultUnlockedMask);

        /// <summary>In one tick: a stack of <paramref name="units"/> (letters for <paramref name="ordinal"/>, or axes for −1) launched.</summary>
        public EntryId Fly(int ordinal, int units, Vec3Mm origin, Vec3Mm vel, uint? releaseTick = null)
        {
            EntryId held = ordinal < 0 ? Throw.PlaceItem(0, 1, "axe", (byte)units) : default;
            EntryId flying = default;
            uint launch = Throw.Sim.CurrentTick;
            Throw.InPhase(() => flying = Launch(ordinal < 0 ? held : Give(ordinal, units), units, origin, vel, launch, releaseTick ?? launch));
            return flying;
        }

        public (EntryId First, EntryId Second) FlyTwo(params (int Ordinal, int Units, Vec3Mm Origin, Vec3Mm Vel)[] flights)
        {
            var ids = new EntryId[2];
            uint launch = Throw.Sim.CurrentTick;
            Throw.InPhase(() =>
            {
                for (int i = 0; i < 2; i++)
                {
                    (int o, int u, Vec3Mm p, Vec3Mm v) = flights[i];
                    ids[i] = Launch(Give(o, u), u, p, v, launch, launch);
                }
            });
            return (ids[0], ids[1]);
        }

        /// <summary>Ticks until no flight is live; returns the log of the tick the last one resolved on.</summary>
        public EventEntry[] Land()
        {
            for (int i = 0; i <= Throw.Throwing.Settings.MaxFlightTicks && Throw.Flights.LiveFlights > 0; i++)
            {
                Throw.TickEmpty(1);
            }

            Assert.Equal(0, Throw.Flights.LiveFlights);
            return Throw.Ticks.Last(t => t.Any(e => e.Kind == ThrowEventKinds.FlightResolved));
        }

        public void AssertChecksGreen()
        {
            Throw.TickEmpty(1);
            Assert.Equal(
                (0L, 0L, 0L),
                (Throw.Sim.Counters[CounterId.ThrowingLandingFaults], Throw.Sim.Counters[CounterId.ThrowingConservationMismatches],
                    Throw.Sim.Counters[CounterId.InventoryConservationMismatches]));
        }

        private EntryId Give(int ordinal, int units)
        {
            InventorySystem inv = Throw.Inventory;
            DefIndex letter = Throw.KindOf("letter");
            AddressId address = Throw.Mail.Registry.AddressOf(new DestinationOrdinal((ushort)ordinal));
            ContainerId hotbar = inv.HotbarOf(_thrower);
            EntryId entry = default;
            for (int i = 0; i < units; i++)
            {
                MailId id = Throw.Mail.Table.Create(letter, address, Spawn, (byte)Throw.Mail.CurrentShift, MailFlags.None);
                Assert.Equal(1, inv.Spawn(inv.IntakeContainer, letter, address.Value, 0, new[] { id }, MergePolicy.NoMerge).Placed);
                Assert.True(inv.TakeUnit(inv.IntakeContainer, Selector.Fifo, ActorId.System, out UnitRef u));
                Assert.True(inv.TryInsert(hotbar, in u, Throw.Actor(0)));
                entry = i == 0 ? EntryId.Real(inv.Entries.LastId) : entry;
            }

            return entry;
        }

        private EntryId Launch(EntryId held, int units, Vec3Mm origin, Vec3Mm vel, uint launch, uint release)
        {
            DetachResult d = Throw.Inventory.DetachForThrow(_thrower, held, (byte)units);
            Assert.Equal(0, (int)d.Reason);
            var init = new FlightInit
            {
                Entry = d.Entry, Units = (byte)units, Thrower = _thrower, Origin = origin, Vel = vel,
                LaunchTick = launch, ReleaseTick = release, ThrownPm = 1000,
            };
            Assert.True(Throw.Flights.TryAppend(in init, out _));
            return d.Entry;
        }
    }
}
