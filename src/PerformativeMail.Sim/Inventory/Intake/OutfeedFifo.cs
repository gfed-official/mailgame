using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Intake;

/// <summary>
/// The outfeed memo (ADR-0009 section 5; GDD R14; TR-inventory-010; story grid-inventory/012): one index-linked FIFO of
/// <c>(arrivalSeq, entrySlot, entryId)</c> nodes per container whose def has <c>BeltAccess.OutfeedOnly</c> (the Intake), all in one
/// preallocated node pool. A list's head and tail handles live in its <see cref="ContainerHeader"/> (<see cref="ContainerHeader.OutfeedHead"/>
/// and <see cref="ContainerHeader.OutfeedTail"/>: node index + 1, 0 = empty). The <see cref="InventoryMutator"/> appends a node on every
/// insertion into such a container and on every merge onto one of its entries, which restamps the target (arrivalSeq is a
/// per-container counter, so append order is sorted order), and leaves the old node behind as a <b>lazy tombstone</b> when the entry
/// leaves or is restamped; a node is live while its entry slot still holds the same entry id, placed in this container, with the same
/// <c>arrivalSeq</c> (a returned entry keeps its id but gets a new <c>arrivalSeq</c>, and so does a merged stack, so the old node is
/// stale). <see cref="TryPeekHead"/> pops stale heads. When a list's tombstones (from leaves and restamps alike) exceed its live entries
/// the mutator <see cref="Compact"/>s it, so after compaction a list holds at most twice its live entries; a partial merge can briefly
/// reach 2 × live + 1 nodes before its compaction. The pool is sized twice the entry pool (see
/// <see cref="InventorySettings.OutfeedNodeCapacity"/>), so exhaustion is not expected; the Release behaviour if it ever happens is
/// tracked in TD-132 item 3. Peek and take are O(1) amortised: each node is appended once and
/// inspected live at most once per peek before it is popped or compacted away. <see cref="CounterId.InventoryOutfeedCandidates"/>
/// (H-PERF-6a, in every build) counts one per candidate visit = one FIFO node examined (GDD H-PERF-6 unit: one per cell probe or
/// candidate visit): the live head and each stale head popped by a peek, and each node walked by a compaction, a clear or a rebuild;
/// never per call, per anchor or per batch. Appending a node examines none and counts nothing.
/// <para>
/// The nodes are a derived memo, not Sim state: the dump carries only the header's <c>NextArrivalSeq</c> and the entries'
/// <c>arrivalSeq</c>, and <see cref="Rebuild"/> rebuilds a list from the live entries, ascending by <c>arrivalSeq</c> (after a load, and by
/// the M1 Intake sort). Allocation-free after construction; never throws on the tick path. Not thread-safe: Sim tick thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// fifo.Append(slot, ref header, e.ArrivalSeq, entrySlot, e.Id.Value);
/// if (fifo.TryPeekHead(slot, ref header, out ushort head)) { /* entries[head] is the oldest live entry */ }
/// </code>
/// </example>
internal sealed class OutfeedFifo
{
    /// <summary>The handle meaning "no node".</summary>
    public const uint NoNode = 0;

    private readonly uint[] _seq;
    private readonly ushort[] _entrySlot;
    private readonly uint[] _entryId;
    private readonly uint[] _next;
    private readonly int[] _nodeCount;
    private readonly Arrival[] _rebuild;
    private readonly Arrival[] _rebuildScratch;
    private readonly EntryPool _entries;
    private readonly SimCounters _counters;
    private uint _freeHead;
    private int _freeCount;

    /// <summary>Preallocates the node pool, the per-container node counts and the rebuild scratch.</summary>
    /// <param name="nodeCapacity">Nodes in the shared pool (at least 1).</param>
    /// <param name="containerCapacity">Container table slots.</param>
    /// <param name="entries">The entry pool (a node's liveness is read from it).</param>
    /// <param name="counters">The world's counters (<see cref="CounterId.InventoryOutfeedCandidates"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">A capacity is below 1.</exception>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public OutfeedFifo(int nodeCapacity, int containerCapacity, EntryPool entries, SimCounters counters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(containerCapacity, 1);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(counters);
        _seq = new uint[nodeCapacity];
        _entrySlot = new ushort[nodeCapacity];
        _entryId = new uint[nodeCapacity];
        _next = new uint[nodeCapacity];
        for (int i = 0; i < nodeCapacity; i++)
        {
            _next[i] = i + 1 < nodeCapacity ? (uint)(i + 2) : NoNode; // the free list threads the pool in index order
        }

        _freeHead = 1;
        _freeCount = nodeCapacity;
        _nodeCount = new int[containerCapacity];
        _rebuild = new Arrival[entries.Capacity];
        _rebuildScratch = new Arrival[entries.Capacity];
        _entries = entries;
        _counters = counters;
    }

    /// <summary>The number of nodes in the pool.</summary>
    public int Capacity => _seq.Length;

    /// <summary>Free nodes in the pool.</summary>
    public int FreeCount => _freeCount;

    /// <summary>Nodes in the container's list, live and tombstoned.</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <returns>The node count.</returns>
    public int NodeCount(ushort containerSlot) => _nodeCount[containerSlot];

    /// <summary>
    /// Reads one node of the container's list, for a read-only walk from <see cref="ContainerHeader.OutfeedHead"/> (tests and
    /// diagnostics, like <see cref="Placement.StackIndex.Next"/>): changes nothing and counts nothing.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="node">A node handle of the list (not <see cref="NoNode"/>).</param>
    /// <param name="arrivalSeq">The node's <c>arrivalSeq</c>.</param>
    /// <param name="entrySlot">The node's entry pool slot.</param>
    /// <param name="live">True when the node is live (not a tombstone).</param>
    /// <returns>The next node handle, <see cref="NoNode"/> at the tail.</returns>
    public uint ReadNode(ushort containerSlot, uint node, out uint arrivalSeq, out ushort entrySlot, out bool live)
    {
        int i = (int)node - 1;
        arrivalSeq = _seq[i];
        entrySlot = _entrySlot[i];
        live = IsLive(containerSlot, i);
        return _next[i];
    }

    /// <summary>
    /// Appends a node for the entry just stamped with <paramref name="arrivalSeq"/> at the tail of the container's list. False (asserts
    /// <see cref="SimAssertCode.InventoryOutfeedNodePoolExhausted"/> under <c>INVENTORY_ASSERT</c>) when the pool is empty, which the
    /// compaction rule makes unreachable; Release then leaves the entry out of the memo.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="h">The container's header (its outfeed handles are updated).</param>
    /// <param name="arrivalSeq">The entry's new <c>arrivalSeq</c>.</param>
    /// <param name="entrySlot">The entry's pool slot.</param>
    /// <param name="entryId">The entry's id value.</param>
    /// <returns>True when appended.</returns>
    public bool Append(ushort containerSlot, ref ContainerHeader h, uint arrivalSeq, ushort entrySlot, uint entryId)
    {
        InventoryAssert.That(_freeHead != NoNode, SimAssertCode.InventoryOutfeedNodePoolExhausted);
        if (_freeHead == NoNode)
        {
            return false;
        }

        uint node = _freeHead;
        int i = (int)node - 1;
        _freeHead = _next[i];
        _freeCount--;
        _seq[i] = arrivalSeq;
        _entrySlot[i] = entrySlot;
        _entryId[i] = entryId;
        _next[i] = NoNode;
        if (h.OutfeedTail == NoNode)
        {
            h.OutfeedHead = node;
        }
        else
        {
            _next[(int)h.OutfeedTail - 1] = node;
        }

        h.OutfeedTail = node;
        _nodeCount[containerSlot]++;
        return true;
    }

    /// <summary>
    /// The pool slot of the container's oldest live entry: pops stale heads (each popped node and the live head count one candidate).
    /// Changes only the memo. False when the list is (now) empty.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="h">The container's header.</param>
    /// <param name="entrySlot">The head entry's pool slot when found.</param>
    /// <returns>True when a live entry heads the list.</returns>
    public bool TryPeekHead(ushort containerSlot, ref ContainerHeader h, out ushort entrySlot)
    {
        uint node = h.OutfeedHead;
        while (node != NoNode)
        {
            _counters.Add(CounterId.InventoryOutfeedCandidates);
            int i = (int)node - 1;
            if (IsLive(containerSlot, i))
            {
                entrySlot = _entrySlot[i];
                return true;
            }

            uint next = _next[i];
            FreeNode(node);
            _nodeCount[containerSlot]--;
            h.OutfeedHead = next;
            if (next == NoNode)
            {
                h.OutfeedTail = NoNode;
            }

            node = next;
        }

        entrySlot = 0;
        return false;
    }

    /// <summary>
    /// Called by the mutator after one of the list's nodes became a tombstone (its entry left the container, or a merge restamped it
    /// and appended a fresh node): compacts the list when its tombstones (every node beyond the live ones) exceed
    /// <paramref name="liveEntries"/>.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="h">The container's header.</param>
    /// <param name="liveEntries">The container's live entries now (each holds exactly one live node).</param>
    public void OnTombstoned(ushort containerSlot, ref ContainerHeader h, int liveEntries)
    {
        if (_nodeCount[containerSlot] - liveEntries > liveEntries)
        {
            Compact(containerSlot, ref h);
        }
    }

    /// <summary>Drops every tombstone of the container's list, keeping the live nodes in order (one candidate per node walked).</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="h">The container's header.</param>
    public void Compact(ushort containerSlot, ref ContainerHeader h)
    {
        uint node = h.OutfeedHead;
        uint head = NoNode;
        uint tail = NoNode;
        int kept = 0;
        while (node != NoNode)
        {
            _counters.Add(CounterId.InventoryOutfeedCandidates);
            int i = (int)node - 1;
            uint next = _next[i];
            if (IsLive(containerSlot, i))
            {
                _next[i] = NoNode;
                if (tail == NoNode)
                {
                    head = node;
                }
                else
                {
                    _next[(int)tail - 1] = node;
                }

                tail = node;
                kept++;
            }
            else
            {
                FreeNode(node);
            }

            node = next;
        }

        h.OutfeedHead = head;
        h.OutfeedTail = tail;
        _nodeCount[containerSlot] = kept;
    }

    /// <summary>Returns every node of the container's list to the pool and empties it (one candidate per node walked).</summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="h">The container's header.</param>
    public void Clear(ushort containerSlot, ref ContainerHeader h)
    {
        uint node = h.OutfeedHead;
        while (node != NoNode)
        {
            _counters.Add(CounterId.InventoryOutfeedCandidates);
            uint next = _next[(int)node - 1];
            FreeNode(node);
            node = next;
        }

        h.OutfeedHead = NoNode;
        h.OutfeedTail = NoNode;
        _nodeCount[containerSlot] = 0;
    }

    /// <summary>
    /// The state of a freshly created container slot: an empty list (its header's handles were cleared with the header). The nodes of a
    /// previous occupant must have been returned with <see cref="Clear"/> before the slot was freed (containers are never freed at M-1a).
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    public void ResetSlot(ushort containerSlot) => _nodeCount[containerSlot] = 0;

    /// <summary>
    /// Rebuilds the container's list from its live entries, ascending by <c>arrivalSeq</c> (each placed entry of the container with a
    /// non-zero <c>arrivalSeq</c>; ADR-0009 section 5: after a load and on the M1 Intake sort). Changes no Sim state (no record, no
    /// version). O(entry pool + n log n); allocation-free. Counts one candidate per old node walked by the <see cref="Clear"/>; the entry
    /// pool scan examines entries, not nodes, and the appends examine none, so neither counts.
    /// </summary>
    /// <param name="containerSlot">The container's table slot.</param>
    /// <param name="h">The container's header.</param>
    public void Rebuild(ushort containerSlot, ref ContainerHeader h)
    {
        Clear(containerSlot, ref h);
        int n = 0;
        for (int s = 0; s < _entries.Capacity; s++)
        {
            ref EntryRecord e = ref _entries[(ushort)s];
            if (e.IsLive && e.ContainerSlot == containerSlot && e.ArrivalSeq != 0)
            {
                _rebuild[n++] = new Arrival(e.ArrivalSeq, (ushort)s);
            }
        }

        StableSort.Sort(_rebuild.AsSpan(0, n), _rebuildScratch.AsSpan(0, n), new BySeqThenSlot());
        for (int i = 0; i < n; i++)
        {
            Arrival a = _rebuild[i];
            _ = Append(containerSlot, ref h, a.Seq, a.Slot, _entries[a.Slot].Id.Value);
        }
    }

    private bool IsLive(ushort containerSlot, int i)
    {
        ref EntryRecord e = ref _entries[_entrySlot[i]];
        return e.Id.Value == _entryId[i] && e.ContainerSlot == containerSlot && e.ArrivalSeq == _seq[i] && e.IsLive;
    }

    private void FreeNode(uint node)
    {
        _next[(int)node - 1] = _freeHead;
        _freeHead = node;
        _freeCount++;
    }

    /// <summary>One live entry of a rebuild: its <c>arrivalSeq</c> and pool slot.</summary>
    private readonly record struct Arrival(uint Seq, ushort Slot);

    /// <summary>Ascending <c>arrivalSeq</c> (unique within a container), then pool slot as the total tie-break (ADR-0006 rule 5).</summary>
    private readonly struct BySeqThenSlot : IComparer<Arrival>
    {
        public int Compare(Arrival x, Arrival y) => x.Seq != y.Seq ? x.Seq.CompareTo(y.Seq) : x.Slot.CompareTo(y.Slot);
    }
}
