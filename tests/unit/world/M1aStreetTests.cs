using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Covers <see cref="M1aStreet"/> (world-stub story 006 AC-1 to AC-3; ADR-0026 section 8, Mail R3, TR-movement-005,
/// TR-movement-018, TR-mail-006): street contents, beach and ramp, and determinism. Built on the composition path
/// (<c>Build(EntityIds, slopeMaxPm, swimEnterMm)</c>) because the parameterless <c>Build()</c> rejects a Post Office.
/// The walkability limits and ids come from <see cref="WorldTablesFixture"/>. No magic coordinates: probe points are
/// <see cref="M1aStreet"/> constants.
/// </summary>
public class M1aStreetTests
{
    private static WorldTables BuildStreet()
    {
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        return b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    private static bool IsWalkable(IWorldQuery w, int tx, int tz) => (w.Tile(tx, tz) & TileFlags.Walkable) != 0;

    private static int TileOf(int coordMm) => coordMm / WorldConstants.TileSizeMm;

    // ---- AC-1: street contents ----

    [Fact]
    public void Ac1_AuthorAndBuild_Succeeds_AndMapIsWithin128By64Tiles()
    {
        WorldTables w = BuildStreet();

        Assert.True(w.TilesX <= M1aStreet.MaxTilesX);
        Assert.True(w.TilesZ <= M1aStreet.MaxTilesZ);
        Assert.Equal(128, M1aStreet.MaxTilesX);
        Assert.Equal(64, M1aStreet.MaxTilesZ);
        Assert.Equal(M1aStreet.TilesX, w.TilesX);
        Assert.Equal(M1aStreet.TilesZ, w.TilesZ);
        Assert.Equal(new MapBounds(0, 0, w.TilesX * 2000L, w.TilesZ * 2000L), w.Bounds);
    }

    [Fact]
    public void Ac1_Lots_AreTenNumbered1To10InAscendingAddressOrder_InDistrict1Street1()
    {
        WorldTables w = BuildStreet();

        Assert.Equal(M1aStreet.HouseCount, w.Lots.Length);
        Assert.Equal(10, w.Lots.Length);
        for (int i = 0; i < w.Lots.Length; i++)
        {
            LotRecord lot = w.Lots[i];
            Assert.Equal(AddressId.Pack(1, 1, i + 1, 0), lot.Address);
            Assert.Equal(M1aStreet.DistrictId, lot.DistrictId);
            Assert.Equal(M1aStreet.StreetId, lot.StreetId);
            Assert.Equal(i + 1, lot.Number);
            if (i > 0)
            {
                Assert.True(w.Lots[i - 1].Address.Value < lot.Address.Value);
            }
        }
    }

    [Fact]
    public void Ac1_OddAndEvenNumbers_StandOnOppositeSidesOfTheStreetCentreLine()
    {
        WorldTables w = BuildStreet();

        for (int i = 0; i < w.Lots.Length; i++)
        {
            LotRecord lot = w.Lots[i];
            bool odd = (lot.Number & 1) == 1;
            if (odd)
            {
                Assert.True(lot.Anchor.Z < M1aStreet.StreetCentreZMm, "odd lot " + lot.Number + " is on the -Z side");
                Assert.Equal(FaceDir.PosZ, lot.Face);
            }
            else
            {
                Assert.True(lot.Anchor.Z > M1aStreet.StreetCentreZMm, "even lot " + lot.Number + " is on the +Z side");
                Assert.Equal(FaceDir.NegZ, lot.Face);
            }
        }
    }

    [Fact]
    public void Ac1_MailboxSpacing_SameSideAtLeast6m_OppositeSideStaggerAtLeast3m()
    {
        WorldTables w = BuildStreet();

        for (int i = 0; i < w.Lots.Length; i++)
        {
            for (int j = i + 1; j < w.Lots.Length; j++)
            {
                Vec3Mm a = w.Lots[i].Anchor; // anchors are the face points moved 300 mm along Z: X spacing is unchanged
                Vec3Mm b = w.Lots[j].Anchor;
                bool sameSide = ((w.Lots[i].Number ^ w.Lots[j].Number) & 1) == 0;
                int along = Math.Abs(a.X - b.X);
                if (sameSide)
                {
                    Assert.True(along >= WorldTablesBuilder.MinSameSideMailboxSpacingMm, "lots " + w.Lots[i].Number + "/" + w.Lots[j].Number);
                }
                else
                {
                    Assert.True(along >= WorldTablesBuilder.MinOppositeSideStaggerMm, "lots " + w.Lots[i].Number + "/" + w.Lots[j].Number);
                }
            }
        }

        Assert.True(M1aStreet.LotPitchMm >= WorldTablesBuilder.MinSameSideMailboxSpacingMm);
        Assert.True(M1aStreet.EvenSideShiftMm >= WorldTablesBuilder.MinOppositeSideStaggerMm);
    }

    [Fact]
    public void Ac1_Solids_OneHouseAndOneRoofPerLot_PlusOnePostOfficeSolid()
    {
        WorldTables w = BuildStreet();

        int houses = 0;
        int roofs = 0;
        int postOffices = 0;
        for (int i = 0; i < w.StaticSolids.Length; i++)
        {
            switch (w.StaticSolids[i].Kind)
            {
                case SolidKind.House:
                    houses++;
                    break;
                case SolidKind.Roof:
                    roofs++;
                    break;
                case SolidKind.PostOffice:
                    postOffices++;
                    break;
            }
        }

        Assert.Equal(10, houses);
        Assert.Equal(10, roofs);
        Assert.Equal(1, postOffices);
        Assert.Equal(21, w.StaticSolids.Length);
    }

    [Fact]
    public void Ac1_EachLot_HasItsOwnHouseSolid_OwnedByItsDestination_WithAnchorOnTheStreetSideOfTheHouse()
    {
        WorldTables w = BuildStreet();

        var seen = new HashSet<ushort>();
        for (int i = 0; i < w.Lots.Length; i++)
        {
            LotRecord lot = w.Lots[i];
            Assert.True(seen.Add(lot.HouseSolid), "house solid shared by lot " + lot.Number);
            StaticSolid house = w.StaticSolids[lot.HouseSolid];
            Assert.Equal(SolidKind.House, house.Kind);
            Assert.Equal(lot.Destination, house.Owner);
            Assert.False(lot.Destination.IsNone);

            // The mailbox (anchor) is in the front yard: past the house's street-side wall, and not inside the house box.
            if ((lot.Number & 1) == 1)
            {
                Assert.True(lot.Anchor.Z > house.Box.Max.Z);
            }
            else
            {
                Assert.True(lot.Anchor.Z < house.Box.Min.Z);
            }

            // The house lies inside the lot's plot along the street, and the mailbox is centred on the plot.
            Assert.True(lot.Anchor.X > house.Box.Min.X && lot.Anchor.X < house.Box.Max.X);
        }
    }

    [Fact]
    public void Ac1_EachLot_HasOneRoofSolidDirectlyOnTopOfItsHouse()
    {
        WorldTables w = BuildStreet();

        for (int i = 0; i < w.Lots.Length; i++)
        {
            StaticSolid house = w.StaticSolids[w.Lots[i].HouseSolid];
            StaticSolid roof = w.StaticSolids[w.Lots[i].HouseSolid + 1];
            Assert.Equal(SolidKind.Roof, roof.Kind);
            Assert.Equal(house.Box.Max.Y, roof.Box.Min.Y);
            Assert.Equal(house.Box.Min.X, roof.Box.Min.X);
            Assert.Equal(house.Box.Max.X, roof.Box.Max.X);
            Assert.Equal(house.Box.Min.Z, roof.Box.Min.Z);
            Assert.Equal(house.Box.Max.Z, roof.Box.Max.Z);
        }
    }

    [Fact]
    public void Ac1_NeighbouringSameSideHouses_LeaveADoorwaySizedGap()
    {
        WorldTables w = BuildStreet();

        // Lots 1 and 3 are neighbours on the odd side; 2 and 4 on the even side (OQ23 observation point, not an AC).
        for (int number = 1; number <= 8; number++)
        {
            StaticSolid a = w.StaticSolids[w.Lots[number - 1].HouseSolid];
            StaticSolid b = w.StaticSolids[w.Lots[number + 1].HouseSolid];
            Assert.Equal(M1aStreet.HouseGapMm, b.Box.Min.X - a.Box.Max.X);
        }

        Assert.Equal(1000, M1aStreet.HouseGapMm);
    }

    [Fact]
    public void Ac1_PostOffice_PadHas8SlotsAt1200mm_AllOnWalkableDryGroundClearOfEverySolid()
    {
        WorldTables w = BuildStreet();
        PostOfficeRecord po = w.PostOffice;

        Assert.Equal(PadSlots.SlotCount, po.PadSlots);
        Assert.Equal(PadSlots.RingRadiusMm, po.PadRingRadiusMm);
        Assert.Equal(new Vec3Mm(M1aStreet.PadCentreXMm, M1aStreet.GroundMm, M1aStreet.PadCentreZMm), po.PadCentre);

        var hits = new ushort[w.StaticSolids.Length];
        for (int k = 0; k < PadSlots.SlotCount; k++)
        {
            Vec3Mm p = PadSlots.Position(w, in po, k);
            Assert.True(IsWalkable(w, TileOf(p.X), TileOf(p.Z)), "slot " + k + " tile walkable");
            Assert.True(w.WaterDepthMm(p.X, p.Z) < WorldTablesFixture.SwimEnterMm, "slot " + k + " dry");
            var point = new AabbMm(p, p);
            Assert.Equal(0, w.QueryStaticSolids(in point, hits));
        }
    }

    [Fact]
    public void Ac1_PostOffice_SolidSitsBetweenItsDoorAndTheStreet_AndTheDoorIsOutsideEverySolid()
    {
        WorldTables w = BuildStreet();
        PostOfficeRecord po = w.PostOffice;

        StaticSolid solid = default;
        for (int i = 0; i < w.StaticSolids.Length; i++)
        {
            if (w.StaticSolids[i].Kind == SolidKind.PostOffice)
            {
                solid = w.StaticSolids[i];
            }
        }

        Assert.True(po.DoorAnchor.Z > solid.Box.Max.Z, "door is on the street side");
        Assert.True(po.DoorAnchor.X > solid.Box.Min.X && po.DoorAnchor.X < solid.Box.Max.X);
        Assert.True(po.PadCentre.Z > solid.Box.Max.Z);
    }

    [Fact]
    public void Ac1_StreetNameAndAddressText_AreInTheWorldStringTable()
    {
        WorldTables w = BuildStreet();

        StringId name = w.StreetNameAt(M1aStreet.StreetMaxXMm / 2, M1aStreet.StreetCentreZMm);
        Assert.NotEqual(StringId.None, name);
        Assert.True(w.TryGetDisplayText(name, out string street));
        Assert.Equal(M1aStreet.StreetName, street);
        Assert.Equal(StringId.None, w.StreetNameAt(M1aStreet.StreetMaxXMm / 2, M1aStreet.StreetMinZMm - 1));

        for (int i = 0; i < w.Lots.Length; i++)
        {
            Assert.True(w.TryGetDisplayText(w.Lots[i].Display, out string text));
            Assert.Equal((i + 1) + " " + M1aStreet.StreetName, text);
        }
    }

    // ---- AC-2: beach and ramp ----

    [Fact]
    public void Ac2_ShallowProbe_HasWaterDepthAbove0AndBelow900_AndItsTileIsWalkable()
    {
        WorldTables w = BuildStreet();

        int depth = w.WaterDepthMm(M1aStreet.BeachShallowXMm, M1aStreet.BeachShallowZMm);

        Assert.InRange(depth, 1, WorldTablesFixture.SwimEnterMm - 1);
        Assert.Equal(500, depth);
        Assert.True(IsWalkable(w, TileOf(M1aStreet.BeachShallowXMm), TileOf(M1aStreet.BeachShallowZMm)));
    }

    [Fact]
    public void Ac2_DeepProbe_HasWaterDepthAtLeast900_AndItsTileIsNotWalkable()
    {
        WorldTables w = BuildStreet();

        int depth = w.WaterDepthMm(M1aStreet.BeachDeepXMm, M1aStreet.BeachDeepZMm);

        Assert.True(depth >= WorldTablesFixture.SwimEnterMm);
        Assert.Equal(2000, depth);
        Assert.False(IsWalkable(w, TileOf(M1aStreet.BeachDeepXMm), TileOf(M1aStreet.BeachDeepZMm)));
    }

    [Fact]
    public void Ac2_Beach_ReadsWaterFromTheHeightFieldOnly_AndHousesAndPadAreDry()
    {
        WorldTables w = BuildStreet();

        Assert.Equal(0, w.WaterDepthMm(M1aStreet.PadCentreXMm, M1aStreet.PadCentreZMm));
        for (int i = 0; i < w.Lots.Length; i++)
        {
            Assert.Equal(0, w.WaterDepthMm(w.Lots[i].Anchor.X, w.Lots[i].Anchor.Z));
        }

        Assert.True(w.TerrainHeightMm(M1aStreet.BeachShallowXMm, M1aStreet.BeachShallowZMm) < 0);
        Assert.True(w.TerrainHeightMm(M1aStreet.BeachDeepXMm, M1aStreet.BeachDeepZMm) < 0);
    }

    [Fact]
    public void Ac2_RampTile_Rises1400mmOver2000mm_At700Permille_AndIsWalkable()
    {
        WorldTables w = BuildStreet();

        w.GetCorners(M1aStreet.RampTileX, M1aStreet.RampTileZ, out HeightCorners h);
        Assert.Equal(M1aStreet.RampRiseMm, h.H10 - h.H00);
        Assert.Equal(M1aStreet.RampRiseMm, h.H11 - h.H01);
        Assert.Equal(1400, M1aStreet.RampRiseMm);
        Assert.Equal(2000, WorldConstants.TileSizeMm);

        int cx = (M1aStreet.RampTileX * WorldConstants.TileSizeMm) + (WorldConstants.TileSizeMm / 2);
        int cz = (M1aStreet.RampTileZ * WorldConstants.TileSizeMm) + (WorldConstants.TileSizeMm / 2);
        w.TerrainGradientPm(cx, cz, out int gx, out int gz);
        Assert.Equal(700, gx);
        Assert.Equal(0, gz);
        Assert.True(IsWalkable(w, M1aStreet.RampTileX, M1aStreet.RampTileZ));
    }

    [Fact]
    public void Ac2_Street_HasExactlyOneTileRising1400mmAlongX()
    {
        WorldTables w = BuildStreet();

        int rising = 0;
        for (int tz = 0; tz < w.TilesZ; tz++)
        {
            for (int tx = 0; tx < w.TilesX; tx++)
            {
                w.GetCorners(tx, tz, out HeightCorners h);
                if (h.H10 - h.H00 == M1aStreet.RampRiseMm && h.H11 - h.H01 == M1aStreet.RampRiseMm)
                {
                    rising++;
                    Assert.Equal(M1aStreet.RampTileX, tx);
                    Assert.Equal(M1aStreet.RampTileZ, tz);
                }
            }
        }

        Assert.Equal(1, rising);
    }

    [Fact]
    public void Ac2_RampRidge_WarpsOnlyTheFourTilesAroundIt_AndTheWarpedRowOneTilesAreNotWalkable()
    {
        WorldTables w = BuildStreet();

        int x0 = M1aStreet.RampTileX;
        int z0 = M1aStreet.RampTileZ;
        for (int tz = 0; tz < w.TilesZ; tz++)
        {
            for (int tx = 0; tx < M1aStreet.BeachShoreCornerX; tx++)
            {
                w.GetCorners(tx, tz, out HeightCorners h);
                bool flat = h.H00 == M1aStreet.GroundMm && h.H10 == M1aStreet.GroundMm
                    && h.H01 == M1aStreet.GroundMm && h.H11 == M1aStreet.GroundMm;
                bool nearRidge = (tx == x0 || tx == x0 + 1) && (tz == z0 || tz == z0 + 1);
                Assert.True(flat != nearRidge, "tile " + tx + "," + tz);
            }
        }

        // The raised corner tilts the row-1 tiles in both X and Z (about 990 permille combined), past the 700 limit.
        // Pinned so a slope-rule change is noticed; the ridge is ~26 m from the street, off every gameplay path.
        Assert.False(IsWalkable(w, x0, z0 + 1));
        Assert.False(IsWalkable(w, x0 + 1, z0 + 1));
        Assert.True(IsWalkable(w, x0, z0));
    }

    [Fact]
    public void Ac1_LotMailboxAndApproachTiles_AreWalkable_AndApproachTilesAreRoad()
    {
        WorldTables w = BuildStreet();

        // Every lot's mailbox tile and approach tile is walkable ground (couriers can reach it).
        for (int i = 0; i < w.Lots.Length; i++)
        {
            Assert.True(IsWalkable(w, w.Lots[i].MailboxTile.Tx, w.Lots[i].MailboxTile.Tz), "mailbox tile of lot " + w.Lots[i].Number);
            Assert.True(IsWalkable(w, w.Lots[i].ApproachTile.Tx, w.Lots[i].ApproachTile.Tz), "approach tile of lot " + w.Lots[i].Number);
            Assert.NotEqual(TileFlags.None, w.Tile(w.Lots[i].ApproachTile.Tx, w.Lots[i].ApproachTile.Tz) & TileFlags.Road);
        }
    }

    // ---- AC-3: determinism ----

    [Fact]
    public void Ac3_TwoIndependentAuthorAndBuildRuns_ProduceIdenticalTables()
    {
        WorldTables a = BuildStreet();
        WorldTables b = BuildStreet();

        Assert.Equal(a.TilesX, b.TilesX);
        Assert.Equal(a.TilesZ, b.TilesZ);
        Assert.Equal(a.Bounds, b.Bounds);
        for (int tz = 0; tz < a.TilesZ; tz++)
        {
            for (int tx = 0; tx < a.TilesX; tx++)
            {
                a.GetCorners(tx, tz, out HeightCorners ha);
                b.GetCorners(tx, tz, out HeightCorners hb);
                Assert.Equal(ha, hb);
                Assert.Equal(a.Tile(tx, tz), b.Tile(tx, tz));
            }
        }

        Assert.Equal(a.StaticSolids.Length, b.StaticSolids.Length);
        for (int i = 0; i < a.StaticSolids.Length; i++)
        {
            Assert.Equal(a.StaticSolids[i], b.StaticSolids[i]);
        }

        Assert.Equal(a.Lots.Length, b.Lots.Length);
        for (int i = 0; i < a.Lots.Length; i++)
        {
            Assert.Equal(a.Lots[i], b.Lots[i]);
            Assert.True(a.TryGetDisplayText(a.Lots[i].Display, out string ta));
            Assert.True(b.TryGetDisplayText(b.Lots[i].Display, out string tb));
            Assert.Equal(ta, tb);
        }

        Assert.Equal(a.PostOffice, b.PostOffice);
    }

    [Fact]
    public void Ac3_Version_IsAPublicConstStartingAt1()
    {
        System.Reflection.FieldInfo? field = typeof(M1aStreet).GetField(nameof(M1aStreet.Version));

        Assert.NotNull(field);
        Assert.True(field!.IsLiteral && field.IsPublic);
        Assert.Equal(1, M1aStreet.Version);
    }
}
