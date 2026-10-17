namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Tunables of the client adapter (ADR-0011 section 6: numbers live in config, not code).
/// </summary>
/// <example>
/// <code>
/// var settings = ClientAdapterSettings.Default with { ReplyTimeoutMs = 3000 };
/// </code>
/// </example>
public sealed record class ClientAdapterSettings
{
    /// <summary>Largest request payload the envelope carries (the 48-byte op payload area).</summary>
    public const int MaxPayloadBytes = 48;

    /// <summary>The defaults: 16 pending requests, 5 s reply timeout.</summary>
    public static ClientAdapterSettings Default { get; } = new();

    /// <summary>
    /// Requests awaiting a reply, or answered <c>busy</c> and still resendable (Grid <c>pendingQueueMax</c>, default 16), 1 to 64
    /// (the host answers a 65th concurrent request <c>busy</c>). A send beyond it is refused.
    /// </summary>
    public int PendingQueueMax { get; init; } = 16;

    /// <summary>Milliseconds without a reply after which a request is counted as timed out (default 5000). At M-1a it is only counted; resync is M0.</summary>
    public long ReplyTimeoutMs { get; init; } = 5000;
}
