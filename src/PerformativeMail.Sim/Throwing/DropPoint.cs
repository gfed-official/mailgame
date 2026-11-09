using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// GDD F8, the Reject / remainder drop point of a mailbox landing (ADR-0015 sections 2 and 5, <c>MAILBOX_REJECT</c> and the wallet-floor
/// remainder of <c>MAILBOX_MISDELIVER</c>; throwing-catching story 012). <c>u = (vx, vz) / |vxz|</c> in permille through
/// <see cref="Throw.RH"/> (<c>|vxz| = ISqrt(vx² + vz²)</c>); <c>D = C − RH(dropPushbackMm × u, 1000)</c>; then 100 mm steps along −u until
/// <c>max(|D.x − V.x|, |D.z − V.z|) ≥ 600</c> (V = the catch volume's centre). The point is then clamped 1000 mm inside the map and
/// dropped onto the surface below it (<see cref="SurfaceBelow"/>), as every other landing is (story 011).
/// <para>
/// Greybox simplifications (simplify table, tc-012): no walkable snap within 1000 mm and no mailbox-base-front-point fallback; for
/// <c>|vxz| &lt; 1000</c> the push runs outward from V along the dominant axis of <c>C − V</c> (+z on a tie at the centre), standing in for
/// the lot's front normal, which Throwing has no read of.
/// </para>
/// Pure, static, allocation-free.
/// </summary>
/// <example>
/// <code>
/// // C (10250, 944, 10500), v (0, 2911, −13695), pushback 500, volume centred on (10250, z 10000): drop (10250, terrain, 11000)
/// Vec3Mm drop = DropPoint.F8(world, in c, in vel, in volume, 500, out bool onWater);
/// </code>
/// </example>
public static class DropPoint
{
    /// <summary>The step along −u, mm.</summary>
    public const int StepMm = 100;

    /// <summary>The horizontal clearance from the volume centre the point must reach (Chebyshev), mm.</summary>
    public const int ClearMm = 600;

    /// <summary>Speeds below this horizontal magnitude, mm/s, have no usable flight direction.</summary>
    public const int MinHorizontalSpeed = 1000;

    private const int MaxSteps = 32;   // |u| ≈ 1000 ‰ moves ≥ 70 mm a step on its larger axis: 9 steps leave any 1 m volume

    /// <summary>The drop point of a bounce off the catch volume <paramref name="volume"/>.</summary>
    /// <param name="world">The world (map bounds, water and terrain).</param>
    /// <param name="c">The volume entry point (the winning contact), mm.</param>
    /// <param name="vel">The flight's launch velocity (its horizontal part never changes).</param>
    /// <param name="volume">The mailbox catch volume.</param>
    /// <param name="pushbackMm">The pushback, <see cref="ThrowSettings.DropPushbackMm"/>.</param>
    /// <param name="onWater">True when the point is over water (the item floats at y 0).</param>
    /// <returns>The drop point on the surface.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public static Vec3Mm F8(IWorldQuery world, in Vec3Mm c, in Vec3Mm vel, in AabbMm volume, int pushbackMm, out bool onWater)
    {
        ArgumentNullException.ThrowIfNull(world);

        // Twice the centre, so the clearance test is exact: max(|2D − (min + max)|) ≥ 2 × 600.
        long sumX = (long)volume.Min.X + volume.Max.X;
        long sumZ = (long)volume.Min.Z + volume.Max.Z;
        long vx = vel.X;
        long vz = vel.Z;
        long len = Fix.ISqrt((ulong)((vx * vx) + (vz * vz)));
        long ux;
        long uz;
        if (len >= MinHorizontalSpeed)
        {
            ux = Throw.RH(vx * 1000, len);
            uz = Throw.RH(vz * 1000, len);
        }
        else
        {
            long ox = (2L * c.X) - sumX;
            long oz = (2L * c.Z) - sumZ;
            bool alongX = Abs(ox) > Abs(oz);
            ux = alongX ? (ox > 0 ? -1000 : 1000) : 0;
            uz = alongX ? 0 : (oz >= 0 ? -1000 : 1000);
        }

        long dx = c.X - Throw.RH(pushbackMm * ux, 1000);
        long dz = c.Z - Throw.RH(pushbackMm * uz, 1000);
        long stepX = Throw.RH(StepMm * ux, 1000);
        long stepZ = Throw.RH(StepMm * uz, 1000);
        for (int i = 0; i < MaxSteps && Abs((2 * dx) - sumX) < 2 * ClearMm && Abs((2 * dz) - sumZ) < 2 * ClearMm; i++)
        {
            dx -= stepX;
            dz -= stepZ;
        }

        var d = new Vec3Mm((int)dx, c.Y, (int)dz);
        Vec3Mm onMap = world.ClampInsideMap(in d, TerminalResolver.MapInsetMm);
        onWater = SurfaceBelow.At(world, in onMap, out Vec3Mm drop) == ReceiverKind.Water;
        return drop;
    }

    private static long Abs(long v) => v < 0 ? -v : v;
}
