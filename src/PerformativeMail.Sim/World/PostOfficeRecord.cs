using PerformativeMail.Sim.Geometry;

namespace PerformativeMail.Sim.World;

/// <summary>
/// The Post Office and its spawn pad (ADR-0026 section 7; world-stub story 005). The world holds exactly one, set with
/// <see cref="WorldTablesBuilder.SetPostOffice"/>; pad slot positions come from <see cref="World.PadSlots.Position"/>.
/// Equality is component-wise (TD-028 item 2).
/// </summary>
/// <example>
/// <code>
/// PostOfficeRecord po = world.PostOffice;
/// Vec3Mm fallback = po.DoorAnchor;   // PC F8 runtime fallback
/// </code>
/// </example>
public readonly struct PostOfficeRecord : IEquatable<PostOfficeRecord>
{
    /// <summary>Door anchor (also the pad fallback).</summary>
    public readonly Vec3Mm DoorAnchor;

    /// <summary>Intake anchor (<c>po_intake</c>).</summary>
    public readonly Vec3Mm IntakeAnchor;

    /// <summary>Pad centre.</summary>
    public readonly Vec3Mm PadCentre;

    /// <summary>Pad ring radius in mm (<see cref="World.PadSlots.RingRadiusMm"/> = 1200 in M-1a).</summary>
    public readonly int PadRingRadiusMm;

    /// <summary>Number of pad slots (<see cref="World.PadSlots.SlotCount"/> = 8 in M-1a).</summary>
    public readonly byte PadSlots;

    /// <summary>Creates a record.</summary>
    /// <param name="doorAnchor">Door anchor.</param>
    /// <param name="intakeAnchor">Intake anchor.</param>
    /// <param name="padCentre">Pad centre.</param>
    /// <param name="padRingRadiusMm">Pad ring radius in mm.</param>
    /// <param name="padSlots">Pad slot count.</param>
    public PostOfficeRecord(Vec3Mm doorAnchor, Vec3Mm intakeAnchor, Vec3Mm padCentre, int padRingRadiusMm, byte padSlots)
    {
        DoorAnchor = doorAnchor;
        IntakeAnchor = intakeAnchor;
        PadCentre = padCentre;
        PadRingRadiusMm = padRingRadiusMm;
        PadSlots = padSlots;
    }

    /// <inheritdoc />
    public bool Equals(PostOfficeRecord other) =>
        DoorAnchor == other.DoorAnchor && IntakeAnchor == other.IntakeAnchor && PadCentre == other.PadCentre
        && PadRingRadiusMm == other.PadRingRadiusMm && PadSlots == other.PadSlots;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PostOfficeRecord other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = DoorAnchor.X;
            hash = (hash * 16777619) ^ DoorAnchor.Y;
            hash = (hash * 16777619) ^ DoorAnchor.Z;
            hash = (hash * 16777619) ^ IntakeAnchor.X;
            hash = (hash * 16777619) ^ IntakeAnchor.Y;
            hash = (hash * 16777619) ^ IntakeAnchor.Z;
            hash = (hash * 16777619) ^ PadCentre.X;
            hash = (hash * 16777619) ^ PadCentre.Y;
            hash = (hash * 16777619) ^ PadCentre.Z;
            hash = (hash * 16777619) ^ PadRingRadiusMm;
            hash = (hash * 16777619) ^ PadSlots;
            return hash;
        }
    }

    /// <summary>Component-wise equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(PostOfficeRecord left, PostOfficeRecord right) => left.Equals(right);

    /// <summary>Component-wise inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when any component differs.</returns>
    public static bool operator !=(PostOfficeRecord left, PostOfficeRecord right) => !left.Equals(right);
}
