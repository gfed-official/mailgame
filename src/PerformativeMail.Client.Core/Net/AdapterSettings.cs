using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Tunables of the host adapter (ADR-0011 section 6: numbers live in config, not code). Story 003 owns only the peer table
/// size; the dedupe cache, outbound queue and pacing numbers join this record in their own stories.
/// </summary>
/// <example>
/// <code>
/// var settings = AdapterSettings.Default with { MaxPeers = 4 };
/// </code>
/// </example>
public sealed record class AdapterSettings
{
    /// <summary>The defaults: an 8-row peer table (one row per player slot).</summary>
    public static AdapterSettings Default { get; } = new();

    /// <summary>
    /// Rows in the preallocated peer table, 1 to <see cref="PlayerSlot.Count"/> (default 8). Counts the loopback peer: the
    /// host plus seven remote players is the v1 maximum.
    /// </summary>
    public int MaxPeers { get; init; } = PlayerSlot.Count;

    /// <summary>
    /// Size of the outbound batch buffer in bytes (network-adapter-loopback story 004), 512 to 65 536; default 65 536
    /// (ADR-0011 section 5: messages up to 64 KB arrive intact). A peer's records for one tick that do not fit are split
    /// into further batches for the same tick, in order (counted in <c>HostAdapter.BatchSplitCount</c>).
    /// </summary>
    public int MaxBatchBytes { get; init; } = Codec.EventBatch.MaxBufferSize;
}
