namespace PerformativeMail.Sim.Players;

/// <summary>
/// Tick-time port for supports a carrier can stand on or collide with: vehicles and running belts (ADR-0013 section 7, TR-movement-004).
/// Latest received snapshot only, never render-time poses. Belts arrive at M-1b, vehicles at M1; at M-1a both are empty.
/// </summary>
/// <example><code>if (env.Supports.TryGetBeltVelocity(tx, tz, out int vx, out int vz, out uint seg)) { /* carry */ }</code></example>
public interface ISupportTransformSource
{
    /// <summary>The latest received vehicle poses; valid until the next tick. Allocation-free.</summary>
    ReadOnlySpan<VehiclePose> Vehicles { get; }

    /// <summary>Looks up the running-belt lane velocity of a tile.</summary>
    /// <param name="tileX">Tile X index.</param>
    /// <param name="tileZ">Tile Z index.</param>
    /// <param name="vx">Lane velocity X, mm/s (0 when false).</param>
    /// <param name="vz">Lane velocity Z, mm/s (0 when false).</param>
    /// <param name="segmentId">The belt segment id (0 when false).</param>
    /// <returns>True when a running belt covers the tile.</returns>
    bool TryGetBeltVelocity(int tileX, int tileZ, out int vx, out int vz, out uint segmentId);
}
