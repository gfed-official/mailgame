using System.Runtime.CompilerServices;
using PerformativeMail.Client.Core.Input;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 003 AC-3 and AC-4: the shared <see cref="UiEventLog"/> ring (4096 entries, overwrite-oldest, coalesced
/// motion, reader API) and its 0 B guarantee. Deterministic: no I/O, no wall clock. Non-parallel because of the allocation
/// tests.
/// </summary>
[Collection(NonParallelCollection.Name)]
[Trait("Id", "TR-input-019")]
public class UiEventLogTests
{
    private static void WriteN(UiEventLog log, int count, int firstId = 0)
    {
        for (int i = 0; i < count; i++)
        {
            log.Write(i, LogKind.GateFired, 0, (ushort)(firstId + i), 0, 0);
        }
    }

    // ------------------------------------------------------------ AC-3 ring

    [Fact]
    public void NewLog_IsEmptyWithCapacity4096()
    {
        var log = new UiEventLog();

        Assert.Equal(4096, UiEventLog.Capacity);
        Assert.Equal(0, log.Count);
        Assert.Equal(0, log.OverwriteCount);
        Assert.False(log.TryGet(0, out _));
    }

    [Fact]
    public void Write_StoresEveryFieldAndAssignsASequenceFromZero()
    {
        var log = new UiEventLog();

        log.Write(1234, LogKind.HoldCancel, 7, 0x1111, 0x2222, 0x3333, 5);
        log.Write(1300, LogKind.Cue, 1, 1, 2, 3);

        Assert.True(log.TryGet(0, out UiEventEntry first));
        Assert.Equal(0u, first.Seq);
        Assert.Equal(1234, first.TimeMs);
        Assert.Equal(LogKind.HoldCancel, first.Kind);
        Assert.Equal(7, first.Reason);
        Assert.Equal(0x1111, first.IdA);
        Assert.Equal(0x2222, first.IdB);
        Assert.Equal(0x3333, first.IdC);
        Assert.Equal(5, first.Flags);
        Assert.True(log.TryGet(1, out UiEventEntry second));
        Assert.Equal(1u, second.Seq);
        Assert.Equal(0, second.Flags);
    }

    [Fact]
    public void Write_BelowCapacity_KeepsEverythingInWriteOrder()
    {
        var log = new UiEventLog();

        WriteN(log, 10);

        Assert.Equal(10, log.Count);
        Assert.Equal(0, log.OverwriteCount);
        for (int i = 0; i < 10; i++)
        {
            Assert.True(log.TryGet(i, out UiEventEntry e));
            Assert.Equal(i, e.IdA);
        }
    }

    [Fact]
    [Trait("Id", "H-R11-4")]
    public void Write_4097Entries_DropsTheOldestAndEnumeratesOldestToNewest()
    {
        var log = new UiEventLog();

        WriteN(log, 4097);

        Assert.Equal(4096, log.Count);
        Assert.Equal(1, log.OverwriteCount);
        Assert.Equal(4097, log.TotalWritten);
        Assert.True(log.TryGet(0, out UiEventEntry oldest));
        Assert.Equal(1u, oldest.Seq);
        Assert.Equal(1, oldest.IdA);
        for (int i = 0; i < 4096; i++)
        {
            Assert.True(log.TryGet(i, out UiEventEntry e));
            Assert.Equal((uint)(i + 1), e.Seq);
            Assert.Equal((ushort)(i + 1), e.IdA);
        }

        Assert.True(log.TryGet(4095, out UiEventEntry newest));
        Assert.Equal(4096u, newest.Seq);
        Assert.False(log.TryGet(4096, out _));
    }

    [Fact]
    public void Write_ExactlyCapacityEntries_OverwritesNothing()
    {
        var log = new UiEventLog();

        WriteN(log, 4096);

        Assert.Equal(4096, log.Count);
        Assert.Equal(0, log.OverwriteCount);
        Assert.True(log.TryGet(0, out UiEventEntry first));
        Assert.Equal(0u, first.Seq);
    }

    [Fact]
    public void Write_ThreeLapsOfTheRing_KeepsTheNewest4096InOrder()
    {
        var log = new UiEventLog();

        WriteN(log, 3 * 4096 + 10);

        Assert.Equal(4096, log.Count);
        Assert.Equal(2 * 4096 + 10, log.OverwriteCount);
        Assert.True(log.TryGet(0, out UiEventEntry oldest));
        Assert.Equal((uint)(2 * 4096 + 10), oldest.Seq);
        Assert.True(log.TryGet(4095, out UiEventEntry newest));
        Assert.Equal((uint)(3 * 4096 + 9), newest.Seq);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void TryGet_OutOfRange_ReturnsFalseAndDefault(int index)
    {
        var log = new UiEventLog();
        WriteN(log, 5);

        bool ok = log.TryGet(index, out UiEventEntry e);

        Assert.False(ok);
        Assert.Equal(LogKind.None, e.Kind);
        Assert.Equal(0u, e.Seq);
    }

    [Fact]
    public void Clear_EmptiesTheRingAndRestartsTheSequence()
    {
        var log = new UiEventLog();
        WriteN(log, 4100);

        log.Clear();
        log.Write(5, LogKind.Config, 0, 9, 0, 0);

        Assert.Equal(1, log.Count);
        Assert.Equal(0, log.OverwriteCount);
        Assert.True(log.TryGet(0, out UiEventEntry e));
        Assert.Equal(0u, e.Seq);
        Assert.Equal(9, e.IdA);
        Assert.False(log.TryGet(1, out _));
    }

    [Fact]
    public void LogKind_HasTheSpecifiedMembersWithStableValues()
    {
        string[] names =
        [
            "RawInput", "GateFired", "GateNotLive", "GateMuted", "GateSwallowed", "Transition",
            "HoldStart", "HoldComplete", "HoldCancel", "Cue", "Dropped", "Config",
        ];

        Assert.Equal(0, (byte)LogKind.None);
        for (int i = 0; i < names.Length; i++)
        {
            Assert.True(Enum.TryParse(names[i], out LogKind kind), names[i]);
            Assert.Equal(i + 1, (byte)kind);
        }

        Assert.Equal(names.Length + 1, Enum.GetValues<LogKind>().Length);
    }

    // ------------------------------------------------------------ AC-3 coalesced motion

    [Fact]
    public void LogMotion_ManyCallsInOneFrame_KeepsOneEntryWithSummedCounts()
    {
        var log = new UiEventLog();

        log.LogMotion(12, 100, 3, -1);
        log.LogMotion(12, 105, 2, 0);
        log.LogMotion(12, 110, -4, 6);

        Assert.Equal(1, log.Count);
        Assert.True(log.TryGet(0, out UiEventEntry e));
        Assert.Equal(LogKind.RawInput, e.Kind);
        Assert.Equal((byte)RawKind.MouseMotion, e.Reason);
        Assert.Equal(UiEventEntry.FlagCoalescedMotion, e.Flags);
        Assert.Equal(1, e.MotionDx);
        Assert.Equal(5, e.MotionDy);
        Assert.Equal(3, e.IdC);
        Assert.Equal(100, e.TimeMs);
    }

    [Fact]
    public void LogMotion_OnEachNewFrame_AppendsOneEntryPerFrame()
    {
        var log = new UiEventLog();

        for (int frame = 0; frame < 5; frame++)
        {
            log.LogMotion(frame, frame * 16, 1, 1);
            log.LogMotion(frame, frame * 16 + 5, 1, 1);
        }

        Assert.Equal(5, log.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.True(log.TryGet(i, out UiEventEntry e));
            Assert.Equal(2, e.MotionDx);
            Assert.Equal(2, e.IdC);
        }
    }

    [Fact]
    public void LogMotion_AfterOtherEntriesInTheSameFrame_UpdatesTheEarlierMotionEntryInPlace()
    {
        var log = new UiEventLog();
        log.LogMotion(3, 10, 1, 0);
        log.Write(11, LogKind.GateFired, 0, 1, 0, 0);

        log.LogMotion(3, 12, 1, 0);

        Assert.Equal(2, log.Count);
        Assert.True(log.TryGet(0, out UiEventEntry motion));
        Assert.Equal(LogKind.RawInput, motion.Kind);
        Assert.Equal(2, motion.MotionDx);
        Assert.True(log.TryGet(1, out UiEventEntry fired));
        Assert.Equal(LogKind.GateFired, fired.Kind);
    }

    [Fact]
    public void LogMotion_SumsBeyondSixteenBits_Saturate()
    {
        var log = new UiEventLog();

        log.LogMotion(1, 0, 30000, -30000);
        log.LogMotion(1, 0, 30000, -30000);
        log.LogMotion(1, 0, int.MaxValue, int.MinValue);

        Assert.True(log.TryGet(0, out UiEventEntry e));
        Assert.Equal(short.MaxValue, e.MotionDx);
        Assert.Equal(short.MinValue, e.MotionDy);
    }

    [Fact]
    public void LogMotion_MergedEventCount_SaturatesAtUShortMax()
    {
        var log = new UiEventLog();

        for (int i = 0; i < 70000; i++)
        {
            log.LogMotion(1, 0, 0, 0);
        }

        Assert.Equal(1, log.Count);
        Assert.True(log.TryGet(0, out UiEventEntry e));
        Assert.Equal(ushort.MaxValue, e.IdC);
    }

    [Fact]
    public void LogMotion_WhenTheFramesEntryWasOverwritten_AppendsAFreshEntry()
    {
        var log = new UiEventLog();
        log.LogMotion(9, 1, 1, 1);
        WriteN(log, 4096);

        log.LogMotion(9, 2, 1, 1);

        Assert.Equal(4096, log.Count);
        Assert.True(log.TryGet(4095, out UiEventEntry newest));
        Assert.Equal((byte)RawKind.MouseMotion, newest.Reason);
        Assert.Equal(1, newest.IdC);
        Assert.Equal(2, newest.TimeMs);
        Assert.True(log.TryGet(0, out UiEventEntry oldest));
        Assert.Equal(LogKind.GateFired, oldest.Kind);
    }

    [Fact]
    public void LogMotion_AfterClear_StartsANewEntryEvenInTheSameFrame()
    {
        var log = new UiEventLog();
        log.LogMotion(4, 0, 5, 5);

        log.Clear();
        log.LogMotion(4, 0, 1, 1);

        Assert.Equal(1, log.Count);
        Assert.True(log.TryGet(0, out UiEventEntry e));
        Assert.Equal(1, e.MotionDx);
        Assert.Equal(1, e.IdC);
    }

    // ------------------------------------------------------------ AC-4 allocation and size

    [Fact]
    public void UiEventEntry_IsAtMost64Bytes()
    {
        Assert.True(Unsafe.SizeOf<UiEventEntry>() <= 64, $"UiEventEntry is {Unsafe.SizeOf<UiEventEntry>()} B");
    }

    [Fact]
    public void UiEventEntry_IsBlittable_HoldsNoReferences()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UiEventEntry>());
    }

    [Fact]
    public void WriteAndLogMotion_100000Calls_Allocate0Bytes()
    {
        var log = new UiEventLog();
        for (int i = 0; i < 300; i++)
        {
            log.Write(i, LogKind.GateFired, 1, 2, 3, 4);
            log.LogMotion(i, i, 1, 1);
        }

        // 100 000 calls per window: 50 000 Write + 50 000 LogMotion across 5 000 frames; wraps the ring many times.
        _ = AllocAssert.Zero("UiEventLog.Write / LogMotion", () =>
        {
            for (int i = 0; i < 50_000; i++)
            {
                log.Write(i, LogKind.GateFired, 1, 2, 3, 4);
                log.LogMotion(i / 10, i, 1, 1);
            }
        });

        Assert.Equal(4096, log.Count);
    }
}
