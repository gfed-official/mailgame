namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Why a connection ended, as reported by a transport to <see cref="ITransportSink.OnDisconnected"/>
/// or requested through <see cref="ITransport.Disconnect"/>. Application-level refusals (for
/// example a failed handshake) are answered with messages, not with these reasons. Values are
/// stable and never renumbered.
/// </summary>
public enum DisconnectReason : byte
{
    /// <summary>No reason given.</summary>
    Unspecified = 0,

    /// <summary>The peer closed the connection deliberately and cleanly.</summary>
    Requested = 1,

    /// <summary>The host removed the peer.</summary>
    Kicked = 2,

    /// <summary>The peer sent bytes the adapter cannot accept.</summary>
    ProtocolViolation = 3,

    /// <summary>The peer stopped responding.</summary>
    Timeout = 4,

    /// <summary>The underlying transport failed.</summary>
    TransportError = 5,
}
