namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// Receiver of transport events. A transport calls these only from inside
/// <see cref="ITransport.Poll"/>, on the main thread, never from a network callback (TR-sim-008:
/// callbacks only enqueue). Implementations apply nothing to the Sim here beyond what the
/// adapter's own rules allow.
/// </summary>
public interface ITransportSink
{
    /// <summary>A peer became connected. Delivered once per connection, before any message from it.</summary>
    /// <param name="peer">The connected peer.</param>
    void OnConnected(PeerHandle peer);

    /// <summary>A peer is gone. Delivered once, after every message the peer sent before leaving.</summary>
    /// <param name="peer">The disconnected peer.</param>
    /// <param name="reason">Why the connection ended.</param>
    void OnDisconnected(PeerHandle peer, DisconnectReason reason);

    /// <summary>
    /// A message arrived. <paramref name="payload"/> is valid only for the duration of this call
    /// (it views transport-owned storage): copy what is needed and do not retain it.
    /// </summary>
    /// <param name="peer">The sending peer.</param>
    /// <param name="channel">The logical channel the message was sent on.</param>
    /// <param name="payload">The message bytes.</param>
    void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload);
}
