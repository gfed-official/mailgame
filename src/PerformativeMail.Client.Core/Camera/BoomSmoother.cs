using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Camera;

/// <summary>
/// Boom length smoothing (GDD player-character-movement P9; TD-198 item 4, approved by Evan 2026-10-29): linear at a fixed rate,
/// pulling in fast enough to cover <see cref="ReferenceBoomMm"/> in <see cref="PullInMs"/> and easing out over
/// <see cref="EaseOutMs"/> for the same span. It never overshoots the target. <c>dtMs</c> is a difference of
/// <c>IRenderClock.RenderSimTimeMs</c>, never Godot's <c>delta</c>; zero or negative changes nothing. Static and pure:
/// no fields, no clock, integer-only, allocation-free.
/// </summary>
/// <example>
/// <code>
/// long now = renderClock.RenderSimTimeMs;
/// boomMm = BoomSmoother.Step(boomMm, BoomSolver.Solve(world, in pivot, yaw, pitch, shoulder, profile.BoomMm), now - lastMs);
/// lastMs = now;
/// </code>
/// </example>
public static class BoomSmoother
{
    /// <summary>The span the rates are set by: the default boom, mm.</summary>
    // Mail Dev default, approved by Evan 2026-10-29 (TD-201 item 4): fixed rates from the 3000 mm span (60 mm/ms pull-in, 7.5 mm/ms ease-out), not per-transition timing.
    public const int ReferenceBoomMm = 3000;

    /// <summary>Time to pull in across the reference span, ms.</summary>
    public const int PullInMs = 50;

    /// <summary>Time to ease out across the reference span, ms.</summary>
    public const int EaseOutMs = 400;

    /// <summary>Moves the boom toward the target for one frame.</summary>
    /// <param name="currentMm">The smoothed boom last frame, mm.</param>
    /// <param name="targetMm">This frame's solved boom (<see cref="BoomSolver.Solve"/>), mm.</param>
    /// <param name="dtMs">Render time since last frame, ms.</param>
    /// <returns>The new smoothed boom, mm, between <paramref name="currentMm"/> and <paramref name="targetMm"/>.</returns>
    public static int Step(int currentMm, int targetMm, long dtMs)
    {
        if (dtMs <= 0 || currentMm == targetMm)
        {
            return currentMm;
        }

        var sink = default(SimCounterSink);
        if (targetMm < currentMm)
        {
            long pull = Fix.MulDiv(ReferenceBoomMm, dtMs, PullInMs, ref sink);
            return pull >= (long)currentMm - targetMm ? targetMm : checked((int)(currentMm - pull));
        }

        long ease = Fix.MulDiv(ReferenceBoomMm, dtMs, EaseOutMs, ref sink);
        return ease >= (long)targetMm - currentMm ? targetMm : checked((int)(currentMm + ease));
    }
}
