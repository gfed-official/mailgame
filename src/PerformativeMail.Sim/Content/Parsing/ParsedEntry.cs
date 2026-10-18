using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>One entry of a <see cref="FieldKind.RefMap"/> value: the key (a def id, resolved at stage 3/4) and its stored number.</summary>
/// <param name="Key">The key as written (unescaped).</param>
/// <param name="Location">The key's position.</param>
/// <param name="Value">The stored value in the row's unit (a share <c>0.60</c> is 600).</param>
/// <example><code>var e = new ParsedEntry("letter", loc, 600);</code></example>
public sealed record ParsedEntry(string Key, SourceLocation Location, long Value);
