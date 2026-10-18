using System.Buffers;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.World;

/// <summary>
/// The single <see cref="IWorldQuery"/> implementation (ADR-0026 section 1): immutable after
/// <see cref="WorldTablesBuilder.Build(int, int)"/>, with no mutating member and no stored water depth
/// (water is derived from the height field). Reads are thread-safe except <see cref="QueryStaticSolids"/> when counters are bound (see below).
/// <para>
/// Beyond the contract, the tables own
/// the small immutable world string table (address and street display text, <see cref="StringNamespace.Run"/>), read
/// through <see cref="TryGetDisplayText"/> by Presentation; the Sim itself never formats or reads these strings.
/// </para>
/// <para>
/// Dump (ADR-0026 section 9, story 007): the tables are an <see cref="ISimStateContributor"/> for <see cref="SectionIds.World"/>.
/// The payload (see <see cref="SectionIds.World"/> for the field order) is immutable, so the builder encodes it once at the end of
/// <c>Build</c> and every dump copies the cached bytes. The display text table and the street table are not dumped (strings never
/// enter the dump; the authored street table only affects Presentation's <c>StreetNameAt</c> and is covered by the replay's
/// <see cref="IWorldSource.Version"/>, which must be bumped on any authored change).
/// Thread safety: concurrent reads are safe, except that <see cref="QueryStaticSolids"/> writes the bound
/// <see cref="SimCounters"/> (if any, see <see cref="WorldTablesBuilder.SetCounters"/>) and must then run on the Sim tick thread.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var b = new WorldTablesBuilder();
/// b.SetCorners(1, 1, new[] { -1000, -1000, -200, -200 });
/// WorldTables w = b.Build();
/// int depth = w.WaterDepthMm(500, 1000);   // 600
/// </code>
/// </example>
public sealed class WorldTables : IWorldQuery, ISimStateContributor
{
    private const long S = WorldConstants.TileSizeMm;

    private readonly int[] _cornerMm;
    private readonly int _tilesX;
    private readonly int _tilesZ;
    private readonly int _cornerStride;
    private readonly MapBounds _bounds;
    private readonly byte[] _flags;
    private readonly StaticSolid[] _solids;
    private readonly StaticSolidGrid _grid;
    private readonly SimCounters? _counters;
    private readonly LotRecord[] _lots;
    private readonly WorldStreet[] _streets;
    private readonly string[] _displayText;
    private readonly PostOfficeRecord _postOffice;
    private readonly ArrayBufferWriter<byte> _dumpCache = new(); // filled once by SealDumpCache; the field itself never changes

    /// <summary>
    /// Takes ownership of the arrays and the grid (the builder passes private copies; flags are row-major, <c>tz * tilesX + tx</c>).
    /// <paramref name="lots"/> is filled by the builder right after construction (anchors need the height field), before
    /// the tables are returned; <paramref name="streets"/> is ascending by (district, street); <paramref name="postOffice"/> is
    /// <c>default</c> when the world has none (legacy overloads only).
    /// </summary>
    internal WorldTables(
        int tilesX,
        int tilesZ,
        int[] cornerMm,
        byte[] flags,
        MapBounds bounds,
        StaticSolid[] solids,
        StaticSolidGrid grid,
        SimCounters? counters,
        LotRecord[] lots,
        WorldStreet[] streets,
        string[] displayText,
        PostOfficeRecord postOffice)
    {
        _postOffice = postOffice;
        _lots = lots;
        _streets = streets;
        _displayText = displayText;
        _solids = solids;
        _grid = grid;
        _counters = counters;
        _tilesX = tilesX;
        _tilesZ = tilesZ;
        _cornerStride = tilesX + 1;
        _cornerMm = cornerMm;
        _flags = flags;
        _bounds = bounds;
    }

    /// <inheritdoc />
    public ushort SectionId => SectionIds.World;

    /// <inheritdoc />
    /// <remarks>Copies the payload cached at the end of <c>Build</c>; allocation-free. Byte-identical to <see cref="WriteFields"/>.</remarks>
    public void Write(ref CanonicalWriter w)
    {
        System.Diagnostics.Debug.Assert(_dumpCache.WrittenCount > 0, "WorldTables.Write before SealDumpCache: the builder seals the cache at the end of Build.");
        w.WriteBytes(_dumpCache.WrittenSpan);
    }

    /// <summary>Bytes of the section framing <see cref="CanonicalWriter.BeginSection"/> writes before a payload: <c>id u16</c> + <c>length u32</c>.</summary>
    internal const int SectionFrameBytes = 6;

    /// <summary>
    /// Encodes the dump payload once and keeps it. Called by the builder as the last step of <c>Build</c> (lots are filled by then);
    /// the section framing (<c>id u16, length u32</c>) written by <see cref="CanonicalWriter"/> is sliced off.
    /// </summary>
    internal void SealDumpCache()
    {
        var output = new ArrayBufferWriter<byte>(4096);
        var w = new CanonicalWriter(output, new CanonicalBuffer());
        w.BeginSection(SectionIds.World);
        WriteFields(ref w);
        w.EndSection();
        if (_dumpCache.WrittenCount == 0)
        {
            _dumpCache.Write(output.WrittenSpan.Slice(SectionFrameBytes));
        }
    }

    /// <summary>
    /// Writes the dump payload field by field, little-endian, in index order: bounds (4 x i64: MinX, MinZ, MaxX, MaxZ); TilesX, TilesZ (i32);
    /// corner heights (u32 count, then i32 each in row-major order, stride TilesX + 1, index <c>cz * (TilesX + 1) + cx</c>); flags (u32 count,
    /// then u8 each, index <c>tz * TilesX + tx</c>); solids (u32 count, then Index u16, Owner u32, Box Min XYZ and Max XYZ as 6 x i32, Kind u8);
    /// lots (u32 count, then every <see cref="LotRecord"/> field in declaration order, <see cref="StringId"/> as ns u8 + index u16);
    /// the Post Office (door, intake, pad centre as 3 x i32 each, ring radius i32, slot count u8). Never a struct blit, never a string.
    /// Used to build the cache and by tests as the cache guard.
    /// </summary>
    /// <param name="w">The writer, with a section open.</param>
    internal void WriteFields(ref CanonicalWriter w)
    {
        w.WriteI64(_bounds.MinXMm);
        w.WriteI64(_bounds.MinZMm);
        w.WriteI64(_bounds.MaxXMm);
        w.WriteI64(_bounds.MaxZMm);
        w.WriteI32(_tilesX);
        w.WriteI32(_tilesZ);

        w.WriteU32((uint)_cornerMm.Length);
        for (int i = 0; i < _cornerMm.Length; i++)
        {
            w.WriteI32(_cornerMm[i]);
        }

        w.WriteU32((uint)_flags.Length);
        for (int i = 0; i < _flags.Length; i++)
        {
            w.WriteU8(_flags[i]);
        }

        w.WriteU32((uint)_solids.Length);
        for (int i = 0; i < _solids.Length; i++)
        {
            StaticSolid s = _solids[i];
            w.WriteU16(s.Index);
            w.WriteU32(s.Owner.Value);
            WriteVec(ref w, in s.Box.Min);
            WriteVec(ref w, in s.Box.Max);
            w.WriteU8((byte)s.Kind);
        }

        w.WriteU32((uint)_lots.Length);
        for (int i = 0; i < _lots.Length; i++)
        {
            LotRecord l = _lots[i];
            w.WriteU32(l.Destination.Value);
            w.WriteU32(l.Address.Value);
            w.WriteU8(l.DistrictId);
            w.WriteU8(l.StreetId);
            w.WriteU16(l.Number);
            w.WriteU8((byte)l.Display.Ns);
            w.WriteU16(l.Display.Index);
            w.WriteI32(l.MailboxTile.Tx);
            w.WriteI32(l.MailboxTile.Tz);
            w.WriteU8((byte)l.Face);
            WriteVec(ref w, in l.Anchor);
            w.WriteI32(l.ApproachTile.Tx);
            w.WriteI32(l.ApproachTile.Tz);
            WriteVec(ref w, in l.TruckStopPoint);
            w.WriteU16(l.HouseSolid);
        }

        WriteVec(ref w, in _postOffice.DoorAnchor);
        WriteVec(ref w, in _postOffice.IntakeAnchor);
        WriteVec(ref w, in _postOffice.PadCentre);
        w.WriteI32(_postOffice.PadRingRadiusMm);
        w.WriteU8(_postOffice.PadSlots);
    }

    private static void WriteVec(ref CanonicalWriter w, in Vec3Mm v)
    {
        w.WriteI32(v.X);
        w.WriteI32(v.Y);
        w.WriteI32(v.Z);
    }

    /// <inheritdoc />
    public int TileSizeMm => WorldConstants.TileSizeMm;

    /// <inheritdoc />
    public int TilesX => _tilesX;

    /// <inheritdoc />
    public int TilesZ => _tilesZ;

    /// <inheritdoc />
    public MapBounds Bounds => _bounds;

    /// <inheritdoc />
    public ReadOnlySpan<StaticSolid> StaticSolids => _solids;

    /// <inheritdoc />
    public ReadOnlySpan<LotRecord> Lots => _lots;

    /// <inheritdoc />
    public PostOfficeRecord PostOffice => _postOffice;

    /// <inheritdoc />
    public long TerrainHeightMm(long xMm, long zMm)
    {
        return WorldHeight.At(this, SaturateToInt(xMm), SaturateToInt(zMm));
    }

    /// <inheritdoc />
    public void GetCorners(int tx, int tz, out HeightCorners h)
    {
        int cx = tx < 0 ? 0 : (tx > _tilesX - 1 ? _tilesX - 1 : tx);
        int cz = tz < 0 ? 0 : (tz > _tilesZ - 1 ? _tilesZ - 1 : tz);
        int i = (cz * _cornerStride) + cx;
        int[] c = _cornerMm;
        h = new HeightCorners(c[i], c[i + 1], c[i + _cornerStride], c[i + _cornerStride + 1]);
    }

    /// <inheritdoc />
    public void TerrainGradientPm(long xMm, long zMm, out int gxPm, out int gzPm)
    {
        WorldHeight.Locate(this, SaturateToInt(xMm), SaturateToInt(zMm), out HeightCorners h, out int fx, out int fz);
        WorldHeight.Gradient(in h, fx, fz, out long gx, out long gz);
        var sink = default(SimCounterSink);
        gxPm = (int)Fix.MulDivRH(gx, 1000, WorldConstants.TileAreaMm2, ref sink);
        gzPm = (int)Fix.MulDivRH(gz, 1000, WorldConstants.TileAreaMm2, ref sink);
    }

    /// <inheritdoc />
    public int WaterDepthMm(long xMm, long zMm)
    {
        long h = TerrainHeightMm(xMm, zMm);
        return h < 0 ? (int)(-h) : 0;
    }

    /// <inheritdoc />
    /// <remarks>A tile outside the map reports <see cref="TileFlags.Blocked"/> (never walkable); no other flag is set for it.</remarks>
    public TileFlags Tile(int tx, int tz)
    {
        if (tx < 0 || tx >= _tilesX || tz < 0 || tz >= _tilesZ)
        {
            return TileFlags.Blocked;
        }

        return (TileFlags)_flags[(tz * _tilesX) + tx];
    }

    /// <inheritdoc />
    /// <remarks>
    /// X and Z are clamped into <c>[Min + inset, Max - inset]</c>; Y is unchanged. A negative inset counts as 0.
    /// If the inset is so large that the range is empty on an axis (<c>Min + inset &gt; Max - inset</c>) that
    /// axis becomes the midpoint of the bounds (floor division).
    /// </remarks>
    public Vec3Mm ClampInsideMap(in Vec3Mm p, int insetMm)
    {
        long inset = insetMm < 0 ? 0 : insetMm;
        return new Vec3Mm(
            ClampAxis(p.X, _bounds.MinXMm, _bounds.MaxXMm, inset),
            p.Y,
            ClampAxis(p.Z, _bounds.MinZMm, _bounds.MaxZMm, inset));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Scans the tile centres inside <c>[p - r, p + r]</c> in ascending <c>tz</c> then ascending <c>tx</c>, keeping the
    /// first strict improvement of the squared mm distance (ties therefore go to the lower <c>tz</c>, then the lower
    /// <c>tx</c>). A centre exactly <paramref name="radiusMm"/> away counts. Returns false with a default
    /// <paramref name="snapped"/> when the radius is negative or no walkable centre is in range. Allocation-free.
    /// </remarks>
    public bool TrySnapWalkable(in Vec3Mm p, int radiusMm, out Vec3Mm snapped)
    {
        snapped = default;
        if (radiusMm < 0)
        {
            return false;
        }

        long r = radiusMm;
        long half = S / 2;
        var sink = default(SimCounterSink);

        // Tile t has its centre at t * S + S/2; keep t with px - r <= centre <= px + r.
        long txLo = -Fix.FloorDiv(-(p.X - r - half), S, ref sink); // ceil
        long txHi = Fix.FloorDiv(p.X + r - half, S, ref sink);
        long tzLo = -Fix.FloorDiv(-(p.Z - r - half), S, ref sink);
        long tzHi = Fix.FloorDiv(p.Z + r - half, S, ref sink);
        if (txLo < 0)
        {
            txLo = 0;
        }

        if (tzLo < 0)
        {
            tzLo = 0;
        }

        if (txHi > _tilesX - 1)
        {
            txHi = _tilesX - 1;
        }

        if (tzHi > _tilesZ - 1)
        {
            tzHi = _tilesZ - 1;
        }

        long limit = r * r;
        long bestD2 = long.MaxValue;
        long bestX = 0;
        long bestZ = 0;
        bool found = false;
        for (long tz = tzLo; tz <= tzHi; tz++)
        {
            long cz = (tz * S) + half;
            long dz = cz - p.Z;
            for (long tx = txLo; tx <= txHi; tx++)
            {
                if ((_flags[(int)((tz * _tilesX) + tx)] & (byte)TileFlags.Walkable) == 0)
                {
                    continue;
                }

                long cx = (tx * S) + half;
                long dx = cx - p.X;
                long d2 = (dx * dx) + (dz * dz);
                if (d2 <= limit && d2 < bestD2)
                {
                    bestD2 = d2;
                    bestX = cx;
                    bestZ = cz;
                    found = true;
                }
            }
        }

        if (!found)
        {
            return false;
        }

        snapped = new Vec3Mm((int)bestX, (int)TerrainHeightMm(bestX, bestZ), (int)bestZ);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Merges at most <c>StaticSolidGrid.MaxMergeBuckets</c> (32) touched buckets with stack cursors; a larger region
    /// falls back to an ascending linear scan of <see cref="StaticSolids"/> (same result) and counts
    /// <see cref="CounterId.WorldSolidQueryFallback"/>. Counters are written only when bound at build.
    /// </remarks>
    public int QueryStaticSolids(in AabbMm region, Span<ushort> indices)
    {
        int written = _grid.Query(in region, indices, out bool truncated, out bool fellBack);
        if (_counters is not null)
        {
            if (truncated)
            {
                _counters.Add(CounterId.WorldSolidQueryOverflow);
            }

            if (fellBack)
            {
                _counters.Add(CounterId.WorldSolidQueryFallback);
            }
        }

        return written;
    }

    /// <summary>
    /// The display name of the street whose footprint contains (x, z) on X/Z (closed: edges count), or
    /// <see cref="StringId.None"/> off every street. Where footprints overlap, the street with the lowest
    /// (district, street) wins. O(streets), allocation-free.
    /// </summary>
    /// <param name="xMm">World X in mm.</param>
    /// <param name="zMm">World Z in mm.</param>
    /// <returns>The street's display string or None.</returns>
    public StringId StreetNameAt(long xMm, long zMm)
    {
        for (int i = 0; i < _streets.Length; i++)
        {
            if (_streets[i].Contains(xMm, zMm))
            {
                return _streets[i].Display;
            }
        }

        return StringId.None;
    }

    /// <summary>
    /// Looks up the text of a world display string (an address or street name added with
    /// <see cref="WorldTablesBuilder.AddDisplayString"/>). For Presentation; the Sim never reads it.
    /// </summary>
    /// <param name="id">The string id (namespace <see cref="StringNamespace.Run"/>).</param>
    /// <param name="text">The text when found, else the empty string.</param>
    /// <returns>True when <paramref name="id"/> is in the world string table.</returns>
    /// <example><code>if (world.TryGetDisplayText(lot.Display, out string label)) { /* render */ }</code></example>
    public bool TryGetDisplayText(StringId id, out string text)
    {
        if (id.Ns == StringNamespace.Run && id.Index < _displayText.Length)
        {
            text = _displayText[id.Index];
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static int ClampAxis(int value, long min, long max, long inset)
    {
        long lo = min + inset;
        long hi = max - inset;
        if (lo > hi)
        {
            return (int)((min + max) >> 1); // floor midpoint
        }

        return (int)(value < lo ? lo : (value > hi ? hi : value));
    }

    private static int SaturateToInt(long v)
    {
        if (v > int.MaxValue)
        {
            return int.MaxValue;
        }

        return v < int.MinValue ? int.MinValue : (int)v;
    }
}
