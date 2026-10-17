using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 006: ground contact, gravity and jump (GDD P4 phase 4, P5, F3; ADR-0013 section 2; TR-movement-001/002/005/020).
/// Flat dry ground (FX-Dry, +1000 mm) unless stated. "Tick n" counts Steps from 1; heights are relative to the ground (1000 mm).
/// </summary>
public class JumpTests
{
    private const int Ground = WorldTablesFixture.DryHeightMm;
    private const int V0 = 7589;
    private const int V0Extra = 6830;
    private const int PerTick = 800;
    private const ushort PlusX = 49152;   // forward = (+1, 0)
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

        public CarrierState State = new() { X = 64_000, Z = 64_000, Y = Ground };

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

        public void Idle(int n) => Tick(n, Held());
    }

    private static CarrierInput Held(int weight = 1000, int extra = 0, CarrierContextFlags ctx = CarrierContextFlags.None, int my = 0, bool sprint = false, bool host = false) =>
        new() { SpeedPermille = weight, ExtraJumps = extra, ContextFlags = ctx, MoveY = my, LookYaw = PlusX, SprintHeld = sprint, HostAuthored = host };

    private static CarrierInput Press(int weight = 1000, int extra = 0, CarrierContextFlags ctx = CarrierContextFlags.None, int my = 0, bool sprint = false, bool host = false) =>
        new() { SpeedPermille = weight, ExtraJumps = extra, ContextFlags = ctx, MoveY = my, LookYaw = PlusX, SprintHeld = sprint, HostAuthored = host, JumpPressed = true };

    /// <summary>Steps a press on tick 1 of a jump from the ground and returns the relative height after each tick (index 0 = tick 1).</summary>
    private static int[] JumpArc(Rig rig, int weight, int ticks)
    {
        var heights = new int[ticks];
        for (int t = 0; t < ticks; t++)
        {
            rig.Tick(t == 0 ? Press(weight) : Held(weight));
            heights[t] = rig.State.Y - Ground;
        }

        return heights;
    }

    // ------------------------------------------------------------------ PCM-F3-1

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void F3_LaunchSpeeds_V0Is7589_AndExtraIs6830()
    {
        var rig = new Rig();

        Assert.Equal(V0, CarrierJump.LaunchSpeed(rig.Settings));
        Assert.Equal(V0Extra, CarrierJump.ExtraLaunchSpeed(rig.Settings));
        Assert.Equal(PerTick, CarrierGravity.PerTickMmS(rig.Settings));
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void F3_Jump_ApexIs1196OnTick9_NotTheTruncated1191()
    {
        var rig = new Rig();

        int[] h = JumpArc(rig, 1000, 12);

        Assert.Equal(1196, h[8]);                 // tick 9
        Assert.True(h[7] < 1196);                 // tick 8
        Assert.Equal(1196, h.Max());
        Assert.NotEqual(1191, h.Max());           // 1191 = the remainder was dropped each tick
        Assert.Equal(Enumerable.Range(0, 9).Select(i => h[i]).Max(), h[8]);
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void F3_Jump_FiresOnThePressTick_AndThatTickIntegratesWithV0()
    {
        var rig = new Rig();

        rig.Tick(Press());

        // Tick 1 of the arc: n = 7589 + 6789 + 0 = 14 378; dy = 239, RemY = 38; Vy ends at vy' = 6789.
        Assert.Equal(LocoState.Airborne, rig.State.Loco);
        Assert.Equal(Ground + 239, rig.State.Y);
        Assert.Equal(V0 - PerTick, rig.State.Vy);
        Assert.Equal(38, rig.State.RemY);
        Assert.Equal(0, rig.State.JumpBufferLeft);
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void F3_Jump_LandsOnTick19_NotOn18Or20()
    {
        var rig = new Rig();
        var loco = new LocoState[22];

        for (int t = 0; t < loco.Length; t++)
        {
            rig.Tick(t == 0 ? Press() : Held());
            loco[t] = rig.State.Loco;
            if (t == 17)
            {
                Assert.Equal(Ground + 233, rig.State.Y);   // tick 18: still 233 mm up
            }
        }

        Assert.All(loco[..18], l => Assert.Equal(LocoState.Airborne, l));
        Assert.Equal(LocoState.Grounded, loco[18]);       // tick 19
        Assert.Equal(19, Array.IndexOf(loco, LocoState.Grounded) + 1);
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void F3_Landing_PutsTheFeetOnTheTerrain_WithVyAndRemYZero()
    {
        var rig = new Rig();

        rig.Tick(Press());
        rig.Tick(18, Held());

        Assert.Equal((LocoState.Grounded, Ground, 0, 0), (rig.State.Loco, rig.State.Y, rig.State.Vy, rig.State.RemY));
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void F3_Fall_CapsAt20000_AndTheTrapezoidStillCarriesTheRemainder()
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 100_000, Loco = LocoState.Airborne } };
        int minVy = 0;

        for (int t = 0; t < 40; t++)
        {
            rig.Idle(1);
            minVy = Math.Min(minVy, rig.State.Vy);
        }

        Assert.Equal(-20_000, minVy);
        Assert.Equal(-20_000, rig.State.Vy);

        // At the cap n = -40 000 + RemY each tick: displacements of 666/667 mm that sum to exactly 2000 mm per 3 ticks.
        int y0 = rig.State.Y;
        int worst = 0;
        for (int t = 0; t < 3; t++)
        {
            int before = rig.State.Y;
            rig.Idle(1);
            int fall = before - rig.State.Y;
            worst = Math.Max(worst, fall);
            Assert.InRange(fall, 666, 667);
        }

        Assert.Equal(667, worst);
        Assert.Equal(2000, y0 - rig.State.Y);
        Assert.InRange(rig.State.RemY, 0, 59);
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void F3_Integrate_ClampsAVyBelowTheCap_AndFloorsNegativeDisplacement()
    {
        SimCounterSink sink = default;
        int vy = -30_000;
        int rem = 0;

        int dy = CarrierGravity.Integrate(ref vy, ref rem, PerTick, 20_000, ref sink);

        // vy' = max(-30 800, -20 000) = -20 000; n = -50 000 -> floor(-833.33) = -834, rem = 40.
        Assert.Equal((-20_000, -834, 40), (vy, dy, rem));
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void F3_ExtraJump_RiseIs969Mm_FromTheSettingsV0Extra()
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 50_000, Loco = LocoState.Airborne } };
        int y0 = rig.State.Y;
        int maxRise = 0;

        rig.Tick(Press(extra: 1));
        maxRise = Math.Max(maxRise, rig.State.Y - y0);
        for (int t = 0; t < 14; t++)
        {
            rig.Tick(Held(extra: 1));
            maxRise = Math.Max(maxRise, rig.State.Y - y0);
        }

        Assert.Equal(969, maxRise);
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void F3_LaunchSpeeds_ComeFromTheSettings_AndAreZeroForDegenerateValues()
    {
        var rig = new Rig();
        PlayerSettings s = rig.Settings;

        Assert.Equal(0, CarrierJump.LaunchSpeed(s with { JumpHeightMm = 0 }));
        Assert.Equal(0, CarrierJump.LaunchSpeed(s with { G = 0 }));
        Assert.Equal(0, CarrierJump.LaunchSpeed(s with { G = -5, JumpHeightMm = -7 }));
        Assert.Equal(0, CarrierJump.ExtraLaunchSpeed(s with { ExtraJumpPm = 0 }));
        Assert.Equal(0, CarrierJump.ExtraLaunchSpeed(s with { ExtraJumpPm = -10 }));
        Assert.Equal(7589 * 2, CarrierJump.ExtraLaunchSpeed(s with { ExtraJumpPm = 2000 }));
        Assert.Equal(int.MaxValue, CarrierJump.LaunchSpeed(s with { G = int.MaxValue, JumpHeightMm = int.MaxValue }));
        Assert.Equal(21_466, CarrierJump.LaunchSpeed(s with { G = 192_000, JumpHeightMm = 1200 }));
    }

    // ------------------------------------------------------------------ PCM-P5-1

    [Theory]
    [Trait("Id", "PCM-P5-1")]
    [InlineData(15, true)]    // 4 ticks before the landing on tick 19: fires on the landing tick
    [InlineData(14, false)]   // 5 ticks before: the buffer has run out
    [InlineData(16, true)]
    [InlineData(18, true)]
    [InlineData(19, true)]    // pressed on the landing tick itself: no lost frame
    public void P5_BufferedPress_FiresOnTheLandingTickOnlyWithinFourTicks(int pressTick, bool fires)
    {
        var rig = new Rig();
        rig.Tick(Press());                                  // tick 1: the first jump
        for (int t = 2; t <= 18; t++)
        {
            rig.Tick(t == pressTick ? Press() : Held());
        }

        rig.Tick(pressTick == 19 ? Press() : Held());        // tick 19: the landing tick

        if (fires)
        {
            // Launched on the landing tick: tick 1 of a fresh arc.
            Assert.Equal((LocoState.Airborne, Ground + 239, V0 - PerTick), (rig.State.Loco, rig.State.Y, rig.State.Vy));
            Assert.Equal(0, rig.State.JumpBufferLeft);
        }
        else
        {
            Assert.Equal((LocoState.Grounded, Ground, 0), (rig.State.Loco, rig.State.Y, rig.State.Vy));
            Assert.Equal(0, rig.State.JumpBufferLeft);
        }
    }

    [Fact]
    [Trait("Id", "PCM-P5-1")]
    public void P5_Press_ArmsTheBufferToJumpBufferTicks_AndItAgesOneTickAtATime()
    {
        var rig = new Rig();
        rig.Tick(Press());      // fires from the ground and is consumed
        rig.Tick(Press());      // airborne, no perk: armed

        Assert.Equal(4, rig.State.JumpBufferLeft);

        rig.Tick(Held());
        Assert.Equal(3, rig.State.JumpBufferLeft);
        rig.Tick(Held());
        rig.Tick(Held());
        rig.Tick(Held());
        Assert.Equal(0, rig.State.JumpBufferLeft);
        rig.Tick(Held());
        Assert.Equal(0, rig.State.JumpBufferLeft);
    }

    [Fact]
    [Trait("Id", "PCM-P5-1")]
    public void P5_BufferedPress_ByTheSettings_JumpBufferTicksZeroMeansNoBuffer_And999ClampsTo255()
    {
        var none = new Rig();
        none.Settings = none.Settings with { JumpBufferTicks = 0 };
        none.Tick(Press());
        none.Tick(Press());                                 // airborne press, nothing to arm
        Assert.Equal(0, none.State.JumpBufferLeft);

        var big = new Rig();
        big.Settings = big.Settings with { JumpBufferTicks = 999 };
        big.Tick(Press());
        big.Tick(Press());
        Assert.Equal(255, big.State.JumpBufferLeft);
    }

    [Fact]
    [Trait("Id", "PCM-P5-1")]
    public void P5_PressOnGroundWithZeroBuffer_StillJumpsOnThatTick()
    {
        var rig = new Rig();
        rig.Settings = rig.Settings with { JumpBufferTicks = 0 };

        rig.Tick(Press());

        Assert.Equal((LocoState.Airborne, V0 - PerTick), (rig.State.Loco, rig.State.Vy));
    }

    [Theory]
    [Trait("Id", "PCM-P5-1")]
    [InlineData(1, true)]
    [InlineData(4, true)]     // pressed 4 ticks after leaving ground: jumps
    [InlineData(5, false)]    // 5 ticks after: coyote has run out
    [InlineData(8, false)]
    public void P5_Coyote_FixturePlacedAirborneWithCoyoteLeft4_JumpsUpTo4TicksLater(int pressTick, bool jumps)
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 20_000, Loco = LocoState.Airborne, CoyoteLeft = 4 } };

        for (int t = 1; t < pressTick; t++)
        {
            rig.Tick(Held());
        }

        int vyBefore = rig.State.Vy;
        rig.Tick(Press());

        if (jumps)
        {
            Assert.Equal(V0 - PerTick, rig.State.Vy);
            Assert.Equal(0, rig.State.CoyoteLeft);
        }
        else
        {
            Assert.Equal(vyBefore - PerTick, rig.State.Vy);
            Assert.Equal(0, rig.State.CoyoteLeft);
        }
    }

    [Theory]
    [Trait("Id", "PCM-P5-1")]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void P5_Coyote_StartedByLosingTheGround_JumpsWithinCoyoteTicks(int pressTick, bool jumps)
    {
        // The terrain-only "edge": the feet sit 2000 mm above the terrain (more than the 400 mm snap-down), so the ground is lost.
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 2000 } };

        rig.Tick(Held());   // tick 0: the ground is lost; coyote starts and is not aged on this tick
        Assert.Equal((LocoState.Airborne, 4), (rig.State.Loco, (int)rig.State.CoyoteLeft));
        for (int t = 1; t < pressTick; t++)
        {
            rig.Tick(Held());
        }

        rig.Tick(Press());

        Assert.Equal(jumps, rig.State.Vy > 0);
    }

    [Fact]
    [Trait("Id", "PCM-P5-1")]
    public void P5_Coyote_ACoyoteJumpIsANormalJump_NotTheExtraJump_AndItCannotRepeat()
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 20_000, Loco = LocoState.Airborne, CoyoteLeft = 2 } };

        rig.Tick(Press(extra: 1));
        Assert.Equal(V0 - PerTick, rig.State.Vy);
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
        Assert.Equal(0, rig.State.CoyoteLeft);

        // The extra jump is still available afterwards.
        rig.Tick(Press(extra: 1));
        Assert.Equal(V0Extra - PerTick, rig.State.Vy);
        Assert.Equal(CarrierFlags.ExtraJumpUsed, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
    }

    [Fact]
    [Trait("Id", "PCM-P5-1")]
    public void P5_Coyote_CoyoteTicksZero_GivesNoGraceAndAnUnrefusedEdgeStartsNoCoyote()
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 401 } };
        rig.Settings = rig.Settings with { CoyoteTicks = 0 };

        rig.Tick(Press());

        Assert.Equal(LocoState.Airborne, rig.State.Loco);
        Assert.True(rig.State.Vy < 0);   // the press only armed the buffer
    }

    [Theory]
    [Trait("Id", "PCM-P5-1")]
    [InlineData(1000)]   // P = 0
    [InlineData(600)]    // P = 40
    public void P5_Apex_IsExactly1196_AtAnyWeight(int speedPermille)
    {
        var rig = new Rig();

        int[] h = JumpArc(rig, speedPermille, 12);

        Assert.Equal(1196, h.Max());
        Assert.Equal(1196, h[8]);
    }

    [Fact]
    [Trait("Id", "PCM-P5-1")]
    public void P5_Apex_IsUnchangedWhileWalkingForwardAtWeight600()
    {
        var rig = new Rig();
        int maxHeight = 0;

        rig.Tick(Press(weight: 600, my: Full));
        for (int t = 0; t < 14; t++)
        {
            rig.Tick(Held(weight: 600, my: Full));
            maxHeight = Math.Max(maxHeight, rig.State.Y - Ground);
        }

        Assert.Equal(1196, maxHeight);
    }

    // ------------------------------------------------------------------ PCM-P5-2

    [Theory]
    [Trait("Id", "PCM-P5-2")]
    [InlineData(-3000)]
    [InlineData(-15_000)]
    public void P5_ExtraJump_SetsVerticalSpeedTo6830_AtAnyFallSpeed(int vy)
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 50_000, Loco = LocoState.Airborne, Vy = vy, RemY = 17 } };
        int y0 = rig.State.Y;

        rig.Tick(Press(extra: 1));

        // Set (not added): this tick integrates 6830 -> 6030 with a zero remainder, whatever the fall speed was: n = 12 860, dy = 214.
        Assert.Equal(V0Extra - PerTick, rig.State.Vy);
        Assert.Equal(214, rig.State.Y - y0);
        Assert.Equal(20, rig.State.RemY);
        Assert.Equal(CarrierFlags.ExtraJumpUsed, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
    }

    [Fact]
    [Trait("Id", "PCM-P5-2")]
    public void P5_ExtraJump_HasNoThirdJump_ThePerAirbornePeriodFlagHolds()
    {
        var rig = new Rig();
        rig.Tick(Press(extra: 1));   // ground jump
        rig.Tick(Press(extra: 1));   // the extra jump
        int vyAfterExtra = rig.State.Vy;

        rig.Tick(Press(extra: 1));   // would be a third jump

        Assert.Equal(vyAfterExtra - PerTick, rig.State.Vy);
        Assert.Equal(CarrierFlags.ExtraJumpUsed, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
        Assert.Equal(4, rig.State.JumpBufferLeft);   // the press waits for the landing instead
    }

    [Fact]
    [Trait("Id", "PCM-P5-2")]
    public void P5_ExtraJump_ResetsOnLanding_SoTheNextAirbornePeriodHasOneAgain()
    {
        var rig = new Rig();
        rig.Tick(Press(extra: 1));
        rig.Tick(Press(extra: 1));
        Assert.Equal(CarrierFlags.ExtraJumpUsed, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
        for (int t = 0; t < 40 && rig.State.Loco == LocoState.Airborne; t++)
        {
            rig.Tick(Held(extra: 1));
        }

        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.ExtraJumpUsed);

        rig.Tick(Press(extra: 1));
        rig.Tick(Press(extra: 1));
        Assert.Equal(V0Extra - PerTick, rig.State.Vy);
    }

    [Fact]
    [Trait("Id", "PCM-P5-2")]
    public void P5_ExtraJump_WithoutThePerk_ThereIsNoMidAirJump()
    {
        var rig = new Rig();
        rig.Tick(Press(extra: 0));
        int vy = rig.State.Vy;

        rig.Tick(Press(extra: 0));

        Assert.Equal(vy - PerTick, rig.State.Vy);
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
    }

    [Fact]
    [Trait("Id", "PCM-P5-2")]
    public void P5_ExtraJump_IsCappedByTheMaxExtraJumpsSetting()
    {
        var rig = new Rig();
        rig.Settings = rig.Settings with { MaxExtraJumps = 0 };
        rig.Tick(Press(extra: 1));
        int vy = rig.State.Vy;

        rig.Tick(Press(extra: 5));

        Assert.Equal(vy - PerTick, rig.State.Vy);
    }

    [Fact]
    [Trait("Id", "PCM-P5-2")]
    public void P5_ExtraJump_AnUnpressedAirbornePeriodKeepsItForLater_AndALaterPressUsesIt()
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 50_000, Loco = LocoState.Airborne } };
        rig.Tick(5, Held(extra: 1));
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.ExtraJumpUsed);

        rig.Tick(Press(extra: 1));

        Assert.Equal(V0Extra - PerTick, rig.State.Vy);
    }

    [Fact]
    [Trait("Id", "PCM-P5-2")]
    public void P5_ExtraJump_AMidAirPressWithThePerk_IsTheExtraJump_NotABufferForLanding()
    {
        var rig = new Rig();
        rig.Tick(Press(extra: 1));
        rig.Tick(Press(extra: 1));

        Assert.Equal(0, rig.State.JumpBufferLeft);
    }

    // ------------------------------------------------------------------ PCM-P5-3

    [Theory]
    [Trait("Id", "PCM-P5-3")]
    [InlineData(CarrierContextFlags.Panel)]
    [InlineData(CarrierContextFlags.Dead)]
    [InlineData(CarrierContextFlags.Mounted)]
    [InlineData(CarrierContextFlags.Panel | CarrierContextFlags.Build)]
    public void P5_Refused_InAPanelOrDeadContext_OnTheGround(CarrierContextFlags ctx)
    {
        var rig = new Rig();

        rig.Tick(Press(ctx: ctx));

        Assert.Equal((LocoState.Grounded, Ground, 0, 0, 0), (rig.State.Loco, rig.State.Y, rig.State.Vy, rig.State.RemY, (int)rig.State.JumpBufferLeft));
    }

    [Fact]
    [Trait("Id", "PCM-P5-3")]
    public void P5_Refused_InAPanelContext_AlsoRefusesTheExtraJump_AndDropsAnArmedBuffer()
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 50_000, Loco = LocoState.Airborne, JumpBufferLeft = 3 } };

        rig.Tick(Press(extra: 1, ctx: CarrierContextFlags.Panel));

        Assert.Equal(-PerTick, rig.State.Vy);
        Assert.Equal(0, rig.State.JumpBufferLeft);
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
    }

    [Fact]
    [Trait("Id", "PCM-P5-3")]
    public void P5_Allowed_InBuildContext()
    {
        var rig = new Rig();

        rig.Tick(Press(ctx: CarrierContextFlags.Build));

        Assert.Equal(V0 - PerTick, rig.State.Vy);
    }

    [Theory]
    [Trait("Id", "PCM-P5-3")]
    [InlineData(LocoState.Mounted)]   // story 009: the Swimming row moved to WaterTests (a swimmer is now held on the water plane with Vy 0)
    public void P5_Refused_WhileSwimmingOrMounted_NoVyChange_NoBufferArm_NoGravity(LocoState loco)
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 500, Loco = loco, Vy = 123, JumpBufferLeft = 2 } };

        rig.Tick(Press(extra: 1));

        Assert.Equal((loco, Ground + 500, 123, 0), (rig.State.Loco, rig.State.Y, rig.State.Vy, (int)rig.State.JumpBufferLeft));
    }

    [Fact]
    [Trait("Id", "PCM-P5-3")]
    public void P5_Refused_ForAHostAuthoredBody_ButGravityStillDropsItToTheGround()
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 3000, Loco = LocoState.Airborne } };

        rig.Tick(Press(host: true));

        Assert.Equal(-PerTick, rig.State.Vy);
        Assert.Equal(0, rig.State.JumpBufferLeft);

        for (int t = 0; t < 40 && rig.State.Loco == LocoState.Airborne; t++)
        {
            rig.Tick(Press(host: true));
        }

        Assert.Equal((LocoState.Grounded, Ground), (rig.State.Loco, rig.State.Y));
    }

    [Theory]
    [Trait("Id", "PCM-P5-3")]
    [InlineData(LocoState.Grounded, CarrierContextFlags.None, false, true)]
    [InlineData(LocoState.Airborne, CarrierContextFlags.None, false, true)]
    [InlineData(LocoState.Airborne, CarrierContextFlags.Build, false, true)]
    [InlineData(LocoState.Swimming, CarrierContextFlags.None, false, false)]
    [InlineData(LocoState.Mounted, CarrierContextFlags.None, false, false)]
    [InlineData(LocoState.Grounded, CarrierContextFlags.Panel, false, false)]
    [InlineData(LocoState.Grounded, CarrierContextFlags.Dead, false, false)]
    [InlineData(LocoState.Grounded, CarrierContextFlags.Mounted, false, false)]
    [InlineData(LocoState.Grounded, CarrierContextFlags.None, true, false)]
    [InlineData((LocoState)9, CarrierContextFlags.None, false, false)]
    public void P5_IsAllowed_TruthTable(LocoState loco, CarrierContextFlags ctx, bool host, bool expected)
    {
        Assert.Equal(expected, CarrierJump.IsAllowed(loco, ctx, host));
    }

    [Fact]
    [Trait("Id", "PCM-P5-3")]
    public void P5_UnknownLocoValue_IsLeftUntouched()
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 500, Loco = (LocoState)9, Vy = 7 } };

        rig.Tick(Press());

        Assert.Equal((Ground + 500, 7, (LocoState)9), (rig.State.Y, rig.State.Vy, rig.State.Loco));
    }

    [Fact]
    [Trait("Id", "PCM-P5-3")]
    public void P5_Landing_At20000MmS_LeavesHorizontalStateUnchanged_NoSlowDownNoStun()
    {
        // Two identical carriers, same horizontal state and input; one is high enough to keep falling, one lands this tick.
        CarrierState Make(int y) => new()
        {
            X = 64_000, Z = 64_000, Y = y, Loco = LocoState.Airborne, Vy = -20_000, Vx = 3000, Vz = -1200, RemX = 7, RemZ = -3,
            Flags = CarrierFlags.Sprinting, BodyYaw = 1234,
        };

        var landing = new Rig { State = Make(Ground + 300) };
        var airborne = new Rig { State = Make(Ground + 100_000) };
        CarrierInput input = Held(my: Full, sprint: true);

        landing.Tick(in input);
        airborne.Tick(in input);

        Assert.Equal(LocoState.Grounded, landing.State.Loco);
        Assert.Equal(LocoState.Airborne, airborne.State.Loco);
        Assert.Equal((airborne.State.Vx, airborne.State.Vz, airborne.State.X, airborne.State.Z, airborne.State.RemX, airborne.State.RemZ),
            (landing.State.Vx, landing.State.Vz, landing.State.X, landing.State.Z, landing.State.RemX, landing.State.RemZ));
        Assert.Equal(airborne.State.Flags & CarrierFlags.Sprinting, landing.State.Flags & CarrierFlags.Sprinting);
        Assert.Equal(airborne.State.BodyYaw, landing.State.BodyYaw);
        Assert.Equal((Ground, 0, 0), (landing.State.Y, landing.State.Vy, landing.State.RemY));
    }

    [Fact]
    [Trait("Id", "PCM-P5-3")]
    public void P5_Landing_From10MetersAtTerminalSpeed_IsASoftStop_AndTheNextTickWalksNormally()
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 10_000, Loco = LocoState.Airborne } };
        CarrierInput walk = Held(my: Full);
        int ticks = 0;
        while (rig.State.Loco == LocoState.Airborne && ticks < 200)
        {
            rig.Tick(in walk);
            ticks++;
        }

        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        int speedAtLanding = CarrierAccel.Length(rig.State.Vx, rig.State.Vz);
        rig.Tick(in walk);

        // No speed was lost to the landing: the next grounded tick accelerates (F2) from where the air left off.
        Assert.True(CarrierAccel.Length(rig.State.Vx, rig.State.Vz) >= speedAtLanding);
    }

    // ------------------------------------------------------------------ Ground contact and the other acceptance bullets

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void Ground_GravityActsOnlyWhileAirborne_AGroundedWalkerKeepsVyRemYAndY()
    {
        var rig = new Rig();

        for (int t = 0; t < 120; t++)
        {
            rig.Tick(Held(my: Full, sprint: (t & 8) != 0));
            Assert.Equal((LocoState.Grounded, Ground, 0, 0), (rig.State.Loco, rig.State.Y, rig.State.Vy, rig.State.RemY));
        }
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void Ground_AGroundedCarrierBelowOrSlightlyAboveTheTerrain_IsHeldOnIt()
    {
        var below = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground - 250, Vy = 400, RemY = 11 } };
        var above = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 400, CoyoteLeft = 3 } };

        below.Tick(Held());
        above.Tick(Held());

        Assert.Equal((LocoState.Grounded, Ground, 0, 0), (below.State.Loco, below.State.Y, below.State.Vy, below.State.RemY));
        Assert.Equal((LocoState.Grounded, Ground, 0), (above.State.Loco, above.State.Y, (int)above.State.CoyoteLeft));
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void Ground_LosingTheGround_StartsCoyoteTicks_AndGravityActsFromThatTick()
    {
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 401 } };

        rig.Tick(Held());

        Assert.Equal(LocoState.Airborne, rig.State.Loco);
        Assert.Equal(4, rig.State.CoyoteLeft);
        Assert.Equal(-PerTick, rig.State.Vy);
        Assert.True(rig.State.Y < Ground + 401);
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void Ground_TheFeetPointSamplesTheBilinearTerrain_OnASlopedTile()
    {
        // FX-Ramp(1000): the rising tile spans x 6000..8000 (tile 3), height 0 -> 1000. A stationary carrier at x = 7000 stands at 500.
        var rig = new Rig(WorldTablesFixture.Ramp(1000)) { State = new CarrierState { X = 7000, Z = 1000, Y = 500 } };

        rig.Idle(5);
        Assert.Equal((LocoState.Grounded, 500), (rig.State.Loco, rig.State.Y));

        // A jump from the slope lands back on the same sample.
        rig.Tick(Press());
        Assert.True(rig.State.Y > 500);
        for (int t = 0; t < 30 && rig.State.Loco == LocoState.Airborne; t++)
        {
            rig.Idle(1);
        }

        Assert.Equal((LocoState.Grounded, 500, 0, 0), (rig.State.Loco, rig.State.Y, rig.State.Vy, rig.State.RemY));
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void Ground_LandingExactlyOnTheTerrain_CountsAsGrounded()
    {
        // Y = ground + 14: one tick of fall from rest is n = -800 -> dy = -14 (floor of -13.33 is -14), so the feet end exactly on the terrain.
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 14, Loco = LocoState.Airborne } };

        rig.Idle(1);

        Assert.Equal((LocoState.Grounded, Ground), (rig.State.Loco, rig.State.Y));
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void Ground_AJumpWithNoLaunchSpeed_LandsAtOnce_AndWastesThePress()
    {
        var rig = new Rig();
        rig.Settings = rig.Settings with { JumpHeightMm = 0 };

        rig.Tick(Press());

        Assert.Equal((LocoState.Grounded, Ground, 0, 0), (rig.State.Loco, rig.State.Y, rig.State.Vy, rig.State.RemY));
    }

    [Fact]
    [Trait("Id", "PCM-P5-1")]
    public void P5_JumpWhileSprinting_KeepsTheSprintFlagAndHorizontalVelocityThroughTheArc()
    {
        var rig = new Rig();
        CarrierInput run = Held(my: Full, sprint: true);
        rig.Tick(20, in run);                                   // ramp up to sprint speed on the ground
        Assert.Equal(CarrierFlags.Sprinting, rig.State.Flags & CarrierFlags.Sprinting);
        int cruise = CarrierAccel.Length(rig.State.Vx, rig.State.Vz);
        Assert.Equal(7500, cruise);

        rig.Tick(Press(my: Full, sprint: true));
        for (int t = 0; t < 17; t++)
        {
            rig.Tick(in run);
            Assert.Equal(LocoState.Airborne, rig.State.Loco);
            Assert.Equal(CarrierFlags.Sprinting, rig.State.Flags & CarrierFlags.Sprinting);
            Assert.Equal(cruise, CarrierAccel.Length(rig.State.Vx, rig.State.Vz));
        }
    }

    [Fact]
    [Trait("Id", "PCM-P5-1")]
    public void P5_ABufferedJump_ConsumedAtLanding_KeepsHorizontalMomentum()
    {
        var rig = new Rig();
        CarrierInput run = Held(my: Full, sprint: true);
        rig.Tick(20, in run);
        int cruise = CarrierAccel.Length(rig.State.Vx, rig.State.Vz);
        rig.Tick(Press(my: Full, sprint: true));
        for (int t = 2; t <= 18; t++)
        {
            rig.Tick(t == 16 ? Press(my: Full, sprint: true) : run);
        }

        rig.Tick(in run);   // landing tick: the buffered press launches again

        Assert.Equal(LocoState.Airborne, rig.State.Loco);
        Assert.Equal(V0 - PerTick, rig.State.Vy);
        Assert.Equal(cruise, CarrierAccel.Length(rig.State.Vx, rig.State.Vz));
        Assert.Equal(CarrierFlags.Sprinting, rig.State.Flags & CarrierFlags.Sprinting);
    }

    // ------------------------------------------------------------------ Randomised sequences

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void Fuzz_10000Sequences_NeverTunnelThroughTheFloor_AndTheRemainderInvariantHolds()
    {
        var rng = new Pcg32(0x5EEDUL, 0x6AUL);
        WorldTables dry = WorldTablesFixture.Dry();
        WorldTables ramp = WorldTablesFixture.Ramp(1000);
        var remotes = new EmptyRemoteCapsuleSource();
        var supports = new EmptySupportTransformSource();
        PlayerSettings settings = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
        long steps = 0;
        long landings = 0;
        long jumps = 0;

        for (int seq = 0; seq < 10_000; seq++)
        {
            bool onRamp = (rng.Next32() & 3) == 0;
            WorldTables world = onRamp ? ramp : dry;
            CarrierEnv env = CarrierEnv.ForM1a(world, settings, new EntityId(5), remotes, supports);
            var scratch = new CarrierScratch(64);
            var state = new CarrierState
            {
                X = onRamp ? 6200 + (int)(rng.Next32() % 1600) : 64_000,
                Z = onRamp ? 1000 : 64_000,
                Loco = (rng.Next32() & 1) == 0 ? LocoState.Grounded : LocoState.Airborne,
            };

            int terrain = WorldHeight.At(world, state.X, state.Z);
            state.Y = terrain + (state.Loco == LocoState.Grounded ? (int)(rng.Next32() % 700) - 100 : (int)(rng.Next32() % 4000));
            if (state.Loco == LocoState.Airborne)
            {
                state.Vy = (int)(rng.Next32() % 36_000) - 28_000;
                state.RemY = (int)(rng.Next32() % 60);
                state.CoyoteLeft = (byte)(rng.Next32() % 6);
            }

            int length = 8 + (int)(rng.Next32() % 40);
            for (int t = 0; t < length; t++)
            {
                uint r = rng.Next32();
                var input = new CarrierInput
                {
                    // Horizontal motion only on the flat map: slopes under a moving carrier are the sweep's job (story 007).
                    MoveY = !onRamp && (r & 1) != 0 ? Full : 0,
                    LookYaw = PlusX,
                    SprintHeld = (r & 2) != 0,
                    JumpPressed = ((r >> 2) & 3) == 0,
                    ExtraJumps = (int)((r >> 4) & 1),
                    SpeedPermille = (r & 0x100) != 0 ? 1000 : 600,
                    ContextFlags = ((r >> 9) & 15) == 0 ? CarrierContextFlags.Panel : CarrierContextFlags.None,
                    HostAuthored = ((r >> 13) & 31) == 0,
                };

                LocoState before = state.Loco;
                OwnedCarrier.Step(ref state, in input, in env, ref scratch);
                steps++;

                int floor = WorldHeight.At(world, state.X, state.Z);
                Assert.True(state.Y >= floor, $"seq {seq} tick {t}: Y {state.Y} below terrain {floor}");
                Assert.InRange(state.RemY, -59, 59);
                Assert.InRange(state.RemY, 0, 59);
                Assert.True(state.Vy >= -20_000);
                if (state.Loco == LocoState.Grounded)
                {
                    Assert.Equal((floor, 0, 0), (state.Y, state.Vy, state.RemY));
                    if (before == LocoState.Airborne)
                    {
                        landings++;
                    }
                }
                else if (before == LocoState.Grounded)
                {
                    jumps++;
                }
            }
        }

        // The generator really exercises the code under test.
        Assert.True(steps > 200_000);
        Assert.True(landings > 1000);
        Assert.True(jumps > 1000);
    }

    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void Fuzz_AFixedSeed_ReplaysBitIdentically()
    {
        static ulong Run()
        {
            var rng = new Pcg32(99UL, 3UL);
            var rig = new Rig();
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < 2000; i++)
            {
                uint r = rng.Next32();
                rig.Tick(new CarrierInput
                {
                    MoveY = (r & 1) != 0 ? Full : 0, LookYaw = (ushort)(r >> 16), JumpPressed = ((r >> 2) & 7) == 0,
                    ExtraJumps = (int)((r >> 5) & 1), SpeedPermille = 1000, SprintHeld = (r & 8) != 0,
                });
                h = (h ^ (ulong)(uint)rig.State.Y) * 1099511628211UL;
                h = (h ^ (ulong)(uint)rig.State.Vy) * 1099511628211UL;
                h = (h ^ (ulong)(uint)rig.State.RemY) * 1099511628211UL;
                h = (h ^ (ulong)(uint)rig.State.X) * 1099511628211UL;
            }

            return h;
        }

        Assert.Equal(Run(), Run());
    }

    // ------------------------------------------------------------------ TD-071(2): numerics counters reach the scratch and the flush

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void Counters_AFixDivZeroInsideTheStep_IsCountedInTheScratch_AndFlushedToTheWorldCounter()
    {
        // airControlPm 0 makes F2's air-control ceiling divide by zero while airborne (it used to vanish into a discarded sink).
        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 5000, Loco = LocoState.Airborne } };
        rig.Settings = rig.Settings with { AirControlPm = 0 };
        var counters = new SimCounters();

        rig.Tick(Held(my: Full));

        Assert.True(rig.Scratch.FixDivZero > 0);
        Assert.Equal(0, rig.Scratch.FixClamped);
        int counted = rig.Scratch.FixDivZero;

        CarrierCounterFlush.Flush(counters, ref rig.Scratch);

        Assert.Equal(counted, (int)counters[CounterId.FixDivZero]);
        Assert.Equal(0L, counters[CounterId.FixClamped]);
        Assert.Equal(0, rig.Scratch.FixDivZero);
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void Counters_FixClampedIsFlushed_AndAWellFormedStepCountsNothing()
    {
        var rig = new Rig();
        var counters = new SimCounters();
        rig.Tick(50, Held(my: Full));
        rig.Tick(Press());
        rig.Tick(30, Held());

        Assert.Equal((0, 0), (rig.Scratch.FixDivZero, rig.Scratch.FixClamped));

        rig.Scratch.FixClamped = 3;
        rig.Scratch.FixDivZero = 2;
        CarrierCounterFlush.Flush(counters, ref rig.Scratch);
        CarrierCounterFlush.Flush(counters, ref rig.Scratch);

        Assert.Equal((3L, 2L), (counters[CounterId.FixClamped], counters[CounterId.FixDivZero]));
        Assert.Equal((0, 0), (rig.Scratch.FixDivZero, rig.Scratch.FixClamped));
    }

    [Fact]
    [Trait("Id", "PCM-F2-1")]
    public void Counters_ClearCountersZeroesTheNumericsCounters_AndTheCountSaturates()
    {
        var scratch = new CarrierScratch(64) { FixDivZero = 5, FixClamped = 6 };
        scratch.Clear();
        Assert.Equal((0, 0), (scratch.FixDivZero, scratch.FixClamped));

        var rig = new Rig { State = new CarrierState { X = 64_000, Z = 64_000, Y = Ground + 5000, Loco = LocoState.Airborne } };
        rig.Settings = rig.Settings with { AirControlPm = 0 };
        rig.Scratch.FixDivZero = int.MaxValue;

        rig.Tick(Held(my: Full));

        Assert.Equal(int.MaxValue, rig.Scratch.FixDivZero);
    }
}

/// <summary>Zero-allocation guardrail for the jump and gravity phase of <see cref="OwnedCarrier.Step"/> (story 006), in the non-parallel collection.</summary>
[Collection(NonParallelCollection.Name)]
public class JumpAllocationTests
{
    [Fact]
    [Trait("Id", "PCM-F3-1")]
    public void Step_10000JumpingCalls_AllocateNothing()
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
        _ = AllocAssert.Zero("10,000 jumping OwnedCarrier.Step calls", () =>
        {
            var state = new CarrierState { X = 64_000, Z = 64_000, Y = WorldTablesFixture.DryHeightMm };
            var scratch = new CarrierScratch(64);
            CarrierEnv env = CarrierEnv.ForM1a(world, settings, new EntityId(5), remotes, supports);
            for (int i = 0; i < 10_000; i++)
            {
                var input = new CarrierInput
                {
                    MoveY = (i & 3) == 0 ? 0 : 32_767,
                    LookYaw = (ushort)(i * 31),
                    SprintHeld = (i & 64) != 0,
                    JumpPressed = i % 7 == 0,
                    ExtraJumps = i & 1,
                    SpeedPermille = 1000,
                    ContextFlags = i % 50 == 0 ? CarrierContextFlags.Panel : CarrierContextFlags.None,
                };
                OwnedCarrier.Step(ref state, in input, in env, ref scratch);
                sink += state.Y + state.Vy + state.RemY;
            }
        });

        Assert.NotEqual(long.MinValue, sink);
    }
}
