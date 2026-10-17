using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// network-adapter-loopback story 005: <c>reqId</c> deduplication in <see cref="HostAdapter"/> (last-16 reply cache plus
/// in-flight set per connection; ADR-0007 section 6, ADR-0011 section 3). A real world ticks and delivers into the adapter
/// through a relay, so replies come from the Sim's own <c>OpReply</c> entries. The handler stamps an application counter into
/// each reply's extra, so a resent reply is provably the original and not a second application.
/// Deterministic: no I/O, no time. Non-parallel because of the allocation test.
/// </summary>
[Collection(NonParallelCollection.Name)]
[Trait("Id", "TR-delivery-011")]
public class ReplyCacheTests
{
    private const ushort ProbeCodecId = 920;

    private static readonly PlayerSlot _slot0 = PlayerSlot.FromValue(0);
    private static readonly PlayerSlot _slot3 = PlayerSlot.FromValue(3);
    private static readonly PeerHandle _remoteA = new(41);
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

    private sealed class Relay : IEventSink
    {
        public IEventSink? Target { get; set; }

        public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow) =>
            Target?.Deliver(tick, entries, in overflow);
    }

    /// <summary>A header-only batch (tag 2, count 0): the host's quiet-tick heartbeat (TD-068 item 1), ignored by these tests; see HostLoopbackSessionTests for its own coverage.</summary>
    private static bool IsHeartbeat(ReadOnlySpan<byte> bytes) => bytes.Length == 7 && bytes[0] == 0x02 && bytes[5] == 0 && bytes[6] == 0;

    /// <summary>A remote transport whose inbox is delivered on Poll and whose sends are recorded.</summary>
    private sealed class QueueTransport : ITransport
    {
        private readonly List<(PeerHandle Peer, byte[]? Bytes)> _inbox = new();

        public List<byte[]> Sent { get; } = new();

        public void Queue(PeerHandle peer, byte[] bytes) => _inbox.Add((peer, bytes));

        public void QueueDisconnect(PeerHandle peer) => _inbox.Add((peer, null));

        public void Poll(ITransportSink sink)
        {
            foreach ((PeerHandle peer, byte[]? bytes) in _inbox)
            {
                if (bytes is null)
                {
                    sink.OnDisconnected(peer, DisconnectReason.Kicked);
                    continue;
                }

                sink.OnMessage(peer, NetChannel.Reliable, bytes);
            }

            _inbox.Clear();
        }

        public void Send(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> bytes)
        {
            if (!IsHeartbeat(bytes))
            {
                Sent.Add(bytes.ToArray());
            }
        }

        public void Disconnect(PeerHandle peer, DisconnectReason reason)
        {
        }
    }

    private sealed class CopySink : ITransportSink
    {
        public List<byte[]> Messages { get; } = new();

        public void OnConnected(PeerHandle peer)
        {
        }

        public void OnDisconnected(PeerHandle peer, DisconnectReason reason)
        {
        }

        public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload)
        {
            Assert.Equal(NetChannel.Reliable, channel);
            if (!IsHeartbeat(payload))
            {
                Messages.Add(payload.ToArray());
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

        public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload) => Messages += IsHeartbeat(payload) ? 0 : 1;
    }

    private sealed class Rig
    {
        private readonly Relay _relay = new();
        private readonly byte[] _request = new byte[RequestEnvelope.MaxSize];
        private readonly int _requestLength;

        public Rig(int opCapacity = 4096)
        {
            Drain = new DrainRig(
                b =>
                {
                    b.BindEventSink(_relay);
                    b.BindOp(_probe, new LambdaHandler(OpKind.TeleportAck, Apply));
                },
                opCapacity: opCapacity,
                instrument: false);
            IOpCodec[] core = SimCoreCodecs.Create();
            Registry = new OpCodecRegistry(World.OpDescriptors, new IOpCodec[] { core[0], core[1], new U32Codec() });
            (HostEnd, ClientEnd) = LoopbackTransport.CreatePair();
            Adapter = new HostAdapter(World, Registry, HostEnd, Remote, new AdapterSettings(), new InMemoryRoster());
            _relay.Target = Adapter;
            var op = new Op { Kind = OpKind.TeleportAck, ReqId = 0 };
            op.SetPayload(5u);
            _requestLength = RequestEnvelope.Encode(Registry, in op, _request);
            Assert.Equal(RequestEnvelope.HeaderSize + 4, _requestLength);
            Assert.Equal(AdmitResult.Ok, Adapter.AdmitLocal(HostEnd.Peer, _slot0));
            Assert.True(Adapter.CompleteJoin(HostAdapter.JoinReqIdBase + 0, 0, new EntityId(DrainRig.PlayerA.Value)));
            Assert.Equal(AdmitResult.Ok, Adapter.Admit(PeerOrigin.Remote, _remoteA, _slot3, 141UL));
            Assert.True(Adapter.CompleteJoin(HostAdapter.JoinReqIdBase + 3, 0, new EntityId(DrainRig.PlayerB.Value)));
            Adapter.PumpInbound();
            World.Tick(); // drains the two refused System PlayerJoins (slots already held in the DrainRig): ring empty, no replies to peers
            Adapter.FlushOutbound(World.CurrentTick);
            Assert.Equal(0, World.Ops.Count);
        }

        public DrainRig Drain { get; }

        public SimWorld World => Drain.World;

        public OpCodecRegistry Registry { get; }

        public LoopbackTransport HostEnd { get; }

        public LoopbackTransport ClientEnd { get; }

        public QueueTransport Remote { get; } = new();

        public HostAdapter Adapter { get; }

        /// <summary>Applications per u16 reqId (the handler's side effect; the reply extra carries the running total).</summary>
        public int[] Applied { get; } = new int[ushort.MaxValue + 1]; // an array, not a Dictionary: the allocation test ticks

        public uint TotalApplied { get; private set; }

        public byte[] Request(ushort reqId)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_request.AsSpan(2), reqId);
            return _request.AsSpan(0, _requestLength).ToArray();
        }

        /// <summary>Allocation-free send of the cached request with a patched reqId (the allocation test).</summary>
        public void SendLoop(ushort reqId)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_request.AsSpan(2), reqId);
            ClientEnd.Send(ClientEnd.Peer, NetChannel.Reliable, new ReadOnlySpan<byte>(_request, 0, _requestLength));
        }

        public void LoopSends(params ushort[] reqIds)
        {
            foreach (ushort id in reqIds)
            {
                ClientEnd.Send(ClientEnd.Peer, NetChannel.Reliable, Request(id));
            }
        }

        public void RemoteSends(ushort reqId) => Remote.Queue(_remoteA, Request(reqId));

        /// <summary>Tick, then flush (frame order: the pump happens before, in the test).</summary>
        public void TickAndFlush()
        {
            World.Tick();
            Adapter.FlushOutbound(World.CurrentTick);
        }

        public List<byte[]> LoopMessages()
        {
            var sink = new CopySink();
            ClientEnd.Poll(sink);
            return sink.Messages;
        }

        public (int Cached, int InFlight) Stats()
        {
            Assert.True(Adapter.TryGetDedupeStats(PeerOrigin.Loopback, HostEnd.Peer, out int cached, out int inFlight));
            return (cached, inFlight);
        }

        private Reply Apply(in Op op, in TickContext ctx)
        {
            ushort id = unchecked((ushort)op.ReqId);
            Applied[id]++;
            TotalApplied++;
            Reply reply = Reply.Ok(in op);
            reply.SetExtra(0xA000_0000u | TotalApplied);
            return reply;
        }
    }

    /// <summary>The OpReply record bodies of one event batch, as the raw bytes the peer received.</summary>
    private static List<byte[]> ReplyBodies(byte[] batch)
    {
        Assert.Equal(EventBatch.Tag, batch[0]);
        Assert.True(EventBatch.TryReadHeader(batch, out _, out ushort count));
        var bodies = new List<byte[]>();
        int at = EventBatch.HeaderSize;
        for (int i = 0; i < count; i++)
        {
            var kind = (EventKind)(BinaryPrimitives.ReadUInt16LittleEndian(batch.AsSpan(at)) & EventBatch.MaxKind);
            at += EventBatch.RecordHeaderSize;
            Assert.Equal(EventKind.OpReply, kind);
            Assert.True(ReplyCodec.TryDecodeBody(batch.AsSpan(at), out _, out int used));
            bodies.Add(batch.AsSpan(at, used).ToArray());
            at += used;
        }

        Assert.Equal(batch.Length, at);
        return bodies;
    }

    private static Reply Bare(byte[] message)
    {
        Assert.Equal(ReplyCodec.Tag, message[0]);
        Assert.True(ReplyCodec.TryDecode(message, out Reply reply));
        return reply;
    }

    private static EventEntry ReplyEv(byte audience, uint reqId, ushort reason = 0, ReplyResult? result = null)
    {
        var r = new Reply { ReqId = reqId, Kind = OpKind.TeleportAck, Reason = reason, Result = result ?? (reason == 0 ? ReplyResult.Ok : ReplyResult.Rejected) };
        r.SetExtra(0x5151u);
        var e = new EventEntry { Kind = EventKind.OpReply, Class = ReplicationClass.OwnerOnly, Audience = audience, CallSeq = 1, PayloadLength = Reply.SizeBytes };
        Span<byte> p = e.Payload;
        MemoryMarshal.Write(p, in r);
        return e;
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

    // ------------------------------------------------------------ AC-1 cached duplicate

    [Fact]
    [Trait("Id", "AC-1")]
    public void Ac1_CachedDuplicate_GetsTheByteIdenticalOriginalReply_EnqueuesNothing_DumpUnchanged()
    {
        // Arrange - reqId 7 applied and its OpReply delivered in the tick batch
        var rig = new Rig();
        rig.LoopSends(7);
        rig.Adapter.PumpInbound();
        rig.TickAndFlush();
        byte[] originalBody = Assert.Single(ReplyBodies(Assert.Single(rig.LoopMessages())));
        Assert.Equal((1, 0), rig.Stats());
        uint opSeq = rig.World.LastOpSeq;
        long enqueued = rig.Adapter.EnqueuedCount;
        string hash = DumpHash.Sha256(rig.World);

        // Act - the same reqId again from the same peer
        rig.LoopSends(7);
        rig.Adapter.PumpInbound();

        // Assert - bare reply = tag + the original record body, byte for byte; nothing enqueued; Sim state untouched
        byte[] resend = Assert.Single(rig.LoopMessages());
        Assert.Equal(ReplyCodec.Tag, resend[0]);
        Assert.Equal(originalBody, resend.AsSpan(1).ToArray());
        Assert.Equal(7u, Bare(resend).ReqId);
        Assert.Equal(0xA000_0001u, Bare(resend).ExtraAs<uint>());
        Assert.Equal(0, rig.World.Ops.Count);
        Assert.Equal(opSeq, rig.World.LastOpSeq);
        Assert.Equal(enqueued, rig.Adapter.EnqueuedCount);
        Assert.Equal(hash, DumpHash.Sha256(rig.World));
        Assert.Equal(1L, rig.Adapter.DuplicateResendCount);
        rig.TickAndFlush();
        Assert.Empty(rig.LoopMessages());
        Assert.Equal(1, rig.Applied[7]);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Ac1_SameReqIdFromADifferentPeer_IsNew()
    {
        var rig = new Rig();
        rig.LoopSends(7);
        rig.Adapter.PumpInbound();
        rig.TickAndFlush();
        rig.LoopMessages();

        rig.RemoteSends(7);
        rig.Adapter.PumpInbound();

        Assert.Equal(1, rig.World.Ops.Count);
        Assert.Equal(0L, rig.Adapter.DuplicateResendCount);
        Assert.Empty(rig.Remote.Sent);
        rig.TickAndFlush();
        Assert.Equal(2, rig.Applied[7]);
        byte[] body = Assert.Single(ReplyBodies(Assert.Single(rig.Remote.Sent)));
        Assert.True(ReplyCodec.TryDecodeBody(body, out Reply reply, out _));
        Assert.Equal(7u, reply.ReqId);
        Assert.Equal(0xA000_0002u, reply.ExtraAs<uint>());
        Assert.Empty(rig.LoopMessages());
    }

    // ------------------------------------------------------------ AC-2 in flight, busy

    [Fact]
    [Trait("Id", "AC-2")]
    public void Ac2_InFlightDuplicate_IsDroppedSilently_AndTheOriginalReplyArrivesOnce()
    {
        var rig = new Rig();
        rig.LoopSends(7);
        rig.Adapter.PumpInbound();
        Assert.Equal((0, 1), rig.Stats());

        rig.LoopSends(7, 7);
        rig.Adapter.PumpInbound();

        Assert.Empty(rig.LoopMessages());
        Assert.Equal(1, rig.World.Ops.Count);
        Assert.Equal(1L, rig.Adapter.EnqueuedCount);
        Assert.Equal(2L, rig.Adapter.InFlightDuplicateDropCount);
        rig.TickAndFlush();
        byte[] body = Assert.Single(ReplyBodies(Assert.Single(rig.LoopMessages())));
        Assert.True(ReplyCodec.TryDecodeBody(body, out Reply reply, out _));
        Assert.Equal(7u, reply.ReqId);
        Assert.Equal(1, rig.Applied[7]);
        Assert.Equal((1, 0), rig.Stats());
    }

    [Fact]
    [Trait("Id", "AC-2")]
    [Trait("Id", "TR-sim-017")]
    public void Ac2_Busy_IsNotCached_ARetryAfterTheRingDrains_IsEnqueuedAndAppliedOnce()
    {
        // Arrange - capacity 16, reserve 8: the 9th player op is refused (8 requests fit).
        var rig = new Rig(opCapacity: 16);
        rig.LoopSends(1, 2, 3, 4, 5, 6, 7, 8, 9);
        rig.Adapter.PumpInbound();
        Reply busy = Bare(Assert.Single(rig.LoopMessages()));
        Assert.Equal(9u, busy.ReqId);
        Assert.Equal(CoreReason.Busy, busy.Reason);
        Assert.Equal((0, 8), rig.Stats());
        rig.TickAndFlush();
        Assert.Equal(8, ReplyBodies(Assert.Single(rig.LoopMessages())).Count);
        Assert.Equal((8, 0), rig.Stats());

        // Act - the busy request re-sent as a new attempt
        rig.LoopSends(9);
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Empty(rig.LoopMessages());
        Assert.Equal(1, rig.World.Ops.Count);
        Assert.Equal(0L, rig.Adapter.DuplicateResendCount);
        rig.TickAndFlush();
        byte[] body = Assert.Single(ReplyBodies(Assert.Single(rig.LoopMessages())));
        Assert.True(ReplyCodec.TryDecodeBody(body, out Reply reply, out _));
        Assert.Equal(9u, reply.ReqId);
        Assert.True(reply.IsOk);
        Assert.Equal(1, rig.Applied[9]);
        Assert.Equal(1L, rig.Adapter.BusyReplyCount);
    }

    // ------------------------------------------------------------ AC-3 window, wrap, reset

    [Fact]
    [Trait("Id", "AC-3")]
    public void Ac3_SeventeenReplies_EvictReqId1_AndKeep2To17()
    {
        var rig = new Rig();
        for (ushort id = 1; id <= 17; id++)
        {
            rig.LoopSends(id);
        }

        rig.Adapter.PumpInbound();
        rig.TickAndFlush();
        Assert.Equal(17, ReplyBodies(Assert.Single(rig.LoopMessages())).Count);
        Assert.Equal((ReplyCache.Capacity, 0), rig.Stats());

        // reqId 1 is out of the window: a new attempt.
        rig.LoopSends(1);
        rig.Adapter.PumpInbound();
        Assert.Empty(rig.LoopMessages());
        Assert.Equal(1, rig.World.Ops.Count);

        // 2..17 are each answered with their own original reply.
        for (ushort id = 2; id <= 17; id++)
        {
            rig.LoopSends(id);
        }

        rig.Adapter.PumpInbound();
        List<byte[]> resent = rig.LoopMessages();
        Assert.Equal(16, resent.Count);
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal((uint)(i + 2), Bare(resent[i]).ReqId);
        }

        Assert.Equal(1, rig.World.Ops.Count);
        Assert.Equal(16L, rig.Adapter.DuplicateResendCount);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Ac3_ReqIdWraps_65535To0_WithoutBreakingTheCache()
    {
        var rig = new Rig();

        // A fresh cache's zeroed slots must not answer reqId 0.
        rig.LoopSends(65534, 65535, 0, 1);
        rig.Adapter.PumpInbound();
        Assert.Equal(4, rig.World.Ops.Count);
        rig.TickAndFlush();
        Assert.Equal(4, ReplyBodies(Assert.Single(rig.LoopMessages())).Count);

        rig.LoopSends(65535, 0, 2);
        rig.Adapter.PumpInbound();

        List<byte[]> resent = rig.LoopMessages();
        Assert.Equal(2, resent.Count);
        Assert.Equal(65535u, Bare(resent[0]).ReqId);
        Assert.Equal(0u, Bare(resent[1]).ReqId);
        Assert.Equal(1, rig.World.Ops.Count); // reqId 2 is new
        Assert.Equal(1, rig.Applied[0]);
        Assert.Equal(1, rig.Applied[65535]);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Ac3_ResetConnection_ClearsThatPeersCacheAndInFlightSet_Only()
    {
        var rig = new Rig();
        rig.LoopSends(1);
        rig.RemoteSends(1);
        rig.Adapter.PumpInbound();
        rig.TickAndFlush();
        rig.LoopMessages();
        rig.Remote.Sent.Clear();
        rig.LoopSends(2);
        rig.Adapter.PumpInbound();
        Assert.Equal((1, 1), rig.Stats());

        Assert.True(rig.Adapter.ResetConnection(PeerOrigin.Loopback, rig.HostEnd.Peer));
        Assert.False(rig.Adapter.ResetConnection(PeerOrigin.Remote, new PeerHandle(99)));

        Assert.Equal((0, 0), rig.Stats());
        Assert.True(rig.Adapter.TryGetDedupeStats(PeerOrigin.Remote, _remoteA, out int remoteCached, out _));
        Assert.Equal(1, remoteCached);
        rig.LoopSends(1, 2);
        rig.RemoteSends(1);
        rig.Adapter.PumpInbound();
        Assert.Equal(3, rig.World.Ops.Count); // the old in-flight 2, then new 1 and 2 from the reset peer
        Assert.Single(rig.Remote.Sent); // the remote peer's 1 is still cached
        Assert.Equal(1u, Bare(rig.Remote.Sent[0]).ReqId);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void InFlightSetFull_TheNextRequestIsBusy_NotEnqueuedNotCached_AndItsRetryAfterADrainIsAppliedOnce()
    {
        // Arrange - 64 distinct requests in flight, no tick between
        var rig = new Rig();
        ushort over = ReplyCache.InFlightCapacity + 1;
        for (ushort id = 1; id <= over; id++)
        {
            rig.LoopSends(id);
        }

        // Act
        rig.Adapter.PumpInbound();

        // Assert - exactly one busy, for the 65th
        Reply busy = Bare(Assert.Single(rig.LoopMessages()));
        Assert.Equal((uint)over, busy.ReqId);
        Assert.Equal(CoreReason.Busy, busy.Reason);
        Assert.Equal(1L, rig.Adapter.InFlightFullBusyCount);
        Assert.Equal(ReplyCache.InFlightCapacity, rig.World.Ops.Count);
        Assert.Equal((0, ReplyCache.InFlightCapacity), rig.Stats());
        rig.TickAndFlush();
        rig.LoopMessages();
        Assert.Equal(0, rig.Applied[over]);
        Assert.Equal((ReplyCache.Capacity, 0), rig.Stats());

        rig.LoopSends(over);
        rig.Adapter.PumpInbound();
        Assert.Empty(rig.LoopMessages());
        rig.TickAndFlush();
        Assert.True(ReplyCodec.TryDecodeBody(Assert.Single(ReplyBodies(Assert.Single(rig.LoopMessages()))), out Reply reply, out _));
        Assert.Equal((uint)over, reply.ReqId);
        Assert.True(reply.IsOk);
        Assert.Equal(1, rig.Applied[over]);
        Assert.Equal(1L, rig.Adapter.InFlightFullBusyCount);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void FreedAndReadmittedRow_StartsWithAnEmptyCache_AndTheSameReqIdIsNew()
    {
        // Arrange - the remote peer has reply 7 cached
        var rig = new Rig();
        rig.RemoteSends(7);
        rig.Adapter.PumpInbound();
        rig.TickAndFlush();
        Assert.True(rig.Adapter.TryGetDedupeStats(PeerOrigin.Remote, _remoteA, out int cached, out _));
        Assert.Equal(1, cached);

        // Act - remote disconnect frees the row; the same handle is admitted again
        rig.Remote.QueueDisconnect(_remoteA);
        rig.Adapter.PumpInbound();
        Assert.False(rig.Adapter.TryGetDedupeStats(PeerOrigin.Remote, _remoteA, out _, out _));
        Assert.Equal(AdmitResult.Ok, rig.Adapter.Admit(PeerOrigin.Remote, _remoteA, _slot3, 141UL));
        Assert.True(rig.Adapter.CompleteJoin(HostAdapter.JoinReqIdBase + 3, 0, new EntityId(DrainRig.PlayerB.Value)));

        // Assert
        Assert.True(rig.Adapter.TryGetDedupeStats(PeerOrigin.Remote, _remoteA, out cached, out int inFlight));
        Assert.Equal((0, 0), (cached, inFlight));
        rig.Remote.Sent.Clear();
        rig.RemoteSends(7);
        rig.Adapter.PumpInbound();
        Assert.Empty(rig.Remote.Sent);
        Assert.Equal(2, rig.World.Ops.Count); // the re-admission's System PlayerJoin and the new request 7
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void ResetConnection_AnOpEnqueuedBeforeTheReset_IsAnOrphan_AndTheNewRequestsOwnReplyIsCached()
    {
        // Arrange - request 7 is in the Sim's ring when the connection resets; the client reuses reqId 7
        var rig = new Rig();
        rig.LoopSends(7);
        rig.Adapter.PumpInbound();
        Assert.True(rig.Adapter.ResetConnection(PeerOrigin.Loopback, rig.HostEnd.Peer));
        rig.LoopSends(7);
        rig.Adapter.PumpInbound();
        Assert.Equal(2, rig.World.Ops.Count);

        // Act - both drain in one tick: the old op first
        rig.TickAndFlush();

        // Assert - both replies forwarded; only the new one (second application) is cached
        List<byte[]> bodies = ReplyBodies(Assert.Single(rig.LoopMessages()));
        Assert.Equal(2, bodies.Count);
        Assert.Equal(1L, rig.Adapter.OrphanReplyCount);
        Assert.Equal(0L, rig.Adapter.UntrackedReplyCount);
        Assert.Equal((1, 0), rig.Stats());
        rig.LoopSends(7);
        rig.Adapter.PumpInbound();
        byte[] resend = Assert.Single(rig.LoopMessages());
        Assert.Equal(0xA000_0002u, Bare(resend).ExtraAs<uint>());
        Assert.Equal(bodies[1], resend.AsSpan(1).ToArray());
        Assert.Equal(2, rig.Applied[7]);
    }

    // ------------------------------------------------------------ TD-065 item 2, join, bad_request, untracked

    [Fact]
    public void DroppedOpReply_OutboundFull_ReleasesInFlight_AndADuplicateGetsThatReplyNotASecondApplication()
    {
        var rig = new Rig();
        rig.LoopSends(7);
        rig.Adapter.PumpInbound();
        Assert.Equal((0, 1), rig.Stats());
        int capacity = rig.World.Settings.EventLogCapacity;
        var log = new EventEntry[capacity + 1];
        for (int i = 0; i < capacity; i++)
        {
            log[i] = ReplyEv(0b0000_1000, 60000); // fills the outbound buffer (remote peer; not in flight there)
        }

        log[capacity] = ReplyEv(1, 7);

        rig.Adapter.Deliver(rig.World.CurrentTick + 100, log, default);

        Assert.Equal(1L, rig.Adapter.OutboundFullDropCount);
        Assert.Equal(1L, rig.Adapter.DroppedReplyReleaseCount);
        Assert.Equal((1, 0), rig.Stats());
        rig.Adapter.FlushOutbound(rig.World.CurrentTick + 100);
        Assert.Empty(rig.LoopMessages());
        rig.LoopSends(7);
        rig.Adapter.PumpInbound();
        Reply resent = Bare(Assert.Single(rig.LoopMessages()));
        Assert.Equal(7u, resent.ReqId);
        Assert.Equal(0x5151u, resent.ExtraAs<uint>());
        Assert.Equal(1, rig.World.Ops.Count); // only the original op 7 (never ticked here)
    }

    [Fact]
    public void UnencodableOpReply_ReleasesInFlightWithoutCaching_SoARetryIsNew()
    {
        var rig = new Rig();
        rig.LoopSends(9);
        rig.Adapter.PumpInbound();
        uint tick = rig.World.CurrentTick + 100;

        EventEntry[] log = { ReplyEv(1, 9, reason: 0, result: ReplyResult.Rejected) };

        // A handler bug: asserts under SIM_ASSERT (thrown after the release and the count), counted otherwise.
        ExpectAssertThenRelease(NetAssertCode.ReplyCacheEncodeFailed, () => rig.Adapter.Deliver(tick, log, default));
        rig.Adapter.FlushOutbound(tick);

        Assert.Equal(1L, rig.Adapter.ReplyCacheEncodeFailureCount);
        Assert.Equal(1L, rig.Adapter.ReplyEncodeFailureCount); // the batch path refused it too (already released: not counted twice)
        Assert.Equal(0L, rig.Adapter.DroppedReplyReleaseCount);
        Assert.Equal((0, 0), rig.Stats());
        Assert.Empty(rig.LoopMessages());
        rig.LoopSends(9);
        rig.Adapter.PumpInbound();
        Assert.Empty(rig.LoopMessages());
        Assert.Equal(2, rig.World.Ops.Count);
    }

    [Fact]
    public void JoinReplies_NeverReachTheCache_EvenWhenTheirLow16BitsMatchAnInFlightReqId()
    {
        var rig = new Rig();
        rig.LoopSends(7);
        rig.Adapter.PumpInbound();
        uint tick = rig.World.CurrentTick + 100;
        long unmatchedBefore = rig.Adapter.UnmatchedJoinReplyCount; // the rig's own refused joins

        rig.Adapter.Deliver(tick, new[] { ReplyEv(1, HostAdapter.JoinReqIdBase + 7) }, default);
        rig.Adapter.FlushOutbound(tick);

        Assert.Equal((0, 1), rig.Stats());
        Assert.Equal(unmatchedBefore + 1, rig.Adapter.UnmatchedJoinReplyCount);
        Assert.Equal(0L, rig.Adapter.UntrackedReplyCount);
        Assert.Empty(rig.LoopMessages());
    }

    [Fact]
    public void BadRequest_IsNotCached_NorInFlight_AndUntrackedRepliesAreSentButNotCached()
    {
        var rig = new Rig();
        rig.ClientEnd.Send(rig.ClientEnd.Peer, NetChannel.Reliable, new byte[] { 0xF0, 0x00, 0x05, 0x00 });
        rig.Adapter.PumpInbound();
        Assert.Equal(CoreReason.BadRequest, Bare(Assert.Single(rig.LoopMessages())).Reason);
        Assert.Equal((0, 0), rig.Stats());

        rig.LoopSends(5);
        rig.Adapter.PumpInbound();
        Assert.Empty(rig.LoopMessages());
        Assert.Equal(1, rig.World.Ops.Count);

        uint tick = rig.World.CurrentTick + 100;
        rig.Adapter.Deliver(tick, new[] { ReplyEv(1, 3) }, default);
        rig.Adapter.FlushOutbound(tick);
        Assert.Single(ReplyBodies(Assert.Single(rig.LoopMessages())));
        Assert.Equal(1L, rig.Adapter.UntrackedReplyCount);
        Assert.Equal((0, 1), rig.Stats());
    }

    [Fact]
    public void ReplyCache_RingAndInFlightSet_Unit()
    {
        var cache = new ReplyCache();
        for (int i = 0; i < ReplyCache.InFlightCapacity; i++)
        {
            Assert.True(cache.TryAddInFlight((ushort)(i * 1000)));
        }

        Assert.True(cache.InFlightFull);
        Assert.False(cache.TryAddInFlight(1));
        Assert.False(cache.TryAddInFlight(0));
        Assert.True(cache.RemoveInFlight(0));
        Assert.False(cache.RemoveInFlight(0));
        Assert.True(cache.IsInFlight(63000));
        Assert.Equal(-1, cache.FindCached(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.CommitStore(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.CommitStore(1, ReplyCache.SlotSize + 1));
        Assert.Equal(0, cache.ResetConnection()); // 63 in flight become orphans
        Assert.Equal(ReplyCache.InFlightCapacity - 1, cache.OrphanCount);
        Assert.Equal(0, cache.InFlightCount);
        for (int i = 0; i < ReplyCache.InFlightCapacity; i++)
        {
            Assert.True(cache.TryAddInFlight((ushort)(i * 1000)));
        }

        Assert.Equal(0, cache.ResetConnection()); // 63 + 64 = 127 orphans
        Assert.True(cache.TryAddInFlight(1));
        Assert.True(cache.TryAddInFlight(2));
        Assert.Equal(1, cache.ResetConnection()); // only one more fits (128)
        Assert.Equal(ReplyCache.OrphanCapacity, cache.OrphanCount);
        Assert.True(cache.TryConsumeOrphan(5000)); // present twice: consumes one
        Assert.True(cache.TryConsumeOrphan(5000));
        Assert.False(cache.TryConsumeOrphan(5000));
        Assert.True(cache.TryConsumeOrphan(1));
        Assert.False(cache.TryConsumeOrphan(2)); // was lost to the overflow
        Assert.True(cache.TryConsumeOrphan(0)); // 0 left in flight before the first reset, so it is an orphan once
        Assert.False(cache.TryConsumeOrphan(0));
        cache.Clear();
        Assert.Equal(0, cache.InFlightCount);
        Assert.Equal(0, cache.CachedCount);
        Assert.Equal(0, cache.OrphanCount);
    }

    // ------------------------------------------------------------ guardrail: 0 B per request

    [Fact]
    public void Dedupe_MissInFlightDropAndCacheHit_AllocateNothingAfterWarmUp()
    {
        // Each iteration: a new reqId (miss: decode, enqueue, in flight), its duplicate (dropped), a tick that replies and
        // caches it, then a third copy (cache hit: resend). reqIds advance and wrap through u16.
        var rig = new Rig();
        var sink = new CountingSink();
        ushort next = 100;

        int runs = AllocAssert.Zero("HostAdapter dedupe paths", () =>
        {
            for (int i = 0; i < 20; i++)
            {
                ushort id = next;
                next = unchecked((ushort)(next + 1));
                rig.SendLoop(id);
                rig.SendLoop(id);
                rig.Adapter.PumpInbound();
                rig.World.Tick();
                rig.Adapter.FlushOutbound(rig.World.CurrentTick);
                rig.SendLoop(id);
                rig.Adapter.PumpInbound();
                rig.ClientEnd.Poll(sink);
            }
        });

        Assert.Equal(20L * runs, rig.Adapter.EnqueuedCount);
        Assert.Equal(20L * runs, rig.Adapter.InFlightDuplicateDropCount);
        Assert.Equal(20L * runs, rig.Adapter.DuplicateResendCount);
        Assert.Equal(40L * runs, sink.Messages);
    }
}
