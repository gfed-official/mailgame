namespace PerformativeMail.Sim.Primitives;

/// <summary>
/// Injected, allocation-free counter sink for the numerics helpers. It wraps a caller-owned
/// <see cref="Span{T}"/> of <see langword="long"/> laid out by <see cref="PrimitiveCounter"/>;
/// the helpers never touch statics. Core's <c>SimCounters</c> (a later story) hands out a sink
/// over the slice that holds this block; tests construct one over a local <c>long[6]</c>.
/// <para>
/// Increments are non-throwing: a <c>default</c> sink or a block shorter than the counter's
/// index silently drops the count, and a counter saturates at <see cref="long.MaxValue"/>.
/// Not thread-safe: one owner at a time (the Sim tick thread).
/// </para>
/// </summary>
/// <example>
/// <code>
/// long[] block = new long[SimCounterSink.RequiredLength];
/// var sink = new SimCounterSink(block);
/// Cents big = Cents.FromCents(Cents.Max, ref sink);
/// Cents scaled = Cents.MulPermille(big, 1_000_000, ref sink);   // clamps
/// long clamps = sink.Get(PrimitiveCounter.MoneyClamped);        // 1
/// </code>
/// </example>
public readonly ref struct SimCounterSink
{
    /// <summary>Number of <see cref="PrimitiveCounter"/> slots a backing block needs to count everything.</summary>
    public const int RequiredLength = 6;

    private readonly Span<long> _counts;

    /// <summary>Creates a sink over <paramref name="counts"/>, indexed by <see cref="PrimitiveCounter"/>.</summary>
    /// <param name="counts">Caller-owned counter block; ideally <see cref="RequiredLength"/> long or more.</param>
    public SimCounterSink(Span<long> counts)
    {
        _counts = counts;
    }

    /// <summary>Length of the backing block.</summary>
    public int Length => _counts.Length;

    /// <summary>Adds one to <paramref name="counter"/>; dropped if the block has no such slot; saturates at <see cref="long.MaxValue"/>.</summary>
    /// <param name="counter">The counter to bump.</param>
    /// <example>
    /// <code>sink.Increment(PrimitiveCounter.FixClamped);</code>
    /// </example>
    public void Increment(PrimitiveCounter counter)
    {
        int index = (int)counter;
        if ((uint)index < (uint)_counts.Length && _counts[index] != long.MaxValue)
        {
            _counts[index]++;
        }
    }

    /// <summary>Reads <paramref name="counter"/>; 0 if the block has no such slot.</summary>
    /// <param name="counter">The counter to read.</param>
    /// <returns>The current count.</returns>
    /// <example>
    /// <code>long n = sink.Get(PrimitiveCounter.FixDivZero);</code>
    /// </example>
    public long Get(PrimitiveCounter counter)
    {
        int index = (int)counter;
        return (uint)index < (uint)_counts.Length ? _counts[index] : 0L;
    }
}
