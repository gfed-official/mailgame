using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Hosting;

/// <summary>
/// Covers TR-sim-003/004/006/009 and GDD SC-F2-1 (F2 catch-up accumulator) for <see cref="TickDriver"/>:
/// one test per SC-F2-1 row (AC-1), the driver states (AC-3) and DropRemaining/AlphaPermille. The F2 invariant
/// property test (AC-2) is <see cref="TickDriverInvariantTests"/>. Integer timestamps only; no clock is read.
/// Resync outside Running asserts under SIM_ASSERT builds and is ignored otherwise, chosen by
/// <see cref="TestBuildExpectation.SimAssertsExpected"/>.
/// </summary>
public class TickDriverTests
{
    private const long Freq = 10_000_000;
    private const int Cap = 4;

    internal static TickDriver Started(long now = 0)
    {
        var d = new TickDriver(Freq, Cap);
        d.Start(now);
        return d;
    }

    // ---- AC-1 SC-F2-1 rows ----

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Row_150ms_Runs4_DropsNone_AlphaHalf()
    {
        TickDriver d = Started();

        int n = d.Advance(1_500_000);

        Assert.Equal(4, n);
        Assert.Equal(0L, d.DroppedTicksTotal);
        Assert.Equal(5_000_000L, d.Acc30);
        Assert.Equal(500, d.AlphaPermille);
        Assert.Equal(0L, d.Hitches);
        Assert.Equal(4L, d.TicksRunTotal);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Row_150msAgain_Runs4_Drops1_AccZero_AlphaZero()
    {
        TickDriver d = Started();
        d.Advance(1_500_000);

        int n = d.Advance(3_000_000);

        Assert.Equal(4, n);
        Assert.Equal(1L, d.DroppedTicksTotal);
        Assert.Equal(0L, d.Acc30);
        Assert.Equal(0, d.AlphaPermille);
        Assert.Equal(1L, d.Hitches);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Row_400ms_Runs4_Drops8_OneHitch()
    {
        TickDriver d = Started();

        int n = d.Advance(4_000_000);

        Assert.Equal(4, n);
        Assert.Equal(8L, d.DroppedTicksTotal);
        Assert.Equal(1L, d.Hitches);
        Assert.Equal(0L, d.Acc30);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Row_5s_Runs4_Drops26_ClampedGrowsBy4s()
    {
        TickDriver d = Started();

        int n = d.Advance(50_000_000);

        Assert.Equal(4, n);
        Assert.Equal(26L, d.DroppedTicksTotal);
        Assert.Equal(4L * Freq, d.ClampedTimeTotal);
        Assert.Equal(1L, d.Hitches);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Row_BackwardsTimestamp_Returns0_AddsNothing_LastUnchanged_Counted()
    {
        TickDriver d = Started(1_000_000);
        d.Advance(2_000_000);
        TickDriverCounters before = d.Counters;
        long accBefore = d.Acc30;

        int n = d.Advance(1_500_000);

        Assert.Equal(0, n);
        Assert.Equal(2_000_000L, d.Last);
        Assert.Equal(accBefore, d.Acc30);
        Assert.Equal(before.BackwardsClock + 1, d.BackwardsClock);
        Assert.Equal(before with { BackwardsClock = before.BackwardsClock + 1 }, d.Counters);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Row_BackwardsThenForward_MeasuresFromUnmovedLast()
    {
        TickDriver d = Started(0);
        d.Advance(1_000_000);
        d.Advance(500_000);

        // 1_000_000 -> 1_500_000 is 50 ms (1.5 ticks), not 100 ms: last was never moved back.
        int n = d.Advance(1_500_000);

        Assert.Equal(1, n);
        Assert.Equal(4L, d.TicksRunTotal);
        Assert.Equal(5_000_000L, d.Acc30);
        Assert.Equal(1L, d.BackwardsClock);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Row_600Frames_Of166667_AlternateZeroOne_AlphaExactHalfThenZero()
    {
        TickDriver d = Started();
        long now = 0;

        for (int i = 0; i < 600; i++)
        {
            now += 166_667;
            int n = d.Advance(now);

            if (i % 2 == 0)
            {
                Assert.Equal(0, n);
                Assert.Equal(500, d.AlphaPermille);
            }
            else
            {
                Assert.Equal(1, n);
                Assert.Equal(0, d.AlphaPermille);
            }
        }

        Assert.Equal(0L, d.DroppedTicksTotal);
        Assert.Equal(300L, d.TicksRunTotal);
        Assert.Equal(0L, d.Hitches);
    }

    [Theory]
    [Trait("Id", "SC-F2-1")]
    [InlineData(999L)]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(1_000_000_000_001L)]
    [InlineData(long.MaxValue)]
    public void Ctor_FrequencyOutOfRange_Throws(long frequency)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickDriver(frequency, 4));
    }

    [Theory]
    [Trait("Id", "SC-F2-1")]
    [InlineData(1_000L)]
    [InlineData(10_000_000L)]
    [InlineData(1_000_000_000_000L)]
    public void Ctor_FrequencyInRange_Succeeds(long frequency)
    {
        var d = new TickDriver(frequency, 4);

        Assert.Equal(frequency, d.Frequency);
        Assert.Equal(4, d.MaxCatchUpTicksPerFrame);
        Assert.Equal(TickDriverState.Idle, d.State);
    }

    [Theory]
    [Trait("Id", "SC-F2-1")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Ctor_CapBelowOne_Throws(int cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickDriver(Freq, cap));
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Ctor_CapOfOne_Succeeds_AndLimitsToOneTick()
    {
        var d = new TickDriver(Freq, 1);
        d.Start(0);

        Assert.Equal(1, d.Advance(1_500_000));
        Assert.Equal(3L, d.DroppedTicksTotal);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Extremes_FrequencyBounds_DoNotOverflow()
    {
        foreach (long freq in new[] { TickDriver.MinFrequency, TickDriver.MaxFrequency })
        {
            var d = new TickDriver(freq, 4);
            d.Start(0);

            int n = d.Advance(long.MaxValue / 2);

            Assert.Equal(4, n);
            Assert.Equal(26L, d.DroppedTicksTotal);
            Assert.InRange(d.Acc30, 0L, freq - 1);
            Assert.InRange(d.AlphaPermille, 0, 999);
        }
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Advance_ExactlyOneTick_RunsOneAndNoDrop()
    {
        // 10^3 Hz clock: one tick is 1000/30 counts; 100 counts is exactly 3 ticks of 1/30 s worth 3000/30.
        var d = new TickDriver(1_000, 4);
        d.Start(0);

        Assert.Equal(0, d.Advance(33));
        Assert.Equal(1, d.Advance(34)); // 34 * 30 = 1020 >= 1000
        Assert.Equal(20L, d.Acc30);
    }

    // ---- DropRemaining / AlphaPermille ----

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void DropRemaining_MovesRunToDropped_AccUnchanged()
    {
        TickDriver d = Started();
        d.Advance(1_500_000);
        long acc = d.Acc30;

        d.DropRemaining(3);

        Assert.Equal(1L, d.TicksRunTotal);
        Assert.Equal(3L, d.DroppedTicksTotal);
        Assert.Equal(acc, d.Acc30);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void DropRemaining_Zero_IsNoOp()
    {
        TickDriver d = Started();
        d.Advance(1_500_000);
        TickDriverCounters before = d.Counters;

        d.DropRemaining(0);

        Assert.Equal(before, d.Counters);
    }

    [Theory]
    [Trait("Id", "SC-F2-1")]
    [InlineData(-1)]
    [InlineData(5)]
    public void DropRemaining_OutOfRange_AssertsOrClampsPerBuild(int m)
    {
        TickDriver d = Started();
        d.Advance(1_500_000); // 4 run

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.Throws<SimAssertException>(() => d.DropRemaining(m));
            Assert.Equal(SimAssertCode.TickDriverDropRange, ex.Code);
            Assert.Equal(4L, d.TicksRunTotal);
        }
        else
        {
            d.DropRemaining(m);
            Assert.Equal(m < 0 ? 4L : 0L, d.TicksRunTotal);
            Assert.Equal(m < 0 ? 0L : 4L, d.DroppedTicksTotal);
        }
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void AssertCodes_AreAppendOnlyValues()
    {
        Assert.Equal((ushort)17, SimAssertCode.TickDriverResyncState);
        Assert.Equal((ushort)18, SimAssertCode.TickDriverDropRange);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void AlphaPermille_StaysWithin0To999()
    {
        TickDriver d = Started();
        long now = 0;
        for (int i = 0; i < 2000; i++)
        {
            now += 99_991;
            d.Advance(now);
            Assert.InRange(d.AlphaPermille, 0, 999);
        }
    }

    // ---- AC-3 driver states ----

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Idle_BeforeStart_AdvanceReturnsZeroAndTouchesNothing()
    {
        var d = new TickDriver(Freq, Cap);

        Assert.Equal(0, d.Advance(5_000_000));
        Assert.Equal(0, d.Advance(1));
        Assert.Equal(default, d.Counters);
        Assert.Equal(0L, d.Acc30);
        Assert.Equal(0, d.AlphaPermille);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Idle_AfterStop_AdvanceReturnsZero_CountersKept()
    {
        TickDriver d = Started();
        d.Advance(1_500_000);
        TickDriverCounters before = d.Counters;

        d.Stop();

        Assert.Equal(TickDriverState.Idle, d.State);
        Assert.Equal(0, d.Advance(9_000_000));
        Assert.Equal(before, d.Counters);
        Assert.Equal(0, d.AlphaPermille);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Paused_AdvanceReturnsZero_AccumulatesNothing_AlphaZero()
    {
        TickDriver d = Started();
        d.Advance(1_500_000);
        TickDriverCounters before = d.Counters;
        long last = d.Last;

        d.Pause();

        Assert.Equal(TickDriverState.Paused, d.State);
        Assert.Equal(0, d.Advance(9_000_000));
        Assert.Equal(0, d.Advance(10_000_000));
        Assert.Equal(before, d.Counters);
        Assert.Equal(last, d.Last);
        Assert.Equal(0, d.AlphaPermille);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Resume_ClearsAccumulator_NoBurst()
    {
        // At 10^3 Hz one tick needs 1000/30 = 33.33 counts, so t0 + 33 is not yet due and t0 + 34 is.
        var d = new TickDriver(1_000, 4);
        d.Start(0);
        d.Pause();
        const long T0 = 50_000;

        d.Resume(T0);

        Assert.Equal(TickDriverState.Running, d.State);
        Assert.Equal(0, d.Advance(T0 + 33));
        Assert.Equal(1, d.Advance(T0 + 34));
        Assert.Equal(0L, d.DroppedTicksTotal);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Resume_AtTickBoundary_AtTenToTheSeventh()
    {
        // freq 3 000 000 gives an integer tick of 100 000 counts: Advance(t0 + tick - 1) -> 0, Advance(t0 + tick) -> 1.
        const long F = 3_000_000;
        const long TickCounts = F / TickRate.Hz;
        var d = new TickDriver(F, 4);
        d.Start(0);
        d.Advance(1_234_567);
        d.Pause();
        const long T0 = 77_000_000;

        d.Resume(T0);

        Assert.Equal(0L, d.Acc30);
        Assert.Equal(0, d.Advance(T0 + TickCounts - 1));
        Assert.Equal(1, d.Advance(T0 + TickCounts));
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void PauseAndResume_WhenAlreadyInThatState_AreNoOps()
    {
        TickDriver d = Started();
        d.Advance(1_500_000);
        long acc = d.Acc30;
        long last = d.Last;

        d.Resume(9_999_999); // Running: ignored
        Assert.Equal(TickDriverState.Running, d.State);
        Assert.Equal(acc, d.Acc30);
        Assert.Equal(last, d.Last);

        d.Pause();
        d.Pause();
        Assert.Equal(TickDriverState.Paused, d.State);

        d.Resume(2_000_000);
        long lastAfter = d.Last;
        d.Resume(8_000_000); // already Running: must not move last or clear acc again
        Assert.Equal(TickDriverState.Running, d.State);
        Assert.Equal(lastAfter, d.Last);
        Assert.Equal(2_000_000L, d.Last);

        var idle = new TickDriver(Freq, Cap);
        idle.Pause();
        idle.Resume(5);
        Assert.Equal(TickDriverState.Idle, idle.State);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Resync_InRunning_ClearsAcc_ChangesNoCounter()
    {
        TickDriver d = Started();
        d.Advance(1_500_000);
        d.Advance(1_000_000); // backwards, so BackwardsClock is non-zero
        TickDriverCounters before = d.Counters;
        Assert.NotEqual(0L, d.Acc30);

        d.Resync(40_000_000);

        Assert.Equal(0L, d.Acc30);
        Assert.Equal(40_000_000L, d.Last);
        Assert.Equal(before, d.Counters);
        Assert.Equal(TickDriverState.Running, d.State);
        Assert.Equal(0, d.AlphaPermille);
    }

    [Theory]
    [Trait("Id", "SC-F2-1")]
    [InlineData(0)]
    [InlineData(1)]
    public void Resync_OutsideRunning_AssertsOrIsIgnoredPerBuild(int mode)
    {
        TickDriver d = Started();
        d.Advance(1_500_000);
        if (mode == 0)
        {
            d.Stop();
        }
        else
        {
            d.Pause();
        }

        TickDriverState state = d.State;
        long last = d.Last;
        TickDriverCounters before = d.Counters;

        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.Throws<SimAssertException>(() => d.Resync(9_000_000));
            Assert.Equal(SimAssertCode.TickDriverResyncState, ex.Code);
        }
        else
        {
            d.Resync(9_000_000);
        }

        Assert.Equal(state, d.State);
        Assert.Equal(last, d.Last);
        Assert.Equal(before, d.Counters);
    }

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Start_FromAnyState_RebasesAndRuns_KeepingCounters()
    {
        TickDriver d = Started();
        d.Advance(1_500_000);
        TickDriverCounters before = d.Counters;

        d.Start(7_000_000);

        Assert.Equal(TickDriverState.Running, d.State);
        Assert.Equal(7_000_000L, d.Last);
        Assert.Equal(0L, d.Acc30);
        Assert.Equal(before, d.Counters);
    }

    // ---- Guardrails ----

    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void TickDriver_HasNoStaticMutableState_AndNoFloatingPointMembers()
    {
        const System.Reflection.BindingFlags All =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly;

        foreach (System.Reflection.FieldInfo f in typeof(TickDriver).GetFields(All))
        {
            Assert.True(!f.IsStatic || f.IsLiteral || f.IsInitOnly, f.Name + " is mutable static state");
            Assert.NotEqual(typeof(double), f.FieldType);
            Assert.NotEqual(typeof(float), f.FieldType);
        }

        Assert.Equal(typeof(int), typeof(TickDriver).GetProperty(nameof(TickDriver.AlphaPermille))!.PropertyType);
    }
}

/// <summary>
/// Zero-allocation guardrail for <see cref="TickDriver.Advance"/>, in the non-parallel collection so runtime warm-up on
/// other test threads cannot land in a measured window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class TickDriverAllocationTests
{
    [Fact]
    [Trait("Id", "SC-F2-1")]
    public void Advance_WarmedUp_AllocatesNothing()
    {
        // TD-008: allocation counts are meaningful only for an optimised Sim.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(TickDriver).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        // Arrange
        TickDriver d = TickDriverTests.Started();
        long now = 0;
        long sink = 0;

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("1,000 Advance calls with periodic hitches", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                now += (i % 7 == 0) ? 50_000_000 : 300_000;
                sink += d.Advance(now) + d.AlphaPermille;
            }
        });

        // Assert
        Assert.NotEqual(long.MinValue, sink);
    }
}
