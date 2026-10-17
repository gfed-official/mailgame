using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Hosting;

/// <summary>
/// AC-2: the F2 invariant property test for <see cref="TickDriver"/>. A seeded <see cref="Pcg32"/> (never
/// <c>System.Random</c>) generates integer timestamp steps, including negatives, zero, and values above one second,
/// interleaved with <c>DropRemaining</c>, <c>Pause</c>/<c>Resume</c> and <c>Resync</c>. After every call:
/// <c>0 &lt;= acc30 &lt; freq</c>, <c>n &lt;= cap</c>, and
/// <c>(TicksRunTotal + DroppedTicksTotal) * freq + acc30 == sum(min(max(delta, 0), freq) * 30)</c> since the last
/// Start/Resume/Resync, exactly.
/// </summary>
public class TickDriverInvariantTests
{
    private static long NextStep(ref Pcg32 rng, long freq)
    {
        switch (rng.Next32() % 8)
        {
            case 0:
                return -(long)(rng.Next32() % (uint)Math.Min(freq, 1_000_000_000L)); // backwards (maybe 0)
            case 1:
                return 0;
            case 2:
                return freq + (long)(rng.Next32() % 1_000_000u) + 1; // above one second
            case 3:
                return freq * (1 + (long)(rng.Next32() % 50u)); // many seconds
            default:
                return (long)(((ulong)rng.Next32() << 8) % (ulong)(freq / 4 + 1)); // typical frame, up to 250 ms
        }
    }

    [Theory]
    [Trait("Id", "SC-F2-1")]
    [InlineData(1_000L, 4, 1UL)]
    [InlineData(10_000_000L, 4, 2UL)]
    [InlineData(10_000_000L, 1, 3UL)]
    [InlineData(10_000_000L, 8, 4UL)]
    [InlineData(1_000_000_000L, 3, 5UL)]
    [InlineData(1_000_000_000_000L, 4, 6UL)]
    [InlineData(1_000_003L, 5, 7UL)]
    public void F2Invariant_HoldsAfterEveryCall_OverSeededRandomSequence(long freq, int cap, ulong seed)
    {
        var rng = new Pcg32(seed, 54);
        var d = new TickDriver(freq, cap);
        long now = 1_000_000_000L;
        d.Start(now);

        long modelLast = now;
        long expectedUnits = 0;       // sum of min(max(delta,0),freq) * 30 since the last re-base
        long baseTotal = 0;           // (run + dropped) at the last re-base
        bool paused = false;
        long totalRun = 0;

        for (int i = 0; i < 20_000; i++)
        {
            uint op = rng.Next32() % 40;
            if (op == 0)
            {
                // Re-base via Resync (Running only).
                if (!paused)
                {
                    now += NextStep(ref rng, freq);
                    if (now < 0) { now = 0; }
                    d.Resync(now);
                    modelLast = now;
                    expectedUnits = 0;
                    baseTotal = d.TicksRunTotal + d.DroppedTicksTotal;
                }
            }
            else if (op == 1)
            {
                d.Pause();
                paused = true;
            }
            else if (op == 2)
            {
                now += NextStep(ref rng, freq);
                if (now < 0) { now = 0; }
                d.Resume(now);
                if (paused)
                {
                    modelLast = now;
                    expectedUnits = 0;
                    baseTotal = d.TicksRunTotal + d.DroppedTicksTotal;
                }

                paused = false;
            }
            else if (op == 3)
            {
                // DropRemaining of a random valid amount: moves run -> dropped, sum unchanged.
                long run = d.TicksRunTotal;
                int m = run == 0 ? 0 : (int)(rng.Next32() % (uint)Math.Min(run, cap) + 1);
                d.DropRemaining(m);
            }
            else
            {
                long step = NextStep(ref rng, freq);
                now += step;
                if (now < 0)
                {
                    now = 0;
                }

                long runBefore = d.TicksRunTotal;
                int n = d.Advance(now);

                Assert.InRange(n, 0, cap);
                if (paused)
                {
                    Assert.Equal(0, n);
                }
                else
                {
                    long delta = now - modelLast;
                    long clamped = Math.Min(Math.Max(delta, 0), freq);
                    expectedUnits += clamped * TickRate.Hz;
                    modelLast = Math.Max(modelLast, now);
                    totalRun += n;
                    Assert.Equal(runBefore + n, d.TicksRunTotal);
                }
            }

            Assert.InRange(d.Acc30, 0L, freq - 1);
            Assert.InRange(d.AlphaPermille, 0, 999);
            long total = d.TicksRunTotal + d.DroppedTicksTotal - baseTotal;
            Assert.Equal(expectedUnits, total * freq + d.Acc30);
            Assert.Equal(modelLast, d.Last);
        }

        Assert.True(totalRun > 0, "the sequence must have released ticks");
        Assert.True(d.BackwardsClock > 0 && d.Hitches > 0 && d.ClampedTimeTotal > 0, "the sequence must exercise every counter");
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void F2Invariant_IsDeterministic_SameSeedSameCounters()
    {
        TickDriverCounters a = Run(42);
        TickDriverCounters b = Run(42);
        TickDriverCounters c = Run(43);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    private static TickDriverCounters Run(ulong seed)
    {
        var rng = new Pcg32(seed, 7);
        var d = new TickDriver(10_000_000, 4);
        long now = 5_000_000;
        d.Start(now);
        for (int i = 0; i < 5_000; i++)
        {
            now = Math.Max(0, now + NextStep(ref rng, 10_000_000));
            d.Advance(now);
        }

        return d.Counters;
    }
}
