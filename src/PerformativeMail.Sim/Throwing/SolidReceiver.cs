using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// A world solid in a <see cref="ReceiverView"/> (ADR-0015 section 4, ADR-0026 section 5): a box gathered by story 003 from
/// <c>IWorldQuery.QueryStaticSolids</c>, rank Solid. <see cref="Key"/> is <c>StaticSolid.Owner.Value</c>, or
/// <c>0x8000_0000 | Index</c> when the owner is None. The tie-break centre is the box centre.
/// </summary>
/// <example>
/// <code>
/// StaticSolid s = world.StaticSolids[i];
/// var r = new SolidReceiver(s.Box, s.Owner == EntityId.None ? 0x8000_0000u | s.Index : s.Owner.Value);
/// </code>
/// </example>
public readonly struct SolidReceiver
{
    /// <summary>The solid's box (inclusive corners).</summary>
    public readonly AabbMm Box;

    /// <summary>The receiver key hashed by the tie-break.</summary>
    public readonly uint Key;

    /// <summary>Creates a solid receiver.</summary>
    /// <param name="box">The box.</param>
    /// <param name="key">The receiver key.</param>
    public SolidReceiver(AabbMm box, uint key)
    {
        Box = box;
        Key = key;
    }
}
