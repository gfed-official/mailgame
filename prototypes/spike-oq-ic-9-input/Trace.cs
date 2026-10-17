using Godot;

namespace SpikeOqIc9Input;

/// <summary>Shared ordered trace of every receiver callback. Not thread-safe (main thread only).</summary>
public static class Trace
{
    public static readonly List<string> Lines = new();
    public static readonly List<(string Node, string Phase, InputEvent Ev)> Events = new();
    public static int Seq;
    public static bool Print = true;

    public static void Clear() { Lines.Clear(); Events.Clear(); Seq = 0; }

    public static void Add(string node, string phase, InputEvent? e)
    {
        Seq++;
        string line = $"  seq={Seq:000} f={Engine.GetProcessFrames()} pf={Engine.GetPhysicsFrames()} {node,-9} {phase,-16} {(e == null ? "" : Util.Desc(e))}";
        Lines.Add(line);
        if (e != null) { Events.Add((node, phase, e)); }
        if (Print) { Util.Log(line); }
    }

    public static IEnumerable<string> Receivers(string phase)
    {
        foreach (var x in Events) { if (x.Phase == phase) { yield return x.Node; } }
    }

    public static string Order(string phase) => string.Join(" > ", Receivers(phase));
}
