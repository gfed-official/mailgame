using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations.Ports;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// Mail &amp; Destinations' Sim module (ADR-0017; ADR-0003 section 4; mail-destinations story 005). At construction, while the world
/// is <c>Constructed</c>, it builds the mail table (<see cref="MailTable"/>, sized from <c>content.Settings.MailTablePages</c>; there
/// is no <c>MailSettings</c> at M-1a, TD-177 item 1) and the destination registry (<see cref="DestinationRegistry"/>, one row per
/// lot of the injected <see cref="IWorldQuery"/>). It reaches the wallet only through the injected <see cref="IWalletGate"/>
/// (the <c>StubWallet</c> at M-1a) and never references the Economy.
/// <para>
/// It binds the <c>MailStep</c> phase (phase 8, <see cref="Step"/>: the feeder credits, 0 at M-1a, and nothing else when idle;
/// manifest batch firing hooks in with stories 012 to 014) and the <see cref="SectionIds.Mail"/> dump section (the table rows, then
/// the registry). It implements and publishes <see cref="IMailApi"/>.
/// </para>
/// Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// // Composition: new MailSystem(context.Events, context.MailIds, context.Rng, context.Tick,
/// //     context.Api&lt;IInventoryApi&gt;(SystemId.GridInventory), context.WalletGate, world, context.Content, context.StatSheet,
/// //     context.CheckRing, context.Counters)
/// bool found = mail.TryGetDestinationFacts(lot.Destination, out DestinationFacts f);
/// </code>
/// </example>
[SimSystem(SystemId.MailDestinations)]
public sealed class MailSystem : ISimSystem, IMailApi
{
    // _events, _rng, _tick, _inventory, _stats and _counters are assigned but not read yet: the story prescribes this
    // constructor shape for stories 006-009, which read them. Kept deliberately; do not remove as unused.
    private readonly EventLogFacade _events;
    private readonly IRngStreams _rng;
    private readonly ITickReader _tick;
    private readonly IInventoryApi _inventory;
    private readonly IWalletGate _wallet;
    private readonly IStatSheet _stats;
    private readonly SimCounters _counters;
    private readonly MailTable _table;
    private readonly DestinationRegistry _registry;

    // F11 feeder credits, one per placed feeder in ascending construct id (ADR-0017 section 7). M-1a has no feeders.
    private readonly int[] _feederCredits = Array.Empty<int>();

    /// <summary>Creates the module and builds its table and destination registry.</summary>
    /// <param name="events">The world's event-log writer facade.</param>
    /// <param name="mailIds">The world's mail id allocator (Mail only).</param>
    /// <param name="rng">Mail's own RNG streams view (<c>mail</c>, <c>mail.special</c>).</param>
    /// <param name="tick">The world's tick reader.</param>
    /// <param name="inventory">Grid Inventory's API.</param>
    /// <param name="wallet">The wallet port (Mail only).</param>
    /// <param name="world">The world; only its lots are read, here.</param>
    /// <param name="content">The accepted content set.</param>
    /// <param name="stats">The world's stat sheet.</param>
    /// <param name="checkRing">The world's check ring (mail table growth is recorded here).</param>
    /// <param name="counters">The world's counters.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException"><c>MailRegistryInvalid</c> when the destination registry rejects the world's lots or the content (TD-079 item 5).</exception>
    public MailSystem(
        EventLogFacade events,
        MailIds mailIds,
        IRngStreams rng,
        ITickReader tick,
        IInventoryApi inventory,
        IWalletGate wallet,
        IWorldQuery world,
        ContentSet content,
        IStatSheet stats,
        SimCheckRing checkRing,
        SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(mailIds);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(checkRing);
        ArgumentNullException.ThrowIfNull(counters);
        _events = events;
        _rng = rng;
        _tick = tick;
        _inventory = inventory;
        _wallet = wallet;
        _stats = stats;
        _counters = counters;
        _table = new MailTable(content.Settings, mailIds, tick, checkRing);
        _registry = BuildRegistry(world, content);
    }

    /// <summary>The wallet port this module was given (tests: the composition root's injection).</summary>
    internal IWalletGate WalletGate => _wallet;

    /// <summary>The destination registry built at construction.</summary>
    internal DestinationRegistry Registry => _registry;

    /// <summary>The mail table.</summary>
    internal MailTable Table => _table;

    /// <inheritdoc />
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Bind(PhaseId.MailStep, new MailStepPhase(this));

        // The dump section moved here from MailTable.BindDump (TD-074 item 4).
        builder.BindStateContributor(new MailStateContributor(_table, _registry));
    }

    /// <inheritdoc />
    public void ConsumeHand(EntityId dest, ReadOnlySpan<EntryRef> inScanOrder, PlayerSlot actor, uint holdStartTick)
        => SimAssert.Fail(SimAssertCode.MailApiNotImplemented); // story 007

    /// <inheritdoc />
    public void SetDeliveriesState(bool open, uint closeTick, ulong unlockedDistrictMask)
        => SimAssert.Fail(SimAssertCode.MailApiNotImplemented); // story 009

    /// <inheritdoc />
    public bool TryGetDestinationFacts(EntityId dest, out DestinationFacts f) => _registry.TryGetDestinationFacts(dest, out f);

    /// <summary>
    /// Phase 8 (ADR-0017 section 7; M-PERF-1c): updates every feeder credit and touches nothing else when idle, so an idle tick visits
    /// no mail row and no complaint bucket (<see cref="CounterId.MailRowsVisited"/>, <see cref="CounterId.MailBucketsVisited"/>).
    /// 0 B.
    /// </summary>
    internal void Step()
    {
        int[] credits = _feederCredits;
        for (int i = 0; i < credits.Length; i++)
        {
            // F11 (test, add insertRateMilli, cap at 30,000) lands with the M-1b feeders; M-1a has none.
        }
    }

    private static DestinationRegistry BuildRegistry(IWorldQuery world, ContentSet content)
    {
        try
        {
            return new DestinationRegistry(world, content);
        }
        catch (ArgumentException e) when (e is not ArgumentNullException)
        {
            throw new SimBuildException(SimBuildErrorCode.MailRegistryInvalid, "Mail & Destinations: " + e.Message);
        }
    }
}

/// <summary>Mail's <c>MailStep</c> phase (phase 8, after <c>AgentsStep</c>; ADR-0003 section 1): <see cref="MailSystem.Step"/>.</summary>
internal sealed class MailStepPhase : ISimPhase
{
    private readonly MailSystem _system;

    public MailStepPhase(MailSystem system)
    {
        _system = system;
    }

    public void Run(in TickContext ctx) => _system.Step();
}
