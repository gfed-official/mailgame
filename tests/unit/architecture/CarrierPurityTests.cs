using System.Reflection;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.Architecture;

/// <summary>
/// Reflection guards for the carrier solver surface (ADR-0013; TR-movement-001, TR-movement-004): no floating point, no
/// <c>SimWorld</c> or Godot-typed member, blittable where the story says so, and no static mutable state.
/// </summary>
public class CarrierPurityTests
{
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

    public static TheoryData<Type> DataTypeCases() => new(_dataTypes);

    public static TheoryData<Type> SolverSurfaceCases() => new(_solverSurface);

    [Theory]
    [MemberData(nameof(DataTypeCases))]
    public void DataTypes_HaveNoFloatingPointSimWorldOrGodotMember(Type type)
    {
        var visited = new HashSet<Type>();
        List<string> bad = [];
        Visit(type, type.Name, visited, bad);

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

    [Theory]
    [MemberData(nameof(SolverSurfaceCases))]
    public void SolverSurface_MethodsAndPropertiesDoNotMentionFloatingPoint(Type type)
    {
        List<string> bad = [];
        foreach (MethodInfo m in type.GetMethods(AllFields | BindingFlags.Instance | BindingFlags.Static))
        {
            Type[] used = [m.ReturnType, .. m.GetParameters().Select(p => p.ParameterType)];
            bad.AddRange(used.Where(IsForbiddenLeaf).Select(t => $"{type.Name}.{m.Name} uses {t.Name}"));
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
