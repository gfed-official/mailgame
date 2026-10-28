using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing story 002 (ADR-0015 section 4; GDD <c>design/gdd/throwing-catching.md</c> F4, T6, TC-F4-1 a/b/c/h, TC-T6-1, TC-T6-2,
/// TC-E-1 b; TR-throwing-003, TR-throwing-005 to 007): <see cref="FlightSweep.FirstContact"/> over hand-built
/// <see cref="ReceiverView"/>s. Contact points are re-checked with a test-side RH written without <see cref="Throw"/> or
/// <c>Fix</c>. FX-Flat: 2000 mm tiles, terrain y = 0, map 0 to 32 000 mm. Non-parallel because of the 0 B measurement.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class FlightSweepTests
{
    private const uint KeyA = 101;
    private const uint KeyB = 102;

    private static readonly WorldTables _fxFlat = Flat(0, 16);

    // FX-Flat MB-A and MB-B catch volumes (GDD l.1132).
    private static readonly AabbMm _mbA = new(new Vec3Mm(9750, 600, 9500), new Vec3Mm(10750, 1400, 10500));
    private static readonly AabbMm _mbB = new(new Vec3Mm(9750, 600, 15500), new Vec3Mm(10750, 1400, 16500));

    // Segment S: straight down 1000 mm, so f = 500 is y = 1500 and f = 300 is y = 1700.
    private static readonly Vec3Mm _s0 = new(5000, 2000, 5000);
    private static readonly Vec3Mm _s1 = new(5000, 1000, 5000);

    // ------------------------------------------------------------------ TC-F4-1 a, b, c

    [Theory]
    [Trait("Id", "TC-F4-1")]
    [Trait("Id", "TC-F4-1b")]
    [Trait("Id", "TC-F4-1c")]
    // a: D = (0, -126, -456); z: enter = ceil(70 000 / 456) = 154 (y and x contain the segment); point y 963 + RH(-19 404, 1000) = 944, z 10570 + RH(-70 224, 1000) = 10500
    [InlineData(10250, 963, 10570, 10250, 837, 10114, true, 154, 10250, 944, 10500)]
    // b: D = (0, -126, -70): enter = 70 000 / 70 = 1000 exactly, a hit at P1
    [InlineData(10250, 963, 10570, 10250, 837, 10500, true, 1000, 10250, 837, 10500)]
    // b: D = (0, -126, -69): enter = ceil(70 000 / 69) = 1015 > 1000, no hit (and nothing else in reach)
    [InlineData(10250, 963, 10570, 10250, 837, 10501, false, 0, 0, 0, 0)]
    // c: P0 inside the volume, D (0, -100, -50): every axis enter < 0, so f = max(enter, 0) = 0 at P0
    [InlineData(10250, 1000, 10200, 10250, 900, 10150, true, 0, 10250, 1000, 10200)]
    public void FxFlatMailbox_SweptSegment_MatchesHandDerivation(
        int x0, int y0, int z0, int x1, int y1, int z1, bool hit, int f, int cx, int cy, int cz)
    {
        // Arrange
        var p0 = new Vec3Mm(x0, y0, z0);
        var p1 = new Vec3Mm(x1, y1, z1);
        var view = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All) { Mailboxes = new[] { new VolumeReceiver(_mbA, KeyA) } };

        // Act
        bool found = FlightSweep.FirstContact(in p0, in p1, in view, new FlightId(7), out Contact c);

        // Assert
        Assert.Equal(hit, found);
        if (hit)
        {
            AssertContact(c, ReceiverKind.Mailbox, KeyA, f, new Vec3Mm(cx, cy, cz), in p0, in p1);
        }
    }

    // ------------------------------------------------------------------ TC-F4-1 h

    [Fact]
    [Trait("Id", "TC-F4-1h")]
    public void HeightfieldMarch_N4FirstSampleBelowAtI2_GivesGroundAt417()
    {
        // Arrange: n = 4, Q_i.y = 250 - 150 i, gaps 100 then -50 -> f = ceil(1000 * 250 / 600) = 417. The y = 0 crossing is
        //   at 417 too, but the water depth there is 0, so it is not Water.
        var p0 = new Vec3Mm(5000, 250, 5000);
        var p1 = new Vec3Mm(6000, -350, 5000);
        var view = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All);

        // Act
        bool found = FlightSweep.FirstContact(in p0, in p1, in view, new FlightId(7), out Contact c);

        // Assert
        Assert.True(found);
        AssertContact(c, ReceiverKind.Ground, 0, 417, new Vec3Mm(5417, 0, 5000), in p0, in p1);
    }

    // ------------------------------------------------------------------ TC-T6-1 (ranks), contact point for every kind

    [Theory]
    [Trait("Id", "TC-T6-1")]
    [Trait("Id", "TR-throwing-005-point")]
    [InlineData(ReceiverKind.ArmedCatcher, ReceiverKind.Mailbox)]
    [InlineData(ReceiverKind.Mailbox, ReceiverKind.Goon)]
    [InlineData(ReceiverKind.Goon, ReceiverKind.Teammate)]
    [InlineData(ReceiverKind.Teammate, ReceiverKind.Belt)]
    [InlineData(ReceiverKind.Belt, ReceiverKind.Solid)]
    [InlineData(ReceiverKind.Solid, ReceiverKind.Ground)]
    [InlineData(ReceiverKind.Ground, ReceiverKind.Water, Traits = new[] { "Id", "TR-throwing-005-water" })]
    public void EqualF500_LowerRankWins_AndSwappingTheTwoRanksFlipsTheWinner(ReceiverKind winner, ReceiverKind loser)
    {
        // Arrange: both receivers are hit at f = 500; swapping their ranks must flip the winner.
        StubPair pair = StubPair.For(winner, loser);
        int[] swapped = { 0, 1, 2, 3, 4, 5, 6, 7 };
        (swapped[(int)winner], swapped[(int)loser]) = (swapped[(int)loser], swapped[(int)winner]);

        // Act
        Contact normal = pair.Sweep(ReceiverRanks.Default);
        Contact flipped = pair.Sweep(Ranks(swapped));

        // Assert
        AssertContact(normal, winner, pair.KeyOf(winner), 500, pair.Point, in pair.P0, in pair.P1);
        AssertContact(flipped, loser, pair.KeyOf(loser), 500, pair.Point, in pair.P0, in pair.P1, rank: (byte)winner);
    }

    [Fact]
    [Trait("Id", "TC-T6-1")]
    public void EqualFAndRank_NearerCentreWins_ThenLowerMix64_SoEachTwinWinsAcrossFlightIds()
    {
        // Arrange: S hits the tops of all four solids at f = 500; the twins share one box and differ only in key.
        var near = new AabbMm(new Vec3Mm(4000, 0, 4000), new Vec3Mm(6000, 1500, 6000));
        var far = new AabbMm(new Vec3Mm(4900, 0, 4900), new Vec3Mm(7000, 1500, 7000));
        SolidReceiver[] byDistance = { new(far, 1), new(near, 2) };
        SolidReceiver[] twins = { new(near, KeyA), new(near, KeyB) };
        int winsA = 0;
        int winsB = 0;

        for (int i = 0; i < 1000; i++)
        {
            var id = new FlightId((ushort)i);
            var distanceView = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All) { Solids = byDistance };
            var twinView = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All) { Solids = twins };

            // Act
            Assert.True(FlightSweep.FirstContact(in _s0, in _s1, in distanceView, id, out Contact d));
            Assert.True(FlightSweep.FirstContact(in _s0, in _s1, in twinView, id, out Contact t));

            // Assert
            Assert.Equal(2u, d.ReceiverKey);
            ulong hashA = SplitMix.Mix64(((ulong)id.Value << 32) | KeyA);
            ulong hashB = SplitMix.Mix64(((ulong)id.Value << 32) | KeyB);
            Assert.Equal(hashA < hashB ? KeyA : KeyB, t.ReceiverKey);
            Assert.Equal(500, t.FPm);
            winsA += t.ReceiverKey == KeyA ? 1 : 0;
            winsB += t.ReceiverKey == KeyB ? 1 : 0;
        }

        Assert.True(winsA > 0 && winsB > 0, $"A won {winsA}, B won {winsB} of 1000");
    }

    [Fact]
    [Trait("Id", "TC-T6-1b")]
    public void EarlierF_BeatsRank_MailboxAt300BeatsArmedCatcherAt400()
    {
        // Arrange: catcher A = 1e6, B = -1e6, C = 640 000, disc = 3.6e11, s = 600 000 -> enter = 400.
        var view = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All)
        {
            Mailboxes = new[] { new VolumeReceiver(new AabbMm(new Vec3Mm(4500, 1000, 4500), new Vec3Mm(5500, 1700, 5500)), KeyA) },
            Catchers = new[] { new SphereReceiver(new Vec3Mm(5000, 1000, 5000), 600, KeyB) },
        };
        var catcherOnly = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All) { Catchers = view.Catchers };

        // Act
        bool found = FlightSweep.FirstContact(in _s0, in _s1, in view, new FlightId(3), out Contact c);
        bool alone = FlightSweep.FirstContact(in _s0, in _s1, in catcherOnly, new FlightId(3), out Contact k);

        // Assert
        Assert.True(found && alone);
        AssertContact(c, ReceiverKind.Mailbox, KeyA, 300, new Vec3Mm(5000, 1700, 5000), in _s0, in _s1);
        AssertContact(k, ReceiverKind.ArmedCatcher, KeyB, 400, new Vec3Mm(5000, 1600, 5000), in _s0, in _s1);
    }

    // ------------------------------------------------------------------ TC-T6-2, TC-E-1 b (descending only)

    [Theory]
    [Trait("Id", "TC-T6-2")]
    [Trait("Id", "TC-E-1b")]
    // TC-T6-2 descending: D = (0, -100, -600); z enter = ceil(300 000 / 600) = 500; point (10250, 1250, 10500)
    [InlineData(10250, 1300, 10800, 10250, 1200, 10200, true, 500, 10250, 1250, 10500)]
    // TC-T6-2 rising (start y 1100): the slab alone would hit at z 10500 (y 1150), but the volume is not counted
    [InlineData(10250, 1100, 10800, 10250, 1200, 10200, false, 0, 0, 0, 0)]
    // TC-T6-2 flat y 1000 -> 1000: not counted either
    [InlineData(10250, 1000, 10800, 10250, 1000, 10200, false, 0, 0, 0, 0)]
    // TC-E-1 b: origin inside MB-A, descending first segment -> f = 0 at P0 (the landing at T+1)
    [InlineData(10250, 1000, 10000, 10250, 950, 9700, true, 0, 10250, 1000, 10000)]
    // TC-E-1 b: origin inside MB-A, upward first segment -> no hit
    [InlineData(10250, 1000, 10000, 10250, 1100, 9700, false, 0, 0, 0, 0)]
    public void MailboxVolume_CountsOnlyOnADescendingSegment(
        int x0, int y0, int z0, int x1, int y1, int z1, bool hit, int f, int cx, int cy, int cz)
    {
        // Arrange
        var p0 = new Vec3Mm(x0, y0, z0);
        var p1 = new Vec3Mm(x1, y1, z1);
        var view = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All) { Mailboxes = new[] { new VolumeReceiver(_mbA, KeyA) } };

        // Act
        bool found = FlightSweep.FirstContact(in p0, in p1, in view, new FlightId(9), out Contact c);

        // Assert
        Assert.Equal(hit, found);
        if (hit)
        {
            AssertContact(c, ReceiverKind.Mailbox, KeyA, f, new Vec3Mm(cx, cy, cz), in p0, in p1);
        }
    }

    [Fact]
    [Trait("Id", "TC-T6-2")]
    public void FirstQualifyingVolume_RisingInsideMbBMisses_DescendingIntoMbALandsWithMbAKey()
    {
        // Arrange: one view holding MB-A and MB-B (FX-Flat); the multi-tick flight between them is story 011.
        var view = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All)
        {
            Mailboxes = new[] { new VolumeReceiver(_mbA, KeyA), new VolumeReceiver(_mbB, KeyB) },
        };

        // Act
        bool rising = FlightSweep.FirstContact(new Vec3Mm(10250, 900, 16200), new Vec3Mm(10250, 1000, 16000), in view, new FlightId(4), out _);
        bool descending = FlightSweep.FirstContact(new Vec3Mm(10250, 1300, 10800), new Vec3Mm(10250, 1200, 10200), in view, new FlightId(4), out Contact c);

        // Assert
        Assert.False(rising);
        Assert.True(descending);
        Assert.Equal(ReceiverKind.Mailbox, c.Kind);
        Assert.Equal(KeyA, c.ReceiverKey);
        Assert.Equal(500, c.FPm);
    }

    // ------------------------------------------------------------------ Terrain and water

    [Theory]
    [Trait("Id", "TR-throwing-005-water")]
    // Water: y = 0 enter = ceil(300 000 / 600) = 500, depth 500 > 0; the march never reaches the terrain.
    [InlineData(-500, 300, -300, ReceiverKind.Water, 500, 0)]
    // Ground: march n = 3, gap_1 = 0 -> f = ceil(1000 * 200 / 600) = 334; the y = 0 crossing has depth 0, so no Water.
    [InlineData(100, 300, -300, ReceiverKind.Ground, 334, 100)]
    public void TerrainAndWater_WaterPlaneCountsWhereDepthIsPositive_GroundComesFromTheMarch(
        int terrainMm, int y0, int y1, ReceiverKind kind, int f, int contactY)
    {
        // Arrange
        WorldTables world = Flat(terrainMm, 8);
        var p0 = new Vec3Mm(5000, y0, 5000);
        var p1 = new Vec3Mm(5000, y1, 5000);
        var view = new ReceiverView(world, world.Bounds, ReceiverRanks.Default, ReceiverMask.All);

        // Act
        bool found = FlightSweep.FirstContact(in p0, in p1, in view, new FlightId(1), out Contact c);

        // Assert
        Assert.True(found);
        AssertContact(c, kind, 0, f, new Vec3Mm(5000, contactY, 5000), in p0, in p1);
    }

    // ------------------------------------------------------------------ Map edge

    [Theory]
    [Trait("Id", "TR-throwing-006-edge")]
    // Each segment moves 400 mm outward across a side and 100 mm down: f = 500, y 450.
    [InlineData(200, 5000, -200, 5000, true, 0u, 0, 5000)]
    [InlineData(31800, 5000, 32200, 5000, true, 1u, 32000, 5000)]
    [InlineData(5000, 200, 5000, -200, true, 2u, 5000, 0)]
    [InlineData(5000, 31800, 5000, 32200, true, 3u, 5000, 32000)]
    // On the -X side moving inward: not stopped (Mail Dev default, pending Evan: sides count only on outward motion)
    [InlineData(0, 5000, 400, 5000, false, 0u, 0, 0)]
    public void SegmentLeavingBounds_HitsTheMapSideAsSolidAtTheCrossing(int x0, int z0, int x1, int z1, bool hit, uint side, int cx, int cz)
    {
        // Arrange
        var p0 = new Vec3Mm(x0, 500, z0);
        var p1 = new Vec3Mm(x1, 400, z1);
        var view = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All);

        // Act
        bool found = FlightSweep.FirstContact(in p0, in p1, in view, new FlightId(2), out Contact c);

        // Assert
        Assert.Equal(hit, found);
        if (hit)
        {
            AssertContact(c, ReceiverKind.Solid, FlightSweep.MapSideKeyBase | side, 500, new Vec3Mm(cx, 450, cz), in p0, in p1);
        }
    }

    // ------------------------------------------------------------------ Broad phase

    [Fact]
    [Trait("Id", "TR-throwing-007-broad")]
    public void FarDynamicReceivers_AreRejectedByTheBoundingSphere_BeforeAnyQuadraticForm()
    {
        // Arrange: |R| = 195 000 mm for both. This proves the pretest only where SIM_ASSERT is on: there, reaching
        //   SegmentIntervals would throw GeometryBroadPhase. In Release SegmentIntervals returns None anyway, so it is vacuous.
        var view = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All)
        {
            Catchers = new[] { new SphereReceiver(new Vec3Mm(5000, 1500, 200_000), 1000, KeyA) },
            Cylinders = new[] { new CylinderReceiver(new Vec3Mm(5000, 1000, 200_000), 600, 1800, ReceiverKind.Goon, KeyB) },
        };

        // Act
        bool found = FlightSweep.FirstContact(in _s0, in _s1, in view, new FlightId(5), out _);

        // Assert
        Assert.False(found);
    }

    [Fact]
    [Trait("Id", "TR-throwing-007-broad")]
    public void RBeyond65536_PassingTheBoundingSphere_IsRejectedBySegmentIntervalsGuard()
    {
        // Arrange: |2C - (P0 + P1)| = 120 000 <= 2r + |D| = 140 000 passes the pretest; |R| = 70 000 fails the guard.
        static bool Sweep()
        {
            var view = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All)
            {
                Catchers = new[] { new SphereReceiver(new Vec3Mm(16_000, 90_000, 16_000), 60_000, KeyA) },
            };
            return FlightSweep.FirstContact(new Vec3Mm(16_000, 20_000, 16_000), new Vec3Mm(16_000, 40_000, 16_000), in view, new FlightId(5), out _);
        }

        // Act + Assert: SimAssert in assert builds; in Release SegmentInterval.None, so no contact despite the geometry
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => Sweep());
            Assert.Equal(SimAssertCode.GeometryBroadPhase, ex.Code);
        }
        else
        {
            Assert.False(Sweep());
        }
    }

    [Fact]
    [Trait("Id", "TR-throwing-007-broad")]
    public void RAtTheBroadPhaseBound_EveryDUpTo1200_MatchesTheOracleWithoutOverflow()
    {
        // Arrange: catcher R on the six axes (|R| = 65 536) and one diagonal against the BigInteger oracle, plus a goon cylinder
        //   with horizontal R against SegmentIntervals.Cylinder called directly (PC-004 owns its maths; this shows the pretest
        //   drops no hit). Every D in the grid with |D| <= 1200. The Sim runs checked arithmetic in Debug, so an overflow throws.
        WorldTables world = Flat(0, 64);
        var p0 = new Vec3Mm(64_000, 30_000, 64_000);
        (int X, int Y, int Z, bool Cylinder)[] offsets =
        {
            (65_536, 0, 0, false), (-65_536, 0, 0, false), (0, 65_536, 0, false),
            (0, -65_536, 0, false), (0, 0, 65_536, false), (0, 0, -65_536, false),
            (37_837, 37_837, 37_837, false), (65_536, 0, 0, true),
        };
        int[] steps = { -1200, -849, -692, -400, -1, 0, 1, 400, 692, 849, 1200 };

        foreach ((int ox, int oy, int oz, bool cylinder) in offsets)
        {
            var centre = new Vec3Mm(p0.X - ox, p0.Y - oy, p0.Z - oz);
            var feet = new Vec3Mm(centre.X, p0.Y - 1500, centre.Z);
            int checkedCount = 0;
            int hits = 0;
            foreach (int dx in steps)
            {
                foreach (int dy in steps)
                {
                    foreach (int dz in steps)
                    {
                        if (((long)dx * dx) + ((long)dy * dy) + ((long)dz * dz) > 1200L * 1200)
                        {
                            continue;
                        }

                        var p1 = new Vec3Mm(p0.X + dx, p0.Y + dy, p0.Z + dz);
                        foreach (int r in new[] { 65_000, 65_536 })
                        {
                            var view = new ReceiverView(world, world.Bounds, ReceiverRanks.Default, ReceiverMask.All)
                            {
                                Catchers = cylinder ? [] : new[] { new SphereReceiver(centre, r, KeyA) },
                                Cylinders = cylinder ? new[] { new CylinderReceiver(feet, r, 3000, ReceiverKind.Goon, KeyA) } : [],
                            };

                            // Act
                            bool found = FlightSweep.FirstContact(in p0, in p1, in view, new FlightId(1), out Contact c);
                            (bool oracleHit, long oracleF) = cylinder ? CylinderDirect(in p0, in p1, in feet, r) : SphereOracle(in p0, in p1, in centre, r);

                            // Assert
                            string at = $"R ({ox}, {oy}, {oz}) cylinder {cylinder}, D ({dx}, {dy}, {dz}), r {r}";
                            Assert.True(oracleHit == found, $"{at}: hit {found}, oracle {oracleHit}");
                            if (found)
                            {
                                Assert.True(oracleF == c.FPm, $"{at}: f {c.FPm}, oracle {oracleF}");
                                hits++;
                            }

                            checkedCount++;
                        }
                    }
                }
            }

            Assert.True(checkedCount > 500 && hits > 0, $"R ({ox}, {oy}, {oz}) cylinder {cylinder}: checked {checkedCount}, hits {hits}");
        }
    }

    // ------------------------------------------------------------------ A = 0

    [Theory]
    [Trait("Id", "TR-throwing-005-a0")]
    // Catcher centre (5000, 1000, 5000) r 500, P0 = P1: C = |R|^2 - r^2. Inside and on the surface hit at f = 0; outside misses
    //   (rejected already by the bounding sphere, which is 2r for a point).
    [InlineData(false, 5000, 1400, true)]
    [InlineData(false, 5000, 1500, true)]
    [InlineData(false, 5000, 1501, false)]
    // Goon base (5000, 1000, 5000) r 300 h 1800: the point 400 mm off the axis is inside the bounding sphere (doubled offset
    //   800 <= ceil(sqrt(600^2 + 1800^2)) = 1898) but outside the circle, so the quadratic sees A = 0 and C > 0.
    [InlineData(true, 5300, 1900, true)]
    [InlineData(true, 5400, 1900, false)]
    public void ZeroLengthSegment_UsesThePointTest_ContactIffCAtMostZero(bool cylinder, int x, int y, bool hit)
    {
        // Arrange
        var p = new Vec3Mm(x, y, 5000);
        var view = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All)
        {
            Catchers = cylinder ? [] : new[] { new SphereReceiver(new Vec3Mm(5000, 1000, 5000), 500, KeyA) },
            Cylinders = cylinder ? new[] { new CylinderReceiver(new Vec3Mm(5000, 1000, 5000), 300, 1800, ReceiverKind.Goon, KeyA) } : [],
        };

        // Act
        bool found = FlightSweep.FirstContact(in p, in p, in view, new FlightId(6), out Contact c);

        // Assert
        Assert.Equal(hit, found);
        if (hit)
        {
            AssertContact(c, cylinder ? ReceiverKind.Goon : ReceiverKind.ArmedCatcher, KeyA, 0, p, in p, in p);
        }
    }

    // ------------------------------------------------------------------ Contact shape

    [Fact]
    [Trait("Id", "TR-throwing-005-point")]
    public void Contact_IsTheStoryFieldList_AllInitOnly()
    {
        // Act
        FieldInfo[] fields = typeof(Contact).GetFields(BindingFlags.Instance | BindingFlags.Public);

        // Assert
        Assert.Equal(
            new[] { "FPm:Int32", "Kind:ReceiverKind", "Point:Vec3Mm", "Rank:Byte", "ReceiverKey:UInt32" },
            fields.Select(f => f.Name + ":" + f.FieldType.Name).Order(StringComparer.Ordinal));
        Assert.All(fields, f => Assert.True(f.IsInitOnly));
    }

    // ------------------------------------------------------------------ ReceiverMask

    [Theory]
    [Trait("Id", "TR-throwing-003-mask")]
    [InlineData(ReceiverKind.ArmedCatcher)]
    [InlineData(ReceiverKind.Goon)]
    public void StaticOnly_SkipsCatchersAndCylinders_AndEqualsAllOnTheStaticReceivers(ReceiverKind dynamicKind)
    {
        // Arrange: on S the mailbox top is f = 500; each dynamic stub is reached first at f = 300.
        VolumeReceiver[] mailboxes = { new(new AabbMm(new Vec3Mm(4500, 1000, 4500), new Vec3Mm(5500, 1500, 5500)), KeyA) };
        SphereReceiver[] catchers = dynamicKind == ReceiverKind.ArmedCatcher ? new[] { new SphereReceiver(new Vec3Mm(5000, 1000, 5000), 700, KeyB) } : [];
        CylinderReceiver[] cylinders = dynamicKind == ReceiverKind.Goon ? new[] { new CylinderReceiver(new Vec3Mm(5000, 1000, 5000), 300, 700, ReceiverKind.Goon, KeyB) } : [];
        var all = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All) { Mailboxes = mailboxes, Catchers = catchers, Cylinders = cylinders };
        var staticOnly = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.StaticOnly) { Mailboxes = mailboxes, Catchers = catchers, Cylinders = cylinders };
        var staticReceivers = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All) { Mailboxes = mailboxes };

        // Act
        Assert.True(FlightSweep.FirstContact(in _s0, in _s1, in all, new FlightId(8), out Contact a));
        Assert.True(FlightSweep.FirstContact(in _s0, in _s1, in staticOnly, new FlightId(8), out Contact s));
        Assert.True(FlightSweep.FirstContact(in _s0, in _s1, in staticReceivers, new FlightId(8), out Contact expected));

        // Assert
        AssertContact(a, dynamicKind, KeyB, 300, new Vec3Mm(5000, 1700, 5000), in _s0, in _s1);
        AssertContact(s, ReceiverKind.Mailbox, KeyA, 500, new Vec3Mm(5000, 1500, 5000), in _s0, in _s1);
        Assert.Equal(expected, s);
    }

    [Fact]
    [Trait("Id", "TR-throwing-003-mask")]
    public void StaticOnly_KeepsBelts_BeltAt300BeatsMailboxAt500_SameAsAll()
    {
        // Arrange: the belt top face (y 1600 to 1700) is reached at f = 300, the mailbox top at f = 500.
        VolumeReceiver[] mailboxes = { new(new AabbMm(new Vec3Mm(4500, 1000, 4500), new Vec3Mm(5500, 1500, 5500)), KeyA) };
        LaneReceiver[] belts = { new(new AabbMm(new Vec3Mm(4500, 1600, 4500), new Vec3Mm(5500, 1700, 5500)), KeyB) };
        var all = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All) { Mailboxes = mailboxes, Belts = belts };
        var staticOnly = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.StaticOnly) { Mailboxes = mailboxes, Belts = belts };

        // Act
        Assert.True(FlightSweep.FirstContact(in _s0, in _s1, in all, new FlightId(8), out Contact a));
        Assert.True(FlightSweep.FirstContact(in _s0, in _s1, in staticOnly, new FlightId(8), out Contact s));

        // Assert
        AssertContact(s, ReceiverKind.Belt, KeyB, 300, new Vec3Mm(5000, 1700, 5000), in _s0, in _s1);
        Assert.Equal(a, s);
    }

    // ------------------------------------------------------------------ Purity

    [Fact]
    [Trait("Id", "TR-throwing-005-purity")]
    public void FlightSweep_IsAStaticClassWithNoStaticFieldAndNoFloatingPointToken()
    {
        // Arrange
        Type t = typeof(FlightSweep);
        string path = Path.Combine(FindRepoRoot(), "src", "PerformativeMail.Sim", "Throwing", "FlightSweep.cs");
        var floating = new Regex(@"\b(float|double|Single|Double)\b", RegexOptions.CultureInvariant);

        // Act
        string[] statics = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(f => !f.IsLiteral).Select(f => f.Name).ToArray();
        string[] tokens = File.ReadAllLines(path).Select(StripComment).Where(l => floating.IsMatch(l)).ToArray();

        // Assert
        Assert.True(t.IsAbstract && t.IsSealed, "FlightSweep is not a static class");
        Assert.True(t.GetMethod(nameof(FlightSweep.FirstContact))!.IsStatic);
        Assert.Empty(statics);
        Assert.Empty(tokens);
    }

    [Fact]
    [Trait("Id", "TR-throwing-005-purity")]
    public void FirstContact_HundredThousandCalls_AllocationIsReported_AdvisoryUntilM0()
    {
        // Arrange
        VolumeReceiver[] mailboxes = { new(_mbA, KeyA) };
        SolidReceiver[] solids = { new(new AabbMm(new Vec3Mm(4000, 0, 4000), new Vec3Mm(6000, 1500, 6000)), 1) };
        SphereReceiver[] catchers = { new(new Vec3Mm(10250, 1000, 9000), 1000, KeyB) };
        var p0 = new Vec3Mm(10250, 963, 10570);
        var p1 = new Vec3Mm(10250, 837, 10114);
        long checksum = 0;

        // Act
        (long bytes, int runs) = AllocAssert.Measure(() =>
        {
            var view = new ReceiverView(_fxFlat, _fxFlat.Bounds, ReceiverRanks.Default, ReceiverMask.All) { Mailboxes = mailboxes, Solids = solids, Catchers = catchers };
            for (int i = 0; i < 100_000; i++)
            {
                if (FlightSweep.FirstContact(in p0, in p1, in view, new FlightId((ushort)i), out Contact c))
                {
                    checksum += c.FPm;
                }
            }
        });

        // Assert: reported, not blocking, until M0 (ADR-0015 erratum E2)
        TestContext.Current.TestOutputHelper?.WriteLine($"FlightSweep.FirstContact x 100 000: best of {runs} windows = {bytes} B (advisory until M0, ADR-0015 E2)");
        Assert.Equal(154L * 100_000 * runs, checksum);
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertContact(in Contact c, ReceiverKind kind, uint key, int f, Vec3Mm point, in Vec3Mm p0, in Vec3Mm p1, byte? rank = null)
    {
        Assert.Equal(kind, c.Kind);
        Assert.Equal(rank ?? (byte)kind, c.Rank);
        Assert.Equal(key, c.ReceiverKey);
        Assert.Equal(f, c.FPm);
        Assert.Equal(point, c.Point);

        // contact = P0 + RH(D * f, 1000), re-derived with the test-side RH
        var formula = new Vec3Mm(
            (int)(p0.X + OracleRH(((long)p1.X - p0.X) * f, 1000)),
            (int)(p0.Y + OracleRH(((long)p1.Y - p0.Y) * f, 1000)),
            (int)(p0.Z + OracleRH(((long)p1.Z - p0.Z) * f, 1000)));
        Assert.Equal(formula, c.Point);
    }

    // Independent RH: floor((2n + d) / (2d)) for d > 0, written without Throw or Fix.
    private static long OracleRH(long n, long d)
    {
        long num = (2 * n) + d;
        long den = 2 * d;
        long q = num / den;
        return (num % den != 0 && num < 0) ? q - 1 : q;
    }

    // F4's sphere interval in BigInteger: the hit verdict and f = max(enter, 0).
    private static (bool Hit, long F) SphereOracle(in Vec3Mm p0, in Vec3Mm p1, in Vec3Mm centre, int r)
    {
        BigInteger dx = (BigInteger)p1.X - p0.X, dy = (BigInteger)p1.Y - p0.Y, dz = (BigInteger)p1.Z - p0.Z;
        BigInteger rx = (BigInteger)p0.X - centre.X, ry = (BigInteger)p0.Y - centre.Y, rz = (BigInteger)p0.Z - centre.Z;
        BigInteger a = (dx * dx) + (dy * dy) + (dz * dz);
        BigInteger b = (dx * rx) + (dy * ry) + (dz * rz);
        BigInteger c = (rx * rx) + (ry * ry) + (rz * rz) - ((BigInteger)r * r);
        if (a.IsZero)
        {
            return (c <= 0, 0);
        }

        BigInteger disc = (b * b) - (a * c);
        if (disc < 0)
        {
            return (false, 0);
        }

        BigInteger s = ISqrtBig(disc);
        BigInteger enter = CeilDivBig(1000 * (-b - s), a);
        BigInteger exit = FloorDivBig(1000 * (-b + s), a);
        bool hit = enter <= exit && exit >= 0 && enter <= 1000;
        return (hit, hit ? (long)BigInteger.Max(enter, 0) : 0);
    }

    private static (bool Hit, long F) CylinderDirect(in Vec3Mm p0, in Vec3Mm p1, in Vec3Mm feet, int r)
    {
        SegmentInterval i = SegmentIntervals.Cylinder(in p0, in p1, in feet, r, 3000);
        return (i.Hit, i.Hit ? i.FractionPm : 0);
    }

    private static BigInteger FloorDivBig(BigInteger a, BigInteger b)
    {
        BigInteger q = BigInteger.DivRem(a, b, out BigInteger rem);
        return (!rem.IsZero && (rem.Sign != b.Sign)) ? q - 1 : q;
    }

    private static BigInteger CeilDivBig(BigInteger a, BigInteger b) => -FloorDivBig(-a, b);

    private static BigInteger ISqrtBig(BigInteger n)
    {
        if (n.IsZero)
        {
            return BigInteger.Zero;
        }

        BigInteger x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
        while (true)
        {
            BigInteger y = (x + (n / x)) / 2;
            if (y >= x)
            {
                return x;
            }

            x = y;
        }
    }

    private static ReceiverRanks Ranks(int[] r) => new(r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7]);

    private static WorldTables Flat(int heightMm, int tiles)
    {
        var corners = new int[(tiles + 1) * (tiles + 1)];
        Array.Fill(corners, heightMm);
        var b = new WorldTablesBuilder();
        b.SetCorners(tiles, tiles, corners);
        return b.Build();
    }

    private static string StripComment(string line)
    {
        int at = line.IndexOf("//", StringComparison.Ordinal);
        return at < 0 ? line : line[..at];
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PerformativeMail.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("PerformativeMail.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Two rank stubs hit at exactly f = 500. Segment S for the pairs above the terrain (catcher disc = 2.5e11, s = 500 000;
    /// boxes topped at y 1500; cylinders base y 1000, height 500). Solid over Ground: y 500 -> -500, a box topped at y 0 and
    /// the march (n = 4, gaps 250 then 0). Ground over Water, the shoreline tie: terrain -1, y 1999 -> -2001; water enter
    /// ceil(1 999 000 / 4000) = 500, march n = 16 with gap_8 = 0 -> 500; point y -1 (depth 1).
    /// </summary>
    private readonly struct StubPair
    {
        public readonly Vec3Mm P0;
        public readonly Vec3Mm P1;
        public readonly Vec3Mm Point;
        private readonly WorldTables _world;
        private readonly ReceiverKind _a;
        private readonly ReceiverKind _b;

        private StubPair(WorldTables world, Vec3Mm p0, Vec3Mm p1, Vec3Mm point, ReceiverKind a, ReceiverKind b)
        {
            _world = world;
            P0 = p0;
            P1 = p1;
            Point = point;
            _a = a;
            _b = b;
        }

        public static StubPair For(ReceiverKind winner, ReceiverKind loser)
        {
            if (loser == ReceiverKind.Water)
            {
                return new StubPair(Flat(-1, 8), new Vec3Mm(5000, 1999, 5000), new Vec3Mm(5000, -2001, 5000), new Vec3Mm(5000, -1, 5000), winner, loser);
            }

            if (loser == ReceiverKind.Ground)
            {
                return new StubPair(_fxFlat, new Vec3Mm(5000, 500, 5000), new Vec3Mm(5000, -500, 5000), new Vec3Mm(5000, 0, 5000), winner, loser);
            }

            return new StubPair(_fxFlat, _s0, _s1, new Vec3Mm(5000, 1500, 5000), winner, loser);
        }

        // Ground, water and map sides carry key 0 / side keys; every stub carries 100 + its kind.
        public uint KeyOf(ReceiverKind kind) => kind is ReceiverKind.Ground or ReceiverKind.Water ? 0 : 100u + (uint)kind;

        public Contact Sweep(ReceiverRanks ranks)
        {
            bool groundPair = _b == ReceiverKind.Ground;
            var view = new ReceiverView(_world, _world.Bounds, ranks, ReceiverMask.All)
            {
                Catchers = Has(ReceiverKind.ArmedCatcher) ? new[] { new SphereReceiver(new Vec3Mm(5000, 1000, 5000), 500, KeyOf(ReceiverKind.ArmedCatcher)) } : [],
                Mailboxes = Has(ReceiverKind.Mailbox) ? new[] { new VolumeReceiver(Box(1000, 1500), KeyOf(ReceiverKind.Mailbox)) } : [],
                Cylinders = Cylinders(),
                Belts = Has(ReceiverKind.Belt) ? new[] { new LaneReceiver(Box(1400, 1500), KeyOf(ReceiverKind.Belt)) } : [],
                Solids = Has(ReceiverKind.Solid) ? new[] { new SolidReceiver(groundPair ? Box(-1000, 0) : Box(0, 1500), KeyOf(ReceiverKind.Solid)) } : [],
            };
            Assert.True(FlightSweep.FirstContact(in P0, in P1, in view, new FlightId(11), out Contact c));
            return c;
        }

        private static AabbMm Box(int yLo, int yHi) => new(new Vec3Mm(4500, yLo, 4500), new Vec3Mm(5500, yHi, 5500));

        private bool Has(ReceiverKind k) => _a == k || _b == k;

        private CylinderReceiver[] Cylinders()
        {
            var list = new List<CylinderReceiver>();
            foreach (ReceiverKind k in new[] { ReceiverKind.Goon, ReceiverKind.Teammate })
            {
                if (Has(k))
                {
                    list.Add(new CylinderReceiver(new Vec3Mm(5000, 1000, 5000), 300, 500, k, KeyOf(k)));
                }
            }

            return list.ToArray();
        }
    }
}
