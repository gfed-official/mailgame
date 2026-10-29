using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing's tick-end conservation record (ADR-0015 section 5; GDD T6 "Every flight ends in exactly one terminal state";
/// TR-throwing-013), run after phase 12 in every build: <c>launched == resolved + live</c> and no <c>FlightId</c> resolved twice in
/// the run. A finding writes one <see cref="CheckCode.ThrowingConservationMismatch"/> record (a = launched − resolved − live, b = the
/// twice-resolved <c>FlightId</c> value or 0) and adds one <see cref="CounterId.ThrowingConservationMismatches"/>, on every tick it
/// holds (the ring's per-code rate limit applies to the record, not the counter). Always on; it reads the table only, mutates nothing,
/// raises nothing and never throws (the pattern of Grid Inventory's conservation check). Allocation-free.
/// </summary>
/// <example>
/// <code>
/// builder.BindTickEnd(new ThrowingTickEndCheck(flights, checkRing, counters));   // ThrowingSystem.Bind
/// </code>
/// </example>
internal sealed class ThrowingTickEndCheck : ITickEndCheck
{
    private readonly FlightTable _flights;
    private readonly SimCheckRing _ring;
    private readonly SimCounters _counters;

    /// <summary>Creates the check.</summary>
    /// <param name="flights">The table it reads.</param>
    /// <param name="ring">The world's check ring.</param>
    /// <param name="counters">The world's counters.</param>
    public ThrowingTickEndCheck(FlightTable flights, SimCheckRing ring, SimCounters counters)
    {
        _flights = flights;
        _ring = ring;
        _counters = counters;
    }

    /// <inheritdoc />
    public void Check(uint tick)
    {
        long a = _flights.Launched - _flights.Resolved - _flights.LiveFlights;
        if (a == 0 && !_flights.HasResolvedTwice)
        {
            return;
        }

        _ring.Record(CheckCode.ThrowingConservationMismatch, tick, a, _flights.HasResolvedTwice ? _flights.ResolvedTwiceId.Value : 0);
        _counters.Add(CounterId.ThrowingConservationMismatches);
    }
}
