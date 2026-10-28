using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// The outcome of a stage 2 walk over one def file: every def (clean or failed) and the findings. A file that failed stage 1
/// is not walked: it has no defs, carries its stage 1 findings and <see cref="SyntaxFailed"/>, and is not <see cref="Ok"/>.
/// </summary>
/// <param name="Path">The file path.</param>
/// <param name="Type">The def type the file holds.</param>
/// <param name="Defs">Every array element, in file order.</param>
/// <param name="Diagnostics">The file's stage 1 findings, then stage 2's in file order of discovery (the orchestrator sorts).</param>
/// <example>
/// <code>
/// SchemaResult r = SchemaWalker.Walk(syntax, table);
/// if (r.Ok) { /* every def is clean */ }
/// </code>
/// </example>
public sealed record SchemaResult(
    string Path,
    DefType Type,
    ImmutableArray<ParsedDef> Defs,
    ImmutableArray<ContentDiagnostic> Diagnostics)
{
    /// <summary>True when stage 1 found an Error in the file, so stage 2 did not walk it.</summary>
    public bool SyntaxFailed { get; init; }

    /// <summary>True when the file passed stage 1 and no def failed stage 2.</summary>
    public bool Ok
    {
        get
        {
            if (SyntaxFailed)
            {
                return false;
            }

            foreach (ParsedDef d in Defs)
            {
                if (d.Failed)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
