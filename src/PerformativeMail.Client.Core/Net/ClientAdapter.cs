using System.Buffers.Binary;
using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// The client end of the network adapter (ADR-0011 sections 2 and 3, ADR-0007 section 6; network-adapter-loopback story 006).
/// Used the same way by the host's own client (over <see cref="LoopbackTransport"/>) and by a remote client: no "is host" shortcut.
/// <list type="bullet">
/// <item><description><b>Requests</b> (TR-input-013): <see cref="TrySend"/>/<see cref="Send"/> wraps a payload in the 4-byte
/// <see cref="RequestEnvelope"/> (<c>kind u16 · reqId u16</c>) under a per-connection u16 wrapping <c>reqId</c> (first id 1, then
/// 65 535 wraps to 0) and sends it exactly once on <see cref="NetChannel.Reliable"/>. Nothing is ever retried at the application
/// level; the one exception is a request the host answered <c>busy</c>, which its owner may send again as a NEW attempt
/// (new <c>reqId</c>) with <see cref="TryResend"/>. A request carries no hold duration.</description></item>
/// <item><description><b>Inbound</b>: <see cref="PumpInbound"/> polls the transport; batches (<see cref="EventBatch"/>) and bare
/// replies (<see cref="ReplyCodec"/>) are decoded into an <see cref="IClientInbox"/> only. The adapter never touches a
/// <c>SimWorld</c>; the inbox is applied by <see cref="MirrorDrainStep"/> at the next client tick (TR-sim-014).</description></item>
/// <item><description><b>Replies</b> (TD-066 item 1): a reply is delivered only for a <c>reqId</c> the adapter awaits, exactly once.
/// An orphan reply (op enqueued before a host reset/rejoin), the second copy when the host sends both the cached bare resend and
/// the batch <c>OpReply</c>, or a reply whose kind disagrees with the request is ignored and counted (<see cref="IgnoredReplyCount"/>).</description></item>
/// <item><description><b>Clock</b>: <see cref="Clock"/> is the <see cref="IHostClock"/> estimator (<see cref="ClientHostClock"/>).</description></item>
/// </list>
/// Main thread only. All storage is allocated by the constructor; <see cref="PumpInbound"/>, <see cref="TrySend"/> and
/// <see cref="TryResend"/> allocate nothing afterwards. Decode never throws on malformed bytes: the message is dropped and counted.
/// </summary>
/// <example>
/// <code>
/// var (hostEnd, clientEnd) = LoopbackTransport.CreatePair();
/// var adapter = new ClientAdapter(clientEnd, clientEnd.Peer, eventCodecs);
/// ushort reqId = adapter.Send(OpKind.TeleportAck, payloadBytes);
/// adapter.PumpInbound(inbox);                  // decode into the inbox; MirrorDrainStep applies it next client tick
/// uint holdStart = adapter.Clock.EstimatedHostTick;
/// </code>
/// </example>
public sealed partial class ClientAdapter : ITransportSink
{
    private readonly ITransport _transport;
    private readonly PeerHandle _host;
    private readonly EventCodecRegistry _events;
    private readonly ClientAdapterSettings _settings;
    private readonly ClientHostClock _clock = new();
    private readonly byte[] _scratch = new byte[RequestEnvelope.HeaderSize + ClientAdapterSettings.MaxPayloadBytes];
    private readonly byte[] _resendBuffer = new byte[ClientAdapterSettings.MaxPayloadBytes];
    private readonly Pending[] _pending;
    private readonly byte[] _payloads;
    private ushort _nextReqId = 1;
    private long _sequence;
    private long _nowMs;
    private bool _timeKnown;

    /// <summary>Creates the adapter. Allocates the pending table and scratch buffers; nothing allocates afterwards.</summary>
    /// <param name="transport">This client's transport end (loopback: the client end of the pair).</param>
    /// <param name="host">The handle under which the transport sees the host (loopback: <c>transport.Peer</c>).</param>
    /// <param name="events">The session's event codec registry, used to decode batches.</param>
    /// <param name="settings">Tunables; <see cref="ClientAdapterSettings.Default"/> when null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="transport"/> or <paramref name="events"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A setting is out of range.</exception>
    public ClientAdapter(ITransport transport, PeerHandle host, EventCodecRegistry events, ClientAdapterSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(events);
        settings ??= ClientAdapterSettings.Default;
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.PendingQueueMax, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.PendingQueueMax, 64);
        ArgumentOutOfRangeException.ThrowIfNegative(settings.ReplyTimeoutMs);
        _transport = transport;
        _host = host;
        _events = events;
        _settings = settings;
        _pending = new Pending[settings.PendingQueueMax];
        _payloads = new byte[settings.PendingQueueMax * ClientAdapterSettings.MaxPayloadBytes];
    }

    /// <summary>The host tick estimator (ADR-0011 section 1). Hand it to the <c>ClientLayerBuilder</c> and to features that stamp host-domain ticks.</summary>
    public IHostClock Clock => _clock;

    /// <summary>Requests currently awaiting a reply or answered <c>busy</c> and still resendable.</summary>
    public int PendingCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _pending.Length; i++)
            {
                count += _pending[i].State != PendingState.Free ? 1 : 0;
            }

            return count;
        }
    }

    /// <summary>Requests sent (first attempts and resends).</summary>
    public long SentCount { get; private set; }

    /// <summary>Requests sent through <see cref="TryResend"/> (a subset of <see cref="SentCount"/>).</summary>
    public long ResendCount { get; private set; }

    /// <summary>Sends refused because the kind was <c>None</c> or the payload exceeded 48 bytes.</summary>
    public long InvalidSendCount { get; private set; }

    /// <summary>Sends refused because the pending table was full of requests still awaiting a reply.</summary>
    public long PendingFullRefusedCount { get; private set; }

    /// <summary>Answered-<c>busy</c> entries dropped (unresendable now) to make room for a new request.</summary>
    public long BusyEvictedCount { get; private set; }

    /// <summary><see cref="TryResend"/> calls refused: unknown <c>reqId</c>, or its reply was not <c>busy</c> (or it has no reply yet).</summary>
    public long ResendRejectedCount { get; private set; }

    /// <summary>Requests that went <see cref="ClientAdapterSettings.ReplyTimeoutMs"/> without a reply (counted once each; M0 resyncs, M-1a only counts).</summary>
    public long ReplyTimeoutCount { get; private set; }

    /// <summary>
    /// Sends a request. Returns the <c>reqId</c> used.
    /// </summary>
    /// <param name="kind">The op kind (not <c>None</c>).</param>
    /// <param name="payload">The kind's wire payload (0 to 48 bytes); the host validates its length against the kind.</param>
    /// <returns>The u16 <c>reqId</c> the request travels under; the reply echoes it.</returns>
    /// <exception cref="InvalidOperationException">The request was refused (see <see cref="TrySend"/>): invalid kind or payload, or the pending table is full.</exception>
    public ushort Send(OpKind kind, ReadOnlySpan<byte> payload)
    {
        if (!TrySend(kind, payload, out ushort reqId))
        {
            throw new InvalidOperationException("The request was refused (invalid kind or payload, or the pending table is full).");
        }

        return reqId;
    }

    /// <summary>
    /// Sends a request exactly once on the reliable channel and records it as pending until its reply arrives. If the table is
    /// full, the oldest request already answered <c>busy</c> is dropped to make room (it can no longer be resent); if every
    /// entry still awaits a reply the send is refused.
    /// </summary>
    /// <param name="kind">The op kind (not <c>None</c>).</param>
    /// <param name="payload">The kind's wire payload (0 to 48 bytes).</param>
    /// <param name="reqId">The <c>reqId</c> used; 0 when refused.</param>
    /// <returns>False (nothing sent, counted) for an invalid kind or payload, or a full table.</returns>
    public bool TrySend(OpKind kind, ReadOnlySpan<byte> payload, out ushort reqId)
    {
        reqId = 0;
        if (kind == OpKind.None || payload.Length > ClientAdapterSettings.MaxPayloadBytes)
        {
            InvalidSendCount++;
            return false;
        }

        int slot = FreeSlot();
        if (slot < 0)
        {
            PendingFullRefusedCount++;
            return false;
        }

        reqId = AllocateReqId();
        ref Pending entry = ref _pending[slot];
        entry.State = PendingState.Awaiting;
        entry.ReqId = reqId;
        entry.Kind = kind;
        entry.Length = (byte)payload.Length;
        entry.SentMs = _timeKnown ? _nowMs : UnstampedMs; // stamped by the first UpdateTime when the clock origin is not known yet
        entry.TimedOut = false;
        entry.Sequence = ++_sequence;
        payload.CopyTo(_payloads.AsSpan(slot * ClientAdapterSettings.MaxPayloadBytes));

        BinaryPrimitives.WriteUInt16LittleEndian(_scratch, (ushort)kind);
        BinaryPrimitives.WriteUInt16LittleEndian(_scratch.AsSpan(2), reqId);
        payload.CopyTo(_scratch.AsSpan(RequestEnvelope.HeaderSize));
        SentCount++;
        _transport.Send(_host, NetChannel.Reliable, new ReadOnlySpan<byte>(_scratch, 0, RequestEnvelope.HeaderSize + payload.Length));
        return true;
    }

    /// <summary>
    /// Sends again a request the host answered <c>busy</c>, as a new attempt under a NEW <c>reqId</c> (ADR-0011 section 3).
    /// Allowed only for a request whose delivered reply was <c>busy</c>; any other request waits for its own reply.
    /// </summary>
    /// <param name="reqId">The <c>reqId</c> of the busy-answered request.</param>
    /// <returns>The new attempt's <c>reqId</c>.</returns>
    /// <exception cref="InvalidOperationException">The resend was refused (see <see cref="TryResend"/>).</exception>
    public ushort Resend(ushort reqId)
    {
        if (!TryResend(reqId, out ushort newReqId))
        {
            throw new InvalidOperationException("Only a request answered busy can be resent.");
        }

        return newReqId;
    }

    /// <summary>
    /// Non-throwing <see cref="Resend"/>. The old <c>reqId</c> is retired (its entry is replaced by the new attempt).
    /// </summary>
    /// <param name="reqId">The <c>reqId</c> of the busy-answered request.</param>
    /// <param name="newReqId">The new attempt's <c>reqId</c>; 0 when refused.</param>
    /// <returns>False (nothing sent, counted in <see cref="ResendRejectedCount"/>) when no pending request has this <c>reqId</c> or its reply was not <c>busy</c>.</returns>
    public bool TryResend(ushort reqId, out ushort newReqId)
    {
        newReqId = 0;
        int slot = FindPending(reqId);
        if (slot < 0 || _pending[slot].State != PendingState.Busy)
        {
            ResendRejectedCount++;
            return false;
        }

        OpKind kind = _pending[slot].Kind;
        int length = _pending[slot].Length;
        _payloads.AsSpan(slot * ClientAdapterSettings.MaxPayloadBytes, length).CopyTo(_resendBuffer);
        _pending[slot] = default;
        if (!TrySend(kind, new ReadOnlySpan<byte>(_resendBuffer, 0, length), out newReqId))
        {
            return false; // cannot happen: the slot just freed is available
        }

        ResendCount++;
        return true;
    }

    /// <summary>Gives up on a busy-answered request (its owner will not resend it), freeing its pending slot.</summary>
    /// <param name="reqId">The <c>reqId</c> of the busy-answered request.</param>
    /// <returns>False when no busy-answered request has this <c>reqId</c>.</returns>
    public bool Forget(ushort reqId)
    {
        int slot = FindPending(reqId);
        if (slot < 0 || _pending[slot].State != PendingState.Busy)
        {
            return false;
        }

        _pending[slot] = default;
        return true;
    }

    /// <summary>
    /// Advances the adapter's notion of time (monotonic milliseconds from the frame driver; a smaller value is ignored). A request sent before the first call is stamped by that first call, so the clock origin is whatever the driver passes first and
    /// counts requests that have waited <see cref="ClientAdapterSettings.ReplyTimeoutMs"/> for a reply. A timed-out request stays
    /// pending (a late reply is still delivered once); resync is M0.
    /// </summary>
    /// <param name="nowMs">Monotonic milliseconds.</param>
    public void UpdateTime(long nowMs)
    {
        if (nowMs < 0 || (_timeKnown && nowMs <= _nowMs))
        {
            return;
        }

        _nowMs = nowMs;
        _timeKnown = true;
        for (int i = 0; i < _pending.Length; i++)
        {
            ref Pending entry = ref _pending[i];
            if (entry.State != PendingState.Awaiting)
            {
                continue;
            }

            if (entry.SentMs == UnstampedMs)
            {
                entry.SentMs = nowMs; // sent before the clock origin was known: the wait starts at the first update
            }

            if (!entry.TimedOut && nowMs - entry.SentMs >= _settings.ReplyTimeoutMs)
            {
                entry.TimedOut = true;
                ReplyTimeoutCount++;
            }
        }
    }

    private ushort AllocateReqId()
    {
        // The id counter wraps 65 535 -> 0 -> 1 (0 is a legal id: the Sim treats reqId as opaque). An id still pending (only
        // possible after 65 536 requests while one waits) is skipped so two live requests never share an id.
        for (int attempt = 0; attempt <= ushort.MaxValue; attempt++)
        {
            ushort candidate = _nextReqId;
            _nextReqId = unchecked((ushort)(candidate + 1));
            if (FindPending(candidate) < 0)
            {
                return candidate;
            }
        }

        return _nextReqId; // unreachable: at most PendingQueueMax ids are live
    }

    private int FreeSlot()
    {
        int oldestBusy = -1;
        for (int i = 0; i < _pending.Length; i++)
        {
            if (_pending[i].State == PendingState.Free)
            {
                return i;
            }

            if (_pending[i].State == PendingState.Busy && (oldestBusy < 0 || _pending[i].Sequence < _pending[oldestBusy].Sequence))
            {
                oldestBusy = i;
            }
        }

        if (oldestBusy >= 0)
        {
            BusyEvictedCount++;
            _pending[oldestBusy] = default;
        }

        return oldestBusy;
    }

    private int FindPending(ushort reqId)
    {
        for (int i = 0; i < _pending.Length; i++)
        {
            if (_pending[i].State != PendingState.Free && _pending[i].ReqId == reqId)
            {
                return i;
            }
        }

        return -1;
    }

    private const long UnstampedMs = -1;

    private enum PendingState : byte
    {
        Free = 0,
        Awaiting = 1,
        Busy = 2,
    }

    private struct Pending
    {
        public PendingState State;
        public bool TimedOut;
        public ushort ReqId;
        public OpKind Kind;
        public byte Length;
        public long SentMs;
        public long Sequence;
    }
}
