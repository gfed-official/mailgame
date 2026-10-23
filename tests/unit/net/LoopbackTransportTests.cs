using PerformativeMail.Client.Core.Net;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// Covers <see cref="LoopbackTransport"/> (story network-adapter-loopback/001; TR-sim-014 host
/// loopback is the same byte path as a remote peer, TR-sim-008 callbacks only enqueue so delivery
/// happens in Poll, TR-sim-042 the adapter lives outside the Sim). AC-2 = loopback semantics,
/// AC-3 = allocation and size limits; AC-1 (assembly and guards) is in AssemblyRulesTests.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class LoopbackTransportTests
{
    private const int SmallRing = 256;
    private const int SmallMax = 32;

    // ------------------------------------------------------------ helpers

    private enum EventKind
    {
        Connected,
        Message,
        Disconnected,
    }

    private readonly record struct Event(EventKind Kind, PeerHandle Peer, NetChannel Channel, byte[] Payload, DisconnectReason Reason);

    /// <summary>Records every event in order; copies payloads so they can be inspected after Poll.</summary>
    private sealed class RecordingSink : ITransportSink
    {
        public List<Event> Events { get; } = new List<Event>();

        public Action<NetChannel, byte[]>? OnMessageHook { get; set; }

        public void OnConnected(PeerHandle peer) =>
            Events.Add(new Event(EventKind.Connected, peer, default, Array.Empty<byte>(), default));

        public void OnDisconnected(PeerHandle peer, DisconnectReason reason) =>
            Events.Add(new Event(EventKind.Disconnected, peer, default, Array.Empty<byte>(), reason));

        public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload)
        {
            byte[] copy = payload.ToArray();
            Events.Add(new Event(EventKind.Message, peer, channel, copy, default));
            OnMessageHook?.Invoke(channel, copy);
        }

        public Event[] Messages() => Events.Where(e => e.Kind == EventKind.Message).ToArray();
    }

    /// <summary>Allocation-free sink for the 0 B test: folds what it sees into counters.</summary>
    private sealed class CountingSink : ITransportSink
    {
        public long Messages;
        public long Bytes;
        public long Checksum;

        public void OnConnected(PeerHandle peer)
        {
        }

        public void OnDisconnected(PeerHandle peer, DisconnectReason reason)
        {
        }

        public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload)
        {
            Messages++;
            Bytes += payload.Length;
            for (int i = 0; i < payload.Length; i++)
            {
                Checksum += payload[i];
            }
        }
    }

    private static byte[] Bytes(params byte[] values) => values;

    private static byte[] Pattern(int length, int seed)
    {
        var data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)(seed * 31 + i * 7);
        }

        return data;
    }

    // ------------------------------------------------------- pair / config

    [Fact]
    public void CreatePair_Default_ReturnsTwoOpenDistinctEndsWithDefaultLimits()
    {
        // Arrange / Act
        var (first, second) = LoopbackTransport.CreatePair();

        // Assert
        Assert.NotSame(first, second);
        Assert.True(first.IsOpen);
        Assert.True(second.IsOpen);
        Assert.Equal(LoopbackTransport.DefaultMaxMessageBytes, first.MaxMessageBytes);
        Assert.Equal(LoopbackTransport.DefaultRingCapacityBytes, second.RingCapacityBytes);
        Assert.Equal(first.Peer, second.Peer);
        Assert.False(first.Peer.IsNone);
    }

    [Fact]
    public void CreatePair_Explicit_UsesConfiguredLimitsAndStartsEmpty()
    {
        // Arrange / Act
        var (first, second) = LoopbackTransport.CreatePair(SmallRing, SmallMax);

        // Assert
        Assert.Equal(SmallMax, first.MaxMessageBytes);
        Assert.Equal(SmallRing, first.RingCapacityBytes);
        Assert.Equal(0, first.InboundPendingBytes);
        Assert.Equal(0, second.InboundPendingBytes);
        Assert.Equal(0L, first.SentCount);
        Assert.Equal(0L, second.DeliveredCount);
    }

    [Theory]
    [InlineData(100, -1)]
    [InlineData(36, 32)]
    public void CreatePair_ConfigThatCannotHoldAMaxMessage_Throws(int ringCapacity, int maxMessage)
    {
        // Arrange - a ring must hold one maximum-size message plus its 5-byte header

        // Act
        Action act = () => LoopbackTransport.CreatePair(ringCapacity, maxMessage);

        // Assert
        Assert.IsAssignableFrom<ArgumentOutOfRangeException>(Record.Exception(act));
    }

    [Fact]
    public void CreatePair_RingExactlyHoldsMaxMessageAndHeader_Accepted()
    {
        // Arrange / Act
        var (first, second) = LoopbackTransport.CreatePair(SmallMax + 5, SmallMax);
        first.Send(first.Peer, NetChannel.Reliable, Pattern(SmallMax, 1));
        var sink = new RecordingSink();
        second.Poll(sink);

        // Assert
        Assert.Equal(Pattern(SmallMax, 1), Assert.Single(sink.Messages()).Payload);
    }

    // ------------------------------------------------------------- AC-2

    [Fact]
    [Trait("Id", "AC-2")]
    public void Send_PayloadOnChannel_DeliveredByOtherEndPollWithPeerAndChannel()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();
        byte[] payload = Bytes(1, 2, 3, 4);

        // Act
        client.Send(client.Peer, NetChannel.Bulk, payload);
        host.Poll(sink);

        // Assert
        Event message = Assert.Single(sink.Messages());
        Assert.Equal(host.Peer, message.Peer);
        Assert.Equal(NetChannel.Bulk, message.Channel);
        Assert.Equal(payload, message.Payload);
        Assert.Equal(1L, client.SentCount);
        Assert.Equal(1L, host.DeliveredCount);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Send_BeforeReceiverPolls_NothingReachesAnySinkEarly()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var senderSink = new RecordingSink();
        var receiverSink = new RecordingSink();

        // Act
        client.Send(client.Peer, NetChannel.Reliable, Bytes(9));
        client.Poll(senderSink);

        // Assert - the sender's own poll never sees its message; the receiver has seen nothing yet
        Assert.Empty(senderSink.Messages());
        Assert.Empty(receiverSink.Events);
        Assert.Equal(1 + 5, host.InboundPendingBytes);

        // Act - only the receiver's Poll delivers it
        host.Poll(receiverSink);

        // Assert
        Assert.Single(receiverSink.Messages());
        Assert.Equal(0, host.InboundPendingBytes);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Send_ManyOnOneChannel_DeliveredInSendOrder()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();

        // Act
        for (byte i = 0; i < 10; i++)
        {
            client.Send(client.Peer, NetChannel.Reliable, Bytes(i, (byte)(i + 100)));
        }

        host.Poll(sink);

        // Assert
        Event[] messages = sink.Messages();
        Assert.Equal(10, messages.Length);
        for (byte i = 0; i < 10; i++)
        {
            Assert.Equal(Bytes(i, (byte)(i + 100)), messages[i].Payload);
        }
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Send_InterleavedChannels_PerChannelOrderAndChannelTagPreserved()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();
        NetChannel[] pattern =
        {
            NetChannel.Reliable, NetChannel.Unreliable, NetChannel.Comms, NetChannel.Bulk,
            NetChannel.Reliable, NetChannel.Comms, NetChannel.Unreliable, NetChannel.Bulk,
        };

        // Act
        for (int i = 0; i < pattern.Length; i++)
        {
            client.Send(client.Peer, pattern[i], Bytes((byte)i));
        }

        host.Poll(sink);

        // Assert - global send order (one ring), so every per-channel subsequence is ordered too
        Event[] messages = sink.Messages();
        Assert.Equal(pattern.Length, messages.Length);
        for (int i = 0; i < pattern.Length; i++)
        {
            Assert.Equal(pattern[i], messages[i].Channel);
            Assert.Equal((byte)i, messages[i].Payload[0]);
        }
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Send_SenderMutatesBufferAfterSend_ReceivedBytesUnchanged()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();
        byte[] buffer = Bytes(10, 20, 30);

        // Act
        client.Send(client.Peer, NetChannel.Reliable, buffer);
        buffer[0] = 99;
        buffer[1] = 99;
        buffer[2] = 99;
        host.Poll(sink);

        // Assert
        Assert.Equal(Bytes(10, 20, 30), Assert.Single(sink.Messages()).Payload);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Send_BothDirections_AreIndependentStreams()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var hostSink = new RecordingSink();
        var clientSink = new RecordingSink();

        // Act
        host.Send(host.Peer, NetChannel.Reliable, Bytes(1));
        client.Send(client.Peer, NetChannel.Reliable, Bytes(2));
        host.Poll(hostSink);
        client.Poll(clientSink);

        // Assert
        Assert.Equal(Bytes(2), Assert.Single(hostSink.Messages()).Payload);
        Assert.Equal(Bytes(1), Assert.Single(clientSink.Messages()).Payload);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Send_ZeroLengthPayload_DeliveredAsEmptySpan()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();

        // Act
        client.Send(client.Peer, NetChannel.Unreliable, ReadOnlySpan<byte>.Empty);
        host.Poll(sink);

        // Assert
        Event message = Assert.Single(sink.Messages());
        Assert.Empty(message.Payload);
        Assert.Equal(NetChannel.Unreliable, message.Channel);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Poll_FirstPollOfEachEnd_DeliversOnConnectedOnceBeforeAnyMessage()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var hostSink = new RecordingSink();
        var clientSink = new RecordingSink();
        client.Send(client.Peer, NetChannel.Reliable, Bytes(5));

        // Act
        host.Poll(hostSink);
        client.Poll(clientSink);

        // Assert - exactly one OnConnected per end, first in that end's event list
        Assert.Equal(EventKind.Connected, hostSink.Events[0].Kind);
        Assert.Equal(EventKind.Message, hostSink.Events[1].Kind);
        Assert.Equal(host.Peer, hostSink.Events[0].Peer);
        Assert.Single(clientSink.Events);
        Assert.Equal(EventKind.Connected, clientSink.Events[0].Kind);
        Assert.Equal(client.Peer, clientSink.Events[0].Peer);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Poll_SecondAndLaterPolls_DoNotRepeatOnConnected()
    {
        // Arrange
        var (host, _) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();

        // Act
        host.Poll(sink);
        host.Poll(sink);
        host.Poll(sink);

        // Assert
        Assert.Single(sink.Events);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Disconnect_OtherEndNextPoll_ReceivesOnDisconnectedWithReasonAfterQueuedMessages()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var hostSink = new RecordingSink();
        client.Send(client.Peer, NetChannel.Reliable, Bytes(1));
        client.Send(client.Peer, NetChannel.Reliable, Bytes(2));

        // Act
        client.Disconnect(client.Peer, DisconnectReason.Kicked);

        // Assert - nothing delivered until the other end polls
        Assert.Empty(hostSink.Events);
        Assert.False(host.IsOpen);
        Assert.False(client.IsOpen);

        // Act
        host.Poll(hostSink);

        // Assert - connected, both queued messages, then the disconnect with its reason
        Assert.Equal(
            new[] { EventKind.Connected, EventKind.Message, EventKind.Message, EventKind.Disconnected },
            hostSink.Events.Select(e => e.Kind).ToArray());
        Assert.Equal(DisconnectReason.Kicked, hostSink.Events[3].Reason);
        Assert.Equal(host.Peer, hostSink.Events[3].Peer);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Disconnect_OnDisconnected_DeliveredExactlyOnce()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();
        client.Disconnect(client.Peer, DisconnectReason.Requested);

        // Act
        host.Poll(sink);
        host.Poll(sink);
        client.Disconnect(client.Peer, DisconnectReason.Timeout);
        host.Disconnect(host.Peer, DisconnectReason.Timeout);
        host.Poll(sink);

        // Assert
        Assert.Single(sink.Events, e => e.Kind == EventKind.Disconnected);
        Assert.Equal(DisconnectReason.Requested, sink.Events.Single(e => e.Kind == EventKind.Disconnected).Reason);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Disconnect_LaterSendsFromEitherEnd_DroppedAndCountedNotDelivered()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var hostSink = new RecordingSink();
        var clientSink = new RecordingSink();
        client.Disconnect(client.Peer, DisconnectReason.Requested);

        // Act
        client.Send(client.Peer, NetChannel.Reliable, Bytes(1));
        host.Send(host.Peer, NetChannel.Reliable, Bytes(2));
        host.Send(host.Peer, NetChannel.Reliable, Bytes(3));
        host.Poll(hostSink);
        client.Poll(clientSink);

        // Assert
        Assert.Empty(hostSink.Messages());
        Assert.Empty(clientSink.Events);
        Assert.Equal(1L, client.DroppedAfterDisconnectCount);
        Assert.Equal(2L, host.DroppedAfterDisconnectCount);
        Assert.Equal(0L, client.SentCount);
        Assert.Equal(0L, host.SentCount);
        Assert.Equal(0, host.InboundPendingBytes);
        Assert.Equal(0, client.InboundPendingBytes);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Disconnect_EndingEnd_GetsNoCallbackAndLaterPollsDeliverNothing()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        host.Send(host.Peer, NetChannel.Reliable, Bytes(1));
        var clientSink = new RecordingSink();

        // Act
        client.Disconnect(client.Peer, DisconnectReason.Requested);
        client.Poll(clientSink);

        // Assert
        Assert.Empty(clientSink.Events);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Disconnect_UnknownPeer_CountedAndConnectionStaysOpen()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var other = new PeerHandle(host.Peer.Value + 1);

        // Act
        client.Disconnect(other, DisconnectReason.Kicked);

        // Assert
        Assert.True(client.IsOpen);
        Assert.True(host.IsOpen);
        Assert.Equal(1L, client.UnknownPeerCount);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Send_UnknownPeer_DroppedAndCounted()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();

        // Act
        client.Send(PeerHandle.None, NetChannel.Reliable, Bytes(1));
        client.Send(new PeerHandle(999), NetChannel.Reliable, Bytes(1));
        host.Poll(sink);

        // Assert
        Assert.Empty(sink.Messages());
        Assert.Equal(2L, client.UnknownPeerCount);
        Assert.Equal(0L, client.SentCount);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Send_UndefinedChannelValue_DroppedAndCounted()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();

        // Act
        client.Send(client.Peer, (NetChannel)4, Bytes(1));
        client.Send(client.Peer, (NetChannel)255, Bytes(1));
        host.Poll(sink);

        // Assert
        Assert.Empty(sink.Messages());
        Assert.Equal(2L, client.InvalidChannelCount);
        Assert.Equal(0L, client.SentCount);
    }

    [Fact]
    [Trait("Id", "TR-sim-008")]
    public void Send_DoesNotInvokeAnySinkInline_OnlyPollDelivers()
    {
        // Arrange - an echo sink on the receiver sends back from inside OnMessage
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var hostSink = new RecordingSink();
        var clientSink = new RecordingSink();
        hostSink.OnMessageHook = (channel, payload) => host.Send(host.Peer, channel, payload);

        // Act
        client.Send(client.Peer, NetChannel.Reliable, Bytes(7, 8));

        // Assert - Send called no sink
        Assert.Empty(hostSink.Events);
        Assert.Empty(clientSink.Events);

        // Act
        host.Poll(hostSink);

        // Assert - the echo is queued for the client, not delivered inline
        Assert.Empty(clientSink.Events);
        client.Poll(clientSink);
        Assert.Equal(Bytes(7, 8), Assert.Single(clientSink.Messages()).Payload);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Poll_SinkSendsIntoItsOwnInboundRing_NewMessageWaitsForNextPoll()
    {
        // Arrange - while the host drains, its sink makes the client send to the host again
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();
        sink.OnMessageHook = (channel, payload) => client.Send(client.Peer, channel, Bytes(payload[0], 1));
        client.Send(client.Peer, NetChannel.Reliable, Bytes(1));

        // Act
        host.Poll(sink);

        // Assert - the drain is bounded by what was queued when Poll began
        Assert.Single(sink.Messages());

        // Act
        host.Poll(sink);

        // Assert
        Assert.Equal(2, sink.Messages().Length);
        Assert.Equal(Bytes(1, 1), sink.Messages()[1].Payload);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Poll_SinkThrows_ExceptionPropagatesAndTheMessageIsConsumed()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();
        sink.OnMessageHook = (_, payload) =>
        {
            if (payload[0] == 1)
            {
                throw new InvalidOperationException("sink failure");
            }
        };
        client.Send(client.Peer, NetChannel.Reliable, Bytes(1));
        client.Send(client.Peer, NetChannel.Reliable, Bytes(2));

        // Act
        Exception? thrown = Record.Exception(() => host.Poll(sink));
        host.Poll(sink);

        // Assert - the failing message is not redelivered; the next one is
        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(2, sink.Messages().Length);
        Assert.Equal(Bytes(2), sink.Messages()[1].Payload);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Poll_NullSink_Throws()
    {
        // Arrange
        var (host, _) = LoopbackTransport.CreatePair(SmallRing, SmallMax);

        // Act
        Exception? thrown = Record.Exception(() => host.Poll(null!));

        // Assert
        Assert.IsType<ArgumentNullException>(thrown);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Poll_ReenteredFromSink_AssertsInAssertBuildsAndIsIgnoredOtherwise()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var inner = new RecordingSink();
        var outer = new RecordingSink();
        outer.OnMessageHook = (_, _) => host.Poll(inner);
        client.Send(client.Peer, NetChannel.Reliable, Bytes(1));

        // Act
        Exception? thrown = Record.Exception(() => host.Poll(outer));

        // Assert - Client.Core is compiled with SIM_ASSERT in Debug / ReleaseAssert only
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException assertion = Assert.IsType<SimAssertException>(thrown);
            Assert.Equal(NetAssertCode.PollReentered, assertion.Code);
        }
        else
        {
            Assert.Null(thrown);
        }

        Assert.Empty(inner.Events);
        Assert.Single(outer.Messages());

        // And Poll works again afterwards
        client.Send(client.Peer, NetChannel.Reliable, Bytes(2));
        Exception? second = Record.Exception(() => host.Poll(new RecordingSink()));
        Assert.Null(second);
    }

    // ------------------------------------------------------------- AC-3

    [Fact]
    [Trait("Id", "AC-3")]
    public void Send_PayloadAboveMaxMessage_RefusedCountedAndNothingQueued()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();

        // Act
        client.Send(client.Peer, NetChannel.Reliable, new byte[SmallMax + 1]);
        client.Send(client.Peer, NetChannel.Bulk, new byte[SmallMax * 4]);
        host.Poll(sink);

        // Assert
        Assert.Equal(2L, client.OversizeRefusedCount);
        Assert.Equal(0L, client.SentCount);
        Assert.Equal(0, host.InboundPendingBytes);
        Assert.Empty(sink.Messages());
        Assert.Equal(SmallRing, client.RingCapacityBytes);
        Assert.Equal(SmallMax, client.MaxMessageBytes);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Send_PayloadExactlyMaxMessage_Accepted()
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new RecordingSink();
        byte[] payload = Pattern(SmallMax, 3);

        // Act
        client.Send(client.Peer, NetChannel.Reliable, payload);
        host.Poll(sink);

        // Assert
        Assert.Equal(0L, client.OversizeRefusedCount);
        Assert.Equal(payload, Assert.Single(sink.Messages()).Payload);
    }

    // Regression (LP-CODE-REVIEW): the length header was written with bare (byte) casts, which
    // throw OverflowException in the checked (SIM_ASSERT) builds for any length > 255.
    [Theory]
    [Trait("Id", "AC-2")]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(300)]
    [InlineData(65_535)]
    [InlineData(LoopbackTransport.DefaultMaxMessageBytes)]
    public void Send_DefaultPairLengthAbove255_DeliveredIntactWithoutThrowing(int length)
    {
        // Arrange
        var (host, client) = LoopbackTransport.CreatePair();
        var sink = new RecordingSink();
        byte[] payload = Pattern(length, 9);

        // Act
        Exception? thrown = Record.Exception(() => client.Send(client.Peer, NetChannel.Reliable, payload));
        host.Poll(sink);

        // Assert
        Assert.Null(thrown);
        Assert.Equal(1L, client.SentCount);
        Assert.Equal(payload, Assert.Single(sink.Messages()).Payload);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Send_RingFull_RefusedCountedAndEarlierMessagesIntact()
    {
        // Arrange - ring 64 B, 20-byte payloads: 25 B each, so two fit and the third does not
        var (host, client) = LoopbackTransport.CreatePair(64, 20);
        var sink = new RecordingSink();

        // Act
        client.Send(client.Peer, NetChannel.Reliable, Pattern(20, 1));
        client.Send(client.Peer, NetChannel.Reliable, Pattern(20, 2));
        client.Send(client.Peer, NetChannel.Reliable, Pattern(20, 3));
        host.Poll(sink);

        // Assert
        Assert.Equal(1L, client.RingFullRefusedCount);
        Assert.Equal(2L, client.SentCount);
        Event[] messages = sink.Messages();
        Assert.Equal(2, messages.Length);
        Assert.Equal(Pattern(20, 1), messages[0].Payload);
        Assert.Equal(Pattern(20, 2), messages[1].Payload);

        // Act - once polled, the ring accepts again without growing
        client.Send(client.Peer, NetChannel.Reliable, Pattern(20, 4));
        host.Poll(sink);

        // Assert
        Assert.Equal(1L, client.RingFullRefusedCount);
        Assert.Equal(Pattern(20, 4), sink.Messages()[2].Payload);
        Assert.Equal(64, client.RingCapacityBytes);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Send_RingFull_ExactFitAcceptedAndOneByteOverRefused()
    {
        // Arrange - 30-byte ring: a 25-byte payload plus 5-byte header fills it exactly
        var (host, client) = LoopbackTransport.CreatePair(30, 25);
        var sink = new RecordingSink();

        // Act
        client.Send(client.Peer, NetChannel.Reliable, Pattern(25, 1));
        client.Send(client.Peer, NetChannel.Reliable, ReadOnlySpan<byte>.Empty);
        host.Poll(sink);

        // Assert - the full ring refuses even a zero-length message (its header needs 5 B)
        Assert.Equal(1L, client.SentCount);
        Assert.Equal(1L, client.RingFullRefusedCount);
        Assert.Equal(Pattern(25, 1), Assert.Single(sink.Messages()).Payload);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void SendPoll_MessagesThatWrapTheRingEnd_DeliveredIntactInOrder()
    {
        // Arrange - 100-byte ring, mixed sizes, so headers and payloads straddle the wrap point
        var (host, client) = LoopbackTransport.CreatePair(100, 30);
        var sink = new RecordingSink();
        var expected = new List<byte[]>();

        // Act
        for (int round = 0; round < 60; round++)
        {
            int length = (round * 7) % 31;
            byte[] payload = Pattern(length, round);
            client.Send(client.Peer, (NetChannel)(round % 4), payload);
            expected.Add(payload);
            if (round % 2 == 1)
            {
                host.Poll(sink);
            }
        }

        host.Poll(sink);

        // Assert
        Event[] messages = sink.Messages();
        Assert.Equal(expected.Count, messages.Length);
        Assert.Equal(0L, client.RingFullRefusedCount);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], messages[i].Payload);
            Assert.Equal((NetChannel)(i % 4), messages[i].Channel);
        }
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void SendPoll_WarmedUp_TenThousandCyclesAllocateNothing()
    {
        // Arrange - a small ring and varied sizes force wrap-around (the scratch path).
        // Measured with AllocAssert (TD-026): the same 10,000-cycle window is warmed up, then measured up to three times;
        // a real per-cycle allocation shows up in every window. The class is in the non-parallel collection.
        // TD-008 (RuntimeHelpers.CreateSpan at Tier-0) does not apply: the transport has no ReadOnlySpan literal tables,
        // so unlike the TrigQ15 test this does not skip on unoptimised builds.
        var (host, client) = LoopbackTransport.CreatePair(1000, 64);
        var hostSink = new CountingSink();
        var clientSink = new CountingSink();
        byte[] source = Pattern(64, 5);

        for (int i = 0; i < 400; i++)
        {
            Cycle(host, client, hostSink, clientSink, source, i);
        }

        long messagesBefore = hostSink.Messages + clientSink.Messages;

        // Act + Assert (0 B)
        int runs = AllocAssert.Zero("10,000 send/poll cycles with ring wrap-around", () =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                Cycle(host, client, hostSink, clientSink, source, i);
            }
        });

        // Assert
        Assert.Equal(20_000L * runs, hostSink.Messages + clientSink.Messages - messagesBefore);
        Assert.Equal(0L, client.RingFullRefusedCount + host.RingFullRefusedCount);
        Assert.Equal(0L, client.OversizeRefusedCount + host.OversizeRefusedCount);
    }

    private static void Cycle(
        LoopbackTransport host,
        LoopbackTransport client,
        CountingSink hostSink,
        CountingSink clientSink,
        byte[] source,
        int i)
    {
        int length = (i * 13) % 65;
        client.Send(client.Peer, (NetChannel)(i & 3), new ReadOnlySpan<byte>(source, 0, length));
        host.Send(host.Peer, NetChannel.Reliable, new ReadOnlySpan<byte>(source, 0, 64 - length));
        host.Poll(hostSink);
        client.Poll(clientSink);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Poll_PayloadSpan_ViewsPreallocatedStorageValidOnlyDuringTheCall()
    {
        // Arrange - the span handed to OnMessage must be readable during the call and not require a copy
        var (host, client) = LoopbackTransport.CreatePair(SmallRing, SmallMax);
        var sink = new CountingSink();
        client.Send(client.Peer, NetChannel.Reliable, Bytes(1, 2, 3));
        client.Send(client.Peer, NetChannel.Reliable, Bytes(4));

        // Act
        host.Poll(sink);

        // Assert
        Assert.Equal(2L, sink.Messages);
        Assert.Equal(4L, sink.Bytes);
        Assert.Equal(10L, sink.Checksum);
    }
}
