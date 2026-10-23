using System.Reflection;
using System.Runtime.InteropServices;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Sim.ClientLayerTests;

/// <summary>
/// Covers the push side of the <see cref="InputLatch"/> through <see cref="IInputLatchWriter"/> (input-controls story 002
/// AC-2, AC-3, AC-4, AC-7; GDD SC-R17-1 and H-R11-1): edge delivery order, catch-up batches, the overflow policy
/// (coalesce level presses, then drop the newest presses, never a release or cancel), step saturation and absolute look.
/// </summary>
public class InputLatchWriterTests
{
    private const FrameAction Level = FrameAction.Jump;       // in the level mask: Tap-style
    private const FrameAction Grammar = FrameAction.Interact;  // not in the level mask: Press/Release grammar

    private sealed class FixedClock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => true;
    }

    private sealed class Rig
    {
        public Rig(bool levelMask = true)
        {
            Counters = new SimCounters();
            Checks = new SimCheckRing(capacity: 64, rateLimitTicks: 0);
            var builder = new ClientLayerBuilder(new FixedClock(), Counters, Checks);
            if (levelMask)
            {
                builder.SetLevelActionMask(FrameActions.Bit(Level));
            }

            Layer = builder.Build();
            Writer = Layer.InputWriter;
        }

        public ClientLayerSut Layer { get; }

        public IInputLatchWriter Writer { get; }

        public SimCounters Counters { get; }

        public SimCheckRing Checks { get; }

        public InputFrame Frame => Layer.InputLatch.Frame;

        public void Push(FrameAction a, EdgeKind k, int seq) => Writer.PushEdge(new InputEdge(a, k, unchecked((ushort)seq), 1));

        public InputFrame First()
        {
            Layer.Step(true);
            return Frame;
        }

        public InputFrame Later()
        {
            Layer.Step(false);
            return Frame;
        }
    }

    private static ushort[] Seqs(in InputFrame f)
    {
        var result = new ushort[f.EdgeCount];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = f.GetEdge(i).ArrivalSeq;
        }

        return result;
    }

    private static ushort[] Range(int from, int count)
    {
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = (ushort)(from + i);
        }

        return result;
    }

    // ---------------------------------------------------------------- AC-2 writer and latch

    [Fact]
    [Trait("Id", "SC-R17-1")]
    public void CatchUpBatch_FirstTickCarriesAllEdgesInOrder_LaterTicksCarryStateOnly()
    {
        var rig = new Rig();
        rig.Push(Grammar, EdgeKind.Press, 1);
        rig.Push(FrameAction.Sprint, EdgeKind.Press, 2);
        rig.Push(Grammar, EdgeKind.Release, 3);
        rig.Writer.PushSteps(FrameAction.Aim, 2);
        rig.Writer.SetState(new InputState { HeldBits = 0b100, MoveX = 100, MoveY = -200, RouterStateId = 5 });

        InputFrame first = rig.First();
        InputFrame second = rig.Later();
        InputFrame third = rig.Later();

        Assert.True(first.IsFirstTickInBatch);
        Assert.Equal(new ushort[] { 1, 2, 3 }, Seqs(first));
        Assert.Equal(1, first.StepCount);
        foreach (InputFrame later in new[] { second, third })
        {
            Assert.False(later.IsFirstTickInBatch);
            Assert.Equal(0, later.EdgeCount);
            Assert.Equal(0, later.StepCount);
            Assert.Equal((ushort)0, later.DroppedEdges);
            Assert.Equal(0b100UL, later.HeldBits);
            Assert.Equal((short)100, later.MoveX);
            Assert.Equal((short)-200, later.MoveY);
        }

        Assert.Equal(new uint[] { 0, 1, 2 }, new[] { first.Tick, second.Tick, third.Tick });
        Assert.Equal(0, rig.Layer.InputLatch.PendingEdgeCount);
    }

    [Fact]
    [Trait("Id", "SC-R17-1")]
    public void ZeroTickFrames_ClearNothing_EdgesAndStepsArriveInOrderOnNextTickThatRuns()
    {
        var rig = new Rig();
        rig.Push(Grammar, EdgeKind.Press, 1);
        rig.Writer.PushSteps(FrameAction.Aim, 1);
        rig.First();

        // Frame A runs no tick, frame B runs no tick: the router keeps pushing; no Step call clears anything.
        rig.Push(Grammar, EdgeKind.Release, 2);
        rig.Writer.PushSteps(FrameAction.Aim, 1);
        rig.Push(FrameAction.Sprint, EdgeKind.Press, 3);
        rig.Writer.PushSteps(FrameAction.Aim, 1);
        rig.Push(FrameAction.Sprint, EdgeKind.Release, 4);

        Assert.Equal(3, rig.Layer.InputLatch.PendingEdgeCount);
        Assert.Equal(1, rig.Layer.InputLatch.PendingStepCount);
        InputFrame next = rig.First();

        Assert.Equal(new ushort[] { 2, 3, 4 }, Seqs(next));
        Assert.Equal(2, next.GetStep(0).N);
        Assert.Equal(FrameAction.Aim, next.GetStep(0).Action);
    }

    [Fact]
    [Trait("Id", "SC-R17-1")]
    public void EdgesPushedMidBatch_StayLatchedUntilTheNextFirstTick()
    {
        var rig = new Rig();
        rig.First();
        rig.Push(Grammar, EdgeKind.Press, 1);

        InputFrame mid = rig.Later();
        Assert.Equal(0, mid.EdgeCount);
        Assert.Equal(1, rig.Layer.InputLatch.PendingEdgeCount);
        Assert.Equal(new ushort[] { 1 }, Seqs(rig.First()));
    }

    [Fact]
    public void EmitNThenM_FrameHoldsNoStaleEdgesOrStepsPastTheCounts()
    {
        var staleRig = new Rig();
        for (int i = 0; i < 5; i++)
        {
            staleRig.Push(Grammar, EdgeKind.Press, i);
        }

        staleRig.Writer.PushSteps(FrameAction.Aim, 3);
        staleRig.Writer.PushSteps(FrameAction.Sprint, 3);
        staleRig.Writer.PushSteps(FrameAction.Jump, 3);
        staleRig.First();
        staleRig.Layer.Step(false);

        var freshRig = new Rig();
        freshRig.First();
        freshRig.Layer.Step(false);
        for (int i = 0; i < 2; i++)
        {
            staleRig.Push(FrameAction.Aim, EdgeKind.Release, 10 + i);
            freshRig.Push(FrameAction.Aim, EdgeKind.Release, 10 + i);
        }

        staleRig.Writer.PushSteps(FrameAction.Aim, 1);
        freshRig.Writer.PushSteps(FrameAction.Aim, 1);
        InputFrame m = staleRig.First();
        InputFrame fresh = freshRig.First();

        Assert.Equal(2, m.EdgeCount);
        Assert.Equal(1, m.StepCount);
        Assert.Equal(new ushort[] { 10, 11 }, Seqs(m));
        Assert.Equal(FrameBytes(fresh), FrameBytes(m));

        // A state-only tick after a full frame also leaves every slot zero.
        InputFrame after = staleRig.Later();
        Assert.Equal(FrameBytes(freshRig.Later()), FrameBytes(after));
    }

    private static byte[] FrameBytes(InputFrame f) => MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref f, 1)).ToArray();

    [Fact]
    public void FirstTickWithNothingPending_EmitsAnEmptyFrame()
    {
        var rig = new Rig();
        rig.Push(Grammar, EdgeKind.Press, 1);
        rig.First();

        InputFrame empty = rig.First();

        Assert.Equal(0, empty.EdgeCount);
        Assert.True(empty.IsFirstTickInBatch);
    }

    [Fact]
    public void WrappingArrivalSeq_IsDeliveredInPushOrder()
    {
        var rig = new Rig();
        foreach (int seq in new[] { 65534, 65535, 0, 1 })
        {
            rig.Push(Grammar, EdgeKind.Press, seq);
        }

        Assert.Equal(new ushort[] { 65534, 65535, 0, 1 }, Seqs(rig.First()));
    }

    [Fact]
    public void EqualArrivalSeq_IsAccepted_AndAnOlderOneAssertsInDebugOnly()
    {
        var rig = new Rig();
        rig.Push(Grammar, EdgeKind.Press, 10);
        rig.Push(Grammar, EdgeKind.Release, 10);

        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException ex = Assert.Throws<SimAssertException>(() => rig.Push(Grammar, EdgeKind.Press, 9));
            Assert.Equal(SimAssertCode.InputEdgeOutOfOrder, ex.Code);
        }
        else
        {
            rig.Push(Grammar, EdgeKind.Press, 9);
            Assert.Equal(3, rig.Layer.InputLatch.PendingEdgeCount);
        }
    }

    [Fact]
    public void PushSide_IsNotPublicOnTheConcreteLatch_AndTheWriterIsTheLatch()
    {
        var rig = new Rig();

        Assert.Same(rig.Layer.InputLatch, rig.Layer.InputWriter);
        string[] pushMembers = { "PushEdge", "PushSteps", "SetState" };
        foreach (string name in pushMembers)
        {
            Assert.Null(typeof(InputLatch).GetMethod(name, BindingFlags.Public | BindingFlags.Instance));
            Assert.NotNull(typeof(IInputLatchWriter).GetMethod(name));
        }

        Assert.NotNull(typeof(InputLatch).GetInterface(nameof(IInputLatchWriter)));
    }

    [Fact]
    public void SetLevelActionMask_AfterBuild_Throws()
    {
        var builder = new ClientLayerBuilder(new FixedClock());
        builder.Build();

        SimBuildException ex = Assert.Throws<SimBuildException>(() => builder.SetLevelActionMask(1));
        Assert.Equal(SimBuildErrorCode.BindAfterFreeze, ex.Code);
    }

    // ---------------------------------------------------------------- AC-3 overflow

    [Fact]
    public void ThirtyTwoEdges_AreNotAnOverflow_NothingCoalescedOrDropped()
    {
        var rig = new Rig();
        for (int i = 0; i < 32; i++)
        {
            rig.Push(Level, EdgeKind.Press, i);   // level presses, but at capacity so no policy runs
        }

        InputFrame f = rig.First();

        Assert.Equal(Range(0, 32), Seqs(f));
        Assert.Equal((ushort)0, f.DroppedEdges);
        Assert.Equal(0, rig.Checks.Count);
        Assert.Equal(0L, rig.Counters[CounterId.InputEdgeOverflow]);
    }

    [Fact]
    public void FortyLevelPresses_CoalesceToTheFirst_NoDropOneRecord()
    {
        var rig = new Rig();
        for (int i = 0; i < 40; i++)
        {
            rig.Push(Level, EdgeKind.Press, i);
        }

        InputFrame f = rig.First();

        Assert.Equal(new ushort[] { 0 }, Seqs(f));
        Assert.Equal((ushort)0, f.DroppedEdges);
        Assert.Equal(0L, rig.Counters[CounterId.InputEdgeOverflow]);
        Assert.Equal(1, rig.Checks.Count);
        Assert.Equal((ushort)CheckCode.InputEdgeOverflow, rig.Checks[0].Code);
        Assert.Equal(0L, rig.Checks[0].A);
        Assert.Equal(39L, rig.Checks[0].B);
    }

    [Fact]
    public void FortyGrammarPresses_AreNeverCoalesced_NewestEightAreDropped()
    {
        var rig = new Rig();
        for (int i = 0; i < 40; i++)
        {
            rig.Push(Grammar, EdgeKind.Press, i);
        }

        InputFrame f = rig.First();

        Assert.Equal(Range(0, 32), Seqs(f));
        Assert.Equal((ushort)8, f.DroppedEdges);
        Assert.Equal(8L, rig.Counters[CounterId.InputEdgeOverflow]);
        Assert.Equal(1, rig.Checks.Count);
        Assert.Equal(8L, rig.Checks[0].A);
        Assert.Equal(0L, rig.Checks[0].B);
        Assert.Equal(0, rig.Layer.InputLatch.PendingEdgeCount);
    }

    [Fact]
    public void WithoutALevelMask_LevelActionPressesAreDroppedNotCoalesced()
    {
        var rig = new Rig(levelMask: false);
        for (int i = 0; i < 40; i++)
        {
            rig.Push(Level, EdgeKind.Press, i);
        }

        InputFrame f = rig.First();

        Assert.Equal(32, f.EdgeCount);
        Assert.Equal((ushort)8, f.DroppedEdges);
    }

    [Fact]
    public void FortyEdgesOfPressReleasePairs_DropOnlyPresses_SurvivorsKeepOrder()
    {
        var rig = new Rig();
        for (int pair = 0; pair < 20; pair++)
        {
            rig.Push(Grammar, EdgeKind.Press, 2 * pair);
            rig.Push(Grammar, EdgeKind.Release, (2 * pair) + 1);
        }

        InputFrame f = rig.First();

        Assert.Equal(32, f.EdgeCount);
        Assert.Equal((ushort)8, f.DroppedEdges);
        int releases = 0;
        for (int i = 0; i < f.EdgeCount; i++)
        {
            releases += f.GetEdge(i).Kind == EdgeKind.Release ? 1 : 0;
        }

        Assert.Equal(20, releases);
        ushort[] seqs = Seqs(f);
        for (int i = 1; i < seqs.Length; i++)
        {
            Assert.True(SerialU16.IsNewer(seqs[i], seqs[i - 1]));
        }

        // The eight dropped presses are the newest eight: pairs 12..19.
        Assert.Equal(Enumerable.Range(0, 24).Select(i => (ushort)i), seqs.Take(24));
        Assert.Equal(new ushort[] { 25, 27, 29, 31, 33, 35, 37, 39 }, seqs.Skip(24).ToArray());
    }

    [Fact]
    public void LevelPressesSeparatedByARelease_AreNotMerged_ButARepeatInOneRunIs()
    {
        var rig = new Rig();
        rig.Push(Level, EdgeKind.Press, 0);
        rig.Push(Level, EdgeKind.Press, 1);       // repeat in the same run: coalesced
        rig.Push(Level, EdgeKind.Release, 2);
        rig.Push(Level, EdgeKind.Press, 3);       // new run after a release: kept
        rig.Push(Level, EdgeKind.Cancel, 4);
        rig.Push(Level, EdgeKind.Press, 5);       // new run after a cancel: kept
        for (int i = 0; i < 28; i++)
        {
            rig.Push(Grammar, EdgeKind.Press, 6 + i);   // 34 edges in total
        }

        InputFrame f = rig.First();

        // 34 edges, one coalesced (seq 1) leaves 33, one newest grammar press (seq 33) dropped.
        Assert.Equal(32, f.EdgeCount);
        Assert.Equal(new ushort[] { 0, 2, 3, 4, 5 }, Seqs(f).Take(5).ToArray());
        Assert.Equal(Range(6, 27), Seqs(f).Skip(5).ToArray());
        Assert.Equal((ushort)1, f.DroppedEdges);
        Assert.Equal(1L, rig.Checks[0].A);
        Assert.Equal(1L, rig.Checks[0].B);
    }

    [Fact]
    public void LevelPressesGatedAgainstDifferentRouterStates_AreNotMerged()
    {
        var rig = new Rig();
        rig.Writer.PushEdge(new InputEdge(Level, EdgeKind.Press, 0, routerStateId: 1));
        rig.Writer.PushEdge(new InputEdge(Level, EdgeKind.Press, 1, routerStateId: 1));   // same state: coalesced
        rig.Writer.PushEdge(new InputEdge(Level, EdgeKind.Press, 2, routerStateId: 7));   // state changed: kept
        rig.Writer.PushEdge(new InputEdge(Level, EdgeKind.Press, 3, routerStateId: 7));   // same state again: coalesced
        rig.Writer.PushEdge(new InputEdge(Level, EdgeKind.Press, 4, routerStateId: 1));   // changed back: kept
        for (int i = 0; i < 30; i++)
        {
            rig.Writer.PushEdge(new InputEdge(Grammar, EdgeKind.Press, (ushort)(5 + i), routerStateId: 1));   // 35 edges
        }

        InputFrame f = rig.First();

        // 35 edges, two coalesced (seq 1, 3) leaves 33, one newest grammar press (seq 34) dropped.
        Assert.Equal(32, f.EdgeCount);
        Assert.Equal(new ushort[] { 0, 2, 4 }, Seqs(f).Take(3).ToArray());
        Assert.Equal(Range(5, 29), Seqs(f).Skip(3).ToArray());
        Assert.Equal((ushort)1, f.DroppedEdges);
        Assert.Equal(1L, rig.Checks[0].A);
        Assert.Equal(2L, rig.Checks[0].B);
    }

    [Fact]
    [Trait("Id", "H-R11-1")]
    public void EmittedEdges_KeepEveryField_IncludingAnEventTimeStateThatDiffersFromTheFrameState()
    {
        var rig = new Rig();
        ushort world = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.None, BodyLayer.OnFoot, false, false, SubmenuLayer.None);
        ushort panel = RouterStateInfo.Pack(CaptureLayer.None, OverlayLayer.Panel, BodyLayer.OnFoot, false, false, SubmenuLayer.None);
        var jump = new InputEdge(FrameAction.Jump, EdgeKind.Press, 10, world, EdgeReason.None, deviceClass: 2, targetEntity: 0xDEADBEEF, targetSlot: 5, presentedSeq: 0xBEEF);
        var cancel = new InputEdge(FrameAction.Interact, EdgeKind.Cancel, 11, panel, EdgeReason.FocusLost, deviceClass: 3, targetEntity: 42, targetSlot: 1, presentedSeq: 7);
        var change = new InputEdge(FrameAction.Aim, EdgeKind.Release, 12, panel, EdgeReason.StateChange, deviceClass: 1);
        rig.Writer.PushEdge(jump);
        rig.Writer.PushEdge(cancel);
        rig.Writer.PushEdge(change);
        rig.Writer.SetState(new InputState { RouterStateId = panel });

        InputFrame f = rig.First();

        Assert.Equal(panel, f.RouterStateId);
        Assert.True(RouterStateInfo.IsPanel(f.RouterStateId));
        Assert.Equal(3, f.EdgeCount);
        Assert.Equal(jump, f.GetEdge(0));
        Assert.Equal(cancel, f.GetEdge(1));
        Assert.Equal(change, f.GetEdge(2));
        Assert.False(RouterStateInfo.IsPanel(f.GetEdge(0).RouterStateId));   // the Jump keeps its event-time World gate
        Assert.Equal(EdgeReason.FocusLost, f.GetEdge(1).Reason);
        Assert.Equal(0xDEADBEEFu, f.GetEdge(0).TargetEntity);
        Assert.Equal((byte)5, f.GetEdge(0).TargetSlot);
        Assert.Equal((ushort)0xBEEF, f.GetEdge(0).PresentedSeq);
        Assert.Equal((byte)2, f.GetEdge(0).DeviceClass);
    }

    [Fact]
    public void TwoLevelActionsInTheMask_AreCoalescedIndependently()
    {
        var counters = new SimCounters();
        var builder = new ClientLayerBuilder(new FixedClock(), counters);
        builder.SetLevelActionMask(FrameActions.Bit(FrameAction.Jump) | FrameActions.Bit(FrameAction.Sprint));
        ClientLayerSut layer = builder.Build();
        for (int i = 0; i < 20; i++)
        {
            layer.InputWriter.PushEdge(new InputEdge(FrameAction.Jump, EdgeKind.Press, (ushort)(2 * i), 1));
            layer.InputWriter.PushEdge(new InputEdge(FrameAction.Sprint, EdgeKind.Press, (ushort)((2 * i) + 1), 1));
        }

        layer.Step(true);
        InputFrame f = layer.InputLatch.Frame;

        Assert.Equal(2, f.EdgeCount);
        Assert.Equal(FrameAction.Jump, f.GetEdge(0).Action);
        Assert.Equal(FrameAction.Sprint, f.GetEdge(1).Action);
    }

    [Fact]
    public void MoreThan32ReleasesAndCancels_AreNeverDropped_ExtraOnesCarryOver()
    {
        var rig = new Rig();
        for (int i = 0; i < 40; i++)
        {
            rig.Push(Grammar, i % 2 == 0 ? EdgeKind.Release : EdgeKind.Cancel, i);
        }

        InputFrame first = rig.First();
        InputFrame second = rig.First();

        Assert.Equal(Range(0, 32), Seqs(first));
        Assert.Equal(Range(32, 8), Seqs(second));
        Assert.Equal((ushort)0, first.DroppedEdges);
        Assert.Equal(0L, rig.Counters[CounterId.InputEdgeOverflow]);
        Assert.Equal(0, rig.Checks.Count);
    }

    [Fact]
    public void DroppedEdges_ReportedOnTheFirstTickOnly_ThenResets()
    {
        var rig = new Rig();
        for (int i = 0; i < 34; i++)
        {
            rig.Push(Grammar, EdgeKind.Press, i);
        }

        InputFrame first = rig.First();
        InputFrame later = rig.Later();
        InputFrame next = rig.First();

        Assert.Equal((ushort)2, first.DroppedEdges);
        Assert.Equal((ushort)0, later.DroppedEdges);
        Assert.Equal((ushort)0, next.DroppedEdges);
    }

    [Fact]
    public void PushingPastTheRingCapacity_DropsTheNewestPresses_CountsThem()
    {
        var rig = new Rig(levelMask: false);
        for (int i = 0; i < 300; i++)
        {
            rig.Push(Grammar, EdgeKind.Press, i);
        }

        Assert.Equal(InputLatch.RingCapacity, rig.Layer.InputLatch.PendingEdgeCount);
        InputFrame f = rig.First();

        Assert.Equal(Range(0, 32), Seqs(f));
        Assert.Equal((ushort)(300 - 32), f.DroppedEdges);
        Assert.Equal(300L - 32, rig.Counters[CounterId.InputEdgeOverflow]);
        Assert.True(rig.Checks.Count > 0);
    }

    [Fact]
    public void ReleaseArrivingAtAFullRing_EvictsTheNewestPress_NotTheRelease()
    {
        var rig = new Rig(levelMask: false);
        for (int i = 0; i < InputLatch.RingCapacity; i++)
        {
            rig.Push(Grammar, EdgeKind.Press, i);
        }

        rig.Push(Grammar, EdgeKind.Release, InputLatch.RingCapacity);
        Assert.Equal(InputLatch.RingCapacity, rig.Layer.InputLatch.PendingEdgeCount);
        InputFrame f = rig.First();

        Assert.Equal(32, f.EdgeCount);
        Assert.Equal(EdgeKind.Release, f.GetEdge(31).Kind);
        Assert.Equal((ushort)InputLatch.RingCapacity, f.GetEdge(31).ArrivalSeq);
        Assert.Equal(Range(0, 31), Seqs(f).Take(31).ToArray());
        Assert.Equal((ushort)(InputLatch.RingCapacity + 1 - 32), f.DroppedEdges);
    }

    [Fact]
    public void RingOfNonPressEdgesOverflowing_DropsTheNewestEdgeAsALastResort()
    {
        var rig = new Rig();
        for (int i = 0; i <= InputLatch.RingCapacity; i++)
        {
            rig.Push(Grammar, EdgeKind.Release, i);
        }

        Assert.Equal(InputLatch.RingCapacity, rig.Layer.InputLatch.PendingEdgeCount);
        Assert.Equal(1L, rig.Counters[CounterId.InputEdgeOverflow]);
        InputFrame f = rig.First();

        Assert.Equal(Range(0, 32), Seqs(f));
        Assert.Equal((ushort)1, f.DroppedEdges);
        Assert.Equal(InputLatch.RingCapacity - 32, rig.Layer.InputLatch.PendingEdgeCount);
    }

    [Fact]
    public void DroppedEdges_SaturatesAtUShortMax()
    {
        var rig = new Rig(levelMask: false);
        for (int i = 0; i < 70_000; i++)
        {
            rig.Push(Grammar, EdgeKind.Press, i);
        }

        Assert.Equal(ushort.MaxValue, rig.First().DroppedEdges);
        Assert.Equal(70_000L - 32, rig.Counters[CounterId.InputEdgeOverflow]);
    }

    [Fact]
    public void OverflowWithoutACheckRing_StillCountsAndDrops()
    {
        var counters = new SimCounters();
        ClientLayerSut layer = new ClientLayerBuilder(new FixedClock(), counters).Build();
        for (int i = 0; i < 33; i++)
        {
            layer.InputWriter.PushEdge(new InputEdge(Grammar, EdgeKind.Press, (ushort)i, 1));
        }

        layer.Step(true);

        Assert.Equal(1L, counters[CounterId.InputEdgeOverflow]);
        Assert.Equal(32, layer.InputLatch.Frame.EdgeCount);
    }

    // ---------------------------------------------------------------- AC-4 steps

    [Theory]
    [InlineData(100, 100, 127)]
    [InlineData(-100, -100, -127)]
    [InlineData(5, 7, 12)]
    [InlineData(1000, 0, 127)]
    [InlineData(int.MaxValue, int.MaxValue, 127)]
    [InlineData(int.MinValue, int.MinValue, -127)]
    [InlineData(127, -1, 126)]
    public void PushSteps_AccumulatesPerAction_SaturatingAtPlusMinus127(int first, int second, int expected)
    {
        var rig = new Rig();
        rig.Writer.PushSteps(FrameAction.Aim, first);
        rig.Writer.PushSteps(FrameAction.Aim, second);

        InputFrame f = rig.First();

        Assert.Equal(1, f.StepCount);
        Assert.Equal(FrameAction.Aim, f.GetStep(0).Action);
        Assert.Equal((sbyte)expected, f.GetStep(0).N);
        Assert.Equal(1, f.Steps.Length);
    }

    [Fact]
    public void PushSteps_NetZeroAndZeroPushes_AreNotEmitted()
    {
        var rig = new Rig();
        rig.Writer.PushSteps(FrameAction.Aim, 0);
        Assert.Equal(0, rig.Layer.InputLatch.PendingStepCount);
        rig.Writer.PushSteps(FrameAction.Sprint, 4);
        rig.Writer.PushSteps(FrameAction.Sprint, -4);
        rig.Writer.PushSteps(FrameAction.Jump, -2);

        InputFrame f = rig.First();

        Assert.Equal(1, f.StepCount);
        Assert.Equal(FrameAction.Jump, f.GetStep(0).Action);
        Assert.Equal((sbyte)-2, f.GetStep(0).N);
    }

    [Fact]
    public void PushSteps_FourDistinctActionsFit_FifthIsDroppedAndCounted_ExistingStillAccumulates()
    {
        var rig = new Rig();
        FrameAction[] four = { FrameAction.Interact, FrameAction.Jump, FrameAction.Sprint, FrameAction.Aim };
        for (int i = 0; i < four.Length; i++)
        {
            rig.Writer.PushSteps(four[i], i + 1);
        }

        rig.Writer.PushSteps((FrameAction)9, 5);
        rig.Writer.PushSteps((FrameAction)9, 5);
        rig.Writer.PushSteps(FrameAction.Sprint, 10);

        Assert.Equal(2L, rig.Counters[CounterId.InputStepDropped]);
        InputFrame f = rig.First();

        Assert.Equal(4, f.StepCount);
        Assert.Equal(four, f.Steps.ToArray().Select(s => s.Action).ToArray());
        Assert.Equal(new sbyte[] { 1, 2, 13, 4 }, f.Steps.ToArray().Select(s => s.N).ToArray());
    }

    [Fact]
    public void Steps_AreClearedByTheFirstTickAndAbsentLater_AndNewBatchStartsEmpty()
    {
        var rig = new Rig();
        rig.Writer.PushSteps(FrameAction.Aim, 3);

        InputFrame first = rig.First();
        InputFrame later = rig.Later();
        InputFrame next = rig.First();

        Assert.Equal(1, first.StepCount);
        Assert.Equal(0, later.StepCount);
        Assert.Equal(0, next.StepCount);
        Assert.Equal(0, rig.Layer.InputLatch.PendingStepCount);
        rig.Writer.PushSteps((FrameAction)9, 1);   // a fresh slot is available again
        Assert.Equal(1, rig.First().StepCount);
    }

    // ---------------------------------------------------------------- AC-7 absolute look, state transport

    [Fact]
    [Trait("Id", "H-R11-1")]
    public void LookYaw_IsAbsolute_FourTicksYieldExactlyThePushedValues()
    {
        var rig = new Rig();
        ushort[] yaws = { 0x0000, 0x2AAA, 0x5555, 0x4000 };
        var seen = new ushort[yaws.Length];

        for (int i = 0; i < yaws.Length; i++)
        {
            rig.Writer.SetState(new InputState { LookYaw = yaws[i], LookPitch = (short)(-i) });
            seen[i] = (i % 2 == 0 ? rig.First() : rig.Later()).LookYaw;
        }

        Assert.Equal(yaws, seen);
        Assert.Equal(typeof(sbyte), typeof(InputStep).GetProperty(nameof(InputStep.N))!.PropertyType);
    }

    [Fact]
    [Trait("Id", "H-R11-1")]
    public void LookYaw_HoldsTheLastPushedValue_WithoutAccumulatingAcrossTicks()
    {
        var rig = new Rig();
        rig.Writer.SetState(new InputState { LookYaw = 0x4000 });

        ushort a = rig.First().LookYaw;
        ushort b = rig.Later().LookYaw;
        ushort c = rig.Later().LookYaw;

        Assert.Equal(new ushort[] { 0x4000, 0x4000, 0x4000 }, new[] { a, b, c });
    }

    [Fact]
    public void State_IsTransportedUnchanged_FlagsFollowTheRouter_FirstInBatchComesFromTheLayer()
    {
        var rig = new Rig();
        rig.Writer.SetState(new InputState
        {
            MoveX = -32768, MoveY = 32767, LookYaw = 65535, LookPitch = -16384, HeldBits = ulong.MaxValue,
            FocusCosMinPm = 707, RouterStateId = 1007,
            Flags = InputFrameFlags.Paused | InputFrameFlags.FocusLost | InputFrameFlags.LookReseeded | InputFrameFlags.FirstInBatch,
        });

        InputFrame later = default;
        InputFrame first = rig.First();
        later = rig.Later();

        Assert.Equal((short)-32768, first.MoveX);
        Assert.Equal((short)32767, first.MoveY);
        Assert.Equal((ushort)65535, first.LookYaw);
        Assert.Equal((short)-16384, first.LookPitch);
        Assert.Equal(ulong.MaxValue, first.HeldBits);
        Assert.Equal((short)707, first.FocusCosMinPm);
        Assert.Equal((ushort)1007, first.RouterStateId);
        Assert.Equal(InputFrameFlags.Paused | InputFrameFlags.FocusLost | InputFrameFlags.LookReseeded | InputFrameFlags.FirstInBatch, first.Flags);
        Assert.Equal(InputFrameFlags.Paused | InputFrameFlags.FocusLost | InputFrameFlags.LookReseeded, later.Flags);

        rig.Writer.SetState(new InputState());
        Assert.Equal(InputFrameFlags.None, rig.Later().Flags);
        Assert.Equal(InputFrameFlags.FirstInBatch, rig.First().Flags);
    }

    [Fact]
    public void LatchNeverDispatches_ItOnlyExposesFrameAndPendingCounts()
    {
        MethodInfo[] methods = typeof(InputLatch).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        string[] names = methods.Select(m => m.Name).Where(n => !n.StartsWith("get_", StringComparison.Ordinal)).ToArray();

        Assert.Equal(new[] { "Step" }, names);
    }
}

/// <summary>Zero-allocation guardrail for the push side, including the overflow paths (TD-054 item 7).</summary>
[Collection(NonParallelCollection.Name)]
public class InputLatchWriterAllocationTests
{
    private sealed class FixedClock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => true;
    }

    [Fact]
    public void PushStepAndOverflow_Allocate0B()
    {
        var counters = new SimCounters();
        var ring = new SimCheckRing();
        var builder = new ClientLayerBuilder(new FixedClock(), counters, ring);
        builder.SetLevelActionMask(FrameActions.Bit(FrameAction.Jump));
        ClientLayerSut layer = builder.Build();
        IInputLatchWriter w = layer.InputWriter;
        var state = new InputState { HeldBits = 3, MoveX = 2, MoveY = 2, LookYaw = 1, LookPitch = 1, RouterStateId = 1 };
        ushort seq = 0;

        int runs = AllocAssert.Zero("InputLatch push, overflow and emit", () =>
        {
            for (int batch = 0; batch < 20; batch++)
            {
                // 300 edges: crosses the 256 ring cap (drop path) and the 32 frame cap (coalesce + drop path).
                for (int i = 0; i < 300; i++)
                {
                    FrameAction a = i % 3 == 0 ? FrameAction.Jump : FrameAction.Interact;
                    EdgeKind k = i % 7 == 0 ? EdgeKind.Release : EdgeKind.Press;
                    var e = new InputEdge(a, k, seq++, 1);
                    w.PushEdge(in e);
                }

                for (int s = 0; s < 6; s++)
                {
                    w.PushSteps((FrameAction)s, 100);
                }

                w.SetState(in state);
                layer.Step(true);
                layer.Step(false);
            }
        });

        Assert.True(runs > 0);
        Assert.True(counters[CounterId.InputEdgeOverflow] > 0);
        Assert.True(counters[CounterId.InputStepDropped] > 0);
        Assert.True(ring.Count > 0);
    }
}
