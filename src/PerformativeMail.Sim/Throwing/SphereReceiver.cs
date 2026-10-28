using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// An armed catcher's sphere in a <see cref="ReceiverView"/> (GDD F4/T6, rank ArmedCatcher; the gather is story 023, Deferred
/// to M0): centre = reported feet + 1000 mm, radius <c>catchRadiusMm</c>. A dynamic receiver: bounding-sphere rejected before
/// the quadratic. <see cref="Key"/> is the player's <c>EntityId.Value</c>; the tie-break centre is <see cref="Centre"/>.
/// </summary>
/// <example>
/// <code>
/// var catcher = new SphereReceiver(new Vec3Mm(feet.X, feet.Y + 1000, feet.Z), 1000, player.Value);
/// </code>
/// </example>
public readonly struct SphereReceiver
{
    /// <summary>The sphere centre.</summary>
    public readonly Vec3Mm Centre;

    /// <summary>The radius, 0 to <see cref="SegmentIntervals.BroadPhaseRadiusMm"/>.</summary>
    public readonly int RadiusMm;

    /// <summary>The receiver key hashed by the tie-break.</summary>
    public readonly uint Key;

    /// <summary>Creates a sphere receiver.</summary>
    /// <param name="centre">The centre.</param>
    /// <param name="radiusMm">The radius.</param>
    /// <param name="key">The receiver key.</param>
    public SphereReceiver(Vec3Mm centre, int radiusMm, uint key)
    {
        Centre = centre;
        RadiusMm = radiusMm;
        Key = key;
    }
}
