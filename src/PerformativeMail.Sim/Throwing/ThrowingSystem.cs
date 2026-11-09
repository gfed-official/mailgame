using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing &amp; Catching's host-side module (GDD <c>design/gdd/throwing-catching.md</c> T4-T6; ADR-0015 sections 1, 4, 5 and 9;
/// throwing-catching stories 008, 010, 011 and 012). It holds the live flights in creation order (<see cref="FlightTable"/>, sized by
/// <see cref="ThrowSettings.MaxFlights"/>), the per-slot cooldown clock <c>Tat</c> (absolute tick, written by the release commit, cleared on
/// every join and leave of the slot), the always-on tick-end conservation record and the canonical dump section. It binds the
/// <c>ThrowRelease</c> handler (<see cref="ThrowReleaseHandler"/>, phase 0, story 010), <see cref="PhaseId.ThrowingStep"/>
/// (<see cref="ThrowingStep"/>, story 011), the join listener, the tick-end check and the state contributor, and declares the
/// <c>ThrowLaunched</c> and <c>FlightResolved</c> event kinds (story 006). No RNG stream. Allocation-free once constructed. Not
/// thread-safe: used on the Sim tick thread.
/// </summary>
/// <example>
/// <code>
/// // Composition: new ThrowingSystem(events, slots, inventory, mail, players, world, content, stats, ids, checkRing, counters)
/// int inAir = throwing.LiveFlights;
/// </code>
/// </example>
[SimSystem(SystemId.Throwing)]
public sealed class ThrowingSystem : ISimSystem, IThrowingApi, IPlayerJoinListener
{
    private readonly SimCheckRing _checks;
    private readonly SimCounters _counters;
    private readonly uint[] _tat = new uint[PlayerSlot.Count];
    private readonly ThrowReleaseHandler _release;
    private readonly ThrowingStep _step;

    /// <summary>
    /// The composition constructor (the one public constructor; ADR-0003 section 4): the event-log writer, the slot view, Grid
    /// Inventory's, Mail's and Players' APIs, the world's read interface, the content set (its <c>throw.*</c> record builds
    /// <see cref="ThrowSettings"/> and the ranks; <c>player.claimToleranceMm</c>), the StatSheet, the id allocator, and the world's check
    /// ring and counters. The selected hotbar slot is <see cref="PlayersHotbarSelection"/> over Players' API (the <c>HotbarSelect</c> op).
    /// </summary>
    /// <param name="events">The event-log writer facade.</param>
    /// <param name="slots">The read-only slot table.</param>
    /// <param name="inventory">Grid Inventory's API.</param>
    /// <param name="mail">Mail &amp; Destinations' API.</param>
    /// <param name="players">Player Character's API.</param>
    /// <param name="world">The world's read interface.</param>
    /// <param name="content">The content set.</param>
    /// <param name="stats">The world's StatSheet.</param>
    /// <param name="ids">The world's id allocator (read only).</param>
    /// <param name="checks">The world's always-on check ring.</param>
    /// <param name="counters">The world's always-on counters.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ThrowingSystem(
        EventLogFacade events,
        ISlotView slots,
        IInventoryApi inventory,
        IMailApi mail,
        IPlayersApi players,
        IWorldQuery world,
        ContentSet content,
        IStatSheet stats,
        EntityIds ids,
        SimCheckRing checks,
        SimCounters counters)
        : this(
            events,
            slots,
            inventory,
            mail,
            players,
            world,
            new ThrowSettings((content ?? throw new ArgumentNullException(nameof(content))).Throw),
            new ReceiverRanks(content.Throw),
            content.Player.ClaimToleranceMm,
            stats,
            ids,
            checks,
            counters)
    {
    }

    /// <summary>Creates the module with ready settings and optional test seams for the hotbar selection and mailbox ports.</summary>
    /// <param name="events">The event-log writer facade.</param>
    /// <param name="slots">The read-only slot table.</param>
    /// <param name="inventory">Grid Inventory's API.</param>
    /// <param name="mail">Mail &amp; Destinations' API.</param>
    /// <param name="players">Player Character's API.</param>
    /// <param name="world">The world's read interface.</param>
    /// <param name="settings">The settings (<see cref="ThrowSettings.MaxFlights"/> sizes the flight table).</param>
    /// <param name="ranks">The receiver rank table.</param>
    /// <param name="claimToleranceMm">The release claim tolerance, mm.</param>
    /// <param name="stats">The world's StatSheet.</param>
    /// <param name="ids">The world's id allocator (read only).</param>
    /// <param name="checks">The world's always-on check ring.</param>
    /// <param name="counters">The world's always-on counters.</param>
    /// <param name="hotbar">The selected hotbar slot; null is <see cref="PlayersHotbarSelection"/> over <paramref name="players"/>.</param>
    /// <param name="mailbox">The mailbox port; null is <see cref="MailboxLanding"/> over <paramref name="mail"/> (story 012).</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    internal ThrowingSystem(
        EventLogFacade events,
        ISlotView slots,
        IInventoryApi inventory,
        IMailApi mail,
        IPlayersApi players,
        IWorldQuery world,
        ThrowSettings settings,
        ReceiverRanks ranks,
        int claimToleranceMm,
        IStatSheet stats,
        EntityIds ids,
        SimCheckRing checks,
        SimCounters counters,
        IHotbarSelection? hotbar = null,
        IMailboxLanding? mailbox = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(counters);
        _checks = checks;
        _counters = counters;
        Settings = settings;
        Flights = new FlightTable(settings.MaxFlights, counters);
        _release = new ThrowReleaseHandler(new ThrowReleaseDeps
        {
            Events = events,
            Slots = slots,
            Inventory = inventory,
            Mail = mail,
            Players = players,
            Stats = stats,
            Hotbar = hotbar ?? new PlayersHotbarSelection(players),
            Corroborator = new PlayersClaimCorroborator(players),
            ClaimToleranceMm = claimToleranceMm,
            ChestProbe = new ChestOriginProbe(world),
            Settings = settings,
            Flights = Flights,
            Tat = _tat,
            Checks = checks,
            Counters = counters,
        });
        var resolver = new TerminalResolver(
            events,
            slots,
            inventory,
            world,
            ids,
            Flights,
            ranks,
            checks,
            counters,
            mailbox ?? new MailboxLanding(events, slots, inventory, mail, world, ids, Flights, settings, checks, counters));

        _step = new ThrowingStep(Flights, settings, new ReceiverViewBuilder(world, new MailCatchVolumeSource(mail), counters), ranks, resolver);
    }

    /// <summary>The settings this module reads every constant from.</summary>
    public ThrowSettings Settings { get; }

    /// <inheritdoc />
    public int LiveFlights => Flights.LiveFlights;

    /// <summary>The live flights in creation order (story 010 appends, story 011 steps and resolves them).</summary>
    internal FlightTable Flights { get; }

    /// <inheritdoc />
    public void FlushAllAsTimeout()
    {
        // Story 013 writes the flush.
    }

    /// <summary>
    /// Binds the <c>ThrowRelease</c> handler (story 010), phase 4 (story 011), the join/leave listener, the tick-end check and the dump
    /// section, and declares the <c>ThrowLaunched</c> and <c>FlightResolved</c> event kinds (story 006).
    /// </summary>
    /// <param name="builder">The world's builder.</param>
    /// <exception cref="SimBuildException">
    /// <c>PlayerJoinListenerAlreadyBound</c>, <c>DuplicateSection</c>, <c>DuplicateEventKind</c> or <c>DuplicateOpHandler</c>.
    /// </exception>
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.BindPlayerJoinListener(this);
        builder.BindOp(ThrowDescriptors.ThrowRelease, _release);
        builder.Bind(PhaseId.ThrowingStep, _step);
        builder.BindTickEnd(new ThrowingTickEndCheck(Flights, _checks, _counters));
        builder.BindStateContributor(new ThrowingStateContributor(Flights, _tat));
        builder.BindEventKind(ThrowEventDescriptors.ThrowLaunched);
        builder.BindEventKind(ThrowEventDescriptors.FlightResolved);
    }

    /// <summary>The slot's leaky-bucket clock <c>Tat</c> (ADR-0015 section 1; the release commit writes <c>tat'</c>, story 028 the bucket).</summary>
    /// <param name="slot">The slot (not none).</param>
    /// <returns>A reference into the module's fixed slot array.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is <see cref="PlayerSlot.None"/>.</exception>
    internal ref uint Tat(PlayerSlot slot)
    {
        if (slot.IsNone)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        return ref _tat[slot.Value];
    }

    /// <summary>Every slot's <c>Tat</c>, read-only, indexed by slot value (the <c>ThrowRelease</c> validator's <see cref="ThrowHostFacts.Tat"/>, story 009).</summary>
    internal ReadOnlySpan<uint> TatClocks => _tat;

    /// <inheritdoc />
    void IPlayerJoinListener.OnPlayerJoined(PlayerSlot slot, EntityId player, uint tick) => _tat[slot.Value] = 0;

    /// <inheritdoc />
    void IPlayerJoinListener.OnPlayerLeft(PlayerSlot slot, EntityId player) => _tat[slot.Value] = 0;
}
