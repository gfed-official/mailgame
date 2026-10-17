using Godot;

namespace SpikeOqIc9Input;

/// <summary>Plain Node receiver for every input phase; optionally marks events handled at one phase.</summary>
public partial class RecvNode : Node
{
    /// <summary>"", "input", "shortcut", "unhandledkey", "unhandled".</summary>
    public string HandleAt = "";
    public bool HandlePressed = true;
    public bool HandleReleased = true;
    public bool KeysOnly = true;
    public bool LogProcess = false;

    private bool Match(InputEvent e)
    {
        if (KeysOnly && e is not InputEventKey) { return false; }
        bool pressed = e.IsPressed();
        return pressed ? HandlePressed : HandleReleased;
    }

    private void Do(string phase, InputEvent e)
    {
        Trace.Add(Name, phase, e);
        if (HandleAt == phase && Match(e))
        {
            GetViewport().SetInputAsHandled();
            Trace.Add(Name, "handled->polled", null);
            Util.Log("    polled right after SetInputAsHandled in " + phase + ": " + Util.Poll() + " IsInputHandled=" + GetViewport().IsInputHandled());
        }
    }

    public override void _Input(InputEvent @event) => Do("input", @event);
    public override void _ShortcutInput(InputEvent @event) => Do("shortcut", @event);
    public override void _UnhandledKeyInput(InputEvent @event) => Do("unhandledkey", @event);
    public override void _UnhandledInput(InputEvent @event) => Do("unhandled", @event);

    public override void _Process(double delta)
    {
        if (LogProcess) { Trace.Add(Name, "_Process", null); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (LogProcess) { Trace.Add(Name, "_PhysicsProcess", null); }
    }
}
