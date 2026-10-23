using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Primitives;
using Xunit;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 005 (TR-content-010, TR-content-011; GDD CDD-SCHEMA-2, CDD-SCHEMA-3 record half, CD6, CD7,
/// CD10 M-1a rows): the shape of the five M-1a def records and their leaves. The def set is DISCOVERED from the Sim
/// assembly through the <see cref="IDef"/> / <see cref="IDefLeaf"/> markers, so a new def cannot escape the reflection
/// gate; a type named <c>*Def</c> in the content namespace that lacks the marker fails a separate test. The field
/// snapshot and the defaults table are LITERAL on purpose: a changed field, unit or default must fail here and force a
/// deliberate edit. Nothing is random, timed or read from disk.
/// </summary>
public class DefShapeTests
{
    private static readonly Assembly _sim = typeof(IDef).Assembly;

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Type[] _scalars =
    {
        typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(string), typeof(DefIndex),
    };

    private static readonly Type[] _forbidden = { typeof(float), typeof(double), typeof(decimal), typeof(Half) };

    // ---- discovery ----

    // Discovery takes ANY concrete type that implements the marker (class or struct), so a "record struct : IDef"
    // cannot escape; the shape checks below then fail it for being the wrong kind of type.
    private static Type[] DefTypesOf(IEnumerable<Type> types) =>
        types.Where(t => typeof(IDef).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();

    private static Type[] LeafTypesOf(IEnumerable<Type> types) =>
        types.Where(t => typeof(IDefLeaf).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();

    private static Type[] DefTypes() => DefTypesOf(_sim.GetTypes());

    private static Type[] LeafTypes() => LeafTypesOf(_sim.GetTypes());

    private static Type[] AllMarked() => DefTypes().Concat(LeafTypes()).ToArray();

    // ---- the reflection gate (CDD-SCHEMA-2) ----

    private static List<string> Scan(IEnumerable<Type> roots)
    {
        var violations = new List<string>();
        var seen = new HashSet<Type>();
        foreach (Type root in roots)
        {
            ScanType(root, seen, violations);
        }

        return violations;
    }

    private static void ScanType(Type t, HashSet<Type> seen, List<string> v)
    {
        if (!seen.Add(t))
        {
            return;
        }

        foreach (FieldInfo f in t.GetFields(AllDeclared))
        {
            if (f.IsStatic)
            {
                // ADR-0003 section 4: static fields only const, or static readonly of a primitive, string or enum
                // (a static readonly Dictionary/List is mutable shared state).
                bool immutableType = f.FieldType.IsPrimitive || f.FieldType == typeof(string) || f.FieldType.IsEnum;
                if (!f.IsLiteral && !(f.IsInitOnly && immutableType))
                {
                    v.Add($"{t.Name}.{f.Name}: static field must be const or static readonly primitive/string/enum");
                }
            }
            else
            {
                CheckMember($"{t.Name}.{f.Name}", f.FieldType, true, seen, v);
            }
        }

        foreach (PropertyInfo p in t.GetProperties(AllDeclared))
        {
            if (p.GetMethod?.IsStatic == true || p.Name == "EqualityContract")
            {
                continue;
            }

            CheckMember($"{t.Name}.{p.Name}", p.PropertyType, true, seen, v);
        }

        foreach (Type nested in t.GetNestedTypes(AllDeclared))
        {
            if (!nested.IsDefined(typeof(CompilerGeneratedAttribute), false))
            {
                ScanType(nested, seen, v);
            }
        }
    }

    private static void CheckMember(string where, Type type, bool listAllowed, HashSet<Type> seen, List<string> v)
    {
        if (type.IsArray)
        {
            v.Add($"{where}: array type {type.Name} (use ImmutableArray<T>)");
        }
        else if (Array.IndexOf(_forbidden, type) >= 0)
        {
            v.Add($"{where}: forbidden type {type.Name}");
        }
        else if (type.IsEnum || Array.IndexOf(_scalars, type) >= 0)
        {
            // allowed scalar
        }
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
        {
            if (!listAllowed)
            {
                v.Add($"{where}: nested list {type.Name}");
            }
            else
            {
                CheckMember(where + "[]", type.GetGenericArguments()[0], false, seen, v);
            }
        }
        else if (type.IsValueType && typeof(IDefLeaf).IsAssignableFrom(type))
        {
            ScanType(type, seen, v);
        }
        else
        {
            v.Add($"{where}: type {type} is not allowed in a def (lists must be ImmutableArray<T>)");
        }
    }

    // ---- gate fixtures: prove the checker itself fails on each forbidden shape ----

    private sealed record class FloatFixture { public float X { get; init; } }

    private sealed record class DoubleFixture { public double X { get; init; } }

    private sealed record class DecimalFixture { public decimal X { get; init; } }

    private sealed record class HalfFixture { public Half X { get; init; } }

    private sealed record class ArrayFixture { public int[] X { get; init; } = Array.Empty<int>(); }

    private sealed record class ListFixture { public List<int> X { get; init; } = new(); }

    private sealed record class NestedListFixture { public ImmutableArray<ImmutableArray<int>> X { get; init; } }

    private sealed record class DefHolderFixture { public ItemDef X { get; init; } = null!; }

    private sealed record class MutableStaticFixture
    {
        public static int Counter = 1;
    }

    private sealed record class StaticDictFixture
    {
        public static readonly Dictionary<string, int> Map = new();
    }

    private sealed record class StaticListFixture
    {
        public static readonly List<int> Items = new();
    }

    private sealed record class StaticReadonlyScalarFixture
    {
        public const int Limit = 4;

        public static readonly int Count = 4;

        public static readonly string Label = "x";

        public static readonly ItemCategory Cat = ItemCategory.Tool;
    }

    private readonly record struct BadLeaf(float Ratio) : IDefLeaf;

    private sealed record class LeafHolderFixture { public BadLeaf X { get; init; } }

    private sealed record class ArrayInLeafListFixture { public ImmutableArray<BadLeaf> X { get; init; } }

    [Fact]
    public void ReflectionGate_FlagsEveryForbiddenShape()
    {
        Type[] bad =
        {
            typeof(FloatFixture), typeof(DoubleFixture), typeof(DecimalFixture), typeof(HalfFixture), typeof(ArrayFixture),
            typeof(ListFixture), typeof(NestedListFixture), typeof(DefHolderFixture), typeof(MutableStaticFixture),
            typeof(LeafHolderFixture), typeof(ArrayInLeafListFixture), typeof(StaticDictFixture), typeof(StaticListFixture),
        };

        foreach (Type t in bad)
        {
            Assert.NotEmpty(Scan(new[] { t }));
        }
    }

    [Fact]
    public void ReflectionGate_AcceptsAWellFormedFixture()
    {
        Assert.Empty(Scan(new[] { typeof(CatchVolume), typeof(MailShare), typeof(ShiftMix) }));
    }

    [Fact]
    public void ReflectionGate_AcceptsConstAndStaticReadonlyScalars()
    {
        Assert.Empty(Scan(new[] { typeof(StaticReadonlyScalarFixture) }));
    }

    // ---- discovery / shape fixtures: a def or leaf of the wrong kind of type must be found AND failed ----

    private readonly record struct StructDefFixture(string Id) : IDef;

    private record class UnsealedDefFixture : IDef
    {
        public string Id { get; init; } = "x";
    }

    private sealed class PlainClassDefFixture : IDef
    {
        public string Id { get; init; } = "x";
    }

    private sealed class ClassLeafFixture : IDefLeaf
    {
        public int X { get; init; }
    }

    private record struct MutableLeafFixture(int X) : IDefLeaf;

    private static readonly Type[] _wrongKinds =
    {
        typeof(StructDefFixture), typeof(UnsealedDefFixture), typeof(PlainClassDefFixture),
        typeof(ClassLeafFixture), typeof(MutableLeafFixture),
    };

    private static List<string> ShapeViolations(IEnumerable<Type> types)
    {
        var v = new List<string>();
        foreach (Type t in DefTypesOf(types))
        {
            if (!t.IsClass || !t.IsSealed)
            {
                v.Add($"{t.Name}: a def must be a sealed class");
            }
            else if (t.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) == null)
            {
                v.Add($"{t.Name}: a def must be a record class");
            }
        }

        foreach (Type t in LeafTypesOf(types))
        {
            if (!t.IsValueType)
            {
                v.Add($"{t.Name}: a leaf must be a struct");
            }
            else if (!t.IsDefined(typeof(IsReadOnlyAttribute), false))
            {
                v.Add($"{t.Name}: a leaf must be readonly");
            }
            else if (t.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) == null)
            {
                v.Add($"{t.Name}: a leaf must be a record struct");
            }
        }

        return v;
    }

    [Fact]
    public void Discovery_FindsMarkedTypesOfAnyKind_AndShapeGateFailsEachWrongKind()
    {
        // Discovery sees all five wrong-kind fixtures (a record struct : IDef cannot escape) ...
        Type[] found = DefTypesOf(_wrongKinds).Concat(LeafTypesOf(_wrongKinds)).ToArray();
        Assert.Equal(_wrongKinds.Length, found.Length);

        // ... and the shape gate reports every one of them.
        foreach (Type t in _wrongKinds)
        {
            Assert.NotEmpty(ShapeViolations(new[] { t }));
        }
    }

    // ---- AC1 ----

    [Fact]
    public void Discovery_FindsTheM1aDefsAndLeaves()
    {
        string[] defs = DefTypes().Select(t => t.Name).ToArray();
        foreach (string expected in new[] { "ContainerDef", "DestinationTypeDef", "ItemDef", "MailKindDef", "MailMixDef" })
        {
            Assert.Contains(expected, defs);
        }

        string[] leaves = LeafTypes().Select(t => t.Name).ToArray();
        foreach (string expected in new[] { "CatchVolume", "MailShare", "ShiftMix" })
        {
            Assert.Contains(expected, leaves);
        }
    }

    [Fact]
    public void EveryTypeNamedDefInTheContentNamespace_CarriesTheMarker()
    {
        string[] unmarked = _sim.GetTypes()
            .Where(t => t.Namespace == "PerformativeMail.Sim.Content" && t.Name.EndsWith("Def", StringComparison.Ordinal))
            .Where(t => !typeof(IDef).IsAssignableFrom(t))
            .Select(t => t.Name).ToArray();

        Assert.Empty(unmarked);
    }

    [Fact]
    public void Schema2_NoForbiddenTypeAndEveryListIsImmutableArray()
    {
        List<string> violations = Scan(AllMarked());

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Schema2_SimAssemblyReferencesNoGodotAssembly()
    {
        string[] godot = _sim.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Godot", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.Empty(godot);
    }

    // ---- AC2 ----

    [Fact]
    public void Defs_AreSealedRecordClasses()
    {
        List<string> violations = ShapeViolations(DefTypes());

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Leaves_AreReadonlyRecordStructs()
    {
        List<string> violations = ShapeViolations(LeafTypes());

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void DefsAndLeaves_HaveNoPublicNonInitSetterAndNoMutablePublicField()
    {
        var offenders = new List<string>();
        foreach (Type t in AllMarked())
        {
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                MethodInfo? set = p.SetMethod;
                if (set != null && !set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                {
                    offenders.Add($"{t.Name}.{p.Name}: public non-init setter");
                }
            }

            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!f.IsInitOnly)
                {
                    offenders.Add($"{t.Name}.{f.Name}: public mutable field");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    // ---- AC3: literal field snapshot (CD10 M-1a rows). R = required, D = defaulted. ----

    private static readonly Dictionary<string, string[]> _fields = new()
    {
        ["ItemDef"] = new[]
        {
            "Id:string:R", "Name:string:R", "Category:ItemCategory:R", "GridW:int:R", "GridH:int:R", "MaxStack:int:R",
            "WeightClass:WeightClass:R", "SellPriceCents:long:D", "HasBuyPrice:bool:D", "BuyPriceCents:long:D",
            "CarryByHand:bool:D", "EquipSlot:EquipSlot:D", "Tags:ImmutableArray<string>:D",
        },
        ["ContainerDef"] = new[]
        {
            "Id:string:R", "Layout:ContainerLayout:R", "HasGrid:bool:D", "GridW:int:D", "GridH:int:D",
            "View:ContainerView:D", "BeltAccess:BeltAccess:D", "HasAllowedCategories:bool:D",
            "AllowedCategories:ImmutableArray<ItemCategory>:D", "HandCarried:bool:D", "AcceptsInsert:bool:D",
            "ReservedSlots:ImmutableArray<int>:D",
        },
        ["MailKindDef"] = new[]
        {
            "Id:string:R", "Name:string:R", "GridW:int:R", "GridH:int:R", "MaxStack:int:R", "BaseValueCents:long:R",
            "WeightClass:WeightClass:R", "CarryByHand:bool:D", "BeltLanes:int:R", "BeltLengthMm:int:R",
            "DeadlineOffsetShifts:int:D", "AcceptedBy:ImmutableArray<DefIndex>:R",
            "ComplaintOnMisdeliveryMilliPoints:int:R", "UnlockShift:int:R", "FallbackKind:DefIndex:D",
        },
        ["DestinationTypeDef"] = new[]
        {
            "Id:string:R", "Accepts:ImmutableArray<DefIndex>:R", "InsertRateMilliPerSec:int:R", "ManualInsertHoldMs:int:R",
            "MaxAutomatedFeeders:int:R", "RequiresUnit:bool:D", "CatchVolume:CatchVolume:R",
        },
        ["MailMixDef"] = new[]
        {
            "Id:string:R", "Shifts:ImmutableArray<ShiftMix>:R", "BatchIntervalMs:int:R", "BatchJitterMs:int:R",
            "TailGuardMs:int:R", "OpeningRampBatches:int:D", "SpawnOverheadPm:int:R", "StreetStreakRatioPm:int:R",
            "RaidSpawnRateRatioPm:int:R", "DistanceStepRatioPm:int:R", "ShiftStepRatioPm:int:R",
        },
        ["CatchVolume"] = new[]
        {
            "WidthMm:int:R", "HeightMm:int:R", "DepthMm:int:R", "CentreHeightMm:int:R", "YawMilliDeg:int:R",
        },
        ["MailShare"] = new[] { "Kind:DefIndex:R", "SharePm:int:R" },
        ["ShiftMix"] = new[] { "Shift:int:R", "Shares:ImmutableArray<MailShare>:R" },
    };

    private static string Pretty(Type t)
    {
        if (t.IsGenericType)
        {
            string name = t.Name.Substring(0, t.Name.IndexOf('`'));
            return name + "<" + string.Join(", ", t.GetGenericArguments().Select(Pretty)) + ">";
        }

        if (t == typeof(int)) { return "int"; }
        if (t == typeof(long)) { return "long"; }
        if (t == typeof(bool)) { return "bool"; }
        if (t == typeof(string)) { return "string"; }
        if (t == typeof(ushort)) { return "ushort"; }
        return t.Name;
    }

    private static bool IsRequired(PropertyInfo p) => p.IsDefined(typeof(RequiredMemberAttribute), false);

    private static string[] Describe(Type t)
    {
        bool leaf = t.IsValueType;
        return t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => $"{p.Name}:{Pretty(p.PropertyType)}:{(leaf || IsRequired(p) ? "R" : "D")}")
            .ToArray();
    }

    [Fact]
    public void M1aDefs_ExposeExactlyTheDocumentedFieldsWithUnitsAndReferenceTypes()
    {
        foreach (KeyValuePair<string, string[]> expected in _fields)
        {
            Type t = AllMarked().Single(x => x.Name == expected.Key);
            string[] actual = Describe(t).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            string[] want = expected.Value.OrderBy(s => s, StringComparer.Ordinal).ToArray();

            Assert.Equal(want, actual);
        }
    }

    [Fact]
    public void SnapshotCoversEveryMarkedType()
    {
        foreach (Type t in AllMarked())
        {
            Assert.True(_fields.ContainsKey(t.Name), $"{t.Name} has no row in the field snapshot; add one");
        }
    }

    [Fact]
    public void ReferenceFieldsHoldDefIndex_AndNoFieldHoldsAnotherDef()
    {
        Assert.Equal(typeof(ImmutableArray<DefIndex>), typeof(MailKindDef).GetProperty("AcceptedBy")!.PropertyType);
        Assert.Equal(typeof(ImmutableArray<DefIndex>), typeof(DestinationTypeDef).GetProperty("Accepts")!.PropertyType);
        Assert.Equal(typeof(DefIndex), typeof(MailKindDef).GetProperty("FallbackKind")!.PropertyType);
        Assert.Equal(typeof(DefIndex), typeof(MailShare).GetProperty("Kind")!.PropertyType);
    }

    [Fact]
    public void ContainerDef_PileHasNoGridAndGridUsesColsByRows()
    {
        ContainerDef pile = new() { Id = "death_bag", Layout = ContainerLayout.Pile };
        ContainerDef chest = new() { Id = "chest", Layout = ContainerLayout.Grid, HasGrid = true, GridW = 8, GridH = 4 };

        Assert.False(pile.HasGrid);
        Assert.Equal((8, 4), (chest.GridW, chest.GridH));
    }

    // ---- AC4: documented defaults are neutral; weightClass has none ----

    private enum Neutral
    {
        Zero,
        False,
        None,
        Any,
        EmptyList,
    }

    private sealed record DefaultRow(Type Def, string Property, object Expected, Neutral Kind);

    private static readonly DefaultRow[] _defaults =
    {
        new(typeof(ItemDef), "SellPriceCents", 0L, Neutral.Zero),
        new(typeof(ItemDef), "HasBuyPrice", false, Neutral.False),
        new(typeof(ItemDef), "BuyPriceCents", 0L, Neutral.Zero),
        new(typeof(ItemDef), "CarryByHand", true, Neutral.Any),
        new(typeof(ItemDef), "EquipSlot", EquipSlot.None, Neutral.None),
        new(typeof(ItemDef), "Tags", ImmutableArray<string>.Empty, Neutral.EmptyList),
        new(typeof(ContainerDef), "HasGrid", false, Neutral.False),
        new(typeof(ContainerDef), "GridW", 0, Neutral.Zero),
        new(typeof(ContainerDef), "GridH", 0, Neutral.Zero),
        new(typeof(ContainerDef), "View", ContainerView.Auto, Neutral.Any),
        new(typeof(ContainerDef), "BeltAccess", BeltAccess.None, Neutral.None),
        new(typeof(ContainerDef), "HasAllowedCategories", false, Neutral.False),
        new(typeof(ContainerDef), "AllowedCategories", ImmutableArray<ItemCategory>.Empty, Neutral.EmptyList),
        new(typeof(ContainerDef), "HandCarried", false, Neutral.False),
        new(typeof(ContainerDef), "AcceptsInsert", true, Neutral.Any),
        new(typeof(ContainerDef), "ReservedSlots", ImmutableArray<int>.Empty, Neutral.EmptyList),
        new(typeof(MailKindDef), "CarryByHand", true, Neutral.Any),
        new(typeof(MailKindDef), "DeadlineOffsetShifts", 0, Neutral.Zero),
        new(typeof(MailKindDef), "FallbackKind", DefIndex.None, Neutral.None),
        new(typeof(DestinationTypeDef), "RequiresUnit", false, Neutral.False),
        new(typeof(MailMixDef), "OpeningRampBatches", 0, Neutral.Zero),
    };

    // A fully-specified instance per def: every required member set, every defaulted member left alone.
    private static readonly Dictionary<Type, Func<object>> _minimal = new()
    {
        [typeof(ItemDef)] = () => new ItemDef
        {
            Id = "axe", Name = "item.axe", Category = ItemCategory.Tool, GridW = 1, GridH = 2, MaxStack = 1,
            WeightClass = WeightClass.Light,
        },
        [typeof(ContainerDef)] = () => new ContainerDef { Id = "chest", Layout = ContainerLayout.Grid },
        [typeof(MailKindDef)] = () => new MailKindDef
        {
            Id = "letter", Name = "mail.letter", GridW = 1, GridH = 1, MaxStack = 20, BaseValueCents = 8,
            WeightClass = WeightClass.Light, BeltLanes = 1, BeltLengthMm = 500, AcceptedBy = ImmutableArray<DefIndex>.Empty,
            ComplaintOnMisdeliveryMilliPoints = 5000, UnlockShift = 1,
        },
        [typeof(DestinationTypeDef)] = () => new DestinationTypeDef
        {
            Id = "house", Accepts = ImmutableArray<DefIndex>.Empty, InsertRateMilliPerSec = 2000, ManualInsertHoldMs = 400,
            MaxAutomatedFeeders = 1, CatchVolume = new CatchVolume(1000, 800, 1000, 1000, 0),
        },
        [typeof(MailMixDef)] = () => new MailMixDef
        {
            Id = "default", Shifts = ImmutableArray<ShiftMix>.Empty, BatchIntervalMs = 15000, BatchJitterMs = 3000,
            TailGuardMs = 5000, SpawnOverheadPm = 1600, StreetStreakRatioPm = 300, RaidSpawnRateRatioPm = 500,
            DistanceStepRatioPm = 250, ShiftStepRatioPm = 100,
        },
    };

    private static bool IsNeutral(Neutral kind, object value) => kind switch
    {
        Neutral.Zero => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) == 0,
        Neutral.False => value is false,
        // "any" for a flag is the permissive value (true); for an enum the "auto"/"any" member.
        Neutral.Any => value is true || (value.GetType().IsEnum && value.ToString() == "Auto"),
        Neutral.None => value is DefIndex d ? d.IsNone : value.GetType().IsEnum && value.ToString() == "None",
        // The caller has already asserted Equals(ImmutableArray<T>.Empty), which a default (uninitialised) array fails.
        Neutral.EmptyList => value is System.Collections.ICollection c && c.Count == 0,
        _ => false,
    };

    [Fact]
    public void EveryDefaultedMember_IsListedInTheDefaultsTable_AndNothingElse()
    {
        foreach (Type def in DefTypes())
        {
            string[] defaulted = def.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => !IsRequired(p)).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] listed = _defaults.Where(r => r.Def == def).Select(r => r.Property)
                .OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.Equal(listed, defaulted);
        }
    }

    [Fact]
    public void EveryDefaultedMember_HasTheDocumentedValue_AndItIsNeutral()
    {
        foreach (DefaultRow row in _defaults)
        {
            object instance = _minimal[row.Def]();
            object actual = row.Def.GetProperty(row.Property)!.GetValue(instance)!;

            // For lists the documented value is ImmutableArray<T>.Empty; ImmutableArray equality is by the backing
            // array reference, so this also fails an uninitialised (default) array.
            Assert.True(Equals(row.Expected, actual),
                $"{row.Def.Name}.{row.Property} default is {actual}, documented {row.Expected}");
            Assert.True(IsNeutral(row.Kind, actual), $"{row.Def.Name}.{row.Property} default {actual} is not {row.Kind}");
        }
    }

    [Fact]
    public void EveryDefType_HasAMinimalFactory()
    {
        foreach (Type def in DefTypes())
        {
            Assert.True(_minimal.ContainsKey(def), $"{def.Name} needs a row in _minimal and in _defaults");
        }
    }

    [Fact]
    public void WeightClass_IsRequiredWithNoDefault_OnItemDefAndMailKindDef()
    {
        foreach (Type t in new[] { typeof(ItemDef), typeof(MailKindDef) })
        {
            PropertyInfo p = t.GetProperty("WeightClass")!;

            Assert.True(IsRequired(p), $"{t.Name}.WeightClass must be a required member");
            Assert.DoesNotContain(_defaults, r => r.Def == t && r.Property == "WeightClass");
        }

        // Even the zero value of the enum is not a class, so default(WeightClass) can never pass as authored data.
        foreach (WeightClass w in Enum.GetValues<WeightClass>())
        {
            Assert.NotEqual((byte)0, (byte)w);
        }
    }

    [Fact]
    public void RequiredMembers_AreMarkedOnTheTypeAndTheCompilerEmitsTheAttribute()
    {
        foreach (Type t in DefTypes())
        {
            Assert.True(t.IsDefined(typeof(RequiredMemberAttribute), false), $"{t.Name} has no required member");
        }
    }
}
