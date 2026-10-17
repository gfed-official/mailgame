namespace PerformativeMail.Sim.Core;

/// <summary>
/// A system's work for one phase, bound with <see cref="SimWorldBuilder.Bind"/>. Called once per tick on
/// the Sim tick thread, in composition (bind) order among binders of the same phase. Must not allocate
/// in steady state, and must not throw (an exception escaping a phase faults the world, Story 012).
/// </summary>
/// <example>
/// <code>
/// sealed class ExpiryPhase : ISimPhase { public void Run(in TickContext ctx) { /* expire items due at ctx.Tick */ } }
/// builder.Bind(PhaseId.ItemsExpire, new ExpiryPhase());
/// </code>
/// </example>
public interface ISimPhase
{
    /// <summary>Runs the phase.</summary>
    /// <param name="ctx">The tick and phase being run.</param>
    void Run(in TickContext ctx);
}
