using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Tick phase 8 of the pure carrier step: water (GDD <c>player-character-movement.md</c> P6 and F7; ADR-0013 section 5; ADR-0026 section 3;
/// TR-movement-018). Depth comes from the height field only (<c>depth = max(0, -terrainH)</c>, sea level 0, the terrain height sampled
/// by <see cref="CarrierSweep.GroundResolve(in CarrierEnv, in CarrierScratch, int, int, int, out bool, out long, out long, out bool, out int)"/>
/// through <c>WorldHeight.Sample</c>'s round-half rule), and only when the support under the feet is terrain: a carrier on a box top
/// above water has depth 0 (stays Grounded, no wade slow). <c>TileFlags.Water</c> is never read.
/// <para>
/// Swim hysteresis: <c>swimming' = swimming ? depth &gt; swimExitMm : depth &gt;= swimEnterMm</c>. A non-swimmer can only enter while its
/// feet are at or below the water plane (an airborne carrier high above deep water keeps falling). Wading:
/// <c>wadePm = swimming ? 1000 : 1000 - MulDiv(maxWadeSlowPm, min(depth, swimEnterMm), swimEnterMm)</c> (truncated), stored as
/// <see cref="CarrierState.WadeSlowPm"/> and applied by phase 2 of the NEXT tick (the water state is evaluated at the end of the tick).
/// </para>
/// <para>
/// Entry (from the ground or the air): Swimming, feet on the water plane, <c>Vy</c> and every remainder zeroed, coyote, jump buffer, the
/// extra-jump flag (PCM-P5-2 water reset) and Sprinting cleared; horizontal velocity is kept (phase 2's cap-drop slows it).
/// Exit (depth at most <c>swimExitMm</c>): Grounded with the feet on the support under them, remainders zeroed. While swimming the feet
/// are held on the plane (surface only) and <see cref="CarrierHaulOut"/> runs. Pure, allocation-free, no statics.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // OwnedCarrier.Step, phase 8:
/// CarrierWater.Apply(ref state, in env, in scratch, wx, wz, hasInput, ref sink);
/// int wadePm = CarrierWater.StoredWadePm(in state);   // read by phase 2 on the next tick
/// </code>
/// </example>
public static class CarrierWater
{
    /// <summary>The water plane (sea level), mm: the height-field datum of <c>IWorldQuery.WaterDepthMm</c> (<c>max(0, -h)</c>). Not a tunable.</summary>
    public const int WaterPlaneMm = 0;

    /// <summary>The water depth under a feet point: <c>max(0, plane - terrainH)</c> when the support there is terrain, 0 on a box top.</summary>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The tick's collider candidates (box tops).</param>
    /// <param name="x">Feet X, mm.</param>
    /// <param name="feetY">Feet Y, mm (box tops above it are not supports).</param>
    /// <param name="z">Feet Z, mm.</param>
    /// <param name="supportMm">The support height from the one support routine.</param>
    /// <param name="walkable">Whether the support is walkable.</param>
    /// <returns>The depth, mm (never negative).</returns>
    /// <example><code>int d = CarrierWater.DepthAt(in env, in scratch, 500, -600, 1000, out _, out _);   // 600 on FX-Beach</code></example>
    public static int DepthAt(in CarrierEnv env, in CarrierScratch scratch, int x, int feetY, int z, out int supportMm, out bool walkable)
    {
        supportMm = CarrierSweep.GroundResolve(in env, in scratch, x, feetY, z, out walkable, out _, out _, out bool boxTop, out int terrainMm);
        if (boxTop)
        {
            return 0;
        }

        return (int)Math.Clamp((long)WaterPlaneMm - terrainMm, 0L, int.MaxValue);
    }

    /// <summary>The swim hysteresis: <c>swimming ? depth &gt; swimExitMm : depth &gt;= swimEnterMm</c>.</summary>
    /// <param name="swimming">Swimming now.</param>
    /// <param name="depthMm">The depth under the feet.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>Swimming after this evaluation.</returns>
    /// <example><code>bool s = CarrierWater.NextSwimming(false, 899, settings);   // false</code></example>
    public static bool NextSwimming(bool swimming, int depthMm, PlayerSettings settings) =>
        swimming ? depthMm > settings.SwimExitMm : depthMm >= settings.SwimEnterMm;

    /// <summary>F7: <c>wadePm = swimming ? 1000 : 1000 - MulDiv(maxWadeSlowPm, min(depth, swimEnterMm), swimEnterMm)</c>.</summary>
    /// <param name="swimming">Swimming (no wade slow).</param>
    /// <param name="depthMm">The depth under the feet.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="sink">Counter sink for the division helper.</param>
    /// <returns>The wade multiplier, permille (1000 when dry).</returns>
    /// <example><code>int w = CarrierWater.WadePm(false, 600, settings, ref sink);   // 867</code></example>
    public static int WadePm(bool swimming, int depthMm, PlayerSettings settings, ref SimCounterSink sink)
    {
        int enter = settings.SwimEnterMm;
        if (swimming || enter <= 0 || depthMm <= 0)
        {
            return CarrierSpeed.Unit;
        }

        long slow = Fix.MulDiv(Math.Clamp(settings.MaxWadeSlowPm, 0, CarrierSpeed.Unit), Math.Min(depthMm, enter), enter, ref sink);
        return CarrierSpeed.Unit - (int)slow;
    }

    /// <summary>The wade multiplier phase 2 uses this tick: the previous tick's result, <c>1000 - WadeSlowPm</c>.</summary>
    /// <param name="state">The carrier.</param>
    /// <returns>Permille, 0 to 1000.</returns>
    public static int StoredWadePm(in CarrierState state) => CarrierSpeed.Unit - Math.Min((int)state.WadeSlowPm, CarrierSpeed.Unit);

    /// <summary>Phase 8: evaluates the water state at the end of the tick (entry, exit, wade, swim hold and haul-out).</summary>
    /// <param name="state">The carrier, updated in place.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The tick's collider candidates.</param>
    /// <param name="wx">The tick's world move vector X (phase 1, Q15 x Q15 scaled).</param>
    /// <param name="wz">The tick's world move vector Z.</param>
    /// <param name="hasInput">A move input is present.</param>
    /// <param name="sink">Counter sink for the division helpers.</param>
    public static void Apply(ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, long wx, long wz, bool hasInput, ref SimCounterSink sink)
    {
        PlayerSettings settings = env.Settings;
        if (state.Loco is LocoState.Grounded or LocoState.Airborne)
        {
            bool inWater = state.Y <= WaterPlaneMm;
            int depth = inWater ? DepthAt(in env, in scratch, state.X, state.Y, state.Z, out _, out _) : 0;
            if (NextSwimming(false, depth, settings))
            {
                EnterSwim(ref state);
                return;
            }

            SetWade(ref state, depth, settings, ref sink);
            return;
        }

        if (state.Loco != LocoState.Swimming)
        {
            return;   // Mounted (Vehicles, M1) and unknown values are left untouched
        }

        int swimDepth = DepthAt(in env, in scratch, state.X, WaterPlaneMm, state.Z, out int support, out _);
        if (!NextSwimming(true, swimDepth, settings))
        {
            ExitSwim(ref state, support, swimDepth, settings, ref sink);
            return;
        }

        state.Y = WaterPlaneMm;
        state.Vy = 0;
        state.RemY = 0;
        state.WadeSlowPm = 0;
        state.Flags &= ~CarrierFlags.Wading;
        CarrierHaulOut.Tick(ref state, in env, in scratch, wx, wz, hasInput);
    }

    /// <summary>Swim entry: feet on the plane, vertical speed and every remainder zeroed, the jump state and Sprinting cleared.</summary>
    /// <param name="state">The carrier.</param>
    internal static void EnterSwim(ref CarrierState state)
    {
        state.Loco = LocoState.Swimming;
        state.Y = WaterPlaneMm;
        state.Vy = 0;
        state.RemX = 0;
        state.RemZ = 0;
        state.RemY = 0;
        state.CoyoteLeft = 0;
        state.JumpBufferLeft = 0;
        state.HaulOutTicks = 0;
        state.WadeSlowPm = 0;
        state.Flags &= ~(CarrierFlags.ExtraJumpUsed | CarrierFlags.Sprinting | CarrierFlags.Wading);
    }

    /// <summary>Leaves the water onto a support (swim exit or haul-out): Grounded with the feet on it, remainders and the haul-out counter zeroed.</summary>
    /// <param name="state">The carrier.</param>
    /// <param name="supportMm">The support height.</param>
    internal static void Leave(ref CarrierState state, int supportMm)
    {
        state.Loco = LocoState.Grounded;
        state.Y = supportMm;
        state.Vy = 0;
        state.RemX = 0;
        state.RemZ = 0;
        state.RemY = 0;
        state.CoyoteLeft = 0;
        state.HaulOutTicks = 0;
        state.WadeSlowPm = 0;
        state.Flags &= ~CarrierFlags.Wading;
    }

    private static void ExitSwim(ref CarrierState state, int supportMm, int depthMm, PlayerSettings settings, ref SimCounterSink sink)
    {
        Leave(ref state, supportMm);
        SetWade(ref state, state.Y <= WaterPlaneMm ? depthMm : 0, settings, ref sink);
    }

    private static void SetWade(ref CarrierState state, int depthMm, PlayerSettings settings, ref SimCounterSink sink)
    {
        int slow = CarrierSpeed.Unit - WadePm(false, depthMm, settings, ref sink);
        state.WadeSlowPm = (ushort)Math.Clamp(slow, 0, CarrierSpeed.Unit);
        state.Flags = depthMm > 0 ? state.Flags | CarrierFlags.Wading : state.Flags & ~CarrierFlags.Wading;
    }
}
