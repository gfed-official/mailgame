using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Net;

/// <summary>
/// The host side of the network adapter (ADR-0011 sections 2 to 4), inbound half (Story 003). It owns the peer table
/// <c>(origin, handle) to (accountKey, playerId, slot, state)</c>, decodes each admitted peer's reliable requests with the
/// codec registry and calls <see cref="SimWorld.EnqueueOp"/> on the main thread inside <see cref="PumpInbound"/> only.
/// <para>
/// Identity: <c>Op.Actor</c> is stamped from the peer table (TR-sim-016, TR-inventory-025); no payload field is ever read for
/// identity. Messages from unknown, merely connected or still-joining peers are dropped and counted: nothing is enqueued or
/// answered. A <c>SystemOnly</c> kind from a peer is refused as <c>bad_request</c> before it reaches the Sim (TD-056 item 5).
/// </para>
/// <para>
/// Replies: when <c>EnqueueOp</c> returns false the adapter answers <c>busy</c> (1); an undecodable message gets
/// <c>bad_request</c> (8). Both are built as a <see cref="Reply"/>, encoded by the shared <see cref="ReplyCodec"/> and sent at
/// once on <see cref="NetChannel.Reliable"/> to that peer, never cached, never an event-log entry, and they consume no
/// <c>opSeq</c>. Any false from <c>EnqueueOp</c> (ring full, player limit, world not accepting ops) is answered <c>busy</c>;
/// the Sim's own counters say which.
/// </para>
/// <para>
/// Frame order: <see cref="PumpInbound"/> polls the remote transport first, then the loopback one, so a remote request
/// beats a same-frame host request on <c>opSeq</c>. The transports' sinks are two small private objects (one per
/// transport) because a single sink could not tell which transport a callback came from, and handles are only unique per
/// transport (<c>LoopbackTransport.Peer</c> is always handle 1 on both ends; TD-016 item 1). Transport callbacks never
/// touch the Sim outside <see cref="PumpInbound"/>: a delivery outside it, or one nested inside another message callback
/// (a transport that delivers during <c>Send</c>), asserts in assert builds and is dropped and counted otherwise.
/// </para>
/// <para>
/// Admission at M-1a is local (<see cref="AdmitLocal"/>): it enqueues the <c>System</c> <c>PlayerJoin</c> and leaves the peer
/// <see cref="PeerState.Joining"/>. The peer becomes <see cref="PeerState.Admitted"/> when the join reply is delivered, which
/// the Sim does through <c>IEventSink.Deliver</c> (Story 004): Story 004 calls <see cref="CompleteJoin"/> from there. M0's
/// <c>Hello</c> / <c>HelloAck</c> slots in as the <c>Connected</c> to <c>Joining</c> step. Main thread only; allocation-free
/// after construction.
/// </para>
/// <para>
/// Split by concern: this file holds the fields, constructor, counters, queries and the frame hooks;
/// <c>HostAdapter.Admission.cs</c> the join state machine; <c>HostAdapter.Inbound.cs</c> the transport sinks, decode and replies.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var (hostEnd, clientEnd) = LoopbackTransport.CreatePair();
/// var host = new HostAdapter(world, registry, hostEnd, null, AdapterSettings.Default, new InMemoryRoster());
/// host.AdmitLocal(hostEnd.Peer, PlayerSlot.FromValue(0));
/// runner = new FrameRunner(clock, driver, client, world, host, FrameRunnerSettings.Default);   // IFrameTransport
/// </code>
/// </example>
public sealed partial class HostAdapter : IFrameTransport, IEventSink
{
    /// <summary>The account key the M-1a local admission uses when the caller gives none.</summary>
    public const ulong LocalAccountKey = 1UL;

    /// <summary>
    /// The request id of the <c>System</c> <c>PlayerJoin</c> op for a slot: this base plus the slot value. Above 65 535, so it
    /// can never collide with a peer's u16 wire request id; Story 004 recognises the join reply by it.
    /// </summary>
    public const uint JoinReqIdBase = 0x0001_0000u;

    private readonly SimWorld _sim;
    private readonly OpCodecRegistry _registry;
    private readonly ITransport _loopback;
    private readonly ITransport? _remote;
    private readonly IRoster _roster;
    private readonly PeerEntry[] _peers;
    private readonly byte[] _replyBuffer = new byte[ReplyCodec.MaxSize];
    private readonly TransportSink _loopbackSink;
    private readonly TransportSink _remoteSink;
    private bool _pumping;
    private bool _inMessage;

    /// <summary>Creates the adapter. Allocates the peer table and scratch buffers; nothing allocates afterwards.</summary>
    /// <param name="sim">The host's world; requests are enqueued into it.</param>
    /// <param name="registry">The session's codec registry (built from <c>sim.OpDescriptors</c> and the composition root's codecs). Not in ADR-0011's sketch; the adapter has no other way to get the codecs.</param>
    /// <param name="loopback">The host end of the in-process pair (the host's own client is the other end).</param>
    /// <param name="remote">The remote transport, or null (always null at M-1a).</param>
    /// <param name="settings">Adapter tunables.</param>
    /// <param name="roster">Networking's account to player-id roster.</param>
    /// <exception cref="ArgumentNullException">A reference argument other than <paramref name="remote"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="remote"/> is the same object as <paramref name="loopback"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="AdapterSettings.MaxPeers"/> is outside 1 to <c>PlayerSlot.Count</c>.</exception>
    public HostAdapter(SimWorld sim, OpCodecRegistry registry, ITransport loopback, ITransport? remote, AdapterSettings settings, IRoster roster)
        : this(sim, registry, loopback, remote, settings, roster, null)
    {
    }

    /// <summary>
    /// Creates the adapter with an explicit event codec registry (Story 004). Allocates the peer table, the outbound buffers
    /// (sized to the world's <see cref="SimCoreSettings.EventLogCapacity"/>) and scratch buffers; nothing allocates afterwards.
    /// </summary>
    /// <param name="sim">The host's world; requests are enqueued into it.</param>
    /// <param name="registry">The session's op codec registry.</param>
    /// <param name="loopback">The host end of the in-process pair.</param>
    /// <param name="remote">The remote transport, or null (always null at M-1a).</param>
    /// <param name="settings">Adapter tunables.</param>
    /// <param name="roster">Networking's account to player-id roster.</param>
    /// <param name="events">The event codec registry, or null for one built from <c>sim.EventDescriptors</c> with no feature codecs (only <c>OpReply</c> leaves).</param>
    /// <exception cref="ArgumentNullException">A reference argument other than <paramref name="remote"/> or <paramref name="events"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="remote"/> is the same object as <paramref name="loopback"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="AdapterSettings.MaxPeers"/> or <see cref="AdapterSettings.MaxBatchBytes"/> is out of range.</exception>
    public HostAdapter(SimWorld sim, OpCodecRegistry registry, ITransport loopback, ITransport? remote, AdapterSettings settings, IRoster roster, EventCodecRegistry? events)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(loopback);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(roster);
        if (ReferenceEquals(loopback, remote))
        {
            throw new ArgumentException("The remote transport must be a different object from the loopback transport.", nameof(remote));
        }

        if (settings.MaxPeers < 1 || settings.MaxPeers > PlayerSlot.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), $"MaxPeers must be 1 to {PlayerSlot.Count}.");
        }

        if (settings.MaxBatchBytes < EventBatch.MinBufferSize || settings.MaxBatchBytes > EventBatch.MaxBufferSize)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), $"MaxBatchBytes must be {EventBatch.MinBufferSize} to {EventBatch.MaxBufferSize}.");
        }

        _sim = sim;
        _registry = registry;
        _loopback = loopback;
        _remote = remote;
        _roster = roster;
        _peers = new PeerEntry[settings.MaxPeers];
        _loopbackSink = new TransportSink(this, PeerOrigin.Loopback);
        _remoteSink = new TransportSink(this, PeerOrigin.Remote);
        _events = events ?? new EventCodecRegistry(sim.EventDescriptors, Array.Empty<IEventCodec>());
        _out = new OutboundBuffers(settings.MaxPeers, sim.Settings.EventLogCapacity, settings.MaxBatchBytes);
        _dedupe = CreateReplyCaches(settings.MaxPeers);
    }

    /// <summary>Requests enqueued into the Sim (excludes the adapter's own <c>PlayerJoin</c> ops).</summary>
    public long EnqueuedCount { get; private set; }

    /// <summary>Messages dropped because the peer was unknown, connected-only or still joining.</summary>
    public long UnadmittedDropCount { get; private set; }

    /// <summary><c>busy</c> replies sent (<c>EnqueueOp</c> returned false).</summary>
    public long BusyReplyCount { get; private set; }

    /// <summary><c>bad_request</c> replies sent (decode failures and refused <c>SystemOnly</c> kinds).</summary>
    public long BadRequestReplyCount { get; private set; }

    /// <summary>Peer requests for a <c>SystemOnly</c> kind that were refused (also counted in <see cref="BadRequestReplyCount"/>).</summary>
    public long SystemOnlyRefusedCount { get; private set; }

    /// <summary>Messages from an admitted peer on a channel other than <see cref="NetChannel.Reliable"/>, dropped (their consumers are later stories).</summary>
    public long NonRequestChannelDropCount { get; private set; }

    /// <summary>Messages a transport delivered outside <see cref="PumpInbound"/>, dropped.</summary>
    public long OutsidePumpDropCount { get; private set; }

    /// <summary>Messages a transport delivered from inside another message callback, dropped.</summary>
    public long ReentrantDropCount { get; private set; }

    /// <summary>Replies the adapter built but could not encode (never expected; counted instead of thrown).</summary>
    public long ReplyEncodeFailureCount { get; private set; }

    /// <summary>Connections refused because the peer table was full (the peer is disconnected).</summary>
    public long PeerTableFullCount { get; private set; }

    /// <summary>Connect events for a peer whose connect was already seen (a genuine duplicate; ignored). The first connect of a freshly admitted loopback row is not counted.</summary>
    public long DuplicateConnectCount { get; private set; }

    /// <summary>Disconnects of the loopback peer (M-1a: ends the session; counted and otherwise ignored).</summary>
    public long LoopbackDisconnectCount { get; private set; }

    /// <summary>Disconnects of remote peers (their row is freed; <c>FreezePlayer</c> and queue cleanup are M0).</summary>
    public long RemoteDisconnectCount { get; private set; }

    /// <summary>Joins that the Sim refused (<see cref="CompleteJoin"/> with a non-zero reason); the peer fell back to <see cref="PeerState.Connected"/>.</summary>
    public long JoinRefusedCount { get; private set; }

    /// <summary>Number of occupied peer-table rows.</summary>
    public int PeerCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _peers.Length; i++)
            {
                if (_peers[i].State != PeerState.None)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>The sink the adapter registers with the loopback transport (internal: tests drive callbacks directly).</summary>
    internal ITransportSink LoopbackSink => _loopbackSink;

    /// <summary>The sink the adapter registers with the remote transport (internal: tests drive callbacks directly).</summary>
    internal ITransportSink RemoteSink => _remoteSink;

    /// <summary>Reports the state of a peer's row.</summary>
    /// <param name="origin">Which transport the handle belongs to.</param>
    /// <param name="peer">The peer handle.</param>
    /// <returns>The state, or <see cref="PeerState.None"/> when there is no row.</returns>
    public PeerState GetPeerState(PeerOrigin origin, PeerHandle peer)
    {
        int index = Find(origin, peer);
        return index < 0 ? PeerState.None : _peers[index].State;
    }

    /// <summary>Reads an admitted (or joining) peer's identity from the table.</summary>
    /// <param name="origin">Which transport the handle belongs to.</param>
    /// <param name="peer">The peer handle.</param>
    /// <param name="playerId">The mapped player id; <see cref="EntityId.None"/> until the join completed.</param>
    /// <param name="slot">The assigned slot; <see cref="PlayerSlot.None"/> for a merely connected peer.</param>
    /// <returns>True when the peer has a row.</returns>
    public bool TryGetPeer(PeerOrigin origin, PeerHandle peer, out EntityId playerId, out PlayerSlot slot)
    {
        int index = Find(origin, peer);
        if (index < 0)
        {
            playerId = EntityId.None;
            slot = PlayerSlot.None;
            return false;
        }

        playerId = _peers[index].PlayerId;
        slot = _peers[index].Slot;
        return true;
    }

    /// <summary>
    /// Frame step 1: polls the remote transport (if any) and then the loopback transport; every delivered request is decoded
    /// and enqueued, or answered <c>busy</c> / <c>bad_request</c>, before this returns. The only place <c>EnqueueOp</c> is
    /// called for peer requests. Re-entering asserts in assert builds and is ignored otherwise.
    /// </summary>
    public void PumpInbound()
    {
        if (_pumping)
        {
            SimAssert.Fail(NetAssertCode.PumpReentered);
            return;
        }

        _pumping = true;
        try
        {
            _remote?.Poll(_remoteSink);
            _loopback.Poll(_loopbackSink);
        }
        finally
        {
            _pumping = false;
        }
    }

    private int Find(PeerOrigin origin, PeerHandle peer)
    {
        for (int i = 0; i < _peers.Length; i++)
        {
            if (_peers[i].State != PeerState.None && _peers[i].Origin == origin && _peers[i].Peer == peer)
            {
                return i;
            }
        }

        return -1;
    }

    private int Allocate(PeerOrigin origin, PeerHandle peer, bool connectSeen)
    {
        for (int i = 0; i < _peers.Length; i++)
        {
            if (_peers[i].State == PeerState.None)
            {
                _peers[i] = new PeerEntry { Origin = origin, Peer = peer, State = PeerState.Connected, ConnectSeen = connectSeen };
                ResetOutbound(i);
                _dedupe[i].Clear(); // a fresh row inherits no orphans
                return i;
            }
        }

        return -1;
    }

    private ITransport TransportOf(PeerOrigin origin) => origin == PeerOrigin.Loopback ? _loopback : _remote!;

    private struct PeerEntry
    {
        public PeerOrigin Origin;
        public PeerHandle Peer;
        public ulong AccountKey;
        public EntityId PlayerId;
        public PlayerSlot Slot;
        public PeerState State;
        public uint JoinReqId;

        /// <summary>The player id the adapter put in this peer's <c>PlayerJoin</c> payload (from the roster; None for a fresh account).</summary>
        public EntityId JoinPlayerId;

        /// <summary>False for a row created by local admission before the transport's first connect event arrived.</summary>
        public bool ConnectSeen;
    }

}
