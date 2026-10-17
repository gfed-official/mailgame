using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.PlayerCharacter;

/// <summary>
/// Player Character story 008: static colliders (GDD P4 colliders; ADR-0013 section 4; ADR-0026 sections 4-5; TR-movement-004/006):
/// the once-per-tick candidate gather, cylinder-vs-box slides that never tunnel, blocked tiles as infinite-height boxes, and walkable box
/// tops in the one support routine; part B: step-up, snap-down off ledges and coyote, tick-start depenetration, ceilings and
/// corners. Worlds are flat 10 x 10 tiles (x/z 0..20000) built with <see cref="WorldTablesBuilder"/>.
/// </summary>
public class ColliderTests
{
    private const ushort PlusX = 49152;        // forward = (+1, 0)
    private const ushort Diagonal = 40960;     // forward = (+1, +1)
    private const int Full = 32767;
    private const int R = 300;
    private const int H = 1400;

    private static AabbMm Box(int x0, int y0, int z0, int x1, int y1, int z1) => new(new Vec3Mm(x0, y0, z0), new Vec3Mm(x1, y1, z1));

    private static WorldTables Flat(Action<WorldTablesBuilder>? extra = null, int heightMm = 0)
    {
        const int Tiles = 10;
        var corners = new int[(Tiles + 1) * (Tiles + 1)];
        Array.Fill(corners, heightMm);
        var b = new WorldTablesBuilder();
        b.SetCorners(Tiles, Tiles, corners);
        extra?.Invoke(b);
        return b.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    private static WorldTables WithSolids(params AabbMm[] boxes) => Flat(b =>
    {
        foreach (AabbMm box in boxes)
        {
            b.AddSolid(EntityId.None, box, SolidKind.Prop);
        }
    });

    private sealed class Rig
    {
        private readonly EmptyRemoteCapsuleSource _remotes = new();
        private readonly EmptySupportTransformSource _supports = new();

        public Rig(WorldTables world, int x, int z, int y, LocoState loco = LocoState.Grounded, int limit = 64)
        {
            World = world;
            State = new CarrierState { X = x, Z = z, Y = y, Loco = loco };
            Scratch = new CarrierScratch(limit);
            Settings = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player) with { MaxCarrierCandidates = limit };
        }

        public WorldTables World { get; }

        public CarrierState State;

        public CarrierScratch Scratch;

        public PlayerSettings Settings { get; }

        public CarrierEnv Env => CarrierEnv.ForM1a(World, Settings, new EntityId(5), _remotes, _supports);

        public void Tick(in CarrierInput input)
        {
            CarrierEnv env = Env;
            OwnedCarrier.Step(ref State, in input, in env, ref Scratch);
        }

        /// <summary>One sweep tick with an explicit displacement: the gather, then <see cref="CarrierSweep.Run"/> (no phases 1 to 4).</summary>
        public void Run(int dx, int dz, int dy = 0)
        {
            CarrierEnv env = Env;
            Span<long> counts = stackalloc long[SimCounterSink.RequiredLength];
            var sink = new SimCounterSink(counts);
            var plan = new GravityPlan { Dy = dy };
            CarrierColliders.Gather(ref Scratch, in env, in State);
            CarrierSweep.Run(ref State, in env, ref Scratch, ref sink, dx, dz, ref plan);
            RunFixDivZero = sink.Get(PrimitiveCounter.FixDivZero);
            RunFixClamped = sink.Get(PrimitiveCounter.FixClamped);
        }

        /// <summary>The numerics counters of the last <see cref="Run"/> sweep (TD-078(9)).</summary>
        public long RunFixDivZero { get; private set; }

        /// <summary>The numerics counters of the last <see cref="Run"/> sweep.</summary>
        public long RunFixClamped { get; private set; }

        public bool OverlapsAny()
        {
            foreach (StaticSolid s in World.StaticSolids)
            {
                if (CylinderVsBox.Overlaps(State.X, State.Y, State.Z, R, H, ColliderBox.From(s.Box)))
                {
                    return true;
                }
            }

            for (int tz = 0; tz < World.TilesZ; tz++)
            {
                for (int tx = 0; tx < World.TilesX; tx++)
                {
                    if ((World.Tile(tx, tz) & TileFlags.Blocked) != 0
                        && CylinderVsBox.Overlaps(State.X, State.Y, State.Z, R, H, ColliderBox.Tile(tx, tz, World.TileSizeMm)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    private static CarrierInput Walk(ushort look, bool sprint = false) =>
        new() { MoveY = Full, LookYaw = look, SprintHeld = sprint, SpeedPermille = 1000 };

    private static CarrierInput Idle() => new() { SpeedPermille = 1000 };

    // ------------------------------------------------------------------ PCM-P4-2a: no tunnelling

    [Fact]
    [Trait("Id", "PCM-P4-2a")]
    public void Run_650mmTowardA100mmWall300mmAhead_StopsTouchingTheNearFace()
    {
        // Arrange: wall x 10000..10100; the carrier's surface is 300 mm short of it.
        var rig = new Rig(WithSolids(Box(10_000, 0, 5_000, 10_100, 3_000, 15_000)), x: 9_400, z: 10_000, y: 0);
        rig.State.Vx = 19_500;

        // Act
        rig.Run(650, 0);

        // Assert
        Assert.Equal(9_700, rig.State.X);
        Assert.Equal(10_000, rig.State.Z);
        Assert.Equal(0, rig.State.Vx);
        Assert.False(rig.OverlapsAny());
        Assert.InRange(rig.Scratch.SlideIterationsMax, 1, 3);
        Assert.True(rig.RunFixDivZero == 0 && rig.RunFixClamped == 0, $"sink {rig.RunFixDivZero}/{rig.RunFixClamped}");
    }

    [Fact]
    [Trait("Id", "PCM-P4-2a")]
    public void Step_SprintingIntoAThinWallFor60Ticks_NeverPassesIt()
    {
        var rig = new Rig(WithSolids(Box(10_000, 0, 5_000, 10_100, 3_000, 15_000)), x: 8_000, z: 10_000, y: 0);
        for (int i = 0; i < 60; i++)
        {
            rig.Tick(Walk(PlusX, sprint: true));
            Assert.True(rig.State.X <= 9_700, $"tick {i}: X {rig.State.X}");
            Assert.InRange(rig.Scratch.SlideIterationsMax, 0, 3);
        }

        Assert.Equal(9_700, rig.State.X);
        Assert.Equal(LocoState.Grounded, rig.State.Loco);
    }

    [Theory]
    [Trait("Id", "PCM-P4-2a")]
    [InlineData(1_300)]
    [InlineData(1_001)]
    [InlineData(1_667)]
    public void Run_A667mmFallOntoA50mmSlab_LandsOnItsTop(int startY)
    {
        var rig = new Rig(WithSolids(Box(8_000, 950, 8_000, 12_000, 1_000, 12_000)), x: 10_000, z: 10_000, y: startY, LocoState.Airborne);
        rig.State.Vy = -20_000;

        rig.Run(0, 0, -667);

        Assert.Equal(startY - 667 <= 1_000 ? 1_000 : startY - 667, rig.State.Y);
        Assert.False(rig.OverlapsAny());
    }

    [Fact]
    [Trait("Id", "PCM-P4-2a")]
    public void Step_FallingAtTerminalSpeedOntoA50mmSlab_LandsOnTopGrounded()
    {
        var rig = new Rig(WithSolids(Box(8_000, 950, 8_000, 12_000, 1_000, 12_000)), x: 10_000, z: 10_000, y: 30_000, LocoState.Airborne);
        rig.State.Vy = -20_000;
        for (int i = 0; i < 120 && rig.State.Loco != LocoState.Grounded; i++)
        {
            rig.Tick(Idle());
            Assert.True(rig.State.Y >= 1_000, $"tick {i}: Y {rig.State.Y}");
        }

        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.Equal(1_000, rig.State.Y);
    }

    /// <summary>
    /// 10 000 seeded trials: a long thin wall (x or z) or a wide thin slab that the carrier starts clear of, plus a random finite thin box
    /// when the start is clear of it; a random displacement of up to 1200 mm per axis, rising or falling (part B added the ceiling slab, kind 3,
    /// and upward velocities). The carrier never ends overlapping a box and never on the far side of the wall, floor slab or ceiling slab.
    /// </summary>
    [Fact]
    [Trait("Id", "PCM-P4-2a")]
    public void Run_10000RandomThinBoxTrials_NeverOverlapAndNeverTunnel()
    {
        var rng = new Pcg32(0x5EED_0008UL, 54UL);
        int Rand(int lo, int hi) => lo + (int)(rng.Next32() % (uint)(hi - lo + 1));
        const int C = 10_000;

        for (int trial = 0; trial < 10_000; trial++)
        {
            int kind = Rand(0, 3);
            int t = Rand(10, 150);
            int side = Rand(0, 1) == 0 ? -1 : 1;
            int gap = Rand(0, 1_200);
            bool airborne = kind >= 2 || Rand(0, 1) == 1;
            int y0 = 0;
            AabbMm main;
            if (kind == 0)
            {
                int near = C + (side * (R + gap));
                main = side > 0 ? Box(near, -500, 2_000, near + t, 6_000, 18_000) : Box(near - t, -500, 2_000, near, 6_000, 18_000);
                y0 = airborne ? Rand(0, 3_000) : 0;
            }
            else if (kind == 1)
            {
                int near = C + (side * (R + gap));
                main = side > 0 ? Box(2_000, -500, near, 18_000, 6_000, near + t) : Box(2_000, -500, near - t, 18_000, 6_000, near);
                y0 = airborne ? Rand(0, 3_000) : 0;
            }
            else if (kind == 2)
            {
                int top = Rand(10, 2_000);
                main = Box(2_000, top - t, 2_000, 18_000, top, 18_000);
                y0 = top + Rand(0, 1_500);
            }
            else
            {
                y0 = Rand(0, 1_500);
                int bottom = y0 + H + Rand(0, 1_200);
                main = Box(2_000, bottom, 2_000, 18_000, bottom + t, 18_000);
            }

            int ex0 = C + Rand(-1_800, 1_800);
            int ez0 = C + Rand(-1_800, 1_800);
            bool thinX = Rand(0, 1) == 0;
            int ey0 = Rand(-500, 2_500);
            AabbMm extra = Box(ex0, ey0, ez0, ex0 + (thinX ? Rand(10, 150) : Rand(10, 1_500)), ey0 + Rand(10, 3_000), ez0 + (thinX ? Rand(10, 1_500) : Rand(10, 150)));
            bool extraClear = !CylinderVsBox.Overlaps(C, y0, C, R, H, ColliderBox.From(extra));
            bool extraYStart = y0 < extra.Max.Y && y0 + H > extra.Min.Y && extra.Max.Y - y0 > 400;   // a wall, not a step-up

            WorldTables world = extraClear ? WithSolids(main, extra) : WithSolids(main);
            var rig = new Rig(world, C, C, y0, airborne ? LocoState.Airborne : LocoState.Grounded);
            Assert.False(rig.OverlapsAny());
            int dx = Rand(-1_200, 1_200);
            int dz = Rand(-1_200, 1_200);
            int dy = airborne ? Rand(-1_200, 1_200) : 0;
            rig.State.Vx = dx * 30;
            rig.State.Vz = dz * 30;

            rig.Run(dx, dz, dy);

            string where = $"trial {trial}: kind {kind} side {side} t {t} d ({dx},{dz},{dy}) -> ({rig.State.X},{rig.State.Y},{rig.State.Z})";
            Assert.False(rig.OverlapsAny(), where);
            Assert.InRange(rig.Scratch.SlideIterationsMax, 0, 3);
            if (kind == 0)
            {
                Assert.True(side > 0 ? rig.State.X <= main.Min.X - R : rig.State.X >= main.Max.X + R, where);
            }
            else if (kind == 1)
            {
                Assert.True(side > 0 ? rig.State.Z <= main.Min.Z - R : rig.State.Z >= main.Max.Z + R, where);
            }
            else if (kind == 2)
            {
                Assert.True(rig.State.Y >= main.Max.Y, where);
            }
            else
            {
                Assert.True(rig.State.Y + H <= main.Min.Y, where);
            }

            // The extra box (when present and a wall at both ends of the move): never crossed while staying in its shadow. Going round
            // a thin box needs the centre to leave its long span by r and come back, which one tick of slides never does.
            bool extraYEnd = rig.State.Y < extra.Max.Y && rig.State.Y + H > extra.Min.Y;
            if (extraClear && extraYStart && extraYEnd)
            {
                bool crossed = thinX
                    ? InSpan(C, rig.State.Z, extra.Min.Z, extra.Max.Z) && Crossed(C, rig.State.X, extra.Min.X, extra.Max.X)
                    : InSpan(C, rig.State.X, extra.Min.X, extra.Max.X) && Crossed(C, rig.State.Z, extra.Min.Z, extra.Max.Z);
                Assert.False(crossed, where + " (extra box tunnelled)");
            }
        }

        static bool InSpan(int a, int b, int min, int max) => a >= min && a <= max && b >= min && b <= max;
        static bool Crossed(int from, int to, int min, int max) => (from < min && to > max) || (from > max && to < min);
    }

    // ------------------------------------------------------------------ Blocked tiles

    [Fact]
    [Trait("Id", "PCM-P4-BlockedTile")]
    public void Step_WalkingIntoABlockedTile_StopsAtItsEdge()
    {
        var rig = new Rig(Flat(b => b.MarkBlocked(5, 5)), x: 8_500, z: 11_000, y: 0);
        for (int i = 0; i < 60; i++)
        {
            rig.Tick(Walk(PlusX, sprint: true));
            Assert.False(rig.OverlapsAny(), $"tick {i}");
        }

        Assert.Equal(10_000 - R, rig.State.X);
        Assert.Equal(11_000, rig.State.Z);
    }

    [Fact]
    [Trait("Id", "PCM-P4-BlockedTile")]
    public void Step_WalkingDiagonallyAlongABlockedTile_SlidesAlongTheEdge()
    {
        var rig = new Rig(Flat(b => b.MarkBlocked(5, 5)), x: 9_600, z: 10_200, y: 0);
        for (int i = 0; i < 90; i++)
        {
            rig.Tick(Walk(Diagonal));
            Assert.False(rig.OverlapsAny(), $"tick {i}");
        }

        Assert.True(rig.State.Z > 12_000 + R, $"Z {rig.State.Z}");   // slid past the tile's far edge
        Assert.True(rig.State.X > 10_000, $"X {rig.State.X}");     // and round it
    }

    [Theory]
    [Trait("Id", "PCM-P4-BlockedTile")]
    [InlineData(0, LocoState.Grounded)]
    [InlineData(50_000, LocoState.Airborne)]
    public void Run_IntoABlockedTileAtAnyHeight_IsStoppedAtTheFace(int y, LocoState loco)
    {
        var rig = new Rig(Flat(b => b.MarkBlocked(5, 5)), x: 9_000, z: 11_000, y: y, loco);

        rig.Run(1_200, 0);

        Assert.Equal(10_000 - R, rig.State.X);
        Assert.False(rig.OverlapsAny());
    }

    // ------------------------------------------------------------------ Candidate gather

    [Fact]
    [Trait("Id", "PCM-P4-Gather")]
    public void Gather_MoreSolidsThanTheLimit_KeepsTheLowestIdsAscendingAndCountsOverflow()
    {
        var boxes = new AabbMm[70];
        for (int i = 0; i < boxes.Length; i++)
        {
            int x = 9_000 + ((69 - i) * 20);   // spatially reversed, so ascending order is not an accident of position
            boxes[i] = Box(x, 2_000, 10_000, x + 10, 2_010, 10_010);
        }

        var rig = new Rig(WithSolids(boxes), x: 10_000, z: 10_000, y: 0);
        CarrierEnv env = rig.Env;

        CarrierColliders.Gather(ref rig.Scratch, in env, in rig.State);

        Assert.Equal(64, rig.Scratch.SolidCount);
        Assert.Equal(1, rig.Scratch.CandidateOverflow);
        for (int i = 0; i < 64; i++)
        {
            Assert.Equal(i, rig.Scratch.Solids[i]);
        }
    }

    [Fact]
    [Trait("Id", "PCM-P4-Gather")]
    public void Gather_ALowerLimit_KeepsThatManyLowestIds()
    {
        var boxes = new AabbMm[10];
        for (int i = 0; i < boxes.Length; i++)
        {
            boxes[i] = Box(9_000 + (i * 20), 0, 10_000, 9_010 + (i * 20), 10, 10_010);
        }

        var rig = new Rig(WithSolids(boxes), x: 10_000, z: 10_000, y: 0, limit: 4);
        CarrierEnv env = rig.Env;

        CarrierColliders.Gather(ref rig.Scratch, in env, in rig.State);

        Assert.Equal(new ushort[] { 0, 1, 2, 3 }, rig.Scratch.Solids.ToArray());
        Assert.True(rig.Scratch.CandidateOverflow >= 1);
    }

    [Fact]
    [Trait("Id", "PCM-P4-Gather")]
    public void Gather_SweptRegion_Is1200PlusRadiusAroundTheFeet()
    {
        var rig = new Rig(
            WithSolids(
                Box(11_500, 0, 10_000, 11_600, 100, 10_100),   // 0: x min on the region edge (+1500): in
                Box(11_501, 0, 10_000, 11_600, 100, 10_100),   // 1: 1 mm beyond: out
                Box(10_000, 0, 8_400, 10_100, 100, 8_500),     // 2: z max at -1500: in
                Box(10_000, 2_601, 10_000, 10_100, 2_700, 10_100), // 3: above head + 1200: out
                Box(10_000, -1_300, 10_000, 10_100, -1_200, 10_100)), // 4: top at feet - 1200: in
            x: 10_000,
            z: 10_000,
            y: 0);
        CarrierEnv env = rig.Env;

        CarrierColliders.Gather(ref rig.Scratch, in env, in rig.State);

        Assert.Equal(new ushort[] { 0, 2, 4 }, rig.Scratch.Solids.ToArray());
        Assert.Equal(0, rig.Scratch.CandidateOverflow);
    }

    [Fact]
    [Trait("Id", "PCM-P4-Gather")]
    public void Step_GathersOncePerTick_FromTheTickStartPosition()
    {
        var boxes = new AabbMm[12];
        for (int i = 0; i < boxes.Length; i++)
        {
            boxes[i] = Box(6_000 + (i * 1_000), 0, 12_000, 6_100 + (i * 1_000), 500, 12_100);
        }

        var rig = new Rig(Flat(b =>
        {
            foreach (AabbMm box in boxes)
            {
                b.AddSolid(EntityId.None, box, SolidKind.Prop);
            }

            b.MarkBlocked(4, 4);
        }), x: 8_000, z: 10_500, y: 0);

        for (int i = 0; i < 40; i++)
        {
            CarrierState start = rig.State;
            var expected = new CarrierScratch(64);
            CarrierEnv env = rig.Env;
            CarrierColliders.Gather(ref expected, in env, in start);

            rig.Tick(Walk(PlusX, sprint: true));

            Assert.Equal(expected.Solids.ToArray(), rig.Scratch.Solids.ToArray());
            Assert.Equal(expected.TileCount, rig.Scratch.TileCount);
        }
    }

    // ------------------------------------------------------------------ Walkable box tops

    [Fact]
    [Trait("Id", "PCM-P4-BoxTop")]
    public void Step_FallingOntoABoxTop_IsGroundedAtTheTopAndStays()
    {
        var rig = new Rig(WithSolids(Box(8_000, 0, 8_000, 12_000, 1_500, 12_000)), x: 10_000, z: 10_000, y: 4_000, LocoState.Airborne);

        for (int i = 0; i < 60; i++)
        {
            rig.Tick(Idle());
        }

        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.Equal(1_500, rig.State.Y);

        for (int i = 0; i < 30; i++)
        {
            rig.Tick(Walk(PlusX));
            Assert.Equal(LocoState.Grounded, rig.State.Loco);
            Assert.Equal(1_500, rig.State.Y);
            if (rig.State.X > 11_500)
            {
                break;
            }
        }
    }

    [Theory]
    [Trait("Id", "PCM-P4-BoxTop")]
    [InlineData(12_000 + 299, 800)]   // centre outside the rectangle, footprint over it: the top
    [InlineData(12_000 + 300, 0)]     // footprint only touching (distance = r): terrain
    [InlineData(11_000, 800)]
    public void GroundResolve_SupportIsTheTopUnderTheFootprint(int x, int expected)
    {
        var rig = new Rig(WithSolids(Box(8_000, 0, 8_000, 12_000, 800, 12_000)), x: x, z: 10_000, y: 800);
        CarrierEnv env = rig.Env;
        CarrierColliders.Gather(ref rig.Scratch, in env, in rig.State);

        int support = CarrierSweep.GroundResolve(in env, in rig.Scratch, x, 800, 10_000, out bool walkable, out _, out _);

        Assert.Equal(expected, support);
        Assert.True(walkable);
    }

    [Theory]
    [Trait("Id", "PCM-P4-BoxTop")]
    [InlineData(900, 1_000, 1_000)]   // top below terrain: terrain wins
    [InlineData(1_200, 1_200, 1_200)] // top above terrain, at the feet: the top
    [InlineData(1_200, 1_100, 1_000)] // top above the feet: a wall, not a support
    public void GroundResolve_SupportIsTheMaxOfTerrainAndTopsAtOrBelowTheFeet(int top, int feetY, int expected)
    {
        WorldTables world = Flat(b => b.AddSolid(EntityId.None, Box(8_000, 0, 8_000, 12_000, top, 12_000), SolidKind.Roof), heightMm: 1_000);
        var rig = new Rig(world, x: 10_000, z: 10_000, y: feetY);
        CarrierEnv env = rig.Env;
        CarrierColliders.Gather(ref rig.Scratch, in env, in rig.State);

        int support = CarrierSweep.GroundResolve(in env, in rig.Scratch, 10_000, feetY, 10_000, out bool walkable, out _, out _);

        Assert.Equal(expected, support);
        Assert.True(walkable);
    }

    // ------------------------------------------------------------------ PCM-P4-1b: step-up

    private const ushort DiagonalMinusZ = 57344;   // forward = (+1, -1): the Diagonal input turned 90 degrees

    [Fact]
    [Trait("Id", "PCM-P4-1b")]
    public void Step_WalkingIntoA400mmLedge_ClimbsItWithoutJumping()
    {
        var rig = new Rig(WithSolids(Box(10_000, 0, 5_000, 18_000, 400, 15_000)), x: 9_000, z: 10_000, y: 0);

        for (int i = 0; i < 40 && rig.State.X < 11_000; i++)
        {
            rig.Tick(Walk(PlusX));
            Assert.False(rig.OverlapsAny(), $"tick {i}");
            Assert.Equal(LocoState.Grounded, rig.State.Loco);
            Assert.True(rig.State.Vy <= 0, $"tick {i}: no jump");
        }

        Assert.Equal(400, rig.State.Y);
        Assert.True(rig.State.X > 10_000 + R, $"X {rig.State.X}");
    }

    [Fact]
    [Trait("Id", "PCM-P4-1b")]
    public void Step_WalkingDiagonallyIntoA401mmLedge_IsBlockedAndSlidesAlongTheFace()
    {
        var rig = new Rig(WithSolids(Box(10_000, 0, 2_000, 14_000, 401, 18_000)), x: 9_000, z: 6_000, y: 0);

        for (int i = 0; i < 60; i++)
        {
            rig.Tick(Walk(Diagonal));
            Assert.False(rig.OverlapsAny(), $"tick {i}");
            Assert.True(rig.State.X <= 10_000 - R, $"tick {i}: X {rig.State.X}");
        }

        Assert.Equal(10_000 - R, rig.State.X);
        Assert.Equal(0, rig.State.Y);
        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.True(rig.State.Z > 8_000, $"Z {rig.State.Z}: slid along the face");
    }

    [Theory]
    [Trait("Id", "PCM-P4-1b")]
    [InlineData(400 + 1_399, false)]   // clearance 1399 < 1400: refused
    [InlineData(400 + 1_400, true)]    // exactly the capsule height: climbs
    public void Step_A400mmStepUnderALowRoof_IsRefusedWithoutHeadroom(int roofBottom, bool climbs)
    {
        var rig = new Rig(
            WithSolids(Box(10_000, 0, 5_000, 18_000, 400, 15_000), Box(10_000, roofBottom, 5_000, 18_000, roofBottom + 200, 15_000)),
            x: 9_000,
            z: 10_000,
            y: 0);

        for (int i = 0; i < 40 && rig.State.X < 11_000; i++)
        {
            rig.Tick(Walk(PlusX));
            Assert.False(rig.OverlapsAny(), $"tick {i}");
        }

        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.Equal(climbs ? 400 : 0, rig.State.Y);
        Assert.Equal(climbs, rig.State.X > 10_000);
        if (!climbs)
        {
            Assert.Equal(10_000 - R, rig.State.X);
        }
    }

    [Fact]
    [Trait("Id", "PCM-P4-1b")]
    public void Run_AnAirborneCarrierIntoA400mmLedge_NeverStepsUp()
    {
        var rig = new Rig(WithSolids(Box(10_000, 0, 5_000, 14_000, 400, 15_000)), x: 9_600, z: 10_000, y: 100, LocoState.Airborne);

        rig.Run(300, 0);

        Assert.Equal(10_000 - R, rig.State.X);
        Assert.False(rig.OverlapsAny());
    }

    // ------------------------------------------------------------------ PCM-P4-1c: walking off a ledge, coyote

    private static Rig OnLedge(int top) =>
        new(WithSolids(Box(5_000, 0, 5_000, 10_000, top, 15_000)), x: 9_000, z: 10_000, y: top);

    [Fact]
    [Trait("Id", "PCM-P4-1c")]
    public void Step_WalkingOffA400mmLedge_StaysGrounded()
    {
        Rig rig = OnLedge(400);

        for (int i = 0; i < 40; i++)
        {
            rig.Tick(Walk(PlusX));
            Assert.Equal(LocoState.Grounded, rig.State.Loco);
            Assert.Equal(rig.State.X >= 10_000 + R ? 0 : 400, rig.State.Y);
        }

        Assert.True(rig.State.X > 10_000 + R, $"X {rig.State.X}");
    }

    /// <summary>Walks off the 401 mm ledge; returns the input history up to and including the ground-loss tick.</summary>
    private static int WalkOff401(Rig rig)
    {
        for (int i = 0; i < 60; i++)
        {
            rig.Tick(Walk(PlusX));
            if (rig.State.Loco == LocoState.Airborne)
            {
                return i;
            }
        }

        throw new InvalidOperationException("never left the ledge");
    }

    [Fact]
    [Trait("Id", "PCM-P4-1c")]
    public void Step_WalkingOffA401mmLedge_GoesAirborneWithCoyoteTicks()
    {
        Rig rig = OnLedge(401);

        WalkOff401(rig);

        Assert.Equal(LocoState.Airborne, rig.State.Loco);
        Assert.Equal(rig.Settings.CoyoteTicks, rig.State.CoyoteLeft);
        Assert.Equal(401, rig.State.Y);
        Assert.True(rig.State.X >= 10_000 + R);
    }

    [Theory]
    [Trait("Id", "PCM-P4-1c")]
    [InlineData(1, true)]
    [InlineData(4, true)]    // 4 ticks after the ground-loss tick: a coyote jump
    [InlineData(5, false)]   // 5 ticks after: coyote has run out
    public void Step_AJumpPressedAfterWalkingOffA401mmLedge_JumpsWithinCoyoteTicks(int pressTick, bool jumps)
    {
        Rig rig = OnLedge(401);
        WalkOff401(rig);
        for (int t = 1; t < pressTick; t++)
        {
            rig.Tick(Walk(PlusX));
            Assert.Equal(LocoState.Airborne, rig.State.Loco);
        }

        rig.Tick(Walk(PlusX) with { JumpPressed = true });

        Assert.Equal(jumps, rig.State.Vy > 0);
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
    }

    [Fact]
    [Trait("Id", "PCM-P4-1c")]
    public void Step_AJumpPressedOnTheSweepGroundLossTick_Jumps()
    {
        // The tick the sweep walks the carrier off the ledge: phase 4 still sees it Grounded, so the press is a ground jump.
        Rig probe = OnLedge(401);
        int lossTick = WalkOff401(probe);
        Rig rig = OnLedge(401);
        for (int t = 0; t < lossTick; t++)
        {
            rig.Tick(Walk(PlusX));
        }

        rig.Tick(Walk(PlusX) with { JumpPressed = true });

        Assert.Equal(LocoState.Airborne, rig.State.Loco);
        Assert.True(rig.State.Vy > 0, $"Vy {rig.State.Vy}");
    }

    [Fact]
    [Trait("Id", "PCM-P4-1c")]
    public void Step_AJumpPressedOnThePhase4GroundLossTick_IsACoyoteJump()
    {
        // TD-072(6): the carrier stands exactly at the ledge edge (footprint touching only), so phase 4's entry check loses the ground
        // on this very tick and starts coyote; the press on the same tick fires the coyote jump (a normal jump, not the extra jump).
        var rig = new Rig(WithSolids(Box(5_000, 0, 5_000, 10_000, 401, 15_000)), x: 10_000 + R, z: 10_000, y: 401);

        rig.Tick(Idle() with { JumpPressed = true });

        Assert.Equal(LocoState.Airborne, rig.State.Loco);
        Assert.Equal(CarrierJump.LaunchSpeed(rig.Settings) - CarrierGravity.PerTickMmS(rig.Settings), rig.State.Vy);
        Assert.Equal(0, rig.State.CoyoteLeft);
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
    }

    // ------------------------------------------------------------------ PCM-P4-2c (static): depenetration

    [Theory]
    [Trait("Id", "PCM-P4-2c")]
    [InlineData(600)]
    [InlineData(2_500)]
    [InlineData(3_000)]
    public void Step_ABoxAppearingOverAStandingCarrier_UpTo3000mm_LiftsItOntoTheTop(int top)
    {
        var rig = new Rig(WithSolids(Box(8_000, 0, 8_000, 12_000, top, 12_000)), x: 10_000, z: 10_000, y: 0);

        rig.Tick(Idle());

        Assert.Equal((10_000, top, 10_000), (rig.State.X, rig.State.Y, rig.State.Z));
        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.Equal(1, rig.Scratch.Depenetrations);
        Assert.False(rig.OverlapsAny());
    }

    [Theory]
    [Trait("Id", "PCM-P4-2c")]
    [InlineData(3_001)]
    [InlineData(8_000)]
    public void Step_ABoxAppearingOverAStandingCarrier_Above3000mm_MovesItToTheNearestWalkablePoint(int top)
    {
        WorldTables world = WithSolids(Box(8_000, 0, 8_000, 12_000, top, 12_000));
        var rig = new Rig(world, x: 10_000, z: 10_000, y: 0);

        // The nearest face is -x (ties x first): probe 7700 / 10000; the nearest walkable tile centre is (7000, 9000) (ties to lower tz).
        Assert.True(world.TrySnapWalkable(new Vec3Mm(10_000 - 2_000 - R, 0, 10_000), 2 * world.TileSizeMm, out Vec3Mm expected));

        rig.Tick(Idle());

        Assert.Equal((7_000, 9_000), (expected.X, expected.Z));
        Assert.Equal((expected.X, expected.Y, expected.Z), (rig.State.X, rig.State.Y, rig.State.Z));
        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.Equal(1, rig.Scratch.Depenetrations);
        Assert.False(rig.OverlapsAny());
    }

    [Fact]
    [Trait("Id", "PCM-P4-2c")]
    public void Step_ACarrierClearOfEveryBox_IsNeverDepenetrated()
    {
        var rig = new Rig(WithSolids(Box(10_000, 0, 5_000, 10_100, 3_000, 15_000)), x: 8_000, z: 10_000, y: 0);

        for (int i = 0; i < 60; i++)
        {
            rig.Tick(Walk(PlusX, sprint: true));
        }

        Assert.Equal(0, rig.Scratch.Depenetrations);
    }

    // ------------------------------------------------------------------ PCM-P4-2d: ceilings

    [Fact]
    [Trait("Id", "PCM-P4-2d")]
    public void Step_JumpingUnderA1500mmRoofSlab_ZeroesVyOnContactAndFalls()
    {
        const int SlabBottom = 1_500;
        var rig = new Rig(WithSolids(Box(5_000, SlabBottom, 5_000, 15_000, 1_700, 15_000)), x: 10_000, z: 10_000, y: 0);
        rig.Tick(Idle());

        rig.Tick(Idle() with { JumpPressed = true });

        Assert.Equal(SlabBottom - H, rig.State.Y);   // the jump's first tick (239 mm) hits the slab
        Assert.Equal(0, rig.State.Vy);
        Assert.Equal(0, rig.State.RemY);
        Assert.Equal(LocoState.Airborne, rig.State.Loco);

        rig.Tick(Idle());
        Assert.True(rig.State.Vy < 0, $"Vy {rig.State.Vy}: falling");
        for (int i = 0; i < 60; i++)
        {
            Assert.True(rig.State.Y <= SlabBottom - H, $"tick {i}: Y {rig.State.Y}");
            Assert.False(rig.OverlapsAny());
            rig.Tick(Idle());
        }

        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.Equal(0, rig.State.Y);
    }

    [Fact]
    [Trait("Id", "PCM-P4-2d")]
    public void Step_RepeatedJumpsWhileWalkingUnderARoof_NeverEndAboveItsBottomMinusTheCapsule()
    {
        const int SlabBottom = 2_000;
        var rig = new Rig(WithSolids(Box(2_000, SlabBottom, 2_000, 18_000, 2_100, 18_000)), x: 4_000, z: 10_000, y: 0);

        for (int i = 0; i < 120 && rig.State.X < 17_000; i++)
        {
            rig.Tick(Walk(PlusX) with { JumpPressed = i % 7 == 0 });
            Assert.True(rig.State.Y <= SlabBottom - H, $"tick {i}: Y {rig.State.Y}");
            Assert.False(rig.OverlapsAny(), $"tick {i}");
        }
    }

    // ------------------------------------------------------------------ Corners

    public static TheoryData<string, AabbMm, AabbMm, int, int> Corners() => new()
    {
        // 90 degrees: a wall facing -x and a wall facing -z meeting at (10000, 10000).
        { "right", Box(10_000, 0, 2_000, 10_100, 3_000, 10_100), Box(2_000, 0, 10_000, 10_100, 3_000, 10_100), 8_000, 8_000 },

        // Acute wedge: a wall facing -x and the vertical edge of a box 200 mm from it (narrower than the capsule), so the two contact
        // normals are about 132 degrees apart and the carrier is funnelled into the notch.
        { "acute", Box(10_000, 0, 2_000, 10_100, 3_000, 18_000), Box(9_000, 0, 10_000, 9_500, 3_000, 11_000), 8_000, 6_000 },
    };

    [Theory]
    [Trait("Id", "PCM-P4-Corner")]
    [MemberData(nameof(Corners))]
    public void Step_PushingDiagonallyIntoACorner_DoesNotJitterAndTurningFreesIt(string name, AabbMm a, AabbMm b, int x, int z)
    {
        var rig = new Rig(WithSolids(a, b), x: x, z: z, y: 0);

        for (int i = 0; i < 120; i++)
        {
            int px = rig.State.X;
            int pz = rig.State.Z;
            rig.Tick(Walk(Diagonal));
            Assert.False(rig.OverlapsAny(), $"{name} tick {i}");
            if (i >= 60)
            {
                Assert.True(Math.Abs(rig.State.X - px) <= 1 && Math.Abs(rig.State.Z - pz) <= 1, $"{name} tick {i}: jitter ({px},{pz}) -> ({rig.State.X},{rig.State.Z})");
            }
        }

        Assert.True(rig.State.X > x + 1_000, $"{name}: reached the corner, X {rig.State.X}");
        int settledZ = rig.State.Z;
        bool freed = false;
        for (int i = 0; i < 3 && !freed; i++)
        {
            rig.Tick(Walk(DiagonalMinusZ));
            Assert.False(rig.OverlapsAny(), $"{name} turn tick {i}");
            freed = rig.State.Z < settledZ;
        }

        Assert.True(freed, $"{name}: frozen at Z {rig.State.Z}");
        Assert.True(rig.Scratch.FixDivZero == 0 && rig.Scratch.FixClamped == 0, $"{name}: Fix counters {rig.Scratch.FixDivZero}/{rig.Scratch.FixClamped}");
    }

    // ------------------------------------------------------------------ PCM-P4-2c: depenetration fallbacks (gate round)

    [Fact]
    [Trait("Id", "PCM-P4-2c")]
    public void Step_StackedBoxesWhoseUpperTopIsAbove3000_MoveTheCarrierToAWalkablePoint()
    {
        // The lower top (2000) would lift, but the box stacked on it reaches 3500 above the feet: the walkable-point fallback wins.
        var rig = new Rig(
            WithSolids(Box(8_000, 0, 8_000, 12_000, 2_000, 12_000), Box(8_000, 2_000, 8_000, 12_000, 3_500, 12_000)),
            x: 10_000,
            z: 10_000,
            y: 0);

        rig.Tick(Idle());

        Assert.Equal((7_000, 0, 9_000), (rig.State.X, rig.State.Y, rig.State.Z));
        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.Equal(1, rig.Scratch.Depenetrations);
        Assert.False(rig.OverlapsAny());
    }

    [Fact]
    [Trait("Id", "PCM-P4-2c")]
    public void Step_ABlockedTileUnderTheCarrier_MovesItOffAndRegathersAtTheNewPosition()
    {
        // Tile (5, 5) spans 10000..12000; the probe is 9700 / 11000 and the nearest walkable centre (9000, 11000). The prop at
        // x 7600..7700 is outside the tick-start region (11000 +- 1500) but inside the new one (9000 +- 1500): only a re-gather has it.
        var rig = new Rig(
            Flat(b =>
            {
                b.MarkBlocked(5, 5);
                b.AddSolid(EntityId.None, Box(7_600, 0, 11_000, 7_700, 100, 11_100), SolidKind.Prop);
            }),
            x: 11_000,
            z: 11_000,
            y: 0);

        rig.Tick(Idle());

        Assert.Equal((9_000, 0, 11_000), (rig.State.X, rig.State.Y, rig.State.Z));
        Assert.Equal(1, rig.Scratch.Depenetrations);
        Assert.False(rig.OverlapsAny());
        var fresh = new CarrierScratch(64);
        CarrierEnv env = rig.Env;
        CarrierColliders.Gather(ref fresh, in env, in rig.State);
        Assert.Equal(new ushort[] { 0 }, fresh.Solids.ToArray());
        Assert.Equal(fresh.Solids.ToArray(), rig.Scratch.Solids.ToArray());
    }

    [Fact]
    [Trait("Id", "PCM-P4-2c")]
    public void Step_WhenTheNearestWalkableCentreIsInsideTheBox_TheProbeOnTerrainIsUsed()
    {
        // The nearest face is -x (1500): probe 8200 / 10000, whose nearest walkable centre (9000, 9000) is inside the box.
        WorldTables world = WithSolids(Box(8_500, 0, 7_000, 13_000, 5_000, 13_000));
        Assert.True(world.TrySnapWalkable(new Vec3Mm(8_200, 0, 10_000), 2 * world.TileSizeMm, out Vec3Mm inside));
        Assert.Equal((9_000, 9_000), (inside.X, inside.Z));
        var rig = new Rig(world, x: 10_000, z: 10_000, y: 0);

        rig.Tick(Idle());

        Assert.Equal((8_200, 0, 10_000), (rig.State.X, rig.State.Y, rig.State.Z));
        Assert.Equal(LocoState.Grounded, rig.State.Loco);
        Assert.Equal(1, rig.Scratch.Depenetrations);
        Assert.False(rig.OverlapsAny());
    }

    [Fact]
    [Trait("Id", "PCM-P4-2c")]
    public void Step_ALiftOf3000mm_RegathersSoABoxAboveTheNewHeadIsACeiling()
    {
        // Slab bottom 5000: outside the tick-start region (y up to 0 + 1400 + 1200 = 2600) but a ceiling 600 mm above the lifted head.
        var rig = new Rig(
            WithSolids(Box(8_000, 0, 8_000, 12_000, 3_000, 12_000), Box(8_000, 5_000, 8_000, 12_000, 5_200, 12_000)),
            x: 10_000,
            z: 10_000,
            y: 0);

        rig.Tick(Idle());
        Assert.Equal(3_000, rig.State.Y);
        Assert.Equal(new ushort[] { 0, 1 }, rig.Scratch.Solids.ToArray());

        rig.Tick(Idle() with { JumpPressed = true });
        for (int i = 0; i < 40; i++)
        {
            Assert.True(rig.State.Y <= 5_000 - H, $"tick {i}: Y {rig.State.Y}");
            rig.Tick(Idle());
        }
    }

    // ------------------------------------------------------------------ CylinderVsBox.Mtv (gate round)

    [Fact]
    [Trait("Id", "PCM-P4-Mtv")]
    public void Mtv_CentreInsideWithEqualXAndZPenetration_PicksX()
    {
        var box = new ColliderBox(0, 0, 0, 1_000, 1_000, 1_000);

        CylinderContact c = CylinderVsBox.Mtv(500, 0, 500, R, H, 500, 500, in box);

        Assert.Equal((ContactAxis.X, -1, -800L, 0L, 0L), (c.Axis, c.Sign, c.Dx, c.Dy, c.Dz));
    }

    [Fact]
    [Trait("Id", "PCM-P4-Mtv")]
    public void Mtv_CentreInsideWithEqualZAndYPenetration_PicksZ()
    {
        var box = new ColliderBox(0, 0, 0, 3_000, 800, 1_000);   // x 1800, z 800, y up 800 / down 1400

        CylinderContact c = CylinderVsBox.Mtv(1_500, 0, 500, R, H, 1_500, 500, in box);

        Assert.Equal((ContactAxis.Z, -1, 0L, 0L, -800L), (c.Axis, c.Sign, c.Dx, c.Dy, c.Dz));
    }

    [Fact]
    [Trait("Id", "PCM-P4-Mtv")]
    public void Mtv_EveryCornerPush_LeavesTheCylinderClear()
    {
        var box = new ColliderBox(0, 0, 0, 1_000, 1_000, 1_000);
        int checkedCount = 0;
        for (int ex = 1; ex < R; ex += 7)
        {
            for (int ez = 1; ez < R; ez += 11)
            {
                if ((ex * ex) + (ez * ez) >= R * R)
                {
                    continue;
                }

                long x = 1_000 + ex;
                long z = 1_000 + ez;
                CylinderContact c = CylinderVsBox.Mtv(x, 0, z, R, H, x, z, in box);
                Assert.Equal(ContactAxis.Corner, c.Axis);
                Assert.False(CylinderVsBox.Overlaps(x + c.Dx, 0, z + c.Dz, R, H, in box), $"({ex},{ez}) -> ({c.Dx},{c.Dz})");
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 500);
    }

    [Fact]
    [Trait("Id", "PCM-P4-Gather")]
    public void Lookups_AnOutOfRangeSolidIndex_IsSkippedNeverThrown()
    {
        var rig = new Rig(WithSolids(Box(9_000, 0, 9_000, 11_000, 500, 11_000)), x: 10_000, z: 10_000, y: 500);
        rig.Scratch.TryAddSolid(999);
        rig.Scratch.TryAddSolid(0);
        CarrierEnv env = rig.Env;

        Assert.False(CarrierColliders.FirstOverlap(in env, in rig.Scratch, 10_000, 500, 10_000, out _));
        Assert.True(CarrierColliders.HighestTopUnder(in env, in rig.Scratch, 10_000, 500, 10_000, out int top));
        Assert.Equal(500, top);
        Assert.False(CarrierColliders.LowestBottomOver(in env, in rig.Scratch, 10_000, 1_900, 10_000, out _));
    }

    // ------------------------------------------------------------------ TD-072(6): buffered press after the extra jump (gate round)

    [Theory]
    [Trait("Id", "PCM-P5-1")]
    [InlineData(1_000)]   // landing on a 1000 mm box top
    [InlineData(0)]       // landing on terrain
    public void Step_APressBufferedAfterTheExtraJump_FiresOnTheLandingTick(int top)
    {
        WorldTables world = top > 0 ? WithSolids(Box(6_000, 0, 6_000, 14_000, top, 14_000)) : Flat();
        var rig = new Rig(world, x: 10_000, z: 10_000, y: top + 2_000, LocoState.Airborne);
        CarrierInput held = Idle() with { ExtraJumps = 1 };

        rig.Tick(held with { JumpPressed = true });
        Assert.NotEqual(CarrierFlags.None, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
        Assert.True(rig.State.Vy > 0);
        for (int guard = 0; !(rig.State.Vy < 0 && rig.State.Y - top <= 300); guard++)
        {
            Assert.True(guard < 120, "never fell to the top");
            rig.Tick(held);
        }

        rig.Tick(held with { JumpPressed = true });   // extra used, no coyote: the press is buffered
        bool fired = rig.State.Vy > 0;
        for (int k = 0; k < rig.Settings.JumpBufferTicks && !fired; k++)
        {
            Assert.Equal(LocoState.Airborne, rig.State.Loco);
            rig.Tick(held);
            fired = rig.State.Vy > 0;
        }

        Assert.True(fired, "the buffered press never fired");
        Assert.True(rig.State.Y >= top, $"Y {rig.State.Y}");
        Assert.True(rig.State.Y <= top + 300, $"Y {rig.State.Y}: launched from the landing");
        Assert.Equal(CarrierFlags.None, rig.State.Flags & CarrierFlags.ExtraJumpUsed);
    }
}

/// <summary>Story 008 part A allocation guardrail: the gather, box slides and box-top support allocate nothing.</summary>
public class ColliderAllocationTests
{
    [Fact]
    [Trait("Id", "PCM-PERF-1")]
    public void Step_10000CallsAmongBoxesAndBlockedTiles_AllocateNothing()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(OwnedCarrier).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        var corners = new int[11 * 11];
        var builder = new WorldTablesBuilder();
        builder.SetCorners(10, 10, corners);
        builder.MarkBlocked(6, 5);
        builder.MarkBlocked(3, 4);
        for (int i = 0; i < 20; i++)
        {
            int x = 6_000 + ((i % 5) * 1_700);
            int z = 6_000 + ((i / 5) * 1_900);
            builder.AddSolid(EntityId.None, new AabbMm(new Vec3Mm(x, 0, z), new Vec3Mm(x + 400 + (i * 30), 200 + (i * 90), z + 150)), SolidKind.Prop);
        }

        WorldTables world = builder.Build(WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
        PlayerSettings settings = PlayerSettings.FromBalance(BuiltInContent.CreateM1a().Player);
        var remotes = new EmptyRemoteCapsuleSource();
        var supports = new EmptySupportTransformSource();
        long sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("10,000 OwnedCarrier.Step calls among boxes and blocked tiles", () =>
        {
            var state = new CarrierState { X = 10_000, Z = 10_000, Y = 0 };
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
