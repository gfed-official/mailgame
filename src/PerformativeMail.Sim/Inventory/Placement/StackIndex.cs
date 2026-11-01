using PerformativeMail.Sim.Inventory.Rules;
using PerformativeMail.Sim.Inventory.Storage;

namespace PerformativeMail.Sim.Inventory.Placement;

/// <summary>
/// The StackKey non-full index (GDD R8; ADR-0009 section 5; TR-inventory-007; story grid-inventory/009): for each container, the placed
/// entries whose count is below their <c>effMax</c>, one intrusive list per <see cref="StackKey"/>, each kept in row-major order
/// (<c>y</c> ascending, then <c>x</c>; on a slots layout that is slot order). The heads live in one preallocated open-addressed table
/// keyed by <c>(containerSlot, StackKey)</c> — the container slot is part of the key, so every container has its own lists, and a
/// lookup never sees another container's stacks. The table has a power-of-two size of at least twice the entry pool (every head is a
/// live entry, so it never fills), linear probing and backward-shift deletion; it is never enumerated. The links are two <c>ushort</c>
/// arrays parallel to the <see cref="EntryPool"/> (so <see cref="EntryRecord"/> keeps its 32 B).
/// <para>
/// Maintained only by <see cref="InventoryMutator"/> (insert, remove, count change, move; <see cref="InventoryMutator.RebuildIndex"/>,
/// through <see cref="ClearContainer"/> then <see cref="Link"/>, when a container's <c>statEpoch</c> bumps). A derived memo: never in
/// the canonical dump, rebuilt from the entries on load, and it never changes a result (PASS M re-checks <c>count &lt; effMax</c> at
/// op time). Insertion is O(list length) (lists are 1–3 long typically), removal and lookup O(1) amortised. Allocates nothing after
/// construction; never throws on the tick path. Not thread-safe: Sim tick thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// for (ushort s = index.Head(containerSlot, subject.Key); s != StackIndex.None; s = index.Next(s))
/// {
///     ref EntryRecord stack = ref entries[s];   // row-major, every one non-full when it was linked
/// }
/// </code>
/// </example>
internal sealed class StackIndex
{
    /// <summary>The "no entry" link value (the end of a list, or no list).</summary>
    public const ushort None = Occupancy.Free;

    private readonly EntryPool _entries;
    private readonly ulong[] _keys;
    private readonly ushort[] _parts; // containerSlot + 1; 0 = empty table slot
    private readonly ushort[] _heads;
    private readonly int _mask;
    private readonly ushort[] _next;
    private readonly ushort[] _prev;
    private readonly bool[] _linked;
    private int _headCount;
    private int _linkedCount;

    /// <summary>Preallocates the head table and the links for every slot of <paramref name="entries"/>.</summary>
    /// <param name="entries">The entry pool the lists link (read for keys, containers and positions).</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    public StackIndex(EntryPool entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;
        int capacity = entries.Capacity;
        int size = 4;
        while (size < capacity * 2)
        {
            size <<= 1;
        }

        _keys = new ulong[size];
        _parts = new ushort[size];
        _heads = new ushort[size];
        _mask = size - 1;
        _next = new ushort[capacity];
        _prev = new ushort[capacity];
        _linked = new bool[capacity];
    }

    /// <summary>The number of non-empty lists over all containers.</summary>
    public int HeadCount => _headCount;

    /// <summary>The number of linked entries over all containers.</summary>
    public int LinkedCount => _linkedCount;

    /// <summary>True when the entry in <paramref name="entrySlot"/> is linked.</summary>
    /// <param name="entrySlot">The entry's pool slot.</param>
    /// <returns>True when linked.</returns>
    public bool Contains(ushort entrySlot) => _linked[entrySlot];

    /// <summary>The first (row-major lowest) non-full stack of <paramref name="key"/> in the container, or <see cref="None"/>.</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="key">The stack key.</param>
    /// <returns>The head's entry slot, or <see cref="None"/>.</returns>
    public ushort Head(ushort containerSlot, StackKey key)
    {
        int i = Find(containerSlot, key.Raw);
        return i < 0 ? None : _heads[i];
    }

    /// <summary>The entry after <paramref name="entrySlot"/> in its list, or <see cref="None"/>.</summary>
    /// <param name="entrySlot">A linked entry's pool slot.</param>
    /// <returns>The next entry slot, or <see cref="None"/>.</returns>
    public ushort Next(ushort entrySlot) => _next[entrySlot];

    /// <summary>
    /// Links the placed entry in <paramref name="entrySlot"/> into its container's list for its key, at its row-major position. An
    /// entry already linked is left as is (unlink it first when its position or key changed). A detached entry is not linked.
    /// </summary>
    /// <param name="entrySlot">The entry's pool slot.</param>
    public void Link(ushort entrySlot)
    {
        ref EntryRecord e = ref _entries[entrySlot];
        if (_linked[entrySlot] || !e.IsLive || e.ContainerSlot == EntryRecord.NoContainer)
        {
            return;
        }

        ushort container = e.ContainerSlot;
        ulong key = StackKey.Of(in e).Raw;
        int order = RowMajor(e.X, e.Y);
        int i = Find(container, key);
        _linked[entrySlot] = true;
        _linkedCount++;
        if (i < 0)
        {
            AddHead(container, key, entrySlot);
            _next[entrySlot] = None;
            _prev[entrySlot] = None;
            return;
        }

        ushort head = _heads[i];
        ref EntryRecord headEntry = ref _entries[head];
        if (order < RowMajor(headEntry.X, headEntry.Y))
        {
            _heads[i] = entrySlot;
            _prev[entrySlot] = None;
            _next[entrySlot] = head;
            _prev[head] = entrySlot;
            return;
        }

        ushort at = head;
        while (_next[at] != None)
        {
            ref EntryRecord n = ref _entries[_next[at]];
            if (order < RowMajor(n.X, n.Y))
            {
                break;
            }

            at = _next[at];
        }

        ushort after = _next[at];
        _prev[entrySlot] = at;
        _next[entrySlot] = after;
        _next[at] = entrySlot;
        if (after != None)
        {
            _prev[after] = entrySlot;
        }
    }

    /// <summary>
    /// Unlinks the entry in <paramref name="entrySlot"/> if it is linked; otherwise does nothing. Must run while the entry still holds
    /// the container, key and position it was linked under (the mutator calls it before changing them).
    /// </summary>
    /// <param name="entrySlot">The entry's pool slot.</param>
    public void Unlink(ushort entrySlot)
    {
        if (!_linked[entrySlot])
        {
            return;
        }

        ushort prev = _prev[entrySlot];
        ushort next = _next[entrySlot];
        if (prev != None)
        {
            _next[prev] = next;
        }
        else
        {
            ref EntryRecord e = ref _entries[entrySlot];
            int i = Find(e.ContainerSlot, StackKey.Of(in e).Raw);
            if (i >= 0)
            {
                if (next == None)
                {
                    RemoveAt(i);
                }
                else
                {
                    _heads[i] = next;
                }
            }
        }

        if (next != None)
        {
            _prev[next] = prev;
        }

        _next[entrySlot] = None;
        _prev[entrySlot] = None;
        _linked[entrySlot] = false;
        _linkedCount--;
    }

    /// <summary>
    /// Unlinks every linked entry whose <c>ContainerSlot</c> is <paramref name="containerSlot"/>, whatever its cells now hold (the first
    /// step of <see cref="InventoryMutator.RebuildIndex"/>). O(entry pool); allocation-free.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    public void ClearContainer(ushort containerSlot)
    {
        for (int s = 0; s < _linked.Length && _linkedCount > 0; s++)
        {
            if (_linked[s] && _entries[(ushort)s].ContainerSlot == containerSlot)
            {
                Unlink((ushort)s);
            }
        }
    }

    private static int RowMajor(byte x, byte y) => (y << 8) | x;

    // Fibonacci hashing of the key mixed with the container slot; the multiply wraps by design, so it is unchecked explicitly (Debug
    // builds compile with checked arithmetic). Never used for ordering (ADR-0006).
    private int Slot(ushort container, ulong key) =>
        (int)(unchecked((key ^ (key >> 31) ^ ((ulong)container << 47)) * 0x9E37_79B9_7F4A_7C15UL) >> 40) & _mask;

    private int Find(ushort container, ulong key)
    {
        ushort part = (ushort)(container + 1);
        int i = Slot(container, key);
        while (true)
        {
            ushort p = _parts[i];
            if (p == 0)
            {
                return -1;
            }

            if (p == part && _keys[i] == key)
            {
                return i;
            }

            i = (i + 1) & _mask;
        }
    }

    private void AddHead(ushort container, ulong key, ushort entrySlot)
    {
        int i = Slot(container, key);
        while (_parts[i] != 0)
        {
            i = (i + 1) & _mask;
        }

        _keys[i] = key;
        _parts[i] = (ushort)(container + 1);
        _heads[i] = entrySlot;
        _headCount++;
    }

    // Backward shift: walk the run after the hole; any key whose home is not cyclically within (hole, j] can fill it.
    private void RemoveAt(int hole)
    {
        int i = hole;
        int j = i;
        while (true)
        {
            j = (j + 1) & _mask;
            ushort p = _parts[j];
            if (p == 0)
            {
                break;
            }

            int home = Slot((ushort)(p - 1), _keys[j]);
            bool inRange = i <= j ? (home > i && home <= j) : (home > i || home <= j);
            if (inRange)
            {
                continue;
            }

            _keys[i] = _keys[j];
            _parts[i] = p;
            _heads[i] = _heads[j];
            i = j;
        }

        _keys[i] = 0UL;
        _parts[i] = 0;
        _heads[i] = 0;
        _headCount--;
    }
}
