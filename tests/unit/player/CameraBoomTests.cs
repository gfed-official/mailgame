using PerformativeMail.Client.Core.Camera;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 021 (GDD P9, PCM-P9-1; TR-movement-024): camera profiles and settings, the aim blend, the boom solve
/// against the Sim geometry, smoothing, the terrain and water floor, and that the solid set is the world query's.
/// </summary>
[Trait("Id", "PCM-P9-1")]
public sealed class CameraBoomTests
{
    private const int Desired = 3000;
    private const int Shoulder = 600;

    [Fact]
    public void ProfilesSettingsAndAimBlend_GiveTheP9Values()
    {
        // Arrange
        var settings = new CameraSettings();

        // Act + Assert: default, aim and swim profiles.
        Assert.Equal((1600, 3000, 600), (CameraProfile.Default.PivotHeightMm, CameraProfile.Default.BoomMm, settings.ShoulderMm(CameraProfile.Default)));
        Assert.Equal((2400, 750, 600), (CameraProfile.Aim.BoomMm, CameraProfile.Aim.ShoulderMm, CameraProfile.Aim.LookSensitivityPm));
        Assert.Equal(1100, CameraProfile.Swim.PivotHeightMm);

        // FOV: default 80, unchanged while aiming unless the optional reduction is on; clamped 60..100.
        Assert.Equal((80, 80), (settings.VerticalFovDeg, settings.FovDeg(aiming: true)));
        settings.AimFovReduction = true;
        Assert.Equal(70, settings.FovDeg(aiming: true));
        settings.SetVerticalFov(60);
        Assert.Equal(60, settings.FovDeg(aiming: true));
        settings.SetVerticalFov(59);
        Assert.Equal(60, settings.VerticalFovDeg);
        settings.SetVerticalFov(101);
        Assert.Equal(100, settings.VerticalFovDeg);
        settings.SetVerticalFov(80);
        Assert.Equal(80, settings.VerticalFovDeg);

        // Pitch -60..+70 degrees.
        Assert.Equal((short)-10_923, CameraSettings.ClampPitch(short.MinValue));
        Assert.Equal((short)12_743, CameraSettings.ClampPitch(short.MaxValue));
        Assert.Equal((short)1000, CameraSettings.ClampPitch(1000));

        // Shoulder swap flips the sign, and a second swap restores it.
        settings.SwapShoulder();
        Assert.Equal(-600, settings.ShoulderMm(CameraProfile.Default));
        settings.SwapShoulder();
        Assert.Equal(600, settings.ShoulderMm(CameraProfile.Default));

        // Aim blend: 0 ms Default, 50 ms halfway, 100 ms and later Aim.
        CameraProfile at0 = CameraBlend.Lerp(CameraProfile.Default, CameraProfile.Aim, 0);
        CameraProfile at50 = CameraBlend.Lerp(CameraProfile.Default, CameraProfile.Aim, 50);
        CameraProfile at100 = CameraBlend.Lerp(CameraProfile.Default, CameraProfile.Aim, 100);
        CameraProfile at250 = CameraBlend.Lerp(CameraProfile.Default, CameraProfile.Aim, 250);
        Assert.Equal((600, 3000), (at0.ShoulderMm, at0.BoomMm));
        Assert.Equal((675, 2700), (at50.ShoulderMm, at50.BoomMm));
        Assert.Equal((750, 2400, 600), (at100.ShoulderMm, at100.BoomMm, at100.LookSensitivityPm));
        Assert.Equal((750, 2400), (at250.ShoulderMm, at250.BoomMm));
    }

    [Theory]
    [InlineData("box", 2000, 3)]
    [InlineData("ridge", 1800, 3)]
    [InlineData("clear", 3000, 0)]
    [InlineData("insideBox", BoomSolver.MinBoomMm, 0)]
    public void Solve_Scenario_ReturnsLongestSafeBoom(string scenario, int expectedMm, int toleranceMm)
    {
        // Arrange: a flat 80 m world at 0, pivot 1600 up at (40 000, 40 000), yaw 0 so the boom runs along +Z.
        var pivot = new Vec3Mm(40_000, 1600, 40_000);
        IWorldQuery w = scenario switch
        {
            // Box whose 250-inflated near face is 2000 mm along the boom (z 42 000).
            "box" => FlatWorld(b => b.AddSolid(EntityId.None, new AabbMm(new Vec3Mm(30_000, 0, 42_250), new Vec3Mm(50_000, 5000, 43_000)), SolidKind.House)),
            "ridge" => RidgeWorld(),
            "insideBox" => FlatWorld(b => b.AddSolid(EntityId.None, new AabbMm(new Vec3Mm(39_000, 0, 39_000), new Vec3Mm(41_000, 3000, 41_000)), SolidKind.House)),
            _ => FlatWorld(),
        };

        // Act
        int boom = BoomSolver.Solve(w, in pivot, 0, 0, Shoulder, Desired);

        // Assert
        Assert.InRange(boom, expectedMm - toleranceMm, expectedMm + toleranceMm);
    }

    [Fact]
    public void Step_PullsInWithin50Ms_EasesOutOver400Ms_IgnoresNonPositiveDt()
    {
        // Pull-in: 3000 -> 300 under continuous obstruction, frames of 17 + 17 + 16 ms.
        int pulled = Run(3000, 300, 17, 50, out bool pullMonotonic);
        Assert.Equal(300, pulled);
        Assert.True(pullMonotonic);

        // Ease-out: 300 -> 3000 in 16/17 ms frames; within 5 % at 400 ms, monotonic, never past the target.
        int eased = Run(300, 3000, 17, 400, out bool easeMonotonic);
        Assert.InRange(eased, 2850, 3000);
        Assert.True(easeMonotonic);

        // Zero and negative dt change nothing.
        Assert.Equal(1000, BoomSmoother.Step(1000, 3000, 0));
        Assert.Equal(1000, BoomSmoother.Step(1000, 300, -16));
    }

    [Theory]
    [InlineData("street")]
    [InlineData("beach")]
    [InlineData("swimmer")]
    public void SolveAndCompose_AtPitchPlus70_KeepCameraAboveTerrainFloorAndWater(string row)
    {
        // Arrange: pitch +70 degrees lowers the camera toward the ground behind the carrier.
        short pitch = CameraSettings.MaxPitchBam;
        IWorldQuery w;
        Vec3Mm feet;
        CameraProfile profile = CameraProfile.Default;
        ushort yaw = 0;   // forward -Z, boom along +Z
        bool swimming = false;
        switch (row)
        {
            case "street":
                // On the street in front of house 1's wall (z 24 000), looking at the house.
                w = BuildStreet();
                feet = new Vec3Mm(34_000, M1aStreet.GroundMm, M1aStreet.OddMailboxZMm);
                break;
            case "beach":
                // FX-Beach rises along +Z, so a boom along +Z sits uphill and the view looks downhill.
                w = WorldTablesFixture.Beach();
                feet = new Vec3Mm(1000, -600, 1000);
                break;
            default:
                w = BuildStreet();
                feet = new Vec3Mm(M1aStreet.BeachDeepXMm, 0, M1aStreet.BeachDeepZMm);
                profile = CameraProfile.Swim;
                swimming = true;
                break;
        }

        Vec3Mm pivot = CameraPose.Pivot(in feet, in profile);

        // Act: on land, Compose gets the full (still-lagging, unsmoothed) boom, so only its terrain clamp keeps the camera up.
        int boom = BoomSolver.Solve(w, in pivot, yaw, pitch, profile.ShoulderMm, profile.BoomMm);
        int lagging = swimming ? boom : profile.BoomMm;
        Vec3Mm cam = CameraPose.Compose(w, in pivot, yaw, pitch, profile.ShoulderMm, profile.BoomMm, lagging, swimming);

        // Assert: the solved (unclamped) camera point and the composed one are at or above terrain + 250.
        Vec3Mm full = CameraPose.Offset(yaw, pitch, profile.ShoulderMm, profile.BoomMm);
        long rawX = pivot.X + ((long)full.X * boom / profile.BoomMm);
        long rawY = pivot.Y + ((long)full.Y * boom / profile.BoomMm);
        long rawZ = pivot.Z + ((long)full.Z * boom / profile.BoomMm);
        Assert.True(rawY >= w.TerrainHeightMm(rawX, rawZ) + 250, $"solved camera y {rawY} below terrain floor");
        Assert.True(cam.Y >= w.TerrainHeightMm(cam.X, cam.Z) + 250, $"composed camera y {cam.Y} below terrain floor");
        if (swimming)
        {
            Assert.Equal(0, cam.Y);
        }
        else
        {
            Assert.True(boom < profile.BoomMm, "the ground shortens the boom");
        }
    }

    [Fact]
    public void Solve_UsesTheWorldQuerysSolidSet()
    {
        // Arrange: the same stub box across the boom, once returned by QueryStaticSolids and once not.
        var box = new StaticSolid(0, EntityId.None, new AabbMm(new Vec3Mm(30_000, 0, 42_250), new Vec3Mm(50_000, 5000, 43_000)), SolidKind.House);
        WorldTables flat = FlatWorld();
        var pivot = new Vec3Mm(40_000, 1600, 40_000);

        // Act
        int withHit = BoomSolver.Solve(new StubWorld(flat, box, returnsIndex: true), in pivot, 0, 0, Shoulder, Desired);
        int withoutHit = BoomSolver.Solve(new StubWorld(flat, box, returnsIndex: false), in pivot, 0, 0, Shoulder, Desired);

        // Assert
        Assert.True(withHit < Desired, $"boom {withHit} not shortened");
        Assert.Equal(Desired, withoutHit);
    }

    internal static WorldTables BuildStreet()
    {
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        return b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    private static int Run(int current, int target, int frameMs, int totalMs, out bool monotonic)
    {
        monotonic = true;
        for (int elapsed = 0; elapsed < totalMs; elapsed += frameMs)
        {
            int next = BoomSmoother.Step(current, target, Math.Min(frameMs, totalMs - elapsed));
            monotonic &= target < current ? next <= current && next >= target : next >= current && next <= target;
            current = next;
        }

        return current;
    }

    private static WorldTables FlatWorld(Action<WorldTablesBuilder>? extra = null)
    {
        const int Tiles = 40;
        var b = new WorldTablesBuilder();
        b.SetCorners(Tiles, Tiles, new int[(Tiles + 1) * (Tiles + 1)]);
        extra?.Invoke(b);
        return b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    /// <summary>Flat at 0 with a ridge of corners at 1500 on row z = 42 000 (linear rise from z 40 000).</summary>
    private static WorldTables RidgeWorld()
    {
        const int Tiles = 40;
        var corners = new int[(Tiles + 1) * (Tiles + 1)];
        for (int cx = 0; cx <= Tiles; cx++)
        {
            corners[(21 * (Tiles + 1)) + cx] = 1500;
        }

        var b = new WorldTablesBuilder();
        b.SetCorners(Tiles, Tiles, corners);
        return b.Build(1000, WorldTablesFixture.SwimEnterMm);
    }

    /// <summary>A flat world whose solid set is one stub box, returned by the query only when asked to.</summary>
    private sealed class StubWorld : IWorldQuery
    {
        private readonly WorldTables _inner;
        private readonly StaticSolid[] _solids;
        private readonly bool _returnsIndex;

        public StubWorld(WorldTables inner, StaticSolid solid, bool returnsIndex)
        {
            _inner = inner;
            _solids = new[] { solid };
            _returnsIndex = returnsIndex;
        }

        public int TileSizeMm => _inner.TileSizeMm;

        public int TilesX => _inner.TilesX;

        public int TilesZ => _inner.TilesZ;

        public MapBounds Bounds => _inner.Bounds;

        public ReadOnlySpan<StaticSolid> StaticSolids => _solids;

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
            if (!_returnsIndex || indices.Length == 0)
            {
                return 0;
            }

            indices[0] = 0;
            return 1;
        }
    }
}

/// <summary>Story 021's 0 B check (advisory until M0, erratum E2): one boom solve on the M-1a street plus one smoothing step.</summary>
[Collection(NonParallelCollection.Name)]
[Trait("Id", "PCM-P9-1")]
public sealed class CameraBoomAllocationTests
{
    [Fact]
    public void SolveOnStreetPlusStep_AllocatesZeroBytes()
    {
        // TD-008: allocation counts are meaningful only for optimised Sim and Client.Core builds.
        bool unoptimised = new[] { typeof(BoomSolver).Assembly, typeof(WorldTables).Assembly }.Any(a =>
            Attribute.GetCustomAttribute(a, typeof(System.Diagnostics.DebuggableAttribute)) is System.Diagnostics.DebuggableAttribute { IsJITOptimizerDisabled: true });
        Assert.SkipWhen(unoptimised, "Unoptimised build: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange: on the street 1000 mm from house 1's wall, the boom pointing into the wall and the ground.
        WorldTables street = CameraBoomTests.BuildStreet();
        var pivot = new Vec3Mm(34_000, M1aStreet.GroundMm + 1600, M1aStreet.OddMailboxZMm);
        int boom = 0;
        int smoothed = 3000;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("BoomSolver.Solve on the street + BoomSmoother.Step", () =>
        {
            boom = BoomSolver.Solve(street, in pivot, 32_768, CameraSettings.MaxPitchBam, 600, 3000);
            smoothed = BoomSmoother.Step(3000, boom, 16);
        });
        Assert.True(boom < 3000);
        Assert.True(smoothed < 3000);
    }
}
