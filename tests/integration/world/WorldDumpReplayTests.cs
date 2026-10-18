using System.Buffers;
using System.Buffers.Binary;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Core.Replay;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.World;

/// <summary>
/// World-stub story 007 (ADR-0026 section 9, TR-sim-033): the <c>World</c> dump section, T1 with it, composition while
/// <c>Constructed</c> and the replay world-source identity. Composes a per-test profile with one stub system that takes
/// <see cref="IWorldQuery"/> by constructor, so the injection path is observable.
/// </summary>
public class WorldDumpReplayTests
{
    private const ulong Seed = 0xC0FFEE;

    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    // ---- rig ----

    private sealed class WorldConsumer : ISimSystem
    {
        public WorldConsumer(IWorldQuery world)
        {
            World = world;
        }

        public IWorldQuery World { get; }

        public void Bind(SimWorldBuilder builder)
        {
        }
    }

    private sealed class Rig
    {
        public Rig()
        {
            Profile = SimProfile.ForTest("world-dump", null, new SimSystemEntry(SystemId.GridInventory, ctx =>
            {
                Consumer = new WorldConsumer(ctx.World ?? throw new InvalidOperationException("no world"));
                return Consumer;
            }));
        }

        public SimProfile Profile { get; }

        public WorldConsumer? Consumer { get; private set; }

        public SimBuildArgs Args(IWorldSource? source, IOpRecorder? recorder = null) =>
            new(Profile, _content, Seed) { WorldSource = source, Recorder = recorder };

        public SimHost Build(IWorldSource? source, IOpRecorder? recorder = null)
        {
            SimBuildArgs args = Args(source, recorder);
            return SimComposition.Build(in args);
        }
    }

    // A test-only world source: a flat 64 x 32 map with the Post Office, one street and lot 1 of M1aStreet, each shifted by a delta.
    private sealed class TweakedSource : IWorldSource
    {
        private readonly int _cornerDeltaMm;
        private readonly int _solidDeltaMm;
        private readonly int _truckDeltaMm;

        public TweakedSource(int cornerDeltaMm = 0, int solidDeltaMm = 0, int truckDeltaMm = 0, uint version = 1)
        {
            _cornerDeltaMm = cornerDeltaMm;
            _solidDeltaMm = solidDeltaMm;
            _truckDeltaMm = truckDeltaMm;
            Version = version;
        }

        public ushort Kind => 250;

        public uint Version { get; }

        public void Author(WorldTablesBuilder b)
        {
            int stride = M1aStreet.TilesX + 1;
            var corners = new int[stride * (M1aStreet.TilesZ + 1)];
            Array.Fill(corners, M1aStreet.GroundMm);
            corners[(2 * stride) + 60] += _cornerDeltaMm;
            b.SetCorners(M1aStreet.TilesX, M1aStreet.TilesZ, corners);

            b.AddSolid(
                EntityId.None,
                new AabbMm(
                    new Vec3Mm(M1aStreet.PostOfficeMinXMm + _solidDeltaMm, M1aStreet.GroundMm, M1aStreet.PostOfficeMinZMm),
                    new Vec3Mm(M1aStreet.PostOfficeMaxXMm, M1aStreet.GroundMm + M1aStreet.PostOfficeHeightMm, M1aStreet.PostOfficeMaxZMm)),
                SolidKind.PostOffice);
            int houseMinX = M1aStreet.FirstOddPlotMinXMm + ((M1aStreet.LotPitchMm - M1aStreet.HouseWidthMm) / 2);
            int houseMinZ = M1aStreet.OddHouseMaxZMm - M1aStreet.HouseDepthMm;
            ushort house = b.AddSolid(
                EntityId.None,
                new AabbMm(
                    new Vec3Mm(houseMinX, M1aStreet.GroundMm, houseMinZ),
                    new Vec3Mm(houseMinX + M1aStreet.HouseWidthMm, M1aStreet.GroundMm + M1aStreet.HouseHeightMm, houseMinZ + M1aStreet.HouseDepthMm)),
                SolidKind.House);

            StringId street = b.AddDisplayString(M1aStreet.StreetName);
            b.AddStreet(
                M1aStreet.StreetId,
                M1aStreet.DistrictId,
                street,
                new AabbMm(
                    new Vec3Mm(M1aStreet.StreetMinXMm, M1aStreet.GroundMm, M1aStreet.StreetMinZMm),
                    new Vec3Mm(M1aStreet.StreetMaxXMm, M1aStreet.GroundMm + M1aStreet.StreetFootprintHeightMm, M1aStreet.StreetMaxZMm)),
                StreetAxis.X);

            const int S = WorldConstants.TileSizeMm;
            int mailboxX = M1aStreet.FirstOddPlotMinXMm + (M1aStreet.LotPitchMm / 2);
            StringId label = b.AddDisplayString("1 " + M1aStreet.StreetName);
            b.AddLot(
                M1aStreet.DistrictId,
                M1aStreet.StreetId,
                1,
                0,
                label,
                new Vec3Mm(mailboxX, M1aStreet.GroundMm, M1aStreet.OddMailboxZMm),
                FaceDir.PosZ,
                new TileCoord(mailboxX / S, M1aStreet.OddMailboxZMm / S),
                new TileCoord(mailboxX / S, (M1aStreet.StreetMinZMm / S)),
                new Vec3Mm(mailboxX + _truckDeltaMm, M1aStreet.GroundMm, M1aStreet.StreetMinZMm + (S / 2)),
                house);

            int doorX = (M1aStreet.PostOfficeMinXMm + M1aStreet.PostOfficeMaxXMm) / 2;
            int frontZ = M1aStreet.PostOfficeMaxZMm + M1aStreet.PostOfficeFrontOffsetMm;
            b.SetPostOffice(
                new Vec3Mm(doorX, M1aStreet.GroundMm, frontZ),
                new Vec3Mm(doorX + M1aStreet.IntakeDoorOffsetXMm, M1aStreet.GroundMm + M1aStreet.IntakeHeightMm, frontZ),
                new Vec3Mm(M1aStreet.PadCentreXMm, M1aStreet.GroundMm, M1aStreet.PadCentreZMm));
        }
    }

    // ---- dump decoding ----

    private static byte[]? FindSection(byte[] dump, ushort id, out int found)
    {
        found = 0;
        byte[]? payload = null;
        int pos = 6; // "PMSD" + format version
        while (pos < dump.Length)
        {
            ushort sid = BinaryPrimitives.ReadUInt16LittleEndian(dump.AsSpan(pos));
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(pos + 2));
            if (sid == id)
            {
                found++;
                payload = dump.AsSpan(pos + 6, len).ToArray();
            }

            pos += 6 + len;
        }

        return payload;
    }

    private sealed class Reader
    {
        private readonly byte[] _b;
        private int _p;

        public Reader(byte[] bytes)
        {
            _b = bytes;
        }

        public bool AtEnd => _p == _b.Length;

        public byte U8() => _b[_p++];

        public ushort U16()
        {
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_b.AsSpan(_p));
            _p += 2;
            return v;
        }

        public uint U32()
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(_b.AsSpan(_p));
            _p += 4;
            return v;
        }

        public int I32()
        {
            int v = BinaryPrimitives.ReadInt32LittleEndian(_b.AsSpan(_p));
            _p += 4;
            return v;
        }

        public long I64()
        {
            long v = BinaryPrimitives.ReadInt64LittleEndian(_b.AsSpan(_p));
            _p += 8;
            return v;
        }

        public Vec3Mm Vec() => new(I32(), I32(), I32());
    }

    private static string Hash(SimHost host) => DumpHash.Sha256(host.World);

    // ---- AC-1 ----

    [Fact]
    [Trait("Id", "WS-007-AC1")]
    public void Dump_WithM1aStreet_ContainsOneWorldSectionThatDecodesToTheQuery()
    {
        var rig = new Rig();
        SimHost host = rig.Build(M1aWorldSource.Instance);
        IWorldQuery q = rig.Consumer!.World;

        byte[]? payload = FindSection(DumpHash.Bytes(host.World), SectionIds.World, out int found);

        Assert.Equal(1, found);
        var r = new Reader(payload!);
        Assert.Equal(q.Bounds, new MapBounds(r.I64(), r.I64(), r.I64(), r.I64()));
        Assert.Equal(q.TilesX, r.I32());
        Assert.Equal(q.TilesZ, r.I32());

        int stride = q.TilesX + 1;
        Assert.Equal((uint)(stride * (q.TilesZ + 1)), r.U32());
        for (int cz = 0; cz <= q.TilesZ; cz++)
        {
            for (int cx = 0; cx <= q.TilesX; cx++)
            {
                int tx = Math.Min(cx, q.TilesX - 1);
                int tz = Math.Min(cz, q.TilesZ - 1);
                q.GetCorners(tx, tz, out HeightCorners h);
                int expected = cx == tx ? (cz == tz ? h.H00 : h.H01) : (cz == tz ? h.H10 : h.H11);
                Assert.Equal(expected, r.I32());
            }
        }

        Assert.Equal((uint)(q.TilesX * q.TilesZ), r.U32());
        for (int tz = 0; tz < q.TilesZ; tz++)
        {
            for (int tx = 0; tx < q.TilesX; tx++)
            {
                Assert.Equal((byte)q.Tile(tx, tz), r.U8());
            }
        }

        Assert.Equal((uint)q.StaticSolids.Length, r.U32());
        for (int i = 0; i < q.StaticSolids.Length; i++)
        {
            StaticSolid s = q.StaticSolids[i];
            Assert.Equal(s.Index, r.U16());
            Assert.Equal(s.Owner.Value, r.U32());
            Assert.Equal(s.Box, new AabbMm(r.Vec(), r.Vec()));
            Assert.Equal((byte)s.Kind, r.U8());
        }

        Assert.Equal((uint)q.Lots.Length, r.U32());
        Assert.Equal(10, q.Lots.Length);
        for (int i = 0; i < q.Lots.Length; i++)
        {
            LotRecord l = q.Lots[i];
            Assert.Equal(l.Destination.Value, r.U32());
            Assert.Equal(l.Address.Value, r.U32());
            Assert.Equal(l.DistrictId, r.U8());
            Assert.Equal(l.StreetId, r.U8());
            Assert.Equal(l.Number, r.U16());
            Assert.Equal(l.Display, new StringId((StringNamespace)r.U8(), r.U16()));
            Assert.Equal(l.MailboxTile, new TileCoord(r.I32(), r.I32()));
            Assert.Equal((byte)l.Face, r.U8());
            Assert.Equal(l.Anchor, r.Vec());
            Assert.Equal(l.ApproachTile, new TileCoord(r.I32(), r.I32()));
            Assert.Equal(l.TruckStopPoint, r.Vec());
            Assert.Equal(l.HouseSolid, r.U16());
        }

        PostOfficeRecord po = q.PostOffice;
        Assert.Equal(po, new PostOfficeRecord(r.Vec(), r.Vec(), r.Vec(), r.I32(), r.U8()));
        Assert.True(r.AtEnd);
    }

    [Fact]
    [Trait("Id", "WS-007-AC1")]
    public void Dump_CachedPayload_EqualsAFreshFieldByFieldWrite()
    {
        var rig = new Rig();
        SimHost host = rig.Build(M1aWorldSource.Instance);
        var tables = (WorldTables)rig.Consumer!.World;

        var fresh = new ArrayBufferWriter<byte>();
        var w = new CanonicalWriter(fresh, new CanonicalBuffer());
        w.BeginSection(SectionIds.World);
        tables.WriteFields(ref w);
        w.EndSection();
        byte[] expected = fresh.WrittenSpan.Slice(WorldTables.SectionFrameBytes).ToArray();

        var viaWrite = new ArrayBufferWriter<byte>();
        var w2 = new CanonicalWriter(viaWrite, new CanonicalBuffer());
        w2.BeginSection(SectionIds.World);
        tables.Write(ref w2);
        w2.EndSection();

        Assert.Equal(expected, viaWrite.WrittenSpan.Slice(WorldTables.SectionFrameBytes).ToArray());
        Assert.Equal(expected, FindSection(DumpHash.Bytes(host.World), SectionIds.World, out _));
    }

    // ---- AC-2 ----

    [Fact]
    [Trait("Id", "WS-007-AC2")]
    public void TwoHostsFromM1aStreet_Run300Ticks_HaveIdenticalDumpHashEveryTick()
    {
        SimHost a = new Rig().Build(M1aWorldSource.Instance);
        SimHost b = new Rig().Build(M1aWorldSource.Instance);
        Assert.Equal(Hash(a), Hash(b));

        for (int t = 0; t < 300; t++)
        {
            a.World.Tick();
            b.World.Tick();
            Assert.Equal(Hash(a), Hash(b));
        }

        Assert.Equal(300u, a.World.CurrentTick);
    }

    [Fact]
    [Trait("Id", "WS-007-AC2")]
    public void TweakedWorld_OneCornerOneSolidCoordinateOrOneTruckStopPoint_ChangesTheTickZeroHash()
    {
        string baseline = Hash(new Rig().Build(new TweakedSource()));

        Assert.Equal(baseline, Hash(new Rig().Build(new TweakedSource())));
        Assert.NotEqual(baseline, Hash(new Rig().Build(new TweakedSource(cornerDeltaMm: 100))));
        Assert.NotEqual(baseline, Hash(new Rig().Build(new TweakedSource(solidDeltaMm: 100))));
        Assert.NotEqual(baseline, Hash(new Rig().Build(new TweakedSource(truckDeltaMm: 100))));
    }

    // ---- AC-3 ----

    [Fact]
    [Trait("Id", "WS-007-AC3")]
    public void Compose_WithM1aStreet_ConsumerGetsTheBuiltTablesAndTenDestinationIdsAreIssued()
    {
        var rig = new Rig();
        SimHost host = rig.Build(M1aWorldSource.Instance);

        var tables = Assert.IsType<WorldTables>(rig.Consumer!.World);
        Assert.Equal(10u, host.Services.EntityIds.Peek(EntityClass.Destination));
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(EntityClass.Destination, tables.Lots[i].Destination.Class);
            Assert.Equal((uint)(i + 1), tables.Lots[i].Destination.Counter);
        }

        Assert.Equal(SimState.Ready, host.World.State);
    }

    [Fact]
    [Trait("Id", "WS-007-AC3")]
    public void Compose_WithoutAWorld_HasNoWorldSection()
    {
        SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.ForTest("none", null), _content, Seed));

        FindSection(DumpHash.Bytes(host.World), SectionIds.World, out int found);

        Assert.Equal(0, found);
    }

    [Fact]
    [Trait("Id", "WS-007-AC3")]
    public void Compose_WithASuppliedWorldTables_BindsItsSection_AndBothWorldInputsAreRefused()
    {
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        WorldTables supplied = b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
        var args = new SimBuildArgs(SimProfile.ForTest("supplied", null), _content, Seed) { World = supplied };

        SimHost host = SimComposition.Build(in args);

        FindSection(DumpHash.Bytes(host.World), SectionIds.World, out int found);
        Assert.Equal(1, found);
        var both = new SimBuildArgs(SimProfile.ForTest("both", null), _content, Seed) { World = supplied, WorldSource = M1aWorldSource.Instance };
        Assert.Throws<ArgumentException>(() => SimComposition.Build(in both));
    }

    [Fact]
    [Trait("Id", "WS-007-AC3")]
    public void Compose_M1aGreyboxWithM1aStreet_BuildsPlayersOverTheTablesAndTicks()
    {
        SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, _content, Seed) { WorldSource = M1aWorldSource.Instance });

        for (int t = 0; t < 5; t++)
        {
            host.World.Tick();
        }

        FindSection(DumpHash.Bytes(host.World), SectionIds.World, out int found);
        Assert.Equal(1, found);
        Assert.Equal(5u, host.World.CurrentTick);
    }

    // ---- AC-4 ----

    [Fact]
    [Trait("Id", "WS-007-AC4")]
    public void Replay_OfARecordingMadeOnM1aStreet_ReproducesEveryTickHash()
    {
        var rig = new Rig();
        var header = RecordingHeader.Create(_content, Seed, null, M1aWorldSource.Instance);
        var rec = new InMemoryRecorder(header);
        SimHost host = rig.Build(M1aWorldSource.Instance, rec);
        Assert.Equal(M1aWorldSource.M1aKind, header.WorldSourceKind);
        Assert.Equal((uint)M1aStreet.Version, header.WorldSourceVersion);

        Assert.True(rec.RecordDump(host.World));
        for (int t = 0; t < 50; t++)
        {
            host.World.Tick();
            Assert.True(rec.RecordDump(host.World));
            rec.Flush(host.World.CurrentTick);
        }

        ReplayResult result = ReplayDriver.Run(rec, rig.Args(M1aWorldSource.Instance));

        Assert.Equal(ReplayOutcome.Reproduced, result.Outcome);
        Assert.Equal(ReplayRefusal.None, result.Refusal);
        Assert.Equal(50u, result.TicksRun);
        Assert.Equal(51, result.DumpHashesChecked);
        Assert.Equal(0, result.DumpHashMismatches);
        Assert.Equal(Hash(host), DumpHash.Sha256(result.Host!.World));
    }

    [Fact]
    [Trait("Id", "WS-007-AC4")]
    public void Replay_OfARecordingWhoseWorldVersionDiffers_IsRefusedBeforeTickZero()
    {
        var rig = new Rig();
        var rec = new InMemoryRecorder(RecordingHeader.Create(_content, Seed, null, new FakeVersion(M1aWorldSource.M1aKind, (uint)M1aStreet.Version + 1)));

        ReplayResult result = ReplayDriver.Run(rec, rig.Args(M1aWorldSource.Instance));

        Assert.Equal(ReplayOutcome.Refused, result.Outcome);
        Assert.Equal(ReplayRefusal.WorldSource, result.Refusal);
        Assert.Equal(0u, result.TicksRun);
        Assert.Null(result.Host);
        Assert.Null(rig.Consumer);
    }

    [Fact]
    [Trait("Id", "WS-007-AC4")]
    public void Header_FirstMismatch_ChecksWorldSourceAfterTheSeedAndBeforeTheCounters()
    {
        var m1a = RecordingHeader.Create(_content, Seed, null, M1aWorldSource.Instance);
        var none = RecordingHeader.Create(_content, Seed);
        var otherKind = RecordingHeader.Create(_content, Seed, null, new FakeVersion(2, (uint)M1aStreet.Version));
        var otherSeedAndWorld = RecordingHeader.Create(_content, Seed + 1, null, new FakeVersion(2, 9));
        var otherCounters = RecordingHeader.Create(_content, Seed, new SimInitialCounters { LastMailId = 3 }, new FakeVersion(2, 9));

        Assert.Equal(ReplayRefusal.None, m1a.FirstMismatch(RecordingHeader.Create(_content, Seed, null, M1aWorldSource.Instance)));
        Assert.Equal(ReplayRefusal.WorldSource, m1a.FirstMismatch(none));
        Assert.Equal(ReplayRefusal.WorldSource, m1a.FirstMismatch(otherKind));
        Assert.Equal(ReplayRefusal.RunSeed, m1a.FirstMismatch(otherSeedAndWorld));
        Assert.Equal(ReplayRefusal.WorldSource, m1a.FirstMismatch(otherCounters));
    }

    private sealed class FakeVersion : IWorldSource
    {
        public FakeVersion(ushort kind, uint version)
        {
            Kind = kind;
            Version = version;
        }

        public ushort Kind { get; }

        public uint Version { get; }

        public void Author(WorldTablesBuilder b) => throw new InvalidOperationException("identity only");
    }
}
