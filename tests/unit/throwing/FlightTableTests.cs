using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.PlayerCharacter;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing story 008 (ADR-0015 sections 5 and 9): the <see cref="FlightTable"/> stores every <c>FlightRecord</c> field and holds 160
/// flights, keeps creation order through a seeded churn and a <c>FlightId</c> wrap (GDD TC-E-6 c), and the tick-end record fires on a
/// flight resolved twice and never in a normal run, the wrap included (a reused id is a new flight, not a second resolve).
/// </summary>
public class FlightTableTests
{
    [Fact]
    [Trait("Id", "TR-throwing-009")]
    [Trait("Id", "TR-throwing-008")]
    public void TryAppend_160FlightsStoreEveryField_The161stIsRefusedAndCounted()
    {
        // Arrange: the shipped composition, so the capacity is the system's own ThrowSettings.MaxFlights.
        var rig = PlayersRig.Create();
        ThrowingSystem throwing = Throwing(rig);
        FlightTable flights = throwing.Flights;

        // Act
        bool[] stored = new bool[160];
        int[] indices = new int[160];
        for (int i = 0; i < 160; i++)
        {
            FlightInit init = Init(i);
            stored[i] = flights.TryAppend(in init, out indices[i]);
        }

        FlightInit extra = Init(160);
        bool refused = !flights.TryAppend(in extra, out int extraIndex);

        // Assert
        Assert.DoesNotContain(false, stored);
        Assert.Equal(Enumerable.Range(0, 160), indices);
        Assert.True(refused);
        Assert.Equal(-1, extraIndex);
        Assert.Equal(1L, rig.Sim.Counters[CounterId.ThrowingFlightCapRefusals]);
        Assert.Equal(160, throwing.LiveFlights);
        for (int i = 0; i < 160; i++)
        {
            FlightInit e = Init(i);
            Assert.Equal(new FlightId((ushort)i), flights.Id(i));
            Assert.Equal(e.Entry, flights.Entry(i));
            Assert.Equal(e.Units, flights.Units(i));
            Assert.Equal(e.KindDef, flights.KindDef(i));
            Assert.Equal(e.Thrower, flights.Thrower(i));
            Assert.Equal(e.ClassPts, flights.ClassPts(i));
            Assert.Equal(e.Origin, flights.Origin(i));
            Assert.Equal(e.Vel, flights.Vel(i));
            Assert.Equal(e.LaunchTick, flights.LaunchTick(i));
            Assert.Equal(e.ThrownPm, flights.ThrownPm(i));
            Assert.Equal(e.ReleaseTick, flights.ReleaseTick(i));
            Assert.Equal(e.OpenAtRelease, flights.OpenAtRelease(i));
            Assert.Equal(e.Hop, flights.Hop(i));
            Assert.Equal(e.OriginClamped, flights.OriginClamped(i));
        }
    }

    [Fact]
    [Trait("Id", "TR-throwing-006")]
    [Trait("Id", "TR-throwing-013")]
    public void SeededChurnAcrossCapacity_KeepsCreationOrder_AndNeverFiresTheTickEndCheck()
    {
        // Arrange: steps alternate between filling (appends outpace removes) and draining, 200 steps each.
        var counters = new SimCounters();
        var ring = new SimCheckRing(rateLimitTicks: 0);
        var flights = new FlightTable(160, counters);
        var check = new ThrowingTickEndCheck(flights, ring, counters);
        var rng = new Pcg32(0x7C08_F11EUL, 8UL);
        var expected = new List<FlightId>();
        int timesFull = 0;
        bool wasFull = false;

        // Act
        for (uint tick = 0; tick < 2000; tick++)
        {
            bool filling = (tick / 200) % 2 == 0;
            int appends = (int)(rng.Next32() % (filling ? 12u : 4u));
            int removes = (int)(rng.Next32() % (filling ? 4u : 12u));
            for (int a = 0; a < appends; a++)
            {
                FlightInit init = Init((int)tick);
                if (flights.TryAppend(in init, out int index))
                {
                    expected.Add(flights.Id(index));
                }
                else
                {
                    Assert.Equal(160, expected.Count);
                }
            }

            for (int r = 0; r < removes && flights.LiveFlights > 0; r++)
            {
                int index;
                do
                {
                    index = (int)(rng.Next32() % (uint)flights.Count);
                }
                while (flights.IsDead(index));

                expected.Remove(flights.Id(index));
                flights.Remove(index);
            }

            flights.Compact();
            check.Check(tick);

            bool full = flights.LiveFlights == 160;
            timesFull += full && !wasFull ? 1 : 0;
            wasFull = full;
            Assert.Equal(expected, Ids(flights));
        }

        // Assert
        Assert.True(timesFull >= 3, $"reached capacity {timesFull} times");
        Assert.Equal(0L, counters[CounterId.ThrowingConservationMismatches]);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    [Trait("Id", "TC-E-6c")]
    public void FlightIdWrap_70000Launches_EachResolvedSoonAfter_StayInCreationOrder()
    {
        // Arrange
        var counters = new SimCounters();
        var ring = new SimCheckRing(rateLimitTicks: 0);
        var flights = new FlightTable(160, counters);
        var check = new ThrowingTickEndCheck(flights, ring, counters);
        var expected = new List<FlightId>();
        bool sawWrapInTable = false;

        // Act: one launch per step; past 4 live, the second-oldest resolves (a middle removal), then the oldest every other step.
        for (int n = 0; n < 70_000; n++)
        {
            FlightInit init = Init(n);
            Assert.True(flights.TryAppend(in init, out _));
            expected.Add(new FlightId(unchecked((ushort)n)));
            if (flights.LiveFlights > 4)
            {
                expected.RemoveAt(1);
                flights.Remove(1);
                if ((n & 1) == 0)
                {
                    expected.RemoveAt(0);
                    flights.Remove(0);
                }
            }

            flights.Compact();
            check.Check((uint)n);
            List<FlightId> ids = Ids(flights);
            Assert.Equal(expected, ids);
            sawWrapInTable |= ids.Contains(new FlightId(ushort.MaxValue)) && ids.Contains(new FlightId(0));
        }

        // Assert: 70 000 launches wrapped the u16 serial, a live set straddling the wrap kept 65 535 ahead of 0, and no reused id
        // (ids 0 to 4463 each served two flights) was ever taken for a second resolve: the tick-end check never fired.
        Assert.Equal(unchecked((ushort)70_000), flights.NextFlightId);
        Assert.True(sawWrapInTable);
        Assert.False(flights.HasResolvedTwice);
        Assert.Equal(0L, counters[CounterId.ThrowingConservationMismatches]);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    [Trait("Id", "TR-throwing-013")]
    public void AFlightResolvedTwice_FiresTheTickEndCheck_CounterAndRingEntry()
    {
        // Arrange: two flights (ids 0 and 1); flight 1 is resolved twice through the internal table.
        var rig = PlayersRig.Create();
        FlightTable flights = Throwing(rig).Flights;
        FlightInit a = Init(0);
        FlightInit b = Init(1);
        Assert.True(flights.TryAppend(in a, out _));
        Assert.True(flights.TryAppend(in b, out _));
        flights.Remove(1);
        flights.Remove(1);

        // Act
        rig.TickEmpty(1);

        // Assert: launched 2 - resolved 2 - live 1 = -1; b = the twice-resolved FlightId.
        Assert.Equal(1L, rig.Sim.Counters[CounterId.ThrowingConservationMismatches]);
        SimCheckRecord record = Assert.Single(Records(rig.Sim.CheckRing, CheckCode.ThrowingConservationMismatch));
        Assert.Equal((-1L, 1L, rig.Sim.CurrentTick - 1), (record.A, record.B, record.Tick));
    }

    [Fact]
    [Trait("Id", "TR-throwing-013")]
    public void AFlightDroppedWithoutResolving_FiresTheTickEndCheck_CounterAndRingEntry()
    {
        // Arrange: two flights (ids 0 and 1); flight 1 leaves the live list through the internal table without being resolved.
        var rig = PlayersRig.Create();
        FlightTable flights = Throwing(rig).Flights;
        FlightInit a = Init(0);
        FlightInit b = Init(1);
        Assert.True(flights.TryAppend(in a, out _));
        Assert.True(flights.TryAppend(in b, out _));
        flights.DropForTests(1);

        // Act
        rig.TickEmpty(1);

        // Assert: launched 2 - resolved 0 - live 1 = 1; no flight resolved twice, so b = 0.
        Assert.Equal(1L, rig.Sim.Counters[CounterId.ThrowingConservationMismatches]);
        SimCheckRecord record = Assert.Single(Records(rig.Sim.CheckRing, CheckCode.ThrowingConservationMismatch));
        Assert.Equal((1L, 0L, rig.Sim.CurrentTick - 1), (record.A, record.B, record.Tick));
    }

    // ---------------------------------------------------------------- helpers

    internal static FlightInit Init(int i) => new()
    {
        Entry = EntryId.Real((uint)(1000 + i)),
        Units = (byte)(1 + (i & 0x3F)),
        KindDef = (ushort)(2000 + i),
        Thrower = PlayerSlot.FromValue((byte)(i & 7)),
        ClassPts = 1 + (i & 7),
        Origin = new Vec3Mm(10_000 + i, 1200 - i, -3 * i),
        Vel = new Vec3Mm(-i, 3406 + i, -16_026 + i),
        LaunchTick = (uint)(500 + i),
        ThrownPm = 1000 + (i & 0x7F),
        ReleaseTick = (uint)(499 + i),
        OpenAtRelease = (i & 1) == 0,
        Hop = (byte)(i & 3),
        OriginClamped = (i & 2) != 0,
    };

    private static ThrowingSystem Throwing(PlayersRig rig) => rig.Host.Systems.OfType<ThrowingSystem>().Single();

    private static List<FlightId> Ids(FlightTable flights)
    {
        var ids = new List<FlightId>(flights.Count);
        for (int i = 0; i < flights.Count; i++)
        {
            Assert.False(flights.IsDead(i));
            ids.Add(flights.Id(i));
        }

        return ids;
    }

    private static List<SimCheckRecord> Records(SimCheckRing ring, CheckCode code)
    {
        var records = new List<SimCheckRecord>();
        for (int i = 0; i < ring.Count; i++)
        {
            if (ring[i].Check == code)
            {
                records.Add(ring[i]);
            }
        }

        return records;
    }
}

/// <summary>
/// Throwing story 008, "0 B" (ADR-0006, TR-throwing-008): after construction a churn of append / middle-remove / compact cycles over a
/// full table allocates nothing. Reported, not blocking, until M0 (ADR-0015 erratum E2).
/// </summary>
[Collection(NonParallelCollection.Name)]
public sealed class FlightTableAllocationTests
{
    [Fact]
    [Trait("Id", "TR-throwing-008")]
    public void ChurnOfCapacityCycles_AllocationIsReported_AdvisoryUntilM0()
    {
        // Arrange: a full table.
        var flights = new FlightTable(160, new SimCounters());
        FlightInit init = FlightTableTests.Init(7);
        for (int i = 0; i < 160; i++)
        {
            Assert.True(flights.TryAppend(in init, out _));
        }

        // Act: 320 cycles, each resolving a middle flight, compacting and launching one.
        (long bytes, int runs) = AllocAssert.Measure(() =>
        {
            for (int c = 0; c < 320; c++)
            {
                flights.Remove(c & 0x7F);
                flights.Compact();
                _ = flights.TryAppend(in init, out _);
            }
        });

        // Assert: reported, not blocking, until M0 (ADR-0015 erratum E2)
        TestContext.Current.TestOutputHelper?.WriteLine($"FlightTable churn x 320 cycles: best of {runs} windows = {bytes} B (advisory until M0, ADR-0015 E2)");
        Assert.Equal(160, flights.LiveFlights);
        Assert.Equal(160L + (320L * runs), flights.Launched);
    }
}
