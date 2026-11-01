using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The real <see cref="IChestOriginProbe"/> (ADR-0015 section 3; GDD F1 "the segment from the chest to the origin is swept against
/// solids and the heightfield", Edge Cases "If the chest → origin sweep is blocked"; TC-E-1 a). It gathers the world solids
/// around the segment through <see cref="IWorldQuery.QueryStaticSolids"/> and runs the one sweep,
/// <see cref="FlightSweep.FirstContact"/>, under <see cref="ReceiverMask.StaticOnly"/> with no mailboxes: any contact
/// (<c>f &lt;= 1000</c>) is blocked. Mail Dev default, approved by Evan 2026-11-01 12:05 PM PT (TD-202 item 1): the sweep also tests what it always tests
/// besides solids and terrain, so an origin across a map side or below the water plane counts as blocked too.
/// Pure: the candidate buffers are on the stack, so it is safe from any thread and allocates 0 B.
/// </summary>
/// <example>
/// <code>
/// var input = new LaunchInput(feet, dx, dy, dz, side, weightClass, rangePm, yaw, settings, new ChestOriginProbe(world));
/// LaunchMath.Compute(in input, out Launch l, out bool fromChest);
/// </code>
/// </example>
public sealed class ChestOriginProbe : IChestOriginProbe
{
    private readonly IWorldQuery _world;

    /// <summary>Creates the probe.</summary>
    /// <param name="world">The world: solids, terrain, water and bounds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public ChestOriginProbe(IWorldQuery world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    /// <inheritdoc />
    public bool IsBlocked(in Vec3Mm chest, in Vec3Mm origin)
    {
        Span<ushort> indices = stackalloc ushort[ThrowSettings.MaxReceiverCandidates];
        Span<SolidReceiver> solids = stackalloc SolidReceiver[ThrowSettings.MaxReceiverCandidates];
        AabbMm region = ReceiverViewBuilder.SegmentBox(in chest, in origin);
        int n = ReceiverViewBuilder.GatherSolids(_world, in region, indices, solids);

        // The ranks and the flight id only order contacts; "blocked" is whether there is any.
        var view = new ReceiverView(_world, _world.Bounds, ReceiverRanks.Default, ReceiverMask.StaticOnly) { Solids = solids[..n] };
        return FlightSweep.FirstContact(in chest, in origin, in view, default, out _);
    }
}
