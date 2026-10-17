namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// FNV-1a 64-bit hash of a UTF-8 byte sequence (ADR-0005 section 1). Stateless and thread-safe.
/// </summary>
/// <example>
/// <code>
/// ulong h = Fnv1a64Hash.Hash("mail"u8);   // 0x1f5964a2ce98072e
/// </code>
/// </example>
public static class Fnv1a64Hash
{
    /// <summary>The FNV-1a 64-bit offset basis, which is also the hash of the empty input.</summary>
    public const ulong OffsetBasis = 0xCBF29CE484222325UL;

    /// <summary>The FNV 64-bit prime.</summary>
    public const ulong Prime = 0x100000001B3UL;

    /// <summary>Hashes <paramref name="utf8"/> with FNV-1a 64. Allocation-free.</summary>
    /// <param name="utf8">The UTF-8 bytes to hash (empty is allowed).</param>
    /// <returns>The 64-bit hash.</returns>
    public static ulong Hash(ReadOnlySpan<byte> utf8)
    {
        ulong h = OffsetBasis;
        for (int i = 0; i < utf8.Length; i++)
        {
            h = unchecked((h ^ utf8[i]) * Prime);
        }

        return h;
    }
}
