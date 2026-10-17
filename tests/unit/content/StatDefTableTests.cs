using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Tests.Unit.Content;

/// <summary>
/// Covers content-data story 006 (TR-content-014, enum half of TR-content-018; GDD CDD-STAT-1, CD11, CD12, CD18):
/// the <see cref="Stat"/> enum and its spelling table, the M-1a <see cref="StatDef"/> table, bound resolution against a
/// <see cref="BalanceTable"/> with CV-GEN-15, and <see cref="RuleFlag"/>. The snapshot tables are LITERAL on purpose:
/// values are part of the content hash, so any drift must fail here and force a deliberate edit. The test project may use
/// reflection-style enumeration (the Sim's banned-API analyzer does not apply). No randomness, time or I/O.
/// </summary>
public class StatDefTableTests
{
    // ------------------------------------------------------------ literal snapshots

    /// <summary>One CD12 row. Base: <c>const:N</c> | <c>key:NAME</c> | <c>container_rows</c> | <c>mail_kind_max_stack</c>. Bound: <c>const:N</c> | <c>key:NAME</c> | <c>base</c>.</summary>
    private readonly record struct Cd12(
        string Name, int Value, string Spelling, string Unit, string Scope, string Qualifier, string Base, string Ops,
        string SetResolve, string Rounding, string Min, string Max);

    private static readonly Cd12[] _cd12 =
    {
        new("DeliverInsertPm", 1, "deliver_insert_pm", "Permille", "Personal", "None", "const:0", "Add", "NotApplicable", "HalfUp", "const:0", "key:delivery.insertPmMax"),
        new("MoveSpeedAddPm", 2, "move_speed_add_pm", "Permille", "Personal", "None", "const:0", "Add", "NotApplicable", "HalfUp", "const:0", "key:player.buffCapPm"),
        new("ExtraJumps", 3, "extra_jumps", "Count", "Personal", "None", "const:0", "Add", "NotApplicable", "HalfUp", "const:0", "key:player.maxExtraJumps"),
        new("RespawnMs", 4, "respawn_ms", "Ms", "Personal", "None", "key:player.respawnMs", "Set", "MinWins", "HalfUp", "key:player.respawnMinMs", "base"),
        new("InventoryRows", 5, "inventory_rows", "Count", "Both", "Container", "container_rows", "Add, Mul", "NotApplicable", "Ceil", "const:1", "key:inventory.maxRows"),
        new("MailMaxStack", 6, "mail_max_stack", "Count", "Team", "MailKind", "mail_kind_max_stack", "Mul", "NotApplicable", "Floor", "const:1", "const:255"),
        new("WeightPenaltyPm", 7, "weight_penalty_pm", "Permille", "Personal", "None", "const:1000", "Mul", "NotApplicable", "HalfUp", "const:0", "const:1000"),
        new("MailValueMultPm", 8, "mail_value_mult_pm", "Permille", "Team", "MailKind", "const:1000", "Mul", "NotApplicable", "HalfUp", "const:0", "key:mail.payMultMaxPm"),
        new("ThrownCorrectPayMultPm", 9, "thrown_correct_pay_mult_pm", "Permille", "Personal", "None", "const:1000", "Mul", "NotApplicable", "HalfUp", "const:0", "key:mail.payMultMaxPm"),
        new("LateValueRatioPm", 10, "late_value_ratio_pm", "Permille", "Team", "None", "key:mail.lateValueRatio", "Set", "MaxWins", "HalfUp", "const:0", "const:1000"),
        new("MisdeliveryPenaltyMultPm", 11, "misdelivery_penalty_mult_pm", "Permille", "Team", "None", "const:1000", "Mul", "NotApplicable", "HalfUp", "const:0", "key:mail.penaltyMultMaxPm"),
        new("MisdeliveryComplaintMultPm", 12, "misdelivery_complaint_mult_pm", "Permille", "Team", "None", "const:1000", "Mul", "NotApplicable", "HalfUp", "const:0", "key:mail.penaltyMultMaxPm"),
        new("ThrowRangePermille", 13, "throw_range_permille", "Permille", "Personal", "None", "const:1000", "Add", "NotApplicable", "HalfUp", "key:throw.rangeMinPermille", "key:throw.rangeMaxPermille"),
    };

    private static readonly (string Name, int Value, string Spelling)[] _ruleFlags =
    {
        ("KeepHotbarOnDeath", 1, "keep_hotbar_on_death"), ("PresortOutfeed", 2, "presort_outfeed"),
        ("InsuredRebuild", 3, "insured_rebuild"), ("SuppressLateComplaint", 4, "suppress_late_complaint"),
        ("PriorityMail", 5, "priority_mail"), ("FreeForecast", 6, "free_forecast"), ("SharedBuild", 7, "shared_build"),
        ("LocalKnowledgeArrow", 8, "local_knowledge_arrow"), ("BeltMomentum", 9, "belt_momentum"),
        ("EarlyDistrictUnlock", 10, "early_district_unlock"), ("EnemiesMarked", 11, "enemies_marked"),
        ("PostalSprint", 12, "postal_sprint"), ("CursedMail", 13, "cursed_mail"), ("RushHour", 14, "rush_hour"),
    };

    /// <summary>The CD12 values of every PROPOSED clamp and base key (the numbers in the CD12 table and its note).</summary>
    private static readonly (string Name, long Value)[] _cd12Values =
    {
        ("delivery.insertPmMax", 750), ("inventory.maxRows", 8), ("mail.payMultMaxPm", 5000), ("mail.penaltyMultMaxPm", 2000),
        ("mail.lateValueRatio", 500), ("player.buffCapPm", 400), ("player.maxExtraJumps", 1), ("player.respawnMinMs", 1000),
        ("player.respawnMs", 10_000), ("throw.rangeMinPermille", 1000), ("throw.rangeMaxPermille", 1500),
    };

    // ------------------------------------------------------------ helpers

    private static string Describe(BaseSource b) => b.Kind switch
    {
        BaseSourceKind.Constant => "const:" + b.Value,
        BaseSourceKind.Balance => "key:" + b.Key,
        BaseSourceKind.ContainerRows => "container_rows",
        BaseSourceKind.MailKindMaxStack => "mail_kind_max_stack",
        _ => "?",
    };

    private static string Describe(StatBound b) => b.Kind switch
    {
        StatBoundKind.Constant => "const:" + b.Value,
        StatBoundKind.Balance => "key:" + b.Key,
        StatBoundKind.Base => "base",
        _ => "?",
    };

    private static string Describe(StatDef d) =>
        string.Join("|", d.Stat, (int)d.Stat, "-", d.Unit, d.Scope, d.Qualifier, Describe(d.Base), d.Ops, d.SetResolve, d.Rounding, Describe(d.Min), Describe(d.Max));

    private static string Describe(Cd12 r) =>
        string.Join("|", r.Name, r.Value, "-", r.Unit, r.Scope, r.Qualifier, r.Base, r.Ops, r.SetResolve, r.Rounding, r.Min, r.Max);

    private static string Utf8(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>PascalCase to snake_case, digit-free: the rule the generated map follows.</summary>
    private static string Snake(string pascal)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(pascal[i]));
        }

        return sb.ToString();
    }

    private static BalanceRegistry OwnerRegistry() => BalanceKeys.CreateRegistry();

    /// <summary>The full production table: the M-1a defaults, with the CD12 values (a fixture: maxRows 8) and then <paramref name="overrides"/> applied.</summary>
    private static BalanceTable Table(params (string Name, long Value)[] overrides)
    {
        var values = new Dictionary<string, long>();
        foreach ((string name, long value) in _cd12Values)
        {
            values[name] = value;
        }

        foreach ((string name, long value) in overrides)
        {
            values[name] = value;
        }

        ImmutableArray<BalanceEntry> entries = BalanceKeys.CreateDefaultEntries()
            .Select(e => values.TryGetValue(e.Name, out long v) ? e with { Value = v, Safe = null } : e)
            .ToImmutableArray();
        BalanceBuildResult r = BalanceTable.Build(OwnerRegistry(), entries);
        Assert.True(r.Validation.CanBuild, "test table must build: " + string.Join("; ", r.Validation.Diagnostics.Select(d => d.Value)));
        return r.Table!;
    }

    // ------------------------------------------------------------ AC-1: Stat enum, table snapshot, bijective map

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatEnum_HoldsTheThirteenM1aMembers_WithExplicitOneBasedCd12Positions()
    {
        Stat[] members = Enum.GetValues<Stat>();

        Assert.Equal(_cd12.Length, members.Length);
        Assert.Equal(StatDefs.Count, members.Length);
        for (int i = 0; i < _cd12.Length; i++)
        {
            Assert.Equal(_cd12[i].Name, members[i].ToString());
            Assert.Equal(_cd12[i].Value, (int)members[i]);
            Assert.Equal(i + 1, (int)members[i]);
        }

        Assert.False(Enum.IsDefined((Stat)0));   // 0 is never a valid loaded value
        Assert.False(Enum.IsDefined((Stat)14));  // rows 14+ are appended by their reading systems' epics
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatDefTable_MatchesTheCd12M1aRows_InEveryColumn()
    {
        ImmutableArray<StatDef> all = StatDefs.All();

        Assert.Equal(_cd12.Length, all.Length);
        for (int i = 0; i < _cd12.Length; i++)
        {
            Assert.Equal(Describe(_cd12[i]), Describe(all[i]));
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatDefs_Get_ReturnsTheRowOfEachStat_AndAllIsInStatOrder()
    {
        foreach (Cd12 row in _cd12)
        {
            StatDef def = StatDefs.Get((Stat)row.Value);

            Assert.Equal(row.Name, def.Stat.ToString());
            Assert.Equal(Describe(row), Describe(def));
            Assert.Equal(def, StatDefs.All()[row.Value - 1]);
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatDefs_UndefinedStat_TryGetReturnsFalse_GetThrows()
    {
        Assert.False(StatDefs.TryGet((Stat)0, out StatDef zero));
        Assert.False(StatDefs.TryGet((Stat)14, out _));
        Assert.Equal(default, zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => StatDefs.Get((Stat)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StatDefs.Get((Stat)200));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ThrowRangePermille_IsClampedByTheThrowKeys_AndMailMaxStackByCodeBounds()
    {
        StatDef range = StatDefs.Get(Stat.ThrowRangePermille);
        StatDef stack = StatDefs.Get(Stat.MailMaxStack);

        Assert.Equal(StatBound.Balance("throw.rangeMinPermille"), range.Min);
        Assert.Equal(StatBound.Balance("throw.rangeMaxPermille"), range.Max);
        Assert.Equal(StatBound.Constant(1), stack.Min);
        Assert.Equal(StatBound.Constant(255), stack.Max);   // uint8 entry count: a correctness bound, in code
        StatBounds b = StatDefs.ResolveBounds(Table()).Get(Stat.ThrowRangePermille);
        Assert.Equal(1000, b.Min);
        Assert.Equal(1500, b.Max);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void EveryStat_HasANonNegativeMin_AndOnlyCorrectnessBoundsAreCodeConstants()
    {
        foreach (StatDef def in StatDefs.All())
        {
            Assert.NotEqual(StatBoundKind.Base, def.Min.Kind);   // Base is a max-only bound
            if (def.Min.Kind == StatBoundKind.Constant)
            {
                Assert.True(def.Min.Value >= 0);
            }

            Assert.True((def.Ops & StatOps.Set) != 0 == (def.SetResolve != SetResolve.NotApplicable), "Set resolve iff Set op: " + def.Stat);
            Assert.NotEqual(StatOps.None, def.Ops);
        }

        // The correctness bounds the GDD keeps in code (CD12 "Which clamp bounds are data").
        string[] codeMaxima = StatDefs.All().Where(d => d.Max.Kind == StatBoundKind.Constant).Select(d => d.Stat + "=" + d.Max.Value).ToArray();
        Assert.Equal(new[] { "MailMaxStack=255", "WeightPenaltyPm=1000", "LateValueRatioPm=1000" }, codeMaxima);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatSpelling_IsBijective_AndIsTheSnakeCaseOfTheMemberName()
    {
        var seen = new HashSet<string>();
        foreach (Stat stat in Enum.GetValues<Stat>())
        {
            string spelling = Utf8(StatSpelling.ToSpelling(stat));

            Assert.Equal(Snake(stat.ToString()), spelling);
            Assert.True(seen.Add(spelling), "duplicate spelling " + spelling);
            Assert.True(StatSpelling.TryParse(Bytes(spelling), out Stat back));
            Assert.Equal(stat, back);
        }

        Assert.Equal(_cd12.Select(r => r.Spelling), Enum.GetValues<Stat>().Select(s => Utf8(StatSpelling.ToSpelling(s))));
    }

    [Theory]
    [Trait("Id", "CDD-STAT-1")]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("THROW_RANGE_PERMILLE")]
    [InlineData("ThrowRangePermille")]
    [InlineData("throw_range_permille ")]
    [InlineData("throw_range")]
    [InlineData("bike_speed_mult_pm")]   // CD12 row 14: not an M-1a member
    public void StatSpelling_TryParse_RejectsNonSpellings(string text)
    {
        Assert.False(StatSpelling.TryParse(Bytes(text), out Stat value));
        Assert.Equal(default, value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatSpelling_ToSpelling_OfAnUndefinedValue_IsEmpty()
    {
        Assert.True(StatSpelling.ToSpelling((Stat)0).IsEmpty);
        Assert.True(StatSpelling.ToSpelling((Stat)99).IsEmpty);
    }

    // ------------------------------------------------------------ keys registered by this story

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ClampKeys_AreRegisteredInTheProductionRegistry_WithCorrectnessOnlyHardRanges()
    {
        BalanceRegistry registry = BalanceKeys.CreateRegistry();

        foreach ((string name, long value) in _cd12Values)
        {
            Assert.True(registry.TryFind(name, out BalanceKey key), name + " must be registered");
            BalanceRow row = registry[key];
            Assert.InRange(value, row.HardMin, row.HardMax);
        }

        // Ratio above 1.0 has no meaning (CD12): the only hard range tighter than width/sign.
        BalanceRow ratio = registry[registry.Find("mail.lateValueRatio")];
        Assert.Equal((0L, 1000L), (ratio.HardMin, ratio.HardMax));
        Assert.Equal(BalanceUnit.Ms, registry[registry.Find("player.respawnMinMs")].Unit);
        Assert.Equal(BalanceUnit.Count, registry[registry.Find("inventory.maxRows")].Unit);
        Assert.Equal(BalanceUnit.Permille, registry[registry.Find("throw.rangeMaxPermille")].Unit);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void EveryKeyNamedByAStatDef_IsRegistered_InTheProductionRegistry()
    {
        BalanceRegistry registry = BalanceKeys.CreateRegistry();
        var named = new List<string>();
        foreach (StatDef def in StatDefs.All())
        {
            if (def.Base.Kind == BaseSourceKind.Balance)
            {
                named.Add(def.Base.Key);
            }

            foreach (StatBound b in new[] { def.Min, def.Max })
            {
                if (b.Kind == StatBoundKind.Balance)
                {
                    named.Add(b.Key);
                }
            }
        }

        Assert.NotEmpty(named);
        Assert.All(named, n => Assert.True(registry.TryFind(n, out _), n));
        Assert.Equal(_cd12Values.Select(v => v.Name).OrderBy(n => n, StringComparer.Ordinal), named.Distinct().OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void OwnerTables_AreFreshArraysPerCall_AndHoldNoHandles()
    {
        Assert.Single(BalanceKeys.Delivery.Rows);
        Assert.Contains(BalanceKeys.Inventory.Rows, r => r.Key == BalanceKeys.Inventory.MaxRowsKey);
        // Story 008 appended the owners' remaining rows; the CD12 clamp keys are still among them.
        Assert.Contains(BalanceKeys.Mail.Rows, r => r.Key == BalanceKeys.Mail.PayMultMaxPmKey);
        Assert.Contains(BalanceKeys.Player.Rows, r => r.Key == BalanceKeys.Player.RespawnMsKey);
        Assert.Contains(BalanceKeys.Throw.Rows, r => r.Key == BalanceKeys.Throw.RangeMaxPermilleKey);
        Assert.All(BalanceKeys.Player.Rows, r => Assert.Equal("player", r.Owner));
        Assert.Equal(BalanceKeys.CreateRegistry().Stamp, BalanceKeys.CreateRegistry().Stamp);
    }

    // ------------------------------------------------------------ AC-2: resolve bounds, CV-GEN-15

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ResolveBounds_AgainstTheCd12Values_GivesMinAndMaxPerStat()
    {
        StatBoundsResult r = StatDefs.ResolveBounds(Table());

        Assert.True(r.Validation.CanBuild);
        Assert.Empty(r.Validation.Diagnostics);
        Assert.Equal(StatDefs.Count, r.Bounds.Length);
        (Stat Stat, int Min, int Max)[] expected =
        {
            (Stat.DeliverInsertPm, 0, 750), (Stat.MoveSpeedAddPm, 0, 400), (Stat.ExtraJumps, 0, 1), (Stat.RespawnMs, 1000, 10_000),
            (Stat.InventoryRows, 1, 8), (Stat.MailMaxStack, 1, 255), (Stat.WeightPenaltyPm, 0, 1000), (Stat.MailValueMultPm, 0, 5000),
            (Stat.ThrownCorrectPayMultPm, 0, 5000), (Stat.LateValueRatioPm, 0, 1000), (Stat.MisdeliveryPenaltyMultPm, 0, 2000),
            (Stat.MisdeliveryComplaintMultPm, 0, 2000), (Stat.ThrowRangePermille, 1000, 1500),
        };
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(new StatBounds(expected[i].Stat, expected[i].Min, expected[i].Max), r.Bounds[i]);
            Assert.Equal(r.Bounds[i], r.Get(expected[i].Stat));
            Assert.True(r.Bounds[i].IsValid);
        }
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ResolveBounds_FollowsTheBalanceValues_NotACopy()
    {
        BalanceTable table = Table(("throw.rangeMaxPermille", 1800), ("player.respawnMs", 8000), ("mail.payMultMaxPm", 6000));

        StatBoundsResult r = StatDefs.ResolveBounds(table);

        Assert.Equal(1800, r.Get(Stat.ThrowRangePermille).Max);
        Assert.Equal(8000, r.Get(Stat.RespawnMs).Max);   // the max of RespawnMs is its base
        Assert.Equal(6000, r.Get(Stat.MailValueMultPm).Max);
        Assert.Equal(6000, r.Get(Stat.ThrownCorrectPayMultPm).Max);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ResolveBounds_MinAboveMax_ReportsCvGen15NamingBothKeys()
    {
        BalanceTable table = Table(("throw.rangeMinPermille", 1600));

        StatBoundsResult r = StatDefs.ResolveBounds(table);

        ContentDiagnostic d = Assert.Single(r.Validation.Diagnostics);
        Assert.Equal("CV-GEN-15", d.RuleId);
        Assert.Equal(GeneralRuleIds.StatBoundsInverted, StatDefs.InvertedBoundsRuleId);
        Assert.Equal(RuleSeverity.Error, d.Severity);
        Assert.Contains("throw.rangeMinPermille", d.Value);
        Assert.Contains("throw.rangeMaxPermille", d.Value);
        Assert.Contains("1600", d.Value);
        Assert.Contains("1500", d.Value);
        Assert.Equal("min <= max", d.Allowed);
        Assert.Equal("throw.rangeMinPermille", d.InputKey);
        Assert.Equal("builtin/statdef/throw_range_permille:0:0", d.Location.ToString());
        Assert.True(d.Def.IsNone);
        Assert.False(r.Validation.CanBuild);
        Assert.False(r.Get(Stat.ThrowRangePermille).IsValid);   // the bounds are still returned
        Assert.Contains("CV-GEN-15", d.Format(RuleRegistry.CreateDefault()));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ResolveBounds_RespawnMinAboveItsBase_NamesTheBaseKeyAsTheMax()
    {
        BalanceTable table = Table(("player.respawnMinMs", 12_000));

        ContentDiagnostic d = Assert.Single(StatDefs.ResolveBounds(table).Validation.Diagnostics);

        Assert.Contains("player.respawnMinMs", d.Value);
        Assert.Contains("player.respawnMs", d.Value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ResolveBounds_TwoInvertedStatsSharingAKey_ReportOneFindingEach_InStatOrder()
    {
        // mail.payMultMaxPm below the constant-0 min cannot happen (hard min 0); one shared max key at 0 is still valid.
        // Two stats invert at once through the shared penalty key only when its hard minimum allows it, so use two keys.
        BalanceTable table = Table(("throw.rangeMinPermille", 2000), ("player.respawnMinMs", 20_000));

        StatBoundsResult r = StatDefs.ResolveBounds(table);

        Assert.Equal(2, r.Validation.Diagnostics.Length);
        Assert.Contains("player.respawnMinMs", r.Validation.Diagnostics[0].Value);   // RespawnMs (4) before ThrowRangePermille (13)
        Assert.Contains("throw.rangeMinPermille", r.Validation.Diagnostics[1].Value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ResolveBounds_MinEqualsMax_IsValid()
    {
        StatBoundsResult r = StatDefs.ResolveBounds(Table(("throw.rangeMinPermille", 1500)));

        Assert.True(r.Validation.CanBuild);
        Assert.Equal(new StatBounds(Stat.ThrowRangePermille, 1500, 1500), r.Get(Stat.ThrowRangePermille));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ResolveBounds_UsesTheSeverityOfTheSuppliedRuleRegistry()
    {
        RuleSpec warn = RuleSpec.Emitted("CV-GEN-15", "content-data-definitions", string.Empty, RuleSeverity.Warning, ValidationStage.Build);
        var rules = new RuleRegistry(ImmutableArray.Create(warn));

        StatBoundsResult r = StatDefs.ResolveBounds(Table(("throw.rangeMinPermille", 1600)), rules);

        Assert.Equal(RuleSeverity.Warning, Assert.Single(r.Validation.Diagnostics).Severity);
        Assert.True(r.Validation.CanBuild);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ResolveBounds_WithoutTheRule_ThrowsArgumentException()
    {
        var rules = new RuleRegistry(ImmutableArray<RuleSpec>.Empty);

        Assert.Throws<ArgumentException>(() => StatDefs.ResolveBounds(Table(), rules));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ResolveBounds_NullTable_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StatDefs.ResolveBounds(null!));
        Assert.Throws<ArgumentNullException>(() => StatDefs.Get(Stat.ExtraJumps).ResolveBounds(null!));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void ResolveBounds_TableWithoutTheNamedKeys_ThrowsArgumentException()
    {
        BalanceTable simOnly = BalanceTable.Build(BalanceKeys.Compose(BalanceKeys.Sim.Rows), SimSettings.DefaultEntries()).Table!;

        Assert.Throws<ArgumentException>(() => StatDefs.ResolveBounds(simOnly));
        Assert.Throws<ArgumentException>(() => StatDefs.Get(Stat.RespawnMs).ResolveBounds(simOnly));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatDef_ResolveBounds_OnACustomRow_HandlesConstantsKeysAndDescriptions()
    {
        BalanceTable table = Table();
        var custom = new StatDef(
            Stat.ExtraJumps, StatUnit.Count, StatScope.Personal, QualifierKind.None, BaseSource.Constant(0), StatOps.Add,
            SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Balance("player.buffCapPm"), StatBound.Constant(100));

        StatBounds b = custom.ResolveBounds(table);

        Assert.Equal(new StatBounds(Stat.ExtraJumps, 400, 100), b);
        Assert.False(b.IsValid);
        Assert.Equal("player.buffCapPm", custom.Describe(custom.Min));
        Assert.Equal("constant", custom.Describe(custom.Max));
        Assert.Equal("player.respawnMs", StatDefs.Get(Stat.RespawnMs).Describe(StatBound.Base()));
        Assert.Equal("base", custom.Describe(StatBound.Base()));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatDef_BaseBound_OnARowWhoseBaseNeedsAContentSet_Throws()
    {
        var bad = new StatDef(
            Stat.InventoryRows, StatUnit.Count, StatScope.Both, QualifierKind.Container, BaseSource.ContainerRows(), StatOps.Add,
            SetResolve.NotApplicable, StatRounding.Ceil, StatBound.Constant(1), StatBound.Base());

        Assert.Throws<InvalidOperationException>(() => bad.ResolveBounds(Table()));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatDef_BaseBound_WithAConstantBase_ResolvesToIt()
    {
        var def = new StatDef(
            Stat.ExtraJumps, StatUnit.Count, StatScope.Personal, QualifierKind.None, BaseSource.Constant(7), StatOps.Set,
            SetResolve.MinWins, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Base());

        Assert.Equal(7, def.ResolveBounds(Table()).Max);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatDef_ResolvedValueBeyondInt32_ThrowsOverflow()
    {
        var registry = new BalanceRegistry(ImmutableArray.Create(new BalanceRow("test", "big", BalanceUnit.Count, 0, 5_000_000_000)));
        BalanceTable table = BalanceTable.Build(
            registry,
            ImmutableArray.Create(new BalanceEntry("test", "test.big", 3_000_000_000, SourceLocation.BuiltIn("builtin/balance/test")))).Table!;
        var def = new StatDef(
            Stat.ExtraJumps, StatUnit.Count, StatScope.Personal, QualifierKind.None, BaseSource.Constant(0), StatOps.Add,
            SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Balance("test.big"));

        Assert.Throws<OverflowException>(() => def.ResolveBounds(table));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatBoundsResult_Get_OfAnUndefinedStat_Throws()
    {
        StatBoundsResult r = StatDefs.ResolveBounds(Table());

        Assert.Throws<ArgumentOutOfRangeException>(() => r.Get((Stat)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.Get((Stat)14));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatDefs_ResolveBounds_IsDeterministic()
    {
        string a = string.Join("|", StatDefs.ResolveBounds(Table(("throw.rangeMinPermille", 1600))).Validation.Diagnostics.Select(d => d.ToString()));
        string b = string.Join("|", StatDefs.ResolveBounds(Table(("throw.rangeMinPermille", 1600))).Validation.Diagnostics.Select(d => d.ToString()));

        Assert.Equal(a, b);
    }

    // ------------------------------------------------------------ CV-GEN-15 registration

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void CvGen15_IsRegistered_AsAnErrorAtTheBuildStage()
    {
        RuleSpec? rule = RuleRegistry.CreateDefault().Find("CV-GEN-15");

        Assert.NotNull(rule);
        Assert.Equal(RuleSeverity.Error, rule!.Severity);
        Assert.Equal(ValidationStage.Build, rule.Stage);
        Assert.Equal("content-data-definitions", rule.Owner);
        Assert.Equal("min_above_max", Assert.Single(rule.Clauses).Name);
    }

    // ------------------------------------------------------------ leaves

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatBound_Factories_SetOnlyTheirOwnField()
    {
        StatBound c = StatBound.Constant(255);
        StatBound k = StatBound.Balance("throw.rangeMaxPermille");
        StatBound b = StatBound.Base();

        Assert.Equal((StatBoundKind.Constant, 255, string.Empty), (c.Kind, c.Value, c.Key));
        Assert.Equal((StatBoundKind.Balance, 0, "throw.rangeMaxPermille"), (k.Kind, k.Value, k.Key));
        Assert.Equal((StatBoundKind.Base, 0, string.Empty), (b.Kind, b.Value, b.Key));
        Assert.Equal(StatBound.Constant(255), c);
        Assert.NotEqual(StatBound.Constant(254), c);
        Assert.Throws<ArgumentException>(() => StatBound.Balance(string.Empty));
        Assert.Throws<ArgumentException>(() => StatBound.Balance(null!));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void BaseSource_Factories_SetOnlyTheirOwnField()
    {
        BaseSource c = BaseSource.Constant(1000);
        BaseSource k = BaseSource.Balance("mail.lateValueRatio");

        Assert.Equal((BaseSourceKind.Constant, 1000, string.Empty), (c.Kind, c.Value, c.Key));
        Assert.Equal((BaseSourceKind.Balance, 0, "mail.lateValueRatio"), (k.Kind, k.Value, k.Key));
        Assert.Equal(BaseSourceKind.ContainerRows, BaseSource.ContainerRows().Kind);
        Assert.Equal(BaseSourceKind.MailKindMaxStack, BaseSource.MailKindMaxStack().Kind);
        Assert.Equal(string.Empty, BaseSource.ContainerRows().Key);
        Assert.Throws<ArgumentException>(() => BaseSource.Balance(string.Empty));
        Assert.Throws<ArgumentException>(() => BaseSource.Balance(null!));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void SupportEnums_HaveExplicitNeverReusedValues()
    {
        Assert.Equal(new[] { 1, 2, 3 }, Enum.GetValues<StatUnit>().Select(v => (int)v));
        Assert.Equal(new[] { 1, 2, 3 }, Enum.GetValues<StatScope>().Select(v => (int)v));
        Assert.Equal(new[] { 1, 2, 3 }, Enum.GetValues<QualifierKind>().Select(v => (int)v));
        Assert.Equal(new[] { 1, 2, 3 }, Enum.GetValues<SetResolve>().Select(v => (int)v));
        Assert.Equal(new[] { 1, 2, 3 }, Enum.GetValues<StatRounding>().Select(v => (int)v));
        Assert.Equal(new[] { 1, 2, 3, 4 }, Enum.GetValues<BaseSourceKind>().Select(v => (int)v));
        Assert.Equal(new[] { 1, 2, 3 }, Enum.GetValues<StatBoundKind>().Select(v => (int)v));
        Assert.Equal(new[] { 0, 1, 2, 4 }, Enum.GetValues<StatOps>().Select(v => (int)v));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void StatTypes_HoldNoStaticFields_AdrNoStaticState()
    {
        Type[] types = { typeof(StatDefs), typeof(StatSpelling), typeof(RuleFlagSpelling), typeof(StatDef), typeof(StatBound), typeof(BaseSource), typeof(StatBoundsResult) };
        foreach (Type t in types)
        {
            FieldInfo[] statics = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => !f.IsLiteral).ToArray();   // const fields are literals
            Assert.True(statics.Length == 0, t.Name + " has static fields: " + string.Join(",", statics.Select(f => f.Name)));
        }
    }

    // ------------------------------------------------------------ AC-3: RuleFlag

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void RuleFlag_HoldsTheFourteenV1Flags_WithExplicitValuesInCd18Order()
    {
        RuleFlag[] members = Enum.GetValues<RuleFlag>();

        Assert.Equal(14, members.Length);
        for (int i = 0; i < _ruleFlags.Length; i++)
        {
            Assert.Equal(_ruleFlags[i].Name, members[i].ToString());
            Assert.Equal(_ruleFlags[i].Value, (int)members[i]);
        }

        Assert.False(Enum.IsDefined((RuleFlag)0));
        Assert.False(Enum.IsDefined((RuleFlag)15));
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void RuleFlagSpelling_MatchesTheSnapshot_AndIsBijective()
    {
        var seen = new HashSet<string>();
        foreach ((string name, int value, string spelling) in _ruleFlags)
        {
            var flag = (RuleFlag)value;

            Assert.Equal(spelling, Utf8(RuleFlagSpelling.ToSpelling(flag)));
            Assert.Equal(Snake(name), spelling);
            Assert.True(seen.Add(spelling));
            Assert.True(RuleFlagSpelling.TryParse(Bytes(spelling), out RuleFlag back));
            Assert.Equal(flag, back);
        }

        Assert.Equal(14, seen.Count);
    }

    [Theory]
    [Trait("Id", "CDD-STAT-1")]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("RUSH_HOUR")]
    [InlineData("RushHour")]
    [InlineData("rush_hour\0")]
    [InlineData("rush")]
    public void RuleFlagSpelling_TryParse_RejectsNonSpellings(string text)
    {
        Assert.False(RuleFlagSpelling.TryParse(Bytes(text), out RuleFlag value));
        Assert.Equal(default, value);
    }

    [Fact]
    [Trait("Id", "CDD-STAT-1")]
    public void RuleFlagSpelling_ToSpelling_OfAnUndefinedValue_IsEmpty()
    {
        Assert.True(RuleFlagSpelling.ToSpelling((RuleFlag)0).IsEmpty);
        Assert.True(RuleFlagSpelling.ToSpelling((RuleFlag)15).IsEmpty);
    }
}
