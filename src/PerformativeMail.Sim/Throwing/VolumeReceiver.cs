using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// A mailbox catch volume in a <see cref="ReceiverView"/> (ADR-0015 section 4; GDD T6): an axis-aligned box that counts only
/// on a descending segment (<c>P1.y &lt; P0.y</c>). <see cref="Key"/> is the destination's <c>EntityId.Value</c>, assigned by the
/// view builder (story 003) and hashed verbatim in the tie-break. The tie-break centre is the box centre.
/// </summary>
/// <example>
/// <code>
/// var mbA = new VolumeReceiver(new AabbMm(new Vec3Mm(9750, 600, 9500), new Vec3Mm(10750, 1400, 10500)), destination.Value);
/// </code>
/// </example>
public readonly struct VolumeReceiver
{
    /// <summary>The catch volume (inclusive corners).</summary>
    public readonly AabbMm Box;

    /// <summary>The receiver key hashed by the tie-break (the destination's <c>EntityId.Value</c>).</summary>
    public readonly uint Key;

    /// <summary>Creates a catch-volume receiver.</summary>
    /// <param name="box">The catch volume.</param>
    /// <param name="key">The receiver key.</param>
    public VolumeReceiver(AabbMm box, uint key)
    {
        Box = box;
        Key = key;
    }
}
