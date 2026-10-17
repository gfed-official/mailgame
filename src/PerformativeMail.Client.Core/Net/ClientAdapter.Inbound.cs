using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

// Inbound half of the client adapter (network-adapter-loopback story 006): transport sink, batch and reply decode into the
// IClientInbox, reply filtering (TD-066 item 1), clock updates.
public sealed partial class ClientAdapter
{
    private IClientInbox? _inbox;
    private bool _pumping;
    private uint _lastBatchTick;

    /// <summary>True from the transport's connect callback until its disconnect callback.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>The reason of the last disconnect (meaningful when <see cref="DisconnectCount"/> is above 0).</summary>
    public DisconnectReason LastDisconnectReason { get; private set; }

    /// <summary>Disconnect callbacks received (the loopback host ending the session). Pending requests are dropped.</summary>
    public long DisconnectCount { get; private set; }

    /// <summary>Event batches decoded into the inbox (a batch dropped for lack of inbox room is counted in <see cref="InboxFullDropCount"/> only).</summary>
    public long BatchesReceived { get; private set; }

    /// <summary>Event records decoded into the inbox (replies excluded).</summary>
    public long EntriesDelivered { get; private set; }

    /// <summary>Replies delivered to the inbox (one per awaited <c>reqId</c>).</summary>
    public long RepliesDelivered { get; private set; }

    /// <summary>
    /// Replies not delivered because their <c>reqId</c> is not awaited: an orphan (op enqueued before a host reset), the second
    /// copy of a reply the host sent twice (bare resend and batch record), or an answer to an already-answered request.
    /// </summary>
    public long IgnoredReplyCount { get; private set; }

    /// <summary>Replies for an awaited <c>reqId</c> whose kind differs from the request's, ignored (a stale reply for a reused id).</summary>
    public long ReplyKindMismatchCount { get; private set; }

    /// <summary>Inbound messages dropped as malformed (unknown tag, truncated or trailing bytes, unknown event kind, undecodable record).</summary>
    public long MalformedInboundCount { get; private set; }

    /// <summary>Inbound messages (or batch records) dropped because the inbox had no room. M0 must resync the mirror on this: a dropped batch's <c>OpReply</c> records are lost too, so those requests stay awaiting until a disconnect or the resync.</summary>
    public long InboxFullDropCount { get; private set; }

    /// <summary>Batches dropped because their tick was below the highest tick already received (cannot happen on an ordered stream).</summary>
    public long OutOfOrderBatchCount { get; private set; }

    /// <summary>Messages dropped because they arrived on a channel other than <see cref="NetChannel.Reliable"/> (join state on Bulk is M0).</summary>
    public long UnexpectedChannelCount { get; private set; }

    /// <summary>Messages dropped because they came from a handle other than the host.</summary>
    public long UnknownPeerCount { get; private set; }

    /// <summary>Messages delivered outside <see cref="PumpInbound"/>, dropped (TR-sim-008: callbacks only enqueue).</summary>
    public long OutsidePumpDropCount { get; private set; }

    /// <summary>
    /// Polls the transport and decodes everything that arrived into <paramref name="inbox"/>: batch records in order with their
    /// batch tick, replies as reply records, the clock updated per batch. Nothing is applied to the client mirrors here;
    /// <see cref="MirrorDrainStep"/> does that at the next client tick. Never touches a <c>SimWorld</c>; malformed bytes are
    /// dropped and counted, never thrown.
    /// </summary>
    /// <param name="inbox">The inbox to fill.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inbox"/> is null.</exception>
    public void PumpInbound(IClientInbox inbox)
    {
        ArgumentNullException.ThrowIfNull(inbox);
        if (_pumping)
        {
            SimAssert.Fail(NetAssertCode.ClientPumpReentered);
            return;
        }

        _pumping = true;
        _inbox = inbox;
        try
        {
            _transport.Poll(this);
        }
        finally
        {
            _pumping = false;
            _inbox = null;
        }
    }

    /// <inheritdoc />
    public void OnConnected(PeerHandle peer)
    {
        if (peer == _host)
        {
            IsConnected = true;
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(PeerHandle peer, DisconnectReason reason)
    {
        if (peer != _host)
        {
            return;
        }

        IsConnected = false;
        DisconnectCount++;
        LastDisconnectReason = reason;
        Array.Clear(_pending);
    }

    /// <inheritdoc />
    public void OnMessage(PeerHandle peer, NetChannel channel, ReadOnlySpan<byte> payload)
    {
        IClientInbox? inbox = _inbox;
        SimAssert.That(_pumping, NetAssertCode.ClientInboundOutsidePump);
        if (!_pumping || inbox is null)
        {
            OutsidePumpDropCount++;
            return;
        }

        if (peer != _host)
        {
            UnknownPeerCount++;
            return;
        }

        if (channel != NetChannel.Reliable)
        {
            UnexpectedChannelCount++;
            return;
        }

        if (payload.Length > 0 && payload[0] == ReplyCodec.Tag && ReplyCodec.TryDecode(payload, out Reply reply))
        {
            HandleReply(inbox, _lastBatchTick, in reply);
        }
        else if (payload.Length > 0 && payload[0] == EventBatch.Tag)
        {
            HandleBatch(inbox, payload);
        }
        else
        {
            MalformedInboundCount++;
        }
    }

    // A batch is atomic: pass 1 (ValidateBatch) proves every record decodes; the tick and inbox-room checks follow; only then
    // does pass 2 (DeliverBatch) deliver. A batch that fails any check delivers nothing.
    private void HandleBatch(IClientInbox inbox, ReadOnlySpan<byte> message)
    {
        if (!ValidateBatch(message, out uint tick, out ushort count))
        {
            return;
        }

        if (_clock.IsValid && tick < _lastBatchTick)
        {
            OutOfOrderBatchCount++;
            return;
        }

        _lastBatchTick = tick;
        _clock.Observe(tick);
        if (inbox.FreeCount < count)
        {
            // Dropped whole (not counted in BatchesReceived). Any OpReply records in it are lost with it: the request they
            // answer stays awaiting until a disconnect or the M0 resync.
            InboxFullDropCount++;
            return;
        }

        BatchesReceived++;
        DeliverBatch(inbox, message, tick);
    }

    // Pass 1: the header, every record's length and every record's decode. Counts a malformed batch.
    private bool ValidateBatch(ReadOnlySpan<byte> message, out uint tick, out ushort count)
    {
        tick = 0;
        count = 0;
        EventEntry scratch = default;
        bool valid = EventBatchReader.TryCreate(message, _events, out EventBatchReader reader);
        while (valid && reader.TryReadNext(out EventBatchRecord record))
        {
            valid = record.Codec.TryDecode(record.Payload, ref scratch, out _);
        }

        valid = valid && reader.IsComplete;
        MalformedInboundCount += valid ? 0 : 1;
        tick = valid ? reader.Tick : 0;
        count = valid ? reader.Count : (ushort)0;
        return valid;
    }

    // Pass 2: the batch already validated, so the re-read and every decode succeed (asserted).
    private void DeliverBatch(IClientInbox inbox, ReadOnlySpan<byte> message, uint tick)
    {
        bool created = EventBatchReader.TryCreate(message, _events, out EventBatchReader reader);
        SimAssert.That(created, NetAssertCode.ClientBatchRevalidateFailed);
        EventEntry scratch = default;
        uint ordinal = 0;
        while (created && reader.TryReadNext(out EventBatchRecord record))
        {
            scratch = default;
            scratch.Ordinal = ordinal++;
            scratch.Kind = record.Kind;
            scratch.Flags = record.IsContinuation ? EventFlags.Continuation : EventFlags.None;
            bool decoded = record.Codec.TryDecode(record.Payload, ref scratch, out _);
            SimAssert.That(decoded, NetAssertCode.ClientBatchRevalidateFailed);
            if (!decoded)
            {
                MalformedInboundCount++;
            }
            else if (record.Kind == EventKind.OpReply)
            {
                Reply reply = scratch.PayloadAs<Reply>();
                HandleReply(inbox, tick, in reply);
            }
            else
            {
                bool added = inbox.TryAddEntry(tick, in scratch);
                EntriesDelivered += added ? 1 : 0;
                InboxFullDropCount += added ? 0 : 1;
            }
        }
    }

    // TD-066 item 1: deliver only the first reply for an awaited reqId. The pending entry is consumed (or turned into a
    // resendable busy entry) on delivery, so a duplicate or orphan finds nothing awaiting and is ignored.
    private void HandleReply(IClientInbox inbox, uint tick, in Reply reply)
    {
        ushort reqId = unchecked((ushort)reply.ReqId);
        int slot = FindPending(reqId);
        if (slot < 0 || _pending[slot].State != PendingState.Awaiting)
        {
            IgnoredReplyCount++;
            return;
        }

        if (_pending[slot].Kind != reply.Kind)
        {
            ReplyKindMismatchCount++;
            return;
        }

        if (!inbox.TryAddReply(tick, in reply))
        {
            InboxFullDropCount++;
            return;
        }

        RepliesDelivered++;
        if (reply.Reason == CoreReason.Busy)
        {
            _pending[slot].State = PendingState.Busy;
        }
        else
        {
            _pending[slot] = default;
        }
    }
}
