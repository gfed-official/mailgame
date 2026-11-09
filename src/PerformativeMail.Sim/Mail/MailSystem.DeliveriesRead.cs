namespace PerformativeMail.Sim.MailDestinations;

/// <summary>The read-only deliveries-state accessor (throwing-catching story 010's <c>openAtRelease</c>).</summary>
public sealed partial class MailSystem
{
    /// <inheritdoc />
    public void GetDeliveriesState(out bool open, out uint closeTick)
    {
        open = _registry.Deliveries.Open;
        closeTick = _registry.Deliveries.CloseTick;
    }
}
