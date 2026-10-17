using Godot;

namespace SpikeOqIc9Input;

/// <summary>Control receiver (stands in for a debug overlay / menu); mouse filter ignore so it never eats mouse GUI input.</summary>
public partial class RecvControl : Control
{
    public override void _Input(InputEvent @event) => Trace.Add(Name, "input", @event);
    public override void _ShortcutInput(InputEvent @event) => Trace.Add(Name, "shortcut", @event);
    public override void _UnhandledKeyInput(InputEvent @event) => Trace.Add(Name, "unhandledkey", @event);
    public override void _UnhandledInput(InputEvent @event) => Trace.Add(Name, "unhandled", @event);
    public override void _GuiInput(InputEvent @event) => Trace.Add(Name, "gui", @event);
}
