using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// Stream seed derivation (ADR-0005 section 1): <c>Derive(runSeed, name, index) =
/// Mix64(Mix64(runSeed ^ Fnv1a64(name)) ^ index)</c>. Stateless and thread-safe.
/// </summary>
/// <example>
/// <code>
/// ulong d = RngDerive.Derive(1, in RngNames.Mail, 2);   // 0x120344c795c4b10e
/// ulong same = RngDerive.Derive(1, "mail"u8, 2);        // identical, from the raw name bytes
/// </code>
/// </example>
public static class RngDerive
{
    /// <summary>Derives the 64-bit seed for stream (<paramref name="nameUtf8"/>, <paramref name="index"/>) of a run.</summary>
    /// <param name="runSeed">The run seed.</param>
    /// <param name="nameUtf8">The stream name as UTF-8 bytes (use a <c>"..."u8</c> literal).</param>
    /// <param name="index">The stream index.</param>
    /// <returns>The derived 64-bit seed.</returns>
    public static ulong Derive(ulong runSeed, ReadOnlySpan<byte> nameUtf8, uint index)
        => Derive(runSeed, Fnv1a64Hash.Hash(nameUtf8), index);

    /// <summary>Derives the seed for stream (<paramref name="name"/>, <paramref name="index"/>) of a run from a registered name's precomputed hash. The normal entry point (RngRegistry uses it).</summary>
    /// <param name="runSeed">The run seed.</param>
    /// <param name="name">A name from <see cref="RngNames"/>.</param>
    /// <param name="index">The stream index (not range-checked here; <c>RngRegistry</c> checks it).</param>
    /// <returns>The derived 64-bit seed; equals the <see cref="Derive(ulong, ReadOnlySpan{byte}, uint)"/> result for the same name bytes.</returns>
    public static ulong Derive(ulong runSeed, in StreamName name, uint index)
        => Derive(runSeed, name.Fnv, index);

    /// <summary>Derives the seed from a precomputed name hash (<see cref="Fnv1a64Hash.Hash"/> of the name). Internal so callers cannot bypass <see cref="RngNames"/>.</summary>
    /// <param name="runSeed">The run seed.</param>
    /// <param name="nameHash">The FNV-1a 64 hash of the stream name.</param>
    /// <param name="index">The stream index.</param>
    /// <returns>The derived 64-bit seed.</returns>
    internal static ulong Derive(ulong runSeed, ulong nameHash, uint index)
        => SplitMix.Mix64(unchecked(SplitMix.Mix64(runSeed ^ nameHash) ^ (ulong)index));

    /// <summary>Builds the PCG32 generator for a derived seed: <c>initState = Mix64(s)</c>, <c>initSeq = Mix64(s ^ 0xDA3E39CB94B95BDB)</c>. Internal: streams are seeded only through <c>RngStream</c>.</summary>
    /// <param name="derived">The value returned by <see cref="Derive(ulong, ReadOnlySpan{byte}, uint)"/>.</param>
    /// <returns>The seeded generator.</returns>
    internal static Pcg32 Seed(ulong derived)
        => new(SplitMix.Mix64(derived), SplitMix.Mix64(derived ^ 0xDA3E39CB94B95BDBUL));
}
