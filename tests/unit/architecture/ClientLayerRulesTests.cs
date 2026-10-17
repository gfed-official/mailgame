using System.Reflection;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.MailDestinations;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// SC-ARCH-1 row (b), GDD R2 / ADR-0003 section 6 (TR-sim-013): no type under <c>PerformativeMail.Sim.ClientLayer</c> (nested
/// and compiler-generated types included) names <see cref="SimWorld"/> or a type from a gameplay system namespace in a field,
/// property, event, constructor parameter or method signature; and no method body references <see cref="SimWorld"/> (IL member
/// references, lambdas included). Gameplay types are banned at signature level only: ADR-0003 section 6 places pure functions
/// (e.g. Mail's evaluation, the carrier's held-effect maths) in gameplay namespaces and lets both roles call them, so a body
/// call into a gameplay namespace is allowed.
/// </summary>
[Trait("Id", "SC-ARCH-1")]
public class ClientLayerRulesTests
{
    private static readonly Assembly _sim = typeof(SimAssembly).Assembly;

    [Fact]
    public void SimAssembly_ClientLayerTypes_NameNoSimWorldOrGameplayType()
    {
        // Arrange
        Type[] clientLayer = _sim.GetTypes()
            .Where(t => t.Namespace is { } ns && (ns == ClientLayerRules.Namespace || ns.StartsWith(ClientLayerRules.Namespace + ".", StringComparison.Ordinal)))
            .ToArray();

        // Act
        List<ArchViolation> violations = ClientLayerRules.Check(clientLayer);

        // Assert
        Assert.NotEmpty(clientLayer);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_GoodFixture_IsClean()
    {
        // Act
        List<ArchViolation> violations = ClientLayerRules.Check(ArchitectureScan.WithNested(typeof(ClientStepGoodFixture)).ToArray());

        // Assert
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_BadFixture_ReportsExactlyTheExpectedViolations()
    {
        // Arrange
        string[] expected =
        {
            "ClientStepHoldsWorldFixture: SimWorld",
            "ClientStepHoldsWorldFixture: PerformativeMail.Sim.Players.CarrierState",
            "ClientStepBodyWorldFixture: SimWorld",
            "ClientStepLambdaWorldFixture: SimWorld",
        };
        Type[] universe = new[] { typeof(ClientStepHoldsWorldFixture), typeof(ClientStepBodyWorldFixture), typeof(ClientStepLambdaWorldFixture) }
            .SelectMany(ArchitectureScan.WithNested)
            .ToArray();

        // Act
        List<ArchViolation> violations = ClientLayerRules.Check(universe);

        // Assert: the body-only PlayersSystem / MailTable references are not violations (ADR-0003 section 6).
        Assert.Equal(expected.Order(StringComparer.Ordinal), violations.Select(v => v.Key).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void GameplayNamespaces_AreTheSystemFolders_NotSimCore()
    {
        // Assert: the classification the rule relies on.
        Assert.True(ClientLayerRules.IsGameplay(typeof(PlayersSystem)));
        Assert.True(ClientLayerRules.IsGameplay(typeof(MailTable)));
        Assert.False(ClientLayerRules.IsGameplay(typeof(SimWorldBuilder)));
        Assert.False(ClientLayerRules.IsGameplay(typeof(PerformativeMail.Sim.Content.ContentSet)));
        Assert.False(ClientLayerRules.IsGameplay(typeof(PerformativeMail.Sim.Primitives.EntityId)));
        Assert.False(ClientLayerRules.IsGameplay(typeof(List<int>)));
    }
}

/// <summary>The SC-ARCH-1 row (b) engine.</summary>
internal static class ClientLayerRules
{
    /// <summary>The client layer namespace.</summary>
    public const string Namespace = "PerformativeMail.Sim.ClientLayer";

    private const string SimRoot = "PerformativeMail.Sim";

    // Sim Core and content folders (ADR-0001 section 1); every other PerformativeMail.Sim.<Folder> is a gameplay system folder.
    private static readonly HashSet<string> _nonGameplay = new(StringComparer.Ordinal)
    {
        "Primitives", "Geometry", "Core", "Hosting", "Composition", "ClientLayer", "Content",
    };

    /// <summary>True for a Sim type whose namespace is a gameplay system folder (Players, Mail, World, ...).</summary>
    public static bool IsGameplay(Type t)
    {
        if (t.Assembly != typeof(SimAssembly).Assembly || t.Namespace is not { } ns || !ns.StartsWith(SimRoot + ".", StringComparison.Ordinal))
        {
            return false;
        }

        string folder = ns[(SimRoot.Length + 1)..].Split('.')[0];
        return !_nonGameplay.Contains(folder);
    }

    /// <summary>Checks the signatures and method bodies of each given type.</summary>
    public static List<ArchViolation> Check(IReadOnlyList<Type> types)
    {
        var violations = new List<ArchViolation>();
        foreach (Type t in types)
        {
            string owner = ArchitectureScan.NameOf(ArchitectureScan.OwnerOf(t));
            foreach ((string where, Type type) in ArchitectureScan.SignatureTypes(t))
            {
                AddBanned(violations, owner, type, $"{ArchitectureScan.NameOf(t)} {where}");
            }

            foreach (MethodBase m in ArchitectureScan.MethodsWithBodies(t))
            {
                foreach (MemberInfo used in ArchitectureScan.BodyReferences(m))
                {
                    if (ArchitectureScan.TypesNamedBy(used).Contains(typeof(SimWorld)))
                    {
                        // Bodies: SimWorld only (ADR-0003 section 6 shared pure functions live in gameplay namespaces).
                        violations.Add(new ArchViolation(owner, nameof(SimWorld), $"body of {ArchitectureScan.NameOf(t)}.{m.Name} ({used.Name})"));
                    }
                }
            }
        }

        return violations;
    }

    private static void AddBanned(List<ArchViolation> violations, string owner, Type type, string where)
    {
        foreach (Type c in ArchitectureScan.Constituents(type).Where(c => !c.HasElementType))
        {
            if (c == typeof(SimWorld))
            {
                violations.Add(new ArchViolation(owner, nameof(SimWorld), where));
            }
            else if (IsGameplay(c))
            {
                violations.Add(new ArchViolation(owner, c.FullName ?? c.Name, where));
            }
        }
    }
}

/// <summary>Seen-passing fixture: a client step that uses only Sim Core and client-layer types.</summary>
internal sealed class ClientStepGoodFixture
{
    private readonly SimCounters _counters = new();

    public long Read(CounterId id) => _counters[id];

    /// <summary>A body-only call into a gameplay namespace: allowed (ADR-0003 section 6 shared pure functions).</summary>
    public Type[] Shared()
    {
        Func<Type> late = () => typeof(MailTable);
        return [typeof(PlayersSystem), late()];
    }
}

/// <summary>Seen-failing fixture: a step type holding <see cref="SimWorld"/> and taking a gameplay type in a signature.</summary>
internal sealed class ClientStepHoldsWorldFixture
{
    private readonly SimWorld? _world;

    public ClientStepHoldsWorldFixture(SimWorld? world)
    {
        _world = world;
    }

    public bool HasWorld => _world is not null;

    public void Step(ref CarrierState carrier)
    {
    }

}

/// <summary>Seen-failing fixture: references <see cref="SimWorld"/> only in a method body (plus allowed gameplay body references).</summary>
internal sealed class ClientStepBodyWorldFixture
{
    public bool IsWorld(object candidate) => candidate is SimWorld;

    public Type Shared() => typeof(PlayersSystem);
}

/// <summary>Seen-failing fixture: references <see cref="SimWorld"/> only inside a lambda.</summary>
internal sealed class ClientStepLambdaWorldFixture
{
    public Func<object, bool> Probe() => candidate => candidate is SimWorld;
}
