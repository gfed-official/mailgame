using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// What the stage 1 walk found in one file. <see cref="Tokens"/> holds every token when the walk reached the end of the
/// file (also when duplicate keys were reported) and is empty after a syntax error (CV-GEN-16), which ends this file only.
/// Immutable.
/// </summary>
/// <param name="Path">The content-relative path.</param>
/// <param name="Diagnostics">The findings (CV-GEN-04 and CV-GEN-16), in file order; every one carries <c>DefRef.None</c> and severity Error.</param>
/// <param name="Tokens">The side table of tokens; offsets are into <see cref="Bytes"/>.</param>
/// <param name="Bytes">The file's original bytes (BOM included), which <see cref="SyntaxToken.Start"/> indexes.</param>
/// <param name="PayloadOffset">0, or 3 when a leading BOM was stripped.</param>
/// <example>
/// <code>
/// SyntaxResult r = SyntaxWalker.Walk("items.jsonc", bytes, TopLevelShape.Array);
/// if (r.Ok) { ReadOnlySpan&lt;byte&gt; first = r.Raw(r.Tokens[0]); }
/// </code>
/// </example>
public sealed record SyntaxResult(
    string Path,
    ImmutableArray<ContentDiagnostic> Diagnostics,
    ImmutableArray<SyntaxToken> Tokens,
    ReadOnlyMemory<byte> Bytes,
    int PayloadOffset)
{
    /// <summary>True when no Error was found: the file is syntactically valid and has no duplicate key.</summary>
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

    /// <summary>The file without its leading BOM (what the JSON reader saw).</summary>
    public ReadOnlyMemory<byte> Payload => Bytes.Slice(PayloadOffset);

    /// <summary>The raw bytes of <paramref name="token"/>.</summary>
    /// <param name="token">A token of this result's <see cref="Tokens"/>.</param>
    public ReadOnlySpan<byte> Raw(SyntaxToken token) => Bytes.Span.Slice(token.Start, token.Length);
}
