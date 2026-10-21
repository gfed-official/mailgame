using PerformativeMail.Sim.Content;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// Everything one boot run passes between its steps, by reference (TR-sim-013: no static holder, no locator). Steps put
/// their products here: step 2 the process's one <see cref="ContentSet"/> (ADR-0010 section 4); stories 005-009 add the
/// session, Sim host, world query and presenter deps. Also carries the boot thread (ADR-0003 section 3: <c>Build</c> and
/// <c>Freeze</c> run on the thread that will tick), the boot-fault entry and the error display. Plain C#, main thread only.
/// </summary>
/// <example>
/// <code>
/// var ctx = new BootContext(new BootLog(GD.Print), System.Environment.CurrentManagedThreadId, loop.FaultBoot, screen);
/// bool ok = new BootSequence(plan).Run(ctx);
/// ContentSet? content = ctx.Content;
/// </code>
/// </example>
public sealed class BootContext
{
    /// <summary>Creates a context.</summary>
    /// <param name="log">The boot log.</param>
    /// <param name="bootThreadId">The managed thread every step must run on (<c>GameLoop.BootThreadId</c>).</param>
    /// <param name="faultBoot">The boot-fault entry (<c>GameLoop.FaultBoot</c>): latches the loop and delivers the fault.</param>
    /// <param name="display">The error display told which step failed before the fault is delivered; null for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="log"/> or <paramref name="faultBoot"/> is null.</exception>
    public BootContext(BootLog log, int bootThreadId, Action<Exception> faultBoot, IBootFailureDisplay? display)
    {
        Log = log ?? throw new ArgumentNullException(nameof(log));
        FaultBoot = faultBoot ?? throw new ArgumentNullException(nameof(faultBoot));
        BootThreadId = bootThreadId;
        Display = display;
    }

    /// <summary>The boot log.</summary>
    public BootLog Log { get; }

    /// <summary>The managed thread id boot runs on; the Sim will be built, frozen and ticked on it (TR-sim-007).</summary>
    public int BootThreadId { get; }

    /// <summary>The boot-fault entry the sequence calls once when a step throws.</summary>
    public Action<Exception> FaultBoot { get; }

    /// <summary>The error display prepared before the fault is delivered; null for none.</summary>
    public IBootFailureDisplay? Display { get; }

    /// <summary>The process's one content set, placed by step 2; null before it ran or when it failed.</summary>
    public ContentSet? Content { get; set; }

    /// <summary>The <c>GetWindow().MinSize</c> value read back by step 1 (width, height); null before it ran.</summary>
    public (int Width, int Height)? MinSizeReadBack { get; set; }

    /// <summary>The failing step of this run; null while every step so far was ok or NOT WIRED.</summary>
    public BootFailure? Failure { get; internal set; }
}
