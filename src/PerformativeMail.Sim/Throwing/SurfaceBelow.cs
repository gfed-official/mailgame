using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The surface directly under a point (GDD <c>design/gdd/throwing-catching.md</c> T6 "drops straight down", TIMEOUT and solid
/// landings; TC-T6-3 b): the sea-level water plane where <see cref="IWorldQuery.WaterDepthMm"/> is positive, else the terrain.
/// Solids are never consulted, so a point over a roof gives the ground below the footprint (items are never left on roofs).
/// Pure, static, allocation-free.
/// </summary>
/// <example>
/// <code>
/// ReceiverKind k = SurfaceBelow.At(world, in p, out Vec3Mm drop);   // Ground at (p.X, terrain, p.Z) or Water at y = 0
/// </code>
/// </example>
public static class SurfaceBelow
{
    /// <summary>Finds the surface under <paramref name="p"/>.</summary>
    /// <param name="w">The world.</param>
    /// <param name="p">The point, mm.</param>
    /// <param name="surface">The surface point: <c>(p.X, 0, p.Z)</c> over water, <c>(p.X, TerrainHeightMm, p.Z)</c> otherwise.</param>
    /// <returns><see cref="ReceiverKind.Water"/> or <see cref="ReceiverKind.Ground"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="w"/> is null.</exception>
    public static ReceiverKind At(IWorldQuery w, in Vec3Mm p, out Vec3Mm surface)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (w.WaterDepthMm(p.X, p.Z) > 0)
        {
            surface = new Vec3Mm(p.X, 0, p.Z);
            return ReceiverKind.Water;
        }

        surface = new Vec3Mm(p.X, (int)w.TerrainHeightMm(p.X, p.Z), p.Z);
        return ReceiverKind.Ground;
    }
}
