using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Covers the static solid table and the 4 m bucket-grid <see cref="IWorldQuery.QueryStaticSolids"/>
/// (world-stub story 003 AC-1 to AC-3; ADR-0026 section 5; TR-throwing-007/005, TR-movement-006 world halves).
/// The brute-force oracle uses the same <see cref="StaticSolidGrid.Overlaps"/> as the query. The 0 B check lives in
/// <see cref="StaticSolidQueryAllocationTests"/> (NonParallel collection).
/// </summary>
public class StaticSolidQueryTests
{
    internal const int RandomSolidCount = 2000;
    internal const int RandomRegionCount = 10_000;

    // ---- fixtures ----

    /// <summary>8 x 8 tiles, bounds [0, 16000]^2 (4 x 4 buckets). Solids: 0 house, 1 roof over everything, 2 bucket-edge box, 3 far-corner box.</summary>
    internal static WorldTables HandBuilt(SimCounters? counters = null)
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(8, 8, new int[81]);
        b.SetCounters(counters);
        b.AddSolid(EntityId.None, Box(1000, 0, 1000, 3000, 3000, 3000), SolidKind.House);
        b.AddSolid(EntityId.None, Box(0, 3000, 0, 16000, 4000, 16000), SolidKind.Roof);
        b.AddSolid(EntityId.Compose(EntityClass.Construct, 7), Box(4000, 0, 4000, 8000, 2000, 8000), SolidKind.Prop);
        b.AddSolid(EntityId.None, Box(12000, 0, 12000, 16000, 1000, 16000), SolidKind.PostOffice);
        return b.Build(700, 900);
    }

    /// <summary>
    /// ~2,000 seeded random solids on 50 x 50 tiles with bounds [-6000, 103000] x [-3000, 101000] (28 x 26 buckets; the Z
    /// extent is an exact bucket multiple so the far edge folds into the last bucket). Mix: small boxes, boxes spanning
    /// many buckets, boxes snapped to bucket edges, and boxes touching the bounds.
    /// </summary>
    internal static WorldTables RandomWorld(ulong seed, SimCounters? counters = null)
    {
        var rng = new Pcg32(seed, 54);
        var b = new WorldTablesBuilder();
        b.SetCorners(50, 50, new int[51 * 51]);
        b.SetBounds(new MapBounds(MinX, MinZ, MaxX, MaxZ));
        b.SetCounters(counters);
        for (int i = 0; i < RandomSolidCount; i++)
        {
            int shape = Next(ref rng, 0, 9);
            int sizeMax = shape < 6 ? 6000 : (shape < 8 ? 30_000 : 60_000);
            int sx = Next(ref rng, 1, sizeMax);
            int sz = Next(ref rng, 1, sizeMax);
            int x0 = Next(ref rng, MinX, MaxX - sx);
            int z0 = Next(ref rng, MinZ, MaxZ - sz);
            if (shape == 5)
            {
                x0 = SnapToBucketEdge(x0, MinX, MaxX - sx);
                z0 = SnapToBucketEdge(z0, MinZ, MaxZ - sz);
            }

            int y0 = Next(ref rng, -2000, 10_000);
            int y1 = y0 + Next(ref rng, 1, 5000);
            var kind = (SolidKind)Next(ref rng, 0, 3);
            b.AddSolid(EntityId.None, Box(x0, y0, z0, x0 + sx, y1, z0 + sz), kind);
        }

        return b.Build(700, 900);
    }

    internal const int MinX = -6000;
    internal const int MinZ = -3000;
    internal const int MaxX = 103_000;
    internal const int MaxZ = 101_000;

    internal static AabbMm Box(int x0, int y0, int z0, int x1, int y1, int z1) =>
        new(new Vec3Mm(x0, y0, z0), new Vec3Mm(x1, y1, z1));

    internal static int Next(ref Pcg32 rng, int lo, int hiInclusive) =>
        lo + (int)(rng.Next32() % (uint)(hiInclusive - lo + 1));

    private static int SnapToBucketEdge(int v, int origin, int max)
    {
        int snapped = origin + ((v - origin) / StaticSolidGrid.BucketSizeMm * StaticSolidGrid.BucketSizeMm);
        return snapped > max ? max : snapped;
    }

    /// <summary>
    /// A seeded random region: mostly small (points, sub-bucket, a few buckets), some huge (past the merge buffer, so the
    /// linear-scan fallback runs), some poking outside the bounds, and some with a face placed exactly on a solid's face.
    /// </summary>
    internal static AabbMm RandomRegion(ref Pcg32 rng, ReadOnlySpan<StaticSolid> solids)
    {
        int shape = Next(ref rng, 0, 19);
        int size = shape switch
        {
            0 => 0,                                  // a point
            < 12 => Next(ref rng, 0, 4000),          // within a bucket or two
            < 17 => Next(ref rng, 4000, 16_000),     // a few buckets
            _ => Next(ref rng, 24_000, 120_000),     // > 32 buckets: fallback scan
        };
        int sx = size;
        int sz = shape >= 17 ? size : Next(ref rng, 0, Math.Max(size, 1));
        int x0 = Next(ref rng, MinX - 10_000, MaxX + 2000);
        int z0 = Next(ref rng, MinZ - 10_000, MaxZ + 2000);
        int y0 = Next(ref rng, -4000, 14_000);
        int y1 = y0 + Next(ref rng, 0, 6000);
        if (shape % 4 == 1 && solids.Length > 0)
        {
            // Touching-face case: region Min.X sits exactly on a solid's Max.X (and overlaps it in Y/Z).
            AabbMm s = solids[Next(ref rng, 0, solids.Length - 1)].Box;
            x0 = s.Max.X;
            z0 = s.Min.Z;
            y0 = s.Max.Y;
            y1 = y0 + 10;
        }

        return Box(x0, y0, z0, x0 + sx, y1, z0 + sz);
    }

    /// <summary>The brute-force oracle: every solid passing the shared closed-interval test, ascending.</summary>
    internal static int BruteForce(IWorldQuery w, in AabbMm region, Span<ushort> output)
    {
        int n = 0;
        ReadOnlySpan<StaticSolid> solids = w.StaticSolids;
        for (int i = 0; i < solids.Length; i++)
        {
            if (StaticSolidGrid.Overlaps(in region, in solids[i].Box))
            {
                output[n++] = (ushort)i;
            }
        }

        return n;
    }

    private static ushort[] Query(IWorldQuery w, AabbMm region, int capacity = 64)
    {
        var buffer = new ushort[capacity];
        int n = w.QueryStaticSolids(in region, buffer);
        return buffer.AsSpan(0, n).ToArray();
    }

    private static SimBuildErrorCode CodeOf(Action build) => Assert.Throws<SimBuildException>(build).Code;

    private static WorldTablesBuilder SmallBuilder()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(2, 2, new int[9]); // bounds [0, 4000]^2
        return b;
    }

    // ---- AC-1: solid table ----

    [Fact]
    public void Ac1_AddSolid_AssignsIndicesInAddOrderFromZero_AndStaticSolidsIsAscending()
    {
        // Arrange
        var b = SmallBuilder();
        EntityId owner = EntityId.Compose(EntityClass.Construct, 3);
        AabbMm a = Box(0, 0, 0, 1000, 1000, 1000);
        AabbMm c = Box(2000, 0, 2000, 4000, 500, 4000);

        // Act
        ushort i0 = b.AddSolid(EntityId.None, a, SolidKind.House);
        ushort i1 = b.AddSolid(owner, c, SolidKind.Roof);
        ushort i2 = b.AddSolid(EntityId.None, a, SolidKind.Prop);
        WorldTables w = b.Build(700, 900);

        // Assert
        Assert.Equal(new ushort[] { 0, 1, 2 }, new[] { i0, i1, i2 });
        ReadOnlySpan<StaticSolid> s = w.StaticSolids;
        Assert.Equal(3, s.Length);
        Assert.Equal(new StaticSolid(0, EntityId.None, a, SolidKind.House), s[0]);
        Assert.Equal(new StaticSolid(1, owner, c, SolidKind.Roof), s[1]);
        Assert.Equal(new StaticSolid(2, EntityId.None, a, SolidKind.Prop), s[2]);
        for (int i = 0; i < s.Length; i++)
        {
            Assert.Equal(i, s[i].Index);
        }
    }

    [Fact]
    public void Ac1_NoSolids_GivesAnEmptyTable_AndQueriesReturnZero()
    {
        // Arrange
        WorldTables w = SmallBuilder().Build(700, 900);

        // Act
        ushort[] hits = Query(w, Box(-100, -100, -100, 9000, 9000, 9000));

        // Assert
        Assert.True(w.StaticSolids.IsEmpty);
        Assert.Empty(hits);
    }

    [Theory]
    [InlineData(1000, 0, 0, 1000, 10, 10)] // Min.X == Max.X
    [InlineData(0, 5, 0, 10, 5, 10)]       // Min.Y == Max.Y
    [InlineData(0, 0, 7, 10, 10, 7)]       // Min.Z == Max.Z
    [InlineData(10, 0, 0, 0, 10, 10)]      // Min.X > Max.X
    [InlineData(0, 10, 0, 10, 0, 10)]      // Min.Y > Max.Y
    [InlineData(0, 0, 10, 10, 10, 0)]      // Min.Z > Max.Z
    public void Ac1_AddSolid_MinNotBelowMaxOnAnAxis_ThrowsWorldSolidBoxDegenerate(int x0, int y0, int z0, int x1, int y1, int z1)
    {
        // Arrange
        var b = SmallBuilder();

        // Act
        SimBuildErrorCode code = CodeOf(() => b.AddSolid(EntityId.None, Box(x0, y0, z0, x1, y1, z1), SolidKind.Prop));

        // Assert
        Assert.Equal(SimBuildErrorCode.WorldSolidBoxDegenerate, code);
    }

    [Theory]
    [InlineData(-1, 0, 0, 100, 10, 100)]        // X below Min
    [InlineData(3900, 0, 0, 4001, 10, 100)]     // X above Max
    [InlineData(0, 0, -1, 100, 10, 100)]        // Z below Min
    [InlineData(0, 0, 3900, 100, 10, 4001)]     // Z above Max
    [InlineData(0, 0, 0, 100, 1_000_001, 100)]  // |Y| over MaxCoordMm
    [InlineData(0, -1_000_001, 0, 100, 0, 100)] // |Y| over MaxCoordMm
    [InlineData(5000, 0, 5000, 6000, 10, 6000)] // wholly outside
    public void Ac1_Build_SolidOutsideBounds_ThrowsWorldSolidOutsideBounds(int x0, int y0, int z0, int x1, int y1, int z1)
    {
        // Arrange
        var b = SmallBuilder();
        b.AddSolid(EntityId.None, Box(x0, y0, z0, x1, y1, z1), SolidKind.Prop);

        // Act
        SimBuildErrorCode code = CodeOf(() => b.Build(700, 900));

        // Assert
        Assert.Equal(SimBuildErrorCode.WorldSolidOutsideBounds, code);
    }

    [Fact]
    public void Ac1_Build_SolidExactlyOnTheBoundsEdges_IsAccepted()
    {
        // Arrange
        var b = SmallBuilder();
        b.AddSolid(EntityId.None, Box(0, -1_000_000, 0, 4000, 1_000_000, 4000), SolidKind.Roof);

        // Act
        WorldTables w = b.Build(700, 900);

        // Assert
        Assert.Equal(1, w.StaticSolids.Length);
        Assert.Equal(new ushort[] { 0 }, Query(w, Box(4000, 0, 4000, 4000, 0, 4000)));
    }

    [Fact]
    public void Ac1_Build_OutsideBoundsCheckUsesTheFinalBounds_SoWideningBoundsAfterAddSolidIsAccepted()
    {
        // Arrange
        var b = SmallBuilder();
        b.AddSolid(EntityId.None, Box(-500, 0, -500, 100, 10, 100), SolidKind.Prop);
        b.SetBounds(new MapBounds(-1000, -1000, 4000, 4000));

        // Act
        WorldTables w = b.Build(700, 900);

        // Assert
        Assert.Equal(new ushort[] { 0 }, Query(w, Box(-1000, 0, -1000, -500, 0, -500)));
    }

    [Fact]
    public void Ac1_AddSolid_65535SolidsAccepted_The65536thThrowsWorldTooManySolids()
    {
        // Arrange
        var b = SmallBuilder();
        for (int i = 0; i < WorldTablesBuilder.MaxStaticSolids; i++)
        {
            int x = i % 4000;
            b.AddSolid(EntityId.None, Box(x, 0, 0, x + 1, 1, 1), SolidKind.Prop);
        }

        // Act
        SimBuildErrorCode code = CodeOf(() => b.AddSolid(EntityId.None, Box(0, 0, 0, 1, 1, 1), SolidKind.Prop));
        WorldTables w = b.Build(700, 900);

        // Assert
        Assert.Equal(65_535, WorldTablesBuilder.MaxStaticSolids);
        Assert.Equal(SimBuildErrorCode.WorldTooManySolids, code);
        Assert.Equal(65_535, w.StaticSolids.Length);
        Assert.Equal(65_534, w.StaticSolids[65_534].Index);
        ushort[] hits = Query(w, Box(3999, 0, 0, 3999, 0, 0), 64);
        Assert.Equal(32, hits.Length); // i % 4000 in {3998, 3999} for i < 65,535
        Assert.Equal(new ushort[] { 3998, 3999, 7998, 7999 }, hits.AsSpan(0, 4).ToArray());
        Assert.Equal(63_999, hits[^1]);
    }

    [Fact]
    public void Ac1_Build_SolidsTouchingTooManyBuckets_ThrowsWorldSolidGridTooLarge()
    {
        // Arrange: 500 x 500 buckets per whole-map solid; 68 of them exceed 2^24 bucket entries.
        var b = new WorldTablesBuilder();
        b.SetCorners(1, 1, new int[4]);
        b.SetBounds(new MapBounds(-1_000_000, -1_000_000, 1_000_000, 1_000_000));
        for (int i = 0; i < 68; i++)
        {
            b.AddSolid(EntityId.None, Box(-1_000_000, 0, -1_000_000, 1_000_000, 1, 1_000_000), SolidKind.Roof);
        }

        // Act
        SimBuildErrorCode code = CodeOf(() => b.Build(700, 900));

        // Assert
        Assert.Equal(SimBuildErrorCode.WorldSolidGridTooLarge, code);
    }

    [Fact]
    public void Ac1_SolidBuildErrorCodes_AreDistinct_AndValuesArePinnedAppendOnly()
    {
        // Arrange
        var codes = new[]
        {
            SimBuildErrorCode.WorldSolidBoxDegenerate,
            SimBuildErrorCode.WorldSolidOutsideBounds,
            SimBuildErrorCode.WorldTooManySolids,
            SimBuildErrorCode.WorldSolidGridTooLarge,
        };

        // Act / Assert
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.Equal(24, (int)SimBuildErrorCode.WorldSolidBoxDegenerate);
        Assert.Equal(25, (int)SimBuildErrorCode.WorldSolidOutsideBounds);
        Assert.Equal(26, (int)SimBuildErrorCode.WorldTooManySolids);
        Assert.Equal(27, (int)SimBuildErrorCode.WorldSolidGridTooLarge);
        Assert.Equal(26, (int)CounterId.WorldSolidQueryOverflow);
        Assert.Equal(27, (int)CounterId.WorldSolidQueryFallback);
    }

    [Fact]
    public void Ac1_Builder_IsReusable_AndBuiltTablesDoNotSeeLaterSolids()
    {
        // Arrange
        var b = SmallBuilder();
        b.AddSolid(EntityId.None, Box(0, 0, 0, 10, 10, 10), SolidKind.Prop);
        WorldTables first = b.Build(700, 900);

        // Act
        b.AddSolid(EntityId.None, Box(0, 0, 0, 10, 10, 10), SolidKind.Prop);
        WorldTables second = b.Build(700, 900);

        // Assert
        Assert.Equal(1, first.StaticSolids.Length);
        Assert.Equal(2, second.StaticSolids.Length);
        Assert.Equal(new ushort[] { 0 }, Query(first, Box(5, 5, 5, 5, 5, 5)));
    }

    [Fact]
    public void Td028_StaticSolid_EqualityAndHash_AreFieldWise()
    {
        // Arrange
        AabbMm box = Box(0, 0, 0, 1, 2, 3);
        var a = new StaticSolid(4, EntityId.None, box, SolidKind.Roof);
        var same = new StaticSolid(4, EntityId.None, box, SolidKind.Roof);

        // Act / Assert
        Assert.True(a == same);
        Assert.True(a.Equals((object)same));
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.True(a != new StaticSolid(5, EntityId.None, box, SolidKind.Roof));
        Assert.True(a != new StaticSolid(4, EntityId.Compose(EntityClass.Construct, 1), box, SolidKind.Roof));
        Assert.True(a != new StaticSolid(4, EntityId.None, Box(0, 0, 0, 1, 2, 4), SolidKind.Roof));
        Assert.True(a != new StaticSolid(4, EntityId.None, box, SolidKind.House));
    }

    // ---- AC-2: query == brute force ----

    [Theory]
    [InlineData(3000, 3000, 3000, 3000, 3000, 3000, new ushort[] { 0, 1 })]          // house corner + roof underside (touching)
    [InlineData(3001, 0, 3001, 3999, 2999, 3999, new ushort[0])]                     // the gap between house, roof and box 2
    [InlineData(8000, 2000, 8000, 9000, 2500, 9000, new ushort[] { 2 })]             // touches box 2's far top corner only
    [InlineData(0, -50, 0, 16000, 5000, 16000, new ushort[] { 0, 1, 2, 3 })]         // the whole map (16 buckets: merge path)
    [InlineData(16000, 0, 16000, 20000, 3000, 20000, new ushort[] { 1, 3 })]         // pokes past the far corner, touches its faces
    [InlineData(-9000, 0, -9000, -1, 9000, -1, new ushort[0])]                       // wholly outside the bounds
    [InlineData(5000, 0, 5000, 4000, 9000, 9000, new ushort[0])]                     // inverted on X: empty
    [InlineData(0, 4001, 0, 16000, 9000, 16000, new ushort[0])]                      // above the roof
    [InlineData(3999, 0, 3999, 4000, 0, 4000, new ushort[] { 2 })]                   // straddles a bucket corner, touches box 2
    public void Ac2_HandBuiltSet_KnownRegions_ReturnExactAscendingIndices(int x0, int y0, int z0, int x1, int y1, int z1, ushort[] expected)
    {
        // Arrange
        WorldTables w = HandBuilt();

        // Act
        ushort[] hits = Query(w, Box(x0, y0, z0, x1, y1, z1));

        // Assert
        Assert.Equal(expected, hits);
    }

    [Theory]
    [InlineData(0x5eed_0001UL)]
    [InlineData(0x5eed_0002UL)]
    public void Ac2_RandomSolids_TenThousandRandomRegions_EqualBruteForce_AscendingWithoutDuplicates(ulong seed)
    {
        // Arrange
        var counters = new SimCounters();
        WorldTables w = RandomWorld(seed, counters);
        var rng = new Pcg32(seed ^ 0xA5A5UL, 7);
        var got = new ushort[RandomSolidCount];
        var want = new ushort[RandomSolidCount];
        int totalHits = 0;
        int touchingMatched = 0;

        for (int q = 0; q < RandomRegionCount; q++)
        {
            AabbMm region = RandomRegion(ref rng, w.StaticSolids);

            // Act
            int n = w.QueryStaticSolids(in region, got);
            int m = BruteForce(w, in region, want);

            // Assert
            Assert.True(n == m, $"query {q}: {n} hits, brute force {m}");
            Assert.True(got.AsSpan(0, n).SequenceEqual(want.AsSpan(0, m)), $"query {q}: index lists differ");
            for (int i = 1; i < n; i++)
            {
                Assert.True(got[i - 1] < got[i], $"query {q}: not strictly ascending at {i}");
            }

            totalHits += n;
            for (int i = 0; i < n; i++)
            {
                if (w.StaticSolids[got[i]].Box.Max.X == region.Min.X)
                {
                    touchingMatched++;
                }
            }
        }

        // The fixture actually exercised what it claims to.
        Assert.True(totalHits > RandomRegionCount, $"too few hits overall ({totalHits})");
        Assert.True(touchingMatched > 100, $"too few touching-face hits ({touchingMatched})");
        Assert.True(counters[CounterId.WorldSolidQueryFallback] > 100, "fallback scan never ran");
        Assert.True(counters[CounterId.WorldSolidQueryFallback] < RandomRegionCount / 2, "the merge path barely ran");
        Assert.Equal(0, counters[CounterId.WorldSolidQueryOverflow]);
    }

    [Fact]
    public void Ac2_RandomSolids_ManySolidsSpanSeveralBuckets()
    {
        // Arrange
        WorldTables w = RandomWorld(0x5eed_0001UL);
        int multi = 0;

        // Act
        foreach (StaticSolid s in w.StaticSolids)
        {
            int bx0 = (s.Box.Min.X - MinX) / StaticSolidGrid.BucketSizeMm;
            int bx1 = (s.Box.Max.X - MinX) / StaticSolidGrid.BucketSizeMm;
            int bz0 = (s.Box.Min.Z - MinZ) / StaticSolidGrid.BucketSizeMm;
            int bz1 = (s.Box.Max.Z - MinZ) / StaticSolidGrid.BucketSizeMm;
            if ((bx1 - bx0 + 1) * (bz1 - bz0 + 1) >= 4)
            {
                multi++;
            }
        }

        // Assert
        Assert.Equal(RandomSolidCount, w.StaticSolids.Length);
        Assert.True(multi > 400, $"only {multi} solids span 4+ buckets");
    }

    [Fact]
    public void Ac2_HandBuiltSet_TenThousandRandomRegions_EqualBruteForce()
    {
        // Arrange
        WorldTables w = HandBuilt();
        var rng = new Pcg32(0x4a4dUL, 3);
        var got = new ushort[8];
        var want = new ushort[8];

        for (int q = 0; q < RandomRegionCount; q++)
        {
            int x0 = Next(ref rng, -2000, 18_000);
            int z0 = Next(ref rng, -2000, 18_000);
            int y0 = Next(ref rng, -500, 4500);
            var region = Box(x0, y0, z0, x0 + Next(ref rng, 0, 9000), y0 + Next(ref rng, 0, 1500), z0 + Next(ref rng, 0, 9000));

            // Act
            int n = w.QueryStaticSolids(in region, got);
            int m = BruteForce(w, in region, want);

            // Assert
            Assert.True(got.AsSpan(0, n).SequenceEqual(want.AsSpan(0, m)), $"query {q} differs");
        }
    }

    [Fact]
    public void Ac2_RegionTouchingMoreBucketsThanTheMergeBuffer_FallsBackToAscendingScan_AndCountsIt()
    {
        // Arrange
        var counters = new SimCounters();
        WorldTables w = RandomWorld(0x5eed_0003UL, counters);
        AabbMm all = Box(MinX, -100_000, MinZ, MaxX, 100_000, MaxZ); // 28 x 26 buckets > MaxMergeBuckets
        var got = new ushort[RandomSolidCount];

        // Act
        int n = w.QueryStaticSolids(in all, got);

        // Assert
        Assert.Equal(RandomSolidCount, n);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(i, got[i]);
        }

        Assert.Equal(1, counters[CounterId.WorldSolidQueryFallback]);
        Assert.Equal(0, counters[CounterId.WorldSolidQueryOverflow]);
    }

    [Fact]
    public void Ac2_RegionWithinTheMergeBuffer_DoesNotCountAFallback()
    {
        // Arrange
        var counters = new SimCounters();
        WorldTables w = HandBuilt(counters);

        // Act
        ushort[] hits = Query(w, Box(0, 0, 0, 16000, 5000, 16000)); // all 16 buckets

        // Assert
        Assert.Equal(new ushort[] { 0, 1, 2, 3 }, hits);
        Assert.Equal(0, counters[CounterId.WorldSolidQueryFallback]);
    }

    [Theory]
    [InlineData(0, 0, 0, 10, 10, 10, 10, 10, 10, 20, 20, 20, true)]   // corner touch
    [InlineData(0, 0, 0, 10, 10, 10, 10, 0, 0, 20, 10, 10, true)]     // face touch
    [InlineData(0, 0, 0, 10, 10, 10, 11, 0, 0, 20, 10, 10, false)]    // 1 mm gap on X
    [InlineData(0, 0, 0, 10, 10, 10, 0, 11, 0, 10, 20, 10, false)]    // 1 mm gap on Y
    [InlineData(0, 0, 0, 10, 10, 10, 0, 0, 11, 10, 10, 20, false)]    // 1 mm gap on Z
    [InlineData(0, 0, 0, 10, 10, 10, 5, 5, 5, 5, 5, 5, true)]         // point inside
    [InlineData(0, 0, 0, 10, 10, 10, 6, 0, 0, 4, 10, 10, false)]      // inverted box: empty
    public void Ac2_Overlaps_IsTheClosedIntervalTest(int ax0, int ay0, int az0, int ax1, int ay1, int az1, int bx0, int by0, int bz0, int bx1, int by1, int bz1, bool expected)
    {
        // Arrange
        AabbMm a = Box(ax0, ay0, az0, ax1, ay1, az1);
        AabbMm b = Box(bx0, by0, bz0, bx1, by1, bz1);

        // Act / Assert
        Assert.Equal(expected, StaticSolidGrid.Overlaps(in a, in b));
        Assert.Equal(expected, StaticSolidGrid.Overlaps(in b, in a));
    }

    // ---- AC-3: overflow ----

    [Theory]
    [InlineData(0, new ushort[0], 1)]
    [InlineData(1, new ushort[] { 0 }, 1)]
    [InlineData(3, new ushort[] { 0, 1, 2 }, 1)]
    [InlineData(4, new ushort[] { 0, 1, 2, 3 }, 0)]   // exactly fits: not truncated
    [InlineData(9, new ushort[] { 0, 1, 2, 3 }, 0)]
    public void Ac3_ShortSpan_WritesLowestIndexHits_ReturnsCount_AndCountsOncePerTruncatingCall(int capacity, ushort[] expected, long overflows)
    {
        // Arrange
        var counters = new SimCounters();
        WorldTables w = HandBuilt(counters);
        AabbMm all = Box(0, 0, 0, 16000, 5000, 16000);
        var buffer = new ushort[capacity];

        // Act
        int n = w.QueryStaticSolids(in all, buffer);

        // Assert
        Assert.Equal(expected.Length, n);
        Assert.Equal(expected, buffer.AsSpan(0, n).ToArray());
        Assert.Equal(overflows, counters[CounterId.WorldSolidQueryOverflow]);
    }

    [Fact]
    public void Ac3_EmptySpan_WithNoHits_DoesNotCount()
    {
        // Arrange
        var counters = new SimCounters();
        WorldTables w = HandBuilt(counters);
        AabbMm gap = Box(3001, 0, 3001, 3999, 2999, 3999);

        // Act
        int n = w.QueryStaticSolids(in gap, Span<ushort>.Empty);

        // Assert
        Assert.Equal(0, n);
        Assert.Equal(0, counters[CounterId.WorldSolidQueryOverflow]);
    }

    [Fact]
    public void Ac3_RandomShortSpans_OnBothPaths_ReturnTheBruteForcePrefix_AndCountExactlyTheTruncatingCalls()
    {
        // Arrange
        var counters = new SimCounters();
        WorldTables w = RandomWorld(0x5eed_0004UL, counters);
        var rng = new Pcg32(0xC0FFEEUL, 11);
        var got = new ushort[RandomSolidCount];
        var want = new ushort[RandomSolidCount];
        long expectedOverflows = 0;

        for (int q = 0; q < RandomRegionCount; q++)
        {
            AabbMm region = RandomRegion(ref rng, w.StaticSolids);
            int capacity = Next(ref rng, 0, 12);

            // Act
            int n = w.QueryStaticSolids(in region, got.AsSpan(0, capacity));
            int m = BruteForce(w, in region, want);

            // Assert
            Assert.Equal(Math.Min(m, capacity), n);
            Assert.True(got.AsSpan(0, n).SequenceEqual(want.AsSpan(0, n)), $"query {q}: not the lowest-index prefix");
            if (m > capacity)
            {
                expectedOverflows++;
            }
        }

        Assert.True(expectedOverflows > 1000, $"fixture truncated only {expectedOverflows} calls");
        Assert.Equal(expectedOverflows, counters[CounterId.WorldSolidQueryOverflow]);
    }

    [Fact]
    public void Ac3_NoCountersBound_TruncatingCall_StillReturnsThePrefixWithoutThrowing()
    {
        // Arrange
        WorldTables w = HandBuilt();
        var buffer = new ushort[2];

        // Act
        int n = w.QueryStaticSolids(Box(0, 0, 0, 16000, 5000, 16000), buffer);

        // Assert
        Assert.Equal(2, n);
        Assert.Equal(new ushort[] { 0, 1 }, buffer);
    }
}

/// <summary>
/// Allocation guardrail for <see cref="IWorldQuery.QueryStaticSolids"/> (story 003 AC-3, ADR-0026 Performance
/// Implications: 0 B). NonParallel so concurrent tests do not disturb <see cref="AllocAssert"/>.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class StaticSolidQueryAllocationTests
{
    [Fact]
    public void Ac3_TenThousandQueries_MergeFallbackAndTruncating_Allocate0Bytes()
    {
        // Arrange: precomputed regions so the window measures only the query.
        var counters = new SimCounters();
        WorldTables w = StaticSolidQueryTests.RandomWorld(0x5eed_0005UL, counters);
        var rng = new Pcg32(0xA110CUL, 5);
        var regions = new AabbMm[StaticSolidQueryTests.RandomRegionCount];
        for (int i = 0; i < regions.Length; i++)
        {
            regions[i] = StaticSolidQueryTests.RandomRegion(ref rng, w.StaticSolids);
        }

        var buffer = new ushort[8];
        long sink = 0;

        // Act / Assert
        int runs = AllocAssert.Zero(
            "QueryStaticSolids x 10,000",
            () =>
            {
                for (int i = 0; i < regions.Length; i++)
                {
                    sink += w.QueryStaticSolids(in regions[i], buffer);
                }
            });

        Assert.NotEqual(0, sink);
        Assert.True(counters[CounterId.WorldSolidQueryFallback] > 0, "fallback path not measured");
        Assert.True(counters[CounterId.WorldSolidQueryOverflow] > 0, "truncating path not measured");
        Assert.True(runs >= 3);
    }
}
