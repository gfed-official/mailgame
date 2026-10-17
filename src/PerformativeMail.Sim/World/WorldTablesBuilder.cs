using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.World;

/// <summary>
/// Collects world data and produces the immutable <see cref="WorldTables"/> (ADR-0026 section 1).
/// Composition-time only; not thread-safe; never used inside a tick. Invalid input throws
/// <see cref="SimBuildException"/> (the Sim's only by-design exception, ADR-0003 section 3).
/// <para>
/// Provides <see cref="SetCorners"/>, <see cref="SetBounds"/>, the tile marks, <see cref="AddSolid"/>,
/// <see cref="SetCounters"/>, the streets, lots and display strings (story 004) and the <c>Build</c> overloads;
/// <see cref="SetPostOffice"/> (story 005). A world with lots must be built with
/// <see cref="Build(EntityIds, int, int)"/>: the overloads without <see cref="EntityIds"/> are for terrain/solid-only
/// worlds and throw <see cref="SimBuildErrorCode.WorldLotsNeedEntityIds"/> when lots were added. The Post Office is
/// required on <see cref="Build(EntityIds, int, int)"/> (the composition path), optional on <see cref="Build(int, int)"/>
/// (validated when set; <see cref="IWorldQuery.PostOffice"/> is <c>default</c> when not) and rejected by <see cref="Build()"/>.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var b = new WorldTablesBuilder();
/// b.SetCorners(2, 1, new[] { 0, 0, 0,  0, 0, 0 });   // (2+1) x (1+1) corners, row-major
/// b.SetBounds(new MapBounds(-1000, -1000, 5000, 3000));
/// b.MarkBlocked(1, 0);
/// ushort house = b.AddSolid(EntityId.None, new AabbMm(new Vec3Mm(0, 0, 0), new Vec3Mm(2000, 3000, 2000)), SolidKind.House);
/// StringId name = b.AddDisplayString("Harbour Lane");
/// b.AddStreet(1, 1, name, new AabbMm(new Vec3Mm(0, 0, 1000), new Vec3Mm(4000, 1, 2000)), StreetAxis.X);
/// b.AddLot(AddressId.Pack(1, 1, 1), b.AddDisplayString("1 Harbour Lane"), new Vec3Mm(1000, 0, 2500), FaceDir.NegZ,
///     new TileCoord(0, 1), new TileCoord(0, 0), new Vec3Mm(1000, 0, 1500), house);
/// b.SetPostOffice(door, intake, padCentre);   // required on the composition path; pad validated at Build
/// IWorldQuery w = b.Build(ids, 700, 900);   // ids: the world's EntityIds while Constructed; limits from the players SimSettings
/// </code>
/// </example>
public sealed partial class WorldTablesBuilder
{
    private int[]? _cornerMm;
    private int _tilesX;
    private int _tilesZ;
    private MapBounds? _bounds;
    private readonly List<TileMark> _marks = new();
    private readonly List<StaticSolid> _solids = new();
    private SimCounters? _counters;

    /// <summary>Most static solids a world may hold: indices are <c>ushort</c> 0 to 65,534.</summary>
    public const int MaxStaticSolids = ushort.MaxValue;

    /// <summary>
    /// Sets the height field. Corners are row-major: index <c>cz * (tilesX + 1) + cx</c>, size
    /// <c>(tilesX + 1) * (tilesZ + 1)</c>. The span is copied, so later changes to it do not affect the
    /// builder. A second call replaces the first.
    /// </summary>
    /// <param name="tilesX">Tiles along X, 1 to <see cref="WorldConstants.MaxTilesPerAxis"/>.</param>
    /// <param name="tilesZ">Tiles along Z, 1 to <see cref="WorldConstants.MaxTilesPerAxis"/>.</param>
    /// <param name="cornerMm">Corner heights in mm; length must be <c>(tilesX + 1) * (tilesZ + 1)</c>.</param>
    /// <exception cref="SimBuildException">A tile count is out of range (<see cref="SimBuildErrorCode.WorldTileCountOutOfRange"/>), the span length is wrong, or a corner height exceeds <see cref="WorldConstants.MaxCornerHeightMm"/>.</exception>
    /// <example>
    /// <code>
    /// builder.SetCorners(1, 1, new[] { -1000, -1000, -200, -200 });
    /// </code>
    /// </example>
    public void SetCorners(int tilesX, int tilesZ, ReadOnlySpan<int> cornerMm)
    {
        if (tilesX < 1 || tilesX > WorldConstants.MaxTilesPerAxis)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldTileCountOutOfRange, "WorldTablesBuilder.SetCorners: tilesX out of range 1.." + WorldConstants.MaxTilesPerAxis + ".");
        }

        if (tilesZ < 1 || tilesZ > WorldConstants.MaxTilesPerAxis)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldTileCountOutOfRange, "WorldTablesBuilder.SetCorners: tilesZ out of range 1.." + WorldConstants.MaxTilesPerAxis + ".");
        }

        long expected = (long)(tilesX + 1) * (tilesZ + 1);
        if (cornerMm.Length != expected)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldCornerCountMismatch, "WorldTablesBuilder.SetCorners: expected " + expected + " corners, got " + cornerMm.Length + ".");
        }

        for (int i = 0; i < cornerMm.Length; i++)
        {
            if (cornerMm[i] < -WorldConstants.MaxCornerHeightMm || cornerMm[i] > WorldConstants.MaxCornerHeightMm)
            {
                throw new SimBuildException(SimBuildErrorCode.WorldCornerHeightOutOfRange, "WorldTablesBuilder.SetCorners: corner " + i + " height out of range +/-" + WorldConstants.MaxCornerHeightMm + " mm.");
            }
        }

        _cornerMm = cornerMm.ToArray();
        _tilesX = tilesX;
        _tilesZ = tilesZ;
    }

    /// <summary>
    /// Sets the map rectangle. Optional: without a call the bounds are the corner grid
    /// <c>[0, TilesX * S] x [0, TilesZ * S]</c>. Validated at <see cref="Build(int, int)"/> (|coord| at most
    /// <see cref="WorldConstants.MaxCoordMm"/>, and the rectangle must contain the corner grid). A second call replaces the first.
    /// </summary>
    /// <param name="bounds">The map rectangle in mm.</param>
    /// <example>
    /// <code>
    /// builder.SetBounds(new MapBounds(-2000, -2000, 10_000, 6000));
    /// </code>
    /// </example>
    public void SetBounds(MapBounds bounds)
    {
        _bounds = bounds;
    }

    /// <summary>Marks tile (<paramref name="tx"/>, <paramref name="tz"/>) <see cref="TileFlags.Blocked"/>; such a tile is never walkable. Range-checked at Build.</summary>
    /// <param name="tx">Tile index along X.</param>
    /// <param name="tz">Tile index along Z.</param>
    /// <example>
    /// <code>
    /// builder.MarkBlocked(3, 4);
    /// </code>
    /// </example>
    public void MarkBlocked(int tx, int tz)
    {
        _marks.Add(new TileMark(tx, tz, TileFlags.Blocked));
    }

    /// <summary>Marks tile (<paramref name="tx"/>, <paramref name="tz"/>) <see cref="TileFlags.Road"/> (display and spawn hint). Range-checked at Build.</summary>
    /// <param name="tx">Tile index along X.</param>
    /// <param name="tz">Tile index along Z.</param>
    /// <example>
    /// <code>
    /// builder.MarkRoad(0, 2);
    /// </code>
    /// </example>
    public void MarkRoad(int tx, int tz)
    {
        _marks.Add(new TileMark(tx, tz, TileFlags.Road));
    }

    /// <summary>Marks tile (<paramref name="tx"/>, <paramref name="tz"/>) <see cref="TileFlags.Lot"/> (display and spawn hint). Range-checked at Build.</summary>
    /// <param name="tx">Tile index along X.</param>
    /// <param name="tz">Tile index along Z.</param>
    /// <example>
    /// <code>
    /// builder.MarkLot(5, 2);
    /// </code>
    /// </example>
    public void MarkLot(int tx, int tz)
    {
        _marks.Add(new TileMark(tx, tz, TileFlags.Lot));
    }

    /// <summary>
    /// Adds a static solid (ADR-0026 section 5) and returns its index: solids are numbered in add order from 0, and
    /// <see cref="IWorldQuery.StaticSolids"/> lists them in that order. The box must have <c>Min &lt; Max</c> on every
    /// axis (checked here) and lie inside the map bounds on X/Z with |Y| at most <see cref="WorldConstants.MaxCoordMm"/>
    /// (checked at Build, since the bounds may still change). Player-built constructs and vehicles are not world solids.
    /// </summary>
    /// <param name="owner">Owning prop or destination, or <see cref="EntityId.None"/> (houses until story 004).</param>
    /// <param name="box">The solid's box in mm (closed).</param>
    /// <param name="kind">The solid's category.</param>
    /// <returns>The new solid's index.</returns>
    /// <exception cref="SimBuildException">A degenerate box (<see cref="SimBuildErrorCode.WorldSolidBoxDegenerate"/>) or more than <see cref="MaxStaticSolids"/> solids (<see cref="SimBuildErrorCode.WorldTooManySolids"/>).</exception>
    /// <example>
    /// <code>
    /// ushort roof = builder.AddSolid(EntityId.None, new AabbMm(new Vec3Mm(0, 3000, 0), new Vec3Mm(6000, 4500, 8000)), SolidKind.Roof);
    /// </code>
    /// </example>
    public ushort AddSolid(EntityId owner, AabbMm box, SolidKind kind)
    {
        if (box.Min.X >= box.Max.X || box.Min.Y >= box.Max.Y || box.Min.Z >= box.Max.Z)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldSolidBoxDegenerate, "WorldTablesBuilder.AddSolid: box Min must be below Max on every axis (solid " + _solids.Count + ").");
        }

        if (_solids.Count >= MaxStaticSolids)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldTooManySolids, "WorldTablesBuilder.AddSolid: more than " + MaxStaticSolids + " static solids.");
        }

        ushort index = (ushort)_solids.Count;
        _solids.Add(new StaticSolid(index, owner, box, kind));
        return index;
    }

    /// <summary>
    /// Binds the world's counters (the composition root passes the <see cref="SimWorld"/>'s). The built tables then count
    /// truncating <c>QueryStaticSolids</c> calls in <see cref="CounterId.WorldSolidQueryOverflow"/> and linear-scan
    /// fallbacks in <see cref="CounterId.WorldSolidQueryFallback"/>; because <see cref="SimCounters"/> is single-threaded,
    /// such tables must then be queried from the Sim tick thread only. Without a call nothing is counted.
    /// </summary>
    /// <param name="counters">The counters to write, or null to count nothing.</param>
    /// <example>
    /// <code>
    /// builder.SetCounters(counters);
    /// </code>
    /// </example>
    public void SetCounters(SimCounters? counters)
    {
        _counters = counters;
    }

    /// <summary>
    /// Builds the tables with no walkability limits: <see cref="TileFlags.Walkable"/> is never set (every tile is
    /// unwalkable, so <c>TrySnapWalkable</c> always fails). Use <see cref="Build(int, int)"/> for a playable world.
    /// Terrain/solid-only worlds: no Post Office may be set (its pad cannot be validated without limits), and
    /// <see cref="IWorldQuery.PostOffice"/> is <c>default</c>.
    /// </summary>
    /// <returns>The world tables.</returns>
    /// <exception cref="SimBuildException">See <see cref="Build(int, int)"/> (except the limit check); lots were added (<see cref="SimBuildErrorCode.WorldLotsNeedEntityIds"/>);
    /// a Post Office was set (<see cref="SimBuildErrorCode.WorldPostOfficeNeedsWalkabilityLimits"/>).</exception>
    /// <example>
    /// <code>
    /// WorldTables tables = builder.Build();
    /// </code>
    /// </example>
    public WorldTables Build()
    {
        return BuildCore(null, false, 0, 0);
    }

    /// <summary>
    /// Builds the immutable tables from a private copy of the current state (the builder may be reused) and
    /// precomputes every tile's <see cref="TileFlags"/>. A tile is <see cref="TileFlags.Walkable"/> iff it is not
    /// blocked, all four corner gradients pass the exact slope test for <paramref name="slopeMaxPm"/>, and the water
    /// depth at its centre is below <paramref name="swimEnterMm"/>. The Post Office is optional here: when
    /// <see cref="SetPostOffice"/> was called its anchors and pad are validated (see there); when not,
    /// <see cref="IWorldQuery.PostOffice"/> is <c>default</c>. The composition path is <see cref="Build(EntityIds, int, int)"/>.
    /// </summary>
    /// <param name="slopeMaxPm">Largest walkable slope in permille (the player's <c>slopeMaxPm</c>, 700 at M-1a); never a literal in World.</param>
    /// <param name="swimEnterMm">Water depth in mm from which a tile centre is unwalkable (the player's <c>swimEnterMm</c>, 900 at M-1a).</param>
    /// <returns>The world tables.</returns>
    /// <exception cref="SimBuildException">Corners not set, a negative limit, bounds out of range or not containing the corner grid, a tile mark outside the map, or lots were added (<see cref="SimBuildErrorCode.WorldLotsNeedEntityIds"/>: use <see cref="Build(EntityIds, int, int)"/>);
    /// a set Post Office fails (<see cref="SimBuildErrorCode.WorldPostOfficeSetTwice"/>, <see cref="SimBuildErrorCode.WorldPostOfficeAnchorOutsideBounds"/>,
    /// <see cref="SimBuildErrorCode.WorldPadSlotTooDeep"/>, <see cref="SimBuildErrorCode.WorldPadSlotNotWalkable"/>, <see cref="SimBuildErrorCode.WorldPadSlotInsideSolid"/>); each has its own <see cref="SimBuildErrorCode"/>.</exception>
    /// <example>
    /// <code>
    /// WorldTables tables = builder.Build(700, 900);
    /// </code>
    /// </example>
    public WorldTables Build(int slopeMaxPm, int swimEnterMm)
    {
        return BuildWithLimits(null, slopeMaxPm, swimEnterMm);
    }

    /// <summary>
    /// As <see cref="Build(int, int)"/>, plus the lots and streets (ADR-0026 section 6): lots are sorted by plain
    /// <see cref="AddressId.Value"/> order (lot index = Mail's DestinationOrdinal), validated (address ranges, duplicates,
    /// streets, house solids, Mail R3 spacing), and then given <see cref="EntityClass.Destination"/> ids from
    /// <paramref name="ids"/> in lot order, so lot i gets counter <c>first + i</c>. Each lot's house solid gets that id as
    /// its <see cref="StaticSolid.Owner"/>. Allocation is legal only while the world is <see cref="SimState.Constructed"/>
    /// on the owner thread, and happens only after every check has passed; each successful call allocates new ids.
    /// If the allocator refuses partway (<see cref="SimBuildErrorCode.WorldDestinationIdRefused"/>), ids already issued
    /// are not rolled back; the build has failed and the world must not be frozen. Each lot needs its own
    /// <see cref="SolidKind.House"/> solid: two lots (e.g. units of one building) cannot share one.
    /// The Post Office is required here (<see cref="SetPostOffice"/> exactly once; ADR-0026 section 7) and its pad is
    /// validated before any id is allocated.
    /// </summary>
    /// <param name="ids">The world's entity id allocator (composition root, before <c>Freeze</c>).</param>
    /// <param name="slopeMaxPm">Largest walkable slope in permille (the player's <c>slopeMaxPm</c>).</param>
    /// <param name="swimEnterMm">Water depth in mm from which a tile centre is unwalkable (the player's <c>swimEnterMm</c>).</param>
    /// <returns>The world tables.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ids"/> is null.</exception>
    /// <exception cref="SimBuildException">Any <see cref="Build(int, int)"/> error, or <see cref="SimBuildErrorCode.AddressOutOfRange"/>,
    /// <see cref="SimBuildErrorCode.DuplicateAddress"/>, <see cref="SimBuildErrorCode.WorldMailboxSpacingTooSmall"/>,
    /// <see cref="SimBuildErrorCode.WorldLotInvalid"/>, <see cref="SimBuildErrorCode.WorldStreetInvalid"/>,
    /// <see cref="SimBuildErrorCode.WorldDisplayStringInvalid"/>, <see cref="SimBuildErrorCode.WorldPostOfficeNotSet"/> or <see cref="SimBuildErrorCode.WorldDestinationIdRefused"/>.</exception>
    /// <example>
    /// <code>
    /// WorldTables tables = builder.Build(ids, 700, 900);
    /// EntityId first = tables.Lots[0].Destination;   // class Destination (0x07)
    /// </code>
    /// </example>
    public WorldTables Build(EntityIds ids, int slopeMaxPm, int swimEnterMm)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return BuildWithLimits(ids, slopeMaxPm, swimEnterMm);
    }

    private WorldTables BuildWithLimits(EntityIds? ids, int slopeMaxPm, int swimEnterMm)
    {
        if (slopeMaxPm < 0 || swimEnterMm < 0)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldInvalidWalkabilityLimit, "WorldTablesBuilder.Build: slopeMaxPm and swimEnterMm must not be negative.");
        }

        return BuildCore(ids, true, slopeMaxPm, swimEnterMm);
    }

    private WorldTables BuildCore(EntityIds? ids, bool computeWalkable, int slopeMaxPm, int swimEnterMm)
    {
        if (_cornerMm is null)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldCornersNotSet, "WorldTablesBuilder.Build: SetCorners has not been called.");
        }

        long gridMaxX = (long)_tilesX * WorldConstants.TileSizeMm;
        long gridMaxZ = (long)_tilesZ * WorldConstants.TileSizeMm;
        MapBounds bounds = _bounds ?? new MapBounds(0, 0, gridMaxX, gridMaxZ);
        ValidateBounds(bounds, gridMaxX, gridMaxZ);

        int[] corners = (int[])_cornerMm.Clone();
        byte[] flags = new byte[_tilesX * _tilesZ];
        for (int i = 0; i < _marks.Count; i++)
        {
            TileMark m = _marks[i];
            if (m.Tx < 0 || m.Tx >= _tilesX || m.Tz < 0 || m.Tz >= _tilesZ)
            {
                throw new SimBuildException(
                    SimBuildErrorCode.WorldTileMarkOutOfRange,
                    "WorldTablesBuilder.Build: tile mark (" + m.Tx + ", " + m.Tz + ") is outside the " + _tilesX + " x " + _tilesZ + " map.");
            }

            flags[(m.Tz * _tilesX) + m.Tx] |= (byte)m.Flag;
        }

        ComputeTerrainFlags(corners, flags, computeWalkable, slopeMaxPm, swimEnterMm);
        StaticSolid[] solids = _solids.ToArray();
        ValidateSolids(solids, in bounds);
        WorldStreet[] streets = PrepareStreets();
        PendingLot[] lots = PrepareLots(ids, streets, solids, in bounds);
        PostOfficeRecord postOffice = PreparePostOffice(ids is not null, computeWalkable, in bounds);
        StaticSolidGrid grid = StaticSolidGrid.Build(solids, in bounds);
        var lotRecords = new LotRecord[lots.Length];
        var tables = new WorldTables(_tilesX, _tilesZ, corners, flags, bounds, solids, grid, _counters, lotRecords, streets, _displayText.ToArray(), postOffice);
        ValidatePad(tables, in postOffice, swimEnterMm);
        EntityId[] destinations = AllocateDestinations(ids, lots, solids); // patches house owners in the tables' own solids array
        FillLotRecords(tables, lots, destinations, lotRecords);
        return tables;
    }

    private static void ValidateBounds(MapBounds b, long gridMaxX, long gridMaxZ)
    {
        const long Max = WorldConstants.MaxCoordMm; // message only
        if (OutOfRange(b.MinXMm) || OutOfRange(b.MinZMm) || OutOfRange(b.MaxXMm) || OutOfRange(b.MaxZMm))
        {
            throw new SimBuildException(SimBuildErrorCode.WorldBoundsOutOfRange, "WorldTablesBuilder.Build: bounds coordinate magnitude exceeds " + Max + " mm.");
        }

        if (b.MinXMm > 0 || b.MinZMm > 0 || b.MaxXMm < gridMaxX || b.MaxZMm < gridMaxZ)
        {
            throw new SimBuildException(
                SimBuildErrorCode.WorldBoundsDoNotContainGrid,
                "WorldTablesBuilder.Build: bounds must contain the corner grid [0, " + gridMaxX + "] x [0, " + gridMaxZ + "].");
        }
    }

    private static void ValidateSolids(StaticSolid[] solids, in MapBounds b)
    {
        for (int i = 0; i < solids.Length; i++)
        {
            AabbMm box = solids[i].Box;
            if (box.Min.X < b.MinXMm || box.Max.X > b.MaxXMm || box.Min.Z < b.MinZMm || box.Max.Z > b.MaxZMm
                || OutOfRange(box.Min.Y) || OutOfRange(box.Max.Y))
            {
                throw new SimBuildException(
                    SimBuildErrorCode.WorldSolidOutsideBounds,
                    "WorldTablesBuilder.Build: static solid " + i + " is outside the map bounds (X/Z) or |Y| exceeds " + WorldConstants.MaxCoordMm + " mm.");
            }
        }
    }

    private static bool OutOfRange(long coord)
    {
        return coord < -WorldConstants.MaxCoordMm || coord > WorldConstants.MaxCoordMm;
    }

    private void ComputeTerrainFlags(int[] corners, byte[] flags, bool computeWalkable, int slopeMaxPm, int swimEnterMm)
    {
        int stride = _tilesX + 1;
        for (int tz = 0; tz < _tilesZ; tz++)
        {
            for (int tx = 0; tx < _tilesX; tx++)
            {
                int i = (tz * stride) + tx;
                var h = new HeightCorners(corners[i], corners[i + 1], corners[i + stride], corners[i + stride + 1]);
                int t = (tz * _tilesX) + tx;
                byte f = flags[t];

                if (h.H00 < 0 || h.H10 < 0 || h.H01 < 0 || h.H11 < 0)
                {
                    f |= (byte)TileFlags.Water;
                }

                if (computeWalkable && (f & (byte)TileFlags.Blocked) == 0 && IsWalkable(in h, slopeMaxPm, swimEnterMm))
                {
                    f |= (byte)TileFlags.Walkable;
                }

                flags[t] = f;
            }
        }
    }

    private static bool IsWalkable(in HeightCorners h, int slopeMaxPm, int swimEnterMm)
    {
        const int S = WorldConstants.TileSizeMm;
        for (int cz = 0; cz < 2; cz++)
        {
            for (int cx = 0; cx < 2; cx++)
            {
                WorldHeight.Gradient(in h, cx * S, cz * S, out long gx, out long gz);
                if (!WorldHeight.IsWalkableSlope(gx, gz, slopeMaxPm))
                {
                    return false;
                }
            }
        }

        int centre = WorldHeight.Sample(in h, S / 2, S / 2);
        int depth = centre < 0 ? -centre : 0;
        return depth < swimEnterMm;
    }

    private readonly struct TileMark
    {
        public readonly int Tx;
        public readonly int Tz;
        public readonly TileFlags Flag;

        public TileMark(int tx, int tz, TileFlags flag)
        {
            Tx = tx;
            Tz = tz;
            Flag = flag;
        }
    }
}
