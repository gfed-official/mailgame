using Godot;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Game.Boot;
using PerformativeMail.Game.Screens;
using PerformativeMail.Game.Smoke;
using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Game.Hosting;

/// <summary>
/// Root of the one main scene (<c>res://src/Game/Main.tscn</c>, ADR-0019 section 1) and the only
/// node that drives the Sim (ADR-0002 section 3). Story 002 owns the skeleton (process settings,
/// the Debug-node strip, the smoke hand-off, the boot seam); story 003 owns the frame loop: each
/// <see cref="_Process"/> runs the installed <see cref="FrameSequence"/> once (pre-frame hooks,
/// <c>RunFrame</c>, render-clock sample, <c>Present(AlphaPermille)</c>, post-present hooks).
/// The tick driver is started on the first <see cref="_Process"/> after boot arms it
/// (<see cref="ArmStart"/>), pause is a driver state only (<see cref="TryPause"/>, never
/// <c>SceneTree.Paused</c>), and any exception, a faulted world or a wrong-thread frame ends the
/// session: the loop logs it, hands the message to <see cref="FaultSink"/> and never runs the Sim
/// again. Boot (story 004, <see cref="RequestBoot"/>) runs the ADR-0019 section 3 <see cref="BootPlan"/> once, with the
/// <c>Main/Screens</c> error screen installed as <see cref="FaultSink"/> before step 1; a failing step goes through
/// <see cref="FaultBoot"/> (the same latch and sink as a frame fault). The real sequence construction (story 005)
/// installs the sequence.
/// Deliberately no static instance, no lookups beyond its own direct children, no scene changes.
/// Main thread only.
/// </summary>
/// <example>
/// <code>
/// bool booted = loop.RequestBoot();                // _Ready does this when AutoBoot is set and no --smoke is honoured
/// // Story 005 boot, after the world is loaded:
/// loop.RegisterPreFrameHook(dispatchImmediates);   // before the first frame
/// loop.ArmStart();                                 // boot step 9: Start on the next _Process
/// if (!loop.TryPause(out string reason)) { /* refused by Networking and Session */ }
/// loop.Resume();
/// </code>
/// </example>
public partial class GameLoop : Node
{
    private const string SmokeArgPrefix = "--smoke=";

    private FrameSequence? _sequence;
    private int _bootThreadId;
    private bool _startPending;
    private bool _faulted;
    private IFaultSink _faultSink = LogFaultSink.Instance;
    private IPeerPresence? _peerPresence;
    private bool? _bootResult;
    private Func<GameLoop, IReadOnlyList<IBootStep>>? _smokeBootPlan;

    /// <summary>
    /// Test seam: when false, <see cref="_Ready"/> does not call <see cref="RequestBoot"/>.
    /// Tests instantiate the scene with this off to inspect the un-booted tree.
    /// </summary>
    [Export]
    public bool AutoBoot { get; set; } = true;

    /// <summary>
    /// The <c>Main/Screens/BootError</c> screen (set in <c>Main.tscn</c>), installed as <see cref="FaultSink"/> before boot
    /// step 1; null leaves the log-only sink.
    /// </summary>
    [Export]
    public BootErrorScreen? ErrorScreen { get; set; }

    /// <summary>How many times <see cref="RequestBoot"/> was called (the first call boots; later calls are refused).</summary>
    public int BootRequestCount { get; private set; }

    /// <summary>The log of the most recent boot context (<see cref="RequestBoot"/> or <see cref="CreateBootContext"/>); null before any.</summary>
    public BootLog? LastBootLog { get; private set; }

    /// <summary>True once the loop faulted; the Sim is never run again.</summary>
    public bool IsFaulted => _faulted;

    /// <summary>True while <see cref="ArmStart"/> has been called and the tick driver is not yet started.</summary>
    public bool StartPending => _startPending;

    /// <summary>True while a frame sequence is installed.</summary>
    public bool HasSequence => _sequence is not null;

    /// <summary>The managed thread recorded as the boot thread (in <see cref="_Ready"/>); every frame must run on it.</summary>
    public int BootThreadId => _bootThreadId;

    /// <summary>
    /// Optional observer of every <c>[GameLoop]</c> log line (pause refusals, faults), called after the line is printed.
    /// The smoke scenarios use it to assert what was logged; null by default.
    /// </summary>
    public Action<string>? LogObserver { get; set; }

    /// <summary>Receives the fault message (default: log only; boot installs <see cref="ErrorScreen"/> before step 1).</summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public IFaultSink FaultSink
    {
        get => _faultSink;
        set => _faultSink = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Networking and Session's peer presence, read through <see cref="SoloPausePolicy"/> by
    /// <see cref="TryPause"/>. Null until provided (the solo boot sets the M-1a loopback presence in
    /// <see cref="RequestBoot"/> when none is set); while null every pause is refused (fail closed).
    /// </summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public IPeerPresence? PeerPresence
    {
        get => _peerPresence;
        set => _peerPresence = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        // The boot thread: every _Process must run on it (TR-sim-007, ADR-0002 section 5, F9).
        _bootThreadId = System.Environment.CurrentManagedThreadId;

        // Also set in Main.tscn; repeated here so the live values never depend on the scene file.
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue;
        ProcessThreadGroup = ProcessThreadGroupEnum.Inherit;

        bool isDebug = OS.IsDebugBuild();
        if (!isDebug)
        {
            CanvasLayer? debug = GetNodeOrNull<CanvasLayer>("Debug");
            if (debug is not null)
            {
                RemoveChild(debug);
                debug.QueueFree();
            }
        }

        string? smokeName = SmokeArgsIgnored ? null : FindSmokeName();
        if (smokeName is not null)
        {
            if (isDebug)
            {
                // The runner decides whether to boot (scenario NeedsBoot), so skip AutoBoot.
                AddChild(new SmokeRunner { ScenarioName = smokeName });
                return;
            }

            GD.Print("[GameLoop] --smoke is ignored in a non-debug build");
        }

        if (AutoBoot)
        {
            RequestBoot();
        }
    }

    /// <summary>
    /// Smoke seam: when true, <see cref="_Ready"/> ignores <c>--smoke=</c> and takes the production path (AutoBoot). A
    /// scenario sets it on a second instance of <c>Main.tscn</c> before adding it, so that instance does not start a
    /// second runner.
    /// </summary>
    internal bool SmokeArgsIgnored { get; set; }

    /// <summary>
    /// Boot (ADR-0019 section 3), once, on the main thread: defaults <see cref="PeerPresence"/> to the M-1a loopback
    /// presence only when none is set, installs <see cref="ErrorScreen"/> as <see cref="FaultSink"/>, then runs the nine
    /// <see cref="BootPlan"/> steps (or the debug-only smoke plan) through <see cref="BootSequence"/>. A step that throws is
    /// reported through <see cref="FaultBoot"/> and no later step runs. A second call is refused: it logs
    /// <c>[GameLoop] boot already ran</c> and returns the first result.
    /// </summary>
    /// <returns>True when every step was ok or NOT WIRED; false when a step failed.</returns>
    public bool RequestBoot()
    {
        BootRequestCount++;
        if (_bootResult is bool first)
        {
            Info("[GameLoop] boot already ran");
            return first;
        }

        _bootResult = false;

        // M-1a solo session: Networking and Session's loopback presence (no remote peers), unless one was provided.
        _peerPresence ??= LoopbackPeerPresence.Instance;
        BootContext ctx = CreateBootContext();
        BootSequence sequence;
        try
        {
            sequence = new BootSequence(_smokeBootPlan?.Invoke(this) ?? CreateBootPlan());
        }
        catch (Exception ex)
        {
            // Building the plan is part of boot: a throw here shows the boot error screen like a failing step (step 0).
            ctx.Log.Write($"BOOT plan FAIL {BootFaultText.Describe(ex)}");
            ctx.Failure = new BootFailure(0, "plan", BootFailureKind.Boot, ex);
            ctx.Display?.PrepareBootFailure(ctx.Failure);
            FaultBoot(ex);
            return false;
        }

        _bootResult = sequence.Run(ctx);
        return _bootResult.Value;
    }

    /// <summary>
    /// Makes the context of a boot run: installs <see cref="ErrorScreen"/> (when set) as <see cref="FaultSink"/> and error
    /// display, and a fresh <see cref="BootLog"/> echoed with <c>GD.Print</c> (kept as <see cref="LastBootLog"/>), on the
    /// boot thread. <see cref="RequestBoot"/> uses it; smoke scenarios use it to run their own plan.
    /// </summary>
    /// <returns>The context.</returns>
    public BootContext CreateBootContext()
    {
        if (ErrorScreen is not null)
        {
            FaultSink = ErrorScreen;
        }
        else
        {
            Error("[GameLoop] no ErrorScreen is set (Main.tscn wires Screens/BootError); boot and frame faults are only logged");
        }

        var log = new BootLog(GD.Print);
        LastBootLog = log;
        return new BootContext(log, _bootThreadId, FaultBoot, _faultSink as IBootFailureDisplay);
    }

    /// <summary>The nine-step production plan (<see cref="BootPlan.Create"/>) over this loop's window and <see cref="ArmStart"/>.</summary>
    /// <param name="content">The step 2 source; null for <see cref="BuiltInContentSource"/>.</param>
    /// <param name="contributors">The step 3/4 contributors; null for <see cref="BootPlan.CreateContributors"/>.</param>
    /// <returns>The steps.</returns>
    public IReadOnlyList<IBootStep> CreateBootPlan(
        IContentBootSource? content = null,
        IReadOnlyList<IBootContributor>? contributors = null) =>
        BootPlan.Create(
            new GodotBootWindow(GetWindow()),
            OS.IsDebugBuild(),
            content ?? new BuiltInContentSource(),
            contributors ?? BootPlan.CreateContributors(),
            ArmStart);

    /// <summary>
    /// The boot-fault entry: a boot step threw. Same private latch and <see cref="FaultSink"/> delivery as a frame fault:
    /// <see cref="IsFaulted"/> becomes true, <see cref="StartPending"/> false, one <c>[GameLoop] FAULT</c> line (type,
    /// message, stack) is logged and the sink gets <see cref="BootFaultText.Describe"/> once. <c>RunFrame</c> is never
    /// called afterwards. The boot sequence prepares the error display with the failing step first.
    /// </summary>
    /// <param name="ex">What the step threw.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ex"/> is null.</exception>
    public void FaultBoot(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        Fault(ex);
    }

    /// <summary>
    /// Smoke seam (debug builds only): <see cref="RequestBoot"/> runs the plan <paramref name="plan"/> builds instead of
    /// <see cref="CreateBootPlan"/> (a scenario's pre-boot hook injects a failing step).
    /// </summary>
    /// <param name="plan">Builds the steps when boot runs.</param>
    /// <exception cref="InvalidOperationException">Not a debug build.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is null.</exception>
    public void SetBootPlanForSmoke(Func<GameLoop, IReadOnlyList<IBootStep>> plan)
    {
        if (!OS.IsDebugBuild())
        {
            throw new InvalidOperationException("GameLoop.SetBootPlanForSmoke is available in debug builds only.");
        }

        _smokeBootPlan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    /// <summary>
    /// Boot step 9 (<see cref="ArmStartStep"/>): the tick driver is started with the clock's timestamp on the next
    /// <see cref="_Process"/>, not here (F12: the first-frame hitch must not become ticks).
    /// Frames before that run no ticks.
    /// </summary>
    public void ArmStart() => _startPending = true;

    /// <summary>Registers a pre-frame hook on the installed sequence, before the first frame.</summary>
    /// <param name="hook">The hook; runs in registration order.</param>
    /// <exception cref="InvalidOperationException">No sequence is installed, or the first frame has run.</exception>
    public void RegisterPreFrameHook(IPreFrameHook hook) => RequireSequence().RegisterPreFrameHook(hook);

    /// <summary>Registers a post-present hook on the installed sequence, before the first frame.</summary>
    /// <param name="hook">The hook; runs in registration order.</param>
    /// <exception cref="InvalidOperationException">No sequence is installed, or the first frame has run.</exception>
    public void RegisterPostPresentHook(IPostPresentHook hook) => RequireSequence().RegisterPostPresentHook(hook);

    /// <summary>
    /// Solo pause (TR-sim-009, TR-input-014): asks Networking and Session's
    /// <see cref="SoloPausePolicy"/> and, when allowed, pauses the tick driver. The loop itself only
    /// refuses when there is nothing running to pause (no sequence, faulted, or the driver not
    /// Running, e.g. still waiting for its start). Every refusal is logged with its reason and leaves
    /// the driver as it was. Never touches <c>SceneTree.Paused</c>.
    /// </summary>
    /// <param name="reason">Empty when paused; otherwise why the pause did not happen.</param>
    /// <returns>True when the driver is now paused.</returns>
    public bool TryPause(out string reason)
    {
        if (_sequence is null)
        {
            reason = "no frame sequence installed";
        }
        else if (_faulted)
        {
            reason = "the loop has faulted";
        }
        else if (_startPending || _sequence.Driver.State != TickDriverState.Running)
        {
            reason = $"the tick driver is not running (state {_sequence.Driver.State}, start pending {_startPending})";
        }
        else if (SoloPausePolicy.CanPause(_peerPresence, out reason))
        {
            _sequence.Driver.Pause();
            return true;
        }

        Info($"[GameLoop] pause refused: {reason}");
        return false;
    }

    /// <summary>
    /// Resumes a paused tick driver at the clock's current timestamp (no burst for the paused
    /// time). A no-op when nothing is installed or the driver is not paused.
    /// </summary>
    public void Resume()
    {
        if (_sequence is null)
        {
            return;
        }

        _sequence.Driver.Resume(_sequence.Clock.Timestamp);
    }

    /// <summary>
    /// Smoke seam (debug builds only): installs <paramref name="seq"/>, clears the fault latch,
    /// records the calling thread as the boot thread and arms or clears <see cref="StartPending"/>.
    /// Stories 004/005 own the real construction and start.
    /// </summary>
    /// <param name="seq">A sequence built from doubles.</param>
    /// <param name="armStart">True to start the driver on the next <see cref="_Process"/>.</param>
    /// <exception cref="InvalidOperationException">Not a debug build.</exception>
    public void InstallForSmoke(FrameSequence seq, bool armStart)
    {
        if (!OS.IsDebugBuild())
        {
            throw new InvalidOperationException("GameLoop.InstallForSmoke is available in debug builds only.");
        }

        ArgumentNullException.ThrowIfNull(seq);
        _sequence = seq;
        _faulted = false;
        _bootThreadId = System.Environment.CurrentManagedThreadId;
        _startPending = armStart;
    }

    /// <summary>
    /// One frame: start the driver if armed, then run the frame sequence once. Any exception, a
    /// faulted world or a call from a thread other than the boot thread faults the loop.
    /// </summary>
    /// <param name="delta">Never read (spike F11: it under-reports wall time under slow frames);
    /// time comes only from the monotonic clock through the tick driver.</param>
    public override void _Process(double delta)
    {
        // F11: delta is deliberately unused. Do not read it here; tools/ci/check-sim-access.sh fails on it.
        FrameSequence? seq = _sequence;
        if (seq is null || _faulted)
        {
            return;
        }

        int thread = System.Environment.CurrentManagedThreadId;
        if (thread != _bootThreadId)
        {
            WrongThreadFault(thread);
            return;
        }

        try
        {
            if (_startPending)
            {
                // Only an Idle driver is started: a repeated ArmStart must not restart a running or paused one.
                _startPending = false;
                if (seq.Driver.State == TickDriverState.Idle)
                {
                    seq.Driver.Start(seq.Clock.Timestamp);
                }
            }

            if (!seq.Run())
            {
                Fault("world Faulted after RunFrame (non-SIM_ASSERT builds do not rethrow)");
            }
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    private FrameSequence RequireSequence() =>
        _sequence ?? throw new InvalidOperationException("No frame sequence is installed (story 004/005).");

    // ex.ToString() carries the full type name, the message, the stack and any inner exceptions.
    private void Fault(Exception ex) => Fault($"[GameLoop] FAULT {ex}", BootFaultText.Describe(ex));

    private void Fault(string message) => Fault($"[GameLoop] FAULT {message}", message);

    // Latch first: a throwing LogObserver must not leave the loop running. The sink still gets the fault;
    // the observer's exception then propagates.
    private void Fault(string faultLine, string message)
    {
        Latch();
        try
        {
            Error(faultLine);
            LogSessionEnded(message);
        }
        finally
        {
            DeliverFault(message);
        }
    }

    // Runs on the wrong thread: latch at once, but hand the message to the sink on the main thread (story 004's error
    // screen touches nodes). The exception is never thrown, so the caller's stack is logged explicitly.
    private void WrongThreadFault(int thread)
    {
        var ex = new InvalidOperationException(
            $"GameLoop._Process ran on managed thread {thread}, not the boot thread {_bootThreadId} (TR-sim-007)");
        string message = $"{ex.GetType().Name}: {ex.Message}";
        Latch();
        try
        {
            Error($"[GameLoop] FAULT {ex.GetType().FullName}: {ex.Message}\n{System.Environment.StackTrace}");
            LogSessionEnded(message);
        }
        finally
        {
            Callable.From(() => DeliverFault(message)).CallDeferred();
        }
    }

    private void Latch()
    {
        _faulted = true;
        _startPending = false;
    }

    private void LogSessionEnded(string message) =>
        Error($"[GameLoop] session ended ({message}); RunFrame will not be called again");

    private void DeliverFault(string message)
    {
        try
        {
            _faultSink.OnFault(message);
        }
        catch (Exception sinkEx)
        {
            Error($"[GameLoop] fault sink threw {sinkEx}");
        }
    }

    private void Info(string line)
    {
        GD.Print(line);
        LogObserver?.Invoke(line);
    }

    private void Error(string line)
    {
        GD.PrintErr(line);
        LogObserver?.Invoke(line);
    }

    private static string? FindSmokeName()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(SmokeArgPrefix, StringComparison.Ordinal))
            {
                return arg[SmokeArgPrefix.Length..];
            }
        }

        return null;
    }
}
