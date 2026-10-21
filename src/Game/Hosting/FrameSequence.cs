using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Game.Hosting;

/// <summary>
/// One frame of the host shell, in the fixed order of ADR-0002 section 3, ADR-0012 section 6 and ADR-0016 section 6:
/// (1) the pre-frame hooks in registration order; (2) <see cref="IFrameRun.RunFrame"/> once, then stop if the world is
/// faulted; (3) the render-clock sample; (4) <see cref="IPresentationRoot.Present"/> with the driver's
/// <c>AlphaPermille</c>; (5) the post-present hooks in registration order. Plain C# with no Godot types, so the smoke
/// scenarios drive it with doubles. Exceptions propagate: <c>GameLoop</c> turns them into a fault. Hooks are registered
/// explicitly before the first frame; <see cref="Run"/> allocates 0 B. Main thread only, not thread-safe.
/// </summary>
/// <example>
/// <code>
/// var seq = new FrameSequence(clock, driver, run, renderClock, presentationRoot);
/// seq.RegisterPreFrameHook(dispatchImmediates);
/// bool ok = seq.Run();   // false: the world faulted after RunFrame
/// </code>
/// </example>
public sealed class FrameSequence
{
    private readonly IFrameRun _run;
    private readonly IRenderClockSampler _renderClock;
    private readonly IPresentationRoot _presentation;
    private IPreFrameHook[] _preHooks = [];
    private IPostPresentHook[] _postHooks = [];

    /// <summary>Creates a sequence with no hooks.</summary>
    /// <param name="clock">The host's monotonic clock (used by <c>GameLoop</c> for <c>Start</c>/<c>Resume</c>).</param>
    /// <param name="driver">The tick driver the run advances; its <c>AlphaPermille</c> is presented.</param>
    /// <param name="run">The frame pump.</param>
    /// <param name="renderClock">The render-clock sampler.</param>
    /// <param name="presentation">The presentation root.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public FrameSequence(
        IMonotonicClock clock,
        TickDriver driver,
        IFrameRun run,
        IRenderClockSampler renderClock,
        IPresentationRoot presentation)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(renderClock);
        ArgumentNullException.ThrowIfNull(presentation);
        Clock = clock;
        Driver = driver;
        _run = run;
        _renderClock = renderClock;
        _presentation = presentation;
    }

    /// <summary>The host's monotonic clock.</summary>
    public IMonotonicClock Clock { get; }

    /// <summary>The tick driver (the only pause mechanism).</summary>
    public TickDriver Driver { get; }

    /// <summary>True once <see cref="Run"/> has been called; registration is closed from then on.</summary>
    public bool HasRun { get; private set; }

    /// <summary>True once a <see cref="Run"/> saw the world faulted after <c>RunFrame</c>.</summary>
    public bool IsWorldFaulted { get; private set; }

    /// <summary>Number of pre-frame hooks registered.</summary>
    public int PreFrameHookCount => _preHooks.Length;

    /// <summary>Number of post-present hooks registered.</summary>
    public int PostPresentHookCount => _postHooks.Length;

    /// <summary>Appends a pre-frame hook; hooks run in registration order.</summary>
    /// <param name="hook">The hook.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hook"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The first frame has already run.</exception>
    /// <example><code>seq.RegisterPreFrameHook(flushToLatch);</code></example>
    public void RegisterPreFrameHook(IPreFrameHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        ThrowIfRunning();
        int n = _preHooks.Length;
        Array.Resize(ref _preHooks, n + 1);
        _preHooks[n] = hook;
    }

    /// <summary>Appends a post-present hook; hooks run in registration order.</summary>
    /// <param name="hook">The hook.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hook"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The first frame has already run.</exception>
    /// <example><code>seq.RegisterPostPresentHook(promptLatchUpdate);</code></example>
    public void RegisterPostPresentHook(IPostPresentHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        ThrowIfRunning();
        int n = _postHooks.Length;
        Array.Resize(ref _postHooks, n + 1);
        _postHooks[n] = hook;
    }

    /// <summary>
    /// Runs one frame (the five steps above). Returns false, without sampling or presenting, when the world is faulted
    /// after <c>RunFrame</c>; the caller must not call it again. Exceptions from any step propagate unchanged.
    /// </summary>
    /// <returns>True when the frame completed; false when the world faulted.</returns>
    /// <example><code>if (!seq.Run()) { Fault("world Faulted"); }</code></example>
    public bool Run()
    {
        HasRun = true;

        IPreFrameHook[] pre = _preHooks;
        for (int i = 0; i < pre.Length; i++)
        {
            pre[i].OnPreFrame();
        }

        _run.RunFrame();
        if (_run.IsFaulted)
        {
            IsWorldFaulted = true;
            return false;
        }

        _renderClock.Sample();
        _presentation.Present(Driver.AlphaPermille);

        IPostPresentHook[] post = _postHooks;
        for (int i = 0; i < post.Length; i++)
        {
            post[i].OnPostPresent();
        }

        return true;
    }

    private void ThrowIfRunning()
    {
        if (HasRun)
        {
            throw new InvalidOperationException("Frame hooks must be registered before the first frame runs.");
        }
    }
}
