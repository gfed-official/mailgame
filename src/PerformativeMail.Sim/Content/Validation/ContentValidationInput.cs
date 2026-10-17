using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// The adapter that presents a candidate <see cref="ContentSet"/> to the stage 4 to 5 rule runner (TD-037 item 3). Real
/// rules are created as <c>RuleSpec.Create&lt;ContentValidationInput&gt;</c> and read <see cref="Candidate"/>; the set itself
/// is never a rule's <c>TInput</c>, so the loader can add its metadata (suppress entries, prior failures, source
/// locations) to this adapter without touching the set. <c>suppress</c> entries that belong to no def (the balance files')
/// ride in one extra <see cref="DefRecord"/> addressed by <see cref="DefRef.None"/>. Internal: the candidate is released only through
/// <see cref="ValidationResult.AcceptedSet{T}"/>. Immutable; safe from any thread.
/// </summary>
/// <example>
/// <code>
/// static void CheckKinds(ContentValidationInput input, RuleContext ctx)
/// {
///     foreach (MailKindDef kind in input.Candidate.MailKinds.AsSpan()) { /* ctx.Report(...) */ }
/// }
/// </code>
/// </example>
public sealed class ContentValidationInput : IValidationInput
{
    internal ContentValidationInput(
        ContentSet candidate,
        ImmutableArray<FailedInput> priorFailures = default,
        ImmutableArray<PendingReference> references = default,
        ImmutableArray<SuppressEntry> setSuppress = default)
    {
        References = references.IsDefault ? ImmutableArray<PendingReference>.Empty : references;
        Candidate = candidate;
        PriorFailures = priorFailures.IsDefault ? ImmutableArray<FailedInput>.Empty : priorFailures;
        Defs = BuildRecords(candidate, setSuppress.IsDefault ? ImmutableArray<SuppressEntry>.Empty : setSuppress);
    }

    /// <summary>The candidate set under validation (not yet accepted).</summary>
    internal ContentSet Candidate { get; }

    /// <summary>The by-id references still to be resolved (CV-GEN-01); empty for built-in content, whose defs hold resolved indices.</summary>
    public ImmutableArray<PendingReference> References { get; }

    /// <inheritdoc />
    public ImmutableArray<DefRecord> Defs { get; }

    /// <inheritdoc />
    public ImmutableArray<FailedInput> PriorFailures { get; }

    /// <summary>The built-in source location of a def: <c>builtin/&lt;folder&gt;/&lt;id&gt;</c>.</summary>
    /// <param name="folder">The def folder.</param>
    /// <param name="id">The def id.</param>
    public static SourceLocation LocationOf(string folder, string id) => SourceLocation.BuiltIn("builtin/" + folder + "/" + id);

    /// <summary>
    /// The location of the set-level record that carries <c>suppress</c> entries for findings that belong to no def (a
    /// balance-key rule such as CV-GRD-07 reports against <see cref="DefRef.None"/>): <c>builtin/balance</c>.
    /// </summary>
    public static SourceLocation SetLocation => SourceLocation.BuiltIn("builtin/balance");

    private static ImmutableArray<DefRecord> BuildRecords(ContentSet set, ImmutableArray<SuppressEntry> setSuppress)
    {
        ImmutableArray<DefRecord>.Builder b = ImmutableArray.CreateBuilder<DefRecord>();
        Add(b, DefType.Container, "containers", set.Containers);
        Add(b, DefType.Item, "items", set.Items);
        Add(b, DefType.MailKind, "mail_kinds", set.MailKinds);
        Add(b, DefType.DestinationType, "destination_types", set.DestinationTypes);
        Add(b, DefType.MailMix, "mail_mixes", set.MailMixes);
        if (!setSuppress.IsEmpty)
        {
            b.Add(new DefRecord(DefRef.None, SetLocation, setSuppress));
        }

        return b.ToImmutable();
    }

    private static void Add<T>(ImmutableArray<DefRecord>.Builder b, DefType type, string folder, DefTable<T> table)
        where T : IDef
    {
        foreach (T def in table.AsSpan())
        {
            b.Add(new DefRecord(new DefRef(type, def.Id), LocationOf(folder, def.Id), ImmutableArray<SuppressEntry>.Empty));
        }
    }
}
