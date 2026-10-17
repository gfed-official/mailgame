using System.Reflection;
using PerformativeMail.Sim.Composition;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// SC-ARCH-1 row (g), owner brief TD-061(3), ADR-0003 section 4 / manifest "no SimWorld references in systems": no
/// <c>[SimSystem]</c> type — nor any type nested in it, nor a base class it inherits from — has a field, property, event,
/// constructor parameter or method parameter/return type naming <c>SimSystemContext</c> or <see cref="SimWorld"/> (arrays,
/// by-ref, nullable and generic arguments unwrapped). <see cref="SimWorldBuilder"/> in <see cref="ISimSystem.Bind"/> is allowed.
/// </summary>
[Trait("Id", "SC-ARCH-1")]
public class SystemIsolationRulesTests
{
    private static readonly Assembly _sim = typeof(SimAssembly).Assembly;

    [Fact]
    public void SimAssembly_SimSystemTypes_NameNoContextOrWorld()
    {
        // Arrange
        Type[] systems = _sim.GetTypes().Where(t => t.IsDefined(typeof(SimSystemAttribute), false)).ToArray();

        // Act
        List<ArchViolation> violations = SystemIsolationRules.Check(systems);

        // Assert
        Assert.NotEmpty(systems);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_GoodFixture_BindingTheBuilder_IsClean()
    {
        // Act
        List<ArchViolation> violations = SystemIsolationRules.Check(new[] { typeof(IsolationGoodSystem) });

        // Assert
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_BadFixtures_ReportExactlyTheExpectedViolations()
    {
        // Arrange
        Type[] universe = { typeof(IsolationHoldsWorldSystem), typeof(IsolationTakesContextSystem), typeof(IsolationInheritsWorldSystem) };
        string[] expected =
        {
            "IsolationHoldsWorldSystem: SimWorld in field _world",
            "IsolationHoldsWorldSystem: SimWorld in property Worlds",
            "IsolationHoldsWorldSystem: SimWorld in field <Worlds>k__BackingField",
            "IsolationHoldsWorldSystem: SimWorld in return of get_Worlds",
            "IsolationHoldsWorldSystem: SimWorld in return of Snapshot",
            "IsolationHoldsWorldSystem: SimWorld in parameter world of Swap",
            "IsolationTakesContextSystem: SimSystemContext in constructor parameter context",
            "IsolationTakesContextSystem+Helper: SimSystemContext in field _context",
            "IsolationTakesContextSystem+Helper: SimWorld in return of Find",
            "IsolationWorldBase: SimWorld in field _world",
        };

        // Act
        List<ArchViolation> violations = SystemIsolationRules.Check(universe);

        // Assert
        Assert.Equal(expected.Order(StringComparer.Ordinal), violations.Select(v => v.Key).Order(StringComparer.Ordinal));
    }
}

/// <summary>The SC-ARCH-1 row (g) engine.</summary>
internal static class SystemIsolationRules
{
    private static readonly Type[] _banned = { typeof(SimSystemContext), typeof(SimWorld) };

    /// <summary>Checks each <c>[SimSystem]</c> type in <paramref name="types"/>, its nested types and its base classes.</summary>
    public static List<ArchViolation> Check(IReadOnlyList<Type> types)
    {
        var violations = new List<ArchViolation>();
        var seen = new HashSet<Type>();
        foreach (Type system in types.Where(t => t.IsDefined(typeof(SimSystemAttribute), false)))
        {
            IEnumerable<Type> scope = ArchitectureScan.WithNested(system);
            for (Type? b = system.BaseType; b is not null && b != typeof(object); b = b.BaseType)
            {
                scope = scope.Concat(ArchitectureScan.WithNested(b));
            }

            foreach (Type t in scope.Where(seen.Add))
            {
                foreach ((string where, Type type) in ArchitectureScan.SignatureTypes(t))
                {
                    foreach (Type c in ArchitectureScan.Constituents(type).Where(c => _banned.Contains(c)).Distinct())
                    {
                        violations.Add(new ArchViolation(ArchitectureScan.NameOf(t), $"{c.Name} in {where}", system.Name));
                    }
                }
            }
        }

        return violations;
    }
}

/// <summary>A fixture system base, clean.</summary>
public abstract class IsolationFixtureBase : ISimSystem
{
    public void Bind(SimWorldBuilder builder)
    {
    }
}

/// <summary>Seen-passing fixture: binds the builder only.</summary>
[SimSystem(SystemId.Economy)]
public sealed class IsolationGoodSystem : IsolationFixtureBase
{
    public IsolationGoodSystem(ITickReader tick)
    {
    }
}

/// <summary>Seen-failing fixture: holds the world in a field, a generic nullable-array property, a return and a by-ref parameter.</summary>
[SimSystem(SystemId.Economy)]
public sealed class IsolationHoldsWorldSystem : IsolationFixtureBase
{
    private SimWorld? _world;

    public IsolationHoldsWorldSystem(ITickReader tick)
    {
    }

    public List<SimWorld?[]>? Worlds { get; }

    public SimWorld? Snapshot() => _world;

    public void Swap(ref SimWorld? world) => (_world, world) = (world, _world);
}

/// <summary>Seen-failing fixture: keeps the composition context, directly and through a nested helper.</summary>
[SimSystem(SystemId.Perks)]
internal sealed class IsolationTakesContextSystem : IsolationFixtureBase
{
    internal IsolationTakesContextSystem(SimSystemContext context)
    {
    }

    /// <summary>A nested helper: nested types count.</summary>
    private sealed class Helper
    {
        private readonly SimSystemContext? _context = null;

        public SimWorld? Find() => _context is null ? null : null;
    }
}

/// <summary>A base class that holds the world (reported through the system that inherits it).</summary>
public abstract class IsolationWorldBase : IsolationFixtureBase
{
    private readonly SimWorld? _world = null;

    protected bool HasWorld => _world is not null;
}

/// <summary>Seen-failing fixture: inherits a world field.</summary>
[SimSystem(SystemId.Defenses)]
public sealed class IsolationInheritsWorldSystem : IsolationWorldBase
{
    public IsolationInheritsWorldSystem(ITickReader tick)
    {
    }
}
