using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Geometry;

/// <summary>
/// Throwing F4's sampled height-field march (design/gdd/throwing-catching.md F4; ADR-0013 section 6; ADR-0026
/// section 2; TR-movement-005, TR-movement-011), shared by the flight sweep and focus occlusion. Terrain height
/// comes only from <see cref="WorldHeight.At"/> (the one bilinear kernel); this class never samples it itself.
/// <para>
/// For the segment <c>P0 -&gt; P1</c>, <c>D = P1 - P0</c>:
/// <c>n = max(1, CeilDiv(|Dxz|, min(cellMm / 2, 250)), CeilDiv(|Dy|, 250))</c>; the samples are
/// <c>Q_i = P0 + RH(D * i / n)</c> for <c>i = 0 ... n</c> (one <see cref="Fix.MulDivRH"/> per component);
/// <c>gap_i = Q_i.y - H(Q_i)</c>. The contact is the first <c>i &gt;= 1</c> with <c>gap_i &lt;= 0</c> (and
/// <c>gap_{i-1} &gt; 0</c>) and
/// <c>f = CeilDiv(1000 * ((i - 1) * (gap_{i-1} - gap_i) + gap_{i-1}), n * (gap_{i-1} - gap_i))</c> in permille.
/// </para>
/// <para>
/// Decisions on points the GDD leaves open: <c>|Dxz|</c> is the Euclidean length, rounded up exactly
/// (<c>ceil(sqrt(dx^2 + dz^2))</c> by <see cref="Fix.ISqrt"/>, so no float and no estimate); a segment whose start
/// sample is already at or below the terrain (<c>gap_0 &lt;= 0</c>) is an immediate contact with <c>f = 0</c>
/// (the "starts below terrain" case); and a segment with a component beyond <see cref="MaxSegmentExtentMm"/> fails
/// <c>SimAssert</c> (<see cref="SimAssertCode.GeometryMarchRange"/>) in assert builds and, in Release, reports no
/// contact without marching, which bounds the work to about 371 samples (a 65 536 x 65 536 horizontal diagonal; flights are under 4 000 mm per tick). Callers that must not fail open pre-check with <see cref="IsWithinExtent"/>.
/// Water planes are not handled here (the flight sweep layers them). Pure, allocation-free, non-throwing.
/// </para>
/// </summary>
/// <example>
/// <code>
/// if (HeightfieldMarch.FirstContact(world, new Vec3Mm(10250, 963, 10570), new Vec3Mm(10250, 837, 10114), out int f))
/// {
///     // contact at f permille of the segment
/// }
/// </code>
/// </example>
public static class HeightfieldMarch
{
    /// <summary>The largest per-axis extent of a marched segment, in mm.</summary>
    public const int MaxSegmentExtentMm = 65_536;

    /// <summary>The upper bound of the sampling step, in mm (F4's <c>min(cellMm / 2, 250)</c>).</summary>
    public const int MaxStepMm = 250;

    private const long PermilleScale = 1000;

    /// <summary>Finds the first height-field contact along a segment.</summary>
    /// <param name="w">The world query supplying the height field (<see cref="IWorldQuery.TileSizeMm"/> is the cell size).</param>
    /// <param name="p0">Segment start.</param>
    /// <param name="p1">Segment end.</param>
    /// <param name="fPm">The contact fraction in permille of the segment (0 to 1000); 0 when there is no contact.</param>
    /// <returns>True when the segment touches or enters the terrain.</returns>
    /// <example>
    /// <code>bool hit = HeightfieldMarch.FirstContact(world, in a, in b, out int fPm);</code>
    /// </example>
    public static bool FirstContact(IWorldQuery w, in Vec3Mm p0, in Vec3Mm p1, out int fPm)
    {
        fPm = 0;
        long dx = (long)p1.X - p0.X;
        long dy = (long)p1.Y - p0.Y;
        long dz = (long)p1.Z - p0.Z;
        bool inRange = IsWithinExtent(dx, dy, dz);
        SimAssert.That(inRange, SimAssertCode.GeometryMarchRange);
        if (!inRange)
        {
            return false;
        }

        int n = SampleCount(w.TileSizeMm, dx, dy, dz);
        long previousGap = WorldHeightGap(w, p0.X, p0.Y, p0.Z);
        if (previousGap <= 0)
        {
            return true;
        }

        for (int i = 1; i <= n; i++)
        {
            long gap = SampleGap(w, in p0, dx, dy, dz, i, n);
            if (gap <= 0)
            {
                fPm = ContactFraction(i, n, previousGap, gap);
                return true;
            }

            previousGap = gap;
        }

        return false;
    }

    /// <summary>
    /// The F4 sample count <c>n = max(1, CeilDiv(|Dxz|, min(cell / 2, 250)), CeilDiv(|Dy|, 250))</c>.
    /// </summary>
    /// <param name="cellMm">The height-field cell size.</param>
    /// <param name="dx">Segment X extent.</param>
    /// <param name="dy">Segment Y extent.</param>
    /// <param name="dz">Segment Z extent.</param>
    /// <returns>The number of intervals, at least 1.</returns>
    /// <example>
    /// <code>int n = HeightfieldMarch.SampleCount(2000, 0, -126, -456);   // 2</code>
    /// </example>
    public static int SampleCount(int cellMm, long dx, long dy, long dz)
    {
        var sink = default(SimCounterSink);
        long step = cellMm / 2 < MaxStepMm ? cellMm / 2 : MaxStepMm;
        if (step < 1)
        {
            step = 1;
        }

        long horizontalSq = (dx * dx) + (dz * dz);
        long floorLen = Fix.ISqrt((ulong)horizontalSq);
        long ceilLen = floorLen * floorLen < horizontalSq ? floorLen + 1 : floorLen;
        long nHorizontal = Fix.CeilDiv(ceilLen, step, ref sink);
        long nVertical = Fix.CeilDiv(dy < 0 ? -dy : dy, MaxStepMm, ref sink);
        long n = nHorizontal > nVertical ? nHorizontal : nVertical;
        return n < 1 ? 1 : (int)n;
    }

    /// <summary>
    /// <c>f = CeilDiv(1000 * ((i - 1) * (gapPrev - gap) + gapPrev), n * (gapPrev - gap))</c>.
    /// </summary>
    /// <param name="i">Index of the first sample at or below the terrain (1 to n).</param>
    /// <param name="n">Number of intervals.</param>
    /// <param name="gapPrev">The gap at sample <c>i - 1</c> (positive).</param>
    /// <param name="gap">The gap at sample <c>i</c> (zero or negative).</param>
    /// <returns>The contact fraction in permille.</returns>
    /// <example>
    /// <code>int f = HeightfieldMarch.ContactFraction(2, 4, 100, -50);   // 417 (TC-F4-1h)</code>
    /// </example>
    public static int ContactFraction(int i, int n, long gapPrev, long gap)
    {
        var sink = default(SimCounterSink);
        long drop = gapPrev - gap;
        long numerator = PermilleScale * (((i - 1L) * drop) + gapPrev);
        return (int)Fix.CeilDiv(numerator, n * drop, ref sink);
    }

    /// <summary>
    /// Whether a segment with these extents is inside the march contract (every component at most
    /// <see cref="MaxSegmentExtentMm"/> in magnitude). Callers pre-check with this instead of relying on the
    /// fail-open Release behaviour of <see cref="FirstContact"/>.
    /// </summary>
    /// <param name="dx">Segment X extent (mm).</param>
    /// <param name="dy">Segment Y extent (mm).</param>
    /// <param name="dz">Segment Z extent (mm).</param>
    /// <returns>True when <see cref="FirstContact"/> will march the segment.</returns>
    /// <example>
    /// <code>bool ok = HeightfieldMarch.IsWithinExtent(0, -126, -456);   // true</code>
    /// </example>
    public static bool IsWithinExtent(long dx, long dy, long dz)
    {
        return WithinAxis(dx) && WithinAxis(dy) && WithinAxis(dz);
    }

    private static bool WithinAxis(long d)
    {
        return d >= -MaxSegmentExtentMm && d <= MaxSegmentExtentMm;
    }

    private static long WorldHeightGap(IWorldQuery w, int x, int y, int z)
    {
        return (long)y - WorldHeight.At(w, x, z);
    }

    private static long SampleGap(IWorldQuery w, in Vec3Mm p0, long dx, long dy, long dz, int i, int n)
    {
        var sink = default(SimCounterSink);
        int x = (int)(p0.X + Fix.MulDivRH(dx, i, n, ref sink));
        int y = (int)(p0.Y + Fix.MulDivRH(dy, i, n, ref sink));
        int z = (int)(p0.Z + Fix.MulDivRH(dz, i, n, ref sink));
        return WorldHeightGap(w, x, y, z);
    }
}
