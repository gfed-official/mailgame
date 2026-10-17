namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Money in cents (ADR-0006 section 1): a plain <c>readonly struct</c> bounded to +/-2^62. It is
/// deliberately not a <c>record struct</c> (<c>init</c>/<c>with</c> would bypass the bound) and
/// has no arithmetic operators, so the range-checked <see cref="Add"/>, <see cref="Sub"/> and
/// <see cref="MulPermille"/> are the only ways to combine amounts. Negative balances inside the
/// range are legal. <c>default</c> is zero. Immutable and thread-safe.
/// <para>
/// The range check is explicit (never <c>checked</c>): with asserts on (Debug/ReleaseAssert) a
/// violation throws <see cref="SimAssertException"/>; otherwise it saturates and counts.
/// </para>
/// </summary>
/// <example>
/// <code>
/// long[] block = new long[SimCounterSink.RequiredLength];
/// var sink = new SimCounterSink(block);
/// Cents price = Cents.FromCents(15, ref sink);
/// Cents scaled = Cents.MulPermille(price, 1500, ref sink);   // 23 (22.5 rounds away from zero)
/// Cents total = Cents.Add(price, scaled, ref sink);          // 38
/// </code>
/// </example>
public readonly struct Cents : IEquatable<Cents>, IComparable<Cents>
{
    /// <summary>Largest magnitude a <see cref="Cents"/> may hold: 2^62 (4 611 686 018 427 387 904).</summary>
    public const long Max = 1L << 62;

    /// <summary>Smallest value a <see cref="Cents"/> may hold: -2^62.</summary>
    public const long Min = -Max;

    private readonly long _value;

    private Cents(long value)
    {
        _value = value;
    }

    /// <summary>The amount in cents, always within [<see cref="Min"/>, <see cref="Max"/>].</summary>
    public long Value => _value;

    /// <summary>
    /// Wraps <paramref name="value"/> after a range check. Out of range asserts
    /// (<see cref="SimAssertCode.MoneyRange"/>) under <c>SIM_ASSERT</c>; otherwise clamps to +/-2^62 and counts
    /// <see cref="PrimitiveCounter.MoneyClamped"/>.
    /// </summary>
    /// <param name="value">Amount in cents.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The money value.</returns>
    /// <example>
    /// <code>Cents wallet = Cents.FromCents(-500, ref sink); // wallet_floor</code>
    /// </example>
    public static Cents FromCents(long value, ref SimCounterSink sink)
    {
        if (value > Max || value < Min)
        {
            SimAssert.Fail(SimAssertCode.MoneyRange);
            sink.Increment(PrimitiveCounter.MoneyClamped);
            return new Cents(value > 0 ? Max : Min);
        }

        return new Cents(value);
    }

    /// <summary>
    /// Range-checked sum. A result outside +/-2^62 asserts (<see cref="SimAssertCode.MoneyOverflow"/>) under
    /// <c>SIM_ASSERT</c>; otherwise it saturates and counts <see cref="PrimitiveCounter.MoneySaturated"/>.
    /// </summary>
    /// <param name="left">Left amount.</param>
    /// <param name="right">Right amount.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The sum.</returns>
    /// <example>
    /// <code>Cents total = Cents.Add(a, b, ref sink);</code>
    /// </example>
    public static Cents Add(Cents left, Cents right, ref SimCounterSink sink)
    {
        return Saturate((Int128)left._value + right._value, ref sink);
    }

    /// <summary>
    /// Range-checked difference; same overflow behaviour as <see cref="Add"/>.
    /// </summary>
    /// <param name="left">Minuend.</param>
    /// <param name="right">Subtrahend.</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The difference.</returns>
    /// <example>
    /// <code>Cents change = Cents.Sub(paid, price, ref sink);</code>
    /// </example>
    public static Cents Sub(Cents left, Cents right, ref SimCounterSink sink)
    {
        return Saturate((Int128)left._value - right._value, ref sink);
    }

    /// <summary>
    /// The only money-rounding entry point: <c>clamp(Fix.MulDivRH(c.Value, permille, 1000), +/-2^62)</c>,
    /// half away from zero, with a clamp counted as <see cref="PrimitiveCounter.MoneyClamped"/> (not
    /// <see cref="PrimitiveCounter.FixClamped"/>).
    /// <para>
    /// Per GDD simulation-core F7 the permille ratio is in 0 to 10^6. A negative value asserts
    /// (<see cref="SimAssertCode.MoneyBadPermille"/>) under <c>SIM_ASSERT</c>; otherwise it is treated as 0
    /// and counted as <see cref="PrimitiveCounter.MoneyBadPermille"/>, so the result is zero. A negative
    /// <paramref name="amount"/> is legal and keeps its sign.
    /// </para>
    /// </summary>
    /// <param name="amount">The amount to scale.</param>
    /// <param name="permille">Ratio in permille (1000 = x1.0).</param>
    /// <param name="sink">Counter sink.</param>
    /// <returns>The scaled amount.</returns>
    /// <example>
    /// <code>Cents v = Cents.MulPermille(Cents.FromCents(336, ref sink), 1100, ref sink); // 370</code>
    /// </example>
    public static Cents MulPermille(Cents amount, int permille, ref SimCounterSink sink)
    {
        if (permille < 0)
        {
            SimAssert.That(permille >= 0, SimAssertCode.MoneyBadPermille);
            sink.Increment(PrimitiveCounter.MoneyBadPermille);
            return default;
        }

        long result = Fix.MulDivRHCore(
            amount._value, permille, 1000, (ulong)Max, PrimitiveCounter.MoneyClamped, ref sink);
        return new Cents(result);
    }

    /// <inheritdoc />
    public bool Equals(Cents other) => _value == other._value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Cents other && _value == other._value;

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)_value ^ (int)(_value >> 32));

    /// <summary>Orders by <see cref="Value"/> ascending.</summary>
    /// <param name="other">The amount to compare against.</param>
    /// <returns>Negative, zero or positive as this amount is lower than, equal to or higher than <paramref name="other"/>.</returns>
    public int CompareTo(Cents other) => _value < other._value ? -1 : (_value > other._value ? 1 : 0);

    /// <summary>Equality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(Cents left, Cents right) => left._value == right._value;

    /// <summary>Inequality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(Cents left, Cents right) => left._value != right._value;

    private static Cents Saturate(Int128 exact, ref SimCounterSink sink)
    {
        if (exact > Max || exact < Min)
        {
            SimAssert.Fail(SimAssertCode.MoneyOverflow);
            sink.Increment(PrimitiveCounter.MoneySaturated);
            return new Cents(exact > 0 ? Max : Min);
        }

        return new Cents((long)exact);
    }
}
