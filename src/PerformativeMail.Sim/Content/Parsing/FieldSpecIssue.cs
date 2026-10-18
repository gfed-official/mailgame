namespace PerformativeMail.Sim.Content.Parsing;

/// <summary>A spec-table self-check finding: the field, the rule it would surface as, and what is wrong.</summary>
/// <param name="FieldName">The offending row's name; a nested row is a dotted path such as <c>catchVolume.widthMetres</c>.</param>
/// <param name="RuleId">The catalogue rule id the defect would otherwise surface as.</param>
/// <param name="Kind">What is wrong with the row; defaults to <see cref="FieldSpecIssueKind.MissingUnit"/>.</param>
/// <example>
/// <code>
/// var issue = new FieldSpecIssue("grid", "CV-GEN-06");   // Kind == MissingUnit
/// </code>
/// </example>
public readonly record struct FieldSpecIssue(string FieldName, string RuleId, FieldSpecIssueKind Kind = FieldSpecIssueKind.MissingUnit);
