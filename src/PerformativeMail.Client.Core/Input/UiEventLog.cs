namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The shared preallocated event ring: 4096 <see cref="UiEventEntry"/> values in one array, allocated once in the constructor
/// (ADR-0012 section 9; TR-input-019). The input router is its first writer; Grid Inventory (H-UI-1), the HUD and the debug
/// overlay read it. Enum ids only, no formatting here. When full, the next write overwrites the oldest entry and
/// <see cref="OverwriteCount"/> rises. <see cref="Write"/> and <see cref="LogMotion"/> allocate 0 B. Not thread-safe: write and
/// read on the main thread.
/// <para>
/// The reader sees entries oldest to newest by <see cref="TryGet"/> index 0 to <see cref="Count"/> - 1. A coalesced motion entry
/// is updated in place, so it keeps the position of the first motion event of its frame.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var log = new UiEventLog();
/// log.Write(clock.NowMs, LogKind.GateFired, reason: 0, idA: (ushort)PhysicalInput.KeyE, idB: 0, idC: 0);
/// log.LogMotion(frame: 12, clock.NowMs, dx: 3, dy: -1);
/// for (int i = 0; i &lt; log.Count; i++)
/// {
///     if (log.TryGet(i, out UiEventEntry e)) { /* format e */ }
/// }
/// </code>
/// </example>
public sealed class UiEventLog
{
    /// <summary>The ring capacity: 4096 entries (a power of two, so the slot is a mask).</summary>
    public const int Capacity = 4096;

    private const int Mask = Capacity - 1;

    private readonly UiEventEntry[] _entries = new UiEventEntry[Capacity];
    private long _written;
    private bool _motionLive;
    private long _motionFrame;
    private long _motionIndex;

    /// <summary>The number of entries currently held: 0 up to <see cref="Capacity"/>.</summary>
    public int Count => _written < Capacity ? (int)_written : Capacity;

    /// <summary>The number of entries overwritten since construction or <see cref="Clear"/> (writes beyond <see cref="Capacity"/>).</summary>
    public long OverwriteCount => _written > Capacity ? _written - Capacity : 0;

    /// <summary>The total number of entries written since construction or <see cref="Clear"/>, including overwritten ones.</summary>
    public long TotalWritten => _written;

    /// <summary>Appends one entry, overwriting the oldest when the ring is full. Allocates 0 B.</summary>
    /// <param name="timeMs">Time in milliseconds (the <see cref="IInputClock"/> domain).</param>
    /// <param name="kind">What is recorded.</param>
    /// <param name="reason">Kind-specific reason id, owned by the writer.</param>
    /// <param name="idA">First id.</param>
    /// <param name="idB">Second id.</param>
    /// <param name="idC">Third id.</param>
    /// <param name="flags">Kind-specific flag bits.</param>
    /// <example><code>log.Write(now, LogKind.HoldCancel, reason: (byte)cancelReason, idA: actionId, idB: 0, idC: 0);</code></example>
    public void Write(long timeMs, LogKind kind, byte reason, ushort idA, ushort idB, ushort idC, byte flags = 0)
    {
        _entries[(int)(_written & Mask)] = new UiEventEntry(unchecked((uint)_written), timeMs, kind, reason, idA, idB, idC, flags);
        _written++;
    }

    /// <summary>
    /// Records relative mouse motion, keeping at most one motion entry per rendered frame: the first call in a frame appends a
    /// <see cref="LogKind.RawInput"/> entry (reason <see cref="RawKind.MouseMotion"/>, flag
    /// <see cref="UiEventEntry.FlagCoalescedMotion"/>) and later calls in the same frame add their counts to it (saturating at
    /// the signed 16-bit range) and raise its merged-event count. If the frame's entry has already been overwritten, a new one
    /// is appended. Allocates 0 B.
    /// </summary>
    /// <param name="frame">The rendered frame number (any monotonically changing value; a different value starts a new entry).</param>
    /// <param name="timeMs">Time in milliseconds of this event (kept from the first event of the frame).</param>
    /// <param name="dx">Relative X counts.</param>
    /// <param name="dy">Relative Y counts.</param>
    /// <example><code>log.LogMotion(frame: 12, now, dx: 3, dy: -1); log.LogMotion(12, now, 2, 0); // one entry: dx 5, dy -1</code></example>
    public void LogMotion(long frame, long timeMs, int dx, int dy)
    {
        if (_motionLive && _motionFrame == frame && _written - _motionIndex <= Capacity)
        {
            int slot = (int)(_motionIndex & Mask);
            UiEventEntry old = _entries[slot];
            _entries[slot] = new UiEventEntry(
                old.Seq,
                old.TimeMs,
                LogKind.RawInput,
                old.Reason,
                SaturatingSum(old.IdA, dx),
                SaturatingSum(old.IdB, dy),
                old.IdC == ushort.MaxValue ? old.IdC : (ushort)(old.IdC + 1),
                old.Flags);
            return;
        }

        _motionLive = true;
        _motionFrame = frame;
        _motionIndex = _written;
        Write(timeMs, LogKind.RawInput, (byte)RawKind.MouseMotion, SaturatingSum(0, dx), SaturatingSum(0, dy), 1, UiEventEntry.FlagCoalescedMotion);
    }

    /// <summary>Reads one entry by age.</summary>
    /// <param name="index">0 for the oldest entry held up to <see cref="Count"/> - 1 for the newest (write order).</param>
    /// <param name="entry">The entry when <paramref name="index"/> is in range; default otherwise.</param>
    /// <returns>True when <paramref name="index"/> is in range.</returns>
    /// <example><code>bool newest = log.TryGet(log.Count - 1, out UiEventEntry e);</code></example>
    public bool TryGet(int index, out UiEventEntry entry)
    {
        int count = Count;
        if (unchecked((uint)index) >= (uint)count)
        {
            entry = default;
            return false;
        }

        entry = _entries[(int)((_written - count + index) & Mask)];
        return true;
    }

    /// <summary>Empties the ring and resets the counters and the motion coalescing, keeping the allocated array (for tests and a session reset).</summary>
    /// <example><code>log.Clear(); // log.Count == 0</code></example>
    public void Clear()
    {
        Array.Clear(_entries);
        _written = 0;
        _motionLive = false;
        _motionFrame = 0;
        _motionIndex = 0;
    }

    private static ushort SaturatingSum(ushort current, int delta)
    {
        long sum = unchecked((short)current) + (long)delta;
        short clamped = (short)Math.Clamp(sum, short.MinValue, short.MaxValue);
        return unchecked((ushort)clamped);
    }
}
