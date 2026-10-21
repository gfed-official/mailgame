using Godot;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Screens;
using PerformativeMail.Sim.Core;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story godot-host-shell/004 AC3 and AC5 (ADR-0019 sections 1 and 3, ADR-0003 Risks): a failing boot step shows the
/// <c>Main/Screens</c> error screen and stops. Each pass installs a fresh <see cref="FrameLoopRig"/> sequence with
/// <c>InstallForSmoke</c> (which clears the fault latch; armed for a-c, unarmed for d), runs <see cref="BootSequence"/>
/// itself on a context from <see cref="GameLoop.BeginBootForSmoke"/>, asserts the latch at once (<c>IsFaulted</c>, <c>StartPending == false</c>,
/// one <c>[GameLoop] FAULT</c> line, later steps not run) and, one frame later (the sink may be delivered deferred), that
/// <c>BootErrorScreen</c> — the single fault sink — was called once and shows the right screen with the exception type,
/// message and (b) code name, Quit visible, and <c>RunFrame</c> never called. Passes: (a) a throwing step-4 contributor;
/// (b) step 6 throwing <c>SimBuildException(ReservedPhase)</c>; (c) a throwing content source (<c>ContentError</c> shows,
/// <c>BootError</c> does not, no <c>BOOT 3</c> line); (d) a clean boot followed by a frame fault shows the same
/// <c>BootError</c> screen with the session-fault title, after a boot fault suppressed on the still-faulted loop left no
/// step behind; (e) the production entry on a second <c>Main.tscn</c> instance
/// (<c>AutoBoot = false</c>): the debug-only <see cref="GameLoop.InstallBootStepsForSmoke"/> gives it a plan the sequence
/// rejects (numbers out of order), so <c>RequestBoot()</c> returns false through <c>FaultBoot</c>, the instance's
/// <c>BootError</c> shows step 0 (plan), and a second call is refused with the same false.
/// </summary>
[SmokeScenario("boot-failure")]
public sealed class BootFailureSmoke : SmokeScenario
{
    private const int FramesAfterFault = 3;

    private static readonly string[] _passes = ["a-contributor", "b-sim-build", "c-content", "d-frame-fault", "e-request-boot"];

    private readonly LogRecorder _log = new();
    private FrameLoopRig _rig = null!;
    private BootContext _boot = null!;
    private BootErrorScreen _screen = null!;
    private int _pass = -1;
    private int _faultsBefore;
    private int _frames;
    private GameLoop? _second;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        BootErrorScreen? screen = ctx.Loop.BootErrorPanel;
        if (screen is null || ctx.Loop.ContentErrorPanel is null)
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

        if (name == "e-request-boot")
        {
            return PollRequestBoot(ctx);
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
        Expect(ctx, $"{name}: fault sink calls after {FramesAfterFault} frames", _screen.FaultsReceived - _faultsBefore, 1);
        Expect(ctx, $"{name}: loop still faulted", ctx.Loop.IsFaulted, true);
        return NextPass(ctx);
    }

    private SmokeStatus NextPass(SmokeContext ctx)
    {
        _pass++;
        _frames = 0;
        _log.Clear();
        if (_pass >= _passes.Length)
        {
            ctx.Loop.LogObserver = null;
            return SmokeStatus.Done;
        }

        string name = _passes[_pass];
        if (name == "e-request-boot")
        {
            StartRequestBoot(ctx);
            return SmokeStatus.Running;
        }

        GameLoop loop = ctx.Loop;
        if (name == "d-frame-fault")
        {
            DropSuppressedBootFault(ctx);
        }

        // Passes a-c start armed so that the latch must clear StartPending; d starts unarmed so that boot step 9 must arm it.
        bool armed = name != "d-frame-fault";
        _rig = new FrameLoopRig(preHooks: 1, postHooks: 1);
        loop.InstallForSmoke(_rig.Sequence, armStart: armed);
        Expect(ctx, $"{name}: fresh install clears the fault latch", loop.IsFaulted, false);
        Expect(ctx, $"{name}: StartPending before boot", loop.StartPending, armed);
        loop.LogObserver = _log.Observer;
        _boot = loop.BeginBootForSmoke();
        Expect(ctx, $"{name}: FaultSink is the BootError screen before step 1", ReferenceEquals(loop.FaultSink, _screen), true);
        _faultsBefore = _screen.FaultsReceived;
        RunPass(ctx, name);
        return SmokeStatus.Running;
    }

    private void RunPass(SmokeContext ctx, string name)
    {
        GameLoop loop = ctx.Loop;
        IReadOnlyList<IBootStep> plan = name switch
        {
            "a-contributor" => loop.BuildBootPlan(contributors: [new FaultingContributor(BootPlan.InputStep)]),
            "b-sim-build" => BootSmokeFaults.WithFailingSimStep(loop),
            "c-content" => loop.BuildBootPlan(content: new FaultingContentSource()),
            _ => loop.BuildBootPlan(),
        };
        bool frameFault = name == "d-frame-fault";
        if (frameFault)
        {
            _rig.Run.ThrowOnCall = 2;
        }

        bool ok = new BootSequence(plan).Run(_boot);
        Expect(ctx, $"{name}: BootSequence.Run returns", ok, frameFault);
        if (frameFault)
        {
            Expect(ctx, $"{name}: StartPending after the clean boot", loop.StartPending, true);
        }
        else
        {
            CheckLatch(ctx, name);
        }
    }

    // Before (d), on the loop still faulted by (c): a boot failure whose fault is suppressed (the first fault keeps the
    // screen) must not leave its announcement behind, or (d)'s frame fault would be shown as that boot step.
    private void DropSuppressedBootFault(SmokeContext ctx)
    {
        GameLoop loop = ctx.Loop;
        int faultsBefore = _screen.FaultsReceived;
        BootSequence.FailBeforeFirstStep(loop.BeginBootForSmoke(), new InjectedFaultException("suppressed boot fault"));
        Expect(ctx, "d-frame-fault: a boot fault on a faulted loop is not delivered", _screen.FaultsReceived - faultsBefore, 0);
        Expect(ctx, "d-frame-fault: suppressed boot fault logged", _log.Count("[GameLoop] boot fault after the loop had already faulted"), 1);
        _log.Clear();
    }

    // (e): the production RequestBoot -> FaultBoot path, on a second Main.tscn instance (one boot per GameLoop).
    private void StartRequestBoot(SmokeContext ctx)
    {
        GameLoop second = GD.Load<PackedScene>("res://src/Game/Main.tscn").Instantiate<GameLoop>();
        second.Name = "MainRequestBootFailure";
        second.AutoBoot = false;
        second.SmokeArgsIgnored = true;
        ctx.Tree.Root.AddChild(second);
        _second = second;
        _faultsBefore = _screen.FaultsReceived;
        second.LogObserver = _log.Observer;
        // A plan the sequence rejects (step numbers out of order): RequestBoot must report it like a failing step.
        second.InstallBootStepsForSmoke([new FaultingStep(2, "two", new InjectedFaultException("never runs")), new FaultingStep(1, "one", new InjectedFaultException("never runs"))]);
        Expect(ctx, "e-request-boot: RequestBoot with a rejected plan returns", second.RequestBoot(), false);
        Expect(ctx, "e-request-boot: IsFaulted", second.IsFaulted, true);
        Expect(ctx, "e-request-boot: StartPending", second.StartPending, false);
        Expect(ctx, "e-request-boot: one [GameLoop] FAULT line", _log.Count("[GameLoop] FAULT "), 1);
        Expect(ctx, "e-request-boot: BOOT 0 plan FAIL logged", BootSmokeFaults.Logged(second.BootLines, $"BOOT 0 plan FAIL {nameof(ArgumentException)}: "), true);
        Expect(ctx, "e-request-boot: status lines (a log parser sees the plan FAIL)", string.Join(";", BootSmokeFaults.StatusLines(second.BootLines).Select(s => $"{s.Number} {s.Name} {s.Status}")), "0 plan FAIL");
        Expect(ctx, "e-request-boot: second RequestBoot after the failed boot returns the first result", second.RequestBoot(), false);
        Expect(ctx, "e-request-boot: second call refused ('[GameLoop] boot already ran')", _log.Count("[GameLoop] boot already ran"), 1);
        Expect(ctx, "e-request-boot: RequestBoot call count", second.BootAttempts, 2);
    }

    private SmokeStatus PollRequestBoot(SmokeContext ctx)
    {
        GameLoop second = _second!;
        BootErrorScreen? screen = second.BootErrorPanel;
        Expect(ctx, "e-request-boot: instance BootError visible one frame later", screen?.Visible ?? false, true);
        Expect(ctx, "e-request-boot: instance fault sink calls", screen?.FaultsReceived ?? -1, 1);
        Expect(ctx, "e-request-boot: title", screen?.HeadingText ?? string.Empty, BootErrorScreen.BootHeading);
        Expect(ctx, "e-request-boot: detail names the plan (step 0)", screen?.WhereText ?? string.Empty, "Failed at boot step 0 (plan).");
        Expect(ctx, "e-request-boot: label shows the plan rejection (type and message)", (screen?.MessageText ?? string.Empty).StartsWith($"{nameof(ArgumentException)}: Boot step 1 (one) does not come after step 2", StringComparison.Ordinal), true);
        Expect(ctx, "e-request-boot: sink state", screen?.Showing ?? BootErrorScreen.Shown.Nothing, BootErrorScreen.Shown.BootStep);
        Expect(ctx, "e-request-boot: Quit visible and focused on the production RequestBoot path", (screen?.QuitShown ?? false) && (screen?.QuitFocused ?? false), true);
        Expect(ctx, "e-request-boot: the smoke host's own screen not called (the instance has its own)", _screen.FaultsReceived - _faultsBefore, 0);
        second.LogObserver = null;
        ctx.Tree.Root.RemoveChild(second);
        second.QueueFree();
        _second = null;
        return NextPass(ctx);
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
        Expect(ctx, $"{name}: BOOT {step} {stepName} FAIL line", BootSmokeFaults.Logged(lines, $"BOOT {step} {stepName} FAIL {ExpectedType(name).Name}"), true);
        Expect(ctx, $"{name}: no line for a later step ({next.Trim()})", BootSmokeFaults.Logged(lines, next), false);
        Expect(ctx, $"{name}: steps before {step} ran (ok or NOT WIRED)", BootSmokeFaults.StatusLines(lines).Count(s => s.Status is "ok" or "NOT WIRED"), step - 1);
        if (name == "b-sim-build")
        {
            Expect(ctx, $"{name}: FAIL line names the SimBuildException code", BootSmokeFaults.Logged(lines, $"BOOT 6 sim FAIL SimBuildException (code {SimBuildErrorCode.ReservedPhase})"), true);
        }

        if (name == "c-content")
        {
            Expect(ctx, $"{name}: no ContentSet placed in BootContext", _boot.Content is null, true);
        }
    }

    // One frame later: the single sink was called once and shows the screen for the failing step's kind.
    private void CheckScreen(SmokeContext ctx, string name)
    {
        BootErrorScreen content = ctx.Loop.ContentErrorPanel!;
        bool isContent = name == "c-content";
        BootErrorScreen shown = isContent ? content : _screen;
        Expect(ctx, $"{name}: fault sink (BootErrorScreen) calls", _screen.FaultsReceived - _faultsBefore, 1);
        Expect(ctx, $"{name}: ContentError screen calls (BootErrorScreen is the only sink)", content.FaultsReceived, 0);
        Expect(ctx, $"{name}: Main/Screens/BootError visible", _screen.Visible, !isContent);
        Expect(ctx, $"{name}: Main/Screens/ContentError visible", content.Visible, isContent);
        Expect(ctx, $"{name}: title", shown.HeadingText, isContent ? BootErrorScreen.ContentHeading : BootErrorScreen.BootHeading);
        Expect(ctx, $"{name}: sink state", _screen.Showing, isContent ? BootErrorScreen.Shown.ContentStep : BootErrorScreen.Shown.BootStep);
        string message = shown.MessageText;
        Type type = ExpectedType(name);
        string expected = name switch
        {
            "a-contributor" => FaultingContributor.Message,
            "b-sim-build" => BootSmokeFaults.ReservedPhaseMessage,
            _ => FaultingContentSource.Message,
        };
        Expect(ctx, $"{name}: label shows the exception type {type.Name}", message.StartsWith(type.Name, StringComparison.Ordinal), true);
        Expect(ctx, $"{name}: label shows the exception message", message.Contains(expected, StringComparison.Ordinal), true);
        if (name == "b-sim-build")
        {
            Expect(ctx, $"{name}: label shows the SimBuildException code name", message.Contains($"code {SimBuildErrorCode.ReservedPhase}", StringComparison.Ordinal), true);
        }

        Expect(ctx, $"{name}: detail names the failing step", shown.WhereText.StartsWith($"Failed at boot step {_boot.Failure?.Step} ", StringComparison.Ordinal), true);
        Expect(ctx, $"{name}: Quit visible", shown.QuitShown, true);
        ctx.Pass($"{name}: screen text: {shown.HeadingText} | {shown.WhereText} | {message}");
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
        if (_screen.FaultsReceived - _faultsBefore == 0 && _frames < 10)
        {
            return SmokeStatus.Running;
        }

        Expect(ctx, "d-frame-fault: fault sink calls", _screen.FaultsReceived - _faultsBefore, 1);
        Expect(ctx, "d-frame-fault: BootError visible after a frame fault", _screen.Visible, true);
        Expect(ctx, "d-frame-fault: ContentError hidden", loop.ContentErrorPanel!.Visible, false);
        Expect(ctx, "d-frame-fault: session-fault title", _screen.HeadingText, BootErrorScreen.SessionHeading);
        Expect(ctx, "d-frame-fault: sink state", _screen.Showing, BootErrorScreen.Shown.SessionFault);
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
