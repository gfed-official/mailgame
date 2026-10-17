using Godot;

namespace SpikeOqIc9Input;

/// <summary>AC-5 headless part (S-30..S-33). Proves handler paths only, never OS delivery.</summary>
public static class FocusProbe
{
    public static async Task Run(Node host)
    {
        SceneTree tree = host.GetTree();
        var rx = new FocusRx { Name = "FocusRx" };
        tree.Root.AddChild(rx);
        await Util.Frames(tree, 2);

        Util.Log("=== S-30 Notification() delivered to a node's _Notification (handler path only, NOT OS delivery)");
        foreach (int w in new[] { (int)Node.NotificationApplicationFocusOut, (int)Node.NotificationApplicationFocusIn, (int)Node.NotificationWMWindowFocusOut, (int)Node.NotificationWMWindowFocusIn })
        {
            rx.Seen.Clear();
            rx.Notification(w);
            Util.Row("S-30", $"rx.Notification({w}) direct -> handler saw [{string.Join(",", rx.Seen)}]");
        }
        foreach (int w in new[] { (int)Node.NotificationApplicationFocusOut, (int)Node.NotificationApplicationFocusIn })
        {
            rx.Seen.Clear();
            tree.Notification(w);
            Util.Row("S-30", $"SceneTree.Notification({w}) (the MainLoop path the OS uses for application focus) -> node saw [{string.Join(",", rx.Seen)}] (does SceneTree propagate it to nodes?)");
        }
        foreach (int w in new[] { (int)Node.NotificationWMWindowFocusOut, (int)Node.NotificationWMWindowFocusIn })
        {
            rx.Seen.Clear();
            tree.Root.Notification(w);
            Util.Row("S-30", $"Root Window.Notification({w}) -> child node saw [{string.Join(",", rx.Seen)}] (is a window notification propagated to children?)");
        }

        Util.Log("=== S-31 Input.MouseMode switching headless (no real pointer)");
        Util.Row("S-31", $"initial Input.MouseMode={Input.MouseMode} DisplayServer.MouseGetMode={DisplayServer.MouseGetMode()} HasFeature(Mouse)={DisplayServer.HasFeature(DisplayServer.Feature.Mouse)} HasFeature(MouseWarp)={DisplayServer.HasFeature(DisplayServer.Feature.MouseWarp)}");
        Input.MouseMode = Input.MouseModeEnum.Captured;
        await Util.Frames(tree, 1);
        Util.Row("S-31", $"after set Captured: Input.MouseMode={Input.MouseMode} DisplayServer.MouseGetMode={DisplayServer.MouseGetMode()}");
        Input.MouseMode = Input.MouseModeEnum.Visible;
        await Util.Frames(tree, 1);
        Util.Row("S-31", $"after set Visible: Input.MouseMode={Input.MouseMode} mouse pos (DisplayServer.MouseGetPosition)={DisplayServer.MouseGetPosition()} viewport mouse pos={tree.Root.GetMousePosition()}");
        Util.Row("S-31", "cursor-position restore on Visible, capture re-apply after focus-in, Wayland pointer lock: see window-*.log (short windowed run) and MANUAL M-2/M-3");

        Util.Log("=== S-32 ignore_joypad_on_unfocused_application at runtime");
        bool v0 = Input.IgnoreJoypadOnUnfocusedApplication;
        Input.IgnoreJoypadOnUnfocusedApplication = true;
        Util.Row("S-32", $"Input.IgnoreJoypadOnUnfocusedApplication default(project unset)={v0}; settable at runtime -> {Input.IgnoreJoypadOnUnfocusedApplication}. Honoured with a real pad while unfocused: NOT RUN - no pad attached (MANUAL M-1)");
        Input.IgnoreJoypadOnUnfocusedApplication = v0;

        Util.Log("=== S-33 held-input state across a synthetic focus-out (does the ENGINE release held keys on focus loss?)");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F10, PhysicalKeycode = Key.F10, Pressed = true });
        Input.FlushBufferedEvents();
        bool held0 = Input.IsKeyPressed(Key.F10);
        tree.Root.Notification((int)Node.NotificationWMWindowFocusOut);
        tree.Notification((int)Node.NotificationApplicationFocusOut);
        await Util.Frames(tree, 1);
        Util.Row("S-33", $"IsKeyPressed(F10) before synthetic focus-out={held0}, after Notification(WMWindowFocusOut + ApplicationFocusOut)={Input.IsKeyPressed(Key.F10)} (handler-path only; real OS focus loss MANUAL M-2)");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F10, PhysicalKeycode = Key.F10, Pressed = false });
        Input.FlushBufferedEvents();
        tree.Notification((int)Node.NotificationApplicationFocusIn);
        rx.QueueFree();
        await Util.Frames(tree, 1);
    }
}
