using System.Collections.Immutable;
using System.Globalization;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>One test-only def: an address, a location and one integer value that the test-only rules read.</summary>
internal sealed record TestDef(DefRef Ref, SourceLocation Where, int Value, ImmutableArray<SuppressEntry> Suppress);

/// <summary>
/// The test-only input that stands in for story 008's <c>ContentSet</c>: a list of defs each carrying one integer.
/// Story 008 replaces this with an adapter over the real set; the rules and fixtures below keep their shape.
/// </summary>
internal sealed class TestInput : IValidationInput
{
    public TestInput(ImmutableArray<TestDef> defs, ImmutableArray<FailedInput> priorFailures = default)
    {
        TestDefs = defs;
        PriorFailures = priorFailures.IsDefault ? ImmutableArray<FailedInput>.Empty : priorFailures;
    }

    public ImmutableArray<TestDef> TestDefs { get; }

    public ImmutableArray<DefRecord> Defs
    {
        get
        {
            ImmutableArray<DefRecord>.Builder b = ImmutableArray.CreateBuilder<DefRecord>();
            foreach (TestDef d in TestDefs)
            {
                b.Add(new DefRecord(d.Ref, d.Where, d.Suppress));
            }

            return b.ToImmutable();
        }
    }

    public ImmutableArray<FailedInput> PriorFailures { get; }

    // ------------------------------------------------------------ builders

    public static TestDef Def(string id, int value, params SuppressEntry[] suppress) =>
        new(new DefRef(DefType.Item, id), SourceLocation.BuiltIn("builtin/items/" + id), value,
            suppress.Length == 0 ? ImmutableArray<SuppressEntry>.Empty : ImmutableArray.Create(suppress));

    public static TestInput Of(params TestDef[] defs) => new(ImmutableArray.Create(defs));

    public static SuppressEntry Suppress(string? rule, string? why) =>
        new(rule, why, SourceLocation.BuiltIn("builtin/suppress"));
}

/// <summary>
/// The test-only rule set (area TST): five rules over <see cref="TestInput"/> covering both severities, both runner
/// stages, a clause with a limit, declared inputs and an <c>alsoTrips</c> case.
/// </summary>
internal static class TestRules
{
    public const string Max = "CV-TST-01";          // Error  stage 4: value must be <= 10 (limit)
    public const string Min = "CV-TST-02";          // Warning stage 4: value must be >= 3 (limit)
    public const string Seven = "CV-TST-03";        // Error  stage 5: value must not be a multiple of 7
    public const string Total = "CV-TST-04";        // Error  stage 5, whole set: sum of values <= 50 (limit)
    public const string NotThirteen = "CV-TST-05";  // Error  stage 4: value must not be 13

    public const string ValueInput = "tst.value";

    public static ImmutableArray<RuleSpec> Table { get; } = ImmutableArray.Create(
        RuleSpec.Create<TestInput>(Max, "test-only", "TST-RULE-1", RuleSeverity.Error, ValidationStage.References,
            ImmutableArray.Create(ValueInput), CheckMax, ImmutableArray.Create(new RuleClause("max", HasLimit: true))),
        RuleSpec.Create<TestInput>(Min, "test-only", "TST-RULE-2", RuleSeverity.Warning, ValidationStage.References,
            ImmutableArray.Create(ValueInput), CheckMin, ImmutableArray.Create(new RuleClause("min", HasLimit: true))),
        RuleSpec.Create<TestInput>(Seven, "test-only", "TST-RULE-3", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ValueInput), CheckSeven),
        RuleSpec.Create<TestInput>(Total, "test-only", "TST-RULE-4", RuleSeverity.Error, ValidationStage.WholeSet,
            ImmutableArray.Create(ValueInput), CheckTotal, ImmutableArray.Create(new RuleClause("total", HasLimit: true))),
        RuleSpec.Create<TestInput>(NotThirteen, "test-only", "TST-RULE-5", RuleSeverity.Error, ValidationStage.References,
            ImmutableArray.Create(ValueInput), CheckNotThirteen));

    /// <summary>The production General, Grid, Mail, Player and Throwing rows plus the test-only rows (a new instance each call).</summary>
    public static RuleRegistry Registry() => RuleRegistry.Compose(
        General.Rules, Grid.Rules, Mail.Rules, PerformativeMail.Sim.Content.Rules.Player.Rules, PerformativeMail.Sim.Content.Rules.Throwing.Rules, Table);

    /// <summary>
    /// The registry the runner executes over a <see cref="TestInput"/>: the General rows that have no check (the suppress
    /// policy rows and the emitted-elsewhere rows) plus the test-only rows. The runner refuses a registry holding a
    /// check that cannot read the input type, and General's set rules (story 009) read the content adapter.
    /// </summary>
    public static RuleRegistry RunnableRegistry() => RuleRegistry.Compose(General.Rules.RemoveAll(r => r.HasCheck), Table);

    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static void CheckMax(TestInput input, RuleContext ctx)
    {
        foreach (TestDef d in input.TestDefs)
        {
            if (!ctx.IsBlocked(d.Ref) && d.Value > 10)
            {
                ctx.Report(d.Ref, d.Where, Num(d.Value), "0..10", ValueInput);
            }
        }
    }

    private static void CheckMin(TestInput input, RuleContext ctx)
    {
        foreach (TestDef d in input.TestDefs)
        {
            if (!ctx.IsBlocked(d.Ref) && d.Value < 3)
            {
                ctx.Report(d.Ref, d.Where, Num(d.Value), "3..", null);
            }
        }
    }

    private static void CheckSeven(TestInput input, RuleContext ctx)
    {
        foreach (TestDef d in input.TestDefs)
        {
            if (!ctx.IsBlocked(d.Ref) && d.Value % 7 == 0)
            {
                ctx.Report(d.Ref, d.Where, Num(d.Value), "not a multiple of 7");
            }
        }
    }

    private static void CheckTotal(TestInput input, RuleContext ctx)
    {
        int sum = 0;
        foreach (TestDef d in input.TestDefs)
        {
            if (!ctx.IsBlocked(d.Ref))
            {
                sum += d.Value;
            }
        }

        if (sum > 50)
        {
            ctx.Report(DefRef.None, SourceLocation.BuiltIn("builtin/set"), Num(sum), "0..50");
        }
    }

    private static void CheckNotThirteen(TestInput input, RuleContext ctx)
    {
        foreach (TestDef d in input.TestDefs)
        {
            if (!ctx.IsBlocked(d.Ref) && d.Value == 13)
            {
                ctx.Report(d.Ref, d.Where, "13", "any value but 13", ValueInput);
            }
        }
    }
}

/// <summary>
/// Every violation and valid-edge fixture the unit tests know. A story that registers a real rule appends its
/// fixtures here; the meta-test in <c>RuleRegistryTests</c> then covers the new clause automatically and fails if a
/// registered clause has none.
/// </summary>
internal static class RuleFixtureCatalogue
{
    private static ValidationResult Run(params TestDef[] defs) =>
        ContentValidationRunner.Run(TestRules.RunnableRegistry(), TestInput.Of(defs));

    private static RuleFixture Violation(string rule, string clause, Func<ValidationResult> produce, params string[] alsoTrips) =>
        new(rule, clause, FixtureKind.Violation, produce, ImmutableArray.Create(alsoTrips));

    private static RuleFixture Edge(string rule, string clause, Func<ValidationResult> produce) =>
        new(rule, clause, FixtureKind.ValidEdge, produce, ImmutableArray<string>.Empty);

    /// <summary>
    /// Builds the production <c>sim.*</c> table from the default entries after <paramref name="change"/> edited them
    /// (content-data story 004: CV-GEN-05 balance clause, CV-GEN-13, CV-GEN-20 are raised by <see cref="BalanceTable.Build"/>).
    /// </summary>
    private static ValidationResult Balance(Action<List<BalanceEntry>> change)
    {
        var entries = new List<BalanceEntry>(BalanceKeys.CreateDefaultEntries());
        change(entries);
        return BalanceTable.Build(BalanceKeys.CreateRegistry(), entries.ToImmutableArray()).Validation;
    }

    /// <summary>Replaces <c>sim.warmupTicks</c> (the single-value probe: hard 0..100000, safe 150..900) in the default entries.</summary>
    private static ValidationResult Warmup(long value, bool keepSafe) => Balance(e =>
    {
        int i = e.FindIndex(x => x.Name == "sim.warmupTicks");
        e[i] = e[i] with { Value = value, Safe = keepSafe ? e[i].Safe : null };
    });

    /// <summary>
    /// Builds the stat-bound table (the five PROPOSED CD12 owner row tables with their CD12 values) after
    /// <paramref name="change"/> edited the values, and resolves every StatDef's bounds (content-data story 006).
    /// </summary>
    private static ValidationResult StatBounds(Action<Dictionary<string, long>> change)
    {
        var values = new Dictionary<string, long>
        {
            ["delivery.insertPmMax"] = 750, ["inventory.maxRows"] = 8, ["mail.payMultMaxPm"] = 5000,
            ["mail.penaltyMultMaxPm"] = 2000, ["mail.lateValueRatio"] = 500, ["player.buffCapPm"] = 400,
            ["player.maxExtraJumps"] = 1, ["player.respawnMinMs"] = 1000, ["player.respawnMs"] = 10_000,
            ["throw.rangeMinPermille"] = 1000, ["throw.rangeMaxPermille"] = 1500,
        };
        change(values);
        BalanceRegistry registry = BalanceKeys.CreateRegistry();
        ImmutableArray<BalanceEntry> entries = BalanceKeys.CreateDefaultEntries()
            .Select(e => values.TryGetValue(e.Name, out long v) ? e with { Value = v, Safe = null } : e)
            .ToImmutableArray();
        return StatDefs.ResolveBounds(BalanceTable.Build(registry, entries).Table!).Validation;
    }

    /// <summary>
    /// Catalogue rule ids a fixture's alsoTrips may name before their rules are registered. CV-PRK-05 (a perk modifier's |A|
    /// exceeds max - min) is listed by CV-THR-02's fixture and lands with the perk rules at M1: remove it then.
    /// </summary>
    public static ImmutableArray<string> ForwardReferences => ImmutableArray.Create("CV-PRK-05");

    /// <summary>Fixtures for the rows registered in <c>Content/Rules/*.cs</c> (real rules).</summary>
    public static ImmutableArray<RuleFixture> Real() => ImmutableArray.Create(
        // CV-GEN-05, clause suppress_why: a suppress entry with no reason.
        Violation(GeneralRuleIds.SuppressWithoutWhy, "suppress_why",
            () => Run(TestInput.Def("a", 5, TestInput.Suppress(TestRules.Min, null)))),
        // CV-GEN-05, clause balance_value_missing: a registered balance key no file supplied (no implicit default).
        Violation(GeneralRuleIds.RequiredFieldMissing, "balance_value_missing",
            () => Balance(e => e.RemoveAll(x => x.Name == "sim.warmupTicks"))),
        // CV-GEN-13: duplicate / unregistered / wrong owner file / hard range (edge: exactly the hard maximum).
        Violation(GeneralRuleIds.BalanceKey, "duplicate_key",
            () => Balance(e => e.Add(e.Find(x => x.Name == "sim.warmupTicks")!))),
        Violation(GeneralRuleIds.BalanceKey, "unregistered_key",
            () => Balance(e => e.Add(e[0] with { Name = "sim.noSuchKey" }))),
        Violation(GeneralRuleIds.BalanceKey, "wrong_owner_file",
            () => Balance(e => { int i = e.FindIndex(x => x.Name == "sim.warmupTicks"); e[i] = e[i] with { File = "player" }; })),
        Violation(GeneralRuleIds.BalanceKey, "outside_hard_range", () => Warmup(100_001, keepSafe: false)),
        Edge(GeneralRuleIds.BalanceKey, "outside_hard_range", () => Warmup(100_000, keepSafe: false)),
        // CV-GEN-20 (Warning): outside the data-file safe range (edge: exactly the safe maximum).
        Violation(GeneralRuleIds.BalanceOutsideSafe, "outside_safe_range", () => Warmup(901, keepSafe: true)),
        Edge(GeneralRuleIds.BalanceOutsideSafe, "outside_safe_range", () => Warmup(900, keepSafe: true)),
        // CV-GEN-15: a StatDef whose min exceeds its max after the balance keys resolve (edge: min == max).
        Violation(GeneralRuleIds.StatBoundsInverted, "min_above_max",
            () => StatBounds(v => v["throw.rangeMinPermille"] = 1501)),
        Edge(GeneralRuleIds.StatBoundsInverted, "min_above_max",
            () => StatBounds(v => v["throw.rangeMinPermille"] = 1500)),
        // CV-GEN-21: suppress names an Error rule / an unknown rule id.
        Violation(GeneralRuleIds.SuppressBadTarget, "names_error_rule",
            () => Run(TestInput.Def("a", 5, TestInput.Suppress(TestRules.Max, "because")))),
        Violation(GeneralRuleIds.SuppressBadTarget, "names_unknown_rule",
            () => Run(TestInput.Def("a", 5, TestInput.Suppress("CV-ZZZ-99", "because")))))
        .AddRange(GeneralRuleFixtures.All())
        .AddRange(GridMailRuleFixtures.All())
        .AddRange(PlayerRuleFixtures.All())
        .AddRange(SchemaRuleFixtures.All())
        .AddRange(SyntaxRuleFixtures.All())
        .AddRange(ThrowingRuleFixtures.All());

    /// <summary>Fixtures for the test-only rows.</summary>
    public static ImmutableArray<RuleFixture> TestOnly() => ImmutableArray.Create(
        Violation(TestRules.Max, "max", () => Run(TestInput.Def("a", 11))),
        Edge(TestRules.Max, "max", () => Run(TestInput.Def("a", 10))),
        Violation(TestRules.Min, "min", () => Run(TestInput.Def("a", 2))),
        Edge(TestRules.Min, "min", () => Run(TestInput.Def("a", 3))),
        Violation(TestRules.Seven, "main", () => Run(TestInput.Def("a", 7))),
        Violation(TestRules.Total, "total", () => Run(
            TestInput.Def("a", 10), TestInput.Def("b", 10), TestInput.Def("c", 10),
            TestInput.Def("d", 10), TestInput.Def("e", 10), TestInput.Def("f", 10))),
        Edge(TestRules.Total, "total", () => Run(
            TestInput.Def("a", 10), TestInput.Def("b", 10), TestInput.Def("c", 10),
            TestInput.Def("d", 10), TestInput.Def("e", 10))),
        // 13 is also above CV-TST-01's limit: a reviewed alsoTrips entry.
        Violation(TestRules.NotThirteen, "main", () => Run(TestInput.Def("a", 13)), TestRules.Max));

    /// <summary>All fixtures.</summary>
    public static ImmutableArray<RuleFixture> All() => Real().AddRange(TestOnly());
}
