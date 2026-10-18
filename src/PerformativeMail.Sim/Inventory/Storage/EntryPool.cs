using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Storage;

/// <summary>
/// The global entry pool (ADR-0009 section 5; ADR-0004 sections 1 and 3; story grid-inventory/002): a preallocated
/// <see cref="EntryRecord"/> array, a LIFO free-slot stack and an <c>entryId -&gt; slot</c> <see cref="IdMap"/>. Slots are reused,
/// ids never: <see cref="LastId"/> is Sim state that only rises (first id 1, last 0x7FFF_FFFF; bit 31 is the client temp flag),
/// and there is no free list of ids. Exhaustion follows ADR-0004 section 3 as <c>MailIds</c> does it: test before increment, write one
/// <see cref="CheckCode.IdExhausted"/> record (a = 0, b = <see cref="LastId"/>), assert, return false with nothing mutated. A full pool
/// writes <see cref="CheckCode.InventoryEntryPoolExhausted"/> (a = capacity, b = live) and returns false. Everything is allocated in
/// the constructor; the mutators allocate nothing. <c>InventoryMutator</c> (story 005) is the only caller outside tests and owns the
/// rule "an entry whose count reaches 0 is freed in the same call". Not thread-safe: Sim tick thread only.
/// </summary>
/// <example>
/// <code>
/// var pool = new EntryPool(8192, ring);
/// if (pool.TryAllocate(tick, out ushort slot, out EntryId id)) { ref EntryRecord e = ref pool[slot]; pool.SetCount(slot, 3); }
/// pool.Free(slot);   // id is gone for ever; the slot goes back on the stack
/// </code>
/// </example>
public sealed class EntryPool
{
    /// <summary>The largest real entry id (bit 31 clear).</summary>
    public const uint MaxRealId = 0x7FFF_FFFFu;

    private readonly EntryRecord[] _records;
    private readonly ushort[] _freeSlots;
    private readonly IdMap _map;
    private readonly SimCheckRing _ring;
    private int _freeCount;
    private uint _lastId;

    /// <summary>Allocates the pool.</summary>
    /// <param name="capacity">Pool slots, 1 to 65,535 (slots are <c>ushort</c>).</param>
    /// <param name="ring">The world's check ring.</param>
    /// <param name="initialLastId">The last id already issued (0 = none, so the first id is 1); at most <see cref="MaxRealId"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> or <paramref name="initialLastId"/> is out of range.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="ring"/> is null.</exception>
    public EntryPool(int capacity, SimCheckRing ring, uint initialLastId = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialLastId, MaxRealId);
        ArgumentNullException.ThrowIfNull(ring);
        _records = new EntryRecord[capacity];
        _freeSlots = new ushort[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _freeSlots[i] = (ushort)(capacity - 1 - i); // top of the stack is slot 0, so fresh pools hand out ascending slots
        }

        _freeCount = capacity;
        _map = new IdMap(capacity);
        _ring = ring;
        _lastId = initialLastId;
    }

    /// <summary>The number of pool slots (fixed at construction).</summary>
    public int Capacity => _records.Length;

    /// <summary>The number of live entries.</summary>
    public int LiveCount => _records.Length - _freeCount;

    /// <summary>The last entry id issued (Sim state; 0 when none has been).</summary>
    public uint LastId => _lastId;

    /// <summary>The record in pool slot <paramref name="slot"/> (a default record when the slot is free).</summary>
    /// <param name="slot">A slot below <see cref="Capacity"/>.</param>
    public ref EntryRecord this[ushort slot] => ref _records[slot];

    /// <summary>
    /// Takes a free slot and the next id. The record is cleared and given the id, <c>Count = 1</c> and
    /// <c>ContainerSlot = <see cref="EntryRecord.NoContainer"/></c>; the caller fills the rest. Returns false, mutating nothing, on id
    /// exhaustion (one <see cref="CheckCode.IdExhausted"/> record, asserts under <c>SIM_ASSERT</c>) or a full pool (one
    /// <see cref="CheckCode.InventoryEntryPoolExhausted"/> record).
    /// </summary>
    /// <param name="tick">The current tick (for the check record).</param>
    /// <param name="slot">The pool slot when allocated.</param>
    /// <param name="id">The new id when allocated, else none.</param>
    /// <returns>True when an entry was allocated.</returns>
    public bool TryAllocate(uint tick, out ushort slot, out EntryId id)
    {
        if (_lastId == MaxRealId)
        {
            _ring.Record(CheckCode.IdExhausted, tick, 0, _lastId);
            SimAssert.Fail(SimAssertCode.IdExhausted);
            slot = 0;
            id = default;
            return false;
        }

        if (_freeCount == 0)
        {
            _ring.Record(CheckCode.InventoryEntryPoolExhausted, tick, _records.Length, LiveCount);
            slot = 0;
            id = default;
            return false;
        }

        _lastId++;
        id = EntryId.Real(_lastId);
        _freeCount--;
        slot = _freeSlots[_freeCount];
        ref EntryRecord record = ref _records[slot];
        record = default;
        record.Id = id;
        record.Count = 1;
        record.ContainerSlot = EntryRecord.NoContainer;
        _ = _map.TryAdd(id.Value, slot); // cannot fail: ids are unique and the map holds twice the pool
        return true;
    }

    /// <summary>Frees a live slot: the id leaves the map for ever, the record is cleared and the slot goes back on the stack.</summary>
    /// <param name="slot">A live slot (asserts under <c>INVENTORY_ASSERT</c>; Release ignores a free slot).</param>
    public void Free(ushort slot)
    {
        ref EntryRecord record = ref _records[slot];
        InventoryAssert.That(record.IsLive, SimAssertCode.InventoryEntrySlotFree);
        if (!record.IsLive)
        {
            return;
        }

        _ = _map.Remove(record.Id.Value);
        record = default;
        _freeSlots[_freeCount] = slot;
        _freeCount++;
    }

    /// <summary>Writes <paramref name="count"/> to a live record, asserting <c>1 &lt;= count</c> (GDD R2; a 0 is the caller's cue to <see cref="Free"/> instead).</summary>
    /// <param name="slot">A live slot.</param>
    /// <param name="count">1 to 255 (0 asserts under <c>INVENTORY_ASSERT</c>; Release writes it as given).</param>
    public void SetCount(ushort slot, byte count)
    {
        InventoryAssert.That(count >= 1, SimAssertCode.InventoryCountZero);
        InventoryAssert.That(_records[slot].IsLive, SimAssertCode.InventoryEntrySlotFree);
        _records[slot].Count = count;
    }

    /// <summary>Looks up the pool slot of <paramref name="id"/>.</summary>
    /// <param name="id">The entry id (none and temp ids are never live here).</param>
    /// <param name="slot">The slot when found.</param>
    /// <returns>True when the id is live.</returns>
    public bool TryGetSlot(EntryId id, out ushort slot)
    {
        if (id.IsNone || id.IsTemp)
        {
            slot = 0;
            return false;
        }

        return _map.TryGet(id.Value, out slot);
    }
}
