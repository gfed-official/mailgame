using PerformativeMail.Client.Core.Presentation;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The state one boot run hands from step to step, always by reference (TR-sim-013: no static holder, no locator).
/// Step 2 stores the process's single <see cref="ContentSet"/> here (ADR-0010 section 4); step 5 the <see cref="Session"/> (story
/// 005); step 6's contributors fill <see cref="Published"/> and step 7 hands it, with the world query, to <see cref="Presenters"/> (story 006). It also fixes the boot thread (ADR-0003 section 3: the Sim is
/// built and frozen on the thread that will tick it), the boot-fault entry and the error display. Main thread only.
/// </summary>
/// <example>
/// <code>
/// BootContext boot = loop.BeginBootForSmoke();                 // or GameLoop's own context inside RequestBoot
/// if (new BootSequence(steps).Run(boot) &amp;&amp; boot.Content is { } set) { /* step 2 produced the content set */ }
/// </code>
/// </example>
/// <param name="log">Receives every <c>BOOT</c> line.</param>
/// <param name="bootThreadId">The managed thread every step must run on (<c>GameLoop.BootThreadId</c>).</param>
/// <param name="faultBoot">The boot-fault entry (<c>GameLoop.FaultBoot</c>), called once when boot fails.</param>
/// <param name="display">Told which step failed before the fault is delivered; null when there is no error screen.</param>
public sealed class BootContext(BootLog log, int bootThreadId, Action<Exception> faultBoot, IBootFailureDisplay? display)
{
    /// <summary>Receives every <c>BOOT</c> line.</summary>
    public BootLog Log { get; } = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>The managed thread id of the boot (and of every later tick, TR-sim-007).</summary>
    public int BootThreadId { get; } = bootThreadId;

    /// <summary>The boot-fault entry; <see cref="BootSequence"/> calls it once when boot fails.</summary>
    public Action<Exception> FaultBoot { get; } = faultBoot ?? throw new ArgumentNullException(nameof(faultBoot));

    /// <summary>The error display prepared before the boot-fault entry runs; null when there is none.</summary>
    public IBootFailureDisplay? Display { get; } = display;

    /// <summary>Step 2's product: the one content set of the process. Null until step 2 succeeds.</summary>
    public ContentSet? Content { get; internal set; }

    /// <summary>What step 1 read back from the window's minimum size after setting it (width, height); null before step 1.</summary>
    public (int Width, int Height)? MinSizeReadBack { get; internal set; }

    /// <summary>Inputs of steps 5-6 (clock, seed, profile, user args); <c>GameLoop</c> sets the real ones, smokes their own.</summary>
    public BootOptions Options { get; internal set; } = new();

    /// <summary>Step 5's product: the host session, the owned-carrier step and (after step 6) the attached ports and render clock. Null until step 5 succeeds.</summary>
    public BootSession? Session { get; internal set; }

    /// <summary>The read-only ClientLayer states boot contributors publish for Presentation (story 006); step 7 hands it to the presenters.</summary>
    public PublishedStates Published { get; } = new();

    /// <summary>The presenters step 7 initialises (<c>GameLoop</c> sets its World, Hud and Audio presenters); null when there are none.</summary>
    public IPresentersBoot? Presenters { get; internal set; }

    /// <summary>What step 8 resolves the view catalogs with (<c>GameLoop</c> sets its presenters' boot); null when there is none.</summary>
    public IViewsBoot? Views { get; internal set; }

    /// <summary>Why this run failed (step, name, kind, exception); null while no step has failed.</summary>
    public BootFailure? Failure { get; internal set; }
}
