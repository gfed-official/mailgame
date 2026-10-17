using Godot;

namespace SpikeOqIc9Input;

/// <summary>Main-scene root. Dispatches on the user arg <c>--probe=&lt;id&gt;</c>; each probe writes logs/&lt;id&gt;.log and quits.</summary>
public partial class Probe : Node
{
    public override async void _Ready()
    {
        string id = Util.ArgValue("--probe") ?? "none";
        // Root is "busy setting up children" during the main scene's _Ready: add_child on it fails.
        // Wait one frame so probes can add nodes to the root (recorded in REPORT as a harness note).
        await Util.Frames(GetTree(), 1);
        Util.OpenLog(id);
        Util.Log("# probe=" + id);
        Util.Log("# godot=" + Engine.GetVersionInfo()["string"] + " display=" + DisplayServer.GetName() +
                 " dotnet=" + System.Environment.Version + " os=" + OS.GetName() + " fps_target=" + Engine.MaxFps);
        try
        {
            switch (id)
            {
                case "baseline": BaselineProbe.Run(); break;
                case "b3": BaselineProbe.RunB3(); break;
                case "order": await OrderProbe.Run(this); break;
                case "devices": await DeviceProbe.Run(this); break;
                case "alloc": await AllocProbe.Run(this); break;
                case "exitleak": await AllocProbe.RunExitLeak(this); break;
                case "focus": await FocusProbe.Run(this); break;
                case "window": await WindowProbe.Run(this); break;
                default: Util.Log("unknown probe id: " + id); break;
            }
        }
        catch (System.Exception ex)
        {
            Util.Log("PROBE EXCEPTION: " + ex);
        }
        Util.Log("# done");
        Util.CloseLog();
        GetTree().Quit();
    }
}
