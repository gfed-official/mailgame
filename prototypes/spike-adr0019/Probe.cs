using Godot;

namespace SpikeAdr0019;

public partial class Probe : Node
{
    [Export] public string Label { get; set; } = "?";

    public override void _EnterTree()
    {
        if (!Util.HasArg("--oq1")) { return; }
        string? sp = Util.ArgValue("--scene-prio");
        if (sp != null && Label == "MainRoot") { ProcessPriority = int.Parse(sp); }
        Util.Log(Label + " _EnterTree prio=" + ProcessPriority);
    }

    public override void _Ready()
    {
        if (!Util.HasArg("--oq1")) { return; }
        Util.Log(Label + " _Ready");
        if (Label == "MainRoot") { GetTree().Root.GetChildren(); }
    }

    public override void _Process(double delta)
    {
        if (!Util.HasArg("--oq1")) { return; }
        ulong f = Engine.GetProcessFrames();
        if (f < 8) { Util.Log(Label + " _Process"); }
        if (Label == "MainRoot")
        {
            if (f == 3)
            {
                DumpRoot("before ChangeScene");
                Util.Log("calling ChangeSceneToFile");
                GetTree().ChangeSceneToFile("res://scenes/Other.tscn");
            }
        }
        if (Label == "OtherRoot" && f > 4)
        {
            DumpRoot("after ChangeScene");
            GetTree().Quit();
        }
    }

    private void DumpRoot(string tag)
    {
        string s = "";
        foreach (Node n in GetTree().Root.GetChildren()) { s += n.Name + "(" + n.GetType().Name + ") "; }
        Util.Log("Root children " + tag + ": " + s + "| CurrentScene=" + GetTree().CurrentScene?.Name);
    }
}
