namespace PerformativeMail.Sim.Hosting;

/// <summary>
/// An immutable snapshot of a <see cref="TickDriver"/>'s monotonically increasing diagnostic totals
/// (ADR-0002 section 1). Presentation and the FrameRunner read these; the Sim never branches on them.
/// </summary>
/// <param name="TicksRunTotal">Ticks released to the caller (reduced by <see cref="TickDriver.DropRemaining"/>).</param>
/// <param name="DroppedTicksTotal">Due ticks discarded by the catch-up cap or <see cref="TickDriver.DropRemaining"/>; never banked.</param>
/// <param name="ClampedTimeTotal">Clock counts discarded by the 1 s per-frame clamp.</param>
/// <param name="BackwardsClock">Number of times a timestamp earlier than the last one was supplied.</param>
/// <param name="Hitches">Number of frames on which ticks were dropped by the cap.</param>
/// <example>
/// <code>
/// TickDriverCounters c = driver.Counters;   // c.Hitches, c.DroppedTicksTotal, ...
/// </code>
/// </example>
public readonly record struct TickDriverCounters(
    long TicksRunTotal,
    long DroppedTicksTotal,
    long ClampedTimeTotal,
    long BackwardsClock,
    long Hitches);
