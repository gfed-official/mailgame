using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Core;
using static PerformativeMail.Client.Core.Input.PhysicalInput;
using ClientLayerSut = PerformativeMail.Sim.ClientLayer.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// Story input-controls 028 AC "Call order" (ADR-0012 sections 4 and 6): <see cref="RouterFrameCalls.BeforeRunFrame"/> hands
/// the frame's immediates to their sinks before it flushes to the latch, and over the real client latch a frame that runs 0
/// ticks keeps its edges latched while the next frame's 3 catch-up ticks carry them on the first tick only. The host shell's
/// hook order around <c>RunFrame</c> and <c>Present</c> is the <c>input-boot</c> smoke's.
/// </summary>
public class RouterFrameCallsTests
{
    private readonly InputTestHarness _h = new();

    [Fact]
    public void BeforeRunFrame_DispatchesImmediatesBeforeTheLatchFlush()
    {
        var order = new List<string>();
        InputRouter router = Router();
        router.RegisterSink(ActionId.Drop, new OrderSink(order));
        var calls = new RouterFrameCalls(router, new OrderLatch(order), _h.Presented);
        router.Feed(_h.Press(KeyQ));
        router.Feed(_h.Press(KeySpace));

        calls.BeforeRunFrame();

        Assert.Equal("immediate", order[0]);
        Assert.Contains("edge", order);
        Assert.Equal(1, order.Count(s => s == "immediate"));
    }

    [Fact]
    public void ZeroTickFrame_KeepsEdgesLatched_NextThreeTickFrameCarriesThemOnTheFirstTickOnly()
    {
        var builder = new ClientLayerBuilder(new ValidClock(), new SimCounters());
        var frames = new FrameRecorder();
        builder.Bind(ClientStepId.CarrierStep, frames);
        ClientLayerSut layer = builder.Build();
        InputRouter router = Router();
        var calls = new RouterFrameCalls(router, layer.InputWriter, _h.Presented);

        router.Feed(_h.Press(KeySpace));
        calls.BeforeRunFrame();               // frame 1: RunFrame runs no tick
        int latchedAfterZeroTicks = layer.InputLatch.PendingEdgeCount;
        calls.BeforeRunFrame();               // frame 2: nothing new, then 3 catch-up ticks
        layer.Step(isFirstTickInBatch: true);
        layer.Step(isFirstTickInBatch: false);
        layer.Step(isFirstTickInBatch: false);

        Assert.Equal(1, latchedAfterZeroTicks);
        Assert.Equal([1, 0, 0], frames.Frames.Select(f => (int)f.EdgeCount));
        InputEdge jump = frames.Frames[0].GetEdge(0);
        Assert.Equal((FrameAction.Jump, EdgeKind.Press), (jump.Action, jump.Kind));
    }

    private InputRouter Router() =>
        new(BuiltInActionMap.CreateM1a(), InputSettings.Defaults, _h.Clock, _h.Interaction, _h.Presented, _h.Carrier, _h.ThrowAim, _h.Ticks, new UiEventLog());

    private sealed class OrderSink(List<string> order) : IImmediateActionSink
    {
        public void OnImmediate(in ImmediateAction a) => order.Add("immediate");
    }

    private sealed class OrderLatch(List<string> order) : IInputLatchWriter
    {
        public void PushEdge(in InputEdge e) => order.Add("edge");

        public void PushSteps(FrameAction a, int n) => order.Add("steps");

        public void SetState(in InputState s) => order.Add("state");
    }

    private sealed class ValidClock : IHostClock
    {
        public uint EstimatedHostTick => 0;

        public bool IsValid => true;
    }

    private sealed class FrameRecorder : IClientStep
    {
        public List<InputFrame> Frames { get; } = [];

        public void Step(in ClientStepContext ctx) => Frames.Add(ctx.Input);
    }
}
