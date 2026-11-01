using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers <see cref="SimCounters"/> and <see cref="CounterId"/> (TR-sim-040; story 007 AC-1).
/// </summary>
public class SimCountersTests
{
    [Fact]
    public void Add_DefaultAndExplicitAmount_IncrementsExactly()
    {
        var counters = new SimCounters();

        counters.Add(CounterId.BadTick);
        Assert.Equal(1L, counters[CounterId.BadTick]);
        counters.Add(CounterId.BadTick, 41);
        Assert.Equal(42L, counters[CounterId.BadTick]);
        Assert.Equal(0L, counters[CounterId.BusyPlayer]);
    }

    [Fact]
    public void TwoInstances_AreIndependent()
    {
        var a = new SimCounters();
        var b = new SimCounters();

        a.Add(CounterId.EventsDropped, 3);

        Assert.Equal(3L, a[CounterId.EventsDropped]);
        Assert.Equal(0L, b[CounterId.EventsDropped]);
    }

    [Fact]
    public void Add_NegativeAmount_DecrementsLevel()
    {
        var counters = new SimCounters();
        counters.Add(CounterId.DrainBacklog, 5);

        counters.Add(CounterId.DrainBacklog, -2);

        Assert.Equal(3L, counters[CounterId.DrainBacklog]);
    }

    [Fact]
    public void Add_Zero_ChangesNothing()
    {
        var counters = new SimCounters();
        counters.Add(CounterId.BadTick, 7);

        counters.Add(CounterId.BadTick, 0);

        Assert.Equal(7L, counters[CounterId.BadTick]);
    }

    [Fact]
    public void Add_LargeAmounts_SaturateWithoutThrowing()
    {
        var counters = new SimCounters();

        counters.Add(CounterId.BadTick, long.MaxValue);
        counters.Add(CounterId.BadTick);
        counters.Add(CounterId.BadTick, long.MaxValue);
        Assert.Equal(long.MaxValue, counters[CounterId.BadTick]);

        counters.Add(CounterId.DrainBacklog, long.MinValue);
        counters.Add(CounterId.DrainBacklog, -1);
        counters.Add(CounterId.DrainBacklog, long.MinValue);
        Assert.Equal(long.MinValue, counters[CounterId.DrainBacklog]);

        counters.Add(CounterId.DrainBacklog, long.MaxValue);
        Assert.Equal(-1L, counters[CounterId.DrainBacklog]);
    }

    [Fact]
    public void UnknownId_WriteDroppedReadZeroNoThrow()
    {
        var counters = new SimCounters();
        var bad = (CounterId)ushort.MaxValue;

        counters.Add(bad, 5);

        Assert.Equal(0L, counters[bad]);
        foreach (CounterId id in Enum.GetValues<CounterId>())
        {
            Assert.Equal(0L, counters[id]);
        }
    }

    [Fact]
    public void CounterId_PrimitiveBlock_MatchesPrimitiveCounterAndIsPinned()
    {
        Assert.Equal(0, (int)CounterId.FixDivZero);
        Assert.Equal(1, (int)CounterId.FixClamped);
        Assert.Equal(2, (int)CounterId.MoneyClamped);
        Assert.Equal(3, (int)CounterId.MoneySaturated);
        Assert.Equal(4, (int)CounterId.FxClamped);
        Assert.Equal(5, (int)CounterId.MoneyBadPermille);
        Assert.Equal((int)PrimitiveCounter.FixDivZero, (int)CounterId.FixDivZero);
        Assert.Equal((int)PrimitiveCounter.FixClamped, (int)CounterId.FixClamped);
        Assert.Equal((int)PrimitiveCounter.MoneyClamped, (int)CounterId.MoneyClamped);
        Assert.Equal((int)PrimitiveCounter.MoneySaturated, (int)CounterId.MoneySaturated);
        Assert.Equal((int)PrimitiveCounter.FxClamped, (int)CounterId.FxClamped);
        Assert.Equal((int)PrimitiveCounter.MoneyBadPermille, (int)CounterId.MoneyBadPermille);
        Assert.Equal(SimCounterSink.RequiredLength, Enum.GetValues<PrimitiveCounter>().Length);
    }

    [Fact]
    public void CounterId_LaterValues_ArePinned()
    {
        Assert.Equal(6, (int)CounterId.BadTick);
        Assert.Equal(7, (int)CounterId.BusyPlayer);
        Assert.Equal(8, (int)CounterId.BusySystem);
        Assert.Equal(9, (int)CounterId.EnqueueBadState);
        Assert.Equal(10, (int)CounterId.EnqueueDuringFlush);
        Assert.Equal(11, (int)CounterId.DrainBacklog);
        Assert.Equal(12, (int)CounterId.EventsDropped);
        Assert.Equal(13, (int)CounterId.EventsOutsideTick);
        Assert.Equal(14, (int)CounterId.EventsAtTickEnd);
        Assert.Equal(15, (int)CounterId.EventsDuringFlush);
        Assert.Equal(16, (int)CounterId.CoalesceLate);
        Assert.Equal(17, (int)CounterId.DumpDuringTick);
        Assert.Equal(18, (int)CounterId.IdAllocIllegal);
        Assert.Equal(19, (int)CounterId.RngIndexOutOfRange);
        Assert.Equal(20, (int)CounterId.StopOutOfPhase);
        Assert.Equal(21, (int)CounterId.RngRefused);
        Assert.Equal(22, (int)CounterId.StopIgnored);
        Assert.Equal(23, (int)CounterId.OpSeqExhausted);
        Assert.Equal(24, (int)CounterId.ScopeLeak);
        Assert.Equal(25, (int)CounterId.ReplyInconsistent);
        Assert.Equal(26, (int)CounterId.WorldSolidQueryOverflow);
        Assert.Equal(27, (int)CounterId.WorldSolidQueryFallback);
        Assert.Equal(28, (int)CounterId.SamplesStale);
        Assert.Equal(29, (int)CounterId.SamplesRefused);
        Assert.Equal(30, (int)CounterId.InputEdgeOverflow);
        Assert.Equal(31, (int)CounterId.CarrierSubSteps);
        Assert.Equal(32, (int)CounterId.CarrierSlideIterations);
        Assert.Equal(33, (int)CounterId.CarrierStepClamped);
        Assert.Equal(34, (int)CounterId.CarrierDepenetrations);
        Assert.Equal(35, (int)CounterId.CarrierCandidateOverflow);
        Assert.Equal(36, (int)CounterId.InputStepDropped);
        Assert.Equal(37, (int)CounterId.StatPoolFull);
        Assert.Equal(38, (int)CounterId.StatWriteRefused);
        Assert.Equal(39, (int)CounterId.HoldInputClamped);
        Assert.Equal(40, (int)CounterId.AnchorRegistryFull);
        Assert.Equal(41, (int)CounterId.AnchorQueryOverflow);
        Assert.Equal(42, (int)CounterId.InventoryMergeCandidates);
        Assert.Equal(43, (int)CounterId.InventoryPlacementProbes);
        Assert.Equal(44, (int)CounterId.InventoryOutfeedCandidates);
        Assert.Equal(45, (int)CounterId.MutatingApiOutOfPhase);
        Assert.Equal(46, (int)CounterId.InventoryWorldItemCapCandidates);
        Assert.Equal(47, (int)CounterId.InventoryWorldItemReturnRetries);
        Assert.Equal(48, (int)CounterId.InventoryWorldItemCapExceeded);
        Assert.Equal(49, (int)CounterId.InventoryItemCountProbes);
        Assert.Equal(50, (int)CounterId.InventoryBacklogVisits);
        Assert.Equal(51, (int)CounterId.InventoryConservationMismatches);
        Assert.Equal(52, (int)CounterId.FocusCandidates);
        Assert.Equal(53, (int)CounterId.FocusCellsVisited);
        Assert.Equal(54, (int)CounterId.InteractPressIgnored);
        Assert.Equal(55, (int)CounterId.FocusOcclusionTests);
        Assert.Equal(56, (int)CounterId.MailRowsVisited);
        Assert.Equal(57, (int)CounterId.MailBucketsVisited);
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(CounterId)));
    }

    [Fact]
    public void Add_EveryDefinedId_IsCountableIndependently()
    {
        var counters = new SimCounters();
        CounterId[] all = Enum.GetValues<CounterId>();

        foreach (CounterId id in all)
        {
            counters.Add(id, (long)id + 1);
        }

        foreach (CounterId id in all)
        {
            Assert.Equal((long)id + 1, counters[id]);
        }

        Assert.Equal((int)all.Max() + 1, counters.Length);
    }

    [Fact]
    public void PrimitiveSink_LengthCoversRequiredBlock()
    {
        var counters = new SimCounters();

        var sink = counters.PrimitiveSink();

        Assert.True(sink.Length >= SimCounterSink.RequiredLength);
    }

    [Fact]
    public void PrimitiveSink_Increments_VisibleThroughIndexer()
    {
        var counters = new SimCounters();
        var sink = counters.PrimitiveSink();

        sink.Increment(PrimitiveCounter.FixDivZero);
        sink.Increment(PrimitiveCounter.MoneyBadPermille);
        sink.Increment(PrimitiveCounter.MoneyBadPermille);

        Assert.Equal(1L, counters[CounterId.FixDivZero]);
        Assert.Equal(2L, counters[CounterId.MoneyBadPermille]);
        Assert.Equal(0L, counters[CounterId.BadTick]);
    }

    [Fact]
    public void PrimitiveSink_NumericsHelper_CountsThroughCounters()
    {
        var counters = new SimCounters();
        var sink = counters.PrimitiveSink();

        long q = Fix.MulDiv(5, 5, 0, ref sink);

        Assert.Equal(0L, q);
        Assert.Equal(1L, counters[CounterId.FixDivZero]);
    }

    [Fact]
    public void PrimitiveSink_DoesNotReachLaterCounters()
    {
        var counters = new SimCounters();
        var sink = counters.PrimitiveSink();

        Assert.Equal(0L, sink.Get((PrimitiveCounter)6));
        sink.Increment((PrimitiveCounter)6);

        Assert.Equal(0L, counters[CounterId.BadTick]);
    }

}

/// <summary>
/// Zero-allocation guardrail for <see cref="SimCounters.Add"/>, in the non-parallel collection so runtime warm-up on
/// other test threads cannot land in a measured window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class SimCountersAllocationTests
{
    [Fact]
    public void Add_WarmedUp_AllocatesNothing()
    {
        // Arrange
        var counters = new SimCounters();

        // Act + Assert (0 B)
        _ = AllocAssert.Zero("1,000 defined and undefined counter adds", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                counters.Add(CounterId.BadTick);
                counters.Add((CounterId)ushort.MaxValue, 3);
            }
        });
    }
}
