using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 003 AC-1 and AC-5: the fake clock (<c>ms = floor(tick * 1000 / 30) + subTickOffset</c>), the record
/// types and the harness that stamps records from it. Deterministic: no I/O, no wall clock.
/// </summary>
[Trait("Id", "TR-input-001")]
public class FakeClockTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 33)]
    [InlineData(2, 66)]
    [InlineData(3, 100)]
    [InlineData(29, 966)]
    [InlineData(30, 1000)]
    [InlineData(31, 1033)]
    [InlineData(300, 10000)]
    public void NowMs_AtTickWithNoOffset_IsFloorOfTickTimes1000Over30(long tick, long expectedMs)
    {
        var clock = new FakeInputClock();

        clock.SetTick(tick);

        Assert.Equal(expectedMs, clock.NowMs);
    }

    [Theory]
    [InlineData(1, 0, 33)]
    [InlineData(1, 10, 43)]
    [InlineData(3, 20, 120)]
    [InlineData(30, 5, 1005)]
    public void NowMs_WithSubTickOffset_AddsTheOffset(long tick, int offset, long expectedMs)
    {
        var clock = new FakeInputClock();
        clock.SetTick(tick);

        clock.SetSubTickOffset(offset);

        Assert.Equal(expectedMs, clock.NowMs);
    }

    [Fact]
    public void SetSubTickOffset_DoesNotChangeTheTick()
    {
        var clock = new FakeInputClock();
        clock.SetTick(7);

        clock.SetSubTickOffset(12);

        Assert.Equal(7, clock.Tick);
    }

    [Fact]
    public void AdvanceTicks_ClearsTheSubTickOffsetAndMovesForward()
    {
        var clock = new FakeInputClock();
        clock.SetTick(2);
        clock.SetSubTickOffset(20);

        clock.AdvanceTicks(1);

        Assert.Equal(3, clock.Tick);
        Assert.Equal(0, clock.SubTickOffsetMs);
        Assert.Equal(100, clock.NowMs);
    }

    [Fact]
    public void NowMs_AcrossThirtyTicks_AdvancesExactlyOneSecond()
    {
        var clock = new FakeInputClock();
        long start = clock.NowMs;

        clock.AdvanceTicks(30);

        Assert.Equal(1000, clock.NowMs - start);
    }

    [Fact]
    public void NowMs_WhenAdvancingTickByTick_NeverGoesBackwards()
    {
        var clock = new FakeInputClock();
        long previous = clock.NowMs;

        for (int i = 0; i < 90; i++)
        {
            clock.AdvanceTicks(1);
            Assert.True(clock.NowMs >= previous);
            previous = clock.NowMs;
        }
    }

    [Fact]
    public void Setters_WithNegativeValues_Throw()
    {
        var clock = new FakeInputClock();

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetTick(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetSubTickOffset(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceTicks(-1));
    }

    [Fact]
    public void FakeInputClock_IsAnIInputClock()
    {
        IInputClock clock = new FakeInputClock();

        Assert.Equal(0, clock.NowMs);
    }

    [Fact]
    public void RawInputRecord_StoresEveryField()
    {
        var r = new RawInputRecord(123, RawKind.PadAxis, DeviceClass.Pad, 2, PhysicalInput.PadLeftStickX, true, true, -16384, 7);

        Assert.Equal(123, r.ArrivalMs);
        Assert.Equal(RawKind.PadAxis, r.Kind);
        Assert.Equal(DeviceClass.Pad, r.Device);
        Assert.Equal(2, r.PadIndex);
        Assert.Equal(PhysicalInput.PadLeftStickX, r.Input);
        Assert.True(r.Pressed);
        Assert.True(r.Echo);
        Assert.Equal(-16384, r.Value);
        Assert.Equal(7, r.Value2);
    }

    [Fact]
    public void RawKindAndDeviceClass_HaveExactlyTheSpecifiedMembersInOrder()
    {
        Assert.Equal(
            new[] { "Key", "MouseButton", "MouseMotion", "Wheel", "PadButton", "PadAxis", "Synthetic" },
            Enum.GetNames<RawKind>());
        Assert.Equal(new[] { "Keyboard", "Mouse", "Pad", "Synthetic" }, Enum.GetNames<DeviceClass>());
    }

    [Fact]
    public void Press_AtATickAndOffset_StampsTheClockTimeAndInfersKindAndDevice()
    {
        var h = new InputTestHarness();
        h.AdvanceTicks(3);
        h.Clock.SetSubTickOffset(20);

        RawInputRecord r = h.Press(PhysicalInput.KeyW);

        Assert.Equal(120, r.ArrivalMs);
        Assert.Equal(RawKind.Key, r.Kind);
        Assert.Equal(DeviceClass.Keyboard, r.Device);
        Assert.Equal(PhysicalInput.KeyW, r.Input);
        Assert.True(r.Pressed);
        Assert.False(r.Echo);
    }

    [Fact]
    public void Release_AfterAdvancingTicks_IsStampedLaterThanThePress()
    {
        var h = new InputTestHarness();
        RawInputRecord press = h.Press(PhysicalInput.MouseLeft);

        h.AdvanceTicks(30);
        RawInputRecord release = h.Release(PhysicalInput.MouseLeft);

        Assert.Equal(RawKind.MouseButton, press.Kind);
        Assert.Equal(DeviceClass.Mouse, press.Device);
        Assert.False(release.Pressed);
        Assert.Equal(1000, release.ArrivalMs - press.ArrivalMs);
    }

    [Fact]
    public void Press_OfPadButtonWithPadIndex_CarriesThePadIndex()
    {
        var h = new InputTestHarness();

        RawInputRecord r = h.Press(PhysicalInput.PadA, pad: 1);

        Assert.Equal(RawKind.PadButton, r.Kind);
        Assert.Equal(DeviceClass.Pad, r.Device);
        Assert.Equal(1, r.PadIndex);
    }

    [Fact]
    public void Press_WithSyntheticDevice_IsASyntheticRecord()
    {
        var h = new InputTestHarness();

        RawInputRecord r = h.Press(PhysicalInput.KeyE, DeviceClass.Synthetic);

        Assert.Equal(RawKind.Synthetic, r.Kind);
        Assert.Equal(DeviceClass.Synthetic, r.Device);
    }

    [Fact]
    public void Press_WithEchoFlag_MarksTheRecordAsAnEcho()
    {
        var h = new InputTestHarness();

        Assert.True(h.Press(PhysicalInput.KeyW, echo: true).Echo);
    }

    [Theory]
    [InlineData(PhysicalInput.PadLeftStickX)]
    [InlineData(PhysicalInput.PadRightTrigger)]
    public void PressOrRelease_OfAnAxisInput_ThrowsAndPointsToAxis(PhysicalInput axis)
    {
        var h = new InputTestHarness();

        Assert.Throws<ArgumentException>(() => h.Press(axis));
        Assert.Throws<ArgumentException>(() => h.Release(axis));
    }

    [Fact]
    public void Axis_WithQ15Value_CarriesTheValueOnAPadAxisRecord()
    {
        var h = new InputTestHarness();

        RawInputRecord r = h.Axis(PhysicalInput.PadLeftStickY, -12000, pad: 0);
        RawInputRecord rest = h.Axis(PhysicalInput.PadLeftStickY, 0);

        Assert.Equal(RawKind.PadAxis, r.Kind);
        Assert.Equal(DeviceClass.Pad, r.Device);
        Assert.Equal(-12000, r.Value);
        Assert.True(r.Pressed);
        Assert.False(rest.Pressed);
    }

    [Fact]
    public void Wheel_WithNotches_CarriesTheNotchCount()
    {
        var h = new InputTestHarness();

        RawInputRecord r = h.Wheel(PhysicalInput.WheelDown, 3);

        Assert.Equal(RawKind.Wheel, r.Kind);
        Assert.Equal(DeviceClass.Mouse, r.Device);
        Assert.Equal(PhysicalInput.WheelDown, r.Input);
        Assert.Equal(3, r.Value);
    }

    [Fact]
    public void Motion_WithCounts_CarriesBothAxes()
    {
        var h = new InputTestHarness();

        RawInputRecord r = h.Motion(-4, 9);

        Assert.Equal(RawKind.MouseMotion, r.Kind);
        Assert.Equal(-4, r.Value);
        Assert.Equal(9, r.Value2);
    }

    [Fact]
    public void RecordingLatchWriter_RecordsEdgesStepsAndStatesInOrder()
    {
        var latch = new RecordingLatchWriter();
        var edge = new InputEdge(
            FrameAction.Jump, EdgeKind.Press, 1, 1);

        latch.PushEdge(in edge);
        latch.PushSteps(FrameAction.Interact, -2);
        latch.SetState(new InputState { MoveY = 5 });

        Assert.Equal(FrameAction.Jump, Assert.Single(latch.Edges).Action);
        Assert.Equal(new RecordingLatchWriter.StepCall(FrameAction.Interact, -2), Assert.Single(latch.Steps));
        Assert.Equal(5, latch.LastState.MoveY);
        latch.Clear();
        Assert.Empty(latch.Edges);
        Assert.Empty(latch.Steps);
        Assert.Empty(latch.States);
    }

    [Fact]
    public void RecordingImmediateSink_RecordsDispatchesInOrder()
    {
        var sink = new RecordingImmediateSink();

        sink.OnImmediate(4, EdgeKind.Press, 100, 1, 9, 0);
        sink.OnImmediate(5, EdgeKind.Release, 200, 1, 9, 1);

        Assert.Equal(2, sink.Calls.Count);
        Assert.Equal(new RecordingImmediateSink.ImmediateCall(4, EdgeKind.Press, 100, 1, 9, 0), sink.Calls[0]);
        Assert.Equal(200, sink.Calls[1].ArrivalMs);
        sink.Clear();
        Assert.Empty(sink.Calls);
    }
}
