using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 009 part A: water (GDD P6, F7; ADR-0013 section 5; ADR-0026 section 3; TR-movement-018): F7 depth and wading,
/// the 900 / 700 mm swim hysteresis, surface-only swim motion, entry from the air, remainder resets, next-tick application and haul-out.
/// FX-Beach is one 2000 mm tile, so the map clamp (1000 mm inset) pins any stepped carrier to its centre; stepped beach tests use
/// <see cref="WideBeach"/>, a 3 x 3 tile strip whose middle row repeats FX-Beach's gradient (beach fz maps to world z = 2000 + fz).
/// </summary>
public class WaterTests
{
    private const ushort PlusX = 49152;       // forward = (+1, 0)
    private const ushort Diagonal = 40960;    // forward = (+1, +1)
    private const ushort PlusZ = 32768;       // forward = (0, +1)
    private const ushort AwayDiagonal = 24576; // forward = (-1, +1)
    private const ushort MinusZ = 0;          // forward = (0, -1)
    private const int Full = 32767;
    private const int BeachX = 2500;          // beach fx 500 on WideBeach
    private const int BeachZ0 = 2000;         // WideBeach z of beach fz 0

    // ---------------------------------------------------------------- fixtures

    private static WorldTables WideBeach()
    {
        int[] rows = { -1800, -1000, -200, 600 };
        var corners = new int[16];
        for (int cz = 0; cz < 4; cz++)
        {
            for (int cx = 0; cx < 4; cx++)
            {
                corners[(cz * 4) + cx] = rows[cz];
            }
        }

        var b = new WorldTablesBuilder();
        b.SetCorners(3, 3, corners);
        return b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    /// <summary>A 700 permille beach falling along +X (1400 mm per tile), 8 x 3 tiles, starting at height 0.</summary>
    private static WorldTables SlopeBeach()
    {
        const int Tx = 8;
        const int Tz = 3;
        var corners = new int[(Tx + 1) * (Tz + 1)];
        for (int cz = 0; cz <= Tz; cz++)
        {
            for (int cx = 0; cx <= Tx; cx++)
            {
                corners[(cz * (Tx + 1)) + cx] = -1400 * cx;
            }
        }

        var b = new WorldTablesBuilder();
        b.SetCorners(Tx, Tz, corners);
        return b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    /// <summary>FX-Deep (64 x 64 tiles at -2000) with static boxes.</summary>
    private static WorldTables DeepWith(params AabbMm[] boxes)
    {
        const int Tiles = WorldTablesFixture.FlatTiles;
        var corners = new int[(Tiles + 1) * (Tiles + 1)];
        Array.Fill(corners, WorldTablesFixture.DeepHeightMm);
        var b = new WorldTablesBuilder();
        b.SetCorners(Tiles, Tiles, corners);
        foreach (AabbMm box in boxes)
        {
            b.AddSolid(EntityId.None, box, SolidKind.Prop);
        }

        return b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    private static AabbMm Box(int x0, int y0, int z0, int x1, int y1, int z1) => new(new Vec3Mm(x0, y0, z0), new Vec3Mm(x1, y1, z1));

    /// <summary>A ledge face at x 64 000 rising from the -2000 floor to <paramref name="topMm"/>, z 62 000 to 66 000.</summary>
    private static WorldTables Ledge(int topMm) => DeepWith(Box(64_000, -2000, 62_000, 66_000, topMm, 66_000));

    private static CarrierInput Move(ushort yaw, int weight = 1000, bool sprint = false, int add = 0, bool jump = false, int extra = 0) =>
        new() { MoveY = Full, LookYaw = yaw, SpeedPermille = weight, SprintHeld = sprint, BuffAddPm = add, JumpPressed = jump, ExtraJumps = extra };

    private static CarrierInput Idle(bool jump = false, int extra = 0) => new() { LookYaw = PlusX, SpeedPermille = 1000, JumpPressed = jump, ExtraJumps = extra };

    private static int TerrainDepth(WorldTables w, int x, int z) => (int)Math.Max(0, -w.TerrainHeightMm(x, z));

    private sealed class Rig
    {
        private readonly EmptyRemoteCapsuleSource _remotes = new();
        private readonly EmptySupportTransformSource _supports = new();

        public Rig(WorldTables world, CarrierState state)
        {
            World = world;
            State = state;
        }

        public WorldTables World { get; }

        public CarrierState State;

        public CarrierScratch Scratch = new(64);

        public PlayerSettings Settings { get; } = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);

        public CarrierEnv Env => CarrierEnv.ForM1a(World, Settings, new EntityId(5), _remotes, _supports);

        public void Tick(in CarrierInput input)
        {
            CarrierEnv env = Env;
            OwnedCarrier.Step(ref State, in input, in env, ref Scratch);
        }

        public void Tick(int n, in CarrierInput input)
        {
            for (int i = 0; i < n; i++)
            {
                Tick(in input);
            }
        }

        /// <summary>The depth the solver measures under a feet point (box tops gathered around it).</summary>
        public int Depth(int x, int y, int z)
        {
            CarrierEnv env = Env;
            var scratch = new CarrierScratch(64);
            var probe = new CarrierState { X = x, Y = y, Z = z };
            CarrierColliders.Gather(ref scratch, in env, in probe);
            return CarrierWater.DepthAt(in env, in scratch, x, y, z, out _, out _);
        }
    }

    private static Rig Grounded(WorldTables w, int x, int z, int vx = 0, int vz = 0) =>
        new(w, new CarrierState { X = x, Z = z, Y = (int)w.TerrainHeightMm(x, z), Vx = vx, Vz = vz, Loco = LocoState.Grounded });

    private static Rig Swimmer(WorldTables w, int x, int z, int vx = 0, int vz = 0) =>
        new(w, new CarrierState { X = x, Z = z, Y = 0, Vx = vx, Vz = vz, Loco = LocoState.Swimming });

    // ---------------------------------------------------------------- PCM-F7-1

    [Theory]
    [Trait("Id", "PCM-F7-1")]
    [InlineData(1000, -600, 600, 867, 4335)]
    [InlineData(1375, -450, 450, 900, 4500)]
    public void F7_FxBeach_HeightDepthWadeAndWalkSpeed(int fz, int height, int depth, int wadePm, int walk)
    {
        // Arrange
        var rig = new Rig(WorldTablesFixture.Beach(), default);
        SimCounterSink sink = default;

        // Act
        int measured = rig.Depth(500, height, fz);
        int wade = CarrierWater.WadePm(false, measured, rig.Settings, ref sink);

        // Assert
        Assert.Equal(height, rig.World.TerrainHeightMm(500, fz));
        Assert.Equal(depth, rig.World.WaterDepthMm(500, fz));
        Assert.Equal(depth, measured);
        Assert.Equal(wadePm, wade);
        Assert.Equal(walk, CarrierSpeed.Final(rig.Settings.WalkMmPerSec, 1000, 0, rig.Settings.BuffCapPm, wade, ref sink));
        Assert.Equal(1000, CarrierWater.WadePm(true, measured, rig.Settings, ref sink));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void F7_FxDry_Wade1000_AndSwimmingWade1000()
    {
        var rig = Grounded(WorldTablesFixture.Dry(), 64_000, 64_000);
        rig.Tick(3, Idle());
        Assert.Equal((LocoState.Grounded, (ushort)0, 1000), (rig.State.Loco, rig.State.WadeSlowPm, CarrierWater.StoredWadePm(in rig.State)));

        var swim = Swimmer(WorldTablesFixture.Deep(), 64_000, 64_000);
        swim.Tick(3, Idle());
        Assert.Equal((LocoState.Swimming, 1000, CarrierFlags.None), (swim.State.Loco, CarrierWater.StoredWadePm(in swim.State), swim.State.Flags & CarrierFlags.Wading));
    }

    [Theory]
    [Trait("Id", "PCM-F7-1")]
    [InlineData(1000, 4335)]
    [InlineData(1375, 4500)]
    public void F7_SteppedWalkThroughShallows_ReachesTheWadedSpeed(int fz, int walk)
    {
        // Arrange: one idle tick stores the wade (it applies from the next tick); then walk along constant depth (+X).
        var rig = Grounded(WideBeach(), 1500, BeachZ0 + fz);
        rig.Tick(Idle());
        Assert.NotEqual(CarrierFlags.None, rig.State.Flags & CarrierFlags.Wading);

        // Act
        rig.Tick(8, Move(PlusX));

        // Assert
        Assert.Equal((LocoState.Grounded, walk, 0), (rig.State.Loco, rig.State.Vx, rig.State.Vz));
    }

    [Theory]
    [Trait("Id", "PCM-F7-1")]
    [InlineData(252, 899, false, LocoState.Grounded)]
    [InlineData(250, 900, false, LocoState.Swimming)]
    [InlineData(747, 701, true, LocoState.Swimming)]
    [InlineData(750, 700, true, LocoState.Grounded)]
    public void F7_Hysteresis_EnterAt900_ExitAt700(int fz, int depth, bool startSwimming, LocoState expected)
    {
        // Arrange
        WorldTables w = WideBeach();
        int z = BeachZ0 + fz;
        Rig rig = startSwimming ? Swimmer(w, BeachX, z) : Grounded(w, BeachX, z);
        Assert.Equal(depth, rig.Depth(BeachX, startSwimming ? 0 : -depth, z));
        Assert.Equal(expected == LocoState.Swimming, CarrierWater.NextSwimming(startSwimming, depth, rig.Settings));

        // Act
        rig.Tick(Idle());

        // Assert: swimmers float on the plane; an exit puts the feet on the terrain.
        Assert.Equal(expected, rig.State.Loco);
        Assert.Equal(expected == LocoState.Swimming ? 0 : -depth, rig.State.Y);
    }

    // ---------------------------------------------------------------- PCM-P6-1

    [Fact]
    [Trait("Id", "PCM-P6-1")]
    public void P6_BoxTopAboveDeepWater_StaysGrounded_NoWadeSlow()
    {
        // Arrange
        var rig = new Rig(DeepWith(Box(62_000, -2000, 62_000, 66_000, 500, 66_000)), new CarrierState { X = 64_000, Y = 500, Z = 64_000 });

        // Act
        rig.Tick(30, Idle());
        rig.Tick(10, Move(PlusX));

        // Assert
        Assert.Equal((LocoState.Grounded, 500, (ushort)0, 1000), (rig.State.Loco, rig.State.Y, rig.State.WadeSlowPm, CarrierWater.StoredWadePm(in rig.State)));
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.Wading);
        Assert.Equal(0, rig.Depth(64_000, 500, 64_000));
    }

    [Fact]
    [Trait("Id", "PCM-P6-1")]
    public void P6_3600TicksOfSwimming_OnFxDeep_StayWellFormed()
    {
        // Arrange
        var rig = Swimmer(WorldTablesFixture.Deep(), 64_000, 64_000);

        // Act + Assert
        for (int t = 0; t < 3600; t++)
        {
            CarrierInput input = Move((ushort)(t * 97), sprint: (t & 1) == 0, jump: t % 7 == 0, extra: 1) with { MoveY = t % 50 < 40 ? Full : 0 };
            rig.Tick(in input);
            Assert.Equal(LocoState.Swimming, rig.State.Loco);
            Assert.Equal((0, 0, 0, 0), (rig.State.Y, rig.State.Vy, rig.State.RemY, (int)rig.State.JumpBufferLeft));
            Assert.InRange(rig.State.RemX, -29, 29);
            Assert.InRange(rig.State.RemZ, -29, 29);
            Assert.InRange(CarrierAccel.Length(rig.State.Vx, rig.State.Vz), 0, 2000);
        }
    }

    // ---------------------------------------------------------------- swim motion

    [Fact]
    [Trait("Id", "PCM-P3-1")]
    public void Swim_SteadySpeed2000_SprintAndJumpRefused()
    {
        // Arrange
        var rig = Swimmer(WorldTablesFixture.Deep(), 64_000, 64_000);

        // Act
        rig.Tick(12, Move(PlusX, sprint: true, jump: true, extra: 1));

        // Assert
        Assert.Equal((2000, 0, LocoState.Swimming), (rig.State.Vx, rig.State.Vz, rig.State.Loco));
        Assert.Equal((0, 0, 0, 0), (rig.State.Y, rig.State.Vy, rig.State.RemY, (int)rig.State.JumpBufferLeft));
        Assert.Equal(CarrierFlags.None, rig.State.Flags & (CarrierFlags.Sprinting | CarrierFlags.ExtraJumpUsed));
    }

    [Fact]
    [Trait("Id", "PCM-P5-3")]
    public void Swim_JumpPress_IsRefused_NoVy_NoBufferArm()
    {
        var rig = Swimmer(WorldTablesFixture.Deep(), 64_000, 64_000);
        rig.State.JumpBufferLeft = 2;
        rig.State.Vy = 123;

        rig.Tick(Idle(jump: true, extra: 1));

        Assert.Equal((LocoState.Swimming, 0, 0, 0), (rig.State.Loco, rig.State.Y, rig.State.Vy, (int)rig.State.JumpBufferLeft));
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void Swim_SprinterEntering_CapDropsTo2000_FirstSwimTickDvMax2625()
    {
        // Arrange: a 10 500 mm/s sprinter (7500 x 1.4 with Postal Sprint) on the floor of FX-Deep; the entry tick still uses the
        // sprint cap. The buff ends in the water (input add 0), so the swim cap is the base 2000 of the AC.
        var rig = Grounded(WorldTablesFixture.Deep(), 64_000, 64_000, vx: 10_500);
        rig.State.Flags = CarrierFlags.Sprinting;
        CarrierInput sprint = Move(PlusX, sprint: true);

        // Act: the entry tick.
        rig.Tick(Move(PlusX, sprint: true, add: 400));
        Assert.Equal((LocoState.Swimming, 10_500), (rig.State.Loco, rig.State.Vx));
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.Sprinting);

        // Assert: dvMax 2625 on the first swim tick, then recomputed from |v| (PCM-F2-1 as amended in LocomotionTests).
        var seen = new List<int>();
        for (int i = 0; i < 12 && rig.State.Vx != 2000; i++)
        {
            rig.Tick(sprint);
            seen.Add(rig.State.Vx);
        }

        Assert.Equal(new[] { 7875, 5906, 4429, 3321, 2490, 2000 }, seen);
    }

    // ---------------------------------------------------------------- entry from the air, resets

    [Fact]
    [Trait("Id", "PCM-P6-1")]
    public void AirEntry_ZeroesVyAndRemY_KeepsHorizontal_ResetsCoyoteAndExtraJump()
    {
        // Arrange: falling at -3000 just above deep water, with the extra jump used and coyote time left (no buffered press).
        var rig = new Rig(WorldTablesFixture.Deep(), new CarrierState
        {
            X = 64_000, Z = 64_000, Y = 10, Vx = 5000, Vy = -3000, RemY = 37, RemX = 13, RemZ = -7,
            Loco = LocoState.Airborne, CoyoteLeft = 3, Flags = CarrierFlags.ExtraJumpUsed,
        });

        // Act
        rig.Tick(Idle());

        // Assert: Vx 4166 is the airborne no-input F2 step (5000 - 834); the entry does not touch it.
        Assert.Equal((LocoState.Swimming, 0, 0, 0), (rig.State.Loco, rig.State.Y, rig.State.Vy, rig.State.RemY));
        Assert.Equal((4166, 0, 0), (rig.State.Vx, rig.State.RemX, rig.State.RemZ));
        Assert.Equal((0, 0), ((int)rig.State.CoyoteLeft, (int)rig.State.JumpBufferLeft));
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
    }

    [Fact]
    [Trait("Id", "PCM-P6-1")]
    public void AirEntry_HighAboveDeepWater_KeepsFalling_UntilTheFeetReachThePlane()
    {
        var rig = new Rig(WorldTablesFixture.Deep(), new CarrierState { X = 64_000, Z = 64_000, Y = 3000, Loco = LocoState.Airborne });

        rig.Tick(Idle());
        Assert.Equal(LocoState.Airborne, rig.State.Loco);

        for (int t = 0; t < 60 && rig.State.Loco == LocoState.Airborne; t++)
        {
            rig.Tick(Idle());
        }

        Assert.Equal((LocoState.Swimming, 0, 0), (rig.State.Loco, rig.State.Y, rig.State.Vy));
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Transitions_ResetRemainders_OnEntryAndExit_ExitFeetOnTerrain()
    {
        // Entry: walking -Z (deeper) from depth 899 with odd velocities so the remainders are non-zero.
        WorldTables w = WideBeach();
        var rig = Grounded(w, BeachX, BeachZ0 + 252, vx: 1234, vz: -3011);
        rig.State.RemX = 17;
        rig.State.RemZ = -11;
        rig.Tick(Move(MinusZ));
        Assert.Equal(LocoState.Swimming, rig.State.Loco);
        Assert.Equal((0, 0, 0), (rig.State.RemX, rig.State.RemZ, rig.State.RemY));

        // Exit: swimming +Z (shallower) from depth 701.
        var swim = Swimmer(w, BeachX + 7, BeachZ0 + 747, vx: 1, vz: 2000);
        swim.State.RemX = 5;
        swim.State.RemZ = 9;
        swim.Tick(Move(PlusZ));
        Assert.Equal(LocoState.Grounded, swim.State.Loco);
        Assert.Equal((0, 0, 0), (swim.State.RemX, swim.State.RemZ, swim.State.RemY));
        Assert.Equal(w.TerrainHeightMm(swim.State.X, swim.State.Z), swim.State.Y);
    }

    // ---------------------------------------------------------------- next-tick application, hysteresis in motion

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void Wade_IsEvaluatedAtTheEndOfTheTick_AndAppliedFromTheNext()
    {
        // Arrange: at depth 600 (wade 867) with the previous tick's wade 900 stored, moving +X at 4500.
        var rig = Grounded(WideBeach(), 1500, BeachZ0 + 1000, vx: 4500);
        rig.State.WadeSlowPm = 100;

        // Act + Assert: tick N still uses 900 (4500), stores 867; tick N+1 uses 867 (4335).
        rig.Tick(Move(PlusX));
        Assert.Equal((4500, (ushort)133), (rig.State.Vx, rig.State.WadeSlowPm));
        rig.Tick(Move(PlusX));
        Assert.Equal(4335, rig.State.Vx);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void CrossingThe900Line_TickNUsesTickNMinus1sWade_SwimFromNPlus1()
    {
        // Arrange: depth 899 (wade 801 stored: 200 x 899 / 900 = 199), walking -Z at the waded cap 4005.
        var rig = Grounded(WideBeach(), BeachX, BeachZ0 + 252, vz: -4005);
        rig.State.WadeSlowPm = 199;

        // Act
        rig.Tick(Move(MinusZ));

        // Assert: tick N kept the walking cap; the carrier is swimming at the end of N; N+1 applies the swim cap-drop.
        Assert.Equal((-4005, LocoState.Swimming), (rig.State.Vz, rig.State.Loco));
        Assert.True(TerrainDepth(rig.World, rig.State.X, rig.State.Z) >= 900);
        rig.Tick(Move(MinusZ));
        Assert.Equal(-4005 + 1002, rig.State.Vz);
    }

    [Fact]
    [Trait("Id", "PCM-F7-1")]
    public void WalkingDownA700PmBeach_SwimsWithin2TicksOfDepth900_AndNeverFlickers()
    {
        // Arrange: start at depth 700 on a walkable 700 permille slope, walking +X (deeper).
        WorldTables w = SlopeBeach();
        var rig = Grounded(w, 1000, 3000);
        int firstCross = -1;
        int firstSwim = -1;

        // Act
        for (int t = 0; t < 60; t++)
        {
            rig.Tick(Move(PlusX));
            if (firstCross < 0 && TerrainDepth(w, rig.State.X, rig.State.Z) >= 900)
            {
                firstCross = t;
            }

            if (rig.State.Loco == LocoState.Swimming && firstSwim < 0)
            {
                firstSwim = t;
            }

            // Assert: once swimming, never back to Grounded while it keeps going deeper.
            if (firstSwim >= 0)
            {
                Assert.Equal(LocoState.Swimming, rig.State.Loco);
            }
        }

        Assert.True(firstCross >= 0 && firstSwim >= 0, $"cross {firstCross}, swim {firstSwim}");
        Assert.InRange(firstSwim - firstCross, 0, 2);
    }

    [Theory]
    [Trait("Id", "PCM-F7-1")]
    [InlineData(false)]
    [InlineData(true)]
    public void Hysteresis_DepthOscillatingAround800_NeverChangesState(bool startSwimming)
    {
        // Pure rule: any depth strictly between 700 and 900 keeps the current state.
        PlayerSettings s = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
        foreach (int d in new[] { 800, 899, 701, 899, 701, 750, 850, 800 })
        {
            Assert.Equal(startSwimming, CarrierWater.NextSwimming(startSwimming, d, s));
        }

        // Stepped: a bang-bang walk/swim on WideBeach between depth 760 and 840 (z 2500 .. 2700) never flips the state.
        WorldTables w = WideBeach();
        Rig rig = startSwimming ? Swimmer(w, BeachX, BeachZ0 + 500) : Grounded(w, BeachX, BeachZ0 + 500);
        LocoState expected = rig.State.Loco;
        ushort yaw = MinusZ;
        int min = int.MaxValue;
        int max = int.MinValue;
        for (int t = 0; t < 400; t++)
        {
            int depth = TerrainDepth(w, rig.State.X, rig.State.Z);
            yaw = depth > 840 ? PlusZ : depth < 760 ? MinusZ : yaw;
            rig.Tick(Move(yaw, weight: 300));
            depth = TerrainDepth(w, rig.State.X, rig.State.Z);
            min = Math.Min(min, depth);
            max = Math.Max(max, depth);
            Assert.Equal(expected, rig.State.Loco);
        }

        Assert.InRange(min, 701, 760);
        Assert.InRange(max, 840, 899);
    }

    // ---------------------------------------------------------------- PCM-P6-3 haul-out

    [Theory]
    [Trait("Id", "PCM-P6-3")]
    [InlineData(PlusX)]
    [InlineData(Diagonal)]
    public void HaulOut_1200Ledge_StandsOnTheTopOnTick15_Not14(ushort yaw)
    {
        // Arrange: a swimmer 350 mm from the face (reach r + 100 = 400).
        var rig = Swimmer(Ledge(1200), 63_650, 64_000);

        // Act + Assert
        for (int t = 1; t <= 14; t++)
        {
            rig.Tick(Move(yaw));
            Assert.Equal((LocoState.Swimming, t), (rig.State.Loco, (int)rig.State.HaulOutTicks));
        }

        rig.Tick(Move(yaw));
        Assert.Equal((LocoState.Grounded, 1200, 0), (rig.State.Loco, rig.State.Y, (int)rig.State.HaulOutTicks));
        Assert.InRange(rig.State.X, 64_000, 66_000);
        Assert.Equal((0, 0, 0), (rig.State.RemX, rig.State.RemZ, rig.State.RemY));

        // Standing: the next ticks keep it on the top.
        rig.Tick(3, Idle());
        Assert.Equal((LocoState.Grounded, 1200), (rig.State.Loco, rig.State.Y));
    }

    [Fact]
    [Trait("Id", "PCM-P6-3")]
    public void HaulOut_ReleasingInputAtTick10_ResetsTheCounter()
    {
        var rig = Swimmer(Ledge(1200), 63_650, 64_000);

        rig.Tick(10, Move(PlusX));
        Assert.Equal(10, rig.State.HaulOutTicks);
        rig.Tick(Idle());
        Assert.Equal((LocoState.Swimming, 0), (rig.State.Loco, (int)rig.State.HaulOutTicks));

        rig.Tick(14, Move(PlusX));
        Assert.Equal((LocoState.Swimming, 14), (rig.State.Loco, (int)rig.State.HaulOutTicks));
        rig.Tick(Move(PlusX));
        Assert.Equal((LocoState.Grounded, 1200), (rig.State.Loco, rig.State.Y));
    }

    [Theory]
    [Trait("Id", "PCM-P6-3")]
    [InlineData(1201, PlusX)]          // too high: never lifts
    [InlineData(1200, PlusZ)]          // parallel to the face
    [InlineData(1200, AwayDiagonal)]   // away from the face
    public void HaulOut_NeverLifts_WhenTooHighOrNotPushingIntoTheFace(int top, ushort yaw)
    {
        var rig = Swimmer(Ledge(top), 63_650, 64_000);

        for (int t = 0; t < 60; t++)
        {
            rig.Tick(Move(yaw));
            Assert.Equal((LocoState.Swimming, 0, 0), (rig.State.Loco, (int)rig.State.HaulOutTicks, rig.State.Y));
        }

        Assert.True(rig.State.X < 64_000 - 299, $"The swimmer passed into the ledge (x {rig.State.X}).");
    }

    // ---------------------------------------------------------------- determinism / allocation

    [Fact]
    [Trait("Id", "PCM-P6-1")]
    public void SwimAndHaulOut_AreDeterministic()
    {
        static CarrierState Run()
        {
            var rig = Swimmer(Ledge(1200), 61_000, 64_100);
            for (int t = 0; t < 120; t++)
            {
                rig.Tick(Move(t < 60 ? PlusX : Diagonal));
            }

            return rig.State;
        }

        CarrierState a = Run();
        CarrierState b = Run();
        Assert.Equal((a.X, a.Y, a.Z, a.Vx, a.Vz, a.Loco, a.HaulOutTicks), (b.X, b.Y, b.Z, b.Vx, b.Vz, b.Loco, b.HaulOutTicks));
        Assert.Equal((a.RemX, a.RemZ, a.RemY, a.WadeSlowPm, a.Flags), (b.RemX, b.RemZ, b.RemY, b.WadeSlowPm, b.Flags));
    }
}

/// <summary>Zero-allocation guardrail for the water phase (wading, swimming and haul-out), in the non-parallel collection.</summary>
[Collection(NonParallelCollection.Name)]
public class WaterAllocationTests
{
    [Fact]
    public void WadeSwimAndHaulOut_WarmedUp_AllocateNothing()
    {
        // Arrange: as the TrigQ15 guardrail, the 0 B rule applies to optimised builds only (Debug ReadOnlySpan literal tables allocate).
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(OwnedCarrier).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: RuntimeHelpers.CreateSpan allocates at Tier-0/unoptimised JIT; the 0 B guardrail applies to optimised builds only.");
        var corners = new int[65 * 65];
        Array.Fill(corners, -2000);
        var b = new WorldTablesBuilder();
        b.SetCorners(64, 64, corners);
        b.AddSolid(EntityId.None, new AabbMm(new Vec3Mm(64_000, -2000, 62_000), new Vec3Mm(66_000, 1200, 66_000)), SolidKind.Prop);
        WorldTables world = b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
        PlayerSettings settings = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
        var remotes = new EmptyRemoteCapsuleSource();
        var supports = new EmptySupportTransformSource();
        var scratch = new CarrierScratch(64);
        var input = new CarrierInput { MoveY = 32767, LookYaw = 49152, SpeedPermille = 1000 };
        var state = new CarrierState { X = 61_000, Z = 64_000, Loco = LocoState.Swimming };
        long sum = 0;

        void Run(int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                if (state.Loco != LocoState.Swimming)
                {
                    state = new CarrierState { X = 61_000, Z = 64_000, Loco = LocoState.Swimming };
                }

                CarrierEnv env = CarrierEnv.ForM1a(world, settings, new EntityId(5), remotes, supports);
                OwnedCarrier.Step(ref state, in input, in env, ref scratch);
                sum += state.X + state.HaulOutTicks;
            }
        }

        Run(200);

        // Act + Assert
        _ = AllocAssert.Zero("200 water ticks (swim, push, haul-out, respawn into the water)", () => Run(200));
        Assert.NotEqual(long.MinValue, sum);
    }
}
