using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Screens;
using PerformativeMail.Sim.Core;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/004 AC3 and AC5 (ADR-0019 sections 1 and 3, ADR-0003 Risks): a failing boot step shows the
/// <c>Main/Screens</c> error screen and stops. Each pass installs a fresh <see cref="FrameLoopRig"/> sequence with
/// <c>InstallForSmoke</c> (which clears the fault latch), runs <see cref="BootSequence"/> itself on a context from
/// <see cref="GameLoop.CreateBootContext"/>, asserts the latch at once (<c>IsFaulted</c>, <c>StartPending == false</c>,
/// one <c>[GameLoop] FAULT</c> line, later steps not run) and, one frame later (the sink may be delivered deferred), that
/// <c>BootErrorScreen</c> — the single fault sink — was called once and shows the right screen with the exception type,
/// message and (b) code name, Quit visible, and <c>RunFrame</c> never called. Passes: (a) a throwing step-4 contributor;
/// (b) step 6 throwing <c>SimBuildException(ReservedPhase)</c>; (c) a throwing content source (<c>ContentError</c> shows,
/// <c>BootError</c> does not, no <c>BOOT 3</c> line); (d) a clean boot followed by a frame fault shows the same
/// <c>BootError</c> screen with the session-fault title.
/// </summary>
[SmokeScenario("boot-failure")]
public sealed class BootFailureSmoke : SmokeScenario
{
    private const int FramesAfterFault = 3;

    private static readonly string[] _passes = ["a-contributor", "b-sim-build", "c-content", "d-frame-fault"];

    private readonly LogRecorder _log = new();
    private FrameLoopRig _rig = null!;
    private BootContext _boot = null!;
    private BootErrorScreen _screen = null!;
    private int _pass = -1;
    private int _faultsBefore;
    private int _frames;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        BootErrorScreen? screen = ctx.Loop.ErrorScreen;
        if (screen is null || BootSmoke.ContentScreen(ctx.Loop) is null)
        {
            ctx.Fail("Main/Screens/BootError or ContentError is missing; nothing to show");
            ctx.Quit();
            return;
        }

        _screen = screen;
        NextPass(ctx);
    }

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        string name = _passes[_pass];
        _frames++;
        if (name == "d-frame-fault")
        {
            return PollFrameFault(ctx);
        }

        if (_frames == 1)
        {
            CheckScreen(ctx, name);
        }

        if (_frames < FramesAfterFault)
        {
            return SmokeStatus.Running;
        }

        Expect(ctx, $"{name}: run-frame calls over {FramesAfterFault} frames after the boot fault", _rig.Run.Calls, 0);
        Expect(ctx, $"{name}: fault sink calls after {FramesAfterFault} frames", _screen.FaultCount - _faultsBefore, 1);
        Expect(ctx, $"{name}: loop still faulted", ctx.Loop.IsFaulted, true);
        return NextPass(ctx);
    }

    private SmokeStatus NextPass(SmokeContext ctx)
    {
        _pass++;
        if (_pass >= _passes.Length)
        {
            ctx.Loop.LogObserver = null;
            return SmokeStatus.Done;
        }

        string name = _passes[_pass];
        GameLoop loop = ctx.Loop;
        _rig = new FrameLoopRig(preHooks: 1, postHooks: 1);
        _frames = 0;
        _log.Clear();
        loop.InstallForSmoke(_rig.Sequence, armStart: false);
        Expect(ctx, $"{name}: fresh install clears the fault latch", loop.IsFaulted, false);
        loop.LogObserver = _log.Observer;
        _boot = loop.CreateBootContext();
        Expect(ctx, $"{name}: FaultSink is the BootError screen before step 1", ReferenceEquals(loop.FaultSink, _screen), true);
        _faultsBefore = _screen.FaultCount;
        IReadOnlyList<IBootStep> plan = name switch
        {
            "a-contributor" => loop.CreateBootPlan(contributors: [new ThrowingBootContributor(BootPlan.InputStep)]),
            "b-sim-build" => BootSmoke.PlanWithSimBuildFailure(loop),
            "c-content" => loop.CreateBootPlan(content: new ThrowingContentSource()),
            _ => loop.CreateBootPlan(),
        };
        if (name == "d-frame-fault")
        {
            _rig.Run.ThrowOnCall = 2;
        }

        bool ok = new BootSequence(plan).Run(_boot);
        if (name == "d-frame-fault")
        {
            Expect(ctx, $"{name}: boot returns true", ok, true);
            Expect(ctx, $"{name}: StartPending after the clean boot", loop.StartPending, true);
            return SmokeStatus.Running;
        }

        Expect(ctx, $"{name}: BootSequence.Run returns false", ok, false);
        CheckLatch(ctx, name);
        return SmokeStatus.Running;
    }

    // Synchronous with the failing step: the latch, the FAULT line and the boot log.
    private void CheckLatch(SmokeContext ctx, string name)
    {
        GameLoop loop = ctx.Loop;
        (int step, string stepName, BootFailureKind kind, string next) = name switch
        {
            "a-contributor" => (4, "input", BootFailureKind.Boot, "BOOT 5 "),
            "b-sim-build" => (6, "sim", BootFailureKind.Boot, "BOOT 7 "),
            _ => (2, "content", BootFailureKind.Content, "BOOT 3 "),
        };
        IReadOnlyList<string> lines = _boot.Log.Lines;
        Expect(ctx, $"{name}: IsFaulted right after the failing step", loop.IsFaulted, true);
        Expect(ctx, $"{name}: StartPending right after the failing step", loop.StartPending, false);
        Expect(ctx, $"{name}: [GameLoop] FAULT lines", _log.Count("[GameLoop] FAULT "), 1);
        Expect(ctx, $"{name}: FAULT line carries the full exception type and stack", _log.Any("[GameLoop] FAULT ", ExpectedType(name).FullName!, " at "), true);
        Expect(ctx, $"{name}: failure recorded in BootContext", $"{_boot.Failure?.Step},{_boot.Failure?.Name},{_boot.Failure?.Kind}", $"{step},{stepName},{kind}");
        Expect(ctx, $"{name}: BOOT {step} {stepName} FAIL line", BootSmoke.Has(lines, $"BOOT {step} {stepName} FAIL {ExpectedType(name).Name}"), true);
        Expect(ctx, $"{name}: no line for a later step ({next.Trim()})", BootSmoke.Has(lines, next), false);
        Expect(ctx, $"{name}: steps before {step} ran (ok or NOT WIRED)", BootSmoke.StatusLines(lines).Count(s => s.Status is "ok" or "NOT WIRED"), step - 1);
        if (name == "b-sim-build")
        {
            Expect(ctx, $"{name}: FAIL line names the SimBuildException code", BootSmoke.Has(lines, $"BOOT 6 sim FAIL SimBuildException (code {SimBuildErrorCode.ReservedPhase})"), true);
        }

        if (name == "c-content")
        {
            Expect(ctx, $"{name}: no ContentSet placed in BootContext", _boot.Content is null, true);
        }
    }

    // One frame later: the single sink was called once and shows the screen for the failing step's kind.
    private void CheckScreen(SmokeContext ctx, string name)
    {
        BootErrorScreen content = BootSmoke.ContentScreen(ctx.Loop)!;
        bool isContent = name == "c-content";
        BootErrorScreen shown = isContent ? content : _screen;
        Expect(ctx, $"{name}: fault sink (BootErrorScreen) calls", _screen.FaultCount - _faultsBefore, 1);
        Expect(ctx, $"{name}: ContentError screen calls (BootErrorScreen is the only sink)", content.FaultCount, 0);
        Expect(ctx, $"{name}: Main/Screens/BootError visible", _screen.Visible, !isContent);
        Expect(ctx, $"{name}: Main/Screens/ContentError visible", content.Visible, isContent);
        Expect(ctx, $"{name}: title", shown.TitleText, isContent ? BootErrorScreen.ContentFailedTitle : BootErrorScreen.BootFailedTitle);
        string message = shown.MessageText;
        Type type = ExpectedType(name);
        string expected = name switch
        {
            "a-contributor" => ThrowingBootContributor.Message,
            "b-sim-build" => BootSmoke.SimBuildMessage,
            _ => ThrowingContentSource.Message,
        };
        Expect(ctx, $"{name}: label shows the exception type {type.Name}", message.StartsWith(type.Name, StringComparison.Ordinal), true);
        Expect(ctx, $"{name}: label shows the exception message", message.Contains(expected, StringComparison.Ordinal), true);
        if (name == "b-sim-build")
        {
            Expect(ctx, $"{name}: label shows the SimBuildException code name", message.Contains($"code {SimBuildErrorCode.ReservedPhase}", StringComparison.Ordinal), true);
        }

        Expect(ctx, $"{name}: detail names the failing step", shown.DetailText.StartsWith($"Boot step {_boot.Failure?.Step} ", StringComparison.Ordinal), true);
        Expect(ctx, $"{name}: Quit visible", shown.QuitVisible, true);
        ctx.Pass($"{name}: screen text: {shown.TitleText} | {shown.DetailText} | {message}");
    }

    private SmokeStatus PollFrameFault(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        if (!loop.IsFaulted)
        {
            if (_frames >= 10)
            {
                ctx.Fail("d-frame-fault: loop not faulted after 10 frames");
                return NextPass(ctx);
            }

            return SmokeStatus.Running;
        }

        // The frame fault is delivered synchronously on the main thread; the screen is checked on the next poll.
        if (_screen.FaultCount - _faultsBefore == 0 && _frames < 10)
        {
            return SmokeStatus.Running;
        }

        Expect(ctx, "d-frame-fault: fault sink calls", _screen.FaultCount - _faultsBefore, 1);
        Expect(ctx, "d-frame-fault: BootError visible after a frame fault", _screen.Visible, true);
        Expect(ctx, "d-frame-fault: ContentError hidden", BootSmoke.ContentScreen(loop)!.Visible, false);
        Expect(ctx, "d-frame-fault: session-fault title", _screen.TitleText, BootErrorScreen.SessionFaultTitle);
        Expect(ctx, "d-frame-fault: label shows the frame exception", _screen.MessageText.StartsWith(nameof(InjectedFaultException), StringComparison.Ordinal), true);
        Expect(ctx, "d-frame-fault: run-frame calls up to the fault", _rig.Run.Calls, 2);
        return NextPass(ctx);
    }

    private static Type ExpectedType(string name) => name switch
    {
        "a-contributor" => typeof(InjectedFaultException),
        "b-sim-build" => typeof(SimBuildException),
        _ => typeof(InvalidOperationException),
    };
}
