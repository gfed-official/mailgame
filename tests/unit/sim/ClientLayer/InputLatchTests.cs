using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Sim.ClientLayerTests;

/// <summary>
/// Covers the <see cref="InputLatch"/> (story 023 AC-1; GDD SC-R17-1): edges attach only to the tick with
/// <c>isFirstTickInBatch == true</c>, later ticks carry held bits and the latest move intent, a 0-tick frame keeps
/// its edges latched in order, and a tick that is not first never discards an edge. Plus the fixed-capacity overflow.
/// </summary>
public class InputLatchTests
{
    private const FrameAction Fire = FrameAction.Jump;
    private const FrameAction Use = FrameAction.Sprint;

    private sealed class FixedClock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => true;
    }

    private sealed class FrameRecorder : IClientStep
    {
        public readonly List<InputFrame> Frames = new();

        public void Step(in ClientStepContext ctx) => Frames.Add(ctx.Input);
    }

    private static InputEdge Edge(FrameAction action, EdgeKind kind, ushort seq) => new(action, kind, seq, 1);

    private static (ClientLayerSut Layer, FrameRecorder Recorder, SimCounters Counters) Make()
    {
        var counters = new SimCounters();
        var builder = new ClientLayerBuilder(new FixedClock(), counters);
        var recorder = new FrameRecorder();
        builder.Bind(ClientStepId.CarrierStep, recorder);
        return (builder.Build(), recorder, counters);
    }

    private static void AssertEdge(InputFrame frame, int index, FrameAction action, EdgeKind kind, ushort seq)
    {
        InputEdge e = frame.GetEdge(index);
        Assert.Equal(action, e.Action);
        Assert.Equal(kind, e.Kind);
        Assert.Equal(seq, e.ArrivalSeq);
    }

    [Fact]
    [Trait("Id", "SC-R17-1")]
    public void ThreeTickFrame_DeliversEdgesOnFirstTickOnly_LaterTicksCarryHeldAndMove()
    {
        (ClientLayerSut layer, FrameRecorder rec, _) = Make();
        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Press, 1));
        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Release, 2));
        layer.InputWriter.SetState(new InputState { HeldBits = 0b10, MoveX = 4, MoveY = -2, LookYaw = 7, LookPitch = -3, RouterStateId = 9 });

        layer.Step(true);
        layer.Step(false);
        layer.Step(false);

        Assert.Equal(3, rec.Frames.Count);
        InputFrame first = rec.Frames[0];
        Assert.True(first.IsFirstTickInBatch);
        Assert.Equal(2, first.EdgeCount);
        AssertEdge(first, 0, Fire, EdgeKind.Press, 1);
        AssertEdge(first, 1, Fire, EdgeKind.Release, 2);
        Assert.Equal(2, first.Edges.Length);
        Assert.Equal(1u, first.Edges[0].ArrivalSeq);
        for (int i = 1; i < 3; i++)
        {
            InputFrame f = rec.Frames[i];
            Assert.False(f.IsFirstTickInBatch);
            Assert.Equal(0, f.EdgeCount);
            Assert.True(f.Edges.IsEmpty);
            Assert.Equal(0b10UL, f.HeldBits);
            Assert.Equal((short)4, f.MoveX);
            Assert.Equal((short)-2, f.MoveY);
            Assert.Equal((ushort)7, f.LookYaw);
            Assert.Equal((short)-3, f.LookPitch);
            Assert.Equal((ushort)9, f.RouterStateId);
            Assert.Equal((uint)i, f.Tick);
        }

        Assert.Equal(0, layer.InputLatch.PendingEdgeCount);
    }

    [Fact]
    [Trait("Id", "SC-R17-1")]
    public void LaterTicks_CarryLatestMoveIntent_AndHeldChangedBetweenTicks()
    {
        (ClientLayerSut layer, FrameRecorder rec, _) = Make();
        layer.InputWriter.SetState(new InputState { HeldBits = 1, MoveX = 2, MoveY = 2, RouterStateId = 1 });
        layer.Step(true);

        layer.InputWriter.SetState(new InputState { HeldBits = 3, MoveX = -6, RouterStateId = 1 });
        layer.Step(false);

        Assert.Equal(1UL, rec.Frames[0].HeldBits);
        Assert.Equal((short)2, rec.Frames[0].MoveX);
        Assert.Equal(3UL, rec.Frames[1].HeldBits);
        Assert.Equal((short)-6, rec.Frames[1].MoveX);
        Assert.Equal((short)0, rec.Frames[1].MoveY);
    }

    [Fact]
    [Trait("Id", "SC-R17-1")]
    public void ZeroTickFrame_KeepsPressLatched_DeliveredInOrderOnNextTickThatRuns()
    {
        (ClientLayerSut layer, FrameRecorder rec, _) = Make();
        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Press, 1));
        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Release, 2));
        layer.Step(true);
        layer.Step(false);
        layer.Step(false);

        // A frame that runs 0 ticks: the router still latches a press, nobody calls Step.
        layer.InputWriter.PushEdge(Edge(Use, EdgeKind.Press, 3));
        Assert.Equal(1, layer.InputLatch.PendingEdgeCount);
        Assert.Equal(3u, layer.ClientTick);

        // Another 0-tick frame adds a second edge; order is preserved across frames.
        layer.InputWriter.PushEdge(Edge(Use, EdgeKind.Release, 4));
        Assert.Equal(2, layer.InputLatch.PendingEdgeCount);

        layer.Step(true);

        InputFrame next = rec.Frames[3];
        Assert.Equal(2, next.EdgeCount);
        AssertEdge(next, 0, Use, EdgeKind.Press, 3);
        AssertEdge(next, 1, Use, EdgeKind.Release, 4);
        Assert.Equal(0, layer.InputLatch.PendingEdgeCount);
    }

    [Fact]
    [Trait("Id", "SC-R17-1")]
    public void EdgesPushedBeforeNonFirstTick_StayLatchedUntilNextFirstTick()
    {
        (ClientLayerSut layer, FrameRecorder rec, _) = Make();
        layer.Step(true);
        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Press, 1));

        layer.Step(false);
        Assert.Equal(0, rec.Frames[1].EdgeCount);
        Assert.Equal(1, layer.InputLatch.PendingEdgeCount);

        layer.Step(true);
        Assert.Equal(1, rec.Frames[2].EdgeCount);
        AssertEdge(rec.Frames[2], 0, Fire, EdgeKind.Press, 1);
    }

    [Fact]
    [Trait("Id", "SC-R17-1")]
    public void DroppedCatchUpTicks_NeverDiscardAnEdge_BatchOfOneStillDeliversAll()
    {
        (ClientLayerSut layer, FrameRecorder rec, _) = Make();
        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Press, 1));
        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Release, 2));

        // The driver dropped the remaining catch-up ticks: only the first tick of the batch ran.
        layer.Step(true);

        Assert.Equal(2, rec.Frames[0].EdgeCount);
    }

    [Fact]
    public void FirstTickWithNoPendingEdges_EmitsEmptyFrame_AfterEdgesWereCleared()
    {
        (ClientLayerSut layer, FrameRecorder rec, _) = Make();
        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Press, 1));
        layer.Step(true);

        layer.Step(true);

        Assert.Equal(1, rec.Frames[0].EdgeCount);
        Assert.Equal(0, rec.Frames[1].EdgeCount);
        Assert.True(rec.Frames[1].IsFirstTickInBatch);
    }

    [Fact]
    public void Frame_OnLayerBeforeAnyTick_IsDefault()
    {
        (ClientLayerSut layer, _, _) = Make();

        Assert.Equal(0, layer.InputLatch.Frame.EdgeCount);
        Assert.Equal(0u, layer.InputLatch.Frame.Tick);
    }

    [Fact]
    public void GetEdge_OutOfRange_Throws()
    {
        (ClientLayerSut layer, FrameRecorder rec, _) = Make();
        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Cancel, 1));
        layer.Step(true);
        InputFrame f = rec.Frames[0];

        Assert.Equal(EdgeKind.Cancel, f.GetEdge(0).Kind);
        Assert.Throws<ArgumentOutOfRangeException>(() => f.GetEdge(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => f.GetEdge(-1));
    }

    [Fact]
    public void ThirtyTwoPressesThenTwoReleases_ReleasesAreNeverDropped_CarryOverToNextFirstTick()
    {
        (ClientLayerSut layer, FrameRecorder rec, SimCounters counters) = Make();
        for (ushort i = 0; i < InputFrame.MaxEdges; i++)
        {
            layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Press, i));
        }

        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Release, 99));
        layer.InputWriter.PushEdge(Edge(Fire, EdgeKind.Cancel, 100));

        layer.Step(true);
        InputFrame f = rec.Frames[0];

        // Dropping the two newest presses makes room for the 2 non-press edges: nothing but presses is lost.
        Assert.Equal(InputFrame.MaxEdges, f.EdgeCount);
        Assert.Equal((ushort)2, f.DroppedEdges);
        Assert.Equal(2L, counters[CounterId.InputEdgeOverflow]);
        Assert.Equal(EdgeKind.Release, f.GetEdge(InputFrame.MaxEdges - 2).Kind);
        Assert.Equal(EdgeKind.Cancel, f.GetEdge(InputFrame.MaxEdges - 1).Kind);
        Assert.Equal(0, layer.InputLatch.PendingEdgeCount);
    }

    [Fact]
    public void EdgeCapacity_IsThirtyTwo()
    {
        Assert.Equal(32, InputFrame.MaxEdges);
    }

    [Fact]
    public void InputEdge_RoundTripsItsFields()
    {
        var e = new InputEdge((FrameAction)5, EdgeKind.Release, 12345, 77);

        Assert.Equal((FrameAction)5, e.Action);
        Assert.Equal(EdgeKind.Release, e.Kind);
        Assert.Equal((ushort)12345, e.ArrivalSeq);
        Assert.Equal((ushort)77, e.RouterStateId);
    }
}
