using PerformativeMail.Sim.Inventory.Storage;

namespace PerformativeMail.Sim.Inventory.Ledger;

/// <summary>
/// The live location states of a conserved unit (story grid-inventory/017; ADR-0009 section 6): <see cref="Placed"/> in a container
/// other than a cursor, <see cref="Cursor"/> on a player's cursor (its own state, <c>InventoryMutator.IsCursor</c>), and the four
/// holder states <see cref="World"/> (a WorldItem's entry), <see cref="Flight"/> (an entry <c>DetachForThrow</c> released),
/// <see cref="Belt"/> (a unit <c>TakeUnit</c> took, held by no entry) and <see cref="Backlog"/> (an entry queued on the Intake
/// backlog). An entry detached inside one call is in no state. Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT (TD-160 item 4; advisory fix, Mail Review PR #36 A11).
/// </summary>
internal enum LedgerState : byte
{
    /// <summary>In a container that is not a cursor.</summary>
    Placed = 0,

    /// <summary>On a player's cursor.</summary>
    Cursor = 1,

    /// <summary>Held by a WorldItem.</summary>
    World = 2,

    /// <summary>A detached entry in flight (released by <c>DetachForThrow</c>, not yet caught or landed).</summary>
    Flight = 3,

    /// <summary>A unit on a belt (taken by <c>TakeUnit</c>, not yet inserted).</summary>
    Belt = 4,

    /// <summary>An entry queued on the Intake backlog.</summary>
    Backlog = 5,
}

/// <summary>
/// The conservation ledger (story grid-inventory/017; ADR-0009 section 6; TR-inventory-012; GDD R19): O(1) count-weighted counters
/// for the mail row (row 0) and one row per item def (row <c>itemId + 1</c>, sized from <c>DefTable&lt;ItemDef&gt;.Count</c> at world
/// build). Each row holds the six live states (<see cref="LedgerState"/>), <c>Consumed</c> split by <see cref="ConsumeReason"/> (the
/// three sub-counters sum to <see cref="Consumed(int)"/>), <c>Destroyed</c>, <c>spawned</c> (a unit's first entry into Inventory
/// custody: <c>Spawn</c> placement or <c>AppendBacklog</c>, ADR-0017 section 6 l.137) and <c>granted</c> (no production grant at
/// M-1a; the <see cref="Recount"/> seam sets it). The invariant the tick-end check reads is
/// <c>Σ live = spawned + granted − consumed − destroyed</c> per row. It also keeps, per entry-pool slot, the holder state a detached
/// entry is in (World, Flight or Backlog; none for a placed entry or one detached mid-call), so a holder crossing knows its source.
/// <para>
/// Only <c>InventoryMutator</c> writes it (the sole-writer architecture test; Approved by Evan 2026-10-22 6:41 PM PT (ADR-0009 §6 l.170 erratum), TD-160 item 1; advisory fix, Mail Review PR #36 A11);
/// its test seams (<see cref="Recount"/>, <see cref="CorruptSpawnedForTests"/>) are the only other writers. Counters are
/// <see cref="long"/>, so no crossing overflows; a crossing never checks a counter (a negative one is the tick-end check's finding).
/// Allocation-free after construction; every crossing is O(1). Not thread-safe: Sim tick thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// long live = ledger.LiveSum(UnitLedger.MailRow);
/// bool holds = live == ledger.Expected(UnitLedger.MailRow);
/// </code>
/// </example>
internal sealed class UnitLedger
{
    /// <summary>The mail row.</summary>
    public const int MailRow = 0;

    /// <summary>The live states per row (<see cref="LedgerState"/>).</summary>
    public const int LiveStateCount = 6;

    /// <summary>The consume reasons per row (<see cref="ConsumeReason"/>).</summary>
    public const int ConsumeReasonCount = 3;

    /// <summary>The per-slot holder value of an entry in no holder state.</summary>
    public const byte NoHolder = 0xFF;

    private const int ConsumedBase = LiveStateCount;
    private const int DestroyedIndex = ConsumedBase + ConsumeReasonCount;
    private const int SpawnedIndex = DestroyedIndex + 1;
    private const int GrantedIndex = SpawnedIndex + 1;
    private const int Stride = GrantedIndex + 1;

    private readonly long[] _counts;
    private readonly byte[] _holders;

    /// <summary>Creates a ledger with every counter 0 and every entry slot in no holder state.</summary>
    /// <param name="itemDefCount">The item def count (<c>DefTable&lt;ItemDef&gt;.Count</c>).</param>
    /// <param name="entryPoolCapacity">The entry pool's capacity (one holder byte per slot).</param>
    public UnitLedger(int itemDefCount, int entryPoolCapacity)
    {
        Rows = 1 + Math.Max(0, itemDefCount);
        _counts = new long[Rows * Stride];
        _holders = new byte[Math.Max(0, entryPoolCapacity)];
        Array.Fill(_holders, NoHolder);
    }

    /// <summary>The rows: the mail row plus one per item def.</summary>
    public int Rows { get; }

    /// <summary>The row of an entry's units: <see cref="MailRow"/> for mail, <c>itemId + 1</c> for an item.</summary>
    /// <param name="e">A live entry.</param>
    /// <returns>The row (out of range for an item id the content does not define).</returns>
    public static int RowOf(in EntryRecord e) => e.IsMail ? MailRow : e.ItemId + 1;

    /// <summary>The units of <paramref name="row"/> in live state <paramref name="state"/>.</summary>
    /// <param name="row">The row.</param>
    /// <param name="state">The live state.</param>
    /// <returns>The units (negative only after a bad crossing).</returns>
    public long Live(int row, LedgerState state) => _counts[(row * Stride) + (int)state];

    /// <summary>The units of <paramref name="row"/> over every live state.</summary>
    /// <param name="row">The row.</param>
    /// <returns>Σ live.</returns>
    public long LiveSum(int row)
    {
        int b = row * Stride;
        long sum = 0;
        for (int s = 0; s < LiveStateCount; s++)
        {
            sum += _counts[b + s];
        }

        return sum;
    }

    /// <summary>The units of <paramref name="row"/> consumed for <paramref name="reason"/>.</summary>
    /// <param name="row">The row.</param>
    /// <param name="reason">The reason.</param>
    /// <returns>The units.</returns>
    public long Consumed(int row, ConsumeReason reason) => _counts[(row * Stride) + ConsumedBase + (int)reason];

    /// <summary>The units of <paramref name="row"/> consumed, over every reason.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The units.</returns>
    public long Consumed(int row)
    {
        int b = (row * Stride) + ConsumedBase;
        return _counts[b] + _counts[b + 1] + _counts[b + 2];
    }

    /// <summary>The units of <paramref name="row"/> destroyed.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The units.</returns>
    public long Destroyed(int row) => _counts[(row * Stride) + DestroyedIndex];

    /// <summary>The units of <paramref name="row"/> that entered Inventory custody (spawn placement or <c>AppendBacklog</c>).</summary>
    /// <param name="row">The row.</param>
    /// <returns>The units.</returns>
    public long Spawned(int row) => _counts[(row * Stride) + SpawnedIndex];

    /// <summary>The units of <paramref name="row"/> granted (only the <see cref="Recount"/> seam sets it at M-1a).</summary>
    /// <param name="row">The row.</param>
    /// <returns>The units.</returns>
    public long Granted(int row) => _counts[(row * Stride) + GrantedIndex];

    /// <summary>What Σ live must equal: <c>spawned + granted − consumed − destroyed</c>.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The expected live units.</returns>
    public long Expected(int row) => Spawned(row) + Granted(row) - Consumed(row) - Destroyed(row);

    /// <summary>The holder state of the detached entry in <paramref name="entrySlot"/>, or <see cref="NoHolder"/>.</summary>
    /// <param name="entrySlot">An entry-pool slot.</param>
    /// <returns>The <see cref="LedgerState"/> value, or <see cref="NoHolder"/>.</returns>
    public byte HolderOf(ushort entrySlot) => entrySlot < _holders.Length ? _holders[entrySlot] : NoHolder;

    /// <summary>Adds <paramref name="units"/> (may be negative) to <paramref name="row"/>'s live state. <c>InventoryMutator</c> only.</summary>
    /// <param name="row">The row (an out-of-range row changes nothing).</param>
    /// <param name="state">The live state.</param>
    /// <param name="units">The signed units.</param>
    internal void AddLive(int row, LedgerState state, int units)
    {
        if (row >= 0 && row < Rows)
        {
            _counts[(row * Stride) + (int)state] += units;
        }
    }

    /// <summary>Adds <paramref name="units"/> to <paramref name="row"/>'s Consumed for <paramref name="reason"/>. <c>InventoryMutator</c> only.</summary>
    /// <param name="row">The row (an out-of-range row changes nothing).</param>
    /// <param name="reason">The reason (an undefined one changes nothing).</param>
    /// <param name="units">The units.</param>
    internal void AddConsumed(int row, ConsumeReason reason, int units)
    {
        if (row >= 0 && row < Rows && (int)reason < ConsumeReasonCount)
        {
            _counts[(row * Stride) + ConsumedBase + (int)reason] += units;
        }
    }

    /// <summary>Adds <paramref name="units"/> to <paramref name="row"/>'s Destroyed. <c>InventoryMutator</c> only.</summary>
    /// <param name="row">The row (an out-of-range row changes nothing).</param>
    /// <param name="units">The units.</param>
    internal void AddDestroyed(int row, int units)
    {
        if (row >= 0 && row < Rows)
        {
            _counts[(row * Stride) + DestroyedIndex] += units;
        }
    }

    /// <summary>Adds <paramref name="units"/> to <paramref name="row"/>'s spawned. <c>InventoryMutator</c> only.</summary>
    /// <param name="row">The row (an out-of-range row changes nothing).</param>
    /// <param name="units">The units.</param>
    internal void AddSpawned(int row, int units)
    {
        if (row >= 0 && row < Rows)
        {
            _counts[(row * Stride) + SpawnedIndex] += units;
        }
    }

    /// <summary>Sets the holder state of the entry in <paramref name="entrySlot"/>. <c>InventoryMutator</c> only.</summary>
    /// <param name="entrySlot">An entry-pool slot (out of range changes nothing).</param>
    /// <param name="holder">A <see cref="LedgerState"/> value, or <see cref="NoHolder"/>.</param>
    internal void SetHolder(ushort entrySlot, byte holder)
    {
        if (entrySlot < _holders.Length)
        {
            _holders[entrySlot] = holder;
        }
    }

    /// <summary>
    /// TEST-ONLY seam (story 017 AC "Recount seam"; Approved by Evan 2026-10-22 2:24 PM PT, confirmed in scope 3:00 PM PT, TD-160 item 3; advisory fix, Mail Review PR #36 A11): recounts every row's
    /// live states and every slot's holder from storage, the way <c>RebuildIndex</c> rebuilds the derived memos after a test arranged
    /// entries outside the mutator. A live entry in a container counts as Cursor (a player's cursor) or Placed; a detached one as
    /// World (held by a WorldItem), Backlog (queued) or else Flight; Belt is 0 (no entry holds a belt unit). Consumed, Destroyed and
    /// spawned are kept; <c>granted</c> is set so <c>Σ live = spawned + granted − consumed − destroyed</c> holds per row. Production never
    /// calls it. O(entry pool + rows); allocation-free.
    /// </summary>
    /// <param name="inventory">The module whose storage is recounted.</param>
    internal void Recount(InventorySystem inventory)
    {
        for (int r = 0; r < Rows; r++)
        {
            Array.Clear(_counts, r * Stride, LiveStateCount);
        }

        Array.Fill(_holders, NoHolder);
        EntryPool entries = inventory.Entries;
        for (int s = 0; s < entries.Capacity && s < _holders.Length; s++)
        {
            ushort slot = (ushort)s;
            ref readonly EntryRecord e = ref entries[slot];
            int row = RowOf(in e);
            if (!e.IsLive || row >= Rows)
            {
                continue;
            }

            LedgerState state;
            if (e.ContainerSlot != EntryRecord.NoContainer)
            {
                state = inventory.Mutator.IsCursorSlot(e.ContainerSlot) ? LedgerState.Cursor : LedgerState.Placed;
            }
            else
            {
                state = inventory.WorldItems.OwnsEntry(slot) ? LedgerState.World
                    : inventory.Backlog.Contains(slot) ? LedgerState.Backlog
                    : LedgerState.Flight;
                _holders[slot] = (byte)state;
            }

            _counts[(row * Stride) + (int)state] += e.Count;
        }

        for (int r = 0; r < Rows; r++)
        {
            int b = r * Stride;
            _counts[b + GrantedIndex] = 0;
            _counts[b + GrantedIndex] = LiveSum(r) - Expected(r);
        }
    }

    /// <summary>
    /// TEST-ONLY seam (story 017 AC "Tick-end check"; TD-160 item 5): adds <paramref name="delta"/> to <paramref name="row"/>'s
    /// <c>spawned</c>, which only the Σ check reads, so the tick-end check reports it and the <c>INVENTORY_ASSERT</c> scan (which
    /// recounts live states) does not trip. Production never calls it.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="delta">The signed corruption.</param>
    internal void CorruptSpawnedForTests(int row, long delta) => _counts[(row * Stride) + SpawnedIndex] += delta;
}
