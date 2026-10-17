using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.MailDestinations;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>Covers mail-destinations story 012 AC-1 and AC-2 (GDD M-F2-1, M-F2-2, M-R10-1f; F2). Deterministic, no IO.</summary>
public class BatchScheduleTests
{
    private const long Start = 10_000;
    private static readonly BatchKnobs _k = BatchKnobs.Default;

    private static (int Nb, long[] Ticks, int[] Counts) Layout(int d, int raid, int n, BatchKnobs k, int[]? jitter = null)
    {
        var plan = new BatchPlan(Start, d, raid, n);
        var ticks = new long[BatchSchedule.MaxBatches];
        var counts = new int[BatchSchedule.MaxBatches];
        int nb = BatchSchedule.Layout(in plan, in k, jitter ?? new int[BatchSchedule.MaxBatches], ticks, counts);
        return (nb, ticks, counts);
    }

    [Fact]
    [Trait("Id", "M-F2-1")]
    public void Shift5_D300_raid90_no_jitter_N708_matches_golden_split()
    {
        (int nb, long[] ticks, int[] c) = Layout(300, 90, 708, _k);
        Assert.Equal(20, nb);
        var plan = new BatchPlan(Start, 300, 90, 708);
        long w = 0;
        for (int b = 0; b < nb; b++)
        {
            w += BatchSchedule.Weight(in plan, in _k, b, ticks[b]);
        }

        Assert.Equal(17_000L, w);
        Assert.Equal(708, c.Take(nb).Sum());
        Assert.Equal(583, c.Take(14).Sum());
        Assert.Equal(125, c.Skip(14).Take(6).Sum());
        Assert.All(c.Take(14), x => Assert.InRange(x, 41, 42));
        Assert.All(c.Skip(14).Take(6), x => Assert.InRange(x, 20, 21));
    }

    [Fact]
    [Trait("Id", "M-F2-1")]
    public void Batch14_weight_flips_at_the_raid_boundary()
    {
        var plan = new BatchPlan(Start, 300, 90, 708);
        var early = new int[BatchSchedule.MaxBatches];
        early[14] = -1;
        (_, long[] t1, _) = Layout(300, 90, 708, _k, early);
        Assert.Equal(Start + 6_299, t1[14]);
        Assert.Equal(1000L, BatchSchedule.Weight(in plan, in _k, 14, t1[14]));
        (_, long[] t0, _) = Layout(300, 90, 708, _k);
        Assert.Equal(Start + 6_300, t0[14]);
        Assert.Equal(500L, BatchSchedule.Weight(in plan, in _k, 14, t0[14]));

        long w = 0;
        for (int b = 0; b < 20; b++)
        {
            w += BatchSchedule.Weight(in plan, in _k, b, t1[b]);
        }

        Assert.Equal(17_500L, w);
    }

    [Theory]
    [Trait("Id", "M-F2-2")]
    [InlineData(240, 16)]
    [InlineData(270, 18)]
    [InlineData(300, 20)]
    [InlineData(210, 14)]
    [InlineData(250, 17)]
    public void Batch_count_is_ceil_of_D_over_interval(int d, int expected)
        => Assert.Equal(expected, BatchSchedule.BatchCount(d, in _k));

    [Theory]
    [Trait("Id", "M-F2-2")]
    [InlineData(300)]
    [InlineData(250)]
    [InlineData(210)]
    public void Maximum_jitter_leaves_the_tail_guard(int d)
    {
        var j = new int[BatchSchedule.MaxBatches];
        Array.Fill(j, 90);
        (int nb, long[] ticks, _) = Layout(d, 0, 100, _k, j);
        long end = Start + (30L * d);
        for (int b = 0; b < nb; b++)
        {
            Assert.True(end - ticks[b] >= 270, $"batch {b}");
        }
    }

    [Fact]
    [Trait("Id", "M-F2-2")]
    public void D250_last_batch_is_clamped_to_7230()
    {
        var j = new int[BatchSchedule.MaxBatches];
        Array.Fill(j, 90);
        (int nb, long[] ticks, _) = Layout(250, 0, 100, _k, j);
        Assert.Equal(17, nb);
        Assert.Equal(Start + 7_230, ticks[16]);
    }

    [Fact]
    [Trait("Id", "M-F2-2")]
    public void Jitter_bounds_with_stub_extremes()
    {
        var low = new int[20];
        var lowRng = DrawTestRig.Stub(new ScriptedSource(0));
        Assert.Equal(20, BatchSchedule.DrawJitters(lowRng, 300, in _k, low));
        Assert.Equal(0, low[0]);
        Assert.All(low.Skip(1), x => Assert.Equal(-90, x));
        Assert.Equal(20UL, lowRng.Draws);

        var high = new int[20];
        BatchSchedule.DrawJitters(DrawTestRig.Stub(new ScriptedSource(uint.MaxValue)), 300, in _k, high);
        Assert.All(high, x => Assert.Equal(90, x));
    }

    [Fact]
    [Trait("Id", "M-F2-2")]
    public void Batch0_jitter_is_non_negative_and_times_strictly_ordered_over_many_seeds()
    {
        var j = new int[BatchSchedule.MaxBatches];
        var plan = new BatchPlan(Start, 300, 90, 708);
        var ticks = new long[BatchSchedule.MaxBatches];
        var counts = new int[BatchSchedule.MaxBatches];
        for (uint seed = 1; seed <= 300; seed++)
        {
            int nb = BatchSchedule.DrawJitters(DrawTestRig.Seeded(DrawTestRig.FuzzSeed + seed, 5), 300, in _k, j);
            Assert.True(j[0] >= 0);
            BatchSchedule.Layout(in plan, in _k, j, ticks, counts);
            for (int b = 1; b < nb; b++)
            {
                Assert.True(ticks[b] > ticks[b - 1]);
                Assert.InRange(j[b], -90, 90);
            }
        }
    }

    [Theory]
    [Trait("Id", "M-F2-2")]
    [Trait("Id2", "M-R10-1f")]
    [InlineData(300, 0, 0, 708)]
    [InlineData(300, 90, 0, 708)]
    [InlineData(300, 90, 2, 708)]
    [InlineData(240, 90, 2, 57)]
    [InlineData(270, 0, 3, 0)]
    [InlineData(210, 90, 0, 814)]
    public void Counts_always_sum_to_N(int d, int raid, int ramp, int n)
    {
        BatchKnobs k = _k with { OpeningRampBatches = ramp };
        (int nb, _, int[] c) = Layout(d, raid, n, k);
        Assert.Equal(n, c.Take(nb).Sum());
        Assert.All(c.Take(nb), x => Assert.True(x >= 0));
    }

    [Fact]
    [Trait("Id", "M-R10-1f")]
    public void Opening_ramp_halves_the_first_batches()
    {
        BatchKnobs k = _k with { OpeningRampBatches = 2 };
        (_, _, int[] c) = Layout(300, 0, 708, k);
        Assert.InRange(Math.Abs((2 * c[0]) - c[2]), 0, 2);
        Assert.InRange(Math.Abs((2 * c[1]) - c[2]), 0, 2);
    }

    [Fact]
    public void Invalid_inputs_return_status_codes()
    {
        Assert.Equal(BatchScheduleStatus.InvalidInput, BatchSchedule.BatchCount(0, in _k));
        Assert.Equal(BatchScheduleStatus.InvalidInput, BatchSchedule.BatchCount(300, _k with { BatchIntervalSeconds = 0 }));
        Assert.Equal(BatchScheduleStatus.InvalidInput, Layout(300, 400, 10, _k).Nb);
        Assert.Equal(BatchScheduleStatus.InvalidInput, Layout(300, 0, -1, _k).Nb);
        Assert.Equal(BatchScheduleStatus.TooManyBatches, BatchSchedule.BatchCount(5000, in _k));
        var plan = new BatchPlan(Start, 300, 0, 10);
        Assert.Equal(BatchScheduleStatus.OutputTooSmall, BatchSchedule.Layout(in plan, in _k, new int[3], new long[3], new int[3]));
        var rng = DrawTestRig.Stub(new ScriptedSource(0));
        Assert.Equal(BatchScheduleStatus.OutputTooSmall, BatchSchedule.DrawJitters(rng, 300, in _k, new int[3]));
        Assert.Equal(0UL, rng.Draws);
        BatchKnobs zero = _k with { RaidSpawnRateMultPm = 0 };
        Assert.Equal(BatchScheduleStatus.ZeroWeight, Layout(300, 300, 10, zero).Nb);
    }

    [Fact]
    public void Knobs_from_mail_mix_def_match_the_defaults()
    {
        var mix = new MailMixDef
        {
            Id = "t", Shifts = ImmutableArray<ShiftMix>.Empty, BatchIntervalMs = 15_000, BatchJitterMs = 3_000,
            TailGuardMs = 9_000, SpawnOverheadPm = 1_600, StreetStreakRatioPm = 300, RaidSpawnRateRatioPm = 500,
            DistanceStepRatioPm = 250, ShiftStepRatioPm = 100,
        };
        Assert.Equal(BatchKnobs.Default, BatchKnobs.From(mix));
    }
}
