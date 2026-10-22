using System.Diagnostics;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory.Storage;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Ledger;

/// <summary>
/// Grid Inventory's tick-end conservation check (story grid-inventory/017; ADR-0009 section 6; TR-inventory-012; GDD R19, H-R19-2),
/// run after phase 12 in every build. Per ledger row (mail, then each item def) it checks
/// <c>Σ live = spawned + granted − consumed − destroyed</c> and that no live counter is negative; each finding writes one
/// <see cref="CheckCode.InventoryConservationMismatch"/> record (a = the row, with the state ordinal + 1 in bits 24-31 for a negative
/// counter; b = the Σ delta or the negative value) and adds one <see cref="CounterId.InventoryConservationMismatches"/> (the ring's
/// per-code rate limit applies to the record, not the counter). It never throws, mutates nothing and raises nothing (ADR-0003
/// section 2). The Release check is O(item defs), fixed at world build, and allocation-free.
/// <para>
/// Under <c>INVENTORY_ASSERT</c> it then runs <see cref="Scan"/> (<see cref="ConditionalAttribute"/>, so the call is compiled out of
/// Release): a recount of every entry-held state from storage against the ledger, each detached entry's holder state against where it
/// is held (an entry detached and held by nothing, in no holder state, is its caller's and is not counted: units missing at tick end
/// are the Σ check's finding), and duplicate mailIds (every live id sorted into a preallocated scratch, adjacent equality). A finding is named in
/// <see cref="ScanFinding"/> and asserts <see cref="SimAssertCode.InventoryConservationScan"/>. Belt is not recountable (no entry
/// holds a belt unit), so the scan does not compare it; a negative Belt is the Release check's finding (TD-145, narrowed). Mail Lead
/// planning defaults pending Evan, TD-160 items 5-7.
/// </para>
/// </summary>
/// <example>
/// <code>
/// builder.BindTickEnd(inventory.ConservationCheck); // InventorySystem.Bind
/// </code>
/// </example>
internal sealed class InventoryConservationCheck : ITickEndCheck
{
    private const int StateShift = 24;

    private readonly UnitLedger _ledger;
    private readonly InventorySystem _inventory;
    private readonly SimCheckRing _ring;
    private readonly SimCounters _counters;
    private readonly long[] _scanLive;
    private readonly uint[] _scanIds;
    private readonly uint[] _scanScratch;

    /// <summary>Creates the check; the scan's scratch is allocated here, only in an <c>INVENTORY_ASSERT</c> build.</summary>
    /// <param name="ledger">The module's ledger.</param>
    /// <param name="inventory">The module whose storage the scan recounts.</param>
    /// <param name="ring">The world's check ring.</param>
    /// <param name="counters">The world's counters.</param>
    public InventoryConservationCheck(UnitLedger ledger, InventorySystem inventory, SimCheckRing ring, SimCounters counters)
    {
        _ledger = ledger;
        _inventory = inventory;
        _ring = ring;
        _counters = counters;
#if INVENTORY_ASSERT
        _scanLive = new long[ledger.Rows * UnitLedger.LiveStateCount];
        _scanIds = new uint[inventory.MailIds.TotalUnits];
        _scanScratch = new uint[inventory.MailIds.TotalUnits];
#else
        _scanLive = Array.Empty<long>();
        _scanIds = Array.Empty<uint>();
        _scanScratch = Array.Empty<uint>();
#endif
    }

    /// <summary>
    /// The last <see cref="Scan"/> finding (<c>INVENTORY_ASSERT</c> builds; <see cref="ScanFindingKind.None"/> until one): its kind
    /// and, per kind, the row and state of a recount mismatch, the entry slot of a holder mismatch, or the duplicated mailId.
    /// </summary>
    public ScanFindingRecord ScanFinding { get; private set; }

    /// <inheritdoc />
    public void Check(uint tick)
    {
        int rows = _ledger.Rows;
        for (int row = 0; row < rows; row++)
        {
            long delta = _ledger.LiveSum(row) - _ledger.Expected(row);
            if (delta != 0)
            {
                Report(tick, row, delta);
            }

            for (int s = 0; s < UnitLedger.LiveStateCount; s++)
            {
                long v = _ledger.Live(row, (LedgerState)s);
                if (v < 0)
                {
                    Report(tick, (long)((uint)row | ((uint)(s + 1) << StateShift)), v);
                }
            }
        }

        Scan();
    }

    private void Report(uint tick, long a, long b)
    {
        _ring.Record(CheckCode.InventoryConservationMismatch, tick, a, b);
        _counters.Add(CounterId.InventoryConservationMismatches);
    }

    /// <summary>
    /// The <c>INVENTORY_ASSERT</c> full scan (see the type remarks). O(entry pool + live mailIds · log); allocation-free (the scratch is
    /// preallocated). Reads storage only.
    /// </summary>
    /// <exception cref="SimAssertException"><see cref="SimAssertCode.InventoryConservationScan"/> on a finding.</exception>
    [Conditional("INVENTORY_ASSERT")]
    private void Scan()
    {
        Array.Clear(_scanLive);
        EntryPool entries = _inventory.Entries;
        MailIdArena arena = _inventory.MailIds;
        int ids = 0;
        for (int s = 0; s < entries.Capacity; s++)
        {
            ushort slot = (ushort)s;
            ref readonly EntryRecord e = ref entries[slot];
            int row = UnitLedger.RowOf(in e);
            if (!e.IsLive || row >= _ledger.Rows)
            {
                continue;
            }

            if (e.IsMail)
            {
                ReadOnlySpan<uint> run = arena.Read(in e.MailIds);
                if (ids + run.Length > _scanIds.Length)
                {
                    Fail(ScanFindingKind.DuplicateMailId, 0, 0); // more live ids than the arena holds: some id is held twice
                    return;
                }

                run.CopyTo(_scanIds.AsSpan(ids));
                ids += run.Length;
            }

            LedgerState state;
            if (e.ContainerSlot != EntryRecord.NoContainer)
            {
                state = _inventory.Mutator.IsCursorSlot(e.ContainerSlot) ? LedgerState.Cursor : LedgerState.Placed;
            }
            else
            {
                state = _inventory.WorldItems.OwnsEntry(slot) ? LedgerState.World
                    : _inventory.Backlog.Contains(slot) ? LedgerState.Backlog
                    : LedgerState.Flight;
                byte holder = _ledger.HolderOf(slot);
                if (holder == UnitLedger.NoHolder && state == LedgerState.Flight)
                {
                    continue; // in no state: detached and held by no holder (its caller's; the Σ check reports units missing at tick end)
                }

                if (holder != (byte)state)
                {
                    Fail(ScanFindingKind.Holder, slot, (int)state);
                    return;
                }
            }

            _scanLive[(row * UnitLedger.LiveStateCount) + (int)state] += e.Count;
        }

        for (int row = 0; row < _ledger.Rows; row++)
        {
            for (int st = 0; st < UnitLedger.LiveStateCount; st++)
            {
                if (st != (int)LedgerState.Belt && _scanLive[(row * UnitLedger.LiveStateCount) + st] != _ledger.Live(row, (LedgerState)st))
                {
                    Fail(ScanFindingKind.Recount, row, st);
                    return;
                }
            }
        }

        StableSort.Sort(_scanIds.AsSpan(0, ids), _scanScratch.AsSpan(0, ids), default(UIntAscending));
        for (int i = 1; i < ids; i++)
        {
            if (_scanIds[i] == _scanIds[i - 1])
            {
                Fail(ScanFindingKind.DuplicateMailId, _scanIds[i], 0);
                return;
            }
        }
    }

    private void Fail(ScanFindingKind kind, long a, long b)
    {
        ScanFinding = new ScanFindingRecord(kind, a, b);
        InventoryAssert.That(false, SimAssertCode.InventoryConservationScan);
    }

    /// <summary>Ascending <see cref="uint"/> order (mailIds are unique keys, so no tie-break is needed).</summary>
    private readonly struct UIntAscending : IComparer<uint>
    {
        public int Compare(uint x, uint y) => x.CompareTo(y);
    }
}

/// <summary>What the <c>INVENTORY_ASSERT</c> conservation scan found (story 017).</summary>
internal enum ScanFindingKind : byte
{
    /// <summary>No finding.</summary>
    None = 0,

    /// <summary>A live state's recount differs from its counter: A = row, B = <see cref="LedgerState"/> ordinal.</summary>
    Recount = 1,

    /// <summary>A detached entry's holder state differs from where it is held: A = entry slot, B = the held state's ordinal.</summary>
    Holder = 2,

    /// <summary>A mailId is held twice: A = the mailId (0 when the live ids overflow the arena's units).</summary>
    DuplicateMailId = 3,
}

/// <summary>One <c>INVENTORY_ASSERT</c> scan finding (story 017): its kind and two kind-specific values.</summary>
internal readonly struct ScanFindingRecord
{
    /// <summary>Creates the finding.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="a">The first value (see <see cref="ScanFindingKind"/>).</param>
    /// <param name="b">The second value.</param>
    public ScanFindingRecord(ScanFindingKind kind, long a, long b)
    {
        Kind = kind;
        A = a;
        B = b;
    }

    /// <summary>The kind.</summary>
    public ScanFindingKind Kind { get; }

    /// <summary>The first value (see <see cref="ScanFindingKind"/>).</summary>
    public long A { get; }

    /// <summary>The second value.</summary>
    public long B { get; }
}
