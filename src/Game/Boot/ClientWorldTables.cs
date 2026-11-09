using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The client's own copy of the world tables (godot-host-shell story 005, for player-character 020's carrier env): authored from the same
/// <see cref="IWorldSource"/> and content limits as the host's, with a private id allocator that starts where a fresh world's does, so
/// the copy equals the host's tables (destination ids included) without the client ever reading the host world (ADR-0003 section 6,
/// ADR-0016 section 1). A remote client builds the same copy at M0. Boot-time only; allocates.
/// </summary>
/// <example><code>WorldTables clientWorld = ClientWorldTables.Build(M1aWorldSource.Instance, content);</code></example>
public static class ClientWorldTables
{
    /// <summary>Authors and builds the tables.</summary>
    /// <param name="source">The world source the host composes with.</param>
    /// <param name="content">The content set (the player's slope and swim limits).</param>
    /// <returns>Immutable tables equal to the host's.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="SimBuildException">The source does not build.</exception>
    public static WorldTables Build(IWorldSource source, ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(content);
        var counters = new SimCounters();
        var builder = new WorldTablesBuilder();
        builder.SetCounters(counters);
        source.Author(builder);
        var constructed = new ConstructedWorld();
        var ids = new EntityIds(constructed, new SimCheckRing(), counters, state: constructed);
        return builder.Build(ids, content.Player.SlopeMaxPm, content.Player.SwimEnterMm);
    }

    /// <summary>A world that is always being constructed: the only state in which destination ids may be issued.</summary>
    private sealed class ConstructedWorld : ITickReader, IWorldStateReader
    {
        public uint CurrentTick => 0;

        public TickStage Stage => TickStage.BetweenTicks;

        public PhaseId Phase => default;

        public SimState State => SimState.Constructed;
    }
}
