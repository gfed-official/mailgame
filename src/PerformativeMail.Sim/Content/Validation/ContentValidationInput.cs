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
    private readonly Dictionary<DefRef, SourceLocation>? _loaded;

    internal ContentValidationInput(
        ContentSet candidate,
        ImmutableArray<FailedInput> priorFailures = default,
        ImmutableArray<PendingReference> references = default,
        ImmutableArray<SuppressEntry> setSuppress = default,
        ImmutableArray<VerbDeclaration> verbDeclarations = default,
        ImmutableArray<DefRecord> loadedRecords = default)
    {
        References = references.IsDefault ? ImmutableArray<PendingReference>.Empty : references;
        Candidate = candidate;
        PriorFailures = priorFailures.IsDefault ? ImmutableArray<FailedInput>.Empty : priorFailures;
        _loaded = LocationIndex(loadedRecords);
        Defs = BuildRecords(candidate, setSuppress.IsDefault ? ImmutableArray<SuppressEntry>.Empty : setSuppress, loadedRecords);
        VerbDeclarations = verbDeclarations.IsDefault ? ImmutableArray<VerbDeclaration>.Empty : verbDeclarations;
    }

    /// <summary>The candidate set under validation (not yet accepted).</summary>
    internal ContentSet Candidate { get; }

    /// <summary>The by-id references still to be resolved (CV-GEN-01); empty for built-in content, whose defs hold resolved indices.</summary>
    public ImmutableArray<PendingReference> References { get; }

    /// <summary>
    /// The interaction verbs the defs declare, as CV-PLR-13 (PCM-P10-1) reads them; empty for built-in content and for the
    /// production pipeline at M-1a, since no def type carries a verb field yet. When one does, the loader fills this list from
    /// that field and the rule body is unchanged (see <see cref="VerbDeclaration"/>).
    /// </summary>
    public ImmutableArray<VerbDeclaration> VerbDeclarations { get; }

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

    /// <summary>
    /// The runner's findings with every synthesised <c>builtin/&lt;folder&gt;/&lt;id&gt;</c> location of a loaded def replaced by
    /// that def's real <c>path:line:column</c> (from its record). Rule bodies locate findings through <see cref="LocationOf"/>,
    /// so the swap happens once here rather than in every rule. Built-in content (no loaded records) is returned unchanged.
    /// Mail Dev default, pending Evan (TD-185 item 3): the swap is a post-run relocation keyed on (def, synthesised path), not
    /// a per-rule lookup.
    /// </summary>
    /// <param name="result">The runner's result over this input.</param>
    internal ValidationResult Relocate(ValidationResult result)
    {
        if (_loaded is null || result.Diagnostics.IsEmpty)
        {
            return result;
        }

        ImmutableArray<ContentDiagnostic>.Builder b = ImmutableArray.CreateBuilder<ContentDiagnostic>(result.Diagnostics.Length);
        bool moved = false;
        foreach (ContentDiagnostic d in result.Diagnostics)
        {
            if (!d.Def.IsNone && _loaded.TryGetValue(d.Def, out SourceLocation real)
                && d.Location == LocationOf(FolderOf(d.Def.Type), d.Def.Id))
            {
                b.Add(d with { Location = real });
                moved = true;
            }
            else
            {
                b.Add(d);
            }
        }

        return moved
            ? new ValidationResult(DiagnosticOrder.Sorted(b.MoveToImmutable()), result.Skipped, result.SuppressedCount, result.UnmatchedSuppressCount)
            : result;
    }

    /// <summary>The synthesised location folder of a def type (<c>builtin/&lt;folder&gt;/&lt;id&gt;</c>).</summary>
    internal static string FolderOf(DefType type) => type switch
    {
        DefType.Container => "containers",
        DefType.Item => "items",
        DefType.MailKind => "mail_kinds",
        DefType.DestinationType => "destination_types",
        _ => "mail_mixes",
    };

    /// <summary>Each loaded def's location by its ref; null when there are no loaded records (built-in content).</summary>
    internal static Dictionary<DefRef, SourceLocation>? LocationIndex(ImmutableArray<DefRecord> loaded)
    {
        if (loaded.IsDefault)
        {
            return null;
        }

        var index = new Dictionary<DefRef, SourceLocation>(loaded.Length);
        foreach (DefRecord r in loaded)
        {
            index[r.Def] = r.Location;
        }

        return index;
    }

    private static ImmutableArray<DefRecord> BuildRecords(ContentSet set, ImmutableArray<SuppressEntry> setSuppress, ImmutableArray<DefRecord> loaded)
    {
        ImmutableArray<DefRecord>.Builder b = ImmutableArray.CreateBuilder<DefRecord>();
        if (!loaded.IsDefault)
        {
            // Loaded content (TD-178 item 1): each def's real location and parsed suppress entries, instead of builtin/<folder>/<id>.
            b.AddRange(loaded);
        }
        else
        {
            Add(b, DefType.Container, FolderOf(DefType.Container), set.Containers);
            Add(b, DefType.Item, FolderOf(DefType.Item), set.Items);
            Add(b, DefType.MailKind, FolderOf(DefType.MailKind), set.MailKinds);
            Add(b, DefType.DestinationType, FolderOf(DefType.DestinationType), set.DestinationTypes);
            Add(b, DefType.MailMix, FolderOf(DefType.MailMix), set.MailMixes);
        }

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
