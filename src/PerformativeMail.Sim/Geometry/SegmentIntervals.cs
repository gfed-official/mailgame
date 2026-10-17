using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Geometry;

/// <summary>
/// Segment-versus-primitive intervals, exactly Throwing F4 (design/gdd/throwing-catching.md; ADR-0013 section 6;
/// TR-movement-001, TR-movement-011). For the segment <c>P0 -&gt; P1</c> (<c>D = P1 - P0</c>) every primitive
/// yields <c>[tEnter, tExit]</c> in permille of the segment as a <see cref="SegmentInterval"/>, whose single
/// hit rule is <c>tEnter &lt;= tExit and tExit &gt;= 0 and tEnter &lt;= 1000</c> with <c>f = max(tEnter, 0)</c>.
/// <list type="bullet">
/// <item><description>Slab (AABB): per axis with <c>D_a != 0</c>, <c>lo' = (lo - P0) * 1000</c>, <c>hi' = (hi - P0) * 1000</c>;
/// for <c>D_a &lt; 0</c> the pair is negated and swapped (so <c>|D_a|</c> divides); <c>enter = CeilDiv(lo', |D_a|)</c>,
/// <c>exit = FloorDiv(hi', |D_a|)</c>. <c>D_a = 0</c> is <c>(-inf, +inf)</c> iff <c>lo &lt;= P0 &lt;= hi</c>, else a miss.</description></item>
/// <item><description>Circle (xz) / sphere (3D), <c>R = P0 - centre</c>: <c>A = |D|^2</c>, <c>B = D.R</c>, <c>C = |R|^2 - r^2</c>,
/// <c>disc = B^2 - A*C</c>; a miss if <c>disc &lt; 0</c> or (<c>A = 0</c> and <c>C &gt; 0</c>); <c>s = ISqrt(disc)</c>,
/// <c>enter = CeilDiv(1000 * (-B - s), A)</c>, <c>exit = FloorDiv(1000 * (-B + s), A)</c>; <c>A = 0</c> and <c>C &lt;= 0</c> is
/// <c>(-inf, +inf)</c> (a point test, no division).</description></item>
/// <item><description>Cylinder: the circle interval intersected with the flat-capped y-slab (later entry, earlier exit).</description></item>
/// </list>
/// <para>
/// Broad-phase contract: the quadratic forms are only valid after the caller's broad-phase reject, which bounds
/// <c>|R| &lt;= <see cref="BroadPhaseRadiusMm"/></c> (65 536 mm). With that and <c>|D|^2 &lt;= 2^31 - 1</c>
/// (<see cref="MaxSegmentSquaredMm2"/>; flights are under 4 000 mm per tick) <c>A*C</c> and <c>B^2</c> stay below 2^63,
/// so every form is exact in <see cref="long"/>. A call outside the contract fails <c>SimAssert</c>
/// (<see cref="SimAssertCode.GeometryBroadPhase"/>) in assert builds and, in Release, returns
/// <see cref="SegmentInterval.None"/> without evaluating the form. The slab test has no quadratic and no such bound.
/// </para>
/// Pure static T2 functions: integer-only, allocation-free, non-throwing, stateless. The focus occlusion (ADR-0014)
/// and the flight sweep (ADR-0015) are the other consumers; this is the single implementation.
/// </summary>
/// <example>
/// <code>
/// // TC-F4-1: the F1 flight at tick 21 into a mailbox catch volume.
/// var box = new AabbMm(new Vec3Mm(9750, 600, 9500), new Vec3Mm(10750, 1400, 10500));
/// SegmentInterval r = SegmentIntervals.Slab(new Vec3Mm(10250, 963, 10570), new Vec3Mm(10250, 837, 10114), in box);
/// // r.Hit, r.FractionPm == 154
/// </code>
/// </example>
public static class SegmentIntervals
{
    /// <summary>The broad-phase bound on the start-to-centre distance, in mm, that the quadratic forms rely on.</summary>
    public const int BroadPhaseRadiusMm = 65_536;

    /// <summary>The largest <c>|D|^2</c> (mm squared) the quadratic forms accept, so that <c>A * C</c> stays below 2^63.</summary>
    public const long MaxSegmentSquaredMm2 = int.MaxValue;

    private const long RadiusSq = (long)BroadPhaseRadiusMm * BroadPhaseRadiusMm;
    private const long PermilleScale = 1000;

    /// <summary>The slab interval of the segment against an axis-aligned box (all three axes).</summary>
    /// <param name="p0">Segment start.</param>
    /// <param name="p1">Segment end.</param>
    /// <param name="box">The box, with <see cref="AabbMm.Min"/> and <see cref="AabbMm.Max"/> inclusive.</param>
    /// <returns>The interval and verdict; a segment starting inside the box has <c>FractionPm == 0</c>.</returns>
    /// <example>
    /// <code>SegmentInterval r = SegmentIntervals.Slab(in a, in b, in box);</code>
    /// </example>
    public static SegmentInterval Slab(in Vec3Mm p0, in Vec3Mm p1, in AabbMm box)
    {
        long enter = long.MinValue;
        long exit = long.MaxValue;
        bool ok = Axis(p0.X, (long)p1.X - p0.X, box.Min.X, box.Max.X, ref enter, ref exit)
            && Axis(p0.Y, (long)p1.Y - p0.Y, box.Min.Y, box.Max.Y, ref enter, ref exit)
            && Axis(p0.Z, (long)p1.Z - p0.Z, box.Min.Z, box.Max.Z, ref enter, ref exit);
        return ok ? new SegmentInterval(enter, exit) : SegmentInterval.None;
    }

    /// <summary>The slab interval along a single axis (for example the y-slab of a cylinder).</summary>
    /// <param name="p0">Segment start on the axis.</param>
    /// <param name="p1">Segment end on the axis.</param>
    /// <param name="lo">Lower bound of the slab.</param>
    /// <param name="hi">Upper bound of the slab.</param>
    /// <returns>The interval and verdict (<c>D = 0</c> inside the slab is unbounded, so a hit with <c>f = 0</c>).</returns>
    /// <example>
    /// <code>SegmentInterval y = SegmentIntervals.SlabAxis(2400, 1500, 0, 1800);</code>
    /// </example>
    public static SegmentInterval SlabAxis(int p0, int p1, int lo, int hi)
    {
        long enter = long.MinValue;
        long exit = long.MaxValue;
        return Axis(p0, (long)p1 - p0, lo, hi, ref enter, ref exit)
            ? new SegmentInterval(enter, exit)
            : SegmentInterval.None;
    }

    /// <summary>The interval against a circle in the xz plane (a cylinder of unbounded height); y is ignored.</summary>
    /// <param name="p0">Segment start.</param>
    /// <param name="p1">Segment end.</param>
    /// <param name="centre">Circle centre (only X and Z are used).</param>
    /// <param name="radiusMm">Radius, 0 to <see cref="BroadPhaseRadiusMm"/>.</param>
    /// <returns>The interval and verdict; see the type remarks for the broad-phase contract.</returns>
    /// <example>
    /// <code>SegmentInterval r = SegmentIntervals.Circle(in a, in b, in centre, 600);</code>
    /// </example>
    public static SegmentInterval Circle(in Vec3Mm p0, in Vec3Mm p1, in Vec3Mm centre, int radiusMm)
    {
        return CircleRaw(in p0, in p1, in centre, radiusMm, out long enter, out long exit)
            ? new SegmentInterval(enter, exit)
            : SegmentInterval.None;
    }

    /// <summary>The interval against a sphere in 3D.</summary>
    /// <param name="p0">Segment start.</param>
    /// <param name="p1">Segment end.</param>
    /// <param name="centre">Sphere centre.</param>
    /// <param name="radiusMm">Radius, 0 to <see cref="BroadPhaseRadiusMm"/>.</param>
    /// <returns>The interval and verdict; see the type remarks for the broad-phase contract.</returns>
    /// <example>
    /// <code>SegmentInterval r = SegmentIntervals.Sphere(in a, in b, in feetPlus1000, 1000);</code>
    /// </example>
    public static SegmentInterval Sphere(in Vec3Mm p0, in Vec3Mm p1, in Vec3Mm centre, int radiusMm)
    {
        long rx = (long)p0.X - centre.X;
        long ry = (long)p0.Y - centre.Y;
        long rz = (long)p0.Z - centre.Z;
        long dx = (long)p1.X - p0.X;
        long dy = (long)p1.Y - p0.Y;
        long dz = (long)p1.Z - p0.Z;
        if (!BroadPhase(rx, ry, rz, dx, dy, dz, radiusMm))
        {
            return SegmentInterval.None;
        }

        long a = (dx * dx) + (dy * dy) + (dz * dz);
        long b = (dx * rx) + (dy * ry) + (dz * rz);
        long c = (rx * rx) + (ry * ry) + (rz * rz) - ((long)radiusMm * radiusMm);
        return Quadratic(a, b, c, out long enter, out long exit)
            ? new SegmentInterval(enter, exit)
            : SegmentInterval.None;
    }

    /// <summary>
    /// The interval against a flat-capped cylinder: the xz circle interval intersected with the y-slab
    /// <c>[base.Y, base.Y + heightMm]</c> (a cylinder entered above its top takes the later entry).
    /// </summary>
    /// <param name="p0">Segment start.</param>
    /// <param name="p1">Segment end.</param>
    /// <param name="baseCentre">Centre of the bottom cap (feet position).</param>
    /// <param name="radiusMm">Radius, 0 to <see cref="BroadPhaseRadiusMm"/>.</param>
    /// <param name="heightMm">Height of the cylinder above <paramref name="baseCentre"/>.</param>
    /// <returns>The interval and verdict; see the type remarks for the broad-phase contract.</returns>
    /// <example>
    /// <code>
    /// // TC-F4-1g: goon r 300 + slack 300, height 1800, entered above its top.
    /// var r = SegmentIntervals.Cylinder(new Vec3Mm(-700, 2400, 0), new Vec3Mm(300, 1500, 0), default, 600, 1800);
    /// </code>
    /// </example>
    public static SegmentInterval Cylinder(
        in Vec3Mm p0, in Vec3Mm p1, in Vec3Mm baseCentre, int radiusMm, int heightMm)
    {
        if (!CircleRaw(in p0, in p1, in baseCentre, radiusMm, out long enter, out long exit))
        {
            return SegmentInterval.None;
        }

        long top = (long)baseCentre.Y + heightMm;
        return Axis(p0.Y, (long)p1.Y - p0.Y, baseCentre.Y, top, ref enter, ref exit)
            ? new SegmentInterval(enter, exit)
            : SegmentInterval.None;
    }

    /// <summary>
    /// Whether a quadratic-form call with these offsets is inside the broad-phase contract (the check the
    /// forms assert). Callers use it as the cheap pre-test before the quadratic.
    /// </summary>
    /// <param name="rx">Start-to-centre X offset (mm).</param>
    /// <param name="ry">Start-to-centre Y offset (mm; 0 for a circle).</param>
    /// <param name="rz">Start-to-centre Z offset (mm).</param>
    /// <param name="dx">Segment X extent (mm).</param>
    /// <param name="dy">Segment Y extent (mm; 0 for a circle).</param>
    /// <param name="dz">Segment Z extent (mm).</param>
    /// <param name="radiusMm">The primitive radius.</param>
    /// <returns>True when <c>|R| &lt;= 65 536</c>, <c>|D|^2 &lt;= 2^31 - 1</c> and the radius is in 0 to 65 536.</returns>
    /// <example>
    /// <code>bool ok = SegmentIntervals.IsWithinBroadPhase(70_000, 0, 0, 100, 0, 0, 300);   // false</code>
    /// </example>
    public static bool IsWithinBroadPhase(long rx, long ry, long rz, long dx, long dy, long dz, long radiusMm)
    {
        // Component checks come first so the squares below cannot overflow, even in checked builds.
        if (!AllWithin(rx, ry, rz) || !AllWithin(dx, dy, dz) || radiusMm < 0 || radiusMm > BroadPhaseRadiusMm)
        {
            return false;
        }

        long rSq = (rx * rx) + (ry * ry) + (rz * rz);
        long dSq = (dx * dx) + (dy * dy) + (dz * dz);
        return rSq <= RadiusSq && dSq <= MaxSegmentSquaredMm2;
    }

    private static bool AllWithin(long x, long y, long z)
    {
        return x >= -BroadPhaseRadiusMm && x <= BroadPhaseRadiusMm
            && y >= -BroadPhaseRadiusMm && y <= BroadPhaseRadiusMm
            && z >= -BroadPhaseRadiusMm && z <= BroadPhaseRadiusMm;
    }

    private static bool BroadPhase(long rx, long ry, long rz, long dx, long dy, long dz, long radiusMm)
    {
        bool ok = IsWithinBroadPhase(rx, ry, rz, dx, dy, dz, radiusMm);
        SimAssert.That(ok, SimAssertCode.GeometryBroadPhase);
        return ok;
    }

    /// <summary>One axis of the slab test, folded into the running (enter, exit); false means a certain miss.</summary>
    private static bool Axis(long p0, long d, long lo, long hi, ref long enter, ref long exit)
    {
        var sink = default(SimCounterSink);
        if (d == 0)
        {
            return lo <= p0 && p0 <= hi;
        }

        long loScaled = (lo - p0) * PermilleScale;
        long hiScaled = (hi - p0) * PermilleScale;
        long magnitude = d;
        if (d < 0)
        {
            magnitude = -d;
            (loScaled, hiScaled) = (-hiScaled, -loScaled);
        }

        long axisEnter = Fix.CeilDiv(loScaled, magnitude, ref sink);
        long axisExit = Fix.FloorDiv(hiScaled, magnitude, ref sink);
        enter = axisEnter > enter ? axisEnter : enter;
        exit = axisExit < exit ? axisExit : exit;
        return true;
    }

    /// <summary>The xz circle interval; false means a certain miss (including a failed broad phase).</summary>
    private static bool CircleRaw(
        in Vec3Mm p0, in Vec3Mm p1, in Vec3Mm centre, int radiusMm, out long enter, out long exit)
    {
        long rx = (long)p0.X - centre.X;
        long rz = (long)p0.Z - centre.Z;
        long dx = (long)p1.X - p0.X;
        long dz = (long)p1.Z - p0.Z;
        enter = 0;
        exit = -1;
        if (!BroadPhase(rx, 0, rz, dx, 0, dz, radiusMm))
        {
            return false;
        }

        long a = (dx * dx) + (dz * dz);
        long b = (dx * rx) + (dz * rz);
        long c = (rx * rx) + (rz * rz) - ((long)radiusMm * radiusMm);
        return Quadratic(a, b, c, out enter, out exit);
    }

    /// <summary>The shared F4 quadratic (circle and sphere); false means a certain miss.</summary>
    private static bool Quadratic(long a, long b, long c, out long enter, out long exit)
    {
        var sink = default(SimCounterSink);
        enter = long.MinValue;
        exit = long.MaxValue;
        if (a == 0)
        {
            return c <= 0;
        }

        long disc = (b * b) - (a * c);
        if (disc < 0)
        {
            return false;
        }

        long s = Fix.ISqrt((ulong)disc);
        enter = Fix.CeilDiv(PermilleScale * (-b - s), a, ref sink);
        exit = Fix.FloorDiv(PermilleScale * (-b + s), a, ref sink);
        return true;
    }
}
