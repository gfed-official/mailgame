using System.Buffers.Binary;
using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;
using ClientLayerType = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// network-adapter-loopback story 006: <see cref="ClientAdapter"/> (reliable requests under a u16 wrapping reqId, batch and
/// reply decode into the <c>ClientLayer</c> inbox, the <see cref="IHostClock"/> estimator), the inbox and
/// <see cref="MirrorDrainStep"/>. AC-1 = requests (TR-input-013), AC-2 = inbox (TR-sim-014), AC-3 = clock (TR-delivery-009).
/// TD-066 item 1 (orphan and duplicate replies) is covered under "TD-066". Host bytes are built by hand in the batch wire
/// format of <see cref="EventBatch"/>, plus one end-to-end test against a real <see cref="HostAdapter"/>.
/// Deterministic: fixed seeds, no I/O, no time. Non-parallel because of the allocation test.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class ClientAdapterTests
{
    private const EventKind Rep = (EventKind)0x0F10;
    private const EventKind Chunk = (EventKind)0x0F11;
    private const EventKind Unknown = (EventKind)0x0F7E;
    private const ushort RepCodecId = 910;
    private const ushort ChunkCodecId = 911;

    private static readonly byte[] _payload = { 1, 2, 3, 4 };
    private static readonly PlayerSlot _slot0 = PlayerSlot.FromValue(0);
    private static readonly OpDescriptor _probe = OpDescriptor.For<uint>(OpKind.TeleportAck, false, 920, 4);

    // ------------------------------------------------------------ fixtures

    private sealed class CopySink : ITransportSink
    {
        public List<(NetChannel Channel, byte[] Bytes)> Messages { get; } = new();

        public void OnConnected(PeerHandle peer)
        {
        }

        public void OnDisconnected(PeerHandle peer, DisconnectReason reason)
        {
        }

        public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload) => Messages.Add((channel, payload.ToArray()));
    }

    private sealed class CountingSink : ITransportSink
    {
        public long Messages;

        public void OnConnected(PeerHandle peer)
        {
        }

        public void OnDisconnected(PeerHandle peer, DisconnectReason reason)
        {
        }

        public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload) => Messages++;
    }

    /// <summary>A transport that delivers scripted messages on Poll (any peer, any channel) and records sends.</summary>
    private sealed class ScriptedTransport : ITransport
    {
        public List<(PeerHandle Peer, NetChannel Channel, byte[] Bytes)> Script { get; } = new();

        public List<byte[]> Sent { get; } = new();

        public Action? OnPoll { get; set; }

        public void Poll(ITransportSink sink)
        {
            OnPoll?.Invoke();
            for (int i = 0; i < Script.Count; i++)
            {
                sink.OnMessage(Script[i].Peer, Script[i].Channel, Script[i].Bytes);
            }

            Script.Clear();
        }

        public void Send(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> bytes) => Sent.Add(bytes.ToArray());

        public void Disconnect(PeerHandle peer, DisconnectReason reason)
        {
        }
    }

    private sealed class RecordingConsumer : IMirrorConsumer
    {
        public List<string> Log { get; } = new();

        public List<(uint Tick, EventEntry Entry)> Entries { get; } = new();

        public List<(uint Tick, Reply Reply)> Replies { get; } = new();

        public void OnEntry(uint tick, in EventEntry entry)
        {
            Entries.Add((tick, entry));
            Log.Add("E");
        }

        public void OnReply(uint tick, in Reply reply)
        {
            Replies.Add((tick, reply));
            Log.Add("R");
        }
    }

    private sealed class CountingConsumer : IMirrorConsumer
    {
        public long Entries;
        public long Replies;

        public void OnEntry(uint tick, in EventEntry entry) => Entries++;

        public void OnReply(uint tick, in Reply reply) => Replies++;
    }

    private static EventCodecRegistry Events() => new(
        new[]
        {
            EventDescriptor.For<uint>(Rep, ReplicationClass.Replicated, false, 1, RepCodecId, 4),
            EventDescriptor.For<uint>(Chunk, ReplicationClass.OwnerOnly, false, 1, ChunkCodecId, 4),
        },
        new IEventCodec[] { new FixedLengthEventCodec(RepCodecId, 4), new FixedLengthEventCodec(ChunkCodecId, 4) });

    private sealed class Rig
    {
        public Rig(ClientAdapterSettings? settings = null, int inboxCapacity = 256)
        {
            (HostEnd, ClientEnd) = LoopbackTransport.CreatePair();
            Inbox = new ClientInbox(inboxCapacity);
            Adapter = new ClientAdapter(ClientEnd, ClientEnd.Peer, Events(), settings);
        }

        public LoopbackTransport HostEnd { get; }

        public LoopbackTransport ClientEnd { get; }

        public ClientInbox Inbox { get; }

        public ClientAdapter Adapter { get; }

        public void Host(byte[] bytes) => HostEnd.Send(HostEnd.Peer, NetChannel.Reliable, bytes);

        public void Pump() => Adapter.PumpInbound(Inbox);

        public ushort Send(OpKind kind = OpKind.TeleportAck, byte[]? payload = null) => Adapter.Send(kind, payload ?? _payload);

        public List<byte[]> HostReceived()
        {
            var sink = new CopySink();
            HostEnd.Poll(sink);
            var list = new List<byte[]>();
            foreach ((NetChannel channel, byte[] bytes) in sink.Messages)
            {
                Assert.Equal(NetChannel.Reliable, channel);
                list.Add(bytes);
            }

            return list;
        }

        public List<ClientInboxRecord> Drain()
        {
            var list = new List<ClientInboxRecord>();
            while (Inbox.TryDequeue(out ClientInboxRecord r))
            {
                list.Add(r);
            }

            return list;
        }

        /// <summary>The host answers: delivers the bare reply and pumps.</summary>
        public void HostRepliesBare(Reply reply)
        {
            Host(BareReply(reply));
            Pump();
        }
    }

    private static Reply Rp(ushort reqId, OpKind kind = OpKind.TeleportAck, ushort reason = 0, uint extra = 0)
    {
        var r = new Reply { ReqId = reqId, Kind = kind, Reason = reason, Result = reason == 0 ? ReplyResult.Ok : ReplyResult.Rejected };
        if (extra != 0)
        {
            r.SetExtra(extra);
        }

        return r;
    }

    private static byte[] BareReply(Reply reply)
    {
        byte[] buf = new byte[ReplyCodec.MaxSize];
        int n = ReplyCodec.Encode(in reply, buf);
        Assert.True(n > 0);
        return buf.AsSpan(0, n).ToArray();
    }

    private static byte[] EvRec(EventKind kind, uint value, bool continuation = false)
    {
        byte[] rec = new byte[EventBatch.RecordHeaderSize + 4];
        ushort field = (ushort)((ushort)kind | (continuation ? EventBatch.ContinuationBit : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(rec, field);
        BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(2), value);
        return rec;
    }

    private static byte[] ReplyRec(Reply reply)
    {
        byte[] buf = new byte[EventBatch.RecordHeaderSize + ReplyCodec.BodyMaxSize];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, (ushort)EventKind.OpReply);
        int n = ReplyCodec.EncodeBody(in reply, buf.AsSpan(EventBatch.RecordHeaderSize));
        Assert.True(n > 0);
        return buf.AsSpan(0, EventBatch.RecordHeaderSize + n).ToArray();
    }

    private static byte[] Batch(uint tick, params byte[][] records) => BatchWithCount(tick, (ushort)records.Length, records);

    private static byte[] BatchWithCount(uint tick, ushort count, params byte[][] records)
    {
        var bytes = new List<byte> { EventBatch.Tag };
        Span<byte> header = stackalloc byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(header, tick);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4), count);
        bytes.AddRange(header.ToArray());
        foreach (byte[] record in records)
        {
            bytes.AddRange(record);
        }

        return bytes.ToArray();
    }

    private static void ExpectAssertThenRelease(ushort code, Action act)
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(act);
            Assert.Equal(code, ex.Code);
        }
        else
        {
            act();
        }
    }

    // ------------------------------------------------------------ AC-1 requests

    [Fact]
    [Trait("Id", "AC-1")]
    public void Send_ReqIds_StartAtOne_IncrementAndWrapThroughZero()
    {
        // Arrange - every request is answered before the next, so the pending table never fills
        var rig = new Rig();
        var drained = new CountingSink();

        // Act / Assert - 65 538 sends cover 65 535 -> 0 -> 1
        for (int i = 0; i < 65_538; i++)
        {
            ushort id = rig.Send();
            Assert.Equal(unchecked((ushort)(i + 1)), id);
            rig.HostEnd.Poll(drained);
            rig.HostRepliesBare(Rp(id));
            rig.Inbox.Clear();
        }

        Assert.Equal(65_538L, drained.Messages);
        Assert.Equal(65_538L, rig.Adapter.SentCount);
        Assert.Equal(0, rig.Adapter.PendingCount);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Send_EmitsExactlyOneReliableMessage_WithTheFourByteEnvelope()
    {
        var rig = new Rig();

        ushort id = rig.Send(OpKind.TeleportAck, _payload);

        List<byte[]> received = rig.HostReceived();
        byte[] message = Assert.Single(received);
        Assert.Equal(RequestEnvelope.HeaderSize + _payload.Length, message.Length);
        Assert.Equal((ushort)OpKind.TeleportAck, BinaryPrimitives.ReadUInt16LittleEndian(message));
        Assert.Equal(id, BinaryPrimitives.ReadUInt16LittleEndian(message.AsSpan(2)));
        Assert.Equal(_payload, message.AsSpan(RequestEnvelope.HeaderSize).ToArray());
        Assert.Equal(1L, rig.ClientEnd.SentCount);
        Assert.Equal(1, rig.Adapter.PendingCount);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Send_EmptyPayload_IsJustTheEnvelope()
    {
        var rig = new Rig();

        rig.Adapter.Send(OpKind.PlayerLeave, ReadOnlySpan<byte>.Empty);

        Assert.Equal(RequestEnvelope.HeaderSize, Assert.Single(rig.HostReceived()).Length);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void NoRequest_IsEverSentAgainAutomatically()
    {
        // Arrange - one request, never answered
        var rig = new Rig();
        rig.Send();

        // Act - time passes far beyond the reply timeout, pumps, unrelated replies and batches arrive
        rig.Adapter.UpdateTime(60_000);
        rig.Pump();
        rig.Host(Batch(3, EvRec(Rep, 1)));
        rig.Pump();
        rig.Adapter.UpdateTime(120_000);

        // Assert - the host saw exactly the original message
        Assert.Single(rig.HostReceived());
        Assert.Equal(1L, rig.Adapter.SentCount);
        Assert.Equal(0L, rig.Adapter.ResendCount);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Resend_OnlyForABusyAnsweredRequest_UnderANewReqId()
    {
        // Arrange
        var rig = new Rig();
        ushort first = rig.Send();
        rig.HostReceived();

        // Assert - before any reply the request is not resendable
        Assert.False(rig.Adapter.TryResend(first, out ushort none));
        Assert.Equal(0, none);
        Assert.Throws<InvalidOperationException>(() => rig.Adapter.Resend(first));

        // Act - the host answers busy
        rig.HostRepliesBare(Rp(first, reason: CoreReason.Busy));
        ushort second = rig.Adapter.Resend(first);

        // Assert - a new attempt: new id, same kind and payload, exactly one more message
        Assert.NotEqual(first, second);
        byte[] message = Assert.Single(rig.HostReceived());
        Assert.Equal(second, BinaryPrimitives.ReadUInt16LittleEndian(message.AsSpan(2)));
        Assert.Equal((ushort)OpKind.TeleportAck, BinaryPrimitives.ReadUInt16LittleEndian(message));
        Assert.Equal(_payload, message.AsSpan(RequestEnvelope.HeaderSize).ToArray());
        Assert.Equal(1L, rig.Adapter.ResendCount);
        Assert.Equal(1, rig.Adapter.PendingCount);

        // Assert - the old id is retired, the new attempt (awaiting) is not resendable, an OK answer retires it
        Assert.False(rig.Adapter.TryResend(first, out _));
        Assert.False(rig.Adapter.TryResend(second, out _));
        rig.HostRepliesBare(Rp(second));
        Assert.False(rig.Adapter.TryResend(second, out _));
        Assert.Equal(5L, rig.Adapter.ResendRejectedCount);
        Assert.Equal(0, rig.Adapter.PendingCount);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Resend_AfterANonBusyRejection_IsRefused()
    {
        var rig = new Rig();
        ushort id = rig.Send();
        rig.HostRepliesBare(Rp(id, reason: CoreReason.BadRequest));

        Assert.False(rig.Adapter.TryResend(id, out _));
        Assert.Equal(1L, rig.Adapter.ResendRejectedCount);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Send_InvalidKindOrOversizePayload_IsRefusedAndNothingIsSent()
    {
        var rig = new Rig();

        Assert.False(rig.Adapter.TrySend(OpKind.None, _payload, out ushort id));
        Assert.Equal(0, id);
        Assert.False(rig.Adapter.TrySend(OpKind.TeleportAck, new byte[ClientAdapterSettings.MaxPayloadBytes + 1], out _));
        Assert.Throws<InvalidOperationException>(() => rig.Adapter.Send(OpKind.None, _payload));
        Assert.True(rig.Adapter.TrySend(OpKind.TeleportAck, new byte[ClientAdapterSettings.MaxPayloadBytes], out _));

        Assert.Equal(3L, rig.Adapter.InvalidSendCount);
        Assert.Single(rig.HostReceived());
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Send_WhenPendingTableIsFullOfAwaitedRequests_IsRefused_ThenFreesOnReply()
    {
        // Arrange
        var rig = new Rig();
        var ids = new List<ushort>();
        for (int i = 0; i < 16; i++)
        {
            ids.Add(rig.Send());
        }

        // Act / Assert - the 17th is refused (pendingQueueMax 16), nothing extra is sent
        Assert.False(rig.Adapter.TrySend(OpKind.TeleportAck, _payload, out _));
        Assert.Throws<InvalidOperationException>(() => rig.Adapter.Send(OpKind.TeleportAck, _payload));
        Assert.Equal(2L, rig.Adapter.PendingFullRefusedCount);
        Assert.Equal(16, rig.HostReceived().Count);

        // A reply frees a slot
        rig.HostRepliesBare(Rp(ids[5]));
        Assert.True(rig.Adapter.TrySend(OpKind.TeleportAck, _payload, out _));
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Send_WhenFull_EvictsTheOldestBusyAnsweredRequest()
    {
        var rig = new Rig(new ClientAdapterSettings { PendingQueueMax = 2 });
        ushort a = rig.Send();
        ushort b = rig.Send();
        rig.HostRepliesBare(Rp(b, reason: CoreReason.Busy));
        rig.HostRepliesBare(Rp(a, reason: CoreReason.Busy));

        Assert.True(rig.Adapter.TrySend(OpKind.TeleportAck, _payload, out _));

        Assert.Equal(1L, rig.Adapter.BusyEvictedCount);
        Assert.False(rig.Adapter.TryResend(a, out _)); // a was sent first, so it is the oldest and was evicted (b answered busy first, which does not matter)
        Assert.True(rig.Adapter.TryResend(b, out _));
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Forget_DropsABusyAnsweredRequestOnly()
    {
        var rig = new Rig();
        ushort id = rig.Send();

        Assert.False(rig.Adapter.Forget(id)); // still awaiting its reply
        rig.HostRepliesBare(Rp(id, reason: CoreReason.Busy));
        Assert.True(rig.Adapter.Forget(id));
        Assert.False(rig.Adapter.Forget(id));
        Assert.Equal(0, rig.Adapter.PendingCount);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void ReqId_StillPending_IsSkippedAfterTheCounterWraps()
    {
        // Arrange - id 1 stays pending while 65 535 other requests cycle the counter back around
        var rig = new Rig();
        ushort stuck = rig.Send();
        Assert.Equal(1, stuck);
        var drained = new CountingSink();
        for (int i = 0; i < 65_535; i++)
        {
            ushort id = rig.Send();
            rig.HostEnd.Poll(drained);
            rig.HostRepliesBare(Rp(id));
            rig.Inbox.Clear();
        }

        // Act - the counter is now at 1 again
        ushort next = rig.Send();

        // Assert
        Assert.Equal(2, next);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void ReplyTimeout_IsCountedOncePerRequest_AndALateReplyIsStillDelivered()
    {
        var rig = new Rig(new ClientAdapterSettings { ReplyTimeoutMs = 5000 });
        rig.Adapter.UpdateTime(1000);
        ushort id = rig.Send();

        rig.Adapter.UpdateTime(5999);
        Assert.Equal(0L, rig.Adapter.ReplyTimeoutCount);
        rig.Adapter.UpdateTime(6000);
        rig.Adapter.UpdateTime(9000);
        rig.Adapter.UpdateTime(100); // time never goes backwards
        Assert.Equal(1L, rig.Adapter.ReplyTimeoutCount);

        rig.HostRepliesBare(Rp(id));
        Assert.Equal(1L, rig.Adapter.RepliesDelivered);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void ReplyTimeout_ForARequestSentBeforeTheFirstUpdateTime_StartsAtTheFirstUpdate()
    {
        var rig = new Rig(new ClientAdapterSettings { ReplyTimeoutMs = 5000 });
        rig.Send(); // sent before the clock origin is known

        rig.Adapter.UpdateTime(-5); // ignored
        rig.Adapter.UpdateTime(1_000_000); // origin: the wait starts here, not at 0
        Assert.Equal(0L, rig.Adapter.ReplyTimeoutCount);
        rig.Adapter.UpdateTime(1_004_999);
        Assert.Equal(0L, rig.Adapter.ReplyTimeoutCount);
        rig.Adapter.UpdateTime(1_005_000);

        Assert.Equal(1L, rig.Adapter.ReplyTimeoutCount);
    }

    [Fact]
    public void Constructor_RejectsNullArgumentsAndBadSettings()
    {
        var (_, client) = LoopbackTransport.CreatePair();
        Assert.Throws<ArgumentNullException>(() => new ClientAdapter(null!, client.Peer, Events()));
        Assert.Throws<ArgumentNullException>(() => new ClientAdapter(client, client.Peer, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientAdapter(client, client.Peer, Events(), new ClientAdapterSettings { PendingQueueMax = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientAdapter(client, client.Peer, Events(), new ClientAdapterSettings { PendingQueueMax = 65 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientAdapter(client, client.Peer, Events(), new ClientAdapterSettings { ReplyTimeoutMs = -1 }));
        Assert.Throws<ArgumentNullException>(() => new Rig().Adapter.PumpInbound(null!));
    }

    // ------------------------------------------------------------ AC-2 inbox

    [Fact]
    [Trait("Id", "AC-2")]
    public void Batch_IsDecodedIntoTheInbox_InOrder_WithItsTick_RepliesAmongTheEntries()
    {
        // Arrange
        var rig = new Rig();
        ushort id = rig.Send();
        rig.Host(Batch(7, EvRec(Rep, 11), ReplyRec(Rp(id, extra: 99)), EvRec(Chunk, 22, continuation: true), EvRec(Rep, 33)));

        // Act
        rig.Pump();

        // Assert
        List<ClientInboxRecord> records = rig.Drain();
        Assert.Equal(4, records.Count);
        Assert.All(records, r => Assert.Equal(7u, r.Tick));
        Assert.Equal(new[] { Rep, EventKind.OpReply, Chunk, Rep }, records.Select(r => r.Entry.Kind).ToArray());
        Assert.Equal(new uint[] { 0, 0, 2, 3 }, records.Select(r => r.Entry.Ordinal).ToArray());
        Assert.Equal(11u, records[0].Entry.PayloadAs<uint>());
        Assert.False(records[0].IsReply);
        Assert.Equal(EventFlags.None, records[0].Entry.Flags);
        Assert.True(records[1].IsReply);
        Reply reply = records[1].Entry.PayloadAs<Reply>();
        Assert.Equal(id, (ushort)reply.ReqId);
        Assert.Equal(99u, reply.ExtraAs<uint>());
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.Equal(EventFlags.Continuation, records[2].Entry.Flags);
        Assert.Equal(22u, records[2].Entry.PayloadAs<uint>());
        Assert.Equal(33u, records[3].Entry.PayloadAs<uint>());
        Assert.Equal(1L, rig.Adapter.BatchesReceived);
        Assert.Equal(3L, rig.Adapter.EntriesDelivered);
        Assert.Equal(1L, rig.Adapter.RepliesDelivered);
        Assert.Equal(0, rig.Adapter.PendingCount);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Inbox_IsNotAppliedUntilMirrorDrainRuns_ThenDispatchedInOrderAtTheNextClientTick()
    {
        // Arrange - a real ClientLayer with the drain step bound at MirrorDrain
        var rig = new Rig();
        ushort id = rig.Send();
        var consumer = new RecordingConsumer();
        var drain = new MirrorDrainStep(rig.Inbox);
        drain.Register(consumer);
        var builder = new ClientLayerBuilder(rig.Adapter.Clock);
        drain.BindTo(builder);
        ClientLayerType layer = builder.Build();
        rig.Host(Batch(4, EvRec(Rep, 1), ReplyRec(Rp(id)), EvRec(Rep, 2)));

        // Act 1 - the adapter decodes into the inbox; the client tick has not run
        rig.Pump();

        // Assert 1 - nothing applied yet
        Assert.Equal(3, rig.Inbox.Count);
        Assert.Empty(consumer.Log);

        // Act 2 - the client tick
        layer.Step(isFirstTickInBatch: true);

        // Assert 2
        Assert.Equal(new[] { "E", "R", "E" }, consumer.Log);
        Assert.Equal(0, rig.Inbox.Count);
        Assert.Equal(4u, consumer.Entries[0].Tick);
        Assert.Equal(id, (ushort)consumer.Replies[0].Reply.ReqId);
        Assert.Same(rig.Adapter.Clock, layer.HostClock);
        Assert.Equal(2L, drain.DrainedEntryCount);
        Assert.Equal(1L, drain.DrainedReplyCount);
    }

    [Theory]
    [InlineData(CoreReason.Busy)]
    [InlineData(CoreReason.BadRequest)]
    [Trait("Id", "AC-2")]
    public void AdapterGeneratedBusyAndBadRequest_AreDeliveredToTheInboxLikeAnyReply(ushort reason)
    {
        var rig = new Rig();
        ushort id = rig.Send();

        rig.HostRepliesBare(Rp(id, reason: reason));

        ClientInboxRecord record = Assert.Single(rig.Drain());
        Assert.True(record.IsReply);
        Reply reply = record.Entry.PayloadAs<Reply>();
        Assert.Equal(reason, reply.Reason);
        Assert.Equal(ReplyResult.Rejected, reply.Result);
        Assert.Equal(id, (ushort)reply.ReqId);
        Assert.Equal(OpKind.TeleportAck, reply.Kind);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void BareReply_CarriesTheLastBatchTick()
    {
        var rig = new Rig();
        ushort id = rig.Send();
        rig.Host(Batch(12));
        rig.Pump();

        rig.HostRepliesBare(Rp(id));

        Assert.Equal(12u, Assert.Single(rig.Drain()).Tick);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void BareReply_BeforeAnyBatch_CarriesTickZero()
    {
        var rig = new Rig();
        ushort id = rig.Send();

        rig.HostRepliesBare(Rp(id));

        ClientInboxRecord record = Assert.Single(rig.Drain());
        Assert.Equal(0u, record.Tick);
        Assert.False(rig.Adapter.Clock.IsValid);
    }

    public static IEnumerable<object[]> MalformedMessages()
    {
        byte[] good = Batch(5, EvRec(Rep, 1), EvRec(Rep, 2));
        yield return new object[] { "empty", Array.Empty<byte>() };
        yield return new object[] { "unknown tag", new byte[] { 0x7F, 1, 2, 3 } };
        yield return new object[] { "tag only (batch)", new byte[] { EventBatch.Tag } };
        yield return new object[] { "tag only (reply)", new byte[] { ReplyCodec.Tag } };
        yield return new object[] { "batch count 3, two records", BatchWithCount(5, 3, EvRec(Rep, 1), EvRec(Rep, 2)) };
        yield return new object[] { "batch count 1, two records (trailing)", BatchWithCount(5, 1, EvRec(Rep, 1), EvRec(Rep, 2)) };
        yield return new object[] { "unknown event kind", Batch(5, EvRec(Unknown, 1)) };
        yield return new object[] { "event kind 0", Batch(5, EvRec((EventKind)0, 1)) };
        yield return new object[] { "truncated record", good.AsSpan(0, good.Length - 1).ToArray() };
        yield return new object[] { "record header cut", good.AsSpan(0, EventBatch.HeaderSize + 1).ToArray() };
        yield return new object[] { "bare reply extraLen 25", new byte[] { ReplyCodec.Tag, 1, 0, 1, 0, 2, 0, 25 }.Concat(new byte[25]).ToArray() };
        yield return new object[] { "bare reply truncated", BareReply(Rp(1, extra: 5)).AsSpan(0, 8).ToArray() };
        yield return new object[] { "bare reply trailing byte", BareReply(Rp(1)).Concat(new byte[] { 0 }).ToArray() };
        yield return new object[] { "batch reply record bad extraLen", Batch(5, new byte[] { 1, 0, 1, 0, 1, 0, 2, 0, 30 }) };
    }

    [Theory]
    [MemberData(nameof(MalformedMessages))]
    [Trait("Id", "AC-2")]
    public void MalformedInbound_IsDroppedAndCounted_NeverThrown_AndNothingIsDelivered(string label, byte[] message)
    {
        _ = label;
        var rig = new Rig();
        rig.Send();
        rig.Host(message);

        rig.Pump();

        Assert.Equal(1L, rig.Adapter.MalformedInboundCount);
        Assert.Equal(0, rig.Inbox.Count);
        Assert.False(rig.Adapter.Clock.IsValid);
        Assert.Equal(1, rig.Adapter.PendingCount);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Batch_WithAGoodRecordThenABadOne_DeliversNothing_AndDoesNotAdvanceTheClock()
    {
        var rig = new Rig();
        rig.Host(Batch(9, EvRec(Rep, 1), EvRec(Unknown, 2)));

        rig.Pump();

        Assert.Equal(1L, rig.Adapter.MalformedInboundCount);
        Assert.Equal(0, rig.Inbox.Count);
        Assert.False(rig.Adapter.Clock.IsValid);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void EveryStrictPrefixOfABatch_IsMalformed_AndRandomBytesNeverThrow()
    {
        var rig = new Rig();
        ushort id = rig.Send();
        byte[] full = Batch(5, EvRec(Rep, 1), ReplyRec(Rp(id, extra: 7)), EvRec(Chunk, 3, true));
        for (int length = 0; length < full.Length; length++)
        {
            rig.Host(full.AsSpan(0, length).ToArray());
        }

        var random = new Random(20_261_016);
        byte[] noise = new byte[64];
        for (int i = 0; i < 500; i++)
        {
            random.NextBytes(noise);
            rig.Host(noise.AsSpan(0, random.Next(noise.Length)).ToArray());
        }

        rig.Pump();

        Assert.True(rig.Adapter.MalformedInboundCount >= full.Length);
        Assert.Equal(0, rig.Inbox.Count); // fixed seed: the noise decodes to nothing
    }

    [Fact]
    [Trait("Id", "TR-sim-014")]
    public void HeaderOnlyBatch_CountZero_IsAccepted_AdvancesTheClock_DeliversNothing_AndIsNotMalformed()
    {
        // TD-068 item 1 (story 007): the host's quiet-tick heartbeat.
        var rig = new Rig();
        rig.Host(Batch(41));
        rig.Pump();

        Assert.Equal(0L, rig.Adapter.MalformedInboundCount);
        Assert.Equal(1L, rig.Adapter.BatchesReceived);
        Assert.Equal(0L, rig.Adapter.EntriesDelivered);
        Assert.Empty(rig.Drain());
        Assert.True(rig.Adapter.Clock.IsValid);
        Assert.Equal(42u, rig.Adapter.Clock.EstimatedHostTick);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Batch_WhenTheInboxHasNoRoom_IsDroppedWhole_ButTheClockStillAdvances()
    {
        var rig = new Rig(inboxCapacity: 2);
        rig.Host(Batch(8, EvRec(Rep, 1), EvRec(Rep, 2), EvRec(Rep, 3)));

        rig.Pump();

        Assert.Equal(1L, rig.Adapter.InboxFullDropCount);
        Assert.Equal(0L, rig.Adapter.BatchesReceived); // a dropped batch is counted in InboxFullDropCount only
        Assert.Equal(0, rig.Inbox.Count);
        Assert.True(rig.Adapter.Clock.IsValid);
        Assert.Equal(9u, rig.Adapter.Clock.EstimatedHostTick);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void DroppedBatch_LosesItsOpReply_AndTheRequestStaysAwaiting()
    {
        var rig = new Rig(inboxCapacity: 1);
        ushort id = rig.Send();
        rig.Host(Batch(8, EvRec(Rep, 1), ReplyRec(Rp(id))));

        rig.Pump();

        Assert.Equal(1L, rig.Adapter.InboxFullDropCount);
        Assert.Equal(0L, rig.Adapter.RepliesDelivered);
        Assert.Equal(1, rig.Adapter.PendingCount);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void BareReply_WhenTheInboxIsFull_IsDropped_AndTheRequestStaysPending()
    {
        var rig = new Rig(inboxCapacity: 1);
        ushort a = rig.Send();
        ushort b = rig.Send();
        rig.HostRepliesBare(Rp(a));

        rig.HostRepliesBare(Rp(b));

        Assert.Equal(1L, rig.Adapter.InboxFullDropCount);
        Assert.Equal(1, rig.Adapter.PendingCount);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Messages_OnOtherChannelsOrFromOtherPeers_AreDroppedAndCounted()
    {
        var transport = new ScriptedTransport();
        var host = new PeerHandle(5);
        var adapter = new ClientAdapter(transport, host, Events());
        var inbox = new ClientInbox(8);
        byte[] batch = Batch(1, EvRec(Rep, 1));
        transport.Script.Add((host, NetChannel.Bulk, batch));
        transport.Script.Add((host, NetChannel.Unreliable, batch));
        transport.Script.Add((new PeerHandle(6), NetChannel.Reliable, batch));
        transport.Script.Add((host, NetChannel.Reliable, batch));

        adapter.PumpInbound(inbox);

        Assert.Equal(2L, adapter.UnexpectedChannelCount);
        Assert.Equal(1L, adapter.UnknownPeerCount);
        Assert.Equal(1, inbox.Count);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Message_DeliveredOutsidePumpInbound_IsNeverDecoded()
    {
        var transport = new ScriptedTransport();
        var adapter = new ClientAdapter(transport, new PeerHandle(5), Events());

        ExpectAssertThenRelease(NetAssertCode.ClientInboundOutsidePump, () => adapter.OnMessage(new PeerHandle(5), NetChannel.Reliable, Batch(1)));

        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(1L, adapter.OutsidePumpDropCount);
        }

        Assert.False(adapter.Clock.IsValid);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void PumpInbound_ReenteredFromATransportCallback_IsRejected()
    {
        var transport = new ScriptedTransport();
        var adapter = new ClientAdapter(transport, new PeerHandle(5), Events());
        var inbox = new ClientInbox(8);
        int polls = 0;
        transport.OnPoll = () =>
        {
            polls++;
            if (polls == 1)
            {
                ExpectAssertThenRelease(NetAssertCode.ClientPumpReentered, () => adapter.PumpInbound(inbox));
            }
        };

        adapter.PumpInbound(inbox);
        adapter.PumpInbound(inbox);

        Assert.Equal(2, polls);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Connect_AndHostDisconnect_AreTracked_AndPendingRequestsAreDropped()
    {
        var rig = new Rig();
        rig.Send();
        rig.Pump();
        Assert.True(rig.Adapter.IsConnected);

        rig.HostEnd.Disconnect(rig.HostEnd.Peer, DisconnectReason.Kicked);
        rig.Pump();

        Assert.False(rig.Adapter.IsConnected);
        Assert.Equal(1L, rig.Adapter.DisconnectCount);
        Assert.Equal(DisconnectReason.Kicked, rig.Adapter.LastDisconnectReason);
        Assert.Equal(0, rig.Adapter.PendingCount);
    }

    [Fact]
    public void Connect_FromAnotherPeer_IsIgnored()
    {
        var transport = new ScriptedTransport();
        var adapter = new ClientAdapter(transport, new PeerHandle(5), Events());

        adapter.OnConnected(new PeerHandle(9));
        adapter.OnDisconnected(new PeerHandle(9), DisconnectReason.Kicked);

        Assert.False(adapter.IsConnected);
        Assert.Equal(0L, adapter.DisconnectCount);
    }

    // ------------------------------------------------------------ AC-3 IHostClock

    [Fact]
    [Trait("Id", "AC-3")]
    public void Clock_IsInvalidBeforeTheFirstBatch_ThenLastBatchTickPlusOne()
    {
        var rig = new Rig();
        ushort id = rig.Send();
        Assert.False(rig.Adapter.Clock.IsValid);

        rig.HostRepliesBare(Rp(id)); // a bare reply carries no tick
        Assert.False(rig.Adapter.Clock.IsValid);

        rig.Host(Batch(41, EvRec(Rep, 1)));
        rig.Pump();

        Assert.True(rig.Adapter.Clock.IsValid);
        Assert.Equal(42u, rig.Adapter.Clock.EstimatedHostTick);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Clock_NeverDecreases_AnOlderBatchIsDropped()
    {
        var rig = new Rig();
        rig.Host(Batch(50, EvRec(Rep, 1)));
        rig.Pump();

        rig.Host(Batch(47, EvRec(Rep, 2)));
        rig.Pump();

        Assert.Equal(51u, rig.Adapter.Clock.EstimatedHostTick);
        Assert.Equal(1L, rig.Adapter.OutOfOrderBatchCount);
        Assert.Equal(1, rig.Inbox.Count); // only the tick-50 record
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Clock_AdvancesWithLaterBatches_AndASplitBatchOfTheSameTickIsDelivered()
    {
        var rig = new Rig();

        rig.Host(Batch(0, EvRec(Rep, 1)));
        rig.Pump();
        Assert.Equal(1u, rig.Adapter.Clock.EstimatedHostTick);
        rig.Host(Batch(0, EvRec(Rep, 2)));
        rig.Host(Batch(3, EvRec(Rep, 3)));
        rig.Pump();

        Assert.Equal(4u, rig.Adapter.Clock.EstimatedHostTick);
        Assert.Equal(3, rig.Inbox.Count);
        Assert.Equal(0L, rig.Adapter.OutOfOrderBatchCount);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void ClientHostClock_Observe_IsMonotonic_AndSaturatesAtTheTickLimit()
    {
        var clock = new ClientHostClock();
        Assert.False(clock.IsValid);
        Assert.Equal(0u, clock.EstimatedHostTick);

        clock.Observe(10);
        clock.Observe(4);
        Assert.Equal(11u, clock.EstimatedHostTick);
        clock.Observe(uint.MaxValue);
        Assert.Equal(uint.MaxValue, clock.EstimatedHostTick);
        clock.Observe(uint.MaxValue - 1);
        Assert.Equal(uint.MaxValue, clock.EstimatedHostTick);
        Assert.True(clock.IsValid);
    }

    // ------------------------------------------------------------ TD-066 item 1: orphan and duplicate replies

    [Fact]
    [Trait("Id", "TD-066")]
    public void OrphanReply_ForAReqIdNotAwaited_IsIgnoredAndCounted()
    {
        var rig = new Rig();

        rig.HostRepliesBare(Rp(777));
        rig.Host(Batch(2, ReplyRec(Rp(778)), EvRec(Rep, 5)));
        rig.Pump();

        Assert.Equal(2L, rig.Adapter.IgnoredReplyCount);
        Assert.Equal(0L, rig.Adapter.RepliesDelivered);
        ClientInboxRecord only = Assert.Single(rig.Drain()); // the event still goes through
        Assert.Equal(Rep, only.Entry.Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Id", "TD-066")]
    public void DuplicateReply_BareAndBatchForTheSameReqId_DeliversExactlyOne(bool bareFirst)
    {
        var rig = new Rig();
        ushort id = rig.Send();
        byte[] bare = BareReply(Rp(id, extra: 5));
        byte[] batch = Batch(6, ReplyRec(Rp(id, extra: 5)));
        rig.Host(bareFirst ? bare : batch);
        rig.Host(bareFirst ? batch : bare);

        rig.Pump();

        ClientInboxRecord only = Assert.Single(rig.Drain());
        Assert.True(only.IsReply);
        Assert.Equal(1L, rig.Adapter.RepliesDelivered);
        Assert.Equal(1L, rig.Adapter.IgnoredReplyCount);
        Assert.Equal(0, rig.Adapter.PendingCount);
    }

    [Fact]
    [Trait("Id", "TD-066")]
    public void SecondReply_ToABusyAnsweredRequest_IsIgnored()
    {
        var rig = new Rig();
        ushort id = rig.Send();
        rig.HostRepliesBare(Rp(id, reason: CoreReason.Busy));

        rig.HostRepliesBare(Rp(id));

        Assert.Equal(1L, rig.Adapter.RepliesDelivered);
        Assert.Equal(1L, rig.Adapter.IgnoredReplyCount);
        Assert.True(rig.Adapter.TryResend(id, out _)); // the busy entry survives the stray reply
    }

    [Fact]
    [Trait("Id", "TD-066")]
    public void JoinReply_TruncatedToWireReqIdZero_IsIgnoredByTheKindCheck_WhileTheRealRequestWithIdZeroWaits()
    {
        // Arrange - cycle the counter so the next request really has wire id 0 (a valid client id)
        var rig = new Rig();
        var drained = new CountingSink();
        for (int i = 0; i < 65_535; i++)
        {
            ushort id = rig.Send();
            rig.HostEnd.Poll(drained);
            rig.HostRepliesBare(Rp(id));
            rig.Inbox.Clear();
        }

        ushort zero = rig.Send(OpKind.TeleportAck);
        Assert.Equal(0, zero);

        // Act - the host's join reply (ReqId 0x10000, truncated to wire id 0 by the codec) arrives
        rig.HostRepliesBare(Rp(0, OpKind.PlayerJoin) with { ReqId = HostAdapter.JoinReqIdBase });

        // Assert - ignored by the kind check; the real request is still awaited and answered later
        Assert.Equal(1L, rig.Adapter.ReplyKindMismatchCount);
        Assert.Equal(0, rig.Inbox.Count);
        Assert.Equal(1, rig.Adapter.PendingCount);
        rig.HostRepliesBare(Rp(0));
        Assert.Equal(0, rig.Adapter.PendingCount);
    }

    [Fact]
    [Trait("Id", "TD-066")]
    public void Reply_WhoseKindDisagreesWithTheRequest_IsIgnoredAndTheRequestKeepsWaiting()
    {
        var rig = new Rig();
        ushort id = rig.Send(OpKind.TeleportAck);

        rig.HostRepliesBare(Rp(id, OpKind.PlayerLeave));

        Assert.Equal(1L, rig.Adapter.ReplyKindMismatchCount);
        Assert.Equal(0, rig.Inbox.Count);
        Assert.Equal(1, rig.Adapter.PendingCount);
        rig.HostRepliesBare(Rp(id));
        Assert.Equal(0, rig.Adapter.PendingCount);
    }

    // ------------------------------------------------------------ inbox and drain step

    [Fact]
    public void ClientInbox_IsAFixedRing_ThatWrapsAndRefusesWhenFull()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientInbox(0));
        var inbox = new ClientInbox(3);
        var entry = new EventEntry { Kind = Rep, PayloadLength = 4 };
        Assert.False(inbox.TryDequeue(out _));

        for (uint round = 0; round < 4; round++)
        {
            Assert.True(inbox.TryAddEntry(round, in entry));
            Assert.True(inbox.TryAddReply(round, Rp(1)));
            Assert.True(inbox.TryAddEntry(round, in entry));
            Assert.Equal(0, inbox.FreeCount);
            Assert.False(inbox.TryAddEntry(round, in entry));
            Assert.False(inbox.TryAddReply(round, Rp(1)));
            Assert.True(inbox.TryDequeue(out ClientInboxRecord a));
            Assert.True(inbox.TryDequeue(out ClientInboxRecord b));
            Assert.True(inbox.TryDequeue(out ClientInboxRecord c));
            Assert.False(a.IsReply);
            Assert.True(b.IsReply);
            Assert.False(c.IsReply);
            Assert.Equal(round, c.Tick);
        }

        inbox.TryAddEntry(1, in entry);
        inbox.Clear();
        Assert.Equal(0, inbox.Count);
        Assert.Equal(3, inbox.Capacity);
    }

    [Fact]
    public void MirrorDrainStep_DispatchesToEveryConsumerInRegistrationOrder_AndCountsUnconsumed()
    {
        var inbox = new ClientInbox(8);
        var drain = new MirrorDrainStep(inbox);
        var entry = new EventEntry { Kind = Rep, PayloadLength = 4 };
        var builder = new ClientLayerBuilder(new ClientHostClock());
        drain.BindTo(builder);
        ClientLayerType layer = builder.Build();

        inbox.TryAddEntry(1, in entry);
        layer.Step(true);
        Assert.Equal(1L, drain.UnconsumedCount);

        var first = new RecordingConsumer();
        var second = new RecordingConsumer();
        drain.Register(first);
        drain.Register(second);
        inbox.TryAddEntry(2, in entry);
        inbox.TryAddReply(2, Rp(3));
        layer.Step(false);

        Assert.Equal(new[] { "E", "R" }, first.Log);
        Assert.Equal(new[] { "E", "R" }, second.Log);
        Assert.Equal(1L, drain.UnconsumedCount);
        Assert.Throws<ArgumentNullException>(() => new MirrorDrainStep(null!));
        Assert.Throws<ArgumentNullException>(() => drain.Register(null!));
        Assert.Throws<ArgumentNullException>(() => drain.BindTo(null!));
    }

    // ------------------------------------------------------------ 0 B steady state

    [Fact]
    [Trait("Id", "Guardrail")]
    public void SteadyState_InboundDecode_AllocatesZeroBytes()
    {
        // Arrange - prebuilt wire bytes, a request/reply cycle, an orphan reply and a drained inbox
        var rig = new Rig();
        byte[] batch = Batch(5, EvRec(Rep, 1), EvRec(Chunk, 2, true), EvRec(Rep, 3));
        byte[] orphan = BareReply(Rp(60_000));
        byte[] reply = BareReply(Rp(0));
        var counting = new CountingSink();
        var consumer = new CountingConsumer();
        var drain = new MirrorDrainStep(rig.Inbox);
        drain.Register(consumer);
        var builder = new ClientLayerBuilder(rig.Adapter.Clock);
        drain.BindTo(builder);
        ClientLayerType layer = builder.Build();
        uint tick = 0;

        // Act / Assert
        AllocAssert.Zero(
            "client adapter send + inbound decode + mirror drain",
            () =>
            {
                tick++;
                BinaryPrimitives.WriteUInt32LittleEndian(batch.AsSpan(1), tick);
                ushort id = rig.Adapter.Send(OpKind.TeleportAck, _payload);
                rig.HostEnd.Poll(counting);
                BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(1), id);
                BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(3), (ushort)OpKind.TeleportAck);
                rig.HostEnd.Send(rig.HostEnd.Peer, NetChannel.Reliable, reply);
                rig.HostEnd.Send(rig.HostEnd.Peer, NetChannel.Reliable, batch);
                rig.HostEnd.Send(rig.HostEnd.Peer, NetChannel.Reliable, orphan);
                rig.Adapter.PumpInbound(rig.Inbox);
                layer.Step(true);
            });

        Assert.True(consumer.Entries > 0);
        Assert.True(consumer.Replies > 0);
        Assert.Equal(0, rig.Adapter.PendingCount);
        Assert.True(rig.Adapter.IgnoredReplyCount > 0);
    }

    // ------------------------------------------------------------ end to end against the real HostAdapter

    private sealed class U32Codec : IOpCodec
    {
        public ushort CodecId => 920;

        public byte WireSize => 4;

        public int SampleCount => 1;

        public bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength)
        {
            payloadLength = 0;
            if (payload.Length != 4)
            {
                return false;
            }

            Span<byte> target = dst;
            payload.CopyTo(target);
            payloadLength = 4;
            return true;
        }

        public int Encode(in Op src, Span<byte> dst)
        {
            if (src.PayloadLength != 4 || dst.Length < 4)
            {
                return -1;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(dst, src.PayloadAs<uint>());
            return 4;
        }

        public Op GetSample(int index)
        {
            var op = default(Op);
            op.SetPayload(1u);
            return op;
        }
    }

    private sealed class Relay : IEventSink
    {
        public IEventSink? Target { get; set; }

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow) => Target?.Deliver(tick, entries, in overflow);
    }

    private sealed class HostRig
    {
        private readonly Relay _relay = new();

        public HostRig()
        {
            Drain = new DrainRig(
                b =>
                {
                    b.BindEventSink(_relay);
                    b.BindOp(_probe, new LambdaHandler(OpKind.TeleportAck, (in Op op, in TickContext _) => Reply.Ok(in op)));
                },
                instrument: false);
            IOpCodec[] core = SimCoreCodecs.Create();
            var registry = new OpCodecRegistry(Drain.World.OpDescriptors, new IOpCodec[] { core[0], core[1], new U32Codec() });
            (LoopbackTransport hostEnd, LoopbackTransport clientEnd) = LoopbackTransport.CreatePair();
            var events = new EventCodecRegistry(Drain.World.EventDescriptors, Array.Empty<IEventCodec>());
            Host = new HostAdapter(Drain.World, registry, hostEnd, null, new AdapterSettings(), new InMemoryRoster(), events);
            _relay.Target = Host;
            Client = new ClientAdapter(clientEnd, clientEnd.Peer, events);
            Assert.Equal(AdmitResult.Ok, Host.AdmitLocal(hostEnd.Peer, _slot0));
            Assert.True(Host.CompleteJoin(HostAdapter.JoinReqIdBase + 0, 0, new EntityId(DrainRig.PlayerA.Value)));
            HostFrame();
            Inbox.Clear();
        }

        public DrainRig Drain { get; }

        public HostAdapter Host { get; }

        public ClientAdapter Client { get; }

        public ClientInbox Inbox { get; } = new(64);

        /// <summary>One host frame: pump requests, tick the world, flush batches. Then the client pumps.</summary>
        public void HostFrame()
        {
            Host.PumpInbound();
            Drain.World.Tick();
            Host.FlushOutbound(Drain.World.CurrentTick);
            Client.PumpInbound(Inbox);
        }
    }

    [Fact]
    [Trait("Id", "TR-sim-014")]
    public void EndToEnd_RequestReachesTheRealHost_AndItsReplyComesBackThroughTheInbox()
    {
        var rig = new HostRig();
        ushort id = rig.Client.Send(OpKind.TeleportAck, _payload);

        rig.HostFrame();

        List<ClientInboxRecord> records = new();
        while (rig.Inbox.TryDequeue(out ClientInboxRecord r))
        {
            records.Add(r);
        }

        ClientInboxRecord record = Assert.Single(records);
        Assert.True(record.IsReply);
        Reply reply = record.Entry.PayloadAs<Reply>();
        Assert.Equal(id, (ushort)reply.ReqId);
        Assert.Equal(OpKind.TeleportAck, reply.Kind);
        Assert.Equal(ReplyResult.Ok, reply.Result);
        Assert.True(rig.Client.Clock.IsValid);
        Assert.Equal(rig.Drain.World.CurrentTick, rig.Client.Clock.EstimatedHostTick);
        Assert.Equal(0, rig.Client.PendingCount);
        Assert.Equal(0L, rig.Client.MalformedInboundCount);
    }

    [Fact]
    [Trait("Id", "TR-sim-014")]
    public void EndToEnd_AMalformedRequest_GetsTheHostsBadRequestReplyInTheInbox()
    {
        var rig = new HostRig();
        ushort id = rig.Client.Send(OpKind.TeleportAck, new byte[] { 1, 2, 3 }); // the kind's wire size is 4

        rig.HostFrame();

        Assert.True(rig.Inbox.TryDequeue(out ClientInboxRecord record));
        Reply reply = record.Entry.PayloadAs<Reply>();
        Assert.Equal(CoreReason.BadRequest, reply.Reason);
        Assert.Equal(id, (ushort)reply.ReqId);
        Assert.Equal(0, rig.Client.PendingCount);
    }

    [Fact]
    [Trait("Id", "TR-sim-014")]
    public void EndToEnd_TwoSequentialRequests_EachGetExactlyOneReply()
    {
        var rig = new HostRig();
        rig.Client.Send(OpKind.TeleportAck, _payload);
        rig.HostFrame();
        rig.Inbox.Clear();

        rig.Client.Send(OpKind.TeleportAck, _payload);
        rig.HostFrame();

        Assert.Equal(2L, rig.Client.RepliesDelivered);
        Assert.Equal(0L, rig.Client.IgnoredReplyCount);
        Assert.Equal(0, rig.Client.PendingCount);
    }
}
