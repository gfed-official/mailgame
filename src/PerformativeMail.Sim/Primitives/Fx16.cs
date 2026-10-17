namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Sub-millimetre fixed point (ADR-0006 section 1): a plain <c>readonly struct</c> over a
/// <see cref="long"/> <see cref="Raw"/> with 16 fractional bits, bounded to
/// [<see cref="MinRaw"/>, <see cref="MaxRaw"/>] = [-2^47, 2^47 - 65 536] so <see cref="ToMm"/>
/// always fits <see cref="int"/>. Out-of-domain results clamp and count
/// <see cref="PrimitiveCounter.FxClamped"/>. Conversion to millimetres is an arithmetic shift
/// (floor), never a division by 65 536. <c>default</c> is zero. Immutable and thread-safe.
/// </summary>
/// <example>
/// <code>
/// long[] block = new long[SimCounterSink.RequiredLength];
/// var sink = new SimCounterSink(block);
/// Fx16 pos = Fx16.FromMm(10);                              // Raw == 655 360
/// Fx16 next = Fx16.Add(pos, Fx16.FromRaw(98_304, ref sink), ref sink);
/// int mm = Fx16.ToMm(next);                                // 11
/// </code>
/// </example>
public readonly struct Fx16 : IEquatable<Fx16>, IComparable<Fx16>
{
    /// <summary>Number of fractional bits.</summary>
    public const int FractionBits = 16;

    /// <summary>Smallest raw value: -2^47 (equals <c>FromMm(int.MinValue).Raw</c>).</summary>
    public const long MinRaw = -(1L << 47);

    /// <summary>Largest raw value: 2^47 - 65 536 (equals <c>FromMm(int.MaxValue).Raw</c>).</summary>
    public const long MaxRaw = (1L << 47) - 65_536;

    private readonly long _raw;

    private Fx16(long raw)
    {
        _raw = raw;
    }

    /// <summary>The raw fixed-point value (millimetres times 65 536), always within [<see cref="MinRaw"/>, <see cref="MaxRaw"/>].</summary>
    public long Raw => _raw;

    /// <summary>
    /// Wraps a raw value, clamping to the domain and counting <see cref="PrimitiveCounter.FxClamped"/> if it was outside.
    /// </summary>
    /// <param name="raw">Raw fixed-point value.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The in-domain value.</returns>
    /// <example>
    /// <code>Fx16 f = Fx16.FromRaw(98_304, ref sink); // 1.5 mm</code>
    /// </example>
    public static Fx16 FromRaw(long raw, ref SimCounterSink sink)
    {
        if (raw > MaxRaw)
        {
            sink.Increment(PrimitiveCounter.FxClamped);
            return new Fx16(MaxRaw);
        }

        if (raw < MinRaw)
        {
            sink.Increment(PrimitiveCounter.FxClamped);
            return new Fx16(MinRaw);
        }

        return new Fx16(raw);
    }

    /// <summary>
    /// Converts whole millimetres: <c>(long)mm &lt;&lt; 16</c>, widened before the shift. Every
    /// <see cref="int"/> is in the domain, so this never clamps.
    /// </summary>
    /// <param name="mm">Millimetres.</param>
    /// <returns>The fixed-point value.</returns>
    /// <example>
    /// <code>Fx16 f = Fx16.FromMm(-3); // Raw == -196 608</code>
    /// </example>
    public static Fx16 FromMm(int mm)
    {
        return new Fx16((long)mm << FractionBits);
    }

    /// <summary>
    /// Floor conversion to millimetres: <c>(int)(Raw &gt;&gt; 16)</c> (arithmetic shift), so -1 maps to -1.
    /// </summary>
    /// <param name="fx">The fixed-point value.</param>
    /// <returns>Millimetres, rounded toward negative infinity.</returns>
    /// <example>
    /// <code>int mm = Fx16.ToMm(Fx16.FromRaw(-98_304, ref sink)); // -2</code>
    /// </example>
    public static int ToMm(Fx16 fx)
    {
        return (int)(fx._raw >> FractionBits);
    }

    /// <summary>Sum, clamped to the domain (counting <see cref="PrimitiveCounter.FxClamped"/>).</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The in-domain sum.</returns>
    /// <example>
    /// <code>Fx16 p = Fx16.Add(a, b, ref sink);</code>
    /// </example>
    public static Fx16 Add(Fx16 left, Fx16 right, ref SimCounterSink sink)
    {
        // Both operands are within +/-2^47, so the sum cannot overflow long.
        return FromRaw(left._raw + right._raw, ref sink);
    }

    /// <summary>Difference, clamped to the domain (counting <see cref="PrimitiveCounter.FxClamped"/>).</summary>
    /// <param name="left">Minuend.</param>
    /// <param name="right">Subtrahend.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The in-domain difference.</returns>
    /// <example>
    /// <code>Fx16 d = Fx16.Sub(a, b, ref sink);</code>
    /// </example>
    public static Fx16 Sub(Fx16 left, Fx16 right, ref SimCounterSink sink)
    {
        return FromRaw(left._raw - right._raw, ref sink);
    }

    /// <inheritdoc />
    public bool Equals(Fx16 other) => _raw == other._raw;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Fx16 other && _raw == other._raw;

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)_raw ^ (int)(_raw >> 32));

    /// <summary>Orders by <see cref="Raw"/> ascending.</summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns>Negative, zero or positive as this value is lower than, equal to or higher than <paramref name="other"/>.</returns>
    public int CompareTo(Fx16 other) => _raw < other._raw ? -1 : (_raw > other._raw ? 1 : 0);

    /// <summary>Equality on <see cref="Raw"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(Fx16 left, Fx16 right) => left._raw == right._raw;

    /// <summary>Inequality on <see cref="Raw"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(Fx16 left, Fx16 right) => left._raw != right._raw;
}
