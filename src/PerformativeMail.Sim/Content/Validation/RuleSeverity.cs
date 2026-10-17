namespace PerformativeMail.Sim.Content.Validation;

/// <summary>
/// How a validator rule's failure is treated (GDD CD22). An <see cref="Error"/> prevents the content set from being
/// built and can never be suppressed; a <see cref="Warning"/> is reported, the set still builds, and a per-def
/// <c>suppress</c> entry with a <c>why</c> can waive it. A rule a gameplay system relies on for correctness is always
/// an Error. "Clamp" is deliberately not a severity. Values are append-only.
/// </summary>
/// <example>
/// <code>
/// bool blocksBuild = rule.Severity == RuleSeverity.Error;
/// </code>
/// </example>
public enum RuleSeverity : byte
{
    /// <summary>Blocks the content set; never suppressible.</summary>
    Error = 1,

    /// <summary>Reported and logged; the set still builds; waivable with <c>suppress</c> plus <c>why</c>.</summary>
    Warning = 2,
}
