using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Composition;

/// <summary>
/// Which systems <see cref="SimComposition.Build"/> constructs (ADR-0003 section 4) and against which
/// <see cref="SystemGraph"/>. The shipped profiles (<see cref="M1aGreybox"/>, <see cref="Full"/>) select a set of
/// <see cref="SystemId"/>s; they are built in <see cref="SimComposition.ShippedOrder"/> by explicit code in
/// <see cref="SimComposition"/>. A per-test profile (<see cref="ForTest"/>) carries its own ordered systems, each with a
/// factory that calls <c>new</c>, and may carry its own graph. An unbuilt system's phases are simply not bound (no-ops).
/// Immutable.
/// </summary>
/// <example>
/// <code>
/// SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, content, seed));
/// bool hasMail = SimProfile.M1aGreybox.Includes(SystemId.MailDestinations);   // true
/// </code>
/// </example>
public sealed class SimProfile
{
    private readonly SystemId[] _systems;
    private readonly SimSystemFactory[]? _factories;

    private SimProfile(string name, SystemId[] systems, SimSystemFactory[]? factories, SystemGraph graph)
    {
        Name = name;
        _systems = systems;
        _factories = factories;
        Graph = graph;
    }

    /// <summary>
    /// The M-1a greybox: Grid Inventory, Player Character, Throwing, Mail &amp; Destinations, Manual Delivery and the Run Structure
    /// stub. Its systems' construction code is added by their epics; until then it builds Sim Core services only and ticks.
    /// </summary>
    public static SimProfile M1aGreybox => new(
        "M1aGreybox",
        [SystemId.GridInventory, SystemId.PlayerCharacter, SystemId.MailDestinations, SystemId.ManualDelivery, SystemId.Throwing, SystemId.RunStructure],
        null,
        SystemGraph.Shipped);

    /// <summary>Every shipped Sim system, in <see cref="SimComposition.ShippedOrder"/>.</summary>
    public static SimProfile Full => new("Full", SimComposition.ShippedOrder.ToArray(), null, SystemGraph.Shipped);

    /// <summary>The profile's name (diagnostics only).</summary>
    public string Name { get; }

    /// <summary>The dependency graph the profile is validated and resolved against.</summary>
    public SystemGraph Graph { get; }

    /// <summary>True for a per-test profile (its systems come with factories and are built in the given order).</summary>
    public bool IsPerTest => _factories is not null;

    /// <summary>Number of systems the profile selects (a per-test profile may list one id twice; <c>Build</c> rejects that).</summary>
    public int Count => _systems.Length;

    /// <summary>The <paramref name="index"/>-th selected system (selection order; for a per-test profile, construction order).</summary>
    /// <param name="index">0 to <see cref="Count"/> - 1.</param>
    public SystemId this[int index] => _systems[index];

    /// <summary>True when the profile selects <paramref name="id"/>.</summary>
    /// <param name="id">The system.</param>
    /// <returns>True when selected.</returns>
    /// <example><code>bool built = profile.Includes(SystemId.Throwing);</code></example>
    public bool Includes(SystemId id) => Array.IndexOf(_systems, id) >= 0;

    /// <summary>
    /// A per-test profile: the systems are constructed in the given order by their factories (plain <c>new</c> only) and resolved
    /// against <paramref name="graph"/> (default <see cref="SystemGraph.Shipped"/>). Duplicates and cycles are not rejected here:
    /// <see cref="SimComposition.Build"/> reports them as <see cref="SimBuildException"/>s.
    /// </summary>
    /// <param name="name">A name for diagnostics.</param>
    /// <param name="graph">The graph, or null for the shipped one.</param>
    /// <param name="systems">The systems, in construction order.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="ArgumentNullException">An argument or a factory is null.</exception>
    /// <example><code>var p = SimProfile.ForTest("probe", null, new SimSystemEntry(SystemId.GridInventory, ctx => new FakeGrid(ctx.Events)));</code></example>
    internal static SimProfile ForTest(string name, SystemGraph? graph, params SimSystemEntry[] systems)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(systems);
        var ids = new SystemId[systems.Length];
        var factories = new SimSystemFactory[systems.Length];
        for (int i = 0; i < systems.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(systems[i].Factory, nameof(systems));
            ids[i] = systems[i].Id;
            factories[i] = systems[i].Factory;
        }

        return new SimProfile(name, ids, factories, graph ?? SystemGraph.Shipped);
    }

    /// <summary>The factory of the <paramref name="index"/>-th system of a per-test profile.</summary>
    internal SimSystemFactory FactoryAt(int index) => _factories![index];
}

/// <summary>Constructs one system for a per-test profile. Must call <c>new</c> with parameters taken from the context only.</summary>
/// <param name="context">The composition context, valid only during the call.</param>
/// <returns>The constructed system (never null).</returns>
internal delegate ISimSystem SimSystemFactory(SimSystemContext context);

/// <summary>One system of a per-test profile.</summary>
/// <param name="Id">Its composition identity.</param>
/// <param name="Factory">Its constructor call.</param>
internal readonly record struct SimSystemEntry(SystemId Id, SimSystemFactory Factory);
