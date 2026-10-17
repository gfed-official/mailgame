using Godot;

namespace SpikeOqIc9Input;

/// <summary>
/// AC-5 short WINDOWED run (S-34): which focus notifications arrive on window open, mouse-mode switching
/// with a real display server, cursor position across Captured -> Visible, live layout labels.
/// Auto-quits after ~3.5 s; always restores MouseMode.Visible. Never moves the desktop's focus itself.
/// </summary>
public static class WindowProbe
{
    public static async Task Run(Node host)
    {
        SceneTree tree = host.GetTree();
        var rx = new FocusRx { Name = "FocusRx" };
        tree.Root.AddChild(rx);
        try
        {
            Util.Row("S-34", $"display server={DisplayServer.GetName()} session XDG_SESSION_TYPE={OS.GetEnvironment("XDG_SESSION_TYPE")} window size={DisplayServer.WindowGetSize()} WindowIsFocused={DisplayServer.WindowIsFocused()} t={Time.GetTicksMsec()}ms");
            ulong t0 = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - t0 < 1000) { await Util.Frames(tree, 1); }
            Util.Row("S-34", $"after 1 s: WindowIsFocused={DisplayServer.WindowIsFocused()} notifications so far=[{string.Join(",", rx.Seen)}] mouse pos={DisplayServer.MouseGetPosition()}");
            Vector2I before = DisplayServer.MouseGetPosition();
            Input.MouseMode = Input.MouseModeEnum.Captured;
            await Util.Frames(tree, 2);
            Util.Row("S-34", $"set Captured: Input.MouseMode={Input.MouseMode} DisplayServer.MouseGetMode={DisplayServer.MouseGetMode()} mouse pos={DisplayServer.MouseGetPosition()} (before={before})");
            t0 = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - t0 < 700) { await Util.Frames(tree, 1); }
            Input.MouseMode = Input.MouseModeEnum.Visible;
            await Util.Frames(tree, 2);
            Util.Row("S-34", $"set Visible: Input.MouseMode={Input.MouseMode} mouse pos={DisplayServer.MouseGetPosition()} (before capture={before}; equal => position restored or never moved)");
            Util.Row("S-34", $"labels (live layout): A->{OS.GetKeycodeString(DisplayServer.KeyboardGetLabelFromPhysical(Key.A))} Q->{OS.GetKeycodeString(DisplayServer.KeyboardGetLabelFromPhysical(Key.Q))} W->{OS.GetKeycodeString(DisplayServer.KeyboardGetLabelFromPhysical(Key.W))} Z->{OS.GetKeycodeString(DisplayServer.KeyboardGetLabelFromPhysical(Key.Z))} layouts={DisplayServer.KeyboardGetLayoutCount()} current={DisplayServer.KeyboardGetCurrentLayout()} name='{(DisplayServer.KeyboardGetLayoutCount() > 0 ? DisplayServer.KeyboardGetLayoutName(DisplayServer.KeyboardGetCurrentLayout()) : "n/a")}'");
            t0 = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - t0 < 1500) { await Util.Frames(tree, 1); }
            Util.Row("S-34", $"end: notifications received during run (in order)=[{string.Join(",", rx.Seen)}] WindowIsFocused={DisplayServer.WindowIsFocused()}");
        }
        finally
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }
}
