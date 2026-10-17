namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// The only transport-specific seam of the network adapter (ADR-0011 section 2). A transport
/// moves raw bytes on logical <see cref="NetChannel"/>s and nothing else. It never calls into the
/// Sim and never decodes: network callbacks only enqueue, and <see cref="Poll"/> delivers on the
/// main thread. Not thread-safe; all members are called from the main thread.
/// </summary>
public interface ITransport
{
    /// <summary>Delivers every pending event and message to <paramref name="sink"/>, synchronously, in arrival order.</summary>
    /// <param name="sink">The receiver of connect, disconnect and message events.</param>
    void Poll(ITransportSink sink);

    /// <summary>
    /// Queues <paramref name="bytes"/> for <paramref name="peer"/> on <paramref name="channel"/>. The
    /// bytes are copied before this call returns. A message the transport cannot accept is dropped
    /// and counted by the transport; there is no exception.
    /// </summary>
    /// <param name="peer">The destination peer.</param>
    /// <param name="channel">The logical channel.</param>
    /// <param name="bytes">The message bytes.</param>
    void Send(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> bytes);

    /// <summary>Closes the connection to <paramref name="peer"/>; later sends to it are dropped.</summary>
    /// <param name="peer">The peer to disconnect.</param>
    /// <param name="reason">Why, as reported to the other end.</param>
    void Disconnect(PeerHandle peer, DisconnectReason reason);
}
