using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Geometry;

/// <summary>
/// The one bilinear height-field kernel (ADR-0013 section 6, ADR-0026 section 2): terrain height,
/// un-divided gradient numerators, the exact slope test and the tile lookup. Pure, allocation-free,
/// integer-only, stateless and therefore thread-safe; part of the T2 (net8.0) golden-vector surface.
/// Nobody else may reimplement these formulas.
/// <para>
/// Edge rule: a tile-local offset is in <c>[0, S]</c>. Inside a map <see cref="At"/> uses the tile
/// containing the point (offset in <c>[0, S)</c>); at the far map edge (<c>x == TilesX * S</c> or
/// <c>z == TilesZ * S</c>) it uses the last tile with offset exactly <c>S</c>, which returns the far
/// corner heights exactly and keeps the surface continuous to the map edge. Points outside the map
/// are clamped onto it first.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var h = new HeightCorners(-1000, -1000, -200, -200);       // FX-Beach
/// int mid = WorldHeight.Sample(in h, 500, 1000);             // -600
/// WorldHeight.Gradient(in h, 500, 1000, out long gx, out long gz);
/// bool ok = WorldHeight.IsWalkableSlope(gx, gz, 700);        // slope limit comes from SimSettings
/// </code>
/// </example>
public static class WorldHeight
{
    private const long S = WorldConstants.TileSizeMm;
    private const long S2 = WorldConstants.TileAreaMm2;

    /// <summary>
    /// Bilinear height (mm) at tile-local offset (<paramref name="fx"/>, <paramref name="fz"/>):
    /// <c>num = h00(S-u)(S-v) + h10 u (S-v) + h01 (S-u) v + h11 u v</c> in <see cref="long"/>, then a
    /// single <see cref="Fix.MulDivRH"/> by <c>S^2</c> (round half away from zero, symmetric about 0).
    /// </summary>
    /// <param name="h">The tile's four corner heights.</param>
    /// <param name="fx">Offset along X inside the tile, nominally 0 to 1999 (2000 is accepted for the far map edge); clamped into 0 to 2000.</param>
    /// <param name="fz">Offset along Z inside the tile, same domain as <paramref name="fx"/>.</param>
    /// <returns>The terrain height in mm.</returns>
    /// <example>
    /// <code>
    /// var h = new HeightCorners(2, 0, 0, 0);
    /// int a = WorldHeight.Sample(in h, 1000, 1000);   // +1 (exact half 0.5 rounds away from zero)
    /// </code>
    /// </example>
    public static int Sample(in HeightCorners h, int fx, int fz)
    {
        long u = ClampOffset(fx);
        long v = ClampOffset(fz);
        long num = ((long)h.H00 * (S - u) * (S - v))
            + ((long)h.H10 * u * (S - v))
            + ((long)h.H01 * (S - u) * v)
            + ((long)h.H11 * u * v);
        var sink = default(SimCounterSink);
        return (int)Fix.MulDivRH(num, 1, S2, ref sink);
    }

    /// <summary>
    /// Un-divided gradient numerators (mm*mm; slope = numerator / S^2):
    /// <c>gx = (h10-h00)(S-fz) + (h11-h01) fz</c> and <c>gz = (h01-h00)(S-fx) + (h11-h10) fx</c>.
    /// </summary>
    /// <param name="h">The tile's four corner heights.</param>
    /// <param name="fx">Offset along X inside the tile (clamped into 0 to 2000).</param>
    /// <param name="fz">Offset along Z inside the tile (clamped into 0 to 2000).</param>
    /// <param name="gx">Numerator of the X slope.</param>
    /// <param name="gz">Numerator of the Z slope.</param>
    /// <example>
    /// <code>
    /// var h = new HeightCorners(0, 1400, 0, 1400);
    /// WorldHeight.Gradient(in h, 0, 0, out long gx, out long gz);   // gx = 1400 * 2000, gz = 0
    /// </code>
    /// </example>
    public static void Gradient(in HeightCorners h, int fx, int fz, out long gx, out long gz)
    {
        long u = ClampOffset(fx);
        long v = ClampOffset(fz);
        gx = (((long)h.H10 - h.H00) * (S - v)) + (((long)h.H11 - h.H01) * v);
        gz = (((long)h.H01 - h.H00) * (S - u)) + (((long)h.H11 - h.H10) * u);
    }

    /// <summary>
    /// Exact walkability on gradient numerators: <c>(gx^2 + gz^2) * 1000^2 &lt;= slopeMaxPm^2 * S^4</c>,
    /// evaluated in <see cref="Int128"/> (no rounding, no square root).
    /// </summary>
    /// <param name="gx">X numerator from <see cref="Gradient"/>.</param>
    /// <param name="gz">Z numerator from <see cref="Gradient"/>.</param>
    /// <param name="slopeMaxPm">Largest walkable slope in permille (from the caller's settings, never a literal here); a negative value accepts nothing.</param>
    /// <returns>True when the slope is at most <paramref name="slopeMaxPm"/>.</returns>
    /// <example>
    /// <code>
    /// bool flat = WorldHeight.IsWalkableSlope(0, 0, 700);   // true
    /// </code>
    /// </example>
    public static bool IsWalkableSlope(long gx, long gz, int slopeMaxPm)
    {
        if (slopeMaxPm < 0)
        {
            return false;
        }

        Int128 lhs = (((Int128)gx * gx) + ((Int128)gz * gz)) * 1_000_000;
        Int128 limit = (Int128)slopeMaxPm * slopeMaxPm * S2 * S2;
        return lhs <= limit;
    }

    /// <summary>
    /// Terrain height (mm) at world position (<paramref name="x"/>, <paramref name="z"/>) through a
    /// world query: clamps onto the map, looks up the tile (<see cref="Fix.FloorDiv"/>) and samples it.
    /// </summary>
    /// <param name="w">The world query supplying <see cref="IWorldQuery.GetCorners"/> and the tile counts.</param>
    /// <param name="x">World X in mm.</param>
    /// <param name="z">World Z in mm.</param>
    /// <returns>The terrain height in mm.</returns>
    /// <example>
    /// <code>
    /// int mm = WorldHeight.At(world, 12_345, 6_789);
    /// </code>
    /// </example>
    public static int At(IWorldQuery w, int x, int z)
    {
        Locate(w, x, z, out HeightCorners h, out int fx, out int fz);
        return Sample(in h, fx, fz);
    }

    /// <summary>
    /// Maps a world position to its tile's corners and tile-local offsets, applying the map clamp and
    /// the far-edge rule (see the type remarks). Shared by <see cref="At"/> and the gradient query so
    /// the lookup exists once.
    /// </summary>
    /// <param name="w">The world query.</param>
    /// <param name="x">World X in mm.</param>
    /// <param name="z">World Z in mm.</param>
    /// <param name="h">The containing tile's corners.</param>
    /// <param name="fx">Offset along X in 0 to 2000.</param>
    /// <param name="fz">Offset along Z in 0 to 2000.</param>
    internal static void Locate(IWorldQuery w, int x, int z, out HeightCorners h, out int fx, out int fz)
    {
        var sink = default(SimCounterSink);
        int tilesX = w.TilesX;
        int tilesZ = w.TilesZ;
        long cx = ClampLong(x, 0L, tilesX * S);
        long cz = ClampLong(z, 0L, tilesZ * S);
        long tx = Fix.FloorDiv(cx, S, ref sink);
        long tz = Fix.FloorDiv(cz, S, ref sink);
        if (tx > tilesX - 1)
        {
            tx = tilesX - 1;
        }

        if (tz > tilesZ - 1)
        {
            tz = tilesZ - 1;
        }

        fx = (int)(cx - (tx * S));
        fz = (int)(cz - (tz * S));
        w.GetCorners((int)tx, (int)tz, out h);
    }

    private static long ClampOffset(int f)
    {
        return ClampLong(f, 0L, S);
    }

    private static long ClampLong(long value, long min, long max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
