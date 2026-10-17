namespace PerformativeMail.Sim.World;

/// <summary>
/// Compile-time constants of the world height field (ADR-0026 section 2). They are code
/// constants, not balance keys: the tile size is part of the determinism contract.
/// </summary>
public static class WorldConstants
{
    /// <summary>Height-field tile edge in mm (2 m).</summary>
    public const int TileSizeMm = 2000;

    /// <summary><see cref="TileSizeMm"/> squared (the bilinear divisor, 4,000,000).</summary>
    public const long TileAreaMm2 = (long)TileSizeMm * TileSizeMm;

    /// <summary>Largest allowed map coordinate magnitude in mm (ADR-0026 <c>Bounds</c>: |coord| at most 1,000,000).</summary>
    public const int MaxCoordMm = 1_000_000;

    /// <summary>Largest tile count along one axis, so that <c>Tiles * TileSizeMm</c> stays within <see cref="MaxCoordMm"/>.</summary>
    public const int MaxTilesPerAxis = MaxCoordMm / TileSizeMm;

    /// <summary>
    /// Largest allowed corner height magnitude in mm (same magnitude as <see cref="MaxCoordMm"/>, 1 km).
    /// Keeps <c>WaterDepthMm</c> and <c>TerrainGradientPm</c> inside int32 and the bilinear numerators far inside int64.
    /// </summary>
    public const int MaxCornerHeightMm = 1_000_000;
}
