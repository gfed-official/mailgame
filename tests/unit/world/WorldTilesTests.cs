using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Covers tile flags (precomputed Walkable, Water hint, Blocked, Road, Lot) and map-bounds validation of
/// <see cref="WorldTablesBuilder"/> / <see cref="WorldTables"/> (world-stub story 002; ADR-0026 sections 3 and 4;
/// TR-movement-006 slope 700 permille, TR-movement-018 swim-enter 900 mm). The 700 and 900 here are test inputs
/// standing in for the players SimSettings values that the composition story passes.
/// </summary>
public class WorldTilesTests
{
    private const int SlopeMaxPm = 700;
    private const int SwimEnterMm = 900;

    private static WorldTablesBuilder SingleTile(int h00, int h10, int h01, int h11)
    {
        var builder = new WorldTablesBuilder();
        builder.SetCorners(1, 1, new[] { h00, h10, h01, h11 });
        return builder;
    }

    private static TileFlags FlagsOf(int h00, int h10, int h01, int h11)
    {
        return SingleTile(h00, h10, h01, h11).Build(SlopeMaxPm, SwimEnterMm).Tile(0, 0);
    }

    // ---- TileFlags contract ----

    [Fact]
    public void TileFlags_Values_ArePinnedToAdr0026()
    {
        Assert.Equal((byte)0, (byte)TileFlags.None);
        Assert.Equal((byte)1, (byte)TileFlags.Blocked);
        Assert.Equal((byte)2, (byte)TileFlags.Water);
        Assert.Equal((byte)4, (byte)TileFlags.Walkable);
        Assert.Equal((byte)8, (byte)TileFlags.Road);
        Assert.Equal((byte)16, (byte)TileFlags.Lot);
    }

    // ---- AC-1: Walkable precompute ----

    [Fact]
    public void Ac1_Walkable_Rise1400Over2000_Is700PermilleAndWalkable()
    {
        TileFlags f = FlagsOf(0, 1400, 0, 1400);

        Assert.True(f.HasFlag(TileFlags.Walkable));
    }

    [Fact]
    public void Ac1_Walkable_Rise1404Over2000_Is702PermilleAndNotWalkable()
    {
        TileFlags f = FlagsOf(0, 1404, 0, 1404);

        Assert.False(f.HasFlag(TileFlags.Walkable));
    }

    [Fact]
    public void Ac1_Walkable_SteepAlongZ_UsesBothAxes()
    {
        Assert.True(FlagsOf(0, 0, 1400, 1400).HasFlag(TileFlags.Walkable));
        Assert.False(FlagsOf(0, 0, 1404, 1404).HasFlag(TileFlags.Walkable));
    }

    [Fact]
    public void Ac1_Walkable_DiagonalSlopeUsesVectorMagnitude()
    {
        // 989/2000 on each axis -> 699.3 permille (walkable); 1000/2000 on each axis -> 707.1 permille (not).
        Assert.True(FlagsOf(0, 989, 989, 1978).HasFlag(TileFlags.Walkable));
        Assert.False(FlagsOf(0, 1000, 1000, 2000).HasFlag(TileFlags.Walkable));
    }

    [Fact]
    public void Ac1_Walkable_SlopeThatRoundsTo700_IsStillNotWalkable()
    {
        // 990/2000 on each axis -> 700.04 permille: rounds to 700 but the exact numerator test rejects it, so a
        // walkability decided from the rounded TerrainGradientPm would wrongly pass this tile.
        Assert.False(FlagsOf(0, 990, 990, 1980).HasFlag(TileFlags.Walkable));
    }

    [Fact]
    public void Ac1_Walkable_SteepAtSeveralCorners_IsNotWalkable()
    {
        // h11 = 1500 only: 0 at the (0,0) corner, 750 permille at (S,0) and (0,S), 1060.7 permille at (S,S).
        Assert.False(FlagsOf(0, 0, 0, 1500).HasFlag(TileFlags.Walkable));
        Assert.True(FlagsOf(0, 0, 0, 989).HasFlag(TileFlags.Walkable)); // 989 on both axes at (S,S): 699.3 permille
    }

    [Theory]
    [InlineData(1100, 0, 0, 0)]
    [InlineData(0, 1100, 0, 0)]
    [InlineData(0, 0, 1100, 0)]
    [InlineData(0, 0, 0, 1100)]
    public void Ac1_Walkable_SteepAtExactlyOneCorner_IsNotWalkable(int h00, int h10, int h01, int h11)
    {
        // The raised corner sees 1100/2000 on both axes (777.8 permille); its two neighbours see 550 permille on
        // one axis and the opposite corner sees 0, so only one of the four corner checks rejects the tile.
        Assert.False(FlagsOf(h00, h10, h01, h11).HasFlag(TileFlags.Walkable));
    }

    [Fact]
    public void Ac1_Flags_NonSquareMap_UseTilesXAsTheRowStride()
    {
        // 3 x 2 tiles (4 corners per row): corner (3, 2) belongs only to tile (2, 1), so only that tile is steep;
        // tile (0, 1) is Blocked. A tilesZ stride or swapped axes would put these flags on the wrong tiles.
        var b = new WorldTablesBuilder();
        b.SetCorners(3, 2, new[]
        {
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 3000,
        });
        b.MarkBlocked(0, 1);
        IWorldQuery w = b.Build(SlopeMaxPm, SwimEnterMm);

        Assert.Equal(TileFlags.Walkable, w.Tile(0, 0));
        Assert.Equal(TileFlags.Walkable, w.Tile(1, 0));
        Assert.Equal(TileFlags.Walkable, w.Tile(2, 0));
        Assert.Equal(TileFlags.Blocked, w.Tile(0, 1));
        Assert.Equal(TileFlags.Walkable, w.Tile(1, 1));
        Assert.Equal(TileFlags.None, w.Tile(2, 1));
        Assert.Equal(TileFlags.Blocked, w.Tile(3, 0)); // outside along X
        Assert.Equal(TileFlags.Blocked, w.Tile(0, 2)); // outside along Z

        // Snap on the same map: the steep tile (2, 1) centre is rejected; (1, 1) and (2, 0) centres are found.
        Assert.False(w.TrySnapWalkable(new Vec3Mm(5000, 0, 3000), 1000, out _));
        Assert.True(w.TrySnapWalkable(new Vec3Mm(3000, 0, 3000), 1000, out Vec3Mm s11));
        Assert.Equal(new Vec3Mm(3000, (int)w.TerrainHeightMm(3000, 3000), 3000), s11);
        Assert.True(w.TrySnapWalkable(new Vec3Mm(5000, 0, 1900), 1000, out Vec3Mm s20));
        Assert.Equal(new Vec3Mm(5000, (int)w.TerrainHeightMm(5000, 1000), 1000), s20);
    }

    [Fact]
    public void Ac1_Walkable_SlopeLimitComesFromBuildParameter()
    {
        WorldTablesBuilder b = SingleTile(0, 1404, 0, 1404);

        Assert.False(b.Build(701, SwimEnterMm).Tile(0, 0).HasFlag(TileFlags.Walkable));
        Assert.True(b.Build(702, SwimEnterMm).Tile(0, 0).HasFlag(TileFlags.Walkable));
        Assert.False(b.Build(0, SwimEnterMm).Tile(0, 0).HasFlag(TileFlags.Walkable));
    }

    [Fact]
    public void Ac1_Walkable_FlatTile_IsWalkableEvenWithZeroSlopeLimit()
    {
        Assert.True(SingleTile(0, 0, 0, 0).Build(0, SwimEnterMm).Tile(0, 0).HasFlag(TileFlags.Walkable));
    }

    [Fact]
    public void Ac1_Walkable_BlockedFlatTile_IsNeverWalkable()
    {
        WorldTablesBuilder b = SingleTile(0, 0, 0, 0);
        b.MarkBlocked(0, 0);

        TileFlags f = b.Build(SlopeMaxPm, SwimEnterMm).Tile(0, 0);

        Assert.True(f.HasFlag(TileFlags.Blocked));
        Assert.False(f.HasFlag(TileFlags.Walkable));
    }

    [Fact]
    public void Ac1_Walkable_CentreDepth899_IsWalkable()
    {
        TileFlags f = FlagsOf(-899, -899, -899, -899);

        Assert.True(f.HasFlag(TileFlags.Walkable));
        Assert.True(f.HasFlag(TileFlags.Water));
    }

    [Fact]
    public void Ac1_Walkable_CentreDepth900_IsNotWalkable()
    {
        TileFlags f = FlagsOf(-900, -900, -900, -900);

        Assert.False(f.HasFlag(TileFlags.Walkable));
        Assert.True(f.HasFlag(TileFlags.Water));
    }

    [Fact]
    public void Ac1_Walkable_DepthIsMeasuredAtTheCentreNotTheCorners()
    {
        // Corners -1000 on one edge and 0 on the other: centre height -500 (depth 500) -> walkable although a corner is 1000 deep.
        // Slope 1000/2000 = 500 permille.
        Assert.True(FlagsOf(-1000, -1000, 0, 0).HasFlag(TileFlags.Walkable));

        // Centre of (-1000, -1000, -800, -800) is -900 -> depth 900 -> not walkable.
        Assert.False(FlagsOf(-1000, -1000, -800, -800).HasFlag(TileFlags.Walkable));
        Assert.True(FlagsOf(-1000, -1000, -798, -798).HasFlag(TileFlags.Walkable));
    }

    [Fact]
    public void Ac1_Walkable_SwimEnterComesFromBuildParameter()
    {
        WorldTablesBuilder b = SingleTile(-900, -900, -900, -900);

        Assert.False(b.Build(SlopeMaxPm, 900).Tile(0, 0).HasFlag(TileFlags.Walkable));
        Assert.True(b.Build(SlopeMaxPm, 901).Tile(0, 0).HasFlag(TileFlags.Walkable));
        Assert.False(b.Build(SlopeMaxPm, 0).Tile(0, 0).HasFlag(TileFlags.Walkable));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, false)]
    [InlineData(-1, 0, 0, 0, true)]
    [InlineData(0, -1, 0, 0, true)]
    [InlineData(0, 0, -1, 0, true)]
    [InlineData(0, 0, 0, -1, true)]
    public void Ac1_Water_IsSetIffAnyCornerBelowZero(int h00, int h10, int h01, int h11, bool expectedWater)
    {
        TileFlags f = FlagsOf(h00, h10, h01, h11);

        Assert.Equal(expectedWater, f.HasFlag(TileFlags.Water));
    }

    [Fact]
    public void Ac1_Water_IsAHintOnly_FlaggedTileCanHaveDryPointsWithZeroDepth()
    {
        // One corner under water (-100), the rest high: the tile is Water-flagged yet the far corner is dry land.
        IWorldQuery w = SingleTile(-100, 100, 100, 100).Build(SlopeMaxPm, SwimEnterMm);

        long height = w.TerrainHeightMm(2000, 2000);

        Assert.True(w.Tile(0, 0).HasFlag(TileFlags.Water));
        Assert.True(height >= 0);
        Assert.Equal(0, w.WaterDepthMm(2000, 2000));
        Assert.True(w.WaterDepthMm(0, 0) > 0); // the authority still reports the wet corner
    }

    [Fact]
    public void Ac1_Flags_AreComputedPerTileRowMajor()
    {
        // 2 x 2 tiles: only tile (1, 0) is steep; only tile (0, 1) is Blocked.
        var b = new WorldTablesBuilder();
        b.SetCorners(2, 2, new[]
        {
            0, 0, 3000,
            0, 0, 3000,
            0, 0, 0,
        });
        b.MarkBlocked(0, 1);
        IWorldQuery w = b.Build(SlopeMaxPm, SwimEnterMm);

        Assert.Equal(TileFlags.Walkable, w.Tile(0, 0));
        Assert.Equal(TileFlags.None, w.Tile(1, 0));
        Assert.Equal(TileFlags.Blocked, w.Tile(0, 1) & TileFlags.Blocked);
        Assert.False(w.Tile(0, 1).HasFlag(TileFlags.Walkable));
        Assert.Equal(TileFlags.None, w.Tile(1, 1)); // gradient 3000/2000 on the far side
    }

    [Fact]
    public void Ac1_Marks_RoadAndLotAreIndependentHints()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(3, 1, new int[8]);
        b.MarkRoad(0, 0);
        b.MarkLot(1, 0);
        b.MarkRoad(2, 0);
        b.MarkLot(2, 0);
        b.MarkBlocked(2, 0);
        IWorldQuery w = b.Build(SlopeMaxPm, SwimEnterMm);

        Assert.Equal(TileFlags.Road | TileFlags.Walkable, w.Tile(0, 0));
        Assert.Equal(TileFlags.Lot | TileFlags.Walkable, w.Tile(1, 0));
        Assert.Equal(TileFlags.Road | TileFlags.Lot | TileFlags.Blocked, w.Tile(2, 0));
    }

    [Fact]
    public void Ac1_Marks_DuplicateMarksAreIdempotent()
    {
        WorldTablesBuilder b = SingleTile(0, 0, 0, 0);
        b.MarkBlocked(0, 0);
        b.MarkBlocked(0, 0);

        Assert.Equal(TileFlags.Blocked, b.Build(SlopeMaxPm, SwimEnterMm).Tile(0, 0));
    }

    [Fact]
    public void Ac1_Tile_OutsideTheMap_ReportsBlockedAndNeverWalkable()
    {
        IWorldQuery w = SingleTile(0, 0, 0, 0).Build(SlopeMaxPm, SwimEnterMm);

        Assert.Equal(TileFlags.Blocked, w.Tile(-1, 0));
        Assert.Equal(TileFlags.Blocked, w.Tile(0, -1));
        Assert.Equal(TileFlags.Blocked, w.Tile(1, 0));
        Assert.Equal(TileFlags.Blocked, w.Tile(0, 1));
        Assert.Equal(TileFlags.Blocked, w.Tile(int.MinValue, int.MaxValue));
    }

    [Fact]
    public void Ac1_Build_WithoutWalkabilityLimits_NeverSetsWalkable()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(2, 1, new int[6]);

        WorldTables w = b.Build();

        Assert.Equal(TileFlags.None, w.Tile(0, 0));
        Assert.Equal(TileFlags.None, w.Tile(1, 0));
    }

    [Fact]
    public void Ac1_Build_NegativeWalkabilityLimits_ThrowWithOwnCode()
    {
        WorldTablesBuilder b = SingleTile(0, 0, 0, 0);

        Assert.Equal(SimBuildErrorCode.WorldInvalidWalkabilityLimit, Assert.Throws<SimBuildException>(() => b.Build(-1, 900)).Code);
        Assert.Equal(SimBuildErrorCode.WorldInvalidWalkabilityLimit, Assert.Throws<SimBuildException>(() => b.Build(700, -1)).Code);
    }

    [Fact]
    public void Ac1_Marks_OutsideTheMap_ThrowAtBuildWithOwnCode()
    {
        foreach ((int tx, int tz) in new[] { (-1, 0), (0, -1), (1, 0), (0, 1) })
        {
            WorldTablesBuilder b = SingleTile(0, 0, 0, 0);
            b.MarkLot(tx, tz);

            Assert.Equal(SimBuildErrorCode.WorldTileMarkOutOfRange, Assert.Throws<SimBuildException>(() => b.Build(SlopeMaxPm, SwimEnterMm)).Code);
        }
    }

    [Fact]
    public void Ac1_Marks_MayBeSetBeforeTheCorners()
    {
        var b = new WorldTablesBuilder();
        b.MarkBlocked(1, 0);
        b.SetCorners(2, 1, new int[6]);

        Assert.True(b.Build(SlopeMaxPm, SwimEnterMm).Tile(1, 0).HasFlag(TileFlags.Blocked));
    }

    [Fact]
    public void Ac1_Build_IsImmutableAndRepeatable_LaterMarksDoNotChangeEarlierTables()
    {
        WorldTablesBuilder b = SingleTile(0, 0, 0, 0);
        WorldTables first = b.Build(SlopeMaxPm, SwimEnterMm);

        b.MarkBlocked(0, 0);
        WorldTables second = b.Build(SlopeMaxPm, SwimEnterMm);

        Assert.Equal(TileFlags.Walkable, first.Tile(0, 0));
        Assert.Equal(TileFlags.Blocked, second.Tile(0, 0));
    }

    // ---- AC-2: bounds validation ----

    [Fact]
    public void Ac2_Bounds_DefaultToTheCornerGrid()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(2, 3, new int[12]);

        Assert.Equal(new MapBounds(0, 0, 4000, 6000), b.Build().Bounds);
    }

    [Fact]
    public void Ac2_Bounds_ExplicitBoundsContainingTheGrid_AreReported()
    {
        WorldTablesBuilder b = SingleTile(0, 0, 0, 0);
        var expected = new MapBounds(-5000, -3000, 9000, 4000);
        b.SetBounds(expected);

        Assert.Equal(expected, b.Build().Bounds);
    }

    [Fact]
    public void Ac2_Bounds_ExactlyTheGridAndExactlyTheLimit_AreAccepted()
    {
        WorldTablesBuilder b = SingleTile(0, 0, 0, 0);
        b.SetBounds(new MapBounds(0, 0, 2000, 2000));
        Assert.Equal(new MapBounds(0, 0, 2000, 2000), b.Build().Bounds);

        b.SetBounds(new MapBounds(-1_000_000, -1_000_000, 1_000_000, 1_000_000));
        Assert.Equal(new MapBounds(-1_000_000, -1_000_000, 1_000_000, 1_000_000), b.Build().Bounds);
    }

    [Theory]
    [InlineData(-1_000_001L, 0L, 2000L, 2000L)]
    [InlineData(0L, -1_000_001L, 2000L, 2000L)]
    [InlineData(0L, 0L, 1_000_001L, 2000L)]
    [InlineData(0L, 0L, 2000L, 1_000_001L)]
    [InlineData(long.MinValue, 0L, 2000L, 2000L)]
    public void Ac2_Bounds_CoordinateOverOneMillion_IsRejectedWithBoundsOutOfRange(long minX, long minZ, long maxX, long maxZ)
    {
        WorldTablesBuilder b = SingleTile(0, 0, 0, 0);
        b.SetBounds(new MapBounds(minX, minZ, maxX, maxZ));

        SimBuildException e = Assert.Throws<SimBuildException>(() => b.Build());

        Assert.Equal(SimBuildErrorCode.WorldBoundsOutOfRange, e.Code);
    }

    [Theory]
    [InlineData(1L, 0L, 2000L, 2000L)]
    [InlineData(0L, 1L, 2000L, 2000L)]
    [InlineData(0L, 0L, 1999L, 2000L)]
    [InlineData(0L, 0L, 2000L, 1999L)]
    [InlineData(2000L, 2000L, 0L, 0L)]
    [InlineData(500L, 500L, 1500L, 1500L)]
    public void Ac2_Bounds_NotContainingTheCornerGrid_IsRejectedWithDoNotContainGrid(long minX, long minZ, long maxX, long maxZ)
    {
        WorldTablesBuilder b = SingleTile(0, 0, 0, 0);
        b.SetBounds(new MapBounds(minX, minZ, maxX, maxZ));

        SimBuildException e = Assert.Throws<SimBuildException>(() => b.Build(SlopeMaxPm, SwimEnterMm));

        Assert.Equal(SimBuildErrorCode.WorldBoundsDoNotContainGrid, e.Code);
    }

    [Fact]
    public void Ac2_Bounds_AreValidatedAtBuildAgainstTheFinalGrid_RegardlessOfCallOrder()
    {
        var b = new WorldTablesBuilder();
        b.SetBounds(new MapBounds(0, 0, 2000, 2000));
        b.SetCorners(2, 1, new int[6]); // grid is now 4000 wide: the earlier bounds no longer contain it

        SimBuildException e = Assert.Throws<SimBuildException>(() => b.Build());

        Assert.Equal(SimBuildErrorCode.WorldBoundsDoNotContainGrid, e.Code);
    }

    [Fact]
    public void Ac2_BuilderErrors_HaveDistinctCodes()
    {
        var codes = new List<SimBuildErrorCode>();

        codes.Add(CodeOf(() => new WorldTablesBuilder().SetCorners(0, 1, new int[4])));
        codes.Add(CodeOf(() => new WorldTablesBuilder().SetCorners(1, 1, new int[3])));
        codes.Add(CodeOf(() => new WorldTablesBuilder().SetCorners(1, 1, new[] { 0, 0, 0, 1_000_001 })));
        codes.Add(CodeOf(() => new WorldTablesBuilder().Build()));
        WorldTablesBuilder out1 = SingleTile(0, 0, 0, 0);
        out1.SetBounds(new MapBounds(0, 0, 2_000_000, 2000));
        codes.Add(CodeOf(() => out1.Build()));
        WorldTablesBuilder out2 = SingleTile(0, 0, 0, 0);
        out2.SetBounds(new MapBounds(0, 0, 10, 10));
        codes.Add(CodeOf(() => out2.Build()));
        WorldTablesBuilder out3 = SingleTile(0, 0, 0, 0);
        out3.MarkRoad(7, 7);
        codes.Add(CodeOf(() => out3.Build()));
        codes.Add(CodeOf(() => SingleTile(0, 0, 0, 0).Build(-1, 0)));

        Assert.Equal(
            new[]
            {
                SimBuildErrorCode.WorldTileCountOutOfRange,
                SimBuildErrorCode.WorldCornerCountMismatch,
                SimBuildErrorCode.WorldCornerHeightOutOfRange,
                SimBuildErrorCode.WorldCornersNotSet,
                SimBuildErrorCode.WorldBoundsOutOfRange,
                SimBuildErrorCode.WorldBoundsDoNotContainGrid,
                SimBuildErrorCode.WorldTileMarkOutOfRange,
                SimBuildErrorCode.WorldInvalidWalkabilityLimit,
            },
            codes);
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.DoesNotContain(default, codes);
    }

    [Fact]
    public void Ac2_WorldBuildErrorCodes_ValuesArePinnedAndAppendOnly()
    {
        Assert.Equal(13, (int)SimBuildErrorCode.WorldTileCountOutOfRange);
        Assert.Equal(14, (int)SimBuildErrorCode.WorldCornerCountMismatch);
        Assert.Equal(15, (int)SimBuildErrorCode.WorldCornerHeightOutOfRange);
        Assert.Equal(16, (int)SimBuildErrorCode.WorldCornersNotSet);
        Assert.Equal(17, (int)SimBuildErrorCode.WorldBoundsOutOfRange);
        Assert.Equal(18, (int)SimBuildErrorCode.WorldBoundsDoNotContainGrid);
        Assert.Equal(19, (int)SimBuildErrorCode.WorldTileMarkOutOfRange);
        Assert.Equal(20, (int)SimBuildErrorCode.WorldInvalidWalkabilityLimit);
    }

    [Fact]
    public void Ac2_MapBounds_EqualityAndHash_AreFieldWise()
    {
        var a = new MapBounds(-1, -2, 3, 4);
        var same = new MapBounds(-1, -2, 3, 4);

        Assert.True(a == same);
        Assert.True(a.Equals((object)same));
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.True(a != new MapBounds(0, -2, 3, 4));
        Assert.True(a != new MapBounds(-1, 0, 3, 4));
        Assert.True(a != new MapBounds(-1, -2, 0, 4));
        Assert.True(a != new MapBounds(-1, -2, 3, 0));
        Assert.False(a.Equals("bounds"));
    }

    private static SimBuildErrorCode CodeOf(Action act)
    {
        return Assert.Throws<SimBuildException>(act).Code;
    }
}
