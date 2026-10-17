namespace PerformativeMail.Sim.Core;

/// <summary>Lifecycle state of a <see cref="SimWorld"/> (ADR-0003 section 7). A world is only visible once it is at least <c>Ready</c>.</summary>
/// <example><code>if (world.State == SimState.Running) { /* ticking */ }</code></example>
public enum SimState : byte
{
    /// <summary>Being built; binds are legal. Represented by the <see cref="SimWorldBuilder"/>; <see cref="SimWorldBuilder.Freeze"/> never yields a world in this state (only internal test construction can).</summary>
    Constructed = 0,

    /// <summary>Frozen; the first <c>Tick()</c> will process tick 0.</summary>
    Ready = 1,

    /// <summary>Ticking.</summary>
    Running = 2,

    /// <summary>Stopped by <see cref="IRunControl"/> (after phase 13 of its tick) or <see cref="SimWorld.Stop"/>; ticking is refused.</summary>
    Stopped = 3,

    /// <summary>An exception escaped a phase; ticking is refused.</summary>
    Faulted = 4,
}
