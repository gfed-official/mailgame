using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Covers <see cref="SimCheckRing"/> and <see cref="CheckCode"/> (TR-sim-040; story 007 AC-2, AC-3).
/// </summary>
public class SimCheckRingTests
{
    private static readonly CheckCode[] _codes = Enum.GetValues<CheckCode>();

    [Fact]
    public void Record_EveryDefinedCode_RateLimitsIndependently()
    {
        var ring = new SimCheckRing();

        foreach (CheckCode code in _codes)
        {
            ring.Record(code, 0, 0, 0);
            ring.Record(code, 10, 0, 0);
        }

        Assert.Equal(_codes.Length, ring.Count);
        foreach (CheckCode code in _codes)
        {
            Assert.Equal(1u, ring.Suppressed(code));
        }

        // An undefined code must not share a slot with any defined one.
        ring.Record((CheckCode)((int)_codes.Max() + 1), 10, 0, 0);
        Assert.Equal(_codes.Length + 1, ring.Count);
    }

    [Fact]
    public void Record_SameCodeAtTicks0_10_29_30_31_60_StoresOnly0_30_60()
    {
        var ring = new SimCheckRing();

        foreach (uint tick in new uint[] { 0, 10, 29, 30, 31, 60 })
        {
            ring.Record(CheckCode.TickHitch, tick, tick, 0);
        }

        Assert.Equal(3, ring.Count);
        Assert.Equal(0u, ring[0].Tick);
        Assert.Equal(30u, ring[1].Tick);
        Assert.Equal(60u, ring[2].Tick);
        Assert.Equal(3u, ring.Suppressed(CheckCode.TickHitch));
    }

    [Fact]
    public void Record_DifferentCodeAtTick10_StoredIndependently()
    {
        var ring = new SimCheckRing();
        ring.Record(CheckCode.TickHitch, 0, 0, 0);

        ring.Record(CheckCode.BadTick, 10, 0, 0);

        Assert.Equal(2, ring.Count);
        Assert.Equal((ushort)CheckCode.BadTick, ring[1].Code);
        Assert.Equal(0u, ring.Suppressed(CheckCode.TickHitch));
        Assert.Equal(0u, ring.Suppressed(CheckCode.BadTick));
    }

    [Fact]
    public void Record_KeepsPayloads()
    {
        var ring = new SimCheckRing();

        ring.Record(CheckCode.IdExhausted, 7, long.MinValue, long.MaxValue);

        SimCheckRecord r = ring[0];
        Assert.Equal(CheckCode.IdExhausted, r.Check);
        Assert.Equal(7u, r.Tick);
        Assert.Equal(long.MinValue, r.A);
        Assert.Equal(long.MaxValue, r.B);
    }

    [Fact]
    public void Record_CustomRateLimit_IsHonoured()
    {
        var ring = new SimCheckRing(capacity: 8, rateLimitTicks: 5);

        foreach (uint tick in new uint[] { 0, 4, 5, 9, 10 })
        {
            ring.Record(CheckCode.BadTick, tick, 0, 0);
        }

        Assert.Equal(5, ring.RateLimitTicks);
        Assert.Equal(3, ring.Count);
        Assert.Equal(10u, ring[2].Tick);
        Assert.Equal(2u, ring.Suppressed(CheckCode.BadTick));
    }

    [Fact]
    public void Record_ZeroRateLimit_StoresEverything()
    {
        var ring = new SimCheckRing(capacity: 8, rateLimitTicks: 0);

        for (int i = 0; i < 5; i++)
        {
            ring.Record(CheckCode.BadTick, 3, 0, 0);
        }

        Assert.Equal(5, ring.Count);
        Assert.Equal(0u, ring.Suppressed(CheckCode.BadTick));
    }

    [Fact]
    public void Record_TickNearUIntMax_RateLimitMathDoesNotWrap()
    {
        var ring = new SimCheckRing();

        ring.Record(CheckCode.TickHitch, uint.MaxValue - 30, 0, 0);
        ring.Record(CheckCode.TickHitch, uint.MaxValue - 1, 0, 0);
        ring.Record(CheckCode.TickHitch, uint.MaxValue, 0, 0);

        Assert.Equal(2, ring.Count);
        Assert.Equal(uint.MaxValue - 30, ring[0].Tick);
        Assert.Equal(uint.MaxValue, ring[1].Tick);
        Assert.Equal(1u, ring.Suppressed(CheckCode.TickHitch));
    }

    [Fact]
    public void Record_TickGoesBackwards_RearmsInsteadOfSuppressingForever()
    {
        var ring = new SimCheckRing();
        ring.Record(CheckCode.TickHitch, uint.MaxValue, 0, 0);

        ring.Record(CheckCode.TickHitch, 0, 0, 0);
        ring.Record(CheckCode.TickHitch, 5, 0, 0);

        Assert.Equal(2, ring.Count);
        Assert.Equal(0u, ring[1].Tick);
        Assert.Equal(1u, ring.Suppressed(CheckCode.TickHitch));
    }

    [Fact]
    public void Record_300DistinctRecords_KeepsNewest256InOrder()
    {
        var ring = new SimCheckRing();

        for (uint i = 0; i < 300; i++)
        {
            // Rotate through the defined codes (distinct (code, tick) pairs); ticks 30 apart so none is rate limited.
            ring.Record(_codes[(int)(i % (uint)_codes.Length)], i * 30, i, -(long)i);
        }

        Assert.Equal(256, ring.Count);
        Assert.Equal(256, ring.Capacity);
        Assert.Equal(44u, ring.Overwritten);
        for (int i = 0; i < 256; i++)
        {
            Assert.Equal((long)(i + 44), ring[i].A);
            Assert.Equal((uint)(i + 44) * 30, ring[i].Tick);
            Assert.Equal((ushort)_codes[(i + 44) % _codes.Length], ring[i].Code);
        }
    }

    [Fact]
    public void Record_ExactlyCapacity_NothingOverwritten()
    {
        var ring = new SimCheckRing(capacity: 4, rateLimitTicks: 0);

        for (uint i = 0; i < 4; i++)
        {
            ring.Record(CheckCode.BadTick, i, i, 0);
        }

        Assert.Equal(4, ring.Count);
        Assert.Equal(0u, ring.Overwritten);
        Assert.Equal(0L, ring[0].A);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var ring = new SimCheckRing(capacity: 4, rateLimitTicks: 0);
        ring.Record(CheckCode.BadTick, 0, 0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => ring[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimCheckRing()[0]);
    }

    [Fact]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimCheckRing(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimCheckRing(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimCheckRing(4, -1));
    }

    [Fact]
    public void Record_MaxCodeValue_DoesNotThrowAndKeepsRawCode()
    {
        var ring = new SimCheckRing();
        var max = (CheckCode)ushort.MaxValue;

        ring.Record(max, 1, 2, 3);
        ring.Record(max, 2, 2, 3);

        Assert.Equal(1, ring.Count);
        Assert.Equal(ushort.MaxValue, ring[0].Code);
        Assert.Equal(1u, ring.Suppressed(max));
    }

    [Fact]
    public void Record_UndefinedCodes_ShareOneRateLimitSlotSeparateFromDefinedCodes()
    {
        var ring = new SimCheckRing();

        ring.Record((CheckCode)200, 0, 0, 0);
        ring.Record((CheckCode)201, 5, 0, 0);
        ring.Record(CheckCode.BadTick, 5, 0, 0);

        Assert.Equal(2, ring.Count);
        Assert.Equal(1u, ring.Suppressed((CheckCode)999));
        Assert.Equal(0u, ring.Suppressed(CheckCode.BadTick));
    }

    [Fact]
    public void CheckCode_Values_ArePinned()
    {
        Assert.Equal(0, (int)CheckCode.None);
        Assert.Equal(1, (int)CheckCode.TickHitch);
        Assert.Equal(2, (int)CheckCode.IdExhausted);
        Assert.Equal(3, (int)CheckCode.BusySystem);
        Assert.Equal(4, (int)CheckCode.OpSeqExhausted);
        Assert.Equal(5, (int)CheckCode.BadTick);
        Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(CheckCode)));
    }

    [Fact]
    public void SimCheckRecord_Is24Bytes()
    {
        Assert.Equal(24, Unsafe.SizeOf<SimCheckRecord>());
    }

    internal static void Exercise(SimCheckRing ring)
    {
        for (uint i = 0; i < 300; i++)
        {
            ring.Record(CheckCode.TickHitch, i * 30, i, 0);
            ring.Record((CheckCode)ushort.MaxValue, i, 0, 0);
        }
    }
}

/// <summary>
/// Allocation checks for <see cref="SimCheckRing"/> (story 007 AC-3), in the non-parallel collection so runtime warm-up
/// on other test threads cannot land in a measured window.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class SimCheckRingAllocationTests
{
    [Fact]
    public void Record_WarmedUp_AllocatesNothingAndNeverThrows()
    {
        // Arrange
        var ring = new SimCheckRing();

        // Act + Assert (0 B; an exception propagates and fails the test)
        _ = AllocAssert.Zero("20 x 600 Record calls, including an undefined code", () =>
        {
            for (int i = 0; i < 20; i++)
            {
                SimCheckRingTests.Exercise(ring);
            }
        });

        // Assert
        Assert.Equal(256, ring.Count);
    }

    [Fact]
    public void Construction_Allocation_IsRoughlyRecordArraySize()
    {
        // Arrange
        SimCheckRing? ring = null;

        // Act
        (long minBytes, _) = AllocAssert.Measure(() => ring = new SimCheckRing());

        // Assert
        Assert.True(minBytes >= 256 * 24, $"constructed ring allocated {minBytes} B, expected at least {256 * 24} B");
        Assert.True(minBytes < 256 * 24 + 1024, $"constructed ring allocated {minBytes} B, expected under {(256 * 24) + 1024} B");
        GC.KeepAlive(ring);
    }
}
