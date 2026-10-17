namespace PerformativeMail.Sim.Hosting;

/// <summary>
/// The host's monotonic time source (ADR-0002 section 3). Implemented by the host (the Godot shell wraps
/// <c>Stopwatch.GetTimestamp()</c>; tests supply integer timestamps). The Sim reads time only through this interface,
/// and only <see cref="FrameRunner"/> does so, once per frame. Not thread-safe: read on the main thread.
/// </summary>
/// <example>
/// <code>
/// sealed class StopwatchClock : IMonotonicClock
/// {
///     public long Timestamp =&gt; System.Diagnostics.Stopwatch.GetTimestamp();
///     public long Frequency =&gt; System.Diagnostics.Stopwatch.Frequency;
/// }
/// </code>
/// </example>
public interface IMonotonicClock
{
    /// <summary>The current monotonic timestamp in counts of <see cref="Frequency"/> per second. Non-negative, non-decreasing.</summary>
    long Timestamp { get; }

    /// <summary>Counts per second of <see cref="Timestamp"/>. Constant for the life of the clock.</summary>
    long Frequency { get; }
}
