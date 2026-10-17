using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Hosting;

/// <summary>
/// Pure, allocation-free, integer-only tick/time conversions (GDD simulation-core F1, TR-sim-001,
/// ADR-0002 section 1, ADR-0006). All arithmetic is <see cref="long"/>; <c>float</c>/<c>double</c>
/// (for example <c>seconds * 30</c>) are forbidden. Thread-safe (stateless).
/// <para>
/// <b>Range checks.</b> Inputs are asserted in range under <c>SIM_ASSERT</c> (Debug/ReleaseAssert):
/// tick counts in [0, 2^31 - 1], milliseconds in [0, 86 400 000]. Without asserts (Release) an
/// out-of-range input is clamped into its range and the helper returns the result for the clamped
/// value. These helpers are pure and take no counter sink, so a clamp is not counted.
/// </para>
/// <para>
/// <b>Round trip is not an identity.</b> Never convert ticks to ms and back:
/// <c>ContentTicks(TicksToMsCeil(1)) == 2</c>. Keep the unit you were given.
/// </para>
/// </summary>
/// <example>
/// <code>
/// long t   = TickTime.ContentTicks(400);                  // 12
/// long ms  = TickTime.TicksToMsFloor(1);                  // 33
/// TickClock c = TickTime.Clock(currentTick: 0, deadlineTick: 360);  // 0:12
/// </code>
/// </example>
public static class TickTime
{
    /// <summary>Largest tick count or tick difference the F1 conversions accept: 2^31 - 1.</summary>
    public const long MaxTicks = int.MaxValue;

    /// <summary>Largest content duration in milliseconds (the ContentValidator cap): 86 400 000.</summary>
    public const long MaxMs = 86_400_000;

    /// <summary>Largest displayed seconds value: 99:59 = 5999 seconds.</summary>
    public const long MaxDisplaySeconds = 5999;

    /// <summary>
    /// Whole milliseconds in <paramref name="ticks"/>, rounded down: <c>(t * 1000) / 30</c>.
    /// The test and input-clock mapping. Out-of-range input asserts (<see cref="SimAssertCode.TickRange"/>)
    /// under <c>SIM_ASSERT</c>, otherwise clamps to [0, <see cref="MaxTicks"/>].
    /// </summary>
    /// <param name="ticks">Tick count or difference, 0 to 2^31 - 1.</param>
    /// <returns>Milliseconds, rounded down.</returns>
    /// <example>
    /// <code>long ms = TickTime.TicksToMsFloor(1); // 33</code>
    /// </example>
    public static long TicksToMsFloor(long ticks)
    {
        SimAssert.That(ticks >= 0 && ticks <= MaxTicks, SimAssertCode.TickRange);
        long t = Math.Clamp(ticks, 0L, MaxTicks);
        return (t * 1000) / TickRate.Hz;
    }

    /// <summary>
    /// Whole milliseconds in <paramref name="ticks"/>, rounded up: <c>(t * 1000 + 29) / 30</c>.
    /// Used for "not before" bounds. Range handling as <see cref="TicksToMsFloor"/>.
    /// </summary>
    /// <param name="ticks">Tick count or difference, 0 to 2^31 - 1.</param>
    /// <returns>Milliseconds, rounded up.</returns>
    /// <example>
    /// <code>long ms = TickTime.TicksToMsCeil(1); // 34</code>
    /// </example>
    public static long TicksToMsCeil(long ticks)
    {
        SimAssert.That(ticks >= 0 && ticks <= MaxTicks, SimAssertCode.TickRange);
        long t = Math.Clamp(ticks, 0L, MaxTicks);
        return (t * 1000 + (TickRate.Hz - 1)) / TickRate.Hz;
    }

    /// <summary>
    /// Ticks for a content duration, rounded up: <c>(ms * 30 + 999) / 1000</c>. 0 ms gives 0 ticks and
    /// any ms of 1 or more gives at least 1 tick. Out-of-range input asserts
    /// (<see cref="SimAssertCode.MsRange"/>) under <c>SIM_ASSERT</c>, otherwise clamps to
    /// [0, <see cref="MaxMs"/>]. Do not feed it the result of <see cref="TicksToMsCeil"/>: the round trip
    /// is not an identity (<c>ContentTicks(TicksToMsCeil(1)) == 2</c>).
    /// </summary>
    /// <param name="ms">Whole milliseconds, 0 to 86 400 000.</param>
    /// <returns>Ticks, rounded up.</returns>
    /// <example>
    /// <code>long t = TickTime.ContentTicks(400); // 12</code>
    /// </example>
    public static long ContentTicks(long ms)
    {
        SimAssert.That(ms >= 0 && ms <= MaxMs, SimAssertCode.MsRange);
        long m = Math.Clamp(ms, 0L, MaxMs);
        return (m * TickRate.Hz + 999) / 1000;
    }

    /// <summary>
    /// Ticks remaining, <c>max(0, (long)deadlineTick - (long)currentTick)</c>. The subtraction is
    /// <see cref="long"/>, so a deadline in the past gives 0 and never wraps as <see cref="uint"/>.
    /// Valid for every <see cref="uint"/> pair, so it asserts nothing.
    /// </summary>
    /// <param name="currentTick">The current tick.</param>
    /// <param name="deadlineTick">The absolute deadline tick.</param>
    /// <returns>Ticks remaining, 0 to <see cref="uint.MaxValue"/>.</returns>
    /// <example>
    /// <code>long r = TickTime.Remaining(currentTick: 20, deadlineTick: 10); // 0</code>
    /// </example>
    public static long Remaining(uint currentTick, uint deadlineTick)
    {
        return Math.Max(0L, (long)deadlineTick - (long)currentTick);
    }

    /// <summary>
    /// Seconds shown on a countdown, rounded up: <c>(r + 29) / 30</c>. It is 0 only when
    /// <paramref name="remaining"/> is 0, so the clock never reads 0:00 while time remains.
    /// A negative input asserts (<see cref="SimAssertCode.TickRange"/>) under <c>SIM_ASSERT</c>,
    /// otherwise it is treated as 0. Uncapped; see <see cref="Clock(long)"/> for the 99:59 cap.
    /// </summary>
    /// <param name="remaining">Ticks remaining, 0 or more (see <see cref="Remaining"/>).</param>
    /// <returns>Display seconds, uncapped.</returns>
    /// <example>
    /// <code>long s = TickTime.DisplaySeconds(331); // 12</code>
    /// </example>
    public static long DisplaySeconds(long remaining)
    {
        SimAssert.That(remaining >= 0, SimAssertCode.TickRange);
        long r = Math.Max(0L, remaining);

        // Ceiling divide without "r + 29", which would overflow for r near long.MaxValue.
        return (r / TickRate.Hz) + (r % TickRate.Hz != 0 ? 1 : 0);
    }

    /// <summary>
    /// The countdown clock for <paramref name="remaining"/> ticks:
    /// <c>min(DisplaySeconds(r), 5999)</c> as minutes and seconds, capped at 99:59.
    /// A negative input is handled as in <see cref="DisplaySeconds"/>.
    /// </summary>
    /// <param name="remaining">Ticks remaining, 0 or more.</param>
    /// <returns>The clock as integers; the HUD formats the text.</returns>
    /// <example>
    /// <code>TickClock c = TickTime.Clock(2160L); // 1:12</code>
    /// </example>
    public static TickClock Clock(long remaining)
    {
        long s = Math.Min(DisplaySeconds(remaining), MaxDisplaySeconds);
        return new TickClock((int)(s / 60), (int)(s % 60));
    }

    /// <summary>
    /// The countdown clock for an absolute deadline: <see cref="Clock(long)"/> of
    /// <see cref="Remaining"/>. A deadline in the past reads 0:00.
    /// </summary>
    /// <param name="currentTick">The current tick.</param>
    /// <param name="deadlineTick">The absolute deadline tick.</param>
    /// <returns>The clock as integers.</returns>
    /// <example>
    /// <code>TickClock c = TickTime.Clock(currentTick: 100, deadlineTick: 90); // 0:00</code>
    /// </example>
    public static TickClock Clock(uint currentTick, uint deadlineTick)
    {
        return Clock(Remaining(currentTick, deadlineTick));
    }
}
