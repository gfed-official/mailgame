namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// One independently testable clause of a rule (GDD CD24: "every rule clause has a violation fixture"). A rule
/// that has a single limit is one clause; a rule with several conditions lists one clause per condition. Names are
/// permanent within their rule. A clause with <see cref="HasLimit"/> also needs a valid-edge fixture at the limit.
/// </summary>
/// <param name="Name">A short snake_case name, unique within the rule.</param>
/// <param name="HasLimit">True when the clause has a numeric or set limit that a valid-edge fixture can sit on.</param>
/// <example>
/// <code>
/// var maxStack = new RuleClause("max_stack", HasLimit: true);   // 127 -> 254 passes, 128 -> 256 errors
/// </code>
/// </example>
public sealed record RuleClause(string Name, bool HasLimit = false)
{
    /// <summary>The clause used by rules that do not name their clauses.</summary>
    public static RuleClause Main => new("main");
}
