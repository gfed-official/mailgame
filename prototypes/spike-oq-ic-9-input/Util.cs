using System.Text;
using Godot;

namespace SpikeOqIc9Input;

/// <summary>Arg parsing, log file output and small helpers shared by every probe.</summary>
public static class Util
{
    private static StreamWriter? _log;

    public static string? ArgValue(string key)
    {
        foreach (string s in OS.GetCmdlineUserArgs())
        {
            if (s.StartsWith(key + "=", StringComparison.Ordinal)) { return s.Substring(key.Length + 1); }
        }
        return null;
    }

    public static bool HasArg(string a)
    {
        foreach (string s in OS.GetCmdlineUserArgs()) { if (s == a) { return true; } }
        return false;
    }

    /// <summary>Open logs/&lt;id&gt;.log (project-relative; throwaway spike, project dir is writable).</summary>
    public static void OpenLog(string id)
    {
        string dir = ProjectSettings.GlobalizePath("res://logs");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, id + ".log");
        _log = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public static void CloseLog()
    {
        _log?.Dispose();
        _log = null;
    }

    public static void Log(string msg)
    {
        _log?.WriteLine(msg);
        GD.Print(msg);
    }

    /// <summary>Row emitted for the findings table: ID, observed facts.</summary>
    public static void Row(string id, string msg) => Log("[" + id + "] " + msg);

    /// <summary>Await n process frames.</summary>
    public static async Task Frames(SceneTree t, int n = 1)
    {
        for (int i = 0; i < n; i++) { await t.ToSignal(t, SceneTree.SignalName.ProcessFrame); }
    }

    public static string Desc(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey k:
                return $"Key(keycode={k.Keycode} phys={k.PhysicalKeycode} pressed={k.Pressed} echo={k.Echo} dev={k.Device})";
            case InputEventMouseButton mb:
                return $"MouseButton(btn={mb.ButtonIndex} pressed={mb.Pressed} pos={mb.Position} dev={mb.Device})";
            case InputEventMouseMotion mm:
                return $"MouseMotion(rel={mm.Relative} screenRel={mm.ScreenRelative} pos={mm.Position} dev={mm.Device})";
            case InputEventJoypadButton jb:
                return $"JoyButton(btn={jb.ButtonIndex} pressed={jb.Pressed} dev={jb.Device})";
            case InputEventJoypadMotion jm:
                return $"JoyMotion(axis={jm.Axis} value={jm.AxisValue:0.###} dev={jm.Device})";
            case InputEventAction a:
                return $"Action({a.Action} pressed={a.Pressed} dev={a.Device})";
            default:
                return e.GetType().Name + "(dev=" + e.Device + ")";
        }
    }

    public static string Poll()
    {
        return $"IsKeyPressed(Space)={Input.IsKeyPressed(Key.Space)} IsPhysicalKeyPressed(Space)={Input.IsPhysicalKeyPressed(Key.Space)} " +
               $"IsActionPressed(ui_accept)={Input.IsActionPressed("ui_accept")} IsActionPressed(ui_select)={Input.IsActionPressed("ui_select")}";
    }
}
