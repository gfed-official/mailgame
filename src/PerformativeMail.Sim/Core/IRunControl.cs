namespace PerformativeMail.Sim.Core;

/// <summary>
/// Stop request port, injected into Run Structure only (ADR-0003 sections 4 and 5). Implemented inside
/// <see cref="SimWorld"/>: legal in phase 12; elsewhere latched as if raised in phase 12 and counted
/// (<see cref="CounterId.StopOutOfPhase"/>); a second request is ignored and counted (<see cref="CounterId.StopIgnored"/>).
/// </summary>
/// <example><code>runControl.RequestStop(StopReason.Unspecified);   // legal in phase 12</code></example>
public interface IRunControl
{
    /// <summary>Latches a stop applied after the current tick completes phase 13.</summary>
    /// <param name="reason">Why the run is stopping.</param>
    void RequestStop(StopReason reason);
}
