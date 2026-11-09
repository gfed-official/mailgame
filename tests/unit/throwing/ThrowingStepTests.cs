using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory.Events;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Inventory.World;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// throwing-catching story 011 (ADR-0015 sections 4 and 5; GDD T5, T6 rows Solid, Terrain, Water, TIMEOUT): <c>Throwing.Step</c> in a real
/// world (<see cref="ThrowWorldRig"/> over <see cref="ThrowFixtureWorld"/>, or the M-1a street for mailboxes). Flights are injected
/// through the real <c>DetachForThrow</c> with chosen origins and velocities, or launched by a real <c>ThrowRelease</c>. Heights are
/// derived from <see cref="Ballistic.PositionAt"/> (the trajectory), never from the step under test. Greybox simplifications (simplify
/// table, tc-011): no walkable snap or search back along -v, no cap eviction run, no 0 B gate.
/// </summary>
public class ThrowingStepTests
{
    private static readonly Vec3Mm _still = default;

    // ---------------------------------------------------------------- Order and age

    [Fact]
    [Trait("Id", "TC-T4-1")]
    public void ThreeFlightsLaunchedOnDifferentTicks_AreResolvedInLaunchOrder_AndAge0IsNeverSwept()
    {
        // Arrange: A, B and C launched on t0, t0+1, t0+2 from heights that first reach the ground at ages 4, 3 and 2, so all land on t0+4
        var rig = new ThrowWorldRig(ThrowFixtureWorld.Build());
        EntryId a = rig.PlaceMail(0, 1, "letter", 1);
        EntryId b = rig.PlaceMail(0, 2, "letter", 2);
        EntryId c = rig.PlaceMail(0, 3, "letter", 3);

        // Act
        uint t0 = rig.Inject((a, 1, new Vec3Mm(14_000, LandsAtAge(rig, 4), 14_000), _still));
        rig.Inject((b, 1, new Vec3Mm(16_000, LandsAtAge(rig, 3), 14_000), _still));
        rig.Inject((c, 1, new Vec3Mm(18_000, LandsAtAge(rig, 2), 14_000), _still));
        int liveAfterC = rig.Flights.LiveFlights;
        FlightId[] ids = { rig.Flights.Id(0), rig.Flights.Id(1), rig.Flights.Id(2) };
        rig.TickEmpty(2);

        // Assert: C (age 0) was not swept on its launch tick; one tick, launch order
        Assert.Equal(3, liveAfterC);
        List<FlightResolved> resolved = rig.All<FlightResolved>(ThrowEventKinds.FlightResolved);
        Assert.Equal(ids.Select(i => i.Value), resolved.Select(r => r.FlightId));
        Assert.All(resolved, r => Assert.Equal((t0 + 4, Terminal.Ground, 0), (r.Tick, r.Terminal, r.ContactY)));
        Assert.Equal(0, rig.Flights.LiveFlights);
    }

    // ---------------------------------------------------------------- TC-T6-3: terrain, water, solids, the map side

    [Theory]
    [Trait("Id", "TC-T6-3")]
    [InlineData("terrain", 20_000, 2_000, 15_000, 0, Terminal.Ground, ReceiverKind.Ground, 20_000, 15_000, false)]
    [InlineData("water", 70_000, 2_000, 20_000, 0, Terminal.Water, ReceiverKind.Water, 70_000, 20_000, true)]
    [InlineData("house wall", 38_000, 2_000, 6_000, 15_000, Terminal.Ground, ReceiverKind.Solid, 40_000, 6_000, false)]
    [InlineData("roof", 42_000, 8_000, 6_000, 0, Terminal.Ground, ReceiverKind.Solid, 42_000, 6_000, false)]
    [InlineData("post office", 7_000, 8_000, 33_000, 0, Terminal.Ground, ReceiverKind.Solid, 7_000, 33_000, false)]
    [InlineData("map side (-X)", 1_500, 3_000, 15_000, -15_000, Terminal.Ground, ReceiverKind.Solid, 1_000, 15_000, false)]
    public void FirstContact_EndsInItsTerminal_OneThrownWorldItemOnTheSurface_RaisedBeforeItsSpawn(
        string receiver, int ox, int oy, int oz, int vx, Terminal terminal, ReceiverKind rankOf, int itemX, int itemZ, bool onWater)
    {
        // Arrange
        _ = receiver;
        var rig = new ThrowWorldRig(ThrowFixtureWorld.Build());
        EntryId entry = rig.PlaceMail(0, 1, "letter", 41);
        int intakeEntries = IntakeEntries(rig);

        // Act
        rig.Inject((entry, 1, new Vec3Mm(ox, oy, oz), new Vec3Mm(vx, 0, 0)));
        rig.TickEmpty(rig.Throwing.Settings.MaxFlightTicks);

        // Assert: one terminal; the item on the surface (y 0: dry ground, or the water plane), never on a roof or in the Intake
        EventEntry raised = Assert.Single(rig.Ticks.SelectMany(t => t), e => e.Kind == ThrowEventKinds.FlightResolved);
        FlightResolved r = raised.PayloadAs<FlightResolved>();
        Assert.Equal((terminal, new ReceiverRanks(rig.Content.Throw).Of(rankOf)), (r.Terminal, r.Rank));
        Assert.True(rig.Inventory.WorldItems.Table.TryGetSlot(new EntityId(r.WorldItem), out ushort slot));
        ref WorldItemRecord w = ref rig.Inventory.WorldItems.Table[slot];
        Assert.Equal((new Vec3Mm(itemX, 0, itemZ), onWater, WorldItemOrigin.Thrown, r.Tick), (w.PosMm, w.OnWater, w.Origin, w.CreatedTick));
        if (!onWater)
        {
            Assert.Equal(r.Tick + (300u * 30u), w.ExpiresTick); // Grid's 300 s from the landing tick
        }

        Assert.Equal(intakeEntries, IntakeEntries(rig));
        EventEntry[] landing = rig.Ticks.Single(t => t.Any(e => e.Kind == ThrowEventKinds.FlightResolved));
        Assert.True(raised.Ordinal < Assert.Single(landing, e => e.Kind == InvEventKinds.WorldItemSpawned).Ordinal);
        AssertDecodes(in raised);
    }

    [Theory]
    [Trait("Id", "TC-T6-3")]
    [InlineData(true)]
    [InlineData(false)]   // the axe: non-mail is destroyed
    public void WaterLanding_FloatsThroughTick299_AtTick300MailReturnsToTheBackOfTheIntake(bool mail)
    {
        // Arrange
        var rig = new ThrowWorldRig(ThrowFixtureWorld.Build());
        EntryId entry = mail ? rig.PlaceMail(0, 1, "letter", 51) : rig.PlaceItem(0, 1, "axe", 1);
        rig.Inject((entry, 1, new Vec3Mm(70_000, 500, 20_000), _still));
        rig.TickEmpty(rig.Throwing.Settings.MaxFlightTicks);
        FlightResolved r = Assert.Single(rig.All<FlightResolved>(ThrowEventKinds.FlightResolved));
        Assert.Equal(Terminal.Water, r.Terminal);

        // Act
        rig.TickTo(r.Tick + 300);
        bool floating = rig.Inventory.WorldItems.Table.TryGetSlot(new EntityId(r.WorldItem), out _);
        rig.TickEmpty(1);

        // Assert
        Assert.True(floating);
        Assert.False(rig.Inventory.WorldItems.Table.TryGetSlot(new EntityId(r.WorldItem), out _));
        bool live = rig.Inventory.Entries.TryGetSlot(entry, out ushort e);
        Assert.Equal(mail, live);
        if (mail)
        {
            Assert.True(rig.Inventory.Containers.TryGetSlot(rig.Inventory.IntakeContainer, out ushort intake));
            Assert.Equal(intake, rig.Inventory.Entries[e].ContainerSlot);
            Assert.Equal(IntakeEntrySlots(rig).Max(s => rig.Inventory.Entries[s].ArrivalSeq), rig.Inventory.Entries[e].ArrivalSeq);
        }
    }

    // ---------------------------------------------------------------- TC-T6-3: age 90

    [Theory]
    [Trait("Id", "TC-T6-3")]
    [InlineData("ground only on segment 89 -> 90", 20_000, 20_000, Terminal.Ground, ReceiverKind.Ground, false)]
    [InlineData("no contact through age 90, over ground", 20_000, 20_000, Terminal.Timeout, ReceiverKind.Ground, false)]
    [InlineData("no contact through age 90, over water", 70_000, 20_000, Terminal.Timeout, ReceiverKind.Water, true)]
    public void Age90_IsSwept_AndNoContactByThenIsTimeout_OnTheSurfaceBelowP90(string flight, int x, int z, Terminal terminal, ReceiverKind surface, bool onWater)
    {
        // Arrange
        var rig = new ThrowWorldRig(ThrowFixtureWorld.Build());
        int max = rig.Throwing.Settings.MaxFlightTicks;
        int oy = terminal == Terminal.Ground ? LandsAtAge(rig, max) : Drop(rig, max) + 10_000;
        EntryId entry = rig.PlaceMail(0, 1, "letter", 61);

        // Act
        uint launched = rig.Inject((entry, 1, new Vec3Mm(x, oy, z), _still));
        rig.TickEmpty(max);

        // Assert
        _ = flight;
        FlightResolved r = Assert.Single(rig.All<FlightResolved>(ThrowEventKinds.FlightResolved));
        Assert.Equal((terminal, launched + (uint)max, new ReceiverRanks(rig.Content.Throw).Of(surface)), (r.Terminal, r.Tick, r.Rank));
        Assert.True(rig.Inventory.WorldItems.Table.TryGetSlot(new EntityId(r.WorldItem), out ushort slot));
        Assert.Equal((new Vec3Mm(x, 0, z), onWater), (rig.Inventory.WorldItems.Table[slot].PosMm, rig.Inventory.WorldItems.Table[slot].OnWater));
    }

    // ---------------------------------------------------------------- TC-E-1 a (with story 010's chest launch and TC-T4-1 c)

    [Fact]
    [Trait("Id", "TC-E-1")]
    [Trait("Id", "TC-T4-1")]
    public void ThrowerFacingAWallAtArmsLength_LaunchesFromTheChestAtT_FirstSweepAtTPlus1_OneWorldItemWithin1mOfTheFeet()
    {
        // Arrange: feet 50 mm from the house's west wall, throwing +X: the hand origin is inside the wall, so the chest probe blocks
        var rig = new ThrowWorldRig(ThrowFixtureWorld.Build());
        var feet = new Vec3Mm(ThrowFixtureWorld.House.Min.X - 50, 0, 6_000);
        rig.Stand(0, feet);
        rig.Selection[0] = 1;
        EntryId entry = rig.PlaceMail(0, 1, "letter", 71);
        uint tat = rig.Throwing.Tat(ThrowWorldRig.Slot(0));

        // Act
        (Reply reply, EventEntry[] launchLog, uint t) = rig.Release(0, ThrowWorldRig.Payload(entry, 1, feet, 16_384, 0, 0));
        int liveAtT = rig.Flights.LiveFlights;
        rig.TickEmpty(1);

        // Assert: launched from feet + (0, 1000, 0) at T, nothing swept on T, resolved on T+1 at the wall, the item next to the feet
        Assert.True(reply.IsOk);
        Assert.DoesNotContain(launchLog, e => e.Kind == ThrowEventKinds.FlightResolved);
        Assert.Equal(1, liveAtT);
        ThrowLaunched l = Assert.Single(launchLog, e => e.Kind == ThrowEventKinds.ThrowLaunched).PayloadAs<ThrowLaunched>();
        Assert.Equal((feet.X, feet.Y + 1_000, feet.Z, t), (l.OriginX, l.OriginY, l.OriginZ, l.LaunchTick));
        FlightResolved r = Assert.Single(rig.All<FlightResolved>(ThrowEventKinds.FlightResolved));
        Assert.Equal((t + 1, Terminal.Ground, ThrowFixtureWorld.House.Min.X), (r.Tick, r.Terminal, r.ContactX));
        Assert.True(rig.Inventory.WorldItems.Table.TryGetSlot(new EntityId(r.WorldItem), out ushort slot));
        Vec3Mm at = rig.Inventory.WorldItems.Table[slot].PosMm;
        long dx = at.X - feet.X;
        long dz = at.Z - feet.Z;
        Assert.True((dx * dx) + (dz * dz) <= 1_000L * 1_000L);
        Assert.Equal(tat, rig.Throwing.Tat(ThrowWorldRig.Slot(0)));
    }

    // ---------------------------------------------------------------- Ports: a mailbox winner goes to IMailboxLanding

    [Theory]
    [Trait("Id", "TC-T6-3")]
    [InlineData(false)]   // production wiring: until story 012 the mailbox drops the item straight down, one terminal
    [InlineData(true)]    // a bound port receives the flight and owns its terminal
    public void MailboxVolumeWinner_IsDispatchedToTheMailboxPort_ExactlyOnce(bool bindDouble)
    {
        // Arrange: the M-1a street; a flight dropped onto the top of the first destination's catch volume (a descending entry)
        var port = new MailboxPortDouble();
        var rig = new ThrowWorldRig(Street(), mailbox: bindDouble ? port : null);
        port.Flights = rig.Flights;
        AabbMm box = rig.Mail.CatchVolumeAt(0);
        EntityId dest = rig.Mail.DestinationAt(0);
        var above = new Vec3Mm((box.Min.X + box.Max.X) / 2, box.Max.Y + 300, (box.Min.Z + box.Max.Z) / 2);
        EntryId entry = rig.PlaceMail(0, 1, "letter", 81);

        // Act
        rig.Inject((entry, 1, above, _still));
        rig.TickEmpty(rig.Throwing.Settings.MaxFlightTicks);

        // Assert
        Assert.Equal(0, rig.Flights.LiveFlights);
        Assert.Equal(1L, rig.Flights.Resolved);
        if (bindDouble)
        {
            Assert.Equal(new[] { dest.Value }, port.Destinations);
            Assert.Empty(rig.All<FlightResolved>(ThrowEventKinds.FlightResolved));
        }
        else
        {
            FlightResolved r = Assert.Single(rig.All<FlightResolved>(ThrowEventKinds.FlightResolved));
            Assert.Equal((dest.Value, new ReceiverRanks(rig.Content.Throw).Of(ReceiverKind.Mailbox)), (r.Receiver, r.Rank));
        }
    }

    // ---------------------------------------------------------------- Exactly one terminal, over a randomised run

    [Fact]
    [Trait("Id", "TR-throwing-013")]
    public void ThousandRandomFlightsOverTheStreet_EachResolvesOnce_ResolvedBeforeItsSpawn_TickEndRecordNeverFires()
    {
        // Arrange: seeded flights from random points above the street, random velocities; at most 7 placed per wave (hotbar 1 to 7)
        var rig = new ThrowWorldRig(Street());
        MapBounds bounds = rig.World.Bounds;
        ulong seed = 0x7C010_011UL;
        const int Total = 1_000;

        // Act
        for (int launched = 0; launched < Total;)
        {
            int wave = Math.Min(7, Total - launched);
            var flights = new (EntryId, byte, Vec3Mm, Vec3Mm)[wave];
            for (int i = 0; i < wave; i++)
            {
                EntryId e = rig.PlaceMail(0, i + 1, "letter", (uint)(1_000 + launched + i));
                long x = bounds.MinXMm + 2_000 + Next(ref seed, bounds.MaxXMm - bounds.MinXMm - 4_000);
                long z = bounds.MinZMm + 2_000 + Next(ref seed, bounds.MaxZMm - bounds.MinZMm - 4_000);
                int y = (int)Math.Max(rig.World.TerrainHeightMm(x, z), 0) + 200 + (int)Next(ref seed, 20_000);
                var v = new Vec3Mm((int)Next(ref seed, 30_000) - 15_000, (int)Next(ref seed, 20_000) - 5_000, (int)Next(ref seed, 30_000) - 15_000);
                flights[i] = (e, 1, new Vec3Mm((int)x, y, (int)z), v);
            }

            while (rig.Flights.LiveFlights + wave > rig.Flights.Capacity)
            {
                rig.TickEmpty(1);
            }

            rig.Inject(flights);
            launched += wave;
        }

        rig.TickEmpty(rig.Throwing.Settings.MaxFlightTicks + 1);

        // Assert
        Assert.Equal((1_000L, 1_000L, 0), (rig.Flights.Launched, rig.Flights.Resolved, rig.Flights.LiveFlights));
        Assert.False(rig.Flights.HasResolvedTwice);
        Assert.Equal(0L, rig.Sim.Counters[CounterId.ThrowingConservationMismatches]);
        Assert.Equal(0L, rig.Sim.Counters[CounterId.ThrowingLandingFaults]);
        foreach (EventEntry[] log in rig.Ticks)
        {
            EventKind[] pairs = log.Where(e => e.Kind is ThrowEventKinds.FlightResolved or InvEventKinds.WorldItemSpawned).Select(e => e.Kind).ToArray();
            for (int i = 0; i < pairs.Length; i++)
            {
                Assert.Equal(i % 2 == 0 ? ThrowEventKinds.FlightResolved : InvEventKinds.WorldItemSpawned, pairs[i]);
            }
        }

        Assert.Equal(Total, rig.All<FlightResolved>(ThrowEventKinds.FlightResolved).Count);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The drop below the origin after <paramref name="k"/> ticks of a still flight, mm (positive).</summary>
    private static int Drop(ThrowWorldRig rig, int k) => -Ballistic.PositionAt(new Launch(default, default, rig.Throwing.Settings.G), k).Y;

    /// <summary>An origin height over y = 0 ground that a still flight first reaches on the segment <c>k - 1 -&gt; k</c>.</summary>
    private static int LandsAtAge(ThrowWorldRig rig, int k) => (Drop(rig, k - 1) + Drop(rig, k)) / 2;

    private static long Next(ref ulong seed, long range)
    {
        seed += 0x9E37_79B9_7F4A_7C15UL;
        ulong z = seed;
        z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EBUL;
        return (long)((z ^ (z >> 31)) % (ulong)range);
    }

    private static WorldTables Street()
    {
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        return b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    private static IEnumerable<ushort> IntakeEntrySlots(ThrowWorldRig rig)
    {
        Assert.True(rig.Inventory.Containers.TryGetSlot(rig.Inventory.IntakeContainer, out ushort intake));
        for (int i = 0; i < rig.Inventory.Entries.Capacity; i++)
        {
            ushort s = (ushort)i;
            if (rig.Inventory.Entries[s].Count > 0 && rig.Inventory.Entries[s].ContainerSlot == intake)
            {
                yield return s;
            }
        }
    }

    private static int IntakeEntries(ThrowWorldRig rig) => IntakeEntrySlots(rig).Count();

    private static void AssertDecodes(in EventEntry raised)
    {
        var codec = new FlightResolvedCodec();
        Span<byte> wire = stackalloc byte[64];
        int n = codec.Encode(in raised, wire);
        EventEntry back = default;
        Assert.True(codec.TryDecode(wire[..n], ref back, out _));
        Assert.Equal(raised.PayloadAs<FlightResolved>(), back.PayloadAs<FlightResolved>());
    }

    /// <summary>A mailbox port that records the destination and resolves the flight (the terminal story 012 will own).</summary>
    private sealed class MailboxPortDouble : IMailboxLanding
    {
        public FlightTable Flights { get; set; } = null!;

        public List<uint> Destinations { get; } = new();

        public void Land(int index, in Contact contact, uint tick)
        {
            Destinations.Add(contact.ReceiverKey);
            Flights.Remove(index);
        }
    }
}
