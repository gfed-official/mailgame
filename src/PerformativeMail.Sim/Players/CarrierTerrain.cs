using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The carrier's view of the height field (GDD <c>player-character-movement.md</c> P4, terrain half; ADR-0013 sections 4 and 6;
/// TR-movement-005): a feet-point sample of the bilinear height, the patch's analytic gradient at that point and the exact slope test.
/// The capsule radius plays no part against terrain. Height and gradient come from <see cref="WorldHeight"/> only (one tile lookup per
/// sample); walkability compares un-divided numerators in <see cref="Int128"/> (<see cref="WorldHeight.IsWalkableSlope"/>), never the
/// rounded permille, so 700 per mille walks and 702 does not. Pure, allocation-free, no statics.
/// </summary>
/// <example>
/// <code>
/// bool walk = CarrierTerrain.Sample(world, x, z, settings.SlopeMaxPm, out int h, out long gx, out long gz);
/// </code>
/// </example>
public static class CarrierTerrain
{
    private const long TileSize = WorldConstants.TileSizeMm;

    /// <summary>
    /// Samples the terrain under a feet point: height (mm), gradient numerators (mm x mm, slope = numerator / 2000^2) and walkability.
    /// Positions outside the map are clamped onto it, and the last tile is used at the far edge (no exception).
    /// </summary>
    /// <param name="world">The world query.</param>
    /// <param name="x">Feet X, mm.</param>
    /// <param name="z">Feet Z, mm.</param>
    /// <param name="slopeMaxPm">The largest walkable slope in permille (<c>player.slopeMaxPm</c>).</param>
    /// <param name="heightMm">The terrain height under the feet.</param>
    /// <param name="gx">Un-divided X gradient numerator.</param>
    /// <param name="gz">Un-divided Z gradient numerator.</param>
    /// <returns>True when the terrain there is walkable (<c>1000^2 (gx^2 + gz^2) &lt;= slopeMaxPm^2 x 2000^4</c>).</returns>
    /// <example><code>bool ok = CarrierTerrain.Sample(world, 7000, 1000, 700, out int h, out _, out _);</code></example>
    public static bool Sample(IWorldQuery world, int x, int z, int slopeMaxPm, out int heightMm, out long gx, out long gz)
    {
        WorldHeight.Locate(world, x, z, out HeightCorners corners, out int fx, out int fz);
        heightMm = WorldHeight.Sample(in corners, fx, fz);
        WorldHeight.Gradient(in corners, fx, fz, out gx, out gz);
        return WorldHeight.IsWalkableSlope(gx, gz, slopeMaxPm);
    }

    /// <summary>
    /// Projects a horizontal move onto the contour of a wall, perpendicular to the gradient: <c>m - g (m . g) / (g . g)</c> with one
    /// rounding (half away from zero) per component, in <see cref="Int128"/>. Used when a move would raise the feet onto non-walkable terrain.
    /// </summary>
    /// <param name="mx">Move X, mm; becomes the contour component.</param>
    /// <param name="mz">Move Z, mm; becomes the contour component.</param>
    /// <param name="gx">Gradient X numerator at the wall.</param>
    /// <param name="gz">Gradient Z numerator at the wall.</param>
    /// <returns>True when a non-zero contour move remains; false when the move pointed down or along the gradient only, the gradient is zero, or nothing is left.</returns>
    /// <example><code>bool left = CarrierTerrain.ProjectOntoContour(ref mx, ref mz, 2_808_000, 0);   // (250, 90) becomes (0, 90)</code></example>
    public static bool ProjectOntoContour(ref int mx, ref int mz, long gx, long gz)
    {
        Int128 gg = ((Int128)gx * gx) + ((Int128)gz * gz);
        Int128 dot = ((Int128)mx * gx) + ((Int128)mz * gz);
        if (gg <= 0 || dot <= 0)
        {
            return false;
        }

        mx -= (int)Fix.DivRoundHalfAway(gx * dot, gg);
        mz -= (int)Fix.DivRoundHalfAway(gz * dot, gg);
        return mx != 0 || mz != 0;
    }

    /// <summary>
    /// The horizontal run that lowers the feet by <paramref name="depthMm"/> down the fall line of a wall: <c>-g x depth x 2000^2 / (g . g)</c>
    /// per component (one rounding, half away from zero; no square root). This is how a body pressed into steep terrain by gravity
    /// slides along and down.
    /// </summary>
    /// <param name="gx">Gradient X numerator at the wall.</param>
    /// <param name="gz">Gradient Z numerator at the wall.</param>
    /// <param name="depthMm">How far the fall went below the surface, mm (not negative).</param>
    /// <param name="runX">X displacement, mm.</param>
    /// <param name="runZ">Z displacement, mm.</param>
    /// <example><code>CarrierTerrain.DownhillRun(2_808_000, 0, 14, out int rx, out int rz);   // rx = -20, rz = 0</code></example>
    public static void DownhillRun(long gx, long gz, int depthMm, out int runX, out int runZ)
    {
        Int128 gg = ((Int128)gx * gx) + ((Int128)gz * gz);
        if (gg <= 0 || depthMm <= 0)
        {
            runX = 0;
            runZ = 0;
            return;
        }

        Int128 scale = (Int128)depthMm * TileSize * TileSize;
        runX = (int)Fix.DivRoundHalfAway(-gx * scale, gg);
        runZ = (int)Fix.DivRoundHalfAway(-gz * scale, gg);
    }
}
