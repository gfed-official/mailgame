using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Covers <see cref="IWorldQuery.ClampInsideMap"/> and <see cref="IWorldQuery.TrySnapWalkable"/> as implemented by
/// <see cref="WorldTables"/> (world-stub story 002 AC-2 clamp and AC-3 snap; ADR-0026 section 4). The 0 B check lives in
/// <see cref="WorldSnapClampAllocationTests"/> because it needs the NonParallel collection.
/// </summary>
public class WorldSnapClampTests
{
    private const int SlopeMaxPm = 700;
    private const int SwimEnterMm = 900;

    /// <summary>Flat walkable map of <paramref name="tiles"/> x <paramref name="tiles"/> tiles, with optional blocked tiles.</summary>
    internal static WorldTables FlatMap(int tiles, params (int Tx, int Tz)[] blocked)
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(tiles, tiles, new int[(tiles + 1) * (tiles + 1)]);
        foreach ((int tx, int tz) in blocked)
        {
            b.MarkBlocked(tx, tz);
        }

        return b.Build(SlopeMaxPm, SwimEnterMm);
    }

    // ---- AC-2: ClampInsideMap ----

    [Fact]
    public void Ac2_Clamp_InRangePoint_IsUnchanged()
    {
        WorldTables w = FlatMap(5); // bounds [0, 10000]^2
        var p = new Vec3Mm(5000, 777, 4000);

        Assert.Equal(p, w.ClampInsideMap(in p, 1000));
    }

    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(9000, 9000)]
    [InlineData(1000, 9000)]
    public void Ac2_Clamp_PointsOnTheInsetBoundary_AreUnchanged(int x, int z)
    {
        WorldTables w = FlatMap(5);
        var p = new Vec3Mm(x, 3, z);

        Assert.Equal(p, w.ClampInsideMap(in p, 1000));
    }

    [Theory]
    [InlineData(-5000, 5000, 1000, 5000)]
    [InlineData(0, 5000, 1000, 5000)]
    [InlineData(999, 5000, 1000, 5000)]
    [InlineData(10_000, 5000, 9000, 5000)]
    [InlineData(9001, 5000, 9000, 5000)]
    [InlineData(5000, -5000, 5000, 1000)]
    [InlineData(5000, 10_001, 5000, 9000)]
    [InlineData(-123_456_789, 987_654_321, 1000, 9000)]
    [InlineData(int.MinValue, int.MaxValue, 1000, 9000)]
    public void Ac2_Clamp_OutOfRangeXz_LandsOnInsetBoundary(int x, int z, int expectedX, int expectedZ)
    {
        WorldTables w = FlatMap(5);
        var p = new Vec3Mm(x, -42, z);

        Vec3Mm c = w.ClampInsideMap(in p, 1000);

        Assert.Equal(new Vec3Mm(expectedX, -42, expectedZ), c);
    }

    [Fact]
    public void Ac2_Clamp_NeverChangesY()
    {
        WorldTables w = FlatMap(5);

        foreach (int y in new[] { int.MinValue, -1, 0, 1, 123_456, int.MaxValue })
        {
            var p = new Vec3Mm(-9999, y, 99_999);
            Assert.Equal(y, w.ClampInsideMap(in p, 1000).Y);
        }
    }

    [Fact]
    public void Ac2_Clamp_UsesExplicitBounds_NotTheCornerGrid()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(1, 1, new int[4]);
        b.SetBounds(new MapBounds(-4000, -6000, 8000, 3000));
        WorldTables w = b.Build(SlopeMaxPm, SwimEnterMm);

        Assert.Equal(new Vec3Mm(-3000, 0, -5000), w.ClampInsideMap(new Vec3Mm(-9000, 0, -9000), 1000));
        Assert.Equal(new Vec3Mm(7000, 0, 2000), w.ClampInsideMap(new Vec3Mm(9000, 0, 9000), 1000));
        Assert.Equal(new Vec3Mm(100, 0, 100), w.ClampInsideMap(new Vec3Mm(100, 0, 100), 1000));
    }

    [Fact]
    public void Ac2_Clamp_ZeroInset_ClampsToTheBoundsThemselves()
    {
        WorldTables w = FlatMap(5);

        Assert.Equal(new Vec3Mm(0, 0, 10_000), w.ClampInsideMap(new Vec3Mm(-1, 0, 10_001), 0));
        Assert.Equal(new Vec3Mm(0, 0, 10_000), w.ClampInsideMap(new Vec3Mm(0, 0, 10_000), 0));
    }

    [Fact]
    public void Ac2_Clamp_NegativeInset_IsTreatedAsZero()
    {
        WorldTables w = FlatMap(5);

        Assert.Equal(new Vec3Mm(0, 0, 10_000), w.ClampInsideMap(new Vec3Mm(-500, 0, 20_000), -1000));
    }

    [Fact]
    public void Ac2_Clamp_InsetLargerThanHalfTheMap_CollapsesToTheMidpoint()
    {
        WorldTables w = FlatMap(1); // [0, 2000]^2, inset 1000 -> empty-width range [1000, 1000]
        Assert.Equal(new Vec3Mm(1000, 5, 1000), w.ClampInsideMap(new Vec3Mm(0, 5, 2000), 1000));

        WorldTables w5 = FlatMap(5);
        Assert.Equal(new Vec3Mm(5000, 5, 5000), w5.ClampInsideMap(new Vec3Mm(0, 5, 10_000), 6000));
        Assert.Equal(new Vec3Mm(5000, 5, 5000), w5.ClampInsideMap(new Vec3Mm(0, 5, 10_000), int.MaxValue));
    }

    // ---- AC-3: TrySnapWalkable ----

    [Fact]
    public void Ac3_Snap_OnATileCentre_ReturnsThatCentre()
    {
        WorldTables w = FlatMap(3);

        bool ok = w.TrySnapWalkable(new Vec3Mm(3000, 0, 5000), 1000, out Vec3Mm s);

        Assert.True(ok);
        Assert.Equal(new Vec3Mm(3000, 0, 5000), s);
    }

    [Fact]
    public void Ac3_Snap_PicksTheNearestWalkableCentre()
    {
        WorldTables w = FlatMap(3);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(2900, 0, 1000), 1000, out Vec3Mm a));
        Assert.Equal(new Vec3Mm(3000, 0, 1000), a);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(1000, 0, 2900), 1000, out Vec3Mm b));
        Assert.Equal(new Vec3Mm(1000, 0, 3000), b);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(4500, 0, 4700), 1500, out Vec3Mm c));
        Assert.Equal(new Vec3Mm(5000, 0, 5000), c);
    }

    [Fact]
    public void Ac3_Snap_SkipsBlockedTiles_AndTakesTheNextNearest()
    {
        WorldTables w = FlatMap(3, (1, 0));

        Assert.True(w.TrySnapWalkable(new Vec3Mm(3000, 0, 1000), 2000, out Vec3Mm s));

        // (1,0) is blocked; (0,0), (2,0) and (1,1) are each 2000 away along one axis; (0,0) comes first in tz then tx order.
        Assert.Equal(new Vec3Mm(1000, 0, 1000), s);
    }

    [Fact]
    public void Ac3_Snap_ExactTieOnX_PicksTheLowerTx()
    {
        WorldTables w = FlatMap(3);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(2000, 0, 1000), 1000, out Vec3Mm s));

        Assert.Equal(new Vec3Mm(1000, 0, 1000), s);
    }

    [Fact]
    public void Ac3_Snap_ExactTieOnZ_PicksTheLowerTz()
    {
        WorldTables w = FlatMap(3);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(1000, 0, 2000), 1000, out Vec3Mm s));

        Assert.Equal(new Vec3Mm(1000, 0, 1000), s);
    }

    [Fact]
    public void Ac3_Snap_FourWayTie_PicksTileZeroZero()
    {
        WorldTables w = FlatMap(3);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(2000, 0, 2000), 1500, out Vec3Mm s));

        Assert.Equal(new Vec3Mm(1000, 0, 1000), s);
    }

    [Fact]
    public void Ac3_Snap_DiagonalTie_LowerTzBeatsLowerTx()
    {
        // Tiles (1,0) and (0,1) are equidistant from (2000, 2000); (0,0) is blocked. Lower tz wins, so (1,0), not (0,1).
        WorldTables w = FlatMap(3, (0, 0), (1, 1));

        Assert.True(w.TrySnapWalkable(new Vec3Mm(2000, 0, 2000), 1500, out Vec3Mm s));

        Assert.Equal(new Vec3Mm(3000, 0, 1000), s);
    }

    [Fact]
    public void Ac3_Snap_RadiusIsInclusive_AtExactDistance()
    {
        WorldTables w = FlatMap(3);
        var p = new Vec3Mm(0, 0, 1000); // centre (1000, 1000) is exactly 1000 away

        Assert.True(w.TrySnapWalkable(in p, 1000, out Vec3Mm hit));
        Assert.Equal(new Vec3Mm(1000, 0, 1000), hit);

        Assert.False(w.TrySnapWalkable(in p, 999, out Vec3Mm miss));
        Assert.Equal(default, miss);
    }

    [Fact]
    public void Ac3_Snap_UsesSquaredDistance_NotPerAxisBox()
    {
        WorldTables w = FlatMap(3);
        var p = new Vec3Mm(0, 0, 0); // centre (1000, 1000): d^2 = 2,000,000, d = 1414.2

        Assert.False(w.TrySnapWalkable(in p, 1414, out _));
        Assert.True(w.TrySnapWalkable(in p, 1415, out Vec3Mm s));
        Assert.Equal(new Vec3Mm(1000, 0, 1000), s);
    }

    [Fact]
    public void Ac3_Snap_YIsTerrainHeightAtTheCentre_NotTheInputY()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(2, 1, new[] { 0, 600, 1000, 0, 600, 1000 }); // tile 0: 0..600; tile 1: 600..1000 (slopes 300/200 permille)
        WorldTables w = b.Build(SlopeMaxPm, SwimEnterMm);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(1100, 99_999, 900), 500, out Vec3Mm s));

        Assert.Equal(1000, s.X);
        Assert.Equal(1000, s.Z);
        Assert.Equal(300, s.Y);
        Assert.Equal(w.TerrainHeightMm(s.X, s.Z), s.Y);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(3000, -5, 1000), 500, out Vec3Mm s2));
        Assert.Equal(w.TerrainHeightMm(s2.X, s2.Z), s2.Y);
        Assert.Equal(800, s2.Y);
    }

    [Fact]
    public void Ac3_Snap_NoWalkableCentreInRadius_ReturnsFalseAndDefault()
    {
        WorldTables w = FlatMap(3, (0, 0));

        bool ok = w.TrySnapWalkable(new Vec3Mm(1000, 7, 1000), 1000, out Vec3Mm s);

        Assert.False(ok);
        Assert.Equal(default, s);
    }

    [Fact]
    public void Ac3_Snap_SteepTile_IsSkipped()
    {
        // x corners 0, 1500, 1500: tile 0 rises 1500 over 2000 (750 permille, steep), tile 1 is flat at 1500.
        var b = new WorldTablesBuilder();
        b.SetCorners(2, 1, new[] { 0, 1500, 1500, 0, 1500, 1500 });
        WorldTables w = b.Build(SlopeMaxPm, SwimEnterMm);

        Assert.False(w.Tile(0, 0).HasFlag(TileFlags.Walkable));
        Assert.True(w.Tile(1, 0).HasFlag(TileFlags.Walkable));
        Assert.True(w.TrySnapWalkable(new Vec3Mm(1000, 0, 1000), 2000, out Vec3Mm s));
        Assert.Equal(new Vec3Mm(3000, 1500, 1000), s);
    }

    [Fact]
    public void Ac3_Snap_DeepWaterTile_IsSkipped()
    {
        // x corners -1000, -1000, 0: tile 0 is flat at depth 1000 (not walkable), tile 1 climbs from -1000 to 0 (centre depth 500).
        var b = new WorldTablesBuilder();
        b.SetCorners(2, 1, new[] { -1000, -1000, 0, -1000, -1000, 0 });
        WorldTables w = b.Build(SlopeMaxPm, SwimEnterMm);

        Assert.False(w.Tile(0, 0).HasFlag(TileFlags.Walkable));
        Assert.True(w.Tile(1, 0).HasFlag(TileFlags.Walkable));
        Assert.True(w.TrySnapWalkable(new Vec3Mm(1000, 0, 1000), 2000, out Vec3Mm s));
        Assert.Equal(new Vec3Mm(3000, -500, 1000), s);
        Assert.False(w.TrySnapWalkable(new Vec3Mm(1000, 0, 1000), 1999, out Vec3Mm none));
        Assert.Equal(default, none);
    }

    [Fact]
    public void Ac3_Snap_WithoutWalkabilityLimits_NeverFinds()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(2, 2, new int[9]);
        WorldTables w = b.Build();

        Assert.False(w.TrySnapWalkable(new Vec3Mm(2000, 0, 2000), 5000, out Vec3Mm s));
        Assert.Equal(default, s);
    }

    [Fact]
    public void Ac3_Snap_PointOutsideTheMap_FindsOnlyCentresInsideTheRadius()
    {
        WorldTables w = FlatMap(3);

        Assert.False(w.TrySnapWalkable(new Vec3Mm(-5000, 0, -5000), 1000, out _));

        Assert.False(w.TrySnapWalkable(new Vec3Mm(-500, 0, -500), 2000, out _)); // d^2 = 4,500,000 > 4,000,000
        Assert.True(w.TrySnapWalkable(new Vec3Mm(-500, 0, -500), 2200, out Vec3Mm s));
        Assert.Equal(new Vec3Mm(1000, 0, 1000), s);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(7000, 0, 7000), 2900, out Vec3Mm far));
        Assert.Equal(new Vec3Mm(5000, 0, 5000), far);
    }

    [Fact]
    public void Ac3_Snap_RadiusZero_OnlyFindsAnExactCentre()
    {
        WorldTables w = FlatMap(3);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(3000, 0, 3000), 0, out Vec3Mm s));
        Assert.Equal(new Vec3Mm(3000, 0, 3000), s);
        Assert.False(w.TrySnapWalkable(new Vec3Mm(3001, 0, 3000), 0, out _));
    }

    [Fact]
    public void Ac3_Snap_NegativeRadius_ReturnsFalseAndDefault()
    {
        WorldTables w = FlatMap(3);

        Assert.False(w.TrySnapWalkable(new Vec3Mm(3000, 0, 3000), -1, out Vec3Mm s));
        Assert.Equal(default, s);
    }

    [Fact]
    public void Ac3_Snap_HugeRadiusAndExtremePoints_DoNotOverflow()
    {
        WorldTables w = FlatMap(3);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(0, 0, 0), int.MaxValue, out Vec3Mm a));
        Assert.Equal(new Vec3Mm(1000, 0, 1000), a);

        Assert.True(w.TrySnapWalkable(new Vec3Mm(1_000_000_000, 0, 1_000_000_000), int.MaxValue, out Vec3Mm b));
        Assert.Equal(new Vec3Mm(5000, 0, 5000), b);

        Assert.False(w.TrySnapWalkable(new Vec3Mm(int.MaxValue, 0, int.MaxValue), int.MaxValue, out _)); // ~3.0e9 mm away
        Assert.False(w.TrySnapWalkable(new Vec3Mm(int.MinValue, 0, int.MinValue), 1000, out _));
        Assert.False(w.TrySnapWalkable(new Vec3Mm(int.MaxValue, 0, 0), 1000, out _));
    }

    [Fact]
    public void Ac3_Snap_SameQuery_IsDeterministic()
    {
        WorldTables w = FlatMap(4, (1, 1), (2, 2));
        var p = new Vec3Mm(4001, 0, 3999);

        w.TrySnapWalkable(in p, 3000, out Vec3Mm first);
        for (int i = 0; i < 50; i++)
        {
            Assert.True(w.TrySnapWalkable(in p, 3000, out Vec3Mm again));
            Assert.Equal(first, again);
        }
    }
}

/// <summary>
/// Allocation guardrail for the snap and clamp queries (story 002 AC-3, ADR-0026 Performance Implications). Sits in the
/// NonParallel collection so concurrent tests do not disturb <see cref="AllocAssert"/>.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class WorldSnapClampAllocationTests
{
    [Fact]
    public void Ac3_Snap_TenThousandCalls_Allocate0Bytes()
    {
        WorldTables w = WorldSnapClampTests.FlatMap(8, (2, 2), (3, 3));
        var near = new Vec3Mm(9100, 0, 9000);
        var miss = new Vec3Mm(-90_000, 0, -90_000);
        long sink = 0;

        AllocAssert.Zero(
            "TrySnapWalkable x 10,000",
            () =>
            {
                for (int i = 0; i < 10_000; i++)
                {
                    bool a = w.TrySnapWalkable(in near, 1000, out Vec3Mm s1);
                    bool b = w.TrySnapWalkable(in miss, 1000, out Vec3Mm s2);
                    sink += (a ? s1.X : 0) + (b ? 1 : s2.Z);
                }
            });

        Assert.NotEqual(0, sink);
    }

    [Fact]
    public void Ac2_Clamp_TenThousandCalls_Allocate0Bytes()
    {
        WorldTables w = WorldSnapClampTests.FlatMap(8);
        var p = new Vec3Mm(-5000, 12, 99_999);
        long sink = 0;

        AllocAssert.Zero(
            "ClampInsideMap x 10,000",
            () =>
            {
                for (int i = 0; i < 10_000; i++)
                {
                    sink += w.ClampInsideMap(in p, 1000).X;
                }
            });

        Assert.NotEqual(0, sink);
    }

    [Fact]
    public void Ac1_Tile_TenThousandCalls_Allocate0Bytes()
    {
        WorldTables w = WorldSnapClampTests.FlatMap(8);
        int sink = 0;

        AllocAssert.Zero(
            "Tile x 10,000",
            () =>
            {
                for (int i = 0; i < 10_000; i++)
                {
                    sink += (int)w.Tile(i & 7, (i >> 3) & 7);
                }
            });

        Assert.NotEqual(0, sink);
    }
}
