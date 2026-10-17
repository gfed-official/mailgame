namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Ordinal of a def id in its type's ordinally-sorted list (0 ... 65 533). Stored as
/// ordinal + 1 so that <c>default</c> is <see cref="None"/> and never means ordinal 0;
/// <see cref="Value"/> reads 0xFFFF for None. Assignment belongs to content-data.
/// </summary>
/// <example>
/// <code>
/// DefIndex first = DefIndex.FromValue(0);          // first.Value == 0, not None
/// bool none = default(DefIndex).IsNone;            // true, default(DefIndex).Value == 0xFFFF
/// </code>
/// </example>
public readonly struct DefIndex : IEquatable<DefIndex>
{
    /// <summary>The <see cref="Value"/> that denotes None.</summary>
    public const ushort NoneValue = 0xFFFF;

    /// <summary>The highest valid ordinal.</summary>
    public const ushort MaxOrdinal = 0xFFFD;

    // ordinal + 1; 0 means None so that default(DefIndex) is None.
    private readonly ushort _v;

    private DefIndex(ushort stored)
    {
        _v = stored;
    }

    /// <summary>The "no def" value; equal to <c>default</c>.</summary>
    public static DefIndex None => default;

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsNone => _v == 0;

    /// <summary>The ordinal, or 0xFFFF when <see cref="IsNone"/>.</summary>
    public ushort Value => _v == 0 ? NoneValue : (ushort)(_v - 1);

    /// <summary>
    /// Creates an index from its external value. 0xFFFF yields <see cref="None"/>. A value above
    /// <see cref="MaxOrdinal"/> asserts under <c>SIM_ASSERT</c> and yields <see cref="None"/> otherwise.
    /// </summary>
    /// <param name="v">The ordinal 0 ... 65 533, or 0xFFFF for None.</param>
    /// <returns>The index, or None for 0xFFFF or an invalid value.</returns>
    /// <example>
    /// <code>DefIndex.FromValue(0xFFFF).IsNone // true</code>
    /// </example>
    public static DefIndex FromValue(ushort v)
    {
        if (v == NoneValue)
        {
            return default;
        }

        SimAssert.That(v <= MaxOrdinal, SimAssertCode.DefIndexRange);
        if (v > MaxOrdinal)
        {
            return default;
        }

        return new DefIndex((ushort)(v + 1));
    }

    /// <inheritdoc />
    public bool Equals(DefIndex other) => _v == other._v;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DefIndex other && _v == other._v;

    /// <inheritdoc />
    public override int GetHashCode() => (int)Value;

    /// <summary>Equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(DefIndex left, DefIndex right) => left._v == right._v;

    /// <summary>Inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(DefIndex left, DefIndex right) => left._v != right._v;
}
