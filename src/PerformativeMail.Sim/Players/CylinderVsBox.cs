using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// A collider box in the carrier's narrow phase (ADR-0013 section 4), all <see cref="long"/> mm, closed on every axis. Built from a
/// static solid / construct <see cref="AabbMm"/> or from a <see cref="World.TileFlags.Blocked"/> tile (infinite height: the y range is
/// <see cref="Unbounded"/>, so its top is never a support and the y axis never wins a push-out).
/// </summary>
/// <example><code>ColliderBox wall = ColliderBox.From(world.StaticSolids[3].Box);</code></example>
public readonly struct ColliderBox
{
    /// <summary>Half-range of an infinite-height box (far beyond any reachable coordinate, far below overflow in squared arithmetic).</summary>
    public const long Unbounded = 1L << 40;

    /// <summary>Minimum X, mm.</summary>
    public readonly long MinX;

    /// <summary>Minimum Y, mm.</summary>
    public readonly long MinY;

    /// <summary>Minimum Z, mm.</summary>
    public readonly long MinZ;

    /// <summary>Maximum X, mm.</summary>
    public readonly long MaxX;

    /// <summary>Maximum Y (the top), mm.</summary>
    public readonly long MaxY;

    /// <summary>Maximum Z, mm.</summary>
    public readonly long MaxZ;

    /// <summary>Creates a box from its corners.</summary>
    /// <param name="minX">Minimum X.</param>
    /// <param name="minY">Minimum Y.</param>
    /// <param name="minZ">Minimum Z.</param>
    /// <param name="maxX">Maximum X.</param>
    /// <param name="maxY">Maximum Y.</param>
    /// <param name="maxZ">Maximum Z.</param>
    public ColliderBox(long minX, long minY, long minZ, long maxX, long maxY, long maxZ)
    {
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
    }

    /// <summary>A finite box from an <see cref="AabbMm"/>.</summary>
    /// <param name="box">The box.</param>
    /// <returns>The collider.</returns>
    public static ColliderBox From(in AabbMm box) => new(box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z);

    /// <summary>The infinite-height box of a blocked tile's footprint.</summary>
    /// <param name="tx">Tile X.</param>
    /// <param name="tz">Tile Z.</param>
    /// <param name="tileSizeMm">Tile edge, mm.</param>
    /// <returns>The collider.</returns>
    public static ColliderBox Tile(int tx, int tz, int tileSizeMm)
    {
        long size = tileSizeMm;
        return new ColliderBox(tx * size, -Unbounded, tz * size, (tx + 1L) * size, Unbounded, (tz + 1L) * size);
    }
}

/// <summary>The axis of a push-out (<see cref="CylinderContact"/>): an exact face normal, or a corner normal (Q15).</summary>
public enum ContactAxis : byte
{
    /// <summary>No contact.</summary>
    None = 0,

    /// <summary>An X face (exact normal, sign in <see cref="CylinderContact.Sign"/>).</summary>
    X = 1,

    /// <summary>A Z face (exact normal, sign in <see cref="CylinderContact.Sign"/>).</summary>
    Z = 2,

    /// <summary>A Y face: +1 onto the top, -1 under the bottom.</summary>
    Y = 3,

    /// <summary>A vertical edge: the closest-point direction, normalised to Q15 in <see cref="CylinderContact.NxQ15"/> / <see cref="CylinderContact.NzQ15"/>.</summary>
    Corner = 4,
}

/// <summary>The minimum translation that separates the carrier cylinder from one box, and the contact normal.</summary>
public readonly struct CylinderContact
{
    /// <summary>Push-out X, mm.</summary>
    public readonly long Dx;

    /// <summary>Push-out Y, mm.</summary>
    public readonly long Dy;

    /// <summary>Push-out Z, mm.</summary>
    public readonly long Dz;

    /// <summary>The normal's kind.</summary>
    public readonly ContactAxis Axis;

    /// <summary>+1 or -1 along an exact <see cref="Axis"/> (0 for <see cref="ContactAxis.Corner"/>).</summary>
    public readonly int Sign;

    /// <summary>Corner normal X, Q15 (32768 = 1).</summary>
    public readonly int NxQ15;

    /// <summary>Corner normal Z, Q15 (32768 = 1).</summary>
    public readonly int NzQ15;

    /// <summary>Creates a contact.</summary>
    /// <param name="dx">Push X.</param>
    /// <param name="dy">Push Y.</param>
    /// <param name="dz">Push Z.</param>
    /// <param name="axis">Normal kind.</param>
    /// <param name="sign">Face sign.</param>
    /// <param name="nx">Corner normal X, Q15.</param>
    /// <param name="nz">Corner normal Z, Q15.</param>
    public CylinderContact(long dx, long dy, long dz, ContactAxis axis, int sign, int nx, int nz)
    {
        Dx = dx;
        Dy = dy;
        Dz = dz;
        Axis = axis;
        Sign = sign;
        NxQ15 = nx;
        NzQ15 = nz;
    }
}

/// <summary>
/// Carrier cylinder vs axis-aligned box (GDD <c>player-character-movement.md</c> P4; ADR-0013 section 4). The carrier is a flat-capped
/// vertical cylinder: feet <c>(x, y, z)</c>, radius <c>r</c>, height <c>h</c>. Overlap: the y ranges overlap strictly
/// (<c>y &lt; MaxY</c> and <c>y + h &gt; MinY</c>, so standing on a top or touching a bottom is not overlap) and the xz circle-rectangle
/// distance squared is <c>&lt; r^2</c>. All arithmetic is <see cref="long"/> and squared; the only root is <see cref="Fix.ISqrt"/> on a
/// corner contact. Pure, allocation-free.
/// </summary>
/// <example><code>bool hit = CylinderVsBox.Overlaps(x, y, z, 300, 1400, in box);</code></example>
public static class CylinderVsBox
{
    private const long One = 32768;

    /// <summary>True when the cylinder overlaps the box (strict on every face).</summary>
    /// <param name="x">Feet X.</param>
    /// <param name="y">Feet Y.</param>
    /// <param name="z">Feet Z.</param>
    /// <param name="r">Radius.</param>
    /// <param name="h">Height.</param>
    /// <param name="b">The box.</param>
    /// <returns>True on overlap.</returns>
    public static bool Overlaps(long x, long y, long z, long r, long h, in ColliderBox b) =>
        y < b.MaxY && y + h > b.MinY && FootprintOver(x, z, r, in b);

    /// <summary>True when the xz disc of radius <paramref name="r"/> overlaps the box's rectangle (distance squared &lt; r^2): the "under the footprint" test of the support.</summary>
    /// <param name="x">Centre X.</param>
    /// <param name="z">Centre Z.</param>
    /// <param name="r">Radius.</param>
    /// <param name="b">The box.</param>
    /// <returns>True when the footprint covers part of the rectangle.</returns>
    public static bool FootprintOver(long x, long z, long r, in ColliderBox b)
    {
        long ex = x - Math.Clamp(x, b.MinX, b.MaxX);
        long ez = z - Math.Clamp(z, b.MinZ, b.MaxZ);
        return (ex * ex) + (ez * ez) < r * r;
    }

    /// <summary>
    /// The minimum translation out of an overlapping box. Centre outside the rectangle: along the closest-point normal to distance
    /// <c>r</c> (exact axis normal on a face; on an edge the direction is normalised with <see cref="Fix.ISqrt"/> and every component is
    /// rounded away from the box, so the result is always clear). Centre inside the rectangle: the least-penetration axis, ties broken
    /// x, z, y; per horizontal axis the exit side is the one the pre-move centre (<paramref name="prevX"/>, <paramref name="prevZ"/>)
    /// lay on, the nearer face when it lay within that axis's range (anti-tunnel refinement of ADR-0013 section 4).
    /// </summary>
    /// <param name="x">Feet X.</param>
    /// <param name="y">Feet Y.</param>
    /// <param name="z">Feet Z.</param>
    /// <param name="r">Radius.</param>
    /// <param name="h">Height.</param>
    /// <param name="prevX">Centre X before the move that produced the overlap.</param>
    /// <param name="prevZ">Centre Z before the move.</param>
    /// <param name="b">The box (overlapping).</param>
    /// <returns>The push and its normal.</returns>
    /// <example><code>CylinderContact c = CylinderVsBox.Mtv(x, y, z, 300, 1400, px, pz, in box);</code></example>
    public static CylinderContact Mtv(long x, long y, long z, long r, long h, long prevX, long prevZ, in ColliderBox b)
    {
        long cx = Math.Clamp(x, b.MinX, b.MaxX);
        long cz = Math.Clamp(z, b.MinZ, b.MaxZ);
        long ex = x - cx;
        long ez = z - cz;
        if (ez == 0 && ex != 0)
        {
            int s = Math.Sign(ex);
            return new CylinderContact(cx + (s * r) - x, 0, 0, ContactAxis.X, s, s * (int)One, 0);
        }

        if (ex == 0 && ez != 0)
        {
            int s = Math.Sign(ez);
            return new CylinderContact(0, 0, cz + (s * r) - z, ContactAxis.Z, s, 0, s * (int)One);
        }

        if (ex != 0)
        {
            long d = Math.Max(1, (long)Fix.ISqrt((ulong)((ex * ex) + (ez * ez))));
            long nx = cx + DivAway(ex * r, d);
            long nz = cz + DivAway(ez * r, d);
            return new CylinderContact(nx - x, 0, nz - z, ContactAxis.Corner, 0, (int)((ex * One) / d), (int)((ez * One) / d));
        }

        // Centre inside the rectangle: least penetration, ties x, z, y.
        int sx = ExitSide(x, prevX, b.MinX, b.MaxX);
        long px = (sx < 0 ? x - b.MinX : b.MaxX - x) + r;
        int sz = ExitSide(z, prevZ, b.MinZ, b.MaxZ);
        long pz = (sz < 0 ? z - b.MinZ : b.MaxZ - z) + r;
        long up = b.MaxY - y;
        long down = y + h - b.MinY;
        int sy = up <= down ? 1 : -1;
        long py = Math.Min(up, down);
        if (px <= pz && px <= py)
        {
            return new CylinderContact(sx * px, 0, 0, ContactAxis.X, sx, sx * (int)One, 0);
        }

        if (pz <= py)
        {
            return new CylinderContact(0, 0, sz * pz, ContactAxis.Z, sz, 0, sz * (int)One);
        }

        return new CylinderContact(0, sy * py, 0, ContactAxis.Y, sy, 0, 0);
    }

    private static int ExitSide(long v, long prev, long min, long max)
    {
        if (prev < min)
        {
            return -1;
        }

        if (prev > max)
        {
            return 1;
        }

        return v - min <= max - v ? -1 : 1;
    }

    /// <summary>Quotient rounded away from zero (d &gt; 0).</summary>
    private static long DivAway(long n, long d)
    {
        long q = n / d;
        if (n % d != 0)
        {
            q += Math.Sign(n);
        }

        return q;
    }
}
