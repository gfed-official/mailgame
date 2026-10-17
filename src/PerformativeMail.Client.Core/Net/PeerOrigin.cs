namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Which of the host adapter's two transports a peer belongs to. The peer table keys its entries by
/// (<see cref="PeerOrigin"/>, <see cref="PeerHandle"/>) because handles are only unique within one transport:
/// <c>LoopbackTransport.Peer</c> is always <c>PeerHandle(1)</c>, and a remote transport may issue 1 as well (TD-016 item 1).
/// Values are stable and never renumbered.
/// </summary>
public enum PeerOrigin : byte
{
    /// <summary>The remote transport (none at M-1a). Polled first each frame.</summary>
    Remote = 0,

    /// <summary>The host's own in-process loopback transport. Polled after the remote one.</summary>
    Loopback = 1,
}
