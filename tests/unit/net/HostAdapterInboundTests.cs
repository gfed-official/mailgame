using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Net;

/// <summary>
/// Covers <see cref="HostAdapter"/> inbound (story network-adapter-loopback/003): AC-1 = actor stamping and admission
/// (TR-sim-016, TR-inventory-025), AC-2 = busy and bad_request replies through the one reply codec (TR-sim-017,
/// TR-inventory-025), AC-3 = frame order and "callbacks only enqueue" (TR-sim-008). Worlds are real (frozen through the
/// builder, stub handlers, no ticks unless a test needs a drain); the remote transport is a scripted fake.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class HostAdapterInboundTests
{
    internal static readonly EntityId PlayerA = new(0x0100_0005u);
    internal static readonly EntityId PlayerB = new(0x0100_0009u);
    internal static readonly PlayerSlot Slot0 = PlayerSlot.FromValue(0);
    internal static readonly PlayerSlot Slot1 = PlayerSlot.FromValue(1);

    // ------------------------------------------------------------ fixtures

    /// <summary>A wire-reachable, player-issuable op whose payload names a player id (to prove the id is ignored).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ProbePayload
    {
        public uint PlayerId;
        public uint Value;
    }

    private sealed class ProbeCodec : IOpCodec
    {
        public ushort CodecId => 900;

        public byte WireSize => 8;

        public int SampleCount => 1;

        public bool TryDecode(ReadOnlySpan<byte> payload, ref OpPayload dst, out byte payloadLength)
        {
            payloadLength = 0;
            if (payload.Length != 8)
            {
                return false;
            }

            var value = new ProbePayload
            {
                PlayerId = BinaryPrimitives.ReadUInt32LittleEndian(payload),
                Value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(4)),
            };
            Span<byte> target = dst;
            MemoryMarshal.Write(target, in value);
            payloadLength = 8;
            return true;
        }

        public int Encode(in Op src, Span<byte> dst)
        {
            if (src.PayloadLength != 8 || dst.Length < 8)
            {
                return -1;
            }

            ProbePayload value = src.PayloadAs<ProbePayload>();
            BinaryPrimitives.WriteUInt32LittleEndian(dst, value.PlayerId);
            BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(4), value.Value);
            return 8;
        }

        public Op GetSample(int index)
        {
            var op = default(Op);
            op.SetPayload(new ProbePayload { PlayerId = 1, Value = 2 });
            return op;
        }
    }

    private sealed class OkHandler : IOpHandler
    {
        public OkHandler(OpKind kind) => Kind = kind;

        public OpKind Kind { get; }

        public Reply Apply(in Op op, in TickContext ctx) => Reply.Ok(in op);
    }

    internal static readonly OpDescriptor ProbeDescriptor =
        OpDescriptor.For<ProbePayload>(OpKind.TeleportAck, systemOnly: false, wireCodecId: 900, wireSize: 8);

    /// <summary>Allocation-free client-side sink.</summary>
    private sealed class CountingSink : ITransportSink
    {
        public long Messages;
        public long Bytes;

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
        }
    }

    /// <summary>Copies every message so a test can inspect it after Poll.</summary>
    private sealed class CopySink : ITransportSink
    {
        public List<(NetChannel Channel, byte[] Bytes)> Messages { get; } = new();

        public void OnConnected(PeerHandle peer)
        {
        }

        public void OnDisconnected(PeerHandle peer, DisconnectReason reason)
        {
        }

        public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload) =>
            Messages.Add((channel, payload.ToArray()));
    }

    /// <summary>A scripted second transport standing in for the remote one.</summary>
    private sealed class FakeTransport : ITransport
    {
        private readonly Queue<Item> _inbox = new();
        private ITransportSink? _sink;

        public List<(PeerHandle Peer, NetChannel Channel, byte[] Bytes)> Sent { get; } = new();

        public List<(PeerHandle Peer, DisconnectReason Reason)> Disconnects { get; } = new();

        /// <summary>Runs inside <see cref="Send"/>, after the bytes are recorded.</summary>
        public Action<PeerHandle>? OnSend { get; set; }

        /// <summary>Runs at the start of <see cref="Poll"/>.</summary>
        public Action? OnPoll { get; set; }

        /// <summary>When set, <see cref="Send"/> delivers these bytes to the sink immediately (a contract-violating transport).</summary>
        public byte[]? EagerBytes { get; set; }

        public void QueueConnect(PeerHandle peer) => _inbox.Enqueue(new Item(0, peer, NetChannel.Reliable, Array.Empty<byte>(), default));

        public void QueueMessage(PeerHandle peer, byte[] bytes, NetChannel channel = NetChannel.Reliable) =>
            _inbox.Enqueue(new Item(1, peer, channel, bytes, default));

        public void QueueDisconnect(PeerHandle peer, DisconnectReason reason) =>
            _inbox.Enqueue(new Item(2, peer, NetChannel.Reliable, Array.Empty<byte>(), reason));

        public void Poll(ITransportSink sink)
        {
            _sink = sink;
            OnPoll?.Invoke();
            int count = _inbox.Count; // like the loopback: what arrived during the poll waits for the next one
            for (int i = 0; i < count; i++)
            {
                Item item = _inbox.Dequeue();
                switch (item.Type)
                {
                    case 0:
                        sink.OnConnected(item.Peer);
                        break;
                    case 1:
                        sink.OnMessage(item.Peer, item.Channel, item.Bytes);
                        break;
                    default:
                        sink.OnDisconnected(item.Peer, item.Reason);
                        break;
                }
            }
        }

        public void Send(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> bytes)
        {
            Sent.Add((peer, channel, bytes.ToArray()));
            OnSend?.Invoke(peer);
            if (EagerBytes is not null)
            {
                _sink!.OnMessage(peer, NetChannel.Reliable, EagerBytes);
            }
        }

        public void Disconnect(PeerHandle peer, DisconnectReason reason) => Disconnects.Add((peer, reason));

        private readonly record struct Item(int Type, PeerHandle Peer, NetChannel Channel, byte[] Bytes, DisconnectReason Reason);
    }

    private sealed class Rig
    {
        public Rig(int capacity = 4096, int reserve = 64, bool withRemote = false, int maxPeers = 8)
        {
            IOpCodec[] core = SimCoreCodecs.Create();
            IOpCodec[] codecs = { core[0], core[1], new ProbeCodec() };
            var builder = new SimWorldBuilder(new SimCoreSettings { OpQueueCapacity = capacity, SystemOpReserve = reserve });
            builder.BindOp(CorePlayerOps.JoinDescriptor, new OkHandler(OpKind.PlayerJoin));
            builder.BindOp(CorePlayerOps.LeaveDescriptor, new OkHandler(OpKind.PlayerLeave));
            builder.BindOp(CodecTestKit.NoWireDescriptor, new OkHandler(OpKind.FreezePlayer));
            builder.BindOp(ProbeDescriptor, new OkHandler(OpKind.TeleportAck));
            builder.BindEventSink(Events);
            World = builder.Freeze();
            Registry = new OpCodecRegistry(World.OpDescriptors, codecs);
            (HostEnd, ClientEnd) = LoopbackTransport.CreatePair();
            Remote = withRemote ? new FakeTransport() : null;
            Roster = new InMemoryRoster();
            Adapter = new HostAdapter(World, Registry, HostEnd, Remote, new AdapterSettings { MaxPeers = maxPeers }, Roster);
        }

        public CaptureSink Events { get; } = new();

        public SimWorld World { get; }

        public OpCodecRegistry Registry { get; }

        public LoopbackTransport HostEnd { get; }

        public LoopbackTransport ClientEnd { get; }

        public FakeTransport? Remote { get; }

        public InMemoryRoster Roster { get; }

        public HostAdapter Adapter { get; }

        public PeerHandle LoopPeer => HostEnd.Peer;

        /// <summary>Local admission plus the join reply the Sim would deliver (Story 004's Deliver stands behind it).</summary>
        public void AdmitLoopback()
        {
            Assert.Equal(AdmitResult.Ok, Adapter.AdmitLocal(LoopPeer, Slot0));
            Assert.True(Adapter.CompleteJoin(HostAdapter.JoinReqIdBase + 0, 0, PlayerA));
        }

        public void AdmitRemote(PeerHandle peer)
        {
            Remote!.QueueConnect(peer);
            Adapter.PumpInbound();
            Assert.Equal(AdmitResult.Ok, Adapter.Admit(PeerOrigin.Remote, peer, Slot1, 77UL));
            Assert.True(Adapter.CompleteJoin(HostAdapter.JoinReqIdBase + 1, 0, PlayerB));
        }

        public byte[] Request(uint reqId, uint claimedPlayer = 0, uint value = 7)
        {
            var op = new Op { Kind = OpKind.TeleportAck, ReqId = reqId };
            op.SetPayload(new ProbePayload { PlayerId = claimedPlayer, Value = value });
            byte[] buffer = new byte[RequestEnvelope.MaxSize];
            int length = RequestEnvelope.Encode(Registry, in op, buffer);
            return buffer.AsSpan(0, length).ToArray();
        }

        public void ClientSends(byte[] bytes, NetChannel channel = NetChannel.Reliable) =>
            ClientEnd.Send(ClientEnd.Peer, channel, bytes);

        public Op QueuedOp(int index)
        {
            OpQueueView ops = World.Ops;
            return ops.PeekAt(index);
        }

        public List<Reply> ClientReplies()
        {
            var sink = new CopySink();
            ClientEnd.Poll(sink);
            var replies = new List<Reply>();
            foreach ((NetChannel channel, byte[] bytes) in sink.Messages)
            {
                Assert.Equal(NetChannel.Reliable, channel);
                Assert.True(ReplyCodec.TryDecode(bytes, out Reply reply));
                replies.Add(reply);
            }

            return replies;
        }
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

    // ------------------------------------------------------------ constructor, settings, roster

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var rig = new Rig();
        Assert.Throws<ArgumentNullException>(() => new HostAdapter(null!, rig.Registry, rig.HostEnd, null, AdapterSettings.Default, rig.Roster));
        Assert.Throws<ArgumentNullException>(() => new HostAdapter(rig.World, null!, rig.HostEnd, null, AdapterSettings.Default, rig.Roster));
        Assert.Throws<ArgumentNullException>(() => new HostAdapter(rig.World, rig.Registry, null!, null, AdapterSettings.Default, rig.Roster));
        Assert.Throws<ArgumentNullException>(() => new HostAdapter(rig.World, rig.Registry, rig.HostEnd, null, null!, rig.Roster));
        Assert.Throws<ArgumentNullException>(() => new HostAdapter(rig.World, rig.Registry, rig.HostEnd, null, AdapterSettings.Default, null!));
    }

    [Fact]
    public void Constructor_RemoteSameAsLoopback_Throws()
    {
        var rig = new Rig();
        Assert.Throws<ArgumentException>(() => new HostAdapter(rig.World, rig.Registry, rig.HostEnd, rig.HostEnd, AdapterSettings.Default, rig.Roster));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Constructor_MaxPeersOutsideOneToEight_Throws(int maxPeers)
    {
        var rig = new Rig();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HostAdapter(rig.World, rig.Registry, rig.HostEnd, null, new AdapterSettings { MaxPeers = maxPeers }, rig.Roster));
    }

    [Fact]
    public void AdapterSettings_Default_IsAnEightRowPeerTable()
    {
        Assert.Equal(8, AdapterSettings.Default.MaxPeers);
    }

    [Fact]
    public void InMemoryRoster_RecordLookupReplaceAndFull()
    {
        var roster = new InMemoryRoster(2);
        Assert.Equal(EntityId.None, roster.Lookup(1UL));
        Assert.True(roster.Record(1UL, PlayerA));
        Assert.True(roster.Record(2UL, PlayerB));
        Assert.False(roster.Record(3UL, PlayerA));
        Assert.True(roster.Record(1UL, PlayerB)); // replace, not a new row
        Assert.Equal(PlayerB, roster.Lookup(1UL));
        Assert.Equal(PlayerB, roster.Lookup(2UL));
        Assert.Equal(EntityId.None, roster.Lookup(3UL));
        Assert.Equal(2, roster.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryRoster(0));
    }

    // ------------------------------------------------------------ AC-1 actor stamping and admission

    [Fact]
    [Trait("Id", "AC-1")]
    public void AdmitLocal_EnqueuesSystemPlayerJoin_AndPeerIsJoiningUntilCompleteJoin()
    {
        // Arrange
        var rig = new Rig();

        // Act
        AdmitResult result = rig.Adapter.AdmitLocal(rig.LoopPeer, Slot0);

        // Assert
        Assert.Equal(AdmitResult.Ok, result);
        Assert.Equal(PeerState.Joining, rig.Adapter.GetPeerState(PeerOrigin.Loopback, rig.LoopPeer));
        Assert.Equal(1, rig.World.Ops.Count);
        Op join = rig.QueuedOp(0);
        Assert.Equal(OpKind.PlayerJoin, join.Kind);
        Assert.True(join.Actor.IsSystem);
        Assert.Equal(1u, join.OpSeq);
        Assert.Equal(HostAdapter.JoinReqIdBase, join.ReqId);
        PlayerJoinPayload payload = join.PayloadAs<PlayerJoinPayload>();
        Assert.Equal(0u, payload.PlayerId);
        Assert.Equal(0, payload.Slot);

        // Act - the join reply is delivered
        Assert.True(rig.Adapter.CompleteJoin(HostAdapter.JoinReqIdBase, 0, PlayerA));

        // Assert
        Assert.Equal(PeerState.Admitted, rig.Adapter.GetPeerState(PeerOrigin.Loopback, rig.LoopPeer));
        Assert.True(rig.Adapter.TryGetPeer(PeerOrigin.Loopback, rig.LoopPeer, out EntityId id, out PlayerSlot slot));
        Assert.Equal(PlayerA, id);
        Assert.Equal(Slot0, slot);
        Assert.Equal(PlayerA, rig.Roster.Lookup(HostAdapter.LocalAccountKey));
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void AdmitLocal_AccountKnownToRoster_JoinPayloadCarriesTheRosterPlayerId()
    {
        var rig = new Rig();
        rig.Roster.Record(42UL, PlayerB);

        AdmitResult result = rig.Adapter.AdmitLocal(rig.LoopPeer, Slot1, accountKey: 42UL);

        Assert.Equal(AdmitResult.Ok, result);
        PlayerJoinPayload payload = rig.QueuedOp(0).PayloadAs<PlayerJoinPayload>();
        Assert.Equal(PlayerB.Value, payload.PlayerId);
        Assert.Equal(1, payload.Slot);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Request_PayloadNamesAnotherPlayer_IsEnqueuedWithTheActorOfThePeer()
    {
        // Arrange
        var rig = new Rig();
        rig.AdmitLoopback();
        rig.ClientSends(rig.Request(reqId: 5, claimedPlayer: PlayerB.Value, value: 99));

        // Act
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Equal(2, rig.World.Ops.Count);
        Op op = rig.QueuedOp(1);
        Assert.Equal(OpKind.TeleportAck, op.Kind);
        Assert.Equal(ActorId.FromEntity(PlayerA), op.Actor);
        Assert.NotEqual(ActorId.FromEntity(PlayerB), op.Actor);
        Assert.Equal(5u, op.ReqId);
        Assert.Equal(2u, op.OpSeq);
        ProbePayload payload = op.PayloadAs<ProbePayload>();
        Assert.Equal(PlayerB.Value, payload.PlayerId); // the payload is carried through untouched, it just is not identity
        Assert.Equal(99u, payload.Value);
        Assert.Equal(1L, rig.Adapter.EnqueuedCount);
        Assert.Empty(rig.ClientReplies());
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Request_FromConnectedPeerNeverAdmitted_IsDroppedCountedAndNotAnswered()
    {
        // Arrange - the first pump delivers OnConnected: a row exists, in state Connected
        var rig = new Rig();
        rig.Adapter.PumpInbound();
        Assert.Equal(PeerState.Connected, rig.Adapter.GetPeerState(PeerOrigin.Loopback, rig.LoopPeer));
        rig.ClientSends(rig.Request(reqId: 5));

        // Act
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Equal(0, rig.World.Ops.Count);
        Assert.Equal(0u, rig.World.LastOpSeq);
        Assert.Equal(1L, rig.Adapter.UnadmittedDropCount);
        Assert.Empty(rig.ClientReplies());
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Request_FromPeerStillJoining_IsDroppedCountedAndNotAnswered()
    {
        // Arrange
        var rig = new Rig();
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(rig.LoopPeer, Slot0));
        rig.ClientSends(rig.Request(reqId: 5));

        // Act
        rig.Adapter.PumpInbound();

        // Assert - only the join op is queued
        Assert.Equal(1, rig.World.Ops.Count);
        Assert.Equal(1L, rig.Adapter.UnadmittedDropCount);
        Assert.Empty(rig.ClientReplies());
        Assert.Equal(0L, rig.Adapter.DuplicateConnectCount); // the loopback's first connect only confirms the row AdmitLocal made
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Request_FromUnknownPeerHandle_IsDroppedCountedAndNotAnswered()
    {
        // Arrange - a remote handle the transport never announced
        var rig = new Rig(withRemote: true);
        rig.Remote!.QueueMessage(new PeerHandle(99), rig.Request(reqId: 5));

        // Act
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Equal(0, rig.World.Ops.Count);
        Assert.Equal(1L, rig.Adapter.UnadmittedDropCount);
        Assert.Empty(rig.Remote.Sent);
        Assert.Equal(PeerState.None, rig.Adapter.GetPeerState(PeerOrigin.Remote, new PeerHandle(99)));
        Assert.False(rig.Adapter.TryGetPeer(PeerOrigin.Remote, new PeerHandle(99), out EntityId id, out PlayerSlot slot));
        Assert.True(id.IsNone);
        Assert.True(slot.IsNone);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void CompleteJoin_Refused_PeerFallsBackToConnectedAndRequestsStayDropped()
    {
        // Arrange
        var rig = new Rig();
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(rig.LoopPeer, Slot0));

        // Act
        bool matched = rig.Adapter.CompleteJoin(HostAdapter.JoinReqIdBase, CoreReason.SlotBusy, EntityId.None);
        rig.ClientSends(rig.Request(reqId: 5));
        rig.Adapter.PumpInbound();

        // Assert
        Assert.True(matched);
        Assert.Equal(PeerState.Connected, rig.Adapter.GetPeerState(PeerOrigin.Loopback, rig.LoopPeer));
        Assert.Equal(1L, rig.Adapter.JoinRefusedCount);
        Assert.Equal(1L, rig.Adapter.UnadmittedDropCount);
        Assert.Equal(EntityId.None, rig.Roster.Lookup(HostAdapter.LocalAccountKey));
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(rig.LoopPeer, Slot0)); // the slot is free again
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void CompleteJoin_OkReplyWithoutPlayerId_IsTreatedAsRefused_AndUnknownRequestIdMatchesNothing()
    {
        var rig = new Rig();
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(rig.LoopPeer, Slot0));

        Assert.False(rig.Adapter.CompleteJoin(HostAdapter.JoinReqIdBase + 5, 0, PlayerA));
        Assert.True(rig.Adapter.CompleteJoin(HostAdapter.JoinReqIdBase, 0, EntityId.None));

        Assert.Equal(PeerState.Connected, rig.Adapter.GetPeerState(PeerOrigin.Loopback, rig.LoopPeer));
        Assert.Equal(1L, rig.Adapter.JoinRefusedCount);
        Assert.False(rig.Adapter.CompleteJoin(HostAdapter.JoinReqIdBase, 0, PlayerA)); // no longer joining
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void AdmitLocal_RefusalCases_ReportTheReasonAndChangeNothing()
    {
        var rig = new Rig(capacity: 2, reserve: 1, maxPeers: 2);

        Assert.Equal(AdmitResult.SlotInvalid, rig.Adapter.AdmitLocal(rig.LoopPeer, PlayerSlot.None));
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(rig.LoopPeer, Slot0));
        Assert.Equal(AdmitResult.AlreadyAdmitted, rig.Adapter.AdmitLocal(rig.LoopPeer, Slot1));
        Assert.Equal(AdmitResult.SlotTaken, rig.Adapter.AdmitLocal(new PeerHandle(7), Slot0));
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(new PeerHandle(7), Slot1)); // second row, second join: the ring (2) is now full
        Assert.Equal(AdmitResult.TableFull, rig.Adapter.AdmitLocal(new PeerHandle(8), PlayerSlot.FromValue(2)));
        Assert.Equal(2, rig.World.Ops.Count);
        Assert.Equal(2, rig.Adapter.PeerCount);

        var full = new Rig(capacity: 2, reserve: 1, maxPeers: 4);
        Assert.Equal(AdmitResult.Ok, full.Adapter.AdmitLocal(new PeerHandle(1), Slot0));
        Assert.Equal(AdmitResult.Ok, full.Adapter.AdmitLocal(new PeerHandle(2), Slot1));
        Assert.Equal(AdmitResult.Refused, full.Adapter.AdmitLocal(new PeerHandle(3), PlayerSlot.FromValue(2)));
        Assert.Equal(PeerState.None, full.Adapter.GetPeerState(PeerOrigin.Loopback, new PeerHandle(3)));
        Assert.Equal(2, full.Adapter.PeerCount);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void AdmitLocal_RefusedForAPeerThatAlreadyHadARow_KeepsItConnected()
    {
        var rig = new Rig(capacity: 2, reserve: 1);
        rig.Adapter.PumpInbound(); // loopback connect: row exists, Connected
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(new PeerHandle(2), Slot1));
        Assert.Equal(AdmitResult.Ok, rig.Adapter.AdmitLocal(new PeerHandle(3), PlayerSlot.FromValue(2))); // ring full now

        Assert.Equal(AdmitResult.Refused, rig.Adapter.AdmitLocal(rig.LoopPeer, Slot0));

        Assert.Equal(PeerState.Connected, rig.Adapter.GetPeerState(PeerOrigin.Loopback, rig.LoopPeer));
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Admit_RemoteOriginWithoutARemoteTransport_Throws()
    {
        var rig = new Rig();
        Assert.Throws<InvalidOperationException>(() => rig.Adapter.Admit(PeerOrigin.Remote, new PeerHandle(1), Slot0, 1UL));
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void PeerTable_KeysByTransportAndHandle_SoEqualHandlesOnBothTransportsStaySeparate()
    {
        // Arrange - TD-016(1): the loopback peer is handle 1 and so is the remote one
        var rig = new Rig(withRemote: true);
        rig.AdmitLoopback();
        rig.Remote!.QueueConnect(new PeerHandle(1));
        rig.Remote.QueueMessage(new PeerHandle(1), rig.Request(reqId: 1, claimedPlayer: PlayerA.Value));
        rig.ClientSends(rig.Request(reqId: 2));

        // Act
        rig.Adapter.PumpInbound();

        // Assert - the remote handle 1 is only Connected so its request is dropped; the loopback handle 1 is Admitted
        Assert.Equal(PeerState.Connected, rig.Adapter.GetPeerState(PeerOrigin.Remote, new PeerHandle(1)));
        Assert.Equal(PeerState.Admitted, rig.Adapter.GetPeerState(PeerOrigin.Loopback, new PeerHandle(1)));
        Assert.Equal(1L, rig.Adapter.UnadmittedDropCount);
        Assert.Equal(2, rig.World.Ops.Count);
        Op op = rig.QueuedOp(1);
        Assert.Equal(2u, op.ReqId);
        Assert.Equal(ActorId.FromEntity(PlayerA), op.Actor);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void RemoteDisconnect_FreesTheRow_SoAReusedHandleDoesNotInheritIdentity()
    {
        // Arrange
        var rig = new Rig(withRemote: true);
        rig.AdmitRemote(new PeerHandle(3));
        Assert.Equal(PeerState.Admitted, rig.Adapter.GetPeerState(PeerOrigin.Remote, new PeerHandle(3)));
        rig.Remote!.QueueDisconnect(new PeerHandle(3), DisconnectReason.Timeout);
        rig.Remote.QueueConnect(new PeerHandle(3));
        rig.Remote.QueueMessage(new PeerHandle(3), rig.Request(reqId: 1));

        // Act
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Equal(1L, rig.Adapter.RemoteDisconnectCount);
        Assert.Equal(PeerState.Connected, rig.Adapter.GetPeerState(PeerOrigin.Remote, new PeerHandle(3)));
        Assert.Equal(1L, rig.Adapter.UnadmittedDropCount);
        Assert.Equal(1, rig.World.Ops.Count); // the remote peer's join only

        // Act - a disconnect for a peer with no row changes nothing
        rig.Remote.QueueDisconnect(new PeerHandle(40), DisconnectReason.Requested);
        rig.Adapter.PumpInbound();
        Assert.Equal(1L, rig.Adapter.RemoteDisconnectCount);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void LoopbackDisconnect_IsCountedAndIgnored_TheRowStaysAdmitted()
    {
        var rig = new Rig();
        rig.AdmitLoopback();

        rig.ClientEnd.Disconnect(rig.ClientEnd.Peer, DisconnectReason.Requested);
        rig.Adapter.PumpInbound();

        Assert.Equal(1L, rig.Adapter.LoopbackDisconnectCount);
        Assert.Equal(PeerState.Admitted, rig.Adapter.GetPeerState(PeerOrigin.Loopback, rig.LoopPeer));
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void RemoteConnect_WhenThePeerTableIsFull_DisconnectsThePeerAndCountsIt()
    {
        // Arrange - two rows: the loopback peer and remote 5; remote 6 does not fit
        var rig = new Rig(withRemote: true, maxPeers: 2);
        rig.AdmitLoopback();
        rig.Remote!.QueueConnect(new PeerHandle(5));
        rig.Remote.QueueConnect(new PeerHandle(6));

        // Act
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Equal(1L, rig.Adapter.PeerTableFullCount);
        Assert.Equal(new[] { (new PeerHandle(6), DisconnectReason.Kicked) }, rig.Remote.Disconnects.ToArray());
        Assert.Equal(2, rig.Adapter.PeerCount);
        Assert.Equal(PeerState.None, rig.Adapter.GetPeerState(PeerOrigin.Remote, new PeerHandle(6)));
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void Request_OnANonReliableChannel_IsDroppedAndCounted()
    {
        var rig = new Rig();
        rig.AdmitLoopback();
        rig.ClientSends(rig.Request(reqId: 5), NetChannel.Unreliable);

        rig.Adapter.PumpInbound();

        Assert.Equal(1, rig.World.Ops.Count); // the join only; the unreliable-channel request never reached the Sim
        Assert.Equal(1L, rig.Adapter.NonRequestChannelDropCount);
        Assert.Empty(rig.ClientReplies());
    }

    // ------------------------------------------------------------ AC-2 busy and bad_request

    [Fact]
    [Trait("Id", "AC-2")]
    public void Request_WhenTheRingIsFull_IsAnsweredBusyInTheSamePumpAndConsumesNoOpSeq()
    {
        // Arrange - capacity 8, reserve 4: players are refused at 4 queued ops; the join takes one, three requests fit
        var rig = new Rig(capacity: 8, reserve: 4);
        rig.AdmitLoopback();
        for (uint req = 1; req <= 4; req++)
        {
            rig.ClientSends(rig.Request(req));
        }

        // Act - one pump
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Equal(4, rig.World.Ops.Count);
        Assert.Equal(4u, rig.World.LastOpSeq);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal((uint)(i + 1), rig.QueuedOp(i).OpSeq);
        }

        Assert.Equal(3L, rig.Adapter.EnqueuedCount);
        Assert.Equal(1L, rig.Adapter.BusyReplyCount);
        Assert.Equal(1L, rig.World.Counters[CounterId.BusyPlayer]);
        List<Reply> replies = rig.ClientReplies();
        Reply busy = Assert.Single(replies);
        Assert.Equal(4u, busy.ReqId);
        Assert.Equal(OpKind.TeleportAck, busy.Kind);
        Assert.Equal(CoreReason.Busy, busy.Reason);
        Assert.Equal(ReplyResult.Rejected, busy.Result);
    }

    private static (byte[] Message, uint ReqId, OpKind Kind) Malformed(string name) => name switch
    {
        "unknown_kind" => (new byte[] { 0xF0, 0x00, 0x34, 0x12, 1, 2, 3, 4, 5, 6, 7, 8 }, 0x1234u, (OpKind)0x00F0),
        "no_wire_codec" => (new byte[] { 0x03, 0x00, 0x05, 0x00 }, 5u, OpKind.FreezePlayer),
        "short_payload" => (new byte[] { 0x04, 0x00, 0x42, 0x00, 1, 2, 3, 4, 5, 6, 7 }, 0x42u, OpKind.TeleportAck),
        "trailing_bytes" => (new byte[] { 0x04, 0x00, 0x42, 0x00, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 0x42u, OpKind.TeleportAck),
        "header_too_short" => (new byte[] { 0x04, 0x00, 0x42 }, 0u, OpKind.None),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [Trait("Id", "AC-2")]
    [InlineData("unknown_kind")]
    [InlineData("no_wire_codec")]
    [InlineData("short_payload")]
    [InlineData("trailing_bytes")]
    [InlineData("header_too_short")]
    public void Request_Malformed_IsAnsweredBadRequestWithTheHeaderReqIdAndNothingIsEnqueued(string name)
    {
        // Arrange
        (byte[] message, uint expectedReqId, OpKind expectedKind) = Malformed(name);
        var rig = new Rig();
        rig.AdmitLoopback();
        rig.ClientSends(message);

        // Act
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Equal(1, rig.World.Ops.Count); // only the join
        Assert.Equal(1u, rig.World.LastOpSeq);
        Reply reply = Assert.Single(rig.ClientReplies());
        Assert.Equal(CoreReason.BadRequest, reply.Reason);
        Assert.Equal(ReplyResult.Rejected, reply.Result);
        Assert.Equal(expectedReqId, reply.ReqId);
        Assert.Equal(expectedKind, reply.Kind);
        Assert.Equal(1L, rig.Adapter.BadRequestReplyCount);
        Assert.Equal(0L, rig.Adapter.EnqueuedCount);
    }

    [Theory]
    [Trait("Id", "AC-2")]
    [InlineData(true)]
    [InlineData(false)]
    public void Request_ForASystemOnlyKind_IsRefusedAsBadRequestBeforeTheSim(bool join)
    {
        // Arrange - PlayerJoin / PlayerLeave decode through the registry; a peer must not be able to enqueue them (TD-056 item 5)
        var rig = new Rig();
        rig.AdmitLoopback();
        Op op = join ? CodecTestKit.Join(0, 3, reqId: 9) : CodecTestKit.Leave(PlayerA.Value, reqId: 9);
        byte[] buffer = new byte[RequestEnvelope.MaxSize];
        int length = RequestEnvelope.Encode(rig.Registry, in op, buffer);
        rig.ClientSends(buffer.AsSpan(0, length).ToArray());

        // Act
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Equal(1, rig.World.Ops.Count);
        Assert.Equal(1u, rig.World.LastOpSeq);
        Reply reply = Assert.Single(rig.ClientReplies());
        Assert.Equal(CoreReason.BadRequest, reply.Reason);
        Assert.Equal(9u, reply.ReqId);
        Assert.Equal(op.Kind, reply.Kind);
        Assert.Equal(1L, rig.Adapter.SystemOnlyRefusedCount);
        Assert.Equal(1L, rig.Adapter.BadRequestReplyCount);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void AdapterReply_IsByteIdenticalToTheSharedReplyCodecEncodingOfTheEquivalentSimReply()
    {
        // Arrange
        var rig = new Rig(capacity: 8, reserve: 4);
        rig.AdmitLoopback();
        for (uint req = 1; req <= 4; req++)
        {
            rig.ClientSends(rig.Request(req));
        }

        rig.Adapter.PumpInbound();
        var sink = new CopySink();
        rig.ClientEnd.Poll(sink);

        // The Sim-side equivalent: Reply.Reject for the op that was refused, encoded by the same codec Story 004 uses.
        var refused = new Op { Kind = OpKind.TeleportAck, ReqId = 4 };
        Reply simReply = Reply.Reject(in refused, CoreReason.Busy);
        byte[] expected = new byte[ReplyCodec.MaxSize];
        int length = ReplyCodec.Encode(in simReply, expected);

        // Assert
        (NetChannel channel, byte[] actual) = Assert.Single(sink.Messages);
        Assert.Equal(NetChannel.Reliable, channel);
        Assert.Equal(expected.AsSpan(0, length).ToArray(), actual);
        Assert.Equal(new byte[] { ReplyCodec.Tag, 0x04, 0x00, 0x04, 0x00, 0x01, 0x00, 0x00 }, actual);
    }

    [Fact]
    [Trait("Id", "AC-1")]
    public void RemoteConnect_ForAPeerWhoseConnectWasAlreadySeen_IsCountedAsADuplicate()
    {
        var rig = new Rig(withRemote: true);
        rig.Remote!.QueueConnect(new PeerHandle(3));
        rig.Remote.QueueConnect(new PeerHandle(3));

        rig.Adapter.PumpInbound();

        Assert.Equal(1L, rig.Adapter.DuplicateConnectCount);
        Assert.Equal(PeerState.Connected, rig.Adapter.GetPeerState(PeerOrigin.Remote, new PeerHandle(3)));
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void BusyAndBadRequest_LeaveTheEventLogUntouched_OnlyEnqueuedOpsProduceEntries()
    {
        // Arrange - three accepted requests, one busy, one malformed (reqId 0x42), one SystemOnly (reqId 9)
        var rig = new Rig(capacity: 8, reserve: 4);
        rig.AdmitLoopback();
        for (uint req = 1; req <= 4; req++)
        {
            rig.ClientSends(rig.Request(req));
        }

        rig.ClientSends(new byte[] { 0x04, 0x00, 0x42, 0x00, 1 });
        Op join = CodecTestKit.Join(0, 3, reqId: 9);
        byte[] buffer = new byte[RequestEnvelope.MaxSize];
        rig.ClientSends(buffer.AsSpan(0, RequestEnvelope.Encode(rig.Registry, in join, buffer)).ToArray());
        rig.Adapter.PumpInbound();
        Assert.Equal(3, rig.ClientReplies().Count); // busy + two bad_request, all on the reply stream

        // Act
        rig.World.Tick();

        // Assert - only the join and requests 1..3 were applied, so only they have OpReply entries
        EventEntry[] log = rig.Events.Last;
        Assert.All(log, e => Assert.Equal(EventKind.OpReply, e.Kind));
        uint[] replied = log.Select(e => e.PayloadAs<Reply>().ReqId).ToArray();
        Assert.Equal(new[] { HostAdapter.JoinReqIdBase, 1u, 2u, 3u }, replied);
    }

    [Fact]
    [Trait("Id", "AC-2")]
    public void Request_MalformedFromARemotePeer_IsAnsweredBadRequestOnTheRemoteTransport()
    {
        // Arrange
        var rig = new Rig(withRemote: true);
        rig.AdmitLoopback();
        rig.AdmitRemote(new PeerHandle(2));
        int opsBefore = rig.World.Ops.Count;
        rig.Remote!.QueueMessage(new PeerHandle(2), new byte[] { 0x04, 0x00, 0x42, 0x00, 1, 2 });

        // Act
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Equal(opsBefore, rig.World.Ops.Count);
        (PeerHandle peer, NetChannel channel, byte[] bytes) = Assert.Single(rig.Remote.Sent);
        Assert.Equal(new PeerHandle(2), peer);
        Assert.Equal(NetChannel.Reliable, channel);
        Assert.True(ReplyCodec.TryDecode(bytes, out Reply reply));
        Assert.Equal(CoreReason.BadRequest, reply.Reason);
        Assert.Equal(0x42u, reply.ReqId);
        Assert.Equal(OpKind.TeleportAck, reply.Kind);
        Assert.Empty(rig.ClientReplies()); // nothing leaked onto the loopback stream
    }

    // ------------------------------------------------------------ AC-3 frame order and callbacks

    [Fact]
    [Trait("Id", "AC-3")]
    public void PumpInbound_RemoteBeforeLoopback_ARemoteRequestGetsTheLowerOpSeqEvenIfSentLater()
    {
        // Arrange - both peers are handle 1; the host's own request is sent first, the remote one second
        var rig = new Rig(withRemote: true);
        rig.AdmitLoopback();
        rig.AdmitRemote(new PeerHandle(1));
        rig.ClientSends(rig.Request(22));
        rig.Remote!.QueueMessage(new PeerHandle(1), rig.Request(11, claimedPlayer: PlayerA.Value));

        // Act
        rig.Adapter.PumpInbound();

        // Assert - joins are opSeq 1 and 2
        Assert.Equal(4, rig.World.Ops.Count);
        Op remote = rig.QueuedOp(2);
        Op loopback = rig.QueuedOp(3);
        Assert.Equal(11u, remote.ReqId);
        Assert.Equal(3u, remote.OpSeq);
        Assert.Equal(ActorId.FromEntity(PlayerB), remote.Actor);
        Assert.Equal(22u, loopback.ReqId);
        Assert.Equal(4u, loopback.OpSeq);
        Assert.Equal(ActorId.FromEntity(PlayerA), loopback.Actor);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void PumpInbound_RemoteBusy_IsAnsweredOnTheRemoteTransportToThatPeer()
    {
        var rig = new Rig(capacity: 4, reserve: 3, withRemote: true); // players are refused once 1 op is queued
        rig.AdmitLoopback();
        rig.AdmitRemote(new PeerHandle(4));
        rig.Remote!.QueueMessage(new PeerHandle(4), rig.Request(33));

        rig.Adapter.PumpInbound();

        (PeerHandle peer, NetChannel channel, byte[] bytes) = Assert.Single(rig.Remote.Sent);
        Assert.Equal(new PeerHandle(4), peer);
        Assert.Equal(NetChannel.Reliable, channel);
        Assert.True(ReplyCodec.TryDecode(bytes, out Reply reply));
        Assert.Equal(33u, reply.ReqId);
        Assert.Equal(CoreReason.Busy, reply.Reason);
        Assert.Empty(rig.ClientReplies());
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Transport_ThatDeliversDuringSend_HasItsMessageQueuedUntilTheNextPoll()
    {
        // Arrange - a malformed request makes the adapter Send a reply; the transport reacts by receiving a valid request
        // (a network callback arriving while Send runs). A compliant transport only queues it.
        var rig = new Rig(withRemote: true);
        rig.AdmitLoopback();
        rig.AdmitRemote(new PeerHandle(2));
        rig.Remote!.OnSend = peer => rig.Remote.QueueMessage(peer, rig.Request(50));
        rig.Remote.QueueMessage(new PeerHandle(2), new byte[] { 0x04, 0x00, 0x01, 0x00 });
        int opsBefore = rig.World.Ops.Count;

        // Act
        rig.Adapter.PumpInbound();

        // Assert - nothing new was enqueued by the message that arrived during Send
        Assert.Equal(opsBefore, rig.World.Ops.Count);
        Assert.Equal(0L, rig.Adapter.EnqueuedCount);
        rig.Remote.OnSend = null;

        // Act - the next Poll delivers it
        rig.Adapter.PumpInbound();

        // Assert
        Assert.Equal(opsBefore + 1, rig.World.Ops.Count);
        Assert.Equal(50u, rig.QueuedOp(opsBefore).ReqId);
        Assert.Equal(1L, rig.Adapter.EnqueuedCount);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Transport_ThatCallsTheSinkFromInsideSend_IsGuarded_NothingIsEnqueuedFromTheNestedCallback()
    {
        // Arrange - a contract-violating transport: Send delivers a valid request straight into OnMessage
        var rig = new Rig(withRemote: true);
        rig.AdmitLoopback();
        rig.AdmitRemote(new PeerHandle(2));
        rig.Remote!.EagerBytes = rig.Request(60);
        rig.Remote.QueueMessage(new PeerHandle(2), new byte[] { 0x04, 0x00, 0x01, 0x00 });
        int opsBefore = rig.World.Ops.Count;

        // Act
        ExpectAssertThenRelease(NetAssertCode.MessageReentered, rig.Adapter.PumpInbound);

        // Assert
        Assert.Equal(opsBefore, rig.World.Ops.Count);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(1L, rig.Adapter.ReentrantDropCount);
        }

        // The adapter is usable afterwards: flags were reset.
        rig.Remote.EagerBytes = null;
        rig.Remote.QueueMessage(new PeerHandle(2), rig.Request(61));
        rig.Adapter.PumpInbound();
        Assert.Equal(opsBefore + 1, rig.World.Ops.Count);
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void Message_DeliveredOutsidePumpInbound_IsNeverEnqueued()
    {
        // Arrange - a callback that fires on its own, not from Poll
        var rig = new Rig(withRemote: true);
        rig.AdmitLoopback();
        rig.AdmitRemote(new PeerHandle(2));
        int opsBefore = rig.World.Ops.Count;
        ITransportSink sink = rig.Adapter.RemoteSink;
        byte[] request = rig.Request(70);

        // Act
        ExpectAssertThenRelease(NetAssertCode.InboundOutsidePump, () => sink.OnMessage(new PeerHandle(2), NetChannel.Reliable, request));

        // Assert
        Assert.Equal(opsBefore, rig.World.Ops.Count);
        if (!TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(1L, rig.Adapter.OutsidePumpDropCount);
        }
    }

    [Fact]
    [Trait("Id", "AC-3")]
    public void PumpInbound_ReenteredFromATransportCallback_IsRejected()
    {
        // Arrange
        var rig = new Rig(withRemote: true);
        rig.AdmitLoopback();
        int polls = 0;
        rig.Remote!.OnPoll = () =>
        {
            polls++;
            if (polls == 1)
            {
                ExpectAssertThenRelease(NetAssertCode.PumpReentered, rig.Adapter.PumpInbound);
            }
        };

        // Act
        rig.Adapter.PumpInbound();

        // Assert - the nested call did nothing, the outer one finished, and the next pump still works
        Assert.Equal(1, polls);
        rig.Adapter.PumpInbound();
        Assert.Equal(2, polls);
    }

    // ------------------------------------------------------------ guardrail: 0 B

    [Fact]
    [Trait("Id", "AC-3")]
    public void PumpInbound_WarmedUp_AllocatesNothingOnAnyInboundPath()
    {
        // Arrange - accepted, bad_request, SystemOnly-refused and non-reliable-channel messages on one rig, busy on another.
        // A big ring (4096) so five windows of 100 accepted requests never need a drain.
        var rig = new Rig(capacity: 4096, reserve: 64);
        rig.AdmitLoopback();
        var busy = new Rig(capacity: 2, reserve: 1);
        busy.AdmitLoopback();
        byte[] valid = rig.Request(1);
        byte[] garbage = { 0xF0, 0x00, 0x01, 0x00 };
        Op joinOp = CodecTestKit.Join(0, 3, reqId: 9);
        byte[] joinBuffer = new byte[RequestEnvelope.MaxSize];
        byte[] systemOnly = joinBuffer.AsSpan(0, RequestEnvelope.Encode(rig.Registry, in joinOp, joinBuffer)).ToArray();
        byte[] busyValid = busy.Request(2);
        var rigSink = new CountingSink();
        var busySink = new CountingSink();

        void Cycle()
        {
            // Story 005: the same reqId would be dropped as in flight (no Deliver reaches this rig's adapter), so each cycle
            // starts a fresh dedupe window; ResetConnection is itself on the measured path.
            rig.Adapter.ResetConnection(PeerOrigin.Loopback, rig.LoopPeer);
            rig.ClientEnd.Send(rig.ClientEnd.Peer, NetChannel.Reliable, valid);
            rig.ClientEnd.Send(rig.ClientEnd.Peer, NetChannel.Reliable, garbage);
            rig.ClientEnd.Send(rig.ClientEnd.Peer, NetChannel.Reliable, systemOnly);
            rig.ClientEnd.Send(rig.ClientEnd.Peer, NetChannel.Unreliable, valid);
            rig.Adapter.PumpInbound();
            rig.ClientEnd.Poll(rigSink);

            busy.ClientEnd.Send(busy.ClientEnd.Peer, NetChannel.Reliable, busyValid);
            busy.Adapter.PumpInbound();
            busy.ClientEnd.Poll(busySink);
        }

        // Act + Assert (0 B), measured with AllocAssert (warm-up runs the same delegate first)
        int runs = AllocAssert.Zero("HostAdapter.PumpInbound inbound paths", () =>
        {
            for (int i = 0; i < 100; i++)
            {
                Cycle();
            }
        });

        // Assert - the window did the work it claims to measure
        Assert.Equal(100L * runs, rig.Adapter.EnqueuedCount);
        Assert.Equal(200L * runs, rig.Adapter.BadRequestReplyCount);
        Assert.Equal(100L * runs, rig.Adapter.NonRequestChannelDropCount);
        Assert.Equal(100L * runs, busy.Adapter.BusyReplyCount);
        Assert.Equal(200L * runs, rigSink.Messages);
        Assert.Equal(100L * runs, busySink.Messages);
    }
}
