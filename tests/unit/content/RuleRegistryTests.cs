using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 003 (TR-content-022 mechanism subset; GDD CDD-RULE-1 mechanism half, CDD-RULE-2,
/// CDD-VAL-1 meta-test, CDD-LOAD-3 skip accounting, CD3.5 diagnostics): diagnostics and their sort order, the rule
/// registry, the severity policy, skip accounting and the fixture meta-test. Rules and inputs are test-only
/// (<see cref="TestRules"/>, <see cref="TestInput"/>); fixtures live in <see cref="RuleFixtureCatalogue"/>.
/// Nothing here is random, timed or read from disk.
/// </summary>
public class RuleRegistryTests
{
    private static ContentDiagnostic Diag(string path, int line, int column, string rule, string value = "v") =>
        new(rule, RuleSeverity.Error, new SourceLocation(path, line, column), DefRef.None, value, "a");

    private static ValidationResult Run(params TestDef[] defs) =>
        ContentValidationRunner.Run(TestRules.RunnableRegistry(), TestInput.Of(defs));

    private static List<string> Ids(ValidationResult r)
    {
        var ids = new List<string>();
        foreach (ContentDiagnostic d in r.Diagnostics)
        {
            ids.Add(d.RuleId);
        }

        return ids;
    }

    // ------------------------------------------------------------ AC-1: diagnostics

    [Fact]
    [Trait("Id", "CD3.5")]
    public void Diagnostic_CarriesRuleSeverityLocationValueAndAllowed()
    {
        // Arrange / Act
        var d = new ContentDiagnostic("CV-THR-12", RuleSeverity.Error, new SourceLocation("balance/throw.jsonc", 7, 14),
            new DefRef(DefType.Item, "letter"), "501", "100..500", "throw.catchTapMs");

        // Assert
        Assert.Equal("CV-THR-12", d.RuleId);
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.Equal("balance/throw.jsonc:7:14", d.Location.ToString());
        Assert.Equal("501", d.Value);
        Assert.Equal("100..500", d.Allowed);
        Assert.Equal("Item:letter", d.Def.ToString());
    }

    [Fact]
    [Trait("Id", "CD3.5")]
    public void SourceLocation_BuiltIn_IsZeroZero()
    {
        SourceLocation loc = SourceLocation.BuiltIn("builtin/items/letter");

        Assert.True(loc.IsBuiltIn);
        Assert.Equal(0, loc.Line);
        Assert.Equal(0, loc.Column);
        Assert.Equal("builtin/items/letter:0:0", loc.ToString());
    }

    [Fact]
    [Trait("Id", "CD3.5")]
    public void SourceLocation_RejectsHalfZeroOrNegativePositions()
    {
        Assert.Throws<ArgumentException>(() => new SourceLocation("a", 0, 1));
        Assert.Throws<ArgumentException>(() => new SourceLocation("a", 1, 0));
        Assert.Throws<ArgumentException>(() => new SourceLocation("a", -1, 1));
    }

    [Fact]
    [Trait("Id", "CD3.5")]
    public void SourceLocation_FromUtf8Offset_CountsLfLinesAndUtf8ByteColumns()
    {
        // Line 1: 'a', tab, e-acute (2 bytes), 'b', CR, LF.  Line 2: 'x' 'y'.
        byte[] text = Encoding.UTF8.GetBytes("a\téb\r\nxy");

        Assert.Equal("f:1:1", SourceLocation.FromUtf8Offset("f", text, 0).ToString());   // 'a'
        Assert.Equal("f:1:2", SourceLocation.FromUtf8Offset("f", text, 1).ToString());   // tab counts 1
        Assert.Equal("f:1:3", SourceLocation.FromUtf8Offset("f", text, 2).ToString());   // e-acute starts
        Assert.Equal("f:1:5", SourceLocation.FromUtf8Offset("f", text, 4).ToString());   // 'b' after a 2-byte char
        Assert.Equal("f:1:6", SourceLocation.FromUtf8Offset("f", text, 5).ToString());   // the CR is a column of line 1
        Assert.Equal("f:2:1", SourceLocation.FromUtf8Offset("f", text, 7).ToString());   // after LF
        Assert.Equal("f:2:3", SourceLocation.FromUtf8Offset("f", text, text.Length).ToString());   // end of file
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceLocation.FromUtf8Offset("f", text, text.Length + 1));
    }

    [Fact]
    [Trait("Id", "CD24")]
    public void DiagnosticSort_OrdersByPathOrdinalLineColumnRuleId()
    {
        // Arrange: a fixed shuffle. Ordinal: "B.jsonc" < "a.jsonc" (upper case first); line and column are numeric.
        ContentDiagnostic[] shuffled =
        {
            Diag("a.jsonc", 10, 1, "CV-AAA-01"),
            Diag("a.jsonc", 2, 9, "CV-AAA-01"),
            Diag("B.jsonc", 5, 5, "CV-ZZZ-01"),
            Diag("a.jsonc", 2, 3, "CV-BBB-02"),
            Diag("a.jsonc", 2, 3, "CV-BBB-01"),
            Diag("a/b.jsonc", 1, 1, "CV-AAA-01"),
        };

        // Act
        ImmutableArray<ContentDiagnostic> sorted = DiagnosticOrder.Sorted(ImmutableArray.Create(shuffled));

        // Assert
        string[] keys = new string[sorted.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = sorted[i].Location + " " + sorted[i].RuleId;
        }

        Assert.Equal(
            new[]
            {
                "B.jsonc:5:5 CV-ZZZ-01",
                "a.jsonc:2:3 CV-BBB-01",
                "a.jsonc:2:3 CV-BBB-02",
                "a.jsonc:2:9 CV-AAA-01",
                "a.jsonc:10:1 CV-AAA-01",
                "a/b.jsonc:1:1 CV-AAA-01",
            },
            keys);
    }

    [Fact]
    [Trait("Id", "CD24")]
    public void DiagnosticSort_IsIndependentOfInputOrder()
    {
        ContentDiagnostic[] forward =
        {
            Diag("x", 1, 1, "CV-AAA-01", "p"), Diag("x", 1, 1, "CV-AAA-01", "q"), Diag("y", 1, 1, "CV-AAA-01"), Diag("a", 3, 1, "CV-AAA-02"),
        };
        ContentDiagnostic[] reversed = (ContentDiagnostic[])forward.Clone();
        Array.Reverse(reversed);

        ImmutableArray<ContentDiagnostic> a = DiagnosticOrder.Sorted(ImmutableArray.Create(forward));
        ImmutableArray<ContentDiagnostic> b = DiagnosticOrder.Sorted(ImmutableArray.Create(reversed));

        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i], b[i]);
        }
    }

    [Fact]
    [Trait("Id", "CD24")]
    public void RunnerOutput_IsSortedAndRepeatable()
    {
        // Three defs trip rules in different stages; the result must be sorted and identical on a second run.
        TestDef[] defs = { TestInput.Def("zeta", 11), TestInput.Def("alpha", 13), TestInput.Def("mid", 1) };
        RuleRegistry registry = TestRules.RunnableRegistry();

        ValidationResult first = ContentValidationRunner.Run(registry, TestInput.Of(defs));
        ValidationResult second = ContentValidationRunner.Run(registry, TestInput.Of(defs));

        var comparer = new DiagnosticComparer();
        for (int i = 1; i < first.Diagnostics.Length; i++)
        {
            Assert.True(comparer.Compare(first.Diagnostics[i - 1], first.Diagnostics[i]) <= 0);
        }

        Assert.Equal(first.Diagnostics.Length, second.Diagnostics.Length);
        for (int i = 0; i < first.Diagnostics.Length; i++)
        {
            Assert.Equal(first.Diagnostics[i].Format(registry), second.Diagnostics[i].Format(registry));
        }
    }

    // ------------------------------------------------------------ AC-2: registry, format, aliases, retired

    /// <summary>A registry shaped like the catalogue rows the aliases are specified against (no duplicate of a future real id).</summary>
    private static RuleRegistry CatalogueShapedRegistry() => RuleRegistry.Compose(
        General.Rules,
        ImmutableArray.Create(
            RuleSpec.Emitted("CV-THR-12", "throwing-catching", "THROW-CV-12", RuleSeverity.Error, ValidationStage.Schema),
            RuleSpec.Emitted("CV-PLR-07", "player-character-movement", "PC rule 7", RuleSeverity.Error, ValidationStage.Schema),
            RuleSpec.Emitted("CV-PLR-06", "player-character-movement", "PC rule 6", RuleSeverity.Error, ValidationStage.Schema,
                alsoKnownAs: ImmutableArray.Create("Throwing rule 14")),
            RuleSpec.Retire("CV-THR-14", "throwing-catching", "THROW-CV-14", "CV-PLR-06")));

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Registry_ExposesIdOwnerAliasSeverityStageAndInputs()
    {
        // Arrange / Act
        RuleSpec rule = TestRules.Registry().Find(TestRules.Max)!;

        // Assert
        Assert.Equal("CV-TST-01", rule.Id);
        Assert.Equal("test-only", rule.Owner);
        Assert.Equal("TST-RULE-1", rule.Alias);
        Assert.Equal(RuleSeverity.Error, rule.Severity);
        Assert.Equal(ValidationStage.References, rule.Stage);
        Assert.Equal(new[] { "tst.value" }, rule.Inputs);
        Assert.Equal(RuleStatus.Active, rule.Status);
        Assert.Equal(RuleSeverity.Warning, TestRules.Registry().Find(TestRules.Min)!.Severity);
        Assert.Equal(ValidationStage.WholeSet, TestRules.Registry().Find(TestRules.Seven)!.Stage);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Message_PrintsIdAndAlias()
    {
        RuleRegistry registry = CatalogueShapedRegistry();
        var d = new ContentDiagnostic("CV-THR-12", RuleSeverity.Error, new SourceLocation("balance/throw.jsonc", 7, 14),
            DefRef.None, "501", "100..500");

        Assert.Equal("CV-THR-12 (THROW-CV-12)", registry.FormatId("CV-THR-12"));
        Assert.Equal("CV-PLR-07 (PC rule 7)", registry.FormatId("CV-PLR-07"));
        Assert.Equal(
            "balance/throw.jsonc:7:14: error CV-THR-12 (THROW-CV-12): value 501, allowed 100..500", d.Format(registry));
        Assert.Equal("CV-GEN-05", registry.FormatId("CV-GEN-05"));   // no alias: bare id
        Assert.Equal("CV-NOP-99", registry.FormatId("CV-NOP-99"));   // unknown: bare id
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Alias_Resolves_ToCanonicalId()
    {
        RuleRegistry registry = CatalogueShapedRegistry();

        Assert.True(registry.TryResolve("THROW-CV-12", out RuleSpec? a));
        Assert.Equal("CV-THR-12", a!.Id);
        Assert.True(registry.TryResolve("PC rule 7", out RuleSpec? b));
        Assert.Equal("CV-PLR-07", b!.Id);
        Assert.True(registry.TryResolve("Throwing rule 14", out RuleSpec? c));
        Assert.Equal("CV-PLR-06", c!.Id);
        Assert.True(registry.TryResolve("CV-PLR-07", out RuleSpec? d));   // a canonical id resolves to itself
        Assert.Equal("CV-PLR-07", d!.Id);
        Assert.False(registry.TryResolve("throw-cv-12", out _));         // exact, case-sensitive
        Assert.False(registry.TryResolve("PC rule 99", out _));
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void RetiredRule_StaysListedAsRetired()
    {
        // Arrange: the registry the product ships, and the catalogue-shaped one that also holds the replacement.
        foreach (RuleRegistry registry in new[] { RuleRegistry.CreateDefault(), CatalogueShapedRegistry() })
        {
            RuleSpec retired = registry.Find("CV-THR-14")!;
            Assert.NotNull(retired);
            Assert.Equal(RuleStatus.Retired, retired.Status);
            Assert.Equal("CV-PLR-06", retired.RetiredInto);
            Assert.False(retired.HasCheck);
            Assert.Contains(registry.Rules, r => r.Id == "CV-THR-14");
        }

        Assert.True(CatalogueShapedRegistry().TryResolve("THROW-CV-14", out RuleSpec? byAlias));
        Assert.Equal("CV-THR-14", byAlias!.Id);
        Assert.NotNull(CatalogueShapedRegistry().Find(CatalogueShapedRegistry().Find("CV-THR-14")!.RetiredInto));
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void RetiredRule_NeverFires_AndNeedsNoFixture()
    {
        RuleRegistry registry = CatalogueShapedRegistry();

        ImmutableArray<FixtureProblem> problems = Check(registry, ImmutableArray<RuleFixture>.Empty);

        Assert.DoesNotContain(problems, p => p.RuleId == "CV-THR-14");
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void Registry_RejectsMalformedTables()
    {
        RuleSpec Row(string id, string alias = "", ImmutableArray<string> aka = default) =>
            RuleSpec.Emitted(id, "o", alias, RuleSeverity.Error, ValidationStage.Schema, alsoKnownAs: aka);

        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(Row("CV-AAA-01"), Row("CV-AAA-01"))));
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(Row("cv-aaa-01"))));
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(Row("CV-AAA-1"))));
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(Row("CV-A-01"))));
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(Row("CV-AAAAA-01"))));
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(Row("CV-AAA-01", "X"), Row("CV-AAA-02", "X"))));
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(Row("CV-AAA-01"), Row("CV-AAA-02", "CV-AAA-01"))));
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(
            RuleSpec.Create<TestInput>("CV-AAA-01", "o", "", RuleSeverity.Error, ValidationStage.References, default, (_, _) => { },
                ImmutableArray.Create(new RuleClause("x"), new RuleClause("x"))))));
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(RuleSpec.Retire("CV-AAA-01", "o", "", ""))));
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void DefaultRegistry_IsTheExplicitOwnerLists_InOrder()
    {
        var expected = new List<RuleSpec>();
        expected.AddRange(General.Rules);
        expected.AddRange(Grid.Rules);
        expected.AddRange(Mail.Rules);
        expected.AddRange(Player.Rules);
        expected.AddRange(Throwing.Rules);

        RuleRegistry registry = RuleRegistry.CreateDefault();

        Assert.Equal(expected.Count, registry.Rules.Length);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Id, registry.Rules[i].Id);
        }
    }

    // ------------------------------------------------------------ AC-3: severity policy

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Warning_IsReported_AndTheSetStillBuilds()
    {
        var set = new object();

        ValidationResult r = Run(TestInput.Def("a", 2));   // 2 < 3: CV-TST-02 (Warning)

        Assert.Equal(new[] { TestRules.Min }, Ids(r));
        Assert.Equal(RuleSeverity.Warning, r.Diagnostics[0].Severity);
        Assert.Equal(1, r.WarningCount);
        Assert.Equal(0, r.ErrorCount);
        Assert.True(r.CanBuild);
        Assert.Same(set, r.AcceptedSet(set));
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Suppress_WithRuleAndWhy_SilencesTheWarning()
    {
        ValidationResult r = Run(TestInput.Def("a", 2, TestInput.Suppress(TestRules.Min, "intended for this world")));

        Assert.Empty(r.Diagnostics);
        Assert.Equal(1, r.SuppressedCount);
        Assert.Equal(0, r.UnmatchedSuppressCount);
        Assert.True(r.CanBuild);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Suppress_OnOneDef_DoesNotSilenceTheSameWarningOnAnotherDef()
    {
        ValidationResult r = Run(
            TestInput.Def("a", 2, TestInput.Suppress(TestRules.Min, "intended")),
            TestInput.Def("b", 1));

        Assert.Single(r.Diagnostics);
        Assert.Equal("b", r.Diagnostics[0].Def.Id);
        Assert.Equal(1, r.SuppressedCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Id", "CDD-RULE-1")]
    public void Suppress_WithoutWhy_IsGen05_AndDoesNotSilence(string? why)
    {
        ValidationResult r = Run(TestInput.Def("a", 2, TestInput.Suppress(TestRules.Min, why)));

        Assert.Equal(new[] { TestRules.Min, GeneralRuleIds.SuppressWithoutWhy }, Ids(r));   // sorted by path: the def's path precedes the suppress entry's
        Assert.Equal("CV-GEN-05", r.Diagnostics[1].RuleId);
        Assert.Equal(1, r.ErrorCount);
        Assert.Equal(1, r.WarningCount);
        Assert.False(r.CanBuild);
        Assert.Equal(0, r.SuppressedCount);
        Assert.Equal(0, r.UnmatchedSuppressCount);   // a malformed entry is an error, not an "unmatched" count
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Suppress_WithoutRule_IsGen05()
    {
        ValidationResult r = Run(TestInput.Def("a", 5, TestInput.Suppress(null, "reason")));

        Assert.Equal(new[] { GeneralRuleIds.SuppressWithoutWhy }, Ids(r));
        Assert.False(r.CanBuild);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Suppress_NamingAnErrorRule_IsAnError_AndTheErrorStillFires()
    {
        ValidationResult r = Run(TestInput.Def("a", 11, TestInput.Suppress(TestRules.Max, "wishful")));

        Assert.Contains(GeneralRuleIds.SuppressBadTarget, Ids(r));
        Assert.Contains(TestRules.Max, Ids(r));   // the Error itself is never silenced
        Assert.Equal(0, r.SuppressedCount);
        Assert.False(r.CanBuild);
    }

    [Theory]
    [InlineData("CV-ZZZ-99")]          // unknown id
    [InlineData("TST-RULE-2")]         // an alias, not the canonical id
    [InlineData("CV-GEN-05")]          // an Error row of the framework itself
    [Trait("Id", "CDD-RULE-1")]
    public void Suppress_NamingAnUnknownOrNonWarningRule_IsGen21(string target)
    {
        ValidationResult r = Run(TestInput.Def("a", 5, TestInput.Suppress(target, "because")));

        Assert.Equal(new[] { GeneralRuleIds.SuppressBadTarget }, Ids(r));
        Assert.False(r.CanBuild);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Suppress_NamingARetiredRule_IsGen21()
    {
        RuleRegistry registry = RuleRegistry.Compose(General.Rules.RemoveAll(r => r.HasCheck), Throwing.Rules.RemoveAll(r => r.HasCheck));   // holds retired CV-THR-14
        TestInput input = TestInput.Of(TestInput.Def("a", 5, TestInput.Suppress("CV-THR-14", "because")));

        ValidationResult r = ContentValidationRunner.Run(registry, input);

        Assert.Equal(new[] { GeneralRuleIds.SuppressBadTarget }, Ids(r));
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Suppress_MatchingNothing_IsOnlyCounted()
    {
        ValidationResult r = Run(TestInput.Def("a", 5, TestInput.Suppress(TestRules.Min, "just in case")));   // 5 >= 3, no warning

        Assert.Empty(r.Diagnostics);
        Assert.Equal(1, r.UnmatchedSuppressCount);
        Assert.Equal(0, r.SuppressedCount);
        Assert.True(r.CanBuild);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Error_AlwaysPreventsTheSetFromBeingBuilt()
    {
        var set = new object();

        ValidationResult r = Run(TestInput.Def("a", 11), TestInput.Def("b", 4));

        Assert.Equal(1, r.ErrorCount);
        Assert.False(r.CanBuild);
        Assert.Null(r.AcceptedSet(set));
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Error_WithWarningsSuppressedAroundIt_StillPreventsBuild()
    {
        ValidationResult r = Run(
            TestInput.Def("a", 2, TestInput.Suppress(TestRules.Min, "intended")),   // warning silenced
            TestInput.Def("b", 11));                                                // error remains

        Assert.Equal(new[] { TestRules.Max }, Ids(r));
        Assert.Null(r.AcceptedSet(new object()));
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Runner_RequiresTheFrameworkRowsInTheRegistry()
    {
        RuleRegistry withoutGeneral = RuleRegistry.Compose(TestRules.Table);

        Assert.Throws<InvalidOperationException>(() => ContentValidationRunner.Run(withoutGeneral, TestInput.Of()));
    }

    // ------------------------------------------------------------ AC-4: skip accounting

    [Fact]
    [Trait("Id", "CDD-LOAD-3")]
    public void FailedInput_SkipsLaterRuleForThatDefOnly_AndNamesTheBlockingField()
    {
        // a: 14 is over the CV-TST-01 limit (stage 4, names tst.value) and a multiple of 7; CV-TST-03 (stage 5) would also flag it.
        // b: 7 is within CV-TST-01's limit, so CV-TST-03 runs and fires.
        ValidationResult r = Run(TestInput.Def("a", 14), TestInput.Def("b", 7));

        Assert.Equal(new[] { TestRules.Max, TestRules.Seven }, Ids(r));   // sorted by (path, rule id)
        // Two stage 5 rules read tst.value (CV-TST-03 and the whole-set CV-TST-04); both are skipped for def a only.
        Assert.Equal(2, r.SkippedCount);
        Assert.Equal(new[] { TestRules.Seven, TestRules.Total }, new[] { r.Skipped[0].RuleId, r.Skipped[1].RuleId });
        foreach (SkippedPair skip in r.Skipped)
        {
            Assert.Equal("a", skip.Def.Id);
            Assert.Equal("tst.value", skip.BlockingInput);
            Assert.Equal("builtin/items/a", skip.BlockingFile);
        }
        foreach (ContentDiagnostic d in r.Diagnostics)
        {
            if (d.RuleId == TestRules.Seven)
            {
                Assert.Equal("b", d.Def.Id);
            }
        }
    }

    [Fact]
    [Trait("Id", "CDD-LOAD-3")]
    public void PriorFailureOfAFile_SkipsTheWholeRule_AndNamesTheFile()
    {
        var prior = ImmutableArray.Create(new FailedInput("tst.value", DefRef.None, "items/core.jsonc"));
        var input = new TestInput(ImmutableArray.Create(TestInput.Def("a", 99), TestInput.Def("b", 98)), prior);

        ValidationResult r = ContentValidationRunner.Run(TestRules.RunnableRegistry(), input);

        // Every rule reading tst.value is skipped (Max, Min, Seven, Total, NotThirteen); one pair each, naming the file.
        Assert.Empty(r.Diagnostics);
        Assert.Equal(5, r.SkippedCount);
        foreach (SkippedPair s in r.Skipped)
        {
            Assert.True(s.Def.IsNone);
            Assert.Equal("items/core.jsonc", s.BlockingFile);
            Assert.Equal("tst.value", s.BlockingInput);
        }

        Assert.True(r.CanBuild);   // skipped is not an error; the loader owns the error that caused it
    }

    [Fact]
    [Trait("Id", "CDD-LOAD-3")]
    public void FailureOfAnUnrelatedInput_SkipsNothing()
    {
        var prior = ImmutableArray.Create(new FailedInput("other.key", DefRef.None, "other.jsonc"));
        var input = new TestInput(ImmutableArray.Create(TestInput.Def("a", 2)), prior);   // 2 trips the CV-TST-02 Warning only

        ValidationResult r = ContentValidationRunner.Run(TestRules.RunnableRegistry(), input);

        Assert.Equal(0, r.SkippedCount);
        Assert.Equal(new[] { TestRules.Min }, Ids(r));   // a Warning names no blocking input, so it blocks nothing
    }

    [Fact]
    [Trait("Id", "CDD-LOAD-3")]
    public void SkippedPairs_AreSortedAndCountedOncePerRuleAndDef()
    {
        // Both defs fail CV-TST-01 and CV-TST-05 (13), both name tst.value; CV-TST-03 and the whole-set CV-TST-04 are skipped
        // per def once each even though two failures block each def.
        ValidationResult r = Run(TestInput.Def("b", 13), TestInput.Def("a", 13));

        var seen = new List<string>();
        foreach (SkippedPair s in r.Skipped)
        {
            seen.Add(s.RuleId + "/" + s.Def.Id);
        }

        Assert.Equal(new[] { "CV-TST-03/a", "CV-TST-03/b", "CV-TST-04/a", "CV-TST-04/b" }, seen);
    }

    // ------------------------------------------------------------ AC-5: snapshot and fixture meta-test

    /// <summary>
    /// The permanent rule ids with their frozen severity and status. Literal on purpose: removing or renumbering an
    /// id (or changing a retired rule back to active) must fail here and force a deliberate edit. Rules may be
    /// appended by later stories; this list is extended with them.
    /// </summary>
    private static readonly (string Id, RuleSeverity? Severity, RuleStatus Status)[] _snapshot =
    {
        ("CV-GEN-05", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GEN-01", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GEN-09", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GEN-17", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GEN-18", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GEN-13", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GEN-15", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GEN-20", RuleSeverity.Warning, RuleStatus.Active),
        ("CV-GEN-21", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GRD-01", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GRD-02", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GRD-03", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GRD-04", RuleSeverity.Error, RuleStatus.Active),
        ("CV-GRD-06", RuleSeverity.Warning, RuleStatus.Active),
        ("CV-GRD-07", RuleSeverity.Warning, RuleStatus.Active),
        ("CV-MAIL-01", RuleSeverity.Error, RuleStatus.Active),
        ("CV-MAIL-02", RuleSeverity.Error, RuleStatus.Active),
        ("CV-MAIL-03", RuleSeverity.Error, RuleStatus.Active),
        ("CV-MAIL-04", RuleSeverity.Error, RuleStatus.Active),
        ("CV-MAIL-05", RuleSeverity.Error, RuleStatus.Active),
        ("CV-MAIL-06", RuleSeverity.Error, RuleStatus.Active),
        ("CV-PLR-01", RuleSeverity.Error, RuleStatus.Active),
        ("CV-PLR-02", RuleSeverity.Error, RuleStatus.Active),
        ("CV-PLR-03", RuleSeverity.Error, RuleStatus.Deferred),
        ("CV-PLR-04", RuleSeverity.Error, RuleStatus.Active),
        ("CV-PLR-05", RuleSeverity.Error, RuleStatus.Deferred),
        ("CV-PLR-06", RuleSeverity.Error, RuleStatus.Active),
        ("CV-PLR-07", RuleSeverity.Error, RuleStatus.Deferred),
        ("CV-PLR-08", RuleSeverity.Error, RuleStatus.Active),
        ("CV-PLR-09", RuleSeverity.Error, RuleStatus.Active),
        ("CV-PLR-10", RuleSeverity.Error, RuleStatus.Active),
        ("CV-PLR-11", RuleSeverity.Error, RuleStatus.Active),
        ("CV-PLR-12", RuleSeverity.Error, RuleStatus.Deferred),
        ("CV-PLR-13", RuleSeverity.Error, RuleStatus.Deferred),
        ("CV-THR-01", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-02", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-03", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-04", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-05", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-06", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-07", RuleSeverity.Error, RuleStatus.Deferred),
        ("CV-THR-08", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-09", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-10", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-11", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-12", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-13", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-14", null, RuleStatus.Retired),   // a retired row's severity and stage are placeholders: only its id and status are frozen
        ("CV-THR-15", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-16", RuleSeverity.Error, RuleStatus.Active),
        ("CV-THR-17", RuleSeverity.Error, RuleStatus.Deferred),
        ("CV-THR-18", RuleSeverity.Error, RuleStatus.Active),
    };

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void RuleIdSnapshot_EveryRegisteredIdIsStillPresentWithTheSameSeverityAndStatus()
    {
        RuleRegistry registry = RuleRegistry.CreateDefault();

        foreach ((string id, RuleSeverity? severity, RuleStatus status) in _snapshot)
        {
            RuleSpec? rule = registry.Find(id);
            Assert.True(rule is not null, "rule id " + id + " was removed or renumbered");
            if (severity is not null)
            {
                Assert.Equal(severity.Value, rule!.Severity);
            }

            Assert.Equal(status, rule!.Status);
        }
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void RuleIdSnapshot_ListsEveryRegisteredIdOnce()
    {
        // The reverse direction: a new id must be added to the snapshot (so that it is frozen from then on).
        RuleRegistry registry = RuleRegistry.CreateDefault();

        foreach (RuleSpec rule in registry.Rules)
        {
            int count = 0;
            foreach ((string id, _, _) in _snapshot)
            {
                if (id == rule.Id)
                {
                    count++;
                }
            }

            Assert.True(count == 1, "rule id " + rule.Id + " is registered but not in the snapshot");
        }
    }

    private static ImmutableArray<FixtureProblem> Check(RuleRegistry registry, ImmutableArray<RuleFixture> fixtures) =>
        FixtureChecker.Check(registry, fixtures, RuleFixtureCatalogue.ForwardReferences);

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void FixtureMetaTest_EveryRegisteredClauseHasAFixtureThatTripsIt()
    {
        // The registry is iterated, never counted: a new rule or clause without a fixture fails here.
        RuleRegistry registry = TestRules.Registry();

        ImmutableArray<FixtureProblem> problems = Check(registry, RuleFixtureCatalogue.All());

        Assert.True(
            problems.IsEmpty,
            "fixture problems: " + string.Join("; ", problems.Select(p => p.Kind + " " + p.RuleId + "/" + p.Clause + " " + p.Detail)));
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void FixtureMetaTest_CoversEveryActiveClauseOfTheProductionRegistry()
    {
        // The production rows alone: each active clause has a violation fixture (the production registry lacks the
        // test-only rows, so the test-only fixtures are excluded and must not be reported as unknown).
        RuleRegistry registry = RuleRegistry.CreateDefault();

        ImmutableArray<FixtureProblem> problems = Check(registry, RuleFixtureCatalogue.Real());

        Assert.True(
            problems.IsEmpty,
            "fixture problems: " + string.Join("; ", problems.Select(p => p.Kind + " " + p.RuleId + "/" + p.Clause + " " + p.Detail)));
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void FixtureChecker_ReportsAClauseWithoutAFixture()
    {
        // Arrange: the registry plus a rule nobody wrote a fixture for.
        RuleRegistry registry = RuleRegistry.Compose(
            General.Rules,
            Grid.Rules,
            Mail.Rules,
            PerformativeMail.Sim.Content.Rules.Player.Rules,
            PerformativeMail.Sim.Content.Rules.Throwing.Rules,
            TestRules.Table,
            ImmutableArray.Create(RuleSpec.Emitted("CV-TST-90", "test-only", "", RuleSeverity.Error, ValidationStage.Schema,
                clauses: ImmutableArray.Create(new RuleClause("covered"), new RuleClause("uncovered")))));
        ImmutableArray<RuleFixture> fixtures = RuleFixtureCatalogue.All().Add(
            new RuleFixture("CV-TST-90", "covered", FixtureKind.Violation, OneDiag("CV-TST-90"), ImmutableArray<string>.Empty));

        // Act
        ImmutableArray<FixtureProblem> problems = Check(registry, fixtures);

        // Assert
        FixtureProblem p = Assert.Single(problems);
        Assert.Equal(FixtureProblemKind.MissingViolationFixture, p.Kind);
        Assert.Equal("CV-TST-90", p.RuleId);
        Assert.Equal("uncovered", p.Clause);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void FixtureChecker_ReportsMissingValidEdgeFixtureForAClauseWithALimit()
    {
        ImmutableArray<RuleFixture> withoutEdge = RuleFixtureCatalogue.All().RemoveAll(
            f => f.Kind == FixtureKind.ValidEdge && f.RuleId == TestRules.Max);

        ImmutableArray<FixtureProblem> problems = Check(TestRules.Registry(), withoutEdge);

        FixtureProblem p = Assert.Single(problems);
        Assert.Equal(FixtureProblemKind.MissingValidEdgeFixture, p.Kind);
        Assert.Equal(TestRules.Max, p.RuleId);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void FixtureChecker_ReportsFixturesThatDoNotTripOrOverTrip()
    {
        RuleRegistry registry = TestRules.Registry();
        ImmutableArray<RuleFixture> baseline = RuleFixtureCatalogue.All();

        // A violation that trips nothing.
        ImmutableArray<FixtureProblem> noTrip = Check(registry, Replace(baseline, TestRules.Max, FixtureKind.Violation,
            () => Run(TestInput.Def("a", 5))));
        Assert.Contains(noTrip, p => p.Kind == FixtureProblemKind.DidNotTrip && p.RuleId == TestRules.Max);

        // A violation that trips another rule not listed in alsoTrips (13 also trips CV-TST-01).
        ImmutableArray<RuleFixture> noReview = baseline.Replace(
            baseline.First(f => f.RuleId == TestRules.NotThirteen),
            new RuleFixture(TestRules.NotThirteen, "main", FixtureKind.Violation, () => Run(TestInput.Def("a", 13)), ImmutableArray<string>.Empty));
        Assert.Contains(Check(registry, noReview), p => p.Kind == FixtureProblemKind.UnreviewedExtraRule);

        // An alsoTrips entry that does not fire.
        ImmutableArray<RuleFixture> stale = baseline.Replace(
            baseline.First(f => f.RuleId == TestRules.Seven),
            new RuleFixture(TestRules.Seven, "main", FixtureKind.Violation, () => Run(TestInput.Def("a", 7)), ImmutableArray.Create(TestRules.Max)));
        Assert.Contains(Check(registry, stale), p => p.Kind == FixtureProblemKind.StaleAlsoTrips);

        // A valid-edge fixture that is not valid.
        ImmutableArray<FixtureProblem> badEdge = Check(registry, Replace(baseline, TestRules.Max, FixtureKind.ValidEdge,
            () => Run(TestInput.Def("a", 11))));
        Assert.Contains(badEdge, p => p.Kind == FixtureProblemKind.ValidEdgeTripped);
    }

    [Theory]
    [InlineData("TST-RULE-2")]    // the alias of the Warning rule CV-TST-02: only the canonical id is accepted
    [InlineData("CV-TST-01")]     // an Error rule
    [InlineData("CV-ZZZ-99")]     // an unknown id
    [Trait("Id", "CDD-RULE-1")]
    public void Suppress_WithoutWhyNamingABadTarget_ReportsBothGen05AndGen21(string target)
    {
        ValidationResult r = Run(TestInput.Def("a", 5, TestInput.Suppress(target, null)));

        // Both entries share one location and one def, so they sort by rule id.
        Assert.Equal(new[] { GeneralRuleIds.SuppressWithoutWhy, GeneralRuleIds.SuppressBadTarget }, Ids(r));
        Assert.Equal(2, r.ErrorCount);
        Assert.False(r.CanBuild);
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void Suppress_NamingTheAliasOfAWarningRule_IsGen21_AndTheWarningStillReports()
    {
        ValidationResult r = Run(TestInput.Def("a", 2, TestInput.Suppress("TST-RULE-2", "intended")));

        Assert.Equal(new[] { TestRules.Min, GeneralRuleIds.SuppressBadTarget }, Ids(r));   // by path: def, then the entry
        Assert.Equal(0, r.SuppressedCount);
        Assert.Equal(0, r.UnmatchedSuppressCount);
        Assert.False(r.CanBuild);
    }

    [Fact]
    [Trait("Id", "CD3.5")]
    public void Format_PrintsWarningPrefixAndTheDefSegment()
    {
        RuleRegistry registry = TestRules.Registry();
        var warning = new ContentDiagnostic(TestRules.Min, RuleSeverity.Warning, new SourceLocation("items.jsonc", 3, 9),
            new DefRef(DefType.Item, "letter"), "2", "3..");
        var error = new ContentDiagnostic(TestRules.Max, RuleSeverity.Error, SourceLocation.BuiltIn("builtin/set"), DefRef.None, "11", "0..10");

        Assert.Equal("items.jsonc:3:9: warning CV-TST-02 (TST-RULE-2) [Item:letter]: value 2, allowed 3..", warning.Format(registry));
        Assert.Equal("builtin/set:0:0: error CV-TST-01 (TST-RULE-1): value 11, allowed 0..10", error.Format(registry));   // no [Def] for none
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void FixtureChecker_ReportsAViolationThatTripsItsRuleTwice_OrAtTheWrongSeverity()
    {
        RuleRegistry registry = TestRules.Registry();
        ImmutableArray<RuleFixture> baseline = RuleFixtureCatalogue.All();

        // Twice: two defs over the CV-TST-01 limit.
        ImmutableArray<FixtureProblem> twice = Check(registry, Replace(baseline, TestRules.Max, FixtureKind.Violation,
            () => Run(TestInput.Def("a", 11), TestInput.Def("b", 12))));
        FixtureProblem two = Assert.Single(twice, p => p.Kind == FixtureProblemKind.DidNotTrip);
        Assert.Equal(TestRules.Max, two.RuleId);
        Assert.Contains("found 2", two.Detail, StringComparison.Ordinal);

        // Wrong severity: a hand-made result carrying CV-TST-02 (a Warning rule) as an Error. The runner stamps the rule's own
        // severity, so only a hand-built result can produce this; the checker must still catch it.
        var wrong = new ContentDiagnostic(TestRules.Min, RuleSeverity.Error, SourceLocation.BuiltIn("x"), DefRef.None, "2", "3..");
        ImmutableArray<FixtureProblem> wrongSeverity = Check(registry, Replace(baseline, TestRules.Min, FixtureKind.Violation,
            () => new ValidationResult(
                ImmutableArray.Create(wrong), ImmutableArray<SkippedPair>.Empty, 0, 0)));
        FixtureProblem bad = Assert.Single(wrongSeverity, p => p.Kind == FixtureProblemKind.DidNotTrip);
        Assert.Equal(TestRules.Min, bad.RuleId);
        Assert.Contains("wrong severity", bad.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void FixtureChecker_ReportsDuplicateAndOrphanFixtures()
    {
        RuleRegistry registry = TestRules.Registry();
        ImmutableArray<RuleFixture> baseline = RuleFixtureCatalogue.All();
        RuleFixture first = baseline.First(f => f.RuleId == TestRules.Seven);

        Assert.Contains(Check(registry, baseline.Add(first)), p => p.Kind == FixtureProblemKind.DuplicateFixture);
        Assert.Contains(
            Check(registry, baseline.Add(new RuleFixture("CV-NOP-01", "main", FixtureKind.Violation, OneDiag("CV-NOP-01"), ImmutableArray<string>.Empty))),
            p => p.Kind == FixtureProblemKind.UnknownRule);
        Assert.Contains(
            Check(registry, baseline.Add(new RuleFixture(TestRules.Seven, "nope", FixtureKind.Violation, OneDiag(TestRules.Seven), ImmutableArray<string>.Empty))),
            p => p.Kind == FixtureProblemKind.UnknownClause);
    }

    private static Func<ValidationResult> OneDiag(string ruleId) => () => new ValidationResultFactory().With(ruleId);

    private static ImmutableArray<RuleFixture> Replace(ImmutableArray<RuleFixture> all, string rule, FixtureKind kind, Func<ValidationResult> produce)
    {
        RuleFixture old = all.First(f => f.RuleId == rule && f.Kind == kind);
        return all.Replace(old, old with { Produce = produce });
    }

    /// <summary>Builds a result with one Error of a given id by running a one-rule registry (ValidationResult has no public constructor).</summary>
    private sealed class ValidationResultFactory
    {
        public ValidationResult With(string ruleId)
        {
            RuleRegistry registry = RuleRegistry.Compose(
                General.Rules.RemoveAll(r => r.HasCheck),
                ImmutableArray.Create(RuleSpec.Create<TestInput>(ruleId, "test-only", "", RuleSeverity.Error, ValidationStage.References,
                    default, (input, ctx) => ctx.Report(DefRef.None, SourceLocation.BuiltIn("x"), "v", "a"))));
            return ContentValidationRunner.Run(registry, TestInput.Of());
        }
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void RetiredInto_ResolvesToARegisteredRule_OnceItsOwnerHasRegistered()
    {
        // The registry lists CV-THR-14 before CV-PLR-06 exists (story 011 registers it); the check below activates the
        // moment any CV-PLR rule is registered, so story 011 cannot forget the target.
        RuleRegistry registry = RuleRegistry.CreateDefault();
        bool playerRulesRegistered = false;
        foreach (RuleSpec r in registry.Rules)
        {
            playerRulesRegistered |= r.Id.StartsWith("CV-PLR-", StringComparison.Ordinal);
        }

        ImmutableArray<string> unresolved = registry.UnresolvedRetirements();

        if (playerRulesRegistered)
        {
            Assert.Empty(unresolved);
        }
        else
        {
            Assert.Equal(new[] { "CV-THR-14" }, unresolved);   // known gap until story 011 registers CV-PLR-06
        }
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void DeferredRows_MustHaveNoCheckAndAReason_AndOnlyDeferredRowsMayCarryOne()
    {
        // (A deferred row with a check cannot be built: Defer and EmittedRuleSpec never have one, and RuleSpec's constructor is private protected.)
        // An active row carrying a deferral reason.
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create<RuleSpec>(
            new EmittedRuleSpec("CV-AAA-02", "o", "", default, RuleSeverity.Error, ValidationStage.Schema, default, default,
                RuleStatus.Active, string.Empty, "inputs not present"))));
        // A retired row carrying a deferral reason.
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create<RuleSpec>(
            new EmittedRuleSpec("CV-AAA-03", "o", "", default, RuleSeverity.Error, ValidationStage.Schema, default, default,
                RuleStatus.Retired, "CV-AAA-01", "inputs not present"))));
        // A deferred row with a retirement target.
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create<RuleSpec>(
            new EmittedRuleSpec("CV-AAA-04", "o", "", default, RuleSeverity.Error, ValidationStage.Schema, default, default,
                RuleStatus.Deferred, "CV-AAA-01", "inputs not present"))));
        // A blank reason.
        Assert.Throws<ArgumentException>(() => RuleRegistry.Compose(ImmutableArray.Create(
            RuleSpec.Defer("CV-AAA-05", "o", "", RuleSeverity.Error, ValidationStage.WholeSet, ""))));
    }

    [Fact]
    [Trait("Id", "CDD-RULE-2")]
    public void UnresolvedRetirements_ReportsRetiredRulesWhoseTargetIsMissing()
    {
        Assert.Empty(CatalogueShapedRegistry().UnresolvedRetirements());
        RuleRegistry missing = RuleRegistry.Compose(General.Rules, ImmutableArray.Create(RuleSpec.Retire("CV-THR-14", "o", "", "CV-PLR-06")));
        Assert.Equal(new[] { "CV-THR-14" }, missing.UnresolvedRetirements());
    }

    [Fact]
    [Trait("Id", "CDD-LOAD-3")]
    public void RuleThatIgnoresIsBlocked_CannotReportOnASkippedDef()
    {
        // CV-TST-03 would flag 14 but is skipped for it (CV-TST-01 failed tst.value); this rule never asks IsBlocked.
        RuleSpec careless = RuleSpec.Create<TestInput>("CV-TST-80", "test-only", "", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(TestRules.ValueInput),
            (input, ctx) =>
            {
                foreach (TestDef d in input.TestDefs)
                {
                    ctx.Report(d.Ref, d.Where, "x", "y");
                }
            });
        RuleRegistry registry = RuleRegistry.Compose(General.Rules.RemoveAll(r => r.HasCheck), TestRules.Table, ImmutableArray.Create(careless));

        ValidationResult r = ContentValidationRunner.Run(registry, TestInput.Of(TestInput.Def("a", 14), TestInput.Def("b", 5)));

        Assert.Contains(r.Skipped, s => s.RuleId == "CV-TST-80" && s.Def.Id == "a");
        Assert.DoesNotContain(r.Diagnostics, d => d.RuleId == "CV-TST-80" && d.Def.Id == "a");
        Assert.Contains(r.Diagnostics, d => d.RuleId == "CV-TST-80" && d.Def.Id == "b");   // unblocked defs still report
    }

    [Fact]
    [Trait("Id", "CDD-RULE-1")]
    public void MismatchedRuleInputType_FailsBeforeAnyRuleRuns()
    {
        int ran = 0;
        RuleSpec first = RuleSpec.Create<TestInput>("CV-TST-81", "test-only", "", RuleSeverity.Error, ValidationStage.References,
            default, (_, _) => ran++);
        RuleSpec wrongInput = RuleSpec.Create<OtherInput>("CV-TST-82", "test-only", "", RuleSeverity.Error, ValidationStage.WholeSet,
            default, (_, _) => ran++);
        RuleRegistry registry = RuleRegistry.Compose(General.Rules.RemoveAll(r => r.HasCheck), ImmutableArray.Create(first, wrongInput));

        Assert.Throws<InvalidOperationException>(() => ContentValidationRunner.Run(registry, TestInput.Of()));
        Assert.Equal(0, ran);
    }

    private sealed class OtherInput : IValidationInput
    {
        public ImmutableArray<DefRecord> Defs => ImmutableArray<DefRecord>.Empty;

        public ImmutableArray<FailedInput> PriorFailures => ImmutableArray<FailedInput>.Empty;
    }

    [Fact]
    [Trait("Id", "CD3.5")]
    public void DefaultLocationAndDefRef_EqualTheirNormalisedNoneValues()
    {
        Assert.Equal(default, new SourceLocation(string.Empty, 0, 0));
        Assert.Equal(string.Empty, default(SourceLocation).Path);
        Assert.True(default(SourceLocation) == new SourceLocation(string.Empty, 0, 0));

        Assert.True(default(DefRef) == new DefRef(0, string.Empty));
        Assert.True(default(DefRef) == new DefRef(0, null!));
        Assert.True(new DefRef(0, null!).IsNone);
        Assert.Equal(string.Empty, default(DefRef).Id);
        Assert.False(new DefRef(DefType.Item, string.Empty).IsNone);   // a typed def with an empty id is not "none"
        Assert.True(DefRef.Compare(new DefRef(DefType.Item, "z"), new DefRef(DefType.Container, "a")) < 0);
        Assert.True(DefRef.Compare(new DefRef(DefType.Item, "a"), new DefRef(DefType.Item, "b")) < 0);
    }

    // ------------------------------------------------------------ architecture guards

    [Fact]
    [Trait("Id", "ADR-0003")]
    public void ValidationAndRuleTypes_HaveNoMutableStaticState()
    {
        // ADR-0003 section 4: a static field must be const, or static readonly of a primitive, string or [ImmutableTable] type.
        // The owner tables are therefore static properties that build their ImmutableArray on each call.
        var offenders = new List<string>();
        foreach (Type t in typeof(RuleRegistry).Assembly.GetTypes())
        {
            if (t.Namespace is not ("PerformativeMail.Sim.Content.Validation" or "PerformativeMail.Sim.Content.Rules"))
            {
                continue;
            }

            foreach (FieldInfo f in t.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                bool isConst = f.IsLiteral;
                // No [ImmutableTable] type exists yet, so only primitives and string qualify; ImmutableArray does not.
                bool immutable = f.IsInitOnly && (f.FieldType.IsPrimitive || f.FieldType == typeof(string));
                if (!isConst && !immutable)
                {
                    offenders.Add(t.Name + "." + f.Name);
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Id", "ADR-0010")]
    public void RuleTables_DoNotUseReflectionDiscovery_SourceScan()
    {
        // ADR-0010 section 5: registration is an explicit list. Scan the new folders for reflection entry points.
        string root = FindRepoRoot();
        string[] folders =
        {
            Path.Combine(root, "src", "PerformativeMail.Sim", "Content", "Validation"),
            Path.Combine(root, "src", "PerformativeMail.Sim", "Content", "Rules"),
        };
        string[] banned = { "GetTypes(", "GetCustomAttribute", "Activator.", "GetMethods(", "Assembly.Get" };
        var hits = new List<string>();
        int files = 0;
        foreach (string folder in folders)
        {
            foreach (string file in Directory.GetFiles(folder, "*.cs"))
            {
                files++;
                foreach (string line in File.ReadAllLines(file))
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (string b in banned)
                    {
                        if (line.Contains(b, StringComparison.Ordinal))
                        {
                            hits.Add(Path.GetFileName(file) + ": " + b);
                        }
                    }
                }
            }
        }

        Assert.True(files >= 10);   // the scan really sees the folders
        Assert.Empty(hits);
    }

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

    [Fact]
    [Trait("Id", "CDD-VAL-1")]
    public void FixtureChecker_AnUnregisteredAlsoTripsId_IsReported_UnlessItIsANamedForwardReference()
    {
        RuleRegistry registry = TestRules.Registry();
        ImmutableArray<RuleFixture> baseline = RuleFixtureCatalogue.All();
        RuleFixture Listing(string id) => new(
            TestRules.Seven, "main", FixtureKind.Violation, () => Run(TestInput.Def("a", 7)), ImmutableArray.Create(id));
        ImmutableArray<RuleFixture> WithListing(string id) => baseline.Replace(baseline.First(f => f.RuleId == TestRules.Seven), Listing(id));

        // A typo'd id ("CV-PRK-5") is neither registered nor allow-listed: reported, even though it never fires.
        FixtureProblem typo = Assert.Single(
            FixtureChecker.Check(registry, WithListing("CV-PRK-5"), RuleFixtureCatalogue.ForwardReferences));
        Assert.Equal(FixtureProblemKind.UnregisteredAlsoTrips, typo.Kind);
        Assert.Equal(TestRules.Seven, typo.RuleId);

        // The allow-listed forward reference passes; without the list it is reported too.
        Assert.Empty(FixtureChecker.Check(registry, WithListing("CV-PRK-05"), RuleFixtureCatalogue.ForwardReferences));
        Assert.Contains(
            FixtureChecker.Check(registry, WithListing("CV-PRK-05")), p => p.RuleId == TestRules.Seven && p.Kind == FixtureProblemKind.UnregisteredAlsoTrips);

        // Once the rule is registered, the id is judged as a normal entry: it must fire (CV-TST-01 does not for 7).
        Assert.Equal(FixtureProblemKind.StaleAlsoTrips,
            Assert.Single(FixtureChecker.Check(registry, WithListing(TestRules.Max), RuleFixtureCatalogue.ForwardReferences)).Kind);
    }
}
