using System.Buffers.Binary;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

// Outbound half of the host adapter (network-adapter-loopback story 004): IEventSink.Deliver (copy-only, phase 13) and
// FlushOutbound (after Tick() returns) turning each peer's entries into ordered event batches (ADR-0008 section 7,
// ADR-0011 section 6).
//
// Buffers (TD-062 item 3): PeerEntry rows are reset with new/default, so nothing outbound lives in a row. A shared copy
// buffer holds each queued entry once (a Replicated entry for three peers is copied once); per-peer index queues, byte and
// batch counters are parallel arrays indexed by peer row, cleared on allocate, on admit and on free.
public sealed partial class HostAdapter
{
    private readonly EventCodecRegistry _events;
    private readonly OutboundBuffers _out;

    /// <summary>Queued entries whose kind has no event codec (never left; also covers kinds the registry could not resolve).</summary>
    public long NoCodecDropCount { get; private set; }

    /// <summary>Audience bits (one per entry and slot) that mapped to no admitted peer at <c>Deliver</c> time, dropped for that slot.</summary>
    public long UnmappedAudienceDropCount { get; private set; }

    /// <summary>
    /// Entries dropped because the shared outbound copy buffer was full (only if several ticks are delivered without a flush).
    /// This is a loss the client cannot see: M0 must feed it into the overflow resync (as if the entry's audience and kind
    /// were in the tick's <see cref="OverflowInfo"/> masks).
    /// </summary>
    public long OutboundFullDropCount { get; private set; }

    /// <summary>
    /// <c>Deliver</c> calls for a tick at or before the last delivered tick, ignored: the Sim delivers each tick once in
    /// increasing order, so a repeat is a duplicate and is never re-sent, whether or not its first delivery queued anything.
    /// The guard is keyed on delivered ticks, not flushed ones, on purpose: after a flush for a tick the world could not run
    /// (TD-057 item 1), that tick's first real <c>Deliver</c> is legitimate and must go out.
    /// </summary>
    public long StaleDeliverCount { get; private set; }

    /// <summary><c>FlushOutbound</c> calls repeating the previous call's tick (TD-057 item 1: the world could not tick); they send nothing new.</summary>
    public long RepeatedFlushTickCount { get; private set; }

    /// <summary>Records an event codec could not encode (skipped; the rest of the batch still goes). Reply failures also count in <see cref="ReplyEncodeFailureCount"/>.</summary>
    public long EventEncodeFailureCount { get; private set; }

    /// <summary>Extra batches sent because one peer's records for a tick did not fit <see cref="AdapterSettings.MaxBatchBytes"/>.</summary>
    public long BatchSplitCount { get; private set; }

    /// <summary>Batch messages sent to all peers.</summary>
    public long BatchesSent { get; private set; }

    /// <summary>Header-only batches (count 0) sent to admitted peers with no entries on a tick (TD-068 item 1); not counted in <see cref="BatchesSent"/>.</summary>
    public long HeartbeatsSent { get; private set; }

    /// <summary>Join replies (<c>ReqId</c> at or above <see cref="JoinReqIdBase"/>) that matched no joining peer.</summary>
    public long UnmatchedJoinReplyCount { get; private set; }

    /// <summary>Ticks delivered with <see cref="OverflowInfo.Overflow"/> set (stored for the M0 resync path, nothing else).</summary>
    public long OverflowTickCount { get; private set; }

    /// <summary>The overflow report of the last overflowing tick (M0 resyncs exactly its masks; stored only at M-1a).</summary>
    public OverflowInfo LastOverflow { get; private set; }

    /// <summary>The tick of <see cref="LastOverflow"/>.</summary>
    public uint LastOverflowTick { get; private set; }

    /// <summary>
    /// <see cref="IEventSink"/>, phase 13, once per tick. Copy-only (ADR-0008 section 7): (a) copies every non-<c>Internal</c>
    /// entry with a codec into the adapter's own buffer once; (b) resolves its audience mask to peer rows through the slot to
    /// row map built at the start of this call (so a queued entry belongs to the peer that held the slot now, never to the
    /// slot); (c) hands each <c>OpReply</c> queued for a peer to the reply-cache hook (Story 005); (d) the reqId tagging hook
    /// is reserved (not implemented; ADR-0009). Join replies complete the join and are never queued. No transport, Godot or Sim
    /// call; <paramref name="entries"/> is not retained.
    /// <para>
    /// The slot to row map is built before the entry loop, so a peer admitted by a join reply in this same call receives none
    /// of this tick's entries: by design, the joiner's view of its join tick comes from JoinState (M0). <see cref="CompleteJoin"/>
    /// runs here and calls <see cref="IRoster.Record"/>, which must therefore be in-memory and non-blocking.
    /// </para>
    /// <para>
    /// Lifetime: one adapter per session (per world). The duplicate-tick guard remembers the last delivered tick and there is
    /// no reset; a new session, or a world whose tick numbers restart, needs a new adapter.
    /// </para>
    /// </summary>
    /// <param name="tick">The tick that is ending.</param>
    /// <param name="entries">The tick's log in raise order, valid only during this call.</param>
    /// <param name="overflow">What the log dropped this tick (stored only).</param>
    public void Deliver(uint tick, ReadOnlySpan<EventEntry> entries, in OverflowInfo overflow)
    {
        if (overflow.Overflow)
        {
            OverflowTickCount++;
            LastOverflow = overflow;
            LastOverflowTick = tick;
        }

        if (_out.DeliveredAny && tick <= _out.LastDeliveredTick)
        {
            StaleDeliverCount++;
            return;
        }

        _out.DeliveredAny = true;
        _out.LastDeliveredTick = tick;

        MapSlotsToRows();
        for (int i = 0; i < entries.Length; i++)
        {
            ref readonly EventEntry entry = ref entries[i];
            if (entry.Kind == EventKind.OpReply && TryCompleteJoinFrom(in entry))
            {
                continue;
            }

            if (entry.Class != ReplicationClass.Internal && entry.Audience != 0)
            {
                QueueEntry(tick, in entry);
            }
        }
    }

    /// <summary>
    /// Frame hook, after each <c>Tick()</c> returns: encodes each peer's queued entries into one batch per tick in log order
    /// (<see cref="EventBatch"/>) and sends it on <see cref="NetChannel.Reliable"/>. Batches carrying entries are stamped with
    /// the tick their entries were delivered in, so a repeated tick (TD-057 item 1: the world could not tick) finds nothing
    /// queued and sends nothing; a tick is never sent twice.
    /// <para>
    /// Heartbeat (TD-068 item 1, network-adapter-loopback story 007): every <see cref="PeerState.Admitted"/> peer that had no
    /// entries on this tick gets a header-only batch (count 0, stamped <paramref name="tick"/>, 7 B) so its
    /// <c>IHostClock</c> estimate advances on quiet ticks (one batch per peer per tick). A repeated flush tick sends none.
    /// Heartbeats are counted in <see cref="HeartbeatsSent"/>, not in <see cref="BatchesSent"/> or the per-peer sent counters.
    /// </para>
    /// </summary>
    /// <param name="tick">The frame runner's tick before the step that just ran (the heartbeat's stamp; also used to count repeats).</param>
    public void FlushOutbound(uint tick)
    {
        bool repeated = _out.FlushCalled && tick == _out.LastFlushCallTick;
        if (repeated)
        {
            RepeatedFlushTickCount++;
        }

        _out.FlushCalled = true;
        _out.LastFlushCallTick = tick;
        for (int row = 0; row < _peers.Length; row++)
        {
            if (_out.QueueCounts[row] > 0)
            {
                FlushPeer(row);
            }
            else if (!repeated && _peers[row].State == PeerState.Admitted)
            {
                SendHeartbeat(row, tick);
            }
        }

        _out.EntryCount = 0;
    }

    private void SendHeartbeat(int row, uint tick)
    {
        byte[] buffer = _out.Batch;
        buffer[0] = EventBatch.Tag;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1), tick);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(5), 0);
        HeartbeatsSent++;
        ref readonly PeerEntry peer = ref _peers[row];
        TransportOf(peer.Origin).Send(peer.Peer, NetChannel.Reliable, new ReadOnlySpan<byte>(buffer, 0, EventBatch.HeaderSize));
    }

    /// <summary>Reads a peer's outbound accounting (byte accounting for the M0 pacing and high-water valve).</summary>
    /// <param name="origin">Which transport the handle belongs to.</param>
    /// <param name="peer">The peer handle.</param>
    /// <param name="queuedEntries">Entries queued for the next flush.</param>
    /// <param name="queuedBytes">Upper bound of their wire bytes (record header plus codec maximum each).</param>
    /// <param name="sentBytes">Batch bytes sent since the row was admitted.</param>
    /// <param name="sentBatches">Batches sent since the row was admitted.</param>
    /// <returns>False (and zeros) when the peer has no row.</returns>
    public bool TryGetOutbound(PeerOrigin origin, PeerHandle peer, out int queuedEntries, out long queuedBytes, out long sentBytes, out long sentBatches)
    {
        int row = Find(origin, peer);
        bool found = row >= 0;
        queuedEntries = found ? _out.QueueCounts[row] : 0;
        queuedBytes = found ? _out.QueuedBytes[row] : 0;
        sentBytes = found ? _out.SentBytes[row] : 0;
        sentBatches = found ? _out.SentBatches[row] : 0;
        return found;
    }

    private void ResetOutbound(int row)
    {
        _out.QueueCounts[row] = 0;
        _out.QueuedBytes[row] = 0;
        _out.SentBytes[row] = 0;
        _out.SentBatches[row] = 0;
        ResetDedupe(row); // Story 005: no cached replies, nothing in flight; still-queued ops become orphans (rejoin)
    }

    private void MapSlotsToRows()
    {
        int[] map = _out.SlotRows;
        Array.Fill(map, -1);
        for (int row = 0; row < _peers.Length; row++)
        {
            if (_peers[row].State == PeerState.Admitted && !_peers[row].Slot.IsNone)
            {
                map[_peers[row].Slot.Value] = row;
            }
        }
    }

    // A join reply (ReqId >= JoinReqIdBase, full u32: the wire id is u16, so it must never be encoded) completes the join and
    // is consumed. The player id is the Sim's (PlayerJoinHandler puts it in Extra[0..4)); a rejoin falls back to the id the
    // adapter put in the payload itself.
    private bool TryCompleteJoinFrom(in EventEntry entry)
    {
        Reply reply = entry.PayloadAs<Reply>();
        if (reply.ReqId < JoinReqIdBase)
        {
            return false;
        }

        uint fromSim = reply.ExtraAs<uint>();
        EntityId player = fromSim != 0 ? new EntityId(fromSim) : JoinPlayerIdFor(reply.ReqId);
        if (!CompleteJoin(reply.ReqId, reply.Reason, player))
        {
            UnmatchedJoinReplyCount++;
        }

        return true;
    }

    private EntityId JoinPlayerIdFor(uint joinReqId)
    {
        for (int i = 0; i < _peers.Length; i++)
        {
            if (_peers[i].State == PeerState.Joining && _peers[i].JoinReqId == joinReqId)
            {
                return _peers[i].JoinPlayerId;
            }
        }

        return EntityId.None;
    }

    private void QueueEntry(uint tick, in EventEntry entry)
    {
        IEventCodec? codec = _events.CodecFor(entry.Kind);
        if (codec is null)
        {
            NoCodecDropCount++;
            return;
        }

        int index = -1;
        for (int slot = 0; slot < PlayerSlot.Count; slot++)
        {
            if ((entry.Audience & (1 << slot)) == 0)
            {
                continue;
            }

            int row = _out.SlotRows[slot];
            if (row < 0)
            {
                UnmappedAudienceDropCount++;
                continue;
            }

            index = index >= 0 ? index : CopyEntry(tick, in entry);
            if (index < 0)
            {
                DropReplyForAudience(in entry, slot); // TD-065 item 2: release the lost reply's reqId
                return;
            }

            AppendToPeer(row, index, EventBatch.RecordHeaderSize + codec.MaxWireSize);
        }
    }

    private int CopyEntry(uint tick, in EventEntry entry)
    {
        int index = _out.EntryCount;
        if (index >= _out.Entries.Length)
        {
            OutboundFullDropCount++;
            return -1;
        }

        _out.Entries[index] = entry;
        _out.Ticks[index] = tick;
        _out.EntryCount = index + 1;
        return index;
    }

    private void AppendToPeer(int row, int index, int maxBytes)
    {
        _out.Queues[row][_out.QueueCounts[row]++] = index;
        _out.QueuedBytes[row] += maxBytes;
        ref readonly EventEntry copy = ref _out.Entries[index];
        if (copy.Kind == EventKind.OpReply)
        {
            OnReplyQueued(row, in copy);
        }
    }

    // (c) OnReplyQueued (HostAdapter.Dedupe.cs, Story 005) moves the reply's u16 reqId from in flight to the row's reply cache.
    // (d) reqId tagging of same-CallSeq entries to the actor's peer (ADR-0009) will also attach there: the entry's CallSeq
    // equals its handler events' CallSeq (TD-048 item 1). Not implemented yet.

    private void FlushPeer(int row)
    {
        int[] queue = _out.Queues[row];
        int count = _out.QueueCounts[row];
        StartBatch(_out.Ticks[queue[0]]);
        for (int i = 0; i < count; i++)
        {
            int index = queue[i];
            uint entryTick = _out.Ticks[index];
            if (entryTick != _out.BatchTick)
            {
                SendBatch(row);
                StartBatch(entryTick);
            }

            AppendRecord(row, in _out.Entries[index]);
        }

        SendBatch(row);
        _out.QueueCounts[row] = 0;
        _out.QueuedBytes[row] = 0;
    }

    private void StartBatch(uint tick)
    {
        byte[] buffer = _out.Batch;
        buffer[0] = EventBatch.Tag;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(1), tick);
        _out.BatchTick = tick;
        _out.BatchLength = EventBatch.HeaderSize;
        _out.BatchCount = 0;
    }

    private void AppendRecord(int row, in EventEntry entry)
    {
        IEventCodec? codec = _events.CodecFor(entry.Kind);
        if (codec is null)
        {
            NoCodecDropCount++;
            return;
        }

        if (_out.BatchLength + EventBatch.RecordHeaderSize + codec.MaxWireSize > _out.Batch.Length || _out.BatchCount == ushort.MaxValue)
        {
            SendBatch(row);
            StartBatch(_out.BatchTick);
            BatchSplitCount++;
        }

        int written = codec.Encode(in entry, _out.Batch.AsSpan(_out.BatchLength + EventBatch.RecordHeaderSize));
        if (written < 0)
        {
            CountEncodeFailure(in entry);
            return;
        }

        ushort kindField = (ushort)((ushort)entry.Kind | ((entry.Flags & EventFlags.Continuation) != 0 ? EventBatch.ContinuationBit : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(_out.Batch.AsSpan(_out.BatchLength), kindField);
        _out.BatchLength += EventBatch.RecordHeaderSize + written;
        _out.BatchCount++;
    }

    private void CountEncodeFailure(in EventEntry entry)
    {
        EventEncodeFailureCount++;
        if (entry.Kind == EventKind.OpReply)
        {
            ReplyEncodeFailureCount++; // ReplyCodec refused the reply (Result contradicts Reason; TD-062 item 2)
            // TD-065 item 2: already released (uncached) at queue time by the same codec (HostAdapter.Dedupe.cs MoveToCache);
            // releasing again here could free a newer in-flight request that reuses the reqId.
        }
    }

    private void SendBatch(int row)
    {
        if (_out.BatchCount == 0)
        {
            return; // every record failed to encode: nothing goes
        }

        BinaryPrimitives.WriteUInt16LittleEndian(_out.Batch.AsSpan(5), _out.BatchCount);
        int length = _out.BatchLength;
        _out.SentBytes[row] += length;
        _out.SentBatches[row]++;
        BatchesSent++;
        ref readonly PeerEntry peer = ref _peers[row];
        TransportOf(peer.Origin).Send(peer.Peer, NetChannel.Reliable, new ReadOnlySpan<byte>(_out.Batch, 0, length));
        _out.BatchCount = 0;
        _out.BatchLength = EventBatch.HeaderSize;
    }

    /// <summary>The adapter's preallocated outbound state (one object so the constructor stays short).</summary>
    private sealed class OutboundBuffers
    {
        public OutboundBuffers(int maxPeers, int entryCapacity, int batchBytes)
        {
            int capacity = Math.Max(1, entryCapacity);
            Entries = new EventEntry[capacity];
            Ticks = new uint[capacity];
            Queues = new int[maxPeers][];
            for (int i = 0; i < maxPeers; i++)
            {
                Queues[i] = new int[capacity];
            }

            QueueCounts = new int[maxPeers];
            QueuedBytes = new long[maxPeers];
            SentBytes = new long[maxPeers];
            SentBatches = new long[maxPeers];
            Batch = new byte[batchBytes];
        }

        public EventEntry[] Entries { get; }

        public uint[] Ticks { get; }

        public int EntryCount { get; set; }

        public int[][] Queues { get; }

        public int[] QueueCounts { get; }

        public long[] QueuedBytes { get; }

        public long[] SentBytes { get; }

        public long[] SentBatches { get; }

        public int[] SlotRows { get; } = new int[PlayerSlot.Count];

        public byte[] Batch { get; }

        public int BatchLength { get; set; }

        public ushort BatchCount { get; set; }

        public uint BatchTick { get; set; }

        public bool DeliveredAny { get; set; }

        public uint LastDeliveredTick { get; set; }

        public bool FlushCalled { get; set; }

        public uint LastFlushCallTick { get; set; }
    }
}
