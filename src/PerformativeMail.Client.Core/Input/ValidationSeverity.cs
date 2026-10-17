namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// How serious a <see cref="ValidationEntry"/> is. <see cref="Error"/> blocks loading (boot refuses to start, the remap is
/// blocked); <see cref="Warning"/> is logged; <see cref="Info"/> is the remap screen's information line ("they never
/// overlap"). <c>0</c> is Error so that an entry built without a severity counts as a failure.
/// </summary>
/// <example><code>bool blocks = entry.Severity == ValidationSeverity.Error;</code></example>
public enum ValidationSeverity : byte
{
    /// <summary>A failure: the map is invalid.</summary>
    Error = 0,

    /// <summary>Suspicious but legal; logged.</summary>
    Warning = 1,

    /// <summary>Legal and intended; an information line (for example a binding shared between disjoint states).</summary>
    Info = 2,
}
