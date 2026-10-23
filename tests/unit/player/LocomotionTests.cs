using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 005: F1 final speed, F2 acceleration, camera-relative heading, body yaw and sprint rules
/// (GDD P3, F1, F2, Game Feel "Ramp"; ADR-0013 sections 1-3; TR-movement-001/002/005/020). Flat dry ground, <c>wadePm</c> 1000.
/// </summary>
public class LocomotionTests
{
    // Camera yaw choices: forward(theta) = (-sin theta, -cos theta).
    private const ushort PlusX = 49152;   // forward = (+1, 0)
    private const ushort MinusX = 16384;  // forward = (-1, 0)
    private const int Full = 32767;

    private sealed class Rig
    {
        private readonly WorldTables _world;
        private readonly EmptyRemoteCapsuleSource _remotes = new();
        private readonly EmptySupportTransformSource _supports = new();

        public Rig(WorldTables? world = null)
        {
            _world = world ?? WorldTablesFixture.Dry();
        }

        public CarrierState State = new() { X = 64_000, Z = 64_000, Y = 1000 };

        public CarrierScratch Scratch = new(64);

        public PlayerSettings Settings { get; set; } = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);

        public void Tick(in CarrierInput input)
        {
            CarrierEnv env = CarrierEnv.ForM1a(_world, Settings, new EntityId(5), _remotes, _supports);
            OwnedCarrier.Step(ref State, in input, in env, ref Scratch);
        }

        public void Tick(int n, in CarrierInput input)
        {
            for (int i = 0; i < n; i++)
            {
                Tick(in input);
            }
        }
    }

    private static CarrierInput In(
        int mx = 0, int my = 0, ushort look = 0, bool sprint = false, int weight = 1000, int add = 0, bool aim = false,
        CarrierContextFlags ctx = CarrierContextFlags.None) =>
        new() { MoveX = mx, MoveY = my, LookYaw = look, SprintHeld = sprint, SpeedPermille = weight, BuffAddPm = add, Aiming = aim, ContextFlags = ctx };

    private static CarrierInput ForwardPlusX(bool sprint = false, int weight = 1000, int add = 0) => In(0, Full, PlusX, sprint, weight, add);

    private static int Speed(in CarrierState s) => CarrierAccel.Length(s.Vx, s.Vz);

    private static int Final(int baseMmS, int weight, int add, int wade = 1000, int cap = 400)
    {
        SimCounterSink sink = default;
        return CarrierSpeed.Final(baseMmS, weight, add, cap, wade, ref sink);
    }

    [Theory]
    [Trait("Id", "PCM-F1-1")]
    [InlineData(5000, 1000, 0, 1000, 5000)]
    [InlineData(5000, 600, 0, 1000, 3000)]
    [InlineData(7500, 600, 0, 1000, 4500)]
    [InlineData(2000, 600, 0, 1000, 1200)]
    [InlineData(2000, 1000, 400, 1000, 2800)]
    [InlineData(7500, 1000, 400, 1000, 10_500)]
    [InlineData(5000, 600, 0, 800, 2400)]
    public void F1_Table_MatchesTheGddOutputRange(int baseMmS, int weight, int add, int wade, int expected)
    {
        Assert.Equal(expected, Final(baseMmS, weight, add, wade));
    }

    [Fact]
    [Trait("Id", "PCM-F1-1")]
    public void F1_SprintAtWeight800_LongLegsX2PlusPostal_CapsAt400_Gives8400()
    {
        // Arrange: Long Legs x2 = 240, Postal 300 -> 540, capped to 400.
        int add = CarrierSpeed.BuffAddPm(2, 3, 120, true, 300);

        // Assert
        Assert.Equal(540, add);
        Assert.Equal(1400, CarrierSpeed.BuffPm(add, 400));
        Assert.Equal(8400, Final(7500, 800, add));
    }

    [Fact]
    [Trait("Id", "PCM-F1-2")]
    public void F1_StatClamps_StacksCapAt3_AndNegativeAddPmIsZero()
    {
        // Assert: 5 stacks behave as 3; -50 behaves as 0 and cannot lower the speed below the unbuffed value.
        Assert.Equal(CarrierSpeed.BuffAddPm(3, 3, 120, false, 0), CarrierSpeed.BuffAddPm(5, 3, 120, false, 0));
        Assert.Equal(360, CarrierSpeed.BuffAddPm(5, 3, 120, false, 0));
        Assert.Equal(0, CarrierSpeed.BuffAddPm(-2, 3, 120, false, -300));
        Assert.Equal(1000, CarrierSpeed.BuffPm(-50, 400));
        Assert.Equal(Final(5000, 1000, 0), Final(5000, 1000, -50));
        Assert.Equal(1200, Final(2000, 600, -50));
    }

    [Fact]
    [Trait("Id", "PCM-F1-2")]
    public void F1_ExtremeInputs_CannotOverflow()
    {
        Assert.Equal(CarrierSpeed.MaxAddPm, CarrierSpeed.BuffAddPm(int.MaxValue, int.MaxValue, int.MaxValue, true, int.MaxValue));
        Assert.Equal(1000 + CarrierSpeed.MaxAddPm, CarrierSpeed.BuffPm(int.MaxValue, int.MaxValue));
        Assert.True(Final(7500, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue) > 0);
    }

    [Fact]
    [Trait("Id", "PCM-F1-2")]
    public void F1_LongLegsX1PlusPostal_Equals420_AndTheCapGivesPlus400()
    {
        // Arrange
        int add = CarrierSpeed.BuffAddPm(1, 3, 120, true, 300);

        // Assert
        Assert.Equal(420, add);
        Assert.Equal(Final(5000, 1000, 400), Final(5000, 1000, add));
        Assert.Equal(7000, Final(5000, 1000, add));
    }

    [Fact]
    [Trait("Id", "PCM-F1-2")]
    public void F1_5000MmS_StepsAre166_167_167_AndThirtyTicksTotalExactly5000()
    {
        // Arrange
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Vx = 5000 } };
        int start = rig.State.X;
        var steps = new List<int>();

        // Act
        for (int i = 0; i < 30; i++)
        {
            int before = rig.State.X;
            rig.Tick(ForwardPlusX());
            steps.Add(rig.State.X - before);
        }

        // Assert
        Assert.Equal(new[] { 166, 167, 167 }, steps.Take(3));
        Assert.Equal(5000, rig.State.X - start);
        Assert.Equal(0, rig.State.RemX);
        Assert.Equal(0, rig.State.Z - 64_000);
    }

    [Fact]
    [Trait("Id", "PCM-F1-2")]
    public void F1_SprintAtWeight805AndBuff1400_Is8452_NotTwoDivisions8451()
    {
        // Assert: one division.
        Assert.Equal(8452, Final(7500, 805, 400));
        Assert.Equal(8451, (7500 * 805 / 1000) * 1400 / 1000);

        // The carrier reaches it through Step.
        var rig = new Rig();
        rig.Tick(8, ForwardPlusX(sprint: true, weight: 805, add: 400));
        Assert.Equal(8452, rig.State.Vx);
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void F2_Walk_DvMax1250_FullSpeedOnTick4()
    {
        // Arrange
        var rig = new Rig();
        SimCounterSink sink = default;
        Assert.Equal(1250, CarrierAccel.DvMax(5000, 0, 4, false, 500, ref sink));

        // Act + Assert
        int[] expected = { 1250, 2500, 3750, 5000, 5000 };
        foreach (int e in expected)
        {
            rig.Tick(ForwardPlusX());
            Assert.Equal(e, rig.State.Vx);
            Assert.Equal(0, rig.State.Vz);
        }
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void F2_SprintAtWeight600_DvMax1125_FullSpeedOnTick4()
    {
        // Arrange
        var rig = new Rig();
        SimCounterSink sink = default;
        Assert.Equal(1125, CarrierAccel.DvMax(4500, 0, 4, false, 500, ref sink));

        // Act + Assert
        foreach (int e in new[] { 1125, 2250, 3375, 4500, 4500 })
        {
            rig.Tick(ForwardPlusX(sprint: true, weight: 600));
            Assert.Equal(e, rig.State.Vx);
        }
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void F2_StopFrom5000_Takes3Ticks_AndFirstTickFrom7500IsDvMax2500()
    {
        // Arrange
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Vx = 5000 } };
        SimCounterSink sink = default;
        Assert.Equal(1667, CarrierAccel.DvMax(5000, 5000, 3, false, 500, ref sink));
        Assert.Equal(2500, CarrierAccel.DvMax(5000, 7500, 3, false, 500, ref sink));

        // Act + Assert (5000)
        foreach (int e in new[] { 3333, 1666, 0 })
        {
            rig.Tick(In());
            Assert.Equal(e, rig.State.Vx);
        }

        // Act + Assert (7500): first tick has dvMax 2500; the full 4-tick stop is in F2_StopFrom7500_SprintFlagCleared_Takes4Ticks.
        rig.State.Vx = 7500;
        rig.Tick(In());
        Assert.Equal(5000, rig.State.Vx);
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void F2_StopFrom7500_SprintFlagCleared_Takes4Ticks()
    {
        // PCM-F2-1 as amended: dvMax = ceil(max(vRef, |v|) / T) is recomputed each tick from the current |v| (sprint flag
        // cleared, vRef 5000), so a stop from 7500 takes 4 ticks: 5000, 3333, 1666, 0.
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Vx = 7500 } };
        foreach (int e in new[] { 5000, 3333, 1666, 0 })
        {
            rig.Tick(In());
            Assert.Equal(e, rig.State.Vx);
        }
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void F2_CapDrop_10500To2000_Reaches2000OnTick6()
    {
        // PCM-F2-1 as amended: dvMax is 2625 on the first tick, then recomputed from the current |v| each tick (1969, 1477,
        // 1108, 831, ...), so the carrier reaches 2000 on tick 6: 7875, 5906, 4429, 3321, 2490, 2000.
        var rig = new Rig(WorldTablesFixture.Deep()) { State = new CarrierState { X = 64_000, Z = 64_000, Vx = 10_500, Loco = LocoState.Swimming } };   // story 009: a swimmer must be in water
        SimCounterSink sink = default;
        Assert.Equal(2625, CarrierAccel.DvMax(2000, 10_500, 4, false, 500, ref sink));
        var seen = new List<int>();
        for (int i = 0; i < 12 && rig.State.Vx != 2000; i++)
        {
            rig.Tick(ForwardPlusX());
            seen.Add(rig.State.Vx);
        }

        Assert.Equal(new[] { 7875, 5906, 4429, 3321, 2490, 2000 }, seen);
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void F2_AirborneWithoutInput_Loses834PerTick_ZeroOnTick6()
    {
        // Arrange
        // Story 006: gravity is live, so the fixture starts well above the +1000 mm ground and cannot land during the six ticks.
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = 100_000, Vx = 5000, Loco = LocoState.Airborne } };

        // Act + Assert
        foreach (int e in new[] { 4166, 3332, 2498, 1664, 830, 0 })
        {
            rig.Tick(In());
            Assert.Equal(e, rig.State.Vx);
        }
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void F2_AirborneWithInput_DvMax625()
    {
        // Arrange
        // Story 006: gravity is live, so the fixture starts well above the +1000 mm ground and stays airborne for all 8 ticks
        // (at Y = 0 it landed on tick 1 and ticks 2-8 ran the 1250/tick ground ramp, so the air ramp went unverified).
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = 100_000, Loco = LocoState.Airborne } };
        SimCounterSink sink = default;
        Assert.Equal(625, CarrierAccel.DvMax(5000, 0, 4, true, 500, ref sink));

        // Act + Assert: 625 per tick on every one of the 8 ticks.
        for (int i = 1; i <= 8; i++)
        {
            rig.Tick(ForwardPlusX());
            Assert.Equal(625 * i, rig.State.Vx);
            Assert.Equal(LocoState.Airborne, rig.State.Loco);
        }
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void F2_FullReversalAtWalk_Takes8Ticks()
    {
        // Arrange
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Vx = 5000 } };
        var reverse = In(0, Full, MinusX);

        // Act + Assert
        for (int i = 1; i <= 7; i++)
        {
            rig.Tick(in reverse);
            Assert.Equal(5000 - (1250 * i), rig.State.Vx);
            Assert.NotEqual(-5000, rig.State.Vx);
        }

        rig.Tick(in reverse);
        Assert.Equal(-5000, rig.State.Vx);
        Assert.Equal(0, rig.State.Vz);
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void F2_YawStep_Is4369At720DegPerSec()
    {
        Assert.Equal(4369, CarrierHeading.YawStep(720));
        Assert.Equal(4369, CarrierHeading.YawStep(new Rig().Settings.TurnDegPerSec));
    }

    [Fact]
    [Trait("Id", "PCM-P3-1")]
    public void P3_SteadySpeeds_WalkAndSprint_WeightFull()
    {
        var walk = new Rig();
        walk.Tick(10, ForwardPlusX());
        Assert.Equal(5000, Speed(walk.State));

        var sprint = new Rig();
        sprint.Tick(10, ForwardPlusX(sprint: true));
        Assert.Equal(7500, Speed(sprint.State));
        Assert.NotEqual(CarrierFlags.None, sprint.State.Flags & CarrierFlags.Sprinting);
    }

    [Fact]
    [Trait("Id", "PCM-P3-1")]
    public void P3_Sprint_NeverDecaysOver3600Ticks()
    {
        // Arrange: 3600 ticks at 7500 mm/s is 900 m, so the run needs a long flat strip (480 x 2 tiles, 960 m) to stay clear of the map-edge clamp (story 007).
        var strip = new WorldTablesBuilder();
        strip.SetCorners(480, 2, Enumerable.Repeat(1000, 481 * 3).ToArray());
        var rig = new Rig(strip.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm)) { State = new CarrierState { X = 2000, Z = 1000, Y = 1000 } };
        rig.Tick(5, ForwardPlusX(sprint: true));

        // Act + Assert: no stamina - 7500 on every one of 3600 ticks.
        for (int i = 0; i < 3600; i++)
        {
            rig.Tick(ForwardPlusX(sprint: true));
            if (rig.State.Vx != 7500 || rig.State.Vz != 0 || (rig.State.Flags & CarrierFlags.Sprinting) == 0)
            {
                Assert.Fail($"Sprint decayed at tick {i}: v=({rig.State.Vx},{rig.State.Vz}) flags={rig.State.Flags}.");
            }
        }
    }

    [Fact]
    [Trait("Id", "PCM-P3-2")]
    public void P3_SprintHeldWithNoMoveInput_VelocityGoesToZero_AndSprintClears()
    {
        // Arrange
        var rig = new Rig();
        rig.Tick(8, ForwardPlusX(sprint: true));
        Assert.Equal(7500, rig.State.Vx);

        // Act + Assert (a stop from 7500 takes 4 ticks, PCM-F2-1 as amended)
        foreach (int e in new[] { 5000, 3333, 1666, 0 })
        {
            rig.Tick(In(sprint: true, look: PlusX));
            Assert.Equal(e, rig.State.Vx);
            Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.Sprinting);
        }

        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.Sprinting);
    }

    [Fact]
    [Trait("Id", "PCM-P3-2")]
    public void P3_SprintPressedWhileAirborne_DoesNotStart_ItStartsOnLanding()
    {
        // Arrange
        // Story 006: gravity is live, so the fixture starts well above the +1000 mm ground and stays airborne for the 20 ticks.
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = 100_000, Loco = LocoState.Airborne } };

        // Act + Assert (airborne)
        rig.Tick(20, ForwardPlusX(sprint: true));
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.Sprinting);
        Assert.Equal(5000, rig.State.Vx);

        // Act + Assert (landing)
        rig.State.Loco = LocoState.Grounded;
        rig.State.Y = 1000;
        rig.State.Vy = 0;
        rig.State.RemY = 0;
        rig.Tick(ForwardPlusX(sprint: true));
        Assert.NotEqual(CarrierFlags.None, rig.State.Flags & CarrierFlags.Sprinting);
        rig.Tick(3, ForwardPlusX(sprint: true));
        Assert.Equal(7500, rig.State.Vx);
    }

    [Fact]
    [Trait("Id", "PCM-P3-2")]
    public void P3_AlreadySprintingCarrier_KeepsSprintingWhenItBecomesAirborne()
    {
        // Arrange
        var rig = new Rig();
        rig.Tick(8, ForwardPlusX(sprint: true));

        // Act
        // Story 006: gravity is live, so the carrier is lifted well above the +1000 mm ground; at ground height it landed on tick 1.
        rig.State.Loco = LocoState.Airborne;
        rig.State.Y = 100_000;
        rig.State.Vy = 0;
        rig.State.RemY = 0;
        rig.Tick(5, ForwardPlusX(sprint: true));

        // Assert
        Assert.Equal(LocoState.Airborne, rig.State.Loco);
        Assert.NotEqual(CarrierFlags.None, rig.State.Flags & CarrierFlags.Sprinting);
        Assert.Equal(7500, rig.State.Vx);

        // Releasing the key ends it even in the air.
        rig.Tick(ForwardPlusX());
        Assert.Equal(LocoState.Airborne, rig.State.Loco);
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.Sprinting);
    }

    [Theory]
    [Trait("Id", "PCM-P3-2")]
    [InlineData(LocoState.Swimming, CarrierContextFlags.None, 2000)]
    [InlineData(LocoState.Mounted, CarrierContextFlags.None, 5000)]
    [InlineData(LocoState.Grounded, CarrierContextFlags.Mounted, 5000)]
    [InlineData(LocoState.Grounded, CarrierContextFlags.Dead, 5000)]
    [InlineData(LocoState.Grounded, CarrierContextFlags.Panel, 5000)]
    [InlineData(LocoState.Grounded, CarrierContextFlags.Panel | CarrierContextFlags.Dead, 5000)]
    public void P3_SprintIsIgnored_InSwimmingMountedDeadAndPanel(LocoState loco, CarrierContextFlags ctx, int walkCapMmS)
    {
        // Arrange: a carrier that was sprinting before the state began.
        var rig = new Rig(loco == LocoState.Swimming ? WorldTablesFixture.Deep() : null) { State = new CarrierState { X = 64_000, Z = 64_000, Flags = CarrierFlags.Sprinting, Loco = loco } };   // story 009: a swimmer must be in water

        // Act
        rig.Tick(12, In(0, Full, PlusX, sprint: true, ctx: ctx));

        // Assert
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.Sprinting);
        if (loco == LocoState.Swimming)
        {
            Assert.Equal(walkCapMmS, Speed(rig.State));
        }
        else
        {
            Assert.True(Speed(rig.State) <= walkCapMmS, $"Speed {Speed(rig.State)} exceeds the walk cap {walkCapMmS}.");
        }
    }

    [Fact]
    [Trait("Id", "PCM-P3-2")]
    public void P3_PanelContext_CarrierStillWalks()
    {
        var rig = new Rig();
        rig.Tick(10, In(0, Full, PlusX, sprint: true, ctx: CarrierContextFlags.Panel));
        Assert.Equal(5000, rig.State.Vx);
    }

    [Fact]
    [Trait("Id", "PCM-P3-3")]
    public void P3_CameraYaw90_ForwardInput_PointsAlongCameraForward_Within1Degree()
    {
        // Arrange
        var rig = new Rig();

        // Act
        rig.Tick(8, In(0, Full, 16384));

        // Assert: forward(90 deg) = (-1, 0). 1 degree = 182 BAM.
        Assert.Equal(-5000, rig.State.Vx);
        ushort heading = TrigQ15.Atan2Bam(-rig.State.Vx, -rig.State.Vz);
        Assert.InRange(Math.Abs(unchecked((short)(heading - 16384))), 0, 182);
        Assert.InRange(Math.Abs(unchecked((short)(rig.State.BodyYaw - 16384))), 0, 25);

        // Right input at yaw 0 is +X; the 90-degree rule maps (dx, dz) to (-dz, dx).
        var right = new Rig();
        right.Tick(8, In(Full, 0, 0));
        Assert.Equal(5000, right.State.Vx);
        Assert.InRange(Math.Abs(right.State.Vz), 0, 87);
    }

    [Fact]
    [Trait("Id", "PCM-P3-3")]
    public void P3_180DegreeTurn_CompletesBodyYawIn8Ticks_Not7()
    {
        // Arrange: facing yaw 0 (input forward at camera 0), then the move direction reverses.
        var rig = new Rig();
        rig.Tick(In(0, Full, 0));
        Assert.Equal(0, rig.State.BodyYaw);
        var back = In(0, -Full, 0);

        // Act + Assert: an exact 180 degree difference is the signed short -32768, so the tie-break turns negative (replay
        // determinism): the first tick lands on 65536 - 4369 = 61167.
        rig.Tick(in back);
        Assert.Equal(61_167, rig.State.BodyYaw);
        for (int i = 2; i <= 7; i++)
        {
            rig.Tick(in back);
            Assert.NotEqual(32768, rig.State.BodyYaw);
        }

        rig.Tick(in back);
        Assert.Equal(32768, rig.State.BodyYaw);
    }

    [Fact]
    [Trait("Id", "PCM-P3-3")]
    public void P3_AimingSetsBodyYawToCameraYaw_ThatTick_WithAndWithoutMoveInput()
    {
        var rig = new Rig();
        rig.Tick(In(0, Full, 0, aim: true));
        Assert.Equal(0, rig.State.BodyYaw);

        rig.Tick(In(0, 0, 40_000, aim: true));
        Assert.Equal(40_000, rig.State.BodyYaw);

        rig.Tick(In(Full, 0, 12_345, aim: true));
        Assert.Equal(12_345, rig.State.BodyYaw);
    }

    [Fact]
    [Trait("Id", "PCM-P3-3")]
    public void P3_BodyYaw_NeverTurnsTheLongWayRound()
    {
        // Arrange: from 60 000 to about 10 000 the short arc is +15 536 through the wrap.
        var up = new Rig { State = new CarrierState { BodyYaw = 60_000 } };
        up.Tick(In(0, Full, 10_000));
        Assert.Equal(64_369, up.State.BodyYaw);
        up.Tick(In(0, Full, 10_000));
        Assert.Equal(3202, up.State.BodyYaw);

        // And the other direction: from 10 000 to about 60 000 the short arc is negative.
        var down = new Rig { State = new CarrierState { BodyYaw = 10_000 } };
        down.Tick(In(0, Full, 60_000));
        Assert.Equal(5631, down.State.BodyYaw);

        // It converges on the target within the tolerance and without overshoot.
        up.Tick(4, In(0, Full, 10_000));
        Assert.InRange(Math.Abs(unchecked((short)(up.State.BodyYaw - 10_000))), 0, 25);
    }

    [Fact]
    [Trait("Id", "PCM-P3-3")]
    public void P3_ZeroInput_KeepsBodyYaw()
    {
        var rig = new Rig { State = new CarrierState { BodyYaw = 1234 } };
        rig.Tick(5, In(0, 0, 40_000));
        Assert.Equal(1234, rig.State.BodyYaw);
    }

    [Fact]
    [Trait("Id", "PCM-FEEL-1")]
    public void Feel_WalkInputFromRest_MovesBody41MmOnTheFirstSteppedTick()
    {
        // Arrange
        var rig = new Rig();
        int x0 = rig.State.X;
        int z0 = rig.State.Z;

        // Act
        rig.Tick(ForwardPlusX());

        // Assert: dvMax 1250 / 30 = 41.67 mm, floored with the remainder carried.
        Assert.InRange(rig.State.X - x0, 40, 42);
        Assert.Equal(41, rig.State.X - x0);
        Assert.Equal(z0, rig.State.Z);
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void Diagonal_InputIsLengthClamped_SoNoGaitIsFaster()
    {
        var rig = new Rig();
        rig.Tick(12, In(Full, Full, 0));
        Assert.InRange(Speed(rig.State), 4999, 5000);
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void Fuzz_1000RandomTicks_NeverExceedsCapOrPreviousSpeed_AndStepsStayWithin350Mm()
    {
        // Arrange
        var rng = new Pcg32(0xC0FFEEUL, 54UL);
        var rig = new Rig();
        long maxStep2 = 0;

        for (int i = 0; i < 1000; i++)
        {
            int roll = (int)(rng.Next32() % 8);
            int mx = roll == 0 ? 0 : (int)(rng.Next32() % 70_000) - 35_000;
            int my = roll == 1 ? 0 : (int)(rng.Next32() % 70_000) - 35_000;
            if (roll == 2)
            {
                mx = 0;
                my = 0;
            }

            LocoState loco = (rng.Next32() % 5) switch { 0 => LocoState.Airborne, 1 => LocoState.Swimming, _ => LocoState.Grounded };
            rig.State.Loco = loco;
            var input = new CarrierInput
            {
                MoveX = mx,
                MoveY = my,
                LookYaw = (ushort)rng.Next32(),
                SprintHeld = (rng.Next32() & 3) != 0,
                Aiming = (rng.Next32() & 7) == 0,
                SpeedPermille = 600 + (int)(rng.Next32() % 401),
                BuffAddPm = (rng.Next32() & 1) == 0 ? 400 : (int)(rng.Next32() % 800) - 100,
                ContextFlags = (rng.Next32() & 15) == 0 ? CarrierContextFlags.Panel : CarrierContextFlags.None,
            };
            int prev = Speed(rig.State);
            int px = rig.State.X;
            int pz = rig.State.Z;

            // Act
            rig.Tick(in input);

            // Assert
            int baseMmS = loco == LocoState.Swimming ? 2000 : (rig.State.Flags & CarrierFlags.Sprinting) != 0 ? 7500 : 5000;
            int vRef = Final(baseMmS, input.SpeedPermille, input.BuffAddPm);
            int now = Speed(rig.State);
            if (now > Math.Max(vRef, prev))
            {
                Assert.Fail($"Tick {i}: |v| {now} exceeds max(vRef {vRef}, previous {prev}).");
            }

            long dx = rig.State.X - px;
            long dz = rig.State.Z - pz;
            Assert.InRange(Math.Abs(dx), 0, 350);
            Assert.InRange(Math.Abs(dz), 0, 350);
            maxStep2 = Math.Max(maxStep2, (dx * dx) + (dz * dz));
        }

        // PCM-F2-1 AC clarification: the 350 mm bound is per axis; the Euclidean bound is < 351.
        Assert.True(maxStep2 < 351L * 351, $"A tick moved {Math.Sqrt(maxStep2):F2} mm (>= 351).");
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void Fuzz_MaxSpeedSprinter_StepNeverExceeds350Mm()
    {
        var rng = new Pcg32(7UL, 9UL);
        var rig = new Rig();
        long maxStep2 = 0;
        for (int i = 0; i < 1000; i++)
        {
            ushort look = (ushort)rng.Next32();
            int px = rig.State.X;
            int pz = rig.State.Z;
            rig.Tick(In(0, Full, look, sprint: true, add: 400));
            long dx = rig.State.X - px;
            long dz = rig.State.Z - pz;
            Assert.InRange(Math.Abs(dx), 0, 350);
            Assert.InRange(Math.Abs(dz), 0, 350);
            maxStep2 = Math.Max(maxStep2, (dx * dx) + (dz * dz));
            if (i % 40 == 39)
            {
                rig.State.X = 64_000;
                rig.State.Z = 64_000;
            }
        }

        // Per-axis steps are floor((V + rem) / 30) <= 350, but a diagonal's two independently floored axes can total up to ~1.4 mm
        // more than |V| / 30: observed 350.17 mm. Per PCM-F2-1 AC clarification the 350 mm bound is per axis and the Euclidean
        // bound is < 351 (rounds to 350).
        Assert.True(maxStep2 < 351L * 351, $"A tick moved {Math.Sqrt(maxStep2):F2} mm (>= 351).");
    }

    [Fact]
    [Trait("Id", "PCM-F1-1")]
    public void Step_PlaceholderPhases_LeaveVerticalStateAndCountersUntouched()
    {
        var rig = new Rig();
        rig.Tick(10, ForwardPlusX(sprint: true));

        Assert.Equal((1000, 0, 0), (rig.State.Y, rig.State.Vy, rig.State.RemY));
        Assert.Equal(0, rig.Scratch.CandidateOverflow + rig.Scratch.StepClamped + rig.Scratch.Depenetrations);
    }
}

/// <summary>Zero-allocation guardrail for <see cref="OwnedCarrier.Step"/> (story 005), in the non-parallel collection.</summary>
[Collection(NonParallelCollection.Name)]
public class LocomotionAllocationTests
{
    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void Step_10000Calls_AllocateNothing()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(OwnedCarrier).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        WorldTables world = WorldTablesFixture.Dry();
        PlayerSettings settings = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
        var remotes = new EmptyRemoteCapsuleSource();
        var supports = new EmptySupportTransformSource();
        long sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("10,000 OwnedCarrier.Step calls", () =>
        {
            var state = new CarrierState { X = 64_000, Z = 64_000 };
            var scratch = new CarrierScratch(64);
            CarrierEnv env = CarrierEnv.ForM1a(world, settings, new EntityId(5), remotes, supports);
            for (int i = 0; i < 10_000; i++)
            {
                var input = new CarrierInput
                {
                    MoveX = (i * 977 % 65_535) - 32_767,
                    MoveY = (i * 619 % 65_535) - 32_767,
                    LookYaw = (ushort)(i * 31),
                    SprintHeld = (i & 64) != 0,
                    SpeedPermille = 1000,
                };
                OwnedCarrier.Step(ref state, in input, in env, ref scratch);
                sink += state.X + state.Vx;
            }
        });

        Assert.NotEqual(long.MinValue, sink);
    }
}
