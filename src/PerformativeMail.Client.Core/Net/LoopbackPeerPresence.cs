namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// The <see cref="IPeerPresence"/> of the M-1a loopback session: the host is the only player, so
/// <see cref="AnyRemotePeer"/> is always false and a solo pause is always allowed. Stateless and immutable, so a single
/// shared <see cref="Instance"/> is safe on any thread.
/// </summary>
/// <example>
/// <code>
/// bool ok = SoloPausePolicy.CanPause(LoopbackPeerPresence.Instance, out string reason);   // true, reason empty
/// </code>
/// </example>
public sealed class LoopbackPeerPresence : IPeerPresence
{
    private LoopbackPeerPresence()
    {
    }

    /// <summary>The shared instance.</summary>
    public static LoopbackPeerPresence Instance { get; } = new LoopbackPeerPresence();

    /// <inheritdoc />
    public bool AnyRemotePeer => false;
}
