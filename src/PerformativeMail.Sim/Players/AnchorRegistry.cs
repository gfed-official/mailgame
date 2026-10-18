using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The interaction anchor registry (ADR-0014 section 1; GDD player-character-movement P10; TR-movement-011): up to
/// <c>maxAnchors</c> anchors, each with its <see cref="AnchorDef"/> and <see cref="IInteractable"/> owner, indexed by a dense 2 m
/// <see cref="AnchorGrid"/>. One verb per <c>(AnchorRef, context)</c> is enforced in every build: a second registration returns
/// <see cref="RegisterResult.DuplicateVerb"/>, records <see cref="CheckCode.AnchorDuplicateVerb"/> and leaves the first intact.
/// Capacity overflow returns <see cref="RegisterResult.Full"/> and counts <see cref="CounterId.AnchorRegistryFull"/>.
/// <para>
/// All storage (the entries, the impl array, the free-slot stack and the grid) is allocated at construction; Register, Unregister,
/// Move, queries and the dump allocate nothing. An anchor's index is its slot in the fixed array: the lowest free slot is taken
/// first at the start, then freed slots are reused most-recently-freed first, all deterministic from the call order. Registration
/// and lookup scan the used slots (at most <c>maxAnchors</c>, run-start work); the per-tick query touches only grid cells.
/// Registry methods have no tick source, so the duplicate-verb check is recorded with tick 0 (the ring still stores it).
/// Not thread-safe: Sim tick thread, phases 0 to 12 or world build.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var registry = new AnchorRegistry(new MapBounds(0, 0, 256_000, 128_000), 4096, 2500, 400, checks, counters);
/// AnchorDef def = AnchorDef.For(new AnchorRef(entity, 0), new Vec3Mm(4000, 0, 9000), Verb.Deliver);
/// RegisterResult r = registry.Register(in def, impl);   // Ok
/// Span&lt;int&gt; found = stackalloc int[64];
/// int n = registry.QueryCells(new Vec3Mm(4500, 0, 9000), found);
/// </code>
/// </example>
public sealed class AnchorRegistry
{
    /// <summary>Context mask of the default World context.</summary>
    public const byte ContextWorld = 1;

    /// <summary>Context mask of the Build context (<see cref="AnchorFlags.BuildModeOnly"/> anchors).</summary>
    public const byte ContextBuild = 2;

    private const byte KnownFlagBits = (byte)(AnchorFlags.AllowWhileSwimming | AnchorFlags.BuildModeOnly | AnchorFlags.SeatOf);
    private const int MaxPriority = 3;

    private readonly Entry[] _entries;
    private readonly IInteractable?[] _impls;
    private readonly int[] _free;
    private readonly AnchorGrid _grid;
    private readonly int _defaultRangeMm;
    private readonly int _incumbentSlackMm;
    private readonly SimCheckRing _checks;
    private readonly SimCounters _counters;
    private int _freeCount;
    private int _highWater;
    private int _count;

    /// <summary>Creates the registry and allocates every array.</summary>
    /// <param name="bounds">The map extent the grid covers.</param>
    /// <param name="maxAnchors">Capacity (<c>player.maxAnchors</c>, default 4096); at least 1.</param>
    /// <param name="defaultRangeMm">The reach stored for an anchor registered with <c>RangeMm == 0</c> (<c>player.rangeMm</c>, 2500).</param>
    /// <param name="incumbentSlackMm">The focus keep slack added to the query radius (<c>player.incumbentSlackMm</c>, 400).</param>
    /// <param name="checks">Receives the duplicate-verb check.</param>
    /// <param name="counters">Receives the capacity and query-overflow counts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="checks"/> or <paramref name="counters"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or range argument is below its minimum, or the grid would be larger than 4 000 000 cells.</exception>
    /// <example><code>var registry = new AnchorRegistry(world.Bounds, 4096, 2500, 400, checks, counters);</code></example>
    public AnchorRegistry(MapBounds bounds, int maxAnchors, int defaultRangeMm, int incumbentSlackMm, SimCheckRing checks, SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAnchors, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultRangeMm, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(incumbentSlackMm);
        _entries = new Entry[maxAnchors];
        _impls = new IInteractable?[maxAnchors];
        _free = new int[maxAnchors];
        for (int i = 0; i < maxAnchors; i++)
        {
            _free[i] = maxAnchors - 1 - i;   // pop order 0, 1, 2...
        }

        _freeCount = maxAnchors;
        _grid = new AnchorGrid(bounds, maxAnchors);
        _defaultRangeMm = defaultRangeMm;
        _incumbentSlackMm = incumbentSlackMm;
        _checks = checks;
        _counters = counters;
        MaxRegisteredRangeMm = 0;
    }

    /// <summary>Anchors currently registered.</summary>
    public int Count => _count;

    /// <summary>Capacity (<c>maxAnchors</c>).</summary>
    public int Capacity => _entries.Length;

    /// <summary>The largest <c>RangeMm</c> ever registered (a running maximum; it does not shrink on unregister, so queries stay conservative).</summary>
    public int MaxRegisteredRangeMm { get; private set; }

    /// <summary>The query radius in cells: <c>CeilDiv(MaxRegisteredRangeMm + incumbentSlackMm, 2000)</c>.</summary>
    public int QueryRadiusCells => (int)(((long)MaxRegisteredRangeMm + _incumbentSlackMm + AnchorGrid.CellSizeMm - 1) / AnchorGrid.CellSizeMm);

    /// <summary>Grid cells along X.</summary>
    public int CellsX => _grid.CellsX;

    /// <summary>Grid cells along Z.</summary>
    public int CellsZ => _grid.CellsZ;

    /// <summary>The cell index (<c>cz * CellsX + cx</c>) a point falls in, clamped into the edge cells.</summary>
    /// <param name="point">The point, mm.</param>
    /// <returns>The cell index.</returns>
    /// <example><code>int cell = registry.CellIndexOf(new Vec3Mm(5000, 0, 5000));</code></example>
    public int CellIndexOf(in Vec3Mm point) => _grid.CellIndexOf(in point);

    /// <summary>The registration validity rule: a non-null impl, a real ref, a catalogue verb, a mode in Tap to Channel, priority 0 to 3, known flag bits, and non-negative range and hold base.</summary>
    private static bool IsValid(in AnchorDef def, IInteractable? impl) =>
        impl is not null
        && !def.Ref.IsNone
        && VerbCatalogue.IsDefined(def.Verb)
        && def.Mode >= InteractMode.Tap && def.Mode <= InteractMode.Channel
        && def.Priority <= MaxPriority
        && def.RangeMm >= 0 && def.HoldBaseMs >= 0
        && ((byte)def.Flags & ~KnownFlagBits) == 0;

    /// <summary>Registers an anchor and its owner.</summary>
    /// <param name="def">The definition; <c>RangeMm == 0</c> is stored as the default reach.</param>
    /// <param name="impl">The owner's pure hooks; null is rejected.</param>
    /// <returns>
    /// <see cref="RegisterResult.Ok"/>; <see cref="RegisterResult.InvalidArgument"/> for a null impl, a none ref, a verb outside the
    /// catalogue, a mode outside Tap to Channel, a priority above 3, unknown flag bits or a negative range or hold base;
    /// <see cref="RegisterResult.DuplicateVerb"/> when the <c>(Ref, context)</c> is live (check recorded, first untouched);
    /// <see cref="RegisterResult.Full"/> when no slot is free (counted, nothing changed).
    /// </returns>
    /// <example><code>RegisterResult r = registry.Register(in def, impl);</code></example>
    public RegisterResult Register(in AnchorDef def, IInteractable impl)
    {
        if (!IsValid(in def, impl))
        {
            return RegisterResult.InvalidArgument;
        }

        byte mask = (def.Flags & AnchorFlags.BuildModeOnly) != 0 ? ContextBuild : ContextWorld;
        if (Find(def.Ref, mask) >= 0)
        {
            _checks.Record(CheckCode.AnchorDuplicateVerb, 0, def.Ref.Entity.Value, def.Ref.Slot);
            return RegisterResult.DuplicateVerb;
        }

        if (_freeCount == 0)
        {
            _counters.Add(CounterId.AnchorRegistryFull);
            return RegisterResult.Full;
        }

        int index = _free[--_freeCount];
        ref Entry e = ref _entries[index];
        e.Def = def;
        if (e.Def.RangeMm == 0)
        {
            e.Def.RangeMm = _defaultRangeMm;
        }

        e.Live = 1;
        e.ContextMask = mask;
        _impls[index] = impl;
        MaxRegisteredRangeMm = Math.Max(MaxRegisteredRangeMm, e.Def.RangeMm);
        _highWater = Math.Max(_highWater, index + 1);
        _count++;
        _grid.Link(index, _grid.CellIndexOf(in e.Def.Point));
        return RegisterResult.Ok;
    }

    /// <summary>Frees every registration of <paramref name="anchor"/> (its World and Build entries); the ref can be registered again afterwards.</summary>
    /// <param name="anchor">The anchor.</param>
    /// <returns><see cref="RegisterResult.Ok"/>, or <see cref="RegisterResult.UnknownAnchor"/> when none is registered.</returns>
    /// <example><code>RegisterResult r = registry.Unregister(anchor);</code></example>
    public RegisterResult Unregister(AnchorRef anchor)
    {
        bool found = false;
        for (int i = 0; i < _highWater; i++)
        {
            ref Entry e = ref _entries[i];
            if (e.Live == 0 || e.Def.Ref != anchor)
            {
                continue;
            }

            _grid.Unlink(i, _grid.CellIndexOf(in e.Def.Point));
            e = default;
            _impls[i] = null;
            _free[_freeCount++] = i;
            _count--;
            found = true;
        }

        return found ? RegisterResult.Ok : RegisterResult.UnknownAnchor;
    }

    /// <summary>Moves every registration of <paramref name="anchor"/> to <paramref name="point"/> and re-buckets it.</summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="point">The new point, mm (outside the map clamps into the edge cells).</param>
    /// <returns><see cref="RegisterResult.Ok"/>, or <see cref="RegisterResult.UnknownAnchor"/> when none is registered.</returns>
    /// <example><code>RegisterResult r = registry.Move(seat, new Vec3Mm(100, 0, 200));</code></example>
    public RegisterResult Move(AnchorRef anchor, in Vec3Mm point)
    {
        bool found = false;
        int newCell = _grid.CellIndexOf(in point);
        for (int i = 0; i < _highWater; i++)
        {
            ref Entry e = ref _entries[i];
            if (e.Live == 0 || e.Def.Ref != anchor)
            {
                continue;
            }

            int oldCell = _grid.CellIndexOf(in e.Def.Point);
            if (oldCell != newCell)
            {
                _grid.Unlink(i, oldCell);
                _grid.Link(i, newCell);
            }

            e.Def.Point = point;
            found = true;
        }

        return found ? RegisterResult.Ok : RegisterResult.UnknownAnchor;
    }

    /// <summary>Finds the lowest-index live registration of <paramref name="anchor"/> in any context.</summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="index">Its index, or -1.</param>
    /// <returns>True when registered.</returns>
    /// <example><code>if (registry.TryGet(anchor, out int i)) { ref readonly AnchorDef d = ref registry.DefAt(i); }</code></example>
    public bool TryGet(AnchorRef anchor, out int index)
    {
        for (int i = 0; i < _highWater; i++)
        {
            if (_entries[i].Live != 0 && _entries[i].Def.Ref == anchor)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    /// <summary>Finds the live registration of <paramref name="anchor"/> in one context.</summary>
    /// <param name="anchor">The anchor.</param>
    /// <param name="contextMask"><see cref="ContextWorld"/> or <see cref="ContextBuild"/>.</param>
    /// <param name="index">Its index, or -1.</param>
    /// <returns>True when registered.</returns>
    /// <example><code>bool has = registry.TryGet(anchor, AnchorRegistry.ContextBuild, out int i);</code></example>
    public bool TryGet(AnchorRef anchor, byte contextMask, out int index)
    {
        index = Find(anchor, contextMask);
        return index >= 0;
    }

    /// <summary>The stored definition at a live <paramref name="index"/> (the resolved reach, never 0).</summary>
    /// <param name="index">An index from <see cref="TryGet(AnchorRef, out int)"/> or <see cref="QueryCells"/>.</param>
    /// <returns>A reference into the fixed entry array; valid until the slot is unregistered.</returns>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is outside 0 to <see cref="Capacity"/> - 1.</exception>
    /// <example><code>int range = registry.DefAt(i).RangeMm;</code></example>
    public ref readonly AnchorDef DefAt(int index) => ref _entries[index].Def;

    /// <summary>The owner of the anchor at <paramref name="index"/>.</summary>
    /// <param name="index">A live index.</param>
    /// <returns>The impl.</returns>
    /// <exception cref="InvalidOperationException">The slot is not live.</exception>
    /// <example><code>IInteractable impl = registry.ImplAt(i);</code></example>
    public IInteractable ImplAt(int index) =>
        _impls[index] ?? throw new InvalidOperationException("Anchor slot " + index + " is not registered.");

    /// <summary>The context mask (<see cref="ContextWorld"/> or <see cref="ContextBuild"/>) of the anchor at <paramref name="index"/>.</summary>
    /// <param name="index">A live index.</param>
    /// <returns>The mask.</returns>
    /// <example><code>bool build = registry.ContextMaskAt(i) == AnchorRegistry.ContextBuild;</code></example>
    public byte ContextMaskAt(int index) => _entries[index].ContextMask;

    /// <summary>
    /// Writes the indices of every anchor whose cell is within <see cref="QueryRadiusCells"/> of the cell holding
    /// <paramref name="point"/> (a clamped square), ascending cell then ascending index, without sorting. When <paramref name="results"/>
    /// is too short the first ones are kept and <see cref="CounterId.AnchorQueryOverflow"/> is counted once.
    /// </summary>
    /// <param name="point">The query point (the chest), mm.</param>
    /// <param name="results">The destination; its length caps the result.</param>
    /// <returns>The number of indices written.</returns>
    /// <example><code>Span&lt;int&gt; found = stackalloc int[128]; int n = registry.QueryCells(chest, found);</code></example>
    public int QueryCells(in Vec3Mm point, Span<int> results)
    {
        int n = _grid.Query(in point, QueryRadiusCells, results, out bool truncated);
        if (truncated)
        {
            _counters.Add(CounterId.AnchorQueryOverflow);
        }

        return n;
    }

    /// <summary>
    /// Writes the canonical dump: <c>u32</c> count, then for each live anchor in ascending index: entity <c>u32</c>, slot <c>u8</c>,
    /// X, Y, Z <c>i32</c>, verb <c>u8</c>, mode <c>u8</c>, holdBaseMs <c>i32</c>, rangeMm <c>i32</c>, priority <c>u8</c>, flags <c>u8</c>.
    /// No object reference is written. Between ticks only; allocation-free.
    /// </summary>
    /// <param name="w">The section writer.</param>
    /// <example><code>registry.Dump(ref w);</code></example>
    public void Dump(ref CanonicalWriter w)
    {
        w.WriteU32((uint)_count);
        for (int i = 0; i < _highWater; i++)
        {
            ref readonly Entry e = ref _entries[i];
            if (e.Live == 0)
            {
                continue;
            }

            w.WriteU32(e.Def.Ref.Entity.Value);
            w.WriteU8(e.Def.Ref.Slot);
            w.WriteI32(e.Def.Point.X);
            w.WriteI32(e.Def.Point.Y);
            w.WriteI32(e.Def.Point.Z);
            w.WriteU8((byte)e.Def.Verb);
            w.WriteU8((byte)e.Def.Mode);
            w.WriteI32(e.Def.HoldBaseMs);
            w.WriteI32(e.Def.RangeMm);
            w.WriteU8(e.Def.Priority);
            w.WriteU8((byte)e.Def.Flags);
        }
    }

    private int Find(AnchorRef anchor, byte contextMask)
    {
        for (int i = 0; i < _highWater; i++)
        {
            ref readonly Entry e = ref _entries[i];
            if (e.Live != 0 && e.ContextMask == contextMask && e.Def.Ref == anchor)
            {
                return i;
            }
        }

        return -1;
    }

    private struct Entry
    {
        public AnchorDef Def;
        public byte Live;
        public byte ContextMask;
    }
}
