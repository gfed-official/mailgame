namespace PerformativeMail.Sim.Core;

/// <summary>
/// What a bound <see cref="ISimPhase"/> is told about the running phase (ADR-0003 section 2). Passed by
/// <c>in</c>. It carries no services: those arrive by constructor.
/// </summary>
/// <example>
/// <code>
/// public void Run(in TickContext ctx) { if (ctx.Tick >= _deadlineTick) { Fire(); } }
/// </code>
/// </example>
public readonly struct TickContext
{
    /// <summary>The tick being processed (the first <c>Tick()</c> after <c>Ready</c> is tick 0).</summary>
    public readonly uint Tick;

    /// <summary>The phase being run.</summary>
    public readonly PhaseId Phase;

    /// <summary>Creates a context.</summary>
    /// <param name="tick">The tick being processed.</param>
    /// <param name="phase">The phase being run.</param>
    public TickContext(uint tick, PhaseId phase)
    {
        Tick = tick;
        Phase = phase;
    }
}
