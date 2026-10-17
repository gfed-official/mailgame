using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Players;

/// <summary>
/// A vehicle's pose from the latest received transform batch (ADR-0013 section 7): id, box-centre position in mm, yaw in BAM, box half
/// extents in mm and the roof surface height in mm. Unmanaged. Vehicles arrive at M1; at M-1a the support source returns none.
/// </summary>
/// <example><code>var v = new VehiclePose(id, 0, 500, 0, yawBam: 16384, 1000, 500, 2000, roofYMm: 1000);</code></example>
public readonly struct VehiclePose
{
    /// <summary>The vehicle's entity id.</summary>
    public readonly EntityId Id;

    /// <summary>Box centre X, mm.</summary>
    public readonly int X;

    /// <summary>Box centre Y, mm.</summary>
    public readonly int Y;

    /// <summary>Box centre Z, mm.</summary>
    public readonly int Z;

    /// <summary>Yaw, BAM (65 536 per turn).</summary>
    public readonly ushort YawBam;

    /// <summary>Half extent along the vehicle's local X, mm.</summary>
    public readonly int HalfX;

    /// <summary>Half extent along Y, mm.</summary>
    public readonly int HalfY;

    /// <summary>Half extent along the vehicle's local Z, mm.</summary>
    public readonly int HalfZ;

    /// <summary>World Y of the roof surface, mm.</summary>
    public readonly int RoofY;

    /// <summary>Creates a pose.</summary>
    /// <param name="id">Entity id.</param>
    /// <param name="x">Centre X, mm.</param>
    /// <param name="y">Centre Y, mm.</param>
    /// <param name="z">Centre Z, mm.</param>
    /// <param name="yawBam">Yaw, BAM.</param>
    /// <param name="halfX">Half extent X, mm.</param>
    /// <param name="halfY">Half extent Y, mm.</param>
    /// <param name="halfZ">Half extent Z, mm.</param>
    /// <param name="roofY">Roof height, mm.</param>
    public VehiclePose(EntityId id, int x, int y, int z, ushort yawBam, int halfX, int halfY, int halfZ, int roofY)
    {
        Id = id;
        X = x;
        Y = y;
        Z = z;
        YawBam = yawBam;
        HalfX = halfX;
        HalfY = halfY;
        HalfZ = halfZ;
        RoofY = roofY;
    }
}
