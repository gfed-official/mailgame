using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>The outcome of a stage 2 walk over one def file: every def (clean or failed) and the findings.</summary>
/// <param name="Path">The file path.</param>
/// <param name="Type">The def type the file holds.</param>
/// <param name="Defs">Every array element, in file order.</param>
/// <param name="Diagnostics">The findings, in file order of discovery (the orchestrator sorts).</param>
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
    /// <summary>True when no def failed.</summary>
    public bool Ok
    {
        get
        {
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
