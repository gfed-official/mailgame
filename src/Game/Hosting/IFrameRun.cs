namespace PerformativeMail.Game.Hosting;

/// <summary>
/// The frame sequence's seam over the Sim's frame pump (ADR-0002 section 2). Story 005's adapter wraps the real runner and
/// sources <see cref="IsFaulted"/> from the world state being <c>Faulted</c>: a non-<c>SIM_ASSERT</c> build does not
/// rethrow a world fault (simulation-core 012), so <see cref="FrameSequence"/> checks it after every
/// <see cref="RunFrame"/>. Smoke scenarios supply recording doubles. Main thread only.
/// </summary>
/// <example>
/// <code>
/// run.RunFrame();
/// if (run.IsFaulted) { /* end the session, never call RunFrame again */ }
/// </code>
/// </example>
public interface IFrameRun
{
    /// <summary>Runs one frame of the Sim: pump inbound, advance the driver, run the released ticks.</summary>
    void RunFrame();

    /// <summary>True once the world has faulted; read after each <see cref="RunFrame"/>.</summary>
    bool IsFaulted { get; }
}
