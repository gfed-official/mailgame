using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>One field of a parsed def or nested object: the <see cref="FieldSpec"/> row it matched and its value.</summary>
/// <param name="Row">The row's index in the table the object was read with.</param>
/// <param name="Name">The row's name (the JSON key).</param>
/// <param name="Location">The key's position in the file.</param>
/// <param name="Value">The converted value.</param>
/// <example><code>ParsedField f = def.Fields[0];   // Row 0 is "id"</code></example>
public sealed record ParsedField(int Row, string Name, SourceLocation Location, ParsedValue Value);
