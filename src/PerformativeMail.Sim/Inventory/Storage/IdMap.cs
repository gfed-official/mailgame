namespace PerformativeMail.Sim.Inventory.Storage;

/// <summary>
/// A preallocated open-addressed <c>uint -&gt; ushort</c> map (ADR-0009 section 5: the <c>entryId -&gt; slot</c> map; reused by later
/// inventory stories). Power-of-two table of at least twice the capacity, linear probing, Fibonacci hashing, key 0 = empty slot;
/// removal uses backward-shift deletion so there are no tombstones and lookups stay bounded. Never enumerated, never resized:
/// 0 B after construction. Generalises the map inlined in <see cref="ContainerTable"/>. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// var map = new IdMap(8192);
/// map.TryAdd(42u, slot);
/// if (map.TryGet(42u, out ushort s)) { map.Remove(42u); }
/// </code>
/// </example>
internal sealed class IdMap
{
    private readonly uint[] _keys;
    private readonly ushort[] _values;
    private readonly int _mask;
    private int _count;

    /// <summary>Allocates a table for up to <paramref name="capacity"/> live keys (table size: the power of two of at least <c>2 * capacity</c>, minimum 4).</summary>
    /// <param name="capacity">1 to 65,535.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is outside 1 to 65,535.</exception>
    public IdMap(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, ushort.MaxValue);
        int size = 4;
        while (size < capacity * 2)
        {
            size <<= 1;
        }

        _keys = new uint[size];
        _values = new ushort[size];
        _mask = size - 1;
    }

    /// <summary>The number of keys held.</summary>
    public int Count => _count;

    /// <summary>The table size (fixed at construction).</summary>
    public int TableSize => _keys.Length;

    /// <summary>Adds <paramref name="key"/>. Returns false, changing nothing, when the key is 0, already present, or the table is full (at most <c>TableSize - 1</c> keys, so one empty marker always remains).</summary>
    /// <param name="key">A non-zero key.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>True when added.</returns>
    public bool TryAdd(uint key, ushort value)
    {
        if (key == 0u || _count == _keys.Length - 1)
        {
            return false; // one empty slot always remains, so a miss lookup terminates
        }

        int i = Hash(key) & _mask;
        while (true)
        {
            uint k = _keys[i];
            if (k == 0u)
            {
                _keys[i] = key;
                _values[i] = value;
                _count++;
                return true;
            }

            if (k == key)
            {
                return false;
            }

            i = (i + 1) & _mask;
        }
    }

    /// <summary>Looks up <paramref name="key"/>.</summary>
    /// <param name="key">The key (0 is never present).</param>
    /// <param name="value">The stored value when found.</param>
    /// <returns>True when present.</returns>
    public bool TryGet(uint key, out ushort value)
    {
        if (key == 0u)
        {
            value = 0;
            return false;
        }

        int i = Hash(key) & _mask;
        while (true)
        {
            uint k = _keys[i];
            if (k == key)
            {
                value = _values[i];
                return true;
            }

            if (k == 0u)
            {
                value = 0;
                return false;
            }

            i = (i + 1) & _mask;
        }
    }

    /// <summary>Removes <paramref name="key"/> with backward-shift deletion (later keys of the probe run move up; no tombstone is left).</summary>
    /// <param name="key">The key.</param>
    /// <returns>True when the key was present.</returns>
    public bool Remove(uint key)
    {
        if (key == 0u)
        {
            return false;
        }

        int i = Hash(key) & _mask;
        while (true)
        {
            uint k = _keys[i];
            if (k == key)
            {
                break;
            }

            if (k == 0u)
            {
                return false;
            }

            i = (i + 1) & _mask;
        }

        // Backward shift: walk the run after i; any key whose home position is not cyclically within (i, j] can fill the hole.
        int j = i;
        while (true)
        {
            j = (j + 1) & _mask;
            uint k = _keys[j];
            if (k == 0u)
            {
                break;
            }

            int home = Hash(k) & _mask;
            bool stays = j > i ? (home > i && home <= j) : (home > i || home <= j);
            if (!stays)
            {
                _keys[i] = k;
                _values[i] = _values[j];
                i = j;
            }
        }

        _keys[i] = 0u;
        _values[i] = 0;
        _count--;
        return true;
    }

    // Fibonacci hashing (bits 7..31 of key * 2^32/phi), as ContainerTable; the multiply wraps by design (Debug compiles checked).
    private static int Hash(uint key) => unchecked((int)((key * 2654435769u) >> 7));
}
