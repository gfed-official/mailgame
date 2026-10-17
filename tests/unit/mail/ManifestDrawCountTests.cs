using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Covers mail-destinations story 012 AC-4 (GDD M-DET-2, M-R14-1b, M-PERF-1e; TR-mail-002/003/004): the fixed draw count
/// K, its per-category split (so a reorder fails loudly), and that the roller never touches <c>mail.special</c>.
/// </summary>
public class ManifestDrawCountTests
{
    private const ulong RunSeed = 0xABCD_0012UL;
    private static readonly BatchKnobs _k = BatchKnobs.Default;

    private sealed class Out
    {
        public Out(int n)
        {
            Kinds = new byte[n];
            for (int i = 0; i < n; i++)
            {
                Kinds[i] = (byte)(i % 6);
            }

            Ticks = new long[BatchSchedule.MaxBatches];
            Counts = new int[BatchSchedule.MaxBatches];
            Addr = new AddressId[n];
            Cursed = new bool[n];
        }

        public byte[] Kinds { get; }
        public long[] Ticks { get; }
        public int[] Counts { get; }
        public AddressId[] Addr { get; }
        public bool[] Cursed { get; }
    }

    private static (RngStream Mail, RngStream Special) Streams(uint shift = 5)
    {
        var registry = new RngRegistry(RunSeed, new SimCounters());
        IRngStreams view = registry.ViewFor(SystemId.MailDestinations);
        return (view.Create(in RngNames.Mail, shift), view.Create(in RngNames.MailSpecial, shift));
    }

    private static int Run(RngStream mail, int d, int raid, int n, int cursedPm, Out o)
    {
        var plan = new BatchPlan(0, d, raid, n);
        return ManifestDraws.Run(mail, in plan, in _k, cursedPm, DrawTestRig.Fifty(), o.Kinds, o.Ticks, o.Counts, o.Addr, o.Cursed);
    }

    [Fact]
    [Trait("Id", "M-DET-2")]
    public void Expected_draws_formula_matches_the_golden_values()
    {
        Assert.Equal(2_163L, ManifestDraws.ExpectedDraws(20, 708));
        Assert.Equal(40L, ManifestDraws.ExpectedDraws(20, 0));
        Assert.Equal(40L + 2 + 0, ManifestDraws.ExpectedDraws(20, 1));
    }

    [Fact]
    [Trait("Id", "M-DET-2")]
    [Trait("Id2", "M-PERF-1e")]
    public void Shift5_roll_makes_exactly_K_2163_draws_and_none_on_mail_special()
    {
        (RngStream mail, RngStream special) = Streams();
        var o = new Out(708);
        Assert.Equal(20, Run(mail, 300, 90, 708, 100, o));
        Assert.Equal(2_163UL, mail.Draws);
        Assert.Equal(0UL, special.Draws);
        Assert.Equal(708, o.Counts.Take(20).Sum());
    }

    [Fact]
    [Trait("Id", "M-DET-2")]
    public void Empty_shift_consumes_only_jitter_and_pair_draws()
    {
        (RngStream mail, _) = Streams();
        var o = new Out(0);
        Assert.Equal(20, Run(mail, 300, 90, 0, 100, o));
        Assert.Equal(40UL, mail.Draws);
    }

    [Fact]
    [Trait("Id", "M-DET-2")]
    public void Empty_batches_still_consume_jitter_and_pair_draws()
    {
        (RngStream mail, _) = Streams();
        var o = new Out(3);
        Run(mail, 300, 90, 3, 0, o);
        Assert.True(o.Counts.Take(20).Count(c => c == 0) >= 17);
        Assert.Equal((ulong)ManifestDraws.ExpectedDraws(20, 3), mail.Draws);
        Assert.Equal(40UL + 6 + 2, mail.Draws);
    }

    [Theory]
    [Trait("Id", "M-R14-1b")]
    [InlineData(0, 100)]
    [InlineData(0, 1000)]
    public void Draw_count_and_addresses_are_identical_for_any_cursed_share(int a, int b)
    {
        (RngStream m1, _) = Streams();
        (RngStream m2, _) = Streams();
        var o1 = new Out(708);
        var o2 = new Out(708);
        Run(m1, 300, 90, 708, a, o1);
        Run(m2, 300, 90, 708, b, o2);
        Assert.Equal(m1.Draws, m2.Draws);
        Assert.Equal(o1.Addr, o2.Addr);
        Assert.Equal(o1.Kinds, o2.Kinds);
        Assert.Equal(o1.Ticks, o2.Ticks);
        Assert.DoesNotContain(true, o1.Cursed);
        Assert.Contains(true, o2.Cursed);
    }

    [Fact]
    [Trait("Id", "M-DET-2")]
    public void Draw_order_is_jitter_then_shuffle_then_per_batch_pair_address_cursed()
    {
        // Compose the steps by hand on a second identical stream, asserting each category's draw count, then compare
        // with Run: a reordered Run would diverge in outputs and not just in counts.
        const int UnitCount = 90;
        (RngStream manual, _) = Streams();
        var plan = new BatchPlan(0, 300, 90, UnitCount);
        var jitter = new int[20];
        Assert.Equal(20, BatchSchedule.DrawJitters(manual, 300, in _k, jitter));
        Assert.Equal(20UL, manual.Draws);

        var kinds = new Out(UnitCount).Kinds;
        UnitShuffle.Shuffle(manual, kinds.AsSpan());
        Assert.Equal(20UL + (UnitCount - 1), manual.Draws);

        var ticks = new long[20];
        var counts = new int[20];
        BatchSchedule.Layout(in plan, in _k, jitter, ticks, counts);
        Assert.Equal(20UL + (UnitCount - 1), manual.Draws);

        AddressId[] houses = DrawTestRig.Fifty();
        var addr = new AddressId[UnitCount];
        var cursed = new bool[UnitCount];
        int offset = 0;
        for (int b = 0; b < 20; b++)
        {
            ulong before = manual.Draws;
            DestinationDraw.DrawBatch(manual, houses, counts[b], 300, 100, addr.AsSpan(offset), cursed.AsSpan(offset));
            Assert.Equal(before + 1UL + (2UL * (ulong)counts[b]), manual.Draws);
            offset += counts[b];
        }

        Assert.Equal((ulong)ManifestDraws.ExpectedDraws(20, UnitCount), manual.Draws);

        (RngStream mail, _) = Streams();
        var o = new Out(UnitCount);
        Run(mail, 300, 90, UnitCount, 100, o);
        Assert.Equal(manual.Draws, mail.Draws);
        Assert.Equal(kinds, o.Kinds);
        Assert.Equal(ticks, o.Ticks.Take(20));
        Assert.Equal(counts, o.Counts.Take(20));
        Assert.Equal(addr, o.Addr);
        Assert.Equal(cursed, o.Cursed);
    }

    [Fact]
    [Trait("Id", "M-DET-2")]
    public void Roller_never_touches_a_spy_mail_special_stream()
    {
        var spy = new ScriptedSource(7);
        RngStream special = DrawTestRig.Stub(spy);
        (RngStream mail, _) = Streams();
        Run(mail, 300, 90, 708, 100, new Out(708));
        Assert.Equal(0, spy.Taken);
        Assert.Equal(0UL, special.Draws);
    }

    [Fact]
    [Trait("Id", "M-DET-2")]
    public void Same_stream_key_reproduces_and_a_different_shift_differs()
    {
        var a = new Out(200);
        var b = new Out(200);
        var c = new Out(200);
        Run(Streams(3).Mail, 300, 90, 200, 100, a);
        Run(Streams(3).Mail, 300, 90, 200, 100, b);
        Run(Streams(4).Mail, 300, 90, 200, 100, c);
        Assert.Equal(a.Addr, b.Addr);
        Assert.Equal(a.Ticks, b.Ticks);
        Assert.NotEqual(a.Addr, c.Addr);
    }

    [Fact]
    public void Input_errors_draw_nothing()
    {
        (RngStream mail, _) = Streams();
        var o = new Out(5);
        var plan = new BatchPlan(0, 300, 90, 5);
        Assert.Equal(BatchScheduleStatus.InvalidInput, ManifestDraws.Run(mail, in plan, in _k, 0, [], o.Kinds, o.Ticks, o.Counts, o.Addr, o.Cursed));
        Assert.Equal(BatchScheduleStatus.OutputTooSmall, ManifestDraws.Run(mail, in plan, in _k, 0, DrawTestRig.Fifty(), new byte[2], o.Ticks, o.Counts, o.Addr, o.Cursed));
        Assert.Equal(BatchScheduleStatus.OutputTooSmall, ManifestDraws.Run(mail, in plan, in _k, 0, DrawTestRig.Fifty(), o.Kinds, new long[3], o.Counts, o.Addr, o.Cursed));
        Assert.Equal(0UL, mail.Draws);
    }
}
