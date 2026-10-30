using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Core.Camera;

/// <summary>
/// The camera boom solve (GDD player-character-movement P9; TR-movement-024; ADR-0013 section 6, ADR-0019 section 2; TD-198
/// items 1–3, approved by Evan 2026-10-29). The boom is a swept sphere approximated as the segment pivot → desired camera
/// position (<see cref="CameraPose.Offset"/>, shoulder included) tested against:
/// <list type="bullet">
/// <item><description>each static solid from one <see cref="IWorldQuery.QueryStaticSolids"/> call over the segment's AABB
/// (grown by the radius) into a <see cref="CandidateCapacity"/>-entry buffer, its box inflated by the radius,
/// with <see cref="SegmentIntervals.Slab"/> in ascending index. On overflow the query keeps the lowest indices; the query
/// writes the bound Sim counters (<c>WorldSolidQueryOverflow</c>, <c>WorldSolidQueryFallback</c>), so call on the thread that
/// ticks the Sim, between ticks. This class counts nothing itself.</description></item>
/// <item><description>the height field, with the segment lowered by the radius, via
/// <see cref="HeightfieldMarch.FirstContact"/>, one permille before its (rounded-up) contact fraction. A segment outside
/// the march contract is treated as a contact at 0.</description></item>
/// </list>
/// The smallest fraction <c>f</c> (permille) gives <c>max(MinBoomMm, desired * f / 1000)</c>. It reads the world on every
/// call and keeps no copy of the geometry. The terrain floor is approximate to march resolution (and <see cref="MinBoomMm"/>
/// wins over it); <see cref="CameraPose.Compose"/>'s clamp is the hard guarantee. Players, items and vehicles are not inputs. Static and pure: no fields, no clock,
/// integer-only, allocation-free.
/// </summary>
/// <example>
/// <code>
/// int target = BoomSolver.Solve(world, in pivot, yawBam, pitchBam, settings.ShoulderMm(in profile), profile.BoomMm);
/// </code>
/// </example>
public static class BoomSolver
{
    /// <summary>The shortest boom returned, mm.</summary>
    public const int MinBoomMm = 300;

    /// <summary>The swept-sphere radius, mm (also the terrain clearance of <see cref="CameraPose.Compose"/>).</summary>
    public const int DefaultRadiusMm = 250;

    /// <summary>Static-solid candidates tested per solve.</summary>
    public const int CandidateCapacity = 64;

    private const int FullPm = SegmentInterval.SegmentEndPm;

    /// <summary>The longest safe boom from <paramref name="pivot"/>.</summary>
    /// <param name="w">The world (height field and static solids).</param>
    /// <param name="pivot">The camera pivot (<see cref="CameraPose.Pivot"/>).</param>
    /// <param name="yawBam">Look yaw, BAM.</param>
    /// <param name="pitchBam">Look pitch, signed BAM (positive raises the view and lowers the camera).</param>
    /// <param name="shoulderMm">Signed shoulder offset, mm.</param>
    /// <param name="desiredBoomMm">The profile's boom, mm.</param>
    /// <param name="radiusMm">Swept-sphere radius, mm (negative counts as 0).</param>
    /// <returns>The boom length, at least <see cref="MinBoomMm"/>.</returns>
    public static int Solve(
        IWorldQuery w, in Vec3Mm pivot, ushort yawBam, short pitchBam, int shoulderMm, int desiredBoomMm, int radiusMm = DefaultRadiusMm)
    {
        ArgumentNullException.ThrowIfNull(w);
        int r = radiusMm < 0 ? 0 : radiusMm;
        Vec3Mm offset = CameraPose.Offset(yawBam, pitchBam, shoulderMm, desiredBoomMm);
        var end = new Vec3Mm(checked(pivot.X + offset.X), checked(pivot.Y + offset.Y), checked(pivot.Z + offset.Z));

        int f = SolidsFraction(w, in pivot, in end, r);
        int terrain = TerrainFraction(w, in pivot, in end, r);
        f = terrain < f ? terrain : f;

        var sink = default(SimCounterSink);
        long boom = f >= FullPm ? desiredBoomMm : Fix.MulDiv(desiredBoomMm, f, FullPm, ref sink);
        return boom < MinBoomMm ? MinBoomMm : (int)boom;
    }

    private static int SolidsFraction(IWorldQuery w, in Vec3Mm p0, in Vec3Mm p1, int r)
    {
        var region = new AabbMm(
            new Vec3Mm(Grow(Math.Min(p0.X, p1.X), -r), Grow(Math.Min(p0.Y, p1.Y), -r), Grow(Math.Min(p0.Z, p1.Z), -r)),
            new Vec3Mm(Grow(Math.Max(p0.X, p1.X), r), Grow(Math.Max(p0.Y, p1.Y), r), Grow(Math.Max(p0.Z, p1.Z), r)));
        Span<ushort> scratch = stackalloc ushort[CandidateCapacity];
        int count = w.QueryStaticSolids(in region, scratch);
        ReadOnlySpan<StaticSolid> solids = w.StaticSolids;
        int best = FullPm;
        for (int i = 0; i < count; i++)
        {
            ushort index = scratch[i];
            if (index >= solids.Length)
            {
                continue;   // out-of-range index: skipped, never dereferenced (as FocusOcclusion)
            }

            AabbMm b = solids[index].Box;
            var inflated = new AabbMm(
                new Vec3Mm(Grow(b.Min.X, -r), Grow(b.Min.Y, -r), Grow(b.Min.Z, -r)),
                new Vec3Mm(Grow(b.Max.X, r), Grow(b.Max.Y, r), Grow(b.Max.Z, r)));
            SegmentInterval hit = SegmentIntervals.Slab(in p0, in p1, in inflated);
            if (hit.Hit && hit.FractionPm < best)
            {
                best = hit.FractionPm;
            }
        }

        return best;
    }

    private static int TerrainFraction(IWorldQuery w, in Vec3Mm p0, in Vec3Mm p1, int r)
    {
        if (!HeightfieldMarch.IsWithinExtent((long)p1.X - p0.X, (long)p1.Y - p0.Y, (long)p1.Z - p0.Z))
        {
            return 0;   // outside the march contract: fail closed to the floor length
        }

        var low0 = new Vec3Mm(p0.X, Grow(p0.Y, -r), p0.Z);
        var low1 = new Vec3Mm(p1.X, Grow(p1.Y, -r), p1.Z);
        if (!HeightfieldMarch.FirstContact(w, in low0, in low1, out int fPm))
        {
            return FullPm;
        }

        // The march rounds the contact fraction up (CeilDiv), up to 1 permille past its interpolated crossing; one permille
        // back cancels that. The interpolation between samples can still miss a crease, so Compose clamps to the floor.
        return fPm > 0 ? fPm - 1 : 0;
    }

    /// <summary><c>v + d</c> saturated to the int range.</summary>
    private static int Grow(int v, int d)
    {
        long s = (long)v + d;
        return s > int.MaxValue ? int.MaxValue : s < int.MinValue ? int.MinValue : (int)s;
    }
}
