using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// Builds the <see cref="ReceiverView"/> one swept segment is tested against (ADR-0015 section 4; GDD F4 "Broad phase", T6, T11;
/// TR-throwing-003, TR-throwing-005 to 007). The host's <c>Throwing.Step</c> and the aim preview call the same
/// <see cref="Build"/>; only the <see cref="ReceiverMask"/> differs. Per segment it gathers, over the closed AABB of the segment:
/// <list type="bullet">
/// <item><description>mailbox catch volumes from the static 4 m <see cref="ReceiverGrid"/> (built at construction from an
/// <see cref="ICatchVolumeSource"/>), ascending ordinal = ascending key;</description></item>
/// <item><description>world solids from <see cref="IWorldQuery.QueryStaticSolids"/> (the only world-solid broad phase), ascending
/// index, keyed <c>Owner.Value</c> or <c>0x8000_0000 | Index</c>;</description></item>
/// <item><description>the caller's catcher spheres, passed through unchanged under <see cref="ReceiverMask.All"/> (the bounding-sphere
/// reject is <see cref="FlightSweep.FirstContact"/>'s) and dropped under <see cref="ReceiverMask.StaticOnly"/>.</description></item>
/// </list>
/// The map sides come from <see cref="IWorldQuery.Bounds"/> in the view (World registers no side solids). Belts and cylinders stay
/// empty at M-1a. A full candidate span keeps the lowest entries: mailbox overflow counts
/// <see cref="CounterId.ThrowingCandidateOverflow"/>, solid overflow is World's <see cref="CounterId.WorldSolidQueryOverflow"/>.
/// <see cref="Build"/> allocates 0 B; not thread-safe per <see cref="ReceiverScratch"/>.
/// </summary>
/// <example>
/// <code>
/// var builder = new ReceiverViewBuilder(world, new MailCatchVolumeSource(mail), counters);
/// var scratch = new ReceiverScratch();
/// ReceiverView view = builder.Build(in p0, in p1, ReceiverMask.All, ranks, catchers, scratch);
/// bool hit = FlightSweep.FirstContact(in p0, in p1, in view, flightId, out Contact c);
/// </code>
/// </example>
public sealed class ReceiverViewBuilder
{
    /// <summary>
    /// The static-receiver version the preview's cache key includes (ADR-0015 section 8): 0 at M-1a, where nothing static changes
    /// during a run; M-1b's construct and belt registry versions replace it (ADR-0026: <c>IWorldQuery</c> has no version). Mail Dev
    /// default, approved by Evan 2026-11-01 12:05 PM PT (TD-202 item 8): it lives here, on the builder the preview shares.
    /// </summary>
    public const int StaticVersion = 0;

    /// <summary>The <see cref="SolidReceiver.Key"/> tag of a solid without an owner (<c>Tag | Index</c>).</summary>
    public const uint UnownedSolidKeyTag = 0x8000_0000u;

    private readonly IWorldQuery _world;
    private readonly ICatchVolumeSource _volumes;
    private readonly SimCounters _counters;
    private readonly ReceiverGrid _grid;

    /// <summary>
    /// Creates the builder and builds the mailbox grid from <paramref name="volumes"/> (run start; allocates). Mail Dev default,
    /// approved by Evan 2026-11-01 12:05 PM PT (TD-202 item 5): the story fixes only <see cref="Build"/>'s signature; the world, the source and the counters
    /// are constructor arguments, and an overflowing build counts once.
    /// </summary>
    /// <param name="world">The world: solids, terrain, water and bounds.</param>
    /// <param name="volumes">The mailbox catch volumes.</param>
    /// <param name="counters">The world's counters (<see cref="CounterId.ThrowingCandidateOverflow"/>).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public ReceiverViewBuilder(IWorldQuery world, ICatchVolumeSource volumes, SimCounters counters)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(counters);
        _world = world;
        _volumes = volumes;
        _counters = counters;
        _grid = new ReceiverGrid(world.Bounds);
        Rebuild();
    }

    /// <summary>
    /// Re-reads every catch volume from the source and rebuilds the grid (allocates). Nothing calls it during an M-1a run; it is kept
    /// so M-1b's registry versions can drive it.
    /// </summary>
    public void Rebuild()
    {
        var rows = new VolumeReceiver[_volumes.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new VolumeReceiver(_volumes.CatchVolumeAt(i), _volumes.DestinationAt(i).Value);
        }

        _grid.Build(rows);
    }

    /// <summary>Gathers the receivers near the segment <c>p0 -&gt; p1</c> into <paramref name="scratch"/> and returns the view.</summary>
    /// <param name="p0">Segment start.</param>
    /// <param name="p1">Segment end.</param>
    /// <param name="mask">Which receivers the sweep will test; <see cref="ReceiverMask.StaticOnly"/> drops <paramref name="catchers"/>.</param>
    /// <param name="ranks">The rank table.</param>
    /// <param name="catchers">Armed catchers' spheres, ascending player slot (empty until M0).</param>
    /// <param name="scratch">The candidate buffers the view's spans point into (valid until the next build with it).</param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scratch"/> is null.</exception>
    public ReceiverView Build(in Vec3Mm p0, in Vec3Mm p1, ReceiverMask mask, ReceiverRanks ranks, ReadOnlySpan<SphereReceiver> catchers, ReceiverScratch scratch)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        AabbMm region = SegmentBox(in p0, in p1);

        ushort[] indices = scratch.Indices;
        VolumeReceiver[] mailboxes = scratch.Mailboxes;
        int mailboxCount = _grid.Query(in region, indices, out bool truncated);
        if (truncated)
        {
            _counters.Add(CounterId.ThrowingCandidateOverflow);
        }

        for (int i = 0; i < mailboxCount; i++)
        {
            mailboxes[i] = _grid.Volume(indices[i]);
        }

        int solidCount = GatherSolids(_world, in region, indices, scratch.Solids);
        return new ReceiverView(_world, _world.Bounds, ranks, mask)
        {
            Mailboxes = new ReadOnlySpan<VolumeReceiver>(mailboxes, 0, mailboxCount),
            Solids = new ReadOnlySpan<SolidReceiver>(scratch.Solids, 0, solidCount),
            Catchers = mask == ReceiverMask.All ? catchers : default,
        };
    }

    /// <summary>The closed AABB of a segment, <c>min(p0, p1)</c> to <c>max(p0, p1)</c> per axis.</summary>
    /// <param name="p0">One end.</param>
    /// <param name="p1">The other end.</param>
    /// <returns>The box.</returns>
    internal static AabbMm SegmentBox(in Vec3Mm p0, in Vec3Mm p1) => new(
        new Vec3Mm(p0.X < p1.X ? p0.X : p1.X, p0.Y < p1.Y ? p0.Y : p1.Y, p0.Z < p1.Z ? p0.Z : p1.Z),
        new Vec3Mm(p0.X > p1.X ? p0.X : p1.X, p0.Y > p1.Y ? p0.Y : p1.Y, p0.Z > p1.Z ? p0.Z : p1.Z));

    /// <summary>
    /// Writes the world solids overlapping <paramref name="region"/> into <paramref name="solids"/>, ascending index, through
    /// <see cref="IWorldQuery.QueryStaticSolids"/> (which counts its own overflow). Shared by <see cref="Build"/> and
    /// <see cref="ChestOriginProbe"/>.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="region">The query box (closed).</param>
    /// <param name="indices">Index scratch, at least as long as <paramref name="solids"/>.</param>
    /// <param name="solids">Destination; its length caps the result.</param>
    /// <returns>The number of solids written.</returns>
    internal static int GatherSolids(IWorldQuery world, in AabbMm region, Span<ushort> indices, Span<SolidReceiver> solids)
    {
        int n = world.QueryStaticSolids(in region, indices[..solids.Length]);
        ReadOnlySpan<StaticSolid> all = world.StaticSolids;
        for (int i = 0; i < n; i++)
        {
            ref readonly StaticSolid s = ref all[indices[i]];
            solids[i] = new SolidReceiver(s.Box, s.Owner == EntityId.None ? UnownedSolidKeyTag | s.Index : s.Owner.Value);
        }

        return n;
    }
}
