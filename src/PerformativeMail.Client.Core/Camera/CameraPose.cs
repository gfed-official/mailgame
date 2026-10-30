using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Core.Camera;

/// <summary>
/// Camera position composition (GDD player-character-movement P9, PCM-P9-1; ADR-0013 section 3 axes; TD-198 item 4, approved
/// by Evan 2026-10-29). From BAM look angles: back = <c>(Sin yaw, Cos yaw) * Cos pitch</c> in x/z with vertical
/// <c>-Sin pitch</c>, right = <c>(Cos yaw, -Sin yaw)</c> (one <see cref="TrigQ15"/> lookup each). The camera is the pivot plus
/// <c>back * desiredBoom + right * shoulder</c>, scaled as a whole by <c>boom / desiredBoom</c> (so a pulled-in boom pulls in
/// the shoulder too, as <see cref="BoomSolver"/> tests it), then hard-clamped to <c>TerrainHeightMm + 250</c> and, while
/// swimming, to <c>y &gt;= 0</c>. Presentation only: no value here feeds the Sim. Pure, integer-only, allocation-free.
/// </summary>
/// <example>
/// <code>
/// Vec3Mm pivot = CameraPose.Pivot(in feet, in profile);
/// Vec3Mm cam = CameraPose.Compose(world, in pivot, yaw, pitch, shoulder, profile.BoomMm, smoothedBoomMm, swimming);
/// </code>
/// </example>
public static class CameraPose
{
    /// <summary>Lowest camera height above the terrain, mm (the boom radius).</summary>
    public const int FloorClearanceMm = BoomSolver.DefaultRadiusMm;

    /// <summary>The water plane a swimmer's camera never goes below, mm.</summary>
    public const int WaterPlaneMm = 0;

    private const long OneSq = (long)TrigQ15.One * TrigQ15.One;

    /// <summary>The pivot: the feet raised by the profile's pivot height.</summary>
    /// <param name="feet">Carrier feet position.</param>
    /// <param name="profile">The (blended) profile.</param>
    /// <returns>The pivot.</returns>
    public static Vec3Mm Pivot(in Vec3Mm feet, in CameraProfile profile) =>
        new(feet.X, checked(feet.Y + profile.PivotHeightMm), feet.Z);

    /// <summary>The pivot-to-camera vector for a boom and shoulder, rounded half away per axis.</summary>
    /// <param name="yawBam">Look yaw, BAM.</param>
    /// <param name="pitchBam">Look pitch, signed BAM.</param>
    /// <param name="shoulderMm">Signed shoulder offset, mm.</param>
    /// <param name="boomMm">Boom length, mm.</param>
    /// <returns>The offset, mm.</returns>
    public static Vec3Mm Offset(ushort yawBam, short pitchBam, int shoulderMm, int boomMm)
    {
        ushort pitch = unchecked((ushort)pitchBam);
        long sy = TrigQ15.Sin(yawBam);
        long cy = TrigQ15.Cos(yawBam);
        long sp = TrigQ15.Sin(pitch);
        long cp = TrigQ15.Cos(pitch);
        var sink = default(SimCounterSink);
        long x = Fix.MulDivRH(checked((boomMm * sy * cp) + (shoulderMm * cy * TrigQ15.One)), 1, OneSq, ref sink);
        long y = Fix.MulDivRH(checked(-boomMm * sp * TrigQ15.One), 1, OneSq, ref sink);
        long z = Fix.MulDivRH(checked((boomMm * cy * cp) - (shoulderMm * sy * TrigQ15.One)), 1, OneSq, ref sink);
        return new Vec3Mm((int)x, (int)y, (int)z);
    }

    /// <summary>The camera position for this frame.</summary>
    /// <param name="w">The world (terrain height).</param>
    /// <param name="pivot">The pivot (<see cref="Pivot"/>).</param>
    /// <param name="yawBam">Look yaw, BAM.</param>
    /// <param name="pitchBam">Look pitch, signed BAM.</param>
    /// <param name="shoulderMm">Signed shoulder offset, mm.</param>
    /// <param name="desiredBoomMm">The profile's boom, mm (the length the solver tested).</param>
    /// <param name="boomMm">The smoothed boom (<see cref="BoomSmoother.Step"/>), mm.</param>
    /// <param name="swimming">Whether the carrier swims (the camera stays at or above the water plane).</param>
    /// <returns>The camera position, mm.</returns>
    public static Vec3Mm Compose(
        IWorldQuery w, in Vec3Mm pivot, ushort yawBam, short pitchBam, int shoulderMm, int desiredBoomMm, int boomMm, bool swimming)
    {
        ArgumentNullException.ThrowIfNull(w);
        Vec3Mm full = Offset(yawBam, pitchBam, shoulderMm, desiredBoomMm);
        var sink = default(SimCounterSink);
        long x = pivot.X;
        long y = pivot.Y;
        long z = pivot.Z;
        if (desiredBoomMm > 0)
        {
            x += Fix.MulDiv(full.X, boomMm, desiredBoomMm, ref sink);
            y += Fix.MulDiv(full.Y, boomMm, desiredBoomMm, ref sink);
            z += Fix.MulDiv(full.Z, boomMm, desiredBoomMm, ref sink);
        }

        long floor = checked(w.TerrainHeightMm(x, z) + FloorClearanceMm);
        y = y < floor ? floor : y;
        if (swimming && y < WaterPlaneMm)
        {
            y = WaterPlaneMm;
        }

        return new Vec3Mm(checked((int)x), checked((int)y), checked((int)z));
    }
}
