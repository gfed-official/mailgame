using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Composition;

/// <summary>
/// The one composition root (ADR-0003 section 4, TR-sim-013). <see cref="Build"/> creates the Sim Core services, constructs
/// the profile's systems with plain <c>new</c> in an explicit order that is a topological order of <see cref="SystemGraph"/>,
/// lets each bind, composes the <see cref="ClientLayer"/> alongside, and freezes the world on the calling thread. No DI
/// container, no service locator, no reflection, no <c>Activator</c>, no static state.
/// <para>
/// Composition order = construction order = same-phase binder order = tick-end order = dump-section order.
/// </para>
/// <para>
/// Must be called on the thread that will call <see cref="SimWorld.Tick"/> (the Godot main thread): the world's owner thread
/// and the id allocators' owner thread are both captured here. Errors are <see cref="SimBuildException"/>s, thrown only here,
/// at boot. Allocates freely (boot-time only).
/// </para>
/// </summary>
/// <example>
/// <code>
/// ContentSet content = BuiltInContent.CreateM1a();          // once per process
/// SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, content, runSeed));
/// host.World.Tick();
/// </code>
/// </example>
public static class SimComposition
{
    /// <summary>
    /// The explicit construction order of the shipped systems: a topological order of <see cref="SystemGraph.Shipped"/>
    /// (checked by a test). A shipped profile is built in this order, restricted to the systems it selects.
    /// </summary>
    public static ReadOnlySpan<SystemId> ShippedOrder =>
    [
        SystemId.WorldGeneration,
        SystemId.HealthDamage,
        SystemId.GridInventory,
        SystemId.PlayerCharacter,
        SystemId.MailDestinations,
        SystemId.Building,
        SystemId.BeltLogistics,
        SystemId.ManualDelivery,
        SystemId.Throwing,
        SystemId.Vehicles,
        SystemId.PlayerCombat,
        SystemId.EnemyAiWaves,
        SystemId.Defenses,
        SystemId.ResourceHarvesting,
        SystemId.Economy,
        SystemId.RunStructure,
        SystemId.Perks,
        SystemId.StampsRunEvents,
    ];

    /// <summary>
    /// The order in which <see cref="Build"/> constructs <paramref name="profile"/>'s systems: <see cref="ShippedOrder"/> filtered
    /// to the profile's selection for a shipped profile; the given order for a per-test profile.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <returns>A new array.</returns>
    /// <example><code>SystemId[] order = SimComposition.ConstructionOrder(SimProfile.M1aGreybox);</code></example>
    public static SystemId[] ConstructionOrder(SimProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var order = new SystemId[profile.Count];
        if (profile.IsPerTest)
        {
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = profile[i];
            }

            return order;
        }

        int n = 0;
        ReadOnlySpan<SystemId> shipped = ShippedOrder;
        for (int i = 0; i < shipped.Length; i++)
        {
            if (profile.Includes(shipped[i]))
            {
                order[n++] = shipped[i];
            }
        }

        return n == order.Length ? order : order[..n];
    }

    /// <summary>
    /// Builds one world and its client layer. Order: validate the profile (<c>DuplicateSystem</c>, <c>DependencyCycle</c>); create
    /// <see cref="SimCounters"/>, <see cref="SimCheckRing"/>, the <see cref="SimWorldBuilder"/>, <see cref="EntityIds"/>,
    /// <see cref="MailIds"/>, <see cref="SlotTable"/>, <see cref="RngRegistry"/> (seeded with <see cref="SimBuildArgs.RunSeed"/>),
    /// the <see cref="EventLogFacade"/> and the <see cref="ITickReader"/>; bind them as Sim Core state; build and bind the world
    /// tables (<see cref="BindWorld"/>); when the profile includes Mail &amp; Destinations, construct and bind the M-1a
    /// <see cref="StubWallet"/> (<see cref="BindWallet"/>); construct and bind each system in <see cref="ConstructionOrder"/>; build the <see cref="ClientLayer"/>; <see cref="SimWorldBuilder.Freeze"/>.
    /// </summary>
    /// <param name="args">Profile, content, run seed, optional counters and host clock.</param>
    /// <returns>The host: a <see cref="SimState.Ready"/> world at tick 0 and a client layer at client tick 0.</returns>
    /// <exception cref="ArgumentException"><see cref="SimBuildArgs.Profile"/> or <see cref="SimBuildArgs.Content"/> is null (for example <c>default(SimBuildArgs)</c>); both <see cref="SimBuildArgs.World"/> and <see cref="SimBuildArgs.WorldSource"/> are set.</exception>
    /// <exception cref="SimBuildException"><c>DuplicateSystem</c>, <c>DependencyCycle</c>, <c>UndeclaredDependency</c>, or any builder code raised by a system's binds.</exception>
    /// <exception cref="InvalidOperationException">A per-test factory returned null.</exception>
    /// <example><code>SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, content, 42));</code></example>
    public static SimHost Build(in SimBuildArgs args)
    {
        SimProfile profile = args.Profile ?? throw new ArgumentException("SimBuildArgs.Profile is null.", nameof(args));
        ContentSet content = args.Content ?? throw new ArgumentException("SimBuildArgs.Content is null.", nameof(args));
        SystemId[] order = ConstructionOrder(profile);
        ThrowIfDuplicate(order);
        ThrowIfCyclic(profile.Graph);

        // Sim Core services, all per world (no statics).
        SimCoreSettings settings = content.Settings.Core;
        SimInitialCounters initial = args.Counters ?? new SimInitialCounters();
        var counters = new SimCounters();
        var ring = new SimCheckRing(settings.CheckRingCapacity);
        var builder = new SimWorldBuilder(settings, counters, ring, probe: null, initialCounters: initial);
        var tick = new WorldTickForwarder();
        var services = new SimCoreServices(
            new EntityIds(tick, ring, counters, initial.EntityCountersSpan, allowTestClasses: false, state: tick),
            new MailIds(tick, ring, counters, initial.LastMailId),
            new RngRegistry(args.RunSeed, counters),
            new SlotTable(),
            new EventLogFacade(counters),
            counters,
            ring,
            new StatSheetWriter(content, counters));
        var runControl = new RunControlForwarder();
        builder.BindCoreState(services.EntityIds, services.MailIds, services.Rng, services.Slots);
        if (args.EventSink is { } eventSink)
        {
            builder.BindEventSink(eventSink);
        }

        if (args.Recorder is { } recorder)
        {
            builder.BindRecorder(recorder);
        }

        // The world tables (ADR-0026 section 9): built while Constructed, bound before the systems, injected by constructor only.
        IWorldQuery? worldQuery = BindWorld(in args, builder, services.EntityIds, counters, content);

        // The wallet port's implementation (ADR-0017 section 8 erratum 2026-10-28): after the world, before the systems; not a system.
        StubWallet? wallet = BindWallet(profile, builder, services, content);

        // Systems, in construction order; each binds right after construction.
        var context = new SimSystemContext(profile.Graph, content, services, tick, runControl, worldQuery, wallet);
        var systems = new List<ISimSystem>(order.Length);
        for (int i = 0; i < order.Length; i++)
        {
            context.Begin(order[i]);
            ISimSystem? system = profile.IsPerTest
                ? profile.FactoryAt(i)(context) ?? throw new InvalidOperationException($"The factory for {order[i]} returned null.")
                : ConstructShipped(order[i], context);
            if (system is null)
            {
                continue; // a shipped system whose epic has not landed yet: its phases stay no-ops
            }

            context.Built(order[i], system);
            system.Bind(builder);
            systems.Add(system);
        }

        context.Close();

        // The client layer: its own counters and the adapter's clock; no reference to the world.
        ClientLayer.ClientLayer client = new ClientLayerBuilder(args.HostClock ?? new UnsyncedHostClock(), new SimCounters()).Build();

        // Freeze on the calling thread (owner thread), then point the late-bound ports at the world.
        SimWorld world = builder.Freeze();
        tick.Attach(world);
        services.Events.Attach(world.Events);
        runControl.Attach(world.RunControl);
        return new SimHost(world, client, content, services, order, systems.ToArray(), wallet);
    }

    // The explicit construction code of the shipped systems, one case per system in ShippedOrder. Each gameplay epic adds its
    // case here (and its row in SystemGraph). A system whose epic has not landed yet returns null and its phases stay no-ops
    // (GridInventory, PlayerCharacter and MailDestinations are built).
    private static ISimSystem? ConstructShipped(SystemId id, SimSystemContext context)
    {
        switch (id)
        {
            case SystemId.PlayerCharacter:
                // Players reads the world only through IWorldQuery; without one the system is left unbuilt (story player-character/001).
                return context.World is { } world ? new PlayersSystem(world, context.Content, context.CheckRing, context.Counters) : null;
            case SystemId.GridInventory:
                return new InventorySystem(context.Content, context.StatSheet, context.CheckRing, context.Events, context.Tick, context.EntityIds, context.Slots);
            case SystemId.MailDestinations:
                // Mail builds its destination registry from IWorldQuery; without one the system is left unbuilt, like Players.
                return context.World is { } mailWorld
                    ? new MailSystem(
                        context.Events,
                        context.MailIds,
                        context.Rng,
                        context.Tick,
                        context.Api<IInventoryApi>(SystemId.GridInventory),
                        context.WalletGate,
                        mailWorld,
                        context.Content,
                        context.StatSheet,
                        context.CheckRing,
                        context.Counters)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>
    /// The world step of <see cref="Build"/> (ADR-0026 section 9, story world-stub/007). With a <see cref="SimBuildArgs.WorldSource"/>
    /// the tables are authored and built here, while the world is Constructed (destination ids are allocated from the world's own
    /// <see cref="EntityIds"/>), with the Sim <see cref="SimCounters"/> bound for solid-query overflow counts, and bound as the first
    /// contributor after Sim Core so the World section's place in the dump is stable. A supplied <see cref="SimBuildArgs.World"/>
    /// that is a <see cref="WorldTables"/> is bound as a contributor too (its replay identity is 0: see <see cref="SimBuildArgs.World"/>).
    /// </summary>
    /// <exception cref="ArgumentException">Both <see cref="SimBuildArgs.World"/> and <see cref="SimBuildArgs.WorldSource"/> are set.</exception>
    private static IWorldQuery? BindWorld(in SimBuildArgs args, SimWorldBuilder builder, EntityIds ids, SimCounters counters, ContentSet content)
    {
        if (args.WorldSource is not { } worldSource)
        {
            if (args.World is WorldTables suppliedTables)
            {
                builder.BindStateContributor(suppliedTables);
            }

            return args.World;
        }

        if (args.World is not null)
        {
            throw new ArgumentException("SimBuildArgs.World and SimBuildArgs.WorldSource are mutually exclusive.", nameof(args));
        }

        var worldBuilder = new WorldTablesBuilder();
        worldBuilder.SetCounters(counters);
        worldSource.Author(worldBuilder);
        WorldTables tables = worldBuilder.Build(ids, content.Player.SlopeMaxPm, content.Player.SwimEnterMm);
        builder.BindStateContributor(tables);
        return tables;
    }

    /// <summary>
    /// The wallet step of <see cref="Build"/> (ADR-0017 section 8 and ADR-0003 section 4 errata 2026-10-28; mail-destinations story 005):
    /// the M-1a <see cref="StubWallet"/>, from <c>sim.startingWalletCents</c>, bound after the World section and before the systems' sections
    /// (section order SimCore, World, Wallet, then the systems), and injected into Mail only (<see cref="SimSystemContext.WalletGate"/>).
    /// Built only for a profile that includes Mail &amp; Destinations, its only consumer, so other profiles' dumps carry no Wallet section
    /// (Evan approved 2026-10-28 (TD-183 item 1)).
    /// </summary>
    private static StubWallet? BindWallet(SimProfile profile, SimWorldBuilder builder, SimCoreServices services, ContentSet content)
    {
        if (!profile.Includes(SystemId.MailDestinations))
        {
            return null;
        }

        // Built (with its Wallet section) for any profile with Mail, even when no world is bound and Mail itself is not constructed (Evan approved 2026-10-28 (TD-183 item 1)).
        var wallet = new StubWallet(content.Settings.StartingWalletCents, services.Events, services.Slots, services.Counters);
        wallet.Bind(builder);
        return wallet;
    }

    private static void ThrowIfDuplicate(SystemId[] order)
    {
        var seen = new bool[256];
        for (int i = 0; i < order.Length; i++)
        {
            if (seen[(int)order[i]])
            {
                throw new SimBuildException(SimBuildErrorCode.DuplicateSystem, $"System {order[i]} is composed twice.");
            }

            seen[(int)order[i]] = true;
        }
    }

    private static void ThrowIfCyclic(SystemGraph graph)
    {
        if (graph.TryFindCycle(out SystemId member))
        {
            throw new SimBuildException(SimBuildErrorCode.DependencyCycle, $"SystemGraph has a dependency cycle through {member}.");
        }
    }
}
