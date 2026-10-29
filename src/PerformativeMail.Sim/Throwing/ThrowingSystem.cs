using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing &amp; Catching's host-side module shell (GDD <c>design/gdd/throwing-catching.md</c> T4-T6; ADR-0015 sections 5 and 9;
/// throwing-catching story 008). It holds the live flights in creation order (<see cref="FlightTable"/>, sized by
/// <see cref="ThrowSettings.MaxFlights"/>), the per-slot cooldown clock <c>Tat</c> (absolute tick, 0 until story 009 writes it,
/// cleared on every join and leave of the slot), the always-on tick-end conservation record and the canonical dump section. It binds
/// the join listener, <see cref="PhaseId.ThrowingStep"/> (empty here; story 011 steps the flights), the tick-end check and the state
/// contributor, and no op or event (stories 006 and 010). No RNG stream. Allocation-free once constructed. Not thread-safe: used on
/// the Sim tick thread.
/// </summary>
/// <example>
/// <code>
/// // Composition: new ThrowingSystem(events, slots, inventory, mail, players, world, content.Throw, checkRing, counters)
/// int inAir = throwing.LiveFlights;   // 0 until story 010 launches
/// </code>
/// </example>
[SimSystem(SystemId.Throwing)]
public sealed class ThrowingSystem : ISimSystem, IThrowingApi, IPlayerJoinListener
{
    private readonly EventLogFacade _events;
    private readonly ISlotView _slots;
    private readonly IInventoryApi _inventory;
    private readonly IMailApi _mail;
    private readonly IPlayersApi _players;
    private readonly IWorldQuery _world;
    private readonly SimCheckRing _checks;
    private readonly SimCounters _counters;
    private readonly uint[] _tat = new uint[PlayerSlot.Count];

    /// <summary>
    /// The composition constructor (the one public constructor; ADR-0003 section 4): the event-log writer, the slot view, Grid
    /// Inventory's, Mail's and Players' APIs, the world's read interface, the content's <c>throw.*</c> record (the constructor builds
    /// <see cref="ThrowSettings"/> from it), and the world's check ring and counters.
    /// </summary>
    /// <param name="events">The event-log writer facade.</param>
    /// <param name="slots">The read-only slot table.</param>
    /// <param name="inventory">Grid Inventory's API.</param>
    /// <param name="mail">Mail &amp; Destinations' API.</param>
    /// <param name="players">Player Character's API.</param>
    /// <param name="world">The world's read interface.</param>
    /// <param name="balance">The content set's <c>throw.*</c> record (<c>ContentSet.Throw</c>).</param>
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
        ThrowBalance balance,
        SimCheckRing checks,
        SimCounters counters)
        : this(events, slots, inventory, mail, players, world, new ThrowSettings(balance), checks, counters)
    {
    }

    /// <summary>Creates the module with ready settings (tests: a smaller <see cref="ThrowSettings.MaxFlights"/>).</summary>
    /// <param name="events">The event-log writer facade.</param>
    /// <param name="slots">The read-only slot table.</param>
    /// <param name="inventory">Grid Inventory's API.</param>
    /// <param name="mail">Mail &amp; Destinations' API.</param>
    /// <param name="players">Player Character's API.</param>
    /// <param name="world">The world's read interface.</param>
    /// <param name="settings">The settings (<see cref="ThrowSettings.MaxFlights"/> sizes the flight table).</param>
    /// <param name="checks">The world's always-on check ring.</param>
    /// <param name="counters">The world's always-on counters.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal ThrowingSystem(
        EventLogFacade events,
        ISlotView slots,
        IInventoryApi inventory,
        IMailApi mail,
        IPlayersApi players,
        IWorldQuery world,
        ThrowSettings settings,
        SimCheckRing checks,
        SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(counters);
        _events = events;
        _slots = slots;
        _inventory = inventory;
        _mail = mail;
        _players = players;
        _world = world;
        _checks = checks;
        _counters = counters;
        Settings = settings;
        Flights = new FlightTable(settings.MaxFlights, counters);
    }

    /// <summary>The settings this module reads every constant from.</summary>
    public ThrowSettings Settings { get; }

    /// <inheritdoc />
    public int LiveFlights => Flights.LiveFlights;

    /// <summary>The live flights in creation order (stories 009-013 append, step and resolve them).</summary>
    internal FlightTable Flights { get; }

    /// <inheritdoc />
    public void FlushAllAsTimeout()
    {
        // Story 013 writes the flush; until then nothing is in the air to end.
    }

    /// <summary>Binds the join/leave listener, phase 4 (an empty step until story 011), the tick-end check and the dump section.</summary>
    /// <param name="builder">The world's builder.</param>
    /// <exception cref="SimBuildException"><c>PlayerJoinListenerAlreadyBound</c> or <c>DuplicateSection</c>.</exception>
    public void Bind(SimWorldBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.BindPlayerJoinListener(this);
        builder.Bind(PhaseId.ThrowingStep, new ThrowingStep());
        builder.BindTickEnd(new ThrowingTickEndCheck(Flights, _checks, _counters));
        builder.BindStateContributor(new ThrowingStateContributor(Flights, _tat));
    }

    /// <summary>The slot's leaky-bucket clock <c>Tat</c> (ADR-0015 section 1; story 009 reads and writes it).</summary>
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

    /// <inheritdoc />
    void IPlayerJoinListener.OnPlayerJoined(PlayerSlot slot, EntityId player, uint tick) => _tat[slot.Value] = 0;

    /// <inheritdoc />
    void IPlayerJoinListener.OnPlayerLeft(PlayerSlot slot, EntityId player) => _tat[slot.Value] = 0;

    /// <summary>Phase 4, <c>Throwing.Step</c>: nothing to step until story 011 sweeps the flights.</summary>
    private sealed class ThrowingStep : ISimPhase
    {
        public void Run(in TickContext ctx)
        {
        }
    }
}
