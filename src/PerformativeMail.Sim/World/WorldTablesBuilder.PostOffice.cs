using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.World;

/// <summary>
/// The Post Office and its spawn pad (world-stub story 005; ADR-0026 section 7, PC P17/F8, ADR-0013 section 3).
/// Every check here runs once at <c>Build</c>, never in a tick; a failure is a <see cref="SimBuildException"/>.
/// </summary>
public sealed partial class WorldTablesBuilder
{
    private Vec3Mm _poDoor;
    private Vec3Mm _poIntake;
    private Vec3Mm _poPadCentre;
    private int _postOfficeSets;

    /// <summary>
    /// Sets the world's one Post Office (ADR-0026 section 7). The record gets <see cref="PadSlots.RingRadiusMm"/> (1200)
    /// and <see cref="PadSlots.SlotCount"/> (8). Validated at <c>Build</c>: each anchor inside the map bounds on X/Z
    /// (|Y| at most <see cref="WorldConstants.MaxCoordMm"/>), and every pad slot (<see cref="PadSlots.Position"/>) on a
    /// <see cref="TileFlags.Walkable"/> tile, with water depth below the build's <c>swimEnterMm</c>, and outside every
    /// static solid. A second call is not an override: it makes <c>Build</c> fail with
    /// <see cref="SimBuildErrorCode.WorldPostOfficeSetTwice"/>. Required by <see cref="Build(EntityIds, int, int)"/>;
    /// optional on <see cref="Build(int, int)"/>; rejected by the parameterless <see cref="Build()"/>.
    /// </summary>
    /// <param name="door">Door anchor in mm (also the runtime pad fallback, PC F8).</param>
    /// <param name="intake">Intake anchor in mm, where <c>po_intake</c> is placed.</param>
    /// <param name="padCentre">Spawn-pad centre in mm (its y is stored as given; slot heights come from the terrain).</param>
    /// <example>
    /// <code>
    /// builder.SetPostOffice(new Vec3Mm(4000, 0, 2000), new Vec3Mm(4500, 1000, 2000), new Vec3Mm(4000, 0, 5000));
    /// </code>
    /// </example>
    public void SetPostOffice(Vec3Mm door, Vec3Mm intake, Vec3Mm padCentre)
    {
        _postOfficeSets++;
        if (_postOfficeSets == 1)
        {
            _poDoor = door;
            _poIntake = intake;
            _poPadCentre = padCentre;
        }
    }

    /// <summary>The record to store: <c>default</c> when no Post Office was set (legal only on the overloads without <see cref="EntityIds"/>).</summary>
    private PostOfficeRecord PreparePostOffice(bool required, bool computeWalkable, in MapBounds bounds)
    {
        if (_postOfficeSets > 1)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldPostOfficeSetTwice, "WorldTablesBuilder.Build: SetPostOffice was called " + _postOfficeSets + " times; a world has one Post Office.");
        }

        if (_postOfficeSets == 0)
        {
            if (required)
            {
                throw new SimBuildException(SimBuildErrorCode.WorldPostOfficeNotSet, "WorldTablesBuilder.Build: SetPostOffice has not been called; Build(EntityIds, slopeMaxPm, swimEnterMm) needs the Post Office and spawn pad.");
            }

            return default;
        }

        if (!computeWalkable)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldPostOfficeNeedsWalkabilityLimits, "WorldTablesBuilder.Build: a Post Office was set, but Build() has no walkability limits to validate its spawn pad; use Build(slopeMaxPm, swimEnterMm) or Build(EntityIds, slopeMaxPm, swimEnterMm).");
        }

        CheckAnchor(_poDoor, in bounds, "door");
        CheckAnchor(_poIntake, in bounds, "intake");
        CheckAnchor(_poPadCentre, in bounds, "pad centre");
        return new PostOfficeRecord(_poDoor, _poIntake, _poPadCentre, PadSlots.RingRadiusMm, PadSlots.SlotCount);
    }

    private static void CheckAnchor(Vec3Mm p, in MapBounds bounds, string name)
    {
        if (!InsideXz(p, in bounds) || OutOfRange(p.Y))
        {
            throw new SimBuildException(
                SimBuildErrorCode.WorldPostOfficeAnchorOutsideBounds,
                "WorldTablesBuilder.Build: Post Office " + name + " anchor (" + p.X + ", " + p.Y + ", " + p.Z + ") is outside the map bounds on X/Z or |Y| exceeds " + WorldConstants.MaxCoordMm + " mm.");
        }
    }

    /// <summary>
    /// ADR-0026 section 7: every pad slot must have water depth below <paramref name="swimEnterMm"/> (checked first: it is
    /// exact at the slot point), lie on a Walkable tile (the tile containing the point; the far map edge belongs to the last
    /// tile, as in the height lookup), and lie outside every static solid (closed box on all three axes, found with a
    /// degenerate <see cref="IWorldQuery.QueryStaticSolids"/> region at the point). Runs on the finished tables, before any
    /// Destination id is allocated. No-op when the world has no Post Office.
    /// </summary>
    private void ValidatePad(WorldTables tables, in PostOfficeRecord po, int swimEnterMm)
    {
        if (_postOfficeSets == 0)
        {
            return;
        }

        var hits = new ushort[Math.Max(1, tables.StaticSolids.Length)]; // never truncates, so no overflow is counted at build
        for (int k = 0; k < PadSlots.SlotCount; k++)
        {
            Vec3Mm p = PadSlots.Position(tables, in po, k);
            string where = "WorldTablesBuilder.Build: spawn-pad slot " + k + " at (" + p.X + ", " + p.Y + ", " + p.Z + ") ";
            if (tables.WaterDepthMm(p.X, p.Z) >= swimEnterMm)
            {
                throw new SimBuildException(SimBuildErrorCode.WorldPadSlotTooDeep, where + "has water depth " + tables.WaterDepthMm(p.X, p.Z) + " mm, at or above swimEnterMm " + swimEnterMm + ".");
            }

            if ((tables.Tile(SlotTile(p.X, _tilesX), SlotTile(p.Z, _tilesZ)) & TileFlags.Walkable) == 0)
            {
                throw new SimBuildException(SimBuildErrorCode.WorldPadSlotNotWalkable, where + "is not on a Walkable tile.");
            }

            var point = new AabbMm(p, p);
            if (tables.QueryStaticSolids(in point, hits) != 0)
            {
                throw new SimBuildException(SimBuildErrorCode.WorldPadSlotInsideSolid, where + "is inside static solid " + hits[0] + ".");
            }
        }
    }

    /// <summary>Tile index along one axis: floor(coord / S), with coord = tiles * S mapped to the last tile. Off-grid stays off-grid (Tile reports Blocked).</summary>
    private static int SlotTile(int coordMm, int tiles)
    {
        const int S = WorldConstants.TileSizeMm;
        int t = coordMm >= 0 ? coordMm / S : ((coordMm + 1) / S) - 1;
        return coordMm == tiles * S ? tiles - 1 : t;
    }
}
