namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// The one rounding toolkit (ADR-0006 section 2): multiply-then-divide with a single rounding,
/// floor/ceiling division and an integer square root. Every helper is pure, allocation-free,
/// non-throwing and thread-safe (no state). A zero divisor returns 0 and counts
/// <see cref="PrimitiveCounter.FixDivZero"/>; a result outside the signed 64-bit domain is
/// clamped to +/-(2^63 - 1) and counts <see cref="PrimitiveCounter.FixClamped"/>. Counters go
/// through the injected <see cref="SimCounterSink"/>; there are no statics.
/// <para>
/// "RH" in every GDD means <see cref="MulDivRH"/>. Throwing's floor-based RH
/// (<c>FloorDiv(2ab + c, 2c)</c>) is deliberately not here.
/// </para>
/// </summary>
/// <example>
/// <code>
/// long[] block = new long[SimCounterSink.RequiredLength];
/// var sink = new SimCounterSink(block);
/// long a = Fix.MulDiv(-7, 1, 2, ref sink);     // -3 (truncates)
/// long b = Fix.MulDivRH(-7, 1, 2, ref sink);   // -4 (half away from zero)
/// </code>
/// </example>
public static class Fix
{
    private const ulong SignedLimit = long.MaxValue;

    /// <summary>
    /// <c>trunc(a * b / c)</c> (toward zero) over an <see cref="Int128"/> intermediate.
    /// </summary>
    /// <param name="a">First factor.</param>
    /// <param name="b">Second factor.</param>
    /// <param name="c">Divisor; 0 returns 0 and counts <see cref="PrimitiveCounter.FixDivZero"/>.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The truncated quotient, clamped to +/-(2^63 - 1) (counting <see cref="PrimitiveCounter.FixClamped"/>).</returns>
    /// <example>
    /// <code>long q = Fix.MulDiv(7, 1, 2, ref sink); // 3</code>
    /// </example>
    public static long MulDiv(long a, long b, long c, ref SimCounterSink sink)
    {
        if (c == 0)
        {
            sink.Increment(PrimitiveCounter.FixDivZero);
            return 0;
        }

        Int128 quotient = (Int128)a * b / c;
        if (quotient > long.MaxValue)
        {
            sink.Increment(PrimitiveCounter.FixClamped);
            return long.MaxValue;
        }

        if (quotient < -(Int128)long.MaxValue)
        {
            sink.Increment(PrimitiveCounter.FixClamped);
            return -long.MaxValue;
        }

        return (long)quotient;
    }

    /// <summary>
    /// <c>a * b / c</c> rounded half away from zero, using the overflow-free <see cref="UInt128"/>
    /// algorithm of ADR-0006 section 2 (never <c>Math.Abs</c>, so <see cref="long.MinValue"/> is safe).
    /// </summary>
    /// <param name="a">First factor.</param>
    /// <param name="b">Second factor.</param>
    /// <param name="c">Divisor; 0 returns 0 and counts <see cref="PrimitiveCounter.FixDivZero"/>.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The rounded quotient, clamped to +/-(2^63 - 1) (counting <see cref="PrimitiveCounter.FixClamped"/>).</returns>
    /// <example>
    /// <code>
    /// long up = Fix.MulDivRH(7, 1, 2, ref sink);    // 4
    /// long down = Fix.MulDivRH(-7, 1, 2, ref sink); // -4
    /// </code>
    /// </example>
    public static long MulDivRH(long a, long b, long c, ref SimCounterSink sink)
    {
        return MulDivRHCore(a, b, c, SignedLimit, PrimitiveCounter.FixClamped, ref sink);
    }

    /// <summary>
    /// Floor division (toward negative infinity) on signed <see cref="long"/>.
    /// </summary>
    /// <param name="a">Dividend.</param>
    /// <param name="b">Divisor; 0 returns 0 and counts <see cref="PrimitiveCounter.FixDivZero"/>.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The floor of <c>a / b</c>; <c>long.MinValue / -1</c> clamps to <see cref="long.MaxValue"/> (counting <see cref="PrimitiveCounter.FixClamped"/>).</returns>
    /// <example>
    /// <code>long q = Fix.FloorDiv(-7, 2, ref sink); // -4</code>
    /// </example>
    public static long FloorDiv(long a, long b, ref SimCounterSink sink)
    {
        if (!TryDivide(a, b, out long quotient, out long remainder, ref sink))
        {
            return quotient;
        }

        if (remainder != 0 && (remainder ^ b) < 0)
        {
            quotient--;
        }

        return quotient;
    }

    /// <summary>
    /// Ceiling division (toward positive infinity) on signed <see cref="long"/>.
    /// </summary>
    /// <param name="a">Dividend.</param>
    /// <param name="b">Divisor; 0 returns 0 and counts <see cref="PrimitiveCounter.FixDivZero"/>.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The ceiling of <c>a / b</c>; <c>long.MinValue / -1</c> clamps to <see cref="long.MaxValue"/> (counting <see cref="PrimitiveCounter.FixClamped"/>).</returns>
    /// <example>
    /// <code>long q = Fix.CeilDiv(7, 2, ref sink); // 4</code>
    /// </example>
    public static long CeilDiv(long a, long b, ref SimCounterSink sink)
    {
        if (!TryDivide(a, b, out long quotient, out long remainder, ref sink))
        {
            return quotient;
        }

        if (remainder != 0 && (remainder ^ b) >= 0)
        {
            quotient++;
        }

        return quotient;
    }

    /// <summary>
    /// Integer floor square root, <c>floor(sqrt(n))</c>, by the bitwise digit-by-digit method
    /// (no floating point, no division).
    /// </summary>
    /// <param name="n">The value.</param>
    /// <returns>The largest <c>r</c> with <c>r * r &lt;= n</c>; at most 2^32 - 1.</returns>
    /// <example>
    /// <code>uint r = Fix.ISqrt(99); // 9</code>
    /// </example>
    public static uint ISqrt(ulong n)
    {
        ulong remainder = n;
        ulong root = 0;
        ulong bit = 1UL << 62;
        while (bit > remainder)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            if (remainder >= root + bit)
            {
                remainder -= root + bit;
                root = (root >> 1) + bit;
            }
            else
            {
                root >>= 1;
            }

            bit >>= 2;
        }

        return (uint)root;
    }

    /// <summary>
    /// Shared core of <see cref="MulDivRH"/> and the typed wrappers (<see cref="Cents.MulPermille"/>):
    /// the normative ADR-0006 algorithm with a caller-chosen symmetric magnitude limit and the
    /// counter to bump when it clamps.
    /// </summary>
    /// <param name="a">First factor.</param>
    /// <param name="b">Second factor.</param>
    /// <param name="c">Divisor.</param>
    /// <param name="limit">Largest allowed result magnitude; at most 2^63 - 1.</param>
    /// <param name="clampCounter">Counter bumped when the magnitude is clamped.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The half-away-from-zero quotient within <c>+/-limit</c>.</returns>
    internal static long MulDivRHCore(
        long a, long b, long c, ulong limit, PrimitiveCounter clampCounter, ref SimCounterSink sink)
    {
        if (c == 0)
        {
            sink.Increment(PrimitiveCounter.FixDivZero);
            return 0;
        }

        bool negative = (a < 0) ^ (b < 0) ^ (c < 0);
        UInt128 numerator = (UInt128)Magnitude(a) * Magnitude(b);
        UInt128 divisor = Magnitude(c);
        UInt128 quotient = numerator / divisor;
        UInt128 remainder = numerator % divisor;
        if (remainder >= divisor - remainder)
        {
            quotient += 1;
        }

        if (quotient > limit)
        {
            quotient = limit;
            sink.Increment(clampCounter);
        }

        long magnitude = (long)quotient;
        return negative ? -magnitude : magnitude;
    }

    /// <summary>
    /// <c>n / d</c> rounded half away from zero over <see cref="Int128"/>: <c>sign(n) * ((2|n| + d) / (2d))</c>,
    /// computed on the <see cref="UInt128"/> magnitude (remainder test, so <c>2|n| + d</c> is never formed and
    /// <see cref="Int128.MinValue"/> is safe; never <c>Math.Abs</c>). The Int128 counterpart of <see cref="MulDivRH"/>
    /// for F1/F5 style numerators that exceed <see cref="long"/>.
    /// <para>
    /// Precondition: <paramref name="d"/> &gt; 0. Policy for a violation: d &lt;= 0 returns 0 with no counter (no sink)
    /// and no assert, matching the long forms' policy; callers pass powers of 1000. Pure,
    /// allocation-free and thread-safe.
    /// </para>
    /// </summary>
    /// <param name="n">Numerator.</param>
    /// <param name="d">Divisor, greater than 0.</param>
    /// <returns>The half-away-from-zero quotient; 0 when <paramref name="d"/> is not positive.</returns>
    /// <example>
    /// <code>
    /// Int128 a = Fix.DivRoundHalfAway(-1500, 1000);   // -2
    /// Int128 b = Fix.DivRoundHalfAway(1499, 1000);    // 1
    /// </code>
    /// </example>
    public static Int128 DivRoundHalfAway(Int128 n, Int128 d)
    {
        if (d <= 0)
        {
            return 0;
        }

        UInt128 divisor = (UInt128)d;
        UInt128 numerator = Magnitude(n);
        UInt128 quotient = numerator / divisor;
        UInt128 remainder = numerator % divisor;
        if (remainder >= divisor - remainder)
        {
            quotient += 1;
        }

        if (n >= 0)
        {
            return (Int128)quotient;
        }

        // |n| <= 2^127 and d >= 1 give quotient <= 2^127: negate as -(q - 1) - 1 so 2^127 never needs to fit positive.
        return quotient == 0 ? Int128.Zero : -(Int128)(quotient - 1) - 1;
    }

    /// <summary>
    /// True floor division (toward negative infinity) over <see cref="Int128"/>; <c>Int128 /</c> truncates, so the
    /// quotient is corrected explicitly (<c>FloorDiv(-1, 1000) == -1</c>). Same <paramref name="d"/> &gt; 0 precondition
    /// and policy as <see cref="DivRoundHalfAway"/>.
    /// </summary>
    /// <param name="n">Numerator.</param>
    /// <param name="d">Divisor, greater than 0.</param>
    /// <returns>The floor of <c>n / d</c>; 0 when <paramref name="d"/> is not positive.</returns>
    /// <example>
    /// <code>Int128 q = Fix.FloorDiv(-1, 1000); // -1</code>
    /// </example>
    public static Int128 FloorDiv(Int128 n, Int128 d)
    {
        if (d <= 0)
        {
            return 0;
        }

        Int128 quotient = n / d;
        if (n % d < 0)
        {
            quotient--;
        }

        return quotient;
    }

    /// <summary>
    /// True ceiling division (toward positive infinity) over <see cref="Int128"/> (<c>CeilDiv(-1, 1000) == 0</c>),
    /// equal to <c>-FloorDiv(-n, d)</c> but without negating <paramref name="n"/> (so <see cref="Int128.MinValue"/>
    /// is safe). Same <paramref name="d"/> &gt; 0 precondition and policy as <see cref="DivRoundHalfAway"/>.
    /// </summary>
    /// <param name="n">Numerator.</param>
    /// <param name="d">Divisor, greater than 0.</param>
    /// <returns>The ceiling of <c>n / d</c>; 0 when <paramref name="d"/> is not positive.</returns>
    /// <example>
    /// <code>Int128 q = Fix.CeilDiv(15001, 1000); // 16</code>
    /// </example>
    public static Int128 CeilDiv(Int128 n, Int128 d)
    {
        if (d <= 0)
        {
            return 0;
        }

        Int128 quotient = n / d;
        if (n % d > 0)
        {
            quotient++;
        }

        return quotient;
    }

    /// <summary>|v| as an unsigned value; correct for <see cref="Int128.MinValue"/> and never calls <c>Math.Abs</c>.</summary>
    private static UInt128 Magnitude(Int128 v)
    {
        return v >= 0 ? (UInt128)v : (UInt128)(-(v + 1)) + 1UL;
    }

    /// <summary>|v| as an unsigned value; correct for <see cref="long.MinValue"/> and never calls <c>Math.Abs</c>.</summary>
    private static ulong Magnitude(long v)
    {
        return v >= 0 ? (ulong)v : (ulong)(-(v + 1)) + 1UL;
    }

    /// <summary>Truncating divide that handles the zero divisor and the <c>MinValue / -1</c> overflow without throwing.</summary>
    /// <returns>True when <paramref name="quotient"/>/<paramref name="remainder"/> are a real truncated division to adjust; false when <paramref name="quotient"/> is the final answer.</returns>
    private static bool TryDivide(long a, long b, out long quotient, out long remainder, ref SimCounterSink sink)
    {
        remainder = 0;
        if (b == 0)
        {
            sink.Increment(PrimitiveCounter.FixDivZero);
            quotient = 0;
            return false;
        }

        if (b == -1)
        {
            if (a == long.MinValue)
            {
                sink.Increment(PrimitiveCounter.FixClamped);
                quotient = long.MaxValue;
            }
            else
            {
                quotient = -a;
            }

            return false;
        }

        quotient = a / b;
        remainder = a % b;
        return true;
    }
}
