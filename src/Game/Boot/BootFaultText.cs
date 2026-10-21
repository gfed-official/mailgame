using PerformativeMail.Sim.Core;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// One-line description of a fault for the log and the error screen: the exception type name, the
/// <see cref="SimBuildException.Code"/> name for a composition failure (ADR-0003 Risks), and the message.
/// </summary>
/// <example>
/// <code>
/// BootFaultText.Describe(new SimBuildException(SimBuildErrorCode.ReservedPhase, "bad bind"));
/// // "SimBuildException (code ReservedPhase): bad bind"
/// </code>
/// </example>
public static class BootFaultText
{
    /// <summary>Describes <paramref name="ex"/> as <c>Type: message</c> or <c>SimBuildException (code Name): message</c>.</summary>
    /// <param name="ex">The exception.</param>
    /// <returns>The description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ex"/> is null.</exception>
    public static string Describe(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        string type = ex.GetType().Name;
        return ex is SimBuildException build
            ? $"{type} (code {build.Code}): {ex.Message}"
            : $"{type}: {ex.Message}";
    }
}
