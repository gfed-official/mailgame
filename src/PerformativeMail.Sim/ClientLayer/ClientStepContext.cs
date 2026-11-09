using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// What an <see cref="IClientStep"/> sees for one client tick. A small readonly struct passed by <c>in</c>;
/// valid only for the duration of the <see cref="IClientStep.Step"/> call. It carries the batch flag, the client
/// tick counter, this tick's latched <see cref="InputFrame"/>, the <see cref="IHostClock"/>, the attached <see cref="ClientPorts"/>
/// and the layer's counters. It never exposes a <c>SimWorld</c>: the client reads only its mirror (ADR-0003 section 6).
/// </summary>
/// <example>
/// <code>
/// public void Step(in ClientStepContext ctx)
/// {
///     if (ctx.IsFirstTickInBatch &amp;&amp; ctx.Input.EdgeCount &gt; 0) { /* resolve edges in 4d */ }
///     uint host = ctx.HostClock.IsValid ? ctx.HostClock.EstimatedHostTick : 0;
/// }
/// </code>
/// </example>
public readonly struct ClientStepContext
{
    private readonly ClientLayer _layer;

    internal ClientStepContext(bool isFirstTickInBatch, uint clientTick, ClientLayer layer)
    {
        IsFirstTickInBatch = isFirstTickInBatch;
        ClientTick = clientTick;
        _layer = layer;
    }

    /// <summary>True on the first client tick of a frame's batch; the only tick that carries input edges.</summary>
    public bool IsFirstTickInBatch { get; }

    /// <summary>The client tick counter: the number of <c>ClientLayer.Step</c> calls before this one (0 on the first).</summary>
    public uint ClientTick { get; }

    /// <summary>The host tick estimate source (implemented by the adapter).</summary>
    public IHostClock HostClock => _layer.HostClock;

    /// <summary>True once the host shell has attached the ports (<see cref="ClientLayer.Attach"/>); steps that need a port do nothing before.</summary>
    public bool IsAttached => _layer.IsAttached;

    /// <summary>The attached ports (every member null while <see cref="IsAttached"/> is false).</summary>
    public ref readonly ClientPorts Ports => ref _layer.Ports;

    /// <summary>The client layer's counters (solver counter flushes and other client-side counts).</summary>
    public SimCounters Counters => _layer.Counters;

    /// <summary>
    /// This tick's input frame. Valid for steps bound to <see cref="ClientStepId.InputLatch"/> and later (the built-in
    /// latch has already emitted it); at <see cref="ClientStepId.MirrorDrain"/> it is still the previous tick's frame.
    /// </summary>
    public ref readonly InputFrame Input => ref _layer.InputLatch.Frame;
}
