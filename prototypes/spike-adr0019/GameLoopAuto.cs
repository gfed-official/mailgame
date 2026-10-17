using Godot;

namespace SpikeAdr0019;

public partial class GameLoopAuto : Node
{
    public override void _EnterTree()
    {
        if (!Util.HasArg("--oq1")) { return; }
        string? p = Util.ArgValue("--auto-prio");
        if (p == "min") { ProcessPriority = int.MinValue; }
        Util.Log("GameLoopAuto _EnterTree prio=" + ProcessPriority + " scene=" + (GetTree().CurrentScene?.Name.ToString() ?? "<null>"));
    }

    public override void _Ready()
    {
        if (!Util.HasArg("--oq1")) { return; }
        Util.Log("GameLoopAuto _Ready scene=" + (GetTree().CurrentScene?.Name.ToString() ?? "<null>"));
    }

    public override void _Process(double delta)
    {
        if (!Util.HasArg("--oq1")) { return; }
        if (Engine.GetProcessFrames() < 8) { Util.Log("GameLoopAuto _Process"); }
    }
}
