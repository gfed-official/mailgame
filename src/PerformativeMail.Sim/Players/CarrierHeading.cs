using PerformativeMail.Sim.Hosting;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// Camera-relative move direction and body yaw (GDD P3/F2; ADR-0013 section 3). Camera yaw theta maps a Q15 input (mx right, my
/// forward) to <c>wx = mx*c - my*s</c>, <c>wz = -mx*s - my*c</c>; forward(theta) = (-sin theta, -cos theta). Body yaw is
/// <c>Atan2Bam(-wx, -wz)</c> and turns by at most <see cref="YawStep"/> per tick along the shortest arc.
/// </summary>
public static class CarrierHeading
{
    /// <summary>Largest Q15 input magnitude (1.0).</summary>
    public const int One = TrigQ15.One;

    /// <summary>Q15 squared, the divisor that turns a Q15 x Q15 product into a unit fraction.</summary>
    public const long OneSquared = (long)One * One;

    /// <summary><c>floor(65536 x turnDegPerSec / (360 x 30))</c> BAM units per tick (4369 at 720 deg/s).</summary>
    /// <param name="turnDegPerSec">Turn rate in degrees per second.</param>
    /// <returns>The per-tick yaw step.</returns>
    public static int YawStep(int turnDegPerSec)
    {
        SimCounterSink none = default;
        return (int)Fix.FloorDiv(65536L * turnDegPerSec, 360L * TickRate.Hz, ref none);
    }

    /// <summary>Clamps each axis to +-1.0 and then the vector length to 1.0 (a diagonal is never faster).</summary>
    /// <param name="mx">Input X (Q15).</param>
    /// <param name="my">Input Y (Q15).</param>
    /// <param name="sink">Arithmetic counters.</param>
    public static void ClampInput(ref int mx, ref int my, ref SimCounterSink sink)
    {
        mx = Math.Clamp(mx, -One, One);
        my = Math.Clamp(my, -One, One);
        long n2 = ((long)mx * mx) + ((long)my * my);
        if (n2 > OneSquared)
        {
            long len = CarrierAccel.CeilSqrt(n2);
            mx = (int)Fix.MulDiv(mx, One, len, ref sink);
            my = (int)Fix.MulDiv(my, One, len, ref sink);
        }
    }

    /// <summary>Rotates a Q15 input vector by camera yaw into world space (scale Q15 x Q15).</summary>
    /// <param name="mx">Input X, already clamped.</param>
    /// <param name="my">Input forward, already clamped.</param>
    /// <param name="cameraYaw">Camera yaw in BAM.</param>
    /// <param name="wx">World X component.</param>
    /// <param name="wz">World Z component.</param>
    public static void ToWorld(int mx, int my, ushort cameraYaw, out long wx, out long wz)
    {
        long s = TrigQ15.Sin(cameraYaw);
        long c = TrigQ15.Cos(cameraYaw);
        wx = (mx * c) - (my * s);
        wz = (-mx * s) - (my * c);
    }

    /// <summary><c>vTarget = w x vRef / 32767^2</c>, limited to length <paramref name="vRef"/>.</summary>
    /// <param name="wx">World X (Q15 x Q15).</param>
    /// <param name="wz">World Z (Q15 x Q15).</param>
    /// <param name="vRef">The gait speed cap in mm/s.</param>
    /// <param name="tx">Target velocity X.</param>
    /// <param name="tz">Target velocity Z.</param>
    /// <param name="sink">Arithmetic counters.</param>
    public static void TargetVelocity(long wx, long wz, int vRef, out int tx, out int tz, ref SimCounterSink sink)
    {
        long x = Fix.MulDiv(wx, vRef, OneSquared, ref sink);
        long z = Fix.MulDiv(wz, vRef, OneSquared, ref sink);
        long t2 = (x * x) + (z * z);
        if (t2 > (long)vRef * vRef)
        {
            long len = CarrierAccel.CeilSqrt(t2);
            x = Fix.MulDiv(x, vRef, len, ref sink);
            z = Fix.MulDiv(z, vRef, len, ref sink);
        }

        tx = (int)x;
        tz = (int)z;
    }

    /// <summary>Body yaw that faces the world move vector, or <paramref name="current"/> when it is zero.</summary>
    /// <param name="wx">World X.</param>
    /// <param name="wz">World Z.</param>
    /// <param name="current">The current yaw.</param>
    /// <returns>The target yaw in BAM.</returns>
    public static ushort TargetYaw(long wx, long wz, ushort current) =>
        wx == 0 && wz == 0 ? current : TrigQ15.Atan2Bam(-wx, -wz);

    /// <summary>Advances <paramref name="yaw"/> toward <paramref name="target"/> by at most <paramref name="yawStep"/>, shortest arc.</summary>
    /// <param name="yaw">Current yaw.</param>
    /// <param name="target">Target yaw.</param>
    /// <param name="yawStep">Largest step in BAM units.</param>
    /// <returns>The new yaw.</returns>
    public static ushort TurnToward(ushort yaw, ushort target, int yawStep)
    {
        int diff = unchecked((short)Bam.Sub(target, yaw));
        if (diff >= -yawStep && diff <= yawStep)
        {
            return target;
        }

        return Bam.Add(yaw, unchecked((ushort)(diff > 0 ? yawStep : -yawStep)));
    }
}
