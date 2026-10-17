namespace PerformativeMail.Sim.Core.Replay;

/// <summary>
/// An append-only list of records with a fixed-capacity primary array and a small fixed scratch array. <c>Add</c> never
/// allocates: past the primary capacity it spills into the scratch, past the scratch it counts the record in
/// <see cref="Lost"/>. The owner grows the primary array only between ticks (<c>InMemoryRecorder.Flush</c>), moving the scratch
/// into it, so the order of records is always primary then scratch. Reading is by index; not thread-safe.
/// </summary>
/// <typeparam name="T">A blittable record struct.</typeparam>
/// <example>
/// <code>
/// RecordList&lt;RecordedOp&gt; ops = recorder.Ops;
/// for (int i = 0; i &lt; ops.Count; i++) { uint b = ops[i].BoundaryTick; }
/// </code>
/// </example>
public sealed class RecordList<T>
    where T : struct
{
    private readonly T[] _scratch;
    private T[] _items;
    private int _count;
    private int _scratchCount;
    private long _lost;

    internal RecordList(int capacity, int scratchCapacity)
    {
        _items = new T[capacity];
        _scratch = new T[scratchCapacity];
    }

    /// <summary>Records held (primary plus scratch).</summary>
    public int Count => _count + _scratchCount;

    /// <summary>Current primary capacity (grows only at <c>Flush</c>).</summary>
    public int Capacity => _items.Length;

    /// <summary>Records currently in the scratch (waiting for the next <c>Flush</c> to move them into the primary array).</summary>
    public int ScratchCount => _scratchCount;

    /// <summary>Records dropped because both the primary array and the scratch were full. Non-zero means the recording is incomplete.</summary>
    public long Lost => _lost;

    /// <summary>The <paramref name="index"/>-th record in arrival order.</summary>
    /// <param name="index">0 to <see cref="Count"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public ref readonly T this[int index]
    {
        get
        {
            if (unchecked((uint)index) >= (uint)(_count + _scratchCount))
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return ref index < _count ? ref _items[index] : ref _scratch[index - _count];
        }
    }

    /// <summary>Copies the records, in order, to a new array (allocates; for tests and tools).</summary>
    /// <returns>A new array of <see cref="Count"/> elements.</returns>
    public T[] ToArray()
    {
        var copy = new T[Count];
        Array.Copy(_items, copy, _count);
        Array.Copy(_scratch, 0, copy, _count, _scratchCount);
        return copy;
    }

    /// <summary>Appends a record without allocating (tick path).</summary>
    internal void Add(in T item)
    {
        if (_scratchCount == 0 && _count < _items.Length)
        {
            _items[_count++] = item;
        }
        else if (_scratchCount < _scratch.Length)
        {
            _scratch[_scratchCount++] = item;
        }
        else
        {
            _lost++;
        }
    }

    /// <summary>
    /// Between ticks only: moves the scratch into the primary array, growing it (doubling, allocation) so that at least
    /// <c>max(scratch capacity, capacity / 2)</c> slots are free afterwards.
    /// </summary>
    internal void Grow()
    {
        int total = _count + _scratchCount;
        int headroom = Math.Max(_scratch.Length, _items.Length / 2);
        long wanted = _items.Length;
        while (wanted - total < headroom)
        {
            wanted *= 2;
        }

        wanted = Math.Min(wanted, Array.MaxLength);
        if (wanted > _items.Length)
        {
            Array.Resize(ref _items, (int)wanted);
        }

        int movable = Math.Min(_scratchCount, _items.Length - _count);
        Array.Copy(_scratch, 0, _items, _count, movable);
        _count += movable;
        if (movable < _scratchCount)
        {
            Array.Copy(_scratch, movable, _scratch, 0, _scratchCount - movable);
        }

        _scratchCount -= movable;
    }
}
