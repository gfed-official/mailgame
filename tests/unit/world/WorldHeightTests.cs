using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Covers the pure height-field kernel <see cref="WorldHeight"/> (world-stub story 001; TR-movement-005,
/// TR-movement-018 height half, TR-throwing-005 world half; ADR-0026 sections 2 to 3, ADR-0013 section 6).
/// Integer-only; runs unchanged on the net10.0 and net8.0 legs (T2).
/// </summary>
public class WorldHeightTests
{
    private const int S = 2000;

    private static readonly HeightCorners _fxBeach = new(-1000, -1000, -200, -200);

    private static WorldTables BuildTables(int tilesX, int tilesZ, params int[] corners)
    {
        var builder = new WorldTablesBuilder();
        builder.SetCorners(tilesX, tilesZ, corners);
        return builder.Build();
    }

    // ---- AC-1: FX-Beach ----

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Sample__fxBeachMidpoint_MinusSixHundred()
    {
        // Arrange (a tile whose corners are h00 = h10 = -1000, h01 = h11 = -200)
        HeightCorners h = _fxBeach;

        // Act
        int height = WorldHeight.Sample(in h, 500, 1000);

        // Assert
        Assert.Equal(-600, height);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Sample__fxBeachAtFz1375_MinusFourFifty()
    {
        // Arrange
        HeightCorners h = _fxBeach;

        // Act
        int height = WorldHeight.Sample(in h, 500, 1375);

        // Assert
        Assert.Equal(-450, height);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void At__fxBeachThroughWorld_MatchesDirectSample()
    {
        // Arrange
        WorldTables world = BuildTables(1, 1, -1000, -1000, -200, -200);

        // Act
        int viaAt = WorldHeight.At(world, 500, 1375);
        int direct = WorldHeight.Sample(in _fxBeach, 500, 1375);

        // Assert
        Assert.Equal(-450, viaAt);
        Assert.Equal(direct, viaAt);
    }

    // ---- AC-2: one rounding rule (RH), golden vectors, both signs ----

    [Theory]
    [MemberData(nameof(GoldenRows))]
    [Trait("Id", "PCM-F7-1")]
    public void Sample_GoldenVector_MatchesCommittedValue(int h00, int h10, int h01, int h11, int fx, int fz, int expected)
    {
        // Arrange
        var h = new HeightCorners(h00, h10, h01, h11);

        // Act
        int actual = WorldHeight.Sample(in h, fx, fz);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(GoldenRows))]
    [Trait("Id", "PCM-F7-1")]
    public void Sample_NegatedCorners_NegatesResult(int h00, int h10, int h01, int h11, int fx, int fz, int expected)
    {
        // Arrange (round half away from zero is odd-symmetric: sample(-h) == -sample(h))
        var negated = new HeightCorners(-h00, -h10, -h01, -h11);

        // Act
        int actual = WorldHeight.Sample(in negated, fx, fz);

        // Assert
        Assert.Equal(-expected, actual);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Sample_ExactHalfPositive_RoundsAwayFromZero()
    {
        // Arrange (2 * 1000 * 1000 / 4,000,000 = 0.5; truncation would give 0)
        var h = new HeightCorners(2, 0, 0, 0);

        // Act
        int actual = WorldHeight.Sample(in h, 1000, 1000);

        // Assert
        Assert.Equal(1, actual);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Sample_ExactHalfNegative_RoundsAwayFromZero()
    {
        // Arrange (-0.5; floor would give -1 for the wrong reason, truncation 0)
        var h = new HeightCorners(-2, 0, 0, 0);

        // Act
        int actual = WorldHeight.Sample(in h, 1000, 1000);

        // Assert
        Assert.Equal(-1, actual);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Sample_BelowHalf_RoundsTowardZeroBothSigns()
    {
        // Arrange (+/-0.25)
        var up = new HeightCorners(1, 0, 0, 0);
        var down = new HeightCorners(-1, 0, 0, 0);

        // Act
        int a = WorldHeight.Sample(in up, 1000, 1000);
        int b = WorldHeight.Sample(in down, 1000, 1000);

        // Assert
        Assert.Equal(0, a);
        Assert.Equal(0, b);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(-5000, 0)]
    [InlineData(int.MinValue, 0)]
    [InlineData(2001, 2000)]
    [InlineData(int.MaxValue, 2000)]
    [Trait("Id", "PCM-F7-1")]
    public void Sample_OffsetOutsideTile_BehavesAsClampedOffset(int rawOffset, int clampedOffset)
    {
        // Arrange
        var h = new HeightCorners(-1234, 567, -89, 4321);

        // Act
        int viaFx = WorldHeight.Sample(in h, rawOffset, 700);
        int viaFz = WorldHeight.Sample(in h, 700, rawOffset);

        // Assert
        Assert.Equal(WorldHeight.Sample(in h, clampedOffset, 700), viaFx);
        Assert.Equal(WorldHeight.Sample(in h, 700, clampedOffset), viaFz);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Sample_ExtremeCornerHeights_DoesNotOverflow()
    {
        // Arrange (int32 extremes times S^2 stays inside int64)
        var high = new HeightCorners(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
        var low = new HeightCorners(int.MinValue + 1, int.MinValue + 1, int.MinValue + 1, int.MinValue + 1);

        // Act
        int a = WorldHeight.Sample(in high, 1234, 567);
        int b = WorldHeight.Sample(in low, 1234, 567);

        // Assert
        Assert.Equal(int.MaxValue, a);
        Assert.Equal(int.MinValue + 1, b);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void At_PointsOutsideMap_ClampToMap()
    {
        // Arrange (a 2 x 1 map with distinct corners)
        WorldTables world = BuildTables(2, 1, 10, 20, 30, 40, 50, 60);

        // Act / Assert: below the minimum corner, beyond the maximum corner, and extreme ints
        Assert.Equal(10, WorldHeight.At(world, -1, -1));
        Assert.Equal(10, WorldHeight.At(world, int.MinValue, int.MinValue));
        Assert.Equal(60, WorldHeight.At(world, 4001, 2001));
        Assert.Equal(60, WorldHeight.At(world, int.MaxValue, int.MaxValue));
        Assert.Equal(40, WorldHeight.At(world, int.MinValue, int.MaxValue));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void At_FarMapEdge_UsesLastTileWithFullOffsetAndReturnsExactCorner()
    {
        // Arrange (2 x 2 map: edge rule is u = S at x = TilesX * S, so the far corner is exact)
        WorldTables world = BuildTables(2, 2, 0, 100, 200, 300, 400, 500, 600, 700, 801);

        // Act / Assert: far corner exactly, far edges continuous with the neighbour tile's own corner
        Assert.Equal(801, WorldHeight.At(world, 4000, 4000));
        Assert.Equal(200, WorldHeight.At(world, 4000, 0));
        Assert.Equal(600, WorldHeight.At(world, 0, 4000));
        Assert.Equal(700, WorldHeight.At(world, 2000, 4000));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void At_TileBoundary_SharedCornerSeenFromBothTiles()
    {
        // Arrange (x = 2000 belongs to tile 1 with offset 0; its value is the shared corner)
        WorldTables world = BuildTables(2, 1, 0, 1000, 2000, 0, 1000, 2000);

        // Act
        int left = WorldHeight.At(world, 1999, 0);
        int boundary = WorldHeight.At(world, 2000, 0);
        int right = WorldHeight.At(world, 2001, 0);

        // Assert
        Assert.Equal(1000, boundary);
        Assert.True(left <= boundary);
        Assert.True(right >= boundary);
    }

    // ---- AC-3: gradient ----

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Gradient_TileRisingAlongX_ReturnsUndividedNumerators()
    {
        // Arrange (1400 mm rise over 2000 mm along +X)
        var h = new HeightCorners(0, 1400, 0, 1400);

        // Act
        WorldHeight.Gradient(in h, 700, 900, out long gx, out long gz);

        // Assert (mm * mm: 1400 * 2000)
        Assert.Equal(2_800_000L, gx);
        Assert.Equal(0L, gz);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Gradient_GeneralTile_MatchesFormulaAtInteriorPoint()
    {
        // Arrange (gx = (h10-h00)(S-fz) + (h11-h01) fz ; gz = (h01-h00)(S-fx) + (h11-h10) fx)
        var h = new HeightCorners(-1234, 567, -89, 4321);

        // Act
        WorldHeight.Gradient(in h, 500, 1375, out long gx, out long gz);

        // Assert (hand-computed: 1801*625 + 4410*1375 ; 1145*1500 + 3754*500)
        Assert.Equal(7_189_375L, gx);
        Assert.Equal(3_594_500L, gz);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Gradient_OffsetOutsideTile_BehavesAsClampedOffset()
    {
        // Arrange
        var h = new HeightCorners(-1234, 567, -89, 4321);

        // Act
        WorldHeight.Gradient(in h, -50, 99_999, out long gx, out long gz);
        WorldHeight.Gradient(in h, 0, S, out long ex, out long ez);

        // Assert
        Assert.Equal(ex, gx);
        Assert.Equal(ez, gz);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Gradient_FlatTile_IsZero()
    {
        // Arrange
        var h = new HeightCorners(300, 300, 300, 300);

        // Act
        WorldHeight.Gradient(in h, 123, 1777, out long gx, out long gz);

        // Assert
        Assert.Equal(0L, gx);
        Assert.Equal(0L, gz);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void IsWalkableSlope_Rise1400Over2000_AcceptedAt700()
    {
        // Arrange
        var h = new HeightCorners(0, 1400, 0, 1400);
        WorldHeight.Gradient(in h, 0, 0, out long gx, out long gz);

        // Act
        bool walkable = WorldHeight.IsWalkableSlope(gx, gz, 700);

        // Assert (exactly 700 permille is allowed)
        Assert.True(walkable);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void IsWalkableSlope_Rise1404Over2000_RejectedAt700()
    {
        // Arrange
        var h = new HeightCorners(0, 1404, 0, 1404);
        WorldHeight.Gradient(in h, 0, 0, out long gx, out long gz);

        // Act
        bool walkable = WorldHeight.IsWalkableSlope(gx, gz, 700);

        // Assert
        Assert.False(walkable);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void IsWalkableSlope_DiagonalSlope_DecidedOnExactNumerators()
    {
        // Arrange: gx = gz = n * 4000 means n permille on each axis (slope = g / S^2), n*sqrt(2) combined.
        // 494 * sqrt(2) = 698.6 (walkable), 495 * sqrt(2) = 700.04 (not), though each axis alone is below 700.
        long below = 494L * 4000;
        long above = 495L * 4000;

        // Act
        bool a = WorldHeight.IsWalkableSlope(below, below, 700);
        bool b = WorldHeight.IsWalkableSlope(above, above, 700);
        bool c = WorldHeight.IsWalkableSlope(-below, below, 700);

        // Assert
        Assert.True(a);
        Assert.False(b);
        Assert.True(c);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void IsWalkableSlope_FlatAndNegativeLimit_Handled()
    {
        // Act / Assert
        Assert.True(WorldHeight.IsWalkableSlope(0, 0, 0));
        Assert.True(WorldHeight.IsWalkableSlope(0, 0, 700));
        Assert.False(WorldHeight.IsWalkableSlope(0, 0, -1));
        Assert.False(WorldHeight.IsWalkableSlope(1, 0, 0));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void IsWalkableSlope_HugeNumerators_NoOverflowAndRejected()
    {
        // Act (|g| near 1.7e13 is the int32-corner worst case; squares need Int128)
        bool walkable = WorldHeight.IsWalkableSlope(4_294_967_295L * S, -4_294_967_295L * S, 700);

        // Assert
        Assert.False(walkable);
    }

    public static IEnumerable<object[]> GoldenRows() => HeightGoldenVectors.TheoryRows();
}
