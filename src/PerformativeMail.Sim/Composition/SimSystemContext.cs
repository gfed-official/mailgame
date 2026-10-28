using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Composition;

/// <summary>
/// The closed injectable list (ADR-0003 section 4) as the composition root holds it while one system is being constructed.
/// Used only by construction code in <see cref="SimComposition"/> and by per-test factories; it is never a constructor
/// parameter of a system (that would make it a service locator, which the reflection test forbids). The guarded members
/// enforce the list at run time: <see cref="MailIds"/> and <see cref="WalletGate"/> only for Mail &amp; Destinations, <see cref="RunControl"/> only for Run
/// Structure, and <see cref="Api{TApi}"/> only for a provider that <see cref="SystemGraph"/> declares and that is already
/// built; anything else throws <see cref="SimBuildException"/> with <c>UndeclaredDependency</c>. The context is closed when
/// <c>Build</c> finishes constructing; any later use throws <see cref="InvalidOperationException"/>.
/// Not thread-safe; boot-time only.
/// </summary>
/// <example>
/// <code>
/// var mail = new MailSystem(ctx.Events, ctx.MailIds, ctx.Rng, ctx.Api&lt;IInventoryApi&gt;(SystemId.GridInventory), ctx.Content);
/// </code>
/// </example>
internal sealed class SimSystemContext
{
    private const int IdCount = 256;

    private readonly ISimSystem?[] _built = new ISimSystem?[IdCount];
    private readonly SystemGraph _graph;
    private readonly ContentSet _content;
    private readonly SimCoreServices _services;
    private readonly ITickReader _tick;
    private readonly IRunControl _runControl;
    private readonly IWorldQuery? _world;
    private readonly IWalletGate? _wallet;
    private bool _closed;
    private SystemId _consumer;

    internal SimSystemContext(SystemGraph graph, ContentSet content, SimCoreServices services, ITickReader tick, IRunControl runControl, IWorldQuery? world = null, IWalletGate? wallet = null)
    {
        _world = world;
        _wallet = wallet;
        _graph = graph;
        _content = content;
        _services = services;
        _tick = tick;
        _runControl = runControl;
    }

    /// <summary>The system being constructed.</summary>
    public SystemId Consumer => Open()._consumer;

    /// <summary>The event-log writer facade (any system).</summary>
    public EventLogFacade Events => Open()._services.Events;

    /// <summary>The entity id allocator (any system that allocates).</summary>
    public EntityIds EntityIds => Open()._services.EntityIds;

    /// <summary>The mail id allocator: Mail &amp; Destinations only.</summary>
    /// <exception cref="SimBuildException"><c>UndeclaredDependency</c> for any other consumer.</exception>
    public MailIds MailIds
    {
        get
        {
            Open();
            Require(_consumer == SystemId.MailDestinations, "MailIds");
            return _services.MailIds;
        }
    }

    /// <summary>
    /// The wallet port (ADR-0017 section 8; ADR-0003 section 4 erratum 2026-10-28): Mail &amp; Destinations only. At M-1a it is the
    /// <c>StubWallet</c> the composition root built before the systems; it is not a <see cref="SystemGraph"/> edge.
    /// </summary>
    /// <exception cref="SimBuildException"><c>UndeclaredDependency</c> for any other consumer.</exception>
    /// <exception cref="InvalidOperationException">The build made no wallet (it does for every profile that includes Mail &amp; Destinations).</exception>
    public IWalletGate WalletGate
    {
        get
        {
            Open();
            Require(_consumer == SystemId.MailDestinations, "WalletGate");
            return _wallet ?? throw new InvalidOperationException("The composition built no wallet for this profile.");
        }
    }

    /// <summary>The read-only player slot view (any system).</summary>
    public ISlotView Slots => Open()._services.Slots;

    /// <summary>The consumer's own owner-filtered RNG view (exposes nothing for a system that owns no stream, such as Throwing).</summary>
    public IRngStreams Rng => Open()._services.Rng.ViewFor(_consumer);

    /// <summary>The tick reader (any system).</summary>
    public ITickReader Tick => Open()._tick;

    /// <summary>The stop port: Run Structure only.</summary>
    /// <exception cref="SimBuildException"><c>UndeclaredDependency</c> for any other consumer.</exception>
    public IRunControl RunControl
    {
        get
        {
            Open();
            Require(_consumer == SystemId.RunStructure, "IRunControl");
            return _runControl;
        }
    }

    /// <summary>
    /// The world's read interface (any system that reads terrain, solids or the Post Office pad; ADR-0026), or null when the build
    /// was given no world (<see cref="SimBuildArgs.World"/>).
    /// </summary>
    public IWorldQuery? World => Open()._world;

    /// <summary>The accepted content set (any system; pass a sub-table where that is all the system reads).</summary>
    public ContentSet Content => Open()._content;

    /// <summary>The world's check ring (any system).</summary>
    public SimCheckRing CheckRing => Open()._services.CheckRing;

    /// <summary>The world's counters (any system).</summary>
    public SimCounters Counters => Open()._services.Counters;

    /// <summary>The world's stat sheet (read face); any system that reads stats may receive it.</summary>
    public IStatSheet StatSheet => Open()._services.Stats.Sheet;

    /// <summary>The stat writer; only Perks and Stamps &amp; Run Events may receive it (ADR-0010 section 7, ADR-0003 section 4).</summary>
    public IStatWriter StatWriter
    {
        get
        {
            Open();
            Require(_consumer == SystemId.Perks || _consumer == SystemId.StampsRunEvents, "IStatWriter");
            return _services.Stats;
        }
    }

    /// <summary>
    /// The <c>I…Api</c> of <paramref name="provider"/>, which must be a declared dependency of the consumer in the profile's
    /// graph, already constructed (so earlier in the construction order) and implement <typeparamref name="TApi"/>.
    /// </summary>
    /// <typeparam name="TApi">The API interface.</typeparam>
    /// <param name="provider">The system that provides it.</param>
    /// <returns>The provider, as its API.</returns>
    /// <exception cref="SimBuildException"><c>UndeclaredDependency</c> when the edge is not declared, the provider is not built (not in the profile, or later in the order) or does not implement <typeparamref name="TApi"/>.</exception>
    public TApi Api<TApi>(SystemId provider)
        where TApi : class
    {
        Open();
        if (!_graph.Declares(_consumer, provider))
        {
            throw new SimBuildException(
                SimBuildErrorCode.UndeclaredDependency,
                $"{_consumer} asks for {typeof(TApi).Name} of {provider}, which SystemGraph does not declare as its dependency.");
        }

        ISimSystem? built = _built[(int)provider];
        if (built is null)
        {
            throw new SimBuildException(
                SimBuildErrorCode.UndeclaredDependency,
                $"{_consumer} asks for {typeof(TApi).Name} of {provider}, which is not built before it in profile order (not in the profile, or later).");
        }

        return built as TApi ?? throw new SimBuildException(
            SimBuildErrorCode.UndeclaredDependency,
            $"{_consumer} asks for {typeof(TApi).Name} of {provider}, which does not provide it.");
    }

    internal void Begin(SystemId consumer)
    {
        Open();
        _consumer = consumer;
    }

    internal void Built(SystemId id, ISimSystem system)
    {
        Open();
        _built[(int)id] = system;
    }

    internal void Close() => _closed = true;

    private SimSystemContext Open() => _closed
        ? throw new InvalidOperationException("The composition context is closed: systems take their dependencies in their constructor, during Build.")
        : this;

    private void Require(bool allowed, string what)
    {
        if (!allowed)
        {
            throw new SimBuildException(SimBuildErrorCode.UndeclaredDependency, $"{_consumer} may not receive {what} (ADR-0003 section 4 injectable list).");
        }
    }
}
