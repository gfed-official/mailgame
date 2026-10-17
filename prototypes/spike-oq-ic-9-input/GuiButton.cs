using Godot;

namespace SpikeOqIc9Input;

/// <summary>Button that logs its _GuiInput and counts activations (Pressed signal / ButtonDown / ButtonUp).</summary>
public partial class GuiButton : Button
{
    public int PressedCount;
    public int DownCount;
    public int UpCount;
    public int ShortcutCount;

    public override void _Ready()
    {
        Pressed += () => { PressedCount++; Trace.Add(Name, "SIGNAL pressed", null); };
        ButtonDown += () => { DownCount++; Trace.Add(Name, "SIGNAL down", null); };
        ButtonUp += () => { UpCount++; Trace.Add(Name, "SIGNAL up", null); };
    }

    public override void _GuiInput(InputEvent @event) => Trace.Add(Name, "gui", @event);

    // Control has its own _Input/_ShortcutInput virtuals; log them to see Button's position in the order.
    public override void _Input(InputEvent @event) => Trace.Add(Name, "input", @event);
    public override void _ShortcutInput(InputEvent @event) => Trace.Add(Name, "shortcut", @event);
    public override void _UnhandledKeyInput(InputEvent @event) => Trace.Add(Name, "unhandledkey", @event);
    public override void _UnhandledInput(InputEvent @event) => Trace.Add(Name, "unhandled", @event);
}
