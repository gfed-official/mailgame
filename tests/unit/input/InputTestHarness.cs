using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// The deterministic fake clock for input tests (input-controls story 003; GDD Formulas, Clocks). Milliseconds are
/// <c>floor(tick * 1000 / 30) + subTickOffset</c>: the Sim tick sets the base, the sub-tick offset places an event inside the
/// tick. Settable per tick and per offset. Never reads the wall clock.
/// </summary>
/// <example>
/// <code>
/// var clock = new FakeInputClock();
/// clock.AdvanceTicks(3);          // NowMs == 100
/// clock.SetSubTickOffset(20);     // NowMs == 120
/// </code>
/// </example>
public sealed class FakeInputClock : IInputClock
{
    /// <summary>The Sim tick rate the clock formula assumes.</summary>
    public const int TicksPerSecond = 30;

    /// <summary>The Sim tick the clock is at.</summary>
    public long Tick { get; private set; }

    /// <summary>The offset in milliseconds inside the current tick.</summary>
    public int SubTickOffsetMs { get; private set; }

    /// <inheritdoc />
    public long NowMs => MsOfTick(Tick) + SubTickOffsetMs;

    /// <summary>The first millisecond of <paramref name="tick"/>: <c>floor(tick * 1000 / 30)</c>.</summary>
    /// <param name="tick">The Sim tick (not negative).</param>
    /// <returns>The millisecond.</returns>
    public static long MsOfTick(long tick) => tick * 1000 / TicksPerSecond;

    /// <summary>Moves the clock to <paramref name="tick"/> and clears the sub-tick offset.</summary>
    /// <param name="tick">The tick (not negative).</param>
    public void SetTick(long tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        Tick = tick;
        SubTickOffsetMs = 0;
    }

    /// <summary>Sets the offset in milliseconds inside the current tick, without changing the tick.</summary>
    /// <param name="offsetMs">The offset (not negative).</param>
    public void SetSubTickOffset(int offsetMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offsetMs);
        SubTickOffsetMs = offsetMs;
    }

    /// <summary>Moves forward by <paramref name="ticks"/> ticks and clears the sub-tick offset.</summary>
    /// <param name="ticks">The number of ticks (not negative).</param>
    public void AdvanceTicks(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        SetTick(Tick + ticks);
    }
}

/// <summary>
/// Builds <see cref="RawInputRecord"/>s stamped from a <see cref="FakeInputClock"/> (input-controls story 003, reused by stories
/// 004 to 024). The record kind and device are inferred from the <see cref="PhysicalInput"/> block unless a device is passed;
/// <see cref="DeviceClass.Synthetic"/> makes the record <see cref="RawKind.Synthetic"/>.
/// </summary>
/// <example>
/// <code>
/// var h = new InputTestHarness();
/// router.Feed(h.Press(PhysicalInput.KeyE));
/// h.AdvanceTicks(2);
/// router.Feed(h.Release(PhysicalInput.KeyE));
/// router.FlushToLatch(h.Latch);
/// </code>
/// </example>
public sealed class InputTestHarness
{
    /// <summary>The shared fake clock. Pass it to the router as its <see cref="IInputClock"/>.</summary>
    public FakeInputClock Clock { get; } = new();

    /// <summary>Records everything the router flushes to the latch.</summary>
    public RecordingLatchWriter Latch { get; } = new();

    /// <summary>Records everything the router dispatches as an immediate action.</summary>
    public RecordingImmediateSink Immediates { get; } = new();

    /// <summary>Advances the shared clock by <paramref name="n"/> Sim ticks (clears the sub-tick offset).</summary>
    /// <param name="n">The number of ticks.</param>
    public void AdvanceTicks(int n) => Clock.AdvanceTicks(n);

    /// <summary>A press record stamped with the current clock time.</summary>
    /// <param name="input">A button, key or wheel input.</param>
    /// <param name="device">The device; inferred from <paramref name="input"/> when null.</param>
    /// <param name="pad">The gamepad index for pad inputs.</param>
    /// <param name="echo">True for an OS key repeat.</param>
    /// <returns>The record.</returns>
    public RawInputRecord Press(PhysicalInput input, DeviceClass? device = null, byte pad = 0, bool echo = false) =>
        Button(input, device, pad, pressed: true, echo);

    /// <summary>A release record stamped with the current clock time.</summary>
    /// <param name="input">A button or key input.</param>
    /// <param name="device">The device; inferred from <paramref name="input"/> when null.</param>
    /// <param name="pad">The gamepad index for pad inputs.</param>
    /// <returns>The record.</returns>
    public RawInputRecord Release(PhysicalInput input, DeviceClass? device = null, byte pad = 0) =>
        Button(input, device, pad, pressed: false, echo: false);

    /// <summary>A gamepad axis sample stamped with the current clock time.</summary>
    /// <param name="input">An axis input such as <see cref="PhysicalInput.PadLeftStickX"/>.</param>
    /// <param name="q15">The axis value in Q15.</param>
    /// <param name="pad">The gamepad index.</param>
    /// <returns>The record; <c>Pressed</c> is true when the value is not 0.</returns>
    public RawInputRecord Axis(PhysicalInput input, short q15, byte pad = 0) =>
        new(Clock.NowMs, RawKind.PadAxis, DeviceClass.Pad, pad, input, q15 != 0, false, q15);

    /// <summary>A wheel record stamped with the current clock time.</summary>
    /// <param name="input"><see cref="PhysicalInput.WheelUp"/> or <see cref="PhysicalInput.WheelDown"/>.</param>
    /// <param name="notches">The notch count (at least 1).</param>
    /// <returns>The record (a press with <c>Value</c> = notches).</returns>
    public RawInputRecord Wheel(PhysicalInput input, short notches = 1) =>
        new(Clock.NowMs, RawKind.Wheel, DeviceClass.Mouse, 0, input, true, false, notches);

    /// <summary>A relative mouse motion record stamped with the current clock time.</summary>
    /// <param name="dx">Relative X counts.</param>
    /// <param name="dy">Relative Y counts.</param>
    /// <returns>The record.</returns>
    public RawInputRecord Motion(short dx, short dy) =>
        new(Clock.NowMs, RawKind.MouseMotion, DeviceClass.Mouse, 0, PhysicalInput.None, false, false, dx, dy);

    private RawInputRecord Button(PhysicalInput input, DeviceClass? device, byte pad, bool pressed, bool echo)
    {
        DeviceClass resolved = device ?? DeviceOf(input);
        RawKind kind = resolved == DeviceClass.Synthetic ? RawKind.Synthetic : KindOf(input);
        if (kind == RawKind.PadAxis)
        {
            throw new ArgumentException($"{input} is an axis; use Axis(...) instead of Press/Release.", nameof(input));
        }

        short value = kind == RawKind.Wheel ? (short)1 : (short)0;
        return new RawInputRecord(Clock.NowMs, kind, resolved, resolved == DeviceClass.Pad ? pad : (byte)0, input, pressed, echo, value);
    }

    private static DeviceClass DeviceOf(PhysicalInput input) => ((ushort)input >> 8) switch
    {
        0x01 => DeviceClass.Keyboard,
        0x02 => DeviceClass.Mouse,
        0x03 => DeviceClass.Pad,
        _ => DeviceClass.Synthetic,
    };

    private static RawKind KindOf(PhysicalInput input) => input switch
    {
        PhysicalInput.WheelUp or PhysicalInput.WheelDown => RawKind.Wheel,
        PhysicalInput.PadLeftStickX or PhysicalInput.PadLeftStickY or PhysicalInput.PadRightStickX
            or PhysicalInput.PadRightStickY or PhysicalInput.PadLeftTrigger or PhysicalInput.PadRightTrigger => RawKind.PadAxis,
        _ => ((ushort)input >> 8) switch
        {
            0x01 => RawKind.Key,
            0x02 => RawKind.MouseButton,
            0x03 => RawKind.PadButton,
            _ => RawKind.Synthetic,
        },
    };
}

/// <summary>
/// An <see cref="IInputLatchWriter"/> that records every call in order (input-controls story 003). The router tests assert on
/// <see cref="Edges"/>, <see cref="Steps"/> and <see cref="States"/>.
/// </summary>
/// <example>
/// <code>
/// router.FlushToLatch(latch);
/// Assert.Single(latch.Edges);
/// Assert.Equal(FrameAction.Jump, latch.Edges[0].Action);
/// </code>
/// </example>
public sealed class RecordingLatchWriter : IInputLatchWriter
{
    /// <summary>One <see cref="PushSteps"/> call.</summary>
    /// <param name="Action">The action.</param>
    /// <param name="Steps">The signed steps.</param>
    public readonly record struct StepCall(FrameAction Action, int Steps);

    /// <summary>Every edge pushed, in call order.</summary>
    public List<InputEdge> Edges { get; } = new();

    /// <summary>Every <see cref="PushSteps"/> call, in call order.</summary>
    public List<StepCall> Steps { get; } = new();

    /// <summary>Every state set, in call order.</summary>
    public List<InputState> States { get; } = new();

    /// <summary>The most recent state, or the default state when none was set.</summary>
    public InputState LastState => States.Count == 0 ? default : States[^1];

    /// <inheritdoc />
    public void PushEdge(in InputEdge e) => Edges.Add(e);

    /// <inheritdoc />
    public void PushSteps(FrameAction a, int n) => Steps.Add(new StepCall(a, n));

    /// <inheritdoc />
    public void SetState(in InputState s) => States.Add(s);

    /// <summary>Forgets everything recorded.</summary>
    public void Clear()
    {
        Edges.Clear();
        Steps.Clear();
        States.Clear();
    }
}

/// <summary>
/// Records immediate-verb dispatches for router tests (input-controls story 003). Shaped after ADR-0012's
/// <c>IImmediateActionSink.OnImmediate(in ImmediateAction)</c> and the field list in story 013, but it does NOT implement that
/// interface yet: neither <c>IImmediateActionSink</c> nor <c>ImmediateAction</c> exists (story 013 owns both). Story 013 adds
/// the interface and an <c>OnImmediate(in ImmediateAction)</c> overload here that forwards to
/// <see cref="OnImmediate(ushort, EdgeKind, long, ushort, uint, byte)"/>, so existing assertions keep working.
/// </summary>
/// <example>
/// <code>
/// sink.OnImmediate(actionId: 4, EdgeKind.Press, arrivalMs: 100, routerStateId: 1, presentedSeq: 0, flags: 0);
/// Assert.Equal(1, sink.Calls.Count);
/// </code>
/// </example>
public sealed class RecordingImmediateSink
{
    /// <summary>One recorded dispatch.</summary>
    /// <param name="ActionId">The action's numeric id.</param>
    /// <param name="Edge">Press, release or cancel.</param>
    /// <param name="ArrivalMs">The arrival time of the firing edge.</param>
    /// <param name="RouterStateId">The router state the edge was gated against.</param>
    /// <param name="PresentedSeq">The presented-snapshot sequence.</param>
    /// <param name="Flags">Action flags (for example PlaceOnRelease).</param>
    public readonly record struct ImmediateCall(ushort ActionId, EdgeKind Edge, long ArrivalMs, ushort RouterStateId, uint PresentedSeq, byte Flags);

    /// <summary>Every dispatch received, in order.</summary>
    public List<ImmediateCall> Calls { get; } = new();

    /// <summary>Records one dispatch.</summary>
    /// <param name="actionId">The action's numeric id.</param>
    /// <param name="edge">Press, release or cancel.</param>
    /// <param name="arrivalMs">The arrival time of the firing edge.</param>
    /// <param name="routerStateId">The router state the edge was gated against.</param>
    /// <param name="presentedSeq">The presented-snapshot sequence.</param>
    /// <param name="flags">Action flags.</param>
    public void OnImmediate(ushort actionId, EdgeKind edge, long arrivalMs, ushort routerStateId, uint presentedSeq, byte flags) =>
        Calls.Add(new ImmediateCall(actionId, edge, arrivalMs, routerStateId, presentedSeq, flags));

    /// <summary>Forgets everything recorded.</summary>
    public void Clear() => Calls.Clear();
}
