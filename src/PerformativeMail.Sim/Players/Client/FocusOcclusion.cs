using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players.Client;

/// <summary>
/// Focus line of sight (ADR-0014 section 2, ADR-0013 section 6; GDD P11; TR-movement-011): whether the chest-to-anchor segment
/// crosses terrain, a blocked tile or a static solid other than the anchor entity's own. Client-side only: the host has no
/// line-of-sight check, and no type outside a <c>.Client</c> namespace may call it (architecture rule, story 015).
/// <list type="bullet">
/// <item><description>Terrain: <see cref="HeightfieldMarch.FirstContact"/>; a contact occludes unless it is at the anchor end
/// (<c>fPm == 1000</c>), so an anchor resting on the ground is not hidden by the ground under it (TD-175 item 3, approved by
/// Evan 2026-10-28 7:26 AM PT).</description></item>
/// <item><description>Blocked tiles: every <see cref="TileFlags.Blocked"/> tile of the map under the segment's x/z AABB, as an
/// <see cref="AabbMm"/> over its closed footprint with <c>Y</c> from <see cref="int.MinValue"/> to <see cref="int.MaxValue"/>.</description></item>
/// <item><description>Static solids: one <see cref="IWorldQuery.QueryStaticSolids"/> call over the segment's AABB (expanded by 0 mm)
/// into the caller's scratch; each solid whose <see cref="StaticSolid.Owner"/> is not the anchor entity is tested with
/// <see cref="SegmentIntervals.Slab"/>.</description></item>
/// </list>
/// Every hit uses <see cref="SegmentInterval.IsHit"/> (<c>enter &lt;= exit</c>, <c>exit &gt;= 0</c>, <c>enter &lt;= 1000</c>), so a
/// segment touching a box face exactly at the anchor end (<c>t = 1000</c>) is occluded. Solids past the scratch length are not
/// tested (the query keeps the lowest indices and counts <c>WorldSolidQueryOverflow</c>; Evan approved 2026-10-28 (TD-182
/// item 2)). Pure, allocation-free, non-throwing.
/// </summary>
/// <example>
/// <code>
/// Span&lt;ushort&gt; solids = stackalloc ushort[settings.MaxCarrierCandidates];
/// bool hidden = FocusOcclusion.IsOccluded(world, in chest, in def.Point, def.Ref.Entity, solids, ref probes);
/// </code>
/// </example>
public static class FocusOcclusion
{
    /// <summary>Tests the chest-to-anchor segment and counts one <see cref="FocusCounters.OcclusionTests"/>.</summary>
    /// <param name="w">The world (terrain, tiles, static solids).</param>
    /// <param name="chest">Segment start: the carrier's chest.</param>
    /// <param name="anchor">Segment end: the anchor point.</param>
    /// <param name="anchorEntity">The anchor's entity; static solids it owns do not occlude it.</param>
    /// <param name="scratch">Solid-index buffer for the one query; its length (<c>maxCarrierCandidates</c>) caps the solids tested.</param>
    /// <param name="c">The pass's probes; <see cref="FocusCounters.OcclusionTests"/> is incremented.</param>
    /// <returns>True when the segment is blocked.</returns>
    /// <example><code>bool hidden = FocusOcclusion.IsOccluded(world, in chest, in point, entity, solids, ref probes);</code></example>
    public static bool IsOccluded(IWorldQuery w, in Vec3Mm chest, in Vec3Mm anchor, EntityId anchorEntity, Span<ushort> scratch, ref FocusCounters c)
    {
        c.OcclusionTests++;
        if (HeightfieldMarch.FirstContact(w, in chest, in anchor, out int fPm) && fPm != SegmentInterval.SegmentEndPm)
        {
            return true;
        }

        var region = new AabbMm(
            new Vec3Mm(Math.Min(chest.X, anchor.X), Math.Min(chest.Y, anchor.Y), Math.Min(chest.Z, anchor.Z)),
            new Vec3Mm(Math.Max(chest.X, anchor.X), Math.Max(chest.Y, anchor.Y), Math.Max(chest.Z, anchor.Z)));
        return CrossesBlockedTile(w, in chest, in anchor, in region) || CrossesSolid(w, in chest, in anchor, in region, anchorEntity, scratch);
    }

    private static bool CrossesBlockedTile(IWorldQuery w, in Vec3Mm chest, in Vec3Mm anchor, in AabbMm region)
    {
        // Evan approved 2026-10-28 (TD-182 item 1): tiles of the map only (as CarrierColliders.Gather); IWorldQuery.Tile
        // reports off-map tiles Blocked, but they do not occlude.
        int size = Math.Max(1, w.TileSizeMm);
        int tx0 = Math.Max(0, FloorTile(region.Min.X, size));
        int tx1 = Math.Min(w.TilesX - 1, FloorTile(region.Max.X, size));
        int tz0 = Math.Max(0, FloorTile(region.Min.Z, size));
        int tz1 = Math.Min(w.TilesZ - 1, FloorTile(region.Max.Z, size));
        for (int tz = tz0; tz <= tz1; tz++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                if ((w.Tile(tx, tz) & TileFlags.Blocked) == 0)
                {
                    continue;
                }

                var tile = new AabbMm(
                    new Vec3Mm(tx * size, int.MinValue, tz * size),
                    new Vec3Mm((tx + 1) * size, int.MaxValue, (tz + 1) * size));
                if (SegmentIntervals.Slab(in chest, in anchor, in tile).Hit)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CrossesSolid(IWorldQuery w, in Vec3Mm chest, in Vec3Mm anchor, in AabbMm region, EntityId anchorEntity, Span<ushort> scratch)
    {
        // Evan approved 2026-10-28 (TD-182 item 2): solids past the scratch are not tested, so a dense area can read as clear.
        int count = w.QueryStaticSolids(in region, scratch);
        ReadOnlySpan<StaticSolid> solids = w.StaticSolids;
        for (int i = 0; i < count; i++)
        {
            ref readonly StaticSolid s = ref solids[scratch[i]];
            if (s.Owner != anchorEntity && SegmentIntervals.Slab(in chest, in anchor, in s.Box).Hit)
            {
                return true;
            }
        }

        return false;
    }

    private static int FloorTile(long v, int size) => (int)Fix.FloorDiv((Int128)v, size);
}
