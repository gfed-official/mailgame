namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Networking and Session's answer to "is anyone else here?" (ADR-0002 section 4, GDD simulation-core R16). Implemented by
/// Networking and Session: <see cref="AnyRemotePeer"/> is true while any remote peer is in any handshake state (connecting,
/// joining or joined). The M-1a loopback session has no remote peers (<see cref="LoopbackPeerPresence"/>); the M0 remote
/// session fills it from its peer table. Consumed only through <see cref="SoloPausePolicy"/>, which owns the pause rule.
/// The ADR-0011 amendment that names this seam is pending (TD-111). Main thread only, not thread-safe.
/// </summary>
/// <example>
/// <code>
/// IPeerPresence presence = LoopbackPeerPresence.Instance;
/// bool alone = !presence.AnyRemotePeer;   // true on the M-1a loopback session
/// </code>
/// </example>
public interface IPeerPresence
{
    /// <summary>True while any remote peer is present in any handshake state.</summary>
    bool AnyRemotePeer { get; }
}
