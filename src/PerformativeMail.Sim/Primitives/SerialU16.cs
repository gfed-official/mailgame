namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// RFC 1982 serial-number arithmetic for a wrapping <see cref="ushort"/> counter (ADR-0004 section 5). Use it instead of
/// numeric comparison for ids that wrap (flight ids, input arrival sequences). All arithmetic is
/// <c>unchecked</c> and never throws. Stateless and thread-safe.
/// <para>
/// <see cref="Diff"/> is <c>(a - b) mod 2^16</c> read as signed. <see cref="IsNewer"/> is true when
/// <see cref="Diff"/> is in <c>[1, 2^15 - 1]</c>. A pair exactly half the range apart is neither newer nor older: it
/// asserts under <c>SIM_ASSERT</c> (<see cref="SimAssertCode.SerialHalfRange"/>) and <see cref="IsNewer"/> returns
/// false in Release.
/// </para>
/// </summary>
/// <example>
/// <code>
/// bool newer = SerialU16.IsNewer(1, 65535);   // true: 1 is the value after 65535 (wrapped)
/// int back = SerialU16.Diff(0, 1);         // -1
/// </code>
/// </example>
public static class SerialU16
{
    /// <summary>The signed difference <c>(a - b) mod 2^16</c> in <c>[-2^15, 2^15 - 1]</c>.</summary>
    /// <param name="a">The first serial number.</param>
    /// <param name="b">The second serial number.</param>
    /// <returns>Positive when <paramref name="a"/> is after <paramref name="b"/>, negative when before, 0 when equal.</returns>
    public static int Diff(ushort a, ushort b)
    {
        return unchecked((short)(a - b));
    }

    /// <summary>True when <paramref name="a"/> is strictly after <paramref name="b"/> in serial order.</summary>
    /// <param name="a">The candidate newer serial number.</param>
    /// <param name="b">The reference serial number.</param>
    /// <returns><c>Diff(a, b)</c> in <c>[1, 2^15 - 1]</c>; false for equal values and for the exact half-range pair.</returns>
    public static bool IsNewer(ushort a, ushort b)
    {
        int d = Diff(a, b);
        SimAssert.That(d != -32768, SimAssertCode.SerialHalfRange);
        return d > 0;
    }
}
