using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Covers the Post Office record and spawn-pad slots (world-stub story 005; ADR-0026 section 7, ADR-0013 section 3,
/// PC P17 / FX-Pad): AC-1 slot geometry through <see cref="PadSlots.Position"/> and AC-2 build-time validation in
/// <see cref="WorldTablesBuilder"/>. All fixtures are hand-authored and deterministic.
/// </summary>
public class PostOfficePadTests
{
    // Pad used by the validation fixtures: an 8 x 8 map ([0, 16000]^2), pad centre (9000, 0, 9000). Slot tiles:
    // 0 (4,3), 1 (4,4), 2 (3,4), 3 (4,4), 4 (4,5), 5 (4,4), 6 (5,4), 7 (4,4). Slot 6 is (10200, y, 9000).
    private const int PadX = 9000;
    private const int PadZ = 9000;
    private const int Slot6X = PadX + 1200;

    /// <summary>
    /// FX-Pad slot offsets (dx, dz) from the centre: forward(k * 8192) = (-sin, -cos) scaled by
    /// MulDivRH(1200, f, 32767); TrigQ15's unit is 32767 and sin/cos 45 degrees is 23170, so the diagonal is
    /// round-half-away(1200 * 23170 / 32767) = round(848.54) = 849.
    /// </summary>
    private static readonly (int Dx, int Dz)[] _expectedOffsets =
    {
        (0, -1200), (-849, -849), (-1200, 0), (-849, 849), (0, 1200), (849, 849), (1200, 0), (849, -849),
    };

    private sealed class StateReader : ITickReader, IWorldStateReader
    {
        public SimState State => SimState.Constructed;

        public uint CurrentTick => 0;

        public TickStage Stage => TickStage.BetweenTicks;

        public PhaseId Phase => default;
    }

    private static EntityIds Ids()
    {
        var reader = new StateReader();
        return new EntityIds(reader, new SimCheckRing(), new SimCounters(), default, state: reader);
    }

    /// <summary>An 8 x 8 map whose corner (cx, cz) height is <c>height(cx, cz)</c>.</summary>
    private static WorldTablesBuilder Map(Func<int, int, int> height)
    {
        var corners = new int[9 * 9];
        for (int cz = 0; cz <= 8; cz++)
        {
            for (int cx = 0; cx <= 8; cx++)
            {
                corners[(cz * 9) + cx] = height(cx, cz);
            }
        }

        var b = new WorldTablesBuilder();
        b.SetCorners(8, 8, corners);
        return b;
    }

    private static WorldTablesBuilder FlatWithPad(int heightMm = 0)
    {
        WorldTablesBuilder b = Map((_, _) => heightMm);
        PadTestKit.SetValidPad(b, PadX, PadZ);
        return b;
    }

    private static AabbMm Box(int x0, int y0, int z0, int x1, int y1, int z1) =>
        new(new Vec3Mm(x0, y0, z0), new Vec3Mm(x1, y1, z1));

    private static SimBuildErrorCode BuildCode(Func<WorldTables> build) =>
        Assert.Throws<SimBuildException>(() => build()).Code;

    // ---- AC-1: slot positions ----

    [Fact]
    public void Ac1_FxPad_AtOrigin_SlotsSitOnRingAlongForwardOfKTimes45Degrees()
    {
        // Arrange (FX-Pad: centre (0, y, 0); flat ground at 0, so y is 0 and the record need not be validated)
        IWorldQuery w = Map((_, _) => 0).Build(700, 900);
        var po = new PostOfficeRecord(default, default, new Vec3Mm(0, 0, 0), PadSlots.RingRadiusMm, PadSlots.SlotCount);

        for (int k = 0; k < 8; k++)
        {
            // Act
            Vec3Mm p = PadSlots.Position(w, in po, k);

            // Assert
            Assert.Equal(_expectedOffsets[k].Dx, p.X);
            Assert.Equal(_expectedOffsets[k].Dz, p.Z);
            Assert.Equal(w.TerrainHeightMm(p.X, p.Z), p.Y);
            long d2 = ((long)p.X * p.X) + ((long)p.Z * p.Z);
            Assert.InRange(d2, 1199L * 1199, 1201L * 1201);
        }

        Assert.Equal(new Vec3Mm(0, 0, -1200), PadSlots.Position(w, in po, 0));
        Assert.Equal(new Vec3Mm(-1200, 0, 0), PadSlots.Position(w, in po, 2));
    }

    [Fact]
    public void Ac1_GentleSlope_SlotYIsTerrainHeightAtTheSlot_AndXzMatchTheRing()
    {
        // Arrange (planar slope: 100 mm per tile along X, 60 mm along Z, i.e. 50 and 30 permille; the build validates the pad)
        WorldTablesBuilder b = Map((cx, cz) => (100 * cx) + (60 * cz));
        b.SetPostOffice(new Vec3Mm(8000, 0, 4000), new Vec3Mm(8500, 1000, 4000), new Vec3Mm(8000, 777, 8000));
        IWorldQuery w = b.Build(700, 900);
        PostOfficeRecord po = w.PostOffice;

        for (int k = 0; k < 8; k++)
        {
            // Act
            Vec3Mm p = PadSlots.Position(w, in po, k);

            // Assert
            Assert.Equal(8000 + _expectedOffsets[k].Dx, p.X);
            Assert.Equal(8000 + _expectedOffsets[k].Dz, p.Z);
            Assert.Equal(w.TerrainHeightMm(p.X, p.Z), p.Y);
            var sink = default(SimCounterSink);
            Assert.Equal(Fix.MulDivRH((100L * p.X) + (60L * p.Z), 1, 2000, ref sink), p.Y); // exact planar height, rounded once
            long dx = p.X - 8000L;
            long dz = p.Z - 8000L;
            Assert.InRange((dx * dx) + (dz * dz), 1199L * 1199, 1201L * 1201);
        }

        Assert.NotEqual(PadSlots.Position(w, in po, 0).Y, PadSlots.Position(w, in po, 4).Y); // y follows the terrain, not PadCentre.Y
    }

    [Fact]
    public void Ac1_SlotIndex_WrapsWithTheBamAngle()
    {
        // Arrange
        IWorldQuery w = Map((_, _) => 0).Build(700, 900);
        var po = new PostOfficeRecord(default, default, new Vec3Mm(8000, 0, 8000), PadSlots.RingRadiusMm, PadSlots.SlotCount);

        // Act / Assert
        Assert.Equal(PadSlots.Position(w, in po, 0), PadSlots.Position(w, in po, 8));
        Assert.Equal(PadSlots.Position(w, in po, 7), PadSlots.Position(w, in po, -1));
        Assert.Equal(1200, PadSlots.RingRadiusMm);
        Assert.Equal(8, PadSlots.SlotCount);
        Assert.Equal(Bam.Turn / 8, PadSlots.SlotStepBam);
    }

    // ---- AC-2: happy path ----

    [Fact]
    public void Ac2_FlatDryPad_BuildsOnCompositionPath_AndPostOfficeReturnsTheRecordUnchanged()
    {
        // Arrange
        var door = new Vec3Mm(PadX, 0, PadZ - 2500);
        var intake = new Vec3Mm(PadX + 500, 1000, PadZ - 2500);
        var centre = new Vec3Mm(PadX, 123, PadZ);
        WorldTablesBuilder b = Map((_, _) => 0);
        b.AddSolid(EntityId.None, Box(PadX - 200, 0, PadZ - 200, PadX + 200, 3000, PadZ + 200), SolidKind.Prop); // a post at the centre is not a slot

        // Act
        b.SetPostOffice(door, intake, centre);
        WorldTables w = b.Build(Ids(), 700, 900);

        // Assert
        PostOfficeRecord po = w.PostOffice;
        Assert.Equal(new PostOfficeRecord(door, intake, centre, 1200, 8), po);
        Assert.True(po == new PostOfficeRecord(door, intake, centre, 1200, 8));
        Assert.False(po != new PostOfficeRecord(door, intake, centre, 1200, 8));
        Assert.Equal(door, po.DoorAnchor);
        Assert.Equal(intake, po.IntakeAnchor);
        Assert.Equal(centre, po.PadCentre);
        Assert.Equal(1200, po.PadRingRadiusMm);
        Assert.Equal((byte)8, po.PadSlots);
        Assert.Equal(po.GetHashCode(), new PostOfficeRecord(door, intake, centre, 1200, 8).GetHashCode());
        Assert.NotEqual(po, new PostOfficeRecord(door, intake, centre, 1201, 8));
    }

    [Fact]
    public void Ac2_LimitsOverload_ValidatesASetPostOffice_AndLeavesAnUnsetOneDefault()
    {
        // Arrange
        WorldTablesBuilder withPo = FlatWithPad();
        WorldTablesBuilder withoutPo = Map((_, _) => 0);

        // Act
        WorldTables a = withPo.Build(700, 900);
        WorldTables c = withoutPo.Build(700, 900);
        WorldTables d = withoutPo.Build();

        // Assert
        Assert.Equal(new Vec3Mm(PadX, 0, PadZ), a.PostOffice.PadCentre);
        Assert.Equal(default, c.PostOffice);
        Assert.Equal(default, d.PostOffice);
    }

    // ---- AC-2: slot validation ----

    [Theory]
    [InlineData(5, 4, false)] // slot 6's tile
    [InlineData(4, 4, false)] // slots 1, 3, 5, 7
    [InlineData(4, 3, false)] // slot 0
    [InlineData(6, 4, true)]  // next to the ring, no slot on it
    [InlineData(5, 5, true)]
    public void Ac2_SlotOnBlockedTile_FailsNotWalkable_NeighbourTileDoesNot(int tx, int tz, bool passes)
    {
        // Arrange
        WorldTablesBuilder b = FlatWithPad();
        b.MarkBlocked(tx, tz);

        // Act
        Exception? thrown = Record.Exception(() => b.Build(Ids(), 700, 900));

        // Assert
        if (passes)
        {
            Assert.Null(thrown);
        }
        else
        {
            Assert.Equal(SimBuildErrorCode.WorldPadSlotNotWalkable, Assert.IsType<SimBuildException>(thrown).Code);
        }
    }

    [Fact]
    public void Ac2_SlotOnSteepDryTile_FailsNotWalkable()
    {
        // Arrange (corner (6, 4) raised 3000 mm: 1500 permille on tiles (5,3), (6,3), (5,4), (6,4); only slot 6 is on one)
        WorldTablesBuilder b = Map((cx, cz) => cx == 6 && cz == 4 ? 3000 : 0);
        PadTestKit.SetValidPad(b, PadX, PadZ);

        // Act
        SimBuildErrorCode code = BuildCode(() => b.Build(Ids(), 700, 900));
        WorldTables loose = b.Build(2200, 900); // the same pad builds with a looser limit (corner gradient (1500, -1500): about 2121 permille)

        // Assert
        Assert.Equal(SimBuildErrorCode.WorldPadSlotNotWalkable, code);
        Assert.Equal(TileFlags.None, Map((cx, cz) => cx == 6 && cz == 4 ? 3000 : 0).Build(700, 900).Tile(5, 4)); // steep, not blocked, dry
        Assert.True((loose.Tile(5, 4) & TileFlags.Walkable) != 0);
    }

    [Theory]
    [InlineData(Slot6X, 0, PadZ - 500, Slot6X + 800, 2000, PadZ + 500, false)]       // min X face touches the slot (closed)
    [InlineData(Slot6X - 800, 0, PadZ - 500, Slot6X, 2000, PadZ + 500, false)]       // max X face touches the slot
    [InlineData(Slot6X - 800, -500, PadZ - 500, Slot6X + 800, 0, PadZ + 500, false)] // top face at ground height (closed Y)
    [InlineData(Slot6X - 800, 0, PadZ, Slot6X + 800, 2000, PadZ + 500, false)]       // min Z face touches the slot
    [InlineData(Slot6X + 1, 0, PadZ - 500, Slot6X + 800, 2000, PadZ + 500, true)]    // 1 mm clear on X
    [InlineData(Slot6X - 800, 1, PadZ - 500, Slot6X + 800, 2000, PadZ + 500, true)]  // overhang 1 mm above the slot
    [InlineData(Slot6X - 800, -500, PadZ - 500, Slot6X + 800, -1, PadZ + 500, true)] // 1 mm below the slot
    [InlineData(Slot6X - 800, 0, PadZ + 1, Slot6X + 800, 2000, PadZ + 500, true)]    // 1 mm clear on Z
    public void Ac2_SlotInsideStaticSolid_FailsInsideSolid_ClosedOnEveryAxis(int x0, int y0, int z0, int x1, int y1, int z1, bool passes)
    {
        // Arrange
        WorldTablesBuilder b = FlatWithPad();
        b.AddSolid(EntityId.None, Box(x0, y0, z0, x1, y1, z1), SolidKind.Prop);

        // Act
        Exception? thrown = Record.Exception(() => b.Build(Ids(), 700, 900));

        // Assert
        if (passes)
        {
            Assert.Null(thrown);
        }
        else
        {
            Assert.Equal(SimBuildErrorCode.WorldPadSlotInsideSolid, Assert.IsType<SimBuildException>(thrown).Code);
        }
    }

    [Fact]
    public void Ac2_SlotWaterDepth_899Passes_900FailsTooDeep_AgainstTheSwimEnterParameter()
    {
        // Arrange
        WorldTablesBuilder shallow = FlatWithPad(-899);
        WorldTablesBuilder deep = FlatWithPad(-900);

        // Act
        Exception? shallowThrown = Record.Exception(() => shallow.Build(Ids(), 700, 900));
        SimBuildErrorCode deepCode = BuildCode(() => deep.Build(Ids(), 700, 900));
        Exception? deepWithHigherLimit = Record.Exception(() => deep.Build(Ids(), 700, 1000)); // the limit is the parameter, not a literal 900
        SimBuildErrorCode shallowWithLowerLimit = BuildCode(() => shallow.Build(Ids(), 700, 899));

        // Assert
        Assert.Null(shallowThrown);
        Assert.Equal(SimBuildErrorCode.WorldPadSlotTooDeep, deepCode);
        Assert.Null(deepWithHigherLimit);
        Assert.Equal(SimBuildErrorCode.WorldPadSlotTooDeep, shallowWithLowerLimit);
    }

    [Fact]
    public void Ac2_OnlyEasternSlotsDeep_CentreAndSlot0Dry_FailsTooDeep()
    {
        // Arrange: a step along X only (corners at x >= 10000 sit at h, the rest at 0), so every tile stays walkable
        // (|h|/2000 per mille along X). With h = -1000 the centre (x 9000) is 500 mm deep and slot 0 (9000, 7800)
        // is 500 mm deep, but slots 5 and 7 (x 9849) are 924 mm and slot 6 (x 10200) 1000 mm deep. A builder that
        // checked only the centre or slot 0 would pass this pad.
        WorldTablesBuilder deep = Map((cx, _) => cx >= 5 ? -1000 : 0);
        PadTestKit.SetValidPad(deep, PadX, PadZ);
        WorldTablesBuilder shallow = Map((cx, _) => cx >= 5 ? -800 : 0); // deepest slot 800 mm
        PadTestKit.SetValidPad(shallow, PadX, PadZ);

        // Act
        SimBuildErrorCode deepCode = BuildCode(() => deep.Build(Ids(), 700, 900));
        Exception? shallowThrown = Record.Exception(() => shallow.Build(Ids(), 700, 900));

        // Assert
        Assert.Equal(SimBuildErrorCode.WorldPadSlotTooDeep, deepCode);
        Assert.Null(shallowThrown);
    }

    [Fact]
    public void Ac2_SlotFailures_AlsoFailTheTerrainOnlyBuildWithLimits()
    {
        // Arrange: Build(int, int) keeps the Post Office optional, but a Post Office that is set is still validated.
        WorldTablesBuilder deep = FlatWithPad(-900);
        WorldTablesBuilder blocked = FlatWithPad();
        blocked.MarkBlocked(5, 4); // slot 6's tile
        WorldTablesBuilder solid = FlatWithPad();
        solid.AddSolid(EntityId.None, Box(Slot6X - 100, -100, PadZ - 100, Slot6X + 100, 3000, PadZ + 100), SolidKind.Prop);

        // Act / Assert
        Assert.Equal(SimBuildErrorCode.WorldPadSlotTooDeep, BuildCode(() => deep.Build(700, 900)));
        Assert.Equal(SimBuildErrorCode.WorldPadSlotNotWalkable, BuildCode(() => blocked.Build(700, 900)));
        Assert.Equal(SimBuildErrorCode.WorldPadSlotInsideSolid, BuildCode(() => solid.Build(700, 900)));
    }

    [Fact]
    public void Ac2_SlotOffTheHeightGrid_FailsNotWalkable()
    {
        // Arrange (bounds wider than the grid: the anchors are in bounds, but slots 2 and 3 fall west of x = 0)
        WorldTablesBuilder b = Map((_, _) => 0);
        b.SetBounds(new MapBounds(-4000, 0, 16000, 16000));
        b.SetPostOffice(new Vec3Mm(500, 0, 4000), new Vec3Mm(500, 0, 4000), new Vec3Mm(500, 0, 8000));

        // Act / Assert
        Assert.Equal(SimBuildErrorCode.WorldPadSlotNotWalkable, BuildCode(() => b.Build(Ids(), 700, 900)));
    }

    [Fact]
    public void Ac2_SlotOnFarMapEdge_UsesTheLastTile()
    {
        // Arrange (slot 6 at x = 16000, the far edge: it belongs to tile 7, as in the height lookup)
        WorldTablesBuilder b = Map((_, _) => 0);
        b.SetPostOffice(new Vec3Mm(14800, 0, 4000), new Vec3Mm(14800, 0, 4000), new Vec3Mm(14800, 0, 8000));
        WorldTablesBuilder blocked = Map((_, _) => 0);
        blocked.SetPostOffice(new Vec3Mm(14800, 0, 4000), new Vec3Mm(14800, 0, 4000), new Vec3Mm(14800, 0, 8000));
        blocked.MarkBlocked(7, 4);

        // Act / Assert
        Assert.Null(Record.Exception(() => b.Build(Ids(), 700, 900)));
        Assert.Equal(SimBuildErrorCode.WorldPadSlotNotWalkable, BuildCode(() => blocked.Build(Ids(), 700, 900)));
    }

    // ---- AC-2: SetPostOffice calls and anchors ----

    [Fact]
    public void Ac2_SetPostOfficeNeverCalled_FailsOnCompositionPath()
    {
        // Arrange
        WorldTablesBuilder b = Map((_, _) => 0);

        // Act / Assert
        Assert.Equal(SimBuildErrorCode.WorldPostOfficeNotSet, BuildCode(() => b.Build(Ids(), 700, 900)));
    }

    [Fact]
    public void Ac2_SetPostOfficeCalledTwice_FailsOnEveryOverload_EvenWithTheSameValues()
    {
        // Arrange
        WorldTablesBuilder b = FlatWithPad();
        PadTestKit.SetValidPad(b, PadX, PadZ);

        // Act / Assert
        Assert.Equal(SimBuildErrorCode.WorldPostOfficeSetTwice, BuildCode(() => b.Build(Ids(), 700, 900)));
        Assert.Equal(SimBuildErrorCode.WorldPostOfficeSetTwice, BuildCode(() => b.Build(700, 900)));
        Assert.Equal(SimBuildErrorCode.WorldPostOfficeSetTwice, BuildCode(() => b.Build()));
    }

    [Fact]
    public void Ac2_PostOfficeOnParameterlessBuild_FailsNeedsWalkabilityLimits()
    {
        // Arrange
        WorldTablesBuilder b = FlatWithPad();

        // Act / Assert
        Assert.Equal(SimBuildErrorCode.WorldPostOfficeNeedsWalkabilityLimits, BuildCode(() => b.Build()));
    }

    [Theory]
    [InlineData(0, -1, 0, 6500)]       // door west of the map
    [InlineData(0, PadX, 0, 16001)]    // door south of the map (the only Z-max row)
    [InlineData(1, 16001, 0, 6500)]    // intake east of the map
    [InlineData(1, PadX, 1_000_001, 6500)] // intake |Y| over MaxCoordMm
    [InlineData(1, PadX, -1_000_001, 6500)]
    [InlineData(2, PadX, 0, -1)]       // pad centre north of the map
    public void Ac2_AnchorOutsideBounds_FailsAnchorOutsideBounds(int which, int x, int y, int z)
    {
        // Arrange
        var bad = new Vec3Mm(x, y, z);
        WorldTablesBuilder b = Map((_, _) => 0);
        b.SetPostOffice(
            which == 0 ? bad : PadTestKit.Door(PadX, PadZ),
            which == 1 ? bad : PadTestKit.Intake(PadX, PadZ),
            which == 2 ? bad : new Vec3Mm(PadX, 0, PadZ));

        // Act / Assert
        Assert.Equal(SimBuildErrorCode.WorldPostOfficeAnchorOutsideBounds, BuildCode(() => b.Build(Ids(), 700, 900)));
        Assert.Equal(SimBuildErrorCode.WorldPostOfficeAnchorOutsideBounds, BuildCode(() => b.Build(700, 900)));
    }

    [Fact]
    public void Ac2_AnchorsOnTheBoundsEdge_Pass()
    {
        // Arrange (closed bounds: the door and intake sit on opposite map corners)
        WorldTablesBuilder b = Map((_, _) => 0);
        b.SetPostOffice(new Vec3Mm(0, 0, 0), new Vec3Mm(16000, 1_000_000, 16000), new Vec3Mm(PadX, 0, PadZ));

        // Act / Assert
        Assert.Null(Record.Exception(() => b.Build(Ids(), 700, 900)));
    }

    [Fact]
    public void Ac2_FailedPad_AllocatesNoDestinationIds()
    {
        // Arrange (one valid lot; the pad's slot 6 is blocked, so the build fails before allocation)
        WorldTablesBuilder b = FlatWithPad();
        StringId street = b.AddDisplayString("Harbour Lane");
        b.AddStreet(1, 1, street, Box(0, 0, 1000, 16000, 1, 3000), StreetAxis.X);
        ushort house = b.AddSolid(EntityId.None, Box(1000, 0, 3500, 3000, 3000, 5500), SolidKind.House);
        b.AddLot(AddressId.Pack(1, 1, 1), b.AddDisplayString("1 Harbour Lane"), new Vec3Mm(2000, 0, 3500), FaceDir.NegZ, new TileCoord(1, 1), new TileCoord(1, 1), new Vec3Mm(2000, 0, 2000), house);
        b.MarkBlocked(5, 4);
        EntityIds ids = Ids();

        // Act
        SimBuildErrorCode code = BuildCode(() => b.Build(ids, 700, 900));
        EntityId next = ids.Allocate(EntityClass.Destination);

        // Assert
        Assert.Equal(SimBuildErrorCode.WorldPadSlotNotWalkable, code);
        Assert.Equal(1u, next.Counter);
    }

    [Fact]
    public void Ac2_PostOfficeErrorCodes_ArePinned()
    {
        Assert.Equal(37, (int)SimBuildErrorCode.WorldPostOfficeNotSet);
        Assert.Equal(38, (int)SimBuildErrorCode.WorldPostOfficeSetTwice);
        Assert.Equal(39, (int)SimBuildErrorCode.WorldPostOfficeAnchorOutsideBounds);
        Assert.Equal(40, (int)SimBuildErrorCode.WorldPadSlotNotWalkable);
        Assert.Equal(41, (int)SimBuildErrorCode.WorldPadSlotInsideSolid);
        Assert.Equal(42, (int)SimBuildErrorCode.WorldPadSlotTooDeep);
        Assert.Equal(43, (int)SimBuildErrorCode.WorldPostOfficeNeedsWalkabilityLimits);
    }
}
