using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content;

/// <summary>
/// What <see cref="ContentLoader.Load"/> produced: every reachable finding of the five stages, the skipped (rule, def) pairs
/// and, when no finding is an Error, the one immutable <see cref="ContentSet"/>. Immutable.
/// </summary>
/// <param name="Validation">The merged findings, skipped pairs and suppress counts of stages 1 to 5.</param>
/// <param name="Set">The accepted set, or null when any stage reported an Error.</param>
/// <example>
/// <code>
/// ContentLoadResult r = new ContentLoader().Load(new DirectorySource("content"));
/// if (r.Set is null) { foreach (ContentDiagnostic d in r.Diagnostics) { /* report d */ } }
/// </code>
/// </example>
public sealed record ContentLoadResult(ValidationResult Validation, ContentSet? Set)
{
    /// <summary>Every finding, sorted by <see cref="DiagnosticOrder"/>.</summary>
    public ImmutableArray<ContentDiagnostic> Diagnostics => Validation.Diagnostics;

    /// <summary>The number of Errors (the set is withheld when it is above 0).</summary>
    public int ErrorCount => Validation.ErrorCount;

    /// <summary>The number of (rule, def) pairs skipped because an input failed an earlier stage.</summary>
    public int SkippedCount => Validation.SkippedCount;
}
