using System.Collections.Immutable;
using System.Globalization;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Validation;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Balance;

/// <summary>What <see cref="BalanceTable.Build"/> produced: the findings and, when none is an Error, the table.</summary>
/// <param name="Validation">All findings (Errors and Warnings), sorted by <see cref="DiagnosticOrder"/>.</param>
/// <param name="Table">The built table, or null when <see cref="ValidationResult.CanBuild"/> is false.</param>
/// <example>
/// <code>
/// BalanceBuildResult r = BalanceTable.Build(registry, entries);
/// if (r.Table is { } table) { long v = table.Get(key); }
/// </code>
/// </example>
public sealed record BalanceBuildResult(ValidationResult Validation, BalanceTable? Table);

/// <summary>
/// The validated balance values of a content set: one <c>long</c> per registry row, read by <see cref="BalanceKey"/>
/// handle with an array index (no string lookup, no allocation). Built once by <see cref="Build"/>, which reports
/// CV-GEN-13 (duplicate key, unregistered key, key outside its registry owner's file, value outside the hard range),
/// CV-GEN-20 (Warning: outside the data-file <c>safe</c> range, still builds) and CV-GEN-05 (a registered key with no
/// value; there is no implicit default). Immutable; safe to read from any thread. Story 013 hashes
/// <see cref="Values"/> together with <see cref="Registry"/> rows in their ordinal order.
/// </summary>
/// <example>
/// <code>
/// BalanceRegistry registry = BalanceKeys.CreateRegistry();
/// BalanceBuildResult r = BalanceTable.Build(registry, BalanceKeys.CreateDefaultEntries());
/// SimKeys keys = BalanceKeys.Sim.Resolve(r.Table!.Registry);
/// long capacity = r.Table!.Get(keys.OpQueueCapacity);   // 4096
/// </code>
/// </example>
public sealed class BalanceTable
{
    private readonly long[] _values;
    private readonly int _stamp;

    private BalanceTable(BalanceRegistry registry, long[] values)
    {
        Registry = registry;
        _values = values;
        _stamp = registry.Stamp;
    }

    /// <summary>The registry this table was built against (its rows are in the same order as <see cref="Values"/>).</summary>
    public BalanceRegistry Registry { get; }

    /// <summary>The values in ordinal <c>owner.key</c> order, for hashing and iteration. Allocation free.</summary>
    public ReadOnlySpan<long> Values => _values;

    /// <summary>
    /// The value of <paramref name="key"/>: a stamp compare and one array read, 0 B. <see cref="BalanceKey.None"/> and a
    /// handle issued by a registry with a different layout are SIM_ASSERT failures
    /// (<see cref="ContentAssertCode.BalanceKeyNone"/>, <see cref="ContentAssertCode.BalanceKeyForeign"/>) and otherwise
    /// throw <see cref="ArgumentException"/>, so a stale handle can never read another row.
    /// </summary>
    /// <param name="key">A handle from <see cref="Registry"/> (see <c>BalanceKeys.Sim.Resolve</c>).</param>
    /// <exception cref="ArgumentException">The handle is None or foreign.</exception>
    public long Get(BalanceKey key)
    {
        SimAssert.That(!key.IsNone, ContentAssertCode.BalanceKeyNone);
        SimAssert.That(key.Stamp == _stamp, ContentAssertCode.BalanceKeyForeign);
        if (key.Stamp != _stamp)
        {
            ThrowNotIssuedHere();
        }

        return _values[key.Index];
    }

    /// <summary>The value of <paramref name="key"/> as an <c>int</c> (every registered hard range fits; pinned by a test).</summary>
    /// <param name="key">A handle from this table's registry.</param>
    /// <exception cref="OverflowException">A future wider row exceeds <c>int</c>.</exception>
    internal int GetInt(BalanceKey key) => checked((int)Get(key));

    private static void ThrowNotIssuedHere() =>
        throw new ArgumentException("The balance key is None or was issued by a different registry.", "key");

    /// <summary>
    /// Validates <paramref name="entries"/> against <paramref name="registry"/> and builds the table. Findings are
    /// reported per entry in input order, then CV-GEN-05 per registered key that no entry supplied (in ordinal order);
    /// the result is sorted by <see cref="DiagnosticOrder"/>. One mistake gives one CV-GEN-13: a key supplied more than
    /// once is reported once per extra occurrence (the first value stands) and its file is not also checked against the
    /// owner; a key outside its owner's file or its hard range still counts as supplied (no CV-GEN-05 cascade). Deterministic; no I/O.
    /// </summary>
    /// <param name="registry">The keys to build for.</param>
    /// <param name="entries">The supplied values, from every balance file or built-in table.</param>
    /// <param name="rules">Supplies the severities of CV-GEN-05, 13 and 20; default is <see cref="RuleRegistry.CreateDefault"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="rules"/> lacks one of the three rules.</exception>
    public static BalanceBuildResult Build(BalanceRegistry registry, ImmutableArray<BalanceEntry> entries, RuleRegistry? rules = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ImmutableArray<BalanceEntry> all = entries.IsDefault ? ImmutableArray<BalanceEntry>.Empty : entries;
        var run = new BuildRun(registry, rules ?? RuleRegistry.CreateDefault(), all);
        foreach (BalanceEntry entry in all)
        {
            run.Add(entry);
        }

        run.ReportMissing();
        ValidationResult result = new(DiagnosticOrder.Sorted(run.Found.ToImmutable()), ImmutableArray<SkippedPair>.Empty, 0, 0);
        return new BalanceBuildResult(result, result.CanBuild ? new BalanceTable(registry, run.Values) : null);
    }

    private static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);

    private sealed class BuildRun
    {
        private readonly BalanceRegistry _registry;
        private readonly bool[] _supplied;
        private readonly int[] _occurrences;
        private readonly RuleSeverity _key;
        private readonly RuleSeverity _safe;
        private readonly RuleSeverity _missing;

        public BuildRun(BalanceRegistry registry, RuleRegistry rules, ImmutableArray<BalanceEntry> entries)
        {
            _registry = registry;
            _supplied = new bool[registry.Count];
            _occurrences = new int[registry.Count];
            Values = new long[registry.Count];
            _key = SeverityOf(rules, GeneralRuleIds.BalanceKey);
            _safe = SeverityOf(rules, GeneralRuleIds.BalanceOutsideSafe);
            _missing = SeverityOf(rules, GeneralRuleIds.RequiredFieldMissing);
            foreach (BalanceEntry e in entries)
            {
                if (registry.TryFind(e.Name, out BalanceKey k))
                {
                    _occurrences[k.Index]++;
                }
            }
        }

        public long[] Values { get; }

        public ImmutableArray<ContentDiagnostic>.Builder Found { get; } = ImmutableArray.CreateBuilder<ContentDiagnostic>();

        private static RuleSeverity SeverityOf(RuleRegistry rules, string id) =>
            rules.Find(id)?.Severity ?? throw new ArgumentException("The rule registry lacks " + id, nameof(rules));

        public void Add(BalanceEntry entry)
        {
            if (!_registry.TryFind(entry.Name, out BalanceKey key))
            {
                Report(GeneralRuleIds.BalanceKey, _key, entry.Where, entry.Name, "a key in the BalanceKey registry", entry.Name);
                return;
            }

            if (_supplied[key.Index])
            {
                Report(GeneralRuleIds.BalanceKey, _key, entry.Where, entry.Name + " supplied twice", "one source per key", entry.Name);
                return;
            }

            BalanceRow row = _registry[key];
            if (_occurrences[key.Index] == 1 && !string.Equals(entry.File, row.Owner, StringComparison.Ordinal))
            {
                Report(GeneralRuleIds.BalanceKey, _key, entry.Where, entry.Name + " in " + entry.File, "owner file " + row.Owner, entry.Name);
            }

            _supplied[key.Index] = true;
            CheckValue(entry, row, key.Index);
        }

        private void CheckValue(BalanceEntry entry, BalanceRow row, int index)
        {
            if (entry.Value < row.HardMin || entry.Value > row.HardMax)
            {
                Report(GeneralRuleIds.BalanceKey, _key, entry.Where, Num(entry.Value), Num(row.HardMin) + ".." + Num(row.HardMax), entry.Name);
                return;
            }

            Values[index] = entry.Value;
            if (entry.Safe is { } safe && (entry.Value < safe.Min || entry.Value > safe.Max))
            {
                Report(GeneralRuleIds.BalanceOutsideSafe, _safe, entry.Where, entry.Name + " = " + Num(entry.Value), Num(safe.Min) + ".." + Num(safe.Max), entry.Name);
            }
        }

        public void ReportMissing()
        {
            for (int i = 0; i < _supplied.Length; i++)
            {
                if (!_supplied[i])
                {
                    BalanceRow row = _registry.Rows[i];
                    Report(
                        GeneralRuleIds.RequiredFieldMissing,
                        _missing,
                        SourceLocation.BuiltIn("balance/" + row.Owner + ".jsonc"),
                        _registry.Names[i] + " absent",
                        "a value for " + _registry.Names[i] + " (" + Num(row.HardMin) + ".." + Num(row.HardMax) + "), no default",
                        _registry.Names[i]);
                }
            }
        }

        private void Report(string ruleId, RuleSeverity severity, SourceLocation where, string value, string allowed, string inputKey) =>
            Found.Add(new ContentDiagnostic(ruleId, severity, where, DefRef.None, value, allowed, inputKey));
    }
}
