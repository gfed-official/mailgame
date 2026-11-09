using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Presentation.Delivery;

/// <summary>
/// Non-<c>[OutcomeBeat]</c> frame driver for <see cref="HandBeatController"/> (Mail Lead 2026-11-09): reads
/// <see cref="IRenderClock"/> and <see cref="IDeliverReachRead"/> and passes plain values into the controller each frame so the
/// outcome-beat type holds only its <see cref="PresentationEventStream"/> cursor plus call arguments.
/// </summary>
/// <example>
/// <code>
/// var driver = new HandBeatDriver(new HandBeatController(events, self), clock, reach);
/// driver.OnHoldCompleted(clientTick, destination);
/// driver.Update(clientTick);
/// foreach (HandBeatStart beat in driver.Hand.Started) { view.Play(in beat); }
/// </code>
/// </example>
public sealed class HandBeatDriver
{
    private readonly IRenderClock _clock;
    private readonly IDeliverReachRead _reach;

    /// <summary>Creates the driver around an existing controller.</summary>
    /// <param name="hand">The outcome-beat controller.</param>
    /// <param name="clock">The render clock (beat start times).</param>
    /// <param name="reach">The carrier's hold reach to an anchor.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public HandBeatDriver(HandBeatController hand, IRenderClock clock, IDeliverReachRead reach)
    {
        ArgumentNullException.ThrowIfNull(hand);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(reach);
        Hand = hand;
        _clock = clock;
        _reach = reach;
    }

    /// <summary>The outcome-beat controller this driver feeds.</summary>
    public HandBeatController Hand { get; }

    /// <summary>Forwards <see cref="HandBeatController.OnHoldCompleted"/>.</summary>
    /// <param name="clientTick">The completion tick.</param>
    /// <param name="destination">The latched destination.</param>
    public void OnHoldCompleted(uint clientTick, EntityId destination) => Hand.OnHoldCompleted(clientTick, destination);

    /// <summary>Forwards <see cref="HandBeatController.OnResync"/>.</summary>
    public void OnResync() => Hand.OnResync();

    /// <summary>
    /// Samples the clock and reach ports, then calls <see cref="HandBeatController.Update"/> with plain values.
    /// Reach is evaluated for <see cref="HandBeatController.AwaitedDestination"/> when a reply is awaited; otherwise false.
    /// </summary>
    /// <param name="clientTick">The current client tick.</param>
    public void Update(uint clientTick)
    {
        EntityId awaited = Hand.AwaitedDestination;
        bool within = awaited != EntityId.None && _reach.IsWithinHoldReach(awaited);
        Hand.Update(clientTick, _clock.RenderSimTimeMs, within);
    }
}
