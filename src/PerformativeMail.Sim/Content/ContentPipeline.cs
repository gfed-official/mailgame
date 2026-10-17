using System.Collections.Immutable;
using System.Globalization;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Stats;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// What <see cref="ContentPipeline.Build"/> produced: the findings of stages 3 to 5 and, when there are no Errors, the set.
/// </summary>
/// <param name="Validation">The merged findings (stat-bound CV-GEN-15 plus the stage 4 to 5 runner).</param>
/// <param name="Set">The accepted set, or null when <see cref="ValidationResult.CanBuild"/> is false.</param>
/// <example>
/// <code>
/// ContentBuildResult r = ContentPipeline.Build(table, containers, items, kinds, destinations, mixes, RuleRegistry.CreateDefault());
/// ContentSet? set = r.Set;
/// </code>
/// </example>
public sealed record ContentBuildResult(ValidationResult Validation, ContentSet? Set);

/// <summary>
/// Assembles a candidate <see cref="ContentSet"/> from def lists (in any order) and a built balance table, runs the stage 4
/// to 5 rule runner over an adapter of the candidate, and releases the set only through
/// <see cref="ValidationResult.AcceptedSet{T}"/> (TD-037 item 3). The candidate never leaves this class unvalidated. Both
/// <see cref="BuiltInContent"/> and the M0 loader use it, so they produce the same types by the same path. Stateless; safe
/// from any thread.
/// </summary>
/// <example>
/// <code>
/// ContentBuildResult r = ContentPipeline.Build(table, containers, items, kinds, destinations, mixes, RuleRegistry.CreateDefault());
/// if (r.Set is null) { /* report r.Validation.Diagnostics */ }
/// </code>
/// </example>
public static class ContentPipeline
{
    /// <summary>
    /// Checks the ids (CV-GEN-01 duplicates, CV-GEN-09 too many), sorts each def list into ordinal <c>DefIndex</c> order,
    /// resolves stat bounds, runs the stage 4 to 5 rules and releases the set. Duplicate ids and too many defs are
    /// reported as Errors (the set is then not released). Of two defs with the same id neither is kept; the type's other
    /// defs are kept. A type with a duplicate, and a type with too many defs (the raw count, duplicates included), is
    /// reported as a failed input: the rules that declare its table key are skipped and counted. A type with too many defs
    /// is also left empty.
    /// </summary>
    /// <param name="balance">A built balance table over <c>BalanceKeys.CreateRegistry()</c>.</param>
    /// <param name="containers">Container defs, any order.</param>
    /// <param name="items">Item defs, any order.</param>
    /// <param name="mailKinds">Mail kind defs, any order.</param>
    /// <param name="destinationTypes">Destination type defs, any order.</param>
    /// <param name="mailMixes">Mail mix defs, any order.</param>
    /// <param name="rules">The rule registry whose stage 3 to 5 rules apply.</param>
    /// <param name="references">By-id references still to be resolved (CV-GEN-01); default means none (built-in defs hold indices).</param>
    /// <exception cref="ArgumentNullException"><paramref name="balance"/> or <paramref name="rules"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// An id is empty (CV-GEN-02 is a loader schema rule, story 018: the caller reports it before calling; asserts first when
    /// SIM_ASSERT is on), the rule registry lacks the CV-GEN-01, CV-GEN-09 or CV-GEN-15 row, or the balance table's registry
    /// lacks a row an owner record reads (<c>FromTable</c>).
    /// </exception>
    /// <exception cref="OverflowException">An owner record's value does not fit an <c>int</c> (a future wider hard range).</exception>
    public static ContentBuildResult Build(
        BalanceTable balance,
        ImmutableArray<ContainerDef> containers,
        ImmutableArray<ItemDef> items,
        ImmutableArray<MailKindDef> mailKinds,
        ImmutableArray<DestinationTypeDef> destinationTypes,
        ImmutableArray<MailMixDef> mailMixes,
        RuleRegistry rules,
        ImmutableArray<PendingReference> references = default)
    {
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentNullException.ThrowIfNull(rules);
        StatBoundsResult bounds = StatDefs.ResolveBounds(balance, rules);
        RuleSeverity dup = SeverityOf(rules, GeneralRuleIds.IdsAndReferences);
        RuleSeverity many = SeverityOf(rules, GeneralRuleIds.TooManyDefs);
        var found = new List<ContentDiagnostic>();
        var failed = new List<FailedInput>();
        var candidate = new ContentSet(
            balance,
            bounds.Bounds,
            ContentIdChecks.Prepare(DefType.Container, "containers", ContentInputKeys.Containers, containers, dup, many, found, failed),
            ContentIdChecks.Prepare(DefType.Item, "items", ContentInputKeys.Items, items, dup, many, found, failed),
            ContentIdChecks.Prepare(DefType.MailKind, "mail_kinds", ContentInputKeys.MailKinds, mailKinds, dup, many, found, failed),
            ContentIdChecks.Prepare(DefType.DestinationType, "destination_types", ContentInputKeys.DestinationTypes, destinationTypes, dup, many, found, failed),
            ContentIdChecks.Prepare(DefType.MailMix, "mail_mixes", ContentInputKeys.MailMixes, mailMixes, dup, many, found, failed));
        ValidationResult runner = ContentValidationRunner.Run(
            rules, new ContentValidationInput(candidate, failed.ToImmutableArray(), references));
        ValidationResult merged = Merge(bounds.Validation.Diagnostics.AddRange(found), runner);
        return new ContentBuildResult(merged, merged.AcceptedSet(candidate));
    }

    private static RuleSeverity SeverityOf(RuleRegistry rules, string id) =>
        rules.Find(id)?.Severity ?? throw new ArgumentException("The rule registry lacks " + id, nameof(rules));

    /// <summary>The ids of <paramref name="defs"/> are assigned ordinal <c>DefIndex</c>es and the defs are placed by index.</summary>
    /// <param name="defs">Defs of one type, any order.</param>
    /// <exception cref="ArgumentException">An id is empty or duplicated (callers go through <see cref="ContentIdChecks"/> first, which removes duplicates).</exception>
    internal static DefTable<T> Sorted<T>(ImmutableArray<T> defs)
        where T : IDef
    {
        ImmutableArray<T> list = defs.IsDefault ? ImmutableArray<T>.Empty : defs;
        string[] ids = new string[list.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = list[i].Id;
        }

        DefAssignment assignment = DefIdAssigner.Assign(ids);
        if (assignment.Status != DefAssignmentStatus.Ok)
        {
            throw new ArgumentException("The def ids cannot be assigned (status " + ((int)assignment.Status).ToString(CultureInfo.InvariantCulture) + ").", nameof(defs));
        }

        T[] placed = new T[list.Length];
        for (int i = 0; i < placed.Length; i++)
        {
            placed[assignment.IndexOfInput[i].Value] = list[i];
        }

        return new DefTable<T>(System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(placed));
    }

    private static ValidationResult Merge(ImmutableArray<ContentDiagnostic> stage3, ValidationResult runner)
    {
        if (stage3.IsEmpty)
        {
            return runner;
        }

        ImmutableArray<ContentDiagnostic> all = DiagnosticOrder.Sorted(stage3.AddRange(runner.Diagnostics));
        return new ValidationResult(all, runner.Skipped, runner.SuppressedCount, runner.UnmatchedSuppressCount);
    }
}
