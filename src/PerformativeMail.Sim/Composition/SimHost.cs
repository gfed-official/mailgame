using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Economy;

namespace PerformativeMail.Sim.Composition;

/// <summary>
/// What <see cref="SimComposition.Build"/> returns (ADR-0003 section 4): the authoritative <see cref="World"/> and the
/// <see cref="Client"/> layer composed alongside it. The two hold no reference to each other (ADR-0003 section 6); only this
/// record holds both. Not thread-safe: use on the thread that called <c>Build</c>.
/// </summary>
/// <example>
/// <code>
/// SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, content, seed));
/// host.World.Tick();
/// host.Client.Step(isFirstTickInBatch: true);
/// </code>
/// </example>
public sealed class SimHost
{
    internal SimHost(SimWorld world, ClientLayer.ClientLayer client, ContentSet content, SimCoreServices services, SystemId[] order, ISimSystem[] systems, StubWallet? wallet = null)
    {
        Wallet = wallet;
        World = world;
        Client = client;
        Content = content;
        Services = services;
        Order = order;
        Systems = systems;
    }

    /// <summary>The authoritative world, <see cref="SimState.Ready"/> when returned.</summary>
    public SimWorld World { get; }

    /// <summary>The client layer (client tick 0 when returned).</summary>
    public ClientLayer.ClientLayer Client { get; }

    /// <summary>The content set the world's systems were built with: the instance passed in <see cref="SimBuildArgs.Content"/>.</summary>
    public ContentSet Content { get; }

    /// <summary>The Sim Core services the composition created for the world (allocators, RNG registry, slot table, event facade).</summary>
    internal SimCoreServices Services { get; }

    /// <summary>The world's stat sheet: the one instance every reader of this world holds (created in <see cref="SimComposition.Build"/>, sized from <c>SimSettings.Stats</c>).</summary>
    public IStatSheet StatSheet => Services.Stats.Sheet;

    /// <summary>The profile's construction order (including systems whose construction code does not exist yet).</summary>
    internal SystemId[] Order { get; }

    /// <summary>The systems actually constructed, in construction order.</summary>
    internal ISimSystem[] Systems { get; }

    /// <summary>The M-1a wallet the composition built for Mail &amp; Destinations (null when the profile does not include Mail).</summary>
    internal StubWallet? Wallet { get; }
}

/// <summary>The Sim Core services <see cref="SimComposition.Build"/> creates for one world (test and dump access).</summary>
/// <param name="EntityIds">The entity id allocator.</param>
/// <param name="MailIds">The mail id allocator.</param>
/// <param name="Rng">The RNG registry seeded with the run seed.</param>
/// <param name="Slots">The player slot table.</param>
/// <param name="Events">The event-log writer facade handed to systems.</param>
/// <param name="Counters">The world's counters.</param>
/// <param name="CheckRing">The world's check ring.</param>
internal sealed record SimCoreServices(
    EntityIds EntityIds,
    MailIds MailIds,
    RngRegistry Rng,
    SlotTable Slots,
    EventLogFacade Events,
    SimCounters Counters,
    SimCheckRing CheckRing,
    StatSheetWriter Stats);
