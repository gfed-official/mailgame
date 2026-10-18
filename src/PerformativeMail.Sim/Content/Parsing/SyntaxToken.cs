using System.Text.Json;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// One entry of the stage 1 side table: the token kind and where its raw bytes lie in the ORIGINAL file (BOM included).
/// Strings and property names include their quotes; numbers and literals are the raw text; brackets are one byte.
/// Story 018's schema walk uses it to attach <c>line:column</c> to a field and to read raw number or string bytes.
/// </summary>
/// <param name="Kind">The JSON token kind.</param>
/// <param name="Start">Byte offset of the token's first byte in the original file.</param>
/// <param name="Length">The token's raw length in bytes.</param>
/// <example>
/// <code>
/// SyntaxToken t = result.Tokens[3];
/// ReadOnlySpan&lt;byte&gt; raw = result.Raw(t);   // for a number: the digits as written
/// </code>
/// </example>
public readonly record struct SyntaxToken(JsonTokenType Kind, int Start, int Length);
