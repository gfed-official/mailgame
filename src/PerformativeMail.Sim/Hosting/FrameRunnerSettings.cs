namespace PerformativeMail.Sim.Hosting;

/// <summary>
/// Settings for <see cref="FrameRunner"/> (ADR-0002 section 2).
/// </summary>
/// <param name="CatchUpGuardMs">
/// The wall-clock catch-up guard in milliseconds. 0 (off) at M-1a: the guard is not built, so any value is accepted
/// (negative is rejected) and none changes behaviour. 10 ms at M0, constrained to at least 2 x tickBudgetP99(host).
/// </param>
/// <example>
/// <code>
/// var runner = new FrameRunner(clock, driver, client, world, transport, FrameRunnerSettings.Default);
/// </code>
/// </example>
public readonly record struct FrameRunnerSettings(int CatchUpGuardMs)
{
    /// <summary>The M-1a settings: guard off.</summary>
    public static FrameRunnerSettings Default => new(0);
}
