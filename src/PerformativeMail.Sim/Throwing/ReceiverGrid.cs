using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The static 4 m grid of mailbox catch volumes (ADR-0015 section 4; GDD F4 "Broad phase"; TR-throwing-007). The only Throwing
/// type that stores receiver indices per cell: world solids come from <c>IWorldQuery.QueryStaticSolids</c>, never a second grid
/// (ADR-0026 section 5), so <see cref="Build"/> takes volumes only. Built once at run start; <see cref="Build"/> may be called
/// again when M-1b's registry versions change (constructs and belts join then).
/// <para>
/// Layout (CSR over X/Z; Y is left to the overlap test): cell <c>(cx, cz)</c> covers <c>[min + c * 4000, min + (c + 1) * 4000)</c>
/// per axis from <see cref="MapBounds"/> Min, coordinates outside the bounds clamp onto the edge cells. Each cell lists, strictly
/// ascending, the ordinal of every volume whose closed box touches it. A query k-way merges the touched cells' lists (equal heads
/// advance together, so no duplicates and nothing is sorted) and keeps the volumes whose closed box overlaps the region; a region
/// touching more than <see cref="MaxMergeCells"/> cells scans every volume in ordinal order instead. Both paths allocate 0 B.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var grid = new ReceiverGrid(world.Bounds);
/// grid.Build(volumes);
/// int n = grid.Query(in region, indices, out bool truncated);   // ascending ordinals
/// </code>
/// </example>
internal sealed class ReceiverGrid
{
    /// <summary>Cell edge in mm (TR-throwing-007 "static 4 m grid"). A code constant, not a tunable.</summary>
    internal const int CellSizeMm = 4000;

    /// <summary>
    /// Most cells a query merges with stack cursors (4 x 4); a flight segment, under 4 m per axis, touches at most 2 x 2. A larger
    /// region scans every volume. Mail Dev default, pending Evan (TD-202 item 3): the cap and the scan fallback (World's grid uses 32).
    /// </summary>
    internal const int MaxMergeCells = 16;

    private readonly long _minX;
    private readonly long _minZ;
    private readonly long _maxX;
    private readonly long _maxZ;
    private readonly int _cellsX;
    private readonly int _cellsZ;
    private readonly int[] _cellStart;
    private VolumeReceiver[] _volumes = [];
    private ushort[] _cellItems = [];

    /// <summary>
    /// Creates an empty grid over the map. Mail Dev default, pending Evan (TD-202 item 4): the bounds come in here so that
    /// <see cref="Build"/> keeps the story's one-parameter <c>Build(ReadOnlySpan&lt;VolumeReceiver&gt;)</c> shape and can be re-run.
    /// </summary>
    /// <param name="bounds">The map bounds (normally <c>IWorldQuery.Bounds</c>).</param>
    internal ReceiverGrid(MapBounds bounds)
    {
        _minX = bounds.MinXMm;
        _minZ = bounds.MinZMm;
        _maxX = bounds.MaxXMm;
        _maxZ = bounds.MaxZMm;
        _cellsX = CellCount(_maxX - _minX);
        _cellsZ = CellCount(_maxZ - _minZ);
        _cellStart = new int[(_cellsX * _cellsZ) + 1];
    }

    /// <summary>The number of volumes in the grid.</summary>
    internal int Count => _volumes.Length;

    /// <summary>The volume at <paramref name="ordinal"/>.</summary>
    /// <param name="ordinal">0 to <see cref="Count"/> - 1.</param>
    /// <returns>The volume.</returns>
    internal ref readonly VolumeReceiver Volume(int ordinal) => ref _volumes[ordinal];

    /// <summary>(Re)fills the cells from <paramref name="volumes"/>, ordinal = position. Run start only: allocates.</summary>
    /// <param name="volumes">The catch volumes in ascending ordinal; at most 65 536.</param>
    /// <exception cref="ArgumentOutOfRangeException">More than 65 536 volumes.</exception>
    internal void Build(ReadOnlySpan<VolumeReceiver> volumes)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(volumes.Length, ushort.MaxValue + 1, nameof(volumes));
        int[] start = _cellStart;
        Array.Clear(start);

        // Pass 1: count entries per cell (stored at c + 1, then prefix-summed).
        int total = 0;
        foreach (ref readonly VolumeReceiver v in volumes)
        {
            Range(in v.Box, out int x0, out int x1, out int z0, out int z1);
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    start[(cz * _cellsX) + cx + 1]++;
                    total++;
                }
            }
        }

        for (int c = 1; c < start.Length; c++)
        {
            start[c] += start[c - 1];
        }

        // Pass 2: fill in ascending ordinal, so each cell list is strictly ascending.
        var items = new ushort[total];
        int[] fill = (int[])start.Clone();
        for (int i = 0; i < volumes.Length; i++)
        {
            Range(in volumes[i].Box, out int x0, out int x1, out int z0, out int z1);
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    items[fill[(cz * _cellsX) + cx]++] = (ushort)i;
                }
            }
        }

        _volumes = volumes.ToArray();
        _cellItems = items;
    }

    /// <summary>
    /// Writes the ascending, duplicate-free ordinals of the volumes whose closed box overlaps <paramref name="region"/> (touching
    /// faces, edges and corners count) into <paramref name="indices"/>, lowest first. Never throws, never allocates.
    /// </summary>
    /// <param name="region">The query box (closed).</param>
    /// <param name="indices">Destination; its length caps the result.</param>
    /// <param name="truncated">True when at least one more hit did not fit.</param>
    /// <returns>The number of ordinals written.</returns>
    internal int Query(in AabbMm region, Span<ushort> indices, out bool truncated)
    {
        truncated = false;
        if (_volumes.Length == 0)
        {
            return 0;
        }

        int x0 = CellOf(region.Min.X, _minX, _maxX, _cellsX);
        int x1 = CellOf(region.Max.X, _minX, _maxX, _cellsX);
        int z0 = CellOf(region.Min.Z, _minZ, _maxZ, _cellsZ);
        int z1 = CellOf(region.Max.Z, _minZ, _maxZ, _cellsZ);
        if (x1 < x0 || z1 < z0)
        {
            return 0; // inverted region
        }

        if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > MaxMergeCells)
        {
            return ScanAll(in region, indices, out truncated);
        }

        Span<int> pos = stackalloc int[MaxMergeCells];
        Span<int> end = stackalloc int[MaxMergeCells];
        int k = 0;
        for (int cz = z0; cz <= z1; cz++)
        {
            for (int cx = x0; cx <= x1; cx++)
            {
                int c = (cz * _cellsX) + cx;
                if (_cellStart[c] < _cellStart[c + 1])
                {
                    pos[k] = _cellStart[c];
                    end[k] = _cellStart[c + 1];
                    k++;
                }
            }
        }

        return Merge(in region, indices, pos, end, k, out truncated);
    }

    /// <summary>Closed-box overlap: the intersection is non-empty on every axis.</summary>
    /// <param name="a">First box.</param>
    /// <param name="b">Second box.</param>
    /// <returns>True when the closed boxes share a point.</returns>
    internal static bool Overlaps(in AabbMm a, in AabbMm b) =>
        Axis(a.Min.X, a.Max.X, b.Min.X, b.Max.X) && Axis(a.Min.Y, a.Max.Y, b.Min.Y, b.Max.Y) && Axis(a.Min.Z, a.Max.Z, b.Min.Z, b.Max.Z);

    private static bool Axis(int aMin, int aMax, int bMin, int bMax) =>
        (aMin > bMin ? aMin : bMin) <= (aMax < bMax ? aMax : bMax);

    private int Merge(in AabbMm region, Span<ushort> indices, Span<int> pos, Span<int> end, int k, out bool truncated)
    {
        truncated = false;
        ushort[] items = _cellItems;
        int written = 0;
        while (k > 0)
        {
            int min = int.MaxValue;
            for (int i = 0; i < k; i++)
            {
                int v = items[pos[i]];
                if (v < min)
                {
                    min = v;
                }
            }

            // Advance every cursor whose head is the minimum (dedupe); drop exhausted cursors by swap-remove.
            int c = 0;
            while (c < k)
            {
                if (items[pos[c]] == min && ++pos[c] == end[c])
                {
                    k--;
                    pos[c] = pos[k];
                    end[c] = end[k];
                    continue;
                }

                c++;
            }

            if (Overlaps(in region, in _volumes[min].Box))
            {
                if (written == indices.Length)
                {
                    truncated = true;
                    return written;
                }

                indices[written++] = (ushort)min;
            }
        }

        return written;
    }

    private int ScanAll(in AabbMm region, Span<ushort> indices, out bool truncated)
    {
        truncated = false;
        int written = 0;
        VolumeReceiver[] volumes = _volumes;
        for (int i = 0; i < volumes.Length; i++)
        {
            if (!Overlaps(in region, in volumes[i].Box))
            {
                continue;
            }

            if (written == indices.Length)
            {
                truncated = true;
                return written;
            }

            indices[written++] = (ushort)i;
        }

        return written;
    }

    private void Range(in AabbMm box, out int x0, out int x1, out int z0, out int z1)
    {
        x0 = CellOf(box.Min.X, _minX, _maxX, _cellsX);
        x1 = CellOf(box.Max.X, _minX, _maxX, _cellsX);
        z0 = CellOf(box.Min.Z, _minZ, _maxZ, _cellsZ);
        z1 = CellOf(box.Max.Z, _minZ, _maxZ, _cellsZ);
    }

    private static int CellCount(long extentMm)
    {
        var sink = default(SimCounterSink); // the divisor is a positive constant: nothing to count
        long n = Fix.FloorDiv(extentMm + CellSizeMm - 1, CellSizeMm, ref sink);
        return n < 1 ? 1 : (int)n;
    }

    /// <summary>Cell of <paramref name="coord"/> after clamping it into <c>[min, max]</c>; the far edge maps to the last cell.</summary>
    private static int CellOf(long coord, long min, long max, int cells)
    {
        long c = coord < min ? min : (coord > max ? max : coord);
        var sink = default(SimCounterSink); // the divisor is a positive constant: nothing to count
        long cell = Fix.FloorDiv(c - min, CellSizeMm, ref sink);
        return cell >= cells ? cells - 1 : (int)cell;
    }
}
