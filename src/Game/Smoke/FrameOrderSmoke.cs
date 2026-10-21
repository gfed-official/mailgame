using Godot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Hosting;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/003 AC1-AC3: the real <see cref="GameLoop"/> runs the installed sequence
/// once per <c>_Process</c> in the order pre hooks, run-frame, sample, present, post hooks, over a
/// 0-tick and a 3-tick (catch-up) frame; <c>Present</c> gets the driver's <c>AlphaPermille</c>; the
/// driver starts on the first frame after <see cref="GameLoop.ArmStart"/>; registration closes
/// after the first frame; an empty hook list is fine; the Stopwatch clock is in range and
/// monotonic. Tick counts come from a fake clock, never wall time.
/// </summary>
[SmokeScenario("frame-order")]
public sealed class FrameOrderSmoke : SmokeScenario
{
    private const int StopwatchFrames = 10;

    private FrameLoopRig _rig = null!;
    private FrameLoopRig _empty = null!;
    private StopwatchMonotonicClock _stopwatch = null!;
    private int _step;
    private int _callsBefore;
    private long _lastStopwatch;
    private int _stopwatchFrames;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        Expect(ctx, "GameLoop.ProcessMode", loop.ProcessMode, Node.ProcessModeEnum.Always);
        Expect(ctx, "GameLoop.ProcessPriority", loop.ProcessPriority, int.MinValue);
        Expect(ctx, "GameLoop.HasSequence before install", loop.HasSequence, false);
        ExpectThrows<InvalidOperationException>(ctx, "RegisterPreFrameHook with no sequence installed",
            () => loop.RegisterPreFrameHook(new RecordingPreHook(new FrameOrderLog(), 9)));

        _rig = new FrameLoopRig(preHooks: 2, postHooks: 2, maxCatchUp: 4, register: false);
        loop.InstallForSmoke(_rig.Sequence, armStart: false);
        loop.RegisterPreFrameHook(_rig.Pre[0]);
        loop.RegisterPreFrameHook(_rig.Pre[1]);
        loop.RegisterPostPresentHook(_rig.Post[0]);
        loop.RegisterPostPresentHook(_rig.Post[1]);
        Expect(ctx, "hooks registered through GameLoop (pre,post)", $"{_rig.Sequence.PreFrameHookCount},{_rig.Sequence.PostPresentHookCount}", "2,2");
        Expect(ctx, "StartPending after install without arming", loop.StartPending, false);
        BeginFrame();
    }

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        switch (_step++)
        {
            case 0:
                // Frame 1: installed, not armed -> runs the full order with no ticks, driver Idle.
                CheckFrame(ctx, "frame 1 (not armed)", _rig, expectTicks: 0);
                Expect(ctx, "driver state before ArmStart", _rig.Driver.State, TickDriverState.Idle);
                loop.ArmStart();
                Expect(ctx, "StartPending after ArmStart", loop.StartPending, true);
                Expect(ctx, "driver state after ArmStart, before the next _Process", _rig.Driver.State, TickDriverState.Idle);

                // Time passing before Start must not become ticks: Start takes the clock at the frame.
                _rig.Clock.Advance(5 * FakeClock.TickCounts);
                BeginFrame();
                return SmokeStatus.Running;

            case 1:
                CheckFrame(ctx, "frame 2 (first armed frame, 0 ticks)", _rig, expectTicks: 0);
                Expect(ctx, "driver state after the first armed _Process", _rig.Driver.State, TickDriverState.Running);
                Expect(ctx, "StartPending after the first armed _Process", loop.StartPending, false);
                ExpectThrows<InvalidOperationException>(ctx, "RegisterPreFrameHook after the first frame",
                    () => loop.RegisterPreFrameHook(new RecordingPreHook(_rig.Log, 8)));
                ExpectThrows<InvalidOperationException>(ctx, "RegisterPostPresentHook after the first frame",
                    () => loop.RegisterPostPresentHook(new RecordingPostHook(_rig.Log, 8)));
                _rig.Clock.Advance(3 * FakeClock.TickCounts);
                BeginFrame();
                return SmokeStatus.Running;

            case 2:
                CheckFrame(ctx, "frame 3 (3 catch-up ticks)", _rig, expectTicks: 3);
                _rig.Clock.Advance(FakeClock.TickCounts * 3 / 2);
                BeginFrame();
                return SmokeStatus.Running;

            case 3:
                CheckFrame(ctx, "frame 4 (1.5 tick periods)", _rig, expectTicks: 1);
                Expect(ctx, "Present alpha at 1.5 tick periods", _rig.Presentation.LastAlpha, 500);

                // A second install with no hooks at all: the frame still runs run-frame, sample, present.
                _empty = new FrameLoopRig(preHooks: 0, postHooks: 0);
                loop.InstallForSmoke(_empty.Sequence, armStart: true);
                _rig = _empty;
                BeginFrame();
                return SmokeStatus.Running;

            case 4:
                CheckFrame(ctx, "frame 5 (empty hook lists, armed)", _empty, expectTicks: 0);
                Expect(ctx, "empty-hook driver state", _empty.Driver.State, TickDriverState.Running);
                _empty.Clock.Advance(2 * FakeClock.TickCounts);
                BeginFrame();
                return SmokeStatus.Running;

            case 5:
                CheckFrame(ctx, "frame 6 (empty hook lists, 2 ticks)", _empty, expectTicks: 2);
                _stopwatch = new StopwatchMonotonicClock();
                long f = _stopwatch.Frequency;
                if (f >= TickDriver.MinFrequency && f <= TickDriver.MaxFrequency)
                {
                    ctx.Pass($"StopwatchMonotonicClock.Frequency {f} within [10^3, 10^12]");
                }
                else
                {
                    ctx.Fail($"StopwatchMonotonicClock.Frequency {f} outside [10^3, 10^12]");
                }

                Expect(ctx, "StopwatchMonotonicClock.Frequency == Stopwatch.Frequency", f, System.Diagnostics.Stopwatch.Frequency);
                _lastStopwatch = _stopwatch.Timestamp;
                return SmokeStatus.Running;

            default:
                long now = _stopwatch.Timestamp;
                if (now < _lastStopwatch)
                {
                    ctx.Fail($"StopwatchMonotonicClock.Timestamp went backwards: {_lastStopwatch} -> {now}");
                    return SmokeStatus.Done;
                }

                _lastStopwatch = now;
                if (++_stopwatchFrames < StopwatchFrames)
                {
                    return SmokeStatus.Running;
                }

                ctx.Pass($"StopwatchMonotonicClock.Timestamp non-decreasing over {StopwatchFrames} frames");
                return SmokeStatus.Done;
        }
    }

    private void BeginFrame()
    {
        _rig.Log.Clear();
        _callsBefore = _rig.Run.Calls;
    }

    private void CheckFrame(SmokeContext ctx, string label, FrameLoopRig rig, int expectTicks)
    {
        int[] expected = rig.ExpectedFullFrame();
        if (rig.Log.Matches(expected))
        {
            ctx.Pass($"{label}: order {rig.Log.Describe()}");
        }
        else
        {
            ctx.Fail($"{label}: order {rig.Log.Describe()}, expected {string.Join(",", expected)}");
        }

        Expect(ctx, $"{label}: run-frame calls this frame", rig.Run.Calls - _callsBefore, 1);
        Expect(ctx, $"{label}: ticks", rig.Run.TicksThisFrame, expectTicks);
        int alpha = rig.Presentation.LastAlpha;
        if (alpha == rig.Driver.AlphaPermille && alpha >= 0 && alpha <= 999)
        {
            ctx.Pass($"{label}: Present alpha {alpha} == driver.AlphaPermille, in 0..999");
        }
        else
        {
            ctx.Fail($"{label}: Present alpha {alpha}, driver.AlphaPermille {rig.Driver.AlphaPermille}");
        }

        Expect(ctx, $"{label}: loop not faulted", ctx.Loop.IsFaulted, false);
    }
}
