using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Throwing's one rounding helper (ADR-0015 section 2; GDD <c>design/gdd/throwing-catching.md</c> F1/F2; TR-throwing-004).
/// <c>RH(n, d) = FloorDiv(2n + d, 2d)</c> for <c>d &gt; 0</c>: round half toward positive infinity, built on the floor
/// division of <see cref="Fix.FloorDiv(long, long, ref SimCounterSink)"/>. It is deliberately <b>not</b>
/// <see cref="Fix.MulDivRH"/> (half away from zero): the GDD golden <c>p(1).z = 19700 + RH(-13695 / 30) = 19244</c> needs
/// the floor-based form. Every rounding in <c>Sim/Throwing/</c> goes through this method; C#'s truncating <c>/</c> is never
/// applied to a signed numerator.
/// <para>
/// Sink (Mail Lead, decision log 2026-10-17): the ADR-0015 Key Interface is sink-free, so a local <c>default</c>
/// <see cref="SimCounterSink"/> is passed to <c>Fix.FloorDiv</c>. For <c>d &gt; 0</c> the divisor <c>2d</c> is never 0 or -1,
/// so no counter can fire on valid input; <c>d &gt; 0</c> is asserted under <c>SIM_ASSERT</c>
/// (<see cref="SimAssertCode.ThrowRhDivisorNotPositive"/>). Callers keep <c>|2n + d|</c> inside <see cref="long"/>
/// (every call site in Throwing is far below it). Pure, allocation-free, thread-safe.
/// </para>
/// <para>
/// Namespace: the folder is <c>Sim/Throwing/</c> but the namespace is <c>PerformativeMail.Sim.ThrowingCatching</c>, because a
/// <c>PerformativeMail.Sim.Throwing</c> namespace shadows <c>Content.Rules.Throwing</c> (the same collision and fix as
/// <c>Sim/Mail/</c> → <c>PerformativeMail.Sim.MailDestinations</c>, TD-074).
/// </para>
/// </summary>
/// <example>
/// <code>
/// long a = Throw.RH(-1, 2);        // 0   (-0.5 rounds up)
/// long b = Throw.RH(-3, 2);        // -1  (-1.5 rounds up)
/// long c = Throw.RH(-13695, 30);   // -456 (-456.5 rounds up)
/// </code>
/// </example>
public static class Throw
{
    /// <summary><c>n / d</c> rounded half toward positive infinity: <c>FloorDiv(2n + d, 2d)</c>.</summary>
    /// <param name="n">The numerator (any sign).</param>
    /// <param name="d">The divisor; must be positive (asserted under <c>SIM_ASSERT</c>).</param>
    /// <returns>The rounded quotient.</returns>
    /// <example><code>long px = o.X + Throw.RH((long)v.X * k, 30);</code></example>
    public static long RH(long n, long d)
    {
        SimAssert.That(d > 0, SimAssertCode.ThrowRhDivisorNotPositive);
        var sink = default(SimCounterSink); // 2d is never 0 or -1 for d > 0: nothing to count (decision log 2026-10-17)
        return Fix.FloorDiv((2 * n) + d, 2 * d, ref sink);
    }
}
