using System.Collections.Immutable;

namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>
/// A variant rule on one <see cref="FieldSpec"/> row (content-data CD10, CV-GEN-14): the row's presence depends on the
/// spelling of a sibling enum row. A container's <c>grid</c> is required when <c>layout</c> is <c>grid</c> or <c>slots</c> and
/// forbidden for <c>pile</c>. The spellings are the data spellings of the discriminator's enum set. Immutable, so thread-safe.
/// </summary>
/// <param name="On">The sibling enum row's name (the discriminator).</param>
/// <param name="RequiredFor">Discriminator spellings for which the field must be present (CV-GEN-05 when it is not).</param>
/// <param name="ForbiddenFor">Discriminator spellings for which the field must be absent (CV-GEN-14 when it is present).</param>
/// <example>
/// <code>
/// var v = new FieldVariant("layout", ImmutableArray.Create("grid", "slots"), ImmutableArray.Create("pile"));
/// </code>
/// </example>
public sealed record FieldVariant(string On, ImmutableArray<string> RequiredFor, ImmutableArray<string> ForbiddenFor);
