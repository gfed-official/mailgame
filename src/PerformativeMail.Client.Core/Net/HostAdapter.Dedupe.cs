using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

// Request deduplication (network-adapter-loopback story 005; ADR-0007 section 6, ADR-0011 section 3, TR-delivery-011,
// TR-sim-017). Keyed (peer row, u16 reqId); the state lives in one preallocated ReplyCache per row (never in PeerEntry, whose
// rows are reset by assignment) and is cleared with the row's outbound state on allocate, admit and free (ResetOutbound).
//
// Inbound (between decode and EnqueueOp): cached -> resend the original reply, enqueue nothing; in flight -> drop silently;
// otherwise EnqueueOp and add to in flight. EnqueueOp false -> busy, neither cached nor in flight (a retry is a new attempt).
// Deliver: an OpReply queued for a peer moves its reqId from in flight to the cache (body encoded by the registry's OpReply
// codec, the same bytes the batch record carries). Join replies (ReqId >= JoinReqIdBase) are consumed before this and never
// cached; System replies (audience 0) reach no row. busy / bad_request are sent by SendRejection and never cached.
public sealed partial class HostAdapter
{
    private readonly ReplyCache[] _dedupe;

    /// <summary>Duplicates of a replied request answered with the cached original reply (nothing enqueued).</summary>
    public long DuplicateResendCount { get; private set; }

    /// <summary>Duplicates of an enqueued, not yet replied request, dropped silently.</summary>
    public long InFlightDuplicateDropCount { get; private set; }

    /// <summary>Requests answered <c>busy</c> because the peer already had <see cref="ReplyCache.InFlightCapacity"/> unreplied requests.</summary>
    public long InFlightFullBusyCount { get; private set; }

    /// <summary><c>OpReply</c>s for a peer whose <c>reqId</c> was not in flight (for example a reply that outlived a <see cref="ResetConnection"/>); sent, not cached.</summary>
    public long UntrackedReplyCount { get; private set; }

    /// <summary>
    /// <c>OpReply</c>s that never reached the wire (outbound buffer full, or encode failure in the batch) whose <c>reqId</c> was
    /// released from in flight (TD-065 item 2). The reply is still cached when it encodes, so a duplicate gets it and the op is
    /// never applied twice.
    /// </summary>
    public long DroppedReplyReleaseCount { get; private set; }

    /// <summary>Replies to ops enqueued before a <see cref="ResetConnection"/> (orphans): forwarded to the peer, never cached.</summary>
    public long OrphanReplyCount { get; private set; }

    /// <summary>In-flight <c>reqId</c>s a <see cref="ResetConnection"/> could not keep as orphans (orphan list full).</summary>
    public long OrphanOverflowCount { get; private set; }

    /// <summary>In-flight <c>reqId</c>s released without caching because their <c>OpReply</c> could not be encoded.</summary>
    public long ReplyCacheEncodeFailureCount { get; private set; }

    /// <summary>
    /// Clears one connection's reply cache and in-flight set (rejoin, ADR-0011 section 4). Requests already enqueued become
    /// orphans: their replies are forwarded uncached and never taken for the reply of a new request with the same <c>reqId</c>.
    /// Admission does the same; allocating or freeing a row forgets everything. The host keeps no per-peer expected <c>reqId</c> counter: <c>reqId</c> is opaque to it (the client's counter
    /// is Story 006's).
    /// </summary>
    /// <param name="origin">Which transport the handle belongs to.</param>
    /// <param name="peer">The peer handle.</param>
    /// <returns>False when the peer has no row.</returns>
    public bool ResetConnection(PeerOrigin origin, PeerHandle peer)
    {
        int row = Find(origin, peer);
        if (row < 0)
        {
            return false;
        }

        ResetDedupe(row);
        return true;
    }

    /// <summary>Reads a peer's dedupe state (diagnostics and tests).</summary>
    /// <param name="origin">Which transport the handle belongs to.</param>
    /// <param name="peer">The peer handle.</param>
    /// <param name="cachedReplies">Replies cached (0 to 16).</param>
    /// <param name="inFlight">Enqueued requests awaiting their reply.</param>
    /// <returns>False (and zeros) when the peer has no row.</returns>
    public bool TryGetDedupeStats(PeerOrigin origin, PeerHandle peer, out int cachedReplies, out int inFlight)
    {
        int row = Find(origin, peer);
        cachedReplies = row >= 0 ? _dedupe[row].CachedCount : 0;
        inFlight = row >= 0 ? _dedupe[row].InFlightCount : 0;
        return row >= 0;
    }

    // Rejoin paths (ResetConnection, and ResetOutbound on admit): the cache goes, in-flight ids become orphans. Rows that are
    // allocated or freed additionally Clear() (orphans included).
    private void ResetDedupe(int row) => OrphanOverflowCount += _dedupe[row].ResetConnection();

    private static ReplyCache[] CreateReplyCaches(int maxPeers)
    {
        var caches = new ReplyCache[maxPeers];
        for (int i = 0; i < maxPeers; i++)
        {
            caches[i] = new ReplyCache();
        }

        return caches;
    }

    // The tail of ProcessRequest: dedupe, actor stamping (TR-sim-016: never from the payload), EnqueueOp, busy.
    private void EnqueueFromPeer(PeerOrigin origin, PeerHandle peer, int row, ref Op op)
    {
        ushort reqId = unchecked((ushort)op.ReqId); // the decoder zero-extends the u16 wire id
        ReplyCache cache = _dedupe[row];
        if (TryResendCached(origin, peer, cache, reqId))
        {
            return;
        }

        if (cache.IsInFlight(reqId))
        {
            InFlightDuplicateDropCount++;
            return;
        }

        if (cache.InFlightFull)
        {
            InFlightFullBusyCount++;
            SendRejection(origin, peer, op.ReqId, op.Kind, CoreReason.Busy);
            return;
        }

        ActorId actor = ActorId.FromEntity(_peers[row].PlayerId);
        op.Actor = actor;
        if (_sim.EnqueueOp(actor, in op))
        {
            EnqueuedCount++;
            bool added = cache.TryAddInFlight(reqId); // not in flight and not full: checked above
            SimAssert.That(added, NetAssertCode.InFlightAddFailed);
            return;
        }

        SendRejection(origin, peer, op.ReqId, op.Kind, CoreReason.Busy);
    }

    private bool TryResendCached(PeerOrigin origin, PeerHandle peer, ReplyCache cache, ushort reqId)
    {
        int slot = cache.FindCached(reqId);
        if (slot < 0)
        {
            return false;
        }

        ReadOnlySpan<byte> body = cache.CachedBody(slot);
        _replyBuffer[0] = ReplyCodec.Tag;
        body.CopyTo(_replyBuffer.AsSpan(1));
        DuplicateResendCount++;
        TransportOf(origin).Send(peer, NetChannel.Reliable, new ReadOnlySpan<byte>(_replyBuffer, 0, body.Length + 1));
        return true;
    }

    // Deliver hook (c): the OpReply was copied and queued for this row. Move its reqId from in flight to the cache. An orphan
    // reply (op enqueued before a ResetConnection) is still forwarded to the peer, uncached: the client ignores a reply it no
    // longer awaits, and the new request with the same reqId keeps its in-flight entry.
    private void OnReplyQueued(int row, in EventEntry reply)
    {
        if (MoveToCache(row, in reply) == ReplyDisposition.Untracked)
        {
            UntrackedReplyCount++;
        }
    }

    // TD-065 item 2: an OpReply that will not reach the peer still releases its reqId (else it would be dropped as in
    // flight forever). It is cached like a delivered one: the op was applied, so a duplicate must get this reply, not a
    // second application. A reqId no longer in flight (already moved at queue time) is left alone.
    private void OnReplyDropped(int row, in EventEntry reply)
    {
        if (MoveToCache(row, in reply) == ReplyDisposition.Released)
        {
            DroppedReplyReleaseCount++;
        }
    }

    // CopyEntry failed: the OpReply is lost for this slot's row and every later row in its audience.
    private void DropReplyForAudience(in EventEntry entry, int fromSlot)
    {
        if (entry.Kind != EventKind.OpReply)
        {
            return;
        }

        for (int slot = fromSlot; slot < PlayerSlot.Count; slot++)
        {
            int row = _out.SlotRows[slot];
            if ((entry.Audience & (1 << slot)) != 0 && row >= 0)
            {
                OnReplyDropped(row, in entry);
            }
        }
    }

    // Orphans first (an op enqueued before a reset drains before any newer op with the same reqId), then in flight. A released
    // reply is cached only when it encodes. This is the single release point for encode failures: the batch path (AppendRecord)
    // runs the same registry codec on the same entry with at least as much room, so it cannot fail where this succeeded.
    private ReplyDisposition MoveToCache(int row, in EventEntry entry)
    {
        uint wideId = entry.PayloadAs<Reply>().ReqId;
        ushort reqId = unchecked((ushort)wideId);
        ReplyCache cache = _dedupe[row];
        if (wideId >= JoinReqIdBase)
        {
            return ReplyDisposition.Untracked; // join replies never reach the cache (Deliver consumes them first; belt and braces)
        }

        if (cache.TryConsumeOrphan(reqId))
        {
            OrphanReplyCount++;
            return ReplyDisposition.Orphan;
        }

        if (!cache.RemoveInFlight(reqId))
        {
            return ReplyDisposition.Untracked;
        }

        IEventCodec? codec = _events.CodecFor(EventKind.OpReply);
        int written = codec is null ? -1 : codec.Encode(in entry, cache.StoreSlot());
        if (written <= 0)
        {
            ReplyCacheEncodeFailureCount++;
            SimAssert.Fail(NetAssertCode.ReplyCacheEncodeFailed); // released uncached: a retry is a new attempt
            return ReplyDisposition.Released;
        }

        cache.CommitStore(reqId, written);
        return ReplyDisposition.Released;
    }

    private enum ReplyDisposition
    {
        Untracked,
        Released,
        Orphan,
    }
}
