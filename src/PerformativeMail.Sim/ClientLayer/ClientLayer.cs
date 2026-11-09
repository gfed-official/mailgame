using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// The client-side shell (ADR-0003 section 6): runs the bound <see cref="IClientStep"/>s once per client tick in
/// <see cref="ClientStepId"/> order (<c>MirrorDrain</c>, <c>InputLatch</c>, <c>CarrierStep</c>, <c>Previews</c>); the
/// <c>Previews</c> row is already in <see cref="PreviewSubStep"/> order, then the plain <c>Previews</c> binds (the builder flattens it).
/// It reads only its mirror and ports, never a <c>SimWorld</c>, even on the host. Created by
/// <see cref="ClientLayerBuilder.Build"/>; the host shell then calls <see cref="Attach"/> once, before the first <see cref="Step"/>
/// (ADR-0016 section 1). <see cref="Step"/> allocates 0 B. Not thread-safe: step it on the thread that owns the client.
/// </summary>
/// <example>
/// <code>
/// ClientLayer layer = new ClientLayerBuilder(hostClock).Build();
/// layer.Attach(in ports);                 // once, before the first Step
/// layer.InputWriter.PushEdge(edge);
/// layer.Step(isFirstTickInBatch: true);    // FrameRunner: Step(i == 0) before each Tick()
/// layer.Step(isFirstTickInBatch: false);
/// </code>
/// </example>
public sealed class ClientLayer
{
    private readonly IClientStep[][] _steps;
    private uint _clientTick;
    private ClientPorts _ports;

    internal ClientLayer(IHostClock hostClock, IClientStep[][] steps, InputLatch inputLatch, SimCounters counters)
    {
        HostClock = hostClock;
        _steps = steps;
        InputLatch = inputLatch;
        Counters = counters;
    }

    /// <summary>The host tick source given to the builder.</summary>
    public IHostClock HostClock { get; }

    /// <summary>The built-in input latch (read side: <see cref="InputLatch.Frame"/>, pending counts). The push side is <see cref="InputWriter"/>.</summary>
    public InputLatch InputLatch { get; }

    /// <summary>The push side of the built-in latch, which is all the router needs (the latch's push members are not public on the concrete type).</summary>
    public IInputLatchWriter InputWriter => InputLatch;

    /// <summary>The client's counters.</summary>
    public SimCounters Counters { get; }

    /// <summary>The client tick the next <see cref="Step"/> call will carry (the number of calls so far).</summary>
    public uint ClientTick => _clientTick;

    /// <summary>True once <see cref="Attach"/> has run.</summary>
    public bool IsAttached { get; private set; }

    /// <summary>The attached ports (every member null before <see cref="Attach"/>); steps read them through <see cref="ClientStepContext.Ports"/>.</summary>
    public ref readonly ClientPorts Ports => ref _ports;

    /// <summary>
    /// Attaches the ports (ADR-0016 section 1): once, after the layer is built and before the first <see cref="Step"/>. A second call,
    /// or a call after the first <see cref="Step"/>, is refused, and so is a null port (bind a null object instead). Boot-time only.
    /// </summary>
    /// <param name="ports">The ports; copied.</param>
    /// <exception cref="InvalidOperationException">Already attached, or a <see cref="Step"/> has already run.</exception>
    /// <exception cref="ArgumentException">A port is null.</exception>
    /// <example><code>layer.Attach(new ClientPorts { Requests = requests, HostClock = clock, TransformSamples = sink, CarrierLoad = load, LocalPlayer = local });</code></example>
    public void Attach(in ClientPorts ports)
    {
        if (IsAttached)
        {
            throw new InvalidOperationException("ClientLayer.Attach runs once; the ports are already attached.");
        }

        if (_clientTick != 0)
        {
            throw new InvalidOperationException($"ClientLayer.Attach must run before the first Step; {_clientTick} step(s) have run.");
        }

        ThrowIfNull(ports.Requests, nameof(ClientPorts.Requests));
        ThrowIfNull(ports.HostClock, nameof(ClientPorts.HostClock));
        ThrowIfNull(ports.TransformSamples, nameof(ClientPorts.TransformSamples));
        ThrowIfNull(ports.CarrierLoad, nameof(ClientPorts.CarrierLoad));
        ThrowIfNull(ports.LocalPlayer, nameof(ClientPorts.LocalPlayer));
        _ports = ports;
        IsAttached = true;
    }

    /// <summary>
    /// Runs one client tick: every bound step in <see cref="ClientStepId"/> order, then advances the client tick counter.
    /// Unbound ids are no-ops. A frame that runs no ticks simply does not call this, so nothing is cleared.
    /// </summary>
    /// <param name="isFirstTickInBatch">True for the first tick of a frame's batch (the only one that carries input edges).</param>
    /// <example><code>for (int i = 0; i &lt; ticks; i++) { layer.Step(i == 0); sim.Tick(); }</code></example>
    public void Step(bool isFirstTickInBatch)
    {
        var ctx = new ClientStepContext(isFirstTickInBatch, _clientTick, this);
        IClientStep[][] steps = _steps;
        for (int id = 0; id < steps.Length; id++)
        {
            IClientStep[] row = steps[id];
            for (int i = 0; i < row.Length; i++)
            {
                row[i].Step(in ctx);
            }
        }

        _clientTick++;
    }

    private static void ThrowIfNull(object? port, string name)
    {
        if (port is null)
        {
            throw new ArgumentException($"ClientPorts.{name} is null; attach a null object for an absent port.", "ports");
        }
    }
}
