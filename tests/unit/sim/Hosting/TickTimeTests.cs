using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Hosting;

/// <summary>
/// Covers TR-sim-001 and GDD SC-F1-1 (F1 tick and time): <see cref="TickRate"/> and
/// <see cref="TickTime"/>. Every AC-1 value is asserted verbatim. Out-of-range inputs assert under
/// SIM_ASSERT builds and clamp otherwise, chosen by <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// All maths is integer-only.
/// </summary>
public class TickTimeTests
{
    private static void AssertClock(int minutes, int seconds, TickClock clock)
    {
        Assert.Equal(minutes, clock.Minutes);
        Assert.Equal(seconds, clock.Seconds);
    }

    [Fact]
    [Trait("Id", "SC-F1-1")]
    public void TickRate_Hz_Is30()
    {
        Assert.Equal(30, TickRate.Hz);
    }

    [Theory]
    [Trait("Id", "SC-F1-1")]
    [InlineData(400L, 12L)]
    [InlineData(150L, 5L)]
    [InlineData(15_000L, 450L)]
    [InlineData(0L, 0L)]
    [InlineData(1L, 1L)]
    [InlineData(100L, 3L)]
    [InlineData(34L, 2L)]
    [InlineData(TickTime.MaxMs, 2_592_000L)]
    public void ContentTicks_Value_MatchesF1(long ms, long expected)
    {
        Assert.Equal(expected, TickTime.ContentTicks(ms));
    }

    [Theory]
    [Trait("Id", "SC-F1-1")]
    [InlineData(1L, 33L, 34L)]
    [InlineData(0L, 0L, 0L)]
    [InlineData(30L, 1000L, 1000L)]
    [InlineData(TickTime.MaxTicks, 71_582_788_233L, 71_582_788_234L)]
    public void TicksToMs_FloorAndCeil_MatchF1(long ticks, long floor, long ceil)
    {
        Assert.Equal(floor, TickTime.TicksToMsFloor(ticks));
        Assert.Equal(ceil, TickTime.TicksToMsCeil(ticks));
    }

    [Fact]
    [Trait("Id", "SC-F1-1")]
    public void RoundTrip_TicksToMsCeilToTicks_IsNotIdentity()
    {
        long ticks = TickTime.ContentTicks(TickTime.TicksToMsCeil(1));

        Assert.Equal(2L, ticks);
    }

    [Theory]
    [Trait("Id", "SC-F1-1")]
    [InlineData(360L, 0, 12)]
    [InlineData(331L, 0, 12)]
    [InlineData(330L, 0, 11)]
    [InlineData(1L, 0, 1)]
    [InlineData(0L, 0, 0)]
    [InlineData(2160L, 1, 12)]
    [InlineData(200_000L, 99, 59)]
    public void Clock_Remaining_MatchesF1(long remaining, int minutes, int seconds)
    {
        AssertClock(minutes, seconds, TickTime.Clock(remaining));
    }

    [Theory]
    [Trait("Id", "SC-F1-1")]
    [InlineData(179_970L, 99, 59)]  // 5999 s exactly
    [InlineData(179_971L, 99, 59)]  // 5999.03 s rounds up to 6000, capped
    [InlineData(179_940L, 99, 58)]  // 5998 s
    [InlineData(179_941L, 99, 59)]
    public void Clock_CapBoundary_Holds99_59(long remaining, int minutes, int seconds)
    {
        AssertClock(minutes, seconds, TickTime.Clock(remaining));
    }

    [Fact]
    [Trait("Id", "SC-F1-1")]
    public void DisplaySeconds_5999Versus6000_AreUncappedAndClockCaps()
    {
        Assert.Equal(5999L, TickTime.DisplaySeconds(179_970));
        Assert.Equal(6000L, TickTime.DisplaySeconds(179_971));
        Assert.Equal(6000L, TickTime.DisplaySeconds(180_000));
        AssertClock(99, 59, TickTime.Clock(180_000));
    }

    [Fact]
    [Trait("Id", "SC-F1-1")]
    public void Clock_NeverZeroWhileTimeRemains()
    {
        for (long r = 1; r <= 30; r++)
        {
            Assert.Equal(1L, TickTime.DisplaySeconds(r));
        }

        Assert.Equal(0L, TickTime.DisplaySeconds(0));
    }

    [Fact]
    [Trait("Id", "SC-F1-1")]
    public void Clock_DeadlineTenTicksInPast_ReadsZero()
    {
        AssertClock(0, 0, TickTime.Clock(currentTick: 110u, deadlineTick: 100u));
        Assert.Equal(0L, TickTime.Remaining(110u, 100u));
    }

    [Theory]
    [Trait("Id", "SC-F1-1")]
    [InlineData(0u, 0u, 0L)]
    [InlineData(5u, 5u, 0L)]
    [InlineData(0u, 360u, 360L)]
    [InlineData(100u, 0u, 0L)]
    [InlineData(uint.MaxValue, 0u, 0L)]
    [InlineData(uint.MaxValue, 1u, 0L)]
    [InlineData(uint.MaxValue, uint.MaxValue - 1, 0L)]
    [InlineData(2_000_000_000u, 10u, 0L)]
    [InlineData(0u, uint.MaxValue, uint.MaxValue)]
    [InlineData(1u, uint.MaxValue, uint.MaxValue - 1L)]
    public void Remaining_UsesLongSubtraction_NeverWraps(uint current, uint deadline, long expected)
    {
        Assert.Equal(expected, TickTime.Remaining(current, deadline));
        Assert.Equal(TickTime.Clock(expected), TickTime.Clock(current, deadline));
    }

    [Fact]
    [Trait("Id", "SC-F1-1")]
    public void Conversions_AtRangeMaxima_DoNotOverflow()
    {
        Assert.True(TickTime.TicksToMsCeil(TickTime.MaxTicks) > TickTime.TicksToMsFloor(TickTime.MaxTicks));
        Assert.Equal(TickTime.MaxMs * 30 / 1000, TickTime.ContentTicks(TickTime.MaxMs));
        AssertClock(99, 59, TickTime.Clock(TickTime.MaxTicks));
        AssertClock(99, 59, TickTime.Clock(0u, uint.MaxValue));
    }

    [Theory]
    [Trait("Id", "SC-F1-1")]
    [InlineData(long.MaxValue)]
    [InlineData(long.MaxValue - 29)]
    [InlineData(long.MaxValue - 28)]
    [InlineData(long.MaxValue - 1)]
    public void Clock_NearLongMaxValue_CapsWithoutOverflow(long remaining)
    {
        AssertClock(99, 59, TickTime.Clock(remaining));
        Assert.True(TickTime.DisplaySeconds(remaining) > 5999L);
    }

    [Fact]
    [Trait("Id", "SC-F1-1")]
    public void TicksToMs_OutOfRange_AssertsOrClampsPerBuild()
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.TickRange, Assert.Throws<SimAssertException>(() => TickTime.TicksToMsFloor(-1)).Code);
            Assert.Equal(SimAssertCode.TickRange, Assert.Throws<SimAssertException>(() => TickTime.TicksToMsCeil(TickTime.MaxTicks + 1)).Code);
        }
        else
        {
            Assert.Equal(0L, TickTime.TicksToMsFloor(-1));
            Assert.Equal(0L, TickTime.TicksToMsCeil(-5));
            Assert.Equal(TickTime.TicksToMsFloor(TickTime.MaxTicks), TickTime.TicksToMsFloor(long.MaxValue));
            Assert.Equal(TickTime.TicksToMsCeil(TickTime.MaxTicks), TickTime.TicksToMsCeil(TickTime.MaxTicks + 1));
        }
    }

    [Fact]
    [Trait("Id", "SC-F1-1")]
    public void ContentTicks_OutOfRange_AssertsOrClampsPerBuild()
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.MsRange, Assert.Throws<SimAssertException>(() => TickTime.ContentTicks(-1)).Code);
            Assert.Equal(SimAssertCode.MsRange, Assert.Throws<SimAssertException>(() => TickTime.ContentTicks(TickTime.MaxMs + 1)).Code);
        }
        else
        {
            Assert.Equal(0L, TickTime.ContentTicks(-1));
            Assert.Equal(TickTime.ContentTicks(TickTime.MaxMs), TickTime.ContentTicks(TickTime.MaxMs + 1));
            Assert.Equal(TickTime.ContentTicks(TickTime.MaxMs), TickTime.ContentTicks(long.MaxValue));
        }
    }

    [Fact]
    [Trait("Id", "SC-F1-1")]
    public void Clock_NegativeRemaining_AssertsOrReadsZeroPerBuild()
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.TickRange, Assert.Throws<SimAssertException>(() => TickTime.Clock(-1L)).Code);
            Assert.Equal(SimAssertCode.TickRange, Assert.Throws<SimAssertException>(() => TickTime.DisplaySeconds(-1L)).Code);
        }
        else
        {
            AssertClock(0, 0, TickTime.Clock(-1L));
            Assert.Equal(0L, TickTime.DisplaySeconds(long.MinValue));
        }
    }
}
