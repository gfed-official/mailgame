using System.Collections.Immutable;
using System.Reflection;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// SC-ARCH-1 row (c), ADR-0003 section 4 (TR-sim-032): no static field in the Sim assembly is mutable. A static field passes
/// when it is <c>const</c>, or <c>static readonly</c> of an immutable type (<see cref="StaticStateRules.IsImmutable"/>);
/// <c>static readonly T[]</c> fails. Types that are (or are nested in) <c>[CompilerGenerated]</c> types are excluded.
/// The positive test scans only the Sim; the seen-failing test runs the same engine on bad fixtures in this assembly.
/// </summary>
[Trait("Id", "SC-ARCH-1")]
public class StaticStateRulesTests
{
    private static readonly Assembly _sim = typeof(SimAssembly).Assembly;

    [Fact]
    public void SimAssembly_HasNoMutableStaticField()
    {
        // Arrange
        Type[] universe = _sim.GetTypes();

        // Act
        List<ArchViolation> violations = StaticStateRules.Check(universe);

        // Assert
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_GoodFixture_IsClean()
    {
        // Act
        List<ArchViolation> violations = StaticStateRules.Check(ArchitectureScan.WithNested(typeof(StaticGoodFixture)).ToArray());

        // Assert
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void RuleEngine_BadFixture_ReportsExactlyTheExpectedViolations()
    {
        // Arrange
        string[] expected =
        {
            "StaticBadFixture: mutable static field Counter (not readonly)",
            "StaticBadFixture: mutable static field Table (static readonly of mutable type Int32[])",
            "StaticBadFixture: mutable static field Names (static readonly of mutable type List`1)",
            "StaticBadFixture: mutable static field Holder (static readonly of mutable type MutableStruct)",
            "StaticBadFixture: mutable static field <Auto>k__BackingField (not readonly)",
            "StaticBadFixture: mutable static field Wrapped (static readonly of mutable type ImmutableArray`1)",
            "StaticBadFixture+Inner: mutable static field Hidden (not readonly)",
            "StaticBadFixture: mutable static field Open (static readonly of mutable type OpenNull)",
            "StaticBadFixture: mutable static field Stateful (static readonly of mutable type StatefulSealed)",
            "StaticBadFixture: mutable static field Derived (static readonly of mutable type SealedOverBase)",
        };

        // Act
        List<ArchViolation> violations = StaticStateRules.Check(ArchitectureScan.WithNested(typeof(StaticBadFixture)).ToArray());

        // Assert
        Assert.Equal(expected.Order(StringComparer.Ordinal), violations.Select(v => v.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void RuleEngine_CompilerGeneratedTypes_AreExcluded()
    {
        // Arrange: the Sim's own <PrivateImplementationDetails> holds static readonly data blobs and must be skipped.
        Type[] generated = _sim.GetTypes().Where(ArchitectureScan.IsCompilerGenerated).ToArray();

        // Act
        List<ArchViolation> violations = StaticStateRules.Check(generated);

        // Assert
        Assert.NotEmpty(generated);
        Assert.Empty(violations);
    }
}

/// <summary>
/// The SC-ARCH-1 row (c) engine: reports every static field of the given types that is not <c>const</c> and not
/// <c>static readonly</c> of an immutable type.
/// </summary>
internal static class StaticStateRules
{
    private const BindingFlags StaticDeclared = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Checks each type (nested types must be passed explicitly; <see cref="Assembly.GetTypes"/> includes them).</summary>
    public static List<ArchViolation> Check(IReadOnlyList<Type> types)
    {
        var violations = new List<ArchViolation>();
        foreach (Type t in types.Where(t => !ArchitectureScan.IsCompilerGenerated(t)))
        {
            foreach (FieldInfo f in t.GetFields(StaticDeclared).Where(f => !f.IsLiteral))
            {
                string? reason = !f.IsInitOnly
                    ? "not readonly"
                    : IsImmutable(f.FieldType) ? null : $"static readonly of mutable type {f.FieldType.Name}";
                if (reason is not null)
                {
                    violations.Add(new ArchViolation(ArchitectureScan.NameOf(t), $"mutable static field {f.Name} ({reason})", $"{t.FullName}.{f.Name}"));
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Immutable: primitives, <see cref="string"/>, <see cref="decimal"/>, enums, <c>[ImmutableTable]</c> types, stateless
    /// sealed classes (no instance field anywhere in the hierarchy, e.g. null objects),
    /// <see cref="Nullable{T}"/> / <see cref="ImmutableArray{T}"/> of an immutable <c>T</c>, and readonly structs whose
    /// instance fields are all of immutable types. Everything else (arrays, other classes, mutable structs) is mutable.
    /// </summary>
    public static bool IsImmutable(Type t) => IsImmutable(t, new HashSet<Type>());

    private static bool IsImmutable(Type t, HashSet<Type> visiting)
    {
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t.IsDefined(typeof(ImmutableTableAttribute), false))
        {
            return true;
        }

        if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(Nullable<>) || t.GetGenericTypeDefinition() == typeof(ImmutableArray<>)))
        {
            return IsImmutable(t.GetGenericArguments()[0], visiting);
        }

        if (t.IsClass && t.IsSealed && t != typeof(string) && !t.IsArray && !typeof(Delegate).IsAssignableFrom(t) && InstanceFields(t).Length == 0)
        {
            return true; // a stateless sealed class (a null object): nothing to mutate
        }

        if (!t.IsValueType || t.IsArray || !t.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), false))
        {
            return false;
        }

        if (!visiting.Add(t))
        {
            return true; // a cycle through value types cannot occur; guard anyway
        }

        return t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .All(f => IsImmutable(f.FieldType, visiting));
    }

    // Instance fields of t and every base class (private inherited fields are only visible per declaring type).
    private static FieldInfo[] InstanceFields(Type t)
    {
        var fields = new List<FieldInfo>();
        for (Type? c = t; c is not null && c != typeof(object); c = c.BaseType)
        {
            fields.AddRange(c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }

        return fields.ToArray();
    }
}

/// <summary>Seen-passing fixture: every allowed static shape (test assembly only, never composed).</summary>
internal static class StaticGoodFixture
{
    public const int Limit = 3;
    public static readonly int Count = 4;
    public static readonly string Name = "ok";
    public static readonly SystemId Owner = SystemId.Economy;
    public static readonly StreamName Stream = RngNames.Shop;
    public static readonly EntityId Entity = default;
    public static readonly GoodTable Table = new();
    public static readonly ImmutableArray<int> Values = ImmutableArray.Create(1, 2);

    public static readonly NullObject Null = new();

    public static int Auto { get; } = 5;

    /// <summary>A stateless sealed class.</summary>
    public sealed class NullObject
    {
    }

    /// <summary>A literal span property: data in the image, no static field.</summary>
    public static ReadOnlySpan<byte> Bytes => [1, 2, 3];

    /// <summary>An <c>[ImmutableTable]</c> wrapper.</summary>
    [ImmutableTable]
    public sealed class GoodTable
    {
        private readonly int[] _values = { 1, 2 };

        public int this[int i] => _values[i];
    }
}

/// <summary>Seen-failing fixture: one field per mutable shape the rule must catch (test assembly only).</summary>
internal static class StaticBadFixture
{
#pragma warning disable CA2211, IDE0044, CS0649 // deliberately mutable statics
    public static int Counter;
    public static readonly int[] Table = { 1, 2 };
    public static readonly List<string> Names = new();
    public static readonly MutableStruct Holder;
    public static readonly ImmutableArray<int[]> Wrapped = ImmutableArray<int[]>.Empty;

    public static readonly OpenNull Open = new();
    public static readonly StatefulSealed Stateful = new();
    public static readonly SealedOverBase Derived = new();

    public static int Auto { get; set; }

    /// <summary>Stateless but not sealed: a subclass could add state.</summary>
    public class OpenNull
    {
    }

    /// <summary>Sealed but with a field.</summary>
    public sealed class StatefulSealed
    {
        public int Value;
    }

    /// <summary>A base class with state.</summary>
    public class BaseWithField
    {
        public int Value;
    }

    /// <summary>Sealed and field-free itself, but inherits a field.</summary>
    public sealed class SealedOverBase : BaseWithField
    {
    }

    /// <summary>A struct that is not <c>readonly</c>.</summary>
    public struct MutableStruct
    {
        public int Value;
    }

    /// <summary>A nested type: its statics count too.</summary>
    public static class Inner
    {
        public static long Hidden;
    }
#pragma warning restore CA2211, IDE0044, CS0649
}
