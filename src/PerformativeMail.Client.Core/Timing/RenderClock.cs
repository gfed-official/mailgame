using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Client.Core.Timing;

/// <summary>
/// The Client.Core <see cref="IRenderClock"/> (ADR-0016 section 6). <see cref="Sample"/> reads the host clock and the tick
/// driver once and freezes the values until the next sample; before the first sample every value is zero/false. On the
/// host it is exact Sim time with no slew and no step (HUD F3); on tick boundaries <see cref="RenderSimTimeMs"/> equals Sim
/// Core F1 (<c>TickTime.TicksToMsFloor(tick)</c>). Integer-only and allocation-free. Main thread only, not thread-safe.
/// Wiring its inputs into the host shell is story godot-host-shell 005.
/// </summary>
/// <example>
/// <code>
/// var renderClock = new RenderClock(session.HostClock, runner.Driver);
/// runner.RunFrame();
/// renderClock.Sample();                       // once per frame, before Present
/// long ms = renderClock.RenderSimTimeMs;
/// </code>
/// </example>
public sealed class RenderClock : IRenderClock
{
    /// <summary>Milliseconds per second.</summary>
    private const long MsPerSecond = 1000;

    private readonly IHostClock _hostClock;
    private readonly TickDriver _driver;

    /// <summary>Creates an unsampled clock.</summary>
    /// <param name="hostClock">The host-tick source (ADR-0011).</param>
    /// <param name="driver">The tick driver whose <c>AlphaPermille</c> is sampled.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <example><code>var rc = new RenderClock(hostClock, driver);</code></example>
    public RenderClock(IHostClock hostClock, TickDriver driver)
    {
        ArgumentNullException.ThrowIfNull(hostClock);
        ArgumentNullException.ThrowIfNull(driver);
        _hostClock = hostClock;
        _driver = driver;
    }

    /// <inheritdoc />
    public uint HostTick { get; private set; }

    /// <inheritdoc />
    public int AlphaPermille { get; private set; }

    /// <inheritdoc />
    public long RenderSimTimeMs { get; private set; }

    /// <inheritdoc />
    public bool IsValid { get; private set; }

    /// <summary>
    /// Computes <c>((long)hostTick * 1000 + alphaPermille) / 30</c>, integer, floor. Exposed so tests and timelines use
    /// the one formula.
    /// </summary>
    /// <param name="hostTick">The host tick.</param>
    /// <param name="alphaPermille">The interpolation fraction, 0..999.</param>
    /// <returns>Render Sim time in whole milliseconds.</returns>
    /// <example><code>long ms = RenderClock.ComputeRenderSimTimeMs(1, 500);   // 50</code></example>
    public static long ComputeRenderSimTimeMs(uint hostTick, int alphaPermille) =>
        ((long)hostTick * MsPerSecond + alphaPermille) / TickRate.Hz;

    /// <summary>Reads the host tick, the driver's alpha and the host clock's validity once and recomputes the render time.</summary>
    /// <example><code>renderClock.Sample();</code></example>
    public void Sample()
    {
        uint tick = _hostClock.EstimatedHostTick;
        int alpha = _driver.AlphaPermille;
        bool valid = _hostClock.IsValid;
        HostTick = tick;
        AlphaPermille = alpha;
        IsValid = valid;
        RenderSimTimeMs = ComputeRenderSimTimeMs(tick, alpha);
    }
}
