using System.Collections.Immutable;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Hashing;
using PerformativeMail.Sim.Content.Parsing;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;
using J = PerformativeMail.Sim.Tests.Unit.Content.SchemaJson;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 018 (TR-content-006/007/011/012; GDD CDD-ID-1, CDD-PARSE-2 unknown-field half, CDD-SCHEMA-3/4/6,
/// CD21 build): the stage 2 schema walk (<see cref="SchemaWalker"/>), the stage 3 build (<see cref="Builder"/>) and the hand-off to
/// <see cref="ContentPipeline"/>. The per-clause rule fixtures are in <see cref="SchemaRuleFixtures"/>.
/// </summary>
public class SchemaStageTests
{
    private static ContentDiagnostic Single(SchemaResult r, string ruleId)
    {
        ContentDiagnostic d = Assert.Single(r.Diagnostics);
        Assert.Equal(ruleId, d.RuleId);
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.False(r.Ok);
        return d;
    }

    private static ContentBuildResult Load(ImmutableArray<SchemaResult> files) =>
        ContentPipeline.Build(ContentFixtures.MinimalValid().BuildBalance(), Builder.Build(files), RuleRegistry.CreateDefault());

    // ------------------------------------------------------------ CDD-ID-1

    [Theory]
    [Trait("Id", "CDD-ID-1")]
    [InlineData("Letter", 1, 1)]
    [InlineData("1abc", 1, 1)]
    [InlineData("a", 49, 1)]
    [InlineData("ab", 1, 0)]
    [InlineData("a", 48, 0)]
    public void IdLint_GivesOneCvGen02_ForEachInvalidId(string part, int repeat, int expected)
    {
        string id = string.Concat(Enumerable.Repeat(part, repeat));

        SchemaResult r = J.Walk(DefType.Item, J.File(J.Plank(("id", "\"" + id + "\""))));

        Assert.Equal(expected, r.Diagnostics.Length);
        Assert.All(r.Diagnostics, d => Assert.Equal(GeneralRuleIds.IdFormat, d.RuleId));
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void DuplicateIdInTwoFiles_IsOneCvGen01NamingBothPathLines_AndNeitherDefIsKept()
    {
        SchemaResult a = J.Walk(DefType.Item, J.File(J.Plank()), "items/a.jsonc");
        SchemaResult b = J.Walk(DefType.Item, J.File(J.Plank(("id", "\"fiber\"")), J.Plank()), "items/b.jsonc");

        BuiltDefs built = Builder.Build(new[] { a, b });

        ContentDiagnostic d = Assert.Single(built.Diagnostics);
        Assert.Equal(GeneralRuleIds.IdsAndReferences, d.RuleId);
        Assert.Equal(new DefRef(DefType.Item, "plank"), d.Def);
        Assert.Contains("items/a.jsonc:2 and items/b.jsonc:3", d.Value, StringComparison.Ordinal);
        Assert.Equal(new[] { "fiber" }, built.Items.Select(i => i.Id));
        Assert.Contains(built.Failed, f => f.Input == ContentInputKeys.Items && f.Def.IsNone);
    }

    [Fact]
    [Trait("Id", "CDD-ID-1")]
    public void TheSameIdInTwoDefTypes_Passes()
    {
        BuiltDefs built = Builder.Build(new[]
        {
            J.Walk(DefType.Item, J.File(J.Plank())),
            J.Walk(DefType.Container, J.File(J.Cursor(("id", "\"plank\"")))),
        });

        Assert.Empty(built.Diagnostics);
        Assert.Equal("plank", Assert.Single(built.Items).Id);
        Assert.Equal("plank", Assert.Single(built.Containers).Id);
    }

    // ------------------------------------------------------------ CDD-PARSE-2 (unknown-field half), CDD-SCHEMA-6

    [Theory]
    [Trait("Id", "CDD-PARSE-2")]
    [Trait("Id", "CDD-SCHEMA-6")]
    [InlineData("maxStak", "did you mean 'maxStack'?")]
    [InlineData("standardIssue", "one of: id, name, category,")]
    public void UnknownField_IsCvGen03AtTheFieldsOwnLineAndColumn_WithTheSuggestion(string key, string allowed)
    {
        string text = J.File(J.Plank((key, "1")));

        ContentDiagnostic d = Single(J.Walk(DefType.Item, text), GeneralRuleIds.UnknownField);

        string line2 = text.Split('\n')[1];
        Assert.Equal((2, line2.IndexOf("\"" + key + "\"", StringComparison.Ordinal) + 1), (d.Location.Line, d.Location.Column));
        Assert.Equal(key, d.Value);
        Assert.StartsWith(allowed, d.Allowed, StringComparison.Ordinal);
        Assert.Equal(new DefRef(DefType.Item, "plank"), d.Def);
    }

    [Fact]
    [Trait("Id", "CDD-PARSE-2")]
    public void SchemaAndSuppress_Pass_AndSuppressIsDiagnosticMetadata_NeverHashed()
    {
        string plain = J.File(J.Plank());
        string reserved = J.File(J.Plank(("$schema", "\"../schema/item.json\""), ("suppress", "[{\"rule\": \"CV-GRD-06\", \"why\": \"fixture\"}]")));

        SchemaResult r = J.Walk(DefType.Item, reserved);
        ContentSet? withKeys = Load(J.MinimalFiles((DefType.Item, reserved))).Set;
        ContentSet? without = Load(J.MinimalFiles((DefType.Item, plain))).Set;

        Assert.True(r.Ok);
        Assert.Empty(r.Diagnostics);
        ParsedDef def = Assert.Single(r.Defs);
        SuppressEntry entry = Assert.Single(def.Suppress);
        Assert.Equal(("CV-GRD-06", "fixture"), (entry.Rule, entry.Why));
        Assert.DoesNotContain(def.Fields, f => f.Name is SchemaWalker.SuppressKey or SchemaWalker.SchemaKey);
        Assert.Equal(ContentHash.Compute(without!), ContentHash.Compute(withKeys!));
    }

    // ------------------------------------------------------------ CDD-SCHEMA-3, Neutral defaults (view)

    [Theory]
    [Trait("Id", "CDD-SCHEMA-3")]
    [InlineData(DefType.Item, "weightClass")]
    [InlineData(DefType.Container, "view")]
    [InlineData(DefType.Container, "grid")]   // variant-required for layout slots
    public void MissingRequiredField_IsExactlyOneCvGen05AtTheDefsBrace(DefType type, string field)
    {
        string def = type == DefType.Item ? J.Plank((field, null)) : J.Cursor((field, null));

        ContentDiagnostic d = Single(J.Walk(type, J.File(def)), GeneralRuleIds.RequiredFieldMissing);

        Assert.Equal(field, d.Value);
        Assert.Equal((2, 3), (d.Location.Line, d.Location.Column));
    }

    [Fact]
    [Trait("Id", "CDD-SCHEMA-3")]
    [Trait("Id", "CD21")]
    public void MissingWeightClass_IsTheOnlyFinding_TheItemTypeIsNotBuilt_AndCvThr08IsSkippedAndCounted()
    {
        BuiltDefs built = Builder.Build(J.MinimalFiles((DefType.Item, J.File(J.Plank(("weightClass", null))))));
        ContentBuildResult r = ContentPipeline.Build(ContentFixtures.MinimalValid().BuildBalance(), built, RuleRegistry.CreateDefault());

        Assert.Empty(built.Items);                     // stage 3 is gated per type ...
        Assert.Single(built.Containers);               // ... the clean types are still built
        Assert.Equal(new[] { GeneralRuleIds.RequiredFieldMissing }, r.Validation.Diagnostics.Select(d => d.RuleId));
        Assert.Contains(r.Validation.Skipped, s => s.RuleId == ThrowingRuleIds.WeightClasses && s.BlockingInput == ContentInputKeys.Items);
        Assert.Null(r.Set);
    }

    [Theory]
    [Trait("Id", "CDD-SCHEMA-3")]
    [InlineData(null, null)]
    [InlineData("[\"mail\"]", null)]
    [InlineData("[]", GridRuleIds.EmptyAllowedCategories)]
    public void AllowedCategories_OmittedPasses_AndEmptyIsCvGrd03AtStage2(string? value, string? rule)
    {
        SchemaResult r = J.Walk(DefType.Container, J.File(J.Cursor(("allowedCategories", value))));

        Assert.Equal(rule is null ? Array.Empty<string>() : new[] { rule }, r.Diagnostics.Select(d => d.RuleId));
        Assert.Equal(rule is not null, Assert.Single(r.Defs).Failed);   // a failed def never reaches stage 4
    }

    // ------------------------------------------------------------ CDD-SCHEMA-4

    [Theory]
    [Trait("Id", "CDD-SCHEMA-4")]
    [InlineData("\"5\"", 1)]
    [InlineData("\"Light\"", 1)]
    [InlineData("\"LIGHT\"", 1)]
    [InlineData("5", 1)]
    [InlineData("\"light\"", 0)]
    public void WeightClass_IsByteExact_AnythingElseIsCvGen08(string json, int expected)
    {
        SchemaResult r = J.Walk(DefType.Item, J.File(J.Plank(("weightClass", json))));

        Assert.Equal(expected, r.Diagnostics.Length);
        Assert.All(r.Diagnostics, d => Assert.Equal(GeneralRuleIds.EnumSpelling, d.RuleId));
    }

    // ------------------------------------------------------------ CDD-SCHEMA-6 (variants and widths)

    [Fact]
    [Trait("Id", "CDD-SCHEMA-6")]
    public void PileContainerWithGrid_IsCvGen14AtTheGridKey()
    {
        string text = J.File(J.Cursor(("layout", "\"pile\"")));

        ContentDiagnostic d = Single(J.Walk(DefType.Container, text), GeneralRuleIds.VariantField);

        Assert.Equal(text.Split('\n')[1].IndexOf("\"grid\"", StringComparison.Ordinal) + 1, d.Location.Column);
    }

    [Theory]
    [Trait("Id", "CDD-SCHEMA-6")]
    [Trait("Id", "TD-075")]
    [InlineData("int", 65_535, false)]
    [InlineData("int", 65_536, true)]
    [InlineData("list", SchemaWalker.MaxListLength, false)]
    [InlineData("list", SchemaWalker.MaxListLength + 1, true)]
    [InlineData("string", SchemaWalker.MaxStringBytes, false)]
    [InlineData("string", SchemaWalker.MaxStringBytes + 1, true)]
    [InlineData("utf8", SchemaWalker.MaxStringBytes, false)]       // 32,767 x 'é' + 'a': bytes, not chars
    [InlineData("utf8", SchemaWalker.MaxStringBytes + 1, true)]    // 32,768 x 'é'
    public void OverStorageWidth_IsCvGen12AtStage2_SoTheEncoderNeverThrowsForLoadedContent(string what, int size, bool fails)
    {
        string text = what switch
        {
            "int" => J.File(J.Plank(("maxStack", size.ToString(System.Globalization.CultureInfo.InvariantCulture)))),
            "list" => SchemaRuleFixtures.Tags(size),
            "utf8" => J.File(J.Plank(("name", "\"" + new string('é', size / 2) + (size % 2 == 1 ? "a" : "") + "\""))),
            _ => SchemaRuleFixtures.Name(size),
        };

        SchemaResult r = J.Walk(DefType.Item, text);

        Assert.Equal(fails ? new[] { GeneralRuleIds.ValueOutOfRange } : Array.Empty<string>(), r.Diagnostics.Select(d => d.RuleId));
        if (!fails)
        {
            ContentSet set = Assert.IsType<ContentSet>(Load(J.MinimalFiles((DefType.Item, text))).Set);
            Assert.Null(Record.Exception(() => ContentHash.Compute(set)));   // the edge runs the canonical encoder without throwing
        }
    }

    // ------------------------------------------------------------ CD21 (build)

    [Fact]
    [Trait("Id", "CD21")]
    public void Build_PreassignsOrdinalIndices_ConvertsSecondsToMs_AndAppliesTheDefaults_IntoAValidSet()
    {
        BuiltDefs built = Builder.Build(J.MinimalFiles());
        ContentBuildResult r = ContentPipeline.Build(ContentFixtures.MinimalValid().BuildBalance(), built, RuleRegistry.CreateDefault());

        Assert.Empty(r.Validation.Diagnostics);
        Assert.NotNull(r.Set);
        Assert.Equal(new[] { "letter", "postcard" }, built.MailKinds.Select(k => k.Id));   // authored postcard first; ordinal order
        DestinationTypeDef house = Assert.Single(built.DestinationTypes);
        Assert.Equal(new[] { DefIndex.FromValue(1), DefIndex.FromValue(0) }, house.Accepts);   // ["postcard", "letter"]
        Assert.Equal((2_000, 400), (house.InsertRateMilliPerSec, house.ManualInsertHoldMs));
        Assert.Equal(new CatchVolume(1_000, 800, 1_000, 1_000, 0), house.CatchVolume);
        MailMixDef mix = Assert.Single(built.MailMixes);
        Assert.Equal((15_000, 3_000, 9_000, 1_600), (mix.BatchIntervalMs, mix.BatchJitterMs, mix.TailGuardMs, mix.SpawnOverheadPm));
        Assert.Equal(new[] { new MailShare(DefIndex.FromValue(1), 400), new MailShare(DefIndex.FromValue(0), 600) }, Assert.Single(mix.Shifts).Shares);
        MailKindDef letter = built.MailKinds[0];
        Assert.Equal((500, 5_000, 0, DefIndex.None), (letter.BeltLengthMm, letter.ComplaintOnMisdeliveryMilliPoints, letter.DeadlineOffsetShifts, letter.FallbackKind));
        ItemDef plank = Assert.Single(built.Items);
        Assert.Equal((0L, false, EquipSlot.None, 0), (plank.SellPriceCents, plank.HasBuyPrice, plank.EquipSlot, plank.Tags.Length));
        ContainerDef cursor = Assert.Single(built.Containers);
        Assert.Equal((BeltAccess.None, false, ContainerView.Auto), (cursor.BeltAccess, cursor.HasAllowedCategories, cursor.View));
    }

    [Theory]
    [Trait("Id", "CD21")]
    [InlineData("empty", null)]                               // `[]` passes stage 2 with no defs
    [InlineData("midWalk", GeneralRuleIds.ValueOutOfRange)]   // a def failing mid-walk leaks nothing into stage 3
    [InlineData("duplicate", GeneralRuleIds.EnumSpelling)]    // Approved by Evan 2026-10-28 6:09 AM PT (TD-176): no CV-GEN-01 until the copy is clean
    public void Build_RunsForATypeOnlyWhenAllItsFilesAreClean(string what, string? rule)
    {
        SchemaResult[] files = what switch
        {
            "empty" => new[] { J.Walk(DefType.Item, "[]") },
            "midWalk" => new[] { J.Walk(DefType.Item, J.File(J.Plank(("maxStack", "65536")), J.Plank(("id", "\"fiber\"")))) },
            _ => new[] { J.Walk(DefType.Item, J.File(J.Plank()), "items/a.jsonc"), J.Walk(DefType.Item, J.File(J.Plank(("weightClass", "\"LIGHT\""))), "items/b.jsonc") },
        };

        BuiltDefs built = Builder.Build(files);

        Assert.Equal(rule is null ? Array.Empty<string>() : new[] { rule }, built.Diagnostics.Select(d => d.RuleId));
        Assert.Empty(built.Items);
        Assert.Equal(rule is not null, built.Failed.Any(f => f.Input == ContentInputKeys.Items));
    }

    [Theory]
    [Trait("Id", "CD21")]
    [InlineData(0, false)]   // exactly MaxDefsPerType builds
    [InlineData(1, true)]
    public void Build_TooManyDefsInOneType_IsCvGen09_AndTheTypeIsLeftEmpty(int over, bool tooMany)
    {
        ImmutableArray<ParsedDef> defs = Enumerable.Range(0, DefIdAssigner.MaxDefsPerType + over)
            .Select(i => new ParsedDef(DefType.MailMix, "m" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), new SourceLocation("mail/mix.jsonc", i + 2, 3), ImmutableArray<ParsedField>.Empty, ImmutableArray<SuppressEntry>.Empty, false))
            .ToImmutableArray();

        BuiltDefs built = Builder.Build(new[] { new SchemaResult("mail/mix.jsonc", DefType.MailMix, defs, ImmutableArray<ContentDiagnostic>.Empty) });

        Assert.Equal(tooMany ? new[] { GeneralRuleIds.TooManyDefs } : Array.Empty<string>(), built.Diagnostics.Select(d => d.RuleId));
        Assert.Equal(tooMany ? 0 : DefIdAssigner.MaxDefsPerType, built.MailMixes.Length);
        Assert.Equal(tooMany, built.Failed.Any(f => f.Input == ContentInputKeys.MailMixes));
    }

    [Theory]
    [Trait("Id", "CD21")]
    [InlineData("[\"nowhere\"]", "'nowhere'")]
    [InlineData("[\"letter\"]", "exists only as MailKind")]
    public void Build_AMissingOrWrongTypeReference_IsCvGen01AtStage4(string acceptedBy, string message)
    {
        string kinds = J.File(J.Kind(("id", "\"postcard\""), ("name", "\"mail.postcard\"")), J.Kind(("acceptedBy", acceptedBy)));
        BuiltDefs built = Builder.Build(J.MinimalFiles((DefType.MailKind, kinds)));

        ContentBuildResult r = ContentPipeline.Build(ContentFixtures.MinimalValid().BuildBalance(), built, RuleRegistry.CreateDefault());

        Assert.Empty(built.Diagnostics);   // stages 1 to 3 are clean: the reference is resolved at stage 4
        Assert.Contains(r.Validation.Diagnostics, d =>
            d.RuleId == GeneralRuleIds.IdsAndReferences && d.Def == new DefRef(DefType.MailKind, "letter") &&
            d.Value.Contains("acceptedBy[0]", StringComparison.Ordinal) && (d.Value + d.Allowed).Contains(message, StringComparison.Ordinal));
        Assert.Null(r.Set);
    }

    // ------------------------------------------------------------ TD-090: SelfCheck at loader construction

    [Fact]
    [Trait("Id", "TD-090")]
    public void SchemaStage_RunsTheTableSelfCheckWhenConstructed()
    {
        ImmutableArray<SchemaTable> bad = ImmutableArray.Create(
            new SchemaTable(DefType.Item, "BadDef", ImmutableArray.Create(FieldSpec.Id(), FieldSpec.Number("n", default))));

        _ = new SchemaStage();
        InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => new SchemaStage(bad));

        Assert.Contains("BadDef.n " + GeneralRuleIds.NumberFormat, e.Message, StringComparison.Ordinal);
    }
}
