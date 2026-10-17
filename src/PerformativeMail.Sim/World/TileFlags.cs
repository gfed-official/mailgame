namespace PerformativeMail.Sim.World;

/// <summary>
/// Per-tile classification (ADR-0026 section 4), precomputed once by <see cref="WorldTablesBuilder.Build(int, int)"/>.
/// <see cref="Water"/> is only a broad-phase hint; the water authority is <see cref="IWorldQuery.WaterDepthMm"/>.
/// </summary>
[Flags]
public enum TileFlags : byte
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The tile is blocked (authored by <see cref="WorldTablesBuilder.MarkBlocked"/>); never <see cref="Walkable"/>.</summary>
    Blocked = 1,

    /// <summary>
    /// Some tile corner is below sea level (height &lt; 0). A broad-phase hint only: the flagged tile can contain
    /// dry points (<c>WaterDepthMm == 0</c>). Never read this as "is in water"; ask <see cref="IWorldQuery.WaterDepthMm"/>.
    /// </summary>
    Water = 2,

    /// <summary>
    /// The tile is not <see cref="Blocked"/>, all four corner gradients pass the exact slope test
    /// (<see cref="PerformativeMail.Sim.Geometry.WorldHeight.IsWalkableSlope"/> with the player's <c>slopeMaxPm</c>) and the water
    /// depth at the tile centre is below the player's <c>swimEnterMm</c>.
    /// <para>
    /// Risk (ADR-0026 Risks): this is a tile-granular decision, so it can disagree with the carrier's per-contact slope
    /// at tile edges (a tile can be flagged walkable while a point inside it is steeper than the limit, or the
    /// reverse). It is used ONLY for <see cref="IWorldQuery.TrySnapWalkable"/> and spawn validation. The carrier
    /// decides walking from the gradient directly (<see cref="IWorldQuery.TerrainGradientPm"/> numerators), never from this flag.
    /// </para>
    /// </summary>
    Walkable = 4,

    /// <summary>The tile is road (display and spawn hint).</summary>
    Road = 8,

    /// <summary>The tile belongs to a lot (display and spawn hint).</summary>
    Lot = 16,
}
