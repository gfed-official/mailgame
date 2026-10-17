using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Tick phase 7 of the pure carrier step: the map-edge clamp (GDD <c>player-character-movement.md</c> P4 phase 7 and PCM-P4-2b; ADR-0013
/// section 4). After the sweep the feet are clamped <see cref="InsetMm"/> inside the map rectangle with
/// <see cref="IWorldQuery.ClampInsideMap"/>, and only the outward velocity component of each clamped axis is zeroed (the inward and
/// tangential components are kept, so a carrier keeps sliding along the edge). The fractional remainder of a clamped axis is dropped with
/// it. Pure, allocation-free, no statics.
/// </summary>
/// <example>
/// <code>
/// bool moved = CarrierBounds.Clamp(ref state, world);   // true when the feet were pushed back inside
/// </code>
/// </example>
public static class CarrierBounds
{
    /// <summary>How far inside the map rectangle the feet must rest, mm (1 m; a fixed rule of the GDD, not a tunable).</summary>
    public const int InsetMm = 1000;

    /// <summary>Clamps X and Z of <paramref name="state"/> into the map inset by <see cref="InsetMm"/> and zeroes the outward velocity of each clamped axis. Y is unchanged (the caller re-grounds).</summary>
    /// <param name="state">The carrier, updated in place.</param>
    /// <param name="world">The world query supplying the map bounds.</param>
    /// <returns>True when X or Z changed.</returns>
    /// <example><code>CarrierBounds.Clamp(ref state, env.World);</code></example>
    public static bool Clamp(ref CarrierState state, IWorldQuery world)
    {
        var p = new Vec3Mm(state.X, state.Y, state.Z);
        Vec3Mm c = world.ClampInsideMap(in p, InsetMm);
        bool moved = false;
        if (c.X != state.X)
        {
            // Pushed inward from the low side (c.X > X) cancels a negative (outward) Vx; from the high side cancels a positive one.
            if ((c.X > state.X && state.Vx < 0) || (c.X < state.X && state.Vx > 0))
            {
                state.Vx = 0;
            }

            state.X = c.X;
            state.RemX = 0;
            moved = true;
        }

        if (c.Z != state.Z)
        {
            if ((c.Z > state.Z && state.Vz < 0) || (c.Z < state.Z && state.Vz > 0))
            {
                state.Vz = 0;
            }

            state.Z = c.Z;
            state.RemZ = 0;
            moved = true;
        }

        return moved;
    }
}
