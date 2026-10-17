namespace PerformativeMail.Sim.Geometry;

/// <summary>
/// The result of a segment-versus-primitive test (Throwing F4, ADR-0013 section 6): the parametric interval
/// <c>[EnterPm, ExitPm]</c> of the segment <c>P0 -&gt; P1</c> inside the primitive, in permille of the segment,
/// and the one <see cref="Hit"/> verdict. An unbounded side is <see cref="NegativeInfinityPm"/> /
/// <see cref="PositiveInfinityPm"/>; finite values beyond the int range saturate onto them (which cannot change
/// the verdict, because the rule only compares against 0 and 1000). A primitive that is missed outright is
/// <see cref="None"/>, an empty interval.
/// <para>
/// The hit rule exists exactly once, in <see cref="IsHit"/>:
/// <c>hit = tEnter &lt;= tExit and tExit &gt;= 0 and tEnter &lt;= 1000</c>, and the contact fraction is
/// <c>f = max(tEnter, 0)</c> (<see cref="FractionPm"/>). A segment that starts inside the primitive therefore has
/// <c>f = 0</c>. Pure value type, no allocation.
/// </para>
/// </summary>
/// <example>
/// <code>
/// SegmentInterval r = SegmentIntervals.Slab(in p0, in p1, in box);
/// if (r.Hit) { int f = r.FractionPm; }
/// (int enter, int exit, bool hit) = r;   // the (EnterPm, ExitPm, Hit) tuple form
/// </code>
/// </example>
public readonly struct SegmentInterval : IEquatable<SegmentInterval>
{
    /// <summary>The unbounded lower end (the primitive extends infinitely backwards along the line).</summary>
    public const int NegativeInfinityPm = int.MinValue;

    /// <summary>The unbounded upper end.</summary>
    public const int PositiveInfinityPm = int.MaxValue;

    /// <summary>The segment covers 0 to 1000 permille.</summary>
    public const int SegmentEndPm = 1000;

    /// <summary>Creates an interval from permille bounds, saturating long values into the int range and applying <see cref="IsHit"/>.</summary>
    /// <param name="enterPm">Entry, in permille of the segment (may be negative or beyond 1000).</param>
    /// <param name="exitPm">Exit, in permille of the segment.</param>
    public SegmentInterval(long enterPm, long exitPm)
    {
        EnterPm = Saturate(enterPm);
        ExitPm = Saturate(exitPm);
        Hit = IsHit(EnterPm, ExitPm);
    }

    /// <summary>The empty interval: a primitive that is never touched.</summary>
    public static SegmentInterval None => new(0L, -1L);

    /// <summary>The entry bound in permille of the segment.</summary>
    public int EnterPm { get; }

    /// <summary>The exit bound in permille of the segment.</summary>
    public int ExitPm { get; }

    /// <summary>True when the segment touches the primitive (the F4 hit rule).</summary>
    public bool Hit { get; }

    /// <summary>The contact fraction <c>max(EnterPm, 0)</c> in permille; meaningful only when <see cref="Hit"/>.</summary>
    public int FractionPm => EnterPm > 0 ? EnterPm : 0;

    /// <summary>
    /// The single hit rule of Throwing F4: <c>tEnter &lt;= tExit and tExit &gt;= 0 and tEnter &lt;= 1000</c>.
    /// </summary>
    /// <param name="enterPm">Entry in permille.</param>
    /// <param name="exitPm">Exit in permille.</param>
    /// <returns>True when the segment touches the primitive.</returns>
    /// <example>
    /// <code>bool h = SegmentInterval.IsHit(154, 2346);   // true</code>
    /// </example>
    public static bool IsHit(long enterPm, long exitPm)
    {
        return enterPm <= exitPm && exitPm >= 0 && enterPm <= SegmentEndPm;
    }

    /// <summary>Deconstructs into the <c>(EnterPm, ExitPm, Hit)</c> tuple form.</summary>
    /// <param name="enterPm">The entry.</param>
    /// <param name="exitPm">The exit.</param>
    /// <param name="hit">The verdict.</param>
    public void Deconstruct(out int enterPm, out int exitPm, out bool hit)
    {
        enterPm = EnterPm;
        exitPm = ExitPm;
        hit = Hit;
    }

    /// <inheritdoc />
    public bool Equals(SegmentInterval other) => EnterPm == other.EnterPm && ExitPm == other.ExitPm;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SegmentInterval other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            return (EnterPm * 16777619) ^ ExitPm;
        }
    }

    /// <summary>Bound-wise equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(SegmentInterval left, SegmentInterval right) => left.Equals(right);

    /// <summary>Bound-wise inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(SegmentInterval left, SegmentInterval right) => !left.Equals(right);

    private static int Saturate(long value)
    {
        if (value < int.MinValue)
        {
            return int.MinValue;
        }

        return value > int.MaxValue ? int.MaxValue : (int)value;
    }
}
