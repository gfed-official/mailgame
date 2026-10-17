using Godot;

namespace SpikeAdr0019;

public partial class S1Read : Node
{
    public override void _Ready()
    {
        GD.Print("[s1] C# reader running");
        foreach (string p in new[] { "res://content/test.jsonc", "res://content/manifest.txt" })
        {
            byte[] b = Godot.FileAccess.GetFileAsBytes(p);
            GD.Print("[s1] " + p + " bytes=" + b.Length + " err=" + Godot.FileAccess.GetOpenError() + " exists=" + Godot.FileAccess.FileExists(p));
        }
        GD.Print("[s1] DirAccess content/: " + string.Join(",", DirAccess.GetFilesAt("res://content")));
        GD.Print("[s1] stretch/mode=" + ProjectSettings.GetSetting("display/window/stretch/mode", "<unset>")
            + " aspect=" + ProjectSettings.GetSetting("display/window/stretch/aspect", "<unset>")
            + " has_mode=" + ProjectSettings.HasSetting("display/window/stretch/mode"));
        GetTree().Quit();
    }
}
