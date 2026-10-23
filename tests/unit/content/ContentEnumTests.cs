using System.Reflection;
using System.Text;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 002 (TR-content-012; GDD CDD-SCHEMA-4 lookup half, CD8, CDD-PARSE-4 enum clause):
/// the six closed M-1a content enums and their hand-written snake_case spelling tables.
/// The snapshot tables below are LITERAL on purpose: the numeric values are part of the content hash (F6 writes
/// them as u16), so a changed value, a reused value, a renamed member or a missing spelling must fail here and
/// force a deliberate edit. Reflection-style enumeration (Enum.GetValues) is allowed in the test project, which
/// does not carry the Sim's banned-API analyzer. Nothing here is random, timed or read from disk.
/// </summary>
public class ContentEnumTests
{
    private delegate bool TryParseFn<T>(ReadOnlySpan<byte> utf8, out T value);

    private delegate ReadOnlySpan<byte> ToSpellingFn<T>(T value);

    /// <summary>One snapshot row: member name, numeric value, data spelling.</summary>
    private readonly record struct Row(string Name, int Value, string Spelling);

    private static readonly Row[] _itemCategory =
    {
        new("Tool", 1, "tool"), new("Material", 2, "material"), new("Consumable", 3, "consumable"),
        new("Ammo", 4, "ammo"), new("Blueprint", 5, "blueprint"), new("Weapon", 6, "weapon"), new("Mail", 7, "mail"),
    };

    private static readonly Row[] _weightClass =
    {
        new("Light", 1, "light"), new("Medium", 2, "medium"), new("Heavy", 3, "heavy"), new("Bulk", 4, "bulk"),
    };

    private static readonly Row[] _containerLayout =
    {
        new("Grid", 1, "grid"), new("Slots", 2, "slots"), new("Pile", 3, "pile"),
    };

    private static readonly Row[] _beltAccess =
    {
        new("None", 1, "none"), new("AnySide", 2, "any_side"), new("LoadingFace", 3, "loading_face"),
        new("OutfeedOnly", 4, "outfeed_only"), new("InserterOnly", 5, "inserter_only"),
    };

    private static readonly Row[] _containerView =
    {
        new("Grid", 1, "grid"), new("Manifest", 2, "manifest"), new("Auto", 3, "auto"),
    };

    private static readonly Row[] _equipSlot =
    {
        new("None", 1, "none"), new("Back", 2, "back"),
    };

    private static readonly Row[] _verb =
    {
        new("Deliver", 1, "deliver"), new("Open", 2, "open"), new("PickUp", 3, "pick_up"), new("Mount", 4, "mount"),
        new("Operate", 5, "operate"), new("Configure", 6, "configure"), new("Shop", 7, "shop"), new("Announce", 8, "announce"),
        new("Dismount", 9, "dismount"), new("Catch", 10, "catch"), new("Push", 11, "push"), new("Rebuild", 12, "rebuild"),
        new("Deconstruct", 13, "deconstruct"),
    };

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>Snapshot + bijectivity + undefined-value checks for one enum, against its literal table.</summary>
    private static void AssertEnum<T>(Row[] expected, TryParseFn<T> tryParse, ToSpellingFn<T> toSpelling)
        where T : struct, Enum
    {
        T[] members = Enum.GetValues<T>();

        // Snapshot: exactly the expected members (a new or removed member fails here).
        Assert.Equal(expected.Length, members.Length);
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(T)));
        for (int i = 0; i < expected.Length; i++)
        {
            Row row = expected[i];
            T member = Enum.Parse<T>(row.Name);   // throws when the expected member was renamed or removed
            Assert.Equal(row.Value, Convert.ToInt32(member));
            Assert.Equal(row.Name, Enum.GetName(member));
            Assert.Contains(member, members);

            // Spelling both ways.
            Assert.Equal(Utf8(row.Spelling), toSpelling(member).ToArray());
            Assert.True(tryParse(Utf8(row.Spelling), out T parsed), row.Spelling);
            Assert.Equal(member, parsed);
        }

        // Numbered from 1 in CD8 order, 0 never valid, values and spellings unique (not reused).
        Assert.Equal(Enumerable.Range(1, expected.Length), expected.Select(r => r.Value));
        Assert.DoesNotContain(members, m => Convert.ToInt32(m) == 0);
        Assert.Equal(expected.Length, expected.Select(r => r.Value).Distinct().Count());
        Assert.Equal(expected.Length, expected.Select(r => r.Spelling).Distinct().Count());
        Assert.All(expected, r => Assert.Matches("^[a-z][a-z_]*$", r.Spelling));

        // Bijective over the real members (not just the literal table): every member has exactly one spelling and
        // every spelling maps back to it; no two members share a spelling.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (T member in members)
        {
            byte[] spelling = toSpelling(member).ToArray();
            Assert.NotEmpty(spelling);
            Assert.True(seen.Add(Encoding.UTF8.GetString(spelling)), "duplicate spelling for " + member);
            Assert.True(tryParse(spelling, out T back));
            Assert.Equal(member, back);
        }

        // Undefined values have no spelling; 0 and 255 are not members.
        Assert.True(toSpelling(default).IsEmpty);
        Assert.True(toSpelling((T)Enum.ToObject(typeof(T), 255)).IsEmpty);
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void ItemCategory_SnapshotAndBijective() =>
        AssertEnum<ItemCategory>(_itemCategory, ItemCategorySpelling.TryParse, ItemCategorySpelling.ToSpelling);

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void WeightClass_SnapshotAndBijective() =>
        AssertEnum<WeightClass>(_weightClass, WeightClassSpelling.TryParse, WeightClassSpelling.ToSpelling);

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void ContainerLayout_SnapshotAndBijective() =>
        AssertEnum<ContainerLayout>(_containerLayout, ContainerLayoutSpelling.TryParse, ContainerLayoutSpelling.ToSpelling);

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void BeltAccess_SnapshotAndBijective() =>
        AssertEnum<BeltAccess>(_beltAccess, BeltAccessSpelling.TryParse, BeltAccessSpelling.ToSpelling);

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void ContainerView_SnapshotAndBijective() =>
        AssertEnum<ContainerView>(_containerView, ContainerViewSpelling.TryParse, ContainerViewSpelling.ToSpelling);

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void EquipSlot_SnapshotAndBijective() =>
        AssertEnum<EquipSlot>(_equipSlot, EquipSlotSpelling.TryParse, EquipSlotSpelling.ToSpelling);

    [Fact]
    [Trait("Id", "PCM-P10-1")]
    public void Verb_SnapshotAndBijective() =>
        AssertEnum<Verb>(_verb, VerbSpelling.TryParse, VerbSpelling.ToSpelling);

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void DefaultedMembers_HaveExplicitNonZeroValues()
    {
        // The FieldSpec default (story 018) supplies these; default(T) must never stand in for them.
        Assert.NotEqual(default, BeltAccess.None);
        Assert.NotEqual(default, ContainerView.Auto);
        Assert.NotEqual(default, EquipSlot.None);
        Assert.Equal(1, (int)BeltAccess.None);
        Assert.Equal(3, (int)ContainerView.Auto);
        Assert.Equal(1, (int)EquipSlot.None);
        Assert.Equal(1, (int)WeightClass.Light);   // F6 worked example: weightClass light encodes as 01 00
    }

    [Theory]
    [Trait("Id", "CDD-SCHEMA-4")]
    [InlineData("5")]            // numeric index of nothing, and a digit string
    [InlineData("1")]            // the numeric value of Light: still no numeric fallback
    [InlineData("01")]
    [InlineData("Light")]        // wrong case
    [InlineData("LIGHT")]
    [InlineData("unknown")]
    [InlineData(" light")]       // no trimming
    [InlineData("light ")]
    [InlineData("ligh")]         // no prefix match
    [InlineData("lightt")]
    [InlineData("light\0")]      // no terminator tolerance
    public void WeightClass_NonSpellings_AreNotFound_WithNoNumericOrCaseInsensitiveFallback(string text)
    {
        Assert.False(WeightClassSpelling.TryParse(Utf8(text), out WeightClass value));
        Assert.Equal(default, value);
        Assert.Equal(0, (int)value);
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void WeightClass_EmptySpan_IsNotFound()
    {
        Assert.False(WeightClassSpelling.TryParse(ReadOnlySpan<byte>.Empty, out WeightClass value));
        Assert.Equal(default, value);
        Assert.False(WeightClassSpelling.TryParse(default, out _));
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void WeightClass_SpellingInsideALargerBuffer_IsFoundOnlyBySlice()
    {
        byte[] buffer = Utf8("xxheavyxx");
        Assert.True(WeightClassSpelling.TryParse(buffer.AsSpan(2, 5), out WeightClass value));
        Assert.Equal(WeightClass.Heavy, value);
        Assert.False(WeightClassSpelling.TryParse(buffer, out _));
        Assert.False(WeightClassSpelling.TryParse(buffer.AsSpan(2, 6), out _));
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void EveryEnum_RejectsWrongCaseNumbersAndEmpty()
    {
        // The same ordinal-only rule holds for the other five enums, checked through their own spellings.
        foreach (string bad in new[] { "", "0", "1", "NONE", "None", "Grid", "GRID", "Mail", "MAIL", "Back", "AnySide", "any-side" })
        {
            byte[] text = Utf8(bad);
            Assert.False(ItemCategorySpelling.TryParse(text, out _), bad);
            Assert.False(ContainerLayoutSpelling.TryParse(text, out _), bad);
            Assert.False(BeltAccessSpelling.TryParse(text, out _), bad);
            Assert.False(ContainerViewSpelling.TryParse(text, out _), bad);
            Assert.False(EquipSlotSpelling.TryParse(text, out _), bad);
        }
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void SameSpellingInDifferentEnums_ResolvesPerEnum()
    {
        // "grid" is both a ContainerLayout and a ContainerView; "none" is both a BeltAccess and an EquipSlot.
        Assert.True(ContainerLayoutSpelling.TryParse("grid"u8, out ContainerLayout layout));
        Assert.True(ContainerViewSpelling.TryParse("grid"u8, out ContainerView view));
        Assert.Equal(ContainerLayout.Grid, layout);
        Assert.Equal(ContainerView.Grid, view);
        Assert.True(BeltAccessSpelling.TryParse("none"u8, out BeltAccess belt));
        Assert.True(EquipSlotSpelling.TryParse("none"u8, out EquipSlot slot));
        Assert.Equal(BeltAccess.None, belt);
        Assert.Equal(EquipSlot.None, slot);
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-4")]
    public void SpellingTables_HaveNoStaticFields()
    {
        // ADR-0003 static-state rule: the tables are code over UTF-8 literals, so there is no
        // static field at all (nothing mutable, nothing to mark [ImmutableTable]).
        foreach (Type t in new[]
        {
            typeof(ItemCategorySpelling), typeof(WeightClassSpelling), typeof(ContainerLayoutSpelling),
            typeof(BeltAccessSpelling), typeof(ContainerViewSpelling), typeof(EquipSlotSpelling), typeof(VerbSpelling),
        })
        {
            FieldInfo[] fields = t.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.Empty(fields);
            Assert.True(t.IsAbstract && t.IsSealed, t.Name + " must be a static class");
        }
    }

    /// <summary>Walks up from the test output directory to the folder holding PerformativeMail.sln.</summary>
    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PerformativeMail.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("PerformativeMail.sln not found above " + AppContext.BaseDirectory);
    }

    private static readonly string[] _forbiddenEnumApis =
    {
        "Enum.GetValues", "Enum.GetNames", "Enum.Parse", "Enum.TryParse", "Enum.IsDefined", "Enum.GetName",
    };

    /// <summary>Returns the forbidden Enum APIs that appear on non-comment lines.</summary>
    private static List<string> ForbiddenEnumApisIn(IEnumerable<string> lines)
    {
        var hits = new List<string>();
        foreach (string line in lines)
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;   // doc and line comments may mention the banned names
            }

            foreach (string api in _forbiddenEnumApis)
            {
                if (line.Contains(api, StringComparison.Ordinal))
                {
                    hits.Add(api);
                }
            }
        }

        return hits;
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-4")]
    public void ContentSources_NeverUseReflectionBasedEnumApis_SourceScan()
    {
        // The one sanctioned file read in this test class. BannedSymbols.txt (RS0030) bans every Enum parse/discovery
        // overload EXCEPT the generic Enum.GetValues<TEnum>(), which Sim Core already uses. Banning it is deferred to
        // simulation-core story 028 (tech debt); until then this scan guards the content path (ADR-0010 section 2).
        string contentDir = Path.Combine(FindRepoRoot(), "src", "PerformativeMail.Sim", "Content");
        string[] files = Directory.GetFiles(contentDir, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        Assert.Contains(files, f => Path.GetFileName(f) == "WeightClassSpelling.cs");   // the scan really sees Content/Enums

        var hits = new List<string>();
        foreach (string file in files)
        {
            foreach (string api in ForbiddenEnumApisIn(File.ReadAllLines(file)))
            {
                hits.Add(Path.GetFileName(file) + ": " + api);
            }
        }

        Assert.Empty(hits);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-4")]
    public void SourceScanRule_FlagsCodeLines_AndIgnoresCommentLines()
    {
        Assert.Equal(new[] { "Enum.GetValues" }, ForbiddenEnumApisIn(new[] { "foreach (var v in Enum.GetValues<Foo>())" }));
        Assert.Equal(new[] { "Enum.Parse" }, ForbiddenEnumApisIn(new[] { "    var x = Enum.Parse<Foo>(s);" }));
        Assert.Empty(ForbiddenEnumApisIn(new[] { "    // Enum.GetValues is banned", "/// <c>Enum.Parse</c> is not used" }));
        Assert.Empty(ForbiddenEnumApisIn(new[] { "int x = 1;" }));
    }
}
