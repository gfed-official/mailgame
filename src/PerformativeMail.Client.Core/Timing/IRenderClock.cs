namespace PerformativeMail.Client.Core.Timing;

/// <summary>
/// The one render clock every Presentation timeline derives from (ADR-0016 section 6, TR-hud-004, TR-throwing-016); never
/// derived from Godot's <c>delta</c>. Sampled once per frame by the host shell after <c>RunFrame</c> and before
/// <c>Present</c>, so every presenter of a frame sees the same values. <c>IHostClock</c> is not amended. Main thread only.
/// </summary>
/// <example>
/// <code>
/// IRenderClock clock = renderClock;
/// if (clock.IsValid)
/// {
///     long nowMs = clock.RenderSimTimeMs;   // HUD timers (F3)
/// }
/// </code>
/// </example>
public interface IRenderClock
{
    /// <summary><c>IHostClock.EstimatedHostTick</c> at the sample (on the host: <c>SimWorld.CurrentTick</c> via the host clock).</summary>
    uint HostTick { get; }

    /// <summary><c>TickDriver.AlphaPermille</c> (ADR-0002) at the sample, 0..999.</summary>
    int AlphaPermille { get; }

    /// <summary><c>((long)HostTick * 1000 + AlphaPermille) / 30</c>, integer, floor.</summary>
    long RenderSimTimeMs { get; }

    /// <summary><c>IHostClock.IsValid</c> at the sample.</summary>
    bool IsValid { get; }
}
