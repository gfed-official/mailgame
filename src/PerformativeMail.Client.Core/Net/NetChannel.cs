namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Logical network channel (ADR-0011 section 2). These ids are the adapter's vocabulary; the
/// mapping onto real transport channels belongs to the transport ADR, not to this enum.
/// Values are stable and never renumbered.
/// </summary>
public enum NetChannel : byte
{
    /// <summary>Loss-tolerant, unordered traffic (for example transform samples).</summary>
    Unreliable = 0,

    /// <summary>Reliable and ordered: requests, replies, batches, handshake.</summary>
    Reliable = 1,

    /// <summary>Large reliable transfers (join state, snapshot chunks).</summary>
    Bulk = 2,

    /// <summary>Player communication (chat, voice signalling).</summary>
    Comms = 3,
}
