using Godot;

namespace PerformativeMail.Game.Input;

/// <summary>
/// The debug-build check of the GUI-focus policy (story input-controls 027): walks <c>Main/Hud</c> and <c>Main/World</c> once
/// and reports every visible <c>Control</c> whose <c>FocusMode</c> is not <c>None</c> (a Control that missed
/// <see cref="GameControlFocus.Apply"/>). <c>Main/Screens</c> is not walked: menus outside play keep stock focus. Gated at run
/// time with <c>OS.IsDebugBuild()</c>, never a preprocessor symbol. <see cref="InputAdapter"/> runs it on its first frame when
/// <see cref="AdapterDeps.Main"/> is set and pushes one warning per Control found. Allocates; runs once. Main thread only.
/// </summary>
/// <example><code>IReadOnlyList&lt;string&gt; bad = FocusPolicyCheck.Run(main);   // empty in a release build</code></example>
public static class FocusPolicyCheck
{
    /// <summary>The game-context subtrees under <c>Main</c> that the check walks.</summary>
    public static readonly string[] GameContextPaths = ["Hud", "World"];

    /// <summary>The offending Controls under <paramref name="main"/>; always empty when <c>OS.IsDebugBuild()</c> is false.</summary>
    /// <param name="main">The <c>Main</c> node.</param>
    /// <returns>Each offender as <c>path (FocusMode)</c>, in tree order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="main"/> is null.</exception>
    public static IReadOnlyList<string> Run(Node main)
    {
        ArgumentNullException.ThrowIfNull(main);
        var found = new List<string>();
        if (!OS.IsDebugBuild())
        {
            return found;
        }

        foreach (string path in GameContextPaths)
        {
            if (main.GetNodeOrNull(path) is Node subtree)
            {
                Walk(subtree, found);
            }
        }

        return found;
    }

    private static void Walk(Node node, List<string> found)
    {
        if (node is Control c && c.IsVisibleInTree() && c.FocusMode != Control.FocusModeEnum.None)
        {
            found.Add($"{c.GetPath()} ({c.FocusMode})");
        }

        foreach (Node child in node.GetChildren())
        {
            Walk(child, found);
        }
    }
}
