using System.Collections.Immutable;
using PerformativeMail.Sim.Content.Validation;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// One def as stage 2 read it: its type, its id (<c>null</c> when absent or failing the lint), where it starts, its fields
/// in <see cref="FieldSpec"/> row order (absent optional fields are simply not listed; the builder applies defaults), its
/// <c>suppress</c> entries, and whether any Error was reported on it. A failed def is kept for diagnostics only and never
/// reaches stage 3. Immutable, so thread-safe.
/// </summary>
/// <example>
/// <code>
/// foreach (ParsedDef d in result.Defs) { if (!d.Failed) { /* build */ } }
/// </code>
/// </example>
/// <param name="Type">The def type the file holds.</param>
/// <param name="Id">The id, or <c>null</c> when missing or invalid.</param>
/// <param name="Start">The position of the def's opening brace.</param>
/// <param name="Fields">The fields present, in row order.</param>
/// <param name="Suppress">The <c>suppress</c> entries (never stored on the gameplay record, never hashed).</param>
/// <param name="Failed">True when at least one Error was reported on this def.</param>
public sealed record ParsedDef(
    DefType Type,
    string? Id,
    SourceLocation Start,
    ImmutableArray<ParsedField> Fields,
    ImmutableArray<SuppressEntry> Suppress,
    bool Failed);
