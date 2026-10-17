using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Tick phase 5 of the pure carrier step: the sub-step sweep over the height field (GDD <c>player-character-movement.md</c> P4, terrain
/// half; ADR-0013 section 4; TR-movement-006). The tick displacement <c>D</c> (own horizontal move from the 1/30 mm remainders plus the
/// vertical move from phase 4) is split into <c>n = clamp(CeilDiv(max(|Dx|, |Dz|, |Dy|), subStepMm), 1, maxSubStepsPerTick)</c>
/// sub-steps; a per-axis displacement beyond <c>subStepMm x maxSubStepsPerTick</c> (1200 mm) is clamped and counted in
/// <see cref="CarrierScratch.StepClamped"/>. Each axis is split with the floor discipline <c>Part(i) = FloorDiv(D (i + 1), n) - FloorDiv(D i, n)</c>
/// so the parts sum exactly to <c>D</c> and the 1/30 remainder is never re-quantised.
/// <para>
/// Per sub-step: (1) move horizontally; a move that would raise the feet onto non-walkable terrain (or, for a Grounded carrier, by more
/// than <c>stepUpMm</c>) is projected onto the contour, at most <c>slideIterations</c> (3) times, then cancelled; (2) the box pass
/// (<see cref="CarrierSlide.SweepBoxes"/>: blocked tiles, static solids and construct boxes gathered once per tick by
/// <see cref="CarrierColliders.Gather"/>; it shares the slide budget); (3) the vertical move and ground resolution through the one
/// <see cref="GroundResolve"/> (terrain and walkable box tops); a Grounded carrier snaps to the support by the one rule
/// <see cref="SnapToSupport"/> (walkable and at most <c>stepUpMm</c> below the feet), otherwise the ground is lost (coyote starts). Non-walkable terrain is a wall: a falling carrier pressed into it slides along and down
/// the fall line, stays Airborne, and gravity keeps applying.
/// </para>
/// Pure, allocation-free, no statics; <see cref="Int128"/> appears only in local numerator arithmetic, never in state.
/// </summary>
/// <example>
/// <code>
/// var plan = default(GravityPlan) with { Dy = -14 };
/// CarrierSweep.Run(ref state, in env, ref scratch, ref sink, dx: 250, dz: 0, ref plan);
/// </code>
/// </example>
public static class CarrierSweep
{
    private const int StepsPerMmDenominator = Hosting.TickRate.Hz;

    /// <summary>
    /// The number of sub-steps for a displacement: <c>clamp(CeilDiv(maxAbsMm, subStepMm), 1, maxSubSteps)</c> (so 1, 300, 301, 600, 650, 1199, 1200 mm give 1, 1, 2, 2, 3, 4, 4).
    /// </summary>
    /// <param name="maxAbsMm">The largest absolute axis displacement, mm.</param>
    /// <param name="subStepMm">The sub-step length (<c>player.subStepMm</c>, 300); values below 1 count as 1.</param>
    /// <param name="maxSubSteps">The per-tick cap (<c>player.maxSubStepsPerTick</c>, 4); values below 1 count as 1.</param>
    /// <returns>The sub-step count, 1 to <paramref name="maxSubSteps"/>.</returns>
    /// <example><code>int n = CarrierSweep.SubStepCount(650, 300, 4);   // 3</code></example>
    public static int SubStepCount(long maxAbsMm, int subStepMm, int maxSubSteps)
    {
        long sub = Math.Max(1, subStepMm);
        long cap = Math.Max(1, maxSubSteps);
        long n = (Math.Max(0, maxAbsMm) + sub - 1) / sub;
        return (int)Math.Clamp(n, 1, cap);
    }

    /// <summary>
    /// Sub-step <paramref name="index"/>'s share of an axis displacement: <c>FloorDiv(total (index + 1), n) - FloorDiv(total index, n)</c>. The shares of
    /// indices 0 to <c>n - 1</c> sum to exactly <paramref name="total"/>.
    /// </summary>
    /// <param name="total">The axis displacement of the tick, mm.</param>
    /// <param name="n">The sub-step count (at least 1).</param>
    /// <param name="index">The sub-step, 0 to <c>n - 1</c>.</param>
    /// <returns>The share, mm.</returns>
    /// <example><code>int a = CarrierSweep.Part(10, 4, 0);   // 2 (shares are 2, 3, 2, 3)</code></example>
    public static int Part(int total, int n, int index)
    {
        long count = Math.Max(1, n);
        return (int)(Fix.FloorDiv((Int128)total * (index + 1), count) - Fix.FloorDiv((Int128)total * index, count));
    }

    /// <summary>
    /// The one support routine (story 007 terrain + story 008 box tops): the support height under a feet point is
    /// <c>max(terrain, highest walkable box top under the footprint at or below the feet)</c>; a box top wins a tie, is always walkable
    /// and has a zero gradient. Every ground decision (phase-4 entry check, horizontal blocking, landing, snap, the wall slide, the map
    /// re-ground) goes through here.
    /// </summary>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The tick's collider candidates (<see cref="CarrierColliders.Gather"/>); none means terrain only.</param>
    /// <param name="x">Feet X, mm.</param>
    /// <param name="y">Feet Y, mm (box tops above it are not supports).</param>
    /// <param name="z">Feet Z, mm.</param>
    /// <param name="walkable">True when a carrier may stand on the support (slope at most <c>slopeMaxPm</c>).</param>
    /// <param name="gx">Un-divided X gradient numerator of the terrain at the point.</param>
    /// <param name="gz">Un-divided Z gradient numerator of the terrain at the point.</param>
    /// <returns>The support height, mm.</returns>
    /// <example><code>int support = CarrierSweep.GroundResolve(in env, in scratch, x, y, z, out bool walkable, out _, out _);</code></example>
    public static int GroundResolve(in CarrierEnv env, in CarrierScratch scratch, int x, int y, int z, out bool walkable, out long gx, out long gz) =>
        GroundResolve(in env, in scratch, x, y, z, out walkable, out gx, out gz, out _, out _);

    /// <summary>The one support routine, also reporting whether the support is a box top and the terrain height under the point (story 009: water depth is measured only over terrain).</summary>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The tick's collider candidates.</param>
    /// <param name="x">Feet X, mm.</param>
    /// <param name="y">Feet Y, mm (box tops above it are not supports).</param>
    /// <param name="z">Feet Z, mm.</param>
    /// <param name="walkable">True when a carrier may stand on the support.</param>
    /// <param name="gx">Un-divided X gradient numerator of the terrain (0 on a box top).</param>
    /// <param name="gz">Un-divided Z gradient numerator of the terrain (0 on a box top).</param>
    /// <param name="boxTop">True when a walkable box top is the support.</param>
    /// <param name="terrainMm">The terrain height under the point, mm.</param>
    /// <returns>The support height, mm.</returns>
    /// <example><code>int support = CarrierSweep.GroundResolve(in env, in scratch, x, y, z, out _, out _, out _, out bool box, out int terrain);</code></example>
    public static int GroundResolve(
        in CarrierEnv env, in CarrierScratch scratch, int x, int y, int z, out bool walkable, out long gx, out long gz, out bool boxTop, out int terrainMm)
    {
        walkable = CarrierTerrain.Sample(env.World, x, z, env.Settings.SlopeMaxPm, out terrainMm, out gx, out gz);
        if (CarrierColliders.HighestTopUnder(in env, in scratch, x, y, z, out int top) && top >= terrainMm)
        {
            walkable = true;
            gx = 0;
            gz = 0;
            boxTop = true;
            return top;
        }

        boxTop = false;
        return terrainMm;
    }

    /// <summary>
    /// The one snap rule (TD-078 item 2; GDD P4 snap-down): a Grounded carrier stays on a walkable support that is at most
    /// <c>stepUpMm</c> below its feet (or any distance above, following terrain up) and its feet are set to it. Returns false (feet
    /// untouched) when the ground is lost.
    /// </summary>
    /// <param name="state">The carrier.</param>
    /// <param name="support">The support height from <see cref="GroundResolve"/>.</param>
    /// <param name="walkable">Whether the support is walkable.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>True when snapped.</returns>
    /// <example><code>if (!CarrierSweep.SnapToSupport(ref state, support, walkable, settings)) { CarrierSweep.LoseGround(ref state, settings); }</code></example>
    public static bool SnapToSupport(ref CarrierState state, int support, bool walkable, PlayerSettings settings)
    {
        if (walkable && (long)state.Y - support <= settings.StepUpMm)
        {
            state.Y = support;
            return true;
        }

        return false;
    }

    /// <summary>Ground lost: Airborne with <c>CoyoteLeft = coyoteTicks</c> and no vertical speed (gravity acts from this tick or the next).</summary>
    internal static void LoseGround(ref CarrierState state, PlayerSettings settings)
    {
        state.Loco = LocoState.Airborne;
        state.CoyoteLeft = CarrierJump.CoyoteStart(settings);
        state.Vy = 0;
        state.RemY = 0;
    }

    /// <summary>Own horizontal step of a tick in mm from a velocity and its 1/30 mm remainder (<c>FloorDiv(v + rem, 30)</c>; the remainder is carried).</summary>
    internal static int HorizontalStep(ref int remainder, int velocityMmS, ref SimCounterSink sink)
    {
        long n = (long)velocityMmS + remainder;
        long d = Fix.FloorDiv(n, StepsPerMmDenominator, ref sink);
        remainder = (int)(n - (StepsPerMmDenominator * d));
        return (int)d;
    }

    /// <summary>
    /// Runs the sweep for one tick. Grounded and Airborne carriers are swept in sub-steps; any other locomotion state (Swimming, Mounted)
    /// takes the plain horizontal move (story 009 routes swimmers through <see cref="SwimMove"/> from <see cref="OwnedCarrier"/>).
    /// </summary>
    /// <param name="state">The carrier, updated in place.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">Counters: <see cref="CarrierScratch.SubStepsMaxThisTick"/>, <see cref="CarrierScratch.SlideIterationsMax"/>, <see cref="CarrierScratch.StepClamped"/>.</param>
    /// <param name="sink">Counter sink for the division helpers.</param>
    /// <param name="dx">Own horizontal displacement X for the tick, mm.</param>
    /// <param name="dz">Own horizontal displacement Z for the tick, mm.</param>
    /// <param name="plan">Phase 4's vertical displacement and unconsumed press; <c>PendingJump</c> is cleared when a landing fires it.</param>
    /// <example><code>CarrierSweep.Run(ref state, in env, ref scratch, ref sink, 250, 0, ref plan);</code></example>
    public static void Run(
        ref CarrierState state, in CarrierEnv env, ref CarrierScratch scratch, ref SimCounterSink sink, int dx, int dz, ref GravityPlan plan)
    {
        if (state.Loco is not (LocoState.Grounded or LocoState.Airborne))
        {
            state.X = AddClamped(state.X, dx);
            state.Z = AddClamped(state.Z, dz);
            return;
        }

        PlayerSettings settings = env.Settings;
        int subStepMm = Math.Max(1, settings.SubStepMm);
        int maxSubSteps = Math.Max(1, settings.MaxSubStepsPerTick);
        int maxSlides = Math.Max(0, settings.SlideIterations);
        long cap = (long)subStepMm * maxSubSteps;
        bool clamped = false;
        dx = CapAxis(dx, cap, ref clamped);
        dz = CapAxis(dz, cap, ref clamped);
        int dy = CapAxis(plan.Dy, cap, ref clamped);
        if (clamped && scratch.StepClamped < int.MaxValue)
        {
            scratch.StepClamped++;
        }

        long largest = Math.Max(Math.Max(Math.Abs((long)dx), Math.Abs((long)dz)), Math.Abs((long)dy));
        int n = SubStepCount(largest, subStepMm, maxSubSteps);
        if (n > scratch.SubStepsMaxThisTick)
        {
            scratch.SubStepsMaxThisTick = n;
        }

        bool verticalSpent = false;
        for (int i = 0; i < n; i++)
        {
            int sy = verticalSpent ? 0 : Part(dy, n, i);
            int slides = 0;
            int startX = state.X;
            int startZ = state.Z;

            // (1) horizontal move over terrain, (2) box pass (slides and step-up), (3) vertical move and ground resolution
            // (snap-down and the ceiling check inside).
            HorizontalMove(ref state, in env, in scratch, Part(dx, n, i), Part(dz, n, i), maxSlides, ref slides);
            CarrierSlide.SweepBoxes(ref state, in env, in scratch, startX, startZ, maxSlides, ref slides, ref sink);
            VerticalAndGround(ref state, in env, in scratch, ref sink, sy, maxSlides, ref slides, ref plan, ref verticalSpent);
            if (slides > scratch.SlideIterationsMax)
            {
                scratch.SlideIterationsMax = slides;
            }
        }
    }

    /// <summary>
    /// The swimmer's horizontal move (story 009): the same sub-steps, terrain contour slides and box pass as <see cref="Run"/>, with no vertical
    /// move and no ground resolution (the feet stay on the water plane; phase 8 decides exit and haul-out). Terrain blocks only where it is
    /// non-walkable and rises above the feet; boxes block as walls (a ledge stops the swimmer at its face). <see cref="Run"/> itself still
    /// takes the plain move for a Swimming carrier; <see cref="OwnedCarrier"/> routes swimmers here.
    /// </summary>
    /// <param name="state">The swimmer, updated in place.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">Counters as for <see cref="Run"/>.</param>
    /// <param name="sink">Counter sink for the division helpers.</param>
    /// <param name="dx">Own horizontal displacement X for the tick, mm.</param>
    /// <param name="dz">Own horizontal displacement Z for the tick, mm.</param>
    /// <example><code>CarrierSweep.SwimMove(ref state, in env, ref scratch, ref sink, 67, 0);</code></example>
    public static void SwimMove(ref CarrierState state, in CarrierEnv env, ref CarrierScratch scratch, ref SimCounterSink sink, int dx, int dz)
    {
        PlayerSettings settings = env.Settings;
        int subStepMm = Math.Max(1, settings.SubStepMm);
        int maxSubSteps = Math.Max(1, settings.MaxSubStepsPerTick);
        int maxSlides = Math.Max(0, settings.SlideIterations);
        long cap = (long)subStepMm * maxSubSteps;
        bool clamped = false;
        dx = CapAxis(dx, cap, ref clamped);
        dz = CapAxis(dz, cap, ref clamped);
        if (clamped && scratch.StepClamped < int.MaxValue)
        {
            scratch.StepClamped++;
        }

        int n = SubStepCount(Math.Max(Math.Abs((long)dx), Math.Abs((long)dz)), subStepMm, maxSubSteps);
        if (n > scratch.SubStepsMaxThisTick)
        {
            scratch.SubStepsMaxThisTick = n;
        }

        for (int i = 0; i < n; i++)
        {
            int slides = 0;
            int startX = state.X;
            int startZ = state.Z;
            HorizontalMove(ref state, in env, in scratch, Part(dx, n, i), Part(dz, n, i), maxSlides, ref slides);
            CarrierSlide.SweepBoxes(ref state, in env, in scratch, startX, startZ, maxSlides, ref slides, ref sink);
            if (slides > scratch.SlideIterationsMax)
            {
                scratch.SlideIterationsMax = slides;
            }
        }
    }

    /// <summary>Re-grounds a carrier whose feet were moved outside the sweep (the map clamp): the same ground routine with no vertical displacement and no wall slide (a slide could carry the feet back out of the map; the carrier rests on the wall instead and the next tick's sweep slides it).</summary>
    /// <param name="state">The carrier, updated in place.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The tick's collider candidates.</param>
    /// <param name="sink">Counter sink for the division helpers.</param>
    /// <example><code>CarrierSweep.Settle(ref state, in env, in scratch, ref sink);</code></example>
    public static void Settle(ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, ref SimCounterSink sink)
    {
        if (state.Loco is not (LocoState.Grounded or LocoState.Airborne))
        {
            return;
        }

        var plan = default(GravityPlan);
        int slides = 0;
        bool spent = false;
        VerticalAndGround(ref state, in env, in scratch, ref sink, 0, 0, ref slides, ref plan, ref spent);   // maxSlides 0: re-grounding never slides the feet back outside the clamped rectangle
    }

    private static void HorizontalMove(ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, int dx, int dz, int maxSlides, ref int slides)
    {
        int mx = dx;
        int mz = dz;
        if (mx == 0 && mz == 0)
        {
            return;
        }

        PlayerSettings settings = env.Settings;
        bool grounded = state.Loco == LocoState.Grounded;
        while (true)
        {
            int nx = AddClamped(state.X, mx);
            int nz = AddClamped(state.Z, mz);
            int height = GroundResolve(in env, in scratch, nx, state.Y, nz, out bool walkable, out long gx, out long gz);
            if (!BlocksFeet(grounded, state.Y, height, walkable, settings.StepUpMm))
            {
                state.X = nx;
                state.Z = nz;
                return;
            }

            if (slides >= maxSlides)
            {
                return;
            }

            slides++;
            if (!CarrierTerrain.ProjectOntoContour(ref mx, ref mz, gx, gz))
            {
                return;
            }
        }
    }

    /// <summary>
    /// A move is blocked when it would raise the feet onto non-walkable terrain; a Grounded carrier is also blocked by walkable terrain that
    /// rises more than <c>stepUpMm</c> in one sub-step. An Airborne carrier moving into walkable terrain above its feet is not blocked (it lands).
    /// </summary>
    private static bool BlocksFeet(bool grounded, int feetY, int terrainMm, bool walkable, int stepUpMm)
    {
        long raise = (long)terrainMm - feetY;
        if (grounded)
        {
            return walkable ? raise > stepUpMm : raise > 0;
        }

        return !walkable && raise > 0;
    }

    private static void VerticalAndGround(
        ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, ref SimCounterSink sink, int dy, int maxSlides, ref int slides, ref GravityPlan plan, ref bool verticalSpent)
    {
        PlayerSettings settings = env.Settings;

        // Pass 1 exists only after a landing fired the buffered press (tick 1 of the arc on the landing tick).
        for (int pass = 0; pass < 2; pass++)
        {
            int support = GroundResolve(in env, in scratch, state.X, state.Y, state.Z, out bool walkable, out long gx, out long gz);
            if (state.Loco == LocoState.Grounded)
            {
                if (SnapToSupport(ref state, support, walkable, settings))
                {
                    return;   // followed the terrain / snapped down
                }

                LoseGround(ref state, settings);
            }

            long ny = (long)state.Y + dy;
            if (dy > 0)
            {
                ny = CeilingClamp(ref state, in env, in scratch, ny, ref verticalSpent);
            }

            if (ny > support)
            {
                state.Y = (int)Math.Min(ny, int.MaxValue);
                return;
            }

            verticalSpent = true;
            bool landed = walkable;
            if (!walkable)
            {
                landed = PressIntoWall(ref state, in env, in scratch, ref sink, ny, support, gx, gz, maxSlides, ref slides, out support);
            }

            if (!landed)
            {
                return;
            }

            CarrierGravity.Land(ref state, support);
            if (!plan.PendingJump)
            {
                return;
            }

            plan.PendingJump = false;
            dy = CarrierGravity.LaunchOnLanding(ref state, settings, ref sink);
        }
    }

    /// <summary>
    /// Ceiling hit (story 008 part B, PCM-P4-2d): a rising move whose head would pass the lowest box bottom over the footprint stops with
    /// the head on that bottom; upward <c>Vy</c> and <c>RemY</c> are zeroed on the contact tick and the rest of the tick's vertical
    /// displacement is dropped, so the carrier falls from the next tick. Returns the (possibly reduced) target feet height.
    /// </summary>
    private static long CeilingClamp(ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, long ny, ref bool verticalSpent)
    {
        long h = env.Settings.CapsuleHeightMm;
        if (!CarrierColliders.LowestBottomOver(in env, in scratch, state.X, (long)state.Y + h, state.Z, out long bottom) || ny + h <= bottom)
        {
            return ny;
        }

        if (state.Vy > 0)
        {
            state.Vy = 0;
        }

        state.RemY = 0;
        verticalSpent = true;
        return bottom - h;
    }

    /// <summary>
    /// A fall (or an embedded start) pressed the feet into non-walkable terrain: the carrier slides along and down the fall line by the run
    /// that matches the depth it sank, stays on the surface and Airborne with zero vertical speed. Returns true (and the support) when the slide
    /// ended on walkable terrain, i.e. the carrier lands.
    /// </summary>
    private static bool PressIntoWall(
        ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, ref SimCounterSink sink, long fellTo, int surface, long gx, long gz, int maxSlides, ref int slides, out int support)
    {
        long depth = surface - fellTo;
        long y = surface;
        support = surface;
        bool walkable = false;
        if (depth > 0 && slides < maxSlides)
        {
            slides++;
            CarrierTerrain.DownhillRun(gx, gz, (int)Math.Min(depth, int.MaxValue), out int rx, out int rz);
            int startX = state.X;
            int startZ = state.Z;
            state.X = AddClamped(state.X, rx);
            state.Z = AddClamped(state.Z, rz);
            CarrierSlide.SweepBoxes(ref state, in env, in scratch, startX, startZ, maxSlides, ref slides, ref sink);
            support = GroundResolve(in env, in scratch, state.X, state.Y, state.Z, out walkable, out _, out _);

            // Never below the surface; above it when the run overshot the fall.
            y = Math.Max(fellTo, support);
        }

        state.Y = (int)y;
        if (y == support)
        {
            if (walkable)
            {
                return true;
            }

            state.Vy = 0;   // resting on the wall: the impact is spent, gravity restarts from zero each tick
            state.RemY = 0;
        }

        return false;
    }

    private static int CapAxis(int d, long cap, ref bool clamped)
    {
        if (d > cap)
        {
            clamped = true;
            return (int)cap;
        }

        if (d < -cap)
        {
            clamped = true;
            return (int)-cap;
        }

        return d;
    }

    private static int AddClamped(int a, int b) => (int)Math.Clamp((long)a + b, int.MinValue, int.MaxValue);
}
