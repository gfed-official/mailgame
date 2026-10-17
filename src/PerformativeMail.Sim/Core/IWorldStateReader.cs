namespace PerformativeMail.Sim.Core;

/// <summary>
/// Read-only view of a world's lifecycle state, injected next to <see cref="ITickReader"/> into the few Core services that
/// need it (the id allocators use it for the <see cref="SimState.Constructed"/> allocation exception, ADR-0004 section 2).
/// Kept separate from <see cref="ITickReader"/> so that interface stays minimal. Systems never receive the
/// <see cref="SimWorld"/> itself (ADR-0003 section 4). Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// bool building = stateReader.State == SimState.Constructed;
/// </code>
/// </example>
public interface IWorldStateReader
{
    /// <summary>The world's lifecycle state.</summary>
    SimState State { get; }
}
