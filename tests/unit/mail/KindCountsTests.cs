using PerformativeMail.Sim.MailDestinations;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Covers mail-destinations story 011 AC-2 and AC-3 (GDD M-F3-1, M-F3-2, M-R1-1b fallback fold; F3; TR-mail-001/002):
/// the deterministic, RNG-free per-kind counts on exact rationals. Deterministic, no IO.
/// </summary>
public class KindCountsTests
{
    private const int Postcard = 0, Letter = 1, Small = 2, Medium = 3, Large = 4, Cargo = 5;

    private static readonly long[] _baseValues = { 4, 8, 30, 70, 160, 600 };

    /// <summary>The GDD spec-03 mixes, shifts 1 to 5, in the order postcard/letter/small/medium/large/cargo (permille).</summary>
    private static readonly int[][] _mixes =
    {
        new[] { 0, 600, 300, 100, 0, 0 },
        new[] { 100, 400, 300, 150, 50, 0 },
        new[] { 100, 300, 250, 200, 100, 50 },
        new[] { 100, 250, 200, 200, 150, 100 },
        new[] { 100, 200, 200, 200, 150, 150 },
    };

    private static KindShare[] Shares(int shift, bool dockUnlocked)
    {
        int[] mix = _mixes[shift - 1];
        var shares = new KindShare[6];
        for (int i = 0; i < 6; i++)
        {
            bool cargo = i == Cargo;
            shares[i] = new KindShare(_baseValues[i], mix[i], cargo ? Large : -1, !cargo || dockUnlocked);
        }

        return shares;
    }

    private static int[] Fx50Districts()
    {
        var d = new List<int>();
        int[] per = { 10, 8, 10, 10, 12 };
        for (int district = 1; district <= 5; district++)
        {
            for (int i = 0; i < per[district - 1]; i++)
            {
                d.Add(district);
            }
        }

        return d.ToArray();
    }

    private static int[] Fx10District1() => Enumerable.Repeat(1, 10).ToArray();

    private static int Run(long v, KindShare[] shares, int[] districts, int shift, int[] n, Int128[]? r = null)
    {
        MailValueKnobs k = MailValueKnobs.Default;
        MeanDist md = MeanDist.Create(districts, k.DistanceStepPm);
        return KindCounts.Compute(v, shares, in md, in k, shift, n, r ?? Span<Int128>.Empty);
    }

    [Fact]
    [Trait("Id", "M-F3-1")]
    public void Solo_shift1_960_on_ten_district1_houses_gives_1_10_74_with_exact_carries()
    {
        var n = new int[6];
        var r = new Int128[6];
        int total = Run(960, Shares(1, false), Fx10District1(), 1, n, r);

        Assert.Equal(1, n[Medium]);
        Assert.Equal(10, n[Small]);
        Assert.Equal(74, n[Letter]);
        Assert.Equal(0, n[Postcard]);
        Assert.Equal(0, n[Large]);
        Assert.Equal(85, total);

        long denominator = 10L * 1_000_000;   // H x 10^6
        Assert.Equal((Int128)26 * denominator, r[Medium]);
        Assert.Equal((Int128)14 * denominator, r[Small]);
        Assert.Equal((Int128)(-2) * denominator, r[Letter]);
    }

    [Fact]
    [Trait("Id", "M-F3-1b")]
    public void Expected_value_is_962_and_the_residual_is_within_half_the_last_E()
    {
        var n = new int[6];
        var r = new Int128[6];
        Run(960, Shares(1, false), Fx10District1(), 1, n, r);

        long expected = (n[Medium] * 70L) + (n[Small] * 30L) + (n[Letter] * 8L) + (n[Postcard] * 4L) + (n[Large] * 160L);
        Assert.Equal(962, expected);

        MailValueKnobs k = MailValueKnobs.Default;
        MeanDist md = MeanDist.Create(Fx10District1(), k.DistanceStepPm);
        Int128 eLast = md.ExpectedNumerator(8, 1000);
        Assert.Equal((Int128)8 * md.Denominator, eLast);
        // |sum n_k E_k - V| = |r_final| <= E_last / 2, compared as 2|r| <= E.
        Assert.True(2 * Int128.Abs(r[Letter]) <= eLast);
        Assert.Equal((Int128)(960 - 962) * md.Denominator, r[Letter]);
    }

    [Fact]
    [Trait("Id", "M-F3-2")]
    public void FX_I50_shift5_24704_gives_the_exact_counts()
    {
        var n = new int[6];
        int total = Run(24_704, Shares(5, false), Fx50Districts(), 5, n);

        Assert.Equal(288, n[Postcard]);
        Assert.Equal(289, n[Letter]);
        Assert.Equal(77, n[Small]);
        Assert.Equal(32, n[Medium]);
        Assert.Equal(22, n[Large]);
        Assert.Equal(0, n[Cargo]);
        Assert.Equal(708, total);
    }

    [Fact]
    [Trait("Id", "M-F3-2b")]
    public void Solo_shift1_672_gives_1_7_49()
    {
        var n = new int[6];
        int total = Run(672, Shares(1, false), Fx10District1(), 1, n);

        Assert.Equal(1, n[Medium]);
        Assert.Equal(7, n[Small]);
        Assert.Equal(49, n[Letter]);
        Assert.Equal(57, total);
    }

    [Fact]
    [Trait("Id", "M-F3-2c")]
    public void A_kind_below_half_an_E_gets_zero_and_carries_everything_on()
    {
        // V_k 4, r -5, E 8: floorDiv(2 x (4 - 5) + 8, 16) = 0; r = -1.
        int n = KindCounts.Step(4, -5, 8, out Int128 rNew);
        Assert.Equal(0, n);
        Assert.Equal((Int128)(-1), rNew);

        // exactly at the half-way point rounds up
        Assert.Equal(1, KindCounts.Step(4, 0, 8, out Int128 rHalf));
        Assert.Equal((Int128)(-4), rHalf);

        // a negative carry never makes n negative
        Assert.Equal(0, KindCounts.Step(0, -100, 8, out Int128 rNeg));
        Assert.Equal((Int128)(-100), rNeg);
    }

    [Fact]
    [Trait("Id", "M-F3-2d")]
    public void Zero_budget_gives_zero_units_without_error()
    {
        var n = new int[6];
        var r = new Int128[6];
        int total = Run(0, Shares(3, true), Fx50Districts(), 3, n, r);

        Assert.Equal(0, total);
        Assert.All(n, x => Assert.Equal(0, x));
        Assert.All(r, x => Assert.Equal(Int128.Zero, x));
    }

    [Fact]
    [Trait("Id", "M-F3-2e")]
    public void Zero_unlocked_houses_is_a_result_code_not_an_exception()
    {
        var n = new int[6];
        n[0] = 99;
        int result = Run(960, Shares(1, false), Array.Empty<int>(), 1, n);

        Assert.Equal(KindCountsStatus.ZeroHouses, result);
        Assert.True(result < 0);
        Assert.All(n, x => Assert.Equal(0, x));
    }

    [Fact]
    [Trait("Id", "M-F3-2f")]
    public void Counts_do_not_depend_on_cursed_share_because_F3_has_no_cursed_term()
    {
        // F3's inputs (V, shares, houses, shift) carry no Cursed term, so cursedShare 0 vs 100 cannot change the counts;
        // the knobs' CursedValueMultPm is an F1 factor only and must not leak into F3.
        var plain = new int[6];
        var cursedKnobs = new int[6];
        MeanDist md = MeanDist.Create(Fx50Districts(), 250);
        MailValueKnobs a = MailValueKnobs.Default;
        MailValueKnobs b = a with { CursedValueMultPm = 9999 };
        int na = KindCounts.Compute(24_704, Shares(5, false), in md, in a, 5, plain);
        int nb = KindCounts.Compute(24_704, Shares(5, false), in md, in b, 5, cursedKnobs);

        Assert.Equal(na, nb);
        Assert.Equal(plain, cursedKnobs);
    }

    [Fact]
    [Trait("Id", "M-R1-1b")]
    public void Locked_dock_folds_the_cargo_share_into_large_and_an_unlocked_dock_moves_nothing()
    {
        int[] districts = Fx50Districts();
        var folded = new int[6];
        Run(24_704, Shares(5, false), districts, 5, folded);

        // Folded equals "cargo share added to Large" authored directly.
        KindShare[] direct = Shares(5, false);
        direct[Large] = direct[Large] with { SharePm = 300 };
        direct[Cargo] = direct[Cargo] with { SharePm = 0 };
        var expected = new int[6];
        Run(24_704, direct, districts, 5, expected);
        Assert.Equal(expected, folded);
        Assert.Equal(0, folded[Cargo]);

        // With the dock unlocked cargo keeps its own share and Large drops to 150 permille.
        var open = new int[6];
        Run(24_704, Shares(5, true), districts, 5, open);
        Assert.True(open[Cargo] > 0);
        Assert.True(open[Large] < folded[Large]);

        // "Nothing moves": an accepting cargo's FallbackIndex is irrelevant.
        KindShare[] openNoFallback = Shares(5, true);
        openNoFallback[Cargo] = openNoFallback[Cargo] with { FallbackIndex = -1 };
        var openNone = new int[6];
        Run(24_704, openNoFallback, districts, 5, openNone);
        Assert.Equal(open, openNone);
    }

    [Fact]
    [Trait("Id", "M-F3-2")]
    public void A_chain_of_fallbacks_is_followed_and_a_dead_end_drops_the_share()
    {
        // kind 0 (accepted) <- kind 1 (not accepted) <- kind 2 (not accepted); kind 3 has no fallback.
        var shares = new[]
        {
            new KindShare(10, 100, -1, true),
            new KindShare(20, 100, 0, false),
            new KindShare(30, 100, 1, false),
            new KindShare(40, 100, -1, false),
        };
        var n = new int[4];
        int total = Run(1000, shares, Fx10District1(), 1, n);

        Assert.Equal(300 / 10, n[0]);   // 100 + 100 + 100 permille of 1000 cents at E = 10
        Assert.Equal(30, total);
        Assert.Equal(0, n[1] + n[2] + n[3]);
    }

    [Fact]
    [Trait("Id", "M-F3-2")]
    public void Invalid_inputs_return_codes()
    {
        var n = new int[6];
        MailValueKnobs k = MailValueKnobs.Default;
        MeanDist md = MeanDist.Create(Fx10District1(), 250);
        Assert.Equal(KindCountsStatus.InvalidInput, KindCounts.Compute(-1, Shares(1, false), in md, in k, 1, n));
        Assert.Equal(KindCountsStatus.OutputTooSmall, KindCounts.Compute(10, Shares(1, false), in md, in k, 1, new int[2]));
        var bad = new[] { new KindShare(8, 1000, 7, false) };
        Assert.Equal(KindCountsStatus.InvalidInput, KindCounts.Compute(10, bad, in md, in k, 1, new int[1]));
        var zeroValue = new[] { new KindShare(0, 1000, -1, true) };
        Assert.Equal(KindCountsStatus.InvalidInput, KindCounts.Compute(10, zeroValue, in md, in k, 1, new int[1]));
        var hugeBase = new[] { new KindShare(KindCounts.MaxBaseValue + 1, 1000, -1, true) };
        Assert.Equal(KindCountsStatus.InvalidInput, KindCounts.Compute(10, hugeBase, in md, in k, 1, new int[1]));
        Assert.Equal(KindCountsStatus.TooManyKinds, KindCounts.Compute(10, new KindShare[33], in md, in k, 1, new int[33]));
    }

    [Fact]
    [Trait("Id", "M-F3-1b")]
    public void Residual_bound_holds_across_shifts_and_budgets()
    {
        int[] districts = Fx50Districts();
        MailValueKnobs k = MailValueKnobs.Default;
        MeanDist md = MeanDist.Create(districts, k.DistanceStepPm);
        for (int shift = 1; shift <= 5; shift++)
        {
            foreach (long v in new long[] { 672, 960, 6400, 24_704, 28_410 })
            {
                var n = new int[6];
                var r = new Int128[6];
                KindShare[] shares = Shares(shift, false);
                Assert.True(KindCounts.Compute(v, shares, in md, in k, shift, n, r) >= 0);

                // Sum n_k E_k vs the budget actually allocated (sum of exact V_k): |difference| <= E of the last processed kind / 2.
                long allocated = 0;
                Int128 sumNE = 0;
                long shiftPm = 1000L + (100L * (shift - 1));
                Int128 eLast = 0;
                long lastBase = long.MaxValue;
                for (int i = 0; i < 6; i++)
                {
                    if (i == Cargo)
                    {
                        continue;
                    }

                    int share = i == Large ? _mixes[shift - 1][Large] + _mixes[shift - 1][Cargo] : _mixes[shift - 1][i];
                    allocated += v * share;
                    Int128 e = md.ExpectedNumerator(_baseValues[i], shiftPm);
                    sumNE += n[i] * e;
                    if (share > 0 && _baseValues[i] < lastBase)
                    {
                        lastBase = _baseValues[i];
                        eLast = e;
                    }
                }

                Int128 diff = Int128.Abs(((Int128)allocated * md.ShareScale) - sumNE);
                Assert.True(2 * diff <= eLast, $"shift {shift} V {v}");
            }
        }
    }

    [Fact]
    [Trait("Id", "M-F3-2")]
    public void Malformed_fallback_chain_returns_InvalidInput_without_throwing()
    {
        // kind 0 non-accepting -> 1, kind 1 non-accepting -> 99 (out of range): the walk must not read it.
        var shares = new[]
        {
            new KindShare(10, 500, 1, false),
            new KindShare(20, 500, 99, false),
        };
        var n = new int[2];
        n[0] = 7;
        int result = Run(1000, shares, Fx10District1(), 1, n);

        Assert.Equal(KindCountsStatus.InvalidInput, result);
        Assert.All(n, x => Assert.Equal(0, x));

        var negative = new[] { new KindShare(10, 500, -2, true) };
        Assert.Equal(KindCountsStatus.InvalidInput, Run(1000, negative, Fx10District1(), 1, new int[1]));
    }

    [Fact]
    [Trait("Id", "M-F3-2")]
    public void Self_and_cyclic_fallbacks_end_at_the_hop_bound_and_drop_the_share()
    {
        var self = new[] { new KindShare(10, 1000, 0, false) };
        var nSelf = new int[1];
        Assert.Equal(0, Run(1000, self, Fx10District1(), 1, nSelf));

        var cycle = new[]
        {
            new KindShare(10, 400, 1, false),
            new KindShare(20, 300, 2, false),
            new KindShare(30, 300, 0, false),
            new KindShare(5, 0, -1, true),
        };
        var nCycle = new int[4];
        Assert.Equal(0, Run(1000, cycle, Fx10District1(), 1, nCycle));
        Assert.All(nCycle, x => Assert.Equal(0, x));
    }

    [Fact]
    [Trait("Id", "M-F3-2")]
    public void Equal_base_values_are_processed_in_index_order()
    {
        // E = 10 (H = 1, district 1, shift 1); V_k = 5 each. Index order: kind 0 rounds up (carry -5), kind 1 rounds to 0.
        var shares = new[] { new KindShare(10, 50, -1, true), new KindShare(10, 50, -1, true) };
        var n = new int[2];
        Assert.Equal(1, Run(100, shares, new[] { 1 }, 1, n));
        Assert.Equal(new[] { 1, 0 }, n);
    }

    [Fact]
    [Trait("Id", "M-F3-2")]
    public void A_nonzero_share_that_rounds_to_zero_carries_into_the_next_kind()
    {
        // GDD edge: Medium with V_k + r < 35 gets none and passes its value on.
        // Large 100: floor((2x100 + 160) / 320) = 1, r = -60. Medium 80 - 60 = 20 < 35 -> 0, r = 20. Small 100 + 20 = 120 -> 4.
        var shares = new[]
        {
            new KindShare(30, 100, -1, true),
            new KindShare(70, 80, -1, true),
            new KindShare(160, 100, -1, true),
        };
        var n = new int[3];
        var r = new Int128[3];
        Assert.Equal(5, Run(1000, shares, new[] { 1 }, 1, n, r));
        Assert.Equal(new[] { 4, 0, 1 }, n);
        Assert.Equal((Int128)(-60) * 1_000_000, r[2]);
        Assert.Equal((Int128)20 * 1_000_000, r[1]);

        // Without the Medium share the Small kind alone would round down to 3.
        var alone = new[] { new KindShare(30, 100, -1, true) };
        var nAlone = new int[1];
        Run(1000, alone, new[] { 1 }, 1, nAlone);
        Assert.Equal(3, nAlone[0]);
    }

    [Fact]
    [Trait("Id", "M-F3-2")]
    public void Validation_branches_return_codes_and_low_shift_and_district_are_treated_as_one()
    {
        MailValueKnobs k = MailValueKnobs.Default;
        MeanDist md = MeanDist.Create(Fx10District1(), 250);
        KindShare[] ok = Shares(1, false);
        Assert.Equal(KindCountsStatus.InvalidInput, KindCounts.Compute(KindCounts.MaxV + 1, ok, in md, in k, 1, new int[6]));
        Assert.True(KindCounts.Compute(KindCounts.MaxV, ok, in md, in k, 1, new int[6]) > 0);

        KindShare[] negShare = Shares(1, false);
        negShare[Letter] = negShare[Letter] with { SharePm = -1 };
        Assert.Equal(KindCountsStatus.InvalidInput, KindCounts.Compute(100, negShare, in md, in k, 1, new int[6]));
        KindShare[] bigShare = Shares(1, false);
        bigShare[Letter] = bigShare[Letter] with { SharePm = 1001 };
        Assert.Equal(KindCountsStatus.InvalidInput, KindCounts.Compute(100, bigShare, in md, in k, 1, new int[6]));

        MeanDist crowd = MeanDist.Create(new int[MeanDist.MaxHouses + 1], 250);
        Assert.False(crowd.IsWithinLimit);
        Assert.Equal(KindCountsStatus.InvalidInput, KindCounts.Compute(100, ok, in crowd, in k, 1, new int[6]));
        MeanDist atLimit = MeanDist.Create(new int[MeanDist.MaxHouses], 250);
        Assert.True(atLimit.IsWithinLimit);
        Assert.True(KindCounts.Compute(100, ok, in atLimit, in k, 1, new int[6]) >= 0);

        var low = new int[6];
        var one = new int[6];
        Assert.Equal(KindCounts.Compute(960, ok, in md, in k, 1, one), KindCounts.Compute(960, ok, in md, in k, -4, low));
        Assert.Equal(one, low);

        // district below 1 contributes nothing; a huge district is clamped, never overflows.
        MeanDist lowDistrict = MeanDist.Create(new[] { -5, 0, 1 }, 250);
        Assert.Equal(0L, lowDistrict.DistrictOffsetSum);
        MeanDist wild = MeanDist.Create(new[] { int.MaxValue, int.MaxValue }, int.MaxValue);
        Assert.Equal(2L * (MeanDist.MaxDistrict - 1), wild.DistrictOffsetSum);
        Assert.True(wild.ExpectedNumerator(KindCounts.MaxBaseValue, 1000) > 0);
    }

    [Fact]
    [Trait("Id", "M-F3-1b")]
    public void Extreme_bounds_do_not_overflow_and_the_residual_bound_holds()
    {
        // V = MaxV, H = 10,000, 32 kinds with distinct descending base values; the last (cheapest) kind absorbs the carry.
        var shares = new KindShare[KindCounts.MaxKinds];
        for (int i = 0; i < shares.Length; i++)
        {
            shares[i] = new KindShare(200 - (5 * i), 31, -1, true);
        }

        var districts = new int[MeanDist.MaxHouses];
        for (int i = 0; i < districts.Length; i++)
        {
            districts[i] = (i % 5) + 1;
        }

        MailValueKnobs k = MailValueKnobs.Default;
        MeanDist md = MeanDist.Create(districts, k.DistanceStepPm);
        var n = new int[shares.Length];
        var r = new Int128[shares.Length];
        int total = KindCounts.Compute(KindCounts.MaxV, shares, in md, in k, 5, n, r);

        Assert.True(total > 0);
        Int128 sumNE = 0;
        for (int i = 0; i < shares.Length; i++)
        {
            sumNE += n[i] * md.ExpectedNumerator(shares[i].BaseValueCents, 1400);
        }

        Int128 allocated = (Int128)KindCounts.MaxV * 31 * shares.Length * md.ShareScale;
        Int128 eLast = md.ExpectedNumerator(shares[^1].BaseValueCents, 1400);
        Assert.True(2 * Int128.Abs(allocated - sumNE) <= eLast);
        Assert.Equal(allocated - sumNE, r[^1]);
    }
}
