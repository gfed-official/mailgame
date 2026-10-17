using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Composition;

/// <summary>
/// Each Sim system's declared dependencies, as data (ADR-0003 section 4; systems-index dependency map, Sim systems only).
/// An edge <c>A → B</c> means "A may receive B's <c>I…Api</c>"; calls go down these edges and news goes up through events.
/// A dependency on a system that a profile does not build is allowed, but a built system that actually asks for its
/// <c>I…Api</c> fails <see cref="SimComposition.Build"/> with <c>UndeclaredDependency</c>.
/// <para>
/// The shipped table is a <c>switch</c> returning <see cref="ReadOnlySpan{T}"/> literals (<see cref="ShippedDependencies"/>):
/// no <c>static readonly T[]</c>, no mutable static state. Tests build other graphs (for example one with a cycle) with
/// <see cref="FromEdges"/>. Immutable once built; safe to share. Algorithms are boot-time only and may allocate.
/// </para>
/// </summary>
/// <example>
/// <code>
/// SystemGraph g = SystemGraph.Shipped;
/// bool ok = g.Declares(SystemId.ManualDelivery, SystemId.MailDestinations);   // true
/// bool acyclic = !g.TryFindCycle(out _);                                        // true
/// </code>
/// </example>
public sealed class SystemGraph
{
    private const int IdCount = 256;

    // Null for the shipped graph (read from the switch); else one row per SystemId value.
    private readonly SystemId[][]? _edges;

    private SystemGraph(SystemId[][]? edges)
    {
        _edges = edges;
    }

    /// <summary>The shipped dependency table (a fresh, equal object on every read).</summary>
    public static SystemGraph Shipped => new(null);

    /// <summary>True for the shipped table.</summary>
    public bool IsShipped => _edges is null;

    /// <summary>
    /// The shipped dependencies of <paramref name="id"/> (Sim systems only: Sim Core, Content &amp; Data, Input, Networking and UI
    /// are not composed systems). Source: <c>design/gdd/systems-index.md</c> "Depends On", restricted to Sim systems.
    /// </summary>
    /// <param name="id">The system.</param>
    /// <returns>Its declared dependencies (empty for a leaf or an undefined id).</returns>
    /// <example><code>ReadOnlySpan&lt;SystemId&gt; deps = SystemGraph.ShippedDependencies(SystemId.Throwing);</code></example>
    public static ReadOnlySpan<SystemId> ShippedDependencies(SystemId id) => id switch
    {
        SystemId.PlayerCharacter => [SystemId.WorldGeneration, SystemId.GridInventory, SystemId.HealthDamage],
        SystemId.MailDestinations => [SystemId.WorldGeneration, SystemId.GridInventory],
        SystemId.ManualDelivery => [SystemId.PlayerCharacter, SystemId.MailDestinations, SystemId.GridInventory],
        SystemId.Throwing => [SystemId.PlayerCharacter, SystemId.MailDestinations, SystemId.GridInventory, SystemId.BeltLogistics, SystemId.HealthDamage],
        SystemId.Building => [SystemId.WorldGeneration],
        SystemId.BeltLogistics => [SystemId.MailDestinations, SystemId.GridInventory, SystemId.Building, SystemId.PlayerCharacter, SystemId.WorldGeneration],
        SystemId.Vehicles => [SystemId.PlayerCharacter, SystemId.WorldGeneration, SystemId.MailDestinations, SystemId.GridInventory, SystemId.Building, SystemId.BeltLogistics, SystemId.HealthDamage],
        SystemId.PlayerCombat => [SystemId.PlayerCharacter, SystemId.HealthDamage],
        SystemId.EnemyAiWaves => [SystemId.WorldGeneration, SystemId.Building, SystemId.HealthDamage, SystemId.PlayerCharacter, SystemId.Vehicles],
        SystemId.Defenses => [SystemId.Building, SystemId.HealthDamage, SystemId.EnemyAiWaves],
        SystemId.ResourceHarvesting => [SystemId.WorldGeneration, SystemId.GridInventory, SystemId.PlayerCharacter],
        SystemId.Economy => [SystemId.MailDestinations, SystemId.GridInventory, SystemId.Building, SystemId.ResourceHarvesting],
        SystemId.RunStructure => [SystemId.WorldGeneration, SystemId.MailDestinations, SystemId.EnemyAiWaves, SystemId.Defenses, SystemId.Economy],
        SystemId.Perks => [SystemId.Economy, SystemId.RunStructure],
        SystemId.StampsRunEvents => [SystemId.MailDestinations, SystemId.EnemyAiWaves, SystemId.RunStructure],

        // WorldGeneration, HealthDamage, GridInventory: Sim Core and Content & Data only.
        _ => [],
    };

    /// <summary>
    /// Builds a graph from explicit rows (tests and tools). Ids not listed have no dependencies. The arrays are copied.
    /// </summary>
    /// <param name="rows">One row per system: the system and what it depends on.</param>
    /// <returns>The graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> or a row's dependency array is null.</exception>
    /// <exception cref="ArgumentException">A system has two rows.</exception>
    /// <example><code>var cyclic = SystemGraph.FromEdges((SystemId.Perks, new[] { SystemId.Economy }), (SystemId.Economy, new[] { SystemId.Perks }));</code></example>
    internal static SystemGraph FromEdges(params (SystemId System, SystemId[] DependsOn)[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var edges = new SystemId[IdCount][];
        for (int i = 0; i < IdCount; i++)
        {
            edges[i] = Array.Empty<SystemId>();
        }

        var seen = new bool[IdCount];
        for (int r = 0; r < rows.Length; r++)
        {
            (SystemId system, SystemId[] dependsOn) = rows[r];
            ArgumentNullException.ThrowIfNull(dependsOn, nameof(rows));
            if (seen[(int)system])
            {
                throw new ArgumentException($"System {system} has two rows.", nameof(rows));
            }

            seen[(int)system] = true;
            edges[(int)system] = (SystemId[])dependsOn.Clone();
        }

        return new SystemGraph(edges);
    }

    /// <summary>The declared dependencies of <paramref name="id"/>.</summary>
    /// <param name="id">The system.</param>
    /// <returns>Its dependencies, in declaration order.</returns>
    /// <example><code>foreach (SystemId d in graph.DependenciesOf(SystemId.Economy)) { }</code></example>
    public ReadOnlySpan<SystemId> DependenciesOf(SystemId id) => _edges is null ? ShippedDependencies(id) : _edges[(int)id];

    /// <summary>True when <paramref name="consumer"/> declares <paramref name="provider"/> as a dependency.</summary>
    /// <param name="consumer">The system asking.</param>
    /// <param name="provider">The system whose <c>I…Api</c> is asked for.</param>
    /// <returns>True when the edge exists.</returns>
    /// <example><code>bool may = graph.Declares(SystemId.Perks, SystemId.RunStructure);</code></example>
    public bool Declares(SystemId consumer, SystemId provider)
    {
        ReadOnlySpan<SystemId> deps = DependenciesOf(consumer);
        for (int i = 0; i < deps.Length; i++)
        {
            if (deps[i] == provider)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Looks for a dependency cycle anywhere in the graph (a self-dependency is a cycle).</summary>
    /// <param name="member">A system on the first cycle found (ascending id search); <see cref="SystemId.None"/> when acyclic.</param>
    /// <returns>True when the graph has a cycle.</returns>
    /// <example><code>if (graph.TryFindCycle(out SystemId at)) { throw new SimBuildException(SimBuildErrorCode.DependencyCycle, $"cycle at {at}"); }</code></example>
    public bool TryFindCycle(out SystemId member)
    {
        var state = new byte[IdCount]; // 0 = unvisited, 1 = on the current path, 2 = done
        for (int i = 0; i < IdCount; i++)
        {
            if (state[i] == 0 && Visit((SystemId)i, state, out member))
            {
                return true;
            }
        }

        member = SystemId.None;
        return false;
    }

    /// <summary>
    /// True when every system in <paramref name="order"/> comes after each of its declared dependencies that is also in
    /// <paramref name="order"/>, and no system appears twice. Dependencies outside <paramref name="order"/> (not built) are ignored.
    /// </summary>
    /// <param name="order">A construction order.</param>
    /// <param name="offender">The first system that breaks the rule; <see cref="SystemId.None"/> when the order is valid.</param>
    /// <returns>True when the order is a valid topological order.</returns>
    /// <example><code>bool ok = SystemGraph.Shipped.IsTopologicalOrder(SimComposition.ShippedOrder, out _);</code></example>
    public bool IsTopologicalOrder(ReadOnlySpan<SystemId> order, out SystemId offender)
    {
        var position = new int[IdCount];
        Array.Fill(position, -1);
        for (int i = 0; i < order.Length; i++)
        {
            if (position[(int)order[i]] >= 0)
            {
                offender = order[i];
                return false;
            }

            position[(int)order[i]] = i;
        }

        for (int i = 0; i < order.Length; i++)
        {
            ReadOnlySpan<SystemId> deps = DependenciesOf(order[i]);
            for (int d = 0; d < deps.Length; d++)
            {
                if (position[(int)deps[d]] >= i)
                {
                    offender = order[i];
                    return false;
                }
            }
        }

        offender = SystemId.None;
        return true;
    }

    private bool Visit(SystemId id, byte[] state, out SystemId member)
    {
        state[(int)id] = 1;
        ReadOnlySpan<SystemId> deps = DependenciesOf(id);
        for (int i = 0; i < deps.Length; i++)
        {
            int d = (int)deps[i];
            if (state[d] == 1)
            {
                member = deps[i];
                return true;
            }

            if (state[d] == 0 && Visit(deps[i], state, out member))
            {
                return true;
            }
        }

        state[(int)id] = 2;
        member = SystemId.None;
        return false;
    }
}
