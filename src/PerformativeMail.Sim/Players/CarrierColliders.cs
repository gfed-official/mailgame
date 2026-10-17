using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// The carrier's collider candidates (GDD <c>player-character-movement.md</c> P4; ADR-0013 section 4; ADR-0026 sections 4-5; story 008):
/// gathered ONCE per tick, before phase 4, into the fixed <see cref="CarrierScratch"/> buffers, for the tick's swept region
/// (<see cref="SweptRegion"/>: the feet position grown by the per-tick displacement cap <c>subStepMm x maxSubStepsPerTick</c> (1200) plus
/// the radius in x/z, and by the cap below the feet and above the head). Sources: <see cref="TileFlags.Blocked"/> tiles inside the map
/// (row-major; tiles outside the map are left to the story-007 map clamp), static solids from <see cref="IWorldQuery.QueryStaticSolids"/>
/// in ascending <see cref="StaticSolid.Index"/>; more than the limit counts <see cref="CarrierScratch.CandidateOverflow"/> and keeps the
/// lowest ids. Construct boxes (<see cref="ICarrierBoxSource"/>) and vehicle OBBs are empty at M-1a. Pure, allocation-free, no statics.
/// </summary>
/// <example><code>CarrierColliders.Gather(ref scratch, in env, in state);</code></example>
public static class CarrierColliders
{
    /// <summary>The tick's swept region around the feet (closed, mm).</summary>
    /// <param name="state">The carrier at the start of the tick.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>The region.</returns>
    /// <example><code>AabbMm region = CarrierColliders.SweptRegion(in state, settings);   // x/z +-1500, y -1200 .. +2600</code></example>
    public static AabbMm SweptRegion(in CarrierState state, PlayerSettings settings)
    {
        long cap = (long)Math.Max(1, settings.SubStepMm) * Math.Max(1, settings.MaxSubStepsPerTick);
        long grow = cap + Math.Max(0, settings.CapsuleRadiusMm);
        long head = Math.Max(0, settings.CapsuleHeightMm);
        return new AabbMm(
            new Vec3Mm(Clamp(state.X - grow), Clamp(state.Y - cap), Clamp(state.Z - grow)),
            new Vec3Mm(Clamp(state.X + grow), Clamp(state.Y + head + cap), Clamp(state.Z + grow)));
    }

    /// <summary>Clears the candidate buffers and gathers this tick's colliders for <paramref name="state"/>.</summary>
    /// <param name="scratch">The scratch; candidates replaced, <see cref="CarrierScratch.CandidateOverflow"/> counted.</param>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="state">The carrier at the start of the tick.</param>
    /// <example><code>CarrierColliders.Gather(ref scratch, in env, in state);</code></example>
    public static void Gather(ref CarrierScratch scratch, in CarrierEnv env, in CarrierState state)
    {
        scratch.ClearCandidates();
        AabbMm region = SweptRegion(in state, env.Settings);
        IWorldQuery world = env.World;

        // Blocked tiles under the region, row-major (ascending tz, then tx).
        int size = Math.Max(1, world.TileSizeMm);
        int tx0 = Math.Max(0, FloorTile(region.Min.X, size));
        int tx1 = Math.Min(world.TilesX - 1, FloorTile(region.Max.X, size));
        int tz0 = Math.Max(0, FloorTile(region.Min.Z, size));
        int tz1 = Math.Min(world.TilesZ - 1, FloorTile(region.Max.Z, size));
        for (int tz = tz0; tz <= tz1; tz++)
        {
            for (int tx = tx0; tx <= tx1; tx++)
            {
                if ((world.Tile(tx, tz) & TileFlags.Blocked) != 0)
                {
                    scratch.TryAddTile(tx, tz);
                }
            }
        }

        // Static solids, ascending Index. One extra slot detects overflow; the query keeps the lowest ids, and the extra is dropped
        // (and counted) by TryAddSolid.
        int limit = Math.Clamp(scratch.Limit, 0, CarrierScratch.SolidCapacity);
        Span<ushort> found = stackalloc ushort[CarrierScratch.SolidCapacity + 1];
        int count = world.QueryStaticSolids(in region, found[..(limit + 1)]);
        int known = world.StaticSolids.Length;
        for (int i = 0; i < count; i++)
        {
            if (found[i] < known)
            {
                scratch.TryAddSolid(found[i]);
            }
            else if (scratch.CandidateOverflow < int.MaxValue)
            {
                scratch.CandidateOverflow++;   // a query index outside StaticSolids is dropped and counted, never dereferenced
            }
        }

        // M-1b hook: ICarrierBoxSource.GatherBoxes(in region, ...) -> scratch.TryAddBox, ascending collider id (see ICarrierBoxSource).
        // M1 hook: vehicle OBB candidates. Unreachable at M-1a: the support source is the empty EmptySupportTransformSource.
        if (!env.Supports.Vehicles.IsEmpty)
        {
            // Vehicles (M1) add their refs here with scratch.TryAddVehicle; no OBB narrow phase exists yet.
        }
    }

    /// <summary>True when any collider candidate is held (the box pass is skipped otherwise).</summary>
    /// <param name="scratch">The scratch.</param>
    /// <returns>True when there is something to collide with.</returns>
    public static bool HasAny(in CarrierScratch scratch) => scratch.TileCount + scratch.SolidCount + scratch.BoxCount > 0;

    /// <summary>The first candidate the cylinder overlaps, in the fixed order tiles, static solids (ascending index), construct boxes.</summary>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The candidates.</param>
    /// <param name="x">Feet X.</param>
    /// <param name="y">Feet Y.</param>
    /// <param name="z">Feet Z.</param>
    /// <param name="box">The overlapped box.</param>
    /// <returns>True when one overlaps.</returns>
    public static bool FirstOverlap(in CarrierEnv env, in CarrierScratch scratch, long x, long y, long z, out ColliderBox box)
    {
        PlayerSettings s = env.Settings;
        long r = s.CapsuleRadiusMm;
        long h = s.CapsuleHeightMm;
        int size = env.World.TileSizeMm;
        for (int i = 0; i < scratch.TileCount; i++)
        {
            scratch.TileAt(i, out int tx, out int tz);
            box = ColliderBox.Tile(tx, tz, size);
            if (CylinderVsBox.Overlaps(x, y, z, r, h, in box))
            {
                return true;
            }
        }

        ReadOnlySpan<StaticSolid> solids = env.World.StaticSolids;
        foreach (ushort index in scratch.Solids)
        {
            if (index >= solids.Length)
            {
                continue;   // out-of-range index: skipped (Gather never stores one)
            }

            box = ColliderBox.From(in solids[index].Box);
            if (CylinderVsBox.Overlaps(x, y, z, r, h, in box))
            {
                return true;
            }
        }

        foreach (AabbMm b in scratch.Boxes)
        {
            box = ColliderBox.From(in b);
            if (CylinderVsBox.Overlaps(x, y, z, r, h, in box))
            {
                return true;
            }
        }

        box = default;
        return false;
    }

    /// <summary>
    /// The highest walkable box top under the footprint (disc of the capsule radius, strict) that is at or below
    /// <paramref name="feetY"/>; blocked tiles have no top. Returns false when there is none.
    /// </summary>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The candidates.</param>
    /// <param name="x">Feet X.</param>
    /// <param name="feetY">Feet Y: tops above it are walls (or step-ups, story 008 part B), not supports.</param>
    /// <param name="z">Feet Z.</param>
    /// <param name="top">The highest top, mm.</param>
    /// <returns>True when a top was found.</returns>
    public static bool HighestTopUnder(in CarrierEnv env, in CarrierScratch scratch, long x, long feetY, long z, out int top)
    {
        long r = env.Settings.CapsuleRadiusMm;
        long best = long.MinValue;
        ReadOnlySpan<StaticSolid> solids = env.World.StaticSolids;
        foreach (ushort index in scratch.Solids)
        {
            if (index >= solids.Length)
            {
                continue;   // out-of-range index: skipped (Gather never stores one)
            }

            ColliderBox box = ColliderBox.From(in solids[index].Box);
            if (box.MaxY <= feetY && box.MaxY > best && CylinderVsBox.FootprintOver(x, z, r, in box))
            {
                best = box.MaxY;
            }
        }

        foreach (AabbMm b in scratch.Boxes)
        {
            ColliderBox box = ColliderBox.From(in b);
            if (box.MaxY <= feetY && box.MaxY > best && CylinderVsBox.FootprintOver(x, z, r, in box))
            {
                best = box.MaxY;
            }
        }

        top = best == long.MinValue ? 0 : (int)best;
        return best != long.MinValue;
    }

    /// <summary>
    /// The lowest box bottom over the footprint (disc of the capsule radius, strict) at or above <paramref name="headY"/>: the ceiling of a
    /// rising carrier (story 008 part B). Blocked tiles have no bottom. Returns false when there is none.
    /// </summary>
    /// <param name="env">Read-only ports and settings.</param>
    /// <param name="scratch">The candidates.</param>
    /// <param name="x">Feet X.</param>
    /// <param name="headY">Head Y (feet + capsule height): bottoms below it are walls, not ceilings.</param>
    /// <param name="z">Feet Z.</param>
    /// <param name="bottom">The lowest bottom, mm.</param>
    /// <returns>True when a bottom was found.</returns>
    public static bool LowestBottomOver(in CarrierEnv env, in CarrierScratch scratch, long x, long headY, long z, out long bottom)
    {
        long r = env.Settings.CapsuleRadiusMm;
        long best = long.MaxValue;
        ReadOnlySpan<StaticSolid> solids = env.World.StaticSolids;
        foreach (ushort index in scratch.Solids)
        {
            if (index >= solids.Length)
            {
                continue;   // out-of-range index: skipped (Gather never stores one)
            }

            ColliderBox box = ColliderBox.From(in solids[index].Box);
            if (box.MinY >= headY && box.MinY < best && CylinderVsBox.FootprintOver(x, z, r, in box))
            {
                best = box.MinY;
            }
        }

        foreach (AabbMm b in scratch.Boxes)
        {
            ColliderBox box = ColliderBox.From(in b);
            if (box.MinY >= headY && box.MinY < best && CylinderVsBox.FootprintOver(x, z, r, in box))
            {
                best = box.MinY;
            }
        }

        bottom = best;
        return best != long.MaxValue;
    }

    private static int FloorTile(long v, int size) => (int)Fix.FloorDiv((Int128)v, size);

    private static int Clamp(long v) => (int)Math.Clamp(v, int.MinValue, int.MaxValue);
}
