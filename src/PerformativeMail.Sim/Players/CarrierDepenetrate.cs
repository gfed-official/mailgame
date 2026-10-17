using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Tick-start depenetration (GDD <c>player-character-movement.md</c> P4; ADR-0013 section 4 depenetration precedence; story 008 part B,
/// PCM-P4-2c static half). Runs right after <see cref="CarrierColliders.Gather"/>: a carrier that overlaps a candidate at the start of the
/// tick (a box appeared over it) is corrected by the precedence <i>static box: lift onto the top if it is at most
/// <see cref="LiftMaxMm"/> above the feet (x/z unchanged), else move to the nearest walkable point</i> (<see cref="IWorldQuery.TrySnapWalkable"/>).
/// A corrected carrier is Grounded at its new feet with no vertical speed, and <see cref="CarrierScratch.Depenetrations"/> counts the
/// correction once per tick. After any correction (a lift or a walkable-point move) the candidates are gathered again for the new
/// position (the move is a teleport, not a sweep; a 3000 mm lift would otherwise leave boxes above the head outside the region). The vehicle-OBB push-out branch (nearest face, whole overlap, never onto a moving roof) is unreachable until
/// Vehicles (M1): a stub guarded by the empty <see cref="ISupportTransformSource"/>. Pure, allocation-free, no statics.
/// </summary>
/// <example><code>CarrierDepenetrate.Run(ref state, ref scratch, in env);</code></example>
public static class CarrierDepenetrate
{
    /// <summary>The highest top (above the feet) a carrier is lifted onto; ADR-0013 section 4 fixes 3000 mm (no balance key exists).</summary>
    public const int LiftMaxMm = 3000;

    /// <summary>Search radius of the walkable-point fallback, in tiles (ADR-0013 does not size it; two tiles always reach a neighbour's centre).</summary>
    public const int SnapSearchTiles = 2;

    /// <summary>Resolves a tick-start overlap with the gathered candidates.</summary>
    /// <param name="state">The carrier, updated in place.</param>
    /// <param name="scratch">The tick's candidates (re-gathered after a walkable-point move); <see cref="CarrierScratch.Depenetrations"/> counted.</param>
    /// <param name="env">Read-only ports and settings.</param>
    public static void Run(ref CarrierState state, ref CarrierScratch scratch, in CarrierEnv env)
    {
        if (state.Loco is not (LocoState.Grounded or LocoState.Airborne))
        {
            return;   // water and mounts: story 009 / M1
        }

        if (!env.Supports.Vehicles.IsEmpty)
        {
            // M1 hook: vehicle-OBB push-out (nearest face, whole overlap, never onto a moving roof) goes here, before static boxes.
        }

        if (!CarrierColliders.HasAny(in scratch))
        {
            return;
        }

        long feet = state.Y;
        int budget = scratch.TileCount + scratch.SolidCount + scratch.BoxCount;
        bool corrected = false;
        for (int i = 0; i <= budget; i++)
        {
            if (!CarrierColliders.FirstOverlap(in env, in scratch, state.X, state.Y, state.Z, out ColliderBox box))
            {
                break;
            }

            corrected = true;
            if (i < budget && box.MaxY - feet <= LiftMaxMm)
            {
                state.Y = (int)box.MaxY;   // lift: x/z unchanged; a stacked box above is tried next
                continue;
            }

            state.Y = (int)feet;
            MoveToWalkable(ref state, in env, in box);
            CarrierGravity.Land(ref state, state.Y);
            scratch.Depenetrations = Saturating(scratch.Depenetrations);
            CarrierColliders.Gather(ref scratch, in env, in state);
            return;
        }

        if (corrected)
        {
            CarrierGravity.Land(ref state, state.Y);
            scratch.Depenetrations = Saturating(scratch.Depenetrations);
            CarrierColliders.Gather(ref scratch, in env, in state);   // a lift of up to 3000 mm moves the swept region too
        }
    }

    /// <summary>
    /// The walkable-point fallback: probe just outside the box's nearest vertical face (ties -x, +x, -z, +z), snap that probe to the
    /// nearest walkable tile centre; if that centre is still inside the box (or none is in range) the probe itself is used, on the terrain.
    /// </summary>
    private static void MoveToWalkable(ref CarrierState state, in CarrierEnv env, in ColliderBox box)
    {
        PlayerSettings s = env.Settings;
        long r = s.CapsuleRadiusMm;
        long x = state.X;
        long z = state.Z;
        long left = x - box.MinX;
        long right = box.MaxX - x;
        long back = z - box.MinZ;
        long front = box.MaxZ - z;
        long best = Math.Min(Math.Min(left, right), Math.Min(back, front));
        long px = x;
        long pz = z;
        if (left == best)
        {
            px = box.MinX - r;
        }
        else if (right == best)
        {
            px = box.MaxX + r;
        }
        else if (back == best)
        {
            pz = box.MinZ - r;
        }
        else
        {
            pz = box.MaxZ + r;
        }

        IWorldQuery world = env.World;
        var probe = new Vec3Mm(Clamp(px), state.Y, Clamp(pz));
        int radius = (int)Math.Min((long)Math.Max(1, world.TileSizeMm) * SnapSearchTiles, int.MaxValue);
        if (world.TrySnapWalkable(in probe, radius, out Vec3Mm snapped)
            && !CylinderVsBox.Overlaps(snapped.X, snapped.Y, snapped.Z, r, s.CapsuleHeightMm, in box))
        {
            state.X = snapped.X;
            state.Y = snapped.Y;
            state.Z = snapped.Z;
            return;
        }

        CarrierTerrain.Sample(world, probe.X, probe.Z, s.SlopeMaxPm, out int height, out _, out _);
        state.X = probe.X;
        state.Y = height;
        state.Z = probe.Z;
    }

    private static int Saturating(int v) => v == int.MaxValue ? v : v + 1;

    private static int Clamp(long v) => (int)Math.Clamp(v, int.MinValue, int.MaxValue);
}
