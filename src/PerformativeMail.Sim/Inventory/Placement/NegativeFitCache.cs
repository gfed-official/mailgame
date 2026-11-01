namespace PerformativeMail.Sim.Inventory.Placement;

/// <summary>
/// The negative-fit cache (ADR-0009 section 5; GDD R8; story grid-inventory/009): per container, <c>(footprintShape, rotated) →
/// versionNoFit</c>. PASS U / PASS R skip a shape that found no free anchor in the container at its current <c>version</c>, so a
/// repeat AutoPlace of that shape visits 0 anchors there. A record is valid only while the container's version equals its
/// <c>versionNoFit</c> (any mutation bumps the version); additionally the container's records are cleared when its <c>statEpoch</c>
/// or its row count changes (R15 growth) and by <see cref="Clear"/> (called by <see cref="InventoryMutator.RebuildIndex"/>) —
/// clearing can only skip less, so it agrees with the ADR.
/// <para>
/// Rules (ADR-0009 section 2): a handler's <c>Validate</c> (AutoPlace planning) only <b>reads</b> it; records are written on the
/// <c>Commit</c> path and by <c>IInventoryApi</c>-style callers after a rejected plan (<see cref="AutoPlace.RecordNoFits"/>). A
/// derived memo: never in the canonical dump, empty after load, never changes a result (a cache-disabled run is identical).
/// </para>
/// <para>
/// Layout: a fixed number of ways (records) per container (the content's distinct footprint shapes times two rotations, rounded up to a
/// power of two), probed linearly from the shape's home way; a full set overwrites the home way (a memo may forget). Allocates
/// nothing after construction; never throws on the tick path. Not thread-safe: Sim tick thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// if (!cache.IsNoFit(slot, in header, 2, 4, rotated: false)) { /* scan the anchors */ }
/// cache.RecordNoFit(slot, in header, 2, 4, rotated: false, versionNoFit: header.Version);   // Commit path only
/// </code>
/// </example>
internal sealed class NegativeFitCache
{
    private readonly int _ways;
    private readonly int _wayMask;
    private readonly uint[] _shapes; // (w << 9 | h << 1 | rotated) + 1; 0 = empty
    private readonly uint[] _versions;
    private readonly uint[] _epochs; // per container: the statEpoch the records were written under
    private readonly ushort[] _rows; // per container: the row count the records were written under

    /// <summary>Preallocates <paramref name="ways"/> records for each of <paramref name="containerCapacity"/> containers.</summary>
    /// <param name="containerCapacity">The container table's capacity.</param>
    /// <param name="ways">Records per container; rounded up to a power of two, at least 4.</param>
    /// <exception cref="ArgumentOutOfRangeException">A capacity is below 1.</exception>
    public NegativeFitCache(int containerCapacity, int ways)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(containerCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ways, 1);
        int w = 4;
        while (w < ways)
        {
            w <<= 1;
        }

        _ways = w;
        _wayMask = w - 1;
        _shapes = new uint[containerCapacity * w];
        _versions = new uint[containerCapacity * w];
        _epochs = new uint[containerCapacity];
        _rows = new ushort[containerCapacity];
    }

    /// <summary>
    /// False disables the cache (every read misses, every write is dropped): the property test's "cache disabled" run, which must give
    /// identical plans, replies and state. True by default.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The number of distinct record shapes a content set needs: its distinct unrotated mail-kind and item footprints, times two
    /// rotations (the <c>ways</c> argument the module passes).
    /// </summary>
    /// <param name="content">The content set.</param>
    /// <returns>At least 1.</returns>
    public static int WaysFor(Content.ContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ReadOnlySpan<Content.MailKindDef> kinds = content.MailKinds.AsSpan();
        ReadOnlySpan<Content.ItemDef> items = content.Items.AsSpan();
        Span<int> seen = stackalloc int[kinds.Length + items.Length];
        int distinct = 0;
        for (int i = 0; i < kinds.Length; i++)
        {
            distinct = AddDistinct(seen, distinct, (kinds[i].GridW << 9) | kinds[i].GridH);
        }

        for (int i = 0; i < items.Length; i++)
        {
            distinct = AddDistinct(seen, distinct, (items[i].GridW << 9) | items[i].GridH);
        }

        return Math.Max(1, distinct * 2);
    }

    /// <summary>
    /// True when (<paramref name="w"/> x <paramref name="h"/>, <paramref name="rotated"/>) found no fit in the container at its current
    /// <c>version</c>, under its current <c>statEpoch</c> and row count. Pure.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="header">The container's header (current version, statEpoch, rows).</param>
    /// <param name="w">The unrotated footprint width.</param>
    /// <param name="h">The unrotated footprint height.</param>
    /// <param name="rotated">The pass's rotation.</param>
    /// <returns>True when the shape is known not to fit now.</returns>
    public bool IsNoFit(ushort containerSlot, in ContainerHeader header, byte w, byte h, bool rotated)
    {
        if (!Enabled || _epochs[containerSlot] != header.StatEpoch || _rows[containerSlot] != header.H)
        {
            return false;
        }

        int way = Find(containerSlot, ShapeOf(w, h, rotated));
        return way >= 0 && _versions[way] == header.Version;
    }

    /// <summary>
    /// Records that (<paramref name="w"/> x <paramref name="h"/>, <paramref name="rotated"/>) found no fit at
    /// <paramref name="versionNoFit"/>. Clears the container's records first when its <c>statEpoch</c> or row count differs from the
    /// one they were written under. Commit path and <c>IInventoryApi</c>-style callers only, never inside <c>Validate</c>.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="header">The container's header (its current statEpoch and rows).</param>
    /// <param name="w">The unrotated footprint width.</param>
    /// <param name="h">The unrotated footprint height.</param>
    /// <param name="rotated">The pass's rotation.</param>
    /// <param name="versionNoFit">The version at which the scan found no fit.</param>
    public void RecordNoFit(ushort containerSlot, in ContainerHeader header, byte w, byte h, bool rotated, uint versionNoFit)
    {
        if (!Enabled)
        {
            return;
        }

        if (_epochs[containerSlot] != header.StatEpoch || _rows[containerSlot] != header.H)
        {
            Clear(containerSlot);
            _epochs[containerSlot] = header.StatEpoch;
            _rows[containerSlot] = header.H;
        }

        uint shape = ShapeOf(w, h, rotated);
        int way = Find(containerSlot, shape);
        if (way < 0)
        {
            way = FindEmpty(containerSlot, shape);
        }

        _shapes[way] = shape;
        _versions[way] = versionNoFit;
    }

    /// <summary>Forgets every record of the container (statEpoch bump, row growth, index rebuild).</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    public void Clear(ushort containerSlot)
    {
        // _epochs/_rows stay: with every way empty a stale pair can only make IsNoFit miss, and RecordNoFit rewrites them on mismatch.
        Array.Clear(_shapes, containerSlot * _ways, _ways);
        Array.Clear(_versions, containerSlot * _ways, _ways);
    }

    private static uint ShapeOf(byte w, byte h, bool rotated) => (((uint)w << 9) | ((uint)h << 1) | (rotated ? 1u : 0u)) + 1u;

    private static int AddDistinct(Span<int> seen, int count, int shape)
    {
        for (int i = 0; i < count; i++)
        {
            if (seen[i] == shape)
            {
                return count;
            }
        }

        seen[count] = shape;
        return count + 1;
    }

    // Fibonacci hashing; the multiply wraps by design (unchecked explicitly: Debug builds compile with checked arithmetic).
    private int Home(uint shape) => (int)(unchecked(shape * 0x9E37_79B9u) >> 24) & _wayMask;

    private int Find(ushort containerSlot, uint shape)
    {
        int baseIndex = containerSlot * _ways;
        int home = Home(shape);
        for (int k = 0; k < _ways; k++)
        {
            int way = baseIndex + ((home + k) & _wayMask);
            uint s = _shapes[way];
            if (s == shape)
            {
                return way;
            }

            if (s == 0u)
            {
                return -1;
            }
        }

        return -1;
    }

    private int FindEmpty(ushort containerSlot, uint shape)
    {
        int baseIndex = containerSlot * _ways;
        int home = Home(shape);
        for (int k = 0; k < _ways; k++)
        {
            int way = baseIndex + ((home + k) & _wayMask);
            if (_shapes[way] == 0u)
            {
                return way;
            }
        }

        return baseIndex + home; // full: overwrite the home way (a memo may forget; the forgotten shape is merely re-scanned)
    }
}
