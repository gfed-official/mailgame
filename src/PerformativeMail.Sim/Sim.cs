namespace PerformativeMail.Sim;

/// <summary>
/// Build-mode facts about the compiled simulation assembly.
/// </summary>
public static class Sim
{
    /// <summary>
    /// <c>true</c> when the Sim was compiled with <c>SIM_ASSERT</c> (so
    /// <see cref="Primitives.SimAssert"/> call sites inside the Sim are live and checked
    /// arithmetic is on); <c>false</c> in an optimised build without asserts. Intended to
    /// be logged at start-up and asserted by tests. This is the one sanctioned
    /// conditional compilation in Sim code and it is keyed on <c>SIM_ASSERT</c>, never
    /// on <c>DEBUG</c> or the configuration name.
    /// </summary>
    /// <example>
    /// <code>
    /// log.Info($"Sim asserts enabled: {Sim.AssertsEnabled}");
    /// </code>
    /// </example>
#if SIM_ASSERT
    public const bool AssertsEnabled = true;
#else
    public const bool AssertsEnabled = false;
#endif
}
