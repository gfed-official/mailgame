using System.Reflection;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players.Client;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// Player Character story 015 (GDD P11 "client-side only; the host has no line-of-sight check"; ADR-0014 section 2): no type in
/// <c>PerformativeMail.Sim.Players</c> outside <c>PerformativeMail.Sim.Players.Client</c> (nested and compiler-generated types
/// included) names <see cref="FocusOcclusion"/> in a signature or a method body. Story 019's host <c>CheckInteraction</c> inherits it.
/// </summary>
[Trait("Id", "PCM-P11-1")]
public class HostLineOfSightRulesTests
{
    private const string Players = "PerformativeMail.Sim.Players";
    private const string PlayersClient = Players + ".Client";

    [Fact]
    public void SimAssembly_PlayersTypesOutsideClient_NeverReferenceFocusOcclusion()
    {
        // Arrange
        Type[] host = typeof(SimAssembly).Assembly.GetTypes()
            .Where(t => t.Namespace is { } ns
                && (ns == Players || ns.StartsWith(Players + ".", StringComparison.Ordinal))
                && ns != PlayersClient && !ns.StartsWith(PlayersClient + ".", StringComparison.Ordinal))
            .ToArray();

        // Act
        List<ArchViolation> violations = Check(host);

        // Assert
        Assert.Contains(typeof(PerformativeMail.Sim.Players.PlayersSystem), host);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_FocusStep_IsReportedForItsBodyCalls()
    {
        // Act: the seen-failing case is the real client caller.
        List<ArchViolation> violations = Check(ArchitectureScan.WithNested(typeof(FocusStep)).ToArray());

        // Assert
        Assert.Equal(new[] { "FocusStep: FocusOcclusion" }, violations.Select(v => v.Key).Distinct());
        Assert.All(violations, v => Assert.StartsWith("body of", v.Where, StringComparison.Ordinal));
    }

    private static List<ArchViolation> Check(IReadOnlyList<Type> types)
    {
        var violations = new List<ArchViolation>();
        foreach (Type t in types)
        {
            string owner = ArchitectureScan.NameOf(ArchitectureScan.OwnerOf(t));
            foreach ((string where, Type type) in ArchitectureScan.SignatureTypes(t))
            {
                if (ArchitectureScan.Constituents(type).Contains(typeof(FocusOcclusion)))
                {
                    violations.Add(new ArchViolation(owner, nameof(FocusOcclusion), $"{ArchitectureScan.NameOf(t)} {where}"));
                }
            }

            foreach (MethodBase m in ArchitectureScan.MethodsWithBodies(t))
            {
                foreach (MemberInfo used in ArchitectureScan.BodyReferences(m))
                {
                    if (ArchitectureScan.TypesNamedBy(used).Contains(typeof(FocusOcclusion)))
                    {
                        violations.Add(new ArchViolation(owner, nameof(FocusOcclusion), $"body of {ArchitectureScan.NameOf(t)}.{m.Name} ({used.Name})"));
                    }
                }
            }
        }

        return violations;
    }
}
