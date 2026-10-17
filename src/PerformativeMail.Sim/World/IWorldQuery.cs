using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.World;

/// <summary>
/// The one read-only world contract (ADR-0026): terrain, water, solids, bounds and lots for every
/// consumer. The single implementation is the immutable <see cref="WorldTables"/>. Members may only
/// be added by the M1 revision of ADR-0026. All members are pure reads, allocation-free and safe to
/// call from any thread once the tables are built (they never mutate).
/// </summary>
/// <example>
/// <code>
/// IWorldQuery w = builder.Build();
/// long h = w.TerrainHeightMm(1000, 3000);
/// int depth = w.WaterDepthMm(1000, 3000);      // max(0, -h)
/// </code>
/// </example>
public interface IWorldQuery
{
    /// <summary>Height-field tile edge in mm (2000).</summary>
    int TileSizeMm { get; }

    /// <summary>Number of tiles along X.</summary>
    int TilesX { get; }

    /// <summary>Number of tiles along Z.</summary>
    int TilesZ { get; }

    /// <summary>Map extent in mm.</summary>
    MapBounds Bounds { get; }

    /// <summary>Bilinear terrain height in mm (one <c>MulDivRH</c>); positions outside the map are clamped onto it. Equals <see cref="WorldHeight.Sample"/> over <see cref="GetCorners"/>.</summary>
    /// <param name="xMm">World X in mm.</param>
    /// <param name="zMm">World Z in mm.</param>
    /// <returns>Height in mm.</returns>
    long TerrainHeightMm(long xMm, long zMm);

    /// <summary>The four corner heights of tile (<paramref name="tx"/>, <paramref name="tz"/>); indices are clamped into the map.</summary>
    /// <param name="tx">Tile index along X.</param>
    /// <param name="tz">Tile index along Z.</param>
    /// <param name="h">The corner heights.</param>
    void GetCorners(int tx, int tz, out HeightCorners h);

    /// <summary>Terrain slope in permille along X and Z at the (clamped) position, rounded once with <c>MulDivRH</c>.</summary>
    /// <param name="xMm">World X in mm.</param>
    /// <param name="zMm">World Z in mm.</param>
    /// <param name="gxPm">Slope along X in permille.</param>
    /// <param name="gzPm">Slope along Z in permille.</param>
    void TerrainGradientPm(long xMm, long zMm, out int gxPm, out int gzPm);

    /// <summary>Water depth in mm: <c>max(0, -TerrainHeightMm)</c> (sea level 0). The water authority.</summary>
    /// <param name="xMm">World X in mm.</param>
    /// <param name="zMm">World Z in mm.</param>
    /// <returns>Depth in mm, 0 on dry land.</returns>
    int WaterDepthMm(long xMm, long zMm);

    /// <summary>Tile classification, precomputed at build (world-stub story 002). A tile outside the map reports <see cref="TileFlags.Blocked"/>, so it is never walkable.</summary>
    /// <param name="tx">Tile index along X.</param>
    /// <param name="tz">Tile index along Z.</param>
    /// <returns>The tile's flags.</returns>
    TileFlags Tile(int tx, int tz);

    /// <summary>Clamps x/z into <c>[Min + inset, Max - inset]</c> of <see cref="Bounds"/>; y is unchanged (world-stub story 002). A negative inset counts as 0; an axis whose inset range is empty clamps to the floor midpoint of the bounds.</summary>
    /// <param name="p">The point.</param>
    /// <param name="insetMm">Inset from the map edge in mm.</param>
    /// <returns>The clamped point.</returns>
    Vec3Mm ClampInsideMap(in Vec3Mm p, int insetMm);

    /// <summary>Snaps to the nearest <see cref="TileFlags.Walkable"/> tile centre within the radius (squared mm, inclusive; ties to lower tz, then lower tx), with y = terrain height there (world-stub story 002). Returns false and <c>default</c> when none is in range or the radius is negative.</summary>
    /// <param name="p">The point.</param>
    /// <param name="radiusMm">Search radius in mm.</param>
    /// <param name="snapped">The snapped point when found.</param>
    /// <returns>True when a walkable position was found.</returns>
    bool TrySnapWalkable(in Vec3Mm p, int radiusMm, out Vec3Mm snapped);

    /// <summary>All static solids (houses, roofs, Post Office, props), ascending <see cref="StaticSolid.Index"/> = position, from 0. Player-built constructs and vehicles are not world solids.</summary>
    ReadOnlySpan<StaticSolid> StaticSolids { get; }

    /// <summary>
    /// Writes the indices of every static solid whose box overlaps <paramref name="region"/> into
    /// <paramref name="indices"/>, ascending and without duplicates, using the static 4 m bucket grid (ADR-0026
    /// section 5; the only world-solid broad phase). Boxes are closed intervals: touching faces, edges or corners count
    /// as overlap (the precise contact test is the consumer's); a region with Min &gt; Max on any axis is empty.
    /// When the span is shorter than the hit count the lowest-index hits are written, the call returns the span length
    /// and <c>CounterId.WorldSolidQueryOverflow</c> is incremented once (only when the builder bound counters with <c>WorldTablesBuilder.SetCounters</c>; an unbound world counts nothing). Never throws, never grows, allocates 0 B;
    /// O(buckets touched + candidates) for small regions.
    /// </summary>
    /// <param name="region">The query box in mm (closed).</param>
    /// <param name="indices">Destination buffer; its length caps the result.</param>
    /// <returns>Number of indices written.</returns>
    int QueryStaticSolids(in AabbMm region, Span<ushort> indices);

    /// <summary>Lots in ascending <see cref="AddressId"/> (= Mail's DestinationOrdinal). Built by <c>WorldTablesBuilder.Build(EntityIds, int, int)</c> (world-stub story 004).</summary>
    ReadOnlySpan<LotRecord> Lots { get; }

    /// <summary>The Post Office and spawn pad (ADR-0026 section 7): validated at build; slot positions via <see cref="PadSlots.Position"/>. <c>default</c> only for a world built without one (terrain/solid-only test worlds; the composition path requires it).</summary>
    PostOfficeRecord PostOffice { get; }

    /// <summary>The display name of the street whose footprint (closed interval) contains the point, or <see cref="StringId.None"/> off-street (world-stub story 004).</summary>
    /// <param name="xMm">World X in mm.</param>
    /// <param name="zMm">World Z in mm.</param>
    /// <returns>The street's display string or None.</returns>
    StringId StreetNameAt(long xMm, long zMm);
}
