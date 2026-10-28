using System.Buffers;
using System.Globalization;
using System.Text;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Hashing;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.PlayerCharacter;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.PlayerCharacter;

/// <summary>
/// Player Character story 010, PCM-P2-1 single-platform (ADR-0013 section 1 and Validation Criteria, ADR-0006 section 4,
/// ADR-0001 section 7; TR-movement-022): the committed 600-tick script <c>tests/unit/player/golden/carrier-600.script.csv</c> run
/// through <see cref="OwnedCarrier.Step"/> over FX-Golden reproduces the committed transform hash chain
/// <c>tests/unit/player/golden/carrier-600.golden.txt</c> on the Linux CI legs (net10.0 and net8.0, Debug and Release). The Windows leg
/// and the blocking PCM-PERF-1 counters are story 028 (M0); here the counters and allocated bytes are only written to the test output.
/// <para>
/// Hash: per tick, SHA-256 (the Sim's <see cref="Sha256"/>) over the 15 transform fields in <c>PlayersStateContributor.WriteCarrier</c>
/// order and widths, written with <see cref="CanonicalWriter"/> (support, push-pair and <c>WadeSlowPm</c> excluded; Evan 2026-10-27
/// 11:09 PM PT, Mail Lead chat: "Use the 15-field hash for player-character 010: everything the carrier writes except support, push-pair
/// and WadeSlowPm"); chain <c>h_t = SHA-256(h_(t-1) || tickHash_t)</c> with <c>h_0</c> = 32 zero bytes.
/// </para>
/// <para>
/// FX-Golden (Mail Lead default, pending Evan) extends the GDD's FX-Dry: 64 x 64 tiles of dry ground at +1000 mm with an FX-Ramp pair
/// in the z 6000-10 000 band (a 1400 mm / 700 permille tile up to a 2400 mm plateau, then a 1404 mm / 702 permille face), an FX-Beach
/// strip falling 1000 mm per tile from z 20 000 into FX-Deep (-2000 mm), and static boxes: a spawn crate the carrier starts inside
/// (a static world's only way to reach depenetration), a 400 mm step, a 401 mm block, a ceiling slab 1.5 m above the ground, a 50 mm
/// thin wall and a pier with a 1200 mm top in the deep water (the haul-out ledge). The map-edge clamp leg stops the carrier at
/// x 127 000 with Vx 0 (<c>MapMaxMm</c> 128 000 minus <see cref="CarrierBounds.InsetMm"/>); no tick samples x = TilesX x S, so it does
/// not exercise ADR-0026's far-edge sampling rule. Settings are the M-1a defaults except <c>buffCapPm</c> 4000: the script's Boost flag
/// sets <c>BuffAddPm</c> 4000, the only input that moves a tick more than <c>subStepMm x maxSubStepsPerTick</c> (1200 mm), so the only
/// way to reach <c>carrierStepClamped</c>.
/// </para>
/// <para>
/// Script: one row per tick (<c>tick,moveX,moveY,lookYaw,flags</c>; flags 1 Sprint, 2 Jump, 4 Aim, 8 Boost; '#' lines name the legs);
/// every tick also has <c>SpeedPermille</c> 1000 and <c>ExtraJumps</c> 1. The remote-capsule and support snapshots go through
/// <see cref="ScriptedRemoteCapsuleSource"/> / <see cref="ScriptedSupportSource"/> with one empty row per tick. Push (F6) and support
/// carry (P7) are out of M-1a scope, so the snapshots cannot change the result: <b>re-record the golden when push lands in M0</b>, and
/// when story 027's movement rulings land.
/// </para>
/// <para>
/// Regenerating the golden (a reviewed golden change, never in CI): run
/// <c>PM_UPDATE_GOLDEN=1 dotnet test tests/PerformativeMail.Sim.Tests --filter-class "*CarrierGoldenHashTests"</c>. The test rewrites
/// <c>carrier-600.golden.txt</c> (LF) and then compares as usual; with <c>CI</c> set it fails instead. Commit the new file with the
/// change that moved it.
/// </para>
/// </summary>
[Trait("Id", "PCM-P2-1")]
public class CarrierGoldenHashTests
{
    internal const int Ticks = 600;
    internal const int BoostAddPm = 4000;
    private const string UpdateVariable = "PM_UPDATE_GOLDEN";
    private const string CiVariable = "CI";
    private const int Radius = 300;

    // FX-Golden geometry the assertions name (mm).
    private const int MapMaxMm = WorldTablesFixture.FlatTiles * WorldConstants.TileSizeMm;
    private const int BlockMaxX = 115_000;        // 401 mm block, x 113 000-115 000
    private const int CeilingBottomMm = 2500;     // slab bottom, 1500 mm above the +1000 ground
    private const int ThinWallMinX = 108_000;     // 50 mm wall, x 108 000-108 050, z 12 000-16 000
    private const int PierTopMm = 1200;
    private const int RampMinX = 94_000;          // 700 permille tile, x 94 000-96 000 (rising toward -X)
    private const int RampMaxX = 96_000;
    private const int SteepMinX = 88_000;         // 702 permille tile, x 88 000-90 000
    private const int SteepMaxX = 90_000;

    // The scripted ticks of the paths that have no counter or flag (read off the run; Mail Lead default, pending Evan).
    private const int MapClampTick = 14;          // walking +X reaches x 127 000 and is held there
    private const int BlockTick = 85;             // sprinting -X into the 401 mm block stops at its face
    private const int CoyoteTick = 133;           // a press one tick after walking off the block top
    private const int CeilingTick = 214;          // a jump under the slab meets it on the launch tick
    private const int ThinWallTick = 344;         // the boosted sprint meets the thin wall

    /// <summary>The script's flags column.</summary>
    [Flags]
    internal enum ScriptFlags
    {
        None = 0,
        Sprint = 1,
        Jump = 2,
        Aim = 4,
        Boost = 8,
    }

    /// <summary>One script row: the tick (1-based) and its input.</summary>
    internal readonly record struct ScriptRow(int Tick, int MoveX, int MoveY, ushort LookYaw, ScriptFlags Flags)
    {
        public bool HasMove => MoveX != 0 || MoveY != 0;

        public bool Jump => (Flags & ScriptFlags.Jump) != 0;

        public CarrierInput ToInput() => new()
        {
            MoveX = MoveX,
            MoveY = MoveY,
            LookYaw = LookYaw,
            SprintHeld = (Flags & ScriptFlags.Sprint) != 0,
            JumpPressed = Jump,
            Aiming = (Flags & ScriptFlags.Aim) != 0,
            BuffAddPm = (Flags & ScriptFlags.Boost) != 0 ? BoostAddPm : 0,
            SpeedPermille = 1000,
            ExtraJumps = 1,
        };
    }

    /// <summary>A run: <c>States[t]</c> after tick t (<c>States[0]</c> is the spawn), each tick's probe counters, and the chain.</summary>
    internal sealed class RunResult
    {
        public CarrierState[] States { get; } = new CarrierState[Ticks + 1];

        public (int SubSteps, int Slides, int StepClamped, int Depenetrations)[] Counters { get; } = new (int, int, int, int)[Ticks + 1];

        public Hash256[] Chain { get; } = new Hash256[Ticks + 1];

        public SimCounters Flushed { get; } = new();

        public long AllocatedAfterWarmUp { get; set; }

        public Hash256 Final => Chain[Ticks];
    }

    // ------------------------------------------------------------------ PCM-P2-1 golden

    [Fact]
    public void GoldenChain_OfThe600TickScript_EqualsTheCommittedGolden()
    {
        // Arrange
        string path = Path.Combine(GoldenDirectory(), "carrier-600.golden.txt");
        ScriptRow[] script = LoadScript();

        // Act
        RunResult run = Run(script);
        string actual = GoldenText(run);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(CiVariable)), $"{UpdateVariable} must never run in CI (CI is set).");
            File.WriteAllText(path, actual, new UTF8Encoding(false));
        }

        // Advisory (story 028 makes these blocking at M0): the probe counters and the bytes allocated by ticks 301-600.
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"PCM-PERF-1 (advisory): carrierSubSteps max {run.Flushed[CounterId.CarrierSubSteps]}, carrierSlideIterations max "
            + $"{run.Flushed[CounterId.CarrierSlideIterations]}, carrierCandidateOverflow {run.Flushed[CounterId.CarrierCandidateOverflow]}, "
            + $"carrierStepClamped {run.Flushed[CounterId.CarrierStepClamped]}, carrierDepenetrations {run.Flushed[CounterId.CarrierDepenetrations]}; "
            + $"allocated by Step after 300 warm-up ticks {run.AllocatedAfterWarmUp} B ({BuildConfig})");

        // Assert
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }

    [Fact]
    public void TickHash_CoversExactlyTheFirst15CarrierFieldsOfThePlayersDump()
    {
        // Arrange: every transform field distinct, and the excluded fields set too (they must not reach the hash).
        var c = new CarrierState
        {
            X = -1, Y = 2, Z = 0x0304_0506, RemX = -29, RemZ = 17, RemY = 59, Vx = -7500, Vz = 37_500, Vy = -20_000,
            BodyYaw = 0xBEEF, Loco = LocoState.Swimming, Flags = CarrierFlags.Sprinting | CarrierFlags.Wading,
            CoyoteLeft = 3, JumpBufferLeft = 4, HaulOutTicks = 14,
            SupportId = 0xFEED, SupportLocalX = 9, SupportYawRef = 11, WadeSlowPm = 133,
        };
        c.Pair[2].OverlapTicks = 7;
        var rig = PlayersRig.Create();
        rig.JoinAndTick(0);
        rig.Record(0).Carrier = c;

        // Act: the Players section payload; one present player puts its carrier at byte 27 (count 4, slot 1, id 4, life 1,
        // three deadlines 12, teleportSeq 1, look yaw 2, look pitch 2).
        var output = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(output, new CanonicalBuffer());
        w.BeginSection(SectionIds.Players);
        new PlayersStateContributor(rig.Players).Write(ref w);
        w.EndSection();
        byte[] carrier = output.WrittenSpan.Slice(6 + 27, TransformHasher.PayloadLength).ToArray();
        byte[] payload = new TransformHasher().Payload(in c).ToArray();

        // Assert
        Assert.Equal(TransformHasher.PayloadLength, payload.Length);
        Assert.Equal(carrier, payload);
    }

    // ------------------------------------------------------------------ script coverage

    [Fact]
    public void Script_ExercisesEverySolverPathOfStories005To009()
    {
        // Arrange
        ScriptRow[] script = LoadScript();
        WorldTables world = FxGolden();
        PlayerSettings settings = Settings();
        int launchTick1 = CarrierJump.LaunchSpeed(settings) - CarrierGravity.PerTickMmS(settings);   // Vy after a ground or coyote launch tick

        // Act
        RunResult run = Run(script);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string path in new[]
        {
            "walk", "sprint", "stop", "180-degree turn", "plain jump", "buffered jump", "coyote jump", "extra jump", "landing",
            "700 permille ramp climb", "702 permille slide", "400 mm step-up", "401 mm block", "ceiling hit", "thin-wall tunnel attempt",
            "wading", "swim entry", "swim exit", "haul-out", "map-edge clamp", "depenetration", "step clamp", "sub-steps > 1",
            "slide iterations > 1",
        })
        {
            counts[path] = 0;
        }

        void Count(string path, bool hit)
        {
            if (hit)
            {
                counts[path]++;
            }
        }

        for (int t = 1; t <= Ticks; t++)
        {
            CarrierState s0 = run.States[t - 1];
            CarrierState s1 = run.States[t];
            ScriptRow row = script[t - 1];
            var c = run.Counters[t];
            bool grounded = s0.Loco == LocoState.Grounded && s1.Loco == LocoState.Grounded;
            Count("walk", grounded && row.HasMove && !Has(s1, CarrierFlags.Sprinting) && Speed2(s1) > 0);
            Count("sprint", Has(s1, CarrierFlags.Sprinting));
            Count("stop", grounded && !row.HasMove && Speed2(s1) < Speed2(s0));
            Count("180-degree turn", t > 1 && script[t - 2].HasMove && row.HasMove && Reverses(script[t - 2], row, in s0));
            Count("plain jump", s0.Loco == LocoState.Grounded && s1.Loco == LocoState.Airborne && row.Jump && s1.Vy == launchTick1);
            Count("buffered jump", !row.Jump && s0.JumpBufferLeft > 0 && s0.Vy <= 0 && s1.Vy == launchTick1);
            Count("coyote jump", s0.Loco == LocoState.Airborne && s0.CoyoteLeft > 0 && row.Jump && s1.Vy == launchTick1 && !Has(s1, CarrierFlags.ExtraJumpUsed));
            Count("extra jump", !Has(s0, CarrierFlags.ExtraJumpUsed) && Has(s1, CarrierFlags.ExtraJumpUsed));
            Count("landing", s0.Loco == LocoState.Airborne && s1.Loco == LocoState.Grounded);
            Count("700 permille ramp climb", grounded && s1.X > RampMinX && s1.X < RampMaxX && s1.Y > s0.Y);
            Count("702 permille slide", s1.Loco == LocoState.Airborne && s1.X > SteepMinX && s1.X < SteepMaxX
                && s1.Y == world.TerrainHeightMm(s1.X, s1.Z) && s1.Y < s0.Y
                && !row.HasMove && s1.X != s0.X && c.Slides > 0);   // the downhill run moves x with no move input
            Count("400 mm step-up", grounded && s1.Y - s0.Y == settings.StepUpMm);
            Count("401 mm block", grounded && row.HasMove && s1.X == BlockMaxX + Radius && s1.Y == WorldTablesFixture.DryHeightMm);
            Count("ceiling hit", s1.Loco == LocoState.Airborne && s1.Vy == 0 && s1.Y + settings.CapsuleHeightMm == CeilingBottomMm);
            Count("thin-wall tunnel attempt", c.StepClamped > 0 && s1.X == ThinWallMinX - Radius);
            Count("wading", Has(s1, CarrierFlags.Wading));
            Count("swim entry", s0.Loco != LocoState.Swimming && s1.Loco == LocoState.Swimming);
            Count("swim exit", s0.Loco == LocoState.Swimming && s1.Loco == LocoState.Grounded && s0.HaulOutTicks == 0);
            Count("haul-out", s1.HaulOutTicks > 0);
            Count("map-edge clamp", row.HasMove && s1.X == MapMaxMm - CarrierBounds.InsetMm && s1.Vx == 0);
            Count("depenetration", c.Depenetrations > 0);
            Count("step clamp", c.StepClamped > 0);
            Count("sub-steps > 1", c.SubSteps > 1);
            Count("slide iterations > 1", c.Slides > 1);
        }

        // Assert: no path count is zero.
        string[] missing = counts.Where(p => p.Value == 0).Select(p => p.Key).ToArray();
        Assert.True(missing.Length == 0, "Paths the golden script no longer exercises: " + string.Join(", ", missing));

        // Assert: the named ticks of the paths with no counter or flag.
        CarrierState clamp = run.States[MapClampTick];
        Assert.True(run.States[MapClampTick - 1].X < clamp.X, "map clamp: the carrier was still approaching the edge");
        Assert.Equal((MapMaxMm - CarrierBounds.InsetMm, 0, 0), (clamp.X, clamp.Vx, clamp.RemX));

        CarrierState block = run.States[BlockTick];
        Assert.Equal((BlockMaxX + Radius, WorldTablesFixture.DryHeightMm, LocoState.Grounded, 0), (block.X, block.Y, block.Loco, block.Vx));

        CarrierState beforeCoyote = run.States[CoyoteTick - 1];
        CarrierState coyote = run.States[CoyoteTick];
        Assert.True(script[CoyoteTick - 1].Jump);
        Assert.Equal((LocoState.Airborne, 1401), (beforeCoyote.Loco, beforeCoyote.Y));
        Assert.True(beforeCoyote.CoyoteLeft > 0);
        Assert.Equal((launchTick1, CarrierFlags.None), (coyote.Vy, coyote.Flags & CarrierFlags.ExtraJumpUsed));

        CarrierState ceiling = run.States[CeilingTick];
        Assert.True(script[CeilingTick - 1].Jump);
        Assert.Equal((CeilingBottomMm - settings.CapsuleHeightMm, 0, LocoState.Airborne), (ceiling.Y, ceiling.Vy, ceiling.Loco));
        Assert.True(run.States[CeilingTick + 1].Vy <= 0, "ceiling: Vy after the contact tick");

        Assert.Equal(ThinWallMinX - Radius, run.States[ThinWallTick].X);
        Assert.True(run.Counters[ThinWallTick].StepClamped > 0, "thin wall: the tick that meets it moves more than 1200 mm");
        Assert.All(run.States.Where(s => s.Z is >= 12_000 and <= 16_000), s => Assert.True(s.X <= ThinWallMinX - Radius, $"tunnelled to x {s.X}"));

        Assert.Contains(run.States, s => s.Loco == LocoState.Grounded && s.Y == PierTopMm);   // the haul-out lift stands on the pier
    }

    // ------------------------------------------------------------------ mutation check

    /// <summary>
    /// Single-unit perturbations of inputs the solver reads on that tick (input clamping, mid-air presses with no extra jump left and
    /// look yaw without move input are not read, so "any input" is not claimed). A 1 BAM look-yaw change while walking without aim is
    /// absorbed by the heading quantisation, so the look-yaw case uses an aiming tick, where the body yaw is the look yaw. Ticks are
    /// Mail Lead defaults, pending Evan.
    /// </summary>
    [Theory]
    [InlineData(Mutation.MoveX, 10)]      // walking
    [InlineData(Mutation.LookYaw, 470)]   // aiming, strafing on the pier
    [InlineData(Mutation.Jump, 45)]       // grounded, sprinting
    public void Mutation_ASingleUnitPerturbationOfAReadInput_ChangesTheFinalHash(Mutation mutation, int tick)
    {
        // Arrange: the baseline run must read the perturbed input on that tick, so a script edit that moves the tick fails here
        ScriptRow[] script = LoadScript();
        RunResult baselineRun = Run(script);
        Hash256 baseline = baselineRun.Final;
        ScriptRow row = script[tick - 1];
        bool grounded = baselineRun.States[tick - 1].Loco == LocoState.Grounded;
        switch (mutation)
        {
            case Mutation.MoveX:
                Assert.True(row.HasMove && (row.Flags & ScriptFlags.Sprint) == 0 && grounded, $"tick {tick}: not walking ({row}, {baselineRun.States[tick - 1].Loco})");
                script[tick - 1] = row with { MoveX = row.MoveX + 1 };
                break;
            case Mutation.LookYaw:
                Assert.True(row.HasMove && (row.Flags & ScriptFlags.Aim) != 0, $"tick {tick}: not aiming with move input ({row})");
                script[tick - 1] = row with { LookYaw = (ushort)(row.LookYaw + 1) };
                break;
            default:
                Assert.True(grounded && (row.Flags & ScriptFlags.Jump) == 0, $"tick {tick}: not grounded without a press ({row}, {baselineRun.States[tick - 1].Loco})");
                script[tick - 1] = row with { Flags = row.Flags | ScriptFlags.Jump };
                break;
        }

        // Act
        Hash256 mutated = Run(script).Final;

        // Assert
        Assert.NotEqual(baseline, mutated);
    }

    /// <summary>The input a mutation-check row perturbs by one unit.</summary>
    public enum Mutation
    {
        MoveX,
        LookYaw,
        Jump,
    }

    // ------------------------------------------------------------------ FX-Golden

    /// <summary>The build the advisory figures come from: only the Release legs' figures count (TD-008: the unoptimised Sim allocates).</summary>
#if DEBUG
    internal const string BuildConfig = "Debug, unoptimised: not representative (TD-008)";
#else
    internal const string BuildConfig = "Release";
#endif

    internal static PlayerSettings Settings() => PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player) with { BuffCapPm = BoostAddPm };

    internal static CarrierState Spawn() => new() { X = 125_000, Y = WorldTablesFixture.DryHeightMm, Z = 6000, BodyYaw = 49_152, Loco = LocoState.Grounded };

    internal static WorldTables FxGolden()
    {
        const int Tiles = WorldTablesFixture.FlatTiles;
        const int Stride = Tiles + 1;
        var corners = new int[Stride * Stride];
        for (int cz = 0; cz <= Tiles; cz++)
        {
            for (int cx = 0; cx <= Tiles; cx++)
            {
                corners[(cz * Stride) + cx] = CornerHeight(cx, cz);
            }
        }

        var b = new WorldTablesBuilder();
        b.SetCorners(Tiles, Tiles, corners);
        b.AddSolid(EntityId.None, Box(124_700, 1000, 5700, 125_300, 1300, 6300), SolidKind.Prop);                    // spawn crate
        b.AddSolid(EntityId.None, Box(118_000, 1000, 7000, 120_000, 1400, 9000), SolidKind.Prop);                    // 400 mm step
        b.AddSolid(EntityId.None, Box(113_000, 1000, 7000, BlockMaxX, 1401, 9000), SolidKind.Prop);                  // 401 mm block
        b.AddSolid(EntityId.None, Box(100_000, CeilingBottomMm, 7000, 103_000, 2700, 9000), SolidKind.Roof);         // ceiling slab
        b.AddSolid(EntityId.None, Box(ThinWallMinX, 1000, 12_000, ThinWallMinX + 50, 4000, 16_000), SolidKind.Prop); // thin wall
        b.AddSolid(EntityId.None, Box(108_000, -2000, 19_000, 112_000, PierTopMm, 30_000), SolidKind.Prop);          // pier
        return b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    /// <summary>FX-Dry at +1000; the FX-Ramp pair in corner rows 3-5 (2400 from x 94 000, 3804 from x 88 000); FX-Beach / FX-Deep from corner row 10.</summary>
    private static int CornerHeight(int cx, int cz)
    {
        if (cz >= 10)
        {
            return Math.Max(WorldTablesFixture.DeepHeightMm, WorldTablesFixture.DryHeightMm - (1000 * (cz - 10)));
        }

        if (cz is >= 3 and <= 5)
        {
            return cx >= 48 ? WorldTablesFixture.DryHeightMm : cx >= 45 ? 2400 : 3804;
        }

        return WorldTablesFixture.DryHeightMm;
    }

    private static AabbMm Box(int x0, int y0, int z0, int x1, int y1, int z1) => new(new Vec3Mm(x0, y0, z0), new Vec3Mm(x1, y1, z1));

    // ------------------------------------------------------------------ run and hash

    internal static RunResult Run(ScriptRow[] script)
    {
        WorldTables world = FxGolden();
        PlayerSettings settings = Settings();
        var remotes = new ScriptedRemoteCapsuleSource(EmptyRows<RemoteCapsule>());
        var supports = new ScriptedSupportSource(EmptyRows<VehiclePose>(), EmptyRows<ScriptedBelt>());
        var env = new CarrierEnv(world, remotes, supports, settings, new EntityId(1));
        var scratch = new CarrierScratch(settings.MaxCarrierCandidates);
        var hasher = new TransformHasher();
        var run = new RunResult();
        CarrierState state = Spawn();
        run.States[0] = state;
        for (int t = 1; t <= Ticks; t++)
        {
            CarrierInput input = script[t - 1].ToInput();
            remotes.SetTick(t);
            supports.SetTick(t);
            long before = GC.GetAllocatedBytesForCurrentThread();
            OwnedCarrier.Step(ref state, in input, in env, ref scratch);
            if (t > 300)
            {
                run.AllocatedAfterWarmUp += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            run.Counters[t] = (scratch.SubStepsMaxThisTick, scratch.SlideIterationsMax, scratch.StepClamped, scratch.Depenetrations);
            CarrierCounterFlush.Flush(run.Flushed, ref scratch);
            run.States[t] = state;
            run.Chain[t] = hasher.Chain(run.Chain[t - 1], in state);
        }

        return run;
    }

    private static T[][] EmptyRows<T>()
    {
        var rows = new T[Ticks + 1][];
        Array.Fill(rows, Array.Empty<T>());
        return rows;
    }

    /// <summary>The per-tick transform hash and the chain step (one instance per run; not thread-safe).</summary>
    internal sealed class TransformHasher
    {
        /// <summary>The transform payload: 9 x i32, u16, u8, u16, 3 x u8.</summary>
        public const int PayloadLength = 44;

        private readonly ArrayBufferWriter<byte> _output = new();
        private readonly CanonicalBuffer _buffer = new();
        private readonly Sha256 _sha = new();
        private readonly byte[] _link = new byte[2 * Hash256.Length];

        /// <summary>The 15 transform fields in <c>PlayersStateContributor.WriteCarrier</c> order and widths, little-endian.</summary>
        public ReadOnlySpan<byte> Payload(in CarrierState c)
        {
            _output.ResetWrittenCount();
            var w = new CanonicalWriter(_output, _buffer);
            w.BeginSection(SectionIds.Players);
            w.WriteI32(c.X);
            w.WriteI32(c.Y);
            w.WriteI32(c.Z);
            w.WriteI32(c.RemX);
            w.WriteI32(c.RemZ);
            w.WriteI32(c.RemY);
            w.WriteI32(c.Vx);
            w.WriteI32(c.Vz);
            w.WriteI32(c.Vy);
            w.WriteU16(c.BodyYaw);
            w.WriteU8((byte)c.Loco);
            w.WriteU16((ushort)c.Flags);
            w.WriteU8(c.CoyoteLeft);
            w.WriteU8(c.JumpBufferLeft);
            w.WriteU8(c.HaulOutTicks);
            w.EndSection();
            return _output.WrittenSpan[6..];   // drop the section frame (id u16, length u32)
        }

        /// <summary><c>h_t = SHA-256(h_(t-1) || SHA-256(payload_t))</c>.</summary>
        public Hash256 Chain(Hash256 previous, in CarrierState c)
        {
            _sha.Reset();
            _sha.Update(Payload(in c));
            Hash256 tick = _sha.Finish();
            previous.CopyTo(_link);
            tick.CopyTo(_link.AsSpan(Hash256.Length));
            _sha.Reset();
            _sha.Update(_link);
            return _sha.Finish();
        }
    }

    private static string GoldenText(RunResult run)
    {
        var sb = new StringBuilder();
        sb.Append("# carrier-600 golden (player-character story 010, PCM-P2-1): the transform hash chain of carrier-600.script.csv over FX-Golden.\n");
        sb.Append("# h_0 = 32 zero bytes; h_t = SHA-256(h_(t-1) || SHA-256(X Y Z RemX RemZ RemY Vx Vz Vy i32, BodyYaw u16, Loco u8, Flags u16,\n");
        sb.Append("# CoyoteLeft JumpBufferLeft HaulOutTicks u8, little-endian)). Regenerate with PM_UPDATE_GOLDEN=1, never in CI (CarrierGoldenHashTests).\n");
        for (int t = 1; t <= Ticks; t = t == 1 ? 50 : t + 50)
        {
            sb.Append(CultureInfo.InvariantCulture, $"chain {t} {run.Chain[t].ToHexString()}\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"final {run.Final.ToHexString()}\n");
        CarrierState s = run.States[Ticks];
        sb.Append(CultureInfo.InvariantCulture, $"state {Ticks} X={s.X} Y={s.Y} Z={s.Z} RemX={s.RemX} RemZ={s.RemZ} RemY={s.RemY} Vx={s.Vx} Vz={s.Vz} Vy={s.Vy} ");
        sb.Append(CultureInfo.InvariantCulture, $"BodyYaw={s.BodyYaw} Loco={s.Loco} Flags={s.Flags} CoyoteLeft={s.CoyoteLeft} JumpBufferLeft={s.JumpBufferLeft} HaulOutTicks={s.HaulOutTicks}\n");
        return sb.ToString();
    }

    private static bool Has(in CarrierState s, CarrierFlags flag) => (s.Flags & flag) != 0;

    private static long Speed2(in CarrierState s) => ((long)s.Vx * s.Vx) + ((long)s.Vz * s.Vz);

    /// <summary>The world move direction flips by 180 degrees while the carrier still moves the old way.</summary>
    private static bool Reverses(ScriptRow previous, ScriptRow row, in CarrierState s)
    {
        CarrierHeading.ToWorld(previous.MoveX, previous.MoveY, previous.LookYaw, out long px, out long pz);
        CarrierHeading.ToWorld(row.MoveX, row.MoveY, row.LookYaw, out long wx, out long wz);
        return (px * wz) - (pz * wx) == 0 && (px * wx) + (pz * wz) < 0 && ((s.Vx * wx) + (s.Vz * wz)) < 0;
    }

    // ------------------------------------------------------------------ files

    internal static ScriptRow[] LoadScript()
    {
        var rows = new List<ScriptRow>(Ticks);
        foreach (string line in File.ReadLines(Path.Combine(GoldenDirectory(), "carrier-600.script.csv")))
        {
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("tick,", StringComparison.Ordinal))
            {
                continue;
            }

            string[] f = line.Split(',');
            rows.Add(new ScriptRow(Parse(f[0]), Parse(f[1]), Parse(f[2]), checked((ushort)Parse(f[3])), (ScriptFlags)Parse(f[4])));
        }

        Assert.Equal(Enumerable.Range(1, Ticks), rows.Select(r => r.Tick));
        return rows.ToArray();
    }

    private static int Parse(string s) => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static string GoldenDirectory() => Path.Combine(FindRepoRoot(), "tests", "unit", "player", "golden");

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PerformativeMail.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("PerformativeMail.sln not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// PCM-PERF-1 microbenchmark, ADVISORY (story 010; TR-movement-021 has no ADR coverage): the worst-case fixture, one carrier taking four
/// sub-steps (a boosted 1250 mm tick, clamped to 1200) among 64 static candidates (an 8 x 8 grid of slabs just above the head, inside the
/// swept region but clear of the capsule), stepped 10 000 times from the same start; the p99 per call is written to the output against
/// the 0.10 ms target and never asserted until the M1 reference machine exists (the blocking form is story 028). The 8-player /
/// 2000-anchor fixture is story 018/020's. The fixture shape is a Mail Lead default, pending Evan.
/// </summary>
[Collection(NonParallelCollection.Name)]
[Trait("Category", "Perf")]
[Trait("Id", "PCM-PERF-1")]
public class CarrierStepPerfTests
{
    private const int Calls = 10_000;

    [Fact]
    public void CarrierStep_WorstCaseFixture_P99PerCall_IsReported_Advisory()
    {
        // Arrange
        var b = new WorldTablesBuilder();
        var corners = new int[(WorldTablesFixture.FlatTiles + 1) * (WorldTablesFixture.FlatTiles + 1)];
        Array.Fill(corners, WorldTablesFixture.DryHeightMm);
        b.SetCorners(WorldTablesFixture.FlatTiles, WorldTablesFixture.FlatTiles, corners);
        for (int i = 0; i < 64; i++)
        {
            int x = 62_600 + (400 * (i % 8));
            int z = 62_600 + (400 * (i / 8));
            b.AddSolid(EntityId.None, new AabbMm(new Vec3Mm(x, 2450, z), new Vec3Mm(x + 100, 2500, z + 100)), SolidKind.Prop);
        }

        WorldTables world = b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
        PlayerSettings settings = CarrierGoldenHashTests.Settings();
        var remotes = new EmptyRemoteCapsuleSource();
        var supports = new EmptySupportTransformSource();
        CarrierEnv env = CarrierEnv.ForM1a(world, settings, new EntityId(1), remotes, supports);
        var scratch = new CarrierScratch(settings.MaxCarrierCandidates);
        var start = new CarrierState { X = 64_000, Y = WorldTablesFixture.DryHeightMm, Z = 64_000, Vx = 37_500, BodyYaw = 49_152, Loco = LocoState.Grounded };
        var input = new CarrierInput { MoveY = 32_767, LookYaw = 49_152, SprintHeld = true, BuffAddPm = CarrierGoldenHashTests.BoostAddPm, SpeedPermille = 1000 };
        var samples = new long[Calls];
        CarrierState state = start;
        for (int i = 0; i < 1000; i++)
        {
            state = start;
            scratch.Clear();
            OwnedCarrier.Step(ref state, in input, in env, ref scratch);
        }

        // Act
        for (int i = 0; i < Calls; i++)
        {
            state = start;
            scratch.Clear();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            OwnedCarrier.Step(ref state, in input, in env, ref scratch);
            samples[i] = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        }

        // Report (advisory: no assertion on time or on the fixture shape; both are in the output).
        Array.Sort(samples);
        double p99Ms = samples[(Calls * 99 / 100) - 1] * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"PCM-PERF-1 (advisory) CarrierStep worst case, {Calls} calls, {CarrierGoldenHashTests.BuildConfig}: p99 {p99Ms:F4} ms per call (target 0.10 ms); "
            + $"sub-steps {scratch.SubStepsMaxThisTick}, candidates {scratch.SolidCount}, step clamped {scratch.StepClamped}");
    }
}
