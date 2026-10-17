namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Marks a <see cref="SmokeScenario"/> subclass as runnable via <c>--smoke=&lt;name&gt;</c>.
/// Discovered by reflection at startup, so other epics add scenarios without touching the shell.
/// </summary>
/// <param name="name">The scenario name used on the command line.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SmokeScenarioAttribute(string name) : Attribute
{
    /// <summary>The scenario name used on the command line.</summary>
    public string Name { get; } = name;
}
