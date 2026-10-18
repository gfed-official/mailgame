using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>One def type's field table, as listed by <see cref="ContentSchema.Tables"/>.</summary>
/// <param name="Type">The def type the rows describe.</param>
/// <param name="TypeName">The def record's type name (<c>ItemDef</c>), used in self-check messages and as a reference target.</param>
/// <param name="Rows">The rows, in canonical encode order.</param>
/// <example>
/// <code>
/// foreach (SchemaTable t in ContentSchema.Tables) { int n = t.Rows.Length; }
/// </code>
/// </example>
public sealed record SchemaTable(DefType Type, string TypeName, ImmutableArray<FieldSpec> Rows);
