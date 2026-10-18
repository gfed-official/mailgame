namespace PerformativeMail.Sim.World;

/// <summary>
/// Where a world's tables come from (ADR-0026 section 9): an authored source (M-1a: <see cref="M1aStreet"/>) that fills a
/// <see cref="WorldTablesBuilder"/>, with an identity recorded in the replay header. The composition root calls
/// <see cref="Author"/> while the world is <c>Constructed</c>, then builds the tables with the world's id allocator.
/// Implementations must be deterministic (no RNG, clock or IO) and stateless between calls.
/// </summary>
/// <example>
/// <code>
/// var args = new SimBuildArgs(profile, content, seed) { WorldSource = M1aWorldSource.Instance };
/// SimHost host = SimComposition.Build(in args);
/// </code>
/// </example>
public interface IWorldSource
{
    /// <summary>The source kind recorded in the replay header (1 = <see cref="M1aStreet"/>); 0 is reserved for "no world".</summary>
    ushort Kind { get; }

    /// <summary>The version of the authored data recorded in the replay header; must change whenever any authored datum changes.</summary>
    uint Version { get; }

    /// <summary>Fills <paramref name="b"/> with the source's terrain, solids, streets, lots and Post Office.</summary>
    /// <param name="b">The builder to fill.</param>
    /// <example><code>source.Author(builder);</code></example>
    void Author(WorldTablesBuilder b);
}
