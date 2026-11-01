using System.Buffers.Binary;
using System.Collections.Immutable;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Economy;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Primitives;
using PerformativeMail.Sim.Tests.Unit.MailDestinations;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Integration.MailDestinations;

/// <summary>
/// Mail-destinations story 005 AC-3 and AC-4 (ADR-0017 sections 7 and 8, ADR-0003 section 4 errata 2026-10-28; M-PERF-1c, M-E-1):
/// the M-1a profile composed over the 10-lot M-1a street builds the <see cref="StubWallet"/> before the systems, injects it into
/// <see cref="MailSystem"/> as <c>IWalletGate</c>, and an idle or paused Mail touches nothing. Also the carried TD-079 item 1
/// obligation: <see cref="IMailApi.TryGetDestinationFacts"/> answers exactly as the registry. Joins the non-parallel collection
/// for the advisory allocation report (see <see cref="AllocAssert"/>).
/// </summary>
[Collection(NonParallelCollection.Name)]
public class MailCompositionTests
{
    private const ulong Seed = 0x5EED_0005;

    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    private static SimHost BuildM1a(ContentSet content) =>
        SimComposition.Build(new SimBuildArgs(SimProfile.M1aGreybox, content, Seed) { WorldSource = M1aWorldSource.Instance });

    private static MailSystem MailOf(SimHost host) => Assert.Single(host.Systems.OfType<MailSystem>());

    [Fact]
    [Trait("Id", "M-R7-2a")]
    public void Ac3_M1aGreybox_BuildsTheStubWalletBeforeTheSystems_InjectsItIntoMail_AndMailRegistersEveryLot()
    {
        // Act
        SimHost host = BuildM1a(_content);
        MailSystem mail = MailOf(host);
        List<(ushort Id, byte[] Payload)> sections = Sections(host.World);

        // Assert: the wallet is the injected port, built from sim.startingWalletCents (0).
        Assert.NotNull(host.Wallet);
        Assert.Same(host.Wallet, mail.WalletGate);
        Assert.Equal(0L, host.Wallet!.Balance.Value);

        // The registry was built from the injected world while Constructed: one row per lot of the 10-lot street.
        Assert.Equal(10, mail.Registry.Count);
        for (ushort i = 0; i < mail.Registry.Count; i++)
        {
            Assert.True(mail.TryGetDestinationFacts(mail.Registry.EntityIdOf(new DestinationOrdinal(i)), out _));
        }

        // SystemGraph is unchanged: no Mail -> Economy edge (Economy -> Mail stays), so Build did not throw DependencyCycle.
        Assert.False(SystemGraph.Shipped.Declares(SystemId.MailDestinations, SystemId.Economy));
        Assert.True(SystemGraph.Shipped.Declares(SystemId.Economy, SystemId.MailDestinations));

        // Section order: SimCore, World, Wallet (balance i64), then the systems in construction order (Inventory, Players, Mail, Throwing).
        Assert.Equal(
            new[] { SectionIds.SimCore, SectionIds.World, SectionIds.Wallet, SectionIds.Inventory, SectionIds.Players, SectionIds.Mail, SectionIds.Throwing },
            sections.Select(s => s.Id).ToArray());
        Assert.Equal(new byte[8], sections[2].Payload);

        // A registry rejection is a composition error with its own code (TD-079 item 5), not an ArgumentException.
        ContentSet noHouse = new(
            _content.Balance,
            _content.ResolvedStatBounds,
            _content.Containers,
            _content.Items,
            _content.MailKinds,
            new DefTable<DestinationTypeDef>(ImmutableArray.Create(_content.DestinationTypes[DefIndex.FromValue(0)] with { Id = "shed" })),
            _content.MailMixes);
        SimBuildException e = Assert.Throws<SimBuildException>(() => BuildM1a(noHouse));
        Assert.Equal(SimBuildErrorCode.MailRegistryInvalid, e.Code);
    }

    [Fact]
    [Trait("Id", "M-PERF-1c")]
    [Trait("Id", "M-E-1")]
    public void Ac4_IdleTicks_VisitNoRowOrBucket_AndLeaveTheMailSectionUnchanged_PauseChangesNothing_AndNoWallClockIsRead()
    {
        // Arrange: M-PERF-1c's scope, 10 destinations and 0 feeders.
        SimHost host = BuildM1a(_content);
        Assert.Equal(10, MailOf(host).Registry.Count);
        byte[] before = MailSection(host.World);

        // Act: 300 idle ticks.
        for (int t = 0; t < 300; t++)
        {
            host.World.Tick();
        }

        byte[] after = MailSection(host.World);

        // Pause: two reads with no Step between, at least 50 ms apart (test-side wall clock only).
        byte[] pausedA = MailSection(host.World);
        Thread.Sleep(60);
        byte[] pausedB = MailSection(host.World);

        // Assert
        Assert.Equal(300u, host.World.CurrentTick);
        Assert.Equal(0L, host.World.Counters[CounterId.MailRowsVisited]);
        Assert.Equal(0L, host.World.Counters[CounterId.MailBucketsVisited]);
        Assert.Equal(before, after);
        Assert.Equal(pausedA, pausedB);
        Assert.Empty(MailScan.WallClockReads(MailScan.MailNamespace));
        Assert.Empty(MailScan.WallClockReads(MailScan.EconomyNamespace));

        // The 0 B check is reported, not asserted, until M0 (erratum E2).
        (long bytes, _) = AllocAssert.Measure(() =>
        {
            for (int t = 0; t < 300; t++)
            {
                host.World.Tick();
            }
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"M-PERF-1c advisory: 300 idle M-1a ticks allocated {bytes} B (0 B gate blocking from M0, E2).");
    }

    [Fact]
    [Trait("Id", "M-R3-facts")]
    public void TryGetDestinationFacts_DelegatesToTheRegistry_WithIdenticalResultsForEveryLotAndForAStranger()
    {
        // Arrange
        MailSystem mail = MailOf(BuildM1a(_content));
        DestinationRegistry registry = mail.Registry;
        var probes = new List<EntityId> { EntityId.None, EntityId.Compose(EntityClass.Player, 1) };
        for (ushort i = 0; i < registry.Count; i++)
        {
            probes.Add(registry.EntityIdOf(new DestinationOrdinal(i)));
        }

        // Act + Assert
        foreach (EntityId id in probes)
        {
            bool viaApi = ((IMailApi)mail).TryGetDestinationFacts(id, out DestinationFacts a);
            bool viaRegistry = registry.TryGetDestinationFacts(id, out DestinationFacts b);
            Assert.Equal(viaRegistry, viaApi);
            Assert.Equal(b, a);
        }

        Assert.Equal(registry.Count, probes.Count(id => registry.TryGetDestinationFacts(id, out _)));
    }

    private static byte[] MailSection(SimWorld world) => Assert.Single(Sections(world), s => s.Id == SectionIds.Mail).Payload;

    private static List<(ushort Id, byte[] Payload)> Sections(SimWorld world)
    {
        byte[] dump = DumpHash.Bytes(world);
        var sections = new List<(ushort, byte[])>();
        int pos = 6; // "PMSD" + format version
        while (pos < dump.Length)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(dump.AsSpan(pos));
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(dump.AsSpan(pos + 2));
            sections.Add((id, dump.AsSpan(pos + 6, length).ToArray()));
            pos += 6 + length;
        }

        return sections;
    }
}
