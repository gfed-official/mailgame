using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The F3 response curve <c>n^curve</c> as a 1024-entry Q15 table for one <c>curvePm</c> (ADR-0012 section 5). Built once by
/// integer-only code, outside the frame: <c>x^(curve/1000)</c> is <c>x^q</c> times one repeated square root of <c>x</c> per set
/// bit of the 20-bit binary fraction of the exponent, all in Q30 with <see cref="Fix.ISqrt"/>. Every step is monotonic in
/// <c>x</c>, so the table is non-decreasing, with <c>t[0] = 0</c> and <c>t[1023] = 32767</c>. <see cref="Map"/> interpolates
/// between entries. Immutable; <see cref="Map"/> allocates 0 B.
/// </summary>
/// <example>
/// <code>
/// var curve = ResponseCurveTable.Build(1500);
/// int look = curve.Map(18432);   // about 13824 (0.5625^1.5 = 0.42)
/// </code>
/// </example>
internal sealed class ResponseCurveTable
{
    /// <summary>Entries in the table.</summary>
    public const int Length = 1024;

    private const int Last = Length - 1;
    private const int Q30Shift = 30;
    private const ulong Q30One = 1UL << Q30Shift;
    private const int FractionBits = 20;

    private readonly short[] _entries = new short[Length];

    private ResponseCurveTable(int curvePm)
    {
        CurvePm = curvePm;
    }

    /// <summary>The identity curve (1.0), which the move stick uses (GDD F3). <see cref="Map"/> returns its input.</summary>
    public static ResponseCurveTable Linear { get; } = Build(InputDefaults.MoveCurvePm);

    /// <summary>The exponent this table was built for, permille.</summary>
    public int CurvePm { get; }

    /// <summary>Entry <paramref name="i"/>: <c>round(32767 × (i / 1023)^(curvePm / 1000))</c>.</summary>
    /// <param name="i">0 to 1023.</param>
    public short this[int i] => _entries[i];

    /// <summary>Builds the table for <paramref name="curvePm"/>. Allocates; call it on a settings change, never per frame.</summary>
    /// <param name="curvePm">The exponent in permille (1000 or more).</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="curvePm"/> is below 1000.</exception>
    public static ResponseCurveTable Build(int curvePm)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(curvePm, 1000);
        var table = new ResponseCurveTable(curvePm);
        int whole = curvePm / 1000;
        long fraction = (((long)(curvePm % 1000) << FractionBits) + 500) / 1000;
        for (int i = 0; i < Length; i++)
        {
            ulong x = (((ulong)i << Q30Shift) + (Last / 2)) / Last;
            ulong p = Pow(x, whole, fraction);
            table._entries[i] = (short)(((p * (ulong)TrigQ15.One) + (Q30One / 2)) >> Q30Shift);
        }

        return table;
    }

    /// <summary>The curve at <paramref name="nQ15"/>, linearly interpolated between entries.</summary>
    /// <param name="nQ15">A magnitude 0 to 32767 (clamped).</param>
    /// <returns>The curved magnitude, 0 to 32767.</returns>
    public int Map(int nQ15)
    {
        nQ15 = Math.Clamp(nQ15, 0, (int)TrigQ15.One);
        if (CurvePm == InputDefaults.MoveCurvePm)
        {
            return nQ15;
        }

        int scaled = nQ15 * Last;
        int i = scaled / TrigQ15.One;
        if (i >= Last)
        {
            return _entries[Last];
        }

        int rem = scaled - (i * TrigQ15.One);
        int lo = _entries[i];
        return lo + ((((_entries[i + 1] - lo) * rem) + (TrigQ15.One / 2)) / TrigQ15.One);
    }

    // x^(whole + fraction / 2^20) for x in Q30, 0 <= x <= 1.0.
    private static ulong Pow(ulong x, int whole, long fraction)
    {
        ulong p = Q30One;
        for (int k = 0; k < whole; k++)
        {
            p = (p * x) >> Q30Shift;
        }

        ulong root = x;
        for (int bit = FractionBits - 1; bit >= 0; bit--)
        {
            root = Fix.ISqrt(root << Q30Shift);
            if ((fraction & (1L << bit)) != 0)
            {
                p = (p * root) >> Q30Shift;
            }
        }

        return p;
    }
}
