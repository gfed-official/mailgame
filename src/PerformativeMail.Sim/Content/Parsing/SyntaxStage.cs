using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Sources;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// Runs <see cref="SyntaxWalker"/> over every file an <see cref="IContentSource"/> lists. A listed file the source cannot
/// read is CV-GEN-10; a file with a syntax error ends that file only, and every other file is still walked.
/// Stateless, so thread-safe when the source is.
/// </summary>
/// <example>
/// <code>
/// SyntaxStageResult r = SyntaxStage.Run(new DirectorySource(root), path =&gt; TopLevelShape.Array);
/// </code>
/// </example>
public static class SyntaxStage
{
    /// <summary>Walks every manifest entry.</summary>
    /// <param name="source">Where the files come from.</param>
    /// <param name="shapeOf">Gives the top-level shape a path must have (def files are arrays, balance files objects).</param>
    /// <returns>The walked files and all findings; a listed file the source cannot read is a CV-GEN-10 finding and has no entry in <see cref="SyntaxStageResult.Files"/>.</returns>
    public static SyntaxStageResult Run(IContentSource source, Func<string, TopLevelShape> shapeOf)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(shapeOf);
        ImmutableArray<SyntaxResult>.Builder files = ImmutableArray.CreateBuilder<SyntaxResult>();
        var diagnostics = new List<ContentDiagnostic>();
        foreach (string path in source.ListManifest())
        {
            if (!source.TryRead(path, out ReadOnlyMemory<byte> bytes))
            {
                diagnostics.Add(SyntaxWalker.MissingFile(path));
                continue;
            }

            SyntaxResult result = SyntaxWalker.Walk(path, bytes, shapeOf(path));
            files.Add(result);
            diagnostics.AddRange(result.Diagnostics);
        }

        return new SyntaxStageResult(files.ToImmutable(), DiagnosticOrder.Sorted(ImmutableArray.CreateRange(diagnostics)));
    }
}
