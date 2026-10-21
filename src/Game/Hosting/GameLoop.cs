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
/// again. Boot (story 004) is <see cref="RequestBoot"/>: once per loop it runs the nine ADR-0019 section 3 steps of
/// <see cref="BuildBootPlan"/> after handing faults to the <c>Main/Screens</c> error panels, and a step that throws ends
/// the session through <see cref="FaultBoot"/> exactly like a frame fault. Story 005 installs the real sequence.
/// Deliberately no static instance, no lookups beyond its own direct children, no scene changes.
/// Main thread only.
/// </summary>
/// <example>
/// <code>
/// if (!loop.RequestBoot()) { /* the boot or content error screen is showing */ }
/// // Story 005, once the world is loaded:
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
    private readonly BootRecord _boot = new();
    private IReadOnlyList<IBootStep>? _smokeBootSteps;

    /// <summary>
    /// Test seam: when false, <see cref="_Ready"/> does not call <see cref="RequestBoot"/>.
    /// Tests instantiate the scene with this off to inspect the un-booted tree.
    /// </summary>
    [Export]
    public bool AutoBoot { get; set; } = true;

    /// <summary>The <c>Main/Screens/BootError</c> panel (wired in <c>Main.tscn</c>); boot makes it the fault sink before step 1.</summary>
    [Export]
    public BootErrorScreen? BootErrorPanel { get; set; }

    /// <summary>The <c>Main/Screens/ContentError</c> panel (wired in <c>Main.tscn</c>); the sink shows it when step 2 fails.</summary>
    [Export]
    public BootErrorScreen? ContentErrorPanel { get; set; }

    /// <summary>Calls of <see cref="RequestBoot"/> so far; only the first one boots.</summary>
    public int BootAttempts => _boot.Attempts;

    /// <summary>The boot outcome: null until <see cref="RequestBoot"/> runs, then true (all steps ok or NOT WIRED) or false.</summary>
    public bool? BootResult => _boot.Result;

    /// <summary>The <c>BOOT</c> lines of this loop's own boot (empty before it); a smoke-run sequence keeps its own context's log.</summary>
    public IReadOnlyList<string> BootLines => _boot.Log?.Lines ?? [];

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

    /// <summary>Receives the fault message (default: log only; boot hands it to <see cref="BootErrorPanel"/>).</summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public IFaultSink FaultSink
    {
        get => _faultSink;
        set => _faultSink = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Networking and Session's peer presence, read through <see cref="SoloPausePolicy"/> by
    /// <see cref="TryPause"/>. Null until provided (the solo boot sets the M-1a loopback presence in
    /// <see cref="RequestBoot"/> unless one was provided first); while null every pause is refused (fail closed).
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
    /// scenario sets it on an extra <c>Main.tscn</c> instance before adding it to the tree, so that instance starts no
    /// second runner.
    /// </summary>
    internal bool SmokeArgsIgnored { get; set; }

    /// <summary>
    /// Boots the client (ADR-0019 section 3) on the main thread; <see cref="_Ready"/> calls it when <see cref="AutoBoot"/> is
    /// set and no <c>--smoke=</c> is honoured, the smoke runner calls it for scenarios that need boot. Only the first call
    /// boots: it provides the M-1a loopback peer presence unless one was set earlier (TD-120), puts the error panels in
    /// charge of faults, then runs <see cref="BuildBootPlan"/> (or the steps installed with
    /// <see cref="InstallBootStepsForSmoke"/>) through <see cref="BootSequence"/>. Every later call logs
    /// <c>[GameLoop] boot already ran</c> and returns the first call's outcome.
    /// </summary>
    /// <returns>True when every step was ok or NOT WIRED; false when boot failed and an error screen is up.</returns>
    public bool RequestBoot()
    {
        _boot.Attempts++;
        if (_boot.Attempts > 1)
        {
            Info("[GameLoop] boot already ran");
            return _boot.Result == true;
        }

        // Counts as failed until the sequence says otherwise (a re-entrant call during boot sees false).
        _boot.Result = false;
        _peerPresence ??= LoopbackPeerPresence.Instance;
        InstallErrorPanels();
        BootContext ctx = NewBootContext();
        _boot.Log = ctx.Log;
        BootSequence sequence;
        try
        {
            sequence = new BootSequence(_smokeBootSteps ?? BuildBootPlan());
        }
        catch (Exception ex)
        {
            BootSequence.FailBeforeFirstStep(ctx, ex);
            return false;
        }

        _boot.Result = sequence.Run(ctx);
        return _boot.Result.Value;
    }

    /// <summary>The production plan: <see cref="BootPlan.Create"/> over this loop's window and <see cref="ArmStart"/>.</summary>
    /// <param name="content">Step 2's source; null for <see cref="BuiltInContentSource"/> (M-1a).</param>
    /// <param name="contributors">Steps 3 and 4's contributors; null for <see cref="BootPlan.CreateContributors"/>.</param>
    /// <returns>The nine steps.</returns>
    public IReadOnlyList<IBootStep> BuildBootPlan(
        IContentBootSource? content = null,
        IReadOnlyList<IBootContributor>? contributors = null) =>
        BootPlan.Create(
            new GodotBootWindow(GetWindow()),
            OS.IsDebugBuild(),
            content ?? new BuiltInContentSource(),
            contributors ?? BootPlan.CreateContributors(),
            ArmStart);

    /// <summary>
    /// Ends the session because boot failed; <see cref="BootSequence"/> calls it once, after it has told the error display
    /// which step failed. It is the frame-fault path: the fault latch is set (<see cref="IsFaulted"/>, no pending start),
    /// one <c>[GameLoop] FAULT</c> line with the exception is logged, <see cref="FaultSink"/> receives
    /// <see cref="BootSequence.Summarize"/> of it once, and <c>RunFrame</c> is never called afterwards. On a loop that has
    /// already faulted it only logs the exception and drops the failure announced to <see cref="BootErrorPanel"/>: the first
    /// fault keeps the screen.
    /// </summary>
    /// <param name="ex">The exception that failed boot.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ex"/> is null.</exception>
    public void FaultBoot(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        if (_faulted)
        {
            // The session already ended: the first fault keeps the screen; this one is only logged, and the step it
            // announced is dropped so that a later fault is not shown as that step.
            BootErrorPanel?.DropBootFailure();
            Error($"[GameLoop] boot fault after the loop had already faulted (not delivered): {BootSequence.Summarize(ex)}");
            return;
        }

        Fault(ex);
    }

    /// <summary>
    /// Smoke seam (debug builds only): does what <see cref="RequestBoot"/> does before its plan runs (the error panels take
    /// over <see cref="FaultSink"/>) and returns a fresh boot context, so a scenario can run its own
    /// <see cref="BootSequence"/>. Not a boot attempt.
    /// </summary>
    /// <returns>The context, logging with <c>GD.Print</c>.</returns>
    /// <exception cref="InvalidOperationException">Not a debug build.</exception>
    public BootContext BeginBootForSmoke()
    {
        RequireDebugBuild(nameof(BeginBootForSmoke));
        InstallErrorPanels();
        return NewBootContext();
    }

    /// <summary>
    /// Smoke seam (debug builds only): <see cref="RequestBoot"/> will run <paramref name="steps"/> instead of
    /// <see cref="BuildBootPlan"/> (a scenario's pre-boot hook installs a plan with an injected failure).
    /// </summary>
    /// <param name="steps">The prebuilt plan; it is validated when boot runs, like the production plan.</param>
    /// <exception cref="InvalidOperationException">Not a debug build.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="steps"/> is null.</exception>
    public void InstallBootStepsForSmoke(IReadOnlyList<IBootStep> steps)
    {
        RequireDebugBuild(nameof(InstallBootStepsForSmoke));
        ArgumentNullException.ThrowIfNull(steps);
        _smokeBootSteps = steps;
    }

    /// <summary>
    /// Boot step 9 (<see cref="BootPlan.StartStep"/>): the tick driver is started with the clock's timestamp on the next
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
    private void Fault(Exception ex) => Fault($"[GameLoop] FAULT {ex}", BootSequence.Summarize(ex));

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

    private static void RequireDebugBuild(string member)
    {
        if (!OS.IsDebugBuild())
        {
            throw new InvalidOperationException($"GameLoop.{member} is a smoke seam; release builds do not offer it.");
        }
    }

    private void InstallErrorPanels()
    {
        if (BootErrorPanel is null)
        {
            Error("[GameLoop] BootErrorPanel is not wired (Main.tscn Screens/BootError); boot and frame faults are only logged");
            return;
        }

        BootErrorPanel.RouteContentFailuresTo(ContentErrorPanel);
        FaultSink = BootErrorPanel;
    }

    private BootContext NewBootContext() =>
        new(new BootLog(GD.Print), _bootThreadId, FaultBoot, _faultSink as IBootFailureDisplay);

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

    // One loop boots once: how often it was asked, what came of it and the lines it wrote.
    private sealed class BootRecord
    {
        public int Attempts { get; set; }

        public bool? Result { get; set; }

        public BootLog? Log { get; set; }
    }
}
