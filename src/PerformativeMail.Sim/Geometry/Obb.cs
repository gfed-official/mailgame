using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Geometry;

/// <summary>
/// Oriented-box helpers (ADR-0013 section 6; TR-movement-001): rotates world points and segments into box-local
/// coordinates and back with Q15 <see cref="TrigQ15"/> so a yawed vehicle box can be tested with the axis-aligned
/// primitives (<see cref="SegmentIntervals.Slab"/> against a box centred on the origin). The box rotates about +Y only.
/// <para>
/// Convention (ADR-0013, Sim-wide): axes +X right, +Y up, -Z forward at yaw 0; for BAM yaw <c>theta</c>,
/// <c>forward = (-sin, -cos)</c> and <c>right = (cos, -sin)</c> in (x, z); positive yaw turns counter-clockwise seen
/// from +Y. The box's local +X is therefore the world vector <c>(cos, -sin)</c> and its local +Z the world vector
/// <c>(sin, cos)</c>. With <c>d = world - centre</c>:
/// <c>localX = RH(dx * cos - dz * sin, 1, 32767)</c>, <c>localZ = RH(dx * sin + dz * cos, 1, 32767)</c>,
/// <c>localY = dy</c>. At yaw 0 this is the identity; at yaw 90 degrees (BAM 16384, sin 32767, cos 0) it is exactly
/// <c>(dx, dz) -&gt; (-dz, dx)</c> (a point north of the box, <c>dz &lt; 0</c>, lies on its local +X axis).
/// Each coordinate is one <see cref="Fix.MulDivRH"/> of an exact int64 sum (one rounding per formula); 32767 is
/// <see cref="TrigQ15.One"/>. The BAM low 4 bits are ignored by the Q15 table, as everywhere in the Sim.
/// </para>
/// Precondition: every input point and centre lies within the map bound (|coordinate| at most 1 000 000 mm, offsets from the
/// centre at most a few tens of metres). The rotated result is an <see cref="int"/>-cast <see cref="Fix.MulDivRH"/> value, so
/// a coordinate beyond roughly 2^31 mm would wrap in unchecked builds (and throw in checked ones); callers clamp first.
/// Pure, allocation-free, non-throwing within that precondition, stateless.
/// </summary>
/// <example>
/// <code>
/// Vec3Mm local = Obb.ToLocal(new Vec3Mm(1000, 5, 2000), new Vec3Mm(0, 0, 0), 16_384);   // (-2000, 5, 1000)
/// Vec3Mm back = Obb.ToWorld(in local, new Vec3Mm(0, 0, 0), 16_384);                       // (1000, 5, 2000)
/// </code>
/// </example>
public static class Obb
{
    /// <summary>Rotates a world point into the box-local frame centred at <paramref name="centre"/>.</summary>
    /// <param name="worldPoint">The point in world mm.</param>
    /// <param name="centre">The box centre in world mm (the local origin).</param>
    /// <param name="yawBam">The box yaw, 65 536 = 360 degrees.</param>
    /// <returns>The point in box-local mm (X right, Y up, Z back of the box).</returns>
    /// <example>
    /// <code>Vec3Mm l = Obb.ToLocal(in p, in centre, vehicleYaw);</code>
    /// </example>
    public static Vec3Mm ToLocal(in Vec3Mm worldPoint, in Vec3Mm centre, ushort yawBam)
    {
        long s = TrigQ15.Sin(yawBam);
        long c = TrigQ15.Cos(yawBam);
        long dx = (long)worldPoint.X - centre.X;
        long dy = (long)worldPoint.Y - centre.Y;
        long dz = (long)worldPoint.Z - centre.Z;
        return Rotate(dx, dy, dz, c, s);
    }

    /// <summary>Rotates a box-local point back into world coordinates (the inverse of <see cref="ToLocal"/>).</summary>
    /// <param name="localPoint">The point in box-local mm.</param>
    /// <param name="centre">The box centre in world mm.</param>
    /// <param name="yawBam">The box yaw, 65 536 = 360 degrees.</param>
    /// <returns>The point in world mm; within 1 mm of the original after a round trip for offsets up to 20 m.</returns>
    /// <example>
    /// <code>Vec3Mm w = Obb.ToWorld(in l, in centre, vehicleYaw);</code>
    /// </example>
    public static Vec3Mm ToWorld(in Vec3Mm localPoint, in Vec3Mm centre, ushort yawBam)
    {
        long s = TrigQ15.Sin(yawBam);
        long c = TrigQ15.Cos(yawBam);
        Vec3Mm offset = Rotate(localPoint.X, localPoint.Y, localPoint.Z, c, -s);
        return new Vec3Mm(
            (int)(centre.X + (long)offset.X),
            (int)(centre.Y + (long)offset.Y),
            (int)(centre.Z + (long)offset.Z));
    }

    /// <summary>Rotates both endpoints of a world segment into the box-local frame.</summary>
    /// <param name="p0">Segment start in world mm.</param>
    /// <param name="p1">Segment end in world mm.</param>
    /// <param name="centre">The box centre in world mm.</param>
    /// <param name="yawBam">The box yaw.</param>
    /// <param name="local0">The start in box-local mm.</param>
    /// <param name="local1">The end in box-local mm.</param>
    /// <example>
    /// <code>
    /// Obb.SegmentToLocal(in a, in b, in centre, yaw, out Vec3Mm l0, out Vec3Mm l1);
    /// SegmentInterval r = SegmentIntervals.Slab(in l0, in l1, in localBox);
    /// </code>
    /// </example>
    public static void SegmentToLocal(
        in Vec3Mm p0, in Vec3Mm p1, in Vec3Mm centre, ushort yawBam, out Vec3Mm local0, out Vec3Mm local1)
    {
        local0 = ToLocal(in p0, in centre, yawBam);
        local1 = ToLocal(in p1, in centre, yawBam);
    }

    /// <summary>
    /// The shared rotation: <c>x' = RH(x * cos - z * sinTerm, 1, 32767)</c>,
    /// <c>z' = RH(x * sinTerm + z * cos, 1, 32767)</c>; y passes through. <see cref="ToLocal"/> uses
    /// <c>sinTerm = sin</c>, <see cref="ToWorld"/> the inverse rotation <c>sinTerm = -sin</c>.
    /// </summary>
    private static Vec3Mm Rotate(long x, long y, long z, long cos, long sinTerm)
    {
        var sink = default(SimCounterSink);
        long rx = Fix.MulDivRH((x * cos) - (z * sinTerm), 1, TrigQ15.One, ref sink);
        long rz = Fix.MulDivRH((x * sinTerm) + (z * cos), 1, TrigQ15.One, ref sink);
        return new Vec3Mm((int)rx, (int)y, (int)rz);
    }
}
