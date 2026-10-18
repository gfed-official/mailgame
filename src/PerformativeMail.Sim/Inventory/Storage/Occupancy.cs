using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Storage;

/// <summary>
/// Per-container cell occupancy (ADR-0009 section 5; story grid-inventory/002): one <c>ulong</c> bitmap per row (bit = column,
/// row-major, so a width up to 64 fits; v1 defs are at most 20 wide) for the <c>fh</c> mask-and-compare footprint test, plus a
/// <c>cellOwner</c> <c>ushort</c> array (<see cref="Free"/> = 0xFFFF) for O(1) "what entry is under this cell". Both are
/// preallocated for every container slot at <c>containerCapacity * maxRows</c> rows and indexed by <see cref="ContainerTable"/> slot;
/// the container's own <c>w x h</c> is passed to each call for the bounds test. <see cref="Reset"/> is boot-time (container creation)
/// and throws on a grid larger than the preallocation; the tick-path members allocate nothing and never throw. Probe counters are
/// story 009's. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// var occ = new Occupancy(64, 16);
/// occ.Reset(containerSlot, 8, 2);
/// if (occ.Fits(containerSlot, 8, 2, x: 3, y: 0, fw: 2, fh: 1)) { occ.Place(containerSlot, 8, 2, 3, 0, 2, 1, entrySlot); }
/// ushort under = occ.OwnerAt(containerSlot, 8, 3, 0);   // entrySlot
/// </code>
/// </example>
public sealed class Occupancy
{
    /// <summary>The <c>cellOwner</c> value of a free cell.</summary>
    public const ushort Free = 0xFFFF;

    /// <summary>The widest grid a row bitmap can hold.</summary>
    public const int BitmapWidth = 64;

    private readonly ulong[] _rows;
    private readonly ushort[] _owners;
    private readonly int _containerCapacity;
    private readonly int _maxRows;
    private readonly int _maxWidth;

    /// <summary>Preallocates the bitmaps and owner cells for <paramref name="containerCapacity"/> containers of up to <paramref name="maxRows"/> x <paramref name="maxWidth"/>.</summary>
    /// <param name="containerCapacity">Container table slots, 1 to 65,535.</param>
    /// <param name="maxRows">Row cap (<c>inventory.maxRows</c>, CD12), at least 1.</param>
    /// <param name="maxWidth">Column cap, 1 to <see cref="BitmapWidth"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of range.</exception>
    public Occupancy(int containerCapacity, int maxRows, int maxWidth = BitmapWidth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(containerCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(containerCapacity, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWidth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxWidth, BitmapWidth);
        _containerCapacity = containerCapacity;
        _maxRows = maxRows;
        _maxWidth = maxWidth;
        _rows = new ulong[containerCapacity * maxRows];
        _owners = new ushort[containerCapacity * maxRows * maxWidth];
        Array.Fill(_owners, Free);
    }

    /// <summary>The number of container slots covered.</summary>
    public int ContainerCapacity => _containerCapacity;

    /// <summary>The row cap per container.</summary>
    public int MaxRows => _maxRows;

    /// <summary>The column cap per container.</summary>
    public int MaxWidth => _maxWidth;

    /// <summary>Clears every bit and owner of <paramref name="containerSlot"/> for a new <paramref name="w"/> x <paramref name="h"/> container (boot-time: container creation).</summary>
    /// <param name="containerSlot">The container table slot.</param>
    /// <param name="w">Columns, 0 to <see cref="MaxWidth"/>.</param>
    /// <param name="h">Rows, 0 to <see cref="MaxRows"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The slot is beyond the capacity, or the grid is larger than the preallocation.</exception>
    public void Reset(ushort containerSlot, int w, int h)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((int)containerSlot, _containerCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(w);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(w, _maxWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(h);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(h, _maxRows);
        Array.Clear(_rows, containerSlot * _maxRows, _maxRows);
        Array.Fill(_owners, Free, containerSlot * _maxRows * _maxWidth, _maxRows * _maxWidth);
    }

    /// <summary>The raw row bitmap (bit <c>x</c> set = cell <c>(x, y)</c> occupied).</summary>
    /// <param name="containerSlot">The container table slot.</param>
    /// <param name="y">A row below <see cref="MaxRows"/>.</param>
    /// <returns>The bitmap.</returns>
    public ulong RowBits(ushort containerSlot, int y) => _rows[containerSlot * _maxRows + y];

    /// <summary>
    /// True when an <paramref name="fw"/> x <paramref name="fh"/> footprint at (<paramref name="x"/>, <paramref name="y"/>) lies inside the
    /// <paramref name="w"/> x <paramref name="h"/> grid and covers no occupied cell: a bounds test, then one mask-and-compare per row.
    /// </summary>
    /// <param name="containerSlot">The container table slot.</param>
    /// <param name="w">The container's columns.</param>
    /// <param name="h">The container's rows.</param>
    /// <param name="x">Top-left column.</param>
    /// <param name="y">Top-left row.</param>
    /// <param name="fw">Effective footprint width.</param>
    /// <param name="fh">Effective footprint height.</param>
    /// <returns>True when the footprint fits.</returns>
    public bool Fits(ushort containerSlot, int w, int h, int x, int y, int fw, int fh)
    {
        if (!InBounds(w, h, x, y, fw, fh))
        {
            return false;
        }

        ulong mask = RowMask(x, fw);
        int row = containerSlot * _maxRows + y;
        for (int r = 0; r < fh; r++)
        {
            if ((_rows[row + r] & mask) != 0UL)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Marks the footprint occupied by <paramref name="entrySlot"/> (the caller checked <see cref="Fits"/>; asserted under <c>INVENTORY_ASSERT</c>).</summary>
    /// <param name="containerSlot">The container table slot.</param>
    /// <param name="w">The container's columns.</param>
    /// <param name="h">The container's rows.</param>
    /// <param name="x">Top-left column.</param>
    /// <param name="y">Top-left row.</param>
    /// <param name="fw">Effective footprint width.</param>
    /// <param name="fh">Effective footprint height.</param>
    /// <param name="entrySlot">The owning entry's pool slot (not <see cref="Free"/>).</param>
    public void Place(ushort containerSlot, int w, int h, int x, int y, int fw, int fh, ushort entrySlot)
    {
        InventoryAssert.That(InBounds(w, h, x, y, fw, fh), SimAssertCode.InventoryFootprintOutOfBounds);
        InventoryAssert.That(entrySlot != Free && Fits(containerSlot, w, h, x, y, fw, fh), SimAssertCode.InventoryCellConflict);
        ulong mask = RowMask(x, fw);
        int row = containerSlot * _maxRows + y;
        for (int r = 0; r < fh; r++)
        {
            _rows[row + r] |= mask;
            Array.Fill(_owners, entrySlot, (row + r) * _maxWidth + x, fw);
        }
    }

    /// <summary>Frees the footprint (every cell must be occupied; asserted under <c>INVENTORY_ASSERT</c>).</summary>
    /// <param name="containerSlot">The container table slot.</param>
    /// <param name="w">The container's columns.</param>
    /// <param name="h">The container's rows.</param>
    /// <param name="x">Top-left column.</param>
    /// <param name="y">Top-left row.</param>
    /// <param name="fw">Effective footprint width.</param>
    /// <param name="fh">Effective footprint height.</param>
    public void Clear(ushort containerSlot, int w, int h, int x, int y, int fw, int fh)
    {
        InventoryAssert.That(InBounds(w, h, x, y, fw, fh), SimAssertCode.InventoryFootprintOutOfBounds);
        ulong mask = RowMask(x, fw);
        int row = containerSlot * _maxRows + y;
        for (int r = 0; r < fh; r++)
        {
            InventoryAssert.That((_rows[row + r] & mask) == mask, SimAssertCode.InventoryCellConflict);
            _rows[row + r] &= ~mask;
            Array.Fill(_owners, Free, (row + r) * _maxWidth + x, fw);
        }
    }

    /// <summary>The entry slot occupying cell (<paramref name="x"/>, <paramref name="y"/>), or <see cref="Free"/> (also for a cell outside the <paramref name="w"/> x <see cref="MaxRows"/> area).</summary>
    /// <param name="containerSlot">The container table slot.</param>
    /// <param name="w">The container's columns.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The owning entry slot or <see cref="Free"/>.</returns>
    public ushort OwnerAt(ushort containerSlot, int w, int x, int y)
    {
        if (x < 0 || y < 0 || x >= w || y >= _maxRows)
        {
            return Free;
        }

        return _owners[(containerSlot * _maxRows + y) * _maxWidth + x];
    }

    private bool InBounds(int w, int h, int x, int y, int fw, int fh) =>
        x >= 0 && y >= 0 && fw >= 1 && fh >= 1 && w <= _maxWidth && h <= _maxRows && x + fw <= w && y + fh <= h;

    // fw == 64 would shift by a masked 0 in C#, so the full mask is special-cased (x is then 0 by the bounds test).
    private static ulong RowMask(int x, int fw) => fw >= BitmapWidth ? ulong.MaxValue : ((1UL << fw) - 1UL) << x;
}
