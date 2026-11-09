using PerformativeMail.Client.Core.Net;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The router's four ClientLayer-published ports as late-bound slots (input-controls story 028). Boot step 4 builds the router
/// (ADR-0019 section 3) before steps 5-6 create the session and publish its ports, so the router is handed these slots and the
/// input boot binds each one once the session exists. An unbound slot reads its idle value: nothing focused, carrier
/// <c>default</c> (alive, OnFoot, cursor empty), a throw never refused and never stale, client tick 0. <see cref="Describe"/>
/// names which slots are bound for the boot log, so a default is never silent. Bind at boot only; 0 B per read. Main thread only.
/// </summary>
/// <example>
/// <code>
/// var ports = new InputPortSlots();
/// var router = new InputRouter(map, settings, clock, ports.Interaction, prompt, ports.Carrier, ports.ThrowAim, ports.Ticks, log);
/// ports.BindSession(session);   // after boot step 6: the client tick
/// </code>
/// </example>
public sealed class InputPortSlots : IInteractionQuery, ICarrierStatusSource, IThrowAimView, IClientTickSource
{
    private readonly InteractionSnapshot _idleInteraction;
    private readonly CarrierStatus _idleCarrier;
    private IInteractionQuery? _interaction;
    private ICarrierStatusSource? _carrier;
    private IThrowAimView? _throwAim;
    private HostSession? _session;

    /// <summary>The interaction snapshot slot.</summary>
    public IInteractionQuery Interaction => this;

    /// <summary>The carrier status slot.</summary>
    public ICarrierStatusSource Carrier => this;

    /// <summary>The throw preview slot.</summary>
    public IThrowAimView ThrowAim => this;

    /// <summary>The client tick slot.</summary>
    public IClientTickSource Ticks => this;

    /// <inheritdoc />
    ref readonly InteractionSnapshot IInteractionQuery.Current => ref _interaction is null ? ref _idleInteraction : ref _interaction.Current;

    /// <inheritdoc />
    ref readonly CarrierStatus ICarrierStatusSource.Current => ref _carrier is null ? ref _idleCarrier : ref _carrier.Current;

    /// <inheritdoc />
    byte IThrowAimView.NotThrowable => _throwAim?.NotThrowable ?? 0;

    /// <inheritdoc />
    bool IThrowAimView.EntryStale => _throwAim?.EntryStale ?? false;

    /// <inheritdoc />
    uint IClientTickSource.ClientTick => _session?.Client.ClientTick ?? 0;

    /// <summary>Binds the published interaction snapshot (Player Character's publisher).</summary>
    /// <param name="query">The port.</param>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    public void BindInteraction(IInteractionQuery query) => _interaction = query ?? throw new ArgumentNullException(nameof(query));

    /// <summary>Binds the published carrier status (Player Character story 020).</summary>
    /// <param name="source">The port.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public void BindCarrier(ICarrierStatusSource source) => _carrier = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Binds Throwing's published throw preview (throwing-catching story 004).</summary>
    /// <param name="view">The port.</param>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    public void BindThrowAim(IThrowAimView view) => _throwAim = view ?? throw new ArgumentNullException(nameof(view));

    /// <summary>Binds the client tick to the session's own client layer.</summary>
    /// <param name="session">The booted host session.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    public void BindSession(HostSession session) => _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <summary>One <c>name=bound|idle</c> entry per slot, for the boot log. Allocates.</summary>
    /// <returns>For example <c>interaction=idle carrier=idle throwAim=idle ticks=bound</c>.</returns>
    public string Describe() =>
        $"interaction={State(_interaction)} carrier={State(_carrier)} throwAim={State(_throwAim)} ticks={State(_session)}";

    private static string State(object? target) => target is null ? "idle" : "bound";
}
