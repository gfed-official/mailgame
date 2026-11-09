using PerformativeMail.Client.Core.Net;
using PerformativeMail.Client.Core.Timing;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Game.Hosting;

/// <summary>
/// The frame sequence's <see cref="IFrameRun"/> over the booted <see cref="HostSession"/> (godot-host-shell story 005): one
/// <see cref="HostSession.RunFrame"/> per frame, and faulted once the world's state is <see cref="SimState.Faulted"/> (a
/// non-<c>SIM_ASSERT</c> build does not rethrow a world fault). Main thread only.
/// </summary>
/// <example><code>var run = new SessionFrameRun(session);   // FrameSequence step 2</code></example>
/// <param name="session">The host session.</param>
public sealed class SessionFrameRun(HostSession session) : IFrameRun
{
    private readonly HostSession _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public bool IsFaulted => _session.World.State == SimState.Faulted;

    /// <inheritdoc />
    public void RunFrame() => _session.RunFrame();
}

/// <summary>The frame sequence's <see cref="IRenderClockSampler"/> over the Client.Core <see cref="RenderClock"/> (ADR-0016 section 6, story 005).</summary>
/// <example><code>var sampler = new RenderClockSampler(renderClock);   // FrameSequence step 3</code></example>
/// <param name="clock">The render clock.</param>
public sealed class RenderClockSampler(RenderClock clock) : IRenderClockSampler
{
    private readonly RenderClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public void Sample() => _clock.Sample();
}
