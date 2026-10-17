using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// The server-only mail table (ADR-0017 section 2, GDD mail-destinations R2, TR-mail-006): a paged struct-of-arrays store
/// addressed directly by <see cref="MailId"/>, with no hash map and no location column. The row of mail id <c>m</c> is page
/// <c>(m - 1) &gt;&gt; 10</c>, slot <c>(m - 1) &amp; 1023</c>. Ids come from the injected <see cref="MailIds"/> in manifest order
/// (lower = older), so only the newest page is ever partially issued.
/// <para>
/// Pages come from a pool preallocated at construction (<see cref="SimSettings.MailTablePages"/>, default 64). A page whose
/// slots have all been issued (or that a later page superseded) and whose live count reaches 0 is cleared and returned to
/// the pool. When the pool is empty it doubles its total page count, records <see cref="CheckCode.MailTableGrow"/> and goes
/// on; that, and the rare doubling of the page map when ids run past it, are the only allocating paths. <see cref="Create"/>,
/// <see cref="Retire"/> and <see cref="TryGetRow"/> otherwise allocate 0 B.
/// </para>
/// <para>
/// <see cref="Create"/> allocates a <see cref="MailId"/>, so it is legal only inside <c>Tick()</c> on the owner thread
/// (ADR-0004); an illegal call is refused by <see cref="MailIds"/> and returns <see cref="MailId.None"/> with no row written.
/// Not thread-safe. Dumped through <see cref="MailStateContributor"/>.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var table = new MailTable(settings, mailIds, tickReader, ring);
/// // inside a phase:
/// MailId id = table.Create(kind, AddressId.Pack(1, 1, 7), 250, spawnShift: 2, MailFlags.None);
/// bool live = table.TryGetRow(id, out MailRow row);   // row.DeadlineShift == 2
/// table.Retire(id);
/// </code>
/// </example>
internal sealed class MailTable
{
    private const int MinMapLength = 256;
    private const int MapPagesPerPoolPage = 16;

    /// <summary><see cref="CheckCode.MailTableGrow"/> payload <c>a</c> when the page pool grew (<c>b</c> = new total page count).</summary>
    public const long GrowKindPool = 1;

    /// <summary><see cref="CheckCode.MailTableGrow"/> payload <c>a</c> when the page map doubled (<c>b</c> = new map length).</summary>
    public const long GrowKindMap = 2;

    private readonly MailIds _ids;
    private readonly ITickReader _tick;
    private readonly SimCheckRing _ring;
    private MailPage?[] _map;
    private MailPage?[] _free;
    private int _freeCount;
    private int _totalPages;
    private int _mapHigh;       // one past the highest page index ever mapped
    private int _openPage = -1; // page index currently receiving rows, -1 = none
    private int _liveCount;

    /// <summary>Creates the table and preallocates <see cref="SimSettings.MailTablePages"/> pages.</summary>
    /// <param name="settings">The world settings; <see cref="SimSettings.MailTablePages"/> sizes the pool.</param>
    /// <param name="ids">The world's mail id allocator.</param>
    /// <param name="tick">The tick reader, used for the tick stamped on grow checks.</param>
    /// <param name="ring">The check ring that receives <see cref="CheckCode.MailTableGrow"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><c>MailTablePages</c> is below 1 (construction only).</exception>
    public MailTable(SimSettings settings, MailIds ids, ITickReader tick, SimCheckRing ring)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MailTablePages, 1);
        _ids = ids;
        _tick = tick;
        _ring = ring;
        int pages = settings.MailTablePages;
        _free = new MailPage?[pages];
        for (int i = 0; i < pages; i++)
        {
            _free[i] = new MailPage();
        }

        _freeCount = pages;
        _totalPages = pages;
        _map = new MailPage?[Math.Max(MinMapLength, checked(pages * MapPagesPerPoolPage))];
    }

    /// <summary>Rows currently live.</summary>
    public int LiveCount => _liveCount;

    /// <summary>Total pages owned by the table (pool plus mapped), including growth.</summary>
    public int TotalPages => _totalPages;

    /// <summary>Pages currently in the free pool.</summary>
    public int FreePages => _freeCount;

    /// <summary>Live rows on page <paramref name="pageIndex"/> (0 when the page is unmapped or out of range).</summary>
    /// <param name="pageIndex">The page number, <c>(mailId - 1) &gt;&gt; 10</c>.</param>
    /// <returns>The page's live count.</returns>
    public int LiveCountOfPage(int pageIndex) =>
        (uint)pageIndex < (uint)_map.Length && _map[pageIndex] is { } page ? page.LiveCount : 0;

    /// <summary>True when every page in the free pool is clean (no stale columns, bitmap or counters). A diagnostic walk; not for the tick path.</summary>
    /// <returns>True when no pooled page carries stale state.</returns>
    public bool FreePagesAreClear()
    {
        for (int i = 0; i < _freeCount; i++)
        {
            if (_free[i] is not { } page || !page.IsClear())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Times the page pool has grown because it was empty (each recorded a <see cref="CheckCode.MailTableGrow"/>).</summary>
    public int PoolGrowCount { get; private set; }

    /// <summary>Times the page map has doubled because ids ran past it (each recorded a <see cref="CheckCode.MailTableGrow"/>).</summary>
    public int MapGrowCount { get; private set; }

    /// <summary>
    /// Allocates the next <see cref="MailId"/> and writes its row with <c>deadlineShift = spawnShift</c> (every v1
    /// <c>deadlineOffsetShifts</c> is 0, GDD R2). Legal only inside <c>Tick()</c> on the owner thread.
    /// <para>
    /// Ordering note: the id is allocated first, so it is consumed (the counter never goes back) even if page acquisition then
    /// throws, for example a checked overflow in the pool or map growth; ids are never reused, which leaves a gap, not a clash.
    /// </para>
    /// </summary>
    /// <param name="kind">The mail kind def.</param>
    /// <param name="addr">The packed destination address.</param>
    /// <param name="spawnValue">The value in cents, fixed for the row's life.</param>
    /// <param name="spawnShift">The shift the row is created in.</param>
    /// <param name="flags">The row flags.</param>
    /// <returns>The new id, or <see cref="MailId.None"/> when the allocator refused (no row written).</returns>
    public MailId Create(DefIndex kind, AddressId addr, ushort spawnValue, byte spawnShift, MailFlags flags)
    {
        MailId id = _ids.Allocate();
        if (id.IsNone)
        {
            return MailId.None;
        }

        uint zero = id.Value - 1u;
        int pageIndex = (int)(zero >> MailPage.Shift);
        int slot = (int)(zero & MailPage.SlotMask);
        MailPage page = PageForCreate(pageIndex);

        page.Add(slot, kind, addr, spawnValue, spawnShift, flags);
        _liveCount++;

        if (slot == MailPage.Size - 1)
        {
            Close(pageIndex, page);
        }

        return id;
    }

    /// <summary>Reads a live row. A retired, never-created or <see cref="MailId.None"/> id reports false.</summary>
    /// <param name="id">The mail id.</param>
    /// <param name="row">The row copy when live, else default.</param>
    /// <returns>True when the row is live.</returns>
    public bool TryGetRow(MailId id, out MailRow row)
    {
        if (TryLocate(id, out MailPage? page, out int slot) && page.IsLive(slot))
        {
            row = page.Read(id, slot);
            return true;
        }

        row = default;
        return false;
    }

    /// <summary>True when <paramref name="id"/> names a live row.</summary>
    /// <param name="id">The mail id.</param>
    /// <returns>True when live.</returns>
    public bool IsLive(MailId id) => TryLocate(id, out MailPage? page, out int slot) && page.IsLive(slot);

    /// <summary>
    /// Retires a row: clears its live bit and decrements the page's live count. A page that is fully issued and now empty
    /// goes back to the pool. Retiring a retired or unknown id changes nothing. Rows retire only through this call.
    /// </summary>
    /// <param name="id">The mail id.</param>
    /// <returns>True when a live row was retired.</returns>
    public bool Retire(MailId id)
    {
        if (!TryLocate(id, out MailPage? page, out int slot) || !page.Remove(slot))
        {
            return false;
        }

        _liveCount--;
        if (page.Closed && page.LiveCount == 0)
        {
            Release((int)((id.Value - 1u) >> MailPage.Shift), page);
        }

        return true;
    }

    /// <summary>
    /// Writes the live rows in ascending <c>mailId</c> (ADR-0017 section 2, ADR-0006 section 4), field by field:
    /// <c>u32 liveCount</c>, then per row <c>mailId u32, kind u16, addressId u32, spawnValue u16, spawnShift u8,
    /// deadlineShift u8, flags u8</c>. Between ticks only; allocation-free.
    /// </summary>
    /// <param name="w">The writer, inside the Mail section.</param>
    public void WriteCanonicalRows(ref CanonicalWriter w)
    {
        w.WriteU32((uint)_liveCount);
        for (int p = 0; p < _mapHigh; p++)
        {
            MailPage? page = _map[p];
            if (page is null || page.LiveCount == 0)
            {
                continue;
            }

            uint baseId = ((uint)p << MailPage.Shift) + 1u;
            for (int word = 0; word < MailPage.LiveWords; word++)
            {
                ulong bits = page.LiveWord(word);
                while (bits != 0UL)
                {
                    int slot = (word << 6) + BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1UL;
                    w.WriteU32(baseId + (uint)slot);
                    page.WriteFields(ref w, slot);
                }
            }
        }
    }

    /// <summary>
    /// Binds the table's dump section (<see cref="SectionIds.Mail"/>). A minimal hook until the Mail system (story 005) owns
    /// the composition wiring.
    /// </summary>
    /// <param name="builder">The world builder, before <c>Freeze</c>.</param>
    /// <param name="registry">The destination registry dumped after the rows (story 002).</param>
    public void BindDump(SimWorldBuilder builder, DestinationRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registry);
        builder.BindStateContributor(new MailStateContributor(this, registry));
    }

    private bool TryLocate(MailId id, [NotNullWhen(true)] out MailPage? page, out int slot)
    {
        page = null;
        slot = 0;
        if (id.IsNone)
        {
            return false;
        }

        uint zero = id.Value - 1u;
        uint pageIndex = zero >> MailPage.Shift;
        if (pageIndex >= (uint)_map.Length)
        {
            return false;
        }

        slot = (int)(zero & MailPage.SlotMask);
        page = _map[pageIndex];
        return page is not null;
    }

    private MailPage PageForCreate(int pageIndex)
    {
        if (pageIndex < _map.Length && _map[pageIndex] is { } existing)
        {
            return existing;
        }

        // A later page opens: the previous newest page can take no more rows (ids ascend), so it may be recycled when empty.
        if (_openPage >= 0 && _openPage < _map.Length && _map[_openPage] is { } previous)
        {
            Close(_openPage, previous);
        }

        MailPage page = TakeFromPool();
        EnsureMapLength(pageIndex);
        _map[pageIndex] = page;
        _openPage = pageIndex;
        if (pageIndex >= _mapHigh)
        {
            _mapHigh = pageIndex + 1;
        }

        return page;
    }

    private void Close(int pageIndex, MailPage page)
    {
        page.Close();
        if (page.LiveCount == 0)
        {
            Release(pageIndex, page);
        }
    }

    private void Release(int pageIndex, MailPage page)
    {
        page.Clear();
        _map[pageIndex] = null;
        _free[_freeCount++] = page;
        if (_openPage == pageIndex)
        {
            _openPage = -1;
        }
    }

    private MailPage TakeFromPool()
    {
        if (_freeCount == 0)
        {
            GrowPool();
        }

        MailPage? page = _free[--_freeCount];
        _free[_freeCount] = null;
        return page ?? throw new InvalidOperationException("The mail page pool held a null page.");
    }

    // The single allocating path: doubles the total page count. Never in a steady-state tick (default 64 pages).
    private void GrowPool()
    {
        int newTotal = checked(_totalPages * 2);
        var free = new MailPage?[newTotal];
        Array.Copy(_free, free, _freeCount);
        for (int i = _totalPages; i < newTotal; i++)
        {
            free[_freeCount++] = new MailPage();
        }

        _free = free;
        _totalPages = newTotal;
        PoolGrowCount++;
        _ring.Record(CheckCode.MailTableGrow, _tick.CurrentTick, GrowKindPool, _totalPages);
    }

    private void EnsureMapLength(int pageIndex)
    {
        if (pageIndex < _map.Length)
        {
            return;
        }

        int length = _map.Length;
        while (length <= pageIndex)
        {
            length = checked(length * 2);
        }

        var map = new MailPage?[length];
        Array.Copy(_map, map, _map.Length);
        _map = map;
        MapGrowCount++;
        _ring.Record(CheckCode.MailTableGrow, _tick.CurrentTick, GrowKindMap, _map.Length);
    }
}
