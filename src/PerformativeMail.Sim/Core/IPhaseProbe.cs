namespace PerformativeMail.Sim.Core;

/// <summary>
/// Dev Tooling timing hook bracketing each phase, including phase 13 (ADR-0003 section 2; control
/// manifest I-1). It is never null-checked per call: the default is <see cref="NullPhaseProbe"/>.
/// Implementations must not touch Sim state, and must not allocate.
/// </summary>
/// <example>
/// <code>
/// sealed class CountingProbe : IPhaseProbe { public int Calls; public void Before(PhaseId p) =&gt; Calls++; public void After(PhaseId p) =&gt; Calls++; }
/// var builder = new SimWorldBuilder(probe: new CountingProbe());
/// </code>
/// </example>
public interface IPhaseProbe
{
    /// <summary>Called immediately before the phase's binders run.</summary>
    /// <param name="phase">The phase about to run.</param>
    void Before(PhaseId phase);

    /// <summary>Called immediately after the phase's binders ran.</summary>
    /// <param name="phase">The phase that ran.</param>
    void After(PhaseId phase);
}

/// <summary>The default <see cref="IPhaseProbe"/>: does nothing. Stateless, so instances are interchangeable.</summary>
/// <example><code>IPhaseProbe probe = new NullPhaseProbe();</code></example>
public sealed class NullPhaseProbe : IPhaseProbe
{
    /// <inheritdoc />
    public void Before(PhaseId phase)
    {
    }

    /// <inheritdoc />
    public void After(PhaseId phase)
    {
    }
}
