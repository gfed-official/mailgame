using System.Reflection;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Covers the height-field half of <see cref="IWorldQuery"/> as implemented by <see cref="WorldTables"/>
/// and <see cref="WorldTablesBuilder"/> (world-stub story 001; ADR-0026 sections 1 to 3; TR-movement-005,
/// TR-movement-018 depth half, TR-throwing-005 world half). Tiles are built directly through the builder.
/// </summary>
public class WorldTablesHeightTests
{
    private static WorldTables Build(int tilesX, int tilesZ, params int[] corners)
    {
        var builder = new WorldTablesBuilder();
        builder.SetCorners(tilesX, tilesZ, corners);
        return builder.Build();
    }

    private static WorldTables BuildSingleTile(int h00, int h10, int h01, int h11)
    {
        return Build(1, 1, h00, h10, h01, h11);
    }

    // ---- shape ----

    [Fact]
    public void Dimensions_TwoByThreeMap_ReportTilesSizeAndDerivedBounds()
    {
        // Arrange
        IWorldQuery w = Build(2, 3, new int[12]);

        // Assert
        Assert.Equal(2000, w.TileSizeMm);
        Assert.Equal(2, w.TilesX);
        Assert.Equal(3, w.TilesZ);
        Assert.Equal(0L, w.Bounds.MinXMm);
        Assert.Equal(0L, w.Bounds.MinZMm);
        Assert.Equal(4000L, w.Bounds.MaxXMm);
        Assert.Equal(6000L, w.Bounds.MaxZMm);
    }

    // ---- AC-1: FX-Beach through IWorldQuery ----

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void FxBeach_MidTile_HeightMinus600AndDepth600()
    {
        // Arrange
        IWorldQuery w = BuildSingleTile(-1000, -1000, -200, -200);

        // Act
        long height = w.TerrainHeightMm(500, 1000);
        int depth = w.WaterDepthMm(500, 1000);

        // Assert
        Assert.Equal(-600L, height);
        Assert.Equal(600, depth);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void FxBeach_NearShallowEdge_HeightMinus450AndDepth450()
    {
        // Arrange
        IWorldQuery w = BuildSingleTile(-1000, -1000, -200, -200);

        // Act
        long height = w.TerrainHeightMm(500, 1375);
        int depth = w.WaterDepthMm(500, 1375);

        // Assert
        Assert.Equal(-450L, height);
        Assert.Equal(450, depth);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void FxBeach_GetCornersFedToSample_ReturnsSameValuesAsQuery()
    {
        // Arrange
        IWorldQuery w = BuildSingleTile(-1000, -1000, -200, -200);

        // Act
        w.GetCorners(0, 0, out HeightCorners h);
        int direct = WorldHeight.Sample(in h, 500, 1375);

        // Assert
        Assert.Equal(new HeightCorners(-1000, -1000, -200, -200), h);
        Assert.Equal(-450, direct);
        Assert.Equal((long)direct, w.TerrainHeightMm(500, 1375));
    }

    // ---- AC-2: golden vectors through the tables, clamp, edge rule ----

    // The two derived golden pins loop the table in one test each and report every failing row by index.
    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainHeightMm_GoldenVector_MatchesCommittedValue()
    {
        Assert.NotEmpty(HeightGoldenVectors.Rows);
        var failures = new List<string>();
        for (int i = 0; i < HeightGoldenVectors.Rows.Length; i++)
        {
            // Arrange
            int[] r = HeightGoldenVectors.Rows[i];
            IWorldQuery w = BuildSingleTile(r[0], r[1], r[2], r[3]);

            // Act
            long actual = w.TerrainHeightMm(r[4], r[5]);

            // Assert (collected)
            if (actual != r[6])
            {
                failures.Add($"golden row {i} {{ {string.Join(", ", r)} }}: expected {r[6]}, got {actual}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainHeightMm_GoldenVectorInTileOneOne_MatchesCommittedValue()
    {
        // Rows with an offset of 2000 belong to the neighbour tile, so only offsets below S are used here.
        int[][] interior = HeightGoldenVectors.Rows.Where(r => r[4] < 2000 && r[5] < 2000).ToArray();
        Assert.NotEmpty(interior);
        foreach (int[] r in interior)
        {
            // Arrange (the same tile placed at tile (1,1) of a 3 x 3 map; neighbours differ so a wrong lookup shows)
            // Corner grid is 4 x 4 row-major; tile (1,1) owns corners (1,1) (2,1) (1,2) (2,2) = indices 5 6 9 10.
            int[] corners = { 7, 7, 7, 7, 7, r[0], r[1], 7, 7, r[2], r[3], 7, 7, 7, 7, 7 };
            IWorldQuery w = Build(3, 3, corners);

            // Act
            long actual = w.TerrainHeightMm(2000 + r[4], 2000 + r[5]);

            // Assert
            Assert.True(actual == r[6], $"golden row {{ {string.Join(", ", r)} }} at tile (1,1): expected {r[6]}, got {actual}");
        }
    }

    [Theory]
    [InlineData(-1L, -1L, 100)]
    [InlineData(-5000L, 0L, 100)]
    [InlineData(long.MinValue, long.MinValue, 100)]
    [InlineData(2001L, 2001L, 900)]
    [InlineData(1_000_000_000_000L, 1_000_000_000_000L, 900)]
    [InlineData(long.MaxValue, long.MaxValue, 900)]
    [InlineData(long.MinValue, long.MaxValue, 500)]
    [InlineData(long.MaxValue, long.MinValue, 300)]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainHeightMm_OutsideMap_ClampedToNearestCorner(long x, long z, long expected)
    {
        // Arrange (corners 100 / 300 / 500 / 900, but only the corner-clamp cases listed above)
        IWorldQuery w = BuildSingleTile(100, 300, 500, 900);

        // Act
        long actual = w.TerrainHeightMm(x, z);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainHeightMm_OutsideMapOnOneAxis_ClampsOnlyThatAxis()
    {
        // Arrange
        IWorldQuery w = BuildSingleTile(100, 300, 500, 900);

        // Act / Assert: x clamped to 0 leaves z interpolating along the x = 0 edge (100 -> 500)
        Assert.Equal(300L, w.TerrainHeightMm(-7777, 1000));
        Assert.Equal(w.TerrainHeightMm(0, 1000), w.TerrainHeightMm(-7777, 1000));
        Assert.Equal(w.TerrainHeightMm(1000, 2000), w.TerrainHeightMm(1000, 90_000));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainHeightMm_FarMapEdge_ReturnsExactFarCornerValues()
    {
        // Arrange (edge rule: x = MaxX uses the last tile with offset S, i.e. the exact far corner column)
        IWorldQuery w = Build(2, 2, 0, 100, 200, 300, 400, 500, 600, 700, 801);
        long maxX = w.Bounds.MaxXMm;
        long maxZ = w.Bounds.MaxZMm;

        // Act / Assert
        Assert.Equal(200L, w.TerrainHeightMm(maxX, 0));
        Assert.Equal(500L, w.TerrainHeightMm(maxX, 2000));
        Assert.Equal(801L, w.TerrainHeightMm(maxX, maxZ));
        Assert.Equal(700L, w.TerrainHeightMm(2000, maxZ));
        Assert.Equal(350L, w.TerrainHeightMm(maxX, 1000));
        Assert.Equal(650L, w.TerrainHeightMm(1000, maxZ));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainHeightMm_AcrossTileBoundary_ContinuousAtSharedCorners()
    {
        // Arrange
        IWorldQuery w = Build(2, 2, 0, 100, 200, 300, 400, 500, 600, 700, 801);

        // Act / Assert: the shared corner (2000, 2000) = 400 read from all four tiles' sides
        Assert.Equal(400L, w.TerrainHeightMm(2000, 2000));
        Assert.True(Math.Abs(w.TerrainHeightMm(1999, 1999) - 400L) <= 1L);
        Assert.True(Math.Abs(w.TerrainHeightMm(2001, 2001) - 400L) <= 1L);
        Assert.Equal(100L, w.TerrainHeightMm(2000, 0));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainHeightMm_EqualsSampleOverGetCorners_AtEverySweepPoint()
    {
        // Arrange (3 x 2 map with varied, signed heights)
        IWorldQuery w = Build(3, 2, -900, -300, 150, 1200, -50, 700, 400, -1200, 80, 90, -75, 1500);

        // Act / Assert: one kernel, one answer, including points outside the map
        for (int x = -500; x <= 6500; x += 331)
        {
            for (int z = -500; z <= 4500; z += 307)
            {
                int cx = Math.Clamp(x, 0, 6000);
                int cz = Math.Clamp(z, 0, 4000);
                int tx = Math.Min(cx / 2000, 2);
                int tz = Math.Min(cz / 2000, 1);
                w.GetCorners(tx, tz, out HeightCorners h);
                int expected = WorldHeight.Sample(in h, cx - (tx * 2000), cz - (tz * 2000));

                Assert.Equal((long)expected, w.TerrainHeightMm(x, z));
                Assert.Equal(expected, WorldHeight.At(w, x, z));
            }
        }
    }

    [Fact]
    public void GetCorners_RowMajorLayout_ReturnsTileCorners()
    {
        // Arrange (2 x 2 tiles = 3 x 3 corners, each value encodes its index)
        IWorldQuery w = Build(2, 2, 0, 1, 2, 3, 4, 5, 6, 7, 8);

        // Act
        w.GetCorners(0, 0, out HeightCorners t00);
        w.GetCorners(1, 0, out HeightCorners t10);
        w.GetCorners(0, 1, out HeightCorners t01);
        w.GetCorners(1, 1, out HeightCorners t11);

        // Assert (H10 is +X, H01 is +Z)
        Assert.Equal(new HeightCorners(0, 1, 3, 4), t00);
        Assert.Equal(new HeightCorners(1, 2, 4, 5), t10);
        Assert.Equal(new HeightCorners(3, 4, 6, 7), t01);
        Assert.Equal(new HeightCorners(4, 5, 7, 8), t11);
    }

    [Fact]
    public void GetCorners_IndicesOutsideMap_ClampedToEdgeTiles()
    {
        // Arrange
        IWorldQuery w = Build(2, 2, 0, 1, 2, 3, 4, 5, 6, 7, 8);

        // Act
        w.GetCorners(-1, -9, out HeightCorners low);
        w.GetCorners(int.MinValue, 0, out HeightCorners lowX);
        w.GetCorners(2, 2, out HeightCorners high);
        w.GetCorners(int.MaxValue, int.MaxValue, out HeightCorners highest);
        w.GetCorners(0, 5, out HeightCorners highZ);

        // Assert
        Assert.Equal(new HeightCorners(0, 1, 3, 4), low);
        Assert.Equal(new HeightCorners(0, 1, 3, 4), lowX);
        Assert.Equal(new HeightCorners(4, 5, 7, 8), high);
        Assert.Equal(new HeightCorners(4, 5, 7, 8), highest);
        Assert.Equal(new HeightCorners(3, 4, 6, 7), highZ);
    }

    // ---- AC-3: gradient through the tables ----

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainGradientPm_Rise1400OverTile_Gives700And0()
    {
        // Arrange
        IWorldQuery w = BuildSingleTile(0, 1400, 0, 1400);

        // Act
        w.TerrainGradientPm(800, 1200, out int gxPm, out int gzPm);

        // Assert
        Assert.Equal(700, gxPm);
        Assert.Equal(0, gzPm);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainGradientPm_Rise1404OverTile_Gives702()
    {
        // Arrange
        IWorldQuery w = BuildSingleTile(0, 1404, 0, 1404);

        // Act
        w.TerrainGradientPm(0, 0, out int gxPm, out int gzPm);

        // Assert
        Assert.Equal(702, gxPm);
        Assert.Equal(0, gzPm);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainGradientPm_RiseAlongZAndNegativeSlope_SignedPermille()
    {
        // Arrange (+700 along +Z, -702 along +X)
        IWorldQuery w = BuildSingleTile(1404, 0, 1404 + 1400, 1400);

        // Act
        w.TerrainGradientPm(1000, 1000, out int gxPm, out int gzPm);

        // Assert
        Assert.Equal(-702, gxPm);
        Assert.Equal(700, gzPm);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainGradientPm_ExactHalfPermille_RoundsAwayFromZeroBothSigns()
    {
        // Arrange (1 mm over 2000 mm = 0.5 permille, a one-rounding RH tie)
        IWorldQuery up = BuildSingleTile(0, 1, 0, 1);
        IWorldQuery down = BuildSingleTile(0, -1, 0, -1);

        // Act
        up.TerrainGradientPm(0, 0, out int upX, out _);
        down.TerrainGradientPm(0, 0, out int downX, out _);

        // Assert
        Assert.Equal(1, upX);
        Assert.Equal(-1, downX);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainGradientPm_OutsideMapAndFarEdge_UsesClampedTile()
    {
        // Arrange (2 x 1 map; right tile rises 1400 along +X, left tile is flat)
        IWorldQuery w = Build(2, 1, 0, 0, 1400, 0, 0, 1400);

        // Act
        w.TerrainGradientPm(w.Bounds.MaxXMm, 1000, out int farX, out _);
        w.TerrainGradientPm(long.MaxValue, long.MaxValue, out int farX2, out _);
        w.TerrainGradientPm(long.MinValue, long.MinValue, out int nearX, out int nearZ);

        // Assert
        Assert.Equal(700, farX);
        Assert.Equal(700, farX2);
        Assert.Equal(0, nearX);
        Assert.Equal(0, nearZ);
    }

    // ---- AC-4: water is derived, never stored ----

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void WaterDepthMm_GoldenVector_EqualsMaxOfZeroAndNegatedHeight()
    {
        Assert.NotEmpty(HeightGoldenVectors.Rows);
        var failures = new List<string>();
        for (int i = 0; i < HeightGoldenVectors.Rows.Length; i++)
        {
            // Arrange
            int[] r = HeightGoldenVectors.Rows[i];
            IWorldQuery w = BuildSingleTile(r[0], r[1], r[2], r[3]);

            // Act
            int depth = w.WaterDepthMm(r[4], r[5]);

            // Assert (collected)
            int expected = (int)Math.Max(0L, -(long)r[6]);
            int derived = (int)Math.Max(0L, -w.TerrainHeightMm(r[4], r[5]));
            if (depth != expected || depth != derived || depth < 0)
            {
                failures.Add($"golden row {i} {{ {string.Join(", ", r)} }}: depth {depth}, expected {expected}, max(0, -TerrainHeightMm) {derived}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void WaterDepthMm_DryLandAndExactlySeaLevel_IsZero()
    {
        // Arrange
        IWorldQuery dry = BuildSingleTile(500, 500, 500, 500);
        IWorldQuery sea = BuildSingleTile(0, 0, 0, 0);
        IWorldQuery shoreline = BuildSingleTile(-1000, -1000, 1000, 1000);

        // Act / Assert
        Assert.Equal(0, dry.WaterDepthMm(900, 900));
        Assert.Equal(0, sea.WaterDepthMm(900, 900));
        Assert.Equal(0, shoreline.WaterDepthMm(1000, 1000));
        Assert.Equal(1000, shoreline.WaterDepthMm(1000, 0));
        Assert.Equal(0, shoreline.WaterDepthMm(1000, 2000));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void WorldTables_NoWaterDepthField_AndEveryInstanceFieldIsReadonly()
    {
        // Arrange
        FieldInfo[] fields = typeof(WorldTables).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // Assert
        Assert.NotEmpty(fields);
        Assert.DoesNotContain(fields, f => f.Name.Contains("water", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fields, f => f.Name.Contains("depth", StringComparison.OrdinalIgnoreCase));
        Assert.All(fields, f => Assert.True(f.IsInitOnly, f.Name + " must be readonly"));
        Assert.True(typeof(WorldTables).IsSealed);
    }

    // ---- builder: argument validation, copy semantics, immutability ----

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(-1, 1, 4)]
    [InlineData(1, 0, 4)]
    [InlineData(1, -3, 4)]
    [InlineData(501, 1, 1004)]
    [InlineData(1, 501, 1004)]
    [InlineData(1, 1, 3)]
    [InlineData(1, 1, 5)]
    [InlineData(2, 1, 4)]
    [InlineData(1, 1, 0)]
    public void SetCorners_InvalidArguments_ThrowsSimBuildException(int tilesX, int tilesZ, int length)
    {
        // Arrange
        var builder = new WorldTablesBuilder();
        int[] corners = new int[length];

        // Act
        var ex = Record.Exception(() => builder.SetCorners(tilesX, tilesZ, corners));

        // Assert
        Assert.IsType<SimBuildException>(ex);
    }

    [Theory]
    [InlineData(1_000_001)]
    [InlineData(-1_000_001)]
    [InlineData(int.MinValue)]
    public void SetCorners_CornerHeightBeyondLimit_ThrowsSimBuildException(int bad)
    {
        // Arrange
        var builder = new WorldTablesBuilder();

        // Act / Assert
        Assert.Throws<SimBuildException>(() => builder.SetCorners(1, 1, new[] { 0, 0, 0, bad }));
    }

    [Fact]
    public void SetCorners_CornerHeightAtLimit_AcceptedAndGradientFitsInt()
    {
        // Arrange
        var builder = new WorldTablesBuilder();
        builder.SetCorners(1, 1, new[] { -1_000_000, 1_000_000, -1_000_000, 1_000_000 });
        WorldTables w = builder.Build();

        // Act
        w.TerrainGradientPm(1000, 1000, out int gx, out int gz);

        // Assert
        Assert.Equal(1_000_000, gx);
        Assert.Equal(0, gz);
        Assert.Equal(1_000_000, w.WaterDepthMm(0, 0));
    }

    [Fact]
    public void SetCorners_MaximumAxisTileCount_Accepted()
    {
        // Arrange (500 tiles = 1,000,000 mm, the |coord| limit)
        var builder = new WorldTablesBuilder();

        // Act
        builder.SetCorners(500, 1, new int[501 * 2]);
        WorldTables w = builder.Build();

        // Assert
        Assert.Equal(500, w.TilesX);
        Assert.Equal(1_000_000L, w.Bounds.MaxXMm);
    }

    [Fact]
    public void Build_WithoutSetCorners_ThrowsSimBuildException()
    {
        // Arrange
        var builder = new WorldTablesBuilder();

        // Act
        var ex = Record.Exception(() => builder.Build());

        // Assert
        Assert.IsType<SimBuildException>(ex);
    }

    [Fact]
    public void SetCorners_RejectedSecondCall_KeepsPreviousValidState()
    {
        // Arrange
        var builder = new WorldTablesBuilder();
        builder.SetCorners(1, 1, new[] { 1, 2, 3, 4 });

        // Act
        var ex = Record.Exception(() => builder.SetCorners(1, 1, new int[3]));
        WorldTables w = builder.Build();

        // Assert
        Assert.IsType<SimBuildException>(ex);
        w.GetCorners(0, 0, out HeightCorners h);
        Assert.Equal(new HeightCorners(1, 2, 3, 4), h);
    }

    [Fact]
    public void Build_AfterSourceArrayMutatedAndBuilderReused_BuiltTablesUnchanged()
    {
        // Arrange
        var builder = new WorldTablesBuilder();
        int[] source = { -1000, -1000, -200, -200 };
        builder.SetCorners(1, 1, source);
        WorldTables first = builder.Build();

        // Act: mutate the caller's array, replace the builder's field, build again
        source[0] = 5000;
        builder.SetCorners(1, 1, new[] { 9, 9, 9, 9 });
        WorldTables second = builder.Build();

        // Assert: copy-on-set and copy-on-build keep each build independent and immutable
        Assert.NotSame(first, second);
        Assert.Equal(-600L, first.TerrainHeightMm(500, 1000));
        Assert.Equal(9L, second.TerrainHeightMm(500, 1000));
    }

    [Fact]
    public void Build_TwiceWithoutChange_ProducesEqualButIndependentTables()
    {
        // Arrange
        var builder = new WorldTablesBuilder();
        builder.SetCorners(2, 1, new[] { 1, 2, 3, 4, 5, 6 });

        // Act
        WorldTables a = builder.Build();
        WorldTables b = builder.Build();

        // Assert
        Assert.NotSame(a, b);
        for (int x = 0; x <= 4000; x += 250)
        {
            Assert.Equal(a.TerrainHeightMm(x, 700), b.TerrainHeightMm(x, 700));
        }
    }

    // ---- Post Office on a terrain-only world (world-stub story 005 replaced the last stub) ----

    [Fact]
    public void PostOffice_TerrainOnlyWorldBuiltWithoutOne_IsDefault()
    {
        // Arrange (the parameterless Build is terrain/solid-only: no Post Office; the composition path requires one, see PostOfficePadTests)
        IWorldQuery w = BuildSingleTile(0, 0, 0, 0);

        // Act / Assert
        Assert.Equal(default, w.PostOffice);
    }

    [Fact]
    public void ContractTypes_DefaultsAndConstructors_RoundTripFields()
    {
        // Arrange
        var box = new AabbMm(new Vec3Mm(0, 0, 0), new Vec3Mm(1, 2, 3));
        var owner = EntityId.Compose(EntityClass.Construct, 1);
        var display = new StringId(StringNamespace.Run, 3);
        var address = new AddressId((1u << 26) | (1u << 18) | (7u << 6));

        // Act
        var solid = new StaticSolid(5, owner, box, SolidKind.Roof);
        var lot = new LotRecord(
            owner, address, 1, 1, 7, display, new TileCoord(1, 2), FaceDir.NegZ,
            new Vec3Mm(1, 2, 3), new TileCoord(3, 4), new Vec3Mm(4, 5, 6), 9);
        var po = new PostOfficeRecord(new Vec3Mm(1, 1, 1), new Vec3Mm(2, 2, 2), new Vec3Mm(3, 3, 3), 1200, 8);
        var bounds = new MapBounds(-1, -2, 3, 4);

        // Assert
        Assert.Equal((ushort)5, solid.Index);
        Assert.Equal(owner, solid.Owner);
        Assert.Equal(box, solid.Box);
        Assert.Equal(SolidKind.Roof, solid.Kind);
        Assert.Equal(address, lot.Address);
        Assert.Equal(owner, lot.Destination);
        Assert.Equal((byte)1, lot.DistrictId);
        Assert.Equal((byte)1, lot.StreetId);
        Assert.Equal((ushort)7, lot.Number);
        Assert.Equal(display, lot.Display);
        Assert.Equal(new TileCoord(1, 2), lot.MailboxTile);
        Assert.Equal(FaceDir.NegZ, lot.Face);
        Assert.Equal(new Vec3Mm(1, 2, 3), lot.Anchor);
        Assert.Equal(new TileCoord(3, 4), lot.ApproachTile);
        Assert.Equal(new Vec3Mm(4, 5, 6), lot.TruckStopPoint);
        Assert.Equal((ushort)9, lot.HouseSolid);
        Assert.Equal(new Vec3Mm(1, 1, 1), po.DoorAnchor);
        Assert.Equal(new Vec3Mm(2, 2, 2), po.IntakeAnchor);
        Assert.Equal(new Vec3Mm(3, 3, 3), po.PadCentre);
        Assert.Equal(1200, po.PadRingRadiusMm);
        Assert.Equal((byte)8, po.PadSlots);
        Assert.Equal(-1L, bounds.MinXMm);
        Assert.Equal(-2L, bounds.MinZMm);
        Assert.Equal(3L, bounds.MaxXMm);
        Assert.Equal(4L, bounds.MaxZMm);
        Assert.Equal(0, (int)TileFlags.None);
        Assert.Equal(31, (int)(TileFlags.Blocked | TileFlags.Water | TileFlags.Walkable | TileFlags.Road | TileFlags.Lot));
    }
}

/// <summary>
/// AC-5: the four height-path queries allocate 0 B over 10,000 calls each on a built
/// <see cref="WorldTables"/>. No <c>ReadOnlySpan</c> literal table is on this path, so TD-008 does not
/// apply and the check runs on Debug and Release. Serialised in the non-parallel collection so another
/// test's allocations cannot land on this thread's counter window; each measurement is attempted up
/// to three times (a tiered-JIT promotion or a runtime background allocation can add a one-off
/// allocation on the thread) and passes when any attempt measures 0 B.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class WorldTablesAllocationTests
{
    private const int Calls = 10_000;
    private const int Attempts = 3;

    private static WorldTables BuildMap()
    {
        var builder = new WorldTablesBuilder();
        builder.SetCorners(3, 2, new[] { -900, -300, 150, 1200, -50, 700, 400, -1200, 80, 90, -75, 1500 });
        return builder.Build();
    }

    private static long Height(WorldTables w)
    {
        long acc = 0;
        for (int i = 0; i < Calls; i++)
        {
            acc += w.TerrainHeightMm((i * 7) % 7000, (i * 13) % 5000);
        }

        return acc;
    }

    private static long Corners(WorldTables w)
    {
        long acc = 0;
        for (int i = 0; i < Calls; i++)
        {
            w.GetCorners(i % 5, (i / 5) % 4, out HeightCorners h);
            acc += h.H00 + h.H11;
        }

        return acc;
    }

    private static long Gradient(WorldTables w)
    {
        long acc = 0;
        for (int i = 0; i < Calls; i++)
        {
            w.TerrainGradientPm((i * 7) % 7000, (i * 13) % 5000, out int gx, out int gz);
            acc += gx + gz;
        }

        return acc;
    }

    private static long Water(WorldTables w)
    {
        long acc = 0;
        for (int i = 0; i < Calls; i++)
        {
            acc += w.WaterDepthMm((i * 7) % 7000, (i * 13) % 5000);
        }

        return acc;
    }

    private static long MeasureBestAttempt(Func<WorldTables, long> loop, WorldTables w, out long checksum)
    {
        checksum = loop(w); // warm-up: JIT, tiering and static initialisation happen here
        checksum += loop(w);
        long best = long.MaxValue;
        for (int attempt = 0; attempt < Attempts && best != 0; attempt++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            checksum += loop(w);
            long after = GC.GetAllocatedBytesForCurrentThread();
            best = Math.Min(best, after - before);
        }

        return best;
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainHeightMm_TenThousandCalls_AllocatesZeroBytes()
    {
        // Arrange
        WorldTables w = BuildMap();

        // Act
        long bytes = MeasureBestAttempt(Height, w, out long checksum);

        // Assert
        Assert.Equal(0L, bytes);
        Assert.NotEqual(long.MinValue, checksum);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void GetCorners_TenThousandCalls_AllocatesZeroBytes()
    {
        // Arrange
        WorldTables w = BuildMap();

        // Act
        long bytes = MeasureBestAttempt(Corners, w, out _);

        // Assert
        Assert.Equal(0L, bytes);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void TerrainGradientPm_TenThousandCalls_AllocatesZeroBytes()
    {
        // Arrange
        WorldTables w = BuildMap();

        // Act
        long bytes = MeasureBestAttempt(Gradient, w, out _);

        // Assert
        Assert.Equal(0L, bytes);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void WaterDepthMm_TenThousandCalls_AllocatesZeroBytes()
    {
        // Arrange
        WorldTables w = BuildMap();

        // Act
        long bytes = MeasureBestAttempt(Water, w, out _);

        // Assert
        Assert.Equal(0L, bytes);
    }
}
