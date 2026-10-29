using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.ThrowingCatching;

/// <summary>
/// The winning contact of one swept segment (ADR-0015 section 4; GDD F4/T6): <c>(Kind, ReceiverKey, FPm, Point, Rank)</c>.
/// <see cref="Point"/> is always <c>P0 + Throw.RH(D * FPm, 1000)</c>. Ground, water and the map sides have no entity:
/// terrain and water carry key 0, the sides <c>0xC000_0000 | side</c> (0 -X, 1 +X, 2 -Z, 3 +Z). Immutable value type.
/// </summary>
/// <example>
/// <code>
/// if (FlightSweep.FirstContact(in p0, in p1, in view, id, out Contact c) &amp;&amp; c.Kind == ReceiverKind.Mailbox)
/// {
///     // c.ReceiverKey is the destination's EntityId.Value
/// }
/// </code>
/// </example>
public readonly struct Contact
{
    /// <summary>What was touched.</summary>
    public readonly ReceiverKind Kind;

    /// <summary>The receiver's key (the value hashed by the tie-break).</summary>
    public readonly uint ReceiverKey;

    /// <summary>The contact fraction in permille of the segment, 0 to 1000.</summary>
    public readonly int FPm;

    /// <summary>The contact point, <c>P0 + Throw.RH(D * FPm, 1000)</c>.</summary>
    public readonly Vec3Mm Point;

    /// <summary>The receiver's rank from the view's <see cref="ReceiverRanks"/>.</summary>
    public readonly byte Rank;

    /// <summary>Creates a contact.</summary>
    /// <param name="kind">What was touched.</param>
    /// <param name="receiverKey">The receiver key.</param>
    /// <param name="fPm">The contact fraction in permille.</param>
    /// <param name="point">The contact point.</param>
    /// <param name="rank">The receiver rank.</param>
    public Contact(ReceiverKind kind, uint receiverKey, int fPm, Vec3Mm point, byte rank)
    {
        Kind = kind;
        ReceiverKey = receiverKey;
        FPm = fPm;
        Point = point;
        Rank = rank;
    }
}
