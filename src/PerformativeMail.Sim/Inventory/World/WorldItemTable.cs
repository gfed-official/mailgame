using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.World;

/// <summary>
/// How a WorldItem came to be in the world (GDD R12 <c>origin</c>). Values are append-only. M-1a creates <see cref="Dropped"/>,
/// <see cref="Thrown"/> and <see cref="Loot"/>; <see cref="BeltLeak"/> and <see cref="Spill"/> are reserved for M-1b (belts; death
/// spill heaps, which hold more than one entry) and are refused by <c>CreateWorldItem</c> until then.
/// </summary>
/// <example><code>EntityId w = inventory.CreateWorldItem(entry, pos, WorldItemOrigin.Thrown, onWater: false, landingTick);</code></example>
public enum WorldItemOrigin : byte
{
    /// <summary>Dropped by a player (<c>drop</c>, story 020).</summary>
    Dropped = 0,

    /// <summary>A throw that landed (timer from the landing tick, GDD R12).</summary>
    Thrown = 1,

    /// <summary>Reserved (M-1b): units leaking off a belt.</summary>
    BeltLeak = 2,

    /// <summary>Reserved (M-1b): a death-bag spill heap (every non-mail entry of the bag, one WorldItem).</summary>
    Spill = 3,

    /// <summary>Loot mail (Mail &amp; Destinations R15): kept off every expiry FIFO and never a cap candidate until Payday (Mail's).</summary>
    Loot = 4,
}

/// <summary>
/// The expiry FIFO a WorldItem is queued on (ADR-0009 section 5: one FIFO per duration class). The <c>waterFloat</c> class (300 ticks)
/// has two queues, one for mail (returns to the Intake) and one for non-mail (destroyed), so the non-mail cap candidate is the lower
/// of two non-mail heads as section 5 says; <see cref="Bag"/> is declared for M-1b bags and nothing is appended to it yet.
/// <see cref="None"/> is a Loot item, on no FIFO.
/// </summary>
internal enum ExpiryFifo : byte
{
    /// <summary><c>mailWorldItem</c>: dropped or thrown mail on ground (9000 ticks).</summary>
    MailWorldItem = 0,

    /// <summary><c>nonMailWorldItem</c> (and <c>spillHeap</c> at M-1b): non-mail on ground (9000 ticks).</summary>
    NonMailWorldItem = 1,

    /// <summary><c>waterFloat</c>, mail queue: mail that landed on water (300 ticks, then the Intake).</summary>
    WaterFloatMail = 2,

    /// <summary><c>waterFloat</c>, non-mail queue: non-mail that landed on water (300 ticks, then destroyed).</summary>
    WaterFloatNonMail = 3,

    /// <summary><c>bag</c> (900 ticks): declared for M-1b bags; empty at M-1a.</summary>
    Bag = 4,

    /// <summary>On no FIFO (Loot).</summary>
    None = 0xFF,
}

/// <summary>
/// One WorldItem (GDD R12; ADR-0009 section 5; story grid-inventory/013): exactly one entry (a detached <see cref="EntryRecord"/> slot,
/// in no container), its position in millimetres (the GDD's int32 cm is flagged to the GDD re-review; Sim Core uses mm), the tick it was
/// created or landed and the tick it expires (<c>createdTick + seconds x 30</c>, GDD D6), its origin, the water flag and its expiry
/// FIFO. <see cref="PrevSlot"/> / <see cref="NextSlot"/> link the live items in ascending id order (the canonical dump's order).
/// </summary>
internal struct WorldItemRecord
{
    /// <summary>The WorldItem's id (class <see cref="EntityClass.WorldItem"/>); none when the slot is free.</summary>
    public EntityId Id;

    /// <summary>The position, mm (supplied by the caller: Throwing's landing resolution, Drop's ground snap).</summary>
    public Vec3Mm PosMm;

    /// <summary>The creation tick (the landing tick for a throw).</summary>
    public uint CreatedTick;

    /// <summary>The expiry tick (<c>CreatedTick</c> + the FIFO's duration); 0 for a Loot item (never expires).</summary>
    public uint ExpiresTick;

    /// <summary>The entry's pool slot (detached: <c>ContainerSlot == NoContainer</c>).</summary>
    public ushort EntrySlot;

    /// <summary>The previous live item in ascending id order, or <see cref="WorldItemTable.NoSlot"/>.</summary>
    public ushort PrevSlot;

    /// <summary>The next live item in ascending id order, or <see cref="WorldItemTable.NoSlot"/>.</summary>
    public ushort NextSlot;

    /// <summary>The origin.</summary>
    public WorldItemOrigin Origin;

    /// <summary>The expiry FIFO.</summary>
    public ExpiryFifo Fifo;

    /// <summary>True when it landed on a non-ground (water) tile.</summary>
    public bool OnWater;

    /// <summary>True when the entry is mail.</summary>
    public bool IsMail;

    /// <summary>True when the slot holds a live item.</summary>
    public readonly bool IsLive => !Id.IsNone;
}

/// <summary>
/// The WorldItem table (ADR-0009 section 5; story grid-inventory/013): a preallocated <see cref="WorldItemRecord"/> array sized from
/// <c>inventory.worldItemCapacity</c> (the cap plus headroom for a temporarily exceeded cap), a LIFO free-slot stack, an open-addressed
/// <c>worldItemId -&gt; slot</c> <see cref="IdMap"/> and an intrusive list of the live items in ascending id order (ids come from the
/// injected <c>EntityIds</c> and only rise, so a new item is always the tail). Slots are reused, ids never (ADR-0004). The caller
/// allocates the id; this table only stores it. Never enumerated through the map; 0 B after construction. Not thread-safe: Sim tick
/// thread only.
/// </summary>
/// <example>
/// <code>
/// if (table.TryAdd(id, out ushort slot)) { ref WorldItemRecord w = ref table[slot]; w.EntrySlot = entrySlot; }
/// for (ushort s = table.FirstSlot; s != WorldItemTable.NoSlot; s = table[s].NextSlot) { /* ascending id */ }
/// </code>
/// </example>
internal sealed class WorldItemTable
{
    /// <summary>"No slot" in the links and lookups.</summary>
    public const ushort NoSlot = 0xFFFF;

    /// <summary>The largest capacity (slot 0xFFFF is the <see cref="NoSlot"/> sentinel).</summary>
    public const int MaxCapacity = ushort.MaxValue;

    private readonly WorldItemRecord[] _records;
    private readonly ushort[] _freeSlots;
    private readonly IdMap _map;
    private int _freeCount;
    private ushort _first = NoSlot;
    private ushort _last = NoSlot;

    /// <summary>Allocates the table.</summary>
    /// <param name="capacity">Slots, 1 to <see cref="MaxCapacity"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is out of range.</exception>
    public WorldItemTable(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, MaxCapacity);
        _records = new WorldItemRecord[capacity];
        _freeSlots = new ushort[capacity];
        for (int i = 0; i < capacity; i++)
        {
            _freeSlots[i] = (ushort)(capacity - 1 - i); // a fresh table hands out ascending slots
        }

        _freeCount = capacity;
        _map = new IdMap(capacity);
    }

    /// <summary>The number of slots.</summary>
    public int Capacity => _records.Length;

    /// <summary>The number of live items (what the soft cap counts: a WorldItem, not its entries).</summary>
    public int LiveCount => _records.Length - _freeCount;

    /// <summary>The live item with the lowest id, or <see cref="NoSlot"/>.</summary>
    public ushort FirstSlot => _first;

    /// <summary>The record in <paramref name="slot"/> (a default record when free).</summary>
    /// <param name="slot">A slot below <see cref="Capacity"/>.</param>
    public ref WorldItemRecord this[ushort slot] => ref _records[slot];

    /// <summary>
    /// Takes a free slot for <paramref name="id"/> (higher than every id added before; asserted by the caller's allocator) and links it
    /// at the tail of the id-ordered list. The record is cleared and given the id; the caller fills the rest. False, changing nothing,
    /// when the table is full or the id is none or already present.
    /// </summary>
    /// <param name="id">The new item's id.</param>
    /// <param name="slot">The slot when added.</param>
    /// <returns>True when added.</returns>
    public bool TryAdd(EntityId id, out ushort slot)
    {
        if (_freeCount == 0 || id.IsNone || _map.TryGet(id.Value, out _))
        {
            slot = NoSlot;
            return false;
        }

        _freeCount--;
        slot = _freeSlots[_freeCount];
        ref WorldItemRecord r = ref _records[slot];
        r = default;
        r.Id = id;
        r.PrevSlot = _last;
        r.NextSlot = NoSlot;
        if (_last != NoSlot)
        {
            _records[_last].NextSlot = slot;
        }
        else
        {
            _first = slot;
        }

        _last = slot;
        _ = _map.TryAdd(id.Value, slot); // cannot fail: checked absent above and the map holds twice the table
        return true;
    }

    /// <summary>Frees a live slot: unlinks it, removes its id from the map for ever and clears the record. A free slot is ignored.</summary>
    /// <param name="slot">The slot.</param>
    public void Remove(ushort slot)
    {
        ref WorldItemRecord r = ref _records[slot];
        if (!r.IsLive)
        {
            return;
        }

        if (r.PrevSlot != NoSlot)
        {
            _records[r.PrevSlot].NextSlot = r.NextSlot;
        }
        else
        {
            _first = r.NextSlot;
        }

        if (r.NextSlot != NoSlot)
        {
            _records[r.NextSlot].PrevSlot = r.PrevSlot;
        }
        else
        {
            _last = r.PrevSlot;
        }

        _ = _map.Remove(r.Id.Value);
        r = default;
        _freeSlots[_freeCount] = slot;
        _freeCount++;
    }

    /// <summary>Looks up the slot of a live item.</summary>
    /// <param name="id">The WorldItem id.</param>
    /// <param name="slot">The slot when live, else <see cref="NoSlot"/>.</param>
    /// <returns>True when live.</returns>
    public bool TryGetSlot(EntityId id, out ushort slot)
    {
        if (!id.IsNone && _map.TryGet(id.Value, out slot))
        {
            return true;
        }

        slot = NoSlot;
        return false;
    }

    /// <summary>True when <paramref name="slot"/> holds the live item <paramref name="id"/> (a FIFO node's liveness test).</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="id">The raw id.</param>
    /// <returns>True when live and the same id.</returns>
    public bool IsLiveAt(ushort slot, uint id) => slot < _records.Length && _records[slot].Id.Value == id && id != 0u;
}
