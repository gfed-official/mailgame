using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.World;

/// <summary>
/// The WorldItem expiry FIFOs (ADR-0009 section 5; GDD R12, D6; story grid-inventory/013): one preallocated ring of
/// <c>(worldItemId, slot, expiresTick)</c> nodes per <see cref="ExpiryFifo"/>. Each FIFO has one constant duration and nodes are
/// appended at creation in creation order (the creation tick never decreases within a FIFO, which <c>CreateWorldItem</c> enforces), so
/// append order is <c>expiresTick</c> order and ties fall to ascending <c>worldItemId</c>. A removed item (picked up, evicted) leaves its
/// node as a <b>lazy tombstone</b>: a node is live while its slot still holds the same id (ids are never reused). A ring that is full
/// on append is compacted first (stale nodes dropped, order kept); every live item is on at most one FIFO and each ring holds the
/// table's capacity, so a compaction always frees room. Not a timing wheel and not a heap (ADR-0009 Alternative 4).
/// <para>
/// <b>Head memo.</b> Each FIFO remembers whether its head node has been verified live since it last changed (a pop, or the removal of
/// the head's item). <see cref="TryPeekHead"/> returns a verified head without reading the node again; otherwise it inspects nodes,
/// popping stale ones, until it finds a live head. With <c>countVisits</c> (the cap-eviction path) every node it inspects adds one to
/// <see cref="CounterId.InventoryWorldItemCapCandidates"/> (46, H-PERF-6d: one per candidate visit, compiled into Release); the expiry
/// pass counts nothing. A head verified by the expiry pass is therefore not counted again by an eviction in the same tick.
/// </para>
/// Allocation-free after construction; never throws on the tick path. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// fifos.Append(ExpiryFifo.MailWorldItem, table, id, slot, expiresTick);
/// if (fifos.TryPeekHead(ExpiryFifo.MailWorldItem, table, countVisits: false, out ushort head)) { /* table[head] expires first */ }
/// </code>
/// </example>
internal sealed class ExpiryFifos
{
    /// <summary>The number of FIFOs (<see cref="ExpiryFifo.MailWorldItem"/> to <see cref="ExpiryFifo.Bag"/>).</summary>
    public const int Count = 5;

    private readonly uint[] _ids;
    private readonly ushort[] _slots;
    private readonly uint[] _expires;
    private readonly int[] _head = new int[Count];
    private readonly int[] _count = new int[Count];
    private readonly bool[] _headVerified = new bool[Count];
    private readonly int _capacity;
    private readonly SimCounters _counters;

    /// <summary>Preallocates <see cref="Count"/> rings of <paramref name="capacityPerFifo"/> nodes.</summary>
    /// <param name="capacityPerFifo">Nodes per ring (the WorldItem table's capacity).</param>
    /// <param name="counters">The world's counters (<see cref="CounterId.InventoryWorldItemCapCandidates"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacityPerFifo"/> is below 1.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="counters"/> is null.</exception>
    public ExpiryFifos(int capacityPerFifo, SimCounters counters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityPerFifo, 1);
        ArgumentNullException.ThrowIfNull(counters);
        _capacity = capacityPerFifo;
        _ids = new uint[Count * capacityPerFifo];
        _slots = new ushort[Count * capacityPerFifo];
        _expires = new uint[Count * capacityPerFifo];
        _counters = counters;
    }

    /// <summary>Nodes per ring.</summary>
    public int CapacityPerFifo => _capacity;

    /// <summary>The nodes held by <paramref name="fifo"/>, live and stale.</summary>
    /// <param name="fifo">The FIFO.</param>
    /// <returns>The node count.</returns>
    public int NodeCount(ExpiryFifo fifo) => _count[(int)fifo];

    /// <summary>
    /// Appends a node at the tail of <paramref name="fifo"/>; a full ring is compacted first. False only when the ring is still full
    /// after compaction (more live items on one FIFO than its capacity: unreachable, the table has the same capacity).
    /// </summary>
    /// <param name="fifo">The FIFO.</param>
    /// <param name="table">The WorldItem table (liveness, for a compaction).</param>
    /// <param name="id">The item's raw id.</param>
    /// <param name="slot">The item's table slot.</param>
    /// <param name="expiresTick">The item's expiry tick (not below the tail's).</param>
    /// <returns>True when appended.</returns>
    public bool Append(ExpiryFifo fifo, WorldItemTable table, uint id, ushort slot, uint expiresTick)
    {
        int f = (int)fifo;
        if (_count[f] == _capacity)
        {
            Compact(fifo, table);
            if (_count[f] == _capacity)
            {
                return false;
            }
        }

        int at = Index(f, _count[f]);
        _ids[at] = id;
        _slots[at] = slot;
        _expires[at] = expiresTick;
        _count[f]++;
        return true;
    }

    /// <summary>
    /// The live head of <paramref name="fifo"/>: a verified head is returned as is; otherwise nodes are inspected from the head, stale
    /// ones popped, until a live one is found and marked verified. With <paramref name="countVisits"/> each inspected node counts one
    /// <see cref="CounterId.InventoryWorldItemCapCandidates"/>.
    /// </summary>
    /// <param name="fifo">The FIFO.</param>
    /// <param name="table">The WorldItem table.</param>
    /// <param name="countVisits">True on the cap-eviction path.</param>
    /// <param name="slot">The head item's table slot when found.</param>
    /// <returns>True when the FIFO holds a live item.</returns>
    public bool TryPeekHead(ExpiryFifo fifo, WorldItemTable table, bool countVisits, out ushort slot)
    {
        int f = (int)fifo;
        while (_count[f] > 0)
        {
            int at = Index(f, 0);
            if (_headVerified[f])
            {
                slot = _slots[at];
                return true;
            }

            if (countVisits)
            {
                _counters.Add(CounterId.InventoryWorldItemCapCandidates);
            }

            if (table.IsLiveAt(_slots[at], _ids[at]))
            {
                _headVerified[f] = true;
                slot = _slots[at];
                return true;
            }

            Pop(fifo); // a lazy tombstone (picked up or evicted)
        }

        slot = WorldItemTable.NoSlot;
        return false;
    }

    /// <summary>The expiry tick stored in the head node (call after a successful <see cref="TryPeekHead"/>).</summary>
    /// <param name="fifo">The FIFO.</param>
    /// <returns>The head's expiry tick.</returns>
    public uint HeadExpiresTick(ExpiryFifo fifo) => _expires[Index((int)fifo, 0)];

    /// <summary>Pops the head node (the expired or evicted item, or a tombstone). An empty FIFO is left as is.</summary>
    /// <param name="fifo">The FIFO.</param>
    public void Pop(ExpiryFifo fifo)
    {
        int f = (int)fifo;
        if (_count[f] == 0)
        {
            return;
        }

        _head[f] = (_head[f] + 1) % _capacity;
        _count[f]--;
        _headVerified[f] = false;
    }

    /// <summary>The item <paramref name="id"/> left the table: if it is the head of <paramref name="fifo"/>, the head memo is dropped (its node is now a tombstone).</summary>
    /// <param name="fifo">The item's FIFO (<see cref="ExpiryFifo.None"/> is ignored).</param>
    /// <param name="id">The item's raw id.</param>
    public void OnRemoved(ExpiryFifo fifo, uint id)
    {
        if (fifo == ExpiryFifo.None)
        {
            return;
        }

        int f = (int)fifo;
        if (_count[f] > 0 && _ids[Index(f, 0)] == id)
        {
            _headVerified[f] = false;
        }
    }

    /// <summary>Drops the stale nodes of <paramref name="fifo"/>, keeping the live ones in order (a full ring on append).</summary>
    /// <param name="fifo">The FIFO.</param>
    /// <param name="table">The WorldItem table.</param>
    public void Compact(ExpiryFifo fifo, WorldItemTable table)
    {
        int f = (int)fifo;
        int n = _count[f];
        int kept = 0;
        for (int i = 0; i < n; i++)
        {
            int from = Index(f, i);
            if (!table.IsLiveAt(_slots[from], _ids[from]))
            {
                continue;
            }

            int to = Index(f, kept);
            _ids[to] = _ids[from];
            _slots[to] = _slots[from];
            _expires[to] = _expires[from];
            kept++;
        }

        _count[f] = kept;
        _headVerified[f] = false;
    }

    /// <summary>The number of live nodes of <paramref name="fifo"/> (the canonical dump writes it before <see cref="WriteLiveIds"/>).</summary>
    /// <param name="fifo">The FIFO.</param>
    /// <param name="table">The WorldItem table.</param>
    /// <returns>The number of live nodes.</returns>
    public int LiveCount(ExpiryFifo fifo, WorldItemTable table)
    {
        int f = (int)fifo;
        int live = 0;
        for (int i = 0; i < _count[f]; i++)
        {
            int at = Index(f, i);
            if (table.IsLiveAt(_slots[at], _ids[at]))
            {
                live++;
            }
        }

        return live;
    }

    /// <summary>Writes the raw ids of the live nodes of <paramref name="fifo"/> in queue order (the canonical dump).</summary>
    /// <param name="fifo">The FIFO.</param>
    /// <param name="table">The WorldItem table.</param>
    /// <param name="w">The dump writer.</param>
    public void WriteLiveIds(ExpiryFifo fifo, WorldItemTable table, ref CanonicalWriter w)
    {
        int f = (int)fifo;
        for (int i = 0; i < _count[f]; i++)
        {
            int at = Index(f, i);
            if (table.IsLiveAt(_slots[at], _ids[at]))
            {
                w.WriteU32(_ids[at]);
            }
        }
    }

    /// <summary>The raw id at queue position <paramref name="i"/> of <paramref name="fifo"/> (tests).</summary>
    /// <param name="fifo">The FIFO.</param>
    /// <param name="i">0 = head.</param>
    /// <returns>The raw id (live or stale).</returns>
    internal uint IdAt(ExpiryFifo fifo, int i) => _ids[Index((int)fifo, i)];

    private int Index(int fifo, int position) => (fifo * _capacity) + ((_head[fifo] + position) % _capacity);
}
