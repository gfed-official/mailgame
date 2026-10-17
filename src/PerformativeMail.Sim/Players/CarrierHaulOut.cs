using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Haul-out (GDD <c>player-character-movement.md</c> P6; ADR-0013 section 5; PCM-P6-3). While Swimming, <see cref="CarrierState.HaulOutTicks"/>
/// counts consecutive ticks in which the move direction points into a walkable surface whose top is above the water plane by at most
/// <c>haulOutMm</c> and within reach (<c>capsuleRadiusMm + 100</c> mm of the face); any failing tick resets it to 0. On the tick it reaches
/// <c>haulOutTicks</c> (15) the carrier is lifted onto the top, Grounded, with the counter reset, so a push from tick 1 stands on the
/// ledge after tick 15, not 14.
/// <para>
/// Surfaces: box tops (static solids, then construct boxes, in the gather order; blocked tiles have no top) where "points into the face"
/// is <c>dot(move, closestPoint - feet) &gt; 0</c> on the xz rectangle, so diagonal input counts while it pushes into the face and
/// parallel or away input does not; and walkable terrain at the probe point <c>feet + unit(move) x reach</c>. The lift point is the feet
/// clamped into the box rectangle shrunk by <c>min(r, half extent)</c> (the probe point for terrain), and the LIFTED feet Y (the top) is
/// passed to <see cref="CarrierSweep.GroundResolve(in CarrierEnv, in CarrierScratch, int, int, int, out bool, out long, out long)"/>,
/// because box tops above the feet are not supports there (TD-084(4)). Pure, allocation-free, no statics.
/// </para>
/// </summary>
/// <example><code>CarrierHaulOut.Tick(ref state, in env, in scratch, wx, wz, hasInput);</code></example>
public static class CarrierHaulOut
{
    /// <summary>The reach slack beyond the capsule radius, mm (GDD P6 "capsule radius + 100 mm"; no balance key exists for it).</summary>
    public const int ReachSlackMm = 100;

    /// <summary>One haul-out tick for a swimmer: counts, resets or lifts.</summary>
    /// <param name="state">The swimmer, updated in place.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The tick's collider candidates.</param>
    /// <param name="wx">The tick's world move vector X.</param>
    /// <param name="wz">The tick's world move vector Z.</param>
    /// <param name="hasInput">A move input is present.</param>
    public static void Tick(ref CarrierState state, in CarrierEnv env, in CarrierScratch scratch, long wx, long wz, bool hasInput)
    {
        if (state.Loco != LocoState.Swimming
            || !hasInput
            || !TryFindLedge(in state, in env, in scratch, wx, wz, out int lx, out int lz, out int top))
        {
            state.HaulOutTicks = 0;
            return;
        }

        int need = Math.Clamp(env.Settings.HaulOutTicks, 1, byte.MaxValue);
        int count = state.HaulOutTicks + 1;
        if (count < need)
        {
            state.HaulOutTicks = (byte)count;
            return;
        }

        // The lifted feet Y goes to the support routine: HighestTopUnder only sees tops at or below the given feet.
        int support = CarrierSweep.GroundResolve(in env, in scratch, lx, top, lz, out bool walkable, out _, out _);
        if (!walkable)
        {
            state.HaulOutTicks = 0;
            return;
        }

        state.X = lx;
        state.Z = lz;
        CarrierWater.Leave(ref state, support);
    }

    /// <summary>
    /// Finds the surface the move pushes into: a box top or walkable terrain whose top lies in (plane, plane + haulOutMm], within
    /// <c>r + 100</c> mm. Returns the lift point and the top.
    /// </summary>
    /// <param name="state">The swimmer.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The tick's collider candidates.</param>
    /// <param name="wx">The world move vector X.</param>
    /// <param name="wz">The world move vector Z.</param>
    /// <param name="liftX">The lift point X.</param>
    /// <param name="liftZ">The lift point Z.</param>
    /// <param name="top">The surface top, mm.</param>
    /// <returns>True when a surface qualifies on this tick.</returns>
    public static bool TryFindLedge(in CarrierState state, in CarrierEnv env, in CarrierScratch scratch, long wx, long wz, out int liftX, out int liftZ, out int top)
    {
        liftX = state.X;
        liftZ = state.Z;
        top = 0;
        if (wx == 0 && wz == 0)
        {
            return false;
        }

        PlayerSettings s = env.Settings;
        long r = Math.Max(0, s.CapsuleRadiusMm);
        long reach = r + ReachSlackMm;
        long plane = CarrierWater.WaterPlaneMm;
        long maxTop = plane + Math.Max(0, s.HaulOutMm);

        ReadOnlySpan<StaticSolid> solids = env.World.StaticSolids;
        foreach (ushort index in scratch.Solids)
        {
            if (index < solids.Length && BoxQualifies(in state, ColliderBox.From(in solids[index].Box), wx, wz, r, reach, plane, maxTop, ref liftX, ref liftZ, ref top))
            {
                return true;
            }
        }

        foreach (AabbMm b in scratch.Boxes)
        {
            if (BoxQualifies(in state, ColliderBox.From(in b), wx, wz, r, reach, plane, maxTop, ref liftX, ref liftZ, ref top))
            {
                return true;
            }
        }

        // Terrain: the probe point one reach along the move direction.
        long len = (long)Fix.ISqrt((ulong)((wx * wx) + (wz * wz)));
        if (len <= 0)
        {
            return false;
        }

        SimCounterSink none = default;
        long px = state.X + Fix.MulDiv(wx, reach, len, ref none);
        long pz = state.Z + Fix.MulDiv(wz, reach, len, ref none);
        int ix = (int)Math.Clamp(px, int.MinValue, int.MaxValue);
        int iz = (int)Math.Clamp(pz, int.MinValue, int.MaxValue);
        bool walkable = CarrierTerrain.Sample(env.World, ix, iz, s.SlopeMaxPm, out int h, out _, out _);
        if (walkable && h > plane && h <= maxTop)
        {
            liftX = ix;
            liftZ = iz;
            top = h;
            return true;
        }

        return false;
    }

    private static bool BoxQualifies(
        in CarrierState state, ColliderBox box, long wx, long wz, long r, long reach, long plane, long maxTop, ref int liftX, ref int liftZ, ref int top)
    {
        if (box.MaxY <= plane || box.MaxY > maxTop)
        {
            return false;
        }

        long ex = Math.Clamp(state.X, box.MinX, box.MaxX) - state.X;
        long ez = Math.Clamp(state.Z, box.MinZ, box.MaxZ) - state.Z;
        if ((ex * ex) + (ez * ez) > reach * reach)
        {
            return false;
        }

        // Points into the face: the move has a positive component toward the closest point (diagonal counts, parallel/away does not).
        if ((Int128)ex * wx + (Int128)ez * wz <= 0)
        {
            return false;
        }

        long insetX = Math.Min(r, (box.MaxX - box.MinX) / 2);
        long insetZ = Math.Min(r, (box.MaxZ - box.MinZ) / 2);
        liftX = (int)Math.Clamp(state.X, box.MinX + insetX, box.MaxX - insetX);
        liftZ = (int)Math.Clamp(state.Z, box.MinZ + insetZ, box.MaxZ - insetZ);
        top = (int)box.MaxY;
        return true;
    }
}
