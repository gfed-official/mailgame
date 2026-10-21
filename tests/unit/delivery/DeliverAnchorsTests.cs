using System.Reflection;
using System.Text.RegularExpressions;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Delivery;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Tests.Unit.Content;
using PerformativeMail.Sim.Tests.Unit.PlayerCharacter;
using PerformativeMail.Sim.Tests.Unit.World;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Sim.Tests.Unit.ManualDelivery;

/// <summary>
/// Covers manual-delivery story 001 AC-1 (GDD M-MD1-1a; TR-delivery-001; ADR-0014 section 1): <see cref="DeliverAnchors.RegisterAll"/>
/// gives every destination of the M-1a street one Deliver anchor with the catalogue's mode, priority and default reach and the
/// <c>house</c> type's hold base; a type with <c>ManualInsertHoldMs</c> 0 gets none; Delivery's source never unregisters or moves an
/// anchor, and the anchors survive 600 ticks of a composed Players system. The run-cycle persistence leg is story 007's AC-7.
/// </summary>
public class DeliverAnchorsTests
{
    private const int DefaultRangeMm = 2500;

    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    // Delivery never unregisters or moves a Deliver anchor, and never sets a priority or reach (both come from the catalogue).
    private static readonly Regex _forbidden = new(@"\b(UnregisterAnchor|MoveAnchor|Unregister|Move)\s*\(|\b(Priority|RangeMm)\s*=(?!=)", RegexOptions.CultureInvariant);

    // ---------------------------------------------------------------- helpers

    private static WorldTables BuildStreet()
    {
        var b = new WorldTablesBuilder();
        M1aStreet.Author(b);
        return b.Build(WorldTablesFixture.NewIds(), WorldTablesFixture.SlopeMaxPm, WorldTablesFixture.SwimEnterMm);
    }

    private static ContentSet HouseWithHold(int manualInsertHoldMs)
    {
        ContentBuildResult r = ContentFixtures.MinimalValid()
            .Replace(ContentFixtures.House with { ManualInsertHoldMs = manualInsertHoldMs })
            .Build();
        Assert.NotNull(r.Set);
        return r.Set;
    }

    private static DeliverInteractable NewImpl() => new(StatSheet.Build(_content, new StatSources([])));

    private static AnchorRef DeliverRef(DestinationRegistry registry, int i) => new(registry.EntityIdOf(new DestinationOrdinal((ushort)i)), 0);

    private static int RegistrationsOf(AnchorRegistry anchors, AnchorRef anchor)
    {
        int n = 0;
        for (int i = 0; i < anchors.Count; i++)
        {
            if (anchors.DefAt(i).Ref == anchor)
            {
                n++;
            }
        }

        return n;
    }

    private static string StripComment(string line)
    {
        int at = line.IndexOf("//", StringComparison.Ordinal);
        return at < 0 ? line : line[..at];
    }

    private static string[] SourceFiles()
    {
        string dir = Path.Combine(FindRepoRoot(), "src", "PerformativeMail.Sim", "Delivery");
        Assert.True(Directory.Exists(dir), dir + " is missing");
        return Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PerformativeMail.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("PerformativeMail.sln not found above " + AppContext.BaseDirectory);
    }

    // ---------------------------------------------------------------- M-MD1-1a (a)

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void RegisterAll_TenLotStreet_RegistersExactlyTenDeliverAnchors_WithTheCatalogueDefaultsAndTheHouseHoldBase()
    {
        // Arrange
        WorldTables world = BuildStreet();
        var registry = new DestinationRegistry(world, _content);
        var players = new PlayersSystem(world, _content.Player);
        DeliverInteractable impl = NewImpl();
        int houseHoldMs = _content.DestinationTypes[registry.TypeOf(new DestinationOrdinal(0))].ManualInsertHoldMs;

        // Act
        RegisterResult result = DeliverAnchors.RegisterAll(players, registry, _content, impl);

        // Assert
        Assert.Equal(RegisterResult.Ok, result);
        Assert.Equal(10, registry.Count);
        Assert.Equal(400, houseHoldMs);   // the built-in house def, 0.4 s
        Assert.Equal(10, players.AnchorCount);
        for (int i = 0; i < registry.Count; i++)
        {
            AnchorRef anchor = DeliverRef(registry, i);
            Assert.True(players.Anchors.TryGet(anchor, AnchorRegistry.ContextWorld, out int index));
            ref readonly AnchorDef stored = ref players.Anchors.DefAt(index);
            Assert.Equal(anchor, stored.Ref);
            Assert.Equal(0, stored.Ref.Slot);
            Assert.Equal(registry.AnchorOf(new DestinationOrdinal((ushort)i)), stored.Point);
            Assert.Equal((Verb.Deliver, InteractMode.Hold, houseHoldMs), (stored.Verb, stored.Mode, stored.HoldBaseMs));
            Assert.Equal(DefaultRangeMm, stored.RangeMm);   // RangeMm 0 in the def resolves to the framework default
            Assert.Equal(VerbCatalogue.DefaultPriority(Verb.PickUp) + 1, stored.Priority);
            Assert.Equal(AnchorFlags.None, stored.Flags);
            Assert.Same(impl, players.Anchors.ImplAt(index));
        }
    }

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void RegisterAll_HouseTypeHoldOtherThanTheCatalogueDefault_IsTheStoredHoldBase()
    {
        // Arrange (600 differs from VerbCatalogue.DefaultHoldBaseMs(Deliver), which AnchorDef.For would otherwise leave)
        WorldTables world = BuildStreet();
        ContentSet slowHouse = HouseWithHold(600);
        var registry = new DestinationRegistry(world, slowHouse);
        var players = new PlayersSystem(world, _content.Player);

        // Act
        RegisterResult result = DeliverAnchors.RegisterAll(players, registry, slowHouse, NewImpl());

        // Assert
        Assert.Equal(RegisterResult.Ok, result);
        Assert.NotEqual(600, VerbCatalogue.DefaultHoldBaseMs(Verb.Deliver));
        Assert.Equal(10, players.AnchorCount);
        for (int i = 0; i < registry.Count; i++)
        {
            Assert.True(players.Anchors.TryGet(DeliverRef(registry, i), AnchorRegistry.ContextWorld, out int index));
            Assert.Equal(600, players.Anchors.DefAt(index).HoldBaseMs);
        }
    }

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void RegisterAll_FirstFailure_StopsAndReturnsIt()
    {
        // Arrange (a Pick-up already on destination 3's slot-0 anchor makes its Deliver a DuplicateVerb)
        WorldTables world = BuildStreet();
        var registry = new DestinationRegistry(world, _content);
        var players = new PlayersSystem(world, _content.Player);
        AnchorDef pickUp = AnchorDef.For(DeliverRef(registry, 3), registry.AnchorOf(new DestinationOrdinal(3)), Verb.PickUp);
        Assert.Equal(RegisterResult.Ok, players.RegisterAnchor(in pickUp, NewImpl()));

        // Act
        RegisterResult result = DeliverAnchors.RegisterAll(players, registry, _content, NewImpl());

        // Assert (destinations 0, 1 and 2 registered before the failure; nothing after it)
        Assert.Equal(RegisterResult.DuplicateVerb, result);
        Assert.Equal(1 + 3, players.AnchorCount);
        Assert.Equal(0, RegistrationsOf(players.Anchors, DeliverRef(registry, 4)));
    }

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void RegisterAll_EachMailbox_HasDeliverAsItsOnlyVerb_AndASecondVerbIsDuplicateVerb()
    {
        // Arrange
        WorldTables world = BuildStreet();
        var registry = new DestinationRegistry(world, _content);
        var players = new PlayersSystem(world, _content.Player);
        Assert.Equal(RegisterResult.Ok, DeliverAnchors.RegisterAll(players, registry, _content, NewImpl()));
        AnchorRef first = DeliverRef(registry, 0);
        AnchorDef pickUp = AnchorDef.For(first, registry.AnchorOf(new DestinationOrdinal(0)), Verb.PickUp);

        // Act
        RegisterResult second = players.RegisterAnchor(in pickUp, NewImpl());

        // Assert
        Assert.Equal(RegisterResult.DuplicateVerb, second);
        for (int i = 0; i < registry.Count; i++)
        {
            AnchorRef anchor = DeliverRef(registry, i);
            Assert.Equal(1, RegistrationsOf(players.Anchors, anchor));
            Assert.False(players.Anchors.TryGet(anchor, AnchorRegistry.ContextBuild, out _));
        }
    }

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void DeliverInteractable_DeclaresItsOwnGetHoldTicksAndShouldCancelHooks()
    {
        // Arrange
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

        // Act
        MethodInfo? holdTicks = typeof(DeliverInteractable).GetMethod(nameof(IInteractable.GetHoldTicks), Declared);
        MethodInfo? shouldCancel = typeof(DeliverInteractable).GetMethod(nameof(IInteractable.ShouldCancel), Declared);

        // Assert (an own GetHoldTicks, not the interface default that passes insertPm 0)
        Assert.NotNull(holdTicks);
        Assert.NotNull(shouldCancel);
        Assert.True(typeof(IInteractable).IsAssignableFrom(typeof(DeliverInteractable)));
        Assert.True(typeof(DeliverInteractable).IsSealed && !typeof(DeliverInteractable).IsPublic);
    }

    // ---------------------------------------------------------------- M-MD1-1a (b)

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void RegisterAll_HouseTypeWithManualInsertHoldZero_RegistersNoAnchor_AndReturnsOk()
    {
        // Arrange
        WorldTables world = BuildStreet();
        ContentSet zeroHold = HouseWithHold(0);
        var registry = new DestinationRegistry(world, zeroHold);
        var players = new PlayersSystem(world, _content.Player);

        // Act
        RegisterResult result = DeliverAnchors.RegisterAll(players, registry, zeroHold, NewImpl());

        // Assert
        Assert.Equal(RegisterResult.Ok, result);
        Assert.Equal(10, registry.Count);
        Assert.Equal(0, players.AnchorCount);
    }

    // ---------------------------------------------------------------- never deregistered

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void DeliverySource_NeverCallsUnregisterAnchorOrMoveAnchor_AndHasNoPriorityAssignment()
    {
        // Arrange
        string[] files = SourceFiles();
        List<string> hits = [];

        // Act
        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match m in _forbidden.Matches(StripComment(lines[i])))
                {
                    hits.Add($"{Path.GetFileName(file)}:{i + 1}: {m.Value}");
                }
            }
        }

        // Assert
        Assert.Contains(files, f => Path.GetFileName(f) == "DeliverAnchors.cs");
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [Theory]
    [Trait("Id", "M-MD1-1a")]
    [InlineData("players.UnregisterAnchor(anchor);")]
    [InlineData("_ = players.MoveAnchor(anchor, in point);")]
    [InlineData("def.Priority = 2;")]
    [InlineData("def.RangeMm = 2500;")]
    public void DeliverySourceScan_MatchesEachForbiddenCall(string line)
    {
        // Arrange
        string code = StripComment(line);

        // Act
        bool hit = _forbidden.IsMatch(code);

        // Assert
        Assert.True(hit);
    }

    [Theory]
    [Trait("Id", "M-MD1-1a")]
    [InlineData("if (def.Priority == other.Priority) { }")]
    [InlineData("byte p = VerbCatalogue.DefaultPriority(Verb.Deliver);")]
    public void DeliverySourceScan_IgnoresComparisonsAndCatalogueReads(string line)
    {
        // Arrange
        string code = StripComment(line);

        // Act
        bool hit = _forbidden.IsMatch(code);

        // Assert
        Assert.False(hit);
    }

    [Fact]
    [Trait("Id", "M-MD1-1a")]
    public void RegisterAll_TenAnchors_AreStillRegisteredAfter600TicksOfAComposedPlayersSystem()
    {
        // Arrange
        WorldTables world = BuildStreet();
        PlayersRig rig = PlayersRig.Create(world);
        var registry = new DestinationRegistry(world, _content);
        DeliverInteractable impl = NewImpl();
        int before = rig.Players.AnchorCount;
        Assert.Equal(RegisterResult.Ok, DeliverAnchors.RegisterAll(rig.Players, registry, _content, impl));
        uint startTick = rig.Sim.CurrentTick;

        // Act
        rig.TickEmpty(600);

        // Assert (the ticks really ran: SimWorld.Tick is a no-op outside Ready/Running)
        Assert.Equal(startTick + 600, rig.Sim.CurrentTick);
        Assert.Equal(before + 10, rig.Players.AnchorCount);
        for (int i = 0; i < registry.Count; i++)
        {
            Assert.True(rig.Players.Anchors.TryGet(DeliverRef(registry, i), AnchorRegistry.ContextWorld, out int index));
            Assert.Equal(Verb.Deliver, rig.Players.Anchors.DefAt(index).Verb);
            Assert.Equal(registry.AnchorOf(new DestinationOrdinal((ushort)i)), rig.Players.Anchors.DefAt(index).Point);
            Assert.Same(impl, rig.Players.Anchors.ImplAt(index));
        }
    }
}
