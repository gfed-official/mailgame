using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.World;

/// <summary>
/// One lot (ADR-0026 section 6). The lot index equals Mail's <c>DestinationOrdinal</c>; lots are in
/// ascending <see cref="Address"/>. Built by <see cref="WorldTablesBuilder"/> (world-stub story 004), which derives
/// <see cref="Anchor"/>, the address components and <see cref="Destination"/>. Field-wise equality.
/// </summary>
/// <example>
/// <code>
/// LotRecord lot = world.Lots[0];
/// bool ours = world.StaticSolids[lot.HouseSolid].Owner == lot.Destination;   // true
/// </code>
/// </example>
public readonly struct LotRecord : IEquatable<LotRecord>
{
    /// <summary>Destination entity (class 0x07), allocated by the builder in lot order.</summary>
    public readonly EntityId Destination;

    /// <summary>Packed address.</summary>
    public readonly AddressId Address;

    /// <summary>District id.</summary>
    public readonly byte DistrictId;

    /// <summary>Street id.</summary>
    public readonly byte StreetId;

    /// <summary>House number.</summary>
    public readonly ushort Number;

    /// <summary>Display string.</summary>
    public readonly StringId Display;

    /// <summary>Tile holding the mailbox.</summary>
    public readonly TileCoord MailboxTile;

    /// <summary>Direction the lot faces.</summary>
    public readonly FaceDir Face;

    /// <summary>Catch anchor (face + 300 mm along the normal, y = ground + 1000 mm).</summary>
    public readonly Vec3Mm Anchor;

    /// <summary>Tile a courier approaches from.</summary>
    public readonly TileCoord ApproachTile;

    /// <summary>Truck stop point.</summary>
    public readonly Vec3Mm TruckStopPoint;

    /// <summary>Index of the lot's house <see cref="StaticSolid"/>.</summary>
    public readonly ushort HouseSolid;

    /// <summary>Creates a lot record.</summary>
    /// <param name="destination">Destination entity.</param>
    /// <param name="address">Packed address.</param>
    /// <param name="districtId">District id.</param>
    /// <param name="streetId">Street id.</param>
    /// <param name="number">House number.</param>
    /// <param name="display">Display string.</param>
    /// <param name="mailboxTile">Mailbox tile.</param>
    /// <param name="face">Facing.</param>
    /// <param name="anchor">Catch anchor.</param>
    /// <param name="approachTile">Approach tile.</param>
    /// <param name="truckStopPoint">Truck stop point.</param>
    /// <param name="houseSolid">House solid index.</param>
    public LotRecord(
        EntityId destination,
        AddressId address,
        byte districtId,
        byte streetId,
        ushort number,
        StringId display,
        TileCoord mailboxTile,
        FaceDir face,
        Vec3Mm anchor,
        TileCoord approachTile,
        Vec3Mm truckStopPoint,
        ushort houseSolid)
    {
        Destination = destination;
        Address = address;
        DistrictId = districtId;
        StreetId = streetId;
        Number = number;
        Display = display;
        MailboxTile = mailboxTile;
        Face = face;
        Anchor = anchor;
        ApproachTile = approachTile;
        TruckStopPoint = truckStopPoint;
        HouseSolid = houseSolid;
    }

    /// <inheritdoc />
    public bool Equals(LotRecord other) =>
        Destination == other.Destination && Address == other.Address && DistrictId == other.DistrictId
        && StreetId == other.StreetId && Number == other.Number && Display == other.Display
        && MailboxTile == other.MailboxTile && Face == other.Face && Anchor == other.Anchor
        && ApproachTile == other.ApproachTile && TruckStopPoint == other.TruckStopPoint && HouseSolid == other.HouseSolid;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LotRecord other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)Destination.Value;
            hash = (hash * 16777619) ^ (int)Address.Value;
            hash = (hash * 16777619) ^ (((int)Display.Ns << 16) | Display.Index);
            hash = (hash * 16777619) ^ MailboxTile.Tx;
            hash = (hash * 16777619) ^ MailboxTile.Tz;
            hash = (hash * 16777619) ^ (int)Face;
            hash = (hash * 16777619) ^ Anchor.X;
            hash = (hash * 16777619) ^ Anchor.Y;
            hash = (hash * 16777619) ^ Anchor.Z;
            hash = (hash * 16777619) ^ ApproachTile.Tx;
            hash = (hash * 16777619) ^ ApproachTile.Tz;
            hash = (hash * 16777619) ^ TruckStopPoint.X;
            hash = (hash * 16777619) ^ TruckStopPoint.Y;
            hash = (hash * 16777619) ^ TruckStopPoint.Z;
            hash = (hash * 16777619) ^ HouseSolid;
            return hash;
        }
    }

    /// <summary>Field-wise equality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(LotRecord left, LotRecord right) => left.Equals(right);

    /// <summary>Field-wise inequality.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when different.</returns>
    public static bool operator !=(LotRecord left, LotRecord right) => !left.Equals(right);
}
