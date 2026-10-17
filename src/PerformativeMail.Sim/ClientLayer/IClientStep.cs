namespace PerformativeMail.Sim.ClientLayer;

/// <summary>
/// A unit of client-tick work bound to one <see cref="ClientStepId"/> by its owning system. Steps read only the
/// client mirror and ports; they never reference a <c>SimWorld</c> or a host-side system (ADR-0016). Must not allocate.
/// </summary>
/// <example>
/// <code>
/// sealed class CarrierStep : IClientStep
/// {
///     public void Step(in ClientStepContext ctx) { if (ctx.IsFirstTickInBatch) { /* read ctx.Input */ } }
/// }
/// </code>
/// </example>
public interface IClientStep
{
    /// <summary>Runs this step for one client tick.</summary>
    /// <param name="ctx">The tick's context; valid only for the duration of the call.</param>
    void Step(in ClientStepContext ctx);
}
