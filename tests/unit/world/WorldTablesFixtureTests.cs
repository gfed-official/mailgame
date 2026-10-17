using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Covers <see cref="WorldTablesFixture"/> (world-stub story 006 AC-4): each Player Character Movement fixture
/// (FX-Dry, FX-Beach, FX-Ramp, FX-Deep, FX-Pad) holds the GDD's world values when read through <see cref="IWorldQuery"/>.
/// </summary>
public class WorldTablesFixtureTests
{
    private static bool IsWalkable(IWorldQuery w, int tx, int tz) => (w.Tile(tx, tz) & TileFlags.Walkable) != 0;

    // ---- FX-Dry ----

    [Fact]
    public void Ac4_FxDry_IsFlat1000mm_OnA64By64Grid_WithMapZeroTo128000()
    {
        IWorldQuery w = WorldTablesFixture.Dry();

        Assert.Equal(64, w.TilesX);
        Assert.Equal(64, w.TilesZ);
        Assert.Equal(new MapBounds(0, 0, 128_000, 128_000), w.Bounds);
        Assert.Equal(1000, w.TerrainHeightMm(0, 0));
        Assert.Equal(1000, w.TerrainHeightMm(64_000, 64_000));
        Assert.Equal(1000, w.TerrainHeightMm(128_000, 128_000));
        Assert.Equal(1000, w.TerrainHeightMm(12_345, 98_765));
        Assert.Equal(0, w.WaterDepthMm(64_000, 64_000));
    }

    [Fact]
    public void Ac4_FxDry_EveryTileIsWalkableAndNotWater()
    {
        IWorldQuery w = WorldTablesFixture.Dry();

        for (int tz = 0; tz < w.TilesZ; tz++)
        {
            for (int tx = 0; tx < w.TilesX; tx++)
            {
                Assert.True(IsWalkable(w, tx, tz));
                Assert.Equal(TileFlags.None, w.Tile(tx, tz) & TileFlags.Water);
            }
        }

        Assert.Equal(0, w.StaticSolids.Length);
        Assert.Equal(0, w.Lots.Length);
    }

    // ---- FX-Beach ----

    [Fact]
    public void Ac4_FxBeach_HasTheGddCorners_AndHeightMinus600At500_1000()
    {
        IWorldQuery w = WorldTablesFixture.Beach();

        Assert.Equal(1, w.TilesX);
        Assert.Equal(1, w.TilesZ);
        w.GetCorners(0, 0, out HeightCorners h);
        Assert.Equal(-1000, h.H00);
        Assert.Equal(-1000, h.H10);
        Assert.Equal(-200, h.H01);
        Assert.Equal(-200, h.H11);
        Assert.Equal(-600, w.TerrainHeightMm(500, 1000));
        Assert.Equal(600, w.WaterDepthMm(500, 1000));
    }

    [Theory]
    [InlineData(1375, 450)]
    [InlineData(1000, 600)]
    [InlineData(250, 900)]
    [InlineData(252, 899)]
    [InlineData(747, 701)]
    [InlineData(750, 700)]
    public void Ac4_FxBeach_WaterDepthAtFx500_MatchesPcmF7Rows(int fzMm, int expectedDepthMm)
    {
        IWorldQuery w = WorldTablesFixture.Beach();

        Assert.Equal(expectedDepthMm, w.WaterDepthMm(500, fzMm));
    }

    // ---- FX-Ramp ----

    [Fact]
    public void Ac4_FxRamp1400_RisesExactly1400Over2000_At700Permille_AndIsWalkable()
    {
        IWorldQuery w = WorldTablesFixture.Ramp(1400);

        w.GetCorners(WorldTablesFixture.RampTileX, 0, out HeightCorners h);
        Assert.Equal(1400, h.H10 - h.H00);
        Assert.Equal(1400, h.H11 - h.H01);
        int tileMinX = WorldTablesFixture.RampTileX * 2000;
        w.TerrainGradientPm(tileMinX + 1000, 1000, out int gx, out int gz);
        Assert.Equal(700, gx);
        Assert.Equal(0, gz);
        Assert.True(IsWalkable(w, WorldTablesFixture.RampTileX, 0));
    }

    [Fact]
    public void Ac4_FxRamp1404_Is702Permille_AndNotWalkable()
    {
        IWorldQuery w = WorldTablesFixture.Ramp(1404);

        int tileMinX = WorldTablesFixture.RampTileX * 2000;
        w.TerrainGradientPm(tileMinX + 1000, 1000, out int gx, out _);
        Assert.Equal(702, gx);
        Assert.False(IsWalkable(w, WorldTablesFixture.RampTileX, 0));
        for (int tx = 0; tx < w.TilesX; tx++)
        {
            if (tx != WorldTablesFixture.RampTileX)
            {
                Assert.True(IsWalkable(w, tx, 0), "tile " + tx);
            }
        }
    }

    [Fact]
    public void Ac4_FxRamp_LowGroundWestAndHighGroundEast_AreFlatAndWalkable()
    {
        IWorldQuery w = WorldTablesFixture.Ramp(1400);

        int tileMinX = WorldTablesFixture.RampTileX * 2000;
        Assert.Equal(0, w.TerrainHeightMm(tileMinX, 1000));
        Assert.Equal(1400, w.TerrainHeightMm(tileMinX + 2000, 1000));
        Assert.Equal(700, w.TerrainHeightMm(tileMinX + 1000, 1000));
        for (int tx = 0; tx < w.TilesX; tx++)
        {
            Assert.True(IsWalkable(w, tx, 0), "tile " + tx);
        }
    }

    // ---- FX-Deep ----

    [Fact]
    public void Ac4_FxDeep_IsFlatMinus2000_Depth2000_AndNothingIsWalkable()
    {
        IWorldQuery w = WorldTablesFixture.Deep();

        Assert.Equal(64, w.TilesX);
        Assert.Equal(64, w.TilesZ);
        Assert.Equal(new MapBounds(0, 0, 128_000, 128_000), w.Bounds);
        Assert.Equal(-2000, w.TerrainHeightMm(64_000, 64_000));
        Assert.Equal(-2000, w.TerrainHeightMm(1, 127_999));
        Assert.Equal(2000, w.WaterDepthMm(64_000, 64_000));
        for (int tz = 0; tz < w.TilesZ; tz++)
        {
            for (int tx = 0; tx < w.TilesX; tx++)
            {
                Assert.False(IsWalkable(w, tx, tz), "tile " + tx + "," + tz);
                Assert.NotEqual(TileFlags.None, w.Tile(tx, tz) & TileFlags.Water);
            }
        }
    }

    // ---- one source for the walkability limits ----

    [Fact]
    public void FixtureSwimEnterMm_MatchesTheBuiltInPlayersBalanceDefault()
    {
        // Drift guard: the fixture pins the PC GDD default; the built-in content-data players record must agree.
        Assert.Equal(WorldTablesFixture.SwimEnterMm, BuiltInContent.CreateM1a().Player.SwimEnterMm);
    }

    [Fact]
    public void FixtureSlopeMaxPm_MatchesTheBuiltInPlayersBalanceDefault()
    {
        // Drift guard (TD-060 item 1): player.slopeMaxPm now exists in content-data; the fixture's pin must agree with it.
        Assert.Equal(WorldTablesFixture.SlopeMaxPm, BuiltInContent.CreateM1a().Player.SlopeMaxPm);
    }

    // ---- FX-Pad ----

    [Fact]
    public void Ac4_FxPad_CentreOnFlatDryGround_8SlotsAt1200mm_AllValid()
    {
        IWorldQuery w = WorldTablesFixture.Pad();
        PostOfficeRecord po = w.PostOffice;

        Assert.Equal(new Vec3Mm(WorldTablesFixture.PadCentreMm, 1000, WorldTablesFixture.PadCentreMm), po.PadCentre);
        Assert.Equal(1200, po.PadRingRadiusMm);
        Assert.Equal(8, po.PadSlots);
        Assert.Equal(0, w.StaticSolids.Length);
        for (int k = 0; k < po.PadSlots; k++)
        {
            Vec3Mm p = PadSlots.Position(w, in po, k);
            Assert.Equal(1000, p.Y);
            Assert.Equal(0, w.WaterDepthMm(p.X, p.Z));
            Assert.True(IsWalkable(w, p.X / 2000, p.Z / 2000), "slot " + k);
        }
    }

    [Fact]
    public void Ac4_FxPad_SlotOffsetsFromTheCentre_AreTheGddRing()
    {
        IWorldQuery w = WorldTablesFixture.Pad();
        PostOfficeRecord po = w.PostOffice;
        (int Dx, int Dz)[] expected =
        {
            (0, -1200), (-849, -849), (-1200, 0), (-849, 849), (0, 1200), (849, 849), (1200, 0), (849, -849),
        };

        for (int k = 0; k < expected.Length; k++)
        {
            Vec3Mm p = PadSlots.Position(w, in po, k);
            Assert.Equal(expected[k].Dx, p.X - po.PadCentre.X);
            Assert.Equal(expected[k].Dz, p.Z - po.PadCentre.Z);
        }
    }

    // ---- shared helper ----

    [Fact]
    public void Ac4_NewIds_GivesAFreshAllocatorEachCall_SoRepeatedBuildsAreIdentical()
    {
        Assert.Equal(WorldTablesFixture.Pad().PostOffice, WorldTablesFixture.Pad().PostOffice);
        Assert.NotSame(WorldTablesFixture.NewIds(), WorldTablesFixture.NewIds());
        Assert.Equal(700, WorldTablesFixture.SlopeMaxPm);
        Assert.Equal(900, WorldTablesFixture.SwimEnterMm);
    }
}
