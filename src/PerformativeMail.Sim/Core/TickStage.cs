namespace PerformativeMail.Sim.Core;

/// <summary>Where the world is within a <c>Tick()</c> call; tick end is a stage, not a <see cref="PhaseId"/>.</summary>
/// <example><code>bool inPhase = reader.Stage == TickStage.InPhase;</code></example>
public enum TickStage : byte
{
    /// <summary>Outside <c>Tick()</c>; only read-only faces are legal.</summary>
    BetweenTicks = 0,

    /// <summary>Inside a phase (0 to 13); <see cref="ITickReader.Phase"/> is valid.</summary>
    InPhase = 1,

    /// <summary>Running the <see cref="ITickEndCheck"/>s between phases 12 and 13.</summary>
    TickEnd = 2,
}
