using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// F2, the closed-form ballistic position (ADR-0015 section 3; GDD <c>design/gdd/throwing-catching.md</c> T5 and F2;
/// TR-throwing-001). The position at flight age <c>k</c> ticks is one O(1) evaluation from the <see cref="Launch"/> alone,
/// so a late event, a rejoin catch-up and a preview frame produce identical integers; there is no accumulated state and
/// no per-tick integration. Every product is widened to <see cref="long"/>; the two RH terms of <c>py</c> are separate
/// <see cref="Throw.RH"/> calls (the golden depends on it). At the knob extremes (<c>g</c> 14 000, <c>k</c> 150)
/// <c>g·k²</c> = 3.15 × 10⁸ still fits <see cref="int"/>. Pure, allocation-free, thread-safe.
/// <para>GDD F2:</para>
/// <code>
/// px(k) = ox + RH(vx·k, 30)
/// py(k) = oy + RH(vy·k, 30) − RH(g·k², 1800)
/// pz(k) = oz + RH(vz·k, 30)
/// </code>
/// </summary>
/// <example>
/// <code>
/// Vec3Mm p20 = Ballistic.PositionAt(in launch, 20);   // (10 250, 963, 10 570) for the TC-F1-1 launch
/// </code>
/// </example>
public static class Ballistic
{
    /// <summary>
    /// The largest age <see cref="PositionAt"/> is defined for: the <c>throw.maxFlightTicks</c> validator ceiling
    /// (CV-THR-04, 150 ticks).
    /// </summary>
    public const int MaxAgeTicks = ThrowingChecks.FlightTicksMax;

    /// <summary>Ticks per second, the F2 time divisor (<c>k / 30</c> seconds; <c>k² / 1800 = (k / 30)² / 2</c>).</summary>
    private const int TicksPerSecond = 30;

    /// <summary>The F2 gravity divisor <c>2 · 30²</c>.</summary>
    private const int GravityDivisor = 2 * TicksPerSecond * TicksPerSecond;

    /// <summary>The position at flight age <paramref name="k"/> (GDD F2, see the type remarks).</summary>
    /// <param name="l">The launch.</param>
    /// <param name="k">
    /// Flight age in ticks, defined for 0 to <see cref="MaxAgeTicks"/>; outside that range it asserts under
    /// <c>SIM_ASSERT</c> (<see cref="SimAssertCode.BallisticAgeOutOfRange"/>) and Release evaluates the same formula.
    /// </param>
    /// <returns>The position in mm; <c>PositionAt(l, 0) == l.Origin</c>.</returns>
    /// <example><code>Vec3Mm p = Ballistic.PositionAt(in launch, age);</code></example>
    public static Vec3Mm PositionAt(in Launch l, int k)
    {
        SimAssert.That(k >= 0 && k <= MaxAgeTicks, SimAssertCode.BallisticAgeOutOfRange);
        long kk = k;
        long px = l.Origin.X + Throw.RH(l.Vel.X * kk, TicksPerSecond);
        long py = l.Origin.Y + Throw.RH(l.Vel.Y * kk, TicksPerSecond) - Throw.RH(l.G * kk * kk, GravityDivisor);
        long pz = l.Origin.Z + Throw.RH(l.Vel.Z * kk, TicksPerSecond);
        return new Vec3Mm((int)px, (int)py, (int)pz);
    }
}
