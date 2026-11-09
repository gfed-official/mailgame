using PerformativeMail.Client.Core.Mirror;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.ThrowingCatching;

namespace PerformativeMail.Client.Core.Throwing;

/// <summary>
/// Feeds the Sim's <see cref="FlightMirror"/> from the client drain and the render clock (ADR-0016 sections 1 and 6; throwing-catching
/// story 017). Registered with <see cref="MirrorDrainStep.Register"/>, it forwards every drained entry and reply to the mirror's
/// <c>IMirrorPart</c>-shaped methods (the mirror ignores other kinds); <c>ClientLayerBuilder.BindMirrorPart</c> replaces it when that
/// builder method lands. <see cref="Present"/> samples the flights once per frame from <see cref="IRenderClock"/>, after <c>RunFrame</c>
/// and before the views draw; an invalid clock leaves the last sample in place. Allocation-free; main thread only.
/// </summary>
/// <example>
/// <code>
/// var flights = new FlightMirrorConsumer(new FlightMirror(settings, ownSlot));
/// drain.Register(flights);
/// flights.Present(renderClock);   // per frame; views read flights.Flights
/// </code>
/// </example>
public sealed class FlightMirrorConsumer : IMirrorConsumer
{
    private readonly FlightMirror _mirror;

    /// <summary>Creates the consumer over <paramref name="mirror"/>.</summary>
    /// <param name="mirror">The flight mirror.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mirror"/> is null.</exception>
    public FlightMirrorConsumer(FlightMirror mirror)
    {
        ArgumentNullException.ThrowIfNull(mirror);
        _mirror = mirror;
    }

    /// <summary>The flights as of the last <see cref="Present"/>.</summary>
    public IFlightRead Flights => _mirror;

    /// <inheritdoc />
    public void OnEntry(uint tick, in EventEntry entry) => _mirror.Apply(in entry, default);

    /// <inheritdoc />
    public void OnReply(uint tick, in Reply reply) => _mirror.ApplyReply(in reply);

    /// <summary>Samples the flights at <c>clock.HostTick − 3</c> + <c>clock.AlphaPermille</c>; nothing while the clock is invalid.</summary>
    /// <param name="clock">The frame's render clock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is null.</exception>
    public void Present(IRenderClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (clock.IsValid)
        {
            _mirror.Sample(clock.HostTick, clock.AlphaPermille);
        }
    }
}
