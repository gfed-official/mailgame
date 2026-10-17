using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

// Inbound half of the host adapter: transport sinks, request decode, EnqueueOp, busy / bad_request replies.
public sealed partial class HostAdapter
{
    private void HandleConnected(PeerOrigin origin, PeerHandle peer)
    {
        int existing = Find(origin, peer);
        if (existing >= 0)
        {
            // The loopback transport reports its connect on the first Poll, normally after AdmitLocal made the row: that
            // first connect only confirms the row. A second connect for a confirmed row is a genuine duplicate.
            if (_peers[existing].ConnectSeen)
            {
                DuplicateConnectCount++;
            }

            _peers[existing].ConnectSeen = true;
            return;
        }

        if (Allocate(origin, peer, connectSeen: true) < 0)
        {
            PeerTableFullCount++;
            TransportOf(origin).Disconnect(peer, DisconnectReason.Kicked);
        }
    }

    private void HandleDisconnected(PeerOrigin origin, PeerHandle peer)
    {
        if (origin == PeerOrigin.Loopback)
        {
            // M-1a: the loopback disconnect ends the session. Keep the row, count it, do nothing else.
            LoopbackDisconnectCount++;
            return;
        }

        int index = Find(origin, peer);
        if (index >= 0)
        {
            // Free the row so a later peer that reuses the handle cannot inherit this peer's identity.
            // M0 additionally enqueues System FreezePlayer and drops the peer's queued batches. Known gap (M0 FreezePlayer):
            // if the peer disconnects while Joining, its System PlayerJoin is already queued or applied, so the Sim
            // gets an orphaned player with no peer, and a later CompleteJoin for it matches nothing.
            _peers[index] = default;
            ResetOutbound(index); // drops the peer's queued entries (ADR-0011 section 4)
            _dedupe[index].Clear(); // a freed row keeps no orphans
            RemoteDisconnectCount++;
        }
    }

    private void HandleMessage(PeerOrigin origin, PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload)
    {
        // TR-sim-008: callbacks only enqueue. Poll is the only legal caller, and only from PumpInbound.
        SimAssert.That(_pumping, NetAssertCode.InboundOutsidePump);
        if (!_pumping)
        {
            OutsidePumpDropCount++;
            return;
        }

        SimAssert.That(!_inMessage, NetAssertCode.MessageReentered);
        if (_inMessage)
        {
            ReentrantDropCount++;
            return;
        }

        _inMessage = true;
        try
        {
            ProcessRequest(origin, peer, channel, payload);
        }
        finally
        {
            _inMessage = false;
        }
    }

    private void ProcessRequest(PeerOrigin origin, PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload)
    {
        int index = Find(origin, peer);
        if (index < 0 || _peers[index].State != PeerState.Admitted)
        {
            UnadmittedDropCount++;
            return;
        }

        if (channel != NetChannel.Reliable)
        {
            NonRequestChannelDropCount++;
            return;
        }

        if (RequestEnvelope.TryDecode(_registry, payload, out Op op) != DecodeResult.Ok)
        {
            RejectMalformed(origin, peer, payload);
            return;
        }

        if (_registry.TryGet(op.Kind, out OpDescriptor? descriptor, out _) && descriptor.SystemOnly)
        {
            // TD-056 item 5: PlayerJoin / PlayerLeave decode through the registry but only the adapter may enqueue them.
            SystemOnlyRefusedCount++;
            SendRejection(origin, peer, op.ReqId, op.Kind, CoreReason.BadRequest);
            return;
        }

        // Story 005 dedupe, then TR-sim-016 actor stamping, EnqueueOp and busy (HostAdapter.Dedupe.cs).
        EnqueueFromPeer(origin, peer, index, ref op);
    }

    private void RejectMalformed(PeerOrigin origin, PeerHandle peer, ReadOnlySpan<byte> message)
    {
        // Echo what the header still tells us (kind u16, reqId u16); a message shorter than 4 bytes echoes zeros.
        uint reqId = 0;
        OpKind kind = OpKind.None;
        if (message.Length >= RequestEnvelope.HeaderSize)
        {
            kind = (OpKind)(ushort)(message[0] | (message[1] << 8));
            reqId = (uint)(message[2] | (message[3] << 8));
        }

        SendRejection(origin, peer, reqId, kind, CoreReason.BadRequest);
    }

    private void SendRejection(PeerOrigin origin, PeerHandle peer, uint reqId, OpKind kind, ushort reason)
    {
        if (reason == CoreReason.Busy)
        {
            BusyReplyCount++;
        }
        else
        {
            BadRequestReplyCount++;
        }

        var reply = new Reply { ReqId = reqId, Kind = kind, Reason = reason, Result = ReplyResult.Rejected };
        int length = ReplyCodec.Encode(in reply, _replyBuffer);
        if (length < 0)
        {
            ReplyEncodeFailureCount++;
            return;
        }

        // Immediate, uncached, on the peer's reliable reply stream (never the event log). A transport that delivers during
        // Send lands in HandleMessage's re-entrancy guard.
        TransportOf(origin).Send(peer, NetChannel.Reliable, new ReadOnlySpan<byte>(_replyBuffer, 0, length));
    }

    /// <summary>One per transport, so a callback knows which transport (and therefore which handle namespace) it came from.</summary>
    private sealed class TransportSink : ITransportSink
    {
        private readonly HostAdapter _owner;
        private readonly PeerOrigin _origin;

        public TransportSink(HostAdapter owner, PeerOrigin origin)
        {
            _owner = owner;
            _origin = origin;
        }

        public void OnConnected(PeerHandle peer) => _owner.HandleConnected(_origin, peer);

        public void OnDisconnected(PeerHandle peer, DisconnectReason reason) => _owner.HandleDisconnected(_origin, peer);

        public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload) =>
            _owner.HandleMessage(_origin, peer, channel, payload);
    }
}
