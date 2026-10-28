using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Defs;
using PerformativeMail.Sim.Content.Parsing;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 018 (phase A): the five M-1a <see cref="FieldSpec"/> tables. Every table passes its own
/// self-check (TD-090), lists the CD10 fields in <c>DefCanonical</c>'s encode order, uses units that match the field-name suffix,
/// names real enum sets and reference targets, and defaults to a neutral value except the GDD-stated exceptions.
/// </summary>
public class FieldSpecTableTests
{
    private static ImmutableArray<string> Names(ImmutableArray<FieldSpec> rows) =>
        rows.Select(r => r.Name).ToImmutableArray();

    private static IEnumerable<(string Path, FieldSpec Row)> AllRows(string prefix, ImmutableArray<FieldSpec> rows)
    {
        foreach (FieldSpec row in rows)
        {
            yield return (prefix + row.Name, row);
            foreach ((string, FieldSpec) nested in AllRows(prefix + row.Name + ".", row.Children))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<(string Path, FieldSpec Row)> EveryRow() =>
        ContentSchema.Tables.SelectMany(t => AllRows(t.TypeName + ".", t.Rows));

    // ------------------------------------------------------------ self-check

    [Fact]
    public void EveryFieldSpecTable_PassesSelfCheck()
    {
        Assert.Equal(5, ContentSchema.Tables.Length);
        foreach (SchemaTable table in ContentSchema.Tables)
        {
            Assert.Empty(FieldSpecTable.SelfCheck(table.Rows));
        }

        Assert.Empty(ContentSchema.SelfCheck());
    }

    [Fact]
    public void Tables_FollowDefTypeOrder_AndAreBuiltFreshOnEachCall()
    {
        DefType[] expected = { DefType.Item, DefType.Container, DefType.MailKind, DefType.DestinationType, DefType.MailMix };
        Assert.Equal(expected, ContentSchema.Tables.Select(t => t.Type).ToArray());
        foreach (DefType type in expected)
        {
            Assert.False(ContentSchema.Rows(type).IsEmpty);
        }

        Assert.True(ContentSchema.Rows((DefType)200).IsEmpty);
        Assert.NotSame(ItemDefFieldSpec.Rows[0], ItemDefFieldSpec.Rows[0]);   // built fresh: no shared static array
    }

    // ------------------------------------------------------------ CD10 snapshot and canonical order

    private static readonly (DefType Type, string[] Names)[] _canonicalOrder =
    {
        // Hand-written from DefCanonical.WriteCanonical (story 013) and the story 018 JSON field table.
        (DefType.Item, new[] { "id", "name", "category", "grid", "maxStack", "weightClass", "sellPrice", "buyPrice", "carryByHand", "equipSlot", "tags" }),
        (DefType.Container, new[] { "id", "layout", "grid", "view", "beltAccess", "allowedCategories", "handCarried", "acceptsInsert", "reservedSlots" }),
        (DefType.MailKind, new[]
        {
            "id", "name", "grid", "maxStack", "baseValue", "weightClass", "carryByHand", "beltLanes", "beltLengthMetres",
            "deadlineOffsetShifts", "acceptedBy", "complaintOnMisdeliveryPoints", "unlockShift", "fallbackKind",
        }),
        (DefType.DestinationType, new[] { "id", "accepts", "insertRate", "manualInsertHoldSeconds", "maxAutomatedFeeders", "requiresUnit", "catchVolume" }),
        // NOTE: the story's prose lists spawnOverhead after the four ratios; DefCanonical (the contract) encodes it before them.
        (DefType.MailMix, new[]
        {
            "id", "shifts", "batchIntervalSeconds", "batchJitterSeconds", "tailGuardSeconds", "openingRampBatches", "spawnOverhead",
            "streetStreakRatio", "raidSpawnRateRatio", "distanceStepRatio", "shiftStepRatio",
        }),
    };

    [Fact]
    public void EachTable_FieldOrder_EqualsTheCanonicalEncodeOrder()
    {
        foreach ((DefType type, string[] names) in _canonicalOrder)
        {
            Assert.Equal(names, Names(ContentSchema.Rows(type)).ToArray());
        }
    }

    [Fact]
    public void NestedTables_FieldOrder_EqualsTheCanonicalEncodeOrder()
    {
        FieldSpec catchVolume = ContentSchema.Rows(DefType.DestinationType).Single(r => r.Name == "catchVolume");
        Assert.Equal(FieldKind.Object, catchVolume.Kind);
        Assert.Equal(
            new[] { "widthMetres", "heightMetres", "depthMetres", "centreHeightMetres", "yawDeg" },
            Names(catchVolume.Children).ToArray());

        FieldSpec shifts = ContentSchema.Rows(DefType.MailMix).Single(r => r.Name == "shifts");
        Assert.Equal(FieldKind.ObjectList, shifts.Kind);
        Assert.Equal(new[] { "shift", "shares" }, Names(shifts.Children).ToArray());
        Assert.Equal(FieldKind.RefMap, shifts.Children[1].Kind);
    }

    private static string Describe(FieldSpec r)
    {
        string req = r.Required ? "R" : "O";
        string def = r.Default is long d ? "=" + d : r.DefaultSpelling is not null ? "=" + r.DefaultSpelling : string.Empty;
        string range = r.Min == int.MinValue && r.Max == int.MaxValue ? string.Empty : "[" + r.Min + ".." + r.Max + "]";
        string target = r.EnumSet ?? r.RefTarget ?? string.Empty;
        string len = r.FixedLength > 0 ? "#" + r.FixedLength : string.Empty;
        string dur = r.IsDuration ? "~dur" : string.Empty;
        string variant = r.Variant is null
            ? string.Empty
            : " {" + r.Variant.On + " req:" + string.Join("/", r.Variant.RequiredFor) + " forbid:" + string.Join("/", r.Variant.ForbiddenFor) + "}";
        return r.Name + ":" + r.Kind + (r.Unit == 0 ? string.Empty : "/" + r.Unit) + len + dur + range + " " + req + def + (target.Length > 0 ? " ->" + target : string.Empty) + variant;
    }

    [Fact]
    public void ItemDef_Snapshot_MatchesCd10()
    {
        Assert.Equal(
            new[]
            {
                "id:Text R", "name:Text R", "category:Enum R ->ItemCategory", "grid:IntList/Count#2[1..255] R",
                "maxStack:Number/Count[0..65535] R", "weightClass:Enum R ->WeightClass", "sellPrice:Number/Cents[0..2147483647] O=0",
                "buyPrice:Number/Cents[0..2147483647] O", "carryByHand:Bool O=0", "equipSlot:Enum O=none ->EquipSlot", "tags:TextList O",
            },
            ItemDefFieldSpec.Rows.Select(Describe).ToArray());
    }

    [Fact]
    public void ContainerDef_Snapshot_MatchesCd10_IncludingTheLayoutVariants()
    {
        Assert.Equal(
            new[]
            {
                "id:Text R", "layout:Enum R ->ContainerLayout",
                "grid:IntList/Count#2[0..255] O {layout req:grid/slots forbid:pile}",
                "view:Enum R ->ContainerView", "beltAccess:Enum O=none ->BeltAccess", "allowedCategories:EnumList O ->ItemCategory",
                "handCarried:Bool O=0", "acceptsInsert:Bool O=0",
                "reservedSlots:IntList/Count[0..65535] O {layout req: forbid:grid/pile}",
            },
            ContainerDefFieldSpec.Rows.Select(Describe).ToArray());
    }

    [Fact]
    public void MailKindDef_Snapshot_MatchesCd10()
    {
        Assert.Equal(
            new[]
            {
                "id:Text R", "name:Text R", "grid:IntList/Count#2[1..255] R", "maxStack:Number/Count[0..65535] R",
                "baseValue:Number/Cents[0..2147483647] R", "weightClass:Enum R ->WeightClass", "carryByHand:Bool O=0",
                "beltLanes:Number/Count[0..2147483647] R", "beltLengthMetres:Number/Metres[0..2147483647] R",
                "deadlineOffsetShifts:Number/Count[0..2147483647] O=0", "acceptedBy:RefList R ->DestinationTypeDef",
                "complaintOnMisdeliveryPoints:Number/Points[0..2147483647] R", "unlockShift:Number/Count[0..2147483647] R",
                "fallbackKind:Reference O ->MailKindDef",
            },
            MailKindDefFieldSpec.Rows.Select(Describe).ToArray());
    }

    [Fact]
    public void DestinationTypeDef_Snapshot_MatchesCd10()
    {
        Assert.Equal(
            new[]
            {
                "id:Text R", "accepts:RefList R ->MailKindDef", "insertRate:Number/PerSecond[0..2147483647] R",
                "manualInsertHoldSeconds:Number/Seconds~dur[0..2147483647] R", "maxAutomatedFeeders:Number/Count[0..2147483647] R",
                "requiresUnit:Bool O=0", "catchVolume:Object R",
            },
            DestinationTypeDefFieldSpec.Rows.Select(Describe).ToArray());
        Assert.Equal(
            new[]
            {
                "widthMetres:Number/Metres[0..2147483647] R", "heightMetres:Number/Metres[0..2147483647] R",
                "depthMetres:Number/Metres[0..2147483647] R", "centreHeightMetres:Number/Metres R", "yawDeg:Number/Deg R",
            },
            DestinationTypeDefFieldSpec.Rows[6].Children.Select(Describe).ToArray());
    }

    [Fact]
    public void MailMixDef_Snapshot_MatchesCd10()
    {
        Assert.Equal(
            new[]
            {
                "id:Text R", "shifts:ObjectList R", "batchIntervalSeconds:Number/Seconds~dur[0..2147483647] R",
                "batchJitterSeconds:Number/Seconds~dur[0..2147483647] R", "tailGuardSeconds:Number/Seconds~dur[0..2147483647] R",
                "openingRampBatches:Number/Count[0..2147483647] O=0", "spawnOverhead:Number/Ratio[0..2147483647] R",
                "streetStreakRatio:Number/Ratio[0..2147483647] R", "raidSpawnRateRatio:Number/Ratio[0..2147483647] R",
                "distanceStepRatio:Number/Ratio[0..2147483647] R", "shiftStepRatio:Number/Ratio[0..2147483647] R",
            },
            MailMixDefFieldSpec.Rows.Select(Describe).ToArray());
        Assert.Equal(
            new[] { "shift:Number/Count[0..2147483647] R", "shares:RefMap/Ratio[0..1000] R ->MailKindDef" },
            MailMixDefFieldSpec.Rows[1].Children.Select(Describe).ToArray());
    }

    // ------------------------------------------------------------ units

    private static readonly (string Suffix, FieldUnit Unit)[] _suffixUnits =
    {
        ("Seconds", FieldUnit.Seconds), ("Metres", FieldUnit.Metres), ("Ratio", FieldUnit.Ratio), ("Points", FieldUnit.Points),
        ("Deg", FieldUnit.Deg), ("Ms", FieldUnit.Ms), ("Mm", FieldUnit.Mm), ("Pm", FieldUnit.Pm), ("Ticks", FieldUnit.Ticks),
        ("Cents", FieldUnit.Cents),
    };

    // Numeric names with no unit suffix: listed explicitly so a new unsuffixed field is a conscious decision.
    private static readonly (string Name, FieldUnit Unit)[] _unsuffixed =
    {
        ("grid", FieldUnit.Count), ("maxStack", FieldUnit.Count), ("sellPrice", FieldUnit.Cents), ("buyPrice", FieldUnit.Cents),
        ("baseValue", FieldUnit.Cents), ("beltLanes", FieldUnit.Count), ("deadlineOffsetShifts", FieldUnit.Count),
        ("unlockShift", FieldUnit.Count), ("insertRate", FieldUnit.PerSecond), ("maxAutomatedFeeders", FieldUnit.Count),
        ("openingRampBatches", FieldUnit.Count), ("spawnOverhead", FieldUnit.Ratio), ("shift", FieldUnit.Count),
        ("shares", FieldUnit.Ratio), ("reservedSlots", FieldUnit.Count),
    };

    private static bool HasUnit(FieldKind kind) => kind is FieldKind.Number or FieldKind.IntList or FieldKind.RefMap;

    [Fact]
    public void EveryNumericRow_UnitMatchesItsFieldNameSuffix_OrIsListedExplicitly()
    {
        int checkedRows = 0;
        foreach ((string path, FieldSpec row) in EveryRow().Where(x => HasUnit(x.Row.Kind)))
        {
            FieldUnit? expected = null;
            foreach ((string suffix, FieldUnit unit) in _suffixUnits)
            {
                if (row.Name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    expected = unit;
                }
            }

            foreach ((string name, FieldUnit unit) in _unsuffixed)
            {
                if (row.Name == name)
                {
                    Assert.True(expected is null, path + " is listed unsuffixed but carries a unit suffix");
                    expected = unit;
                }
            }

            Assert.True(expected is not null, path + " has no unit suffix and is not in the unsuffixed list");
            Assert.True(expected == row.Unit, path + " declares " + row.Unit + ", its name says " + expected);
            checkedRows++;
        }

        Assert.True(checkedRows >= 25, "expected to cover every numeric row, covered " + checkedRows);
    }

    [Fact]
    public void DurationRows_AreExactlyTheSecondsRows_AndNoOtherRowIsADuration()
    {
        foreach ((string path, FieldSpec row) in EveryRow())
        {
            Assert.Equal(row.Name.EndsWith("Seconds", StringComparison.Ordinal), row.IsDuration);
            if (row.IsDuration)
            {
                Assert.True(row.Unit is FieldUnit.Seconds or FieldUnit.Ms, path);
            }
        }
    }

    // ------------------------------------------------------------ defaults

    // No exceptions to "defaults are neutral" (CD10 owner ruling 2026-10-21): every default is in {any, 0, false, none, 1000}.
    [Fact]
    public void EveryDefault_IsNeutral_WithNoExceptions()
    {
        List<string> exceptions = new();
        foreach ((string path, FieldSpec row) in EveryRow())
        {
            if (row.Default is long d)
            {
                bool neutral = d == 0 || (d == 1000 && row.Kind == FieldKind.Number);
                if (!neutral)
                {
                    exceptions.Add(path + "=" + d);
                }
            }

            if (row.DefaultSpelling is string s && s is not ("none" or "any"))
            {
                exceptions.Add(path + "=" + s);
            }
        }

        Assert.Empty(exceptions);
    }

    [Fact]
    public void RequiredRows_CarryNoDefault_AndTheDefaultsAreWhatCd10States()
    {
        foreach ((string path, FieldSpec row) in EveryRow().Where(x => x.Row.Required))
        {
            Assert.True(row.Default is null && row.DefaultSpelling is null, path);
        }

        FieldSpec[] carry = { ItemDefFieldSpec.Rows[8], MailKindDefFieldSpec.Rows[6] };
        foreach (FieldSpec row in carry)
        {
            Assert.Equal("carryByHand", row.Name);
            Assert.Equal(0L, row.Default);
        }

        Assert.Equal(0L, ContainerDefFieldSpec.Rows.Single(r => r.Name == "acceptsInsert").Default);
        Assert.Equal(0L, ContainerDefFieldSpec.Rows.Single(r => r.Name == "handCarried").Default);
        Assert.True(ContainerDefFieldSpec.Rows.Single(r => r.Name == "view").Required);   // required, no default (OQ-018-2)
        Assert.Equal("none", ContainerDefFieldSpec.Rows.Single(r => r.Name == "beltAccess").DefaultSpelling);
        Assert.Equal("none", ItemDefFieldSpec.Rows.Single(r => r.Name == "equipSlot").DefaultSpelling);
        Assert.Null(ItemDefFieldSpec.Rows.Single(r => r.Name == "buyPrice").Default);   // absent = no buy price
    }

    // ------------------------------------------------------------ enum sets, reference targets, variants

    private static bool KnownSpelling(string set, string spelling)
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(spelling);
        return set switch
        {
            "ItemCategory" => ItemCategorySpelling.TryParse(utf8, out _),
            "WeightClass" => WeightClassSpelling.TryParse(utf8, out _),
            "EquipSlot" => EquipSlotSpelling.TryParse(utf8, out _),
            "ContainerLayout" => ContainerLayoutSpelling.TryParse(utf8, out _),
            "ContainerView" => ContainerViewSpelling.TryParse(utf8, out _),
            "BeltAccess" => BeltAccessSpelling.TryParse(utf8, out _),
            _ => false,
        };
    }

    private static readonly string[] _enumSets = { "ItemCategory", "WeightClass", "EquipSlot", "ContainerLayout", "ContainerView", "BeltAccess" };

    [Fact]
    public void EveryEnumRow_NamesAKnownSet_AndItsDefaultSpellingParses()
    {
        int enums = 0;
        foreach ((string path, FieldSpec row) in EveryRow().Where(x => x.Row.Kind is FieldKind.Enum or FieldKind.EnumList))
        {
            Assert.Contains(row.EnumSet, _enumSets);
            Assert.True(row.DefaultSpelling is null || KnownSpelling(row.EnumSet!, row.DefaultSpelling), path);
            enums++;
        }

        Assert.Equal(8, enums);   // category, weightClass x2, equipSlot, layout, view, beltAccess, allowedCategories
    }

    [Fact]
    public void EveryReferenceRow_NamesAKnownDefType()
    {
        string[] defTypes = { nameof(ItemDef), nameof(ContainerDef), nameof(MailKindDef), nameof(DestinationTypeDef), nameof(MailMixDef) };
        foreach ((string path, FieldSpec row) in EveryRow().Where(x => x.Row.Kind is FieldKind.Reference or FieldKind.RefList or FieldKind.RefMap))
        {
            Assert.Contains(row.RefTarget, defTypes);
        }
    }

    [Fact]
    public void ContainerVariants_NameLayoutSpellings_AndCoverEveryLayoutExactlyOnce()
    {
        FieldSpec grid = ContainerDefFieldSpec.Rows.Single(r => r.Name == "grid");
        FieldSpec slots = ContainerDefFieldSpec.Rows.Single(r => r.Name == "reservedSlots");
        foreach (FieldSpec row in new[] { grid, slots })
        {
            Assert.NotNull(row.Variant);
            Assert.Equal("layout", row.Variant!.On);
            Assert.False(row.Required);
            foreach (string spelling in row.Variant.RequiredFor.Concat(row.Variant.ForbiddenFor))
            {
                Assert.True(KnownSpelling("ContainerLayout", spelling), spelling);
            }
        }

        // grid: required for grid and slots, forbidden for pile. reservedSlots: slots only (forbidden for grid and pile).
        Assert.Equal(new[] { "grid", "slots" }, grid.Variant!.RequiredFor.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "pile" }, grid.Variant.ForbiddenFor.ToArray());
        Assert.Empty(slots.Variant!.RequiredFor);
        Assert.Equal(new[] { "grid", "pile" }, slots.Variant.ForbiddenFor.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ListRows_GridIsFixedLengthTwo_OthersAnyLength()
    {
        foreach ((string path, FieldSpec row) in EveryRow().Where(x => x.Row.Kind is FieldKind.IntList or FieldKind.TextList or FieldKind.EnumList or FieldKind.RefList or FieldKind.ObjectList))
        {
            Assert.Equal(row.Name == "grid" ? 2 : 0, row.FixedLength);
        }
    }

    [Fact]
    public void FieldKindAndUnit_Appends_HaveExplicitValues()
    {
        Assert.Equal(6, (int)FieldKind.IntList);
        Assert.Equal(7, (int)FieldKind.TextList);
        Assert.Equal(8, (int)FieldKind.EnumList);
        Assert.Equal(9, (int)FieldKind.RefList);
        Assert.Equal(10, (int)FieldKind.Object);
        Assert.Equal(11, (int)FieldKind.ObjectList);
        Assert.Equal(12, (int)FieldKind.RefMap);
        Assert.Equal(12, (int)FieldUnit.PerSecond);
    }
}
