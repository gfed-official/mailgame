using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Sim.Core;

/// <summary>
/// Story 021: the transform sample ring, the tick-prologue latch, stamping, ordering, recording and the dump block
/// (GDD R5, ADR-0003 section 2, ADR-0006 sections 4 and 7).
/// </summary>
public class SampleLatchTests
{
    private static PlayerSlot Slot(byte v) => PlayerSlot.FromValue(v);

    private static TransformSample Sample(uint seq, int x = 0) => new() { Seq = seq, PosXMm = x };

    private sealed class SampleRecorder : IOpRecorder
    {
        public List<(uint Tick, byte Slot, uint Seq, int X)> Samples { get; } = new();

        public void OnEnqueued(uint boundaryTick, in Op op)
        {
        }

        public void OnDrained(uint applyTick, in Op op, in Reply reply)
        {
        }

        public void OnSample(uint receivedTick, PlayerSlot slot, in TransformSample sample) =>
            Samples.Add((receivedTick, slot.Value, sample.Seq, sample.PosXMm));

        public void OnOverflow(uint tick)
        {
        }
    }

    /// <summary>A phase-3 reader that snapshots the latch each tick, and optionally runs an action.</summary>
    private sealed class Reader : ISimPhase
    {
        public Func<ISampleLatch>? Latch;
        public Action? Inside;
        public List<List<(byte Slot, uint Seq, uint Tick)>> PerTick { get; } = new();

        public void Run(in TickContext ctx)
        {
            ISampleLatch latch = Latch!();
            var list = new List<(byte, uint, uint)>();
            for (int i = 0; i < latch.Count; i++)
            {
                list.Add((latch[i].Slot, latch[i].Seq, latch.ReceivedTick));
            }

            PerTick.Add(list);
            Inside?.Invoke();
        }
    }

    private sealed class Rig
    {
        public Rig(int ringCapacity = 256, bool record = true)
        {
            Recorder = new SampleRecorder();
            var settings = new SimCoreSettings { SampleRingCapacity = ringCapacity };
            Counters = new SimCounters();
            var builder = new SimWorldBuilder(settings, Counters);
            if (record)
            {
                builder.BindRecorder(Recorder);
            }

            builder.Bind(PhaseId.PlayersStep, Reader);
            World = builder.Freeze();
            Reader.Latch = () => World.Samples;
        }

        public SampleRecorder Recorder { get; }

        public Reader Reader { get; } = new();

        public SimCounters Counters { get; }

        public SimWorld World { get; }

        public bool Write(byte slot, uint seq, int x = 0)
        {
            TransformSample s = Sample(seq, x);
            return World.TryWriteSample(Slot(slot), in s);
        }
    }

    // ---------------------------------------------------------------- AC-1

    [Fact]
    public void Ac1_SamplesWrittenBetweenTicks_AreStampedWithTheNextTick_AndInvisibleBefore()
    {
        var rig = new Rig();
        rig.World.Tick(); // tick 0
        Assert.Equal(0, rig.World.Samples.Count);

        Assert.True(rig.Write(1, 1));
        Assert.Equal(0, rig.World.Samples.Count); // written but not latched until the prologue

        rig.World.Tick(); // tick 1

        Assert.Equal(new[] { ((byte)1, 1u, 1u) }, rig.Reader.PerTick[1].ToArray());
        Assert.Equal(new[] { (1u, (byte)1, 1u, 0) }, rig.Recorder.Samples.ToArray());
    }

    [Fact]
    public void Ac1_SampleWrittenInsidePhaseOfTickT_IsDrainedAtTickTPlusOne()
    {
        var rig = new Rig();
        rig.Reader.Inside = () =>
        {
            if (rig.World.CurrentTick == 0)
            {
                rig.Write(2, 9);
            }
        };

        rig.World.Tick(); // tick 0 writes
        Assert.Empty(rig.Reader.PerTick[0]);
        rig.World.Tick(); // tick 1 drains

        Assert.Equal(new[] { ((byte)2, 9u, 1u) }, rig.Reader.PerTick[1].ToArray());
    }

    [Fact]
    public void Ac1_LatchIsReplacedEachTick()
    {
        var rig = new Rig();
        rig.Write(0, 1);
        rig.World.Tick();
        rig.World.Tick();

        Assert.Single(rig.Reader.PerTick[0]);
        Assert.Empty(rig.Reader.PerTick[1]);
        Assert.Equal(1u, rig.World.Samples.LastSeq(Slot(0))); // last seq survives the tick
    }

    // ---------------------------------------------------------------- AC-2

    [Fact]
    public void Ac2_SeqFiveThreeSixSixSeven_LatchesFiveSixSeven_AndCountsStale()
    {
        var rig = new Rig();
        foreach (uint seq in new uint[] { 5, 3, 6, 6, 7 })
        {
            Assert.True(rig.Write(4, seq));
        }

        rig.World.Tick();

        Assert.Equal(new[] { 5u, 6u, 7u }, rig.Reader.PerTick[0].Select(s => s.Seq).ToArray());
        Assert.Equal(2L, rig.Counters[CounterId.SamplesStale]);
        Assert.Equal(7u, rig.World.Samples.LastSeq(Slot(4)));
    }

    [Fact]
    public void Ac2_StaleIsJudgedAgainstEarlierTicksToo_AndSendersAreIndependent()
    {
        var rig = new Rig();
        rig.Write(0, 10);
        rig.World.Tick();
        rig.Write(0, 10); // equal to last: stale
        rig.Write(1, 2); // other sender: fresh although lower than slot 0's seq
        rig.World.Tick();

        Assert.Equal(new[] { ((byte)1, 2u, 1u) }, rig.Reader.PerTick[1].ToArray());
        Assert.Equal(1L, rig.Counters[CounterId.SamplesStale]);
        Assert.Equal(0u, rig.World.Samples.LastSeq(Slot(7)));
    }

    [Fact]
    public void Ac2_LatchExposesSlotOrderThenSeqOrder()
    {
        var rig = new Rig();
        rig.Write(5, 1);
        rig.Write(2, 1);
        rig.Write(5, 2);
        rig.Write(0, 4);
        rig.Write(2, 3);
        rig.World.Tick();

        Assert.Equal(
            new[] { ((byte)0, 4u), ((byte)2, 1u), ((byte)2, 3u), ((byte)5, 1u), ((byte)5, 2u) },
            rig.Reader.PerTick[0].Select(s => (s.Slot, s.Seq)).ToArray());
    }

    // ---------------------------------------------------------------- AC-3

    [Fact]
    public void Ac3_Recorder_GetsOneOnSamplePerAcceptedSample_InLatchOrder()
    {
        var rig = new Rig();
        rig.Write(3, 2, 30);
        rig.Write(1, 5, 10);
        rig.Write(3, 1, 99); // stale: older than 2
        rig.Write(1, 6, 11);
        rig.World.Tick();
        rig.Write(1, 7, 12);
        rig.World.Tick();

        Assert.Equal(
            new[] { (0u, (byte)1, 5u, 10), (0u, (byte)1, 6u, 11), (0u, (byte)3, 2u, 30), (1u, (byte)1, 7u, 12) },
            rig.Recorder.Samples.ToArray());
    }

    [Fact]
    public void Ac3_Dump_EncodesPerSlotLastSeq_AndSamples_ChangingOneChangesTheHash()
    {
        string Hash(int x)
        {
            var rig = new Rig();
            rig.Write(2, 8, x);
            rig.World.Tick();
            return DumpHash.Sha256(rig.World);
        }

        Assert.Equal(Hash(100), Hash(100));
        Assert.NotEqual(Hash(100), Hash(101));

        var a = new Rig();
        a.Write(2, 8);
        a.World.Tick();
        var b = new Rig();
        b.Write(2, 9);
        b.World.Tick();
        Assert.NotEqual(DumpHash.Sha256(a.World), DumpHash.Sha256(b.World));
    }

    [Fact]
    public void Ac3_Dump_LatchBlockLayout_IsPerSlotSeq_ThenTickAndCount_ThenSampleFields()
    {
        var rig = new Rig();
        TransformSample s = new() { Seq = 8, PosXMm = -5, PosYMm = 6, PosZMm = 7, YawBam = 0x1234, PitchBam = 0x5678, Flags = 3 };
        rig.World.Tick();
        Assert.True(rig.World.TryWriteSample(Slot(2), in s));
        rig.World.Tick(); // latch tick 1

        byte[] dump = DumpHash.Bytes(rig.World);
        uint len = BitConverter.ToUInt32(dump, 8);
        byte[] core = dump[12..(12 + (int)len)];
        int slotsBlock = 8 * 9;
        int latchEnd = core.Length - slotsBlock;
        int latchStart = latchEnd - (8 * 5 + 4 + 4 + 24);
        byte[] latch = core[latchStart..latchEnd];

        for (int slot = 0; slot < 8; slot++)
        {
            Assert.Equal(slot == 2 ? (byte)1 : (byte)0, latch[slot * 5]);
            Assert.Equal(slot == 2 ? 8u : 0u, BitConverter.ToUInt32(latch, (slot * 5) + 1));
        }

        Assert.Equal(1u, BitConverter.ToUInt32(latch, 40)); // ReceivedTick
        Assert.Equal(1u, BitConverter.ToUInt32(latch, 44)); // count
        Assert.Equal(8u, BitConverter.ToUInt32(latch, 48));
        Assert.Equal(-5, BitConverter.ToInt32(latch, 52));
        Assert.Equal(6, BitConverter.ToInt32(latch, 56));
        Assert.Equal(7, BitConverter.ToInt32(latch, 60));
        Assert.Equal(0x1234, BitConverter.ToUInt16(latch, 64));
        Assert.Equal(0x5678, BitConverter.ToUInt16(latch, 66));
        Assert.Equal(new byte[] { 2, 3, 0, 0 }, latch[68..72]);
    }

    [Fact]
    public void Ac3_SamplesPendingInTheRing_AreNotPartOfTheDump()
    {
        var rig = new Rig();
        string before = DumpHash.Sha256(rig.World);
        rig.Write(0, 1);
        Assert.Equal(before, DumpHash.Sha256(rig.World));
    }

    // ---------------------------------------------------------------- AC-4

    [Fact]
    public void Ac4_WriteFromNonOwnerThread_AssertsUnderSimAssert_OtherwiseIsRefusedAndCounted()
    {
        var rig = new Rig();
        Exception? caught = null;
        bool accepted = true;
        var thread = new Thread(() =>
        {
            try
            {
                accepted = rig.Write(0, 1);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.Start();
        thread.Join();

        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.WrongThread, Assert.IsType<SimAssertException>(caught).Code);
        }
        else
        {
            Assert.Null(caught);
            Assert.False(accepted);
            Assert.Equal(1L, rig.Counters[CounterId.SamplesRefused]);
        }

        rig.World.Tick();
        Assert.Empty(rig.Reader.PerTick[0]);
    }

    [Fact]
    public void Ac4_FullRing_RefusesAndCounts_NeverGrowsOrOverwrites()
    {
        var rig = new Rig(ringCapacity: 4);
        for (uint i = 1; i <= 4; i++)
        {
            Assert.True(rig.Write(0, i));
        }

        Assert.False(rig.Write(0, 5));
        Assert.False(rig.Write(1, 1));
        Assert.Equal(2L, rig.Counters[CounterId.SamplesRefused]);

        rig.World.Tick();

        Assert.Equal(new[] { 1u, 2u, 3u, 4u }, rig.Reader.PerTick[0].Select(s => s.Seq).ToArray());
        Assert.True(rig.Write(0, 5)); // drained: room again
    }

    [Fact]
    public void Ac4_NoneSlot_IsRefused()
    {
        var rig = new Rig();
        TransformSample s = Sample(1);

        Assert.False(rig.World.TryWriteSample(PlayerSlot.None, in s));
        Assert.Equal(1L, rig.Counters[CounterId.SamplesRefused]);
    }

    [Fact]
    public void Ac4_TryWriteSample_StampsTheSlotArgumentOverTheSamplesOwnField()
    {
        var rig = new Rig();
        TransformSample s = Sample(1);
        s.Slot = 7; // an untrusted claim
        rig.World.TryWriteSample(Slot(3), in s);
        rig.World.Tick();

        Assert.Equal((byte)3, rig.Reader.PerTick[0][0].Slot);
        Assert.Equal((byte)7, s.Slot); // the caller's copy is untouched
    }

    [Fact]
    public void Ac4_NonPowerOfTwoCapacity_IsRoundedUp_OrAsserts()
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            var ex = Assert.Throws<SimAssertException>(() => new Rig(ringCapacity: 6));
            Assert.Equal(SimAssertCode.SampleRingCapacityNotPow2, ex.Code);
        }
        else
        {
            var rig = new Rig(ringCapacity: 6);
            for (uint i = 1; i <= 8; i++)
            {
                Assert.True(rig.Write(0, i));
            }

            Assert.False(rig.Write(0, 9));
        }
    }

    [Fact]
    public void Ac4_TransformSample_IsBlittable_AndPaddingFree()
    {
        Assert.Equal(24, System.Runtime.CompilerServices.Unsafe.SizeOf<TransformSample>());
        Assert.False(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<TransformSample>());
    }
}

[Collection(NonParallelCollection.Name)]
public class SampleLatchAllocationTests
{
    [Fact]
    public void TenThousandWriteDrainCycles_AllocateNothing_InOptimisedBuilds()
    {
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(SimWorld).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");

        SimWorld world = new SimWorldBuilder().Freeze();
        PlayerSlot a = PlayerSlot.FromValue(0);
        PlayerSlot b = PlayerSlot.FromValue(5);
        uint seq = 0;

        AllocAssert.Zero(
            "10 000 write and drain cycles",
            () =>
            {
                for (int i = 0; i < 10_000; i++)
                {
                    seq++;
                    var s = new TransformSample { Seq = seq, PosXMm = (int)seq };
                    world.TryWriteSample(b, in s);
                    world.TryWriteSample(a, in s);
                    world.Tick();
                }
            });

        Assert.Equal(2, world.Samples.Count);
        Assert.Equal(0L, world.Counters[CounterId.SamplesStale]);
    }
}
