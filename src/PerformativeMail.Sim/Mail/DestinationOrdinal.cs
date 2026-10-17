namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Dense destination index: the lot index in <c>IWorldQuery.Lots</c> (ascending <c>AddressId</c>), and the wire id map for
/// <c>destination u16</c> (ADR-0017 section 3, ADR-0026 section 6). Stored raw (0 is a valid ordinal); an absent
/// destination is reported by a failed <c>TryGet</c>, never by a sentinel. Explicit equality per ADR-0004.
/// </summary>
/// <example>
/// <code>
/// DestinationOrdinal o = new DestinationOrdinal(3);   // lot index 3
/// ushort wire = o.Value;                              // 3
/// </code>
/// </example>
public readonly struct DestinationOrdinal : IEquatable<DestinationOrdinal>, IComparable<DestinationOrdinal>
{
    /// <summary>The ordinal (lot index), 0 to 65 535.</summary>
    public readonly ushort Value;

    /// <summary>Wraps a raw ordinal.</summary>
    /// <param name="value">The lot index.</param>
    public DestinationOrdinal(ushort value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public int CompareTo(DestinationOrdinal other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public bool Equals(DestinationOrdinal other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DestinationOrdinal other && Value == other.Value;

    /// <inheritdoc />
    public override int GetHashCode() => Value;

    /// <inheritdoc />
    public override string ToString() => "dest#" + Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Equality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(DestinationOrdinal left, DestinationOrdinal right) => left.Value == right.Value;

    /// <summary>Inequality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(DestinationOrdinal left, DestinationOrdinal right) => left.Value != right.Value;
}
