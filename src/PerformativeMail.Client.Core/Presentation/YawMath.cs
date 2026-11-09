using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Presentation;

/// <summary>
/// Integer BAM yaw helpers for Presentation (player-character story 022, ADR-0013 section 3 and its engine review note 1): the
/// shortest signed arc between two yaws (the delta wrapped as <see cref="short"/>), a by-<c>AlphaPermille</c> blend along that arc, and
/// the unchecked <c>&amp; 0xFFFF</c> wrap of a rounded yaw (negative yaws wrap). The radians side (<c>Rotation.Y = bam × 2π / 65536</c>
/// with no sign flip, and camera yaw → BAM rounding half away from zero) is <c>YawRadians</c> in the Godot project, because Client.Core
/// carries no floats. Pure and allocation-free.
/// </summary>
/// <example>
/// <code>
/// short arc = YawMath.ShortestArc(65000, 500);        // 1036: through 0, not the long way round
/// ushort mid = YawMath.Blend(65000, 500, 500);        // 65518 (65000 + 518)
/// ushort bam = YawMath.Wrap(-16384);                  // 49152
/// </code>
/// </example>
public static class YawMath
{
    /// <summary>The signed shortest arc from <paramref name="from"/> to <paramref name="to"/>, BAM: <c>(short)unchecked((ushort)(to − from))</c>.</summary>
    /// <param name="from">Start yaw, BAM.</param>
    /// <param name="to">End yaw, BAM.</param>
    /// <returns>The arc in [−32768, 32767]; exactly half a turn is −32768.</returns>
    public static short ShortestArc(ushort from, ushort to) => unchecked((short)(ushort)(to - from));

    /// <summary>Blends from <paramref name="from"/> toward <paramref name="to"/> along the shortest arc by <paramref name="alphaPermille"/> (clamped to 0..1000), rounding half away from zero.</summary>
    /// <param name="from">Yaw at alpha 0, BAM.</param>
    /// <param name="to">Yaw at alpha 1000, BAM.</param>
    /// <param name="alphaPermille">The blend fraction, permille.</param>
    /// <returns>The blended yaw, BAM.</returns>
    public static ushort Blend(ushort from, ushort to, int alphaPermille)
    {
        int alpha = Math.Clamp(alphaPermille, 0, 1000);
        var sink = default(SimCounterSink); // |arc × alpha| ≤ 32768000 and the divisor is 1000: nothing to count
        return Wrap(from + Fix.MulDivRH(ShortestArc(from, to), alpha, 1000, ref sink));
    }

    /// <summary>Wraps a rounded yaw onto the BAM circle with an unchecked <c>&amp; 0xFFFF</c>; negative yaws wrap (−1 → 65535).</summary>
    /// <param name="bam">The rounded yaw in BAM units, any range.</param>
    /// <returns>The yaw, BAM.</returns>
    public static ushort Wrap(long bam) => unchecked((ushort)(bam & 0xFFFF));
}
