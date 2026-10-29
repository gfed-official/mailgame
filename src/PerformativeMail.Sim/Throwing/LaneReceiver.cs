using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// A belt-lane top face in a <see cref="ReceiverView"/> (GDD F4/T6, rank Belt; M-1b, empty at M-1a): a thin box tested by the
/// slab interval. A full, refusing or just-deconstructed lane is left out of the view by its builder. The tie-break centre is
/// the box centre.
/// </summary>
/// <example>
/// <code>
/// var lane = new LaneReceiver(new AabbMm(new Vec3Mm(0, 900, 0), new Vec3Mm(600, 920, 4000)), beltId.Value);
/// </code>
/// </example>
public readonly struct LaneReceiver
{
    /// <summary>The lane's top face (inclusive corners).</summary>
    public readonly AabbMm TopFace;

    /// <summary>The receiver key hashed by the tie-break.</summary>
    public readonly uint Key;

    /// <summary>Creates a belt-lane receiver.</summary>
    /// <param name="topFace">The top face.</param>
    /// <param name="key">The receiver key.</param>
    public LaneReceiver(AabbMm topFace, uint key)
    {
        TopFace = topFace;
        Key = key;
    }
}
