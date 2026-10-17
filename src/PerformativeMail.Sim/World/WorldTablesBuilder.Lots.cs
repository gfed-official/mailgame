using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.World;

/// <summary>
/// Streets, lots and world display strings (world-stub story 004; ADR-0026 section 6, ADR-0017 section 1, Mail R3/R10,
/// Simulation Core R19). Nothing here references a Mail type: Mail builds its destination registry from
/// <see cref="IWorldQuery.Lots"/>.
/// </summary>
public sealed partial class WorldTablesBuilder
{
    /// <summary>Mail R3 (OQ15): least Euclidean X/Z distance in mm between two mailbox face points on the same side of one street. A world-build guarantee, not a balance key.</summary>
    public const int MinSameSideMailboxSpacingMm = 6000;

    /// <summary>Mail R3 (OQ15): least stagger in mm along the street axis between two mailbox face points on opposite sides of one street. A world-build guarantee, not a balance key.</summary>
    public const int MinOppositeSideStaggerMm = 3000;

    /// <summary>Mail R3: the catch anchor sits this far (mm) in front of the mailbox face, along the lot's <see cref="FaceDir"/>.</summary>
    public const int MailboxAnchorOffsetMm = 300;

    /// <summary>Mail R3: the catch anchor sits this far (mm) above the terrain at its own X/Z.</summary>
    public const int MailboxAnchorHeightMm = 1000;

    private readonly List<WorldStreet> _streets = new();
    private readonly List<PendingLot> _lots = new();
    private readonly List<string> _displayText = new();

    /// <summary>
    /// Adds a display text (an address label or street name) to the world's immutable string table and returns its
    /// <see cref="StringId"/> in <see cref="StringNamespace.Run"/>, numbered from 0 in add order. Texts are stored as given and
    /// never deduplicated or formatted; Presentation reads them back with <see cref="WorldTables.TryGetDisplayText"/>.
    /// </summary>
    /// <param name="text">The text (not null).</param>
    /// <returns>The new string's id.</returns>
    /// <exception cref="SimBuildException"><paramref name="text"/> is null or the table already holds 65,535 strings (<see cref="SimBuildErrorCode.WorldDisplayStringInvalid"/>).</exception>
    /// <example><code>StringId name = builder.AddDisplayString("Harbour Lane");   // (Run, 0) for the first string</code></example>
    public StringId AddDisplayString(string text)
    {
        if (text is null || _displayText.Count >= ushort.MaxValue)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldDisplayStringInvalid, "WorldTablesBuilder.AddDisplayString: text is null or the world string table is full (" + ushort.MaxValue + ").");
        }

        _displayText.Add(text);
        return new StringId(StringNamespace.Run, (ushort)(_displayText.Count - 1));
    }

    /// <summary>
    /// Registers a street centre strip: its X/Z footprint answers <see cref="IWorldQuery.StreetNameAt"/>, and its
    /// <paramref name="axis"/> and centre line decide "side" and "along the street" in the Mail R3 spacing check for the
    /// lots whose address has this district and street. Validated at <c>Build</c> (district 1 to 63, Min &lt; Max on X and Z,
    /// a known display string, no repeated district and street).
    /// </summary>
    /// <param name="streetId">Street id within the district, 0 to 255.</param>
    /// <param name="districtId">District id, 1 to 63.</param>
    /// <param name="display">The street name from <see cref="AddDisplayString"/>.</param>
    /// <param name="footprint">The centre strip in mm; only X and Z are used.</param>
    /// <param name="axis">The axis the street runs along.</param>
    /// <example><code>builder.AddStreet(1, 1, name, new AabbMm(new Vec3Mm(0, 0, 4000), new Vec3Mm(64_000, 1, 8000)), StreetAxis.X);</code></example>
    public void AddStreet(byte streetId, byte districtId, StringId display, AabbMm footprint, StreetAxis axis)
    {
        _streets.Add(new WorldStreet(streetId, districtId, display, footprint, axis));
    }

    /// <summary>
    /// Adds a lot. The builder derives the district, street and number from <paramref name="address"/>, and the
    /// <see cref="LotRecord.Anchor"/> from the face point and <paramref name="face"/> (Mail R3: +300 mm along the face,
    /// y = ground + 1000 mm), so the anchor cannot drift from the rule. Everything is validated at <c>Build</c>.
    /// </summary>
    /// <param name="address">The packed address (district 0, including None, fails with <see cref="SimBuildErrorCode.AddressOutOfRange"/>).</param>
    /// <param name="display">The address label from <see cref="AddDisplayString"/>.</param>
    /// <param name="mailboxFacePoint">The mailbox face centre in mm; must lie strictly on one side of its street's centre line.</param>
    /// <param name="face">The lot's facing (one of the four axis directions).</param>
    /// <param name="mailboxTile">The tile holding the mailbox.</param>
    /// <param name="approachTile">The tile a courier approaches from.</param>
    /// <param name="truckStopPoint">The truck stop point in mm.</param>
    /// <param name="houseSolid">Index of the lot's <see cref="SolidKind.House"/> solid (from <see cref="AddSolid"/>); not shared with another lot.</param>
    /// <example><code>builder.AddLot(AddressId.Pack(1, 1, 3), label, new Vec3Mm(9000, 0, 9000), FaceDir.NegZ, new TileCoord(4, 4), new TileCoord(4, 3), new Vec3Mm(9000, 0, 6000), house);</code></example>
    public void AddLot(AddressId address, StringId display, Vec3Mm mailboxFacePoint, FaceDir face, TileCoord mailboxTile, TileCoord approachTile, Vec3Mm truckStopPoint, ushort houseSolid)
    {
        AddLot((int)address.District, (int)address.Street, (int)address.Number, (int)address.Unit, display, mailboxFacePoint, face, mailboxTile, approachTile, truckStopPoint, houseSolid);
    }

    /// <summary>
    /// As <see cref="AddLot(AddressId, StringId, Vec3Mm, FaceDir, TileCoord, TileCoord, Vec3Mm, ushort)"/> but from unpacked
    /// address components, so values that do not fit their bit field (district &gt; 63, street &gt; 255, number &gt; 4095,
    /// unit &gt; 63, or any negative) reach the builder and fail <c>Build</c> with <see cref="SimBuildErrorCode.AddressOutOfRange"/>
    /// instead of being masked by <see cref="AddressId.Pack"/>. Content and code-authored streets use this overload.
    /// </summary>
    /// <param name="district">District id, 1 to 63.</param>
    /// <param name="street">Street id, 0 to 255.</param>
    /// <param name="number">House number, 0 to 4095.</param>
    /// <param name="unit">Unit, 0 to 63 (0 = none).</param>
    /// <param name="display">The address label from <see cref="AddDisplayString"/>.</param>
    /// <param name="mailboxFacePoint">The mailbox face centre in mm.</param>
    /// <param name="face">The lot's facing.</param>
    /// <param name="mailboxTile">The tile holding the mailbox.</param>
    /// <param name="approachTile">The tile a courier approaches from.</param>
    /// <param name="truckStopPoint">The truck stop point in mm.</param>
    /// <param name="houseSolid">Index of the lot's house solid.</param>
    /// <example><code>builder.AddLot(1, 1, 3, 0, label, face, FaceDir.NegZ, tile, approach, stop, house);</code></example>
    public void AddLot(int district, int street, int number, int unit, StringId display, Vec3Mm mailboxFacePoint, FaceDir face, TileCoord mailboxTile, TileCoord approachTile, Vec3Mm truckStopPoint, ushort houseSolid)
    {
        _lots.Add(new PendingLot(district, street, number, unit, display, mailboxFacePoint, face, mailboxTile, approachTile, truckStopPoint, houseSolid));
    }

    private bool IsWorldString(StringId id) => id.Ns == StringNamespace.Run && id.Index < _displayText.Count;

    /// <summary>Validates the streets and returns them ascending by (district, street).</summary>
    private WorldStreet[] PrepareStreets()
    {
        WorldStreet[] streets = _streets.ToArray();
        int[] keys = new int[streets.Length];
        for (int i = 0; i < streets.Length; i++)
        {
            WorldStreet s = streets[i];
            if (s.DistrictId < 1 || s.DistrictId > AddressId.MaxDistrict)
            {
                throw new SimBuildException(SimBuildErrorCode.AddressOutOfRange, "WorldTablesBuilder.Build: street " + i + " (in add order) has district " + s.DistrictId + "; must be 1.." + AddressId.MaxDistrict + ".");
            }

            if (s.Footprint.Min.X >= s.Footprint.Max.X || s.Footprint.Min.Z >= s.Footprint.Max.Z || (s.Axis != StreetAxis.X && s.Axis != StreetAxis.Z))
            {
                throw new SimBuildException(SimBuildErrorCode.WorldStreetInvalid, "WorldTablesBuilder.Build: street " + i + " (in add order) has a footprint with Min >= Max on X or Z, or an undefined axis.");
            }

            if (!IsWorldString(s.Display))
            {
                throw new SimBuildException(SimBuildErrorCode.WorldDisplayStringInvalid, "WorldTablesBuilder.Build: street " + i + " (in add order) display is not a world display string.");
            }

            keys[i] = s.Key;
        }

        SortByKey(keys, streets);
        for (int i = 1; i < keys.Length; i++)
        {
            if (keys[i] == keys[i - 1])
            {
                throw new SimBuildException(SimBuildErrorCode.WorldStreetInvalid, "WorldTablesBuilder.Build: district " + (keys[i] >> 8) + " street " + (keys[i] & 0xFF) + " is registered twice.");
            }
        }

        return streets;
    }

    /// <summary>Validates the lots (allocating nothing) and returns them in lot order: ascending plain <c>uint</c> address value.</summary>
    private PendingLot[] PrepareLots(EntityIds? ids, WorldStreet[] streets, StaticSolid[] solids, in MapBounds bounds)
    {
        if (_lots.Count == 0)
        {
            return Array.Empty<PendingLot>();
        }

        if (ids is null)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldLotsNeedEntityIds, "WorldTablesBuilder.Build: " + _lots.Count + " lots were added; build with Build(EntityIds, slopeMaxPm, swimEnterMm) so their Destination ids can be allocated.");
        }

        PendingLot[] lots = _lots.ToArray();
        uint[] keys = new uint[lots.Length];
        for (int i = 0; i < lots.Length; i++)
        {
            ValidateAddressRange(in lots[i], i);
            keys[i] = AddressId.Pack(lots[i].District, lots[i].Street, lots[i].Number, lots[i].Unit).Value;
        }

        SortByKey(keys, lots); // plain uint order, stable; keys are checked unique below
        var houseUsed = new bool[solids.Length];
        for (int i = 0; i < lots.Length; i++)
        {
            if (i > 0 && keys[i] == keys[i - 1])
            {
                throw new SimBuildException(SimBuildErrorCode.DuplicateAddress, "WorldTablesBuilder.Build: address 0x" + keys[i].ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + " is used by two lots.");
            }

            ValidateLot(in lots[i], i, streets, solids, houseUsed, in bounds);
        }

        ValidateSpacing(lots, keys, streets);
        return lots;
    }

    private static void ValidateAddressRange(in PendingLot lot, int addIndex)
    {
        if (lot.District < 1 || lot.District > AddressId.MaxDistrict
            || lot.Street < 0 || lot.Street > AddressId.MaxStreet
            || lot.Number < 0 || lot.Number > AddressId.MaxNumber
            || lot.Unit < 0 || lot.Unit > AddressId.MaxUnit)
        {
            throw new SimBuildException(
                SimBuildErrorCode.AddressOutOfRange,
                "WorldTablesBuilder.Build: lot " + addIndex + " (in add order) address (" + lot.District + ", " + lot.Street + ", " + lot.Number + ", " + lot.Unit
                + ") is out of range (district 1..63, street 0..255, number 0..4095, unit 0..63).");
        }
    }

    private void ValidateLot(in PendingLot lot, int lotIndex, WorldStreet[] streets, StaticSolid[] solids, bool[] houseUsed, in MapBounds bounds)
    {
        string where = "WorldTablesBuilder.Build: lot " + lotIndex + " (in lot order) ";
        if (!IsWorldString(lot.Display))
        {
            throw new SimBuildException(SimBuildErrorCode.WorldDisplayStringInvalid, where + "display is not a world display string.");
        }

        if (lot.Face > FaceDir.NegZ)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldLotInvalid, where + "face is not one of the four axis directions.");
        }

        int street = FindStreet(streets, (lot.District << 8) | lot.Street);
        if (street < 0)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldLotInvalid, where + "has no street registered for district " + lot.District + " street " + lot.Street + ".");
        }

        if (SideOffset2(lot.FacePoint, in streets[street]) == 0)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldLotInvalid, where + "has its mailbox face point on the street centre line, so its side is undefined.");
        }

        if (!InsideXz(lot.FacePoint, in bounds) || !InsideXz(lot.TruckStop, in bounds) || !InsideGrid(lot.MailboxTile) || !InsideGrid(lot.ApproachTile))
        {
            throw new SimBuildException(SimBuildErrorCode.WorldLotInvalid, where + "has a face point, truck stop or tile outside the map.");
        }

        if (lot.HouseSolid >= solids.Length || solids[lot.HouseSolid].Kind != SolidKind.House || houseUsed[lot.HouseSolid])
        {
            throw new SimBuildException(SimBuildErrorCode.WorldLotInvalid, where + "house solid " + lot.HouseSolid + " is out of range, not SolidKind.House, or already used by another lot.");
        }

        houseUsed[lot.HouseSolid] = true;
    }

    private bool InsideGrid(TileCoord t) => t.Tx >= 0 && t.Tx < _tilesX && t.Tz >= 0 && t.Tz < _tilesZ;

    private static bool InsideXz(Vec3Mm p, in MapBounds b) =>
        p.X >= b.MinXMm && p.X <= b.MaxXMm && p.Z >= b.MinZMm && p.Z <= b.MaxZMm;

    private static int FindStreet(WorldStreet[] streets, int key)
    {
        for (int i = 0; i < streets.Length; i++)
        {
            if (streets[i].Key == key)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Mail R3 over every pair of lots on the same street (contiguous in lot order, since district and street are the
    /// address's high bits). Side = sign of the doubled perpendicular offset of the face point from the street centre line.
    /// </summary>
    private static void ValidateSpacing(PendingLot[] lots, uint[] keys, WorldStreet[] streets)
    {
        int start = 0;
        while (start < lots.Length)
        {
            int key = (lots[start].District << 8) | lots[start].Street;
            int end = start + 1;
            while (end < lots.Length && ((lots[end].District << 8) | lots[end].Street) == key)
            {
                end++;
            }

            WorldStreet street = streets[FindStreet(streets, key)];
            for (int i = start; i < end; i++)
            {
                for (int j = i + 1; j < end; j++)
                {
                    CheckPair(in lots[i], in lots[j], in street, keys[i], keys[j]);
                }
            }

            start = end;
        }
    }

    private static void CheckPair(in PendingLot a, in PendingLot b, in WorldStreet street, uint keyA, uint keyB)
    {
        long offA = SideOffset2(a.FacePoint, in street); // never 0: ValidateLot rejects centre-line face points
        long offB = SideOffset2(b.FacePoint, in street);
        long dx = (long)a.FacePoint.X - b.FacePoint.X;
        long dz = (long)a.FacePoint.Z - b.FacePoint.Z;
        if ((offA > 0) == (offB > 0))
        {
            const long MinSq = (long)MinSameSideMailboxSpacingMm * MinSameSideMailboxSpacingMm;
            if ((dx * dx) + (dz * dz) < MinSq)
            {
                throw new SimBuildException(SimBuildErrorCode.WorldMailboxSpacingTooSmall, "WorldTablesBuilder.Build: same-side mailboxes of lots 0x" + Hex(keyA) + " and 0x" + Hex(keyB) + " are closer than " + MinSameSideMailboxSpacingMm + " mm (Mail R3).");
            }

            return;
        }

        long along = street.Axis == StreetAxis.X ? dx : dz;
        if (along < MinOppositeSideStaggerMm && along > -MinOppositeSideStaggerMm)
        {
            throw new SimBuildException(SimBuildErrorCode.WorldMailboxSpacingTooSmall, "WorldTablesBuilder.Build: opposite-side mailboxes of lots 0x" + Hex(keyA) + " and 0x" + Hex(keyB) + " are staggered less than " + MinOppositeSideStaggerMm + " mm along the street (Mail R3).");
        }
    }

    /// <summary>Twice the signed perpendicular offset of <paramref name="p"/> from the street centre line (exact in integers).</summary>
    private static long SideOffset2(Vec3Mm p, in WorldStreet s)
    {
        AabbMm f = s.Footprint;
        return s.Axis == StreetAxis.X
            ? (2L * p.Z) - ((long)f.Min.Z + f.Max.Z)
            : (2L * p.X) - ((long)f.Min.X + f.Max.X);
    }

    /// <summary>
    /// Stable insertion sort of <paramref name="items"/> by the parallel plain-integer <paramref name="keys"/> (no comparer
    /// object; ADR-0006 rule 5 bans <c>Array.Sort</c>). O(n^2) worst case, fine for build-time lot and street counts.
    /// </summary>
    private static void SortByKey<TKey, TItem>(TKey[] keys, TItem[] items)
        where TKey : struct, IComparable<TKey>
    {
        for (int i = 1; i < keys.Length; i++)
        {
            TKey k = keys[i];
            TItem item = items[i];
            int j = i - 1;
            while (j >= 0 && keys[j].CompareTo(k) > 0)
            {
                keys[j + 1] = keys[j];
                items[j + 1] = items[j];
                j--;
            }

            keys[j + 1] = k;
            items[j + 1] = item;
        }
    }

    private static string Hex(uint v) => v.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Allocates one Destination id per lot, in lot order, and patches each house solid's owner. Runs after every check.</summary>
    private static EntityId[] AllocateDestinations(EntityIds? ids, PendingLot[] lots, StaticSolid[] solids)
    {
        if (lots.Length == 0 || ids is null)
        {
            return Array.Empty<EntityId>();
        }

        var destinations = new EntityId[lots.Length];
        for (int i = 0; i < lots.Length; i++)
        {
            EntityId id = ids.Allocate(EntityClass.Destination);
            if (id.IsNone)
            {
                throw new SimBuildException(SimBuildErrorCode.WorldDestinationIdRefused, "WorldTablesBuilder.Build: EntityIds refused the Destination id for lot " + i + " (allocate only while the world is Constructed, on its owner thread).");
            }

            destinations[i] = id;
            StaticSolid house = solids[lots[i].HouseSolid];
            solids[lots[i].HouseSolid] = new StaticSolid(house.Index, id, house.Box, house.Kind);
        }

        return destinations;
    }

    /// <summary>Writes the final records; the anchor height reads the finished height field.</summary>
    private static void FillLotRecords(WorldTables tables, PendingLot[] lots, EntityId[] destinations, LotRecord[] records)
    {
        for (int i = 0; i < lots.Length; i++)
        {
            PendingLot l = lots[i];
            int ax = l.FacePoint.X + (l.Face == FaceDir.PosX ? MailboxAnchorOffsetMm : (l.Face == FaceDir.NegX ? -MailboxAnchorOffsetMm : 0));
            int az = l.FacePoint.Z + (l.Face == FaceDir.PosZ ? MailboxAnchorOffsetMm : (l.Face == FaceDir.NegZ ? -MailboxAnchorOffsetMm : 0));
            int ay = (int)tables.TerrainHeightMm(ax, az) + MailboxAnchorHeightMm;
            records[i] = new LotRecord(
                destinations[i],
                AddressId.Pack(l.District, l.Street, l.Number, l.Unit),
                (byte)l.District,
                (byte)l.Street,
                (ushort)l.Number,
                l.Display,
                l.MailboxTile,
                l.Face,
                new Vec3Mm(ax, ay, az),
                l.ApproachTile,
                l.TruckStop,
                l.HouseSolid);
        }
    }

    private readonly struct PendingLot
    {
        public readonly int District;
        public readonly int Street;
        public readonly int Number;
        public readonly int Unit;
        public readonly StringId Display;
        public readonly Vec3Mm FacePoint;
        public readonly FaceDir Face;
        public readonly TileCoord MailboxTile;
        public readonly TileCoord ApproachTile;
        public readonly Vec3Mm TruckStop;
        public readonly ushort HouseSolid;

        public PendingLot(int district, int street, int number, int unit, StringId display, Vec3Mm facePoint, FaceDir face, TileCoord mailboxTile, TileCoord approachTile, Vec3Mm truckStop, ushort houseSolid)
        {
            District = district;
            Street = street;
            Number = number;
            Unit = unit;
            Display = display;
            FacePoint = facePoint;
            Face = face;
            MailboxTile = mailboxTile;
            ApproachTile = approachTile;
            TruckStop = truckStop;
            HouseSolid = houseSolid;
        }
    }
}
