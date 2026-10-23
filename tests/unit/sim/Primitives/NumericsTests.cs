using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Primitives;

/// <summary>
/// Covers the numerics toolkit (TR-sim-031, TR-sim-034; GDD SC-F7-1; ADR-0006 section 2):
/// <see cref="Cents"/>, <see cref="Fx16"/>, <see cref="Fix"/> and <see cref="SimCounterSink"/>.
/// Every ADR-0006 section 2 N-vector row is asserted verbatim as a Theory row. Rows must pass
/// identically in Debug (checked) and Release on net10.0 and net8.0; only the
/// <see cref="Cents"/> overflow branches differ, chosen by
/// <see cref="TestBuildExpectation.SimAssertsExpected"/>. The test assembly never defines
/// SIM_ASSERT and never calls SimAssert. All numerics here are integer-only.
/// <para>
/// <see cref="SimCounterSink"/> is a ref struct and cannot be captured by a lambda, so each
/// helper builds a sink over the caller's <c>long[]</c> block and the block is asserted afterwards.
/// </para>
/// </summary>
public class NumericsTests
{
    private const long Max63 = long.MaxValue;

    internal static long[] NewBlock() => new long[SimCounterSink.RequiredLength];

    private static long Count(long[] block, PrimitiveCounter counter) => block[(int)counter];

    /// <summary>Asserts all six counters at once so "uncounted" rows prove nothing else fired.</summary>
    private static void AssertCounts(
        long[] block,
        long fixDivZero = 0,
        long fixClamped = 0,
        long moneyClamped = 0,
        long moneySaturated = 0,
        long fxClamped = 0,
        long moneyBadPermille = 0)
    {
        Assert.Equal(fixDivZero, Count(block, PrimitiveCounter.FixDivZero));
        Assert.Equal(fixClamped, Count(block, PrimitiveCounter.FixClamped));
        Assert.Equal(moneyClamped, Count(block, PrimitiveCounter.MoneyClamped));
        Assert.Equal(moneySaturated, Count(block, PrimitiveCounter.MoneySaturated));
        Assert.Equal(fxClamped, Count(block, PrimitiveCounter.FxClamped));
        Assert.Equal(moneyBadPermille, Count(block, PrimitiveCounter.MoneyBadPermille));
    }

    private static long MulDiv(long[] block, long a, long b, long c)
    {
        var sink = new SimCounterSink(block);
        return Fix.MulDiv(a, b, c, ref sink);
    }

    private static long MulDivRH(long[] block, long a, long b, long c)
    {
        var sink = new SimCounterSink(block);
        return Fix.MulDivRH(a, b, c, ref sink);
    }

    private static long FloorDiv(long[] block, long a, long b)
    {
        var sink = new SimCounterSink(block);
        return Fix.FloorDiv(a, b, ref sink);
    }

    private static long CeilDiv(long[] block, long a, long b)
    {
        var sink = new SimCounterSink(block);
        return Fix.CeilDiv(a, b, ref sink);
    }

    /// <summary>Wraps an in-range amount; the (separate) range-check behaviour is tested on its own.</summary>
    private static Cents Money(long cents)
    {
        long[] scratch = NewBlock();
        var sink = new SimCounterSink(scratch);
        Cents money = Cents.FromCents(cents, ref sink);
        AssertCounts(scratch);
        return money;
    }

    private static long MulPermille(long[] block, long cents, int permille)
    {
        Cents amount = Money(cents);
        var sink = new SimCounterSink(block);
        return Cents.MulPermille(amount, permille, ref sink).Value;
    }

    private static long AddCents(long[] block, long left, long right)
    {
        Cents a = Money(left);
        Cents b = Money(right);
        var sink = new SimCounterSink(block);
        return Cents.Add(a, b, ref sink).Value;
    }

    private static long SubCents(long[] block, long left, long right)
    {
        Cents a = Money(left);
        Cents b = Money(right);
        var sink = new SimCounterSink(block);
        return Cents.Sub(a, b, ref sink).Value;
    }

    private static long FromCents(long[] block, long value)
    {
        var sink = new SimCounterSink(block);
        return Cents.FromCents(value, ref sink).Value;
    }

    private static Fx16 FromRaw(long[] block, long raw)
    {
        var sink = new SimCounterSink(block);
        return Fx16.FromRaw(raw, ref sink);
    }

    private static Fx16 FxAdd(long[] block, long left, long right)
    {
        Fx16 a = FromRaw(NewBlock(), left);
        Fx16 b = FromRaw(NewBlock(), right);
        var sink = new SimCounterSink(block);
        return Fx16.Add(a, b, ref sink);
    }

    private static Fx16 FxSub(long[] block, long left, long right)
    {
        Fx16 a = FromRaw(NewBlock(), left);
        Fx16 b = FromRaw(NewBlock(), right);
        var sink = new SimCounterSink(block);
        return Fx16.Sub(a, b, ref sink);
    }

    /// <summary>Fixed-seed SplitMix64 (no System.Random); wraps by design.</summary>
    private static ulong NextSplitMix64(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Any long (including MinValue), with a random effective bit width so small values occur too.</summary>
    private static long NextLong(ref ulong state)
    {
        ulong raw = NextSplitMix64(ref state);
        int shift = (int)(NextSplitMix64(ref state) % 64UL);
        return unchecked((long)(raw >> shift));
    }

    private static UInt128 Magnitude(long v) => v >= 0 ? (UInt128)(ulong)v : (UInt128)(ulong)(-(v + 1)) + 1;

    // ---------------------------------------------------------------- AC-1 (SC-F7-1) headline values

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void MulPermille_15At1500_Is23AndUncounted()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulPermille(block, 15, 1500);

        // Assert
        Assert.Equal(23L, result);
        AssertCounts(block);
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void MulPermille_Minus15At1500_IsMinus23()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulPermille(block, -15, 1500);

        // Assert
        Assert.Equal(-23L, result);
        AssertCounts(block);
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void MulPermille_MaxTimesMillion_ClampsToMaxAndCountsMoneyClamped()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulPermille(block, Cents.Max, 1_000_000);

        // Assert
        Assert.Equal(Cents.Max, result);
        AssertCounts(block, moneyClamped: 1);
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void MulDiv_7Over2_TruncatesTo3AndMinus7To_Minus3()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long positive = MulDiv(block, 7, 1, 2);
        long negative = MulDiv(block, -7, 1, 2);

        // Assert
        Assert.Equal(3L, positive);
        Assert.Equal(-3L, negative);
        AssertCounts(block);
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void MulDiv_ZeroDivisor_ReturnsZeroAndCountsFixDivZero()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulDiv(block, 5, 5, 0);

        // Assert
        Assert.Equal(0L, result);
        AssertCounts(block, fixDivZero: 1);
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void MulDivRH_MinValueTimesMinValue_ClampsWithoutThrowing()
    {
        // Arrange (the Debug build of the Sim is checked; nothing may overflow)
        long[] block = NewBlock();

        // Act
        long result = MulDivRH(block, long.MinValue, long.MinValue, 1);

        // Assert
        Assert.Equal(Max63, result);
        AssertCounts(block, fixClamped: 1);
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void FxToMm_98304AndMinus98304_Are1AndMinus2()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        int up = Fx16.ToMm(FromRaw(block, 98_304));
        int down = Fx16.ToMm(FromRaw(block, -98_304));

        // Assert
        Assert.Equal(1, up);
        Assert.Equal(-2, down);
        AssertCounts(block);
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void CentsAdd_MaxMinusOnePlusTwo_AssertsOrSaturatesPerBuild()
    {
        // Arrange
        long[] block = NewBlock();

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(
                () => AddCents(block, Cents.Max - 1, 2));
            Assert.Equal(SimAssertCode.MoneyOverflow, thrown.Code);
            AssertCounts(block);
        }
        else
        {
            long result = AddCents(block, Cents.Max - 1, 2);
            Assert.Equal(Cents.Max, result);
            AssertCounts(block, moneySaturated: 1);
        }
    }

    // ---------------------------------------------------------------- AC-2: ADR-0006 section 2 N-vector table, one row per call

    [Theory]
    [Trait("Id", "SC-F7-1")]
    [InlineData(15L, 1500, 23L, 0L)]
    [InlineData(-15L, 1500, -23L, 0L)]
    [InlineData(1L, 500, 1L, 0L)]
    [InlineData(-1L, 500, -1L, 0L)]
    [InlineData(1L, 499, 0L, 0L)]
    [InlineData(336L, 1100, 370L, 0L)]
    [InlineData(Cents.Max, 1_000_000, Cents.Max, 1L)]
    [InlineData(Cents.Min, 1000, Cents.Min, 0L)]
    public void NVector_MulPermille_MatchesAdrRow(long cents, int permille, long expected, long moneyClamped)
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulPermille(block, cents, permille);

        // Assert
        Assert.Equal(expected, result);
        AssertCounts(block, moneyClamped: moneyClamped);
    }

    [Theory]
    [Trait("Id", "SC-F7-1")]
    [InlineData(7L, 1L, 2L, 3L, 0L)]
    [InlineData(-7L, 1L, 2L, -3L, 0L)]
    [InlineData(5L, 5L, 0L, 0L, 1L)]
    public void NVector_MulDiv_MatchesAdrRow(long a, long b, long c, long expected, long fixDivZero)
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulDiv(block, a, b, c);

        // Assert
        Assert.Equal(expected, result);
        AssertCounts(block, fixDivZero: fixDivZero);
    }

    [Theory]
    [Trait("Id", "SC-F7-1")]
    [InlineData(7L, 1L, 2L, 4L, 0L)]
    [InlineData(-7L, 1L, 2L, -4L, 0L)]
    [InlineData(5L, 1L, 3L, 2L, 0L)]
    [InlineData(4L, 1L, 3L, 1L, 0L)]
    [InlineData(-5L, 1L, -3L, 2L, 0L)]
    [InlineData(long.MinValue, long.MinValue, 1L, long.MaxValue, 1L)]
    [InlineData(long.MinValue, 1L, -1L, long.MaxValue, 1L)]
    [InlineData(long.MinValue, 1L, 1L, -long.MaxValue, 1L)]
    [InlineData(long.MinValue, long.MinValue, long.MinValue, -long.MaxValue, 1L)]
    public void NVector_MulDivRH_MatchesAdrRow(long a, long b, long c, long expected, long fixClamped)
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulDivRH(block, a, b, c);

        // Assert
        Assert.Equal(expected, result);
        AssertCounts(block, fixClamped: fixClamped);
    }

    [Theory]
    [Trait("Id", "SC-F7-1")]
    [InlineData(98_304L, 1)]
    [InlineData(-98_304L, -2)]
    [InlineData(-1L, -1)]
    public void NVector_Fx16ToMm_MatchesAdrRow(long raw, int expectedMm)
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        int mm = Fx16.ToMm(FromRaw(block, raw));

        // Assert
        Assert.Equal(expectedMm, mm);
        AssertCounts(block);
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void NVector_CentsAddOverflow_MatchesAdrRowForBuildMode()
    {
        // Arrange
        long[] block = NewBlock();

        // Act + Assert (Debug: assert; Release: 2^62 and moneySaturated +1)
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Throws<SimAssertException>(() => AddCents(block, Cents.Max - 1, 2));
            AssertCounts(block);
        }
        else
        {
            Assert.Equal(Cents.Max, AddCents(block, Cents.Max - 1, 2));
            AssertCounts(block, moneySaturated: 1);
        }
    }

    // ---------------------------------------------------------------- Fix.MulDiv / MulDivRH beyond the table

    [Theory]
    [InlineData(-5L, 1L, 3L, -2L)]
    [InlineData(-4L, 1L, 3L, -1L)]
    [InlineData(5L, 1L, -3L, -2L)]
    [InlineData(-7L, 1L, -2L, 4L)]
    [InlineData(1L, 1L, 2L, 1L)]
    [InlineData(0L, 7L, 3L, 0L)]
    [InlineData(-1L, 1L, 3L, 0L)]
    public void MulDivRH_SignAndTieCombinations_RoundHalfAwayFromZero(long a, long b, long c, long expected)
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulDivRH(block, a, b, c);

        // Assert
        Assert.Equal(expected, result);
        AssertCounts(block);
    }

    [Fact]
    public void MulDivRH_ZeroDivisor_ReturnsZeroAndCountsFixDivZero()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulDivRH(block, 5, 5, 0);

        // Assert
        Assert.Equal(0L, result);
        AssertCounts(block, fixDivZero: 1);
    }

    [Fact]
    public void MulDivRH_ProductBeyondInt64WithExactQuotient_DoesNotOverflow()
    {
        // Arrange: a*b = 2^124 needs 125 bits; the quotient 2^62 fits.
        long[] block = NewBlock();

        // Act
        long result = MulDivRH(block, 1L << 62, 1L << 62, 1L << 62);

        // Assert
        Assert.Equal(1L << 62, result);
        AssertCounts(block);
    }

    [Fact]
    public void MulDiv_ExtremeOperands_ClampToSignedLimitAndCount()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long high = MulDiv(block, long.MaxValue, 2, 1);
        long low = MulDiv(block, long.MinValue, 1, 1);
        long mixed = MulDiv(block, long.MinValue, long.MinValue, -1);

        // Assert
        Assert.Equal(Max63, high);
        Assert.Equal(-Max63, low);
        Assert.Equal(-Max63, mixed);
        AssertCounts(block, fixClamped: 3);
    }

    [Fact]
    public void MulDiv_ProductBeyondInt64WithInRangeQuotient_IsExactAndUncounted()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulDiv(block, 1L << 62, -(1L << 62), 1L << 62);

        // Assert
        Assert.Equal(-(1L << 62), result);
        AssertCounts(block);
    }

    [Fact]
    public void MulDivRH_SeededSweepAgainstIndependentUInt128Reference_Agrees()
    {
        // Arrange: reference = sign * ((2|a||b| + |c|) / (2|c|)), a different formulation of half-away rounding.
        ulong state = 0x5EED_0006_0000_0003UL;
        long[] block = NewBlock();
        long expectedClamps = 0;

        for (int i = 0; i < 10_000; i++)
        {
            long a = NextLong(ref state);
            long b = NextLong(ref state);
            long c = NextLong(ref state);
            if (c == 0)
            {
                c = 1;
            }

            UInt128 dd = Magnitude(c);
            UInt128 reference = ((2 * Magnitude(a) * Magnitude(b)) + dd) / (2 * dd);
            bool negative = (a < 0) ^ (b < 0) ^ (c < 0);
            bool clamped = reference > (UInt128)(ulong)long.MaxValue;
            long magnitude = clamped ? long.MaxValue : (long)(ulong)reference;
            long expected = negative ? -magnitude : magnitude;
            if (clamped)
            {
                expectedClamps++;
            }

            // Act
            long actual = MulDivRH(block, a, b, c);

            // Assert
            Assert.Equal(expected, actual);
        }

        AssertCounts(block, fixClamped: expectedClamps);
    }

    // ---------------------------------------------------------------- FloorDiv / CeilDiv

    [Theory]
    [Trait("Id", "SC-F7-1")]
    [InlineData(7L, 2L, 3L, 4L)]
    [InlineData(-7L, 2L, -4L, -3L)]
    [InlineData(7L, -2L, -4L, -3L)]
    [InlineData(-7L, -2L, 3L, 4L)]
    [InlineData(6L, 3L, 2L, 2L)]
    [InlineData(-6L, 3L, -2L, -2L)]
    [InlineData(6L, -3L, -2L, -2L)]
    [InlineData(-6L, -3L, 2L, 2L)]
    [InlineData(0L, 5L, 0L, 0L)]
    [InlineData(0L, -5L, 0L, 0L)]
    [InlineData(5L, 1L, 5L, 5L)]
    [InlineData(-5L, -1L, 5L, 5L)]
    [InlineData(long.MinValue, 2L, -(1L << 62), -(1L << 62))]
    public void FloorDivAndCeilDiv_SignCombinations_MatchMathematicalFloorAndCeiling(
        long a, long b, long expectedFloor, long expectedCeil)
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long floor = FloorDiv(block, a, b);
        long ceil = CeilDiv(block, a, b);

        // Assert
        Assert.Equal(expectedFloor, floor);
        Assert.Equal(expectedCeil, ceil);
        AssertCounts(block);
    }

    [Fact]
    public void FloorDivAndCeilDiv_ZeroDivisor_ReturnZeroAndCountFixDivZero()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long floor = FloorDiv(block, 9, 0);
        long ceil = CeilDiv(block, 9, 0);

        // Assert
        Assert.Equal(0L, floor);
        Assert.Equal(0L, ceil);
        AssertCounts(block, fixDivZero: 2);
    }

    [Fact]
    public void FloorDivAndCeilDiv_MinValueByMinusOne_ClampToMaxAndCountWithoutThrowing()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long floor = FloorDiv(block, long.MinValue, -1);
        long ceil = CeilDiv(block, long.MinValue, -1);

        // Assert
        Assert.Equal(Max63, floor);
        Assert.Equal(Max63, ceil);
        AssertCounts(block, fixClamped: 2);
    }

    [Fact]
    public void FloorDivAndCeilDiv_SeededSweep_SatisfyDefiningInequalities()
    {
        // Arrange
        ulong state = 0x5EED_0006_0000_0004UL;
        long[] block = NewBlock();

        for (int i = 0; i < 10_000; i++)
        {
            long a = NextLong(ref state);
            long b = NextLong(ref state);
            if (b == 0 || b == -1)
            {
                b = 3;
            }

            // Act
            long floor = FloorDiv(block, a, b);
            long ceil = CeilDiv(block, a, b);

            // Assert: floor*b is on the near side of a by less than |b|; ceil = floor or floor + 1.
            Int128 remainder = (Int128)a - ((Int128)floor * b);
            Int128 absB = b < 0 ? -(Int128)b : b;
            if (b > 0)
            {
                Assert.True(remainder >= 0 && remainder < absB);
            }
            else
            {
                Assert.True(remainder <= 0 && remainder > -absB);
            }

            Assert.True(ceil == floor || ceil == floor + 1);
            Assert.Equal(remainder == 0, ceil == floor);
        }

        AssertCounts(block);
    }

    // ---------------------------------------------------------------- ISqrt

    [Theory]
    [Trait("Id", "SC-F7-1")]
    [InlineData(0UL, 0U)]
    [InlineData(1UL, 1U)]
    [InlineData(2UL, 1U)]
    [InlineData(3UL, 1U)]
    [InlineData(4UL, 2U)]
    [InlineData(4_294_967_295UL, 65_535U)]
    [InlineData(4_611_686_018_427_387_904UL, 2_147_483_648U)]
    [InlineData(ulong.MaxValue, uint.MaxValue)]
    public void ISqrt_EdgeValues_ReturnFloorSquareRoot(ulong n, uint expected)
    {
        // Arrange + Act
        uint root = Fix.ISqrt(n);

        // Assert
        Assert.Equal(expected, root);
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void ISqrt_SeededSweepOf10000Samples_SatisfiesRootBracketInUInt128()
    {
        // Arrange
        ulong state = 0x5EED_0006_0000_0005UL;

        for (int i = 0; i < 10_000; i++)
        {
            ulong raw = NextSplitMix64(ref state);
            ulong n = raw >> (int)(NextSplitMix64(ref state) % 64UL);

            // Act
            uint r = Fix.ISqrt(n);

            // Assert: r*r <= n < (r+1)*(r+1), integer-only in UInt128
            UInt128 low = (UInt128)r * r;
            UInt128 high = ((UInt128)r + 1) * ((UInt128)r + 1);
            Assert.True(low <= n && n < high, $"ISqrt({n}) = {r}");
        }
    }

    [Fact]
    public void ISqrt_PerfectSquaresAndNeighbours_ReturnExactRoot()
    {
        // Arrange
        ulong state = 0x5EED_0006_0000_0006UL;

        for (int i = 0; i < 2_000; i++)
        {
            uint r = (uint)(NextSplitMix64(ref state) >> 32);
            if (r == 0)
            {
                r = 1;
            }

            ulong square = (ulong)r * r;

            // Act + Assert
            Assert.Equal(r, Fix.ISqrt(square));
            Assert.Equal(r - 1, Fix.ISqrt(square - 1));
        }
    }

    // ---------------------------------------------------------------- Fx16

    [Theory]
    [Trait("Id", "SC-F7-1")]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(123_456)]
    public void FxFromMm_AnyInt_RoundTripsThroughToMmWithoutClamping(int mm)
    {
        // Arrange + Act
        Fx16 fx = Fx16.FromMm(mm);
        int back = Fx16.ToMm(fx);

        // Assert
        Assert.Equal(mm, back);
        Assert.Equal((long)mm << 16, fx.Raw);
    }

    [Fact]
    public void FxFromMm_IntExtremes_MatchDomainBounds()
    {
        // Arrange + Act
        long lowest = Fx16.FromMm(int.MinValue).Raw;
        long highest = Fx16.FromMm(int.MaxValue).Raw;

        // Assert
        Assert.Equal(-(1L << 47), Fx16.MinRaw);
        Assert.Equal((1L << 47) - 65_536, Fx16.MaxRaw);
        Assert.Equal(Fx16.MinRaw, lowest);
        Assert.Equal(Fx16.MaxRaw, highest);
        Assert.Equal(16, Fx16.FractionBits);
    }

    [Theory]
    [InlineData(65_535L, 0)]
    [InlineData(65_536L, 1)]
    [InlineData(-65_536L, -1)]
    [InlineData(-65_537L, -2)]
    public void FxToMm_FractionalRaw_FloorsTowardNegativeInfinity(long raw, int expectedMm)
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        int mm = Fx16.ToMm(FromRaw(block, raw));

        // Assert
        Assert.Equal(expectedMm, mm);
        AssertCounts(block);
    }

    [Theory]
    [Trait("Id", "SC-F7-1")]
    [InlineData((1L << 47) - 65_535L, (1L << 47) - 65_536L)]
    [InlineData(1L << 47, (1L << 47) - 65_536L)]
    [InlineData(long.MaxValue, (1L << 47) - 65_536L)]
    [InlineData(-(1L << 47) - 1L, -(1L << 47))]
    [InlineData(long.MinValue, -(1L << 47))]
    public void FxFromRaw_OutOfDomain_ClampsToDomainAndCountsFxClamped(long raw, long expectedRaw)
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        Fx16 fx = FromRaw(block, raw);

        // Assert
        Assert.Equal(expectedRaw, fx.Raw);
        AssertCounts(block, fxClamped: 1);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData((1L << 47) - 65_536L)]
    [InlineData(-(1L << 47))]
    [InlineData(12_345L)]
    public void FxFromRaw_InDomain_IsKeptAndUncounted(long raw)
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        Fx16 fx = FromRaw(block, raw);

        // Assert
        Assert.Equal(raw, fx.Raw);
        AssertCounts(block);
    }

    [Fact]
    public void FxAddAndSub_InDomain_AreExactAndUncounted()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        Fx16 sum = FxAdd(block, 98_304, -65_536);
        Fx16 difference = FxSub(block, 98_304, 65_536);

        // Assert
        Assert.Equal(32_768L, sum.Raw);
        Assert.Equal(32_768L, difference.Raw);
        AssertCounts(block);
    }

    [Fact]
    public void FxAddAndSub_BeyondDomain_ClampAndCountFxClamped()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        Fx16 high = FxAdd(block, Fx16.MaxRaw, Fx16.MaxRaw);
        Fx16 low = FxAdd(block, Fx16.MinRaw, Fx16.MinRaw);
        Fx16 subHigh = FxSub(block, Fx16.MaxRaw, Fx16.MinRaw);
        Fx16 subLow = FxSub(block, Fx16.MinRaw, Fx16.MaxRaw);

        // Assert
        Assert.Equal(Fx16.MaxRaw, high.Raw);
        Assert.Equal(Fx16.MinRaw, low.Raw);
        Assert.Equal(Fx16.MaxRaw, subHigh.Raw);
        Assert.Equal(Fx16.MinRaw, subLow.Raw);
        AssertCounts(block, fxClamped: 4);
    }

    [Fact]
    public void FxEquality_ValuesOrderingAndHash_FollowRaw()
    {
        // Arrange
        Fx16 one = Fx16.FromMm(1);
        Fx16 oneAgain = Fx16.FromMm(1);
        Fx16 two = Fx16.FromMm(2);

        // Act + Assert
        Assert.True(one == oneAgain);
        Assert.True(one != two);
        Assert.True(one.Equals(oneAgain));
        Assert.True(one.Equals((object)oneAgain));
        Assert.False(one.Equals((object)"x"));
        Assert.Equal(one.GetHashCode(), oneAgain.GetHashCode());
        Assert.True(one.CompareTo(two) < 0);
        Assert.True(two.CompareTo(one) > 0);
        Assert.Equal(0, one.CompareTo(oneAgain));
        Assert.Equal(default, Fx16.FromMm(0));
    }

    // ---------------------------------------------------------------- Cents

    [Theory]
    [InlineData(0L)]
    [InlineData(-500L)]
    [InlineData(Cents.Max)]
    [InlineData(Cents.Min)]
    public void CentsFromCents_InRange_IsKeptAndUncounted(long value)
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = FromCents(block, value);

        // Assert
        Assert.Equal(value, result);
        AssertCounts(block);
    }

    [Theory]
    [InlineData(Cents.Max + 1, Cents.Max)]
    [InlineData(long.MaxValue, Cents.Max)]
    [InlineData(Cents.Min - 1, Cents.Min)]
    [InlineData(long.MinValue, Cents.Min)]
    public void CentsFromCents_OutOfRange_AssertsOrClampsPerBuild(long value, long clampedTo)
    {
        // Arrange
        long[] block = NewBlock();

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => FromCents(block, value));
            Assert.Equal(SimAssertCode.MoneyRange, thrown.Code);
            AssertCounts(block);
        }
        else
        {
            Assert.Equal(clampedTo, FromCents(block, value));
            AssertCounts(block, moneyClamped: 1);
        }
    }

    [Fact]
    public void CentsAddAndSub_NegativeBalancesInsideRange_AreLegalAndExact()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long sum = AddCents(block, -500, 200);
        long difference = SubCents(block, -500, 200);
        long edgeAdd = AddCents(block, Cents.Max, Cents.Min);
        long edgeSub = SubCents(block, Cents.Min, Cents.Min);

        // Assert
        Assert.Equal(-300L, sum);
        Assert.Equal(-700L, difference);
        Assert.Equal(0L, edgeAdd);
        Assert.Equal(0L, edgeSub);
        AssertCounts(block);
    }

    [Fact]
    public void CentsAddAndSub_ExactlyOnTheBound_AreAcceptedUncounted()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long top = AddCents(block, Cents.Max - 1, 1);
        long bottom = SubCents(block, Cents.Min + 1, 1);

        // Assert
        Assert.Equal(Cents.Max, top);
        Assert.Equal(Cents.Min, bottom);
        AssertCounts(block);
    }

    [Fact]
    public void CentsAddAndSub_BeyondBoundInBothDirections_AssertOrSaturatePerBuild()
    {
        // Arrange
        long[] block = NewBlock();

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.MoneyOverflow, Assert.Throws<SimAssertException>(() => AddCents(block, Cents.Max, Cents.Max)).Code);
            Assert.Equal(SimAssertCode.MoneyOverflow, Assert.Throws<SimAssertException>(() => AddCents(block, Cents.Min, -1)).Code);
            Assert.Equal(SimAssertCode.MoneyOverflow, Assert.Throws<SimAssertException>(() => SubCents(block, Cents.Min, 1)).Code);
            Assert.Equal(SimAssertCode.MoneyOverflow, Assert.Throws<SimAssertException>(() => SubCents(block, Cents.Max, Cents.Min)).Code);
            AssertCounts(block);
        }
        else
        {
            Assert.Equal(Cents.Max, AddCents(block, Cents.Max, Cents.Max));
            Assert.Equal(Cents.Min, AddCents(block, Cents.Min, -1));
            Assert.Equal(Cents.Min, SubCents(block, Cents.Min, 1));
            Assert.Equal(Cents.Max, SubCents(block, Cents.Max, Cents.Min));
            AssertCounts(block, moneySaturated: 4);
        }
    }

    [Fact]
    public void CentsMulPermille_ZeroPermille_IsZeroAndUncounted()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long zero = MulPermille(block, 12_345, 0);
        long zeroOfNegative = MulPermille(block, -12_345, 0);

        // Assert
        Assert.Equal(0L, zero);
        Assert.Equal(0L, zeroOfNegative);
        AssertCounts(block);
    }

    /// <summary>GDD simulation-core F7: pm is 0 to 10^6; a negative pm asserts, or is treated as 0 and counted.</summary>
    [Theory]
    [InlineData(15L, -1500)]
    [InlineData(-15L, -1500)]
    [InlineData(12_345L, -1)]
    [InlineData(0L, -1)]
    [InlineData(Cents.Max, int.MinValue)]
    [InlineData(Cents.Min, int.MinValue)]
    [InlineData(15L, int.MinValue)]
    public void CentsMulPermille_NegativePermille_AssertsOrIsZeroAndCountsMoneyBadPermille(long cents, int permille)
    {
        // Arrange
        long[] block = NewBlock();

        // Act + Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(
                () => MulPermille(block, cents, permille));
            Assert.Equal(SimAssertCode.MoneyBadPermille, thrown.Code);
            AssertCounts(block);
        }
        else
        {
            long result = MulPermille(block, cents, permille);
            Assert.Equal(0L, result);
            AssertCounts(block, moneyBadPermille: 1);
        }
    }

    [Fact]
    public void CentsMulPermille_SeededSweep_MatchesGddF7ClosedForm()
    {
        // Arrange: independent oracle, sign(c) * min((|c| * pm + 500) / 1000, 2^62), all in Int128
        ulong state = 0x5EED_0007_0000_000FUL;
        long[] block = NewBlock();
        long expectedClamps = 0;
        Int128 bound = Cents.Max;

        for (int i = 0; i < 10_000; i++)
        {
            long magnitude = (long)(NextSplitMix64(ref state) >> 2);
            long cents = (NextSplitMix64(ref state) & 1UL) == 0UL ? magnitude : -magnitude;
            int permille = (int)(NextSplitMix64(ref state) % 1_000_001UL);
            Int128 abs = cents < 0 ? -(Int128)cents : cents;
            Int128 rounded = ((abs * permille) + 500) / 1000;
            Int128 capped = rounded > bound ? bound : rounded;
            if (rounded > bound)
            {
                expectedClamps++;
            }

            long expected = (long)(cents < 0 ? -capped : capped);

            // Act
            long actual = MulPermille(block, cents, permille);

            // Assert
            Assert.Equal(expected, actual);
        }

        AssertCounts(block, moneyClamped: expectedClamps);
    }

    [Fact]
    public void CentsMulPermille_NegativeBeyondBound_ClampsToMinAndCountsMoneyNotFix()
    {
        // Arrange
        long[] block = NewBlock();

        // Act
        long result = MulPermille(block, Cents.Min, 1001);

        // Assert
        Assert.Equal(Cents.Min, result);
        AssertCounts(block, moneyClamped: 1);
    }

    [Fact]
    public void CentsMulPermille_SeededSweep_EqualsClampedMulDivRH()
    {
        // Arrange: Cents.MulPermille == clamp(MulDivRH(c, pm, 1000), +/-2^62)
        ulong state = 0x5EED_0006_0000_0007UL;
        long[] block = NewBlock();
        long[] reference = NewBlock();
        long expectedClamps = 0;

        for (int i = 0; i < 10_000; i++)
        {
            long magnitude = (long)(NextSplitMix64(ref state) >> 2);
            long cents = (NextSplitMix64(ref state) & 1UL) == 0UL ? magnitude : -magnitude;
            int permille = (int)(NextSplitMix64(ref state) % 1_000_001UL);
            long wide = MulDivRH(reference, cents, permille, 1000);
            long expected = Math.Clamp(wide, Cents.Min, Cents.Max);
            if (expected != wide)
            {
                expectedClamps++;
            }

            // Act
            long actual = MulPermille(block, cents, permille);

            // Assert
            Assert.Equal(expected, actual);
        }

        AssertCounts(block, moneyClamped: expectedClamps);
    }

    [Fact]
    public void CentsEquality_ValuesOrderingAndHash_FollowValue()
    {
        // Arrange
        Cents five = Money(5);
        Cents fiveAgain = Money(5);
        Cents minusFive = Money(-5);

        // Act + Assert
        Assert.True(five == fiveAgain);
        Assert.True(five != minusFive);
        Assert.True(five.Equals(fiveAgain));
        Assert.True(five.Equals((object)fiveAgain));
        Assert.False(five.Equals((object)5L));
        Assert.Equal(five.GetHashCode(), fiveAgain.GetHashCode());
        Assert.True(minusFive.CompareTo(five) < 0);
        Assert.True(five.CompareTo(minusFive) > 0);
        Assert.Equal(0, five.CompareTo(fiveAgain));
        Assert.Equal(0L, default(Cents).Value);
        Assert.Equal(1L << 62, Cents.Max);
        Assert.Equal(-(1L << 62), Cents.Min);
    }

    [Fact]
    public void MoneyAndFixedPointTypes_PublicSurface_HasNoRawOperatorsOrPublicConstructors()
    {
        // Arrange
        Type[] types = { typeof(Cents), typeof(Fx16) };

        foreach (Type type in types)
        {
            // Act
            bool hasAddition = type.GetMethod("op_Addition") is not null;
            bool hasSubtraction = type.GetMethod("op_Subtraction") is not null;
            bool hasPublicCtor = type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Length > 0;
            bool hasClone = type.GetMethod("<Clone>$") is not null;

            // Assert (not a record struct: no init/with bypass; no raw + / -)
            Assert.False(hasAddition, type.Name);
            Assert.False(hasSubtraction, type.Name);
            Assert.False(hasPublicCtor, type.Name);
            Assert.False(hasClone, type.Name);
            Assert.True(type.IsValueType, type.Name);
        }
    }

    // ---------------------------------------------------------------- SimCounterSink / PrimitiveCounter

    [Fact]
    public void SimCounterSink_Increment_BumpsOnlyTheNamedSlot()
    {
        // Arrange
        long[] block = NewBlock();
        var sink = new SimCounterSink(block);

        // Act
        sink.Increment(PrimitiveCounter.MoneySaturated);
        sink.Increment(PrimitiveCounter.MoneySaturated);
        sink.Increment(PrimitiveCounter.FxClamped);

        // Assert
        Assert.Equal(2L, sink.Get(PrimitiveCounter.MoneySaturated));
        Assert.Equal(1L, sink.Get(PrimitiveCounter.FxClamped));
        Assert.Equal(6, sink.Length);
        AssertCounts(block, moneySaturated: 2, fxClamped: 1);
    }

    [Fact]
    public void SimCounterSink_EachCounter_MapsToItsDocumentedSlot()
    {
        // Arrange
        long[] block = NewBlock();
        var sink = new SimCounterSink(block);

        // Act
        sink.Increment(PrimitiveCounter.FixDivZero);
        sink.Increment(PrimitiveCounter.FixClamped);
        sink.Increment(PrimitiveCounter.MoneyClamped);
        sink.Increment(PrimitiveCounter.MoneySaturated);
        sink.Increment(PrimitiveCounter.FxClamped);
        sink.Increment(PrimitiveCounter.MoneyBadPermille);

        // Assert
        Assert.Equal(new long[] { 1, 1, 1, 1, 1, 1 }, block);
        Assert.Equal(0, (int)PrimitiveCounter.FixDivZero);
        Assert.Equal(1, (int)PrimitiveCounter.FixClamped);
        Assert.Equal(2, (int)PrimitiveCounter.MoneyClamped);
        Assert.Equal(3, (int)PrimitiveCounter.MoneySaturated);
        Assert.Equal(4, (int)PrimitiveCounter.FxClamped);
        Assert.Equal(5, (int)PrimitiveCounter.MoneyBadPermille);
        Assert.Equal(6, SimCounterSink.RequiredLength);
    }

    [Fact]
    public void SimCounterSink_DefaultOrShortBlock_DropsCountsWithoutThrowing()
    {
        // Arrange
        var empty = default(SimCounterSink);
        long[] shortBlock = new long[2];
        var shortSink = new SimCounterSink(shortBlock);

        // Act
        empty.Increment(PrimitiveCounter.FixClamped);
        shortSink.Increment(PrimitiveCounter.FixClamped);
        shortSink.Increment(PrimitiveCounter.FxClamped);
        long viaHelper = Fix.MulDivRH(5, 5, 0, ref empty);

        // Assert
        Assert.Equal(0L, empty.Get(PrimitiveCounter.FixClamped));
        Assert.Equal(0, empty.Length);
        Assert.Equal(1L, shortSink.Get(PrimitiveCounter.FixClamped));
        Assert.Equal(0L, shortSink.Get(PrimitiveCounter.FxClamped));
        Assert.Equal(new long[] { 0, 1 }, shortBlock);
        Assert.Equal(0L, viaHelper);
    }

    [Fact]
    public void SimCounterSink_CounterAtLongMax_SaturatesWithoutOverflowing()
    {
        // Arrange (the Debug Sim is checked: ++ on long.MaxValue would throw)
        long[] block = NewBlock();
        block[(int)PrimitiveCounter.FixClamped] = long.MaxValue;
        var sink = new SimCounterSink(block);

        // Act
        sink.Increment(PrimitiveCounter.FixClamped);

        // Assert
        Assert.Equal(long.MaxValue, sink.Get(PrimitiveCounter.FixClamped));
    }

    internal static void ExerciseHelpers(long[] block)
    {
        var sink = new SimCounterSink(block);
        Cents c = Cents.FromCents(336, ref sink);
        Cents scaled = Cents.MulPermille(c, 1100, ref sink);
        Cents total = Cents.Sub(Cents.Add(c, scaled, ref sink), c, ref sink);
        Fx16 fx = Fx16.Add(Fx16.FromMm(3), Fx16.FromRaw(1, ref sink), ref sink);
        long sum = total.Value + Fx16.ToMm(fx)
            + Fix.MulDiv(7, 1, 2, ref sink) + Fix.MulDivRH(7, 1, 2, ref sink)
            + Fix.FloorDiv(-7, 2, ref sink) + Fix.CeilDiv(-7, 2, ref sink)
            + Fix.ISqrt(99);
        if (sum == long.MinValue)
        {
            throw new InvalidOperationException("unreachable");
        }
    }
}

/// <summary>
/// Zero-allocation guardrail for the numerics helpers, in the non-parallel collection so runtime warm-up on other test
/// threads cannot land in a measured window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class NumericsAllocationTests
{
    [Fact]
    public void NumericsHelpers_WarmedUp_AllocateNothing()
    {
        // Arrange
        long[] block = NumericsTests.NewBlock();

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("100 rounds of Cents/Fx16/Fix helpers", () =>
        {
            for (int i = 0; i < 100; i++)
            {
                NumericsTests.ExerciseHelpers(block);
            }
        });
    }
}
