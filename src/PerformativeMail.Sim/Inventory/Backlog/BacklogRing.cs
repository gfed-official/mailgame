using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Backlog;

/// <summary>
/// The Intake backlog (GDD R14; ADR-0017 section 6: its storage and drain are Inventory's; story grid-inventory/021): an append-ordered
/// queue of whole detached mail entries, each a live entry-pool slot in no container and held by no WorldItem (the WorldItems pattern:
/// a return already owns its detached entry and mailIds block, so its append is O(1) with no copy). The links are intrusive arrays
/// indexed by entry slot, preallocated at world build with capacity = the entry pool's (<c>inventory.entryPoolCapacity</c>; Mail Lead
/// planning default pending Evan, TD-157 item 3), so the queue never grows and every live detached entry fits: a full ring is
/// unreachable (<see cref="SimAssertCode.InventoryBacklogRingFull"/>). The drain removes from anywhere in O(1) (the skip rule lets a
/// later entry leave ahead of one that does not fit). <see cref="Units"/> and <see cref="Count"/> are O(1) memos;
/// <see cref="AppendSeq"/> counts appends (the drain's gating memo reads it). Raises nothing and touches no version: the caller
/// raises <c>BacklogUnits</c>. Allocation-free after construction; never throws on the tick path. Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// if (ring.CanAppend(entrySlot)) { ring.Append(entrySlot, count); }
/// for (ushort s = ring.Head; s != BacklogRing.None; s = ring.Next(s)) { /* append order */ }
/// </code>
/// </example>
internal sealed class BacklogRing
{
    /// <summary>The end-of-queue link.</summary>
    public const ushort None = ushort.MaxValue;

    private readonly ushort[] _next;
    private readonly ushort[] _prev;
    private readonly bool[] _linked;
    private ushort _head = None;
    private ushort _tail = None;
    private int _count;
    private int _units;
    private uint _appendSeq;
    private int _limit;

    /// <summary>Creates an empty ring for an entry pool of <paramref name="capacity"/> slots.</summary>
    /// <param name="capacity">The entry pool's capacity (1 to 65535).</param>
    public BacklogRing(int capacity)
    {
        _next = new ushort[capacity];
        _prev = new ushort[capacity];
        _linked = new bool[capacity];
        _limit = capacity;
    }

    /// <summary>The entry slots the ring can hold.</summary>
    public int Capacity => _linked.Length;

    /// <summary>The queued entries (<c>IInventoryApi.BacklogEntries</c>).</summary>
    public int Count => _count;

    /// <summary>The queued units, summed over the entries (<c>IInventoryApi.BacklogUnits</c>).</summary>
    public int Units => _units;

    /// <summary>The appends made so far (wraps; only compared for equality).</summary>
    public uint AppendSeq => _appendSeq;

    /// <summary>The oldest entry's slot, or <see cref="None"/>.</summary>
    public ushort Head => _head;

    /// <summary>The entry after <paramref name="slot"/> in append order, or <see cref="None"/>.</summary>
    /// <param name="slot">A queued entry slot.</param>
    /// <returns>The next slot.</returns>
    public ushort Next(ushort slot) => _next[slot];

    /// <summary>True when <paramref name="slot"/> is queued.</summary>
    /// <param name="slot">An entry slot.</param>
    /// <returns>True when queued.</returns>
    public bool Contains(ushort slot) => slot < _linked.Length && _linked[slot];

    /// <summary>True when <see cref="Append"/> of <paramref name="slot"/> would succeed: in range, not queued, and the ring not full.</summary>
    /// <param name="slot">An entry slot.</param>
    /// <returns>True when appendable.</returns>
    public bool CanAppend(ushort slot) => slot < _linked.Length && !_linked[slot] && _count < _limit;

    /// <summary>
    /// TEST-ONLY seam (story 021 AC "Ring capacity"): caps the queue at <paramref name="limit"/> entries, so a test can reach the
    /// unreachable full-ring path (<see cref="SimAssertCode.InventoryBacklogRingFull"/>). Production never calls it: the limit is
    /// <see cref="Capacity"/> from construction.
    /// </summary>
    /// <param name="limit">The entries the ring takes before <see cref="CanAppend"/> refuses (0 to <see cref="Capacity"/>).</param>
    internal void LimitForTests(int limit) => _limit = limit;

    /// <summary>Appends <paramref name="slot"/> (holding <paramref name="units"/>) at the tail. The caller checked <see cref="CanAppend"/>.</summary>
    /// <param name="slot">The detached entry's slot.</param>
    /// <param name="units">Its count.</param>
    public void Append(ushort slot, int units)
    {
        _linked[slot] = true;
        _next[slot] = None;
        _prev[slot] = _tail;
        if (_tail == None)
        {
            _head = slot;
        }
        else
        {
            _next[_tail] = slot;
        }

        _tail = slot;
        _count++;
        _units += units;
        _appendSeq = unchecked(_appendSeq + 1u);
    }

    /// <summary>Unlinks the queued <paramref name="slot"/> (holding <paramref name="units"/>) from wherever it is.</summary>
    /// <param name="slot">A queued entry slot.</param>
    /// <param name="units">Its count.</param>
    public void Remove(ushort slot, int units)
    {
        ushort p = _prev[slot];
        ushort n = _next[slot];
        if (p == None)
        {
            _head = n;
        }
        else
        {
            _next[p] = n;
        }

        if (n == None)
        {
            _tail = p;
        }
        else
        {
            _prev[n] = p;
        }

        _linked[slot] = false;
        _count--;
        _units -= units;
    }

    /// <summary>Writes the count, then each queued entry's id in queue order (the inventory dump section, after the WorldItems block).</summary>
    /// <param name="w">The dump writer.</param>
    /// <param name="entries">The entry pool (ids).</param>
    public void Write(ref CanonicalWriter w, EntryPool entries)
    {
        w.WriteU32((uint)_count);
        for (ushort s = _head; s != None; s = _next[s])
        {
            w.WriteU32(entries[s].Id.Value);
        }
    }
}
