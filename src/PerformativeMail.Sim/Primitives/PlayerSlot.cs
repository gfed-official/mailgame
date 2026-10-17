namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// A player slot 0-7. Stored as slot + 1 so that <c>default</c> is <see cref="None"/>;
/// <see cref="Value"/> reads 0xFF for None. Slot assignment and reuse rules belong to the
/// <c>SlotTable</c> (a later story).
/// </summary>
/// <example>
/// <code>
/// PlayerSlot s = PlayerSlot.FromValue(7);   // s.Value == 7
/// bool none = default(PlayerSlot).IsNone;   // true, default(PlayerSlot).Value == 0xFF
/// </code>
/// </example>
public readonly struct PlayerSlot : IEquatable<PlayerSlot>
{
    /// <summary>Number of valid slots (0 ... 7).</summary>
    public const int Count = 8;

    /// <summary>The <see cref="Value"/> that denotes None.</summary>
    public const byte NoneValue = 0xFF;

    // slot + 1; 0 means None so that default(PlayerSlot) is None.
    private readonly byte _v;

    private PlayerSlot(byte stored)
    {
        _v = stored;
    }

    /// <summary>The "no slot" value; equal to <c>default</c>.</summary>
    public static PlayerSlot None => default;

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsNone => _v == 0;

    /// <summary>The slot 0-7, or 0xFF when <see cref="IsNone"/>.</summary>
    public byte Value => _v == 0 ? NoneValue : (byte)(_v - 1);

    /// <summary>
    /// Creates a slot from its external value. 0xFF yields <see cref="None"/>. A value of 8 ...
    /// 0xFE asserts under <c>SIM_ASSERT</c> and yields <see cref="None"/> otherwise.
    /// </summary>
    /// <param name="v">The slot 0-7, or 0xFF for None.</param>
    /// <returns>The slot, or None for 0xFF or an invalid value.</returns>
    /// <example>
    /// <code>PlayerSlot.FromValue(0xFF).IsNone // true</code>
    /// </example>
    public static PlayerSlot FromValue(byte v)
    {
        if (v == NoneValue)
        {
            return default;
        }

        SimAssert.That(v < Count, SimAssertCode.IdSlotRange);
        if (v >= Count)
        {
            return default;
        }

        return new PlayerSlot((byte)(v + 1));
    }

    /// <inheritdoc />
    public bool Equals(PlayerSlot other) => _v == other._v;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PlayerSlot other && _v == other._v;

    /// <inheritdoc />
    public override int GetHashCode() => (int)Value;

    /// <summary>Equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(PlayerSlot left, PlayerSlot right) => left._v == right._v;

    /// <summary>Inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(PlayerSlot left, PlayerSlot right) => left._v != right._v;
}
