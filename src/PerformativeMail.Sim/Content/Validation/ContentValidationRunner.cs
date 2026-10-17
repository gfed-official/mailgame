using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// Runs the stage 4 to 5 rules of a <see cref="RuleRegistry"/> over a built input and applies the severity policy
/// (GDD CD22 to CD24, ADR-0010 sections 4 and 5). Both <c>BuiltInContent</c> and the loader call it. Stateless and
/// deterministic: the same registry and input always give the same result, in the same order. Not thread-safe with
/// respect to a shared mutable input (inputs are immutable by contract). An exception thrown by a rule check
/// propagates unchanged (the CLI maps it to exit code 3).
/// <para>
/// Order of work: (1) the suppress policy over every def's <c>suppress</c> entries; (2) stage 4 then stage 5 rules in
/// registration order, skipping and counting rules whose declared inputs failed (prior failures from the input, then
/// Error findings that named an input key in an earlier stage); (3) applying valid <c>suppress</c> entries to
/// Warnings; (4) sorting. Errors are never suppressed.
/// </para>
/// </summary>
/// <example>
/// <code>
/// RuleRegistry registry = RuleRegistry.CreateDefault();
/// ValidationResult result = ContentValidationRunner.Run(registry, contentSetAdapter);
/// if (!result.CanBuild) { foreach (var d in result.Diagnostics) { log(d.Format(registry)); } }
/// </code>
/// </example>
public static class ContentValidationRunner
{
    /// <summary>
    /// Validates <paramref name="input"/> with the stage 4 and 5 rules of <paramref name="registry"/>.
    /// </summary>
    /// <param name="registry">Must contain the General rows CV-GEN-05 and CV-GEN-21 (the suppress policy reports through them).</param>
    /// <param name="input">The built input.</param>
    /// <exception cref="InvalidOperationException">The registry lacks CV-GEN-05 or CV-GEN-21, or a runnable rule's input type does not match <paramref name="input"/> (checked before any rule runs).</exception>
    public static ValidationResult Run(RuleRegistry registry, IValidationInput input)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(input);
        RuleSpec genWhy = RequirePolicyRule(registry, GeneralRuleIds.SuppressWithoutWhy);
        RuleSpec genTarget = RequirePolicyRule(registry, GeneralRuleIds.SuppressBadTarget);
        RequireMatchingInputType(registry, input);

        ImmutableArray<DefRecord> defs = input.Defs.IsDefault ? ImmutableArray<DefRecord>.Empty : input.Defs;
        var found = new List<ContentDiagnostic>();
        var skipped = new List<SkippedPair>();
        var failed = new List<FailedInput>();
        if (!input.PriorFailures.IsDefault)
        {
            failed.AddRange(input.PriorFailures);
        }

        // 1. Suppress policy: malformed entries are errors, found before any rule runs.
        foreach (DefRecord def in defs)
        {
            foreach (SuppressEntry entry in EntriesOf(def))
            {
                CheckSuppressEntry(registry, genWhy, genTarget, def.Def, entry, found);
            }
        }

        // 2. Rules, stage by stage. Error findings of a stage block later-stage rules that read the same input.
        RunStage(registry, input, ValidationStage.References, found, skipped, failed);
        RunStage(registry, input, ValidationStage.WholeSet, found, skipped, failed);

        // 3. Apply valid suppress entries to Warnings; 4. sort and package.
        bool[] silenced = new bool[found.Count];
        int unmatched = ApplySuppress(registry, defs, found, silenced);
        return Assemble(found, silenced, skipped, unmatched);
    }

    private static ImmutableArray<SuppressEntry> EntriesOf(DefRecord def) =>
        def.Suppress.IsDefault ? ImmutableArray<SuppressEntry>.Empty : def.Suppress;

    /// <summary>Fails before any rule runs if a runnable rule cannot read this input, so a mismatch never leaves a half-run.</summary>
    private static void RequireMatchingInputType(RuleRegistry registry, IValidationInput input)
    {
        foreach (RuleSpec rule in registry.Rules)
        {
            if (IsRunnable(rule) && !rule.Accepts(input))
            {
                throw new InvalidOperationException(
                    "Rule " + rule.Id + " cannot read an input of type " + input.GetType().Name + ".");
            }
        }
    }

    private static bool IsRunnable(RuleSpec rule) =>
        rule.Status == RuleStatus.Active && rule.HasCheck &&
        rule.Stage is ValidationStage.References or ValidationStage.WholeSet;

    /// <summary>
    /// Marks the Warnings silenced by valid <c>suppress</c> entries in <paramref name="silenced"/> (Errors are never
    /// touched) and returns how many valid entries matched nothing.
    /// </summary>
    private static int ApplySuppress(RuleRegistry registry, ImmutableArray<DefRecord> defs, List<ContentDiagnostic> found, bool[] silenced)
    {
        int unmatched = 0;
        foreach (DefRecord def in defs)
        {
            foreach (SuppressEntry entry in EntriesOf(def))
            {
                if (IsValidWarningSuppress(registry, entry) && !SilenceMatches(def.Def, entry, found, silenced))
                {
                    unmatched++;
                }
            }
        }

        return unmatched;
    }

    private static bool SilenceMatches(DefRef def, SuppressEntry entry, List<ContentDiagnostic> found, bool[] silenced)
    {
        bool matched = false;
        for (int i = 0; i < found.Count; i++)
        {
            ContentDiagnostic d = found[i];
            if (d.Severity == RuleSeverity.Warning && d.Def == def &&
                string.Equals(d.RuleId, entry.Rule, StringComparison.Ordinal))
            {
                silenced[i] = true;
                matched = true;
            }
        }

        return matched;
    }

    private static ValidationResult Assemble(List<ContentDiagnostic> found, bool[] silenced, List<SkippedPair> skipped, int unmatched)
    {
        var kept = new List<ContentDiagnostic>(found.Count);
        int suppressed = 0;
        for (int i = 0; i < found.Count; i++)
        {
            if (silenced[i])
            {
                suppressed++;
            }
            else
            {
                kept.Add(found[i]);
            }
        }

        ContentDiagnostic[] diagnostics = kept.ToArray();
        DiagnosticOrder.Sort(diagnostics);
        SkippedPair[] skippedArray = skipped.ToArray();
        if (skippedArray.Length > 1)
        {
            StableSort.Sort<SkippedPair, SkippedComparer>(skippedArray, new SkippedPair[skippedArray.Length], new SkippedComparer());
        }

        return new ValidationResult(
            ImmutableArray.Create(diagnostics), ImmutableArray.Create(skippedArray), suppressed, unmatched);
    }

    private static RuleSpec RequirePolicyRule(RuleRegistry registry, string id) =>
        registry.Find(id) ?? throw new InvalidOperationException(
            "The registry must contain " + id + " (include Rules.General.Rules); the suppress policy reports through it.");

    private static bool IsValidWarningSuppress(RuleRegistry registry, SuppressEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Rule) || string.IsNullOrWhiteSpace(entry.Why))
        {
            return false;
        }

        RuleSpec? target = registry.Find(entry.Rule);
        return target is not null && target.Status == RuleStatus.Active && target.Severity == RuleSeverity.Warning;
    }

    private static void CheckSuppressEntry(
        RuleRegistry registry,
        RuleSpec genWhy,
        RuleSpec genTarget,
        DefRef def,
        SuppressEntry entry,
        List<ContentDiagnostic> found)
    {
        if (string.IsNullOrWhiteSpace(entry.Rule))
        {
            found.Add(new ContentDiagnostic(
                genWhy.Id, genWhy.Severity, entry.Location, def, "(missing)", "suppress[].rule: a Warning rule id"));
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Why))
        {
            found.Add(new ContentDiagnostic(
                genWhy.Id, genWhy.Severity, entry.Location, def, "suppress " + entry.Rule + " without why", "suppress[].why: a non-empty reason"));
        }

        RuleSpec? target = registry.Find(entry.Rule);
        if (target is null || target.Status != RuleStatus.Active)
        {
            found.Add(new ContentDiagnostic(
                genTarget.Id, genTarget.Severity, entry.Location, def, entry.Rule, "the id of a registered Warning rule"));
        }
        else if (target.Severity == RuleSeverity.Error)
        {
            found.Add(new ContentDiagnostic(
                genTarget.Id, genTarget.Severity, entry.Location, def, entry.Rule, "a Warning rule id; " + entry.Rule + " is an Error and cannot be suppressed"));
        }
    }

    private static void RunStage(
        RuleRegistry registry,
        IValidationInput input,
        ValidationStage stage,
        List<ContentDiagnostic> found,
        List<SkippedPair> skipped,
        List<FailedInput> failed)
    {
        int firstOfStage = found.Count;
        foreach (RuleSpec rule in registry.Rules)
        {
            if (rule.Stage != stage || !IsRunnable(rule))
            {
                continue;
            }

            // A failure with no def (a whole file or set) skips the rule outright: one pair, no run.
            FailedInput? wholeCause = FindWholeRuleFailure(rule, failed);
            if (wholeCause is not null)
            {
                AddSkip(skipped, rule.Id, DefRef.None, wholeCause);
                continue;
            }

            // Otherwise the rule runs, but not for the defs whose declared inputs failed (counted once per def).
            List<DefRef> blocked = CollectBlockedDefs(rule, failed, skipped);
            rule.Run(input, new RuleContext(rule, found, blocked));
        }

        PromoteErrorsToFailures(found, firstOfStage, failed);
    }

    private static FailedInput? FindWholeRuleFailure(RuleSpec rule, List<FailedInput> failed)
    {
        foreach (FailedInput f in failed)
        {
            if (f.Def.IsNone && DeclaresInput(rule, f.Input))
            {
                return f;
            }
        }

        return null;
    }

    private static List<DefRef> CollectBlockedDefs(RuleSpec rule, List<FailedInput> failed, List<SkippedPair> skipped)
    {
        var blocked = new List<DefRef>();
        foreach (FailedInput f in failed)
        {
            if (DeclaresInput(rule, f.Input) && !blocked.Contains(f.Def))
            {
                blocked.Add(f.Def);
                AddSkip(skipped, rule.Id, f.Def, f);
            }
        }

        return blocked;
    }

    /// <summary>Errors that named an input now block later-stage rules reading that input.</summary>
    private static void PromoteErrorsToFailures(List<ContentDiagnostic> found, int firstOfStage, List<FailedInput> failed)
    {
        for (int i = firstOfStage; i < found.Count; i++)
        {
            ContentDiagnostic d = found[i];
            if (d.Severity == RuleSeverity.Error && d.InputKey is not null)
            {
                failed.Add(new FailedInput(d.InputKey, d.Def, d.Location.Path));
            }
        }
    }

    private static bool DeclaresInput(RuleSpec rule, string input)
    {
        foreach (string declared in rule.Inputs)
        {
            if (string.Equals(declared, input, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddSkip(List<SkippedPair> skipped, string ruleId, DefRef def, FailedInput cause) =>
        skipped.Add(new SkippedPair(ruleId, def, cause.Input, cause.File));

    private readonly struct SkippedComparer : IComparer<SkippedPair>
    {
        public int Compare(SkippedPair? x, SkippedPair? y)
        {
            if (x is null || y is null)
            {
                return ReferenceEquals(x, y) ? 0 : (x is null ? -1 : 1);
            }

            int c = string.CompareOrdinal(x.RuleId, y.RuleId);
            if (c != 0)
            {
                return c;
            }

            c = DefRef.Compare(x.Def, y.Def);
            if (c != 0)
            {
                return c;
            }

            c = string.CompareOrdinal(x.BlockingInput, y.BlockingInput);
            return c != 0 ? c : string.CompareOrdinal(x.BlockingFile, y.BlockingFile);
        }
    }
}
