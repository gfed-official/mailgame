using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Geometry;

/// <summary>
/// Covers <see cref="Obb"/> (player-character story 004; TR-movement-001; ADR-0013 section 3 yaw convention and section 6).
/// Convention under test: for BAM yaw theta, local +X is world <c>(cos, -sin)</c> and local +Z is world <c>(sin, cos)</c>, so
/// <c>local = (dx cos - dz sin, dy, dx sin + dz cos) / 32767</c>; yaw 90 degrees (BAM 16384) maps world offsets
/// <c>(dx, dz) -> (-dz, dx)</c>. Integer-only; net10.0 and net8.0.
/// </summary>
public class ObbTests
{
    private static readonly Vec3Mm _origin = new(0, 0, 0);

    // ---- AC-7: yaw 90 degrees exact, yaw 0 identity ----

    [Theory]
    [InlineData(1000, 2000, -2000, 1000)]
    [InlineData(-7, 13, -13, -7)]
    [InlineData(20_000, -20_000, 20_000, 20_000)]
    [InlineData(0, -2000, 2000, 0)]    // a point north of the box lies on its local +X axis
    public void ToLocal_YawNinetyDegrees_MapsOffsetsExactly(int dx, int dz, int expectedX, int expectedZ)
    {
        // Arrange (BAM 16384: sin = 32767, cos = Sin(32768) = 0, so x' = RH(-dz * 32767 / 32767) with no rounding at all)
        var centre = new Vec3Mm(5000, 300, -4000);
        var world = new Vec3Mm(5000 + dx, 300 + 11, -4000 + dz);

        // Act
        Vec3Mm local = Obb.ToLocal(in world, in centre, Bam.Quarter);

        // Assert (y passes through as an offset)
        Assert.Equal(new Vec3Mm(expectedX, 11, expectedZ), local);
    }

    [Theory]
    [InlineData(1000, 2000)]
    [InlineData(-1234, 5678)]
    [InlineData(20_000, -20_000)]
    public void ToLocal_YawZero_IsTheIdentityOnOffsets(int dx, int dz)
    {
        // Arrange (sin = 0, cos = 32767: RH(dx * 32767 / 32767) = dx)
        var centre = new Vec3Mm(100, 200, 300);
        var world = new Vec3Mm(100 + dx, 250, 300 + dz);

        // Act
        Vec3Mm local = Obb.ToLocal(in world, in centre, 0);

        // Assert
        Assert.Equal(new Vec3Mm(dx, 50, dz), local);
    }

    [Fact]
    public void ToLocal_HalfAndThreeQuarterTurns_AreExact()
    {
        // Arrange
        var world = new Vec3Mm(1000, 0, 2000);

        // Act
        Vec3Mm half = Obb.ToLocal(in world, in _origin, Bam.Half);                // sin 0, cos -32767 -> (-dx, -dz)
        Vec3Mm threeQuarter = Obb.ToLocal(in world, in _origin, Bam.ThreeQuarters);   // sin -32767, cos 0 -> (dz, -dx)

        // Assert
        Assert.Equal(new Vec3Mm(-1000, 0, -2000), half);
        Assert.Equal(new Vec3Mm(2000, 0, -1000), threeQuarter);
    }

    [Fact]
    public void ToLocal_FortyFiveDegrees_UsesTheQ15Table()
    {
        // Arrange: BAM 8192 -> table index 512: sin = cos = round(32767 * sin 45) = round(23169.8) = 23170.
        //   (1000, 0): x' = RH(1000 * 23170 / 32767) = RH(707.1) = 707, z' = RH(1000 * 23170 / 32767) = 707.
        var world = new Vec3Mm(1000, 0, 0);

        // Act
        Vec3Mm local = Obb.ToLocal(in world, in _origin, 8192);

        // Assert
        Assert.Equal(new Vec3Mm(707, 0, 707), local);
    }

    [Fact]
    public void ToLocal_IgnoresTheLowFourBamBits_LikeTheTrigTable()
    {
        // Arrange
        var world = new Vec3Mm(3000, 0, -1500);

        // Act
        Vec3Mm a = Obb.ToLocal(in world, in _origin, 12_288);
        Vec3Mm b = Obb.ToLocal(in world, in _origin, 12_288 + 15);

        // Assert
        Assert.Equal(a, b);
    }

    [Fact]
    public void ToLocal_NegativeRoundingHalves_AreSymmetric()
    {
        // Arrange: 45 degrees, offset (1, 0): x' = z' = RH(23170 / 32767) = RH(0.707) = 1; negated offset -> -1 (half away from zero, symmetric).
        // Act
        Vec3Mm pos = Obb.ToLocal(new Vec3Mm(1, 0, 0), in _origin, 8192);
        Vec3Mm neg = Obb.ToLocal(new Vec3Mm(-1, 0, 0), in _origin, 8192);

        // Assert
        Assert.Equal(new Vec3Mm(1, 0, 1), pos);
        Assert.Equal(new Vec3Mm(-1, 0, -1), neg);
    }

    // ---- AC-7: round trip within 1 mm for |offset| <= 20 m ----

    [Fact]
    public void RoundTrip_AllTableAnglesAndExtremeOffsets_WithinOneMillimetre()
    {
        // Arrange
        int[][] offsets =
        {
            new[] { 20_000, 0, 20_000 }, new[] { -20_000, 5, 20_000 }, new[] { 20_000, -9, -20_000 }, new[] { -20_000, 0, -20_000 },
            new[] { 12_345, 777, -6_789 }, new[] { 1, 0, 1 }, new[] { 0, 0, 20_000 }, new[] { 20_000, 0, 0 },
            new[] { -19_999, 0, 7 }, new[] { 14_142, 0, 14_142 },
        };
        var centre = new Vec3Mm(-3000, 1200, 8000);
        int worst = 0;

        // Act
        for (int yaw = 0; yaw < Bam.Turn; yaw += 16)
        {
            foreach (int[] o in offsets)
            {
                var world = new Vec3Mm(centre.X + o[0], centre.Y + o[1], centre.Z + o[2]);
                Vec3Mm local = Obb.ToLocal(in world, in centre, (ushort)yaw);
                Vec3Mm back = Obb.ToWorld(in local, in centre, (ushort)yaw);
                worst = Math.Max(worst, Math.Max(Math.Abs(back.X - world.X), Math.Max(Math.Abs(back.Y - world.Y), Math.Abs(back.Z - world.Z))));
            }
        }

        // Assert
        Assert.True(worst <= 1, $"worst round-trip error {worst} mm exceeds 1 mm");
    }

    [Fact]
    public void ToWorld_YawNinetyDegrees_IsTheExactInverse()
    {
        // Arrange: local (-2000, 11, 1000) is world offset (1000, 2000) at yaw 90.
        var centre = new Vec3Mm(5000, 300, -4000);

        // Act
        Vec3Mm world = Obb.ToWorld(new Vec3Mm(-2000, 11, 1000), in centre, Bam.Quarter);

        // Assert
        Assert.Equal(new Vec3Mm(6000, 311, -2000), world);
    }

    // ---- segments and the slab integration ----

    [Fact]
    public void SegmentToLocal_RotatesBothEndpoints()
    {
        // Arrange
        var centre = new Vec3Mm(5000, 0, 5000);
        var p0 = new Vec3Mm(5000, 500, 3000);
        var p1 = new Vec3Mm(5000, 500, 5000);

        // Act
        Obb.SegmentToLocal(in p0, in p1, in centre, Bam.Quarter, out Vec3Mm l0, out Vec3Mm l1);

        // Assert (dz = -2000 -> local x = 2000; dz = 0 -> 0)
        Assert.Equal(new Vec3Mm(2000, 500, 0), l0);
        Assert.Equal(new Vec3Mm(0, 500, 0), l1);
    }

    [Fact]
    public void YawedBox_SegmentTestedThroughToLocalAndSlab_HitsAtTheRotatedFace()
    {
        // Arrange: a vehicle box centred (5000, 0, 5000), yawed 90 degrees, local half extents x 1000, z 500, y 0..1000.
        //   World: its local x axis points north (-z), so it spans z 4000..6000 and x 4500..5500.
        //   Segment (5000, 500, 3000) -> (5000, 500, 5000) in local: (2000, 500, 0) -> (0, 500, 0).
        //   x slab: D = -2000: lo' = (-1000 - 2000) * 1000 = -3e6, hi' = (1000 - 2000) * 1000 = -1e6 -> (1e6, 3e6)/2000 -> (500, 1500).
        //   y, z: D = 0 and inside -> f = 500 (the world z = 4000 face is halfway along the 2000 mm segment).
        var centre = new Vec3Mm(5000, 0, 5000);
        var localBox = new AabbMm(new Vec3Mm(-1000, 0, -500), new Vec3Mm(1000, 1000, 500));
        Obb.SegmentToLocal(new Vec3Mm(5000, 500, 3000), new Vec3Mm(5000, 500, 5000), in centre, Bam.Quarter, out Vec3Mm l0, out Vec3Mm l1);

        // Act
        SegmentInterval r = SegmentIntervals.Slab(in l0, in l1, in localBox);

        // Assert
        Assert.True(r.Hit);
        Assert.Equal(500, r.EnterPm);
        Assert.Equal(1500, r.ExitPm);
    }
}

/// <summary>Zero-allocation check for <see cref="Obb"/> (AC-8). Non-parallel; skipped on the unoptimised Sim like the other trig tests (TD-008).</summary>
[Collection(NonParallelCollection.Name)]
public class ObbAllocationTests
{
    [Fact]
    public void ToLocalAndToWorld_WarmedUp_AllocateNothing()
    {
        // Arrange: the Q15 sine table is a ReadOnlySpan literal that allocates at the unoptimised JIT (TD-008).
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(TrigQ15).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: RuntimeHelpers.CreateSpan (ReadOnlySpan literal table) allocates at the unoptimised JIT; the 0 B guardrail applies to optimised builds only.");
        var centre = new Vec3Mm(1000, 0, -1000);
        var p = new Vec3Mm(15_000, 200, -12_000);
        int sink = 0;

        // Act + Assert
        _ = AllocAssert.Zero("20,000 ToLocal/ToWorld calls", () =>
        {
            for (int i = 0; i < 5000; i++)
            {
                Vec3Mm local = Obb.ToLocal(in p, in centre, (ushort)(i * 13));
                Vec3Mm back = Obb.ToWorld(in local, in centre, (ushort)(i * 13));
                Obb.SegmentToLocal(in p, in back, in centre, (ushort)i, out Vec3Mm l0, out Vec3Mm l1);
                sink += back.X + l0.Z + l1.X;
            }
        });

        Assert.NotEqual(int.MinValue, sink);
    }
}
