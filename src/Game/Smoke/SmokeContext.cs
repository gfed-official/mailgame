using Godot;
using PerformativeMail.Game.Hosting;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Assertion sink and tree access handed to a scenario. Each assertion prints one
/// machine-parsed line: <c>SMOKE &lt;name&gt; PASS|FAIL &lt;detail&gt;</c>.
/// </summary>
public sealed class SmokeContext(string name, GameLoop loop, SceneTree tree)
{
    /// <summary>The scenario name.</summary>
    public string Name { get; } = name;

    /// <summary>The root <c>GameLoop</c> (the <c>Main</c> node).</summary>
    public GameLoop Loop { get; } = loop;

    /// <summary>The scene tree.</summary>
    public SceneTree Tree { get; } = tree;

    /// <summary>Number of passed assertions.</summary>
    public int PassCount { get; private set; }

    /// <summary>Number of failed assertions.</summary>
    public int FailCount { get; private set; }

    /// <summary>True once the scenario asked to finish early.</summary>
    public bool QuitRequested { get; private set; }

    /// <summary>Records a passed assertion.</summary>
    /// <param name="msg">Detail text (single line).</param>
    public void Pass(string msg)
    {
        PassCount++;
        GD.Print($"SMOKE {Name} PASS {OneLine(msg)}");
    }

    /// <summary>Records a failed assertion.</summary>
    /// <param name="msg">Detail text (single line).</param>
    public void Fail(string msg)
    {
        FailCount++;
        GD.Print($"SMOKE {Name} FAIL {OneLine(msg)}");
    }

    /// <summary>Requests early completion; the runner stops polling and reports.</summary>
    public void Quit() => QuitRequested = true;

    private static string OneLine(string msg) => msg.ReplaceLineEndings(" ");
}
