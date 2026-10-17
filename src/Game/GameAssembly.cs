namespace PerformativeMail.Game;

/// <summary>
/// Placeholder that anchors the <c>PerformativeMail.Game</c> namespace (the Godot host
/// assembly) until the first host node lands. Contains no gameplay code and
/// deliberately has no <c>using Godot</c>, so it is immune to the
/// <c>System.Environment</c> / <c>Godot.Environment</c> ambiguity under ImplicitUsings.
/// </summary>
/// <example>
/// <code>
/// string name = GameAssembly.Name; // "PerformativeMail.Game"
/// </code>
/// </example>
public static class GameAssembly
{
    /// <summary>The assembly's simple name; must match <c>project/assembly_name</c> in project.godot.</summary>
    public const string Name = "PerformativeMail.Game";
}
