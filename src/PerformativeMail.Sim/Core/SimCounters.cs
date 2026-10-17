using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Core;

/// <summary>
/// Per-world always-on counters: a <c>long[]</c> indexed by <see cref="CounterId"/>. Owned and
/// injected by the composition root; never static. Not thread-safe: written by the Sim tick thread
/// only and read between ticks.
/// <para>
/// <see cref="Add"/> and the indexer never throw: an id beyond <see cref="Length"/> is dropped on
/// write and reads 0. Adds saturate at <see cref="long.MinValue"/> / <see cref="long.MaxValue"/>
/// (no <c>checked</c> arithmetic as control flow). A negative <c>n</c> is allowed because
/// <see cref="CounterId.DrainBacklog"/> is a level.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var counters = new SimCounters();
/// counters.Add(CounterId.EventsDropped);
/// counters.Add(CounterId.EventsDropped, 4);
/// long dropped = counters[CounterId.EventsDropped];   // 5
/// var sink = counters.PrimitiveSink();                 // for Fix/Cents/Fx16 helpers
/// Fix.MulDiv(1, 1, 0, ref sink);
/// long zeroDivs = counters[CounterId.FixDivZero];      // 1
/// </code>
/// </example>
public sealed class SimCounters
{
    private readonly long[] _counts;

    /// <summary>Creates a zeroed set of counters sized from the highest defined <see cref="CounterId"/>, so an appended id is always countable.</summary>
    public SimCounters()
    {
        int max = 0;
        foreach (CounterId id in Enum.GetValues<CounterId>())
        {
            max = Math.Max(max, (int)id);
        }

        _counts = new long[max + 1];
    }

    /// <summary>Number of counter slots: one past the highest defined <see cref="CounterId"/>.</summary>
    public int Length => _counts.Length;

    /// <summary>Adds <paramref name="n"/> to <paramref name="id"/>, saturating; ignored for an unknown id.</summary>
    /// <param name="id">The counter.</param>
    /// <param name="n">Amount to add (default 1); may be negative or large.</param>
    /// <example><code>counters.Add(CounterId.BadTick); counters.Add(CounterId.DrainBacklog, -3);</code></example>
    public void Add(CounterId id, long n = 1)
    {
        int index = (int)id;
        if ((uint)index >= (uint)_counts.Length)
        {
            return;
        }

        long cur = _counts[index];
        if (n > 0)
        {
            _counts[index] = cur > long.MaxValue - n ? long.MaxValue : cur + n;
        }
        else if (n < 0)
        {
            _counts[index] = cur < long.MinValue - n ? long.MinValue : cur + n;
        }
    }

    /// <summary>Sets <paramref name="id"/> to <paramref name="value"/> (for level counters such as <see cref="CounterId.DrainBacklog"/>); ignored for an unknown id.</summary>
    /// <param name="id">The counter.</param>
    /// <param name="value">The new level.</param>
    /// <example><code>counters.Set(CounterId.DrainBacklog, 36);</code></example>
    public void Set(CounterId id, long value)
    {
        int index = (int)id;
        if ((uint)index < (uint)_counts.Length)
        {
            _counts[index] = value;
        }
    }

    /// <summary>Current value of <paramref name="id"/>; 0 for an unknown id.</summary>
    /// <param name="id">The counter.</param>
    /// <example><code>long n = counters[CounterId.BadTick];</code></example>
    public long this[CounterId id]
    {
        get
        {
            int index = (int)id;
            return (uint)index < (uint)_counts.Length ? _counts[index] : 0L;
        }
    }

    /// <summary>
    /// A <see cref="SimCounterSink"/> over the numerics block (ids 0 to 5), length
    /// <see cref="SimCounterSink.RequiredLength"/>. Increments are visible through the indexer.
    /// </summary>
    /// <example><code>var sink = counters.PrimitiveSink(); Fix.MulDiv(1, 1, 0, ref sink);</code></example>
    public SimCounterSink PrimitiveSink() => new(_counts.AsSpan(0, SimCounterSink.RequiredLength));
}
