using System.Diagnostics;
using PerformativeMail.Sim.Hosting;

namespace PerformativeMail.Game.Hosting;

/// <summary>
/// The host's time source (ADR-0002 section 3, spike F10): <see cref="Stopwatch.GetTimestamp"/> counts at
/// <see cref="Stopwatch.Frequency"/> per second. Never Godot's <c>delta</c> (F11). Stateless; thread-safe.
/// <c>Frequency</c> must fall in 10^3..10^12 for the tick driver (asserted by the <c>frame-order</c> smoke; the boot log
/// prints it, story 004).
/// </summary>
/// <example>
/// <code>
/// var clock = new StopwatchMonotonicClock();
/// var driver = new TickDriver(clock.Frequency, 4);
/// driver.Start(clock.Timestamp);
/// </code>
/// </example>
public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    /// <inheritdoc />
    public long Timestamp => Stopwatch.GetTimestamp();

    /// <inheritdoc />
    public long Frequency => Stopwatch.Frequency;
}
