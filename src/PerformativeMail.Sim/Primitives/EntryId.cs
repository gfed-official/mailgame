namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Identity of an inventory entry. Real ids are minted by the Sim with bit 31 clear
/// (1 ... 0x7FFF_FFFF); client temp ids are exactly
/// <c>0x8000_0000 | slot &lt;&lt; 20 | ctr</c> with <c>ctr &lt; 2^20</c> and bits 23-30 zero.
/// <c>default</c> equals <see cref="None"/> (0). Temp-counter exhaustion is ADR-0009's concern.
/// </summary>
/// <example>
/// <code>
/// EntryId temp = EntryId.Temp(PlayerSlot.FromValue(5), 0x12345);  // 0x8051_2345, IsTemp
/// EntryId real = EntryId.Real(42);                                // IsTemp == false
/// </code>
/// </example>
public readonly struct EntryId : IEquatable<EntryId>
{
    /// <summary>Bit 31: set on temp ids, clear on real ids.</summary>
    public const uint TempFlag = 0x8000_0000u;

    /// <summary>Mask of the 20-bit temp counter (bits 0-19).</summary>
    public const uint TempCounterMask = 0xF_FFFFu;

    // Bits 23-30 (reserved zero in a temp id).
    private const uint TempReservedMask = 0x7F80_0000u;

    /// <summary>The raw 32-bit encoding.</summary>
    public readonly uint Value;

    private EntryId(uint value)
    {
        Value = value;
    }

    /// <summary>The "no entry" id (raw 0); equal to <c>default</c>.</summary>
    public static EntryId None => default;

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsNone => Value == 0u;

    /// <summary>True for a client temp id (bit 31 set).</summary>
    public bool IsTemp => (Value & TempFlag) != 0u;

    /// <summary>For a temp id the owning slot (bits 20-22); <see cref="PlayerSlot.None"/> for a real id or None.</summary>
    public PlayerSlot TempSlot => IsTemp ? PlayerSlot.FromValue((byte)((Value >> 20) & 0x7u)) : default;

    /// <summary>For a temp id the 20-bit counter (bits 0-19); 0 for a real id.</summary>
    public uint TempCounter => IsTemp ? Value & TempCounterMask : 0u;

    /// <summary>
    /// Wraps a real (Sim-minted) id. Bit 31 set asserts under <c>SIM_ASSERT</c> and yields
    /// <see cref="None"/> otherwise.
    /// </summary>
    /// <param name="value">The real id, 1 ... 0x7FFF_FFFF.</param>
    /// <returns>The real id, or None if bit 31 was set.</returns>
    /// <example>
    /// <code>EntryId.Real(42).Value // 42</code>
    /// </example>
    public static EntryId Real(uint value)
    {
        SimAssert.That((value & TempFlag) == 0u, SimAssertCode.EntryRealBit31);
        if ((value & TempFlag) != 0u)
        {
            return default;
        }

        return new EntryId(value);
    }

    /// <summary>
    /// Builds a client temp id <c>0x8000_0000 | slot &lt;&lt; 20 | ctr</c>. A counter of 2^20 or more
    /// asserts under <c>SIM_ASSERT</c> and is masked to 20 bits otherwise; a None slot asserts and
    /// yields <see cref="None"/> otherwise.
    /// </summary>
    /// <param name="s">The owning player slot (0-7).</param>
    /// <param name="ctr">The per-connection temp counter, 0 ... 0xF_FFFF.</param>
    /// <returns>The temp id.</returns>
    /// <example>
    /// <code>EntryId.Temp(PlayerSlot.FromValue(5), 0x12345).Value // 0x8051_2345</code>
    /// </example>
    public static EntryId Temp(PlayerSlot s, uint ctr)
    {
        SimAssert.That(!s.IsNone, SimAssertCode.EntryTempSlotNone);
        SimAssert.That(ctr < (1u << 20), SimAssertCode.EntryTempCtrRange);
        if (s.IsNone)
        {
            return default;
        }

        return new EntryId(TempFlag | ((uint)s.Value << 20) | (ctr & TempCounterMask));
    }

    /// <summary>
    /// Wraps a raw value received from a snapshot or wire payload. A value with bit 31 clear is a
    /// real id; with bit 31 set it must be a well-formed temp id (bits 23-30 zero), else it asserts
    /// under <c>SIM_ASSERT</c> and yields <see cref="None"/> otherwise.
    /// </summary>
    /// <param name="raw">The raw encoding.</param>
    /// <returns>The id, or None for a malformed temp encoding.</returns>
    public static EntryId FromValue(uint raw)
    {
        bool wellFormed = (raw & TempFlag) == 0u || (raw & TempReservedMask) == 0u;
        SimAssert.That(wellFormed, SimAssertCode.EntryTempLayout);
        if (!wellFormed)
        {
            return default;
        }

        return new EntryId(raw);
    }

    /// <inheritdoc />
    public bool Equals(EntryId other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EntryId other && Value == other.Value;

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)Value);

    /// <summary>Equality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(EntryId left, EntryId right) => left.Value == right.Value;

    /// <summary>Inequality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(EntryId left, EntryId right) => left.Value != right.Value;
}
