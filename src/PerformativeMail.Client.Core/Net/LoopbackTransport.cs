using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// In-process <see cref="ITransport"/> used for solo play and the host's own client (ADR-0011
/// section 2): the same byte path as a remote peer, with no shortcut around the codec. Create
/// the two ends with <see cref="CreatePair()"/>. Bytes sent on one end are copied into a
/// preallocated ring and handed to the other end's sink by that end's <see cref="Poll"/> and
/// never earlier; order is send order (one ring per direction, so per-channel order holds
/// trivially). Each end sees the other as the single peer <see cref="Peer"/>.
/// <para>
/// All storage is allocated by the factory. It never grows: a message above the configured
/// maximum, or one that does not fit the free ring space, is refused and counted (see the
/// <c>*Count</c> properties) and <see cref="Send"/> and <see cref="Poll"/> allocate nothing
/// afterwards. Single-threaded: both ends and their sinks run on the main thread.
/// </para>
/// <para>
/// Lifecycle: the first <see cref="Poll"/> on an end delivers <c>OnConnected</c> once. After
/// <see cref="Disconnect"/> on one end, every send from either end is dropped and counted; the
/// other end's next <see cref="Poll"/> delivers the messages that were already queued, then
/// <c>OnDisconnected</c> once. The ending end gets no callback and its later polls deliver nothing.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var (host, client) = LoopbackTransport.CreatePair();
/// client.Send(host.Peer, NetChannel.Reliable, payload);   // copied now
/// host.Poll(sink);                                         // sink.OnConnected, then sink.OnMessage
/// </code>
/// </example>
public sealed class LoopbackTransport : ITransport
{
    /// <summary>Default largest accepted message (ADR-0011 section 5: at most 64 KB intact).</summary>
    public const int DefaultMaxMessageBytes = 65_536;

    /// <summary>Default per-direction ring size in bytes (headers included).</summary>
    public const int DefaultRingCapacityBytes = 262_144;

    private const uint PeerValue = 1u;

    private readonly MessageRing _inbound;
    private readonly MessageRing _outbound;
    private readonly int _maxMessageBytes;
    private LoopbackTransport? _other;
    private bool _closed;
    private bool _closedLocally;
    private bool _connectedDelivered;
    private bool _disconnectPending;
    private DisconnectReason _disconnectReason;
    private bool _polling;

    private LoopbackTransport(MessageRing inbound, MessageRing outbound, int maxMessageBytes)
    {
        _inbound = inbound;
        _outbound = outbound;
        _maxMessageBytes = maxMessageBytes;
    }

    /// <summary>The handle under which this end sees the other end; the same value on both ends.</summary>
    public PeerHandle Peer => new PeerHandle(PeerValue);

    /// <summary>Largest payload this transport accepts, in bytes.</summary>
    public int MaxMessageBytes => _maxMessageBytes;

    /// <summary>Per-direction ring capacity in bytes, headers included.</summary>
    public int RingCapacityBytes => _outbound.CapacityBytes;

    /// <summary>True until either end calls <see cref="Disconnect"/>.</summary>
    public bool IsOpen => !_closed;

    /// <summary>Bytes queued toward this end and not yet polled (headers included).</summary>
    public int InboundPendingBytes => _inbound.UsedBytes;

    /// <summary>Messages this end accepted into the ring.</summary>
    public long SentCount { get; private set; }

    /// <summary>Messages this end delivered to a sink.</summary>
    public long DeliveredCount { get; private set; }

    /// <summary>Sends refused because the payload exceeded <see cref="MaxMessageBytes"/>.</summary>
    public long OversizeRefusedCount { get; private set; }

    /// <summary>Sends refused because the outbound ring had too little free space.</summary>
    public long RingFullRefusedCount { get; private set; }

    /// <summary>Sends dropped because the connection had been disconnected.</summary>
    public long DroppedAfterDisconnectCount { get; private set; }

    /// <summary>Sends or disconnects addressed to a handle other than <see cref="Peer"/>.</summary>
    public long UnknownPeerCount { get; private set; }

    /// <summary>Sends on a value that is not a defined <see cref="NetChannel"/>.</summary>
    public long InvalidChannelCount { get; private set; }

    /// <summary>Creates a connected pair with <see cref="DefaultRingCapacityBytes"/> and <see cref="DefaultMaxMessageBytes"/>.</summary>
    /// <returns>The two ends; what one sends the other polls.</returns>
    public static (LoopbackTransport First, LoopbackTransport Second) CreatePair()
        => CreatePair(DefaultRingCapacityBytes, DefaultMaxMessageBytes);

    /// <summary>Creates a connected pair with explicit, fixed storage (two rings of <paramref name="ringCapacityBytes"/>).</summary>
    /// <param name="ringCapacityBytes">Per-direction ring size in bytes; must hold one maximum-size message plus its header.</param>
    /// <param name="maxMessageBytes">Largest accepted payload; must be at least 0.</param>
    /// <returns>The two ends; what one sends the other polls.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A size is negative, or the ring cannot hold a maximum-size message.</exception>
    public static (LoopbackTransport First, LoopbackTransport Second) CreatePair(int ringCapacityBytes, int maxMessageBytes)
    {
        if (maxMessageBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMessageBytes), "Must not be negative.");
        }

        if (ringCapacityBytes < MessageRing.HeaderBytes || maxMessageBytes > ringCapacityBytes - MessageRing.HeaderBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ringCapacityBytes),
                "Must hold at least one maximum-size message plus its header.");
        }

        var firstToSecond = new MessageRing(ringCapacityBytes, maxMessageBytes);
        var secondToFirst = new MessageRing(ringCapacityBytes, maxMessageBytes);
        var first = new LoopbackTransport(secondToFirst, firstToSecond, maxMessageBytes);
        var second = new LoopbackTransport(firstToSecond, secondToFirst, maxMessageBytes);
        first._other = second;
        second._other = first;
        return (first, second);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delivers <c>OnConnected</c> once (first call), then every message queued when the call began
    /// (bounded, so a sink that sends cannot extend the drain), then a pending <c>OnDisconnected</c>.
    /// Re-entering <c>Poll</c> from a sink callback asserts in assert builds and is ignored otherwise.
    /// A sink that throws still consumes the message it was handling.
    /// </remarks>
    public void Poll(ITransportSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_polling)
        {
            SimAssert.Fail(NetAssertCode.PollReentered);
            return;
        }

        if (_closedLocally)
        {
            return;
        }

        _polling = true;
        try
        {
            PeerHandle peer = Peer;
            if (!_connectedDelivered)
            {
                _connectedDelivered = true;
                sink.OnConnected(peer);
            }

            int budget = _inbound.UsedBytes;
            while (budget > 0 && _inbound.TryPeekHeader(out NetChannel channel, out int length))
            {
                try
                {
                    sink.OnMessage(peer, channel, _inbound.PeekPayload(length));
                    DeliveredCount++;
                }
                finally
                {
                    _inbound.Skip(length);
                    budget -= MessageRing.HeaderBytes + length;
                }
            }

            if (_disconnectPending)
            {
                _disconnectPending = false;
                sink.OnDisconnected(peer, _disconnectReason);
            }
        }
        finally
        {
            _polling = false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Copies <paramref name="bytes"/> into the outbound ring. Dropped and counted, never thrown,
    /// when the peer is unknown, the channel is undefined, the connection is closed, the payload
    /// exceeds <see cref="MaxMessageBytes"/>, or the ring is full.
    /// </remarks>
    public void Send(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> bytes)
    {
        if (peer != Peer)
        {
            UnknownPeerCount++;
            return;
        }

        if (channel > NetChannel.Comms)
        {
            InvalidChannelCount++;
            return;
        }

        if (_closed)
        {
            DroppedAfterDisconnectCount++;
            return;
        }

        if (bytes.Length > _maxMessageBytes)
        {
            OversizeRefusedCount++;
            return;
        }

        if (!_outbound.TryWrite(channel, bytes))
        {
            RingFullRefusedCount++;
            return;
        }

        SentCount++;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Closes both directions. The other end's next <see cref="Poll"/> reports
    /// <c>OnDisconnected(Peer, reason)</c> after its already-queued messages. A repeat call, or a
    /// call on an already-closed connection, does nothing; an unknown peer is counted.
    /// </remarks>
    public void Disconnect(PeerHandle peer, DisconnectReason reason)
    {
        if (peer != Peer)
        {
            UnknownPeerCount++;
            return;
        }

        if (_closed)
        {
            return;
        }

        LoopbackTransport other = _other!;
        _closed = true;
        _closedLocally = true;
        other._closed = true;
        other._disconnectPending = true;
        other._disconnectReason = reason;
    }
}
