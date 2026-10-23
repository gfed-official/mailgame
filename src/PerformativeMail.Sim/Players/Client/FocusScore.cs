using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// One candidate's focus geometry from the carrier's chest (GDD F4; ADR-0014 section 2): the chest distance <see cref="D"/>, the
/// horizontal distance <see cref="Dh"/> and the camera-forward cosine <see cref="CosPm"/> in permille.
/// </summary>
/// <param name="D">Chest-to-anchor distance, mm.</param>
/// <param name="Dh">Horizontal chest-to-anchor distance, mm.</param>
/// <param name="CosPm">Cosine to the horizontal camera forward, permille, in [−1000, 1000].</param>
/// <example><code>FocusGeometry g = FocusScore.Measure(in chest, in def.Point, fx, fz, settings.NearAxisMm, ref sink);</code></example>
public readonly record struct FocusGeometry(long D, long Dh, int CosPm);

/// <summary>
/// The F4 focus maths (GDD player-character-movement F4, P11; ADR-0014 section 2; TR-movement-002, TR-movement-011): integer on
/// <see langword="long"/>, pure, allocation free. Lower score wins. The occlusion term is story 015's.
/// </summary>
/// <example>
/// <code>
/// FocusScore.Forward(frame.LookYaw, out int fx, out int fz);
/// FocusGeometry g = FocusScore.Measure(in chest, in def.Point, fx, fz, s.NearAxisMm, ref sink);
/// bool ok = FocusScore.InRange(g.D, def.RangeMm, incumbent, s.IncumbentSlackMm) &amp;&amp; FocusScore.InCone(g.D, g.CosPm, frame.FocusCosMinPm, s.NearMm);
/// long score = FocusScore.Score(g.D, g.CosPm, def.RangeMm, def.Priority, incumbent, s);
/// </code>
/// </example>
public static class FocusScore
{
    /// <summary>Cosine 1 in permille.</summary>
    public const int CosOnePm = 1000;

    /// <summary>The Q10 length of the horizontal forward before truncation.</summary>
    public const int Q10One = 1024;

    /// <summary>The chest point: <c>feet + (0, chestMm, 0)</c>.</summary>
    /// <param name="feet">The carrier's feet, mm.</param>
    /// <param name="chestMm">The chest height (<c>player.chestMm</c>).</param>
    /// <returns>The chest, mm.</returns>
    /// <example><code>Vec3Mm chest = FocusScore.Chest(in feet, settings.ChestMm);</code></example>
    public static Vec3Mm Chest(in Vec3Mm feet, int chestMm) => new(feet.X, feet.Y + chestMm, feet.Z);

    /// <summary>
    /// The horizontal camera forward in Q10: <c>forward(θ) = (−sin θ, −cos θ)</c> in Q15 (ADR-0013 section 3, the
    /// <see cref="CarrierHeading.ToWorld"/> convention), arithmetic-shifted <c>&gt;&gt; 5</c>.
    /// </summary>
    /// <param name="lookYaw">The look yaw in BAM (<c>InputFrame.LookYaw</c>).</param>
    /// <param name="fwdQ10X">Forward X, Q10.</param>
    /// <param name="fwdQ10Z">Forward Z, Q10.</param>
    /// <example><code>FocusScore.Forward(0, out int fx, out int fz);   // (0, −1024): yaw 0 looks down −Z</code></example>
    public static void Forward(ushort lookYaw, out int fwdQ10X, out int fwdQ10Z)
    {
        fwdQ10X = -TrigQ15.Sin(lookYaw) >> 5;
        fwdQ10Z = -TrigQ15.Cos(lookYaw) >> 5;
    }

    /// <summary>
    /// <c>d = ISqrt(Δx² + Δy² + Δz²)</c>, <c>dh = ISqrt(Δx² + Δz²)</c> and
    /// <c>cosPm = dh &lt; nearAxisMm ? 1000 : MulDiv(dot(fwdQ10, Δxz), 1000, 1024·dh)</c>, clamped to [−1000, 1000] because the
    /// shifted forward is not exactly 1024 long (D6, Approved by Evan 2026-10-22 8:24 PM PT (Mail Lead chat), TD-164).
    /// </summary>
    /// <param name="chest">The chest point, mm.</param>
    /// <param name="point">The anchor point, mm.</param>
    /// <param name="fwdQ10X">Forward X, Q10 (<see cref="Forward"/>).</param>
    /// <param name="fwdQ10Z">Forward Z, Q10.</param>
    /// <param name="nearAxisMm">Below this horizontal distance the cosine is 1000 (<c>player.nearAxisMm</c>, 150).</param>
    /// <param name="sink">Arithmetic counters (a zero divisor when <paramref name="nearAxisMm"/> is 0 and dh is 0).</param>
    /// <returns>The geometry.</returns>
    /// <example><code>FocusGeometry g = FocusScore.Measure(in chest, in point, 0, -1024, 150, ref sink);</code></example>
    public static FocusGeometry Measure(in Vec3Mm chest, in Vec3Mm point, int fwdQ10X, int fwdQ10Z, int nearAxisMm, ref SimCounterSink sink)
    {
        long dx = (long)point.X - chest.X;
        long dy = (long)point.Y - chest.Y;
        long dz = (long)point.Z - chest.Z;
        long h2 = (dx * dx) + (dz * dz);
        long d = Fix.ISqrt((ulong)(h2 + (dy * dy)));
        long dh = Fix.ISqrt((ulong)h2);
        int cosPm = CosOnePm;
        if (dh >= nearAxisMm)
        {
            long dot = (fwdQ10X * dx) + (fwdQ10Z * dz);
            cosPm = (int)Math.Clamp(Fix.MulDiv(dot, CosOnePm, Q10One * dh, ref sink), -CosOnePm, CosOnePm);
        }

        return new FocusGeometry(d, dh, cosPm);
    }

    /// <summary>The range term of eligibility: <c>d ≤ rangeMm (+ incumbentSlackMm if incumbent)</c>.</summary>
    /// <param name="d">Chest distance, mm.</param>
    /// <param name="rangeMm">The anchor's reach.</param>
    /// <param name="incumbent">True for the focused anchor.</param>
    /// <param name="incumbentSlackMm"><c>player.incumbentSlackMm</c> (400).</param>
    /// <returns>True when in range.</returns>
    /// <example><code>bool keep = FocusScore.InRange(2900, 2500, incumbent: true, 400);   // true</code></example>
    public static bool InRange(long d, int rangeMm, bool incumbent, int incumbentSlackMm) =>
        d <= (long)rangeMm + (incumbent ? incumbentSlackMm : 0);

    /// <summary>The cone term of eligibility: <c>cosPm ≥ cosMinPm ∨ d ≤ nearMm</c>.</summary>
    /// <param name="d">Chest distance, mm.</param>
    /// <param name="cosPm">The cosine, permille.</param>
    /// <param name="cosMinPm">The cone threshold (<c>InputFrame.FocusCosMinPm</c>).</param>
    /// <param name="nearMm"><c>player.nearMm</c> (800): always in the cone inside this distance.</param>
    /// <returns>True when in the cone.</returns>
    /// <example><code>bool seen = FocusScore.InCone(1200, 635, 635, 800);   // true</code></example>
    public static bool InCone(long d, int cosPm, int cosMinPm, int nearMm) => cosPm >= cosMinPm || d <= nearMm;

    /// <summary>
    /// <c>score = (wDist·d·1000 / rangeMm + wAng·(1000 − cosPm)) / 1000 − 100·priority − (incumbent ? stickyPm : 0)</c>, floor
    /// division of non-negative terms (the GDD formula literally). Lower wins.
    /// </summary>
    /// <param name="d">Chest distance, mm (non-negative).</param>
    /// <param name="cosPm">The cosine, permille, in [−1000, 1000].</param>
    /// <param name="rangeMm">The anchor's reach (positive; the registry stores the default for 0).</param>
    /// <param name="priority">The anchor's priority, 0 to 3.</param>
    /// <param name="incumbent">True for the focused anchor.</param>
    /// <param name="settings">The weights (<c>WDist</c>, <c>WAng</c>, <c>StickyPm</c>).</param>
    /// <returns>The score.</returns>
    /// <example><code>long s = FocusScore.Score(1500, 950, 2500, 0, false, settings);   // 270</code></example>
    public static long Score(long d, int cosPm, int rangeMm, byte priority, bool incumbent, PlayerSettings settings)
    {
        long distance = (long)settings.WDist * d * CosOnePm / rangeMm;
        long angle = (long)settings.WAng * (CosOnePm - cosPm);
        return ((distance + angle) / CosOnePm) - (100L * priority) - (incumbent ? settings.StickyPm : 0);
    }
}
