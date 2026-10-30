namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// The sink a rule check writes to while the runner executes it. It stamps every finding with the rule's id and
/// severity (the rule body cannot pick them) and answers whether a def's inputs are blocked by an earlier failure.
/// Not thread-safe: one context is used by one rule on one thread. Created only by <see cref="ContentValidationRunner"/>.
/// </summary>
/// <example>
/// <code>
/// static void Check(MySet set, RuleContext ctx)
/// {
///     foreach (var d in set.Defs)
///     {
///         if (ctx.IsBlocked(d.Ref)) { continue; }   // its input failed an earlier stage; the runner counts the skip
///         if (d.Value &gt; 10) { ctx.Report(d.Ref, d.Where, "11", "0..10", "my.value"); }
///     }
/// }
/// </code>
/// </example>
public sealed class RuleContext
{
    private readonly RuleSpec _rule;
    private readonly List<ContentDiagnostic> _sink;
    private readonly List<DefRef> _blocked;

    internal RuleContext(RuleSpec rule, List<ContentDiagnostic> sink, List<DefRef> blocked)
    {
        _rule = rule;
        _sink = sink;
        _blocked = blocked;
    }

    /// <summary>
    /// True when one of this rule's declared inputs failed an earlier stage for <paramref name="def"/>. A per-def rule
    /// must skip such a def (the runner has already counted the skipped pair).
    /// </summary>
    /// <param name="def">The def about to be checked.</param>
    public bool IsBlocked(DefRef def)
    {
        for (int i = 0; i < _blocked.Count; i++)
        {
            if (_blocked[i] == def)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records one finding for this rule. A finding attributed to a def that <see cref="IsBlocked"/> is dropped: the
    /// runner has already counted that (rule, def) pair as skipped, so a rule that forgets to consult
    /// <see cref="IsBlocked"/> cannot both be counted as skipped and report. Findings with <see cref="DefRef.None"/> are never dropped.
    /// </summary>
    /// <param name="def">The def the finding is attributed to (the def a <c>suppress</c> must sit on), or <see cref="DefRef.None"/>.</param>
    /// <param name="location">Where it points; <see cref="SourceLocation.BuiltIn"/> for built-in content.</param>
    /// <param name="value">The offending value as text.</param>
    /// <param name="allowed">The allowed range or set as text.</param>
    /// <param name="inputKey">The declared input this shows to be invalid, so later-stage rules reading it are skipped; null if none.</param>
    public void Report(DefRef def, SourceLocation location, string value, string allowed, string? inputKey = null)
    {
        if (!def.IsNone && IsBlocked(def))
        {
            return;
        }

        _sink.Add(new ContentDiagnostic(_rule.Id, _rule.Severity, location, def, value, allowed, inputKey));
    }
}
