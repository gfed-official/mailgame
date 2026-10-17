namespace PerformativeMail.Sim.Hosting;

/// <summary>
/// The lifecycle state of a <see cref="TickDriver"/> (GDD R16/R17): <c>Idle -&gt; Running &lt;-&gt; Paused</c>.
/// Pause is a driver state, never <c>SceneTree.Paused</c> (TR-sim-009).
/// </summary>
/// <example>
/// <code>
/// var d = new TickDriver(10_000_000, 4);   // d.State == TickDriverState.Idle
/// d.Start(0);                              // d.State == TickDriverState.Running
/// </code>
/// </example>
public enum TickDriverState
{
    /// <summary>Not started, or stopped. <see cref="TickDriver.Advance"/> returns 0.</summary>
    Idle = 0,

    /// <summary>Accumulating time and releasing ticks.</summary>
    Running = 1,

    /// <summary>Paused. <see cref="TickDriver.Advance"/> returns 0 and accumulates nothing.</summary>
    Paused = 2,
}
