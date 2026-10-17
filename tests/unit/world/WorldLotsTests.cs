using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.World;

/// <summary>
/// Covers the lot/address table of <see cref="WorldTablesBuilder"/> and <see cref="WorldTables"/> (world-stub story 004
/// AC-2 to AC-5; ADR-0026 section 6, Mail R3/R10, Simulation Core R19; TR-mail-006): lot order and Destination ids,
/// address validation, anchor derivation, the Mail R3 mailbox-spacing gate, <see cref="IWorldQuery.StreetNameAt"/> and
/// the world display-string table. All fixtures are hand-authored or fixed-seed.
/// </summary>
public class WorldLotsTests
{
    private const int DestinationClass = (int)EntityClass.Destination;

    // ---- fixtures ----

    /// <summary>A Constructed (or other-state) world for <see cref="EntityIds"/>: between ticks, never in a phase.</summary>
    private sealed class StateReader : ITickReader, IWorldStateReader
    {
        public StateReader(SimState state)
        {
            State = state;
        }

        public SimState State { get; }

        public uint CurrentTick => 0;

        public TickStage Stage => TickStage.BetweenTicks;

        public PhaseId Phase => default;
    }

    private static EntityIds Ids(SimState state = SimState.Constructed, uint lastDestination = 0)
    {
        var reader = new StateReader(state);
        var initial = new uint[256];
        initial[DestinationClass] = lastDestination;
        return new EntityIds(reader, new SimCheckRing(), new SimCounters(), initial, state: reader);
    }

    private static AabbMm Box(int x0, int y0, int z0, int x1, int y1, int z1) =>
        new AabbMm(new Vec3Mm(x0, y0, z0), new Vec3Mm(x1, y1, z1));

    /// <summary>
    /// 16 x 8 tiles ([0, 32000] x [0, 16000]). Corner (cx, cz) height = <c>slopeX * cx + slopeZ * cz</c> (planar, so
    /// the bilinear height is exact). Street A = district 1 street 1 along X, z in [7000, 9000] (centre 8000).
    /// Street B = district 1 street 2 along Z, x in [14000, 16000] (centre 15000), crossing A.
    /// </summary>
    private sealed class Rig
    {
        public Rig(int slopeX = 0, int slopeZ = 0)
        {
            var corners = new int[17 * 9];
            for (int cz = 0; cz <= 8; cz++)
            {
                for (int cx = 0; cx <= 16; cx++)
                {
                    corners[(cz * 17) + cx] = (slopeX * cx) + (slopeZ * cz);
                }
            }

            B.SetCorners(16, 8, corners);
            StreetA = B.AddDisplayString("Harbour Lane");
            StreetB = B.AddDisplayString("Quay Street");
            B.AddStreet(1, 1, StreetA, Box(0, 0, 7000, 32000, 1, 9000), StreetAxis.X);
            B.AddStreet(2, 1, StreetB, Box(14000, 0, 0, 16000, 1, 16000), StreetAxis.Z);
        }

        public WorldTablesBuilder B { get; } = new();

        public StringId StreetA { get; }

        public StringId StreetB { get; }

        public ushort House(int x, int z) =>
            B.AddSolid(EntityId.None, Box(x - 1000, 0, Math.Max(0, z - 1000), x + 1000, 3000, Math.Min(16000, z + 1000)), SolidKind.House);

        /// <summary>Adds a house at the face point and a lot whose mailbox face point is (x, 0, z).</summary>
        public void Lot(AddressId a, int x, int z, FaceDir face)
        {
            LotOn(a, x, z, face, House(x, z));
        }

        public void LotOn(AddressId a, int x, int z, FaceDir face, ushort house, StringId? label = null)
        {
            var tile = new TileCoord(Math.Min(x / 2000, 15), Math.Min(z / 2000, 7));
            B.AddLot(a, label ?? B.AddDisplayString("lot " + a.Number), new Vec3Mm(x, 0, z), face, tile, tile, new Vec3Mm(x, 0, 8000), house);
        }

        public void LotParts(int d, int s, int n, int u, int x, int z)
        {
            var tile = new TileCoord(x / 2000, z / 2000);
            B.AddLot(d, s, n, u, B.AddDisplayString("lot"), new Vec3Mm(x, 0, z), FaceDir.NegZ, tile, tile, new Vec3Mm(x, 0, 8000), House(x, z));
        }

        /// <summary>Builds on the composition path; adds the shared valid Post Office first (once per rig, so a rig may be rebuilt).</summary>
        public WorldTables Build(EntityIds ids)
        {
            if (!_padSet)
            {
                PadTestKit.SetValidPad(B, PadX, PadZ);
                _padSet = true;
            }

            return B.Build(ids, 700, 900);
        }

        // Pad ring x 22800..25200, z 12300..14700: in the map, off both streets' houses in every test here, and the
        // sloped rig (50 / 80 permille) stays walkable. Story 005 requires a Post Office on Build(EntityIds, ...).
        private const int PadX = 24000;
        private const int PadZ = 13500;
        private bool _padSet;
    }

    private static SimBuildException BuildFails(Rig rig, EntityIds? ids = null)
    {
        return Assert.Throws<SimBuildException>(() => rig.Build(ids ?? Ids()));
    }

    /// <summary>The 11-lot street of the order tests: (address, x, z, face). Same-side gaps 7000 mm, cross-side stagger 3500 mm.</summary>
    private static readonly (AddressId Address, int X, int Z, FaceDir Face)[] _street11 =
    {
        (AddressId.Pack(1, 1, 1), 2000, 10000, FaceDir.NegZ),
        (AddressId.Pack(1, 1, 3), 9000, 10000, FaceDir.NegZ),
        (AddressId.Pack(1, 1, 5), 16000, 10000, FaceDir.NegZ),
        (AddressId.Pack(1, 1, 7), 23000, 10000, FaceDir.NegZ),
        (AddressId.Pack(1, 1, 9), 30000, 10000, FaceDir.NegZ),
        (AddressId.Pack(1, 1, 2), 5500, 6000, FaceDir.PosZ),
        (AddressId.Pack(1, 1, 4), 12500, 6000, FaceDir.PosZ),
        (AddressId.Pack(1, 1, 6), 19500, 6000, FaceDir.PosZ),
        (AddressId.Pack(1, 1, 8), 26500, 6000, FaceDir.PosZ),
        (AddressId.Pack(1, 2, 1), 17000, 2000, FaceDir.NegX),
        (AddressId.Pack(1, 2, 2), 17000, 13000, FaceDir.NegX),
    };

    /// <summary>Builds <see cref="_street11"/>: houses and labels in table order, then a prop, then the lots in the order given by <paramref name="seed"/> (0 = table order).</summary>
    private static (WorldTables World, ushort[] Houses, ushort Prop) BuildStreet11(uint seed, EntityIds ids)
    {
        var rig = new Rig();
        var houses = new ushort[_street11.Length];
        var labels = new StringId[_street11.Length];
        for (int i = 0; i < _street11.Length; i++)
        {
            houses[i] = rig.House(_street11[i].X, _street11[i].Z);
            labels[i] = rig.B.AddDisplayString("lot " + i);
        }

        ushort prop = rig.B.AddSolid(EntityId.None, Box(100, 0, 100, 900, 900, 900), SolidKind.Prop);
        int[] order = new int[_street11.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        if (seed != 0)
        {
            var rng = new Pcg32(seed, 11);
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = (int)(rng.Next32() % (uint)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
        }

        foreach (int i in order)
        {
            rig.LotOn(_street11[i].Address, _street11[i].X, _street11[i].Z, _street11[i].Face, houses[i], labels[i]);
        }

        return (rig.Build(ids), houses, prop);
    }

    // ---- AC-2: lot order and destination ids ----

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(0xBEEFu)]
    public void Ac2_LotsAddedInAnyOrder_ComeOutAscendingAndIdenticalToTableOrderBuild(uint seed)
    {
        // Arrange / Act
        WorldTables reference = BuildStreet11(0, Ids()).World;
        WorldTables shuffled = BuildStreet11(seed, Ids()).World;

        // Assert
        Assert.Equal(_street11.Length, shuffled.Lots.Length);
        for (int i = 1; i < shuffled.Lots.Length; i++)
        {
            Assert.True(shuffled.Lots[i - 1].Address.Value < shuffled.Lots[i].Address.Value);
        }

        for (int i = 0; i < shuffled.Lots.Length; i++)
        {
            Assert.True(reference.Lots[i] == shuffled.Lots[i]);
            Assert.Equal(reference.StaticSolids[i], shuffled.StaticSolids[i]);
        }
    }

    [Fact]
    public void Ac2_DestinationIds_ClassDestinationContiguousInLotOrderFromFirstCounter()
    {
        // Arrange (5 Destination ids already issued, so the first lot gets counter 6)
        EntityIds ids = Ids(lastDestination: 5);

        // Act
        WorldTables w = BuildStreet11(7, ids).World;

        // Assert
        for (int i = 0; i < w.Lots.Length; i++)
        {
            EntityId d = w.Lots[i].Destination;
            Assert.Equal(EntityClass.Destination, d.Class);
            Assert.Equal(0x07, (int)d.Class);
            Assert.Equal(6u + (uint)i, d.Counter);
        }

        Assert.Equal(5u + (uint)_street11.Length, ids.Peek(EntityClass.Destination));
    }

    [Fact]
    public void Ac2_HouseSolid_IsKindHouseOwnedByItsLotsDestination_OtherSolidsUntouched()
    {
        // Arrange / Act
        (WorldTables w, ushort[] houses, ushort prop) = BuildStreet11(3, Ids());

        // Assert
        for (int i = 0; i < w.Lots.Length; i++)
        {
            LotRecord lot = w.Lots[i];
            int spec = Array.FindIndex(_street11, e => e.Address == lot.Address);
            Assert.Equal(houses[spec], lot.HouseSolid);
            StaticSolid house = w.StaticSolids[lot.HouseSolid];
            Assert.Equal(SolidKind.House, house.Kind);
            Assert.Equal(lot.Destination, house.Owner);
            Assert.Equal((byte)lot.Address.District, lot.DistrictId);
            Assert.Equal((byte)lot.Address.Street, lot.StreetId);
            Assert.Equal((ushort)lot.Address.Number, lot.Number);
        }

        Assert.Equal(EntityId.None, w.StaticSolids[prop].Owner);
    }

    [Fact]
    public void Ac2_LotsWithoutEntityIds_FailWithDistinctCode_OnBothLegacyOverloads()
    {
        // Arrange
        var rig = new Rig();
        rig.Lot(AddressId.Pack(1, 1, 1), 2000, 10000, FaceDir.NegZ);

        // Act
        var parameterless = Assert.Throws<SimBuildException>(() => rig.B.Build());
        var limits = Assert.Throws<SimBuildException>(() => rig.B.Build(700, 900));

        // Assert
        Assert.Equal(SimBuildErrorCode.WorldLotsNeedEntityIds, parameterless.Code);
        Assert.Equal(SimBuildErrorCode.WorldLotsNeedEntityIds, limits.Code);
    }

    [Fact]
    public void Ac2_WorldWithoutLots_BuildsWithoutEntityIds_AndHasNoLots()
    {
        // Arrange
        var rig = new Rig();

        // Act
        WorldTables w = rig.B.Build(700, 900);

        // Assert
        Assert.True(w.Lots.IsEmpty);
    }

    [Fact]
    public void Ac2_AllocationOutsideConstructed_IsRefused_AndBuildFails()
    {
        // Arrange
        var rig = new Rig();
        rig.Lot(AddressId.Pack(1, 1, 1), 2000, 10000, FaceDir.NegZ);
        EntityIds ready = Ids(SimState.Ready);

        // Act
        Exception? thrown = Record.Exception(() => rig.Build(ready));

        // Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            Assert.Equal(SimAssertCode.IdAllocIllegal, Assert.IsType<SimAssertException>(thrown).Code);
        }
        else
        {
            Assert.Equal(SimBuildErrorCode.WorldDestinationIdRefused, Assert.IsType<SimBuildException>(thrown).Code);
        }

        Assert.Equal(0u, ready.Peek(EntityClass.Destination));
    }

    [Fact]
    public void Ac2_SameInputs_BuildIdenticalTables()
    {
        // Arrange / Act
        WorldTables a = BuildStreet11(9, Ids()).World;
        WorldTables b = BuildStreet11(9, Ids()).World;

        // Assert
        for (int i = 0; i < a.Lots.Length; i++)
        {
            Assert.True(a.Lots[i].Equals(b.Lots[i]));
            Assert.True(a.Lots[i].Equals((object)b.Lots[i]));
            Assert.False(a.Lots[i] != b.Lots[i]);
            Assert.Equal(a.Lots[i].GetHashCode(), b.Lots[i].GetHashCode());
        }

        Assert.True(a.Lots[0] != a.Lots[1]);
    }

    // ---- AC-3: address validation ----

    [Fact]
    public void Ac3_DuplicateAddress_FailsWithDistinctCode_AndAllocatesNothing()
    {
        // Arrange
        var rig = new Rig();
        rig.Lot(AddressId.Pack(1, 1, 4), 3000, 10000, FaceDir.NegZ);
        rig.Lot(AddressId.Pack(1, 1, 4), 20000, 10000, FaceDir.NegZ);
        EntityIds ids = Ids();

        // Act
        SimBuildException e = BuildFails(rig, ids);

        // Assert
        Assert.Equal(SimBuildErrorCode.DuplicateAddress, e.Code);
        Assert.NotEqual(SimBuildErrorCode.AddressOutOfRange, e.Code);
        Assert.Equal(0u, ids.Peek(EntityClass.Destination));
    }

    [Theory]
    [InlineData(0, 1, 1, 0)]
    [InlineData(64, 1, 1, 0)]
    [InlineData(-1, 1, 1, 0)]
    [InlineData(1, 256, 1, 0)]
    [InlineData(1, -1, 1, 0)]
    [InlineData(1, 1, 4096, 0)]
    [InlineData(1, 1, -1, 0)]
    [InlineData(1, 1, 1, 64)]
    [InlineData(1, 1, 1, -1)]
    public void Ac3_LotComponentOutOfRange_FailsWithAddressOutOfRange(int d, int s, int n, int u)
    {
        // Arrange
        var rig = new Rig();
        rig.LotParts(d, s, n, u, 3000, 10000);
        EntityIds ids = Ids();

        // Act
        SimBuildException e = BuildFails(rig, ids);

        // Assert
        Assert.Equal(SimBuildErrorCode.AddressOutOfRange, e.Code);
        Assert.Equal(0u, ids.Peek(EntityClass.Destination));
    }

    [Fact]
    public void Ac3_NoneAddress_FailsWithAddressOutOfRange()
    {
        // Arrange
        var rig = new Rig();
        rig.Lot(AddressId.None, 3000, 10000, FaceDir.NegZ);

        // Act / Assert
        Assert.Equal(SimBuildErrorCode.AddressOutOfRange, BuildFails(rig).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    public void Ac3_StreetDistrictOutOfRange_FailsWithAddressOutOfRange(int district)
    {
        // Arrange
        var rig = new Rig();
        rig.B.AddStreet(3, (byte)district, rig.StreetA, Box(0, 0, 0, 2000, 1, 2000), StreetAxis.X);

        // Act / Assert
        Assert.Equal(SimBuildErrorCode.AddressOutOfRange, BuildFails(rig).Code);
    }

    [Fact]
    public void Ac3_UpperBoundaryAddress_Builds()
    {
        // Arrange
        var rig = new Rig();
        rig.B.AddStreet(255, 63, rig.StreetA, Box(0, 0, 12000, 32000, 1, 13000), StreetAxis.X);
        rig.LotParts(63, 255, 4095, 63, 3000, 14000);

        // Act
        WorldTables w = rig.Build(Ids());

        // Assert
        Assert.Equal(0xFFFF_FFFFu, w.Lots[0].Address.Value);
        Assert.Equal((byte)63, w.Lots[0].DistrictId);
        Assert.Equal((byte)255, w.Lots[0].StreetId);
        Assert.Equal((ushort)4095, w.Lots[0].Number);
    }

    // ---- AC-4: anchor and spacing ----

    [Theory]
    [InlineData(FaceDir.PosX, 4000, 10000, 4300, 10000)]
    [InlineData(FaceDir.NegX, 12000, 10000, 11700, 10000)]
    [InlineData(FaceDir.PosZ, 20000, 6000, 20000, 6300)]
    [InlineData(FaceDir.NegZ, 28000, 10000, 28000, 9700)]
    public void Ac4_Anchor_IsFacePointPlus300AlongFace_AtGroundPlus1000OnSlope(FaceDir face, int x, int z, int ax, int az)
    {
        // Arrange (height = x / 20 + 2z / 25: 100 mm per tile along X, 160 mm per tile along Z)
        var rig = new Rig(slopeX: 100, slopeZ: 160);
        rig.Lot(AddressId.Pack(1, 1, 1), x, z, face);

        // Act
        WorldTables w = rig.Build(Ids());
        LotRecord lot = w.Lots[0];

        // Assert
        int expectedY = (ax / 20) + (2 * az / 25) + 1000;
        Assert.Equal(face, lot.Face);
        Assert.Equal(new Vec3Mm(ax, expectedY, az), lot.Anchor);
        Assert.Equal(w.TerrainHeightMm(ax, az) + 1000, lot.Anchor.Y);
        Assert.NotEqual(w.TerrainHeightMm(x, z) + 1000, lot.Anchor.Y); // the height is read at the anchor, not the face point
    }

    [Theory]
    [InlineData(5999, false)]
    [InlineData(6000, true)]
    public void Ac4_SameSideMailboxesAlongStreet_6000Passes5999Fails(int gap, bool passes)
    {
        // Arrange
        var rig = new Rig();
        rig.Lot(AddressId.Pack(1, 1, 1), 5000, 10000, FaceDir.NegZ);
        rig.Lot(AddressId.Pack(1, 1, 3), 5000 + gap, 10000, FaceDir.NegZ);

        // Act
        Exception? thrown = Record.Exception(() => rig.Build(Ids()));

        // Assert
        AssertSpacing(thrown, passes);
    }

    [Theory]
    [InlineData(4799, false)]
    [InlineData(4800, true)]
    public void Ac4_SameSideMailboxesDiagonal_EuclideanXzDistanceIsChecked(int dz, bool passes)
    {
        // Arrange (dx 3600, dz 4800 -> exactly 6000 mm; both north of the centre line)
        var rig = new Rig();
        rig.Lot(AddressId.Pack(1, 1, 1), 5000, 10000, FaceDir.NegZ);
        rig.Lot(AddressId.Pack(1, 1, 3), 8600, 10000 + dz, FaceDir.NegZ);

        // Act
        Exception? thrown = Record.Exception(() => rig.Build(Ids()));

        // Assert
        AssertSpacing(thrown, passes);
    }

    [Theory]
    [InlineData(2999, false)]
    [InlineData(3000, true)]
    [InlineData(-2999, false)]
    [InlineData(-3000, true)]
    public void Ac4_OppositeSideMailboxesOnXStreet_Stagger3000Passes2999Fails(int stagger, bool passes)
    {
        // Arrange
        var rig = new Rig();
        rig.Lot(AddressId.Pack(1, 1, 1), 10000, 10000, FaceDir.NegZ);
        rig.Lot(AddressId.Pack(1, 1, 2), 10000 + stagger, 6000, FaceDir.PosZ);

        // Act
        Exception? thrown = Record.Exception(() => rig.Build(Ids()));

        // Assert
        AssertSpacing(thrown, passes);
    }

    [Theory]
    [InlineData(2999, false)]
    [InlineData(3000, true)]
    public void Ac4_OppositeSideMailboxesOnZStreet_StaggerIsMeasuredAlongZ(int stagger, bool passes)
    {
        // Arrange (street B runs along Z with centre x = 15000)
        var rig = new Rig();
        rig.Lot(AddressId.Pack(1, 2, 1), 17000, 3000, FaceDir.NegX);
        rig.Lot(AddressId.Pack(1, 2, 2), 13000, 3000 + stagger, FaceDir.PosX);

        // Act
        Exception? thrown = Record.Exception(() => rig.Build(Ids()));

        // Assert
        AssertSpacing(thrown, passes);
    }

    [Fact]
    public void Ac4_CloseMailboxesOnDifferentStreets_AreNotCompared()
    {
        // Arrange (1 m apart, but one is on street A and one on street B)
        var rig = new Rig();
        rig.Lot(AddressId.Pack(1, 1, 1), 17000, 10000, FaceDir.NegZ);
        rig.Lot(AddressId.Pack(1, 2, 1), 17000, 11000, FaceDir.NegX);

        // Act
        WorldTables w = rig.Build(Ids());

        // Assert
        Assert.Equal(2, w.Lots.Length);
    }

    [Fact]
    public void Ac4_SpacingConstants_AreMailR3Values()
    {
        // Assert
        Assert.Equal(6000, WorldTablesBuilder.MinSameSideMailboxSpacingMm);
        Assert.Equal(3000, WorldTablesBuilder.MinOppositeSideStaggerMm);
        Assert.Equal(300, WorldTablesBuilder.MailboxAnchorOffsetMm);
        Assert.Equal(1000, WorldTablesBuilder.MailboxAnchorHeightMm);
    }

    private static void AssertSpacing(Exception? thrown, bool passes)
    {
        if (passes)
        {
            Assert.Null(thrown);
        }
        else
        {
            Assert.Equal(SimBuildErrorCode.WorldMailboxSpacingTooSmall, Assert.IsType<SimBuildException>(thrown).Code);
        }
    }

    // ---- lot / street / string validation (story-level guards) ----

    [Fact]
    public void Lot_MalformedInputs_FailWithWorldLotInvalid()
    {
        // Arrange
        var unknownStreet = new Rig();
        unknownStreet.Lot(AddressId.Pack(1, 9, 1), 3000, 10000, FaceDir.NegZ);
        var notHouse = new Rig();
        ushort prop = notHouse.B.AddSolid(EntityId.None, Box(2000, 0, 9000, 4000, 1000, 11000), SolidKind.Prop);
        notHouse.LotOn(AddressId.Pack(1, 1, 1), 3000, 10000, FaceDir.NegZ, prop);
        var shared = new Rig();
        ushort house = shared.House(3000, 10000);
        shared.LotOn(AddressId.Pack(1, 1, 1), 3000, 10000, FaceDir.NegZ, house);
        shared.LotOn(AddressId.Pack(1, 1, 3), 20000, 10000, FaceDir.NegZ, house);
        var badHouseIndex = new Rig();
        badHouseIndex.LotOn(AddressId.Pack(1, 1, 1), 3000, 10000, FaceDir.NegZ, 500);
        var badFace = new Rig();
        badFace.Lot(AddressId.Pack(1, 1, 1), 3000, 10000, (FaceDir)9);
        var onCentreLine = new Rig();
        onCentreLine.Lot(AddressId.Pack(1, 1, 1), 3000, 8000, FaceDir.NegZ);
        var outside = new Rig();
        outside.B.AddLot(AddressId.Pack(1, 1, 1), outside.StreetA, new Vec3Mm(40_000, 0, 10000), FaceDir.NegZ, new TileCoord(1, 1), new TileCoord(1, 1), new Vec3Mm(3000, 0, 8000), outside.House(3000, 10000));

        // Act / Assert
        foreach (Rig rig in new[] { unknownStreet, notHouse, shared, badHouseIndex, badFace, onCentreLine, outside })
        {
            Assert.Equal(SimBuildErrorCode.WorldLotInvalid, BuildFails(rig).Code);
        }
    }

    [Fact]
    public void Street_DuplicateOrDegenerate_FailsWithWorldStreetInvalid()
    {
        // Arrange
        var duplicate = new Rig();
        duplicate.B.AddStreet(1, 1, duplicate.StreetA, Box(0, 0, 0, 2000, 1, 2000), StreetAxis.X);
        var degenerate = new Rig();
        degenerate.B.AddStreet(5, 1, degenerate.StreetA, Box(0, 0, 0, 0, 1, 2000), StreetAxis.X);
        var badAxis = new Rig();
        badAxis.B.AddStreet(5, 1, badAxis.StreetA, Box(0, 0, 0, 2000, 1, 2000), (StreetAxis)7);

        // Act / Assert
        foreach (Rig rig in new[] { duplicate, degenerate, badAxis })
        {
            Assert.Equal(SimBuildErrorCode.WorldStreetInvalid, BuildFails(rig).Code);
        }
    }

    [Fact]
    public void DisplayStrings_UnknownIdsAndNullText_FailWithWorldDisplayStringInvalid()
    {
        // Arrange
        var unknownLotLabel = new Rig();
        unknownLotLabel.B.AddLot(AddressId.Pack(1, 1, 1), new StringId(StringNamespace.Run, 99), new Vec3Mm(3000, 0, 10000), FaceDir.NegZ, new TileCoord(1, 5), new TileCoord(1, 4), new Vec3Mm(3000, 0, 8000), unknownLotLabel.House(3000, 10000));
        var wrongNamespace = new Rig();
        wrongNamespace.B.AddStreet(5, 1, new StringId(StringNamespace.Item, 0), Box(0, 0, 0, 2000, 1, 2000), StreetAxis.X);
        var b = new WorldTablesBuilder();

        // Act
        var nullText = Assert.Throws<SimBuildException>(() => b.AddDisplayString(null!));

        // Assert
        Assert.Equal(SimBuildErrorCode.WorldDisplayStringInvalid, BuildFails(unknownLotLabel).Code);
        Assert.Equal(SimBuildErrorCode.WorldDisplayStringInvalid, BuildFails(wrongNamespace).Code);
        Assert.Equal(SimBuildErrorCode.WorldDisplayStringInvalid, nullText.Code);
    }

    // ---- AC-5: StreetNameAt and the world string table ----

    [Fact]
    public void Ac5_StreetNameAt_InsideAndOnEdge_ReturnsStreet_OffStreet_ReturnsNone()
    {
        // Arrange
        var rig = new Rig();
        WorldTables w = rig.B.Build(700, 900);

        // Act / Assert
        Assert.Equal(rig.StreetA, w.StreetNameAt(5000, 8000));    // inside
        Assert.Equal(rig.StreetA, w.StreetNameAt(0, 7000));       // min corner (closed)
        Assert.Equal(rig.StreetA, w.StreetNameAt(32000, 9000));   // max corner (closed)
        Assert.Equal(StringId.None, w.StreetNameAt(5000, 9001));  // just past the edge
        Assert.Equal(StringId.None, w.StreetNameAt(5000, 12000)); // off every street
        Assert.Equal(StringId.None, w.StreetNameAt(-50_000, 8000));
        Assert.Equal(rig.StreetB, w.StreetNameAt(15000, 12000));
        Assert.Equal(rig.StreetA, w.StreetNameAt(15000, 8000));   // crossing: lowest (district, street) wins
    }

    [Fact]
    public void Ac5_DisplayText_RoundTripsWorldStrings_AndRejectsOthers()
    {
        // Arrange
        var rig = new Rig();
        WorldTables w = rig.B.Build(700, 900);

        // Act
        bool foundA = w.TryGetDisplayText(rig.StreetA, out string a);
        bool foundB = w.TryGetDisplayText(rig.StreetB, out string b);
        bool foundOther = w.TryGetDisplayText(new StringId(StringNamespace.Item, 0), out string other);
        bool foundNone = w.TryGetDisplayText(StringId.None, out _);

        // Assert
        Assert.True(foundA);
        Assert.Equal("Harbour Lane", a);
        Assert.True(foundB);
        Assert.Equal("Quay Street", b);
        Assert.Equal(new StringId(StringNamespace.Run, 0), rig.StreetA);
        Assert.False(foundOther);
        Assert.Equal(string.Empty, other);
        Assert.False(foundNone);
    }

    [Fact]
    public void Ac2_BuildWithNullEntityIds_ThrowsEvenWithoutLots()
    {
        var b = new WorldTablesBuilder();
        b.SetCorners(1, 1, new int[4]);

        Assert.Throws<ArgumentNullException>(() => b.Build(null!, 700, 900));
    }
}
