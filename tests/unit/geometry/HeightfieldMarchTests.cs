using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.Geometry;

/// <summary>
/// Covers <see cref="HeightfieldMarch"/> (player-character story 004; TR-movement-005, TR-movement-011; Throwing F4
/// heightfield march; ADR-0013 section 6, ADR-0026 section 2). Terrain comes from the real <see cref="WorldTables"/>
/// kernel built through <see cref="WorldTablesBuilder"/> (the FX-Dry / FX-Ramp shapes, until world-stub story 006 adds the
/// shared fixture). Golden rows are hand-derived in <see cref="GeometryGoldenVectors"/>. Integer-only; net10.0 and net8.0.
/// </summary>
public class HeightfieldMarchTests
{
    private const int Tiles = 8;

    /// <summary>A flat plane at <paramref name="heightMm"/> over 8 x 8 tiles (16 000 mm square).</summary>
    private static WorldTables Flat(int heightMm)
    {
        int[] corners = new int[(Tiles + 1) * (Tiles + 1)];
        Array.Fill(corners, heightMm);
        var builder = new WorldTablesBuilder();
        builder.SetCorners(Tiles, Tiles, corners);
        return builder.Build();
    }

    /// <summary>FX-Ramp: one 2000 mm tile rising <paramref name="riseMm"/> along +X (h00 = h01 = 0, h10 = h11 = rise).</summary>
    private static WorldTables Ramp(int riseMm)
    {
        var builder = new WorldTablesBuilder();
        builder.SetCorners(1, 1, new[] { 0, riseMm, 0, riseMm });
        return builder.Build();
    }

    // ---- AC-6: golden vectors ----

    [Theory]
    [MemberData(nameof(GeometryGoldenVectors.MarchIndices), MemberType = typeof(GeometryGoldenVectors))]
    [Trait("Id", "TC-F4-1")]
    public void FirstContact_GoldenVector_MatchesHandDerivation(int index)
    {
        // Arrange
        MarchVector v = GeometryGoldenVectors.March[index];
        WorldTables world = Flat(v.Plane);

        // Act
        bool hit = HeightfieldMarch.FirstContact(world, v.P0, v.P1, out int f);

        // Assert
        Assert.True(v.Hit == hit, $"{v.Name}: Hit expected {v.Hit}, got {hit}");
        Assert.True(v.FractionPm == f, $"{v.Name}: f expected {v.FractionPm}, got {f}");
    }

    [Fact]
    [Trait("Id", "TC-F4-1")]
    public void FirstContact_DescendingSegmentAcrossFxRamp_ContactAtFraction589()
    {
        // Arrange: FX-Ramp 1400 over 2000 -> H(x) = 0.7 x. P0 = (0, 1000, 1000), D = (1000, -1000, 0): |Dxz| = 1000 -> 4, |Dy| = 1000 -> 4, n = 4.
        //   Q_i = (250 i, 1000 - 250 i, 1000), H(Q_i) = 175 i exactly -> gap_i = 1000 - 425 i = 1000, 575, 150, -275.
        //   First <= 0 at i = 3: gapPrev = 150, gap = -275, drop = 425.
        //   f = ceil(1000 * (2 * 425 + 150) / (4 * 425)) = ceil(1,000,000 / 1700) = ceil(588.24) = 589.
        WorldTables world = Ramp(1400);

        // Act
        bool hit = HeightfieldMarch.FirstContact(world, new Vec3Mm(0, 1000, 1000), new Vec3Mm(1000, 0, 1000), out int f);

        // Assert
        Assert.True(hit);
        Assert.Equal(589, f);
    }

    [Fact]
    public void FirstContact_LevelSegmentIntoTheRamp_ContactAtFraction715()
    {
        // Arrange: a chest-height ray into the hillside. FX-Ramp H(x) = 0.7 x; P0 = (0, 500, 1000), D = (1000, 0, 0): n = max(4, 0) = 4.
        //   Q_i = (250 i, 500), H = 175 i -> gap_i = 500 - 175 i = 500, 325, 150, -25. First <= 0 at i = 3: gapPrev = 150, gap = -25, drop = 175.
        //   f = ceil(1000 * (2 * 175 + 150) / (4 * 175)) = ceil(500000 / 700) = ceil(714.29) = 715.
        WorldTables world = Ramp(1400);

        // Act
        bool hit = HeightfieldMarch.FirstContact(world, new Vec3Mm(0, 500, 1000), new Vec3Mm(1000, 500, 1000), out int f);

        // Assert
        Assert.True(hit);
        Assert.Equal(715, f);
    }

    [Fact]
    public void FirstContact_RisingSegmentShallowerThanTheRamp_ContactAtFraction834()
    {
        // Arrange: P0 = (0, 500, 1000), D = (1000, 100, 0): n = max(4, ceil(100/250) = 1) = 4. Q_i y = 500 + 25 i, H = 175 i
        //   -> gap_i = 500 - 150 i = 500, 350, 200, 50, -100. First <= 0 at i = 4: gapPrev = 50, gap = -100, drop = 150.
        //   f = ceil(1000 * (3 * 150 + 50) / (4 * 150)) = ceil(500000 / 600) = ceil(833.33) = 834.
        WorldTables world = Ramp(1400);

        // Act
        bool hit = HeightfieldMarch.FirstContact(world, new Vec3Mm(0, 500, 1000), new Vec3Mm(1000, 600, 1000), out int f);

        // Assert
        Assert.True(hit);
        Assert.Equal(834, f);
    }

    [Fact]
    public void IsWithinExtent_BoundaryValues()
    {
        // Assert: +-65536 per component is inside, one more is out, on any axis.
        Assert.True(HeightfieldMarch.IsWithinExtent(0, -126, -456));
        Assert.True(HeightfieldMarch.IsWithinExtent(65_536, -65_536, 65_536));
        Assert.False(HeightfieldMarch.IsWithinExtent(65_537, 0, 0));
        Assert.False(HeightfieldMarch.IsWithinExtent(0, -65_537, 0));
        Assert.False(HeightfieldMarch.IsWithinExtent(0, 0, 65_537));
        Assert.False(HeightfieldMarch.IsWithinExtent(0, 0, long.MinValue));
    }

    [Fact]
    public void FirstContact_RampSegmentThatStaysAbove_DoesNotTouch()
    {
        // Arrange: the same line at y = 1000 -> 800 stays above 0.7 x (700 at x = 1000).
        WorldTables world = Ramp(1400);

        // Act
        bool hit = HeightfieldMarch.FirstContact(world, new Vec3Mm(0, 1000, 1000), new Vec3Mm(1000, 800, 1000), out int f);

        // Assert (Q_i.y = 1000 - 50 i, H = 175 i -> gap_i = 1000 - 225 i; gap_4 = 100 > 0)
        Assert.False(hit);
        Assert.Equal(0, f);
    }

    [Fact]
    public void FirstContact_StartsBelowTheRamp_IsImmediateContactAtZero()
    {
        // Arrange: P0 = (1000, 100, 1000): H = 700, gap_0 = -600.
        WorldTables world = Ramp(1400);

        // Act
        bool hit = HeightfieldMarch.FirstContact(world, new Vec3Mm(1000, 100, 1000), new Vec3Mm(1000, 5000, 1000), out int f);

        // Assert
        Assert.True(hit);
        Assert.Equal(0, f);
    }

    [Fact]
    public void FirstContact_StartingExactlyOnTheTerrain_IsContactAtZero()
    {
        // Arrange: gap_0 = 0 <= 0.
        WorldTables world = Flat(500);

        // Act
        bool hit = HeightfieldMarch.FirstContact(world, new Vec3Mm(3000, 500, 3000), new Vec3Mm(3000, 900, 3000), out int f);

        // Assert
        Assert.True(hit);
        Assert.Equal(0, f);
    }

    [Fact]
    public void FirstContact_BeyondTheMapEdge_UsesTheClampedHeight()
    {
        // Arrange: a 1 x 1 tile map at 880; the segment runs outside it (WorldHeight.At clamps onto the map).
        //   P0 = (-5000, 1000, -5000), D = (0, -200, 0): n = max(1, 0, ceil(200/250) = 1) = 1; gap_0 = 120, gap_1 = -80, drop = 200.
        //   f = ceil(1000 * (0 + 120) / (1 * 200)) = 600.
        var builder = new WorldTablesBuilder();
        builder.SetCorners(1, 1, new[] { 880, 880, 880, 880 });
        WorldTables world = builder.Build();

        // Act
        bool hit = HeightfieldMarch.FirstContact(world, new Vec3Mm(-5000, 1000, -5000), new Vec3Mm(-5000, 800, -5000), out int f);

        // Assert
        Assert.True(hit);
        Assert.Equal(600, f);
    }

    [Fact]
    public void FirstContact_SegmentParallelToTheRampSurface_NeverTouches()
    {
        // Arrange: the segment runs 1 mm above the surface (slope 0.7): P0 = (0, 1, 1000) -> P1 = (1000, 701, 1000).
        //   n = max(ceil(1000/250) = 4, ceil(700/250) = 3) = 4; Q_i = (250 i, 1 + 175 i), H = 175 i -> gap_i = 1 > 0 at every sample.
        WorldTables world = Ramp(1400);

        // Act
        bool hit = HeightfieldMarch.FirstContact(world, new Vec3Mm(0, 1, 1000), new Vec3Mm(1000, 701, 1000), out _);

        // Assert
        Assert.False(hit);
    }

    // ---- sample count and fraction helpers ----

    [Theory]
    [InlineData(2000, 0, 0, 0, 1)]            // zero-length: max(1, 0, 0)
    [InlineData(2000, 0, -126, -456, 2)]      // F4 example flight: ceil(456/250) = 2
    [InlineData(2000, 900, -600, 0, 4)]       // TC-F4-1h: ceil(900/250) = 4 beats ceil(600/250) = 3
    [InlineData(2000, 0, -251, 0, 2)]         // vertical only: ceil(251/250) = 2
    [InlineData(2000, 0, 250, 0, 1)]
    [InlineData(2000, 250, 0, 1, 2)]          // |Dxz| = sqrt(62501) = 250.002 -> must round UP to 251 -> 2 (a floor sqrt would give 1)
    [InlineData(2000, 250, 0, 0, 1)]          // exactly 250
    [InlineData(2000, 300, 0, 400, 2)]        // 3-4-5: 500 -> 2
    [InlineData(2000, 1, 0, 1, 1)]            // sqrt(2) = 1.41 -> 2 -> ceil(2/250) = 1
    [InlineData(400, 201, 0, 0, 2)]           // cell 400: step = min(200, 250) = 200 -> ceil(201/200) = 2
    [InlineData(400, 200, 0, 0, 1)]
    [InlineData(1, 5, 0, 0, 5)]               // degenerate cell: step floors at 1
    [InlineData(100_000, 750, 0, 0, 3)]       // huge cell: step capped at 250
    public void SampleCount_FollowsTheF4Formula(int cell, long dx, long dy, long dz, int expected)
    {
        // Act
        int n = HeightfieldMarch.SampleCount(cell, dx, dy, dz);

        // Assert
        Assert.Equal(expected, n);
    }

    [Theory]
    [InlineData(2, 4, 100, -50, 417)]    // TC-F4-1h: ceil(1000 * (150 + 100) / (4 * 150)) = ceil(416.67)
    [InlineData(1, 1, 50, 0, 1000)]      // lands exactly on the surface at the segment end
    [InlineData(1, 2, 63, 0, 500)]       // ceil(1000 * 63 / (2 * 63))
    [InlineData(3, 4, 150, -275, 589)]   // the ramp row: ceil(1000 * (2 * 425 + 150) / (4 * 425))
    [InlineData(1, 3, 1, -1, 167)]       // ceil(1000 * 1 / (3 * 2)) = ceil(166.67)
    public void ContactFraction_MatchesTheClosedForm(int i, int n, long gapPrev, long gap, int expected)
    {
        // Act
        int f = HeightfieldMarch.ContactFraction(i, n, gapPrev, gap);

        // Assert
        Assert.Equal(expected, f);
    }

    // ---- bounds ----

    [Fact]
    public void FirstContact_SegmentBeyondTheMarchExtent_IsRejected()
    {
        // Arrange: |Dy| = 70000 > 65536.
        WorldTables world = Flat(0);
        var p0 = new Vec3Mm(1000, 80_000, 1000);
        var p1 = new Vec3Mm(1000, 10_000, 1000);

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => HeightfieldMarch.FirstContact(world, in p0, in p1, out _));
            Assert.Equal(SimAssertCode.GeometryMarchRange, ex.Code);
        }
        else
        {
            bool hit = HeightfieldMarch.FirstContact(world, in p0, in p1, out int f);
            Assert.False(hit);
            Assert.Equal(0, f);
        }
    }

    [Fact]
    public void FirstContact_AtTheMarchExtentLimit_IsAccepted()
    {
        // Arrange: |Dy| = 65536 exactly, straight down onto a flat plane at 0 (n = ceil(65536/250) = 263).
        WorldTables world = Flat(0);

        // Act
        bool hit = HeightfieldMarch.FirstContact(world, new Vec3Mm(1000, 65_536, 1000), new Vec3Mm(1000, 0, 1000), out int f);

        // Assert (y_i = 65536 - RH(65536 i / 263); y_263 = 0 -> gap 0 at the last sample; gapPrev = y_262 = 65536 - RH(65286.83) = 65536 - 65287 = 249;
        //   f = ceil(1000 * (262 * 249 + 249) / (263 * 249)) = 1000)
        Assert.True(hit);
        Assert.Equal(1000, f);
    }

    [Fact]
    public void ExtentConstants_AreTheDocumentedBounds()
    {
        Assert.Equal(65_536, HeightfieldMarch.MaxSegmentExtentMm);
        Assert.Equal(250, HeightfieldMarch.MaxStepMm);
    }
}

/// <summary>Zero-allocation check for <see cref="HeightfieldMarch"/> (AC-8). Non-parallel so no other test disturbs the window.</summary>
[Collection(NonParallelCollection.Name)]
public class HeightfieldMarchAllocationTests
{
    [Fact]
    public void FirstContact_WarmedUp_AllocatesNothing()
    {
        // Arrange
        int[] corners = new int[9 * 9];
        Array.Fill(corners, 880);
        var builder = new WorldTablesBuilder();
        builder.SetCorners(8, 8, corners);
        WorldTables world = builder.Build();
        var p0 = new Vec3Mm(10250, 963, 10570);
        var p1 = new Vec3Mm(10250, 837, 10114);
        var q1 = new Vec3Mm(10250, 1963, 10570);
        var q2 = new Vec3Mm(10250, 1837, 10114);
        int sink = 0;

        // Act + Assert
        _ = AllocAssert.Zero("10,000 marches (contact and no-contact)", () =>
        {
            for (int i = 0; i < 5000; i++)
            {
                _ = HeightfieldMarch.FirstContact(world, in p0, in p1, out int a);
                _ = HeightfieldMarch.FirstContact(world, in q1, in q2, out int b);
                sink += a + b;
            }
        });

        Assert.NotEqual(int.MinValue, sink);
    }
}
