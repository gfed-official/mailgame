using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// What stage 1 found over a whole source. <see cref="Files"/> holds one <see cref="SyntaxResult"/> per listed file that could
/// be read, in manifest order; <see cref="Diagnostics"/> holds every finding (CV-GEN-04, CV-GEN-10, CV-GEN-16) sorted by
/// <see cref="DiagnosticOrder"/>. Immutable.
/// </summary>
/// <param name="Files">The walked files, in manifest order.</param>
/// <param name="Diagnostics">All findings, sorted.</param>
/// <example>
/// <code>
/// SyntaxStageResult r = SyntaxStage.Run(source, p =&gt; p.StartsWith("balance/", StringComparison.Ordinal) ? TopLevelShape.Object : TopLevelShape.Array);
/// foreach (SyntaxResult f in r.Files) { if (f.Ok) { /* stage 2 */ } }
/// </code>
/// </example>
public sealed record SyntaxStageResult(ImmutableArray<SyntaxResult> Files, ImmutableArray<ContentDiagnostic> Diagnostics)
{
    /// <summary>True when no Error was found.</summary>
    public bool Ok
    {
        get
        {
            foreach (ContentDiagnostic d in Diagnostics)
            {
                if (d.Severity == RuleSeverity.Error)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
