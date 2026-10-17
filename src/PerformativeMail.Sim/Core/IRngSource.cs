namespace PerformativeMail.Sim.Core;

/// <summary>
/// Test seam supplying raw 32-bit draws to an <see cref="RngStream"/> in place of its PCG32 generator.
/// Production code never implements this; only the internal <see cref="RngStream"/> test constructor accepts it.
/// </summary>
/// <example>
/// <code>
/// sealed class Fixed : IRngSource { public uint Next32() =&gt; 0x80000000u; }
/// var stream = new RngStream(new Fixed());
/// uint five = stream.NextInt(10);   // 5
/// </code>
/// </example>
public interface IRngSource
{
    /// <summary>Returns the next raw 32-bit draw.</summary>
    /// <returns>A 32-bit value.</returns>
    uint Next32();
}
