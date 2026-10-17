namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Packed 32-bit lot address (ADR-0017 section 1): bits 31-26 district (6), 25-18 street (8),
/// 17-6 number (12), 5-0 unit (6, 0 = none). Numeric order of <see cref="Value"/> is the manifest
/// order (district, street, number, unit). <c>default</c> equals <see cref="None"/> (0).
/// <para>
/// Created minimal by world-stub story 001 and completed by world-stub story 004 (<see cref="Pack"/>). Component ranges:
/// district 1 to 63 (0 is reserved, so a valid address is never None), street 0 to 255, number 0 to 4095, unit 0 to 63.
/// <see cref="Pack"/> only asserts the ranges; range <i>errors</i> are reported by <c>WorldTablesBuilder.Build</c>
/// (<c>SimBuildErrorCode.AddressOutOfRange</c>). Addresses are never interned as strings (ADR-0017 section 1).
/// </para>
/// </summary>
/// <example>
/// <code>
/// AddressId a = AddressId.Pack(1, 1, 7);   // district 1, street 1, number 7, no unit
/// uint number = a.Number;                  // 7
/// bool before = a.Value &lt; AddressId.Pack(1, 2, 0).Value;   // true: numeric order is (district, street, number, unit)
/// </code>
/// </example>
public readonly struct AddressId : IEquatable<AddressId>, IComparable<AddressId>
{
    /// <summary>The raw packed encoding.</summary>
    public readonly uint Value;

    /// <summary>Wraps a raw packed value.</summary>
    /// <param name="value">The packed encoding.</param>
    public AddressId(uint value)
    {
        Value = value;
    }

    /// <summary>The "no address" id (raw 0); equal to <c>default</c>.</summary>
    public static AddressId None => default;

    /// <summary>Largest district id (6 bits).</summary>
    public const int MaxDistrict = 63;

    /// <summary>Largest street id (8 bits).</summary>
    public const int MaxStreet = 255;

    /// <summary>Largest house number (12 bits).</summary>
    public const int MaxNumber = 4095;

    /// <summary>Largest unit (6 bits).</summary>
    public const int MaxUnit = 63;

    /// <summary>
    /// Packs the components as <c>district &lt;&lt; 26 | street &lt;&lt; 18 | number &lt;&lt; 6 | unit</c>. Asserts the
    /// ranges (district 1 to 63, street 0 to 255, number 0 to 4095, unit 0 to 63) under <c>SIM_ASSERT</c>
    /// (<see cref="SimAssertCode.AddressComponentOutOfRange"/>); without it each component is masked to its field width, so an
    /// out-of-range value never spills into a neighbouring field. Callers that need range <i>errors</i> use the world builder.
    /// </summary>
    /// <param name="district">District id, 1 to 63.</param>
    /// <param name="street">Street id, 0 to 255.</param>
    /// <param name="number">House number, 0 to 4095.</param>
    /// <param name="unit">Unit, 0 to 63 (0 = none).</param>
    /// <returns>The packed address.</returns>
    /// <example><code>AddressId.Pack(63, 255, 4095, 63).Value   // 0xFFFF_FFFF</code></example>
    public static AddressId Pack(int district, int street, int number, int unit = 0)
    {
        SimAssert.That(
            district >= 1 && district <= MaxDistrict && street >= 0 && street <= MaxStreet
            && number >= 0 && number <= MaxNumber && unit >= 0 && unit <= MaxUnit,
            SimAssertCode.AddressComponentOutOfRange);
        return new AddressId(
            (((uint)district & 0x3Fu) << 26)
            | (((uint)street & 0xFFu) << 18)
            | (((uint)number & 0xFFFu) << 6)
            | ((uint)unit & 0x3Fu));
    }

    /// <summary>True when this is <see cref="None"/>.</summary>
    public bool IsNone => Value == 0u;

    /// <summary>District id (bits 26-31).</summary>
    public uint District => Value >> 26;

    /// <summary>Street id within the district (bits 18-25).</summary>
    public uint Street => (Value >> 18) & 0xFFu;

    /// <summary>House number (bits 6-17).</summary>
    public uint Number => (Value >> 6) & 0xFFFu;

    /// <summary>Unit within the house (bits 0-5); 0 = none.</summary>
    public uint Unit => Value & 0x3Fu;

    /// <inheritdoc />
    public int CompareTo(AddressId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public bool Equals(AddressId other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AddressId other && Value == other.Value;

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)Value);

    /// <summary>Equality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(AddressId left, AddressId right) => left.Value == right.Value;

    /// <summary>Inequality on <see cref="Value"/>.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(AddressId left, AddressId right) => left.Value != right.Value;
}
