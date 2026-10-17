namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// The SplitMix64 output step used for stream derivation (ADR-0005 section 1). Stateless and thread-safe.
/// </summary>
/// <example>
/// <code>
/// ulong m = SplitMix.Mix64(0);   // 0xe220a8397b1dcdaf
/// </code>
/// </example>
public static class SplitMix
{
    /// <summary>
    /// The full SplitMix64 step, including the <c>+ 0x9E3779B97F4A7C15</c> gamma (so <c>Mix64(0)</c> is not 0).
    /// </summary>
    /// <param name="x">The input value.</param>
    /// <returns>The mixed 64-bit value.</returns>
    public static ulong Mix64(ulong x)
    {
        ulong z = unchecked(x + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return unchecked(z ^ (z >> 31));
    }
}
