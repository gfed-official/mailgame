using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// F1, the launch (ADR-0015 sections 2 and 3; GDD <c>design/gdd/throwing-catching.md</c> T4 and F1; TR-throwing-001,
/// TR-throwing-004). The one implementation the host (at the apply tick), the aim preview and every peer call. Integer
/// only, every product widened to <see cref="long"/> before the first multiply, every rounding through
/// <see cref="Throw.RH"/>. No RNG and no spread term (TR-throwing-002). Pure, allocation-free, thread-safe.
/// <para>GDD F1:</para>
/// <code>
/// n       = isqrt(dx² + dy² + dz²)                      host bounds: 16000 ≤ n ≤ 16768, -876 ≤ FloorDiv(1000·dy, n) ≤ 980
/// speedPm = isqrt(ThrowRangePermille × 1000)
/// v       = RH(baseSpeed[class] × speedPm, 1000)
/// vel     = (RH(v·dx, n), RH(v·dy, n), RH(v·dz, n))
/// h       = isqrt(dx² + dz²)                            (h &lt; 2048: body-yaw basis)
/// origin  = feet + (RH(fwdOffMm·dx, h) + RH(side·sideOffMm·(−dz), h),
///                   handHeightMm,
///                   RH(fwdOffMm·dz, h) + RH(side·sideOffMm·dx, h))
/// </code>
/// If the chest-to-origin segment is blocked (<see cref="IChestOriginProbe"/>), the origin is the chest point
/// <c>feet + (0, 1000, 0)</c> and <c>originFromChest</c> is true.
/// </summary>
/// <example>
/// <code>
/// var input = new LaunchInput(new Vec3Mm(10_000, 0, 20_000), 0, 3406, -16_026, 1, WeightClass.Light, 1000, 0, settings);
/// bool ok = LaunchMath.Compute(in input, out Launch l, out bool fromChest);
/// // ok, l.Vel == (0, 2911, -13 695), l.Origin == (10 250, 1200, 19 700), fromChest == false
/// </code>
/// </example>
public static class LaunchMath
{
    /// <summary>Smallest accepted direction length <c>n</c> (ADR-0015 section 3 host bound).</summary>
    public const int MinDirLength = 16_000;

    /// <summary>Largest accepted direction length <c>n</c> (ADR-0015 section 3 host bound).</summary>
    public const int MaxDirLength = 16_768;

    /// <summary>Largest accepted elevation <c>FloorDiv(1000·dy, n)</c>, permille of the unit direction (ADR-0015 section 3).</summary>
    public const int MaxElevationPm = 980;

    /// <summary>Smallest accepted elevation <c>FloorDiv(1000·dy, n)</c>, permille of the unit direction (ADR-0015 section 3).</summary>
    public const int MinElevationPm = -876;

    /// <summary>Below this horizontal length <c>h</c> the origin uses the body-yaw basis (ADR-0015 section 3 defence branch).</summary>
    public const int MinHorizontalLength = 2048;

    /// <summary>Height of the chest point above the feet, mm (ADR-0015 section 3: the chest-to-origin probe start).</summary>
    public const int ChestHeightMm = 1000;

    /// <summary>
    /// Validates the direction against the host bounds and builds the launch (GDD F1, see the type remarks).
    /// Returns false (<c>bad_direction</c>) when <c>n</c> or the elevation is out of bounds, when the weight class has no
    /// base speed (<see cref="ThrowSettings.BaseSpeed"/> returns 0), when <c>ThrowRangePermille &lt;= 0</c>, when
    /// <c>Side</c> is not +1 or -1 (the wire field is client-supplied; any other value would move the hand origin by
    /// <c>side × sideOffMm</c>), or when the input carries no settings; <paramref name="l"/> is then <c>default</c>.
    /// </summary>
    /// <param name="i">The launch input.</param>
    /// <param name="l">The launch, when accepted.</param>
    /// <param name="originFromChest">True when the chest probe reported the hand origin blocked and the origin is the chest point.</param>
    /// <returns>True when the direction passed the host bounds and the launch was built.</returns>
    /// <example>
    /// <code>if (!LaunchMath.Compute(in input, out Launch l, out bool fromChest)) { /* reply bad_direction */ }</code>
    /// </example>
    public static bool Compute(in LaunchInput i, out Launch l, out bool originFromChest)
    {
        l = default;
        originFromChest = false;
        ThrowSettings s = i.Settings;
        if (s is null || i.ThrowRangePermille <= 0 || (i.Side != 1 && i.Side != -1))
        {
            return false;
        }

        int baseSpeed = s.BaseSpeed(i.Class);
        if (baseSpeed <= 0)
        {
            return false;
        }

        long n = Fix.ISqrt((ulong)DirLengthSquared(i.Dx, i.Dy, i.Dz));
        if (n < MinDirLength || n > MaxDirLength)
        {
            return false;
        }

        var sink = default(SimCounterSink); // n >= 16000 here: the divisor is never 0 or -1, nothing to count
        long elevationPm = Fix.FloorDiv(1000L * i.Dy, n, ref sink);
        if (elevationPm > MaxElevationPm || elevationPm < MinElevationPm)
        {
            return false;
        }

        long speedPm = Fix.ISqrt((ulong)i.ThrowRangePermille * 1000UL);
        long v = Throw.RH(baseSpeed * speedPm, 1000);
        var vel = new Vec3Mm((int)Throw.RH(v * i.Dx, n), (int)Throw.RH(v * i.Dy, n), (int)Throw.RH(v * i.Dz, n));

        Vec3Mm origin = OriginFor(in i.Feet, i.Dx, i.Dz, i.Side, i.BodyYawBam, s);
        var chest = new Vec3Mm(i.Feet.X, i.Feet.Y + ChestHeightMm, i.Feet.Z);
        if (i.ChestProbe is not null && i.ChestProbe.IsBlocked(in chest, in origin))
        {
            origin = chest;
            originFromChest = true;
        }

        l = new Launch(origin, vel, s.G);
        return true;
    }

    /// <summary>
    /// <c>dx² + dy² + dz²</c> with every component widened to <see cref="long"/> before squaring, so the int16 extremes
    /// cannot wrap (int32 squaring of (-32768, 32767, 32767) would go negative). The test seam of TC-F1-1 e.
    /// </summary>
    /// <param name="dx">Direction x.</param>
    /// <param name="dy">Direction y.</param>
    /// <param name="dz">Direction z.</param>
    /// <returns>The squared length, 0 to 3 221 225 472.</returns>
    internal static long DirLengthSquared(short dx, short dy, short dz)
    {
        long x = dx;
        long y = dy;
        long z = dz;
        return (x * x) + (y * y) + (z * z);
    }

    /// <summary>
    /// The F1 hand origin (the one origin routine <see cref="Compute"/> calls; it applies no host bounds). The horizontal
    /// basis is <c>(dx, dz)</c> with <c>h = isqrt(dx² + dz²)</c>; when <c>h &lt; 2048</c> (unreachable for a host-validated
    /// direction) it is the ADR-0013 section 3 body forward <c>(−sin, −cos)</c> of <paramref name="bodyYawBam"/> in Q15
    /// (<see cref="TrigQ15"/>) with <c>h = TrigQ15.One</c> (32 767).
    /// </summary>
    /// <param name="feet">The thrower's feet, mm.</param>
    /// <param name="dx">Direction x.</param>
    /// <param name="dz">Direction z.</param>
    /// <param name="side">+1 right-handed, -1 left-handed.</param>
    /// <param name="bodyYawBam">The body yaw in BAM (defence branch only).</param>
    /// <param name="s">The throw knobs (hand height, forward and side offsets).</param>
    /// <returns>The hand origin, mm.</returns>
    internal static Vec3Mm OriginFor(in Vec3Mm feet, short dx, short dz, sbyte side, ushort bodyYawBam, ThrowSettings s)
    {
        long bx = dx;
        long bz = dz;
        long h = Fix.ISqrt((ulong)((bx * bx) + (bz * bz)));
        if (h < MinHorizontalLength)
        {
            bx = -TrigQ15.Sin(bodyYawBam);
            bz = -TrigQ15.Cos(bodyYawBam);
            h = TrigQ15.One;
        }

        long fwd = s.FwdOffMm;
        long sideOff = (long)side * s.SideOffMm;
        long x = feet.X + Throw.RH(fwd * bx, h) + Throw.RH(sideOff * -bz, h);
        long z = feet.Z + Throw.RH(fwd * bz, h) + Throw.RH(sideOff * bx, h);
        return new Vec3Mm((int)x, feet.Y + s.HandHeightMm, (int)z);
    }
}
