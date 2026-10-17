using Godot;

namespace SpikeOqIc9Input;

/// <summary>
/// AC-3: event order and handled state (S-01..S-09). All events are SYNTHETIC (Input.ParseInputEvent), headless.
/// </summary>
public static class OrderProbe
{
    private sealed class Fixture
    {
        public RecvNode Early = null!;
        public RecvNode Plain = null!;
        public RecvNode Late = null!;
        public RecvControl Overlay = null!;
        public GuiButton Button = null!;
        public Control UiHost = null!;
        public readonly List<Node> All = new();
    }

    private static async Task<Fixture> Build(Node host, int earlyPriority = int.MinValue, string place = "first", bool priorityAfterAdd = false,
                                              string handleAt = "", bool handlePressed = true, bool handleReleased = true, bool focusButton = true, string handler = "Early")
    {
        Window root = host.GetTree().Root;
        var f = new Fixture();
        f.Early = new RecvNode { Name = "Early" };
        if (!priorityAfterAdd) { f.Early.ProcessPriority = earlyPriority; }
        f.Plain = new RecvNode { Name = "Plain" };
        f.Late = new RecvNode { Name = "Late" };
        RecvNode h = handler == "Late" ? f.Late : f.Early;
        h.HandleAt = handleAt; h.HandlePressed = handlePressed; h.HandleReleased = handleReleased;
        f.Overlay = new RecvControl { Name = "Overlay", MouseFilter = Control.MouseFilterEnum.Ignore };
        f.UiHost = new Control { Name = "UiHost", MouseFilter = Control.MouseFilterEnum.Ignore };
        f.UiHost.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        f.Button = new GuiButton { Name = "Button", Text = "probe", Position = new Vector2(10, 10), CustomMinimumSize = new Vector2(200, 60) };
        f.Button.Shortcut = new Shortcut { Events = new Godot.Collections.Array { new InputEventKey { Keycode = Key.F6 } } };
        f.UiHost.AddChild(f.Button);

        if (place == "first") { root.AddChild(f.Early); root.MoveChild(f.Early, 0); }
        else if (place == "none") { root.AddChild(f.Early); }
        root.AddChild(f.Plain);
        root.AddChild(f.UiHost);
        root.AddChild(f.Overlay);
        if (place == "last") { root.AddChild(f.Early); }
        root.AddChild(f.Late);
        if (priorityAfterAdd) { f.Early.ProcessPriority = earlyPriority; }
        f.All.AddRange(new Node[] { f.Early, f.Plain, f.UiHost, f.Overlay, f.Late });
        await Util.Frames(host.GetTree(), 2);
        if (focusButton) { f.Button.GrabFocus(); await Util.Frames(host.GetTree(), 1); }
        return f;
    }

    private static async Task Free(Node host, Fixture f)
    {
        foreach (Node n in f.All) { if (GodotObject.IsInstanceValid(n)) { n.QueueFree(); } }
        await Util.Frames(host.GetTree(), 2);
        Trace.Clear();
    }

    private static InputEventKey K(Key k, bool pressed, bool echo = false) =>
        new() { Keycode = k, PhysicalKeycode = k, Pressed = pressed, Echo = echo };

    private static void Send(InputEvent e, bool flush = true)
    {
        Input.ParseInputEvent(e);
        if (flush) { Input.FlushBufferedEvents(); }
    }

    private static string Counts(Fixture f) =>
        $"Button.PressedSignals={f.Button.PressedCount} down={f.Button.DownCount} up={f.Button.UpCount} ButtonPressed={f.Button.ButtonPressed}";

    private static string Seen(string phase) => Trace.Order(phase) is { Length: > 0 } s ? s : "(nobody)";

    public static async Task Run(Node host)
    {
        SceneTree tree = host.GetTree();
        Trace.Print = true;

        // ---------------------------------------------------------------- S-00 startup: from which frame are injected events delivered?
        Util.Log("=== S-00 startup: are synthetic events delivered in the first frames? (fixture built after one frame, F7 injected + flushed every frame)");
        Trace.Print = false;
        var f0 = await Build(host, focusButton: false);
        for (int i = 0; i < 6; i++)
        {
            Trace.Clear();
            Send(K(Key.F7, i % 2 == 0));
            Util.Log($"  [S-00] frame={Engine.GetProcessFrames()} elapsed_msec={Time.GetTicksMsec()} root.IsInsideTree={host.GetTree().Root.IsInsideTree()} _Input calls={Trace.Events.Count(x => x.Phase == "input")} Window.HasFocus={host.GetTree().Root.HasFocus()} Visible={host.GetTree().Root.Visible}");
            await Util.Frames(host.GetTree(), 1);
        }
        Trace.Print = true;
        await Free(host, f0);

        // ---------------------------------------------------------------- S-01 delivery order (AC-3a)
        Util.Log("=== S-01 delivery order, Space press/release, nothing handled (ADR item 1)");
        var f = await Build(host);
        Util.Log($"focus owner = {tree.Root.GuiGetFocusOwner()?.Name} (Button focused: {f.Button.HasFocus()}); Input.UseAccumulatedInput={Input.UseAccumulatedInput}");
        foreach (Key key in new[] { Key.Space, Key.Enter })
        {
            Util.Log($"--- {key} press");
            Trace.Clear();
            ulong fr0 = Engine.GetProcessFrames();
            Send(K(key, true), flush: false);
            int afterParse = Trace.Lines.Count;
            Input.FlushBufferedEvents();
            int afterFlush = Trace.Lines.Count;
            Util.Log($"  [S-01 same-frame] callbacks right after ParseInputEvent (no flush)={afterParse}; right after FlushBufferedEvents={afterFlush}; frame at inject={fr0} frame after flush={Engine.GetProcessFrames()}");
            await Util.Frames(tree, 1);
            Util.Log($"  [S-01 same-frame] callbacks after one more frame={Trace.Lines.Count}");
            Util.Log($"--- {key} release");
            Send(K(key, false));
            Util.Log("  " + Counts(f));
            Util.Log($"  ORDER input: {Seen("input")}");
            Util.Log($"  ORDER gui: {Seen("gui")}");
            Util.Log($"  ORDER shortcut: {Seen("shortcut")}");
            Util.Log($"  ORDER unhandledkey: {Seen("unhandledkey")}");
            Util.Log($"  ORDER unhandled: {Seen("unhandled")}");
            Trace.Clear();
        }
        // F6 = Button.Shortcut (Control shortcut stage)
        Util.Log("--- F6 press/release (Button.Shortcut = F6; Control shortcut fires between GUI and unhandled)");
        Trace.Clear();
        int before = f.Button.PressedCount;
        Send(K(Key.F6, true));
        Send(K(Key.F6, false));
        Util.Log($"  F6: Button.PressedSignals delta={f.Button.PressedCount - before}");
        Util.Log($"  ORDER input: {Seen("input")} | gui: {Seen("gui")} | shortcut: {Seen("shortcut")} | unhandledkey: {Seen("unhandledkey")} | unhandled: {Seen("unhandled")}");
        // F7 = no GUI meaning: full pass to unhandled
        Util.Log("--- F7 press (no GUI meaning; reaches every phase)");
        Trace.Clear();
        Send(K(Key.F7, true));
        Send(K(Key.F7, false));
        Util.Log($"  ORDER input: {Seen("input")} | gui: {Seen("gui")} | shortcut: {Seen("shortcut")} | unhandledkey: {Seen("unhandledkey")} | unhandled: {Seen("unhandled")}");
        await Free(host, f);

        // ---------------------------------------------------------------- S-02/S-03 SetInputAsHandled matrix (AC-3b, 3c)
        Util.Log("=== S-02 / S-03 SetInputAsHandled effect on GUI activation + later receivers; polled reads while handled (ADR item 1, GDD polled-read behaviour)");
        var rows = new (string Name, string At, bool P, bool R, string H)[]
        {
            ("none handled (control)", "", true, true, "Early"),
            ("[Early = LAST in _Input order] handled in _Input: press+release", "input", true, true, "Early"),
            ("[Early] handled in _Input: press only", "input", true, false, "Early"),
            ("[Early] handled in _Input: release only", "input", false, true, "Early"),
            ("[Late = FIRST in _Input order] handled in _Input: press+release", "input", true, true, "Late"),
            ("[Late] handled in _Input: press only", "input", true, false, "Late"),
            ("[Late] handled in _Input: release only", "input", false, true, "Late"),
            ("[Early] handled in _ShortcutInput: press+release", "shortcut", true, true, "Early"),
            ("[Early] handled in _UnhandledKeyInput: press+release", "unhandledkey", true, true, "Early"),
            ("[Late] handled in _ShortcutInput: press+release", "shortcut", true, true, "Late"),
            ("[Late] handled in _UnhandledKeyInput: press+release", "unhandledkey", true, true, "Late"),
        };
        foreach (var r in rows)
        {
            Util.Log($"--- {r.Name}");
            f = await Build(host, handleAt: r.At, handlePressed: r.P, handleReleased: r.R, handler: r.H);
            Trace.Clear();
            Send(K(Key.Space, true));
            Util.Log($"  after press : poll {Util.Poll()}  IsInputHandled(after flush)={tree.Root.IsInputHandled()}");
            Util.Log($"  press ORDER input: {Seen("input")} | gui: {Seen("gui")} | shortcut: {Seen("shortcut")} | unhandledkey: {Seen("unhandledkey")} | unhandled: {Seen("unhandled")}");
            var pressTrace = new List<string>(Trace.Lines);
            Trace.Clear();
            await Util.Frames(tree, 1);
            Util.Log($"  next frame  : poll {Util.Poll()}");
            Send(K(Key.Space, false));
            Util.Log($"  after release: poll {Util.Poll()}");
            Util.Log($"  release ORDER input: {Seen("input")} | gui: {Seen("gui")} | shortcut: {Seen("shortcut")} | unhandledkey: {Seen("unhandledkey")} | unhandled: {Seen("unhandled")}");
            Util.Log("  RESULT " + r.Name + ": " + Counts(f));
            await Free(host, f);
        }

        // ---------------------------------------------------------------- S-04 release consumed by GUI still seen by earlier _Input (AC-3d)
        Util.Log("=== S-04 releases vs GUI (AC-3d)");
        f = await Build(host);
        Trace.Clear();
        Send(K(Key.Space, true));
        Send(K(Key.Space, false));
        bool earlyRelease = Trace.Events.Any(x => x.Node == "Early" && x.Phase == "input" && x.Ev.IsReleased());
        bool guiRelease = Trace.Events.Any(x => x.Node == "Button" && x.Phase == "gui" && x.Ev.IsReleased());
        bool lateRelease = Trace.Events.Any(x => x.Node == "Late" && x.Phase == "unhandled" && x.Ev.IsReleased());
        bool lateKeyRelease = Trace.Events.Any(x => x.Node == "Late" && x.Phase == "unhandledkey" && x.Ev.IsReleased());
        Util.Log($"  [S-04a] Space release: Early._Input saw it={earlyRelease}; Button GUI consumed it (_GuiInput saw it)={guiRelease}; Late._UnhandledKeyInput saw it={lateKeyRelease}; Late._UnhandledInput saw it={lateRelease}");
        await Free(host, f);

        Util.Log("--- S-04b: Space pressed (GUI takes it), then the focused Button is freed before the release arrives");
        f = await Build(host);
        Trace.Clear();
        Send(K(Key.Space, true));
        f.Button.QueueFree();
        await Util.Frames(tree, 2);
        Trace.Clear();
        Send(K(Key.Space, false));
        Util.Log($"  [S-04b] release after the GUI owner was freed: Early._Input saw it={Trace.Events.Any(x => x.Node == "Early" && x.Phase == "input" && x.Ev.IsReleased())}; focus owner now={tree.Root.GuiGetFocusOwner()?.Name ?? "(none)"}; ORDER input: {Seen("input")} | unhandled: {Seen("unhandled")}");
        await Free(host, f);

        Util.Log("--- S-04c: mouse press on the Button (mouse grab), Button hidden, then the release arrives");
        f = await Build(host, focusButton: false);
        Send(new InputEventMouseMotion { Position = new Vector2(100, 40), GlobalPosition = new Vector2(100, 40) });
        await Util.Frames(tree, 1);
        Trace.Clear();
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left, Position = new Vector2(100, 40), GlobalPosition = new Vector2(100, 40) });
        Util.Log($"  after mouse press: {Counts(f)} Button.HasFocus={f.Button.HasFocus()} (FocusMode=All default; click focus)");
        f.Button.Visible = false;
        await Util.Frames(tree, 1);
        Trace.Clear();
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, ButtonMask = 0, Position = new Vector2(100, 40), GlobalPosition = new Vector2(100, 40) });
        Util.Log($"  [S-04c] mouse release after the GUI node was hidden: Early._Input saw it={Trace.Events.Any(x => x.Node == "Early" && x.Phase == "input" && x.Ev.IsReleased())}; ORDER input: {Seen("input")} | unhandled: {Seen("unhandled")}");
        await Free(host, f);

        // ---------------------------------------------------------------- S-05 frame order (AC-3e)
        Util.Log("=== S-05 are a frame's _Input events delivered before that frame's _Process? (ADR item 2)");
        f = await Build(host, focusButton: false);
        f.Early.LogProcess = true;
        f.Plain.LogProcess = true;
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Trace.Clear();
        Trace.Add("probe", "inject(noflush)", null);
        Send(K(Key.F7, true), flush: false);
        Util.Log($"  [S-05a] callbacks immediately after ParseInputEvent without flush = {Trace.Events.Count(x => x.Phase == "input")} _Input calls");
        await Util.Frames(tree, 3);
        Util.Log("  [S-05a] ^ trace above: first _Input vs the _Process/_PhysicsProcess of the injecting frame and the next frames");
        Trace.Clear();
        Trace.Add("probe", "inject+flush", null);
        Send(K(Key.F7, false), flush: true);
        Util.Log($"  [S-05b] callbacks immediately after ParseInputEvent+FlushBufferedEvents = {Trace.Events.Count(x => x.Phase == "input")} _Input calls (synchronous)");
        await Util.Frames(tree, 2);
        await Free(host, f);

        // ---------------------------------------------------------------- S-06 node-order control (AC-3f)
        Util.Log("=== S-06 receiver order for one F7 press: tree order vs ProcessPriority (ADR Engine Validation note 3)");
        var cfgs = new (string Name, int Prio, string Place, bool After)[]
        {
            ("P0 control: Early FIRST child, priority 0", 0, "first", false),
            ("P1 Early LAST child, priority 0", 0, "last", false),
            ("P2 Early FIRST child, priority int.MinValue (set before AddChild)", int.MinValue, "first", false),
            ("P3 Early LAST child, priority int.MinValue (set before AddChild)", int.MinValue, "last", false),
            ("P4 Early FIRST child, priority int.MaxValue", int.MaxValue, "first", false),
            ("P5 Early LAST child, priority int.MinValue set AFTER AddChild", int.MinValue, "last", true),
            ("P6 Early FIRST child, priority -100", -100, "first", false),
        };
        foreach (var c in cfgs)
        {
            f = await Build(host, c.Prio, c.Place, c.After, focusButton: false);
            Trace.Clear();
            Send(K(Key.F7, true));
            Util.Log($"  [S-06] {c.Name}");
            Util.Log($"         tree order of root children: {string.Join(", ", tree.Root.GetChildren().Select(n => n.Name))}");
            Util.Log($"         _Input order: {Seen("input")}");
            Util.Log($"         _ShortcutInput order: {Seen("shortcut")}");
            Util.Log($"         _UnhandledKeyInput order: {Seen("unhandledkey")}");
            Util.Log($"         _UnhandledInput order: {Seen("unhandled")}");
            await Free(host, f);
        }

        Util.Log("--- S-06b Window.WindowInput signal position in the order (Early FIRST child, prio MinValue)");
        f = await Build(host, int.MinValue, "first", focusButton: false);
        Window.WindowInputEventHandler wi = ev => Trace.Add("WINDOWSIG", "windowinput", ev);
        tree.Root.WindowInput += wi;
        Trace.Clear();
        Send(K(Key.F7, true));
        Util.Log($"  [S-06b] first 3 callbacks of the F7 press, in call order: {string.Join(" , ", Trace.Events.Take(3).Select(x => x.Node + ":" + x.Phase))}");
        Util.Log($"  [S-06b] windowinput signal fired {Trace.Events.Count(x => x.Phase == "windowinput")} time(s); position of WINDOWSIG relative to the first _Input receiver: {(Trace.Events.FindIndex(x => x.Node == "WINDOWSIG"))} (0 = before every _Input)");
        tree.Root.WindowInput -= wi;
        await Free(host, f);

        Util.Log("--- S-06c Window.WindowInput handler calls SetInputAsHandled(): does it stop _Input receivers / GUI activation? (Space press+release, Button focused)");
        f = await Build(host, int.MinValue, "first");
        Window.WindowInputEventHandler wih = ev =>
        {
            Trace.Add("WINDOWSIG", "windowinput", ev);
            tree.Root.SetInputAsHandled();
            Util.Log($"    in WindowInput handler after SetInputAsHandled: IsInputHandled={tree.Root.IsInputHandled()} poll {Util.Poll()}");
        };
        tree.Root.WindowInput += wih;
        Trace.Clear();
        Send(K(Key.Space, true));
        Util.Log($"  [S-06c] press: ORDER windowinput: {Seen("windowinput")} | input: {Seen("input")} | gui: {Seen("gui")} | unhandled: {Seen("unhandled")}");
        Send(K(Key.Space, false));
        Util.Log($"  [S-06c] after release: {Counts(f)}");
        tree.Root.WindowInput -= wih;
        await Free(host, f);

        // ---------------------------------------------------------------- S-07 fallback: FocusMode.None + GuiReleaseFocus (NO-GO fallback path)
        Util.Log("=== S-07 GUI-activation fallbacks when SetInputAsHandled is not used");
        f = await Build(host);
        Send(K(Key.Space, true)); Send(K(Key.Space, false));
        Util.Log($"  [S-07a] control, Button focused, Space press+release: {Counts(f)}");
        await Free(host, f);
        f = await Build(host, focusButton: false);
        f.Button.FocusMode = Control.FocusModeEnum.None;
        f.Button.GrabFocus();
        await Util.Frames(tree, 1);
        Send(K(Key.Space, true)); Send(K(Key.Space, false));
        Util.Log($"  [S-07b] FocusMode.None (GrabFocus attempted), Space press+release: {Counts(f)} HasFocus={f.Button.HasFocus()}");
        await Free(host, f);
        f = await Build(host);
        tree.Root.GuiReleaseFocus();
        await Util.Frames(tree, 1);
        Send(K(Key.Space, true)); Send(K(Key.Space, false));
        Util.Log($"  [S-07c] Viewport.GuiReleaseFocus() after focus, Space press+release: {Counts(f)} focus owner={tree.Root.GuiGetFocusOwner()?.Name ?? "(none)"}");
        // Early still receives with no GUI focus (adapter does not depend on focus)
        Util.Log($"  [S-07c] Early._Input still saw Space press/release without GUI focus: {Trace.Events.Count(x => x.Node == "Early" && x.Phase == "input")} events");
        await Free(host, f);

        // ---------------------------------------------------------------- S-08 mouse / joypad through the same pipeline
        Util.Log("=== S-08 mouse + joypad button/motion events through the same phase order");
        f = await Build(host, focusButton: false);
        Send(new InputEventMouseMotion { Position = new Vector2(100, 40), GlobalPosition = new Vector2(100, 40), Relative = new Vector2(3, 0), ScreenRelative = new Vector2(3, 0) });
        await Util.Frames(tree, 1);
        Trace.Clear();
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left, Position = new Vector2(100, 40), GlobalPosition = new Vector2(100, 40) });
        Send(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, ButtonMask = 0, Position = new Vector2(100, 40), GlobalPosition = new Vector2(100, 40) });
        Util.Log($"  [S-08a-diag] Button global rect={f.Button.GetGlobalRect()} size={f.Button.Size} hovered={tree.Root.GuiGetHoveredControl()?.Name ?? "(none)"} MouseFilter={f.Button.MouseFilter} Disabled={f.Button.Disabled}");
        Util.Log($"  [S-08a] mouse click on Button: {Counts(f)}; ORDER input: {Seen("input")} | gui: {Seen("gui")} | unhandled: {Seen("unhandled")}");
        Trace.Clear();
        f.Button.GrabFocus();
        await Util.Frames(tree, 1);
        Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = true });
        Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = false });
        Util.Log($"  [S-08b] joypad A (device 0) press/release with Button focused: {Counts(f)} (ui_accept has no joypad event, B5); ORDER input: {Seen("input")} | gui: {Seen("gui")} | unhandled: {Seen("unhandled")}");
        Trace.Clear();
        Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.Y, Pressed = true });
        Send(new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.Y, Pressed = false });
        Util.Log($"  [S-08c] joypad Y (device 0; bound to ui_select in the default map, device -1 = any) with Button focused: {Counts(f)}; ORDER input: {Seen("input")} | gui: {Seen("gui")} | unhandled: {Seen("unhandled")}");
        Trace.Clear();
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0.9f });
        Util.Log($"  [S-08d] joypad LeftX 0.9 (ui_right bound at |1.0|): ORDER input: {Seen("input")} | gui: {Seen("gui")} | unhandled: {Seen("unhandled")}; focus owner={tree.Root.GuiGetFocusOwner()?.Name}");
        Send(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0f });
        await Free(host, f);

        // ---------------------------------------------------------------- S-09 pause + ProcessMode
        Util.Log("=== S-09 ProcessMode / SceneTree.Paused (ADR notes: adapter ProcessMode=Always)");
        f = await Build(host, focusButton: false);
        f.Late.ProcessMode = Node.ProcessModeEnum.Always;
        tree.Paused = true;
        await Util.Frames(tree, 1);
        Trace.Clear();
        Send(K(Key.F7, true));
        Util.Log($"  [S-09] SceneTree.Paused=true; Early(Inherit) / Plain(Inherit) / Late(Always): _Input receivers = {Seen("input")}");
        tree.Paused = false;
        await Free(host, f);
    }
}
