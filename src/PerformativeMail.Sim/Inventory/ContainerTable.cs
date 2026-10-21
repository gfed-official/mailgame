using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory;

/// <summary>
/// The container registry (ADR-0009 section 5): a preallocated <see cref="ContainerHeader"/> array indexed by table slot plus an
/// open-addressed <c>containerId -> slot</c> map (power-of-two size of at least twice the capacity, linear probing, key 0 = empty). Ids
/// start at 1, rise by one per container and are never reused within a run; <see cref="NextId"/> is Sim state. Slots are handed out in
/// ascending order and (in this story) never freed, so ascending slot order is ascending id order. Everything is allocated in the
/// constructor; <see cref="TryCreate"/> and the lookups allocate nothing and never throw. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// var table = new ContainerTable(64);
/// if (table.TryCreate(defIndex, 8, 1, ContainerLayout.Slots, ContainerHeader.NoOwner, out ContainerId id) &amp;&amp; table.TryGetSlot(id, out ushort slot)) { ref ContainerHeader h = ref table[slot]; }
/// </code>
/// </example>
public sealed class ContainerTable
{
    private readonly ContainerHeader[] _headers;
    private readonly uint[] _mapKeys;
    private readonly ushort[] _mapSlots;
    private readonly int _mapMask;
    private int _used;
    private uint _nextId = 1;

    /// <summary>Allocates the table for <paramref name="capacity"/> containers.</summary>
    /// <param name="capacity">1 to 65,535 (slots are <c>ushort</c>).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is outside 1 to 65,535.</exception>
    public ContainerTable(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, ushort.MaxValue);
        _headers = new ContainerHeader[capacity];
        int mapSize = 4;
        while (mapSize < capacity * 2)
        {
            mapSize <<= 1;
        }

        _mapKeys = new uint[mapSize];
        _mapSlots = new ushort[mapSize];
        _mapMask = mapSize - 1;
    }

    /// <summary>The number of table slots (fixed at construction).</summary>
    public int Capacity => _headers.Length;

    /// <summary>The number of live containers.</summary>
    public int LiveCount => _used;

    /// <summary>The id the next created container will get (Sim state; starts at 1).</summary>
    public uint NextId => _nextId;

    /// <summary>The header in table slot <paramref name="slot"/> (a default header when the slot is free).</summary>
    /// <param name="slot">A slot below <see cref="Capacity"/>.</param>
    public ref ContainerHeader this[ushort slot] => ref _headers[slot];

    /// <summary>
    /// Creates a container: takes the lowest free slot, assigns the next id and fills the identity and dimension fields (versions 0,
    /// <c>FreeCells = w * h</c>, no entries). Returns false, changing nothing, when the table is full or the id counter is exhausted.
    /// </summary>
    /// <param name="def">The def ordinal.</param>
    /// <param name="w">Columns.</param>
    /// <param name="h">Rows.</param>
    /// <param name="layout">The def's layout.</param>
    /// <param name="ownerSlot">The owning player slot value, or <see cref="ContainerHeader.NoOwner"/>.</param>
    /// <param name="id">The new id, or none.</param>
    /// <returns>True when the container was created.</returns>
    public bool TryCreate(DefIndex def, int w, int h, Content.ContainerLayout layout, byte ownerSlot, out ContainerId id)
    {
        if (_used == _headers.Length || _nextId == 0)
        {
            id = default;
            return false;
        }

        id = new ContainerId(_nextId);
        _nextId++;
        ushort slot = (ushort)_used;
        _used++;
        ref ContainerHeader header = ref _headers[slot];
        header = default;
        header.Id = id;
        header.DefIndex = def;
        header.W = (ushort)w;
        header.H = (ushort)h;
        header.Layout = layout;
        header.OwnerSlot = ownerSlot;
        header.FreeCells = (ushort)(w * h);
        header.NextArrivalSeq = 1;

        int i = Hash(id.Value) & _mapMask;
        while (_mapKeys[i] != 0)
        {
            i = (i + 1) & _mapMask;
        }

        _mapKeys[i] = id.Value;
        _mapSlots[i] = slot;
        return true;
    }

    /// <summary>Looks up the table slot of <paramref name="id"/>.</summary>
    /// <param name="id">The container id.</param>
    /// <param name="slot">The slot when found.</param>
    /// <returns>True when the id is live.</returns>
    public bool TryGetSlot(ContainerId id, out ushort slot)
    {
        if (id.IsNone)
        {
            slot = 0;
            return false;
        }

        int i = Hash(id.Value) & _mapMask;
        while (true)
        {
            uint key = _mapKeys[i];
            if (key == id.Value)
            {
                slot = _mapSlots[i];
                return true;
            }

            if (key == 0)
            {
                slot = 0;
                return false;
            }

            i = (i + 1) & _mapMask;
        }
    }

    // Fibonacci hashing (bits 7..31 of key * 2^32/phi): spreads sequential ids over the table; the multiply wraps by design, so it is
    // unchecked explicitly (Debug builds compile with checked arithmetic).
    private static int Hash(uint key) => unchecked((int)((key * 2654435769u) >> 7));
}
