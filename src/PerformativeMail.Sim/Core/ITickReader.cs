namespace PerformativeMail.Sim.Core;

/// <summary>
/// Read-only view of the tick position, injected into any system that needs it (ADR-0003 section 4).
/// Systems never receive the <see cref="SimWorld"/> itself. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// bool due = reader.CurrentTick >= deadlineTick;        // absolute deadlines (R15)
/// bool legal = reader.Stage == TickStage.InPhase;
/// </code>
/// </example>
public interface ITickReader
{
    /// <summary>The tick being processed during <c>Tick()</c>; the next tick to process between ticks.</summary>
    uint CurrentTick { get; }

    /// <summary>Where the world is within a <c>Tick()</c> call.</summary>
    TickStage Stage { get; }

    /// <summary>The running phase. Valid only when <see cref="Stage"/> is <see cref="TickStage.InPhase"/>.</summary>
    PhaseId Phase { get; }
}
