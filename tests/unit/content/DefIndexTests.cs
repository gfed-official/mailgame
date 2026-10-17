using System.Collections.Immutable;
using System.Reflection;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 001 (TR-content-007/008/009; GDD CDD-ID-2, CDD-ID-3, CDD-ID-4 first half):
/// the ordinal assigner, <see cref="DefTable{T}"/>, and the def members of <see cref="StringNamespace"/>.
/// The id structs themselves are owned and primarily tested by sim-core story 002 (IdTypesTests); this
/// file is their content-side regression guard. Behaviour that differs between SIM_ASSERT and other
/// builds branches on <see cref="TestBuildExpectation.SimAssertsExpected"/>. All inputs are fixed
/// permutations; nothing is random, timed or read from disk.
/// </summary>
public class DefIndexTests
{
    // The six mail kinds of M-1a in a fixed, non-sorted order.
    private static readonly string[] _shuffledMailKinds = { "small", "letter", "cargo", "postcard", "large", "medium" };

    private static readonly string[] _sortedMailKinds = { "cargo", "large", "letter", "medium", "postcard", "small" };

    private static string[] SyntheticIds(int count)
    {
        // Fixed, reversed order so the input is as far from sorted as possible; ids sort by number.
        var ids = new string[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = "id_" + (count - 1 - i).ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
        }

        return ids;
    }

    /// <summary>
    /// Runs <paramref name="act"/>. With Sim asserts on, verifies it throws
    /// <see cref="SimAssertException"/> with <paramref name="code"/> and returns false; otherwise runs it
    /// (any exception propagates) and returns true.
    /// </summary>
    private static bool RunExpectingAssertWhenEnabled(Action act, ushort code)
    {
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(act);
            Assert.Equal(code, thrown.Code);
            return false;
        }

        act();
        return true;
    }

    // ------------------------------------------------------------ AC-1: None and DefTable lookup

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void DefIndex_Default_IsNoneWithValue0xFFFF()
    {
        // Arrange
        DefIndex index = default;

        // Act / Assert
        Assert.True(index.IsNone);
        Assert.Equal((ushort)0xFFFF, index.Value);
        Assert.Equal(DefIndex.None, index);
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void DefTable_IndexedWithAssignedIndex_ReturnsDefAtThatOrdinal()
    {
        // Arrange: defs are stored in DefIndex (sorted-id) order, as the loader does.
        DefAssignment assignment = DefIdAssigner.Assign(_shuffledMailKinds);
        string[] defs = new string[assignment.Count];
        for (int ordinal = 0; ordinal < defs.Length; ordinal++)
        {
            defs[ordinal] = "def:" + assignment.SortedIds[ordinal];
        }

        var table = new DefTable<string>(defs.ToImmutableArray());

        // Act / Assert: every input id resolves to its own def.
        Assert.Equal(6, table.Count);
        for (int i = 0; i < _shuffledMailKinds.Length; i++)
        {
            Assert.Equal("def:" + _shuffledMailKinds[i], table[assignment.IndexOfInput[i]]);
        }

        Assert.Equal("def:cargo", table[DefIndex.FromValue(0)]);
        Assert.Equal("def:small", table[DefIndex.FromValue(5)]);
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void DefTable_IndexedWithNone_AssertsOrThrowsIndexOutOfRange()
    {
        // Arrange
        var table = new DefTable<int>(ImmutableArray.Create(10, 20, 30));
        DefIndex none = default;

        // Act / Assert
        if (TestBuildExpectation.SimAssertsExpected)
        {
            SimAssertException thrown = Assert.Throws<SimAssertException>(() => table[none]);
            Assert.Equal(ContentAssertCode.DefTableNoneIndex, thrown.Code);
        }
        else
        {
            Assert.Throws<IndexOutOfRangeException>(() => table[none]);
        }
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void DefTable_IndexedAtOrAboveCount_ThrowsIndexOutOfRange()
    {
        // Arrange
        var table = new DefTable<int>(ImmutableArray.Create(10, 20, 30));

        // Act / Assert: the ordinal is a valid DefIndex but not a def of this table.
        Assert.Throws<IndexOutOfRangeException>(() => table[DefIndex.FromValue(3)]);
    }

    [Fact]
    [Trait("Id", "CDD-ID-4")]
    public void DefTable_AsSpan_IsInDefIndexOrderAndMatchesCount()
    {
        // Arrange
        var table = new DefTable<int>(ImmutableArray.Create(7, 8, 9));

        // Act
        ReadOnlySpan<int> span = table.AsSpan();

        // Assert
        Assert.Equal(3, table.Count);
        Assert.Equal(3, span.Length);
        Assert.Equal(7, span[0]);
        Assert.Equal(8, span[1]);
        Assert.Equal(9, span[2]);
        Assert.Equal(span[2], table[DefIndex.FromValue(2)]);
    }

    [Fact]
    [Trait("Id", "CDD-ID-4")]
    public void DefTable_DefaultImmutableArray_IsEmpty()
    {
        // Arrange / Act
        var table = new DefTable<int>(default);

        // Assert
        Assert.Equal(0, table.Count);
        Assert.True(table.AsSpan().IsEmpty);
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void DefTable_ConstructedWithMoreThanMaxDefs_AssertsWhenEnabled()
    {
        // Arrange
        ImmutableArray<byte> tooMany = ImmutableArray.Create(new byte[DefIdAssigner.MaxDefsPerType + 1]);

        // Act
        DefTable<byte>? built = null;
        bool ran = RunExpectingAssertWhenEnabled(() => built = new DefTable<byte>(tooMany), ContentAssertCode.DefTableTooLarge);

        // Assert
        if (ran)
        {
            Assert.Equal(DefIdAssigner.MaxDefsPerType + 1, built!.Count);
        }
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void DefTable_ConstructedWithMaxDefs_HoldsLastOrdinal()
    {
        // Arrange
        var items = new int[DefIdAssigner.MaxDefsPerType];
        items[^1] = 42;

        // Act
        var table = new DefTable<int>(items.ToImmutableArray());

        // Assert
        Assert.Equal(65_534, table.Count);
        Assert.Equal(42, table[DefIndex.FromValue(DefIndex.MaxOrdinal)]);
    }

    // ------------------------------------------------------------ AC-2: ordinal order (CDD-ID-2)

    [Fact]
    [Trait("Id", "CDD-ID-2")]
    public void Assign_ShuffledMailKinds_YieldsOrdinalsCargo0Large1Letter2Medium3Postcard4Small5()
    {
        // Arrange / Act
        DefAssignment result = DefIdAssigner.Assign(_shuffledMailKinds);

        // Assert
        Assert.Equal(DefAssignmentStatus.Ok, result.Status);
        Assert.Equal(6, result.Count);
        Assert.Equal(_sortedMailKinds, result.SortedIds);
        for (int i = 0; i < _shuffledMailKinds.Length; i++)
        {
            int expectedOrdinal = Array.IndexOf(_sortedMailKinds, _shuffledMailKinds[i]);
            Assert.Equal((ushort)expectedOrdinal, result.IndexOfInput[i].Value);
        }

        Assert.Equal((ushort)0, result.IndexOfInput[2].Value); // cargo
        Assert.Equal((ushort)1, result.IndexOfInput[4].Value); // large
        Assert.Equal((ushort)2, result.IndexOfInput[1].Value); // letter
        Assert.Equal((ushort)3, result.IndexOfInput[5].Value); // medium
        Assert.Equal((ushort)4, result.IndexOfInput[3].Value); // postcard
        Assert.Equal((ushort)5, result.IndexOfInput[0].Value); // small
    }

    [Fact]
    [Trait("Id", "CDD-ID-2")]
    public void Assign_BikeIds_YieldsBike0Bike2Is1BikeCargo2()
    {
        // Arrange
        string[] input = { "bike_cargo", "bike2", "bike" };

        // Act
        DefAssignment result = DefIdAssigner.Assign(input);

        // Assert: '2' (0x32) sorts before '_' (0x5F) under ordinal comparison.
        Assert.Equal(new[] { "bike", "bike2", "bike_cargo" }, result.SortedIds);
        Assert.Equal((ushort)2, result.IndexOfInput[0].Value);
        Assert.Equal((ushort)1, result.IndexOfInput[1].Value);
        Assert.Equal((ushort)0, result.IndexOfInput[2].Value);
    }

    [Fact]
    [Trait("Id", "CDD-ID-2")]
    public void Assign_UpperCaseVersusLowerCase_UsesOrdinalNotCultureOrder()
    {
        // Arrange: a culture-aware compare would put "alpha" before "Zeta"; ordinal puts 'Z' (0x5A) first.
        string[] input = { "alpha", "Zeta" };

        // Act
        DefAssignment result = DefIdAssigner.Assign(input);

        // Assert
        Assert.Equal(new[] { "Zeta", "alpha" }, result.SortedIds);
        Assert.Equal((ushort)1, result.IndexOfInput[0].Value);
        Assert.Equal((ushort)0, result.IndexOfInput[1].Value);
    }

    [Fact]
    [Trait("Id", "CDD-ID-2")]
    public void Assign_ImmutableArrayAndSpanOverloads_AgreeAndDoNotMutateInput()
    {
        // Arrange
        string[] input = (string[])_shuffledMailKinds.Clone();

        // Act
        DefAssignment fromSpan = DefIdAssigner.Assign(input.AsSpan());
        DefAssignment fromImmutable = DefIdAssigner.Assign(input.ToImmutableArray());

        // Assert
        Assert.Equal(fromSpan.SortedIds, fromImmutable.SortedIds);
        Assert.Equal(fromSpan.IndexOfInput, fromImmutable.IndexOfInput);
        Assert.Equal(_shuffledMailKinds, input);
    }

    [Fact]
    [Trait("Id", "CDD-ID-2")]
    public void Assign_AnyInputPermutation_GivesSameSortedIds()
    {
        // Arrange: three fixed permutations of the same set.
        string[][] permutations =
        {
            _sortedMailKinds,
            new[] { "small", "postcard", "medium", "letter", "large", "cargo" },
            new[] { "medium", "cargo", "small", "large", "postcard", "letter" },
        };

        // Act / Assert
        foreach (string[] permutation in permutations)
        {
            Assert.Equal(_sortedMailKinds, DefIdAssigner.Assign(permutation).SortedIds);
        }
    }

    [Fact]
    [Trait("Id", "CDD-ID-2")]
    public void Assign_EmptyAndSingleInput_Work()
    {
        // Act
        DefAssignment empty = DefIdAssigner.Assign(ReadOnlySpan<string>.Empty);
        DefAssignment defaultArray = DefIdAssigner.Assign(default(ImmutableArray<string>));
        DefAssignment single = DefIdAssigner.Assign(new[] { "only" });

        // Assert
        Assert.Equal(DefAssignmentStatus.Ok, empty.Status);
        Assert.Equal(0, empty.Count);
        Assert.Empty(empty.SortedIds);
        Assert.Equal(DefAssignmentStatus.Ok, defaultArray.Status);
        Assert.Equal(0, defaultArray.Count);
        Assert.Equal(DefAssignmentStatus.Ok, single.Status);
        Assert.Equal((ushort)0, single.IndexOfInput[0].Value);
    }

    [Fact]
    [Trait("Id", "CDD-ID-2")]
    public void Assign_MoreThanOneInsertionRun_SortsOrdinallyAcrossMergePasses()
    {
        // Arrange: 100 ids span several StableSort runs and merge passes.
        string[] input = SyntheticIds(100);

        // Act
        DefAssignment result = DefIdAssigner.Assign(input);

        // Assert
        for (int ordinal = 0; ordinal < 100; ordinal++)
        {
            Assert.Equal("id_" + ordinal.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), result.SortedIds[ordinal]);
        }

        Assert.Equal((ushort)99, result.IndexOfInput[0].Value);
        Assert.Equal((ushort)0, result.IndexOfInput[99].Value);
    }

    [Fact]
    [Trait("Id", "CDD-ID-2")]
    public void Assign_DuplicateIds_AssertsWhenEnabledElseReportsDuplicateIds()
    {
        // Arrange
        string[] input = { "a", "b", "a" };

        // Act
        DefAssignment? result = null;
        bool ran = RunExpectingAssertWhenEnabled(() => result = DefIdAssigner.Assign(input), ContentAssertCode.DefIdsNotUnique);

        // Assert
        if (ran)
        {
            Assert.Equal(DefAssignmentStatus.DuplicateIds, result!.Status);
            Assert.Equal(3, result.Count);
            Assert.Empty(result.SortedIds);
            Assert.Empty(result.IndexOfInput);
        }
    }

    [Theory]
    [Trait("Id", "CDD-ID-2")]
    [InlineData(null)]
    [InlineData("")]
    public void Assign_NullOrEmptyId_AssertsWhenEnabledElseReportsInvalidId(string? badId)
    {
        // Arrange
        string?[] input = { "a", badId, "b" };

        // Act
        DefAssignment? result = null;
        bool ran = RunExpectingAssertWhenEnabled(() => result = DefIdAssigner.Assign(input!), ContentAssertCode.DefIdInvalid);

        // Assert: without SIM_ASSERT the status is InvalidId, never Ok, and nothing is assigned.
        if (ran)
        {
            Assert.Equal(DefAssignmentStatus.InvalidId, result!.Status);
            Assert.Equal(3, result.Count);
            Assert.Empty(result.SortedIds);
            Assert.Empty(result.IndexOfInput);
        }
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void Assign_MoreThanMaxIdsContainingDuplicateAndInvalid_ReportsTooManyDefsFirst()
    {
        // Arrange: 65 535 ids, the last two equal, plus a null: the count alone decides, before any id is inspected.
        string?[] input = new string?[DefIdAssigner.MaxDefsPerType + 1];
        for (int i = 0; i < input.Length; i++)
        {
            input[i] = "id_" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
        }

        input[^1] = input[^2];
        input[0] = null;

        // Act (never asserts: TooManyDefs is decided on the count)
        DefAssignment result = DefIdAssigner.Assign(input!);

        // Assert
        Assert.Equal(DefAssignmentStatus.TooManyDefs, result.Status);
        Assert.Equal(65_535, result.Count);
    }

    [Fact]
    [Trait("Id", "CDD-ID-2")]
    public void Assign_InvalidIdAndDuplicate_ReportsInvalidIdBeforeDuplicateIds()
    {
        // Arrange
        string?[] input = { "a", "a", "" };

        // Act
        DefAssignment? result = null;
        bool ran = RunExpectingAssertWhenEnabled(() => result = DefIdAssigner.Assign(input!), ContentAssertCode.DefIdInvalid);

        // Assert
        if (ran)
        {
            Assert.Equal(DefAssignmentStatus.InvalidId, result!.Status);
        }
    }

    [Fact]
    [Trait("Id", "CDD-ID-2")]
    public void ContentAssertCode_Values_AreDistinctAndInTheContentBlock2000To2999()
    {
        // Arrange
        ushort[] codes = typeof(ContentAssertCode)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(ushort))
            .Select(f => (ushort)f.GetRawConstantValue()!)
            .ToArray();

        // Assert: Sim owns < 1000, Client.Core 1000-1999, content 2000-2999.
        Assert.Equal(20, codes.Length);   // story 004 added BalanceKeyNone (2004) and BalanceKeyForeign (2005); story 007 added 2006 to 2011 (StatMulFactorsExceeded, StatSetWithoutResolve, StatMulOutOfRange, StatAddsTooMany, StatBoundsInverted, StatRoundingUnknown); story 014 added 2012 to 2017 (StatSheetUnknownStat, StatSheetBadQualifier, StatSheetBadSlot, StatSheetNegativeTransient, StatSheetTransientNotAdd, StatSheetBadModifier); story 014 revision added 2018 (StatSheetQualifierOnUnqualified); story 015 added 2019 (StatSheetBadFlagGrant)
        Assert.All(codes, c => Assert.InRange((int)c, 2000, 2999));
        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    // ------------------------------------------------------------ AC-3: 65 534 / 65 535 boundary

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void Assign_65534Ids_AssignsOrdinals0To65533AndNeverNone()
    {
        // Arrange
        string[] input = SyntheticIds(DefIdAssigner.MaxDefsPerType);

        // Act
        DefAssignment result = DefIdAssigner.Assign(input);

        // Assert
        Assert.Equal(DefAssignmentStatus.Ok, result.Status);
        Assert.Equal(65_534, result.Count);
        Assert.Equal(65_534, result.SortedIds.Length);
        var seen = new bool[65_534];
        for (int i = 0; i < input.Length; i++)
        {
            DefIndex index = result.IndexOfInput[i];
            Assert.False(index.IsNone);
            Assert.NotEqual((ushort)0xFFFF, index.Value);
            Assert.True(index.Value <= DefIndex.MaxOrdinal);
            Assert.False(seen[index.Value]);
            seen[index.Value] = true;
        }

        Assert.DoesNotContain(false, seen);
        Assert.Equal("id_00000", result.SortedIds[0]);
        Assert.Equal("id_65533", result.SortedIds[65_533]);
        Assert.Equal((ushort)65_533, result.IndexOfInput[0].Value);
        Assert.Equal((ushort)0, result.IndexOfInput[65_533].Value);
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void Assign_65535Ids_ReturnsTooManyDefsCarryingCountWithNoIndices()
    {
        // Arrange
        string[] input = SyntheticIds(DefIdAssigner.MaxDefsPerType + 1);

        // Act
        DefAssignment result = DefIdAssigner.Assign(input);

        // Assert: a result value, not an exception, and nothing assigned.
        Assert.Equal(DefAssignmentStatus.TooManyDefs, result.Status);
        Assert.Equal(65_535, result.Count);
        Assert.Empty(result.SortedIds);
        Assert.Empty(result.IndexOfInput);
    }

    // ------------------------------------------------------------ AC-4: namespace separation

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void Assign_AddingMailKindAaa_ChangesNoItemDefIndex()
    {
        // Arrange: each def type has its own id list, hence its own ordinal space.
        string[] itemIds = { "stamp", "envelope", "parcel" };
        string[] mailKinds = { "small", "letter" };
        string[] mailKindsWithAaa = { "small", "letter", "aaa" };

        // Act
        DefAssignment itemsBefore = DefIdAssigner.Assign(itemIds);
        DefAssignment kindsBefore = DefIdAssigner.Assign(mailKinds);
        DefAssignment kindsAfter = DefIdAssigner.Assign(mailKindsWithAaa);
        DefAssignment itemsAfter = DefIdAssigner.Assign(itemIds);

        // Assert
        Assert.Equal(itemsBefore.IndexOfInput, itemsAfter.IndexOfInput);
        Assert.Equal(itemsBefore.SortedIds, itemsAfter.SortedIds);
        Assert.Equal((ushort)0, kindsBefore.IndexOfInput[1].Value); // letter was 0 ...
        Assert.Equal((ushort)1, kindsAfter.IndexOfInput[1].Value);  // ... and moves to 1 once "aaa" is added
        Assert.Equal((ushort)0, kindsAfter.IndexOfInput[2].Value);  // aaa
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void StringId_EqualIndexDifferentNamespace_AreUnequal()
    {
        // Arrange
        var item = new StringId(StringNamespace.Item, 4);
        var kind = new StringId(StringNamespace.MailKind, 4);
        var itemAgain = new StringId(StringNamespace.Item, 4);

        // Act / Assert
        Assert.False(item == kind);
        Assert.True(item != kind);
        Assert.False(item.Equals(kind));
        Assert.False(item.Equals((object)kind));
        Assert.NotEqual(item.GetHashCode(), kind.GetHashCode());
        Assert.True(item == itemAgain);
        Assert.True(item.Equals(itemAgain));
    }

    [Theory]
    [Trait("Id", "CDD-ID-3")]
    [InlineData(StringNamespace.Item, (ushort)0)]
    [InlineData(StringNamespace.Container, (ushort)7)]
    [InlineData(StringNamespace.MailKind, (ushort)65_533)]
    [InlineData(StringNamespace.DestinationType, (ushort)1)]
    [InlineData(StringNamespace.MailMix, (ushort)300)]
    [InlineData(StringNamespace.Run, (ushort)3)]
    public void StringIdGetHashCode_Namespace_IsNsShifted16OrIndex(StringNamespace ns, ushort index)
    {
        // Arrange
        var id = new StringId(ns, index);

        // Act / Assert
        Assert.Equal(((int)ns << 16) | index, id.GetHashCode());
    }

    // ------------------------------------------------------------ AC-5: namespace members and API shape

    [Fact]
    [Trait("Id", "CDD-ID-4")]
    public void StringNamespace_DefMembers_HaveTheM1aValuesAndNoneAndRunAreUnchanged()
    {
        // Assert
        Assert.Equal((byte)0, (byte)StringNamespace.None);
        Assert.Equal((byte)1, (byte)StringNamespace.Item);
        Assert.Equal((byte)2, (byte)StringNamespace.Container);
        Assert.Equal((byte)3, (byte)StringNamespace.MailKind);
        Assert.Equal((byte)4, (byte)StringNamespace.DestinationType);
        Assert.Equal((byte)5, (byte)StringNamespace.MailMix);
        Assert.Equal((byte)0xFF, (byte)StringNamespace.Run);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(StringNamespace)));
    }

    [Fact]
    [Trait("Id", "CDD-ID-4")]
    public void DefType_Members_EqualTheStringNamespaceMembersOfTheSameName()
    {
        // Arrange
        string[] names = Enum.GetNames<DefType>();

        // Act / Assert
        Assert.Equal(5, names.Length);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(DefType)));
        foreach (string name in names)
        {
            Assert.True(Enum.TryParse(name, out StringNamespace ns), name);
            Assert.Equal((byte)Enum.Parse<DefType>(name), (byte)ns);
        }

        Assert.Equal((byte)1, (byte)DefType.Item);
        Assert.Equal((byte)2, (byte)DefType.Container);
        Assert.Equal((byte)3, (byte)DefType.MailKind);
        Assert.Equal((byte)4, (byte)DefType.DestinationType);
        Assert.Equal((byte)5, (byte)DefType.MailMix);
    }

    [Fact]
    [Trait("Id", "CDD-ID-4")]
    public void DefTable_PublicApi_HasOnlyADefIndexIndexerAndNoStringTypedMember()
    {
        // Arrange
        Type table = typeof(DefTable<int>);
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // Act
        PropertyInfo[] indexers = table.GetProperties(Declared).Where(p => p.GetIndexParameters().Length > 0).ToArray();

        // Assert: the one indexer takes a DefIndex, never an int, ushort or string key.
        PropertyInfo indexer = Assert.Single(indexers);
        ParameterInfo indexParameter = Assert.Single(indexer.GetIndexParameters());
        Assert.Equal(typeof(DefIndex), indexParameter.ParameterType);

        // Assert: no public method, constructor, property or field takes or returns a string.
        foreach (MethodBase method in table.GetMethods(Declared).Cast<MethodBase>().Concat(table.GetConstructors()))
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Assert.NotEqual(typeof(string), parameter.ParameterType);
            }

            if (method is MethodInfo info)
            {
                Assert.NotEqual(typeof(string), info.ReturnType);
            }
        }

        foreach (PropertyInfo property in table.GetProperties(Declared))
        {
            Assert.NotEqual(typeof(string), property.PropertyType);
        }

        foreach (FieldInfo field in table.GetFields(Declared))
        {
            Assert.NotEqual(typeof(string), field.FieldType);
        }

        Assert.Equal(new[] { typeof(ImmutableArray<int>) }, Assert.Single(table.GetConstructors()).GetParameters().Select(p => p.ParameterType).ToArray());
        string[] names = table.GetMembers(Declared).Select(m => m.Name).ToArray();
        Assert.Contains("Count", names);
        Assert.Contains("AsSpan", names);
        Assert.Contains("Item", names);
    }

    [Fact]
    [Trait("Id", "CDD-ID-3")]
    public void IdTypes_AreDeclaredOnlyInPrimitivesAndNeverRedeclaredInContent()
    {
        // Arrange
        Assembly sim = typeof(DefIndex).Assembly;
        Type[] all = sim.GetTypes();

        // Act / Assert: exactly one declaration of each, in the Primitives namespace.
        foreach (string name in new[] { "DefIndex", "StringId", "StringNamespace" })
        {
            Type declared = Assert.Single(all, t => t.Name == name);
            Assert.Equal("PerformativeMail.Sim.Primitives", declared.Namespace);
        }

        Assert.DoesNotContain(all, t => t.Namespace == "PerformativeMail.Sim.Content" && (t.Name == "DefIndex" || t.Name == "StringId"));
    }

    [Fact]
    [Trait("Id", "TR-content-008")]
    public void DefIndex_Type_HasNoSerialisationAttributesAndNoJsonConverterExists()
    {
        // Arrange
        Type defIndex = typeof(DefIndex);

        // Act
        bool serialisationAttribute = defIndex.GetCustomAttributes(inherit: false).Any(a =>
            a.GetType().Namespace is { } ns
            && (ns.StartsWith("System.Text.Json", StringComparison.Ordinal)
                || ns.StartsWith("System.Runtime.Serialization", StringComparison.Ordinal)
                || a is SerializableAttribute));
        bool jsonConverter = typeof(DefIndex).Assembly.GetTypes().Any(t =>
            IsJsonConverterOfDefIndex(t.BaseType));

        // Assert: DefIndex is never persisted (ADR-0024 persists string ids).
        Assert.False(serialisationAttribute);
        Assert.False(jsonConverter);
    }

    private static bool IsJsonConverterOfDefIndex(Type? type)
    {
        for (Type? t = type; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType
                && t.Namespace?.StartsWith("System.Text.Json", StringComparison.Ordinal) == true
                && t.GetGenericArguments().Contains(typeof(DefIndex)))
            {
                return true;
            }
        }

        return false;
    }
}
