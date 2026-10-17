using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.MailDestinations;

/// <summary>
/// One page of the mail table (ADR-0017 section 2): preallocated 1,024-element columns plus a <c>live</c> bitmap
/// (<c>ulong[16]</c>). Every array is allocated once, in the constructor; a page is recycled through the table's pool and
/// <see cref="Clear"/>ed, never reallocated. The columns are private: rows enter through <see cref="Add"/> and leave through
/// <see cref="Remove"/>, which keep the live bitmap and <see cref="LiveCount"/> in step. There is deliberately no location column.
/// </summary>
internal sealed class MailPage
{
    /// <summary>Rows per page.</summary>
    public const int Size = 1024;

    /// <summary><c>log2(Size)</c>: <c>page = (mailId - 1) &gt;&gt; Shift</c>.</summary>
    public const int Shift = 10;

    /// <summary><c>slot = (mailId - 1) &amp; SlotMask</c>.</summary>
    public const uint SlotMask = Size - 1;

    /// <summary>Words in the live bitmap (<c>Size / 64</c>).</summary>
    public const int LiveWords = Size / 64;

    private readonly DefIndex[] _kind = new DefIndex[Size];
    private readonly AddressId[] _address = new AddressId[Size];
    private readonly ushort[] _spawnValue = new ushort[Size];
    private readonly byte[] _spawnShift = new byte[Size];
    private readonly byte[] _deadlineShift = new byte[Size];
    private readonly byte[] _flags = new byte[Size];
    private readonly ulong[] _live = new ulong[LiveWords];

    /// <summary>Rows currently live on this page.</summary>
    public int LiveCount { get; private set; }

    /// <summary>True once the page will receive no more rows (its last slot was issued, or a later page was opened).</summary>
    public bool Closed { get; private set; }

    /// <summary>True when <paramref name="slot"/> holds a live row.</summary>
    /// <param name="slot">Slot, 0 to 1023.</param>
    /// <returns>True when the slot's live bit is set.</returns>
    public bool IsLive(int slot) => (_live[slot >> 6] & (1UL << (slot & 63))) != 0UL;

    /// <summary>The live bitmap word <paramref name="word"/> (bit <c>n</c> = slot <c>word * 64 + n</c>), for ascending iteration.</summary>
    /// <param name="word">Word index, 0 to <see cref="LiveWords"/> - 1.</param>
    /// <returns>The word.</returns>
    public ulong LiveWord(int word) => _live[word];

    /// <summary>Writes a row into <paramref name="slot"/>, sets its live bit and increments <see cref="LiveCount"/>. The slot must not be live.</summary>
    /// <param name="slot">Slot, 0 to 1023.</param>
    /// <param name="kind">The mail kind def.</param>
    /// <param name="address">The packed destination address.</param>
    /// <param name="spawnValue">The value in cents.</param>
    /// <param name="spawnShift">The spawn shift; also stored as the deadline shift.</param>
    /// <param name="flags">The row flags.</param>
    public void Add(int slot, DefIndex kind, AddressId address, ushort spawnValue, byte spawnShift, MailFlags flags)
    {
        _kind[slot] = kind;
        _address[slot] = address;
        _spawnValue[slot] = spawnValue;
        _spawnShift[slot] = spawnShift;
        _deadlineShift[slot] = spawnShift;
        _flags[slot] = (byte)flags;
        _live[slot >> 6] |= 1UL << (slot & 63);
        LiveCount++;
    }

    /// <summary>Clears <paramref name="slot"/>'s live bit and decrements <see cref="LiveCount"/>. A slot that is not live is left alone.</summary>
    /// <param name="slot">Slot, 0 to 1023.</param>
    /// <returns>True when a live row was removed.</returns>
    public bool Remove(int slot)
    {
        if (!IsLive(slot))
        {
            return false;
        }

        _live[slot >> 6] &= ~(1UL << (slot & 63));
        LiveCount--;
        return true;
    }

    /// <summary>Marks the page as receiving no more rows.</summary>
    public void Close() => Closed = true;

    /// <summary>Copies the row in <paramref name="slot"/> (live or not; the caller checks <see cref="IsLive"/>).</summary>
    /// <param name="id">The mail id of the slot.</param>
    /// <param name="slot">Slot, 0 to 1023.</param>
    /// <returns>The row copy.</returns>
    public MailRow Read(MailId id, int slot) =>
        new(id, _kind[slot], _address[slot], _spawnValue[slot], _spawnShift[slot], _deadlineShift[slot], (MailFlags)_flags[slot]);

    /// <summary>Writes the slot's fields in dump order (after the caller wrote the id): kind u16, addressId u32, spawnValue u16, spawnShift u8, deadlineShift u8, flags u8.</summary>
    /// <param name="w">The writer, inside the Mail section.</param>
    /// <param name="slot">Slot, 0 to 1023.</param>
    public void WriteFields(ref CanonicalWriter w, int slot)
    {
        w.WriteU16(_kind[slot].Value);
        w.WriteU32(_address[slot].Value);
        w.WriteU16(_spawnValue[slot]);
        w.WriteU8(_spawnShift[slot]);
        w.WriteU8(_deadlineShift[slot]);
        w.WriteU8(_flags[slot]);
    }

    /// <summary>Resets every column, the bitmap and the counters so the page can be reused. Allocation-free.</summary>
    public void Clear()
    {
        Array.Clear(_kind);
        Array.Clear(_address);
        Array.Clear(_spawnValue);
        Array.Clear(_spawnShift);
        Array.Clear(_deadlineShift);
        Array.Clear(_flags);
        Array.Clear(_live);
        LiveCount = 0;
        Closed = false;
    }

    /// <summary>True when every column and the bitmap hold their default (zero) value and the counters are reset: a clean pooled page.</summary>
    /// <returns>True when the page carries no stale state.</returns>
    public bool IsClear()
    {
        if (LiveCount != 0 || Closed)
        {
            return false;
        }

        for (int i = 0; i < Size; i++)
        {
            if (!_kind[i].IsNone || !_address[i].IsNone || _spawnValue[i] != 0 || _spawnShift[i] != 0 || _deadlineShift[i] != 0 || _flags[i] != 0)
            {
                return false;
            }
        }

        for (int i = 0; i < LiveWords; i++)
        {
            if (_live[i] != 0UL)
            {
                return false;
            }
        }

        return true;
    }
}
