using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The dense uniform 2 m grid over <see cref="MapBounds"/> that indexes anchors (ADR-0014 section 1): <c>int[] cellHead</c> plus an
/// intrusive <c>int[] next</c> list per cell, both allocated once at construction (the 1 km map is 250 000 cells, about 1 MiB, on the
/// large object heap, so never reallocated). Each cell's list is kept in ascending anchor index, so a query iterates by ascending
/// cell then ascending anchor index with no sort. Points outside the bounds clamp into the edge cells. Not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var grid = new AnchorGrid(new MapBounds(0, 0, 256_000, 128_000), 4096);
/// grid.Link(0, grid.CellIndexOf(new Vec3Mm(5000, 0, 5000)));
/// </code>
/// </example>
internal sealed class AnchorGrid
{
    /// <summary>The cell edge in mm.</summary>
    public const int CellSizeMm = 2000;

    /// <summary>The most cells accepted (a 2 km by 2 km map is 1 000 000), so a bad bound cannot allocate gigabytes.</summary>
    private const long MaxCells = 4_000_000;

    private readonly int[] _cellHead;
    private readonly int[] _next;
    private readonly long _minXMm;
    private readonly long _minZMm;

    public AnchorGrid(MapBounds bounds, int capacity)
    {
        _minXMm = bounds.MinXMm;
        _minZMm = bounds.MinZMm;
        CellsX = CellCount(bounds.MaxXMm - bounds.MinXMm);
        CellsZ = CellCount(bounds.MaxZMm - bounds.MinZMm);
        long total = (long)CellsX * CellsZ;
        if (total > MaxCells)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "The anchor grid would exceed " + MaxCells + " cells.");
        }

        _cellHead = new int[total];
        Array.Fill(_cellHead, -1);
        _next = new int[capacity];
        Array.Fill(_next, -1);
    }

    /// <summary>Cells along X (at least 1).</summary>
    public int CellsX { get; }

    /// <summary>Cells along Z (at least 1).</summary>
    public int CellsZ { get; }

    /// <summary>The cell index <c>cz * CellsX + cx</c> containing <paramref name="p"/>, clamped into the edge cells.</summary>
    public int CellIndexOf(in Vec3Mm p) => (ClampCell(p.Z - _minZMm, CellsZ) * CellsX) + ClampCell(p.X - _minXMm, CellsX);

    /// <summary>Links <paramref name="index"/> into <paramref name="cell"/>'s list, keeping it in ascending index order.</summary>
    public void Link(int index, int cell)
    {
        int cur = _cellHead[cell];
        if (cur < 0 || cur > index)
        {
            _next[index] = cur;
            _cellHead[cell] = index;
            return;
        }

        while (true)
        {
            int following = _next[cur];
            if (following < 0 || following > index)
            {
                _next[index] = following;
                _next[cur] = index;
                return;
            }

            cur = following;
        }
    }

    /// <summary>Removes <paramref name="index"/> from <paramref name="cell"/>'s list (no-op when absent).</summary>
    public void Unlink(int index, int cell)
    {
        int cur = _cellHead[cell];
        if (cur == index)
        {
            _cellHead[cell] = _next[index];
            _next[index] = -1;
            return;
        }

        while (cur >= 0)
        {
            int following = _next[cur];
            if (following == index)
            {
                _next[cur] = _next[index];
                _next[index] = -1;
                return;
            }

            cur = following;
        }
    }

    /// <summary>
    /// Writes the anchor indices of every cell within <paramref name="radiusCells"/> of the cell containing <paramref name="point"/>
    /// (a square, clamped to the grid), ascending cell then ascending anchor index. Stops at <c>results.Length</c>, and reports
    /// the cells whose lists were walked.
    /// </summary>
    /// <returns>
    /// The number written; <paramref name="truncated"/> is set when more existed, and <paramref name="cellsVisited"/> counts each
    /// cell entered, the one a truncation stops in included.
    /// </returns>
    public int Query(in Vec3Mm point, int radiusCells, Span<int> results, out bool truncated, out int cellsVisited)
    {
        truncated = false;
        cellsVisited = 0;
        int cx = ClampCell(point.X - _minXMm, CellsX);
        int cz = ClampCell(point.Z - _minZMm, CellsZ);
        int x0 = Math.Max(0, cx - radiusCells);
        int x1 = Math.Min(CellsX - 1, cx + radiusCells);
        int z0 = Math.Max(0, cz - radiusCells);
        int z1 = Math.Min(CellsZ - 1, cz + radiusCells);
        int count = 0;
        for (int z = z0; z <= z1; z++)
        {
            int row = z * CellsX;
            for (int x = x0; x <= x1; x++)
            {
                cellsVisited++;
                for (int i = _cellHead[row + x]; i >= 0; i = _next[i])
                {
                    if (count == results.Length)
                    {
                        truncated = true;
                        return count;
                    }

                    results[count++] = i;
                }
            }
        }

        return count;
    }

    private static int CellCount(long spanMm)
    {
        long cells = spanMm <= 0 ? 1 : ((spanMm + CellSizeMm - 1) / CellSizeMm);
        return (int)Math.Min(cells, MaxCells);
    }

    private static int ClampCell(long offsetMm, int cells)
    {
        if (offsetMm <= 0)
        {
            return 0;
        }

        long c = offsetMm / CellSizeMm;
        return c >= cells ? cells - 1 : (int)c;
    }
}
