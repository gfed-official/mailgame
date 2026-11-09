using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The flight render timeline (ADR-0016 section 6 "Flights": <c>HostTick − flightRenderDelayTicks</c> (3) + alpha; TR-throwing-016 client
/// half; throwing-catching story 017). A flight launched at tick <c>L</c> is drawn at render tick <c>H − 3</c>, age <c>k = H − 3 − L</c>,
/// as the integer segment <c>(p(k), p(k + 1))</c> of <see cref="Ballistic.PositionAt"/> plus <c>alpha</c>/1000 of a tick. Every frame is one
/// closed-form evaluation per endpoint, so a late <c>ThrowLaunched</c> lands on its current age at once (fast-forward, no replay). Before
/// <c>L</c> the flight is <see cref="FlightPhase.Hidden"/> (the GDD OQ16 hold-at-age-0 is M0). A resolved flight's age is clamped to its
/// resolution age <c>r</c>: the last segment ends at the contact, and from age <c>r</c> on it is <see cref="FlightPhase.Landed"/> at the
/// <c>FlightResolved</c> contact, never a <c>p(k)</c> past it. Integers only; pure, allocation-free, thread-safe.
/// </summary>
/// <example><code>FlightFrame f = FlightTimeline.Sample(in track, clock.HostTick, clock.AlphaPermille, settings.MaxFlightTicks);</code></example>
public static class FlightTimeline
{
    /// <summary>
    /// <c>flightRenderDelayTicks</c>: flights render this many ticks behind the host tick (ADR-0016 section 6). A constant at M-1a, not a
    /// <c>throw.*</c> balance row.
    /// </summary>
    public const int RenderDelayTicks = 3;

    /// <summary>Samples <paramref name="track"/> at host tick <paramref name="hostTick"/> plus <paramref name="alphaPermille"/>.</summary>
    /// <param name="track">The flight's launch and, once known, its resolution.</param>
    /// <param name="hostTick"><c>IRenderClock.HostTick</c>.</param>
    /// <param name="alphaPermille"><c>IRenderClock.AlphaPermille</c>, clamped to 0..999.</param>
    /// <param name="maxAgeTicks"><c>throw.maxFlightTicks</c>: an unresolved flight holds at <c>p(max)</c> past it.</param>
    /// <returns>Where the view draws the flight this frame.</returns>
    public static FlightFrame Sample(in FlightTrack track, uint hostTick, int alphaPermille, int maxAgeTicks)
    {
        int alpha = alphaPermille < 0 ? 0 : alphaPermille > 999 ? 999 : alphaPermille;
        long age = (long)hostTick - RenderDelayTicks - track.LaunchTick;
        if (age < 0)
        {
            return default;
        }

        Launch launch = track.Launch;
        if (track.ResolvedAge >= 0 && age >= track.ResolvedAge)
        {
            return new FlightFrame(FlightPhase.Landed, track.ResolvedAge, track.Contact, track.Contact, 0, (long)track.ResolvedAge * 1000);
        }

        if (track.ResolvedAge < 0 && age >= maxAgeTicks)
        {
            Vec3Mm end = Ballistic.PositionAt(in launch, maxAgeTicks);
            return new FlightFrame(FlightPhase.InFlight, maxAgeTicks, end, end, 0, (long)maxAgeTicks * 1000);
        }

        int k = (int)age;
        Vec3Mm from = Ballistic.PositionAt(in launch, k);
        Vec3Mm to = k + 1 == track.ResolvedAge ? track.Contact : Ballistic.PositionAt(in launch, k + 1);
        return new FlightFrame(FlightPhase.InFlight, k, from, to, alpha, ((long)k * 1000) + alpha);
    }
}

/// <summary>
/// What <see cref="FlightTimeline.Sample"/> needs of one flight: the F2 launch and launch tick from <c>ThrowLaunched</c>, and, once
/// <c>FlightResolved</c> has arrived, the resolution age (<c>tick − launchTick</c>) and contact. Values only.
/// </summary>
/// <example><code>var track = new FlightTrack(launch, launchTick, FlightTrack.Unresolved, default);</code></example>
public readonly struct FlightTrack
{
    /// <summary>The <see cref="ResolvedAge"/> of a flight whose <c>FlightResolved</c> has not arrived.</summary>
    public const int Unresolved = -1;

    /// <summary>The F2 launch (origin, velocity, the client's <c>throw.g</c>).</summary>
    public readonly Launch Launch;

    /// <summary>The host tick the flight launched on (age 0).</summary>
    public readonly uint LaunchTick;

    /// <summary>The resolution age in ticks, or <see cref="Unresolved"/>.</summary>
    public readonly int ResolvedAge;

    /// <summary>The <c>FlightResolved</c> contact point (meaningful once resolved).</summary>
    public readonly Vec3Mm Contact;

    /// <summary>Creates a track.</summary>
    /// <param name="launch">The launch.</param>
    /// <param name="launchTick">The launch tick.</param>
    /// <param name="resolvedAge">The resolution age, or <see cref="Unresolved"/>.</param>
    /// <param name="contact">The contact point.</param>
    public FlightTrack(Launch launch, uint launchTick, int resolvedAge, Vec3Mm contact)
    {
        Launch = launch;
        LaunchTick = launchTick;
        ResolvedAge = resolvedAge;
        Contact = contact;
    }
}

/// <summary>One flight's sampled render position (<see cref="FlightTimeline.Sample"/>). <c>default</c> is <see cref="FlightPhase.Hidden"/>.</summary>
/// <example><code>if (f.Phase == FlightPhase.InFlight) { view.Place(f.From, f.To, f.AlphaPermille); }</code></example>
public readonly struct FlightFrame
{
    /// <summary>Hidden, in flight or landed.</summary>
    public readonly FlightPhase Phase;

    /// <summary>The integer age <c>k</c> drawn (clamped to the resolution age or <c>maxFlightTicks</c>).</summary>
    public readonly int AgeTicks;

    /// <summary><c>p(k)</c>; the contact when <see cref="FlightPhase.Landed"/>.</summary>
    public readonly Vec3Mm From;

    /// <summary><c>p(k + 1)</c>, the contact on the last segment of a resolved flight; the contact when <see cref="FlightPhase.Landed"/>.</summary>
    public readonly Vec3Mm To;

    /// <summary>How far from <see cref="From"/> to <see cref="To"/> the view places the flight, 0..999 (0 when landed or held).</summary>
    public readonly int AlphaPermille;

    /// <summary>Elapsed render time since launch in thousandths of a tick, stopped at the contact (the spin input).</summary>
    public readonly long ElapsedMilliTicks;

    /// <summary>Creates a frame.</summary>
    /// <param name="phase">The phase.</param>
    /// <param name="ageTicks">The age drawn.</param>
    /// <param name="from">The segment start.</param>
    /// <param name="to">The segment end.</param>
    /// <param name="alphaPermille">The fraction, permille.</param>
    /// <param name="elapsedMilliTicks">The elapsed render time, milli-ticks.</param>
    public FlightFrame(FlightPhase phase, int ageTicks, Vec3Mm from, Vec3Mm to, int alphaPermille, long elapsedMilliTicks)
    {
        Phase = phase;
        AgeTicks = ageTicks;
        From = from;
        To = to;
        AlphaPermille = alphaPermille;
        ElapsedMilliTicks = elapsedMilliTicks;
    }
}

/// <summary>Where a mirrored flight is on the render timeline.</summary>
/// <example><code>bool draw = frame.Phase != FlightPhase.Hidden;</code></example>
public enum FlightPhase : byte
{
    /// <summary>The render tick is before the launch tick: not drawn.</summary>
    Hidden = 0,

    /// <summary>Drawn on a segment of the arc.</summary>
    InFlight = 1,

    /// <summary>At its contact point; the mirror drops it at the next sample.</summary>
    Landed = 2,
}
