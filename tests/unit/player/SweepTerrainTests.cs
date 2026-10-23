using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 007: the sub-step sweep over the height field (GDD P4 terrain half, Edge Cases "Zero, max and out-of-range";
/// ADR-0013 section 4; TR-movement-001/005/006). Worlds are built directly with <see cref="WorldTablesBuilder"/> so a ramp can be walked in Z
/// as well (FX-Ramp itself is one tile deep, which pins Z to 1000 mm through the map clamp).
/// </summary>
public class SweepTerrainTests
{
    private const ushort PlusX = 49152;      // forward = (+1, 0)
    private const ushort MinusX = 16384;     // forward = (-1, 0)
    private const ushort PlusXMinusZ = 40960; // forward = (+1, +1)
    private const ushort MinusXPlusZ = 24576; // forward = (-1, +1)
    private const int Full = 32767;
    private const int RampTileX = 3;         // the rising tile spans x 6000..8000
    private const int Row = 8000;            // a Z away from every map edge

    /// <summary>10 x 8 tiles (x 0..20000, z 0..16000); corners west of the ramp tile at 0, east of it at <paramref name="riseMm"/>.</summary>
    private static WorldTables RampWorld(int riseMm) => Columns(10, 8, (cx, _) => cx > RampTileX ? riseMm : 0);

    private static WorldTables Columns(int tilesX, int tilesZ, Func<int, int, int> corner)
    {
        int stride = tilesX + 1;
        var corners = new int[stride * (tilesZ + 1)];
        for (int cz = 0; cz <= tilesZ; cz++)
        {
            for (int cx = 0; cx <= tilesX; cx++)
            {
                corners[(cz * stride) + cx] = corner(cx, cz);
            }
        }

        var b = new WorldTablesBuilder();
        b.SetCorners(tilesX, tilesZ, corners);
        return b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    /// <summary>Flat, a 650 mm walkable ramp (with a 50 per mille Z twist), a flat shelf, a 3000 mm steep rise (1500 per mille), a high plateau: slopes, cliffs and edges.</summary>
    private static WorldTables MixedWorld()
    {
        int[] columns = { 0, 0, 0, 0, 1300, 1300, 1300, 4300, 4300, 4300, 4300 };
        return Columns(10, 8, (cx, cz) => columns[cx] + ((cz & 1) * 100));
    }

    private sealed class Rig
    {
        private readonly EmptyRemoteCapsuleSource _remotes = new();
        private readonly EmptySupportTransformSource _supports = new();

        public Rig(WorldTables world, int x, int z, int y, LocoState loco = LocoState.Grounded)
        {
            World = world;
            State = new CarrierState { X = x, Z = z, Y = y, Loco = loco };
        }

        public WorldTables World { get; }

        public CarrierState State;

        public CarrierScratch Scratch = new(64);

        public PlayerSettings Settings { get; set; } = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);

        public int Terrain => WorldHeight.At(World, State.X, State.Z);

        public void Tick(in CarrierInput input)
        {
            CarrierEnv env = CarrierEnv.ForM1a(World, Settings, new EntityId(5), _remotes, _supports);
            OwnedCarrier.Step(ref State, in input, in env, ref Scratch);
        }

        public void Tick(int n, in CarrierInput input)
        {
            for (int i = 0; i < n; i++)
            {
                Tick(in input);
            }
        }

        /// <summary>Calls the sweep directly with an explicit tick displacement (one tick of the sweep, no phases 1 to 4).</summary>
        public void Run(int dx, int dz, int dy = 0)
        {
            CarrierEnv env = CarrierEnv.ForM1a(World, Settings, new EntityId(5), _remotes, _supports);
            SimCounterSink sink = default;
            var plan = new GravityPlan { Dy = dy };
            CarrierSweep.Run(ref State, in env, ref Scratch, ref sink, dx, dz, ref plan);
        }
    }

    private static CarrierInput Walk(ushort look, bool sprint = false, int mx = 0) =>
        new() { MoveX = mx, MoveY = Full, LookYaw = look, SprintHeld = sprint, SpeedPermille = 1000 };

    private static CarrierInput Idle() => new() { SpeedPermille = 1000 };

    // ------------------------------------------------------------------ Sub-step split

    [Theory]
    [Trait("Id", "PCM-P4-2a")]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(300, 1)]
    [InlineData(301, 2)]
    [InlineData(600, 2)]
    [InlineData(601, 3)]
    [InlineData(1200, 4)]
    [InlineData(1201, 4)]
    [InlineData(100_000, 4)]
    public void SubStepCount_FollowsCeilDivBy300_ClampedToOneAndFour(int displacementMm, int expected)
    {
        Assert.Equal(expected, CarrierSweep.SubStepCount(displacementMm, 300, 4));
    }

    [Fact]
    [Trait("Id", "PCM-P4-2a")]
    public void SubStepCount_GuardsNonsenseSettings_AndNegativeDisplacement()
    {
        Assert.Equal(1, CarrierSweep.SubStepCount(-50, 0, 0));
        Assert.Equal(2, CarrierSweep.SubStepCount(2, 1, 64));
        Assert.Equal(64, CarrierSweep.SubStepCount(1_000_000, 1, 64));
    }

    [Fact]
    [Trait("Id", "PCM-P4-2a")]
    public void Part_SubStepSharesSumExactlyToTheTickDisplacement_ForEveryTotalAndCount()
    {
        // Act + Assert: every total in a window around 0 and the clamp edge, every n in 1..4.
        for (int total = -1300; total <= 1300; total++)
        {
            for (int n = 1; n <= 4; n++)
            {
                int sum = 0;
                for (int i = 0; i < n; i++)
                {
                    int part = CarrierSweep.Part(total, n, i);
                    sum += part;
                    Assert.True(Math.Abs(part - ((double)total / n)) < 1.0, $"total {total} n {n} part {part}");
                }

                Assert.Equal(total, sum);
            }
        }
    }

    [Theory]
    [Trait("Id", "PCM-P4-2a")]
    [InlineData(1, 1)]
    [InlineData(300, 1)]
    [InlineData(301, 2)]
    [InlineData(1200, 4)]
    public void Run_Displacement_UsesTheExpectedSubSteps_AndMovesExactlyD(int dx, int expectedSubSteps)
    {
        var rig = new Rig(WorldTablesFixture.Dry(), 20_000, 64_000, 1000);

        rig.Run(dx, 0);

        Assert.Equal(expectedSubSteps, rig.Scratch.SubStepsMaxThisTick);
        Assert.Equal(20_000 + dx, rig.State.X);
        Assert.Equal(0, rig.Scratch.StepClamped);
    }

    [Theory]
    [Trait("Id", "PCM-P4-2a")]
    [InlineData(1201)]
    [InlineData(-1201)]
    public void Run_DisplacementOver1200_IsClampedTo1200_UsesFourSubSteps_AndCountsStepClamped(int dx)
    {
        var rig = new Rig(WorldTablesFixture.Dry(), 64_000, 64_000, 1000);

        rig.Run(dx, 0);

        Assert.Equal(4, rig.Scratch.SubStepsMaxThisTick);
        Assert.Equal(1, rig.Scratch.StepClamped);
        Assert.Equal(64_000 + Math.Sign(dx) * 1200, rig.State.X);
    }

    [Fact]
    [Trait("Id", "PCM-P4-2a")]
    public void Run_AFallOver1200_IsClampedToo_AndTheVerticalAxisCountsForTheSubStepCount()
    {
        var rig = new Rig(WorldTablesFixture.Dry(), 64_000, 64_000, 1000 + 50_000, LocoState.Airborne);

        rig.Run(0, 0, dy: -2000);

        Assert.Equal((4, 1, 1000 + 50_000 - 1200), (rig.Scratch.SubStepsMaxThisTick, rig.Scratch.StepClamped, rig.State.Y));
        var one = new Rig(WorldTablesFixture.Dry(), 64_000, 64_000, 1000 + 50_000, LocoState.Airborne);
        one.Run(0, 0, dy: -650);
        Assert.Equal(3, one.Scratch.SubStepsMaxThisTick);
    }

    [Fact]
    [Trait("Id", "PCM-P4-2a")]
    public void Run_UsesTheSettingsForSubStepLengthAndCap()
    {
        var rig = new Rig(WorldTablesFixture.Dry(), 20_000, 64_000, 1000);
        rig.Settings = rig.Settings with { SubStepMm = 100, MaxSubStepsPerTick = 6 };

        rig.Run(550, 0);

        Assert.Equal((6, 20_550), (rig.Scratch.SubStepsMaxThisTick, rig.State.X));
        rig.Run(800, 0);
        Assert.Equal((6, 1, 20_550 + 600), (rig.Scratch.SubStepsMaxThisTick, rig.Scratch.StepClamped, rig.State.X));
    }

    [Fact]
    [Trait("Id", "PCM-P4-2a")]
    public void Run_SwimmingAndMountedCarriers_TakeThePlainHorizontalMove_WithNoSubSteps()
    {
        var swim = new Rig(WorldTablesFixture.Dry(), 64_000, 64_000, 777, LocoState.Swimming);
        var mounted = new Rig(WorldTablesFixture.Dry(), 64_000, 64_000, 777, LocoState.Mounted);

        swim.Run(900, -500);
        mounted.Run(-5000, 40);

        Assert.Equal((64_900, 63_500, 777, 0), (swim.State.X, swim.State.Z, swim.State.Y, swim.Scratch.SubStepsMaxThisTick));
        Assert.Equal((59_000, 64_040, 777, 0), (mounted.State.X, mounted.State.Z, mounted.State.Y, mounted.Scratch.SubStepsMaxThisTick));
    }

    // ------------------------------------------------------------------ PCM-P4-1a: slopes

    [Theory]
    [Trait("Id", "PCM-P4-1a")]
    [InlineData(1400, true)]
    [InlineData(1401, false)]
    [InlineData(1404, false)]
    public void Slope_BoundaryVectors_AreDecidedOnExactNumerators(int rise, bool walkable)
    {
        // Arrange: gx = rise x 2000, so 1000^2 x gx^2 <= 700^2 x 2000^4 holds only up to rise 1400.
        WorldTables world = RampWorld(rise);

        // Act
        bool ok = CarrierTerrain.Sample(world, 7000, Row, 700, out int height, out long gx, out long gz);

        // Assert
        Assert.Equal(walkable, ok);
        Assert.Equal((rise * 2000L, 0L), (gx, gz));
        Assert.Equal((rise + 1) / 2, height);   // 7000 is the tile middle; an exact half rounds away from zero
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Walking_StraightUpThe1400Ramp_AscendsToTheTop_FollowingTheTerrainEveryTick()
    {
        // Arrange
        var rig = new Rig(RampWorld(1400), 4000, Row, 0);
        CarrierInput walk = Walk(PlusX);

        // Act + Assert
        for (int t = 0; t < 80; t++)
        {
            rig.Tick(in walk);
            Assert.Equal(LocoState.Grounded, rig.State.Loco);
            Assert.Equal(rig.Terrain, rig.State.Y);
        }

        Assert.True(rig.State.X > 8000);
        Assert.Equal(1400, rig.State.Y);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Sweep_GroundedWalkOverTheRampTile_KeepsYOnTheTerrainAtEverySubStepEnd()
    {
        // Arrange: 300 mm per call is exactly one sub-step each.
        var rig = new Rig(RampWorld(1400), 5000, Row, 0);

        // Act + Assert
        for (int i = 0; i < 20; i++)
        {
            rig.Run(300, 0);
            Assert.Equal(rig.Terrain, rig.State.Y);
            Assert.Equal(LocoState.Grounded, rig.State.Loco);
        }

        // A 900 mm tick is three sub-steps; the end is still on the terrain.
        var rig3 = new Rig(RampWorld(1400), 5200, Row, 0);
        rig3.Run(900, 0);
        Assert.Equal((3, 6100, 1400 * 100 / 2000), (rig3.Scratch.SubStepsMaxThisTick, rig3.State.X, rig3.State.Y));
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Walking_OnFxRamp1400_YEqualsTheFloorEveryTick_NotOneTickBehind()
    {
        // FX-Ramp itself (8 x 1 tiles, rising tile at x 6000..8000); Z is pinned to 1000 by the map clamp.
        WorldTables world = WorldTablesFixture.Ramp(1400);
        var rig = new Rig(world, 2000, 1000, 0);
        CarrierInput sprint = Walk(PlusX, sprint: true);

        for (int t = 0; t < 60; t++)
        {
            rig.Tick(in sprint);
            Assert.Equal(WorldHeight.At(world, rig.State.X, rig.State.Z), rig.State.Y);
        }

        Assert.Equal((1400, 1000), (rig.State.Y, rig.State.Z));
    }

    [Theory]
    [Trait("Id", "PCM-P4-1a")]
    [InlineData(1404)]
    public void Walking_StraightUpASteepRamp_NeverRaisesTheFeetAboveWhereTheSteepTerrainBegins(int rise)
    {
        // Arrange
        var rig = new Rig(RampWorld(rise), 4000, Row, 0);
        CarrierInput walk = Walk(PlusX, sprint: true);

        // Act + Assert
        for (int t = 0; t < 80; t++)
        {
            rig.Tick(in walk);
            Assert.Equal(LocoState.Grounded, rig.State.Loco);
            Assert.Equal(0, rig.State.Y);
            Assert.True(rig.State.X < 6000, $"tick {t}: X {rig.State.X} entered the steep tile");
        }

        Assert.True(rig.State.X > 5500);
        Assert.True(rig.Scratch.SlideIterationsMax >= 1);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Walking_DiagonallyIntoASteepRamp_SlidesAlongTheContour()
    {
        // Arrange: 100 mm short of the wall, moving (+200, +100): the +X part would climb the wall, the +Z part follows the contour.
        var rig = new Rig(RampWorld(1404), 5900, Row, 0);

        // Act
        rig.Run(200, 100);

        // Assert
        Assert.Equal((5900, Row + 100, 0, LocoState.Grounded), (rig.State.X, rig.State.Z, rig.State.Y, rig.State.Loco));
        Assert.Equal(1, rig.Scratch.SlideIterationsMax);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Walking_AtADiagonalAngleIntoASteepRamp_MakesProgressAlongTheWall_ThroughTheTickStep()
    {
        var rig = new Rig(RampWorld(1404), 5000, Row, 0);
        CarrierInput walk = Walk(PlusXMinusZ);

        rig.Tick(30, in walk);

        Assert.True(rig.State.X < 6000);
        Assert.True(rig.State.Z > Row + 1000);
        Assert.Equal((0, LocoState.Grounded), (rig.State.Y, rig.State.Loco));
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Walking_IntoAWallWithNoSlideIterations_CancelsTheMove()
    {
        var rig = new Rig(RampWorld(1404), 5900, Row, 0);
        rig.Settings = rig.Settings with { SlideIterations = 0 };

        rig.Run(200, 100);

        Assert.Equal((5900, Row, 0), (rig.State.X, rig.State.Z, rig.Scratch.SlideIterationsMax));
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Standing_OnThe1404Ramp_SlidesMonotonicallyDownhill_UntilWalkableTerrain()
    {
        // Arrange: feet on the steep tile, no input.
        WorldTables world = RampWorld(1404);
        var rig = new Rig(world, 7000, Row, 702);
        CarrierInput none = Idle();
        int previous = rig.Terrain;
        int ticks = 0;
        bool leftGround = false;

        // Act
        while (ticks < 200 && !(ticks > 0 && rig.State.Loco == LocoState.Grounded))
        {
            rig.Tick(in none);
            ticks++;
            int now = rig.Terrain;
            Assert.True(now <= previous, $"tick {ticks}: terrain rose from {previous} to {now}");
            Assert.True(rig.State.Y >= now);
            leftGround |= rig.State.Loco == LocoState.Airborne;
            previous = now;
        }

        // Assert: it slid off the steep tile onto the flat ground at 0 and landed there.
        Assert.True(leftGround);
        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.True(rig.State.X < 6000);
        Assert.Equal((0, 0, 0), (rig.State.Y, rig.State.Vy, rig.State.RemY));
        Assert.True(ticks > 20 && ticks < 120, $"slid for {ticks} ticks");
        Assert.Equal(Row, rig.State.Z);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Standing_OnThe1404Ramp_GravityActsAndTheFirstTickStartsCoyote()
    {
        var rig = new Rig(RampWorld(1404), 7000, Row, 702);

        rig.Tick(Idle());

        Assert.Equal(LocoState.Airborne, rig.State.Loco);
        Assert.Equal(CarrierJump.CoyoteStart(rig.Settings), rig.State.CoyoteLeft);
        Assert.Equal(rig.Terrain, rig.State.Y);   // resting on the wall, not falling through it
        Assert.True(rig.State.X < 7000);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Standing_OnThe1400Ramp_StaysPut()
    {
        var rig = new Rig(RampWorld(1400), 7000, Row, 700);

        for (int t = 0; t < 30; t++)
        {
            rig.Tick(Idle());
        }

        Assert.Equal((7000, 700, LocoState.Grounded), (rig.State.X, rig.State.Y, rig.State.Loco));
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Falling_OntoASteepRamp_SlidesDownAndLandsOnTheFlatBelow_NeverTunnelling()
    {
        // Arrange: 5 m above the steep tile.
        WorldTables world = RampWorld(1404);
        var rig = new Rig(world, 7000, Row, 702 + 5000, LocoState.Airborne);
        int ticks = 0;

        // Act
        while (ticks < 400 && rig.State.Loco == LocoState.Airborne)
        {
            rig.Tick(Idle());
            ticks++;
            Assert.True(rig.State.Y >= rig.Terrain);
        }

        // Assert
        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.True(rig.State.X < 6000);
        Assert.Equal(0, rig.State.Y);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Falling_FromAboveASteepRampAt4Substeps_StaysOnTheSurface()
    {
        // 667 mm per tick (terminal speed) is three sub-steps; the fall is spent on the first contact.
        var rig = new Rig(RampWorld(1404), 7000, Row, 702 + 20_000, LocoState.Airborne);
        rig.State.Vy = -20_000;

        for (int t = 0; t < 60 && rig.State.Loco == LocoState.Airborne; t++)
        {
            rig.Tick(Idle());
            Assert.True(rig.State.Y >= rig.Terrain);
            Assert.InRange(rig.Scratch.SubStepsMaxThisTick, 1, 4);
        }

        Assert.True(rig.Scratch.SubStepsMaxThisTick >= 3);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Falling_WithAPressArmed_FiresTheBufferedJumpWhenTheWallSlideLands()
    {
        // The press is armed while sliding down the wall; the slide ends on walkable ground and the buffered press launches on that tick.
        var rig = new Rig(RampWorld(1404), 6030, Row, 0 + 42, LocoState.Airborne);
        bool launched = false;
        for (int t = 0; t < 40 && !launched; t++)
        {
            rig.Tick(new CarrierInput { SpeedPermille = 1000, JumpPressed = t == 0 });
            launched = rig.State.Vy > 0;
        }

        Assert.True(launched);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Walking_DownTheWalkableRamp_StaysGrounded_AtWalkAndSprintSpeeds()
    {
        foreach (bool sprint in new[] { false, true })
        {
            var rig = new Rig(RampWorld(1400), 10_000, Row, 1400);
            CarrierInput walk = Walk(MinusX, sprint);
            for (int t = 0; t < 60; t++)
            {
                rig.Tick(in walk);
                Assert.Equal(LocoState.Grounded, rig.State.Loco);
                Assert.Equal(rig.Terrain, rig.State.Y);
            }

            Assert.Equal(0, rig.State.Y);
        }
    }

    // ------------------------------------------------------------------ Terrain follow, snap-down, ground loss

    [Fact]
    [Trait("Id", "PCM-P4-1c")]
    public void Snap_ADrop_OfExactlyStepUp_StaysGrounded_OneMillimetreMoreGoesAirborne()
    {
        // Arrange: the same sub-step, feet 400 and 401 mm above the terrain.
        var at = new Rig(WorldTablesFixture.Dry(), 64_000, 64_000, WorldTablesFixture.DryHeightMm + 400);
        var over = new Rig(WorldTablesFixture.Dry(), 64_000, 64_000, WorldTablesFixture.DryHeightMm + 401);

        // Act
        at.Run(0, 0);
        over.Run(0, 0);

        // Assert
        Assert.Equal((LocoState.Grounded, WorldTablesFixture.DryHeightMm), (at.State.Loco, at.State.Y));
        Assert.Equal(LocoState.Airborne, over.State.Loco);
        Assert.Equal(WorldTablesFixture.DryHeightMm + 401, over.State.Y);
        Assert.Equal(CarrierJump.CoyoteStart(over.Settings), over.State.CoyoteLeft);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1c")]
    public void Snap_UsesTheStepUpSetting_NotALiteral()
    {
        var rig = new Rig(WorldTablesFixture.Dry(), 64_000, 64_000, WorldTablesFixture.DryHeightMm + 250);
        rig.Settings = rig.Settings with { StepUpMm = 249 };

        rig.Run(0, 0);

        Assert.Equal(LocoState.Airborne, rig.State.Loco);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1c")]
    public void Walking_OffAHighPlateauOntoASteepFace_LosesTheGroundWithCoyote_ThenLandsBelow()
    {
        // Arrange: a 3000 mm drop over one tile (1500 per mille): the plateau is west, the low ground east. Story 009: lifted by
        // 3000 mm so the low ground is dry (at -3000 it is deep water and the carrier would swim).
        WorldTables world = Columns(10, 8, (cx, _) => cx > RampTileX ? 0 : 3000);
        var rig = new Rig(world, 4000, Row, 3000);
        CarrierInput walk = Walk(PlusX);
        int firstAirborne = -1;

        // Act
        for (int t = 0; t < 200 && !(firstAirborne >= 0 && rig.State.Loco == LocoState.Grounded); t++)
        {
            LocoState before = rig.State.Loco;
            rig.Tick(in walk);
            Assert.True(rig.State.Y >= rig.Terrain);
            if (before == LocoState.Grounded && rig.State.Loco == LocoState.Airborne && firstAirborne < 0)
            {
                firstAirborne = t;
                Assert.Equal(CarrierJump.CoyoteStart(rig.Settings), rig.State.CoyoteLeft);
            }
        }

        // Assert
        Assert.True(firstAirborne >= 0);
        Assert.Equal((LocoState.Grounded, 0), (rig.State.Loco, rig.State.Y));
        Assert.True(rig.State.X > 8000);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1c")]
    public void Walking_UpAnUnwalkableStep_IsBlockedEvenWhenTheRiseIsWalkableInSlope()
    {
        // A walkable slope (500 per mille) whose rise in one sub-step cannot exceed stepUp: with stepUp 100 a 300 mm sub-step up it is a wall.
        var rig = new Rig(RampWorld(1000), 5900, Row, 0);
        rig.Settings = rig.Settings with { StepUpMm = 100 };

        rig.Run(300, 0);

        // Destination x 6200 is 100 mm up the ramp (rise 100 is allowed), so it passes; one more sub-step of 300 rises 150 and is blocked.
        Assert.Equal((6200, 100), (rig.State.X, rig.State.Y));
        rig.Run(300, 0);
        Assert.Equal((6200, 100), (rig.State.X, rig.State.Y));
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Feet_PointRule_TheCapsuleRadiusPlaysNoPartAgainstTerrain()
    {
        // Arrange: 250 mm from the steep tile, inside the 300 mm capsule radius.
        var rig = new Rig(RampWorld(1404), 5750, Row, 0);
        Assert.True(rig.Settings.CapsuleRadiusMm >= 300);

        // Act
        rig.Tick(30, Idle());
        rig.Run(0, 250);
        rig.Run(0, -250);

        // Assert: not pushed, not blocked, not sliding.
        Assert.Equal((5750, Row, 0, LocoState.Grounded), (rig.State.X, rig.State.Z, rig.State.Y, rig.State.Loco));
        Assert.Equal(0, rig.Scratch.SlideIterationsMax);
    }

    // ------------------------------------------------------------------ Projection and slide maths

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void ProjectOntoContour_RemovesTheUphillComponent()
    {
        int mx = 250;
        int mz = 90;

        bool left = CarrierTerrain.ProjectOntoContour(ref mx, ref mz, 2_808_000, 0);

        Assert.True(left);
        Assert.Equal((0, 90), (mx, mz));
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void ProjectOntoContour_DiagonalGradient_LeavesAMoveAlongTheContour()
    {
        int mx = 300;
        int mz = 0;

        bool left = CarrierTerrain.ProjectOntoContour(ref mx, ref mz, 2_000_000, 2_000_000);

        Assert.True(left);
        Assert.Equal((150, -150), (mx, mz));
    }

    [Theory]
    [Trait("Id", "PCM-P4-1a")]
    [InlineData(-200, 0, 2_808_000, 0)]      // moving downhill: nothing to project
    [InlineData(100, 100, 0, 0)]              // no gradient
    [InlineData(200, 0, 2_808_000, 0)]        // pure uphill: nothing left
    [InlineData(0, 0, 2_808_000, 0)]          // no move
    public void ProjectOntoContour_ReportsNothingLeft(int mx, int mz, long gx, long gz)
    {
        Assert.False(CarrierTerrain.ProjectOntoContour(ref mx, ref mz, gx, gz));
    }

    [Theory]
    [Trait("Id", "PCM-P4-1a")]
    [InlineData(2_808_000, 0, 14, -20, 0)]
    [InlineData(-2_808_000, 0, 14, 20, 0)]
    [InlineData(0, 3_000_000, 300, 0, -400)]
    [InlineData(2_000_000, 2_000_000, 100, -100, -100)]
    [InlineData(2_808_000, 0, 0, 0, 0)]
    [InlineData(0, 0, 14, 0, 0)]
    public void DownhillRun_IsDepthOverSlope_AlongTheFallLine(long gx, long gz, int depth, int expectedX, int expectedZ)
    {
        CarrierTerrain.DownhillRun(gx, gz, depth, out int rx, out int rz);

        Assert.Equal((expectedX, expectedZ), (rx, rz));
    }

    // ------------------------------------------------------------------ PCM-P4-2b: the map edge

    [Theory]
    [Trait("Id", "PCM-P4-2b")]
    [InlineData(500, -3000, 1000, 0)]
    [InlineData(127_500, 3000, 127_000, 0)]
    [InlineData(500, 3000, 1000, 3000)]       // inward component is kept
    [InlineData(127_500, -3000, 127_000, -3000)]
    [InlineData(500, 0, 1000, 0)]
    [InlineData(64_000, 5000, 64_000, 5000)]  // inside: untouched
    public void Clamp_PutsTheFeet1000MmInside_AndZeroesOnlyTheOutwardVelocity(int x, int vx, int expectedX, int expectedVx)
    {
        // Arrange
        var state = new CarrierState { X = x, Z = 64_000, Y = 1000, Vx = vx, Vz = 777, RemX = 11, RemZ = 13 };

        // Act
        bool moved = CarrierBounds.Clamp(ref state, WorldTablesFixture.Dry());

        // Assert: Z is tangential and untouched on both counts.
        Assert.Equal(expectedX != x, moved);
        Assert.Equal((expectedX, expectedVx, 64_000, 777, 13), (state.X, state.Vx, state.Z, state.Vz, state.RemZ));
        Assert.Equal(expectedX != x ? 0 : 11, state.RemX);
    }

    [Fact]
    [Trait("Id", "PCM-P4-2b")]
    public void Clamp_ACorner_ClampsBothAxes_AndZeroesBothOutwardComponents()
    {
        var state = new CarrierState { X = -5000, Z = 200_000, Vx = -100, Vz = 900, Y = 1000 };

        Assert.True(CarrierBounds.Clamp(ref state, WorldTablesFixture.Dry()));

        Assert.Equal((1000, 127_000, 0, 0), (state.X, state.Z, state.Vx, state.Vz));
    }

    [Fact]
    [Trait("Id", "PCM-P4-2b")]
    public void Walking_OutwardAtTheWestEdge_RestsAt1000_WithOutwardVelocityZero_EveryTick()
    {
        var rig = new Rig(WorldTablesFixture.Dry(), 1600, 64_000, 1000);
        CarrierInput walk = Walk(MinusX, sprint: true);
        var east = new Rig(WorldTablesFixture.Dry(), 126_000, 64_000, 1000);
        CarrierInput walkEast = Walk(PlusX, sprint: true);

        for (int t = 0; t < 60; t++)
        {
            rig.Tick(in walk);
            Assert.True(rig.State.X >= 1000);
            east.Tick(in walkEast);
            Assert.True(east.State.X <= 127_000, $"east tick {t}: X {east.State.X}");
        }

        Assert.Equal((1000, 0, 64_000), (rig.State.X, rig.State.Vx, rig.State.Z));
        Assert.Equal((127_000, 0), (east.State.X, east.State.Vx));   // and the east edge, 1000 mm inside
    }

    [Fact]
    [Trait("Id", "PCM-P4-2b")]
    public void Walking_DiagonallyIntoTheWestEdge_KeepsSlidingAlongIt()
    {
        // Arrange: forward = (-1, +1): the -X part is held at the edge, the +Z part keeps going.
        var rig = new Rig(WorldTablesFixture.Dry(), 1200, 20_000, 1000);
        CarrierInput walk = Walk(MinusXPlusZ);
        int lastZ = rig.State.Z;

        // Act + Assert
        for (int t = 0; t < 60; t++)
        {
            rig.Tick(in walk);
            Assert.True(rig.State.Z > lastZ);
            lastZ = rig.State.Z;
        }

        Assert.Equal((1000, 0), (rig.State.X, rig.State.Vx));
        Assert.True(rig.State.Vz > 0);
        Assert.True(rig.State.Z > 20_000 + 3000);
    }

    [Fact]
    [Trait("Id", "PCM-P4-2b")]
    public void Walking_IntoTheNorthAndSouthEdges_ClampsZ()
    {
        var north = new Rig(WorldTablesFixture.Dry(), 64_000, 1500, 1000);
        var south = new Rig(WorldTablesFixture.Dry(), 64_000, 126_500, 1000);

        // forward(theta) = (-sin, -cos): yaw 0 is -Z, yaw 32768 is +Z.
        north.Tick(30, new CarrierInput { MoveY = Full, LookYaw = 0, SprintHeld = true, SpeedPermille = 1000 });
        south.Tick(30, new CarrierInput { MoveY = Full, LookYaw = 32768, SprintHeld = true, SpeedPermille = 1000 });

        Assert.Equal((1000, 0), (north.State.Z, north.State.Vz));
        Assert.Equal((127_000, 0), (south.State.Z, south.State.Vz));
    }

    [Fact]
    [Trait("Id", "PCM-P4-2b")]
    public void Height_LookupsAtTheLastTileAndBeyond_DoNotThrow_AndReturnTheEdgeHeight()
    {
        WorldTables world = WorldTablesFixture.Dry();
        int[] edges = { 0, 127_999, 128_000, 128_001, int.MaxValue, int.MinValue };

        foreach (int x in edges)
        {
            foreach (int z in edges)
            {
                Assert.True(CarrierTerrain.Sample(world, x, z, 700, out int h, out long gx, out long gz));
                Assert.Equal((1000, 0L, 0L), (h, gx, gz));
            }
        }

        // And a carrier started outside the map is pulled in, not crashed.
        var rig = new Rig(world, 200_000, -50_000, 1000);
        rig.Tick(Idle());
        Assert.Equal((127_000, 1000), (rig.State.X, rig.State.Z));
    }

    [Fact]
    [Trait("Id", "PCM-P4-2b")]
    public void Clamp_ReGroundsTheFeet_WhenTheEdgeMoveChangesTheTerrainUnderThem()
    {
        // Arrange: 4 x 8 tiles (x 0..8000); the last two columns climb 1000 mm over x 4000..8000, so the clamp at x 7000 changes the height.
        WorldTables world = Columns(4, 8, (cx, _) => cx <= 2 ? 0 : (cx - 2) * 1000);
        var rig = new Rig(world, 6800, Row, WorldHeight.At(world, 6800, Row));
        CarrierInput walk = Walk(PlusX, sprint: true);

        // Act + Assert
        for (int t = 0; t < 20; t++)
        {
            rig.Tick(in walk);
            Assert.Equal(rig.Terrain, rig.State.Y);
            Assert.Equal(LocoState.Grounded, rig.State.Loco);
        }

        Assert.Equal((7000, WorldHeight.At(world, 7000, Row)), (rig.State.X, rig.State.Y));
    }

    // ------------------------------------------------------------------ Fuzz and determinism

    [Fact]
    [Trait("Id", "PCM-PERF-1")]
    public void Fuzz_10000TicksOverSlopesCliffsAndEdges_KeepEveryInvariantAndTheProbeCountersInBounds()
    {
        // Arrange
        WorldTables world = MixedWorld();
        var rng = new Pcg32(0x7007UL, 0x2AUL);
        var rig = new Rig(world, 4000, 8000, 0);
        int slideTicks = 0;
        int multiStepTicks = 0;
        int groundLosses = 0;
        int edgeTicks = 0;
        int landings = 0;
        int steepRests = 0;

        // Act + Assert
        for (int t = 0; t < 10_000; t++)
        {
            if (t % 250 == 0)
            {
                // Re-seed the carrier somewhere on the map, grounded or dropped from a few metres.
                int px = 1000 + (int)(rng.Next32() % 18_000);
                int pz = 1000 + (int)(rng.Next32() % 14_000);
                bool air = (rng.Next32() & 1) == 0;
                rig.State = new CarrierState { X = px, Z = pz, Loco = air ? LocoState.Airborne : LocoState.Grounded };
                rig.State.Y = WorldHeight.At(world, px, pz) + (air ? (int)(rng.Next32() % 3000) : 0);
            }

            uint r = rng.Next32();
            var input = new CarrierInput
            {
                MoveX = (r & 0x30) == 0 ? ((int)((r >> 8) & 0xFFFF) - 32_768) : 0,
                MoveY = (r & 0xC) == 0 ? 0 : Full,
                LookYaw = (ushort)((t / 40 * 7919) ^ (int)(r >> 20)),
                SprintHeld = (r & 0x2) != 0,
                JumpPressed = ((r >> 6) & 15) == 0,
                ExtraJumps = (int)((r >> 10) & 1),
                SpeedPermille = (r & 0x100) != 0 ? 1000 : 600,
            };

            LocoState before = rig.State.Loco;
            rig.Scratch.Clear();
            rig.Tick(in input);

            int floor = rig.Terrain;
            Assert.True(rig.State.Y >= floor, $"tick {t}: Y {rig.State.Y} below terrain {floor}");
            Assert.InRange(rig.State.RemY, 0, 59);
            Assert.InRange(rig.State.RemX, -29, 29);
            Assert.InRange(rig.State.RemZ, -29, 29);
            Assert.InRange(rig.State.X, 1000, 19_000);
            Assert.InRange(rig.State.Z, 1000, 15_000);
            Assert.InRange(rig.Scratch.SubStepsMaxThisTick, 1, 4);
            Assert.InRange(rig.Scratch.SlideIterationsMax, 0, 3);
            Assert.Equal(0, rig.Scratch.StepClamped);
            Assert.True(rig.State.Vy >= -20_000);
            if (rig.State.Loco == LocoState.Grounded)
            {
                Assert.Equal((floor, 0, 0), (rig.State.Y, rig.State.Vy, rig.State.RemY));
                Assert.True(CarrierTerrain.Sample(world, rig.State.X, rig.State.Z, 700, out _, out _, out _), $"tick {t}: grounded on a wall");
                landings += before == LocoState.Airborne ? 1 : 0;
            }
            else if (rig.State.Y == floor)
            {
                steepRests++;
                Assert.False(CarrierTerrain.Sample(world, rig.State.X, rig.State.Z, 700, out _, out _, out _), $"tick {t}: airborne on walkable ground at rest");
            }

            slideTicks += rig.Scratch.SlideIterationsMax > 0 ? 1 : 0;
            multiStepTicks += rig.Scratch.SubStepsMaxThisTick > 1 ? 1 : 0;
            groundLosses += before == LocoState.Grounded && rig.State.Loco == LocoState.Airborne ? 1 : 0;
            edgeTicks += rig.State.X is 1000 or 19_000 || rig.State.Z is 1000 or 15_000 ? 1 : 0;
        }

        // The generator really reaches the code under test.
        Assert.True(slideTicks > 50, $"slides {slideTicks}");
        Assert.True(multiStepTicks > 50, $"multi {multiStepTicks}");
        Assert.True(groundLosses > 20, $"ground losses {groundLosses}");
        Assert.True(edgeTicks > 50, $"edge {edgeTicks}");
        Assert.True(landings > 20, $"landings {landings}");
        Assert.True(steepRests > 5, $"steep rests {steepRests}");
    }

    [Fact]
    [Trait("Id", "PCM-P4-2a")]
    public void Fuzz_ASweepWithRandomDisplacements_NeverBreaksTheSubStepAndSlideCaps()
    {
        WorldTables world = MixedWorld();
        var rng = new Pcg32(0xC0FFEEUL, 9UL);
        var rig = new Rig(world, 8000, 8000, 0);
        int clamped = 0;

        for (int i = 0; i < 10_000; i++)
        {
            if (i % 100 == 0)
            {
                rig.State = new CarrierState { X = 1000 + (int)(rng.Next32() % 18_000), Z = 1000 + (int)(rng.Next32() % 14_000), Loco = LocoState.Airborne };
                rig.State.Y = WorldHeight.At(world, rig.State.X, rig.State.Z) + 200;
            }

            int dx = (int)(rng.Next32() % 3001) - 1500;
            int dz = (int)(rng.Next32() % 3001) - 1500;
            int dy = (int)(rng.Next32() % 1001) - 800;
            rig.Scratch.Clear();
            rig.Run(dx, dz, dy);

            Assert.InRange(rig.Scratch.SubStepsMaxThisTick, 1, 4);
            Assert.InRange(rig.Scratch.SlideIterationsMax, 0, 3);
            Assert.True(rig.State.Y >= rig.Terrain);
            clamped += rig.Scratch.StepClamped;
        }

        Assert.True(clamped > 1000);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Fuzz_AFixedSeed_ReplaysBitIdentically()
    {
        static ulong Run()
        {
            var rng = new Pcg32(99UL, 3UL);
            var rig = new Rig(MixedWorld(), 5000, 8000, 0);
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < 3000; i++)
            {
                uint r = rng.Next32();
                rig.Tick(new CarrierInput
                {
                    MoveY = (r & 1) != 0 ? Full : 0, LookYaw = (ushort)(r >> 16), JumpPressed = ((r >> 2) & 7) == 0,
                    SpeedPermille = 1000, SprintHeld = (r & 8) != 0,
                });
                h = (h ^ (ulong)(uint)rig.State.X) * 1099511628211UL;
                h = (h ^ (ulong)(uint)rig.State.Y) * 1099511628211UL;
                h = (h ^ (ulong)(uint)rig.State.Z) * 1099511628211UL;
                h = (h ^ (ulong)(uint)rig.State.Vy) * 1099511628211UL;
            }

            return h;
        }

        Assert.Equal(Run(), Run());
    }

    // ------------------------------------------------------------------ PCM-PERF-1 probe counters on FX-Ramp

    [Fact]
    [Trait("Id", "PCM-PERF-1")]
    public void WorstCase_FourSubStepsUpTheFxRamp_StaysInsideTheProbeCounters_AndFlushesThem()
    {
        // Arrange: a 1200 mm tick up FX-Ramp(1400) (one tile deep): four sub-steps, Y on the terrain at the end.
        WorldTables world = WorldTablesFixture.Ramp(1400);
        var rig = new Rig(world, 5000, 1000, 0);
        var counters = new PerformativeMail.Sim.Core.SimCounters();

        // Act
        rig.Run(1200, 0);
        CarrierCounterFlush.Flush(counters, ref rig.Scratch);

        // Assert
        Assert.Equal((6200, WorldHeight.At(world, 6200, 1000)), (rig.State.X, rig.State.Y));
        Assert.Equal(4L, counters[PerformativeMail.Sim.Core.CounterId.CarrierSubSteps]);
        Assert.InRange(counters[PerformativeMail.Sim.Core.CounterId.CarrierSlideIterations], 0L, 3L);
        Assert.Equal(0L, counters[PerformativeMail.Sim.Core.CounterId.CarrierStepClamped]);
    }

    [Fact]
    [Trait("Id", "PCM-PERF-1")]
    public void WorstCase_AFourSubStepWallSlide_CountsSlidesWithinTheCap()
    {
        var rig = new Rig(RampWorld(1404), 5800, Row, 0);

        rig.Run(1200, 400);

        Assert.Equal(4, rig.Scratch.SubStepsMaxThisTick);
        Assert.InRange(rig.Scratch.SlideIterationsMax, 1, 3);
        Assert.Equal(0, rig.State.Y);
        Assert.True(rig.State.X < 6000);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void ASweep_OfSprintSpeed_IsASingleSubStep()
    {
        var rig = new Rig(RampWorld(1400), 4000, Row, 0);

        rig.Tick(50, Walk(PlusX, sprint: true));

        Assert.Equal(1, rig.Scratch.SubStepsMaxThisTick);
    }

    // ------------------------------------------------------------------ QA coverage: Z axis, diagonals, slide cap, edges

    /// <summary>10 x 8 tiles; corners with cz &gt; 3 sit at <paramref name="riseMm"/> (a ramp rising towards +Z over z 6000..8000), or, when <paramref name="towardMinusZ"/>, corners with cz &lt;= 3 do (rising towards -Z).</summary>
    private static WorldTables ZRampWorld(int riseMm, bool towardMinusZ = false) =>
        Columns(10, 8, (_, cz) => (towardMinusZ ? cz <= 3 : cz > 3) ? riseMm : 0);

    private const ushort PlusZ = 32768;   // forward(theta) = (-sin, -cos): yaw 32768 is +Z, yaw 0 is -Z
    private const ushort MinusZ = 0;

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Walking_StraightUpTheZ1400Ramp_BothWays_AscendsWithYOnTheFloorEveryTick()
    {
        // Arrange: +Z ramp walked towards +Z; the mirrored ramp (rising towards -Z) walked towards -Z.
        foreach ((bool towardMinus, int startZ, ushort yaw) in new[] { (false, 5000, PlusZ), (true, 9000, MinusZ) })
        {
            var rig = new Rig(ZRampWorld(1400, towardMinus), 10_000, startZ, 0);
            CarrierInput walk = Walk(yaw);

            // Act + Assert
            for (int t = 0; t < 80; t++)
            {
                rig.Tick(in walk);
                Assert.Equal(LocoState.Grounded, rig.State.Loco);
                Assert.Equal(rig.Terrain, rig.State.Y);
            }

            Assert.Equal(1400, rig.State.Y);
            Assert.True(towardMinus ? rig.State.Z < 6000 : rig.State.Z > 8000);
        }
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Walking_StraightUpTheZ1404Ramp_IsHeldAtTheContour_NeverRaisingTheFeet()
    {
        // Arrange: the steep tile spans z 6000..8000 (+Z ramp) or the mirror of it (-Z ramp, steep below z 8000).
        var up = new Rig(ZRampWorld(1404), 10_000, 5000, 0);
        var down = new Rig(ZRampWorld(1404, towardMinusZ: true), 10_000, 9000, 0);

        // Act + Assert
        for (int t = 0; t < 80; t++)
        {
            up.Tick(Walk(PlusZ, sprint: true));
            down.Tick(Walk(MinusZ, sprint: true));
            Assert.Equal((0, LocoState.Grounded), (up.State.Y, up.State.Loco));
            Assert.Equal((0, LocoState.Grounded), (down.State.Y, down.State.Loco));
            Assert.True(up.State.Z < 6000, $"tick {t}: Z {up.State.Z} entered the steep tile");
            Assert.True(down.State.Z >= 8000, $"tick {t}: Z {down.State.Z} entered the steep tile");
        }

        Assert.True(up.State.Z > 5500);
        Assert.True(down.State.Z < 8500);
    }

    [Theory]
    [Trait("Id", "PCM-P4-1a")]
    [InlineData(989, true)]
    [InlineData(990, false)]
    public void Slope_DiagonalPlane_IsDecidedOnTheCombinedExactNumerators(int risePerAxis, bool walkable)
    {
        // Arrange: a plane rising risePerAxis over 2000 mm in both X and Z; 1000^2 (gx^2 + gz^2) <= 700^2 S^4 holds while 2 r^2 <= 980 000.
        WorldTables world = Columns(10, 8, (cx, cz) => risePerAxis * (Math.Max(0, cx - 3) + Math.Max(0, cz - 3)));

        // Act
        bool ok = CarrierTerrain.Sample(world, 9000, 9000, 700, out _, out long gx, out long gz);

        // Assert: each axis alone is far below 700 per mille; only the combination decides.
        Assert.Equal((risePerAxis * 2000L, risePerAxis * 2000L), (gx, gz));
        Assert.Equal(walkable, ok);
        Assert.Equal(walkable, WorldHeight.IsWalkableSlope(risePerAxis * 2000L, risePerAxis * 2000L, 700));
    }

    private static WorldTables ConcaveCornerWorld() => Columns(10, 8, (cx, cz) => (cx > 3 ? 1404 : 0) + (cz > 3 ? 1404 : 0));

    [Theory]
    [Trait("Id", "PCM-PERF-1")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Slide_AConcaveCornerOfTwoSteepFaces_NeverExceedsTheCap_AndCancelsThePureUphillMove(int cap)
    {
        // Arrange: steep faces east (x > 6000) and south (z > 6000); the corner at (6000, 6000) is concave.
        WorldTables world = ConcaveCornerWorld();
        var rig = new Rig(world, 5900, 5900, 0);
        rig.Settings = rig.Settings with { SlideIterations = cap };

        // Act: straight into the corner; the move is pure uphill so nothing is left after the first projection.
        rig.Run(300, 300);

        // Assert
        Assert.Equal((5900, 5900, 0, LocoState.Grounded), (rig.State.X, rig.State.Z, rig.State.Y, rig.State.Loco));
        Assert.Equal(Math.Min(cap, 1), rig.Scratch.SlideIterationsMax);

        // And over every approach direction the cap holds and the carrier never ends inside a wall.
        int[] starts = { 5000, 5700, 5900, 5990 };
        int[] moves = { -300, -100, 0, 100, 300 };
        foreach (int sx in starts)
        {
            foreach (int sz in starts)
            {
                foreach (int mx in moves)
                {
                    foreach (int mz in moves)
                    {
                        var r = new Rig(world, sx, sz, 0);
                        r.Settings = r.Settings with { SlideIterations = cap };
                        r.Run(mx, mz);
                        Assert.InRange(r.Scratch.SlideIterationsMax, 0, cap);
                        Assert.True(r.State.Y >= r.Terrain, $"start ({sx},{sz}) move ({mx},{mz}): Y {r.State.Y} below terrain");
                    }
                }
            }
        }
    }

    [Fact]
    [Trait("Id", "PCM-P4-1c")]
    public void Walking_MinusXOffASmallDropOntoTheSteepFace_LosesGroundWithCoyote_SteepTerrainIsNotSupport_NoSnap()
    {
        // Arrange: on the flat shelf at 1404 just east of the 1404 mm steep face (x 6000..8000); a 150 mm step west puts the feet ~75 mm above the face.
        var rig = new Rig(RampWorld(1404), 8100, Row, 1404);

        // Act
        rig.Run(-150, 0);

        // Assert: the drop is well inside the 400 mm snap, but a wall is not support, so the ground is lost instead of snapped to.
        int terrain = rig.Terrain;
        Assert.InRange(1404 - terrain, 1, 400);
        Assert.Equal((7950, 1404, LocoState.Airborne), (rig.State.X, rig.State.Y, rig.State.Loco));
        Assert.Equal(CarrierJump.CoyoteStart(rig.Settings), rig.State.CoyoteLeft);
    }

    [Fact]
    [Trait("Id", "PCM-P4-2b")]
    public void Standing_OnASteepTileWhoseDownhillSideIsTheMapEdge_RestsAtTheClamp_WithTerrainNonIncreasing()
    {
        // Arrange: a 702 per mille plane rising towards +X from the west edge (x = 0); downhill is out of the map.
        WorldTables world = Columns(10, 8, (cx, _) => cx * 1404);
        var rig = new Rig(world, 1300, Row, WorldHeight.At(world, 1300, Row));
        int previous = rig.Terrain;

        // Act + Assert
        for (int t = 0; t < 120; t++)
        {
            rig.Tick(Idle());
            int now = rig.Terrain;
            Assert.True(now <= previous, $"tick {t}: terrain rose from {previous} to {now}");
            Assert.True(rig.State.Y >= now);
            Assert.True(rig.State.X >= 1000);
            Assert.InRange(rig.Scratch.SlideIterationsMax, 0, 3);
            previous = now;
        }

        Assert.Equal((1000, Row), (rig.State.X, rig.State.Z));
        Assert.True(rig.State.Vx >= 0);
        Assert.Equal(rig.Terrain, rig.State.Y);
    }

    [Fact]
    [Trait("Id", "PCM-P4-2b")]
    public void Height_LookupsOnASlopedWorldAtTheIntegerExtremes_ClampToTheEdgeHeights_AndDoNotThrow()
    {
        // Arrange: x 0..20000 rises 0 to 1404 over tile 3; z is constant.
        WorldTables world = RampWorld(1404);
        int[] extremes = { int.MinValue, int.MaxValue };

        foreach (int x in extremes)
        {
            foreach (int z in extremes)
            {
                // Act
                bool ok = CarrierTerrain.Sample(world, x, z, 700, out int h, out long gx, out long gz);
                bool edgeOk = CarrierTerrain.Sample(world, x == int.MinValue ? 0 : 20_000, z == int.MinValue ? 0 : 16_000, 700, out int eh, out long egx, out long egz);

                // Assert: the clamped lookup equals the lookup on the map edge itself.
                Assert.Equal((x == int.MinValue ? 0 : 1404), h);
                Assert.Equal((edgeOk, eh, egx, egz), (ok, h, gx, gz));
            }
        }
    }

    [Fact]
    [Trait("Id", "PCM-P4-1a")]
    public void Airborne_100MmAboveTheRampFloor_MovingIntoRisingWalkableTerrain_LandsAndIsNotBlocked()
    {
        // Arrange: x 6500 has floor 350; 300 mm on, the floor is 560, above the feet at 450.
        var rig = new Rig(RampWorld(1400), 6500, Row, 450, LocoState.Airborne);

        // Act
        rig.Run(300, 0);

        // Assert
        Assert.Equal((6800, 560, LocoState.Grounded), (rig.State.X, rig.State.Y, rig.State.Loco));
        Assert.Equal(0, rig.Scratch.SlideIterationsMax);
    }
}

/// <summary>Zero-allocation guardrail for the terrain sweep (story 007), in the non-parallel collection; skipped on unoptimised Sim builds (TD-008).</summary>
[Collection(NonParallelCollection.Name)]
public class SweepTerrainAllocationTests
{
    [Fact]
    [Trait("Id", "PCM-PERF-1")]
    public void Step_10000SweepingCallsOverSlopesAndCliffs_AllocateNothing()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(OwnedCarrier).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        int[] columns = { 0, 0, 0, 0, 1300, 1300, 1300, 4300, 4300, 4300, 4300 };
        var corners = new int[11 * 9];
        for (int cz = 0; cz <= 8; cz++)
        {
            for (int cx = 0; cx <= 10; cx++)
            {
                corners[(cz * 11) + cx] = columns[cx] + ((cz & 1) * 100);
            }
        }

        var builder = new WorldTablesBuilder();
        builder.SetCorners(10, 8, corners);
        WorldTables world = builder.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
        PlayerSettings settings = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
        var remotes = new EmptyRemoteCapsuleSource();
        var supports = new EmptySupportTransformSource();
        long sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("10,000 sweeping OwnedCarrier.Step calls", () =>
        {
            var state = new CarrierState { X = 4000, Z = 8000, Y = 0 };
            var scratch = new CarrierScratch(64);
            CarrierEnv env = CarrierEnv.ForM1a(world, settings, new EntityId(5), remotes, supports);
            for (int i = 0; i < 10_000; i++)
            {
                var input = new CarrierInput
                {
                    MoveX = (i * 977 % 65_535) - 32_767,
                    MoveY = (i & 7) == 0 ? 0 : 32_767,
                    LookYaw = (ushort)((i / 64) * 5237),
                    SprintHeld = (i & 32) != 0,
                    JumpPressed = i % 23 == 0,
                    SpeedPermille = 1000,
                };
                OwnedCarrier.Step(ref state, in input, in env, ref scratch);
                sink += state.X + state.Y + state.Z;
            }
        });

        Assert.NotEqual(long.MinValue, sink);
    }
}
