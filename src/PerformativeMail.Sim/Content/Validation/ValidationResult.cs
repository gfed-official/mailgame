using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// One (rule, def) pair a rule did not run for because a declared input failed an earlier stage (GDD CD24,
/// CDD-LOAD-3). <see cref="Def"/> is <see cref="DefRef.None"/> when the whole rule was skipped for a file or set.
/// </summary>
/// <param name="RuleId">The skipped rule.</param>
/// <param name="Def">The def it was skipped for.</param>
/// <param name="BlockingInput">The failed field or input key that blocked it.</param>
/// <param name="BlockingFile">The file holding the failure; empty when not file-based.</param>
/// <example>
/// <code>
/// foreach (SkippedPair s in result.Skipped) { Console.WriteLine(s.RuleId + " skipped for " + s.Def + ": " + s.BlockingInput); }
/// </code>
/// </example>
public sealed record SkippedPair(string RuleId, DefRef Def, string BlockingInput, string BlockingFile);

/// <summary>
/// What a validation run found. Immutable. <see cref="Diagnostics"/> holds every unsuppressed Error and Warning,
/// sorted by <see cref="DiagnosticOrder"/>; <see cref="Skipped"/> is sorted by rule id, def type, def id (ordinal).
/// </summary>
/// <example>
/// <code>
/// ValidationResult result = ContentValidationRunner.Run(registry, input);
/// ContentSet? built = result.AcceptedSet(candidate);   // null when any Error was found
/// </code>
/// </example>
public sealed class ValidationResult
{
    internal ValidationResult(
        ImmutableArray<ContentDiagnostic> diagnostics,
        ImmutableArray<SkippedPair> skipped,
        int suppressedCount,
        int unmatchedSuppressCount)
    {
        Diagnostics = diagnostics;
        Skipped = skipped;
        SuppressedCount = suppressedCount;
        UnmatchedSuppressCount = unmatchedSuppressCount;
        int errors = 0;
        foreach (ContentDiagnostic d in diagnostics)
        {
            if (d.Severity == RuleSeverity.Error)
            {
                errors++;
            }
        }

        ErrorCount = errors;
        WarningCount = diagnostics.Length - errors;
    }

    /// <summary>
    /// Wraps findings produced outside the stage 4 to 5 runner (a stage 1 syntax walk, a builder) as a result: sorted by
    /// <see cref="DiagnosticOrder"/>, nothing skipped or suppressed. Lets a fixture check a stage 1 rule with
    /// <see cref="FixtureChecker"/>.
    /// </summary>
    /// <param name="diagnostics">The findings; a default array reads as empty.</param>
    /// <example>
    /// <code>
    /// ValidationResult r = ValidationResult.FromDiagnostics(SyntaxWalker.Walk(path, bytes, TopLevelShape.Array).Diagnostics);
    /// </code>
    /// </example>
    public static ValidationResult FromDiagnostics(ImmutableArray<ContentDiagnostic> diagnostics) =>
        new(DiagnosticOrder.Sorted(diagnostics), ImmutableArray<SkippedPair>.Empty, 0, 0);

    /// <summary>Unsuppressed findings, sorted by (path ordinal, line, column, rule id).</summary>
    public ImmutableArray<ContentDiagnostic> Diagnostics { get; }

    /// <summary>The skipped (rule, def) pairs.</summary>
    public ImmutableArray<SkippedPair> Skipped { get; }

    /// <summary>The number of skipped (rule, def) pairs ("N skipped" in the CLI summary).</summary>
    public int SkippedCount => Skipped.Length;

    /// <summary>The number of Error findings.</summary>
    public int ErrorCount { get; }

    /// <summary>The number of unsuppressed Warning findings (what <c>--strict</c> fails on).</summary>
    public int WarningCount { get; }

    /// <summary>The number of Warning findings silenced by a valid <c>suppress</c> entry.</summary>
    public int SuppressedCount { get; }

    /// <summary>The number of valid <c>suppress</c> entries that matched no finding; counted in the summary only, never an error.</summary>
    public int UnmatchedSuppressCount { get; }

    /// <summary>True when there is no Error: the content set may be built. Warnings never block.</summary>
    public bool CanBuild => ErrorCount == 0;

    /// <summary>
    /// The gate that enforces "an Error always prevents the set from being built": returns <paramref name="candidate"/>
    /// when <see cref="CanBuild"/>, otherwise null.
    /// </summary>
    /// <typeparam name="T">The built set type (story 008's <c>ContentSet</c>).</typeparam>
    /// <param name="candidate">The built set to release.</param>
    public T? AcceptedSet<T>(T candidate)
        where T : class => CanBuild ? candidate : null;
}
