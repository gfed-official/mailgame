namespace PerformativeMail.Sim.Core;

/// <summary>
/// Per-world fixed-capacity ring of always-on check records, present in every build. Owned and
/// injected by the composition root; never static. Not thread-safe (Sim tick thread only).
/// <para>
/// <see cref="Record"/> never throws, formats a string or allocates. It is rate limited per code:
/// a record is stored when the code has never been recorded, when at least
/// <see cref="RateLimitTicks"/> ticks have passed since its last stored record, or when the tick
/// went backwards (the code re-arms); otherwise the code's <see cref="Suppressed"/> count rises.
/// The tick difference is computed in <see cref="long"/>, so values near <see cref="uint.MaxValue"/>
/// are safe. When full, the oldest record is overwritten and <see cref="Overwritten"/> rises.
/// Counts saturate at <see cref="uint.MaxValue"/>.
/// </para>
/// <para>
/// Invalid codes: a code beyond the defined <see cref="CheckCode"/> range (for example
/// <c>(CheckCode)ushort.MaxValue</c>) is recorded with its raw value, but all such codes share one
/// rate-limit slot, and <see cref="Suppressed"/> for any of them reports that shared slot.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var ring = new SimCheckRing();
/// ring.Record(CheckCode.BadTick, 0, 7, 0);    // stored
/// ring.Record(CheckCode.BadTick, 10, 8, 0);   // suppressed
/// uint s = ring.Suppressed(CheckCode.BadTick); // 1
/// SimCheckRecord oldest = ring[0];             // tick 0, A == 7
/// </code>
/// </example>
public sealed class SimCheckRing
{
    private const long NeverRecorded = long.MinValue;

    private readonly SimCheckRecord[] _records;
    private readonly long[] _lastTick;
    private readonly uint[] _suppressed;
    private readonly int _overflowSlot;   // == number of defined codes, derived from the enum's maximum
    private int _head;      // index of the oldest record
    private int _count;
    private uint _overwritten;

    /// <summary>Creates a ring, preallocating every array.</summary>
    /// <param name="capacity">Record capacity; must be at least 1 (default 256).</param>
    /// <param name="rateLimitTicks">Minimum ticks between stored records of one code; 0 disables rate limiting (default 30).</param>
    /// <exception cref="ArgumentOutOfRangeException">Capacity below 1 or negative rate limit (construction only).</exception>
    public SimCheckRing(int capacity = 256, int rateLimitTicks = 30)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(rateLimitTicks);
        _records = new SimCheckRecord[capacity];
        RateLimitTicks = rateLimitTicks;

        // Derived from the enum itself so an appended CheckCode member automatically gets its own slot.
        int maxCode = 0;
        foreach (CheckCode defined in Enum.GetValues<CheckCode>())
        {
            maxCode = Math.Max(maxCode, (int)defined);
        }

        _overflowSlot = maxCode + 1;
        _lastTick = new long[_overflowSlot + 1];
        _suppressed = new uint[_overflowSlot + 1];
        Array.Fill(_lastTick, NeverRecorded);
    }

    /// <summary>Maximum number of records held.</summary>
    public int Capacity => _records.Length;

    /// <summary>Minimum ticks between stored records of one code.</summary>
    public int RateLimitTicks { get; }

    /// <summary>Records currently held (at most <see cref="Capacity"/>).</summary>
    public int Count => _count;

    /// <summary>Number of records evicted because the ring was full.</summary>
    public uint Overwritten => _overwritten;

    /// <summary>The i-th record, oldest first (0 to <see cref="Count"/> - 1).</summary>
    /// <param name="i">Index.</param>
    /// <exception cref="ArgumentOutOfRangeException">Index outside 0 to Count - 1 (reading is not the always-on path).</exception>
    public SimCheckRecord this[int i]
    {
        get
        {
            if (unchecked((uint)i) >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(i));
            }

            int index = _head + i;
            if (index >= _records.Length)
            {
                index -= _records.Length;
            }

            return _records[index];
        }
    }

    /// <summary>Records a check subject to the per-code rate limit. Never throws, formats or allocates.</summary>
    /// <param name="code">Check code; any value is accepted.</param>
    /// <param name="tick">Current tick.</param>
    /// <param name="a">Payload a.</param>
    /// <param name="b">Payload b.</param>
    /// <example><code>ring.Record(CheckCode.IdExhausted, tick, nextId, 0);</code></example>
    public void Record(CheckCode code, uint tick, long a, long b)
    {
        ushort raw = (ushort)code;
        int slot = raw < _overflowSlot ? raw : _overflowSlot;
        if (!ShouldStore(slot, tick))
        {
            if (_suppressed[slot] != uint.MaxValue)
            {
                _suppressed[slot]++;
            }

            return;
        }

        _lastTick[slot] = tick;
        Append(new SimCheckRecord(raw, tick, a, b));
    }

    /// <summary>Rate-limit decision for a slot: never recorded, window elapsed, or tick went backwards.</summary>
    private bool ShouldStore(int slot, uint tick)
    {
        long last = _lastTick[slot];
        if (last == NeverRecorded)
        {
            return true;
        }

        long delta = (long)tick - last;
        return delta < 0 || delta >= RateLimitTicks;
    }

    /// <summary>Appends to the ring, evicting the oldest record when full.</summary>
    private void Append(SimCheckRecord record)
    {
        int length = _records.Length;
        int writeAt;
        if (_count < length)
        {
            writeAt = _head + _count;
            if (writeAt >= length)
            {
                writeAt -= length;
            }

            _count++;
        }
        else
        {
            writeAt = _head;
            _head = _head + 1 == length ? 0 : _head + 1;
            if (_overwritten != uint.MaxValue)
            {
                _overwritten++;
            }
        }

        _records[writeAt] = record;
    }

    /// <summary>Records suppressed by the rate limit for <paramref name="code"/> (shared slot for undefined codes).</summary>
    /// <param name="code">Check code.</param>
    /// <example><code>uint s = ring.Suppressed(CheckCode.TickHitch);</code></example>
    public uint Suppressed(CheckCode code)
    {
        ushort raw = (ushort)code;
        return _suppressed[raw < _overflowSlot ? raw : _overflowSlot];
    }
}
