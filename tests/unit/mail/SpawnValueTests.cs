using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.MailDestinations;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Covers mail-destinations story 011 AC-1 (GDD M-F1-1, F1; TR-mail-001): the spawn value is one rounding over the
/// product of five factors, always fits <see cref="ushort"/>. Deterministic, no IO.
/// </summary>
public class SpawnValueTests
{
    private static readonly MailValueKnobs _k = MailValueKnobs.Default;

    [Theory]
    [Trait("Id", "M-F1-1")]
    [InlineData("a postcard D2 s2", 4, 2, 2, false, false, 6)]
    [InlineData("b Medium D4 s3 Cursed", 70, 4, 3, true, false, 221)]
    [InlineData("c letter D5 s5", 8, 5, 5, false, false, 22)]
    [InlineData("d postcard D1 s2", 4, 1, 2, false, false, 4)]
    [InlineData("e Found Large D5 s5", 160, 5, 5, false, true, 1344)]
    [InlineData("f Cursed Large D5 s5", 160, 5, 5, true, false, 672)]
    [InlineData("g Found Medium D1 s2", 70, 1, 2, false, true, 231)]
    public void Golden_values(string label, int baseValue, int district, int shift, bool cursed, bool found, int expected)
    {
        Assert.NotEmpty(label);
        Assert.Equal((ushort)expected, SpawnValue.Compute(baseValue, district, shift, cursed, found, in _k));
    }

    [Fact]
    [Trait("Id", "M-F1-1b")]
    public void Per_factor_rounding_would_give_222_but_one_rounding_gives_221()
    {
        // 70 x 1.75 = 122.5 -> 123; x 1.2 = 147.6 -> 148; x 1.5 = 222 (the compounding the GDD forbids).
        long step1 = RoundHalfUp(70L * 1750, 1000);
        long step2 = RoundHalfUp(step1 * 1200, 1000);
        long step3 = RoundHalfUp(step2 * 1500, 1000);
        Assert.Equal(222, step3);
        Assert.Equal(221, SpawnValue.Compute(70, 4, 3, true, false, in _k));
    }

    [Fact]
    [Trait("Id", "M-F1-1")]
    public void Every_result_over_the_v1_domain_fits_ushort_and_stays_in_the_documented_range()
    {
        int[] bases = { 4, 8, 30, 70, 160 };
        int max = 0;
        int min = int.MaxValue;
        foreach (int b in bases)
        {
            for (int d = 1; d <= 5; d++)
            {
                for (int s = 1; s <= 5; s++)
                {
                    for (int flags = 0; flags < 4; flags++)
                    {
                        int v = SpawnValue.Compute(b, d, s, (flags & 1) != 0, (flags & 2) != 0, in _k);
                        max = Math.Max(max, v);
                        min = Math.Min(min, v);
                    }
                }
            }
        }

        Assert.Equal(4, min);
        // Found and Cursed together is never spawned (R15) and is only the algebraic ceiling: 160 x 2 x 1.4 x 1.5 x 3 = 2016.
        Assert.Equal(2016, max);
    }

    [Fact]
    [Trait("Id", "M-F1-1")]
    public void Out_of_range_inputs_saturate_instead_of_wrapping()
    {
        Assert.Equal(ushort.MaxValue, SpawnValue.Compute(100_000, 5, 5, false, true, in _k));
        Assert.Equal(ushort.MaxValue, SpawnValue.Compute(int.MaxValue, 1000, 1000, true, true, in _k));
        Assert.Equal((ushort)0, SpawnValue.Compute(-5, 1, 1, false, false, in _k));
        // district / shift below 1 behave as 1
        Assert.Equal(SpawnValue.Compute(8, 1, 1, false, false, in _k), SpawnValue.Compute(8, -3, 0, false, false, in _k));
    }

    private static long RoundHalfUp(long numerator, long denominator) => (numerator + (denominator / 2)) / denominator;

    [Fact]
    [Trait("Id", "M-F1-1")]
    public void Default_knobs_match_the_BuiltInContent_balance_defaults()
    {
        MailBalanceDefaults();
    }

    private static void MailBalanceDefaults()
    {
        var mail = BuiltInContent.CreateM1a().Mail;
        MailValueKnobs d = MailValueKnobs.Default;
        Assert.Equal(mail.DistanceStepPm, d.DistanceStepPm);
        Assert.Equal(mail.ShiftStepPm, d.ShiftStepPm);
        Assert.Equal(d, MailValueKnobs.From(mail, d.CursedValueMultPm, d.FoundValueMultPm));
    }
}
