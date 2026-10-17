namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// One validator finding (GDD CD3.5, ADR-0010 section 2): the rule id, severity, <c>path:line:column</c>, the
/// offending value and the allowed range or set. Immutable. <see cref="Value"/> and <see cref="Allowed"/> are
/// already-rendered text (integers print in invariant form; the Sim has no floating point), so a diagnostic list
/// is byte-identical on every platform. A list sorts by <see cref="DiagnosticOrder"/>.
/// </summary>
/// <param name="RuleId">The permanent rule id, for example <c>CV-GRD-07</c>.</param>
/// <param name="Severity">The rule's severity.</param>
/// <param name="Location">Where the finding points; <c>0:0</c> for built-in content.</param>
/// <param name="Def">The def the finding is attributed to (used by <c>suppress</c> and skip accounting), or <see cref="DefRef.None"/>.</param>
/// <param name="Value">The offending value, as text.</param>
/// <param name="Allowed">The allowed range or set, as text.</param>
/// <param name="InputKey">The declared rule input this finding shows to be invalid (it blocks later-stage rules that declare it), or null when it blocks nothing.</param>
/// <example>
/// <code>
/// var d = new ContentDiagnostic("CV-THR-12", RuleSeverity.Error, new SourceLocation("balance/throw.jsonc", 7, 14),
///     DefRef.None, "501", "100..500", "throw.catchTapMs");
/// string text = d.Format(registry);   // "balance/throw.jsonc:7:14: error CV-THR-12 (THROW-CV-12): value 501, allowed 100..500"
/// </code>
/// </example>
public sealed record ContentDiagnostic(
    string RuleId,
    RuleSeverity Severity,
    SourceLocation Location,
    DefRef Def,
    string Value,
    string Allowed,
    string? InputKey = null)
{
    /// <summary>
    /// Renders <c>path:line:column: error|warning ID (ALIAS): value V, allowed A</c>, printing both the id and the
    /// alias when the rule has one (GDD CD23).
    /// </summary>
    /// <param name="registry">Supplies the alias for <see cref="RuleId"/>; an id the registry does not know prints bare.</param>
    public string Format(RuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        string sev = Severity == RuleSeverity.Error ? "error" : "warning";
        string def = Def.IsNone ? string.Empty : " [" + Def + "]";
        return Location + ": " + sev + " " + registry.FormatId(RuleId) + def + ": value " + Value + ", allowed " + Allowed;
    }
}
