using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Inventory.Storage;

/// <summary>
/// The mail-ids arena (GDD <c>grid-inventory.md</c> R6; ADR-0009 section 5; TR-inventory-003, TR-inventory-005; story
/// grid-inventory/003). Every entry's ascending <c>mailIds[]</c> run lives in one block of one of nine size classes of 1, 2, 4 … 256
/// <c>uint</c>s; each class is one preallocated slab plus a LIFO free-block stack, both sized in the constructor from the unit budget
/// (<c>inventory.mailIdArenaUnits</c>): <c>blocks = clamp(budget / 9 / classSize, 4, 4096)</c>. A <see cref="MailIdsHandle"/> names
/// a run as (class, block, start, count).
/// <para>
/// Rules: take-k-oldest copies the k lowest ids out and advances <c>start</c> (O(1), no move); a union is a two-pointer sorted merge
/// into exactly one new block of the result's size class, freeing both inputs; a block whose live count is at or below a quarter of
/// its class is re-homed (copied to the smallest fitting class) on its next take, which is hysteresis: a re-homed run is always above
/// half of its new class, so a +-1 oscillation around the threshold re-homes at most once per crossing. An empty class borrows from
/// the next larger class; when every larger class is empty the request fails with nothing mutated and one
/// <see cref="CheckCode.InventoryArenaExhausted"/> record (the validator maps it to <c>capacity_exhausted</c>, story 004). The arena
/// stores raw <c>uint</c> ids (<c>MailId</c> wraps at the system boundary) and exposes only span reads. Allocates nothing after
/// construction. Not thread-safe: Sim tick thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var arena = new MailIdArena(settings.MailIdArenaUnits, checkRing);
/// if (arena.TryCreate(ids, tick, out MailIdsHandle h))
/// {
///     arena.Take(ref h, 2, taken, tick);        // the two lowest ids; h now starts two units later
///     ReadOnlySpan&lt;uint&gt; rest = arena.Read(in h);
/// }
/// </code>
/// </example>
public sealed class MailIdArena
{
    /// <summary>The number of size classes (1, 2, 4 … 256 units).</summary>
    public const int ClassCount = 9;

    /// <summary>The largest run (the size of the last class).</summary>
    public const int MaxUnits = 1 << (ClassCount - 1);

    /// <summary>The fewest blocks a class is given, whatever the budget.</summary>
    public const int MinBlocksPerClass = 4;

    /// <summary>The most blocks a class is given (the handle's block field is 12 bits).</summary>
    public const int MaxBlocksPerClass = 4096;

    /// <summary>The smallest accepted unit budget (<c>inventory.mailIdArenaUnits</c> hard minimum).</summary>
    public const int MinTotalUnits = 1024;

    private readonly SimCheckRing _ring;
    private readonly uint[][] _slabs = new uint[ClassCount][];
    private readonly ushort[][] _freeStacks = new ushort[ClassCount][];
    private readonly byte[][] _live = new byte[ClassCount][];
    private readonly int[] _freeCount = new int[ClassCount];
    private readonly int[] _blocks = new int[ClassCount];
    private readonly int[] _peakLive = new int[ClassCount];
    private readonly int _totalUnits;
    private int _liveBlocks;
    private int _liveUnits;

    /// <summary>Preallocates every class's slab and free stack from <paramref name="totalUnits"/>.</summary>
    /// <param name="totalUnits">The unit budget (<c>inventory.mailIdArenaUnits</c>), at least <see cref="MinTotalUnits"/>.</param>
    /// <param name="ring">The check ring exhaustion is recorded in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ring"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="totalUnits"/> is below <see cref="MinTotalUnits"/>.</exception>
    public MailIdArena(int totalUnits, SimCheckRing ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentOutOfRangeException.ThrowIfLessThan(totalUnits, MinTotalUnits);
        _ring = ring;
        _totalUnits = totalUnits;
        int unitsPerClass = totalUnits / ClassCount;
        for (int c = 0; c < ClassCount; c++)
        {
            int size = 1 << c;
            int blocks = Math.Clamp(unitsPerClass / size, MinBlocksPerClass, MaxBlocksPerClass);
            _blocks[c] = blocks;
            _slabs[c] = new uint[blocks * size];
            _live[c] = new byte[blocks];
            ushort[] stack = new ushort[blocks];
            for (int i = 0; i < blocks; i++)
            {
                stack[i] = (ushort)(blocks - 1 - i); // block 0 is on top
            }

            _freeStacks[c] = stack;
            _freeCount[c] = blocks;
        }
    }

    /// <summary>The unit budget the arena was built from.</summary>
    public int TotalUnits => _totalUnits;

    /// <summary>The number of blocks currently held by live handles.</summary>
    public int LiveBlocks => _liveBlocks;

    /// <summary>The sum of the live handles' counts.</summary>
    public int LiveUnits => _liveUnits;

    /// <summary>The run length of a class.</summary>
    /// <param name="classIndex">0 to 8.</param>
    public static int ClassSize(int classIndex) => 1 << classIndex;

    /// <summary>The number of blocks a class was given.</summary>
    /// <param name="classIndex">0 to 8.</param>
    public int BlocksPerClass(int classIndex) => _blocks[classIndex];

    /// <summary>The number of free blocks in a class right now.</summary>
    /// <param name="classIndex">0 to 8.</param>
    public int FreeBlocks(int classIndex) => _freeCount[classIndex];

    /// <summary>The most blocks a class has had live at once (story 018's below-50 % assertion).</summary>
    /// <param name="classIndex">0 to 8.</param>
    public int PeakLiveBlocks(int classIndex) => _peakLive[classIndex];

    /// <summary>The smallest class whose size holds <paramref name="units"/> (1 to <see cref="MaxUnits"/>).</summary>
    /// <param name="units">The run length.</param>
    public static int ClassFor(int units)
    {
        int c = 0;
        while ((1 << c) < units)
        {
            c++;
        }

        return c;
    }

    /// <summary>
    /// Dry run of a block reservation (the validator's <c>capacity_exhausted</c> check): true when the fitting class or any larger
    /// class has a free block. Nothing is reserved. At most 0 units is always reservable; more than <see cref="MaxUnits"/> never.
    /// </summary>
    /// <param name="units">The run length to be created.</param>
    public bool CanReserve(int units)
    {
        if (units <= 0)
        {
            return true;
        }

        if (units > MaxUnits)
        {
            return false;
        }

        for (int c = ClassFor(units); c < ClassCount; c++)
        {
            if (_freeCount[c] > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Stores <paramref name="ascendingIds"/> (1 to <see cref="MaxUnits"/> strictly ascending ids; asserted under <c>INVENTORY_ASSERT</c>,
    /// Release stores them as given) in a fresh block of the smallest fitting class, borrowing from larger classes when it is empty.
    /// An empty span yields none and false without a record; more than <see cref="MaxUnits"/> ids, or exhaustion, yields false with
    /// one <see cref="CheckCode.InventoryArenaExhausted"/> record and nothing mutated.
    /// </summary>
    /// <param name="ascendingIds">The ids, strictly ascending.</param>
    /// <param name="tick">The current tick (for the check record).</param>
    /// <param name="h">The new handle, or none.</param>
    /// <returns>True when a block was taken and filled.</returns>
    public bool TryCreate(ReadOnlySpan<uint> ascendingIds, uint tick, out MailIdsHandle h)
    {
        int n = ascendingIds.Length;
        if (n == 0)
        {
            h = default;
            return false;
        }

        InventoryAssert.That(IsStrictlyAscending(ascendingIds), SimAssertCode.InventoryMailIdsNotAscending);
        if (!TryAcquire(n, ClassCount, tick, out int cls, out int block))
        {
            h = default;
            return false;
        }

        ascendingIds.CopyTo(BlockSpan(cls, block, 0, n));
        _liveUnits += n;
        h = new MailIdsHandle(cls, block, 0, n);
        return true;
    }

    /// <summary>The live run of <paramref name="h"/> (<c>[start, start + count)</c> of its block); empty for none.</summary>
    /// <param name="h">The handle.</param>
    public ReadOnlySpan<uint> Read(in MailIdsHandle h)
    {
        if (h.IsNone)
        {
            return ReadOnlySpan<uint>.Empty;
        }

        return BlockSpan(h.ClassIndex, h.Block, h.Start, h.Count);
    }

    /// <summary>
    /// Takes the <paramref name="k"/> lowest ids out of <paramref name="h"/> (TR-inventory-005): copies them to <paramref name="taken"/>
    /// and advances the run's start (O(1); the block and class are unchanged). Taking the whole run frees the block and sets
    /// <paramref name="h"/> to none. When the run was already at or below a quarter of its class before this take, the remainder is
    /// re-homed to the smallest fitting class if a block strictly smaller than the current class is free; otherwise the block is
    /// kept (a take never fails). Under <c>INVENTORY_ASSERT</c> a none handle, <paramref name="k"/> outside 1 to <c>Count</c> or a
    /// short <paramref name="taken"/> asserts; Release takes nothing for none or <c>k</c> below 1 and clamps <c>k</c> to the count.
    /// </summary>
    /// <param name="h">The handle; updated in place.</param>
    /// <param name="k">How many ids to take, 1 to <c>h.Count</c>.</param>
    /// <param name="taken">Receives the k ids, ascending.</param>
    /// <param name="tick">The current tick (unused by a take, which cannot fail; kept for the mutator's uniform call shape).</param>
    /// <returns>The number of ids taken.</returns>
    public int Take(ref MailIdsHandle h, int k, Span<uint> taken, uint tick)
    {
        _ = tick;
        InventoryAssert.That(!h.IsNone && k >= 1 && k <= h.Count && taken.Length >= k, SimAssertCode.InventoryArenaHandleInvalid);
        if (h.IsNone || k < 1)
        {
            return 0;
        }

        int count = h.Count;
        if (k > count)
        {
            k = count;
        }

        int cls = h.ClassIndex;
        int block = h.Block;
        ReadOnlySpan<uint> run = Read(in h);
        run[..k].CopyTo(taken);
        _liveUnits -= k;
        if (k == count)
        {
            Release(cls, block);
            h = default;
            return k;
        }

        int remaining = count - k;
        if (count <= (1 << cls) >> 2)
        {
            int target = ClassFor(remaining);
            if (target < cls && TryAcquireBelow(target, cls, out int newCls, out int newBlock))
            {
                run[k..].CopyTo(BlockSpan(newCls, newBlock, 0, remaining));
                Release(cls, block);
                h = new MailIdsHandle(newCls, newBlock, 0, remaining);
                return k;
            }
        }

        h = new MailIdsHandle(cls, block, h.Start + k, remaining);
        return k;
    }

    /// <summary>
    /// Merges two runs (R6: the destination list is the sorted union) into exactly one new block of the result's size class
    /// (borrowing up), then frees both inputs. The runs must be disjoint (asserted under <c>INVENTORY_ASSERT</c>; Release keeps
    /// both copies of a duplicate, adjacent). A none input contributes nothing; two none inputs, or the same handle twice, yield
    /// false without a record (asserted). A result over <see cref="MaxUnits"/> ids, or exhaustion, yields false with one
    /// <see cref="CheckCode.InventoryArenaExhausted"/> record and nothing mutated (the block is acquired before anything is freed).
    /// </summary>
    /// <param name="a">The first run.</param>
    /// <param name="b">The second run.</param>
    /// <param name="tick">The current tick (for the check record).</param>
    /// <param name="result">The union's handle, or none.</param>
    /// <returns>True when the union was stored and the inputs freed.</returns>
    public bool TryUnion(in MailIdsHandle a, in MailIdsHandle b, uint tick, out MailIdsHandle result)
    {
        InventoryAssert.That(!(a.IsNone && b.IsNone) && a != b, SimAssertCode.InventoryArenaHandleInvalid);
        if ((a.IsNone && b.IsNone) || a == b)
        {
            result = default;
            return false;
        }

        if (!TryMerge(Read(in a), Read(in b), tick, out result))
        {
            return false;
        }

        Free(in a);
        Free(in b);
        return true;
    }

    /// <summary>
    /// Merges <paramref name="ascendingIds"/> into <paramref name="h"/> under the union rules (one new block of the result class,
    /// the old block freed). A none <paramref name="h"/> is a <see cref="TryCreate"/>; an empty span is a no-op that returns true.
    /// </summary>
    /// <param name="h">The handle; replaced on success, untouched on failure.</param>
    /// <param name="ascendingIds">The ids to add, strictly ascending and disjoint from the run (asserted under <c>INVENTORY_ASSERT</c>).</param>
    /// <param name="tick">The current tick (for the check record).</param>
    /// <returns>True when the ids were merged in.</returns>
    public bool TryAppend(ref MailIdsHandle h, ReadOnlySpan<uint> ascendingIds, uint tick)
    {
        if (ascendingIds.Length == 0)
        {
            return true;
        }

        if (h.IsNone)
        {
            return TryCreate(ascendingIds, tick, out h);
        }

        InventoryAssert.That(IsStrictlyAscending(ascendingIds), SimAssertCode.InventoryMailIdsNotAscending);
        if (!TryMerge(Read(in h), ascendingIds, tick, out MailIdsHandle merged))
        {
            return false;
        }

        Free(in h);
        h = merged;
        return true;
    }

    /// <summary>Returns the handle's block to its class (none is ignored). Freeing a block that is not live asserts under <c>INVENTORY_ASSERT</c>; Release ignores it.</summary>
    /// <param name="h">The handle.</param>
    public void Free(in MailIdsHandle h)
    {
        if (h.IsNone)
        {
            return;
        }

        int cls = h.ClassIndex;
        int block = h.Block;
        InventoryAssert.That(cls < ClassCount && block < _blocks[cls] && _live[cls][block] != 0, SimAssertCode.InventoryArenaHandleInvalid);
        if (cls >= ClassCount || block >= _blocks[cls] || _live[cls][block] == 0)
        {
            return;
        }

        _liveUnits -= h.Count;
        Release(cls, block);
    }

    private static bool IsStrictlyAscending(ReadOnlySpan<uint> ids)
    {
        for (int i = 1; i < ids.Length; i++)
        {
            if (ids[i] <= ids[i - 1])
            {
                return false;
            }
        }

        return true;
    }

    private Span<uint> BlockSpan(int cls, int block, int start, int count) => _slabs[cls].AsSpan((block << cls) + start, count);

    /// <summary>Acquires and fills one block of the result class; frees nothing. Records exhaustion.</summary>
    private bool TryMerge(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b, uint tick, out MailIdsHandle result)
    {
        int total = a.Length + b.Length;
        if (total > MaxUnits)
        {
            _ring.Record(CheckCode.InventoryArenaExhausted, tick, total, ClassCount);
            result = default;
            return false;
        }

        if (!TryAcquire(total, ClassCount, tick, out int cls, out int block))
        {
            result = default;
            return false;
        }

        Span<uint> dst = BlockSpan(cls, block, 0, total);
        int i = 0;
        int j = 0;
        int o = 0;
        while (i < a.Length && j < b.Length)
        {
            uint x = a[i];
            uint y = b[j];
            InventoryAssert.That(x != y, SimAssertCode.InventoryMailIdsNotAscending);
            if (x <= y)
            {
                dst[o++] = x;
                i++;
            }
            else
            {
                dst[o++] = y;
                j++;
            }
        }

        while (i < a.Length)
        {
            dst[o++] = a[i++];
        }

        while (j < b.Length)
        {
            dst[o++] = b[j++];
        }

        _liveUnits += total;
        result = new MailIdsHandle(cls, block, 0, total);
        return true;
    }

    /// <summary>Pops a block from the fitting class or any larger class below <paramref name="classLimit"/>; records exhaustion when <paramref name="classLimit"/> is <see cref="ClassCount"/>.</summary>
    private bool TryAcquire(int units, int classLimit, uint tick, out int cls, out int block)
    {
        int fit = ClassFor(units);
        for (int c = fit; c < classLimit; c++)
        {
            if (_freeCount[c] > 0)
            {
                cls = c;
                block = Pop(c);
                return true;
            }
        }

        if (classLimit == ClassCount)
        {
            _ring.Record(CheckCode.InventoryArenaExhausted, tick, units, fit);
        }

        cls = 0;
        block = 0;
        return false;
    }

    /// <summary>A re-home acquisition: a block of a class in <c>[target, current)</c>, never recorded as exhaustion.</summary>
    private bool TryAcquireBelow(int target, int current, out int cls, out int block)
    {
        for (int c = target; c < current; c++)
        {
            if (_freeCount[c] > 0)
            {
                cls = c;
                block = Pop(c);
                return true;
            }
        }

        cls = 0;
        block = 0;
        return false;
    }

    private int Pop(int cls)
    {
        _freeCount[cls]--;
        int block = _freeStacks[cls][_freeCount[cls]];
        _live[cls][block] = 1;
        _liveBlocks++;
        int live = _blocks[cls] - _freeCount[cls];
        if (live > _peakLive[cls])
        {
            _peakLive[cls] = live;
        }

        return block;
    }

    private void Release(int cls, int block)
    {
        _live[cls][block] = 0;
        _freeStacks[cls][_freeCount[cls]] = (ushort)block;
        _freeCount[cls]++;
        _liveBlocks--;
    }
}
