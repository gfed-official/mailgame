using System.Collections.Immutable;
using System.Globalization;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Stats;

/// <summary>What <see cref="StatDefs.ResolveBounds"/> produced: per-stat bounds and the CV-GEN-15 findings.</summary>
/// <param name="Validation">The findings (CV-GEN-15 per stat whose min exceeds its max); <see cref="ValidationResult.CanBuild"/> is false when any.</param>
/// <param name="Bounds">The resolved bounds, one per stat in <see cref="StatDefs.Count"/> order (position i is stat value i+1), including any with min above max.</param>
/// <example>
/// <code>
/// StatBoundsResult r = StatDefs.ResolveBounds(table);
/// if (r.Validation.CanBuild) { StatBounds b = r.Get(Stat.ThrowRangePermille); }
/// </code>
/// </example>
public sealed record StatBoundsResult(ValidationResult Validation, ImmutableArray<StatBounds> Bounds)
{
    /// <summary>The bounds of one stat.</summary>
    /// <param name="stat">A defined stat.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stat"/> is not a defined member.</exception>
    public StatBounds Get(Stat stat)
    {
        int value = (int)stat;
        if (value < 1 || value > Bounds.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(stat));
        }

        return Bounds[value - 1];
    }
}

/// <summary>
/// The M-1a <see cref="StatDef"/> table (content-data CD12 rows 1 to 13; story 006): code, not data. It is a switch over
/// the closed <see cref="Stat"/> enum with no static fields (ADR-0003 section 4 holds trivially; there is nothing to
/// mark as an immutable table), and the rows are plain readonly values, so reading one allocates nothing. Rows 14 and
/// on are appended by their reading systems' epics. Design clamps are balance keys named here by string and resolved
/// against a <c>BalanceTable</c> (<see cref="ResolveBounds"/>); those keys are registered in
/// <see cref="BalanceKeys"/> and are PROPOSED until each owner lists them (OQ-CDD-18). Correctness bounds are code
/// constants: <c>MailMaxStack</c> at most 255 (entry <c>count</c> is <c>uint8</c>), <c>WeightPenaltyPm</c> and
/// <c>LateValueRatioPm</c> 0 to 1000, every min at least 0. Stateless; safe from any thread.
/// </summary>
/// <example>
/// <code>
/// StatDef def = StatDefs.Get(Stat.MailMaxStack);   // Floor, Mul, clamp 1..255 (code)
/// StatBoundsResult r = StatDefs.ResolveBounds(balanceTable);
/// StatBounds range = r.Get(Stat.ThrowRangePermille);   // 1000..1500 with the default balance values
/// </code>
/// </example>
public static class StatDefs
{
    private const string DeliverInsertPmMax = BalanceKeys.Delivery.Owner + "." + BalanceKeys.Delivery.InsertPmMaxKey;
    private const string BuffCapPm = BalanceKeys.Player.Owner + "." + BalanceKeys.Player.BuffCapPmKey;
    private const string MaxExtraJumps = BalanceKeys.Player.Owner + "." + BalanceKeys.Player.MaxExtraJumpsKey;
    private const string RespawnMs = BalanceKeys.Player.Owner + "." + BalanceKeys.Player.RespawnMsKey;
    private const string RespawnMinMs = BalanceKeys.Player.Owner + "." + BalanceKeys.Player.RespawnMinMsKey;
    private const string InventoryMaxRows = BalanceKeys.Inventory.Owner + "." + BalanceKeys.Inventory.MaxRowsKey;
    private const string PayMultMaxPm = BalanceKeys.Mail.Owner + "." + BalanceKeys.Mail.PayMultMaxPmKey;
    private const string PenaltyMultMaxPm = BalanceKeys.Mail.Owner + "." + BalanceKeys.Mail.PenaltyMultMaxPmKey;
    private const string LateValueRatio = BalanceKeys.Mail.Owner + "." + BalanceKeys.Mail.LateValueRatioKey;
    private const string ThrowRangeMin = BalanceKeys.Throw.Owner + "." + BalanceKeys.Throw.RangeMinPermilleKey;
    private const string ThrowRangeMax = BalanceKeys.Throw.Owner + "." + BalanceKeys.Throw.RangeMaxPermilleKey;

    /// <summary>The number of M-1a rows (stat values 1 to <see cref="Count"/>).</summary>
    public const int Count = 13;

    /// <summary>The CV-GEN-15 rule id (a <see cref="StatDef"/> whose min exceeds its max after its balance keys resolve).</summary>
    public const string InvertedBoundsRuleId = GeneralRuleIds.StatBoundsInverted;

    /// <summary>The row of a stat.</summary>
    /// <param name="stat">A defined stat.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stat"/> is not a defined member.</exception>
    /// <example>
    /// <code>
    /// StatDef def = StatDefs.Get(Stat.ThrowRangePermille);
    /// </code>
    /// </example>
    public static StatDef Get(Stat stat) => TryGet(stat, out StatDef def) ? def : throw new ArgumentOutOfRangeException(nameof(stat));

    /// <summary>The row of a stat, or false for a value that is not a defined member.</summary>
    /// <param name="stat">Any value.</param>
    /// <param name="def">The row, or <c>default</c>.</param>
    /// <example>
    /// <code>
    /// bool ok = StatDefs.TryGet((Stat)99, out StatDef def);   // false
    /// </code>
    /// </example>
    public static bool TryGet(Stat stat, out StatDef def)
    {
        switch (stat)
        {
            case Stat.DeliverInsertPm:
                def = Row(stat, StatUnit.Permille, StatScope.Personal, QualifierKind.None, BaseSource.Constant(0), StatOps.Add, SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Balance(DeliverInsertPmMax));
                return true;
            case Stat.MoveSpeedAddPm:
                def = Row(stat, StatUnit.Permille, StatScope.Personal, QualifierKind.None, BaseSource.Constant(0), StatOps.Add, SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Balance(BuffCapPm));
                return true;
            case Stat.ExtraJumps:
                def = Row(stat, StatUnit.Count, StatScope.Personal, QualifierKind.None, BaseSource.Constant(0), StatOps.Add, SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Balance(MaxExtraJumps));
                return true;
            case Stat.RespawnMs:
                def = Row(stat, StatUnit.Ms, StatScope.Personal, QualifierKind.None, BaseSource.Balance(RespawnMs), StatOps.Set, SetResolve.MinWins, StatRounding.HalfUp, StatBound.Balance(RespawnMinMs), StatBound.Base());
                return true;
            case Stat.InventoryRows:
                def = Row(stat, StatUnit.Count, StatScope.Both, QualifierKind.Container, BaseSource.ContainerRows(), StatOps.Add | StatOps.Mul, SetResolve.NotApplicable, StatRounding.Ceil, StatBound.Constant(1), StatBound.Balance(InventoryMaxRows));
                return true;
            case Stat.MailMaxStack:
                def = Row(stat, StatUnit.Count, StatScope.Team, QualifierKind.MailKind, BaseSource.MailKindMaxStack(), StatOps.Mul, SetResolve.NotApplicable, StatRounding.Floor, StatBound.Constant(1), StatBound.Constant(255));
                return true;
            case Stat.WeightPenaltyPm:
                def = Row(stat, StatUnit.Permille, StatScope.Personal, QualifierKind.None, BaseSource.Constant(1000), StatOps.Mul, SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Constant(1000));
                return true;
            case Stat.MailValueMultPm:
                def = Row(stat, StatUnit.Permille, StatScope.Team, QualifierKind.MailKind, BaseSource.Constant(1000), StatOps.Mul, SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Balance(PayMultMaxPm));
                return true;
            case Stat.ThrownCorrectPayMultPm:
                // CD12 gives 0..5000 without naming a key; mail.payMultMaxPm is the CD12 "Which clamp bounds are data" key. FLAGGED for owner confirmation.
                def = Row(stat, StatUnit.Permille, StatScope.Personal, QualifierKind.None, BaseSource.Constant(1000), StatOps.Mul, SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Balance(PayMultMaxPm));
                return true;
            case Stat.LateValueRatioPm:
                def = Row(stat, StatUnit.Permille, StatScope.Team, QualifierKind.None, BaseSource.Balance(LateValueRatio), StatOps.Set, SetResolve.MaxWins, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Constant(1000));
                return true;
            case Stat.MisdeliveryPenaltyMultPm:
                def = Row(stat, StatUnit.Permille, StatScope.Team, QualifierKind.None, BaseSource.Constant(1000), StatOps.Mul, SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Balance(PenaltyMultMaxPm));
                return true;
            case Stat.MisdeliveryComplaintMultPm:
                def = Row(stat, StatUnit.Permille, StatScope.Team, QualifierKind.None, BaseSource.Constant(1000), StatOps.Mul, SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Constant(0), StatBound.Balance(PenaltyMultMaxPm));
                return true;
            case Stat.ThrowRangePermille:
                def = Row(stat, StatUnit.Permille, StatScope.Personal, QualifierKind.None, BaseSource.Constant(1000), StatOps.Add, SetResolve.NotApplicable, StatRounding.HalfUp, StatBound.Balance(ThrowRangeMin), StatBound.Balance(ThrowRangeMax));
                return true;
            default:
                def = default;
                return false;
        }
    }

    /// <summary>All M-1a rows in stat-value order (a new array per call; not for per-tick code).</summary>
    /// <example>
    /// <code>
    /// foreach (StatDef def in StatDefs.All()) { /* 13 rows */ }
    /// </code>
    /// </example>
    public static ImmutableArray<StatDef> All()
    {
        ImmutableArray<StatDef>.Builder rows = ImmutableArray.CreateBuilder<StatDef>(Count);
        for (int value = 1; value <= Count; value++)
        {
            rows.Add(Get((Stat)value));
        }

        return rows.MoveToImmutable();
    }

    /// <summary>
    /// Resolves every row's bounds against <paramref name="table"/> and reports CV-GEN-15 (the rule's severity, an Error)
    /// for each stat whose min exceeds its max, naming both bounds (the balance key names, or the constant). Findings are
    /// in stat order. Deterministic; no I/O. Per-qualifier bases (container rows, mail-kind <c>maxStack</c>) are story 014
    /// and base-in-range is CV-GEN-17 (story 009); neither is checked here.
    /// </summary>
    /// <param name="table">A table whose registry holds every clamp key of the table (the production registry, or a test one that includes those owner rows).</param>
    /// <param name="rules">Supplies CV-GEN-15's severity; default is <see cref="RuleRegistry.CreateDefault"/>.</param>
    /// <exception cref="ArgumentException">The registry or <paramref name="rules"/> lacks a needed key or rule.</exception>
    /// <example>
    /// <code>
    /// StatBoundsResult r = StatDefs.ResolveBounds(table);
    /// bool ok = r.Validation.CanBuild;
    /// </code>
    /// </example>
    public static StatBoundsResult ResolveBounds(BalanceTable table, RuleRegistry? rules = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        RuleSeverity severity = (rules ?? RuleRegistry.CreateDefault()).Find(GeneralRuleIds.StatBoundsInverted)?.Severity
            ?? throw new ArgumentException("The rule registry lacks " + GeneralRuleIds.StatBoundsInverted, nameof(rules));
        ImmutableArray<StatBounds>.Builder bounds = ImmutableArray.CreateBuilder<StatBounds>(Count);
        ImmutableArray<ContentDiagnostic>.Builder found = ImmutableArray.CreateBuilder<ContentDiagnostic>();
        for (int value = 1; value <= Count; value++)
        {
            StatDef def = Get((Stat)value);
            StatBounds b = def.ResolveBounds(table);
            bounds.Add(b);
            if (!b.IsValid)
            {
                found.Add(Inverted(def, b, severity));
            }
        }

        var result = new ValidationResult(found.ToImmutable(), ImmutableArray<SkippedPair>.Empty, 0, 0);
        return new StatBoundsResult(result, bounds.MoveToImmutable());
    }

    private static ContentDiagnostic Inverted(StatDef def, StatBounds b, RuleSeverity severity)
    {
        string min = Num(b.Min) + " (" + def.Describe(def.Min) + ")";
        string max = Num(b.Max) + " (" + def.Describe(def.Max) + ")";
        string statName = System.Text.Encoding.UTF8.GetString(StatSpelling.ToSpelling(def.Stat));
        return new ContentDiagnostic(
            GeneralRuleIds.StatBoundsInverted,
            severity,
            SourceLocation.BuiltIn("builtin/statdef/" + statName),
            DefRef.None,
            "min " + min + " above max " + max,
            "min <= max",
            def.Min.Kind == StatBoundKind.Balance ? def.Min.Key : def.Max.Key);
    }

    private static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static StatDef Row(
        Stat stat, StatUnit unit, StatScope scope, QualifierKind qualifier, BaseSource baseSource, StatOps ops,
        SetResolve setResolve, StatRounding rounding, StatBound min, StatBound max) =>
        new(stat, unit, scope, qualifier, baseSource, ops, setResolve, rounding, min, max);
}
