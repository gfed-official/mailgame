using System.Numerics;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Primitives;

/// <summary>
/// Covers the <see cref="Int128"/> overloads added to <see cref="Fix"/> for content-data story 007
/// (<c>DivRoundHalfAway</c>, <c>FloorDiv</c>, <c>CeilDiv</c>): they must agree with the long helpers on the long domain
/// (ADR-0006 section 2 N-vectors) and with a <see cref="BigInteger"/> reference beyond it. Deterministic seed, no time.
/// </summary>
public class FixInt128Tests
{
    private static readonly long[] _edges =
    {
        0, 1, -1, 2, -2, 3, -3, 7, -7, 999, -999, 1000, -1000, 1499, -1499, 1500, -1500, 1501, -1501,
        long.MaxValue, long.MinValue, long.MaxValue - 1, long.MinValue + 1, 1L << 62, -(1L << 62), 1L << 32, -(1L << 32),
    };

    private static readonly long[] _positiveDivisors =
    {
        1, 2, 3, 7, 10, 999, 1000, 1001, 1_000_000, 1L << 31, 1L << 32, 1_000_000_000_000_000_000L, long.MaxValue, long.MaxValue - 1,
    };

    private static void AssertAgrees(long n, long d)
    {
        long[] block = new long[SimCounterSink.RequiredLength];
        var sink = new SimCounterSink(block);
        long floor = Fix.FloorDiv(n, d, ref sink);
        long ceil = Fix.CeilDiv(n, d, ref sink);
        long half = Fix.MulDivRH(n, 1, d, ref sink);

        // The long helpers clamp to +/-(2^63 - 1) (MulDivRH) or to MaxValue (MinValue / -1); only a clamped result
        // may differ from the unbounded Int128 answer, and it is then the long helper that is bounded.
        Assert.Equal((Int128)floor, Fix.FloorDiv((Int128)n, (Int128)d));
        Assert.Equal((Int128)ceil, Fix.CeilDiv((Int128)n, (Int128)d));
        if (half != -long.MaxValue || n != long.MinValue || d != 1)
        {
            Assert.Equal((Int128)half, Fix.DivRoundHalfAway((Int128)n, (Int128)d));
        }
        else
        {
            Assert.Equal((Int128)long.MinValue, Fix.DivRoundHalfAway((Int128)n, (Int128)d));
        }
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void Int128Overloads_EdgeGrid_AgreeWithLongHelpers()
    {
        // Act + Assert (every edge numerator against every positive divisor)
        foreach (long n in _edges)
        {
            foreach (long d in _positiveDivisors)
            {
                AssertAgrees(n, d);
            }
        }
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void Int128Overloads_SeededSweep_AgreeWithLongHelpers()
    {
        // Arrange
        var rng = new Pcg32(0xD1FF128UL, 7);

        // Act + Assert
        for (int i = 0; i < 5000; i++)
        {
            long n = (long)(((ulong)rng.Next32() << 32) | rng.Next32());
            if (i % 3 == 0)
            {
                n >>= (int)(rng.Next32() % 60);
            }

            long d = (long)((((ulong)rng.Next32() << 32) | rng.Next32()) >> 1);
            d >>= (int)(rng.Next32() % 62);
            if (d == 0)
            {
                d = 1;
            }

            AssertAgrees(n, d);
        }
    }

    [Theory]
    [Trait("Id", "SC-F7-1")]
    [InlineData(-1500L, 1000L, -2L, -2L, -1L)]    // half-away, floor, ceil
    [InlineData(1500L, 1000L, 2L, 1L, 2L)]
    [InlineData(-1L, 1000L, 0L, -1L, 0L)]
    [InlineData(-500L, 1000L, -1L, -1L, 0L)]
    [InlineData(499L, 1000L, 0L, 0L, 1L)]
    [InlineData(-6L, 3L, -2L, -2L, -2L)]
    public void Int128Overloads_KnownVectors_ReturnExpected(long n, long d, long half, long floor, long ceil)
    {
        // Assert
        Assert.Equal((Int128)half, Fix.DivRoundHalfAway(n, d));
        Assert.Equal((Int128)floor, Fix.FloorDiv(n, d));
        Assert.Equal((Int128)ceil, Fix.CeilDiv(n, d));
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void Int128Overloads_BeyondLong_MatchBigIntegerReference()
    {
        // Arrange
        var rng = new Pcg32(0xB16B16UL, 9);
        for (int i = 0; i < 3000; i++)
        {
            Int128 n = RandomInt128(ref rng);
            Int128 d = RandomInt128(ref rng) >> (int)(rng.Next32() % 120);
            if (d < 0)
            {
                d = -(d + 1);
            }

            if (d == 0)
            {
                d = 1000;
            }

            BigInteger bn = (BigInteger)n;
            BigInteger bd = (BigInteger)d;
            BigInteger q = BigInteger.DivRem(bn, bd, out BigInteger r);
            BigInteger floor = r.Sign < 0 ? q - 1 : q;
            BigInteger ceil = r.Sign > 0 ? q + 1 : q;
            BigInteger magnitude = BigInteger.Divide((2 * BigInteger.Abs(bn)) + bd, 2 * bd);
            BigInteger half = bn.Sign < 0 ? -magnitude : magnitude;

            // Act + Assert
            Assert.Equal(floor, (BigInteger)Fix.FloorDiv(n, d));
            Assert.Equal(ceil, (BigInteger)Fix.CeilDiv(n, d));
            Assert.Equal(half, (BigInteger)Fix.DivRoundHalfAway(n, d));
        }
    }

    [Fact]
    [Trait("Id", "SC-F7-1")]
    public void Int128Overloads_ExtremeNumerators_DoNotOverflow()
    {
        // Assert (no checked-context exception on either leg)
        Assert.Equal(Int128.MinValue, Fix.DivRoundHalfAway(Int128.MinValue, 1));
        Assert.Equal(Int128.MaxValue, Fix.DivRoundHalfAway(Int128.MaxValue, 1));
        Assert.Equal(Int128.MinValue, Fix.FloorDiv(Int128.MinValue, 1));
        Assert.Equal(Int128.MinValue, Fix.CeilDiv(Int128.MinValue, 1));
        Assert.Equal(Int128.MaxValue, Fix.CeilDiv(Int128.MaxValue, 1));
        Assert.Equal((Int128)(-((BigInteger)1 << 126)), Fix.DivRoundHalfAway(Int128.MinValue, 2));
        Assert.Equal((Int128)((BigInteger)1 << 126), Fix.DivRoundHalfAway(Int128.MaxValue, 2));
    }

    [Theory]
    [Trait("Id", "SC-F7-1")]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-1000L)]
    public void Int128Overloads_NonPositiveDivisor_ReturnZero(long d)
    {
        // Assert (documented policy: precondition d > 0, violation returns 0 like the long zero-divisor policy)
        Assert.Equal(Int128.Zero, Fix.DivRoundHalfAway(12345, d));
        Assert.Equal(Int128.Zero, Fix.FloorDiv(12345, d));
        Assert.Equal(Int128.Zero, Fix.CeilDiv(12345, d));
    }

    private static Int128 RandomInt128(ref Pcg32 rng)
    {
        UInt128 hi = ((UInt128)rng.Next32() << 96) | ((UInt128)rng.Next32() << 64);
        UInt128 lo = ((UInt128)rng.Next32() << 32) | rng.Next32();
        UInt128 bits = hi | lo;
        bits >>= 1 + (int)(rng.Next32() % 100);   // at least one bit: the value is non-negative, so negating it cannot overflow
        Int128 magnitude = unchecked((Int128)bits);
        return rng.Next32() % 2 == 0 ? magnitude : -magnitude;
    }
}
