namespace PerformativeMail.Sim.Content;

/// <summary>
/// F3 ExactTickCheck (content-data F3): whether a millisecond duration is a whole number of 30 Hz ticks, that is
/// <c>(ms x 30) mod 1000 = 0</c>, computed in <see cref="long"/> (<c>ms x 30</c> leaves <see cref="int"/> above 71 582 788 ms).
/// It is equivalent to <c>ms mod 100 = 0</c> (gcd(30, 1000) = 10). F3 is for fields that must be whole ticks
/// (<c>catchTapMs</c>, <c>catchPredictMs</c>, CV-THR-12); a field that need not be whole rounds up through
/// <see cref="Hosting.TickTime.ContentTicks"/> instead. Pure, allocation-free, thread-safe, no static state. The caller range-checks
/// <paramref name="ms"/> (the formula's domain is 0 to 86 400 000); a negative multiple of 100 also answers true.
/// </summary>
/// <example>
/// <code>
/// bool ok  = ExactTickCheck.IsWholeTicks(500);   // true: 15 000 mod 1 000 = 0, 15 ticks
/// bool bad = ExactTickCheck.IsWholeTicks(501);   // false: 15 030 mod 1 000 = 30
/// </code>
/// </example>
public static class ExactTickCheck
{
    private const long MillisecondsPerSecond = 1000;

    /// <summary>True when <paramref name="ms"/> milliseconds is a whole number of ticks at <see cref="SimSettings.TickRateHz"/>.</summary>
    /// <param name="ms">The duration in milliseconds.</param>
    /// <example><code>bool ok = ExactTickCheck.IsWholeTicks(100);   // true, 3 ticks</code></example>
    public static bool IsWholeTicks(long ms) => (ms * SimSettings.TickRateHz) % MillisecondsPerSecond == 0;
}
