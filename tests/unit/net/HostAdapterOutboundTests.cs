using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// network-adapter-loopback story 004: <see cref="HostAdapter"/> as <see cref="IEventSink"/> (copy-only <c>Deliver</c>, audience
/// fan-out through the slot to peer map) and <see cref="HostAdapter.FlushOutbound"/> (one ordered batch per peer per tick).
/// Deterministic: no I/O, no time. Non-parallel because of the allocation test.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class HostAdapterOutboundTests
{
    private const EventKind Rep = (EventKind)0x0F10;
    private const EventKind Own = (EventKind)0x0F11;
    private const EventKind Intl = (EventKind)0x0F12;
    private const EventKind NoWire = (EventKind)0x0F13;
    private const EventKind Missing = (EventKind)0x0F14;
    private const ushort ProbeCodecId = 920;

    private static readonly PlayerSlot _slot0 = PlayerSlot.FromValue(0);
    private static readonly PlayerSlot _slot1 = PlayerSlot.FromValue(1);
    private static readonly PlayerSlot _slot2 = PlayerSlot.FromValue(2);
    private static readonly PlayerSlot _slot3 = PlayerSlot.FromValue(3);
    private static readonly OpDescriptor _probe = OpDescriptor.For<uint>(OpKind.TeleportAck, false, ProbeCodecId, 4);

    // ------------------------------------------------------------ fixtures

    private sealed class U32Codec : IOpCodec
    {
        public ushort CodecId => ProbeCodecId;

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

    /// <summary>The world needs its sink at build time and the adapter needs the world: the relay closes the loop.</summary>
    private sealed class Relay : IEventSink
    {
        public IEventSink? Target { get; set; }

        public bool Capture { get; set; }

        public List<EventEntry[]> Ticks { get; } = new();

        public List<uint> TickNumbers { get; } = new();

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
        {
            if (Capture)
            {
                Ticks.Add(entries.ToArray());
                TickNumbers.Add(tick);
            }

            Target?.Deliver(tick, entries, in overflow);
        }
    }

    /// <summary>A header-only batch (tag 2, count 0): the host's quiet-tick heartbeat (TD-068 item 1), ignored by these tests; see HostLoopbackSessionTests for its own coverage.</summary>
    private static bool IsHeartbeat(ReadOnlySpan<byte> bytes) => bytes.Length == 7 && bytes[0] == 0x02 && bytes[5] == 0 && bytes[6] == 0;

    private sealed class RemoteSpy : ITransport
    {
        public List<(PeerHandle Peer, NetChannel Channel, byte[] Bytes)> Sent { get; } = new();

        public void Poll(ITransportSink sink)
        {
        }

        /// <summary>When true, sends are only counted (the allocation test: copying the bytes would allocate).</summary>
        public bool CountOnly { get; set; }

        public long SendCount { get; private set; }

        public long HeartbeatCount { get; private set; }

        public void Send(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> bytes)
        {
            if (IsHeartbeat(bytes))
            {
                HeartbeatCount++;
                return;
            }

            SendCount++;
            if (!CountOnly)
            {
                Sent.Add((peer, channel, bytes.ToArray()));
            }
        }

        public void Disconnect(PeerHandle peer, DisconnectReason reason)
        {
        }
    }

    private sealed class CopySink : ITransportSink
    {
        public List<(NetChannel Channel, byte[] Bytes)> Messages { get; } = new();

        public void OnConnected(PeerHandle peer)
        {
        }

        public void OnDisconnected(PeerHandle peer, DisconnectReason reason)
        {
        }

        public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload)
        {
            if (!IsHeartbeat(payload))
            {
                Messages.Add((channel, payload.ToArray()));
            }
        }
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

    private readonly record struct Rec(EventKind Kind, bool Continuation, uint Value, Reply Reply);

    private sealed class Rig
    {
        private readonly Relay _relay = new();

        public Rig(LambdaHandler.ApplyFn? apply = null, int maxBatchBytes = EventBatch.MaxBufferSize, bool capture = false)
        {
            LambdaHandler.ApplyFn body = apply ?? ((in Op op, in TickContext _) => Reply.Ok(in op));
            _relay.Capture = capture;
            Drain = new DrainRig(
                b =>
                {
                    b.BindEventSink(_relay);
                    b.BindEventKind(EventDescriptor.For<uint>(Rep, ReplicationClass.Replicated, false, 1, 910, 4));
                    b.BindEventKind(EventDescriptor.For<uint>(Own, ReplicationClass.OwnerOnly, false, 1, 911, 4));
                    b.BindEventKind(EventDescriptor.For<uint>(Intl, ReplicationClass.Internal, false, 1, 0, 4));
                    b.BindEventKind(EventDescriptor.For<uint>(NoWire, ReplicationClass.Replicated, false, 1, 0, 4));
                    b.BindEventKind(EventDescriptor.For<uint>(Missing, ReplicationClass.Replicated, false, 1, 999, 4));
                    b.BindOp(_probe, new LambdaHandler(OpKind.TeleportAck, body));
                },
                instrument: false);
            IOpCodec[] core = SimCoreCodecs.Create();
            Registry = new OpCodecRegistry(World.OpDescriptors, new IOpCodec[] { core[0], core[1], new U32Codec() });
            Events = new EventCodecRegistry(World.EventDescriptors, new IEventCodec[] { new FixedLengthEventCodec(910, 4), new FixedLengthEventCodec(911, 4) });
            (HostEnd, ClientEnd) = LoopbackTransport.CreatePair();
            Adapter = new HostAdapter(World, Registry, HostEnd, Remote, new AdapterSettings { MaxBatchBytes = maxBatchBytes }, Roster, Events);
            _relay.Target = Adapter;
        }

        public DrainRig Drain { get; }

        public SimWorld World => Drain.World;

        public Relay Sink => _relay;

        public OpCodecRegistry Registry { get; }

        public EventCodecRegistry Events { get; }

        public LoopbackTransport HostEnd { get; }

        public LoopbackTransport ClientEnd { get; }

        public RemoteSpy Remote { get; } = new();

        public InMemoryRoster Roster { get; } = new();

        public HostAdapter Adapter { get; }

        /// <summary>Maps the loopback peer to slot 0 (the Sim's PlayerA) without a Sim join (its queued PlayerJoin is refused later and matches nothing).</summary>
        public void AdmitLoopbackAtSlot0()
        {
            Assert.Equal(AdmitResult.Ok, Adapter.AdmitLocal(HostEnd.Peer, _slot0));
            Assert.True(Adapter.CompleteJoin(HostAdapter.JoinReqIdBase + 0, 0, new EntityId(DrainRig.PlayerA.Value)));
        }

        public void AdmitRemote(PeerHandle peer, PlayerSlot slot, uint playerId)
        {
            Assert.Equal(AdmitResult.Ok, Adapter.Admit(PeerOrigin.Remote, peer, slot, 100UL + peer.Value));
            Assert.True(Adapter.CompleteJoin(HostAdapter.JoinReqIdBase + slot.Value, 0, new EntityId(playerId)));
        }

        public List<byte[]> ClientMessages()
        {
            var sink = new CopySink();
            ClientEnd.Poll(sink);
            var list = new List<byte[]>();
            foreach ((NetChannel channel, byte[] bytes) in sink.Messages)
            {
                Assert.Equal(NetChannel.Reliable, channel);
                list.Add(bytes);
            }

            return list;
        }
    }

    private static readonly PeerHandle _remoteA = new(41);
    private static readonly PeerHandle _remoteB = new(42);

    private static EventEntry Ev(EventKind kind, ReplicationClass cls, byte audience, uint value, uint callSeq = 1, EventFlags flags = 0)
    {
        var e = new EventEntry { Kind = kind, Class = cls, Audience = audience, CallSeq = callSeq, PayloadLength = 4, Flags = flags };
        Span<byte> p = e.Payload;
        BinaryPrimitives.WriteUInt32LittleEndian(p, value);
        return e;
    }

    private static EventEntry ReplyEv(byte audience, uint reqId, ushort reason = 0, ReplyResult? result = null, uint extra = 0)
    {
        var r = new Reply { ReqId = reqId, Kind = OpKind.TeleportAck, Reason = reason, Result = result ?? (reason == 0 ? ReplyResult.Ok : ReplyResult.Rejected) };
        if (extra != 0)
        {
            r.SetExtra(extra);
        }

        var e = new EventEntry { Kind = EventKind.OpReply, Class = ReplicationClass.OwnerOnly, Audience = audience, CallSeq = 1, PayloadLength = Reply.SizeBytes };
        Span<byte> p = e.Payload;
        MemoryMarshal.Write(p, in r);
        return e;
    }

    private static (uint Tick, List<Rec> Records) Decode(byte[] message)
    {
        Assert.True(EventBatch.TryReadHeader(message, out uint tick, out ushort count));
        var list = new List<Rec>();
        int at = EventBatch.HeaderSize;
        for (int i = 0; i < count; i++)
        {
            ushort field = BinaryPrimitives.ReadUInt16LittleEndian(message.AsSpan(at));
            at += EventBatch.RecordHeaderSize;
            var kind = (EventKind)(field & EventBatch.MaxKind);
            bool continuation = (field & EventBatch.ContinuationBit) != 0;
            if (kind == EventKind.OpReply)
            {
                Assert.True(ReplyCodec.TryDecodeBody(message.AsSpan(at), out Reply reply, out int used));
                list.Add(new Rec(kind, continuation, 0, reply));
                at += used;
            }
            else
            {
                list.Add(new Rec(kind, continuation, BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(at)), default));
                at += 4;
            }
        }

        Assert.Equal(message.Length, at);
        return (tick, list);
    }

    private static Op ProbeOp(uint reqId)
    {
        var op = new Op { Kind = OpKind.TeleportAck, ReqId = reqId };
        op.SetPayload(5u);
        return op;
    }

    // ------------------------------------------------------------ AC-1 copy-only Deliver

    [Fact]
    public void Ac1_Deliver_IsCopyOnly_FansOutBySlot_AndSurvivesTheSpanBeingOverwritten()
    {
        // Arrange: loopback peer at slot 0, remote peer at slot 3; slot 6 has no peer.
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        rig.AdmitRemote(_remoteA, _slot3, DrainRig.PlayerB.Value);
        EventEntry[] log =
        {
            Ev(Intl, ReplicationClass.Internal, 0, 1),
            Ev(Rep, ReplicationClass.Replicated, 0b0000_1001, 11),
            Ev(Own, ReplicationClass.OwnerOnly, 1 << 3, 22),
            ReplyEv(1 << 0, 5),
            Ev(Rep, ReplicationClass.Replicated, 0b0100_0001, 33),
        };

        // Act: Deliver, then the Sim overwrites its span (the Debug 0xDD fill).
        rig.Adapter.Deliver(10, log, default);
        long hostSendsAfterDeliver = rig.HostEnd.SentCount;
        int remoteSendsAfterDeliver = rig.Remote.Sent.Count;
        MemoryMarshal.AsBytes(log.AsSpan()).Fill(0xDD);

        // Assert: nothing reached a transport inside Deliver; entries are in the adapter's own buffers.
        Assert.Equal(0, hostSendsAfterDeliver);
        Assert.Equal(0, remoteSendsAfterDeliver);
        Assert.True(rig.Adapter.TryGetOutbound(PeerOrigin.Loopback, rig.HostEnd.Peer, out int loopQueued, out long loopBytes, out _, out _));
        Assert.Equal(3, loopQueued);
        Assert.True(loopBytes > 0);
        Assert.True(rig.Adapter.TryGetOutbound(PeerOrigin.Remote, _remoteA, out int remoteQueued, out _, out _, out _));
        Assert.Equal(2, remoteQueued);
        Assert.Equal(1, rig.Adapter.UnmappedAudienceDropCount); // slot 6's bit of the last entry

        rig.Adapter.FlushOutbound(11);

        (uint tick, List<Rec> loop) = Decode(Assert.Single(rig.ClientMessages()));
        Assert.Equal(10u, tick);
        Assert.Equal(new[] { Rep, EventKind.OpReply, Rep }, loop.Select(r => r.Kind).ToArray());
        Assert.Equal(11u, loop[0].Value);
        Assert.Equal(5u, loop[1].Reply.ReqId);
        Assert.Equal(33u, loop[2].Value);
        (PeerHandle peer, NetChannel channel, byte[] bytes) = Assert.Single(rig.Remote.Sent);
        Assert.Equal(_remoteA, peer);
        Assert.Equal(NetChannel.Reliable, channel);
        (_, List<Rec> remote) = Decode(bytes);
        Assert.Equal(new[] { (Rep, 11u), (Own, 22u) }, remote.Select(r => (r.Kind, r.Value)).ToArray());
        Assert.True(rig.Adapter.TryGetOutbound(PeerOrigin.Remote, _remoteA, out int afterQueued, out long afterBytes, out long sentBytes, out long sentBatches));
        Assert.Equal(0, afterQueued);
        Assert.Equal(0, afterBytes);
        Assert.Equal(bytes.Length, sentBytes);
        Assert.Equal(1, sentBatches);
    }

    [Fact]
    public void Ac1_KindsWithoutACodec_AreDroppedAndCounted()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        EventEntry[] log =
        {
            Ev(NoWire, ReplicationClass.Replicated, 1, 1),
            Ev(Missing, ReplicationClass.Replicated, 1, 2),
            Ev(DrainRig.TestEvent, ReplicationClass.Replicated, 1, 3),
            Ev((EventKind)0x7000, ReplicationClass.Replicated, 1, 4), // undeclared
        };

        rig.Adapter.Deliver(1, log, default);
        rig.Adapter.FlushOutbound(1);

        Assert.Equal(4, rig.Adapter.NoCodecDropCount);
        Assert.Equal(1, rig.Events.MissingCodecCount);
        Assert.Equal(2, rig.Events.NoWireKindCount); // NoWire and DrainRig.TestEvent: Replicated with WireCodecId 0
        Assert.Empty(rig.ClientMessages());
    }

    // ------------------------------------------------------------ AC-2 ordered batch

    [Fact]
    public void Ac2_WorldTick_OneBatchInLogOrder_HandlerEventsPrecedeTheirReply_NoInternal()
    {
        // Arrange: the handler raises Replicated, Internal, OwnerOnly; Sim Core appends the OpReply after them.
        Rig? rig = null;
        rig = new Rig(
            (in Op op, in TickContext _) =>
            {
                uint a = 1, b = 2, c = 3;
                rig!.World.Events.TryRaise(Rep, 0b0000_1001, in a);
                rig.World.Events.TryRaise(Intl, 0, in b);
                rig.World.Events.TryRaise(Own, 1 << 0, in c);
                return Reply.Ok(in op);
            },
            capture: true);
        rig.AdmitLoopbackAtSlot0();
        rig.AdmitRemote(_remoteA, _slot3, DrainRig.PlayerB.Value);
        Assert.True(rig.World.EnqueueOp(DrainRig.PlayerA, ProbeOp(77)));

        // Act
        rig.World.Tick();
        rig.Adapter.FlushOutbound(rig.World.CurrentTick);

        // Assert: exactly one batch for the delivered tick, in log order, reply last, no Internal.
        (uint tick, List<Rec> loop) = Decode(Assert.Single(rig.ClientMessages()));
        Assert.Equal(rig.Sink.TickNumbers[^1], tick);
        Assert.Equal(new[] { Rep, Own, EventKind.OpReply }, loop.Select(r => r.Kind).ToArray());
        Assert.Equal(77u, loop[2].Reply.ReqId);
        Assert.True(loop[2].Reply.IsOk);
        (_, List<Rec> remote) = Decode(Assert.Single(rig.Remote.Sent).Bytes);
        Assert.Equal(new[] { Rep }, remote.Select(r => r.Kind).ToArray());

        // TD-048 item 1: the OpReply carries its handler's CallSeq, so the adapter can link them.
        EventEntry[] log = rig.Sink.Ticks[^1];
        EventEntry replyEntry = log.Single(e => e.Kind == EventKind.OpReply && e.PayloadAs<Reply>().ReqId == 77u);
        EventEntry rep = log.Single(e => e.Kind == Rep);
        Assert.NotEqual(0u, replyEntry.CallSeq);
        Assert.Equal(rep.CallSeq, replyEntry.CallSeq);
    }

    [Fact]
    public void Ac2_EmptyTick_SendsOnlyHeartbeats_AndAPeerWithNothingGetsAHeaderOnlyBatch()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        rig.AdmitRemote(_remoteA, _slot3, DrainRig.PlayerB.Value);

        rig.World.Tick(); // only the queued PlayerJoin replies (System actor, audience 0)
        rig.Adapter.FlushOutbound(rig.World.CurrentTick);
        Assert.Empty(rig.ClientMessages());   // TD-068 item 1: header-only batches are filtered by the helpers
        Assert.Empty(rig.Remote.Sent);
        Assert.Equal(0, rig.Remote.SendCount);
        Assert.Equal(1, rig.Remote.HeartbeatCount);
        Assert.Equal(1, rig.HostEnd.SentCount);   // the loopback peer's heartbeat
        Assert.Equal(2L, rig.Adapter.HeartbeatsSent);
        Assert.Equal(0L, rig.Adapter.BatchesSent);

        rig.Adapter.FlushOutbound(rig.World.CurrentTick); // a repeated tick (the world could not tick) sends nothing at all
        Assert.Equal(2L, rig.Adapter.HeartbeatsSent);

        rig.Adapter.Deliver(50, new[] { Ev(Own, ReplicationClass.OwnerOnly, 1 << 3, 9) }, default);
        rig.Adapter.FlushOutbound(50);
        Assert.Empty(rig.ClientMessages());
        Assert.Single(rig.Remote.Sent);
        Assert.Equal(1, rig.Adapter.BatchesSent);
        Assert.Equal(3L, rig.Adapter.HeartbeatsSent); // the loopback peer had nothing on tick 50; the remote peer had its batch
    }

    [Fact]
    public void Heartbeat_NotSentToAPeerWithARealBatchThatTick_NorToAJoiningPeer()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        Assert.Equal(AdmitResult.Ok, rig.Adapter.Admit(PeerOrigin.Remote, _remoteA, _slot3, 141UL)); // stays Joining

        rig.Adapter.Deliver(50, new[] { Ev(Rep, ReplicationClass.Replicated, 0b0000_1001, 1) }, default);
        rig.Adapter.FlushOutbound(50);

        Assert.Equal(1, rig.HostEnd.SentCount);          // the real batch only
        Assert.Single(rig.ClientMessages());
        Assert.Equal(0L, rig.Adapter.HeartbeatsSent);
        Assert.Equal(0, rig.Remote.SendCount);            // Joining: no batch (slot 3 unmapped) and no heartbeat
        Assert.Equal(0, rig.Remote.HeartbeatCount);

        rig.Adapter.FlushOutbound(51);                    // quiet tick
        Assert.Equal(2, rig.HostEnd.SentCount);
        Assert.Equal(1L, rig.Adapter.HeartbeatsSent);
        Assert.Equal(0, rig.Remote.HeartbeatCount);
    }

    [Fact]
    public void Ac2_ContinuationRun_ArrivesContiguousAndInOrder()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        EventEntry[] log =
        {
            Ev(Rep, ReplicationClass.Replicated, 1, 1, callSeq: 4),
            Ev(Rep, ReplicationClass.Replicated, 1, 2, callSeq: 4, flags: EventFlags.Continuation),
            Ev(Rep, ReplicationClass.Replicated, 1, 3, callSeq: 4, flags: EventFlags.Continuation),
            Ev(Own, ReplicationClass.OwnerOnly, 1, 4, callSeq: 5),
        };

        rig.Adapter.Deliver(3, log, default);
        rig.Adapter.FlushOutbound(3);

        (_, List<Rec> records) = Decode(Assert.Single(rig.ClientMessages()));
        Assert.Equal(new[] { 1u, 2u, 3u, 4u }, records.Select(r => r.Value).ToArray());
        Assert.Equal(new[] { false, true, true, false }, records.Select(r => r.Continuation).ToArray());
    }

    [Fact]
    public void Ac2_TwoTicksDeliveredBeforeOneFlush_GoAsTwoBatchesInTickOrder()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();

        rig.Adapter.Deliver(1, new[] { Ev(Rep, ReplicationClass.Replicated, 1, 10) }, default);
        rig.Adapter.Deliver(2, new[] { Ev(Rep, ReplicationClass.Replicated, 1, 20) }, default);
        rig.Adapter.FlushOutbound(2);

        List<byte[]> messages = rig.ClientMessages();
        Assert.Equal(2, messages.Count);
        Assert.Equal((1u, 10u), (Decode(messages[0]).Tick, Decode(messages[0]).Records[0].Value));
        Assert.Equal((2u, 20u), (Decode(messages[1]).Tick, Decode(messages[1]).Records[0].Value));
    }

    [Fact]
    public void Ac2_RecordsBeyondMaxBatchBytes_SplitIntoOrderedBatchesOfTheSameTick()
    {
        var rig = new Rig(maxBatchBytes: EventBatch.MinBufferSize);
        rig.AdmitLoopbackAtSlot0();
        var log = new EventEntry[200];
        for (int i = 0; i < log.Length; i++)
        {
            log[i] = Ev(Rep, ReplicationClass.Replicated, 1, (uint)i);
        }

        rig.Adapter.Deliver(8, log, default);
        rig.Adapter.FlushOutbound(8);

        List<byte[]> messages = rig.ClientMessages();
        Assert.True(messages.Count >= 2);
        Assert.All(messages, m => Assert.True(m.Length <= EventBatch.MinBufferSize));
        var values = messages.SelectMany(m => Decode(m).Records).Select(r => r.Value).ToArray();
        Assert.Equal(Enumerable.Range(0, 200).Select(i => (uint)i).ToArray(), values);
        Assert.All(messages, m => Assert.Equal(8u, Decode(m).Tick));
        Assert.Equal(messages.Count - 1, rig.Adapter.BatchSplitCount);
    }

    // ------------------------------------------------------------ AC-3 peers, not slots

    [Fact]
    public void Ac3_SlotTakenByANewcomerBeforeFlush_NewcomerDoesNotReceiveTheOldEntry()
    {
        // Arrange: _remoteA holds slot 2 when tick 5 is delivered.
        var rig = new Rig();
        rig.AdmitRemote(_remoteA, _slot2, 0x0100_0101u);
        rig.Adapter.Deliver(5, new[] { Ev(Own, ReplicationClass.OwnerOnly, 1 << 2, 9) }, default);

        // Act: _remoteA leaves, _remoteB takes slot 2, then the flush runs.
        rig.Adapter.RemoteSink.OnDisconnected(_remoteA, DisconnectReason.Requested);
        Assert.False(rig.Adapter.TryGetOutbound(PeerOrigin.Remote, _remoteA, out _, out _, out _, out _));
        rig.AdmitRemote(_remoteB, _slot2, 0x0100_0102u);
        rig.Adapter.FlushOutbound(5);

        // Assert: nobody receives tick 5's entry; _remoteB gets what is delivered while it holds the slot.
        Assert.Empty(rig.Remote.Sent);
        rig.Adapter.Deliver(6, new[] { Ev(Own, ReplicationClass.OwnerOnly, 1 << 2, 10) }, default);
        rig.Adapter.FlushOutbound(6);
        (PeerHandle peer, _, byte[] bytes) = Assert.Single(rig.Remote.Sent);
        Assert.Equal(_remoteB, peer);
        Assert.Equal(10u, Decode(bytes).Records[0].Value);
    }

    [Fact]
    public void Ac3_JoiningPeer_IsNotMappedUntilAdmitted()
    {
        var rig = new Rig();
        Assert.Equal(AdmitResult.Ok, rig.Adapter.Admit(PeerOrigin.Remote, _remoteA, _slot2, 5UL)); // Joining

        rig.Adapter.Deliver(1, new[] { Ev(Rep, ReplicationClass.Replicated, 1 << 2, 1) }, default);
        rig.Adapter.FlushOutbound(1);

        Assert.Empty(rig.Remote.Sent);
        Assert.Equal(1, rig.Adapter.UnmappedAudienceDropCount);
    }

    // ------------------------------------------------------------ join completion (TD-062 item 1)

    [Fact]
    public void JoinReply_ThroughDeliver_AdmitsThePeer_AndALaterRequestIsStampedWithThatActor()
    {
        // Arrange: slot 1 is free in the Sim; the real PlayerJoinHandler allocates the id.
        var rig = new Rig();
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(rig.HostEnd.Peer, _slot1));

        // Act: the world drains the System PlayerJoin and delivers its reply to the adapter.
        rig.World.Tick();
        rig.Adapter.FlushOutbound(rig.World.CurrentTick);

        // Assert: admitted with the Sim's id; the join reply was never encoded to the peer.
        Assert.Equal(PeerState.Admitted, rig.Adapter.GetPeerState(PeerOrigin.Loopback, rig.HostEnd.Peer));
        Assert.True(rig.Adapter.TryGetPeer(PeerOrigin.Loopback, rig.HostEnd.Peer, out EntityId player, out PlayerSlot slot));
        Assert.False(player.IsNone);
        Assert.Equal(_slot1, slot);
        Assert.Equal(_slot1, rig.Drain.Slots.SlotOf(player));
        Assert.Equal(player, rig.Roster.Lookup(HostAdapter.LocalAccountKey));
        Assert.Empty(rig.ClientMessages());

        // A subsequent request from the peer is enqueued with that actor.
        byte[] buffer = new byte[RequestEnvelope.MaxSize];
        Op request = ProbeOp(3);
        int length = RequestEnvelope.Encode(rig.Registry, in request, buffer);
        rig.ClientEnd.Send(rig.ClientEnd.Peer, NetChannel.Reliable, buffer.AsSpan(0, length));
        rig.Adapter.PumpInbound();
        Assert.Equal(1, rig.Adapter.EnqueuedCount);
        Op queued = rig.World.Ops.PeekAt(0);
        Assert.Equal(player.Value, queued.Actor.Value);
    }

    [Fact]
    public void JoinReply_Refused_FallsBackToConnected_AndUnmatchedJoinRepliesAreConsumed()
    {
        var rig = new Rig();
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(rig.HostEnd.Peer, _slot0)); // the Sim's slot 0 is taken

        rig.World.Tick();
        rig.Adapter.FlushOutbound(rig.World.CurrentTick);

        Assert.Equal(PeerState.Connected, rig.Adapter.GetPeerState(PeerOrigin.Loopback, rig.HostEnd.Peer));
        Assert.Equal(1, rig.Adapter.JoinRefusedCount);

        // A join reply nobody waits for, even one with an audience bit, is consumed and never encoded.
        rig.AdmitRemote(_remoteA, _slot3, DrainRig.PlayerB.Value);
        rig.Adapter.Deliver(90, new[] { ReplyEv(1 << 3, HostAdapter.JoinReqIdBase + 3) }, default);
        rig.Adapter.FlushOutbound(90);
        Assert.Equal(1, rig.Adapter.UnmatchedJoinReplyCount);
        Assert.Empty(rig.Remote.Sent);
    }

    [Fact]
    public void JoinReply_WithoutASimId_UsesTheIdTheAdapterPutInThePayload()
    {
        var rig = new Rig();
        var known = new EntityId(0x0100_0042u);
        Assert.True(rig.Roster.Record(HostAdapter.LocalAccountKey, known));
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(rig.HostEnd.Peer, _slot1));

        rig.Adapter.Deliver(1, new[] { ReplyEv(0, HostAdapter.JoinReqIdBase + 1) }, default);

        Assert.True(rig.Adapter.TryGetPeer(PeerOrigin.Loopback, rig.HostEnd.Peer, out EntityId player, out _));
        Assert.Equal(known, player);
    }

    // ------------------------------------------------------------ repeated tick (TD-057 item 1), stale, overflow

    [Fact]
    public void RepeatedFlushTick_SendsNothingNew_AndAStaleDeliverIsIgnored()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        rig.Adapter.Deliver(4, new[] { Ev(Rep, ReplicationClass.Replicated, 1, 1) }, default);
        rig.Adapter.FlushOutbound(4);
        Assert.Single(rig.ClientMessages());

        rig.Adapter.FlushOutbound(4); // the world could not tick
        Assert.Empty(rig.ClientMessages());
        Assert.Equal(1, rig.Adapter.RepeatedFlushTickCount);

        rig.Adapter.Deliver(4, new[] { Ev(Rep, ReplicationClass.Replicated, 1, 2) }, default); // a tick already flushed
        rig.Adapter.FlushOutbound(4);
        Assert.Empty(rig.ClientMessages());
        Assert.Equal(1, rig.Adapter.StaleDeliverCount);
        Assert.Equal(1, rig.Adapter.BatchesSent);
    }

    [Fact]
    public void LateDuplicateDeliver_AfterAnEmptyFlush_IsStale_ButAFirstDeliverAfterAnEmptyFlushGoesOut()
    {
        // Arrange: tick 5 delivered with nothing for any peer, then flushed (nothing sent).
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        rig.Adapter.Deliver(5, new[] { Ev(Intl, ReplicationClass.Internal, 0, 1) }, default);
        rig.Adapter.FlushOutbound(5);

        // Act + Assert: a second Deliver(5) is a duplicate and is ignored.
        rig.Adapter.Deliver(5, new[] { Ev(Rep, ReplicationClass.Replicated, 1, 2) }, default);
        rig.Adapter.FlushOutbound(5);
        Assert.Equal(1, rig.Adapter.StaleDeliverCount);
        Assert.Empty(rig.ClientMessages());

        // A flush for tick 6 that the world could not run, then tick 6's first real Deliver: it goes out.
        rig.Adapter.FlushOutbound(6);
        rig.Adapter.Deliver(6, new[] { Ev(Rep, ReplicationClass.Replicated, 1, 3) }, default);
        rig.Adapter.FlushOutbound(6);
        (uint tick, List<Rec> records) = Decode(Assert.Single(rig.ClientMessages()));
        Assert.Equal(6u, tick);
        Assert.Equal(3u, records[0].Value);
        Assert.Equal(1, rig.Adapter.StaleDeliverCount);
    }

    [Fact]
    public void RepeatedFlushTick_FromAStoppedWorld_SendsNothing()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        rig.World.Tick();
        rig.Adapter.FlushOutbound(rig.World.CurrentTick);
        rig.ClientMessages();

        rig.Adapter.FlushOutbound(rig.World.CurrentTick);
        rig.Adapter.FlushOutbound(rig.World.CurrentTick);

        Assert.Empty(rig.ClientMessages());
        Assert.Equal(2, rig.Adapter.RepeatedFlushTickCount);
    }

    [Fact]
    public void Overflow_IsStoredOnly()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        var overflow = new OverflowInfo(true, 0b0000_0001, 0b10UL, 5);

        rig.Adapter.Deliver(7, ReadOnlySpan<EventEntry>.Empty, in overflow);
        rig.Adapter.FlushOutbound(7);

        Assert.Equal(1, rig.Adapter.OverflowTickCount);
        Assert.Equal(7u, rig.Adapter.LastOverflowTick);
        Assert.Equal(0b10UL, rig.Adapter.LastOverflow.DroppedKindsMask);
        Assert.Equal((byte)1, rig.Adapter.LastOverflow.DroppedAudienceMask);
        Assert.Empty(rig.ClientMessages());
    }

    [Fact]
    public void OutboundBuffer_FullAcrossUnflushedTicks_DropsAndCounts()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        int capacity = rig.World.Settings.EventLogCapacity;
        var log = new EventEntry[capacity];
        for (int i = 0; i < capacity; i++)
        {
            log[i] = Ev(Rep, ReplicationClass.Replicated, 1, (uint)i);
        }

        rig.Adapter.Deliver(1, log, default);
        rig.Adapter.Deliver(2, new[] { Ev(Rep, ReplicationClass.Replicated, 1, 0) }, default);

        Assert.Equal(1, rig.Adapter.OutboundFullDropCount);
    }

    // ------------------------------------------------------------ encode failure (TD-062 item 2)

    [Fact]
    public void ReplyEncodeFailure_IsCountedAndSkipped_TheRestOfTheBatchStillGoes()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        EventEntry[] log =
        {
            ReplyEv(1, 9, reason: 0, result: ReplyResult.Rejected), // Result contradicts Reason
            Ev(Rep, ReplicationClass.Replicated, 1, 3),
        };

        rig.Adapter.Deliver(2, log, default);
        rig.Adapter.FlushOutbound(2);

        Assert.Equal(1, rig.Adapter.ReplyEncodeFailureCount);
        Assert.Equal(1, rig.Adapter.EventEncodeFailureCount);
        (_, List<Rec> records) = Decode(Assert.Single(rig.ClientMessages()));
        Assert.Equal(new[] { Rep }, records.Select(r => r.Kind).ToArray());

        rig.Adapter.Deliver(3, new[] { ReplyEv(1, 9, reason: 0, result: ReplyResult.Rejected) }, default);
        rig.Adapter.FlushOutbound(3);
        Assert.Empty(rig.ClientMessages()); // a batch whose every record failed is not sent
        Assert.Equal(2, rig.Adapter.ReplyEncodeFailureCount);
    }

    // ------------------------------------------------------------ allocation

    [Fact]
    public void DeliverAndFlush_AllocateNothingAfterWarmUp()
    {
        var rig = new Rig();
        rig.AdmitLoopbackAtSlot0();
        rig.AdmitRemote(_remoteA, _slot3, DrainRig.PlayerB.Value);
        rig.Remote.CountOnly = true;
        EventEntry[] log =
        {
            Ev(Intl, ReplicationClass.Internal, 0, 1),
            Ev(Rep, ReplicationClass.Replicated, 0b0000_1001, 2),
            Ev(Own, ReplicationClass.OwnerOnly, 1, 3),
            ReplyEv(1, 4, extra: 0xABCDu),
            ReplyEv(0, HostAdapter.JoinReqIdBase + 7),
        };
        var sink = new CountingSink();
        uint tick = 0;

        int runs = AllocAssert.Zero("HostAdapter.Deliver + FlushOutbound", () =>
        {
            tick++;
            rig.Adapter.Deliver(tick, log, default);
            rig.Adapter.FlushOutbound(tick);
            rig.ClientEnd.Poll(sink);
        });

        Assert.True(runs > 0);
        Assert.True(sink.Messages > 0);
        Assert.True(rig.Remote.SendCount > 0);
    }

    // ------------------------------------------------------------ codecs, registry, settings

    [Fact]
    public void ReplyRecord_IsTheBareReplyMinusItsTag()
    {
        var reply = new Reply { ReqId = 0x1_0203, Kind = OpKind.TeleportAck, Reason = CoreReason.Busy, Result = ReplyResult.Rejected };
        reply.SetExtra(0x0000_0A0Bu);
        byte[] bare = new byte[ReplyCodec.MaxSize];
        byte[] body = new byte[ReplyCodec.BodyMaxSize];

        int bareLength = ReplyCodec.Encode(in reply, bare);
        int bodyLength = ReplyCodec.Encode(in reply, Span<byte>.Empty) < 0 ? ReplyCodec.EncodeBody(in reply, body) : -1;

        Assert.Equal(bareLength - 1, bodyLength);
        Assert.Equal(ReplyCodec.Tag, bare[0]);
        Assert.Equal(bare.AsSpan(1, bodyLength).ToArray(), body.AsSpan(0, bodyLength).ToArray());
        Assert.True(ReplyCodec.TryDecodeBody(body.AsSpan(0, bodyLength + 3), out Reply back, out int consumed));
        Assert.Equal(bodyLength, consumed);
        Assert.Equal(0x0203u, back.ReqId);
        Assert.False(ReplyCodec.TryDecodeBody(body.AsSpan(0, 3), out _, out _));
        Assert.False(ReplyCodec.TryDecodeBody(body.AsSpan(0, bodyLength - 1), out _, out _));
        Assert.Equal(-1, ReplyCodec.EncodeBody(in reply, body.AsSpan(0, 2)));
    }

    [Fact]
    public void EventCodecs_RefuseWhatTheyCannotEncode()
    {
        byte[] dst = new byte[64];
        var fixed4 = new FixedLengthEventCodec(910, 4);
        EventEntry wide = Ev(Rep, ReplicationClass.Replicated, 1, 1);
        wide.PayloadLength = 8;

        Assert.Equal(-1, fixed4.Encode(in wide, dst));
        Assert.Equal(-1, fixed4.Encode(Ev(Rep, ReplicationClass.Replicated, 1, 1), dst.AsSpan(0, 3)));
        Assert.Equal(-1, ReplyEventCodec.Instance.Encode(Ev(Rep, ReplicationClass.Replicated, 1, 1), dst));
        Assert.Equal(0, ReplyEventCodec.Instance.CodecId);
        Assert.Equal(ReplyCodec.BodyMaxSize, ReplyEventCodec.Instance.MaxWireSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedLengthEventCodec(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedLengthEventCodec(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedLengthEventCodec(1, 49));
    }

    [Fact]
    public void EventCodecRegistry_ValidatesCodecsAndKinds()
    {
        var rep = EventDescriptor.For<uint>(Rep, ReplicationClass.Replicated, false, 1, 910, 4);
        var tooHigh = new EventDescriptor((EventKind)0x8000, ReplicationClass.Replicated, 4, false, 1, 910, 4);
        var codec = new FixedLengthEventCodec(910, 4);

        Assert.Throws<ArgumentNullException>(() => new EventCodecRegistry(new[] { rep }, null!));
        Assert.Throws<ArgumentNullException>(() => new EventCodecRegistry(new[] { rep }, new IEventCodec[] { null! }));
        Assert.Throws<ArgumentException>(() => new EventCodecRegistry(new[] { rep }, new IEventCodec[] { codec, new FixedLengthEventCodec(910, 8) }));
        Assert.Throws<ArgumentException>(() => new EventCodecRegistry(new[] { rep }, new IEventCodec[] { ReplyEventCodec.Instance }));
        Assert.Throws<ArgumentException>(() => new EventCodecRegistry(new[] { tooHigh }, new IEventCodec[] { codec }));

        var registry = new EventCodecRegistry(new[] { rep }, new IEventCodec[] { codec });
        Assert.Same(codec, registry.CodecFor(Rep));
        Assert.Same(ReplyEventCodec.Instance, registry.CodecFor(EventKind.OpReply));
        Assert.Null(registry.CodecFor((EventKind)0x7FFF));
        Assert.Equal(0, registry.MissingCodecCount);
    }

    [Fact]
    public void EventBatch_TryReadHeader_RefusesShortOrUntaggedMessages()
    {
        Assert.False(EventBatch.TryReadHeader(new byte[] { EventBatch.Tag, 0, 0 }, out _, out _));
        Assert.False(EventBatch.TryReadHeader(new byte[] { ReplyCodec.Tag, 0, 0, 0, 0, 0, 0 }, out uint tick, out ushort count));
        Assert.Equal(0u, tick);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(EventBatch.MinBufferSize - 1)]
    [InlineData(EventBatch.MaxBufferSize + 1)]
    public void Constructor_MaxBatchBytesOutOfRange_Throws(int bytes)
    {
        var rig = new Rig();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HostAdapter(rig.World, rig.Registry, rig.HostEnd, null, new AdapterSettings { MaxBatchBytes = bytes }, rig.Roster, rig.Events));
    }

    [Fact]
    public void Constructor_WithoutEventRegistry_OnlyRepliesLeave()
    {
        var rig = new Rig();
        var (hostEnd, clientEnd) = LoopbackTransport.CreatePair();
        var adapter = new HostAdapter(rig.World, rig.Registry, hostEnd, null, AdapterSettings.Default, rig.Roster);
        Assert.Equal(AdmitResult.Ok, adapter.AdmitLocal(hostEnd.Peer, _slot0));
        Assert.True(adapter.CompleteJoin(HostAdapter.JoinReqIdBase, 0, new EntityId(DrainRig.PlayerA.Value)));

        adapter.Deliver(1, new[] { Ev(Rep, ReplicationClass.Replicated, 1, 1), ReplyEv(1, 2) }, default);
        adapter.FlushOutbound(1);

        var sink = new CopySink();
        clientEnd.Poll(sink);
        (_, List<Rec> records) = Decode(Assert.Single(sink.Messages).Bytes);
        Assert.Equal(new[] { EventKind.OpReply }, records.Select(r => r.Kind).ToArray());
        Assert.Equal(1, adapter.NoCodecDropCount);
    }
}
