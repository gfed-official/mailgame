using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Presentation.Delivery;

/// <summary>
/// One hand beat to play (manual-delivery story 006): a <see cref="HandBeatOutcome"/> stamped by <see cref="HandBeatDriver"/> with
/// whether it belongs to the current prompt and its start time. A plain descriptor for the Godot hand view (story 011); it carries no
/// duration.
/// </summary>
/// <param name="Actor">The acting player.</param>
/// <param name="Destination">The destination.</param>
/// <param name="ApplyTick">The host tick the hold applied on (the batch's <c>sourceTick</c>).</param>
/// <param name="Response">The hand response; a late reply's <see cref="HandResponse.SlotIn"/> is false (MD7).</param>
/// <param name="Riffle">GDD F4 for the entries posted (<see cref="RiffleTiming.Stacks"/> 0 for none).</param>
/// <param name="AttachedToPrompt">False for another player's hold and for a mailbox the carrier has walked beyond 2.9 m from.</param>
/// <param name="StartMs">The <see cref="IRenderClock.RenderSimTimeMs"/> of the frame the beat started on.</param>
public readonly record struct HandBeatStart(
    PlayerSlot Actor, EntityId Destination, uint ApplyTick, HandResponse Response, RiffleTiming Riffle, bool AttachedToPrompt, long StartMs);

/// <summary>Whether the carrier is still within hold reach (2.9 m) of a destination's Deliver anchor, from the published carrier and the anchor point.</summary>
/// <example><code>bool attach = reach.IsWithinHoldReach(destination);</code></example>
public interface IDeliverReachRead
{
    /// <summary>True while the local carrier is within hold reach of <paramref name="destination"/>'s anchor.</summary>
    /// <param name="destination">The destination.</param>
    /// <returns>True when within reach.</returns>
    bool IsWithinHoldReach(EntityId destination);
}

/// <summary>
/// Non-<c>[OutcomeBeat]</c> stamping stage for <see cref="HandBeatController"/> (ADR-0016 section 5): the controller emits cursor-only
/// <see cref="HandBeatOutcome"/>s, and this driver stamps each with <see cref="IRenderClock.RenderSimTimeMs"/> and, for an own hold,
/// <see cref="IDeliverReachRead.IsWithinHoldReach"/> of its destination (another player's hold is never attached). Main thread only.
/// </summary>
/// <example>
/// <code>
/// var driver = new HandBeatDriver(new HandBeatController(events, self), clock, reach);
/// driver.OnHoldCompleted(clientTick, destination);
/// driver.Update(clientTick);
/// foreach (HandBeatStart beat in driver.Started) { view.Play(in beat); }
/// </code>
/// </example>
public sealed class HandBeatDriver
{
    private readonly IRenderClock _clock;
    private readonly IDeliverReachRead _reach;
    private readonly List<HandBeatStart> _started = [];

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

    /// <summary>The outcome-beat controller this driver stamps.</summary>
    public HandBeatController Hand { get; }

    /// <summary>The beats the last <see cref="Update"/> started, stamped, in stream order.</summary>
    public IReadOnlyList<HandBeatStart> Started => _started;

    /// <summary>Forwards <see cref="HandBeatController.OnHoldCompleted"/>.</summary>
    /// <param name="clientTick">The completion tick.</param>
    /// <param name="destination">The latched destination.</param>
    public void OnHoldCompleted(uint clientTick, EntityId destination) => Hand.OnHoldCompleted(clientTick, destination);

    /// <summary>Forwards <see cref="HandBeatController.OnResync"/>.</summary>
    public void OnResync() => Hand.OnResync();

    /// <summary>Updates the controller, then replaces <see cref="Started"/> with its outcomes stamped for this frame.</summary>
    /// <param name="clientTick">The current client tick.</param>
    public void Update(uint clientTick)
    {
        Hand.Update(clientTick);
        _started.Clear();
        long startMs = _clock.RenderSimTimeMs;
        foreach (HandBeatOutcome o in Hand.Started)
        {
            bool attached = o.Actor == Hand.Self && _reach.IsWithinHoldReach(o.Destination);
            _started.Add(new HandBeatStart(o.Actor, o.Destination, o.ApplyTick, o.Response, o.Riffle, attached, startMs));
        }
    }
}
