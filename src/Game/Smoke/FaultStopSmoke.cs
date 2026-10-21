using PerformativeMail.Game.Hosting;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/003 AC5 (TR-sim-007, ADR-0002 Implementation Guidelines): each fault
/// source, injected into a fresh sequence, faults the real <see cref="GameLoop"/> exactly once
/// through its <see cref="IFaultSink"/>, and run-frame is never called again over the next 10
/// frames. Sources: run-frame throws, a pre hook throws, a post hook throws, the render-clock
/// sample throws, <c>Present</c> throws, the world reports faulted after run-frame (no throw),
/// a <c>_Process</c> call from a background thread (the thread check faults before any Sim call),
/// and run-frame throwing while the log observer throws on the FAULT line (the loop latches before
/// it logs, so it still stops and the sink still gets the fault). The <c>[GameLoop] FAULT</c> lines on stderr are expected.
/// </summary>
[SmokeScenario("fault-stop")]
public sealed class FaultStopSmoke : SmokeScenario
{
    private const int TriggerCall = 3;
    private const int MaxFramesToFault = 10;
    private const int FramesAfterFault = 10;

    private static readonly string[] _cases =
    [
        "run-frame-throws",
        "pre-hook-throws",
        "post-hook-throws",
        "sampler-throws",
        "present-throws",
        "world-faulted",
        "wrong-thread",
        "log-observer-throws",
    ];

    private const string ObserverThrowMessage = "log observer throws on the FAULT line";

    private readonly LogRecorder _log = new();
    private FrameLoopRig _rig = null!;
    private RecordingFaultSink _sink = null!;
    private int _bootThread;
    private int _presentsAtFault;
    private int _case = -1;
    private int _frames;
    private bool _faultSeen;
    private int _callsAtFault;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx) => NextCase(ctx);

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        string name = _cases[_case];
        GameLoop loop = ctx.Loop;
        _frames++;
        if (!_faultSeen)
        {
            if (!loop.IsFaulted)
            {
                if (_frames >= MaxFramesToFault)
                {
                    ctx.Fail($"{name}: loop not faulted after {_frames} frames");
                    return NextCase(ctx);
                }

                return SmokeStatus.Running;
            }

            OnFaulted(ctx, name);
            return SmokeStatus.Running;
        }

        if (_frames < FramesAfterFault)
        {
            return SmokeStatus.Running;
        }

        Expect(ctx, $"{name}: run-frame calls over {FramesAfterFault} frames after the fault", _rig.Run.Calls - _callsAtFault, 0);
        Expect(ctx, $"{name}: Present calls over {FramesAfterFault} frames after the fault", _rig.Presentation.Calls - _presentsAtFault, 0);
        Expect(ctx, $"{name}: fault sink messages after {FramesAfterFault} more frames", _sink.Messages.Count, 1);
        Expect(ctx, $"{name}: loop still faulted", loop.IsFaulted, true);
        return NextCase(ctx);
    }

    private void OnFaulted(SmokeContext ctx, string name)
    {
        _faultSeen = true;
        _frames = 0;
        _callsAtFault = _rig.Run.Calls;
        _presentsAtFault = _rig.Presentation.Calls;
        ctx.Pass($"{name}: GameLoop.IsFaulted after the injected fault (run-frame calls so far {_callsAtFault})");
        Expect(ctx, $"{name}: fault sink message count", _sink.Messages.Count, 1);
        string expected = name == "world-faulted" ? "Faulted" : name == "wrong-thread" ? nameof(InvalidOperationException) : nameof(InjectedFaultException);
        string message = _sink.Messages.Count > 0 ? _sink.Messages[0] : string.Empty;
        if (message.Contains(expected, StringComparison.Ordinal))
        {
            ctx.Pass($"{name}: fault message names '{expected}': {message}");
        }
        else
        {
            ctx.Fail($"{name}: fault message '{message}' does not contain '{expected}'");
        }

        Expect(ctx, $"{name}: fault sink called on the main (boot) thread", _sink.Threads.Count > 0 && _sink.Threads[0] == _bootThread, true);
        CheckFaultLog(ctx, name);
        if (name == "world-faulted")
        {
            // The faulting frame stops right after run-frame: no sample, no present.
            Expect(ctx, $"{name}: sample/present calls (stopped after run-frame)", $"{_rig.Sampler.Calls},{_rig.Presentation.Calls}", $"{TriggerCall - 1},{TriggerCall - 1}");
        }
    }

    private SmokeStatus NextCase(SmokeContext ctx)
    {
        _case++;
        if (_case >= _cases.Length)
        {
            return SmokeStatus.Done;
        }

        _rig = new FrameLoopRig(preHooks: 2, postHooks: 2);
        _sink = new RecordingFaultSink();
        _frames = 0;
        _faultSeen = false;
        GameLoop loop = ctx.Loop;
        _bootThread = System.Environment.CurrentManagedThreadId;
        _log.Clear();
        loop.LogObserver = _log.Observer;
        loop.FaultSink = _sink;
        switch (_cases[_case])
        {
            case "run-frame-throws": _rig.Run.ThrowOnCall = TriggerCall; break;
            case "pre-hook-throws": _rig.Pre[1].ThrowOnCall = TriggerCall; break;
            case "post-hook-throws": _rig.Post[0].ThrowOnCall = TriggerCall; break;
            case "sampler-throws": _rig.Sampler.ThrowOnCall = TriggerCall; break;
            case "present-throws": _rig.Presentation.ThrowOnCall = TriggerCall; break;
            case "world-faulted": _rig.Run.FaultAfterCall = TriggerCall; break;
            case "log-observer-throws":
                _rig.Run.ThrowOnCall = TriggerCall;
                loop.LogObserver = ThrowOnFaultLine;
                break;
            default: break;
        }

        loop.InstallForSmoke(_rig.Sequence, armStart: true);
        Expect(ctx, $"{_cases[_case]}: fresh install clears the fault latch", loop.IsFaulted, false);
        if (_cases[_case] == "wrong-thread")
        {
            RunOffThread(ctx, loop);
        }
        else if (_cases[_case] == "log-observer-throws")
        {
            RunToObserverThrow(ctx, loop);
        }

        return SmokeStatus.Running;
    }

    private void ThrowOnFaultLine(string line)
    {
        _log.Observer(line);
        if (line.StartsWith("[GameLoop] FAULT ", StringComparison.Ordinal))
        {
            throw new InjectedFaultException(ObserverThrowMessage);
        }
    }

    // The observer's exception leaves _Process; the frames are run here so the scenario catches it
    // instead of the engine.
    private void RunToObserverThrow(SmokeContext ctx, GameLoop loop)
    {
        Exception? escaped = null;
        for (int i = 0; i < TriggerCall && escaped is null; i++)
        {
            try
            {
                loop._Process(0);
            }
            catch (InjectedFaultException ex) when (ex.Message == ObserverThrowMessage)
            {
                escaped = ex;
            }
        }

        Expect(ctx, "log-observer-throws: the observer's exception left _Process", escaped is not null, true);
        Expect(ctx, "log-observer-throws: loop latched faulted despite the throwing observer", loop.IsFaulted, true);
        Expect(ctx, "log-observer-throws: run-frame calls up to the fault", _rig.Run.Calls, TriggerCall);
        Expect(ctx, "log-observer-throws: fault sink still called", _sink.Messages.Count, 1);
    }

    // AC5: the exception is logged with its type and stack (the world fault has no exception, only its message).
    private void CheckFaultLog(SmokeContext ctx, string name)
    {
        bool ok = name switch
        {
            "world-faulted" => _log.Any("[GameLoop] FAULT ", "world Faulted"),
            "wrong-thread" => _log.Any("[GameLoop] FAULT ", typeof(InvalidOperationException).FullName!, " at "),
            _ => _log.Any("[GameLoop] FAULT ", typeof(InjectedFaultException).FullName!, " at "),
        };
        Expect(ctx, $"{name}: fault logged with type and stack", ok, true);
        // The throwing observer cuts the logging short after the FAULT line.
        Expect(ctx, $"{name}: session-ended line logged", _log.Any("[GameLoop] session ended"), name != "log-observer-throws");
    }

    private void RunOffThread(SmokeContext ctx, GameLoop loop)
    {
        // GameLoop._Process touches no Godot API before the thread check; the fault path then only
        // prints and queues the sink call with Callable.CallDeferred. That both are safe off the main
        // thread is long-standing Godot 4 behaviour, UNVERIFIED for 4.7.1 (observed on 4.7.2).
        int bootThread = System.Environment.CurrentManagedThreadId;
        int worker = -1;
        Task.Run(() =>
        {
            worker = System.Environment.CurrentManagedThreadId;
            loop._Process(0);
        }).Wait();
        if (worker != bootThread)
        {
            ctx.Pass($"wrong-thread: _Process called from managed thread {worker} (boot thread {bootThread})");
        }
        else
        {
            ctx.Fail($"wrong-thread: background task ran on the boot thread {bootThread}; the case proves nothing");
        }

        Expect(ctx, "wrong-thread: run-frame calls on the off-thread frame", _rig.Run.Calls, 0);
        Expect(ctx, "wrong-thread: loop latched faulted at once", loop.IsFaulted, true);
        Expect(ctx, "wrong-thread: sink not called on the worker (delivered deferred on the main thread)", _sink.Messages.Count, 0);
        Expect(ctx, "wrong-thread: StartPending cleared (driver never started)", $"{loop.StartPending},{_rig.Driver.State}", "False,Idle");
    }
}
