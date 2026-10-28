using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// A flat-capped cylinder in a <see cref="ReceiverView"/> (GDD F4/T6): a goon (M-1b; radius + <c>enemyHitSlackMm</c>,
/// <c>EnemyDef</c> height) or a teammate bonk cylinder (M0; r 300, h 1400). Empty at M-1a. A dynamic receiver: bounding-sphere
/// rejected before the quadratic. <see cref="Kind"/> must be <see cref="ReceiverKind.Goon"/> or <see cref="ReceiverKind.Teammate"/>
/// and selects the rank. The tie-break centre is the axis point at mid-height.
/// </summary>
/// <example>
/// <code>
/// var goon = new CylinderReceiver(feet, 600, 1800, ReceiverKind.Goon, goonId.Value);
/// </code>
/// </example>
public readonly struct CylinderReceiver
{
    /// <summary>Centre of the bottom cap (the feet).</summary>
    public readonly Vec3Mm BaseCentre;

    /// <summary>The radius, 0 to <see cref="SegmentIntervals.BroadPhaseRadiusMm"/>.</summary>
    public readonly int RadiusMm;

    /// <summary>The height above <see cref="BaseCentre"/>.</summary>
    public readonly int HeightMm;

    /// <summary>
    /// <see cref="ReceiverKind.Goon"/> or <see cref="ReceiverKind.Teammate"/>. Mail Dev default, pending Evan (TD-181): not validated;
    /// <see cref="FlightSweep.FirstContact"/> ranks the cylinder as whatever kind it carries.
    /// </summary>
    public readonly ReceiverKind Kind;

    /// <summary>The receiver key hashed by the tie-break.</summary>
    public readonly uint Key;

    /// <summary>Creates a cylinder receiver.</summary>
    /// <param name="baseCentre">Centre of the bottom cap.</param>
    /// <param name="radiusMm">The radius.</param>
    /// <param name="heightMm">The height.</param>
    /// <param name="kind">Goon or Teammate.</param>
    /// <param name="key">The receiver key.</param>
    public CylinderReceiver(Vec3Mm baseCentre, int radiusMm, int heightMm, ReceiverKind kind, uint key)
    {
        BaseCentre = baseCentre;
        RadiusMm = radiusMm;
        HeightMm = heightMm;
        Kind = kind;
        Key = key;
    }
}
