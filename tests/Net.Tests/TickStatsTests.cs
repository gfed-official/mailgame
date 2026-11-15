using PerformativeMail.Server;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    public class TickStatsTests
    {
        [Fact]
        public void TheWarmUpIsNotCounted()
        {
            var stats = new TickStats();
            for (int i = 0; i < TickStats.WarmUpTicks; i++) stats.Add(50.0);
            Assert.Equal(0, stats.Count);
            Assert.Equal(0.0, stats.MeanMs);
            Assert.Equal(0.0, stats.PercentileMs(0.99));

            stats.Add(1.0);
            Assert.Equal(1, stats.Count);
            Assert.Equal(1.0, stats.MeanMs);
            Assert.Equal(1.0, stats.MaxMs);
        }

        [Fact]
        public void MeanPercentileAndMaxOfAKnownSpread()
        {
            var stats = new TickStats();
            for (int i = 0; i < TickStats.WarmUpTicks; i++) stats.Add(0.0);
            // 980 quick ticks, 15 slower ones, 5 that are far over the scale.
            for (int i = 0; i < 980; i++) stats.Add(0.11);
            for (int i = 0; i < 15; i++) stats.Add(1.50);
            for (int i = 0; i < 5; i++) stats.Add(40.0);

            Assert.Equal(1000, stats.Count);
            Assert.Equal((980 * 0.11 + 15 * 1.50 + 5 * 40.0) / 1000, stats.MeanMs, 6);
            Assert.Equal(40.0, stats.MaxMs);
            Assert.Equal(0.12, stats.PercentileMs(0.50), 6);  // the upper edge of the 0.10–0.12 ms bucket
            Assert.Equal(0.12, stats.PercentileMs(0.98), 6);
            Assert.Equal(1.52, stats.PercentileMs(0.99), 6);
            Assert.Equal(40.0, stats.PercentileMs(0.999), 6); // off the scale: the worst tick stands in
            Assert.Contains("p99 1.52 ms", stats.ToString());
        }
    }
}
