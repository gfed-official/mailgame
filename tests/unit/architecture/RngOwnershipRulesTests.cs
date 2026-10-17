using System.Reflection;
using System.Text;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Tests.Unit.World;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// SC-ARCH-1 row (f), ADR-0005 section 3 (TR-sim-029): in a composed world, every <see cref="IRngStreams"/> view a system holds
/// is an <see cref="RngOwnerView"/> built for that system and exposes only names whose registered owner is that system;
/// Throwing's view is empty. Views are found by reading each built system's instance fields. Until the gameplay systems
/// exist, fixture systems stand in; each epic that adds its system to <see cref="SimProfile.Full"/> is covered automatically.
/// </summary>
[Trait("Id", "SC-ARCH-1")]
public class RngOwnershipRulesTests
{
    private static readonly ContentSet _content = BuiltInContent.CreateM1a();

    public static TheoryData<string> ShippedProfiles => new() { "Full", "M1aGreybox" };

    [Theory]
    [MemberData(nameof(ShippedProfiles))]
    public void ShippedProfile_ComposedWithAWorld_EveryHeldViewIsOwnerOnly(string profileName)
    {
        // Arrange
        SimProfile profile = profileName == "Full" ? SimProfile.Full : SimProfile.M1aGreybox;
        SimHost host = SimComposition.Build(new SimBuildArgs(profile, _content, 7) { World = WorldTablesFixture.Pad() });

        // Act
        List<ArchViolation> violations = RngOwnershipRules.Check(RngOwnershipRules.ViewsOf(host));

        // Assert
        Assert.NotEmpty(host.Systems);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void FixtureWorld_EveryStreamOwnerAndThrowingTakeTheContextView_AllOwnerOnly()
    {
        // Arrange: one fixture system per stream owner, plus Throwing, each taking the composition context's view.
        SystemId[] owners = Enumerable.Range(0, RngNames.Count).Select(i => RngNames.ByOrdinal(i).Owner).Distinct().ToArray();
        SimSystemEntry[] entries = owners.Append(SystemId.Throwing)
            .Select(id => new SimSystemEntry(id, ctx => new RngFixtureSystem(ctx.Rng)))
            .ToArray();
        SimHost host = SimComposition.Build(new SimBuildArgs(SimProfile.ForTest("rng-owners", null, entries), _content, 7));

        // Act
        List<(SystemId System, IRngStreams View)> views = RngOwnershipRules.ViewsOf(host);
        List<ArchViolation> violations = RngOwnershipRules.Check(views);

        // Assert: clean, every system holds one view, Throwing's is empty and together the views cover every name once.
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.Equal(owners.Length + 1, views.Count);
        Assert.Empty(((RngOwnerView)views.Single(v => v.System == SystemId.Throwing).View).ExposedNames());
        Assert.Equal(RngNames.Count, views.Sum(v => ((RngOwnerView)v.View).ExposedNames().Length));
    }

    [Fact]
    public void RuleEngine_ViewsBuiltForTheWrongOwner_ReportExactlyTheExpectedViolations()
    {
        // Arrange: views minted from a foreign registry for the wrong owner, and a view that is not owner-filtered.
        var foreign = new RngRegistry(1, new SimCounters());
        SimProfile profile = SimProfile.ForTest(
            "rng-wrong-owner",
            null,
            new SimSystemEntry(SystemId.MailDestinations, _ => new RngFixtureSystem(foreign.ViewFor(SystemId.Economy))),
            new SimSystemEntry(SystemId.Throwing, _ => new RngFixtureSystem(foreign.ViewFor(SystemId.Perks))),
            new SimSystemEntry(SystemId.Economy, _ => new RngFixtureSystem(new UnfilteredStreamsFixture(foreign))));
        SimHost host = SimComposition.Build(new SimBuildArgs(profile, _content, 7));
        string[] expected =
        {
            "MailDestinations: view built for Economy",
            $"MailDestinations: exposes {RngOwnershipRules.Label(RngNames.Shop)} owned by Economy",
            "Throwing: view built for Perks",
            $"Throwing: exposes {RngOwnershipRules.Label(RngNames.Perks)} owned by Perks",
            "Throwing: view is not empty",
            "Economy: view UnfilteredStreamsFixture is not an owner-filtered RngOwnerView",
        };

        // Act
        List<ArchViolation> violations = RngOwnershipRules.Check(RngOwnershipRules.ViewsOf(host));

        // Assert
        Assert.Equal(expected.Order(StringComparer.Ordinal), violations.Select(v => v.Key).Order(StringComparer.Ordinal));
    }
}

/// <summary>The SC-ARCH-1 row (f) engine.</summary>
internal static class RngOwnershipRules
{
    private const BindingFlags InstanceDeclared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>A stream name for messages (its ASCII name, or the ordinal when it has none).</summary>
    public static string Label(in StreamName name) => name.Name.IsEmpty ? $"#{name.Ordinal}" : Encoding.ASCII.GetString(name.Name);

    /// <summary>
    /// Every <see cref="IRngStreams"/> held in an instance field (own or inherited) of a built system, paired with the system's
    /// id: its <see cref="SimSystemAttribute"/> id, or its profile position when every profile entry was built (test profiles).
    /// </summary>
    public static List<(SystemId System, IRngStreams View)> ViewsOf(SimHost host)
    {
        var views = new List<(SystemId, IRngStreams)>();
        for (int i = 0; i < host.Systems.Length; i++)
        {
            ISimSystem system = host.Systems[i];
            SystemId id = system.GetType().GetCustomAttribute<SimSystemAttribute>()?.Id
                ?? (host.Systems.Length == host.Order.Length
                    ? host.Order[i]
                    : throw new InvalidOperationException($"{system.GetType().Name} has no [SimSystem] and the profile skipped systems."));
            for (Type? t = system.GetType(); t is not null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(InstanceDeclared))
                {
                    if (f.GetValue(system) is IRngStreams view)
                    {
                        views.Add((id, view));
                    }
                }
            }
        }

        return views;
    }

    /// <summary>Checks each (system, view) pair.</summary>
    public static List<ArchViolation> Check(IEnumerable<(SystemId System, IRngStreams View)> views)
    {
        var violations = new List<ArchViolation>();
        foreach ((SystemId system, IRngStreams view) in views)
        {
            string owner = system.ToString();
            if (view is not RngOwnerView filtered)
            {
                violations.Add(new ArchViolation(owner, $"view {view.GetType().Name} is not an owner-filtered RngOwnerView", "held view"));
                continue;
            }

            if (filtered.Owner != system)
            {
                violations.Add(new ArchViolation(owner, $"view built for {filtered.Owner}", "held view"));
            }

            StreamName[] exposed = filtered.ExposedNames();
            foreach (StreamName name in exposed.Where(n => n.Owner != system))
            {
                violations.Add(new ArchViolation(owner, $"exposes {Label(in name)} owned by {name.Owner}", "held view"));
            }

            if (system == SystemId.Throwing && exposed.Length > 0)
            {
                violations.Add(new ArchViolation(owner, "view is not empty", "held view"));
            }
        }

        return violations;
    }
}

/// <summary>A fixture system that holds the RNG view it was given (test assembly only).</summary>
internal sealed class RngFixtureSystem : ISimSystem
{
    private readonly IRngStreams _streams;

    public RngFixtureSystem(IRngStreams streams)
    {
        _streams = streams;
    }

    public IRngStreams Streams => _streams;

    public void Bind(SimWorldBuilder builder)
    {
    }
}

/// <summary>Seen-failing fixture: an <see cref="IRngStreams"/> that is not owner-filtered (hands out any stream).</summary>
internal sealed class UnfilteredStreamsFixture : IRngStreams
{
    private readonly RngRegistry _registry;

    public UnfilteredStreamsFixture(RngRegistry registry)
    {
        _registry = registry;
    }

    public RngStream Create(in StreamName name, uint index) => _registry.Create(in name, index);
}
