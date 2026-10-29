using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Balance;
using PerformativeMail.Sim.Content.Parsing;
using PerformativeMail.Sim.Content.Rules;
using PerformativeMail.Sim.Content.Sources;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// The content loader (ADR-0010 section 4, GDD CD21): <see cref="Load"/> runs the five stages over an
/// <see cref="IContentSource"/> and returns one immutable <see cref="ContentSet"/> or every reachable finding.
/// <list type="number">
/// <item>Each listed path is mapped to its def type by a fixed table (CD2): <c>items/containers.jsonc</c> is ContainerDef, any
/// other <c>items/*.jsonc</c> ItemDef; <c>mail/kinds.jsonc</c>, <c>mail/mix.jsonc</c> and <c>mail/destinations.jsonc</c> are
/// MailKindDef, MailMixDef and DestinationTypeDef; <c>balance/*.jsonc</c> is balance; everything under
/// <c>presentation/</c> is skipped (never read, never hashed); any other path is CV-GEN-16, an unknown content file.</item>
/// <item>Stages 1 and 2 run per file (<see cref="SyntaxWalker"/>, then <see cref="SchemaStage.Walk"/> or
/// <see cref="BalanceWalker"/>); a file the source cannot read is CV-GEN-10.</item>
/// <item>Stage 3 builds a def type only when all its files are clean (<see cref="SchemaStage.Build"/>); a type that is not
/// built is a failed input, so the stage 4 to 5 rules that declare it are skipped and counted, naming the blocking file.</item>
/// <item>The balance entries of every file build one <see cref="BalanceTable"/> (CV-GEN-13, CV-GEN-20, CV-GEN-05), and stages
/// 4 and 5 run through <see cref="ContentPipeline"/>, the same runner and <see cref="ContentSet"/> constructor that
/// <see cref="BuiltInContent"/> uses, so equal inputs give equal canonical bytes.</item>
/// </list>
/// <c>…Seconds</c> fields load as ms and stay ms; the loader adds no tick conversion (ADR-0010 Errata 2026-10-21). There is no
/// parse counter (TD-178 item 3, Approved by Evan 2026-10-28 7:24 AM PT). Constructing a loader constructs its
/// <see cref="SchemaStage"/>, which runs <see cref="ContentSchema.SelfCheck()"/> (TD-090). Immutable after construction; a load
/// is thread-safe when its source is. Load-time only (allocates).
/// </summary>
/// <example>
/// <code>
/// ContentLoadResult r = new ContentLoader().Load(new DirectorySource("content"));
/// ContentSet content = r.Set ?? throw new InvalidOperationException("content failed to load");
/// </code>
/// </example>
public sealed class ContentLoader
{
    private const string Extension = ".jsonc";

    private readonly SchemaStage _schema;
    private readonly RuleRegistry _rules;

    /// <summary>Creates a loader over <see cref="RuleRegistry.CreateDefault"/>.</summary>
    /// <exception cref="InvalidOperationException">A field table fails its self-check (a code defect).</exception>
    public ContentLoader()
        : this(RuleRegistry.CreateDefault())
    {
    }

    /// <summary>Creates a loader whose stage 3 to 5 rules come from <paramref name="rules"/>.</summary>
    /// <param name="rules">The rule registry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A field table fails its self-check (a code defect).</exception>
    public ContentLoader(RuleRegistry rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
        _schema = new SchemaStage();
    }

    /// <summary>Where a content path goes.</summary>
    private enum FileKind : byte
    {
        Unknown = 0,
        Def = 1,
        Balance = 2,
        Presentation = 3,
    }

    /// <summary>Loads every content file of <paramref name="source"/>.</summary>
    /// <param name="source">The content files.</param>
    /// <returns>The findings and, when there is no Error, the set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public ContentLoadResult Load(IContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        BalanceRegistry registry = BalanceKeys.CreateRegistry();
        var found = new List<ContentDiagnostic>();
        var defFiles = new List<SchemaResult>();
        ImmutableArray<BalanceEntry>.Builder entries = ImmutableArray.CreateBuilder<BalanceEntry>();
        var failedOwners = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in source.ListManifest())
        {
            FileKind kind = Classify(path, out DefType type);
            if (kind == FileKind.Presentation)
            {
                continue;
            }

            if (kind == FileKind.Unknown)
            {
                found.Add(new ContentDiagnostic(
                    GeneralRuleIds.SyntaxShape, RuleSeverity.Error, new SourceLocation(path, 1, 1), DefRef.None, path,
                    "a known content file (items/*.jsonc, mail/{kinds,mix,destinations}.jsonc, balance/*.jsonc, presentation/**)"));
                continue;
            }

            if (!source.TryRead(path, out ReadOnlyMemory<byte> bytes))
            {
                found.Add(SyntaxWalker.MissingFile(path));
                if (kind == FileKind.Def)
                {
                    defFiles.Add(new SchemaResult(path, type, ImmutableArray<ParsedDef>.Empty, ImmutableArray<ContentDiagnostic>.Empty) { SyntaxFailed = true });
                }
                else
                {
                    failedOwners.Add(BalanceWalker.OwnerOf(path));
                }

                continue;
            }

            if (kind == FileKind.Def)
            {
                defFiles.Add(_schema.Walk(SyntaxWalker.Walk(path, bytes, TopLevelShape.Array), type));
                continue;
            }

            BalanceFileResult balanceFile = BalanceWalker.Walk(SyntaxWalker.Walk(path, bytes, TopLevelShape.Object), registry);
            found.AddRange(balanceFile.Diagnostics);
            entries.AddRange(balanceFile.Entries);
            if (!balanceFile.Ok)
            {
                failedOwners.Add(balanceFile.Owner);
            }
        }

        BuiltDefs defs = _schema.Build(defFiles);
        BalanceBuildResult balance = BalanceTable.Build(registry, entries.ToImmutable(), _rules);
        found.AddRange(WithoutCascade(balance.Validation.Diagnostics, failedOwners));
        if (balance.Table is null)
        {
            // Approved by Evan 2026-10-28 10:55 PM PT (TD-185 item 4): stages 4 and 5 read the balance table, so they do not run when it
            // cannot be built; the set is withheld and the stage 1 to 3 and balance findings are returned.
            ValidationResult early = new(DiagnosticOrder.Sorted(defs.Diagnostics.AddRange(found)), ImmutableArray<SkippedPair>.Empty, 0, 0);
            return new ContentLoadResult(early, null);
        }

        ContentBuildResult built = ContentPipeline.Build(balance.Table, defs with { Diagnostics = defs.Diagnostics.AddRange(found) }, _rules);
        return new ContentLoadResult(built.Validation, built.Set);
    }

    /// <summary>
    /// The balance findings without the CV-GEN-05 "absent" of a key whose owner's file failed stage 1 or 2: that file's own
    /// finding already explains it. Approved by Evan 2026-10-29 7:44 AM PT (TD-189 item 1).
    /// </summary>
    private static IEnumerable<ContentDiagnostic> WithoutCascade(ImmutableArray<ContentDiagnostic> findings, HashSet<string> failedOwners)
    {
        foreach (ContentDiagnostic d in findings)
        {
            bool cascade = string.Equals(d.RuleId, GeneralRuleIds.RequiredFieldMissing, StringComparison.Ordinal)
                && d.InputKey is string name && name.IndexOf('.') is int dot && dot > 0 && failedOwners.Contains(name.Substring(0, dot));
            if (!cascade)
            {
                yield return d;
            }
        }
    }

    /// <summary>
    /// The fixed path to def-type table (CD2). Approved by Evan 2026-10-28 10:55 PM PT (TD-185 item 5): <c>items/*</c> and
    /// <c>balance/*</c> match files directly in those folders only, so a file in a subfolder is an unknown content file.
    /// </summary>
    private static FileKind Classify(string path, out DefType type)
    {
        type = default;
        if (path.StartsWith("presentation/", StringComparison.Ordinal))
        {
            return FileKind.Presentation;
        }

        if (!path.EndsWith(Extension, StringComparison.Ordinal))
        {
            return FileKind.Unknown;
        }

        if (IsDirectChild(path, "items/"))
        {
            type = string.Equals(path, "items/containers.jsonc", StringComparison.Ordinal) ? DefType.Container : DefType.Item;
            return FileKind.Def;
        }

        if (IsDirectChild(path, BalanceWalker.Prefix))
        {
            return FileKind.Balance;
        }

        switch (path)
        {
            case "mail/kinds.jsonc":
                type = DefType.MailKind;
                return FileKind.Def;
            case "mail/mix.jsonc":
                type = DefType.MailMix;
                return FileKind.Def;
            case "mail/destinations.jsonc":
                type = DefType.DestinationType;
                return FileKind.Def;
            default:
                return FileKind.Unknown;
        }
    }

    private static bool IsDirectChild(string path, string folder) =>
        path.StartsWith(folder, StringComparison.Ordinal)
        && path.Length > folder.Length + Extension.Length
        && path.IndexOf('/', folder.Length) < 0;
}
