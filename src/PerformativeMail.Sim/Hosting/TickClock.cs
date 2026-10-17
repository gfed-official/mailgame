namespace PerformativeMail.Sim.Hosting;

/// <summary>
/// A countdown clock reading as integers (GDD F1). The Sim stores no strings; the HUD formats
/// <c>Minutes:Seconds</c> (Presentation). Produced by <see cref="TickTime.Clock(uint, uint)"/>,
/// whose results are within 0-99 minutes and 0-59 seconds. Immutable and thread-safe.
/// </summary>
/// <remarks>
/// A presentation-facing display value: never stored in state, ordered or hashed (the synthesised record-struct members are
/// deterministic over two ints). The 0-99 / 0-59 bounds hold only for values produced by <see cref="TickTime.Clock(long)"/>.
/// </remarks>
/// <param name="Minutes">Whole minutes, 0 to 99.</param>
/// <param name="Seconds">Seconds within the minute, 0 to 59.</param>
/// <example>
/// <code>
/// TickClock c = TickTime.Clock(currentTick: 0, deadlineTick: 360);   // c.Minutes == 0, c.Seconds == 12
/// </code>
/// </example>
public readonly record struct TickClock(int Minutes, int Seconds);
