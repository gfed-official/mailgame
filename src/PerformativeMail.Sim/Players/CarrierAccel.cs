using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// F2 acceleration and deceleration (GDD F2; ADR-0013 section 2). One clamped horizontal vector per tick:
/// <c>v' = v + clampLength(vTarget - v, dvMax)</c> with <c>dvMax = ceil(max(vRef, |v|) / (airborne ? Tair : T))</c>.
/// Lengths used to scale a step are rounded up so a step never exceeds <c>dvMax</c>; the result is finally limited to
/// <c>max(vRef, |v|)</c>, so own input never raises the speed above the cap or the previous speed.
/// </summary>
public static class CarrierAccel
{
    /// <summary>Integer length of (x, z), rounded down.</summary>
    /// <param name="x">X component.</param>
    /// <param name="z">Z component.</param>
    /// <returns>floor(sqrt(x^2 + z^2)).</returns>
    public static int Length(int x, int z) => (int)Fix.ISqrt((ulong)(((long)x * x) + ((long)z * z)));

    /// <summary>Integer square root rounded up.</summary>
    /// <param name="n">A non-negative value.</param>
    /// <returns>ceil(sqrt(n)).</returns>
    public static long CeilSqrt(long n)
    {
        long r = Fix.ISqrt((ulong)Math.Max(n, 0));
        return (r * r) < n ? r + 1 : r;
    }

    /// <summary>The per-tick speed-change limit in mm/s.</summary>
    /// <param name="vRef">The speed cap of the current gait.</param>
    /// <param name="speedMmS">The current own horizontal speed.</param>
    /// <param name="baseTicks">T: ticksAccel with input, ticksDecel without.</param>
    /// <param name="airborne">Whether the carrier is airborne.</param>
    /// <param name="airControlPm">Air control permille.</param>
    /// <param name="sink">Arithmetic counters.</param>
    /// <returns>dvMax.</returns>
    public static int DvMax(int vRef, int speedMmS, int baseTicks, bool airborne, int airControlPm, ref SimCounterSink sink)
    {
        long ticks = baseTicks;
        if (airborne)
        {
            ticks = Fix.CeilDiv(ticks * CarrierSpeed.Unit, airControlPm, ref sink);
        }

        ticks = Math.Max(ticks, 1);
        return (int)Fix.CeilDiv(Math.Max(vRef, speedMmS), ticks, ref sink);
    }

    /// <summary>Applies one tick of F2 to the carrier's own horizontal velocity.</summary>
    /// <param name="vx">Velocity X, mm/s.</param>
    /// <param name="vz">Velocity Z, mm/s.</param>
    /// <param name="targetX">Target velocity X.</param>
    /// <param name="targetZ">Target velocity Z.</param>
    /// <param name="vRef">The speed cap of the current gait (F1).</param>
    /// <param name="hasInput">Whether a move input is present (selects ticksAccel or ticksDecel).</param>
    /// <param name="airborne">Whether the carrier is airborne.</param>
    /// <param name="settings">Player settings.</param>
    /// <param name="sink">Arithmetic counters.</param>
    public static void Apply(
        ref int vx, ref int vz, int targetX, int targetZ, int vRef, bool hasInput, bool airborne, PlayerSettings settings, ref SimCounterSink sink)
    {
        int speed = Length(vx, vz);
        int dvMax = DvMax(vRef, speed, hasInput ? settings.TicksAccel : settings.TicksDecel, airborne, settings.AirControlPm, ref sink);
        long dx = (long)targetX - vx;
        long dz = (long)targetZ - vz;
        long d2 = (dx * dx) + (dz * dz);
        long nx;
        long nz;
        if (d2 <= (long)dvMax * dvMax)
        {
            nx = targetX;
            nz = targetZ;
        }
        else
        {
            long len = CeilSqrt(d2);
            nx = vx + Fix.MulDiv(dx, dvMax, len, ref sink);
            nz = vz + Fix.MulDiv(dz, dvMax, len, ref sink);
        }

        long limit = Math.Max(vRef, speed);
        long n2 = (nx * nx) + (nz * nz);
        if (n2 > limit * limit)
        {
            long len = CeilSqrt(n2);
            nx = Fix.MulDiv(nx, limit, len, ref sink);
            nz = Fix.MulDiv(nz, limit, len, ref sink);
        }

        vx = (int)nx;
        vz = (int)nz;
    }
}
