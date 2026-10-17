using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Hosting;

/// <summary>
/// The Godot-free integer fixed-step accumulator (GDD F2, TR-sim-003/004/006/009, ADR-0002 sections 1 and 4).
/// It never reads a clock: the host passes a monotonic timestamp in counts of <see cref="Frequency"/> per second.
/// Each <see cref="Advance"/> releases at most <see cref="MaxCatchUpTicksPerFrame"/> ticks; surplus due ticks are
/// dropped and counted, never banked. All arithmetic is <c>long</c>; the accumulator is kept in
/// 1/30-count units (<c>acc30</c>) so a tick is exactly <see cref="Frequency"/> units and no rounding drifts.
/// O(1) per call and allocation-free. Not thread-safe: owned by the main thread.
/// </summary>
/// <remarks>
/// Timestamps must be non-negative and monotonic in practice (a stopwatch count). A timestamp earlier than the last one
/// is counted in <c>BackwardsClock</c> and treated as zero elapsed time; <c>last</c> is never moved back.
/// </remarks>
/// <example>
/// <code>
/// var driver = new TickDriver(frequency: 10_000_000, maxCatchUpTicksPerFrame: 4);
/// driver.Start(now: 0);
/// int ticks = driver.Advance(now: 1_500_000);   // 150 ms: 4 ticks run, acc30 = 5_000_000
/// int alpha = driver.AlphaPermille;             // 500 (Presentation only)
/// </code>
/// </example>
public sealed class TickDriver
{
    /// <summary>Smallest accepted clock frequency (counts per second).</summary>
    public const long MinFrequency = 1_000;

    /// <summary>Largest accepted clock frequency (counts per second).</summary>
    public const long MaxFrequency = 1_000_000_000_000;

    private readonly long _frequency;
    private readonly int _maxCatchUp;

    private long _last;
    private long _acc30;
    private long _ticksRunTotal;
    private long _droppedTicksTotal;
    private long _clampedTimeTotal;
    private long _backwardsClock;
    private long _hitches;

    /// <summary>Creates an <see cref="TickDriverState.Idle"/> driver.</summary>
    /// <param name="frequency">Clock counts per second, 10^3 to 10^12 inclusive.</param>
    /// <param name="maxCatchUpTicksPerFrame">Cap on ticks released per <see cref="Advance"/> call, at least 1 (the 3-8 safe range is a SimSettings concern).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frequency"/> is outside 10^3..10^12, or <paramref name="maxCatchUpTicksPerFrame"/> is below 1.</exception>
    /// <example>
    /// <code>
    /// var d = new TickDriver(10_000_000, 4);
    /// </code>
    /// </example>
    public TickDriver(long frequency, int maxCatchUpTicksPerFrame)
    {
        if (frequency < MinFrequency || frequency > MaxFrequency)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency), "Frequency must be within 10^3..10^12 counts per second.");
        }

        if (maxCatchUpTicksPerFrame < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCatchUpTicksPerFrame), "The catch-up cap must be at least 1.");
        }

        _frequency = frequency;
        _maxCatchUp = maxCatchUpTicksPerFrame;
        State = TickDriverState.Idle;
    }

    /// <summary>Clock counts per second.</summary>
    public long Frequency => _frequency;

    /// <summary>The cap on ticks released per <see cref="Advance"/> call.</summary>
    public int MaxCatchUpTicksPerFrame => _maxCatchUp;

    /// <summary>The current lifecycle state.</summary>
    public TickDriverState State { get; private set; }

    /// <summary>A snapshot of the diagnostic totals.</summary>
    public TickDriverCounters Counters =>
        new(_ticksRunTotal, _droppedTicksTotal, _clampedTimeTotal, _backwardsClock, _hitches);

    /// <summary>Ticks released so far (less any removed by <see cref="DropRemaining"/>).</summary>
    public long TicksRunTotal => _ticksRunTotal;

    /// <summary>Due ticks discarded so far (cap overflow and <see cref="DropRemaining"/>).</summary>
    public long DroppedTicksTotal => _droppedTicksTotal;

    /// <summary>Clock counts discarded by the 1 s clamp so far.</summary>
    public long ClampedTimeTotal => _clampedTimeTotal;

    /// <summary>Backwards timestamps seen so far.</summary>
    public long BackwardsClock => _backwardsClock;

    /// <summary>Frames on which the cap dropped ticks. The FrameRunner writes the hitch record when this increases.</summary>
    public long Hitches => _hitches;

    /// <summary>The newest timestamp accepted (diagnostic and test seam).</summary>
    internal long Last => _last;

    /// <summary>The accumulator in 1/30-count units, always in <c>[0, Frequency)</c> after a call (diagnostic and test seam).</summary>
    internal long Acc30 => _acc30;

    /// <summary>
    /// The interpolation fraction of the next tick in permille, 0-999, exactly <c>acc30 * 1000 / Frequency</c>;
    /// 0 while <see cref="TickDriverState.Paused"/>. Presentation-only: Sim code must never read it.
    /// </summary>
    public int AlphaPermille => State == TickDriverState.Paused ? 0 : (int)(_acc30 * 1000 / _frequency);

    /// <summary>Enters <see cref="TickDriverState.Running"/> from any state with <c>last = now; acc30 = 0</c>. Counters are kept.</summary>
    /// <param name="now">The host's current monotonic timestamp.</param>
    /// <example>
    /// <code>
    /// driver.Start(clockNow);
    /// </code>
    /// </example>
    public void Start(long now)
    {
        _last = now;
        _acc30 = 0;
        State = TickDriverState.Running;
    }

    /// <summary>Returns to <see cref="TickDriverState.Idle"/> and clears the accumulator. Counters are kept. Safe in any state.</summary>
    public void Stop()
    {
        _acc30 = 0;
        State = TickDriverState.Idle;
    }

    /// <summary>Pauses a <see cref="TickDriverState.Running"/> driver; a no-op in any other state. Time stops accumulating.</summary>
    public void Pause()
    {
        if (State == TickDriverState.Running)
        {
            State = TickDriverState.Paused;
        }
    }

    /// <summary>
    /// Resumes a <see cref="TickDriverState.Paused"/> driver with <c>last = now; acc30 = 0</c> (no burst for the paused
    /// time); a no-op in any other state.
    /// </summary>
    /// <param name="now">The host's current monotonic timestamp.</param>
    public void Resume(long now)
    {
        if (State == TickDriverState.Paused)
        {
            _last = now;
            _acc30 = 0;
            State = TickDriverState.Running;
        }
    }

    /// <summary>
    /// Re-bases a <see cref="TickDriverState.Running"/> driver (<c>last = now; acc30 = 0</c>), for example after a long
    /// host stall. Changes no counter and writes no hitch. Outside <c>Running</c> it asserts
    /// (<see cref="SimAssertCode.TickDriverResyncState"/>) under <c>SIM_ASSERT</c> and is ignored otherwise.
    /// </summary>
    /// <param name="now">The host's current monotonic timestamp.</param>
    public void Resync(long now)
    {
        SimAssert.That(State == TickDriverState.Running, SimAssertCode.TickDriverResyncState);
        if (State != TickDriverState.Running)
        {
            return;
        }

        _last = now;
        _acc30 = 0;
    }

    /// <summary>
    /// Advances to <paramref name="now"/> and returns the number of ticks to run (0 to <see cref="MaxCatchUpTicksPerFrame"/>).
    /// Implements GDD F2 exactly. Returns 0 and touches nothing unless <see cref="TickDriverState.Running"/>.
    /// </summary>
    /// <param name="now">The host's current monotonic timestamp.</param>
    /// <returns>The ticks to run this frame.</returns>
    /// <example>
    /// <code>
    /// int n = driver.Advance(clockNow);
    /// for (int i = 0; i &lt; n; i++) { /* sim.Tick(); */ }
    /// </code>
    /// </example>
    public int Advance(long now)
    {
        if (State != TickDriverState.Running)
        {
            return 0;
        }

        long delta;
        if (now < _last)
        {
            _backwardsClock++;
            delta = 0;
        }
        else
        {
            delta = now - _last;
            _last = now;
        }

        if (delta > _frequency)
        {
            _clampedTimeTotal += delta - _frequency;
            delta = _frequency;
        }

        _acc30 += delta * TickRate.Hz;
        long due = _acc30 / _frequency;
        long n = due < _maxCatchUp ? due : _maxCatchUp;
        _acc30 -= n * _frequency;
        if (due > n)
        {
            _droppedTicksTotal += due - n;
            _acc30 %= _frequency;
            _hitches++;
        }

        _ticksRunTotal += n;
        return (int)n;
    }

    /// <summary>
    /// Moves <paramref name="m"/> ticks from run to dropped (<c>TicksRunTotal -= m; DroppedTicksTotal += m</c>); the
    /// accumulator is unchanged. Used by the M0 catch-up guard when it abandons the tail of a frame. Out of range
    /// (<c>m &lt; 0</c> or above <c>TicksRunTotal</c>) asserts (<see cref="SimAssertCode.TickDriverDropRange"/>) under
    /// <c>SIM_ASSERT</c> and clamps into range otherwise.
    /// </summary>
    /// <param name="m">Ticks to reclassify as dropped.</param>
    public void DropRemaining(int m)
    {
        SimAssert.That(m >= 0 && m <= _ticksRunTotal, SimAssertCode.TickDriverDropRange);
        long moved = m < 0 ? 0 : (m > _ticksRunTotal ? _ticksRunTotal : m);
        _ticksRunTotal -= moved;
        _droppedTicksTotal += moved;
    }
}
