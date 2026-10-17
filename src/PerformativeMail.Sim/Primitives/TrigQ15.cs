namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Integer trigonometry on BAM angles (ADR-0006 sections 1 and 6). Sine is a table lookup in a
/// committed 4096-entry Q15 table (8 KiB) indexed by <c>bam &gt;&gt; 4</c>, with values
/// <c>round-half-away(32767 * sin(2 * pi * i / 4096))</c> generated once by
/// <c>tools/GenTrigQ15</c> and pinned by a SHA-256 test (the T2 cross-OS pin). Results are
/// Q15 in -32767 ... 32767 (so -32768 never occurs and negation is safe). Cosine is
/// <c>Sin(bam + 16384)</c>. There is no interpolation of either table. <see cref="Atan2Bam"/> is
/// the ADR-0013 section 3 addition, backed by the committed 1025-entry AtanQ15 table (see
/// AtanQ15.g.cs). Both tables are <c>ReadOnlySpan</c> literal properties (see TrigQ15.g.cs), not
/// static fields (ADR-0003 section 4). Stateless and thread-safe.
/// </summary>
/// <example>
/// <code>
/// short s = TrigQ15.Sin(16_384);                  // 32 767 (sin 90 degrees)
/// short c = TrigQ15.Cos(0);                       // 32 767 (cos 0 degrees)
/// long dx = (long)speed * TrigQ15.Cos(heading) &gt;&gt; 15;   // Q15 scale back to the speed's unit
/// </code>
/// </example>
public static partial class TrigQ15
{
    /// <summary>Number of table entries (one per 16 BAM units).</summary>
    public const int TableLength = 4096;

    /// <summary>The Q15 representation of 1.0 (the largest magnitude returned).</summary>
    public const short One = 32_767;

    /// <summary>Sine of a BAM angle as Q15.</summary>
    /// <param name="bam">The angle; 65 536 = 360 degrees. The low 4 bits are ignored.</param>
    /// <returns>A value in -32767 ... 32767.</returns>
    public static short Sin(ushort bam)
    {
        return Table[bam >> 4];
    }

    /// <summary>Cosine of a BAM angle as Q15, defined as <c>Sin(bam + 16384)</c> with BAM wrap.</summary>
    /// <param name="bam">The angle; 65 536 = 360 degrees. The low 4 bits are ignored.</param>
    /// <returns>A value in -32767 ... 32767.</returns>
    public static short Cos(ushort bam)
    {
        return Sin(Bam.Add(bam, Bam.Quarter));
    }

    /// <summary>Number of <c>AtanTable</c> entries: <c>atan(i / 1024)</c> for <c>i = 0 ... 1024</c>.</summary>
    public const int AtanTableLength = 1025;

    /// <summary>
    /// Integer <c>atan2(y, x)</c> as a BAM angle (ADR-0013 section 3). Axis convention: +X right, +Y up; the
    /// result is measured counter-clockwise from +X, so <c>(y=0, x=1)</c> is 0, <c>(1, 0)</c> is 16384.
    /// Reduction: take magnitudes, order them so <c>0 &lt;= t = min / max &lt;= 1</c>, look up the committed
    /// <c>AtanTable</c> at <c>MulDivRH(min, 1024, max)</c> (no interpolation, error at most about 6 BAM), then
    /// fold back by swap flag and quadrant. All BAM arithmetic wraps via <c>unchecked((ushort)...)</c>.
    /// <para>
    /// <c>(0, 0)</c> returns 0 and is never used for turning. <see cref="long.MinValue"/> has no positive
    /// counterpart in <see cref="long"/>; its magnitude saturates to <see cref="long.MaxValue"/> (a relative
    /// change of 2^-63, far below table resolution) so the reduction never overflows or throws in checked builds.
    /// Stateless, allocation-free, thread-safe.
    /// </para>
    /// </summary>
    /// <param name="y">Vertical component (any <see cref="long"/>).</param>
    /// <param name="x">Horizontal component (any <see cref="long"/>).</param>
    /// <returns>The angle, 65 536 = 360 degrees.</returns>
    /// <example>
    /// <code>
    /// ushort east = TrigQ15.Atan2Bam(0, 1);       // 0
    /// ushort north = TrigQ15.Atan2Bam(1, 0);      // 16384
    /// ushort ne = TrigQ15.Atan2Bam(5, 5);         // 8192
    /// ushort yaw = TrigQ15.Atan2Bam(-wx, -wz);    // body yaw for a world move vector w (ADR-0013 section 3)
    /// </code>
    /// </example>
    public static ushort Atan2Bam(long y, long x)
    {
        long ax = SaturatedAbs(x);
        long ay = SaturatedAbs(y);
        if (ax == 0 && ay == 0)
        {
            return 0;
        }

        bool swap = ay > ax;
        long min = swap ? ax : ay;
        long max = swap ? ay : ax;

        // min <= max and max > 0, so the quotient is in 0 ... 1024: no divide-by-zero and no clamp ever counts.
        SimCounterSink sink = default;
        int index = (int)Fix.MulDivRH(min, 1024, max, ref sink);
        int octant = AtanTable[index];
        int first = swap ? 16_384 - octant : octant;

        int bam;
        if (x >= 0)
        {
            bam = y >= 0 ? first : -first;
        }
        else
        {
            bam = y >= 0 ? 32_768 - first : 32_768 + first;
        }

        return unchecked((ushort)bam);
    }

    private static long SaturatedAbs(long v)
    {
        if (v >= 0)
        {
            return v;
        }

        return v == long.MinValue ? long.MaxValue : -v;
    }
}
