using PerformativeMail.Client.Core.Net;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Game.Smoke;

// Recording doubles for the story godot-host-shell/003 frame-loop smoke scenarios. None of them
// allocates per call on the happy path (frame-alloc measures them); only an injected throw does.

/// <summary>Phase codes written to <see cref="FrameOrderLog"/>.</summary>
public static class FramePhase
{
    /// <summary>Pre-frame hook <c>n</c> logs <c>PreHook + n</c>.</summary>
    public const int PreHook = 10;

    /// <summary>The run-frame call.</summary>
    public const int RunFrame = 20;

    /// <summary>The render-clock sample.</summary>
    public const int Sample = 30;

    /// <summary>The presentation root.</summary>
    public const int Present = 40;

    /// <summary>Post-present hook <c>n</c> logs <c>PostHook + n</c>.</summary>
    public const int PostHook = 50;
}

/// <summary>A preallocated log of phase codes, shared by every double of one sequence.</summary>
public sealed class FrameOrderLog
{
    private readonly int[] _entries = new int[64];

    /// <summary>Number of entries since the last <see cref="Clear"/>.</summary>
    public int Count { get; private set; }

    /// <summary>True when an entry was dropped because the log was full.</summary>
    public bool Overflowed { get; private set; }

    /// <summary>Appends a phase code.</summary>
    /// <param name="code">The <see cref="FramePhase"/> code.</param>
    public void Add(int code)
    {
        if (Count == _entries.Length)
        {
            Overflowed = true;
            return;
        }

        _entries[Count++] = code;
    }

    /// <summary>Empties the log.</summary>
    public void Clear()
    {
        Count = 0;
        Overflowed = false;
    }

    /// <summary>The logged codes as text (allocates; for assertions only).</summary>
    /// <returns>A comma-separated list.</returns>
    public string Describe() => string.Join(",", _entries[..Count]);

    /// <summary>True when the log equals <paramref name="expected"/> exactly.</summary>
    /// <param name="expected">Expected codes in order.</param>
    /// <returns>Whether the log matches.</returns>
    public bool Matches(int[] expected)
    {
        if (Overflowed || Count != expected.Length)
        {
            return false;
        }

        for (int i = 0; i < Count; i++)
        {
            if (_entries[i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>An <see cref="IMonotonicClock"/> the scenario sets by hand; 10^7 counts per second.</summary>
public sealed class FakeClock : IMonotonicClock
{
    /// <summary>Counts per second.</summary>
    public const long Freq = 10_000_000;

    /// <summary>Counts per tick (1/30 s).</summary>
    public const long TickCounts = Freq / TickRate.Hz;

    /// <inheritdoc />
    public long Timestamp { get; set; } = 5_000_000;

    /// <inheritdoc />
    public long Frequency => Freq;

    /// <summary>Moves the clock forward by <paramref name="counts"/>.</summary>
    /// <param name="counts">Clock counts.</param>
    public void Advance(long counts) => Timestamp += counts;
}

/// <summary>The exception the doubles throw on request.</summary>
public sealed class InjectedFaultException(string message) : Exception(message)
{
}

/// <summary>
/// A recording <see cref="IFrameRun"/>: advances the driver from the clock (the real pump's step 2),
/// counts calls and ticks, and can throw on a given call or report the world faulted after one.
/// </summary>
public sealed class RecordingFrameRun(IMonotonicClock clock, TickDriver driver, FrameOrderLog log) : IFrameRun
{
    /// <summary>Total calls.</summary>
    public int Calls { get; private set; }

    /// <summary>Ticks released by the last call.</summary>
    public int TicksThisFrame { get; private set; }

    /// <summary>Ticks released over all calls.</summary>
    public long TotalTicks { get; private set; }

    /// <summary>Throw on this call number (1-based); 0 = never.</summary>
    public int ThrowOnCall { get; set; }

    /// <summary>Report <see cref="IsFaulted"/> from this call number on (1-based); 0 = never.</summary>
    public int FaultAfterCall { get; set; }

    /// <inheritdoc />
    public bool IsFaulted => FaultAfterCall > 0 && Calls >= FaultAfterCall;

    /// <inheritdoc />
    public void RunFrame()
    {
        log.Add(FramePhase.RunFrame);
        Calls++;
        if (Calls == ThrowOnCall)
        {
            throw new InjectedFaultException($"run-frame threw on call {Calls}");
        }

        TicksThisFrame = driver.Advance(clock.Timestamp);
        TotalTicks += TicksThisFrame;
    }
}

/// <summary>A recording pre-frame hook.</summary>
public sealed class RecordingPreHook(FrameOrderLog log, int id) : IPreFrameHook
{
    /// <summary>Total calls.</summary>
    public int Calls { get; private set; }

    /// <summary>Throw on this call number (1-based); 0 = never.</summary>
    public int ThrowOnCall { get; set; }

    /// <inheritdoc />
    public void OnPreFrame()
    {
        log.Add(FramePhase.PreHook + id);
        Calls++;
        if (Calls == ThrowOnCall)
        {
            throw new InjectedFaultException($"pre-frame hook {id} threw on call {Calls}");
        }
    }
}

/// <summary>A recording post-present hook.</summary>
public sealed class RecordingPostHook(FrameOrderLog log, int id) : IPostPresentHook
{
    /// <summary>Total calls.</summary>
    public int Calls { get; private set; }

    /// <summary>Throw on this call number (1-based); 0 = never.</summary>
    public int ThrowOnCall { get; set; }

    /// <inheritdoc />
    public void OnPostPresent()
    {
        log.Add(FramePhase.PostHook + id);
        Calls++;
        if (Calls == ThrowOnCall)
        {
            throw new InjectedFaultException($"post-present hook {id} threw on call {Calls}");
        }
    }
}

/// <summary>A recording render-clock sampler.</summary>
public sealed class RecordingSampler(FrameOrderLog log) : IRenderClockSampler
{
    /// <summary>Total calls.</summary>
    public int Calls { get; private set; }

    /// <summary>Throw on this call number (1-based); 0 = never.</summary>
    public int ThrowOnCall { get; set; }

    /// <inheritdoc />
    public void Sample()
    {
        log.Add(FramePhase.Sample);
        Calls++;
        if (Calls == ThrowOnCall)
        {
            throw new InjectedFaultException($"render-clock sample threw on call {Calls}");
        }
    }
}

/// <summary>A recording presentation root.</summary>
public sealed class RecordingPresentation(FrameOrderLog log) : IPresentationRoot
{
    /// <summary>Total calls.</summary>
    public int Calls { get; private set; }

    /// <summary>The alpha of the last call.</summary>
    public int LastAlpha { get; private set; } = -1;

    /// <summary>Throw on this call number (1-based); 0 = never.</summary>
    public int ThrowOnCall { get; set; }

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        log.Add(FramePhase.Present);
        Calls++;
        LastAlpha = alphaPermille;
        if (Calls == ThrowOnCall)
        {
            throw new InjectedFaultException($"present threw on call {Calls}");
        }
    }
}

/// <summary>Records every fault message.</summary>
public sealed class RecordingFaultSink : IFaultSink
{
    /// <summary>The messages received, in order.</summary>
    public List<string> Messages { get; } = [];

    /// <inheritdoc />
    public void OnFault(string message) => Messages.Add(message);
}

/// <summary>An <see cref="IPeerPresence"/> the scenario sets by hand.</summary>
public sealed class FakePeerPresence : IPeerPresence
{
    /// <inheritdoc />
    public bool AnyRemotePeer { get; set; }
}

/// <summary>One frame sequence built entirely from doubles.</summary>
public sealed class FrameLoopRig
{
    /// <summary>Builds the rig with <paramref name="preHooks"/> pre-frame and <paramref name="postHooks"/> post-present hooks registered on the sequence.</summary>
    /// <param name="preHooks">Number of pre-frame hooks (ids 1..n).</param>
    /// <param name="postHooks">Number of post-present hooks (ids 1..n).</param>
    /// <param name="maxCatchUp">The driver's catch-up cap.</param>
    /// <param name="register">False to create the hooks without registering them (the scenario registers them through <c>GameLoop</c>).</param>
    public FrameLoopRig(int preHooks, int postHooks, int maxCatchUp = 4, bool register = true)
    {
        Driver = new TickDriver(FakeClock.Freq, maxCatchUp);
        Run = new RecordingFrameRun(Clock, Driver, Log);
        Sampler = new RecordingSampler(Log);
        Presentation = new RecordingPresentation(Log);
        Sequence = new FrameSequence(Clock, Driver, Run, Sampler, Presentation);
        Pre = new RecordingPreHook[preHooks];
        for (int i = 0; i < preHooks; i++)
        {
            Pre[i] = new RecordingPreHook(Log, i + 1);
            if (register)
            {
                Sequence.RegisterPreFrameHook(Pre[i]);
            }
        }

        Post = new RecordingPostHook[postHooks];
        for (int i = 0; i < postHooks; i++)
        {
            Post[i] = new RecordingPostHook(Log, i + 1);
            if (register)
            {
                Sequence.RegisterPostPresentHook(Post[i]);
            }
        }
    }

    /// <summary>The shared order log.</summary>
    public FrameOrderLog Log { get; } = new();

    /// <summary>The fake clock.</summary>
    public FakeClock Clock { get; } = new();

    /// <summary>The real tick driver.</summary>
    public TickDriver Driver { get; }

    /// <summary>The recording run-frame.</summary>
    public RecordingFrameRun Run { get; }

    /// <summary>The recording sampler.</summary>
    public RecordingSampler Sampler { get; }

    /// <summary>The recording presentation root.</summary>
    public RecordingPresentation Presentation { get; }

    /// <summary>The pre-frame hooks, in registration order.</summary>
    public RecordingPreHook[] Pre { get; }

    /// <summary>The post-present hooks, in registration order.</summary>
    public RecordingPostHook[] Post { get; }

    /// <summary>The sequence under test.</summary>
    public FrameSequence Sequence { get; }

    /// <summary>The expected log of one complete frame.</summary>
    /// <returns>Phase codes in order.</returns>
    public int[] ExpectedFullFrame()
    {
        var codes = new List<int>();
        for (int i = 0; i < Pre.Length; i++)
        {
            codes.Add(FramePhase.PreHook + i + 1);
        }

        codes.Add(FramePhase.RunFrame);
        codes.Add(FramePhase.Sample);
        codes.Add(FramePhase.Present);
        for (int i = 0; i < Post.Length; i++)
        {
            codes.Add(FramePhase.PostHook + i + 1);
        }

        return [.. codes];
    }
}
