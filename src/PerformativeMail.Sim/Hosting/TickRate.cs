namespace PerformativeMail.Sim.Hosting;

/// <summary>
/// The simulation tick rate (TR-sim-001, ADR-0002 section 1): whole fixed ticks of 1/30 s.
/// A compile-time constant, never data and never a setting. Variable-step simulation is
/// forbidden. All Sim code writes the rate as <see cref="Hz"/>, never as a literal 30.
/// </summary>
/// <example>
/// <code>
/// long ticksPerSecond = TickRate.Hz;               // 30
/// long ticksPerMinute = 60L * TickRate.Hz;         // 1800
/// </code>
/// </example>
public static class TickRate
{
    /// <summary>Ticks per second (30).</summary>
    public const int Hz = 30;
}
