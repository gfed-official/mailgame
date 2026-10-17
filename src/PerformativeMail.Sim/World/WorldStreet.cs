using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.World;

/// <summary>
/// One registered street centre strip (world-stub story 004): the footprint answers <see cref="IWorldQuery.StreetNameAt"/>
/// and the axis drives the Mail R3 spacing check. Internal: the street list is not part of <see cref="IWorldQuery"/>.
/// </summary>
internal readonly struct WorldStreet
{
    public readonly byte StreetId;
    public readonly byte DistrictId;
    public readonly StringId Display;
    public readonly AabbMm Footprint;
    public readonly StreetAxis Axis;

    public WorldStreet(byte streetId, byte districtId, StringId display, AabbMm footprint, StreetAxis axis)
    {
        StreetId = streetId;
        DistrictId = districtId;
        Display = display;
        Footprint = footprint;
        Axis = axis;
    }

    /// <summary>Sort and lookup key: <c>district &lt;&lt; 8 | street</c>, the same order as the address's high bits.</summary>
    public int Key => (DistrictId << 8) | StreetId;

    /// <summary>Closed containment of (x, z) in the footprint (edges count as on the street).</summary>
    public bool Contains(long xMm, long zMm) =>
        xMm >= Footprint.Min.X && xMm <= Footprint.Max.X && zMm >= Footprint.Min.Z && zMm <= Footprint.Max.Z;
}
