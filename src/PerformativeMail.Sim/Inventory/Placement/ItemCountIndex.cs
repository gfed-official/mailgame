using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Placement;

/// <summary>
/// The per-container <c>itemId → units</c> index (GDD R17; ADR-0009 section 5 table; story grid-inventory/015): for each container, the
/// total units of each item def (non-mail entries only; mail has no item key) placed in it, so <c>CountItem</c> is a map read and never
/// a scan. One preallocated open-addressed table keyed by <c>(containerSlot, itemId)</c> — the container slot is part of the key, so a
/// lookup never sees another container's units. A key is present only while its units are above 0. The table has a power-of-two size of
/// at least twice the entry pool (every key has at least one live item entry behind it, so it is never more than half full), linear
/// probing and backward-shift deletion; it is never enumerated.
/// <para>
/// Maintained only by <see cref="InventoryMutator"/>'s item primitives (<c>Place</c>, <c>Remove</c>, <c>MergeInto</c>,
/// <c>TakeOldest</c>; <c>Transfer</c> across containers through <c>Remove</c> then <c>Place</c>; <see cref="InventoryMutator.RebuildIndex"/>
/// through <see cref="ClearContainer"/> then <see cref="Add"/>). A derived memo: never in the canonical dump, rebuilt from the entries
/// on load. <see cref="Units"/> counts <see cref="CounterId.InventoryItemCountProbes"/> once per table slot it reads; the count
/// depends on the table's collisions, never on how many entries a container holds. Allocates nothing after construction; never
/// throws on the tick path. Not thread-safe: Sim tick thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// index.Add(containerSlot, entry.ItemId, entry.Count);   // the mutator, on Place
/// int planks = index.Units(containerSlot, plank.Value);  // CountItem
/// </code>
/// </example>
internal sealed class ItemCountIndex
{
    private readonly uint[] _keys; // (containerSlot + 1) << 16 | itemId; 0 = empty table slot
    private readonly int[] _units;
    private readonly int _mask;
    private readonly int _shift; // 64 - log2(table size): Home keeps the product's top bits
    private readonly SimCounters _counters;
    private int _keyCount;

    /// <summary>The most containers the key packs: <c>containerSlot + 1</c> takes the key's high 16 bits, and 0 marks an empty slot.</summary>
    public const int MaxContainerCapacity = ushort.MaxValue;

    /// <summary>
    /// Preallocates the table for an entry pool of <paramref name="entryCapacity"/> slots over a container table of
    /// <paramref name="containerCapacity"/> slots.
    /// </summary>
    /// <param name="entryCapacity">The entry pool's capacity (the most keys that can be live at once).</param>
    /// <param name="containerCapacity">The container table's capacity (every container slot is below it); at most <see cref="MaxContainerCapacity"/>.</param>
    /// <param name="counters">The world's counters (<see cref="CounterId.InventoryItemCountProbes"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="entryCapacity"/> or <paramref name="containerCapacity"/> is below 1, or <paramref name="containerCapacity"/> is above <see cref="MaxContainerCapacity"/> (a key would not fit its 16 bits).</exception>
    /// <exception cref="ArgumentNullException"><paramref name="counters"/> is null.</exception>
    public ItemCountIndex(int entryCapacity, int containerCapacity, SimCounters counters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(entryCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(containerCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(containerCapacity, MaxContainerCapacity);
        ArgumentNullException.ThrowIfNull(counters);
        int size = 4;
        int bits = 2;
        while (size < entryCapacity * 2)
        {
            size <<= 1;
            bits++;
        }

        _keys = new uint[size];
        _units = new int[size];
        _mask = size - 1;
        _shift = 64 - bits;
        _counters = counters;
    }

    /// <summary>The number of live <c>(container, itemId)</c> keys over all containers.</summary>
    public int KeyCount => _keyCount;

    /// <summary>The table's size (a power of two, at least twice the entry pool).</summary>
    public int TableSize => _keys.Length;

    /// <summary>
    /// The units of <paramref name="itemId"/> in the container (0 when none). Counts one <see cref="CounterId.InventoryItemCountProbes"/>
    /// per table slot read, the empty slot that ends a miss included.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="itemId">The item def ordinal.</param>
    /// <returns>The units.</returns>
    public int Units(ushort containerSlot, ushort itemId)
    {
        uint key = Key(containerSlot, itemId);
        int i = Home(key);
        int probes = 0;
        int units = 0;
        while (true)
        {
            probes++;
            uint k = _keys[i];
            if (k == 0u)
            {
                break;
            }

            if (k == key)
            {
                units = _units[i];
                break;
            }

            i = (i + 1) & _mask;
        }

        _counters.Add(CounterId.InventoryItemCountProbes, probes);
        return units;
    }

    /// <summary>
    /// Adds <paramref name="delta"/> (negative to subtract) to the units of <paramref name="itemId"/> in the container; a key that reaches
    /// 0 is deleted. A total that would go below 0 asserts <see cref="SimAssertCode.InventoryItemCountNegative"/> under
    /// <c>INVENTORY_ASSERT</c>; Release deletes the key. A delta of 0 changes nothing.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="itemId">The item def ordinal.</param>
    /// <param name="delta">The change in units.</param>
    public void Add(ushort containerSlot, ushort itemId, int delta)
    {
        if (delta == 0)
        {
            return;
        }

        uint key = Key(containerSlot, itemId);
        int i = Home(key);
        while (_keys[i] != 0u && _keys[i] != key)
        {
            i = (i + 1) & _mask;
        }

        if (_keys[i] == 0u)
        {
            InventoryAssert.That(delta > 0, SimAssertCode.InventoryItemCountNegative);
            if (delta < 0)
            {
                return;
            }

            _keys[i] = key;
            _units[i] = delta;
            _keyCount++;
            return;
        }

        int units = _units[i] + delta;
        InventoryAssert.That(units >= 0, SimAssertCode.InventoryItemCountNegative);
        if (units > 0)
        {
            _units[i] = units;
            return;
        }

        RemoveAt(i);
    }

    /// <summary>Deletes every key of the container (the first step of <see cref="InventoryMutator.RebuildIndex"/>). O(table); allocation-free.</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    public void ClearContainer(ushort containerSlot)
    {
        uint part = (uint)(containerSlot + 1);
        int i = 0;
        while (i < _keys.Length && _keyCount > 0)
        {
            // A backward shift may move a later key into slot i, so the slot is re-read before moving on. A key wrapped from the table's
            // start into its end was already passed over, so it is never one of this container's.
            if (_keys[i] != 0u && (_keys[i] >> 16) == part)
            {
                RemoveAt(i);
                continue;
            }

            i++;
        }
    }

    private static uint Key(ushort containerSlot, ushort itemId) => ((uint)(containerSlot + 1) << 16) | itemId;

    // Fibonacci hashing: the top log2(size) bits of the wrapped product (middle bits put keys a fixed stride apart, such as one item
    // in successive containers, into a single run). The multiply wraps by design, so it is unchecked explicitly (Debug builds compile
    // with checked arithmetic). Never used for ordering (ADR-0006).
    private int Home(uint key) => (int)(unchecked((ulong)key * 0x9E37_79B9_7F4A_7C15UL) >> _shift);

    // Backward shift: walk the run after the hole; any key whose home is not cyclically within (hole, j] can fill it.
    private void RemoveAt(int hole)
    {
        int i = hole;
        int j = i;
        while (true)
        {
            j = (j + 1) & _mask;
            uint k = _keys[j];
            if (k == 0u)
            {
                break;
            }

            int home = Home(k);
            bool inRange = i <= j ? (home > i && home <= j) : (home > i || home <= j);
            if (inRange)
            {
                continue;
            }

            _keys[i] = k;
            _units[i] = _units[j];
            i = j;
        }

        _keys[i] = 0u;
        _units[i] = 0;
        _keyCount--;
    }
}
