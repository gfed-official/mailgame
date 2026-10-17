using System.Buffers;
using System.Buffers.Binary;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.MailDestinations;

/// <summary>
/// Covers mail-destinations story 002 (TR-mail-006 registry half, TR-mail-018; GDD M-R3-1a; ADR-0017 section 3): the
/// destination registry built from <c>IWorldQuery.Lots</c>, its ordinal and reverse maps, the catch-volume mapping, the
/// facts accessor and the appended dump columns. Deterministic, no IO. The 0 B clause is in
/// <see cref="DestinationRegistryAllocationTests"/>.
/// </summary>
public class DestinationRegistryTests
{
    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    private static WorldTables BuildStreet()
    {
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        return b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    /// <summary>A synthetic lot list: ids 1..n in lot order, street 1 (n up to 4095 numbers), faces cycling through all four.</summary>
    private static LotRecord[] SyntheticLots(int n, uint idStart = 1, bool reverseIds = false)
    {
        var lots = new LotRecord[n];
        for (int i = 0; i < n; i++)
        {
            uint counter = reverseIds ? idStart + (uint)(n - 1 - i) : idStart + (uint)i;
            lots[i] = new LotRecord(
                EntityId.Compose(EntityClass.Destination, counter),
                AddressId.Pack(1, 1, 2 * (i + 1)),   // even numbers only, so odd numbers are guaranteed misses
                1,
                1,
                (ushort)(2 * (i + 1)),
                default,
                new TileCoord(i, 0),
                (FaceDir)(i % 4),
                new Vec3Mm((i * 12_000) + 500, 1000, 30_000),
                new TileCoord(i, 1),
                default,
                (ushort)i);
        }

        return lots;
    }

    // ---- AC-1 (M-R3-1a) ----

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac1_M1aStreet_YieldsTenDestinations_AndAddressToDestinationToAddressRoundTrips()
    {
        WorldTables world = BuildStreet();

        var registry = new DestinationRegistry(world, _content);

        Assert.Equal(10, registry.Count);
        Assert.Equal(world.Lots.Length, registry.Count);
        for (int i = 0; i < registry.Count; i++)
        {
            AddressId address = world.Lots[i].Address;
            Assert.True(registry.TryGetOrdinal(address, out DestinationOrdinal byAddress));
            EntityId dest = registry.EntityIdOf(byAddress);
            Assert.True(registry.TryGetOrdinal(dest, out DestinationOrdinal byDest));
            Assert.Equal(byAddress, byDest);
            Assert.Equal(address, registry.AddressOf(byDest));
        }
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac1_EveryRow_IsTheHouseType_WithTheR3Values_ReadFromTheDestinationTypeDef()
    {
        var registry = new DestinationRegistry(BuildStreet(), _content);
        string[] kinds = { "postcard", "letter", "small", "medium", "large" };

        for (ushort i = 0; i < registry.Count; i++)
        {
            DefIndex type = registry.TypeOf(new DestinationOrdinal(i));
            DestinationTypeDef def = _content.DestinationTypes[type];

            Assert.Equal("house", def.Id);
            Assert.Equal(2000, def.InsertRateMilliPerSec);
            Assert.Equal(400, def.ManualInsertHoldMs);   // 0.4 s as stored (ms)
            Assert.Equal(1, def.MaxAutomatedFeeders);
            Assert.False(def.RequiresUnit);
            Assert.Equal(kinds.Length, def.Accepts.Length);
            foreach (string kind in kinds)
            {
                Assert.Contains(def.Accepts, d => _content.MailKinds[d].Id == kind);
            }

            Assert.Equal(new CatchVolume(1000, 800, 1000, 1000, 0), def.CatchVolume);   // 1.0 x 0.8 x 1.0 m, centre 1.0 m up
        }
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac1_CatchVolume_IsAnAxisAlignedBoxCentredOnTheAnchor_OneMetreAboveTheBase_ForEachFace()
    {
        // Anchor y 1000 -> base y 0 -> centre y 1000; height 800 -> y 600..1400. Width 1000 x depth 1000 is square, so
        // every face gives the same x/z extents here; the non-square mapping is covered below.
        var registry = new DestinationRegistry(BuildStreet(), _content);
        for (ushort i = 0; i < registry.Count; i++)
        {
            var o = new DestinationOrdinal(i);
            Vec3Mm a = registry.AnchorOf(o);
            AabbMm box = registry.CatchVolumeOf(o);

            Assert.Equal(new Vec3Mm(a.X - 500, a.Y - 1000 + 1000 - 400, a.Z - 500), box.Min);
            Assert.Equal(new Vec3Mm(a.X + 500, a.Y - 1000 + 1000 + 400, a.Z + 500), box.Max);
        }
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac1_ToWorldBox_WidthRunsAlongTheFace_DepthAlongTheNormal()
    {
        var anchor = new Vec3Mm(10_000, 1500, 20_000);   // base y 500
        var volume = new CatchVolume(WidthMm: 600, HeightMm: 400, DepthMm: 1000, CentreHeightMm: 700, YawMilliDeg: 0);

        AabbMm alongZ = DestinationRegistry.ToWorldBox(anchor, FaceDir.PosZ, volume);   // normal z: depth in z, width in x
        AabbMm alongX = DestinationRegistry.ToWorldBox(anchor, FaceDir.NegX, volume);   // normal x: depth in x, width in z

        Assert.Equal(new AabbMm(new Vec3Mm(9_700, 1000, 19_500), new Vec3Mm(10_300, 1400, 20_500)), alongZ);   // y: centre 1200 +/- 200
        Assert.Equal(new AabbMm(new Vec3Mm(9_500, 1000, 19_700), new Vec3Mm(10_500, 1400, 20_300)), alongX);
        Assert.Equal(alongZ, DestinationRegistry.ToWorldBox(anchor, FaceDir.NegZ, volume));
        Assert.Equal(alongX, DestinationRegistry.ToWorldBox(anchor, FaceDir.PosX, volume));
    }

    // ---- AC-2 ----

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac2_OrdinalIsLotIndex_RowEntityIsTheLotDestination_AnchorIsTheLotAnchor()
    {
        WorldTables world = BuildStreet();
        var registry = new DestinationRegistry(world, _content);

        for (int i = 0; i < world.Lots.Length; i++)
        {
            var o = new DestinationOrdinal((ushort)i);
            Assert.Equal(world.Lots[i].Destination, registry.EntityIdOf(o));
            Assert.Equal(world.Lots[i].Address, registry.AddressOf(o));
            Assert.Equal(world.Lots[i].Anchor, registry.AnchorOf(o));
            Assert.Equal(world.Lots[i].DistrictId, registry.DistrictOf(o));
            Assert.True(registry.TryGetOrdinal(world.Lots[i].Destination, out DestinationOrdinal back));
            Assert.Equal((ushort)i, back.Value);
            Assert.Equal(DestinationRegistry.NoFeeder, registry.FeederSlotOf(o));
            Assert.Equal(0L, registry.TokensPm(o));
            Assert.Equal(0L, registry.LastTick(o));
        }
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac2_WorldAllocatedDestinationIds_AreContiguous_SoTheEntityLookupIsComputed()
    {
        var registry = new DestinationRegistry(BuildStreet(), _content);

        Assert.True(registry.IdLookupIsComputed);
        Assert.False(registry.TryGetOrdinal(EntityId.Compose(EntityClass.Destination, 11), out _));   // one past the last
        Assert.False(registry.TryGetOrdinal(EntityId.Compose(EntityClass.Destination, 999), out _));
        Assert.False(registry.TryGetOrdinal(default(EntityId), out _));
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac2_NonContiguousIds_FallBackToASortedSearch_AndStillMapToTheLotIndex()
    {
        // Ids descending in lot order: not contiguous ascending, so the sorted copy is searched.
        LotRecord[] lots = SyntheticLots(20, idStart: 5, reverseIds: true);
        var registry = new DestinationRegistry(lots, _content);

        Assert.False(registry.IdLookupIsComputed);
        for (int i = 0; i < lots.Length; i++)
        {
            Assert.True(registry.TryGetOrdinal(lots[i].Destination, out DestinationOrdinal o));
            Assert.Equal((ushort)i, o.Value);
        }

        Assert.False(registry.TryGetOrdinal(EntityId.Compose(EntityClass.Destination, 4), out _));
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac2_UnknownAddress_MissesInAtMostTenProbes_On1000Lots_AndEveryKnownAddressHits()
    {
        LotRecord[] lots = SyntheticLots(1000);
        var registry = new DestinationRegistry(lots, _content);
        int worstMiss = 0;
        int worstHit = 0;

        for (int i = 0; i < lots.Length; i++)
        {
            Assert.True(registry.TryGetOrdinal(lots[i].Address, out DestinationOrdinal o, out int hitProbes));
            Assert.Equal((ushort)i, o.Value);
            worstHit = Math.Max(worstHit, hitProbes);

            // Odd numbers are never registered, including below the first and above the last.
            Assert.False(registry.TryGetOrdinal(AddressId.Pack(1, 1, (2 * i) + 1), out _, out int missProbes));
            worstMiss = Math.Max(worstMiss, missProbes);
        }

        Assert.False(registry.TryGetOrdinal(AddressId.Pack(2, 1, 2), out _, out int otherDistrict));
        Assert.False(registry.TryGetOrdinal(AddressId.None, out _, out int none));
        worstMiss = Math.Max(worstMiss, Math.Max(otherDistrict, none));

        Assert.InRange(worstMiss, 1, 10);
        Assert.InRange(worstHit, 1, 10);
    }

    // ---- build rejections ----

    private static ContentSet WithDestinationTypes(params DestinationTypeDef[] defs) => new(
        _content.Balance,
        _content.ResolvedStatBounds,
        _content.Containers,
        _content.Items,
        _content.MailKinds,
        new DefTable<DestinationTypeDef>(System.Collections.Immutable.ImmutableArray.Create(defs)),
        _content.MailMixes);

    private static ContentSet WithHouseVolume(CatchVolume volume) =>
        WithDestinationTypes(_content.DestinationTypes[DefIndex.FromValue(0)] with { CatchVolume = volume });

    [Fact]
    public void Build_RejectsDuplicateAddresses()
    {
        LotRecord[] dup = SyntheticLots(3);
        dup[2] = new LotRecord(dup[2].Destination, dup[1].Address, 1, 1, 4, default, default, FaceDir.PosZ, dup[2].Anchor, default, default, 0);

        Assert.Throws<ArgumentException>(() => new DestinationRegistry(dup, _content));
    }

    [Fact]
    public void Build_RejectsDuplicateDestinationIds_OnTheSortedPath()
    {
        // Ids 1, 3, 3: not contiguous, so the sorted copy is built and its duplicate check fires.
        LotRecord[] lots = SyntheticLots(3);
        lots[1] = new LotRecord(EntityId.Compose(EntityClass.Destination, 3), lots[1].Address, 1, 1, 4, default, default, FaceDir.PosZ, lots[1].Anchor, default, default, 1);
        lots[2] = new LotRecord(EntityId.Compose(EntityClass.Destination, 3), lots[2].Address, 1, 1, 6, default, default, FaceDir.PosZ, lots[2].Anchor, default, default, 2);

        Assert.Throws<ArgumentException>(() => new DestinationRegistry(lots, _content));
    }

    [Fact]
    public void Build_RejectsMoreLotsThanAnOrdinalHolds_65536Lots()
    {
        var lots = new LotRecord[ushort.MaxValue + 1];   // the check precedes any per-lot work

        Assert.Throws<ArgumentException>(() => new DestinationRegistry(lots, _content));
    }

    [Fact]
    public void Build_Accepts65535Lots_TheLargestOrdinalCount()
    {
        LotRecord[] lots = new LotRecord[ushort.MaxValue];
        for (int i = 0; i < lots.Length; i++)
        {
            // Packed addresses need a valid number field (12 bits): spread over the street field instead.
            lots[i] = new LotRecord(
                EntityId.Compose(EntityClass.Destination, (uint)i + 1),
                new AddressId((uint)(1 << 26) + ((uint)i << 6)),
                1, 1, 0, default, default, FaceDir.PosZ, new Vec3Mm(0, 1000, 0), default, default, 0);
        }

        var registry = new DestinationRegistry(lots, _content);

        Assert.Equal(ushort.MaxValue, registry.Count);
        Assert.True(registry.TryGetOrdinal(lots[^1].Destination, out DestinationOrdinal last));
        Assert.Equal(ushort.MaxValue - 1, last.Value);
    }

    [Fact]
    public void Build_RejectsAContentSetWithoutAHouseDef()
    {
        ContentSet noHouse = WithDestinationTypes(_content.DestinationTypes[DefIndex.FromValue(0)] with { Id = "shed" });

        Assert.Throws<ArgumentException>(() => new DestinationRegistry(SyntheticLots(2), noHouse));
    }

    [Fact]
    public void Build_RejectsNullContent_WhenThereAreLots_ButNotWhenThereAreNone()
    {
        Assert.Throws<ArgumentNullException>(() => new DestinationRegistry(SyntheticLots(2), null));
        Assert.Equal(0, new DestinationRegistry(ReadOnlySpan<LotRecord>.Empty, null).Count);
    }

    [Fact]
    public void Build_RejectsANullWorld()
    {
        Assert.Throws<ArgumentNullException>(() => new DestinationRegistry((IWorldQuery)null!, _content));
    }

    [Theory]
    [InlineData(1000, 800, 1000, 1000, 90_000)]   // yaw: the box would not be axis-aligned
    [InlineData(0, 800, 1000, 1000, 0)]           // zero width
    [InlineData(1000, 0, 1000, 1000, 0)]          // zero height
    [InlineData(1000, 800, -1, 1000, 0)]          // negative depth
    public void Build_RejectsAHouseCatchVolume_WithYawOrANonPositiveExtent(int w, int h, int d, int centre, int yaw)
    {
        ContentSet bad = WithHouseVolume(new CatchVolume(w, h, d, centre, yaw));

        Assert.Throws<ArgumentException>(() => new DestinationRegistry(SyntheticLots(2), bad));
        Assert.Equal(0, new DestinationRegistry(ReadOnlySpan<LotRecord>.Empty, bad).Count);   // no lots, nothing read
    }

    // ---- empty registry and lookup boundaries ----

    [Fact]
    public void EmptyRegistry_FindsNothing_ByEntityOrAddress_AndFactsAreFalse()
    {
        DestinationRegistry empty = DestinationRegistry.CreateEmpty();

        Assert.False(empty.IdLookupIsComputed);
        Assert.False(empty.TryGetDestinationFacts(EntityId.Compose(EntityClass.Destination, 1), out DestinationFacts f));
        Assert.Equal(default(EntityId), f.Id);
        Assert.False(empty.TryGetDestinationFacts(default, out _));
        Assert.False(empty.TryGetOrdinal(EntityId.Compose(EntityClass.Destination, 1), out _));
        Assert.False(empty.TryGetOrdinal(AddressId.Pack(1, 1, 1), out _, out int probes));
        Assert.Equal(0, probes);
    }

    [Fact]
    public void ComputedIdLookup_JustBelowTheFirstId_Misses_WithoutUnsignedWrap()
    {
        LotRecord[] lots = SyntheticLots(5, idStart: 100);
        var registry = new DestinationRegistry(lots, _content);
        uint first = lots[0].Destination.Value;

        Assert.True(registry.IdLookupIsComputed);
        Assert.False(registry.TryGetOrdinal(new EntityId(first - 1), out _));
        Assert.True(registry.TryGetOrdinal(new EntityId(first), out DestinationOrdinal firstOrd));
        Assert.Equal((ushort)0, firstOrd.Value);
        Assert.True(registry.TryGetOrdinal(new EntityId(first + 4), out DestinationOrdinal lastOrd));
        Assert.Equal((ushort)4, lastOrd.Value);
        Assert.False(registry.TryGetOrdinal(new EntityId(first + 5), out _));
        Assert.False(registry.TryGetOrdinal(new EntityId(0), out _));
        Assert.False(registry.TryGetOrdinal(new EntityId(uint.MaxValue), out _));
    }

    [Fact]
    public void IsDistrictUnlocked_OnlyDistricts1To63Count_0_64_AndAbove_AreNeverUnlocked()
    {
        var state = new DeliveriesState();
        state.Set(open: true, closeTick: 0, unlockedDistrictMask: ulong.MaxValue);   // every bit set

        Assert.False(state.IsDistrictUnlocked(0));    // district 0 is reserved (AddressId.Pack rejects it)
        Assert.True(state.IsDistrictUnlocked(1));
        Assert.True(state.IsDistrictUnlocked(63));
        Assert.False(state.IsDistrictUnlocked(64));   // outside the 6-bit district field
        Assert.False(state.IsDistrictUnlocked(255));
        Assert.False(new DeliveriesState().IsDistrictUnlocked(2));
        Assert.True(new DeliveriesState().IsDistrictUnlocked(1));
    }

    // ---- AC-3 ----

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac3_Facts_ForARegisteredDestination_CarryIdAddressType_AndAreClosedInDistrict1ByDefault()
    {
        WorldTables world = BuildStreet();
        var registry = new DestinationRegistry(world, _content);

        for (int i = 0; i < world.Lots.Length; i++)
        {
            Assert.True(registry.TryGetDestinationFacts(world.Lots[i].Destination, out DestinationFacts f));
            Assert.Equal(world.Lots[i].Destination, f.Id);
            Assert.Equal(world.Lots[i].Address, f.Address);
            Assert.Equal("house", _content.DestinationTypes[f.Type].Id);
            Assert.Equal(DestState.Closed, f.State);   // no SetDeliveriesState yet; District 1 is unlocked
        }
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac3_Facts_ForAnUnregisteredEntity_AreFalse_AndDefault()
    {
        var registry = new DestinationRegistry(BuildStreet(), _content);

        Assert.False(registry.TryGetDestinationFacts(EntityId.Compose(EntityClass.Destination, 500), out DestinationFacts f));
        Assert.Equal(default(EntityId), f.Id);
        Assert.False(registry.TryGetDestinationFacts(default, out _));
        Assert.False(registry.TryGetDestinationFacts(EntityId.Compose(EntityClass.Construct, 1), out _));
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Ac3_RawState_FollowsTheDeliveriesState_LockedBeatsClosedBeatsOpen_WithNoGrace()
    {
        var registry = new DestinationRegistry(BuildStreet(), _content);
        EntityId dest = BuildStreet().Lots[0].Destination;

        registry.Deliveries.Set(open: true, closeTick: 0, unlockedDistrictMask: DeliveriesState.DefaultUnlockedMask);
        Assert.True(registry.TryGetDestinationFacts(dest, out DestinationFacts open));
        Assert.Equal(DestState.Open, open.State);

        registry.Deliveries.Set(open: false, closeTick: 100, unlockedDistrictMask: DeliveriesState.DefaultUnlockedMask);
        Assert.True(registry.TryGetDestinationFacts(dest, out DestinationFacts closed));
        Assert.Equal(DestState.Closed, closed.State);   // raw state: no grace window here (story 003)

        registry.Deliveries.Set(open: true, closeTick: 0, unlockedDistrictMask: 1UL << 1);   // only District 2 unlocked
        Assert.True(registry.TryGetDestinationFacts(dest, out DestinationFacts locked));
        Assert.Equal(DestState.Locked, locked.State);
    }

    // ---- dump (appended after the mail rows) ----

    private static byte[] DumpSection(MailTable table, DestinationRegistry registry)
    {
        var output = new ArrayBufferWriter<byte>();
        var scratch = new CanonicalBuffer();
        var w = new CanonicalWriter(output, scratch);
        var contributor = new MailStateContributor(table, registry);
        w.BeginSection(contributor.SectionId);
        contributor.Write(ref w);
        w.EndSection();
        return output.WrittenSpan.ToArray();
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Dump_AppendsTheRegistryColumns_AfterTheMailRows_FieldByField()
    {
        WorldTables world = BuildStreet();
        var registry = new DestinationRegistry(world, _content);
        var rig = new MailTableRig();
        rig.Create();   // one live row: 4 + 15 bytes of table data

        byte[] bytes = DumpSection(rig.Table, registry);

        ReadOnlySpan<byte> p = bytes.AsSpan(6);
        const int TableBytes = 4 + (4 + 2 + 4 + 2 + 1 + 1 + 1);
        const int RowBytes = 4 + 4 + 2 + 1 + 12 + 12 + 12 + 2 + 8 + 8;
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(p));
        Assert.Equal((ushort)10, BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(TableBytes)));
        Assert.Equal(TableBytes + 2 + (10 * RowBytes), p.Length);

        ReadOnlySpan<byte> r = p.Slice(TableBytes + 2 + (3 * RowBytes), RowBytes);   // ordinal 3
        var o = new DestinationOrdinal(3);
        Assert.Equal(world.Lots[3].Destination.Value, BinaryPrimitives.ReadUInt32LittleEndian(r));
        Assert.Equal(world.Lots[3].Address.Value, BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(4)));
        Assert.Equal(registry.TypeOf(o).Value, BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(8)));
        Assert.Equal(world.Lots[3].DistrictId, r[10]);
        Assert.Equal(world.Lots[3].Anchor.X, BinaryPrimitives.ReadInt32LittleEndian(r.Slice(11)));
        Assert.Equal(world.Lots[3].Anchor.Y, BinaryPrimitives.ReadInt32LittleEndian(r.Slice(15)));
        Assert.Equal(world.Lots[3].Anchor.Z, BinaryPrimitives.ReadInt32LittleEndian(r.Slice(19)));
        AabbMm box = registry.CatchVolumeOf(o);
        Assert.Equal(box.Min.X, BinaryPrimitives.ReadInt32LittleEndian(r.Slice(23)));
        Assert.Equal(box.Min.Y, BinaryPrimitives.ReadInt32LittleEndian(r.Slice(27)));
        Assert.Equal(box.Min.Z, BinaryPrimitives.ReadInt32LittleEndian(r.Slice(31)));
        Assert.Equal(box.Max.X, BinaryPrimitives.ReadInt32LittleEndian(r.Slice(35)));
        Assert.Equal(box.Max.Y, BinaryPrimitives.ReadInt32LittleEndian(r.Slice(39)));
        Assert.Equal(box.Max.Z, BinaryPrimitives.ReadInt32LittleEndian(r.Slice(43)));
        Assert.Equal(DestinationRegistry.NoFeeder, BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(47)));
        Assert.Equal(0L, BinaryPrimitives.ReadInt64LittleEndian(r.Slice(49)));
        Assert.Equal(0L, BinaryPrimitives.ReadInt64LittleEndian(r.Slice(57)));
    }

    [Fact]
    [Trait("Id", "M-R3-1a")]
    public void Dump_IsDeterministic_AndTracksComplaintStateColumns()
    {
        var first = new DestinationRegistry(BuildStreet(), _content);
        var second = new DestinationRegistry(BuildStreet(), _content);
        var rig = new MailTableRig();

        byte[] a = DumpSection(rig.Table, first);
        Assert.Equal(a, DumpSection(rig.Table, second));

        second.TokensPm(new DestinationOrdinal(0)) = 5;
        Assert.NotEqual(a, DumpSection(rig.Table, second));
    }
}
