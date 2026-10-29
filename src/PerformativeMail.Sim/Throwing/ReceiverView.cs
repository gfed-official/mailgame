using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Everything one swept segment is tested against (ADR-0015 section 4 and Key Interfaces; GDD T6). Story 003 builds it from
/// the static 4 m grid, <c>IWorldQuery.QueryStaticSolids</c> and the dynamic registries; <see cref="FlightSweep.FirstContact"/>
/// only reads it. The spans are borrowed (the view is a <c>ref struct</c> and never outlives them). Terrain and water come
/// from <see cref="World"/>, the four vertical map sides from <see cref="Bounds"/>. <see cref="Cylinders"/> and
/// <see cref="Belts"/> stay empty at M-1a and <see cref="Catchers"/> until M0. <c>default</c> is not valid: <see cref="World"/>
/// must be set, so build it with the constructor.
/// </summary>
/// <example>
/// <code>
/// var view = new ReceiverView(world, world.Bounds, new ReceiverRanks(contentSet.Throw), ReceiverMask.All)
/// {
///     Mailboxes = volumes,
///     Solids = solids,
/// };
/// bool hit = FlightSweep.FirstContact(in p0, in p1, in view, flightId, out Contact c);
/// </code>
/// </example>
public readonly ref struct ReceiverView
{
    /// <summary>Creates a view with empty receiver spans.</summary>
    /// <param name="world">The world: terrain through <c>HeightfieldMarch</c>, water through <see cref="IWorldQuery.WaterDepthMm"/>.</param>
    /// <param name="bounds">The map rectangle whose four vertical sides are rank-Solid receivers (normally <c>world.Bounds</c>).</param>
    /// <param name="ranks">The rank table (from <c>ThrowBalance</c>).</param>
    /// <param name="mask">Which receivers to test.</param>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is null.</exception>
    public ReceiverView(IWorldQuery world, MapBounds bounds, ReceiverRanks ranks, ReceiverMask mask)
    {
        ArgumentNullException.ThrowIfNull(world);
        World = world;
        Bounds = bounds;
        Ranks = ranks;
        Mask = mask;
    }

    /// <summary>Mailbox catch volumes (descending entry only).</summary>
    public ReadOnlySpan<VolumeReceiver> Mailboxes { get; init; }

    /// <summary>World solids near the segment.</summary>
    public ReadOnlySpan<SolidReceiver> Solids { get; init; }

    /// <summary>Armed catchers' spheres (dynamic; skipped under <see cref="ReceiverMask.StaticOnly"/>).</summary>
    public ReadOnlySpan<SphereReceiver> Catchers { get; init; }

    /// <summary>Goon and teammate cylinders (dynamic; skipped under <see cref="ReceiverMask.StaticOnly"/>).</summary>
    public ReadOnlySpan<CylinderReceiver> Cylinders { get; init; }

    /// <summary>Belt-lane top faces (static; kept under <see cref="ReceiverMask.StaticOnly"/>).</summary>
    public ReadOnlySpan<LaneReceiver> Belts { get; init; }

    /// <summary>The map rectangle; its four vertical sides are rank-Solid receivers.</summary>
    public MapBounds Bounds { get; }

    /// <summary>The world (terrain and water).</summary>
    public IWorldQuery World { get; }

    /// <summary>
    /// The rank table. Evan approved 2026-10-28 (TD-181): the story's view lists no rank member, but <c>FirstContact</c> reads
    /// nothing but its arguments and the ranks are content (THROW-CV-05), so the view carries them.
    /// </summary>
    public ReceiverRanks Ranks { get; }

    /// <summary>Which receivers are tested.</summary>
    public ReceiverMask Mask { get; }
}
