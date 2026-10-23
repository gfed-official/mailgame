using System.Numerics;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 007 (TR-content-015; GDD CDD-STAT-3 last clause, CDD-STAT-10, CDD-STAT-11 formula half,
/// CDD-STAT-14; F1 <see cref="StatResolve"/>, F5 <see cref="WorstCase"/>). The F1 Example lines are normative vectors.
/// A <see cref="BigInteger"/> reference implementation of F1 lives here only (System.Numerics stays out of the Sim).
/// The determinism property uses a fixed, logged PCG32 seed: no time, no System.Random.
/// </summary>
public class StatResolveTests
{
    private const ulong PropertySeed = 0x5EEDF00D_0000_0007UL;
    private const ulong PropertyStream = 54UL;

    // ------------------------------------------------------------ BigInteger reference (test project only)

    private static BigInteger RefRound(StatRounding rounding, BigInteger n, BigInteger d)
    {
        switch (rounding)
        {
            case StatRounding.Floor:
                return RefFloor(n, d);
            case StatRounding.Ceil:
                return -RefFloor(-n, d);
            default:
                BigInteger magnitude = BigInteger.Divide((2 * BigInteger.Abs(n)) + d, 2 * d);
                return n.Sign < 0 ? -magnitude : magnitude;
        }
    }

    private static BigInteger RefFloor(BigInteger n, BigInteger d)
    {
        BigInteger q = BigInteger.DivRem(n, d, out BigInteger r);
        return r.Sign < 0 ? q - 1 : q;
    }

    /// <summary>F1 straight from the GDD text. Returns (unclamped X, clamped value).</summary>
    private static (BigInteger Unclamped, int Value) RefResolve(
        StatRounding rounding, SetResolve setResolve, int min, int max, int baseValue,
        IReadOnlyList<int> adds, IReadOnlyList<int> muls, IReadOnlyList<int> sets, int transientAdd)
    {
        BigInteger x;
        if (sets.Count > 0)
        {
            BigInteger pick = sets[0];
            foreach (int s in sets)
            {
                pick = setResolve == SetResolve.MaxWins ? BigInteger.Max(pick, s) : BigInteger.Min(pick, s);
            }

            x = pick;
        }
        else
        {
            BigInteger p = BigInteger.One;
            foreach (int m in muls)
            {
                p *= m;
            }

            BigInteger sumA = transientAdd;
            foreach (int a in adds)
            {
                sumA += a;
            }

            BigInteger d = BigInteger.Pow(1000, muls.Count);
            x = RefRound(rounding, (baseValue * p) + (d * sumA), d);
        }

        BigInteger clamped = BigInteger.Min(BigInteger.Max(x, min), max);
        return (x, (int)clamped);
    }

    private static long RefSaturate(BigInteger v) =>
        v > long.MaxValue ? long.MaxValue : v < long.MinValue ? long.MinValue : (long)v;

    private static StatResolution ResolveRow(Stat stat, int min, int max, int baseValue, int[] adds, int[] muls, int[] sets, int transient)
    {
        StatDef def = StatDefs.Get(stat);
        return StatResolve.Resolve(def, new StatBounds(stat, min, max), baseValue, adds, muls, sets, transient);
    }

    // ------------------------------------------------------------ AC-1: F1 Example table

    [Theory]
    [Trait("Id", "CDD-STAT-10")]
    [Trait("Id", "CDD-STAT-3")]
    //          stat                           min   max    base   adds           muls                    sets            transient  expected
    [InlineData(Stat.InventoryRows,            1,    24,    10,    new int[0],    new[] { 1500 },         new int[0],     0,         15)]    // depot Ceil
    [InlineData(Stat.InventoryRows,            1,    24,    4,     new int[0],    new[] { 1500 },         new int[0],     0,         6)]     // chest base 4
    [InlineData(Stat.InventoryRows,            1,    24,    5,     new int[0],    new[] { 1500 },         new int[0],     0,         8)]     // chest base 5 (7 500 -> Ceil)
    [InlineData(Stat.InventoryRows,            1,    24,    10,    new[] { 1 },   new[] { 1500 },         new int[0],     0,         16)]    // Adds are not multiplied
    [InlineData(Stat.MoveSpeedAddPm,           0,    400,   0,     new[] { 360 }, new int[0],             new int[0],     300,       400)]   // 660 clamped
    [InlineData(Stat.MoveSpeedAddPm,           0,    400,   0,     new[] { 360 }, new int[0],             new int[0],     0,         360)]
    [InlineData(Stat.LateValueRatioPm,         0,    1000,  500,   new int[0],    new int[0],             new[] { 1000 }, 0,         1000)]  // Set max-wins
    [InlineData(Stat.LateValueRatioPm,         0,    1000,  500,   new int[0],    new int[0],             new int[0],     0,         500)]
    [InlineData(Stat.LateValueRatioPm,         0,    1000,  500,   new int[0],    new int[0],             new[] { 700, 900 }, 0,     900)]   // two max-wins Sets
    [InlineData(Stat.LateValueRatioPm,         0,    1000,  500,   new[] { 40 },  new[] { 2000 },         new[] { 700 },  25,        700)]   // Set discards Add, Mul, transient
    [InlineData(Stat.LateValueRatioPm,         0,    1000,  500,   new int[0],    new int[0],             new[] { 300 },  0,         300)]   // Set below base lowers the stat
    [InlineData(Stat.RespawnMs,                1000, 10000, 500,   new int[0],    new int[0],             new int[0],     0,         1000)]  // base below min, no sources
    [InlineData(Stat.DeliverInsertPm,          0,    750,   0,     new[] { 500 }, new int[0],             new int[0],     0,         500)]
    [InlineData(Stat.MailMaxStack,             1,    255,   20,    new int[0],    new[] { 2000 },         new int[0],     0,         40)]    // letter Floor
    [InlineData(Stat.WeightPenaltyPm,          0,    1000,  1000,  new int[0],    new[] { 900, 700 },     new int[0],     0,         630)]   // QuotaMultPm-shaped HalfUp
    [InlineData(Stat.RespawnMs,                1000, 10000, 10000, new int[0],    new int[0],             new[] { 500 },  0,         1000)]  // Set clamped up
    [InlineData(Stat.RespawnMs,                1000, 10000, 10000, new int[0],    new int[0],             new[] { 3000 }, 0,         3000)]
    [InlineData(Stat.RespawnMs,                1000, 10000, 10000, new int[0],    new int[0],             new[] { 3000, 2000 }, 0,   2000)]  // min-wins
    [InlineData(Stat.RespawnMs,                1000, 10000, 10000, new int[0],    new int[0],             new[] { 12000 }, 0,        10000)] // Set above base clamps to base
    public void Resolve_F1ExampleLines_ReturnListedValue(
        Stat stat, int min, int max, int baseValue, int[] adds, int[] muls, int[] sets, int transient, int expected)
    {
        // Act
        StatResolution r = ResolveRow(stat, min, max, baseValue, adds, muls, sets, transient);

        // Assert
        Assert.Equal(expected, r.Value);
        StatDef def = StatDefs.Get(stat);
        (BigInteger _, int refValue) = RefResolve(def.Rounding, def.SetResolve, min, max, baseValue, adds, muls, sets, transient);
        Assert.Equal(refValue, r.Value);
    }

    [Theory]
    [Trait("Id", "CDD-STAT-10")]
    [InlineData(StatRounding.HalfUp, -1500L, 1000L, -2L)]   // sign: -((3000 + 1000) / 2000)
    [InlineData(StatRounding.HalfUp, 1500L, 1000L, 2L)]
    [InlineData(StatRounding.HalfUp, -1499L, 1000L, -1L)]
    [InlineData(StatRounding.HalfUp, 1499L, 1000L, 1L)]
    [InlineData(StatRounding.Floor, -1L, 1000L, -1L)]
    [InlineData(StatRounding.Ceil, -1L, 1000L, 0L)]
    [InlineData(StatRounding.Floor, 1999L, 1000L, 1L)]
    [InlineData(StatRounding.Ceil, 1001L, 1000L, 2L)]
    [InlineData(StatRounding.Ceil, 15000L, 1000L, 15L)]
    [InlineData(StatRounding.Floor, 0L, 1L, 0L)]
    public void Round_SignAndTrueFloorCeil_MatchGddVectors(StatRounding rounding, long n, long d, long expected)
    {
        // Act
        Int128 actual = StatResolve.Round(rounding, n, d);

        // Assert
        Assert.Equal((Int128)expected, actual);
    }

    [Theory]
    [Trait("Id", "CDD-STAT-10")]
    [InlineData(StatRounding.HalfUp, -3, 500, 0, -2)]   // N = -3 * 500 = -1500, D = 1000
    [InlineData(StatRounding.Floor, 1, 999, -1, -1)]    // N = 999 - 1000 = -1
    [InlineData(StatRounding.Ceil, 1, 999, -1, 0)]
    public void Resolve_NegativeNumerator_RoundsOnceThroughResolvePath(StatRounding rounding, int baseValue, int mulPermille, int add, int expected)
    {
        // Act
        StatResolution r = StatResolve.Resolve(rounding, SetResolve.NotApplicable, -10, 10, baseValue, [add], [mulPermille], [], 0);

        // Assert
        Assert.Equal(expected, r.Value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_BaseBelowMinWithNoSources_ClampsUpAndReportsMin()
    {
        // Act
        StatResolution r = ResolveRow(Stat.RespawnMs, 1000, 10000, 500, [], [], [], 0);

        // Assert
        Assert.Equal(1000, r.Value);
        Assert.Equal(StatClamp.Min, r.ClampedAt);
        Assert.Equal(500L, r.Unclamped);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_MoveSpeedWithTransient_ReportsExplainComponents()
    {
        // Act
        StatResolution r = ResolveRow(Stat.MoveSpeedAddPm, 0, 400, 0, [120, 120, 120], [], [], 300);

        // Assert
        Assert.Equal(660, r.SumA);
        Assert.Equal(660L, r.Unclamped);
        Assert.Equal(StatClamp.Max, r.ClampedAt);
        Assert.Equal(400, r.Value);
        Assert.Equal(0, r.K);
        Assert.Equal(Int128.Zero, r.BaseTimesP);
        Assert.False(r.HasSet);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_DepotWithMulAndAdd_ReportsProductComponentsAndNoClamp()
    {
        // Act
        StatResolution r = ResolveRow(Stat.InventoryRows, 1, 24, 10, [1], [1500], [], 0);

        // Assert
        Assert.Equal((Int128)15000, r.BaseTimesP);
        Assert.Equal(1, r.K);
        Assert.Equal(1, r.SumA);
        Assert.Equal(16L, r.Unclamped);
        Assert.Equal(StatClamp.None, r.ClampedAt);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_SetBelowMin_ReportsSetStateAndMinClamp()
    {
        // Act
        StatResolution r = ResolveRow(Stat.RespawnMs, 1000, 10000, 10000, [5], [1500], [500], 7);

        // Assert
        Assert.True(r.HasSet);
        Assert.Equal(500, r.SetValue);
        Assert.Equal(500L, r.Unclamped);
        Assert.Equal(StatClamp.Min, r.ClampedAt);
        Assert.Equal(1000, r.Value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_NoSources_ReturnsBaseWithEmptyProduct()
    {
        // Act
        StatResolution r = ResolveRow(Stat.InventoryRows, 1, 24, 10, [], [], [], 0);

        // Assert (P = 1, k = 0, D = 1)
        Assert.Equal(10, r.Value);
        Assert.Equal(0, r.K);
        Assert.Equal((Int128)10, r.BaseTimesP);
        Assert.Equal(0L, r.SumA);
        Assert.Equal(StatClamp.None, r.ClampedAt);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_ZeroMul_CollapsesProductToZero()
    {
        // Act
        StatResolution r = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 0, 1000, 800, [], [0, 5000], [], 0);

        // Assert
        Assert.Equal(0, r.Value);
        Assert.Equal(Int128.Zero, r.BaseTimesP);
    }

    // ------------------------------------------------------------ AC-2: extreme bound

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_ExtremeBound_UnclampedMatchesBigIntegerAndValueIsIntMax()
    {
        // Arrange: base 2^31-1, six Mul 10 000, 65 535 Adds of 2^31-1, synthetic row min 0, max int.MaxValue, HalfUp.
        var synthetic = new StatDef(
            Stat.DeliverInsertPm, StatUnit.Count, StatScope.Personal, QualifierKind.None, BaseSource.Constant(int.MaxValue),
            StatOps.Add | StatOps.Mul, SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Constant(int.MaxValue));
        int[] adds = new int[65_535];
        Array.Fill(adds, int.MaxValue);
        int[] muls = [10_000, 10_000, 10_000, 10_000, 10_000, 10_000];
        BigInteger sumA = new BigInteger(int.MaxValue) * 65_535;
        BigInteger expected = RefRound(StatRounding.HalfUp, (new BigInteger(int.MaxValue) * BigInteger.Pow(10_000, 6)) + (BigInteger.Pow(1000, 6) * sumA), BigInteger.Pow(1000, 6));

        // Act (a Debug build is checked: any overflow would throw)
        StatResolution r = StatResolve.Resolve(synthetic, new StatBounds(Stat.DeliverInsertPm, 0, int.MaxValue), int.MaxValue, adds, muls, [], 0);

        // Assert
        Assert.Equal(RefSaturate(expected), r.Unclamped);
        Assert.Equal(6, r.K);
        Assert.Equal((long)int.MaxValue * 65_535, r.SumA);
        Assert.Equal(int.MaxValue, r.Value);
        Assert.Equal(StatClamp.Max, r.ClampedAt);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_ExtremeNegativeBound_UnclampedMatchesBigIntegerAndClampsToMin()
    {
        // Arrange: the negative mirror, with a Mul product that is also extreme.
        int[] adds = new int[65_535];
        Array.Fill(adds, int.MinValue);
        int[] muls = [10_000, 10_000, 10_000, 10_000, 10_000, 10_000];
        (BigInteger unclamped, int value) = RefResolve(
            StatRounding.HalfUp, SetResolve.NotApplicable, int.MinValue, int.MaxValue, int.MinValue, adds, muls, [], 0);

        // Act
        StatResolution r = StatResolve.Resolve(
            StatRounding.HalfUp, SetResolve.NotApplicable, int.MinValue, int.MaxValue, int.MinValue, adds, muls, [], 0);

        // Assert
        Assert.Equal(RefSaturate(unclamped), r.Unclamped);
        Assert.Equal(value, r.Value);
        Assert.Equal(int.MinValue, r.Value);
        Assert.Equal(StatClamp.Min, r.ClampedAt);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Saturate_OutsideInt64_Clamps()
    {
        // Saturate is defensive: within the F1 contract |X| is at most about 2^51, so this branch is unreachable from
        // Resolve. It is exercised directly to pin the saturation behaviour Explain relies on.
        Assert.Equal(long.MaxValue, StatResolve.Saturate(Int128.MaxValue));
        Assert.Equal(long.MinValue, StatResolve.Saturate(Int128.MinValue));
        Assert.Equal(-5L, StatResolve.Saturate(-5));
    }

    [Theory]
    [Trait("Id", "CDD-STAT-10")]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Resolve_InContractCorner_MatchesBigIntegerReference(int baseValue)
    {
        // Arrange: base at either int32 extreme, six Muls of 10 000, 65 535 Adds of int.MaxValue.
        int[] adds = new int[65_535];
        Array.Fill(adds, int.MaxValue);
        int[] muls = [10_000, 10_000, 10_000, 10_000, 10_000, 10_000];
        (BigInteger unclamped, int value) = RefResolve(
            StatRounding.HalfUp, SetResolve.NotApplicable, int.MinValue, int.MaxValue, baseValue, adds, muls, [], int.MaxValue);

        // Act
        StatResolution r = StatResolve.Resolve(
            StatRounding.HalfUp, SetResolve.NotApplicable, int.MinValue, int.MaxValue, baseValue, adds, muls, [], int.MaxValue);

        // Assert
        Assert.Equal(RefSaturate(unclamped), r.Unclamped);
        Assert.Equal(value, r.Value);
    }

    // ------------------------------------------------------------ AC-3: determinism property

    [Fact]
    [Trait("Id", "CDD-STAT-14")]
    public void Resolve_ThousandRandomSets_EveryPushOrderEqualsBigIntegerReference()
    {
        // Arrange (seed fixed and logged; the seed is also in every failure message)
        TestContext.Current.TestOutputHelper?.WriteLine($"CDD-STAT-14 PCG32 seed=0x{PropertySeed:X16} stream={PropertyStream}");
        var rng = new Pcg32(PropertySeed, PropertyStream);

        for (int caseIndex = 0; caseIndex < 1000; caseIndex++)
        {
            StatRounding rounding = (StatRounding)(1 + (int)(rng.Next32() % 3));
            SetResolve setResolve = rng.Next32() % 2 == 0 ? SetResolve.MinWins : SetResolve.MaxWins;
            int baseValue = RandomInt(ref rng);
            int[] muls = RandomValues(ref rng, (int)(rng.Next32() % 7), 0, 10_000);
            int[] adds = RandomWideValues(ref rng, (int)(rng.Next32() % 40));
            int[] sets = rng.Next32() % 4 == 0 ? RandomWideValues(ref rng, 1 + (int)(rng.Next32() % 3)) : [];
            int transient = rng.Next32() % 3 == 0 ? (int)(rng.Next32() & 0x7FFFFFFF) : 0;
            int bound1 = RandomInt(ref rng);
            int bound2 = RandomInt(ref rng);
            int min = Math.Min(bound1, bound2);
            int max = Math.Max(bound1, bound2);
            string where = $"seed=0x{PropertySeed:X16} stream={PropertyStream} case={caseIndex}";
            (BigInteger refUnclamped, int refValue) = RefResolve(rounding, setResolve, min, max, baseValue, adds, muls, sets, transient);

            StatResolution first = StatResolve.Resolve(rounding, setResolve, min, max, baseValue, adds, muls, sets, transient);
            Assert.True(refValue == first.Value && RefSaturate(refUnclamped) == first.Unclamped, where + " (original order)");

            // Reversed, then several seeded shuffles of each list independently.
            Reverse(adds);
            Reverse(muls);
            Reverse(sets);
            StatResolution reversed = StatResolve.Resolve(rounding, setResolve, min, max, baseValue, adds, muls, sets, transient);
            Assert.True(first == reversed, where + " (reversed)");
            for (int shuffle = 0; shuffle < 4; shuffle++)
            {
                Shuffle(ref rng, adds);
                Shuffle(ref rng, muls);
                Shuffle(ref rng, sets);
                StatResolution shuffled = StatResolve.Resolve(rounding, setResolve, min, max, baseValue, adds, muls, sets, transient);
                Assert.True(first == shuffled, where + $" (shuffle {shuffle})");
            }
        }
    }

    private static int RandomInt(ref Pcg32 rng)
    {
        // A third of the draws are small values, the rest span the full int32 range.
        uint r = rng.Next32();
        return r % 3 == 0 ? (int)(rng.Next32() % 4001) - 2000 : (int)rng.Next32();
    }

    private static int[] RandomValues(ref Pcg32 rng, int count, int lo, int hi)
    {
        int[] values = new int[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = lo + (int)(rng.Next32() % (uint)(hi - lo + 1));
        }

        return values;
    }

    private static int[] RandomWideValues(ref Pcg32 rng, int count)
    {
        int[] values = new int[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = RandomInt(ref rng);
        }

        return values;
    }

    private static void Reverse(int[] values) => Array.Reverse(values);

    private static void Shuffle(ref Pcg32 rng, int[] values)
    {
        for (int i = values.Length - 1; i > 0; i--)
        {
            int j = (int)(rng.Next32() % (uint)(i + 1));
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    // ------------------------------------------------------------ AC-4: F5

    [Theory]
    [Trait("Id", "CDD-STAT-11")]
    [InlineData(40, 80L)]    // postcard
    [InlineData(20, 40L)]    // letter
    [InlineData(200, 400L)]  // hypothetical, errors in CV-GRD-01
    public void WorstCaseHigh_MaxStackWithBulkMailer_MatchesGddValues(int maxStack, long expected)
    {
        // Act
        WorstCaseResult r = WorstCase.High(StatRounding.Floor, maxStack, [], [2000], [], 0);

        // Assert
        Assert.Equal(WorstCaseStatus.Ok, r.Status);
        Assert.Equal(expected, r.Value);
        Assert.Equal(1, r.KWorst);
        Assert.Equal(1, r.KSelected);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCaseHigh_MoveSpeedLongLegsThreeStacksWithTransient_Is660()
    {
        // Act
        WorstCaseResult r = WorstCase.High(StatRounding.HalfUp, 0, [120, 120, 120], [], [], 300);

        // Assert
        Assert.Equal(WorstCaseStatus.Ok, r.Status);
        Assert.Equal(660L, r.Value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCaseLow_QuotaMultMuls900x700x450OnBase1000_Is284()
    {
        // Act
        WorstCaseResult r = WorstCase.Low(StatRounding.HalfUp, 1000, [], [900, 700, 450], []);

        // Assert (283.5 rounds half away from zero)
        Assert.Equal(WorstCaseStatus.Ok, r.Status);
        Assert.Equal(284L, r.Value);
        Assert.Equal(3, r.KSelected);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCase_SelectsOnlyRaisingOrLoweringUnits()
    {
        // Arrange: mixed units, base 100.
        int[] adds = [50, -30, 0];
        int[] muls = [2000, 500, 1000];
        int[] sets = [300, 20];

        // Act
        WorstCaseResult high = WorstCase.High(StatRounding.HalfUp, 100, adds, muls, sets, 10);
        WorstCaseResult low = WorstCase.Low(StatRounding.HalfUp, 100, adds, muls, sets);

        // Assert: high N = 100*2000 + 1000*(50+10) = 260000, D = 1000 -> 260; Set 300 raises -> 300.
        Assert.Equal(300L, high.Value);
        Assert.Equal(1, high.KSelected);
        Assert.Equal(3, high.KWorst);

        // low N = 100*500 + 1000*(-30) = 20000 -> 20; Set 20 does not lower below 20; Set 300 ignored.
        Assert.Equal(20L, low.Value);
        Assert.Equal(1, low.KSelected);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCaseLow_LoweringSetBelowRoundedValue_WinsTheMin()
    {
        // Act
        WorstCaseResult low = WorstCase.Low(StatRounding.HalfUp, 100, [], [900], [40, 95]);

        // Assert: R = 90; sets 40 and 95 are below base 100, 40 is the smallest.
        Assert.Equal(40L, low.Value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCaseHigh_SetBelowRoundedValue_IsIgnored()
    {
        // Act: base 100, Mul 2000 gives R = 200; Set 150 is above base but below R, so max(R, S) stays 200.
        WorstCaseResult r = WorstCase.High(StatRounding.HalfUp, 100, [], [2000], [150], 0);

        // Assert
        Assert.Equal(200L, r.Value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCase_NegativeBase_SetBetweenBestAndBaseCounts()
    {
        // Act: High: base -100, Mul 2000 -> R = -200 (below base); Set -150 lies between R and base and beats R.
        WorstCaseResult high = WorstCase.High(StatRounding.HalfUp, -100, [], [2000], [-150], 0);

        // Low: base -100, Mul 500 -> R = -50; Set -70 is below R and counts.
        WorstCaseResult low = WorstCase.Low(StatRounding.HalfUp, -100, [], [500], [-70]);

        // Assert
        Assert.Equal(-150L, high.Value);
        Assert.Equal(-70L, low.Value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCaseLow_RandomUnits_MatchesBigIntegerReferenceOfLoweringSubset()
    {
        // Arrange
        var rng = new Pcg32(PropertySeed ^ 0xF51, PropertyStream);
        for (int i = 0; i < 300; i++)
        {
            StatRounding rounding = (StatRounding)(1 + (int)(rng.Next32() % 3));
            int baseValue = (int)(rng.Next32() % 100_000);
            int[] muls = RandomValues(ref rng, (int)(rng.Next32() % 7), 0, 2000);
            int[] adds = RandomValues(ref rng, (int)(rng.Next32() % 8), -500, 500);
            int[] sets = RandomValues(ref rng, (int)(rng.Next32() % 3), 0, 100_000);
            int[] loweringMuls = muls.Where(m => m < 1000).ToArray();
            int[] loweringAdds = adds.Where(a => a < 0).ToArray();
            (BigInteger rounded, int _) = RefResolve(
                rounding, SetResolve.MaxWins, int.MinValue, int.MaxValue, baseValue, loweringAdds, loweringMuls, [], 0);
            BigInteger expected = rounded;
            foreach (int set in sets)
            {
                expected = BigInteger.Min(expected, set);
            }

            // Act
            WorstCaseResult r = WorstCase.Low(rounding, baseValue, adds, muls, sets);

            // Assert
            Assert.Equal(RefSaturate(expected), r.Value);
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCase_NoUnits_ReturnsRoundedBase()
    {
        // Act
        WorstCaseResult r = WorstCase.High(StatRounding.Floor, 40, [], [], [], 0);

        // Assert
        Assert.Equal(WorstCaseStatus.Ok, r.Status);
        Assert.Equal(40L, r.Value);
        Assert.Equal(0, r.KWorst);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCase_MoreThanSixMulUnits_ReportsTooManyFactorsAndSkipsValue()
    {
        // Arrange: seven Mul units (k_worst = 7), even ones that would not raise count toward k_worst.
        int[] muls = [1500, 1500, 1500, 1500, 1500, 1500, 900];

        // Act
        WorstCaseResult high = WorstCase.High(StatRounding.HalfUp, 10, [], muls, [], 0);
        WorstCaseResult low = WorstCase.Low(StatRounding.HalfUp, 10, [], muls, []);

        // Assert
        Assert.Equal(WorstCaseStatus.TooManyFactors, high.Status);
        Assert.Equal(0L, high.Value);
        Assert.Equal(7, high.KWorst);
        Assert.Equal(WorstCaseStatus.TooManyFactors, low.Status);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCase_ExactlySixMulUnits_IsAccepted()
    {
        // Act
        WorstCaseResult r = WorstCase.High(StatRounding.HalfUp, 1000, [], [10_000, 10_000, 10_000, 10_000, 10_000, 10_000], [], 0);

        // Assert: 1000 * 10^24 / 10^18 = 10^9.
        Assert.Equal(WorstCaseStatus.Ok, r.Status);
        Assert.Equal(1_000_000_000L, r.Value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-11")]
    public void WorstCaseHigh_RandomUnits_MatchesBigIntegerReferenceOfRaisingSubset()
    {
        // Arrange
        var rng = new Pcg32(PropertySeed ^ 0xF5, PropertyStream);
        for (int i = 0; i < 300; i++)
        {
            StatRounding rounding = (StatRounding)(1 + (int)(rng.Next32() % 3));
            int baseValue = (int)(rng.Next32() % 100_000);
            int[] muls = RandomValues(ref rng, (int)(rng.Next32() % 7), 0, 4000);
            int[] adds = RandomValues(ref rng, (int)(rng.Next32() % 8), -500, 500);
            int transient = (int)(rng.Next32() % 400);
            int[] raisingMuls = muls.Where(m => m > 1000).ToArray();
            int[] raisingAdds = adds.Where(a => a > 0).ToArray();
            (BigInteger expected, int _) = RefResolve(
                rounding, SetResolve.MaxWins, int.MinValue, int.MaxValue, baseValue, raisingAdds, raisingMuls, [], transient);

            // Act
            WorstCaseResult r = WorstCase.High(rounding, baseValue, adds, muls, [], transient);

            // Assert
            Assert.Equal(RefSaturate(expected), r.Value);
        }
    }

    // ------------------------------------------------------------ AC-5: k > 6 asserts

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_MoreThanSixMuls_TripsSimAssertWhenAssertsEnabled()
    {
        // Arrange: seven neutral Muls; the value is unaffected so a build without the assert is checkable.
        int[] muls = [1000, 1000, 1000, 1000, 1000, 1000, 1000];
        SimAssertException? thrown = null;
        StatResolution r = default;

        // Act
        try
        {
            r = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 0, 100, 5, [], muls, [], 0);
        }
        catch (SimAssertException e)
        {
            thrown = e;
        }

        // Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(thrown);
            Assert.Equal(ContentAssertCode.StatMulFactorsExceeded, thrown.Code);
        }
        else
        {
            Assert.Null(thrown);
            Assert.Equal(5, r.Value);
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_SetOnStatWithoutSetResolve_TripsSimAssertWhenAssertsEnabled()
    {
        // Arrange
        SimAssertException? thrown = null;
        StatResolution r = default;

        // Act
        try
        {
            r = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 0, 1000, 5, [], [], [300, 200], 0);
        }
        catch (SimAssertException e)
        {
            thrown = e;
        }

        // Assert (a build without the assert resolves as min-wins, deterministically)
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(thrown);
            Assert.Equal(ContentAssertCode.StatSetWithoutResolve, thrown.Code);
        }
        else
        {
            Assert.Null(thrown);
            Assert.Equal(200, r.Value);
        }
    }

    [Theory]
    [Trait("Id", "CDD-STAT-10")]
    [InlineData(10_001)]   // one above the 10 000 permille ceiling
    [InlineData(-1)]       // below zero
    public void Resolve_MulOutOfRange_TripsSimAssertWhenAssertsEnabled(int mul)
    {
        // Arrange
        SimAssertException? thrown = null;
        StatResolution r = default;

        // Act
        try
        {
            r = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, -1000, 1000, 5, [], [mul], [], 0);
        }
        catch (SimAssertException e)
        {
            thrown = e;
        }

        // Assert (a build without the assert computes the arithmetic as given: 5 * mul / 1000)
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(thrown);
            Assert.Equal(ContentAssertCode.StatMulOutOfRange, thrown.Code);
        }
        else
        {
            Assert.Null(thrown);
            Assert.Equal((int)RefRound(StatRounding.HalfUp, 5 * mul, 1000), r.Value);
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_MulAtBothRangeEnds_DoesNotAssert()
    {
        // Act
        StatResolution r = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 0, 100_000, 5, [], [0], [], 0);
        StatResolution r2 = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 0, 100_000, 5, [], [StatResolve.MaxMulPermille], [], 0);

        // Assert
        Assert.Equal(0, r.Value);
        Assert.Equal(50, r2.Value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_MoreThan65535Adds_TripsSimAssertWhenAssertsEnabled()
    {
        // Arrange
        int[] adds = new int[StatResolve.MaxModifiers + 1];
        Array.Fill(adds, 1);
        SimAssertException? thrown = null;
        StatResolution r = default;

        // Act
        try
        {
            r = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 0, 1_000_000, 0, adds, [], [], 0);
        }
        catch (SimAssertException e)
        {
            thrown = e;
        }

        // Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(thrown);
            Assert.Equal(ContentAssertCode.StatAddsTooMany, thrown.Code);
        }
        else
        {
            Assert.Null(thrown);
            Assert.Equal(65_536, r.Value);
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Resolve_MinAboveMax_TripsSimAssertWhenAssertsEnabled()
    {
        // Arrange
        SimAssertException? thrown = null;
        StatResolution r = default;

        // Act
        try
        {
            r = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 10, 5, 7, [], [], [], 0);
        }
        catch (SimAssertException e)
        {
            thrown = e;
        }

        // Assert (a build without the assert raises below-min values first: 7 < 10 gives min)
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(thrown);
            Assert.Equal(ContentAssertCode.StatBoundsInverted, thrown.Code);
        }
        else
        {
            Assert.Null(thrown);
            Assert.Equal(10, r.Value);
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void Round_UndefinedMode_TripsSimAssertWhenAssertsEnabledElseHalfUp()
    {
        // Arrange
        SimAssertException? thrown = null;
        Int128 result = 0;

        // Act
        try
        {
            result = StatResolve.Round((StatRounding)99, 1500, 1000);
        }
        catch (SimAssertException e)
        {
            thrown = e;
        }

        // Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.NotNull(thrown);
            Assert.Equal(ContentAssertCode.StatRoundingUnknown, thrown.Code);
        }
        else
        {
            Assert.Null(thrown);
            Assert.Equal((Int128)2, result);
        }
    }
}

/// <summary>Zero-allocation guardrail for F1 and F5, in the non-parallel collection (TD-026).</summary>
[Collection(NonParallelCollection.Name)]
public class StatResolveAllocationTests
{
    [Fact]
    [Trait("Id", "CDD-STAT-10")]
    public void ResolveAndWorstCase_WarmedUp_AllocateNothing()
    {
        // Arrange
        int[] adds = [120, 120, 120, -5];
        int[] muls = [1500, 900, 700];
        int[] sets = [500, 700];
        StatDef def = StatDefs.Get(Stat.LateValueRatioPm);
        var bounds = new StatBounds(def.Stat, 0, 1000);
        long sink = 0;

        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(StatResolve).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("200 x Resolve (no Set, Set) + WorstCase High/Low", () =>
        {
            for (int i = 0; i < 200; i++)
            {
                StatResolution a = StatResolve.Resolve(StatRounding.HalfUp, SetResolve.NotApplicable, 0, 100_000, 1000, adds, muls, [], 300);
                StatResolution b = StatResolve.Resolve(def, bounds, 500, adds, muls, sets, 0);
                WorstCaseResult c = WorstCase.High(StatRounding.Ceil, 1000, adds, muls, sets, 300);
                WorstCaseResult d = WorstCase.Low(StatRounding.Floor, 1000, adds, muls, sets);
                sink += a.Value + b.Value + c.Value + d.Value;
            }
        });
        Assert.NotEqual(0L, sink);
    }
}
