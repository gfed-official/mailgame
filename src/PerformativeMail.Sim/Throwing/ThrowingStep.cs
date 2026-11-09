using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Phase 4, <c>Throwing.Step</c> (ADR-0015 section 4; ADR-0003 phase order; GDD T5 "The sweep"; TR-throwing-005, -006, -008, -013;
/// throwing-catching story 011): the live flights in creation order (table position, never <see cref="FlightId"/>). A flight launched
/// this tick (age 0) is skipped; a flight of age <c>k</c> (1 to <c>maxFlightTicks</c>) sweeps exactly <c>p(k − 1) → p(k)</c> of
/// <see cref="Ballistic.PositionAt"/> against the receiver view <see cref="ReceiverViewBuilder.Build"/> gathers for that segment
/// (<see cref="ReceiverMask.All"/>, no catcher spheres before M0), and the one <see cref="FlightSweep.FirstContact"/> winner goes to
/// <see cref="TerminalResolver.Resolve"/>; no contact by age <c>maxFlightTicks</c> is <see cref="TerminalResolver.Timeout"/>. A resolved
/// flight is dead for the rest of the step and the table is compacted at its end. The candidate buffers are this step's own
/// <see cref="ReceiverScratch"/>. Allocation-free. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example><code>builder.Bind(PhaseId.ThrowingStep, new ThrowingStep(flights, settings, receivers, ranks, resolver));</code></example>
internal sealed class ThrowingStep : ISimPhase
{
    private readonly FlightTable _flights;
    private readonly ThrowSettings _settings;
    private readonly ReceiverViewBuilder _receivers;
    private readonly ReceiverRanks _ranks;
    private readonly TerminalResolver _resolver;
    private readonly ReceiverScratch _scratch = new();

    /// <summary>Creates the step.</summary>
    /// <param name="flights">The live flights.</param>
    /// <param name="settings">The throw knobs (<c>g</c>, <c>maxFlightTicks</c>).</param>
    /// <param name="receivers">The receiver view builder.</param>
    /// <param name="ranks">The rank table.</param>
    /// <param name="resolver">The terminal switch.</param>
    public ThrowingStep(FlightTable flights, ThrowSettings settings, ReceiverViewBuilder receivers, ReceiverRanks ranks, TerminalResolver resolver)
    {
        _flights = flights;
        _settings = settings;
        _receivers = receivers;
        _ranks = ranks;
        _resolver = resolver;
    }

    /// <inheritdoc />
    public void Run(in TickContext ctx)
    {
        uint tick = ctx.Tick;
        int max = _settings.MaxFlightTicks;
        for (int i = 0; i < _flights.Count; i++)
        {
            if (_flights.IsDead(i))
            {
                continue;
            }

            uint launched = _flights.LaunchTick(i);
            if (tick <= launched)
            {
                continue; // the launch tick (age 0) never sweeps
            }

            uint age = tick - launched;
            int k = age >= (uint)max ? max : (int)age;
            var launch = new Launch(_flights.Origin(i), _flights.Vel(i), _settings.G);
            Vec3Mm p0 = Ballistic.PositionAt(in launch, k - 1);
            Vec3Mm p1 = Ballistic.PositionAt(in launch, k);
            ReceiverView view = _receivers.Build(in p0, in p1, ReceiverMask.All, _ranks, default, _scratch);
            if (FlightSweep.FirstContact(in p0, in p1, in view, _flights.Id(i), out Contact c))
            {
                _resolver.Resolve(i, in c, tick);
            }
            else if (k == max)
            {
                _resolver.Timeout(i, in p1, tick);
            }
        }

        _flights.Compact();
    }
}
