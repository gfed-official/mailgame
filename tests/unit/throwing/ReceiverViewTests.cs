using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Geometry;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.ThrowingCatching;
using PerformativeMail.Sim.World;
using G = PerformativeMail.Sim.Tests.Unit.ThrowingCatching.ThrowGoldenVectors;

namespace PerformativeMail.Sim.Tests.Unit.ThrowingCatching;

/// <summary>
/// Throwing story 003 (ADR-0015 sections 3 and 4, ADR-0026 section 5, ADR-0017; GDD <c>design/gdd/throwing-catching.md</c> F1, F4
/// "Broad phase", T6, Edge Cases; TC-E-1 a, TC-T6-3 b, TC-F4-1 broad phase; TR-throwing-003, TR-throwing-005 to 007): the
/// <see cref="ReceiverViewBuilder"/> over the shipped <see cref="M1aStreet"/> and Mail's registry, the
/// <see cref="ChestOriginProbe"/> through <see cref="LaunchMath.Compute"/>, and <see cref="SurfaceBelow"/>. The "only
/// <c>ReceiverGrid</c> stores cells" source guard is in <c>ThrowingRulesTests</c>. Non-parallel because of the 0 B measurement.
/// </summary>
[Collection(NonParallelCollection.Name)]
public class ReceiverViewTests
{
    private static readonly WorldTables _street = BuildStreet();
    private static readonly MailSystem _mail = Assert.Single(
        SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, BuiltInContent.CreateM1a(), 7) { World = _street }).Systems.OfType<MailSystem>());

    private static readonly ReceiverViewBuilder _builder = new(_street, new MailCatchVolumeSource(_mail), new SimCounters());

    private static readonly Vec3Mm _chest = new(10_000, 1000, 20_000);

    /// <summary>Street segments and the registry ordinals (house number - 1) whose catch box the segment's AABB overlaps.</summary>
    public static TheoryData<string, Vec3Mm, Vec3Mm, int[]> GridCases() => new()
    {
        // House 1's box: x 33 500 to 34 500, y 1600 to 2400, z 24 800 to 25 800 (cell 8, 6).
        { "inside one cell", new Vec3Mm(34_000, 3000, 25_300), new Vec3Mm(34_000, 2000, 25_300), [0] },

        // 3 x 4 cells; house 2's box (x 39 500 to 40 500) straddles the x = 40 000 cell edge and is listed in both cells.
        { "spans several cells", new Vec3Mm(33_000, 2000, 25_000), new Vec3Mm(41_000, 2000, 39_000), [0, 1] },
        { "on a cell edge", new Vec3Mm(40_000, 3000, 38_700), new Vec3Mm(40_000, 1000, 38_700), [1] },
        { "touching a box face", new Vec3Mm(36_000, 2000, 25_300), new Vec3Mm(34_500, 2000, 25_300), [0] },
        { "no overlap", new Vec3Mm(36_000, 2000, 25_300), new Vec3Mm(34_501, 2000, 25_300), [] },
        { "map corner", new Vec3Mm(126_000, 3000, 62_000), new Vec3Mm(128_000, 1000, 64_000), [] },
        { "off the map edge", new Vec3Mm(-2000, 2000, -2000), new Vec3Mm(1000, 2000, 1000), [] },
    };

    /// <summary>TC-E-1 a worlds; all are flat at y = 0 around the FX-T feet (10 000, 0, 20 000) except the slope.</summary>
    public static TheoryData<string, bool> ProbeCases() => new()
    {
        { "wall", true },
        { "ledge", true },
        { "origin below slope", true },
        { "clear", false },
    };

    [Theory]
    [MemberData(nameof(GridCases))]
    [Trait("Id", "TC-F4-1")]
    [Trait("Id", "TR-throwing-007")]
    public void GridCandidates_EqualTheBruteForceOverlapScan_InAscendingKeyWithoutDuplicates(string row, Vec3Mm p0, Vec3Mm p1, int[] expectedOrdinals)
    {
        // Arrange
        var scratch = new ReceiverScratch();
        uint[] expected = expectedOrdinals.Select(o => _mail.DestinationAt(o).Value).ToArray();
        uint[] bruteForce = Enumerable.Range(0, _mail.DestinationCount)
            .Where(o => ClosedOverlap(p0, p1, _mail.CatchVolumeAt(o)))
            .Select(o => _mail.DestinationAt(o).Value)
            .ToArray();

        // Act
        ReceiverView view = _builder.Build(in p0, in p1, ReceiverMask.All, ReceiverRanks.Default, default, scratch);
        uint[] keys = view.Mailboxes.ToArray().Select(m => m.Key).ToArray();

        // Assert
        Assert.True(bruteForce.SequenceEqual(expected), row + ": the row's expectation is stale against the street");
        Assert.True(keys.SequenceEqual(bruteForce), row + ": grid [" + string.Join(", ", keys) + "]");
    }

    [Fact]
    [Trait("Id", "TR-throwing-005")]
    public void Mailboxes_AreMailsRegistryRows_CatchBoxAndDestinationIdAsKey()
    {
        // Arrange: a segment whose AABB covers the whole map (the linear-scan path).
        var scratch = new ReceiverScratch();
        DestinationRegistry registry = _mail.Registry;
        var p0 = new Vec3Mm(0, 0, 0);
        var p1 = new Vec3Mm(128_000, 5000, 64_000);

        // Act
        ReceiverView view = _builder.Build(in p0, in p1, ReceiverMask.StaticOnly, ReceiverRanks.Default, default, scratch);
        VolumeReceiver[] mailboxes = view.Mailboxes.ToArray();

        // Assert
        Assert.Equal(M1aStreet.HouseCount, registry.Count);
        Assert.Equal(registry.Count, mailboxes.Length);
        for (ushort i = 0; i < registry.Count; i++)
        {
            Assert.Equal(registry.CatchVolumeOf(new DestinationOrdinal(i)), mailboxes[i].Box);
            Assert.Equal(registry.EntityIdOf(new DestinationOrdinal(i)).Value, mailboxes[i].Key);
        }
    }

    [Fact]
    [Trait("Id", "TR-throwing-007")]
    public void MailboxCandidatesPastCapacity_KeepTheLowest64_AndCountThrowingCandidateOverflowOnce()
    {
        // Arrange: 70 volumes stacked on one spot.
        var box = new AabbMm(new Vec3Mm(5000, 500, 5000), new Vec3Mm(6000, 1500, 6000));
        var source = new Volumes(Enumerable.Range(0, 70).Select(i => (box, (uint)(100 + i))).ToArray());
        var counters = new SimCounters();
        var builder = new ReceiverViewBuilder(WorldTablesFixture.Dry(), source, counters);
        var p0 = new Vec3Mm(5500, 2000, 5500);
        var p1 = new Vec3Mm(5500, 1000, 5500);

        // Act
        ReceiverView view = builder.Build(in p0, in p1, ReceiverMask.All, ReceiverRanks.Default, default, new ReceiverScratch());
        uint[] keys = view.Mailboxes.ToArray().Select(m => m.Key).ToArray();

        // Assert
        Assert.Equal(Enumerable.Range(100, ThrowSettings.MaxReceiverCandidates).Select(k => (uint)k), keys);
        Assert.Equal(1L, counters[CounterId.ThrowingCandidateOverflow]);
    }

    [Theory]
    [InlineData(ReceiverMask.All)]
    [InlineData(ReceiverMask.StaticOnly)]
    [Trait("Id", "TR-throwing-003")]
    public void Build_CarriesTheWorldAndItsBounds_AndPassesTheCatchersOnlyUnderAll(ReceiverMask mask)
    {
        // Arrange
        SphereReceiver[] catchers = [new(new Vec3Mm(34_000, 2000, 27_000), 1000, 7), new(new Vec3Mm(36_000, 2000, 27_000), 1000, 9)];
        var p0 = new Vec3Mm(34_000, 3000, 25_300);
        var p1 = new Vec3Mm(34_000, 2000, 25_300);

        // Act
        ReceiverView view = _builder.Build(in p0, in p1, mask, ReceiverRanks.Default, catchers, new ReceiverScratch());

        // Assert
        Assert.Same(_street, view.World);
        Assert.Equal(_street.Bounds, view.Bounds);
        if (mask == ReceiverMask.All)
        {
            Assert.True(view.Catchers == (ReadOnlySpan<SphereReceiver>)catchers, "Catchers is not the caller's span");
        }
        else
        {
            Assert.True(view.Catchers.IsEmpty);
        }
    }

    [Theory]
    [MemberData(nameof(ProbeCases))]
    [Trait("Id", "TC-E-1")]
    public void ChestToOriginBlocked_ByWallLedgeOrSlope_LaunchesFromTheChest_AClearPathFromTheF1Origin(string row, bool blocked)
    {
        // Arrange
        WorldTables world = ProbeWorld(row);
        LaunchInput input = G.Input(G.FxTDir.Dx, G.FxTDir.Dy, G.FxTDir.Dz, G.DefaultSettings(), probe: new ChestOriginProbe(world));

        // Act
        bool ok = LaunchMath.Compute(in input, out Launch l, out bool fromChest);

        // Assert
        Assert.True(ok);
        Assert.Equal(blocked, fromChest);
        Assert.Equal(blocked ? _chest : G.FxTOrigin, l.Origin);
        if (row == "ledge")
        {
            // Mail Dev default, pending Evan (TD-202 item 2): the ledge row (face 10 mm ahead of the chest) carries the age-1 check; the
            // wall 200 mm ahead is met at f about 439 on the age-1 segment, so the GDD's f about 0 holds only for a solid at the chest.
            Vec3Mm p1 = Ballistic.PositionAt(in l, 1);
            var builder = new ReceiverViewBuilder(world, new Volumes([]), new SimCounters());
            ReceiverView view = builder.Build(in l.Origin, in p1, ReceiverMask.All, ReceiverRanks.Default, default, new ReceiverScratch());
            Assert.True(FlightSweep.FirstContact(in l.Origin, in p1, in view, new FlightId(1), out Contact c));
            Assert.Equal(ReceiverKind.Solid, c.Kind);
            Assert.InRange(c.FPm, 0, 100);
        }
    }

    [Theory]
    [InlineData("water", M1aStreet.BeachShallowXMm, 2000, M1aStreet.BeachShallowZMm, ReceiverKind.Water, 0)]
    [InlineData("street", 50_000, 3000, M1aStreet.StreetCentreZMm, ReceiverKind.Ground, M1aStreet.GroundMm)]
    [InlineData("roof of house 1", 34_000, 5000, 18_000, ReceiverKind.Ground, M1aStreet.GroundMm)]
    [Trait("Id", "TC-T6-3")]
    public void SurfaceBelow_IsWaterAtSeaLevel_ElseTheGroundUnderThePoint_NeverARoof(string row, int x, int y, int z, ReceiverKind kind, int surfaceY)
    {
        // Arrange
        var p = new Vec3Mm(x, y, z);

        // Act
        ReceiverKind got = SurfaceBelow.At(_street, in p, out Vec3Mm surface);

        // Assert
        Assert.True(got == kind, row + ": " + got);
        Assert.Equal(new Vec3Mm(x, surfaceY, z), surface);
    }

    [Fact]
    [Trait("Id", "TR-throwing-005-purity")]
    public void Build_OnTheStreet_AThousandCalls_AllocateZeroBytes()
    {
        // Arrange: the 0 B guardrail applies to optimised builds only (TD-008). The several-cells segment (merge path, two
        // mailboxes) with two catchers.
        var debuggable = (System.Diagnostics.DebuggableAttribute?)Attribute.GetCustomAttribute(
            typeof(ReceiverViewBuilder).Assembly, typeof(System.Diagnostics.DebuggableAttribute));
        Assert.SkipWhen(
            debuggable is { IsJITOptimizerDisabled: true },
            "Unoptimised Sim: the 0 B guardrail applies to optimised builds only (TD-008).");
        var scratch = new ReceiverScratch();
        SphereReceiver[] catchers = [new(new Vec3Mm(34_000, 2000, 27_000), 1000, 7), new(new Vec3Mm(36_000, 2000, 27_000), 1000, 9)];
        var p0 = new Vec3Mm(33_000, 2000, 25_000);
        var p1 = new Vec3Mm(41_000, 2000, 39_000);
        long checksum = 0;

        // Act + Assert (0 B)
        int runs = AllocAssert.Zero("ReceiverViewBuilder.Build x 1000", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                ReceiverView view = _builder.Build(in p0, in p1, ReceiverMask.All, ReceiverRanks.Default, catchers, scratch);
                checksum += view.Mailboxes.Length + view.Catchers.Length;
            }
        });
        Assert.Equal(4L * 1000 * runs, checksum);
    }

    // ------------------------------------------------------------------ helpers

    private static WorldTables BuildStreet()
    {
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        return b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    private static bool ClosedOverlap(Vec3Mm p0, Vec3Mm p1, AabbMm box) =>
        Math.Min(p0.X, p1.X) <= box.Max.X && box.Min.X <= Math.Max(p0.X, p1.X)
        && Math.Min(p0.Y, p1.Y) <= box.Max.Y && box.Min.Y <= Math.Max(p0.Y, p1.Y)
        && Math.Min(p0.Z, p1.Z) <= box.Max.Z && box.Min.Z <= Math.Max(p0.Z, p1.Z);

    /// <summary>
    /// 16 x 16 tiles (32 m). The FX-T throw goes -Z from the feet (10 000, 0, 20 000): chest (10 000, 1000, 20 000), F1 origin
    /// (10 250, 1200, 19 700). Wall: a House 200 mm in front of the feet. Ledge: a box at chest height whose face is 10 mm ahead
    /// of the chest. Slope: the terrain rises 10 000 mm over the tile north of the feet, so it is 1500 at the origin's z.
    /// </summary>
    private static WorldTables ProbeWorld(string row)
    {
        const int Tiles = 16;
        const int Stride = Tiles + 1;
        var corners = new int[Stride * Stride];
        if (row == "origin below slope")
        {
            for (int cz = 0; cz < Stride; cz++)
            {
                for (int cx = 0; cx < Stride; cx++)
                {
                    corners[(cz * Stride) + cx] = cz * WorldConstants.TileSizeMm < G.Feet.Z ? 10_000 : 0;
                }
            }
        }

        var b = new WorldTablesBuilder();
        b.SetCorners(Tiles, Tiles, corners);
        if (row == "wall")
        {
            b.AddSolid(EntityId.None, new AabbMm(new Vec3Mm(9000, 0, 19_500), new Vec3Mm(11_000, 3000, 19_800)), SolidKind.House);
        }
        else if (row == "ledge")
        {
            b.AddSolid(EntityId.None, new AabbMm(new Vec3Mm(9000, 900, 19_000), new Vec3Mm(11_000, 1400, 19_990)), SolidKind.Prop);
        }

        return b.Build();
    }

    /// <summary>A hand-built catch-volume source.</summary>
    private sealed class Volumes((AabbMm Box, uint Key)[] rows) : ICatchVolumeSource
    {
        public int Count => rows.Length;

        public EntityId DestinationAt(int ordinal) => new(rows[ordinal].Key);

        public AabbMm CatchVolumeAt(int ordinal) => rows[ordinal].Box;
    }
}
