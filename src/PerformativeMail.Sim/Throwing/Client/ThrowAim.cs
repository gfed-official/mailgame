using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The throw aim quantiser (GDD <c>design/gdd/throwing-catching.md</c> T3 "Launch direction" and F1 <c>dir</c>; throwing-catching story
/// 004): the camera aim becomes the int16 x3 direction the preview draws and <c>ThrowRelease</c> sends, quantised once. The pitch is
/// clamped to −60°…+70° and <c>loftDeg</c> is added (elevation −54°…+76° at the default 6°); the yaw uses the Player Character convention
/// (ADR-0013 section 3: yaw 0 faces −Z, forward is <c>(−sin, −cos)</c>). The vector has length <see cref="Length"/> (2^14), so every
/// result passes the F1 host bounds (<see cref="LaunchMath.ValidateDir"/>). Sine and cosine come from the committed Q15 table
/// (<see cref="TrigQ15"/>), linearly interpolated between its 16-BAM entries so the elevation is exact to well under 0.01°; every
/// rounding is <see cref="Throw.RH"/>. Pure, integer-only, allocation-free, thread-safe.
/// </summary>
/// <example>
/// <code>
/// ThrowAim.Quantise(0, 1092, 6000, out short dx, out short dy, out short dz);   // pitch +6°, facing −Z: (0, 3406, −16 026)
/// </code>
/// </example>
public static class ThrowAim
{
    /// <summary>The length of every quantised direction (2^14; inside the host bounds 16 000 to 16 768).</summary>
    public const int Length = 16_384;

    /// <summary>The lowest camera pitch that counts, BAM: −60°.</summary>
    public const short MinPitchBam = -10_923;

    /// <summary>The highest camera pitch that counts, BAM: +70°.</summary>
    public const short MaxPitchBam = 12_743;

    private const long BamPerTurn = 65_536;
    private const long MilliDegPerTurn = 360_000;
    private const long Q15Squared = (long)TrigQ15.One * TrigQ15.One;

    /// <summary>Quantises the camera aim to the throw direction (see the type remarks).</summary>
    /// <param name="yawBam">The camera yaw, BAM (0 faces −Z).</param>
    /// <param name="pitchBam">The camera pitch, signed BAM (positive looks up); clamped to <see cref="MinPitchBam"/>…<see cref="MaxPitchBam"/>.</param>
    /// <param name="loftMilliDeg">The added elevation, milli-degrees (<c>throw.loftDeg</c>, default 6000).</param>
    /// <param name="dx">Direction x.</param>
    /// <param name="dy">Direction y (up).</param>
    /// <param name="dz">Direction z.</param>
    public static void Quantise(ushort yawBam, short pitchBam, int loftMilliDeg, out short dx, out short dy, out short dz)
    {
        int pitch = pitchBam < MinPitchBam ? MinPitchBam : pitchBam > MaxPitchBam ? MaxPitchBam : pitchBam;
        ushort elevation = unchecked((ushort)(pitch + (int)Throw.RH(loftMilliDeg * BamPerTurn, MilliDegPerTurn)));
        long sinE = Sin(elevation);
        long cosE = Sin(Bam.Add(elevation, Bam.Quarter));
        long sinY = Sin(yawBam);
        long cosY = Sin(Bam.Add(yawBam, Bam.Quarter));
        dy = (short)Throw.RH(Length * sinE, TrigQ15.One);
        dx = (short)Throw.RH(-Length * sinY * cosE, Q15Squared);
        dz = (short)Throw.RH(-Length * cosY * cosE, Q15Squared);
    }

    // Q15 sine with linear interpolation between the table's entries (TrigQ15 ignores the low 4 bits of the angle).
    private static long Sin(ushort bam)
    {
        ushort lo = (ushort)(bam & 0xFFF0);
        long s0 = TrigQ15.Sin(lo);
        long s1 = TrigQ15.Sin(unchecked((ushort)(lo + 16)));
        return s0 + Throw.RH((s1 - s0) * (bam & 0xF), 16);
    }
}
