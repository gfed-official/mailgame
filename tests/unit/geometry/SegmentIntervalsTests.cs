using System.Numerics;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Geometry;

/// <summary>
/// Covers <see cref="SegmentIntervals"/> and <see cref="SegmentInterval"/> (player-character story 004;
/// TR-movement-001, TR-movement-005, TR-movement-011; Throwing F4; ADR-0013 section 6). Golden rows are hand-derived in
/// <see cref="GeometryGoldenVectors"/>; the overflow property test compares against a BigInteger oracle written
/// from the F4 formulas. Integer-only, deterministic (no seeds, no clocks), runs unchanged on the net10.0 and net8.0 legs.
/// </summary>
public class SegmentIntervalsTests
{
    private static void AssertInterval(SegmentInterval actual, int enter, int exit, bool hit, string name)
    {
        Assert.True(enter == actual.EnterPm, $"{name}: EnterPm expected {enter}, got {actual.EnterPm}");
        Assert.True(exit == actual.ExitPm, $"{name}: ExitPm expected {exit}, got {actual.ExitPm}");
        Assert.True(hit == actual.Hit, $"{name}: Hit expected {hit}, got {actual.Hit}");
    }

    // The rejected-call contract: SimAssert throws in assert builds; Release returns None without the quadratic.
    private static void ExpectBroadPhaseReject(Func<SegmentInterval> call)
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => _ = call());
            Assert.Equal(SimAssertCode.GeometryBroadPhase, ex.Code);
        }
        else
        {
            Assert.Equal(SegmentInterval.None, call());
        }
    }

    // ---- AC-1: slab ----

    [Theory]
    [MemberData(nameof(GeometryGoldenVectors.SlabIndices), MemberType = typeof(GeometryGoldenVectors))]
    [Trait("Id", "TC-F4-1")]
    public void Slab_GoldenVector_MatchesHandDerivation(int index)
    {
        // Arrange
        SlabVector v = GeometryGoldenVectors.Slab[index];

        // Act
        SegmentInterval r = SegmentIntervals.Slab(v.P0, v.P1, v.Box);

        // Assert
        AssertInterval(r, v.Enter, v.Exit, v.Hit, v.Name);
    }

    [Fact]
    public void Slab_SegmentStartingInside_FractionIsZero()
    {
        // Arrange
        var box = new AabbMm(new Vec3Mm(-10, -10, -10), new Vec3Mm(10, 10, 10));

        // Act
        SegmentInterval r = SegmentIntervals.Slab(new Vec3Mm(0, 0, 0), new Vec3Mm(1000, 0, 0), in box);

        // Assert (x: enter = ceil(-10000/1000) = -10, exit = floor(10000/1000) = 10)
        Assert.True(r.Hit);
        Assert.Equal(-10, r.EnterPm);
        Assert.Equal(10, r.ExitPm);
        Assert.Equal(0, r.FractionPm);
    }

    [Fact]
    public void Slab_ExtremeCoordinates_DoNotOverflow()
    {
        // Arrange: p1 - p0 would overflow int32; the slab widens to int64 first (and checked builds would throw).
        var box = new AabbMm(new Vec3Mm(-1, -1, -1), new Vec3Mm(1, 1, 1));

        // Act
        SegmentInterval r = SegmentIntervals.Slab(new Vec3Mm(int.MinValue, 0, 0), new Vec3Mm(int.MaxValue, 0, 0), in box);

        // Assert (D = 4294967295; lo' = (-1 - int.MinValue) * 1000 = 2147483647000 -> enter = ceil(499.99999988) = 500;
        //   hi' = (1 + 2147483648) * 1000 = 2147483649000 -> exit = floor(500.00000035) = 500: a hit at exactly 500)
        Assert.Equal(500, r.EnterPm);
        Assert.Equal(500, r.ExitPm);
        Assert.True(r.Hit);
    }

    [Fact]
    public void SlabAxis_SaturatesHugeBoundsWithoutChangingTheVerdict()
    {
        // Arrange: lo - p0 = 3e9 mm, so enter = 3e12 permille, far beyond int.
        // Act
        SegmentInterval far = SegmentIntervals.SlabAxis(-1_500_000_000, -1_500_000_000 + 1, 1_500_000_000, 2_000_000_000);
        SegmentInterval behind = SegmentIntervals.SlabAxis(1_500_000_000, 1_500_000_000 + 1, -2_000_000_000, -1_500_000_000);

        // Assert
        Assert.Equal(int.MaxValue, far.EnterPm);
        Assert.False(far.Hit);
        Assert.Equal(int.MinValue, behind.ExitPm);
        Assert.False(behind.Hit);
    }

    [Fact]
    public void SlabAxis_ZeroDirection_UnboundedInsideAndNoneOutside()
    {
        // Arrange / Act
        SegmentInterval inside = SegmentIntervals.SlabAxis(1500, 1500, 0, 1800);
        SegmentInterval onEdge = SegmentIntervals.SlabAxis(1800, 1800, 0, 1800);
        SegmentInterval outside = SegmentIntervals.SlabAxis(2400, 2400, 0, 1800);

        // Assert (lo <= P0 <= hi is inclusive)
        AssertInterval(inside, int.MinValue, int.MaxValue, true, "inside");
        AssertInterval(onEdge, int.MinValue, int.MaxValue, true, "edge");
        AssertInterval(outside, 0, -1, false, "outside");
    }

    // ---- AC-2: circle / sphere ----

    [Theory]
    [MemberData(nameof(GeometryGoldenVectors.CircleIndices), MemberType = typeof(GeometryGoldenVectors))]
    [Trait("Id", "TC-F4-1")]
    public void Circle_GoldenVector_MatchesHandDerivation(int index)
    {
        // Arrange
        RoundVector v = GeometryGoldenVectors.Circle[index];

        // Act
        SegmentInterval r = SegmentIntervals.Circle(v.P0, v.P1, v.Centre, v.Radius);

        // Assert
        AssertInterval(r, v.Enter, v.Exit, v.Hit, v.Name);
    }

    [Theory]
    [MemberData(nameof(GeometryGoldenVectors.SphereIndices), MemberType = typeof(GeometryGoldenVectors))]
    [Trait("Id", "TC-F4-1")]
    public void Sphere_GoldenVector_MatchesHandDerivation(int index)
    {
        // Arrange
        RoundVector v = GeometryGoldenVectors.Sphere[index];

        // Act
        SegmentInterval r = SegmentIntervals.Sphere(v.P0, v.P1, v.Centre, v.Radius);

        // Assert
        AssertInterval(r, v.Enter, v.Exit, v.Hit, v.Name);
    }

    [Fact]
    public void Circle_IgnoresY_WhereSphereDoesNot()
    {
        // Arrange (the R5 row: 1500 mm above the centre's height)
        RoundVector v = GeometryGoldenVectors.Sphere[5];

        // Act
        SegmentInterval circle = SegmentIntervals.Circle(v.P0, v.P1, v.Centre, v.Radius);
        SegmentInterval sphere = SegmentIntervals.Sphere(v.P0, v.P1, v.Centre, v.Radius);

        // Assert (circle: R = (-2000, 0), A = 16e6, B = -8e6, C = 3e6, disc = 64e12 - 48e12 = 16e12 -> s = 4e6, enter = ceil(1000 * 4e6 / 16e6) = 250)
        Assert.True(circle.Hit);
        Assert.Equal(250, circle.EnterPm);
        Assert.False(sphere.Hit);
    }

    // ---- AC-3: cylinder ----

    [Theory]
    [MemberData(nameof(GeometryGoldenVectors.CylinderIndices), MemberType = typeof(GeometryGoldenVectors))]
    [Trait("Id", "TC-F4-1")]
    public void Cylinder_GoldenVector_MatchesHandDerivation(int index)
    {
        // Arrange
        CylinderVector v = GeometryGoldenVectors.Cylinder[index];

        // Act
        SegmentInterval r = SegmentIntervals.Cylinder(v.P0, v.P1, v.Centre, v.Radius, v.Height);

        // Assert
        AssertInterval(r, v.Enter, v.Exit, v.Hit, v.Name);
    }

    [Fact]
    [Trait("Id", "TC-F4-1")]
    public void Cylinder_EnteredAboveItsTop_TakesTheLaterEntry()
    {
        // Arrange (TC-F4-1g)
        var p0 = new Vec3Mm(-700, 2400, 0);
        var p1 = new Vec3Mm(300, 1500, 0);

        // Act
        SegmentInterval cylinder = SegmentIntervals.Cylinder(in p0, in p1, default, 600, 1800);
        SegmentInterval circleOnly = SegmentIntervals.Circle(in p0, in p1, default, 600);

        // Assert (667 from the y-slab, not the circle's 100)
        Assert.Equal(667, cylinder.FractionPm);
        Assert.Equal(100, circleOnly.FractionPm);
    }

    [Fact]
    public void Cylinder_BaseAboveZero_ShiftsTheSlab()
    {
        // Arrange: base at y = 1000, height 500 -> slab 1000..1500; the segment runs at y = 1200 through the circle (r = 300).
        // Act
        SegmentInterval hit = SegmentIntervals.Cylinder(new Vec3Mm(-1000, 1200, 0), new Vec3Mm(1000, 1200, 0), new Vec3Mm(0, 1000, 0), 300, 500);
        SegmentInterval miss = SegmentIntervals.Cylinder(new Vec3Mm(-1000, 900, 0), new Vec3Mm(1000, 900, 0), new Vec3Mm(0, 1000, 0), 300, 500);

        // Assert (circle: R = (-1000, 0), D = (2000, 0): A = 4e6, B = -2e6, C = 1e6 - 9e4 = 9.1e5, disc = 4e12 - 3.64e12 = 3.6e11, s = 6e5 ->
        //   enter = ceil(1000 * (2e6 - 6e5) / 4e6) = 350; the y = 900 line is below the slab)
        Assert.True(hit.Hit);
        Assert.Equal(350, hit.FractionPm);
        Assert.False(miss.Hit);
    }

    // ---- AC-4: hit rule ----

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(154, 2346, true)]
    [InlineData(-4000, 4000, true)]
    [InlineData(1000, 1000, true)]      // touching at t = 1000
    [InlineData(1001, 2000, false)]     // enters after the segment ends
    [InlineData(5, 4, false)]           // tEnter > tExit
    [InlineData(-3000, -1, false)]      // behind the segment: tExit < 0
    [InlineData(-3000, 0, true)]        // tExit = 0 still counts
    [InlineData(0, -1, false)]          // the None interval
    public void IsHit_TruthTable(int enter, int exit, bool expected)
    {
        // Act
        bool hit = SegmentInterval.IsHit(enter, exit);
        var interval = new SegmentInterval(enter, exit);

        // Assert (the constructor applies the same single rule)
        Assert.Equal(expected, hit);
        Assert.Equal(expected, interval.Hit);
    }

    [Fact]
    public void SegmentInterval_FractionIsMaxOfEnterAndZero_AndDeconstructs()
    {
        // Arrange
        var inside = new SegmentInterval(-50, 700);
        var ahead = new SegmentInterval(300, 700);

        // Act
        (int enter, int exit, bool hit) = ahead;

        // Assert
        Assert.Equal(0, inside.FractionPm);
        Assert.Equal(300, ahead.FractionPm);
        Assert.Equal((300, 700, true), (enter, exit, hit));
    }

    [Fact]
    public void SegmentInterval_Saturates_AndEqualityIsBoundWise()
    {
        // Arrange
        var big = new SegmentInterval(long.MinValue, long.MaxValue);
        var same = new SegmentInterval(int.MinValue, int.MaxValue);
        object boxed = same;

        // Assert
        Assert.Equal(SegmentInterval.NegativeInfinityPm, big.EnterPm);
        Assert.Equal(SegmentInterval.PositiveInfinityPm, big.ExitPm);
        Assert.True(big == same);
        Assert.False(big != same);
        Assert.True(big.Equals(boxed));
        Assert.False(big.Equals("x"));
        Assert.Equal(big.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(SegmentInterval.None, big);
    }

    // ---- AC-5: broad phase ----

    [Fact]
    public void Circle_RadiusBeyondBroadPhase_IsRejectedBeforeTheQuadratic()
    {
        // Arrange: |R| = 65537 mm along x.
        var p0 = new Vec3Mm(65_537, 0, 0);
        var p1 = new Vec3Mm(65_000, 0, 0);

        // Act + Assert
        ExpectBroadPhaseReject(() => SegmentIntervals.Circle(in p0, in p1, default, 300));
        ExpectBroadPhaseReject(() => SegmentIntervals.Sphere(in p0, in p1, default, 300));
        ExpectBroadPhaseReject(() => SegmentIntervals.Cylinder(in p0, in p1, default, 300, 1800));
    }

    [Fact]
    public void Sphere_DiagonalBeyondBroadPhase_IsRejectedEvenWhenEveryComponentFits()
    {
        // Arrange: components 40000 each: |R|^2 = 4.8e9 > 2^32 = 4.29e9.
        var p0 = new Vec3Mm(40_000, 40_000, 40_000);
        var p1 = new Vec3Mm(39_000, 40_000, 40_000);

        // Act + Assert
        ExpectBroadPhaseReject(() => SegmentIntervals.Sphere(in p0, in p1, default, 300));
    }

    [Fact]
    public void Circle_SegmentOrRadiusBeyondContract_IsRejected()
    {
        // Arrange
        var near = new Vec3Mm(1000, 0, 0);
        var longEnd = new Vec3Mm(1000 + 50_000, 0, 0);

        // Act + Assert (|D| = 50000 > 46340; radius 65537 > 65536; negative radius)
        ExpectBroadPhaseReject(() => SegmentIntervals.Circle(in near, in longEnd, default, 300));
        ExpectBroadPhaseReject(() => SegmentIntervals.Circle(in near, in near, default, 65_537));
        ExpectBroadPhaseReject(() => SegmentIntervals.Circle(in near, in near, default, -1));
    }

    [Fact]
    public void Circle_ExtremeCoordinates_AreRejectedWithoutOverflow()
    {
        // Arrange: p0 - centre would overflow int32.
        var p0 = new Vec3Mm(int.MaxValue, 0, 0);
        var centre = new Vec3Mm(int.MinValue, 0, 0);

        // Act + Assert
        ExpectBroadPhaseReject(() => SegmentIntervals.Circle(in p0, in p0, in centre, 300));
    }

    [Fact]
    public void IsWithinBroadPhase_BoundaryValues()
    {
        // Assert: exactly 65536 along an axis is inside, one more is out; |D|^2 = 2^31 - 1 is the limit.
        Assert.True(SegmentIntervals.IsWithinBroadPhase(65_536, 0, 0, 0, 0, 0, 0));
        Assert.True(SegmentIntervals.IsWithinBroadPhase(0, 0, -65_536, 0, 0, 0, 65_536));
        Assert.False(SegmentIntervals.IsWithinBroadPhase(65_537, 0, 0, 0, 0, 0, 0));
        Assert.False(SegmentIntervals.IsWithinBroadPhase(0, -65_537, 0, 0, 0, 0, 0));
        Assert.True(SegmentIntervals.IsWithinBroadPhase(46_341, 0, 46_340, 0, 0, 0, 0));   // 46341^2 + 46340^2 = 4294883881 <= 2^32
        Assert.False(SegmentIntervals.IsWithinBroadPhase(46_341, 0, 46_341, 0, 0, 0, 0));  // 4294976562 > 2^32 = 4294967296
        Assert.True(SegmentIntervals.IsWithinBroadPhase(0, 0, 0, 46_340, 0, 0, 0));        // 46340^2 = 2147395600 <= 2^31 - 1
        Assert.False(SegmentIntervals.IsWithinBroadPhase(0, 0, 0, 46_341, 0, 0, 0));       // 2147488281 > 2147483647
        Assert.False(SegmentIntervals.IsWithinBroadPhase(0, 0, 0, 0, 65_537, 0, 0));
        Assert.False(SegmentIntervals.IsWithinBroadPhase(0, 0, 0, 0, 0, 0, 65_537));
        Assert.False(SegmentIntervals.IsWithinBroadPhase(0, 0, 0, 0, 0, 0, -1));
    }

    [Fact]
    public void Circle_AtExactlyTheBroadPhaseBound_IsAcceptedAndExact()
    {
        // Arrange: R = (65536, 0), D = (-4000, 0), r = 300 -> hand: A = 1.6e7, B = -262,144,000, C = 4,294,967,296 - 90,000 = 4,294,877,296;
        //   B^2 = 68,719,476,736,000,000; A*C = 68,718,036,736,000,000; disc = 1,440,000,000,000 -> s = 1,200,000.
        //   enter = ceil(1000 * (262,144,000 - 1,200,000) / 16e6) = 16309 exactly (beyond the segment, so no hit);
        //   exit = floor(1000 * 263,344,000 / 16e6) = 16459.
        var p0 = new Vec3Mm(65_536, 0, 0);
        var p1 = new Vec3Mm(61_536, 0, 0);

        // Act
        SegmentInterval r = SegmentIntervals.Circle(in p0, in p1, default, 300);

        // Assert
        AssertInterval(r, 16_309, 16_459, false, "bound");
    }

    // ---- AC-5: int64 overflow property test, |R| = 65 536, |D| up to 4 000 ----

    private static BigInteger FloorDivBig(BigInteger a, BigInteger b)
    {
        BigInteger q = BigInteger.DivRem(a, b, out BigInteger rem);
        return (!rem.IsZero && (rem.Sign != b.Sign)) ? q - 1 : q;
    }

    private static BigInteger CeilDivBig(BigInteger a, BigInteger b)
    {
        BigInteger q = BigInteger.DivRem(a, b, out BigInteger rem);
        return (!rem.IsZero && (rem.Sign == b.Sign)) ? q + 1 : q;
    }

    private static BigInteger ISqrtBig(BigInteger n)
    {
        if (n.IsZero)
        {
            return BigInteger.Zero;
        }

        BigInteger x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
        while (true)
        {
            BigInteger y = (x + (n / x)) >> 1;
            if (y >= x)
            {
                return x;
            }

            x = y;
        }
    }

    // F4 circle/sphere in unbounded arithmetic: returns (valid, enter, exit); unbounded sides are long.Min/Max.
    private static (bool Valid, BigInteger Enter, BigInteger Exit) Oracle(long[] r, long[] d, long radius)
    {
        BigInteger a = 0, b = 0, c = -(BigInteger)radius * radius;
        for (int i = 0; i < r.Length; i++)
        {
            a += (BigInteger)d[i] * d[i];
            b += (BigInteger)d[i] * r[i];
            c += (BigInteger)r[i] * r[i];
        }

        if (a.IsZero)
        {
            return (c <= 0, long.MinValue, long.MaxValue);
        }

        BigInteger disc = (b * b) - (a * c);
        if (disc < 0)
        {
            return (false, 0, -1);
        }

        BigInteger s = ISqrtBig(disc);
        return (true, CeilDivBig(1000 * (-b - s), a), FloorDivBig(1000 * (-b + s), a));
    }

    private static void AssertMatchesOracle(SegmentInterval actual, (bool Valid, BigInteger Enter, BigInteger Exit) expected, string what)
    {
        if (!expected.Valid)
        {
            Assert.True(!actual.Hit && actual.EnterPm == 0 && actual.ExitPm == -1, what + ": expected None");
            return;
        }

        BigInteger clampedEnter = BigInteger.Clamp(expected.Enter, int.MinValue, int.MaxValue);
        BigInteger clampedExit = BigInteger.Clamp(expected.Exit, int.MinValue, int.MaxValue);
        Assert.True(clampedEnter == actual.EnterPm && clampedExit == actual.ExitPm,
            $"{what}: expected [{clampedEnter}, {clampedExit}], got [{actual.EnterPm}, {actual.ExitPm}]");
        bool hit = expected.Enter <= expected.Exit && expected.Exit >= 0 && expected.Enter <= 1000;
        Assert.True(hit == actual.Hit, what + ": hit");
    }

    [Fact]
    public void Quadratics_AtTheBroadPhaseBound_MatchWideArithmeticAndNeverOverflow()
    {
        // Arrange: |R| = 65 536 on the axes and on near-diagonals, |D| up to 4 000, radii from 0 to 65 536.
        // (Debug builds are checked: any int64 overflow inside the Sim would throw OverflowException here.)
        long[][] starts =
        {
            new long[] { 65_536, 0, 0 }, new long[] { -65_536, 0, 0 }, new long[] { 0, 0, 65_536 }, new long[] { 0, 0, -65_536 },
            new long[] { 0, 65_536, 0 }, new long[] { 0, -65_536, 0 }, new long[] { 46_341, 0, 46_340 },
            new long[] { -46_340, 0, -46_341 }, new long[] { 37_800, 37_800, 37_800 }, new long[] { 0, 0, 0 },
        };
        long[][] steps =
        {
            new long[] { -4000, 0, 0 }, new long[] { 4000, 0, 0 }, new long[] { 0, 0, -4000 }, new long[] { 0, 0, 4000 },
            new long[] { 0, -4000, 0 }, new long[] { 2828, 0, 2828 }, new long[] { -2828, 0, -2828 },
            new long[] { 2300, 2300, 2300 }, new long[] { -2300, -2300, -2300 }, new long[] { 1, 0, 0 }, new long[] { 0, 0, 0 },
            new long[] { -46_340, 0, 0 }, new long[] { 0, 0, 46_340 }, new long[] { 32_767, 0, 32_767 },
        };
        long[] radii = { 0, 1, 300, 1500, 65_536 };

        // Act + Assert
        int cases = 0;
        foreach (long[] r in starts)
        {
            foreach (long[] d in steps)
            {
                foreach (long radius in radii)
                {
                    var p0 = new Vec3Mm((int)r[0], (int)r[1], (int)r[2]);
                    var p1 = new Vec3Mm((int)(r[0] + d[0]), (int)(r[1] + d[1]), (int)(r[2] + d[2]));
                    string what = $"R=({r[0]},{r[1]},{r[2]}) D=({d[0]},{d[1]},{d[2]}) r={radius}";

                    AssertMatchesOracle(SegmentIntervals.Sphere(in p0, in p1, default, (int)radius), Oracle(r, d, radius), "sphere " + what);
                    AssertMatchesOracle(
                        SegmentIntervals.Circle(in p0, in p1, default, (int)radius),
                        Oracle(new[] { r[0], r[2] }, new[] { d[0], d[2] }, radius),
                        "circle " + what);
                    cases += 2;
                }
            }
        }

        Assert.Equal(2 * starts.Length * steps.Length * radii.Length, cases);
    }
}

/// <summary>Zero-allocation checks for <see cref="SegmentIntervals"/> (AC-8). Non-parallel so no other test disturbs the window.</summary>
[Collection(NonParallelCollection.Name)]
public class SegmentIntervalsAllocationTests
{
    [Fact]
    public void AllPrimitives_WarmedUp_AllocateNothing()
    {
        // Arrange
        var box = new AabbMm(new Vec3Mm(9750, 600, 9500), new Vec3Mm(10750, 1400, 10500));
        var p0 = new Vec3Mm(10250, 963, 10570);
        var p1 = new Vec3Mm(10250, 837, 10114);
        var centre = new Vec3Mm(10250, 900, 10300);
        int sink = 0;

        // Act + Assert
        _ = AllocAssert.Zero("20,000 interval tests across slab, circle, sphere and cylinder", () =>
        {
            for (int i = 0; i < 5000; i++)
            {
                sink += SegmentIntervals.Slab(in p0, in p1, in box).EnterPm;
                sink += SegmentIntervals.Circle(in p0, in p1, in centre, 600).EnterPm;
                sink += SegmentIntervals.Sphere(in p0, in p1, in centre, 600).EnterPm;
                sink += SegmentIntervals.Cylinder(in p0, in p1, in centre, 600, 1800).EnterPm;
            }
        });

        Assert.NotEqual(int.MinValue, sink);
    }
}
