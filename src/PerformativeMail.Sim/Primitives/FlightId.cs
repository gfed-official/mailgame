namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Identity of a projectile flight: a wrapping <c>ushort</c> counter assigned in creation
/// order. Any value is valid and there is no reserved None ("no flight" is a separate bool).
/// <b>Never compare numerically</b>: ids wrap, so this type deliberately exposes no
/// <see cref="IComparable{T}"/> and no relational operators; use <c>SerialU16</c> (a later
/// story) and process flights in list (creation) order.
/// </summary>
/// <example>
/// <code>
/// var a = new FlightId(65535);
/// var b = new FlightId(0);          // the id created after a (wrapped)
/// bool same = a == b;               // false; equality only
/// </code>
/// </example>
public readonly struct FlightId : IEquatable<FlightId>
{
    /// <summary>The raw wrapping counter value.</summary>
    public readonly ushort Value;

    /// <summary>Wraps a raw counter value.</summary>
    /// <param name="value">The raw flight counter.</param>
    public FlightId(ushort value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public bool Equals(FlightId other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FlightId other && Value == other.Value;

    /// <inheritdoc />
    public override int GetHashCode() => (int)Value;

    /// <summary>Equality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(FlightId left, FlightId right) => left.Value == right.Value;

    /// <summary>Inequality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(FlightId left, FlightId right) => left.Value != right.Value;
}
