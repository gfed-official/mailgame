using System;

namespace PerformativeMail.Server
{
    /// <summary>
    /// How long server ticks take (doc 12 M0: "server tick ≤ 2 ms with 8 players"): mean, percentiles
    /// and the worst one, over every tick after a warm-up in which the runtime is still compiling.
    /// A fixed histogram, so measuring allocates nothing.
    /// </summary>
    public sealed class TickStats
    {
        /// <summary>The first five seconds of ticks are not counted: JIT and first-use costs, not the game's.</summary>
        public const int WarmUpTicks = 150;

        private const double BucketMs = 0.02;
        private const int Buckets = 500; // up to 10 ms; anything slower lands in the last bucket

        private readonly int[] _histogram = new int[Buckets + 1];
        private int _seen;
        private double _sumMs;

        /// <summary>Ticks counted (after the warm-up).</summary>
        public int Count { get; private set; }
        public double MaxMs { get; private set; }
        public double MeanMs => Count == 0 ? 0.0 : _sumMs / Count;

        public void Add(double ms)
        {
            if (_seen++ < WarmUpTicks) return;
            Count++;
            _sumMs += ms;
            if (ms > MaxMs) MaxMs = ms;
            _histogram[Math.Min(Buckets, (int)(ms / BucketMs))]++;
        }

        /// <summary>The tick time that the given share of ticks (0..1) stayed at or under, to the bucket's upper edge.</summary>
        public double PercentileMs(double share)
        {
            if (Count == 0) return 0.0;
            int wanted = Math.Max(1, (int)Math.Ceiling(share * Count)), seen = 0;
            for (int i = 0; i <= Buckets; i++)
            {
                seen += _histogram[i];
                if (seen >= wanted) return i == Buckets ? MaxMs : (i + 1) * BucketMs;
            }
            return MaxMs;
        }

        public override string ToString() =>
            FormattableString.Invariant($"mean {MeanMs:0.000} ms, p99 {PercentileMs(0.99):0.00} ms, max {MaxMs:0.00} ms over {Count} ticks");
    }
}
