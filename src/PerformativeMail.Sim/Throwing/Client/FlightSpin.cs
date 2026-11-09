using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Flight spin (GDD Visual/Audio "Flight (every peer)": singles 2 rev/s, bundles 1 rev/s, stops at contact; ADR-0005 section 3: a pure
/// function of <c>flightId</c>, no RNG stream; TR-throwing-021 pure-function half; throwing-catching story 017). The angle is a BAM
/// (65 536 per revolution): a start phase from the flight id (a multiplicative hash) plus the turn over the elapsed render time, which
/// <see cref="FlightTimeline"/> stops at the contact, rounded once by <see cref="Throw.RH"/>. Integers only, so every peer computes the
/// same value. Pure, allocation-free, thread-safe.
/// </summary>
/// <example><code>ushort bam = FlightSpin.AngleBam(id, units > 1, frame.ElapsedMilliTicks);</code></example>
public static class FlightSpin
{
    /// <summary>BAM units per revolution.</summary>
    public const int BamPerRev = 65_536;

    /// <summary>The start-phase hash multiplier, about 65 536 / φ.</summary>
    private const uint PhaseMultiplier = 40_503;

    /// <summary>Milli-ticks per second (30 ticks × 1000).</summary>
    private const long MilliTicksPerSecond = 30_000;

    /// <summary>The spin angle of flight <paramref name="id"/>.</summary>
    /// <param name="id">The flight id.</param>
    /// <param name="bundle">True for a bundle (more than one unit): 1 rev/s; a single spins at 2 rev/s.</param>
    /// <param name="elapsedMilliTicks">Elapsed render time since launch in thousandths of a tick (<see cref="FlightFrame.ElapsedMilliTicks"/>); negative is 0.</param>
    /// <returns>The angle, BAM.</returns>
    public static ushort AngleBam(FlightId id, bool bundle, long elapsedMilliTicks)
    {
        long elapsed = elapsedMilliTicks < 0 ? 0 : elapsedMilliTicks;
        long revPerSecond = bundle ? 1 : 2;
        long turn = Throw.RH(elapsed * BamPerRev * revPerSecond, MilliTicksPerSecond);
        uint phase = unchecked(id.Value * PhaseMultiplier);
        return unchecked((ushort)(phase + (ulong)turn));
    }
}
