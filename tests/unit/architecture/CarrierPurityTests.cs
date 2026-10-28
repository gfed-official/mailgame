using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// Reflection guards for the carrier solver surface (ADR-0013; TR-movement-001, TR-movement-004): no floating point, no
/// <c>SimWorld</c> or Godot-typed member, blittable where the story says so, and no static mutable state. Story 010 (PCM-P2-1 "No Godot")
/// adds the physics-name rule: the solver is Sim-only, so nothing it can reach is named after an engine physics type.
/// </summary>
public class CarrierPurityTests
{
    /// <summary>
    /// Engine physics type names the carrier solver must never reference (story 010; the list approved by Evan 2026-10-27 10:47 PM PT,
    /// Mail Lead chat: "Approve both player-character 010 defaults: the no-physics-types list and the position-and-velocity-only hash").
    /// </summary>
    private static readonly string[] _physicsNames = ["CharacterBody", "PhysicsBody", "PhysicsServer", "RigidBody", "CollisionShape", "Jolt"];

    private const BindingFlags AllFields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Type[] _dataTypes =
    [
        typeof(CarrierInput), typeof(CarrierEnv), typeof(RemoteCapsule), typeof(VehiclePose), typeof(CarrierScratch),
    ];

    private static readonly Type[] _solverSurface =
    [
        typeof(CarrierInput), typeof(CarrierEnv), typeof(RemoteCapsule), typeof(VehiclePose), typeof(CarrierScratch),
        typeof(CarrierSolidBuffer), typeof(CarrierBoxBuffer), typeof(CarrierVehicleBuffer), typeof(CarrierCounterFlush),
        typeof(EmptyRemoteCapsuleSource), typeof(EmptySupportTransformSource), typeof(IRemoteCapsuleSource), typeof(ISupportTransformSource),
    ];

    public static TheoryData<Type> SolverSurfaceCases() => new(_solverSurface);

    // The two member scans loop their type lists in one test each; every offence names its type.
    [Fact]
    public void DataTypes_HaveNoFloatingPointSimWorldOrGodotMember()
    {
        List<string> bad = [];
        foreach (Type type in _dataTypes)
        {
            Visit(type, type.Name, new HashSet<Type>(), bad);
        }

        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    [Theory]
    [MemberData(nameof(SolverSurfaceCases))]
    public void SolverSurface_HasNoStaticMutableField(Type type)
    {
        string[] offenders = type.GetFields(AllFields)
            .Where(f => f.IsStatic && !f.IsLiteral)
            .Select(f => $"{type.Name}.{f.Name}")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SolverSurface_MethodsAndPropertiesDoNotMentionFloatingPoint()
    {
        List<string> bad = [];
        foreach (Type type in _solverSurface)
        {
            foreach (MethodInfo m in type.GetMethods(AllFields | BindingFlags.Instance | BindingFlags.Static))
            {
                Type[] used = [m.ReturnType, .. m.GetParameters().Select(p => p.ParameterType)];
                bad.AddRange(used.Where(IsForbiddenLeaf).Select(t => $"{type.Name}.{m.Name} uses {t.Name}"));
            }
        }

        Assert.True(bad.Count == 0, string.Join("; ", bad));
    }

    [Fact]
    public void BlittableTypes_ContainNoReferences()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<CarrierInput>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<RemoteCapsule>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<VehiclePose>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<CarrierScratch>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<CarrierSolidBuffer>());
    }

    [Fact]
    public void ReadonlyLayouts_InputCapsulePoseAreReadonlyStructs_AndEnvIsAReadonlyRefStruct()
    {
        foreach (Type t in new[] { typeof(CarrierInput), typeof(RemoteCapsule), typeof(VehiclePose), typeof(CarrierEnv) })
        {
            Assert.True(t.IsValueType, t.Name);
            Assert.True(t.IsDefined(typeof(IsReadOnlyAttribute), inherit: false), $"{t.Name} is not a readonly struct");
            Assert.All(t.GetFields(AllFields).Where(f => !f.IsStatic), f => Assert.True(f.IsInitOnly, $"{t.Name}.{f.Name} is not readonly"));
        }

        Assert.True(typeof(CarrierEnv).IsByRefLike);
        Assert.False(typeof(CarrierScratch).IsByRefLike);
    }

    [Fact]
    public void CarrierEnv_HoldsTheDeclaredMembersOnly()
    {
        string[] names = typeof(CarrierEnv).GetFields(AllFields).Where(f => !f.IsStatic).Select(f => f.Name).Order().ToArray();

        Assert.Equal(new[] { "Remotes", "Self", "Settings", "Supports", "World" }, names);
    }

    /// <summary>
    /// PCM-P2-1 "No Godot", physics half: no type the Sim assembly defines or references by metadata (every <c>TypeDef</c> and
    /// <c>TypeRef</c>, so method bodies, locals and generic arguments count, not only signatures) has a physics name. That is stricter
    /// than the story's <c>PerformativeMail.Sim/Players</c> scope and implies it. The Godot-assembly half is <c>AssemblyRulesTests</c>.
    /// </summary>
    [Fact]
    [Trait("Id", "PCM-P2-1")]
    public void SimAssembly_DefinesAndReferencesNoPhysicsNamedType()
    {
        // Arrange
        using var pe = new PEReader(File.OpenRead(typeof(OwnedCarrier).Assembly.Location));
        MetadataReader md = pe.GetMetadataReader();

        // Act
        IEnumerable<string> defined = md.TypeDefinitions.Select(h => md.GetTypeDefinition(h))
            .Select(d => md.GetString(d.Namespace) + "." + md.GetString(d.Name));
        IEnumerable<string> referenced = md.TypeReferences.Select(h => md.GetTypeReference(h))
            .Select(r => md.GetString(r.Namespace) + "." + md.GetString(r.Name));
        string[] offenders = defined.Concat(referenced)
            .Where(name => _physicsNames.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.True(md.TypeDefinitions.Any(h => md.GetString(md.GetTypeDefinition(h).Name) == nameof(OwnedCarrier)), "the scan did not read the Sim's Players types");
        Assert.Empty(offenders);
    }

    private static bool IsForbiddenLeaf(Type t)
    {
        Type e = t.IsByRef || t.IsPointer || t.IsArray ? t.GetElementType()! : t;
        return e == typeof(float) || e == typeof(double) || e == typeof(decimal) || e == typeof(Half)
            || e.Name == "SimWorld" || (e.Namespace?.StartsWith("Godot", StringComparison.Ordinal) ?? false);
    }

    private static void Visit(Type type, string path, HashSet<Type> visited, List<string> bad)
    {
        if (!visited.Add(type))
        {
            return;
        }

        foreach (FieldInfo f in type.GetFields(AllFields))
        {
            string where = $"{path}.{f.Name}";
            Type ft = f.FieldType;
            if (IsForbiddenLeaf(ft))
            {
                bad.Add($"{where}: {ft.FullName}");
            }
            else if (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum && ft.Assembly == typeof(CarrierInput).Assembly && !f.IsStatic)
            {
                Visit(ft, where, visited, bad);
            }
        }
    }
}
