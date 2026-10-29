using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 015 (ADR-0014 section 2, ADR-0013 section 6; GDD P11; PCM-P11-1, PCM-P11-2c, PCM-PERF-1): the
/// <see cref="FocusOcclusion"/> rows and <see cref="FocusStep"/>'s lazy, score-ordered occlusion. The direct rows use a 2 000 mm
/// segment from <see cref="_chest"/> straight down −Z to <see cref="_anchor"/> on <see cref="FocusRig.FlatWorld"/>.
/// </summary>
public class FocusOcclusionTests
{
    private static readonly Vec3Mm _chest = new(41_000, 1000, 42_000);
    private static readonly Vec3Mm _anchor = new(41_000, 1000, 40_000);
    private static readonly EntityId _target = FocusRig.Ref(1).Entity;
    private static readonly EntityId _other = FocusRig.Ref(2).Entity;

    private static AabbMm Box(int minX, int minY, int minZ, int maxX, int maxY, int maxZ) =>
        new(new Vec3Mm(minX, minY, minZ), new Vec3Mm(maxX, maxY, maxZ));

    /// <summary>An 80 mm cube 80 % of the way from the rig's chest to <paramref name="anchor"/>, owned by no entity.</summary>
    private static AabbMm BlockerFor(Vec3Mm anchor)
    {
        int x = FocusRig.Feet.X + ((anchor.X - FocusRig.Feet.X) * 4 / 5);
        int z = FocusRig.Feet.Z + ((anchor.Z - FocusRig.Feet.Z) * 4 / 5);
        return Box(x - 40, 960, z - 40, x + 40, 1040, z + 40);
    }

    private static bool Occluded(IWorldQuery w, Vec3Mm anchor, out FocusCounters c)
    {
        c = default;
        Span<ushort> solids = stackalloc ushort[CarrierScratch.SolidCapacity];
        return FocusOcclusion.IsOccluded(w, in _chest, in anchor, _target, solids, ref c);
    }

    // ---- PCM-P11-1: occlusion rows ----

    [Theory]
    [Trait("Id", "PCM-P11-1")]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void IsOccluded_SolidOnSegment_OccludesUnlessOwnedByTheAnchorEntity(bool otherOwner, bool expected)
    {
        // Arrange: a house-like box across the middle of the segment.
        WorldTables w = FocusRig.FlatWorld(b => b.AddSolid(otherOwner ? _other : _target, Box(40_500, 0, 40_800, 41_500, 3000, 41_200), SolidKind.House));

        // Act
        bool occluded = Occluded(w, _anchor, out FocusCounters c);

        // Assert
        Assert.Equal(expected, occluded);
        Assert.Equal(1, c.OcclusionTests);
    }

    [Theory]
    [Trait("Id", "PCM-P11-1")]
    [InlineData(40_000, true)]   // the face touches the segment at t = 1000 exactly: IsHit's enter <= 1000 makes it a hit
    [InlineData(39_999, false)]  // 1 mm short of the anchor end: a miss
    public void IsOccluded_BoxFaceAtTheAnchorEnd_FollowsTheSlabHitRule(int maxZ, bool expected)
    {
        // Arrange
        WorldTables w = FocusRig.FlatWorld(b => b.AddSolid(_other, Box(40_500, 0, 39_000, 41_500, 3000, maxZ), SolidKind.Prop));

        // Act + Assert
        Assert.Equal(expected, Occluded(w, _anchor, out _));
    }

    [Theory]
    [Trait("Id", "PCM-P11-1")]
    [InlineData(2000, 1000, true)]  // a ridge (corner row z = 40 000 at 2 000 mm) between chest and anchor
    [InlineData(0, 0, false)]       // anchor resting on flat ground: first contact at fPm = 1000 (TD-175 item 3)
    [InlineData(0, -10, true)]      // anchor 10 mm under the ground: contact before the anchor end
    public void IsOccluded_Terrain_OccludesUnlessTheContactIsAtTheAnchorEnd(int ridgeMm, int anchorY, bool expected)
    {
        // Arrange: chest and anchor 200 mm either side of the ridge tiles' outer corners (terrain 200 mm under both on the ridge).
        const int Tiles = 40;
        int[] corners = new int[(Tiles + 1) * (Tiles + 1)];
        Array.Fill(corners, ridgeMm, 20 * (Tiles + 1), Tiles + 1);
        var b = new WorldTablesBuilder();
        b.SetCorners(Tiles, Tiles, corners);
        WorldTables w = b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
        Vec3Mm chest = new(41_000, 1000, 41_800);
        Vec3Mm anchor = new(41_000, anchorY, ridgeMm > 0 ? 38_200 : 40_000);
        var c = default(FocusCounters);

        // Act
        bool occluded = FocusOcclusion.IsOccluded(w, in chest, in anchor, _target, stackalloc ushort[CarrierScratch.SolidCapacity], ref c);

        // Assert
        Assert.Equal(expected, occluded);
    }

    [Fact]
    [Trait("Id", "PCM-P11-1")]
    public void IsOccluded_BlockedTileUnderTheSegment_OccludesAtAnyHeight()
    {
        // Arrange: tile (20, 20) is x/z 40 000 to 42 000; the segment runs through it 1 m above the flat ground.
        WorldTables w = FocusRig.FlatWorld(b => b.MarkBlocked(20, 20));

        // Act + Assert
        Assert.True(Occluded(w, _anchor, out _));
    }

    [Fact]
    [Trait("Id", "PCM-P11-1")]
    public void IsOccluded_ClearSegment_QueriesSolidsOnceOverTheSegmentAabbIntoTheScratch()
    {
        // Arrange: solids off the segment only.
        var w = new CountingWorld(FocusRig.FlatWorld(b => b.AddSolid(_other, Box(45_000, 0, 45_000, 46_000, 3000, 46_000), SolidKind.Prop)));
        Vec3Mm anchor = new(40_500, 1200, 40_000);
        var c = default(FocusCounters);
        Span<ushort> solids = stackalloc ushort[48];

        // Act
        bool occluded = FocusOcclusion.IsOccluded(w, in _chest, in anchor, _target, solids, ref c);

        // Assert
        Assert.False(occluded);
        Assert.Equal(1, w.SolidQueries);
        Assert.Equal(Box(40_500, 1000, 40_000, 41_000, 1200, 42_000), w.LastRegion);
        Assert.Equal(48, w.LastScratchLength);
    }

    // ---- Lazy order (PCM-PERF-1) ----

    /// <summary>Five candidates whose scores rise with k (distance and angle both grow): 160, 208, 273, 340, 405.</summary>
    private static Vec3Mm Fan(int k) => FocusRig.Ahead(1000 + (200 * k), 250 * k);

    [Theory]
    [Trait("Id", "PCM-PERF-1")]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public void Step_NoIncumbent_TestsInScoreOrderAndStopsAtTheFirstClear(int occludedBest, int expectedTests)
    {
        // Arrange
        var rig = new FocusRig(world: b =>
        {
            for (int k = 0; k < occludedBest; k++)
            {
                b.AddSolid(EntityId.None, BlockerFor(Fan(k)), SolidKind.Prop);
            }
        });
        AnchorRef[] fan = new AnchorRef[5];
        for (int k = 0; k < 5; k++)
        {
            fan[k] = rig.Add((uint)(k + 1), Fan(k));
        }

        // Act
        AnchorRef focused = rig.Tick();

        // Assert
        Assert.Equal(fan[occludedBest], focused);
        Assert.Equal(5, rig.Focus.Probes.Candidates);
        Assert.Equal(expectedTests, rig.Focus.Probes.OcclusionTests);
        Assert.True(rig.Counters[CounterId.FocusOcclusionTests] <= rig.Counters[CounterId.FocusCandidates]);
    }

    [Fact]
    [Trait("Id", "PCM-PERF-1")]
    public void Step_IncumbentIsTheBestAndClear_RunsOneTest()
    {
        // Arrange
        var rig = new FocusRig();
        for (int k = 0; k < 5; k++)
        {
            rig.Add((uint)(k + 1), Fan(k));
        }

        AnchorRef incumbent = rig.Tick();

        // Act
        AnchorRef focused = rig.Tick();

        // Assert
        Assert.Equal(incumbent, focused);
        Assert.Equal(1, rig.Focus.Probes.OcclusionTests);
    }

    // ---- Incumbent occlusion ----

    [Theory]
    [Trait("Id", "PCM-P11-1")]
    [InlineData(1100, -300, 1600, 400)]  // the occluded incumbent still scores best: tested first, then the next one passes
    [InlineData(1600, -500, 900, 300)]   // the other anchor now scores best: the one extra incumbent test (TD-175 item 1) drops it
    public void Step_UnlatchedIncumbentBecomesOccluded_DropsItAndFocusesTheNextClearThatTick(int aDist, int aSide, int bDist, int bSide)
    {
        // Arrange: A is focused; then A moves behind a blocker while B stays clear.
        Vec3Mm hidden = FocusRig.Ahead(aDist, aSide);
        var rig = new FocusRig(world: b => b.AddSolid(EntityId.None, BlockerFor(hidden), SolidKind.Prop));
        AnchorRef a = rig.Add(1, FocusRig.Ahead(1000));
        AnchorRef b = rig.Add(2, FocusRig.Ahead(1600, 400));
        Assert.Equal(a, rig.Tick());
        rig.Anchors.Move(a, hidden);
        rig.Anchors.Move(b, FocusRig.Ahead(bDist, bSide));

        // Act
        AnchorRef focused = rig.Tick();

        // Assert: no dwell on loss (focusDwellTicks is 4).
        Assert.Equal(b, focused);
        Assert.Equal(2, rig.Focus.Probes.OcclusionTests);
    }

    [Fact]
    [Trait("Id", "PCM-P11-2c")]
    public void Step_LatchedIncumbentOccludedForOneTick_KeepsFocusAndSkipsTheTest()
    {
        // Arrange
        Vec3Mm clear = FocusRig.Ahead(1000);
        Vec3Mm hidden = FocusRig.Ahead(1100, -300);
        var rig = new FocusRig(world: b => b.AddSolid(EntityId.None, BlockerFor(hidden), SolidKind.Prop));
        AnchorRef a = rig.Add(1, clear);
        rig.Add(2, FocusRig.Ahead(1600, 400));
        Assert.Equal(a, rig.Tick());
        long before = rig.Counters[CounterId.FocusOcclusionTests];
        rig.State.Latched = true;
        rig.Anchors.Move(a, hidden);

        // Act
        AnchorRef during = rig.Tick();
        rig.Anchors.Move(a, clear);
        AnchorRef after = rig.Tick();

        // Assert
        Assert.Equal(a, during);
        Assert.Equal(a, after);
        Assert.Equal(0, rig.Focus.Probes.OcclusionTests);
        Assert.Equal(before, rig.Counters[CounterId.FocusOcclusionTests]);
    }

    /// <summary>Delegates to a <see cref="WorldTables"/> and records the <see cref="QueryStaticSolids"/> calls.</summary>
    private sealed class CountingWorld : IWorldQuery
    {
        private readonly WorldTables _inner;

        public CountingWorld(WorldTables inner) => _inner = inner;

        public int SolidQueries { get; private set; }

        public AabbMm LastRegion { get; private set; }

        public int LastScratchLength { get; private set; }

        public int TileSizeMm => _inner.TileSizeMm;

        public int TilesX => _inner.TilesX;

        public int TilesZ => _inner.TilesZ;

        public MapBounds Bounds => _inner.Bounds;

        public ReadOnlySpan<StaticSolid> StaticSolids => _inner.StaticSolids;

        public ReadOnlySpan<LotRecord> Lots => _inner.Lots;

        public PostOfficeRecord PostOffice => _inner.PostOffice;

        public long TerrainHeightMm(long xMm, long zMm) => _inner.TerrainHeightMm(xMm, zMm);

        public void GetCorners(int tx, int tz, out HeightCorners h) => _inner.GetCorners(tx, tz, out h);

        public void TerrainGradientPm(long xMm, long zMm, out int gxPm, out int gzPm) => _inner.TerrainGradientPm(xMm, zMm, out gxPm, out gzPm);

        public int WaterDepthMm(long xMm, long zMm) => _inner.WaterDepthMm(xMm, zMm);

        public TileFlags Tile(int tx, int tz) => _inner.Tile(tx, tz);

        public Vec3Mm ClampInsideMap(in Vec3Mm p, int insetMm) => _inner.ClampInsideMap(in p, insetMm);

        public bool TrySnapWalkable(in Vec3Mm p, int radiusMm, out Vec3Mm snapped) => _inner.TrySnapWalkable(in p, radiusMm, out snapped);

        public StringId StreetNameAt(long xMm, long zMm) => _inner.StreetNameAt(xMm, zMm);

        public int QueryStaticSolids(in AabbMm region, Span<ushort> indices)
        {
            SolidQueries++;
            LastRegion = region;
            LastScratchLength = indices.Length;
            return _inner.QueryStaticSolids(in region, indices);
        }
    }
}

/// <summary>Story 015's advisory 0 B check (ADR-0014 erratum E2: reported, never failing, until M0). Joins the non-parallel collection (see <see cref="AllocAssert"/>).</summary>
[Collection(NonParallelCollection.Name)]
public sealed class FocusOcclusionAllocationTests
{
    [Fact]
    [Trait("Id", "PCM-PERF-1")]
    public void IsOccluded_10000Passes_ReportsAllocatedBytes()
    {
        // Arrange: a diagonal segment (x + z = 84 500) whose AABB holds a blocked tile, a solid it misses and the anchor's own
        // solid it crosses, so every branch runs and the segment stays clear.
        WorldTables w = FocusRig.FlatWorld(b =>
        {
            b.AddSolid(FocusRig.Ref(1).Entity, new AabbMm(new Vec3Mm(42_000, 0, 42_000), new Vec3Mm(42_500, 3000, 42_500)), SolidKind.House);
            b.AddSolid(EntityId.None, new AabbMm(new Vec3Mm(41_000, 0, 41_000), new Vec3Mm(41_400, 3000, 41_400)), SolidKind.Prop);
            b.MarkBlocked(20, 20);
        });
        Vec3Mm chest = new(41_000, 1000, 43_500);
        Vec3Mm anchor = new(43_500, 1000, 41_000);
        EntityId target = FocusRig.Ref(1).Entity;
        int hidden = 0;
        var c = default(FocusCounters);

        // Act
        (long bytes, int runs) = AllocAssert.Measure(() =>
        {
            Span<ushort> solids = stackalloc ushort[CarrierScratch.SolidCapacity];
            for (int i = 0; i < 10_000; i++)
            {
                hidden += FocusOcclusion.IsOccluded(w, in chest, in anchor, target, solids, ref c) ? 1 : 0;
            }
        });

        // Assert: reported only (advisory until M0); the segment is clear, so the work ran.
        TestContext.Current.TestOutputHelper?.WriteLine($"FocusOcclusion.IsOccluded x 10 000: {bytes} B (min of {runs} runs; advisory, ADR-0014 E2)");
        Assert.Equal(0, hidden);
        Assert.Equal(10_000L * runs, c.OcclusionTests);
    }
}
