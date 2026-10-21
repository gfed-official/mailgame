namespace PerformativeMail.Game.Hosting;

/// <summary>
/// The frame sequence's seam over the Client.Core render clock (ADR-0016 section 6): sampled exactly once per frame after
/// <c>RunFrame</c> and before <c>Present</c>. Story 005 adapts <c>PerformativeMail.Client.Core.Timing.RenderClock</c>.
/// Main thread only.
/// </summary>
/// <example><code>sampler.Sample();   // FrameSequence step 3</code></example>
public interface IRenderClockSampler
{
    /// <summary>Samples the render clock for this frame.</summary>
    void Sample();
}
