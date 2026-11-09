using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Core.Presentation;

/// <summary>
/// The owned carrier's rendered feet pose (player-character story 022, ADR-0016 section 6: "present, ≤ 1-tick interpolation by
/// <c>AlphaPermille</c>"): the feet and body yaw blended from the state before the last tick (<c>ICarrierView.Previous</c>, alpha 0) to
/// the state after it (<c>ICarrierView.Current</c>, alpha 1000). Horizontal and vertical positions are interpolated, never extrapolated,
/// so the pose stays on the segment the solver swept; the height is raised to <see cref="IWorldQuery.TerrainHeightMm"/> at the rendered
/// x/z when the blend would dip below the ground (a straight blend across a ridge). Yaw takes the shortest arc
/// (<see cref="YawMath.Blend"/>). Integer mm, pure and allocation-free; the view divides by 1000 at its boundary.
/// </summary>
/// <example>
/// <code>
/// var pose = CarrierRenderPose.Compute(in view.Previous, in view.Current, alphaPermille, world);
/// node.Position = new Vector3(pose.XMm / 1000f, pose.YMm / 1000f, pose.ZMm / 1000f);
/// </code>
/// </example>
public readonly struct CarrierRenderPose
{
    /// <summary>Creates a pose.</summary>
    /// <param name="xMm">Feet X, mm.</param>
    /// <param name="yMm">Feet Y, mm.</param>
    /// <param name="zMm">Feet Z, mm.</param>
    /// <param name="yaw">Body yaw, BAM.</param>
    public CarrierRenderPose(int xMm, int yMm, int zMm, ushort yaw)
    {
        XMm = xMm;
        YMm = yMm;
        ZMm = zMm;
        Yaw = yaw;
    }

    /// <summary>Rendered feet X, mm.</summary>
    public int XMm { get; }

    /// <summary>Rendered feet Y, mm (never below the terrain at the rendered x/z).</summary>
    public int YMm { get; }

    /// <summary>Rendered feet Z, mm.</summary>
    public int ZMm { get; }

    /// <summary>Rendered body yaw, BAM.</summary>
    public ushort Yaw { get; }

    /// <summary>Blends <paramref name="prev"/> to <paramref name="curr"/> by <paramref name="alphaPermille"/> (clamped to 0..1000).</summary>
    /// <param name="prev">The state before the last tick (shown at alpha 0).</param>
    /// <param name="curr">The state after the last tick (shown at alpha 1000).</param>
    /// <param name="alphaPermille">The tick driver's fraction toward the next tick, permille.</param>
    /// <param name="world">The world tables (the terrain floor).</param>
    /// <returns>The rendered pose.</returns>
    public static CarrierRenderPose Compute(in CarrierState prev, in CarrierState curr, int alphaPermille, IWorldQuery world)
    {
        ArgumentNullException.ThrowIfNull(world);
        int alpha = Math.Clamp(alphaPermille, 0, 1000);
        int x = Lerp(prev.X, curr.X, alpha);
        int y = Lerp(prev.Y, curr.Y, alpha);
        int z = Lerp(prev.Z, curr.Z, alpha);
        long ground = world.TerrainHeightMm(x, z);
        if (y < ground)
        {
            y = (int)ground;
        }

        return new CarrierRenderPose(x, y, z, YawMath.Blend(prev.BodyYaw, curr.BodyYaw, alpha));
    }

    private static int Lerp(int from, int to, int alpha)
    {
        var sink = default(SimCounterSink); // a one-tick delta times ≤ 1000 over 1000 never clamps: nothing to count
        return (int)(from + Fix.MulDivRH((long)to - from, alpha, 1000, ref sink));
    }
}
