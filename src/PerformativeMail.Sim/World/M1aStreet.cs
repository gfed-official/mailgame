using System.Globalization;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.World;

/// <summary>
/// The M-1a hand-authored street (world-stub story 006; ADR-0026 section 8, Mail R3, PC P4/P17/F7, TR-movement-005,
/// TR-movement-018, TR-mail-006): a deterministic, code-authored data table in the style of <c>BuiltInContent</c>.
/// No RNG, no IO, no clock: the same source yields the same tables. <see cref="Version"/> is recorded as the world
/// source identity by story 007 and must change whenever any authored datum below changes.
/// <para>
/// Layout (greybox, mm; X runs along the street, tile = 2000): the map is 64 x 32 tiles (128 m x 64 m). The street is
/// the strip z 28,000 to 36,000 (centre line z = 32,000), x 0 to 100,000. Odd houses 1, 3, 5, 7, 9 stand on the -Z side,
/// even houses 2, 4, 6, 8, 10 on the +Z side. Houses sit on 12 m plots (<see cref="LotPitchMm"/>), so same-side
/// mailboxes are 12 m apart; the even side is shifted by half a plot (<see cref="EvenSideShiftMm"/>), so opposite-side
/// mailboxes are staggered 6 m along the street. The Post Office with its spawn pad stands at the west end (x 4 to 20 m);
/// the beach strip runs from x = 98 m to the east map edge, falling to -2000 mm; one authored ramp ridge sits at the
/// north edge (<see cref="RampTileX"/>, <see cref="RampTileZ"/>).
/// </para>
/// <para>
/// Everything except the numbers and the placeholder street name is derived from the constants. Build with
/// <c>Build(EntityIds, slopeMaxPm, swimEnterMm)</c> (the composition path); the parameterless <c>Build()</c> rejects a Post Office.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var b = new WorldTablesBuilder();
/// M1aStreet.Author(b);
/// WorldTables street = b.Build(ids, 700, 900);   // ids: the world's EntityIds while Constructed
/// </code>
/// </example>
public static class M1aStreet
{
    /// <summary>Version of the authored data, recorded in the replay header (ADR-0026 section 9). Bump on every change to the authored tables.</summary>
    public const int Version = 1;

    /// <summary>Map width limit in tiles (ADR-0026 section 8).</summary>
    public const int MaxTilesX = 128;

    /// <summary>Map depth limit in tiles (ADR-0026 section 8).</summary>
    public const int MaxTilesZ = 64;

    /// <summary>Authored map width in tiles (128 m).</summary>
    public const int TilesX = 64;

    /// <summary>Authored map depth in tiles (64 m).</summary>
    public const int TilesZ = 32;

    /// <summary>District id of the only district.</summary>
    public const int DistrictId = 1;

    /// <summary>Street id of the only street.</summary>
    public const int StreetId = 1;

    /// <summary>Number of houses (numbers 1 to 10).</summary>
    public const int HouseCount = 10;

    /// <summary>Dry ground height in mm.</summary>
    public const int GroundMm = 1000;

    // PLACEHOLDER (no World Generation GDD): the street name is a stand-in until narrative or level design names it.
    /// <summary>Placeholder street name (display text only; no rule depends on it).</summary>
    public const string StreetName = "Placeholder Lane";

    // ---- street strip ----

    /// <summary>Street strip west end (x, mm).</summary>
    public const int StreetMinXMm = 0;

    /// <summary>Street strip east end (x, mm); the beach starts just past it.</summary>
    public const int StreetMaxXMm = 100_000;

    /// <summary>Street strip near edge (z, mm).</summary>
    public const int StreetMinZMm = 28_000;

    /// <summary>Street strip far edge (z, mm).</summary>
    public const int StreetMaxZMm = 36_000;

    /// <summary>Street centre line (z, mm); houses on each side of it.</summary>
    public const int StreetCentreZMm = 32_000;

    /// <summary>Height of the street record's footprint box above the ground (mm); a thin slab, display and query only.</summary>
    public const int StreetFootprintHeightMm = 1;

    // ---- houses ----

    /// <summary>Plot length along the street; same-side mailbox pitch (Mail R3 needs at least 6000).</summary>
    public const int LotPitchMm = 12_000;

    /// <summary>Even side shift along the street relative to the odd side; the opposite-side stagger (Mail R3 needs at least 3000).</summary>
    public const int EvenSideShiftMm = 6000;

    /// <summary>House width along the street.</summary>
    public const int HouseWidthMm = 11_000;

    /// <summary>Clear gap between neighbouring same-side houses: doorway-sized, so PC OQ23 (capsule vs doorway) can be observed here.</summary>
    public const int HouseGapMm = LotPitchMm - HouseWidthMm;

    /// <summary>House depth across the street.</summary>
    public const int HouseDepthMm = 12_000;

    /// <summary>House wall height.</summary>
    public const int HouseHeightMm = 3000;

    /// <summary>Roof volume thickness on top of the house.</summary>
    public const int RoofThicknessMm = 600;

    /// <summary>X of the west edge of the first odd plot (house 1).</summary>
    public const int FirstOddPlotMinXMm = 28_000;

    /// <summary>Odd (-Z side) house far wall, towards the street (z, mm).</summary>
    public const int OddHouseMaxZMm = 24_000;

    /// <summary>Even (+Z side) house near wall, towards the street (z, mm).</summary>
    public const int EvenHouseMinZMm = 40_000;

    /// <summary>Odd mailbox face point (z, mm): in the front yard, on the -Z side of the centre line.</summary>
    public const int OddMailboxZMm = 25_000;

    /// <summary>Even mailbox face point (z, mm): in the front yard, on the +Z side of the centre line.</summary>
    public const int EvenMailboxZMm = 39_000;

    // ---- Post Office ----

    /// <summary>Post Office west wall (x, mm).</summary>
    public const int PostOfficeMinXMm = 4000;

    /// <summary>Post Office east wall (x, mm).</summary>
    public const int PostOfficeMaxXMm = 20_000;

    /// <summary>Post Office north wall (z, mm).</summary>
    public const int PostOfficeMinZMm = 12_000;

    /// <summary>Post Office south wall, towards the street (z, mm).</summary>
    public const int PostOfficeMaxZMm = 24_000;

    /// <summary>Post Office wall height.</summary>
    public const int PostOfficeHeightMm = 4000;

    /// <summary>Post Office door and intake distance in front of the south wall (z, mm). Greybox guess, no GDD value.</summary>
    public const int PostOfficeFrontOffsetMm = 1000;

    /// <summary>Intake X offset east of the door (mm). Greybox guess, no GDD value.</summary>
    public const int IntakeDoorOffsetXMm = 2000;

    /// <summary>Intake height above the ground (mm); the door sits on the ground. Greybox guess, no GDD value.</summary>
    public const int IntakeHeightMm = 1000;

    /// <summary>Spawn pad centre X (mm), in front of the Post Office on the street strip.</summary>
    public const int PadCentreXMm = 12_000;

    /// <summary>Spawn pad centre Z (mm); the 1200 mm ring lies wholly on the street strip, clear of every solid.</summary>
    public const int PadCentreZMm = 30_000;

    // ---- beach ----

    /// <summary>Last dry-ground corner column; corners east of it fall by <see cref="BeachStepMm"/> per tile.</summary>
    public const int BeachShoreCornerX = 49;

    /// <summary>Beach drop per tile, mm (300 permille, walkable).</summary>
    public const int BeachStepMm = 600;

    /// <summary>Sea floor height, mm (depth 2000).</summary>
    public const int SeaFloorMm = -2000;

    /// <summary>Probe point (x, mm) in a shallow beach tile: 0 &lt; depth &lt; 900 (depth 500).</summary>
    public const int BeachShallowXMm = 103_000;

    /// <summary>Probe point (z, mm) in the shallow beach tile.</summary>
    public const int BeachShallowZMm = StreetCentreZMm;

    /// <summary>Probe point (x, mm) in a deep tile: depth at least 900 (depth 2000).</summary>
    public const int BeachDeepXMm = 109_000;

    /// <summary>Probe point (z, mm) in the deep tile.</summary>
    public const int BeachDeepZMm = StreetCentreZMm;

    // ---- ramp ----

    /// <summary>The authored ramp tile's X index: it rises <see cref="RampRiseMm"/> over one tile along +X (700 permille, exactly the walkable limit).</summary>
    public const int RampTileX = 20;

    /// <summary>The authored ramp tile's Z index (the north edge row).</summary>
    public const int RampTileZ = 0;

    /// <summary>Ramp rise over one tile (2000 mm), FX-Ramp's walkable case.</summary>
    public const int RampRiseMm = 1400;

    // ---- authoring ----

    /// <summary>
    /// Adds the M-1a street to <paramref name="b"/>: corners and bounds, road and lot tile marks, the display strings, the
    /// street, ten lots each with a House and a Roof solid, the Post Office solid, and the Post Office with its pad.
    /// Does not call Build; the caller picks the overload (<c>Build(EntityIds, slopeMaxPm, swimEnterMm)</c>). Deterministic.
    /// </summary>
    /// <param name="b">An empty builder.</param>
    /// <example>
    /// <code>
    /// M1aStreet.Author(builder);
    /// </code>
    /// </example>
    public static void Author(WorldTablesBuilder b)
    {
        ArgumentNullException.ThrowIfNull(b);
        AuthorTerrain(b);
        AuthorRoadMarks(b);

        StringId street = b.AddDisplayString(StreetName);
        b.AddStreet(
            StreetId,
            DistrictId,
            street,
            new AabbMm(new Vec3Mm(StreetMinXMm, GroundMm, StreetMinZMm), new Vec3Mm(StreetMaxXMm, GroundMm + StreetFootprintHeightMm, StreetMaxZMm)),
            StreetAxis.X);

        for (int i = 0; i < HouseCount; i++)
        {
            AuthorLot(b, i + 1);
        }

        AuthorPostOffice(b);
    }

    private static void AuthorTerrain(WorldTablesBuilder b)
    {
        int stride = TilesX + 1;
        var corners = new int[stride * (TilesZ + 1)];
        for (int cz = 0; cz <= TilesZ; cz++)
        {
            for (int cx = 0; cx <= TilesX; cx++)
            {
                corners[(cz * stride) + cx] = CornerHeightMm(cx, cz);
            }
        }

        b.SetCorners(TilesX, TilesZ, corners);
        b.SetBounds(new MapBounds(0, 0, (long)TilesX * WorldConstants.TileSizeMm, (long)TilesZ * WorldConstants.TileSizeMm));
    }

    private static int CornerHeightMm(int cx, int cz)
    {
        // Ramp ridge: the two corners east of the ramp tile (rows RampTileZ and RampTileZ + 1) are raised by the rise.
        // The tile west of the ridge is the rising ramp; the tile east of it is its mirror (a falling tile). The ridge
        // corner on row RampTileZ + 1 also warps tiles (RampTileX, 1) and (RampTileX + 1, 1): their surface tilts in both
        // X and Z (a corner-only rise, about 990 permille combined, so not Walkable at the 700 limit). The ridge is ~26 m from
        // the street, so no gameplay path crosses it; M1aStreetTests pins the warped tiles' walkability.
        if (cx == RampTileX + 1 && (cz == RampTileZ || cz == RampTileZ + 1))
        {
            return GroundMm + RampRiseMm;
        }

        if (cx > BeachShoreCornerX)
        {
            int h = GroundMm - (BeachStepMm * (cx - BeachShoreCornerX));
            return h < SeaFloorMm ? SeaFloorMm : h;
        }

        return GroundMm;
    }

    private static void AuthorRoadMarks(WorldTablesBuilder b)
    {
        const int S = WorldConstants.TileSizeMm;
        for (int tz = StreetMinZMm / S; tz < StreetMaxZMm / S; tz++)
        {
            for (int tx = StreetMinXMm / S; tx < StreetMaxXMm / S; tx++)
            {
                b.MarkRoad(tx, tz);
            }
        }
    }

    private static void AuthorLot(WorldTablesBuilder b, int number)
    {
        bool odd = (number & 1) == 1;
        int slot = (number - 1) / 2;
        int plotMinX = FirstOddPlotMinXMm + (slot * LotPitchMm) + (odd ? 0 : EvenSideShiftMm);
        int houseMinX = plotMinX + ((LotPitchMm - HouseWidthMm) / 2);
        int houseMinZ = odd ? OddHouseMaxZMm - HouseDepthMm : EvenHouseMinZMm;
        var house = new AabbMm(
            new Vec3Mm(houseMinX, GroundMm, houseMinZ),
            new Vec3Mm(houseMinX + HouseWidthMm, GroundMm + HouseHeightMm, houseMinZ + HouseDepthMm));

        ushort houseSolid = AddHouseAndRoof(b, house);
        MarkLotTiles(b, house);
        AddLotRecord(b, number, odd, plotMinX + (LotPitchMm / 2), houseSolid);
    }

    /// <summary>One House and one Roof solid per lot, in this order (indices 2 * (number - 1) and 2 * (number - 1) + 1).</summary>
    private static ushort AddHouseAndRoof(WorldTablesBuilder b, AabbMm house)
    {
        ushort solid = b.AddSolid(EntityId.None, house, SolidKind.House);
        int top = house.Max.Y;
        b.AddSolid(
            EntityId.None,
            new AabbMm(new Vec3Mm(house.Min.X, top, house.Min.Z), new Vec3Mm(house.Max.X, top + RoofThicknessMm, house.Max.Z)),
            SolidKind.Roof);
        return solid;
    }

    private static void MarkLotTiles(WorldTablesBuilder b, AabbMm house)
    {
        const int S = WorldConstants.TileSizeMm;
        for (int tz = house.Min.Z / S; tz < house.Max.Z / S; tz++)
        {
            for (int tx = house.Min.X / S; tx < (house.Max.X + S - 1) / S; tx++)
            {
                b.MarkLot(tx, tz);
            }
        }
    }

    private static void AddLotRecord(WorldTablesBuilder b, int number, bool odd, int mailboxX, ushort houseSolid)
    {
        const int S = WorldConstants.TileSizeMm;
        int mailboxZ = odd ? OddMailboxZMm : EvenMailboxZMm;
        int mailboxTx = mailboxX / S;
        int approachTz = odd ? StreetMinZMm / S : (StreetMaxZMm / S) - 1; // the street-edge tile row on this lot's side
        int truckZ = odd ? StreetMinZMm + (S / 2) : StreetMaxZMm - (S / 2);

        StringId label = b.AddDisplayString(number.ToString(CultureInfo.InvariantCulture) + " " + StreetName);
        b.AddLot(
            DistrictId,
            StreetId,
            number,
            0,
            label,
            new Vec3Mm(mailboxX, GroundMm, mailboxZ),
            odd ? FaceDir.PosZ : FaceDir.NegZ,
            new TileCoord(mailboxTx, mailboxZ / S),
            new TileCoord(mailboxTx, approachTz),
            new Vec3Mm(mailboxX, GroundMm, truckZ),
            houseSolid);
    }

    private static void AuthorPostOffice(WorldTablesBuilder b)
    {
        b.AddSolid(
            EntityId.None,
            new AabbMm(
                new Vec3Mm(PostOfficeMinXMm, GroundMm, PostOfficeMinZMm),
                new Vec3Mm(PostOfficeMaxXMm, GroundMm + PostOfficeHeightMm, PostOfficeMaxZMm)),
            SolidKind.PostOffice);

        int doorX = (PostOfficeMinXMm + PostOfficeMaxXMm) / 2;
        int frontZ = PostOfficeMaxZMm + PostOfficeFrontOffsetMm;
        b.SetPostOffice(
            new Vec3Mm(doorX, GroundMm, frontZ),
            new Vec3Mm(doorX + IntakeDoorOffsetXMm, GroundMm + IntakeHeightMm, frontZ),
            new Vec3Mm(PadCentreXMm, GroundMm, PadCentreZMm));
    }
}
