using System.Reflection;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// SC-ARCH-1 row (c) support: the structural check behind <see cref="ImmutableTableAttribute"/>, which the static-state rule
/// trusts. Every <c>[ImmutableTable]</c> type in the Sim is a sealed class or a readonly struct, every instance field across
/// its hierarchy is <c>initonly</c>, and no property in its hierarchy has a setter other than <c>init</c>. (Deep immutability
/// of referenced storage, e.g. a private array, stays a review promise.)
/// </summary>
[Trait("Id", "SC-ARCH-1")]
public class ImmutableTableRulesTests
{
    private static readonly Assembly _sim = typeof(SimAssembly).Assembly;

    [Fact]
    public void SimAssembly_EveryImmutableTableType_IsStructurallyImmutable()
    {
        // Arrange
        Type[] marked = _sim.GetTypes().Where(t => t.IsDefined(typeof(ImmutableTableAttribute), false)).ToArray();

        // Act
        List<ArchViolation> violations = ImmutableTableRules.Check(marked);

        // Assert
        Assert.Contains(typeof(OpDescriptor), marked);
        Assert.Contains(typeof(EventDescriptor), marked);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_GoodFixtures_AreClean()
    {
        // Act
        List<ArchViolation> violations = ImmutableTableRules.Check(new[] { typeof(ImmutableGoodClassFixture), typeof(ImmutableGoodStructFixture), typeof(ImmutableGoodRecordFixture) });

        // Assert
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_BadFixtures_ReportExactlyTheExpectedViolations()
    {
        // Arrange
        Type[] universe =
        {
            typeof(ImmutableUnsealedFixture), typeof(ImmutableMutableFieldFixture), typeof(ImmutableSetterFixture),
            typeof(ImmutableMutableStructFixture), typeof(ImmutableInheritsMutableFixture),
        };
        string[] expected =
        {
            "ImmutableUnsealedFixture: [ImmutableTable] class is not sealed",
            "ImmutableMutableFieldFixture: [ImmutableTable] field _count is not readonly",
            "ImmutableSetterFixture: [ImmutableTable] property Value has a non-init setter",
            "ImmutableMutableStructFixture: [ImmutableTable] struct is not readonly",
            "ImmutableMutableStructFixture: [ImmutableTable] field Value is not readonly",
            "ImmutableInheritsMutableFixture: [ImmutableTable] field Count is not readonly",
        };

        // Act
        List<ArchViolation> violations = ImmutableTableRules.Check(universe);

        // Assert
        Assert.Equal(expected.Order(StringComparer.Ordinal), violations.Select(v => v.Key).Order(StringComparer.Ordinal));
    }
}

/// <summary>The structural engine for <see cref="ImmutableTableAttribute"/> types.</summary>
internal static class ImmutableTableRules
{
    private const BindingFlags InstanceDeclared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Checks each marked type in <paramref name="types"/> (unmarked types are ignored).</summary>
    public static List<ArchViolation> Check(IReadOnlyList<Type> types)
    {
        var violations = new List<ArchViolation>();
        foreach (Type t in types.Where(t => t.IsDefined(typeof(ImmutableTableAttribute), false)))
        {
            string owner = ArchitectureScan.NameOf(t);
            if (t.IsClass && !t.IsSealed)
            {
                violations.Add(new ArchViolation(owner, "[ImmutableTable] class is not sealed", t.FullName!));
            }

            if (t.IsValueType && !t.IsDefined(typeof(IsReadOnlyAttribute), false))
            {
                violations.Add(new ArchViolation(owner, "[ImmutableTable] struct is not readonly", t.FullName!));
            }

            for (Type? c = t; c is not null && c != typeof(object) && c != typeof(ValueType); c = c.BaseType)
            {
                foreach (FieldInfo f in c.GetFields(InstanceDeclared).Where(f => !f.IsInitOnly))
                {
                    violations.Add(new ArchViolation(owner, $"[ImmutableTable] field {f.Name} is not readonly", $"{c.FullName}.{f.Name}"));
                }

                foreach (PropertyInfo p in c.GetProperties(InstanceDeclared))
                {
                    MethodInfo? setter = p.GetSetMethod(nonPublic: true);
                    bool init = setter is not null && setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
                    if (setter is not null && !init)
                    {
                        violations.Add(new ArchViolation(owner, $"[ImmutableTable] property {p.Name} has a non-init setter", $"{c.FullName}.{p.Name}"));
                    }
                }
            }
        }

        return violations;
    }
}

/// <summary>Seen-passing fixture: sealed class, readonly fields, get-only and init-only properties.</summary>
[ImmutableTable]
internal sealed class ImmutableGoodClassFixture
{
    private readonly int[] _values = { 1, 2 };

    public int Count { get; init; }

    public int this[int i] => _values[i];
}

/// <summary>Seen-passing fixture: a readonly struct.</summary>
[ImmutableTable]
internal readonly struct ImmutableGoodStructFixture
{
    public ImmutableGoodStructFixture(int value)
    {
        Value = value;
    }

    public int Value { get; }
}

/// <summary>Seen-passing fixture: a sealed positional record.</summary>
[ImmutableTable]
internal sealed record ImmutableGoodRecordFixture(int Kind, string Name);

/// <summary>Seen-failing fixture: not sealed.</summary>
[ImmutableTable]
internal class ImmutableUnsealedFixture
{
}

/// <summary>Seen-failing fixture: a mutable field.</summary>
[ImmutableTable]
internal sealed class ImmutableMutableFieldFixture
{
    private int _count;

    public int Bump() => ++_count;
}

/// <summary>Seen-failing fixture: a non-init setter (no backing field).</summary>
[ImmutableTable]
internal sealed class ImmutableSetterFixture
{
    public int Value
    {
        get => 0;
        set { }
    }
}

/// <summary>Seen-failing fixture: a struct that is not readonly.</summary>
[ImmutableTable]
internal struct ImmutableMutableStructFixture
{
#pragma warning disable CS0649 // deliberately mutable, never assigned
    public int Value;
#pragma warning restore CS0649
}

/// <summary>A base class with a mutable field.</summary>
internal class ImmutableMutableBaseFixture
{
#pragma warning disable CS0649 // deliberately mutable, never assigned
    public int Count;
#pragma warning restore CS0649
}

/// <summary>Seen-failing fixture: sealed and field-free itself, but inherits a mutable field.</summary>
[ImmutableTable]
internal sealed class ImmutableInheritsMutableFixture : ImmutableMutableBaseFixture
{
}
