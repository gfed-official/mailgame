using Godot;
using PerformativeMail.Client.Core.Net;
using PerformativeMail.Game.Smoke;

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
/// again. Boot (story 004) and the real sequence construction (story 005) install the sequence.
/// Deliberately no static instance, no lookups beyond its own direct children, no scene changes.
/// Main thread only.
/// </summary>
/// <example>
/// <code>
/// // Story 004/005 boot, after the world is loaded:
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
    private IPeerPresence _peerPresence = LoopbackPeerPresence.Instance;

    /// <summary>
    /// Test seam: when false, <see cref="_Ready"/> does not call <see cref="RequestBoot"/>.
    /// Tests instantiate the scene with this off to inspect the un-booted tree.
    /// </summary>
    [Export]
    public bool AutoBoot { get; set; } = true;

    /// <summary>True once the loop faulted; the Sim is never run again.</summary>
    public bool IsFaulted => _faulted;

    /// <summary>True while <see cref="ArmStart"/> has been called and the tick driver is not yet started.</summary>
    public bool StartPending => _startPending;

    /// <summary>True while a frame sequence is installed.</summary>
    public bool HasSequence => _sequence is not null;

    /// <summary>Receives the fault message (default: log only; story 004 installs the error screen).</summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public IFaultSink FaultSink
    {
        get => _faultSink;
        set => _faultSink = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Networking and Session's peer presence, read through <see cref="SoloPausePolicy"/> by
    /// <see cref="TryPause"/> (default: the M-1a loopback presence, no remote peers).
    /// </summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public IPeerPresence PeerPresence
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

        string? smokeName = FindSmokeName();
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
    /// Boot seam. Story 004 replaces this body with the real boot sequence.
    /// </summary>
    /// <returns>True when boot succeeded; false when it did not run or failed.</returns>
    public bool RequestBoot()
    {
        GD.Print("[GameLoop] boot not wired (story 004)");
        return false;
    }

    /// <summary>
    /// Boot step 9 (story 004): the tick driver is started with the clock's timestamp on the next
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
    /// <see cref="SoloPausePolicy"/> and, when allowed, pauses the tick driver. A refusal is logged
    /// with its reason and leaves the driver running. Never touches <c>SceneTree.Paused</c>.
    /// </summary>
    /// <param name="reason">Empty when paused; otherwise why the pause did not happen.</param>
    /// <returns>True when the driver was paused (or the request was accepted).</returns>
    public bool TryPause(out string reason)
    {
        if (_sequence is null)
        {
            reason = "no frame sequence installed";
            return false;
        }

        if (!SoloPausePolicy.CanPause(_peerPresence, out reason))
        {
            GD.Print($"[GameLoop] pause refused: {reason}");
            return false;
        }

        _sequence.Driver.Pause();
        return true;
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
            Fault(new InvalidOperationException(
                $"GameLoop._Process ran on managed thread {thread}, not the boot thread {_bootThreadId} (TR-sim-007)"));
            return;
        }

        try
        {
            if (_startPending)
            {
                _startPending = false;
                seq.Driver.Start(seq.Clock.Timestamp);
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

    private void Fault(Exception ex)
    {
        GD.PrintErr($"[GameLoop] FAULT {ex.GetType().FullName}: {ex.Message}");
        GD.PrintErr(ex.StackTrace ?? "(no stack trace)");
        Latch($"{ex.GetType().Name}: {ex.Message}");
    }

    private void Fault(string message)
    {
        GD.PrintErr($"[GameLoop] FAULT {message}");
        Latch(message);
    }

    private void Latch(string message)
    {
        _faulted = true;
        _startPending = false;
        GD.PrintErr("[GameLoop] session ended; RunFrame will not be called again");
        try
        {
            _faultSink.OnFault(message);
        }
        catch (Exception sinkEx)
        {
            GD.PrintErr($"[GameLoop] fault sink threw {sinkEx.GetType().FullName}: {sinkEx.Message}");
        }
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
