using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Net.Codec;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;
using ClientLayerShell = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Client.Core.Net;

/// <summary>Optional pieces of a <see cref="HostSession"/>; every property has a working default.</summary>
public sealed class HostSessionOptions
{
    /// <summary>Host adapter tunables; defaults when null.</summary>
    public AdapterSettings? Adapter { get; init; }

    /// <summary>Client adapter tunables; defaults when null.</summary>
    public ClientAdapterSettings? ClientAdapter { get; init; }

    /// <summary>Frame runner tunables; <see cref="FrameRunnerSettings.Default"/> when null.</summary>
    public FrameRunnerSettings? Frame { get; init; }

    /// <summary>Account to player roster; an <see cref="InMemoryRoster"/> when null.</summary>
    public IRoster? Roster { get; init; }

    /// <summary>Inbox capacity in records (default <see cref="ClientInbox.DefaultCapacity"/>).</summary>
    public int InboxCapacity { get; init; } = ClientInbox.DefaultCapacity;

    /// <summary>Binds the host client's other steps (input latch consumers, carrier, previews) before the layer is built. The MirrorDrain step is already bound.</summary>
    public Action<ClientLayerBuilder>? ConfigureClient { get; init; }

    /// <summary>Wraps the frame transport (the host adapter) for instrumentation; the wrapper must forward both calls.</summary>
    public Func<IFrameTransport, IFrameTransport>? WrapTransport { get; init; }
}

/// <summary>
/// The host process's one-session assembly (ADR-0011 sections 1 and 2, ADR-0002 section 2; network-adapter-loopback story 007;
/// TR-sim-014, TR-sim-008, TR-sim-042): one <see cref="SimWorld"/> built by <see cref="SimComposition.Build"/>, one
/// <c>ClientLayer</c>, and the host's own client reaching the Sim only through <see cref="ClientAdapter"/> over
/// <see cref="LoopbackTransport"/> to the <see cref="HostAdapter"/>: no "is host" path.
/// <list type="bullet">
/// <item><description><b>Frame</b> (<see cref="RunFrame"/>): <c>HostAdapter.PumpInbound</c> (remote, then loopback), <c>Advance</c>, then per
/// tick <c>ClientLayer.Step</c> (MirrorDrain pumps the client adapter into the inbox, then applies it), <c>SimWorld.Tick</c>,
/// <c>HostAdapter.FlushOutbound</c>. After the frame the client adapter decodes the last tick's batch into the inbox (decode
/// only, so the client clock equals <c>CurrentTick</c> between frames); nothing is applied until the next client tick.</description></item>
/// <item><description><b>Reply timing</b>: a batch flushed after tick T is applied by MirrorDrain at exactly the next client tick.
/// A request sent before <see cref="RunFrame"/> is enqueued by that frame's PumpInbound and applies at its first tick; one sent
/// from inside a client step waits for the next frame's PumpInbound (frame start only, TR-sim-008).</description></item>
/// <item><description><b>Join</b>: construction admits the local loopback peer into slot 0; its System <c>PlayerJoin</c> applies on tick 0
/// and the peer is admitted when tick 0's log is delivered. Do not send before <see cref="IsLocalJoined"/>: such a request is dropped
/// unanswered and its pending slot is held until disconnect (TD-068 item 2).</description></item>
/// <item><description><b>Clock</b>: the injected clock is read once per frame (by the frame runner); the same sample feeds
/// <c>ClientAdapter.UpdateTime</c>.</description></item>
/// </list>
/// Godot-free; the Godot GameLoop holds one. Main thread only; the constructing thread owns the world.
/// </summary>
/// <example>
/// <code>
/// var session = new HostSession(new SimBuildArgs(profile, content, seed), clock, gameOpCodecs, gameEventCodecs);
/// session.AddEventSink(presentationSink);            // host Presentation, optional, before the first frame
/// session.MirrorDrain.Register(containerMirror);
/// session.RunFrame();                                // once per Godot physics frame
/// ushort reqId = session.ClientAdapter.Send(kind, payload);
/// </code>
/// </example>
public sealed class HostSession
{
    private readonly SampledClock _clock;
    private readonly DeferredEventSink _relay = new();
    private readonly FrameRunner _runner;
    private readonly ClientInbox _inbox;

    /// <summary>Builds the session, admits the local peer and starts the tick driver at the clock's current time.</summary>
    /// <param name="args">Composition arguments. <c>EventSink</c> is set by the session; <c>HostClock</c> is unused (the host client uses the adapter's estimator).</param>
    /// <param name="clock">The monotonic clock the frame runner reads once per frame.</param>
    /// <param name="gameOpCodecs">Wire codecs of the game's op kinds (the Sim Core codecs are added); null for none.</param>
    /// <param name="eventCodecs">Wire codecs of the game's event kinds; null for none.</param>
    /// <param name="options">Optional pieces; defaults when null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The local peer could not be admitted into slot 0.</exception>
    public HostSession(
        in SimBuildArgs args,
        IMonotonicClock clock,
        IEnumerable<IOpCodec>? gameOpCodecs = null,
        IEnumerable<IEventCodec>? eventCodecs = null,
        HostSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        options ??= new HostSessionOptions();
        _clock = new SampledClock(clock);
        Host = SimComposition.Build(args with { EventSink = _relay });
        SimWorld world = Host.World;

        (OpCodecRegistry opRegistry, EventCodecRegistry events) = BuildCodecs(world, gameOpCodecs, eventCodecs);
        (LoopbackTransport hostEnd, LoopbackTransport clientEnd) = LoopbackTransport.CreatePair();
        HostAdapter = new HostAdapter(world, opRegistry, hostEnd, null, options.Adapter ?? new AdapterSettings(), options.Roster ?? new InMemoryRoster(), events);
        _relay.Attach(HostAdapter);

        ClientAdapter = new ClientAdapter(clientEnd, clientEnd.Peer, events, options.ClientAdapter);
        _inbox = new ClientInbox(options.InboxCapacity);
        MirrorDrain = new MirrorDrainStep(_inbox, ClientAdapter);
        Client = BuildClient(ClientAdapter.Clock, MirrorDrain, options.ConfigureClient);

        HostClock = new HostClock(world);
        Driver = new TickDriver(clock.Frequency, world.Settings.MaxCatchUpTicksPerFrame);
        IFrameTransport transport = options.WrapTransport?.Invoke(HostAdapter) ?? HostAdapter;
        _runner = new FrameRunner(_clock, Driver, Client, world, transport, options.Frame ?? FrameRunnerSettings.Default);

        LocalPeer = hostEnd.Peer;
        AdmitResult admitted = HostAdapter.AdmitLocal(LocalPeer, PlayerSlot.FromValue(0));
        if (admitted != AdmitResult.Ok)
        {
            throw new InvalidOperationException($"The local peer could not be admitted into slot 0: {admitted}.");
        }

        Driver.Start(_clock.Timestamp);
        ClientAdapter.UpdateTime(_clock.LastMilliseconds);
    }

    /// <summary>The composed host (world, content). The <c>Client</c> built by composition is unused; use <see cref="Client"/>.</summary>
    public SimHost Host { get; }

    /// <summary>The authoritative world.</summary>
    public SimWorld World => Host.World;

    /// <summary>The host's own client layer.</summary>
    public ClientLayerShell Client { get; }

    /// <summary>The host side of the adapter (remote is null at M-1a).</summary>
    public HostAdapter HostAdapter { get; }

    /// <summary>The host's own client adapter over the loopback transport; send requests through it.</summary>
    public ClientAdapter ClientAdapter { get; }

    /// <summary>The MirrorDrain step; register mirror consumers on it before the first frame.</summary>
    public MirrorDrainStep MirrorDrain { get; }

    /// <summary>The tick driver (<see cref="TickDriver.AlphaPermille"/> for render interpolation).</summary>
    public TickDriver Driver { get; }

    /// <summary>The handle under which the host adapter knows the local loopback peer.</summary>
    public PeerHandle LocalPeer { get; }

    /// <summary>The local player's entity id once its <c>PlayerJoin</c> has applied and been delivered (tick 0's log); <see cref="EntityId.None"/> before.</summary>
    public EntityId LocalPlayerId => HostAdapter.TryGetPeer(PeerOrigin.Loopback, LocalPeer, out EntityId id, out _) ? id : EntityId.None;

    /// <summary>
    /// True once the local player's join has completed (tick 0's log delivered, normally after the first frame that runs a tick).
    /// Callers must not send requests before this: the host drops a request from a still-joining peer without a reply, so its
    /// pending slot in <see cref="ClientAdapter"/> waits until a disconnect (TD-068 item 2; resync is M0).
    /// </summary>
    public bool IsLocalJoined => HostAdapter.GetPeerState(PeerOrigin.Loopback, LocalPeer) == PeerState.Admitted;

    /// <summary>The host's <see cref="IHostClock"/> for host Presentation (<c>CurrentTick</c>).</summary>
    public IHostClock HostClock { get; }

    /// <summary>Attaches host Presentation's (or a test spy's) event sink; it sees each tick's log after the host adapter does. Setup-time call.</summary>
    /// <param name="sink">The sink.</param>
    public void AddEventSink(IEventSink sink) => _relay.Attach(sink);

    /// <summary>Runs one frame (ADR-0002 section 2) and lets the client adapter decode the frame's last batch into the inbox.</summary>
    public void RunFrame()
    {
        _runner.RunFrame();
        ClientAdapter.PumpInbound(_inbox);
        ClientAdapter.UpdateTime(_clock.LastMilliseconds);
    }

    // The Sim Core op codecs plus the game's, and the event codecs, over the built world's descriptors (one shared event registry
    // for the host and client adapters).
    private static (OpCodecRegistry Ops, EventCodecRegistry Events) BuildCodecs(SimWorld world, IEnumerable<IOpCodec>? gameOpCodecs, IEnumerable<IEventCodec>? eventCodecs)
    {
        var opCodecs = new List<IOpCodec>(SimCoreCodecs.Create());
        if (gameOpCodecs is not null)
        {
            opCodecs.AddRange(gameOpCodecs);
        }

        IEventCodec[] events = eventCodecs is null ? Array.Empty<IEventCodec>() : new List<IEventCodec>(eventCodecs).ToArray();
        return (new OpCodecRegistry(world.OpDescriptors, opCodecs.ToArray()), new EventCodecRegistry(world.EventDescriptors, events));
    }

    // The host client's layer: the adapter's clock, MirrorDrain first, then whatever the caller binds.
    private static ClientLayerShell BuildClient(IHostClock clock, MirrorDrainStep drain, Action<ClientLayerBuilder>? configure)
    {
        var builder = new ClientLayerBuilder(clock);
        drain.BindTo(builder);
        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>Wraps the injected clock and remembers the last sample so the frame's single read also feeds the adapter's time.</summary>
    private sealed class SampledClock : IMonotonicClock
    {
        private readonly IMonotonicClock _inner;
        private long _last;

        public SampledClock(IMonotonicClock inner) => _inner = inner;

        // Side effect on purpose: reading Timestamp samples the inner clock and remembers it. The frame runner is the sole
        // per-frame reader (ADR-0002 section 2 step 2: one clock read per frame); the session only reads LastMilliseconds.
        public long Timestamp => _last = _inner.Timestamp;

        public long Frequency => _inner.Frequency;

        public long LastMilliseconds
        {
            get
            {
                long f = _inner.Frequency;
                return (_last / f * 1000) + (_last % f * 1000 / f);
            }
        }
    }
}
