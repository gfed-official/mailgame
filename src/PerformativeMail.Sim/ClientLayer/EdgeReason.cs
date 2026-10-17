namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// Why the router produced an <see cref="InputEdge"/> that its grammar alone would not (ADR-0012 section 4). Explicit,
/// append-only values.
/// </summary>
/// <example><code>bool forced = edge.Reason == EdgeReason.StateChange;</code></example>
public enum EdgeReason : byte
{
    /// <summary>An ordinary edge from the player's own input.</summary>
    None = 0,

    /// <summary>The Back ladder produced the edge.</summary>
    Back = 1,

    /// <summary>A router state change cancelled or released the action.</summary>
    StateChange = 2,

    /// <summary>The window lost focus.</summary>
    FocusLost = 3,

    /// <summary>The device that held the action was lost.</summary>
    DeviceLost = 4,

    /// <summary>A toggle action's second press produced a release.</summary>
    ToggleSecondPress = 5,

    /// <summary>The presented interaction target went stale before the edge was resolved.</summary>
    EntryStale = 6,

    /// <summary>The action was refused because the actor was busy.</summary>
    Busy = 7,
}
