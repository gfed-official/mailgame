using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.World;

/// <summary>
/// The static 4 m bucket grid behind <see cref="IWorldQuery.QueryStaticSolids"/> (ADR-0026 section 5,
/// TR-throwing-007 world half). Built once by <see cref="WorldTablesBuilder"/> and immutable afterwards; the only
/// world-solid grid (ADR-0015 consumers must use this query, never a second grid).
/// <para>
/// Layout (CSR, X/Z only; Y is checked by the overlap test): bucket <c>(bx, bz)</c> covers
/// <c>[origin + b * 4000, origin + (b + 1) * 4000)</c> per axis from <see cref="MapBounds"/> Min, the last bucket
/// on each axis also holding the far bounds edge. <c>_bucketStart</c> has <c>buckets + 1</c> entries and
/// <c>_bucketItems[_bucketStart[b] .. _bucketStart[b + 1])</c> lists, strictly ascending, every solid whose closed
/// box touches bucket <c>b</c> (filled in index order, so ascending is free).
/// </para>
/// <para>
/// A query k-way merges the touched buckets' ascending lists (skipping equal heads, so no duplicates and nothing is
/// sorted) with the cursors in a <c>stackalloc</c> buffer of <see cref="MaxMergeBuckets"/>. A region touching more
/// buckets than that falls back to an ascending linear scan of all solids. Both paths allocate 0 B.
/// </para>
/// </summary>
internal sealed class StaticSolidGrid
{
    /// <summary>Bucket edge in mm (ADR-0026 section 5; TR-throwing-007 "static 4 m grid"). A code constant, not a tunable.</summary>
    internal const int BucketSizeMm = 4000;

    /// <summary>
    /// Most buckets a query merges with stack cursors (a region up to about 4 x 8 buckets, at least a 12 m x 28 m box).
    /// A region touching more buckets uses the linear scan and is counted in <see cref="CounterId.WorldSolidQueryFallback"/>.
    /// </summary>
    internal const int MaxMergeBuckets = 32;

    /// <summary>Most bucket entries (sum over solids of buckets touched) the grid may hold; 32 MB of <c>ushort</c>.</summary>
    internal const int MaxBucketEntries = 1 << 24;

    private readonly StaticSolid[] _solids;
    private readonly long _originX;
    private readonly long _originZ;
    private readonly long _maxX;
    private readonly long _maxZ;
    private readonly int _bucketsX;
    private readonly int _bucketsZ;
    private readonly int[] _bucketStart;
    private readonly ushort[] _bucketItems;

    private StaticSolidGrid(StaticSolid[] solids, in MapBounds bounds, int bucketsX, int bucketsZ, int[] bucketStart, ushort[] bucketItems)
    {
        _solids = solids;
        _originX = bounds.MinXMm;
        _originZ = bounds.MinZMm;
        _maxX = bounds.MaxXMm;
        _maxZ = bounds.MaxZMm;
        _bucketsX = bucketsX;
        _bucketsZ = bucketsZ;
        _bucketStart = bucketStart;
        _bucketItems = bucketItems;
    }

    /// <summary>Buckets along X.</summary>
    internal int BucketsX => _bucketsX;

    /// <summary>Buckets along Z.</summary>
    internal int BucketsZ => _bucketsZ;

    /// <summary>Total bucket entries (for tests).</summary>
    internal int EntryCount => _bucketItems.Length;

    /// <summary>
    /// The single closed-interval overlap test shared by the query and the brute-force test oracle: true iff the
    /// intersection of the two closed boxes is non-empty on every axis (<c>max(min) &lt;= min(max)</c>), so touching
    /// faces, edges and corners overlap and a box with Min &gt; Max on an axis overlaps nothing.
    /// </summary>
    /// <param name="a">First box.</param>
    /// <param name="b">Second box.</param>
    /// <returns>True when the closed boxes share at least one point.</returns>
    internal static bool Overlaps(in AabbMm a, in AabbMm b)
    {
        return Math.Max(a.Min.X, b.Min.X) <= Math.Min(a.Max.X, b.Max.X)
            && Math.Max(a.Min.Y, b.Min.Y) <= Math.Min(a.Max.Y, b.Max.Y)
            && Math.Max(a.Min.Z, b.Min.Z) <= Math.Min(a.Max.Z, b.Max.Z);
    }

    /// <summary>
    /// Builds the grid over <paramref name="bounds"/>. The caller has already checked every box is well-formed and
    /// inside the bounds; <paramref name="solids"/> is owned by the grid afterwards.
    /// </summary>
    /// <param name="solids">Solids in ascending index (index == position).</param>
    /// <param name="bounds">Validated map bounds (positive width and depth).</param>
    /// <returns>The frozen grid.</returns>
    /// <exception cref="SimBuildException">The bucket entries exceed <see cref="MaxBucketEntries"/> (<see cref="SimBuildErrorCode.WorldSolidGridTooLarge"/>).</exception>
    internal static StaticSolidGrid Build(StaticSolid[] solids, in MapBounds bounds)
    {
        int bucketsX = BucketCount(bounds.MaxXMm - bounds.MinXMm);
        int bucketsZ = BucketCount(bounds.MaxZMm - bounds.MinZMm);
        int[] start = new int[(bucketsX * bucketsZ) + 1];

        // Pass 1: count entries per bucket (stored at b + 1, then prefix-summed).
        long total = 0;
        for (int i = 0; i < solids.Length; i++)
        {
            Range(in solids[i].Box, in bounds, bucketsX, bucketsZ, out int x0, out int x1, out int z0, out int z1);
            total += (long)(x1 - x0 + 1) * (z1 - z0 + 1);
            if (total > MaxBucketEntries)
            {
                throw new SimBuildException(
                    SimBuildErrorCode.WorldSolidGridTooLarge,
                    "WorldTablesBuilder.Build: static solids touch more than " + MaxBucketEntries + " grid buckets in total (solid " + i + ").");
            }

            for (int bz = z0; bz <= z1; bz++)
            {
                for (int bx = x0; bx <= x1; bx++)
                {
                    start[(bz * bucketsX) + bx + 1]++;
                }
            }
        }

        for (int b = 1; b < start.Length; b++)
        {
            start[b] += start[b - 1];
        }

        // Pass 2: fill in ascending solid index, so each bucket list is strictly ascending.
        ushort[] items = new ushort[total];
        int[] fill = (int[])start.Clone();
        for (int i = 0; i < solids.Length; i++)
        {
            Range(in solids[i].Box, in bounds, bucketsX, bucketsZ, out int x0, out int x1, out int z0, out int z1);
            for (int bz = z0; bz <= z1; bz++)
            {
                for (int bx = x0; bx <= x1; bx++)
                {
                    items[fill[(bz * bucketsX) + bx]++] = (ushort)i;
                }
            }
        }

        return new StaticSolidGrid(solids, in bounds, bucketsX, bucketsZ, start, items);
    }

    /// <summary>
    /// Writes the ascending, duplicate-free indices of the solids whose closed box overlaps <paramref name="region"/>
    /// (<see cref="Overlaps"/>) into <paramref name="indices"/>, lowest indices first, and returns the count written.
    /// Never throws, never allocates.
    /// </summary>
    /// <param name="region">The query box (closed).</param>
    /// <param name="indices">Destination; when it fills before the last hit the result is truncated.</param>
    /// <param name="truncated">True when at least one more hit did not fit.</param>
    /// <param name="fellBack">True when the region touched more than <see cref="MaxMergeBuckets"/> buckets and the linear scan ran.</param>
    /// <returns>The number of indices written.</returns>
    internal int Query(in AabbMm region, Span<ushort> indices, out bool truncated, out bool fellBack)
    {
        truncated = false;
        fellBack = false;
        if (_solids.Length == 0
            || region.Min.X > region.Max.X || region.Min.Y > region.Max.Y || region.Min.Z > region.Max.Z
            || region.Max.X < _originX || region.Min.X > _maxX
            || region.Max.Z < _originZ || region.Min.Z > _maxZ)
        {
            return 0; // empty region, or disjoint from the bounds that contain every solid
        }

        int x0 = BucketOf(region.Min.X, _originX, _maxX, _bucketsX);
        int x1 = BucketOf(region.Max.X, _originX, _maxX, _bucketsX);
        int z0 = BucketOf(region.Min.Z, _originZ, _maxZ, _bucketsZ);
        int z1 = BucketOf(region.Max.Z, _originZ, _maxZ, _bucketsZ);
        if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > MaxMergeBuckets)
        {
            fellBack = true;
            return ScanAll(in region, indices, out truncated);
        }

        Span<int> pos = stackalloc int[MaxMergeBuckets];
        Span<int> end = stackalloc int[MaxMergeBuckets];
        int k = 0;
        for (int bz = z0; bz <= z1; bz++)
        {
            for (int bx = x0; bx <= x1; bx++)
            {
                int b = (bz * _bucketsX) + bx;
                if (_bucketStart[b] < _bucketStart[b + 1])
                {
                    pos[k] = _bucketStart[b];
                    end[k] = _bucketStart[b + 1];
                    k++;
                }
            }
        }

        return Merge(in region, indices, pos, end, k, out truncated);
    }

    private int Merge(in AabbMm region, Span<ushort> indices, Span<int> pos, Span<int> end, int k, out bool truncated)
    {
        truncated = false;
        ushort[] items = _bucketItems;
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

            if (Overlaps(in region, in _solids[min].Box))
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
        StaticSolid[] solids = _solids;
        for (int i = 0; i < solids.Length; i++)
        {
            if (!Overlaps(in region, in solids[i].Box))
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

    private static int BucketCount(long extentMm)
    {
        long n = (extentMm + BucketSizeMm - 1) / BucketSizeMm;
        return n < 1 ? 1 : (int)n;
    }

    /// <summary>Bucket of <paramref name="coord"/> after clamping it into <c>[origin, max]</c>; the far edge maps to the last bucket.</summary>
    private static int BucketOf(long coord, long origin, long max, int buckets)
    {
        long c = coord < origin ? origin : (coord > max ? max : coord);
        var sink = default(SimCounterSink); // divisor is a positive constant: nothing to count
        long b = Fix.FloorDiv(c - origin, BucketSizeMm, ref sink);
        return b >= buckets ? buckets - 1 : (int)b;
    }

    private static void Range(in AabbMm box, in MapBounds bounds, int bucketsX, int bucketsZ, out int x0, out int x1, out int z0, out int z1)
    {
        x0 = BucketOf(box.Min.X, bounds.MinXMm, bounds.MaxXMm, bucketsX);
        x1 = BucketOf(box.Max.X, bounds.MinXMm, bounds.MaxXMm, bucketsX);
        z0 = BucketOf(box.Min.Z, bounds.MinZMm, bounds.MaxZMm, bucketsZ);
        z1 = BucketOf(box.Max.Z, bounds.MinZMm, bounds.MaxZMm, bucketsZ);
    }
}
