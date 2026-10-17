using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Extension point for non-world carrier colliders (ADR-0013 section 4; story 008): player-built constructs, fences and nodes are owned
/// by their systems' registries, not by <c>IWorldQuery</c>, and reach the solver through this tick-time port in M-1b. Empty at M-1a,
/// so nothing implements it yet. When it lands: add it to <see cref="CarrierEnv"/> and call it from <see cref="CarrierColliders.Gather"/>
/// (the marked hook) with the same swept region, feeding <see cref="CarrierScratch.TryAddBox"/> in ascending collider id. Everything
/// downstream (slides, support tops) already iterates <see cref="CarrierScratch.Boxes"/>.
/// </summary>
/// <example><code>int n = source.GatherBoxes(in region, boxes);</code></example>
public interface ICarrierBoxSource
{
    /// <summary>Writes the boxes overlapping <paramref name="region"/> (closed) in ascending collider id; returns the count written (at most <c>boxes.Length</c>). Must not allocate.</summary>
    /// <param name="region">The tick's swept region, mm.</param>
    /// <param name="boxes">Destination.</param>
    /// <returns>Boxes written.</returns>
    int GatherBoxes(in AabbMm region, Span<AabbMm> boxes);
}
