using Godot;

namespace SpikeOqIc9Input;

/// <summary>AC-4: devices and axes (S-10..S-19). All events SYNTHETIC; no pad is attached.</summary>
public static class DeviceProbe
{
    /// <summary>Candidate adapter rule: classify by event TYPE first, then by Device.</summary>
    public static string Classify(InputEvent e)
    {
        switch (e)
        {
            case InputEventAction: return "Synthetic(action; never changes last-used device)";
            case InputEventJoypadButton jb: return $"Pad#{jb.Device}";
            case InputEventJoypadMotion jm: return $"Pad#{jm.Device}";
            case InputEventKey k:
                return k.Device == InputEvent.DeviceIdKeyboard ? "Keyboard" : $"Keyboard(unexpected device {k.Device})";
            case InputEventMouseButton:
            case InputEventMouseMotion:
                return e.Device == InputEvent.DeviceIdMouse ? "Mouse"
                     : e.Device == InputEvent.DeviceIdEmulation ? "Mouse(emulation id)"
                     : $"Mouse(unexpected device {e.Device})";
            default: return "Other:" + e.GetType().Name;
        }
    }

    /// <summary>The WRONG rule (device first): shows why type-first is needed.</summary>
    public static string NaiveDeviceFirst(InputEvent e) =>
        e.Device == InputEvent.DeviceIdKeyboard ? "Keyboard" : e.Device == InputEvent.DeviceIdMouse ? "Mouse" : e.Device == InputEvent.DeviceIdEmulation ? "Mouse(emu)" : "Pad/other";

    private static InputEvent? Deliver(RecvNode rx, InputEvent ev)
    {
        Trace.Clear();
        Input.ParseInputEvent(ev);
        Input.FlushBufferedEvents();
        foreach (var x in Trace.Events) { if (x.Phase == "input") { return x.Ev; } }
        return null;
    }

    public static async Task Run(Node host)
    {
        SceneTree tree = host.GetTree();
        Trace.Print = false;
        var rx = new RecvNode { Name = "Rx" };
        tree.Root.AddChild(rx);
        await Util.Frames(tree, 2);

        Util.Log("=== S-10 compile-checked C# names (this file compiled): InputEvent.DeviceIdKeyboard/DeviceIdMouse/DeviceIdEmulation, InputEvent.Device, Input.JoyConnectionChanged, Input.GetJoyAxis, Input.GetJoyInfo, Input.MouseMode, Input.UseAccumulatedInput, DisplayServer.KeyboardGetLabelFromPhysical");
        Util.Row("S-10", $"Device property type={typeof(InputEvent).GetProperty("Device")!.PropertyType.Name}, DeviceIdKeyboard type={InputEvent.DeviceIdKeyboard.GetType().Name}; DeviceIdKeyboard={InputEvent.DeviceIdKeyboard} DeviceIdMouse={InputEvent.DeviceIdMouse} DeviceIdEmulation={InputEvent.DeviceIdEmulation}");

        // ---- S-11 classification table
        Util.Log("=== S-11 classification: event TYPE first, then Device (injected via ParseInputEvent+Flush, classified AFTER delivery to _Input)");
        var cases = new (string Name, Func<InputEvent> Make)[]
        {
            ("InputEventAction ui_accept (device default 0)", () => new InputEventAction { Action = "ui_accept", Pressed = true }),
            ("InputEventJoypadButton A device 0", () => new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = true }),
            ("InputEventJoypadButton A device 1", () => new InputEventJoypadButton { Device = 1, ButtonIndex = JoyButton.A, Pressed = true }),
            ("InputEventJoypadButton A device 16 (== DeviceIdKeyboard)", () => new InputEventJoypadButton { Device = 16, ButtonIndex = JoyButton.A, Pressed = true }),
            ("InputEventJoypadButton A device 32 (== DeviceIdMouse)", () => new InputEventJoypadButton { Device = 32, ButtonIndex = JoyButton.A, Pressed = true }),
            ("InputEventJoypadMotion LeftX device 16", () => new InputEventJoypadMotion { Device = 16, Axis = JoyAxis.LeftX, AxisValue = 0.5f }),
            ("InputEventJoypadMotion LeftX device 32", () => new InputEventJoypadMotion { Device = 32, Axis = JoyAxis.LeftX, AxisValue = 0.5f }),
            ("InputEventKey F7 (device default)", () => new InputEventKey { Keycode = Key.F7, PhysicalKeycode = Key.F7, Pressed = true }),
            ("InputEventKey F7 Device=0 (old-style keyboard id)", () => new InputEventKey { Keycode = Key.F7, PhysicalKeycode = Key.F7, Pressed = true, Device = 0 }),
            ("InputEventMouseButton Right (device default)", () => new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(2000, 2000) }),
            ("InputEventMouseMotion (device default)", () => new InputEventMouseMotion { Position = new Vector2(2000, 2000), Relative = new Vector2(1, 0) }),
            ("InputEventMouseButton Right Device=DeviceIdEmulation", () => new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(2000, 2000), Device = (int)InputEvent.DeviceIdEmulation }),
        };
        foreach (var c in cases)
        {
            var ev = c.Make();
            long dev = ev.Device;
            var got = Deliver(rx, ev);
            Util.Row("S-11", $"{c.Name,-62} injectedDevice={dev,3} delivered={(got == null ? "NOT DELIVERED" : "yes dev=" + got.Device + " sameManagedObject=" + ReferenceEquals(got, ev))} " +
                             $"typeFirstRule={(got == null ? "-" : Classify(got))} | naiveDeviceFirstRule={(got == null ? "-" : NaiveDeviceFirst(got))}");
            // key releases so polled state does not leak between rows
            if (ev is InputEventKey k) { k.Pressed = false; Deliver(rx, k); }
        }

        // ---- S-12 emulated mouse from touch: which Device id does the engine assign?
        Util.Log("=== S-12 touch -> emulated mouse (input_devices/pointing/emulate_mouse_from_touch): is DeviceIdEmulation actually produced?");
        Trace.Clear();
        Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = true, Position = new Vector2(2000, 2000) });
        Input.FlushBufferedEvents();
        foreach (var x in Trace.Events.Where(x => x.Phase == "input")) { Util.Row("S-12", $"delivered {Util.Desc(x.Ev)} | typeFirstRule={Classify(x.Ev)}"); }
        Util.Row("S-12", $"ProjectSettings emulate_mouse_from_touch={ProjectSettings.GetSetting("input_devices/pointing/emulate_mouse_from_touch")} emulate_touch_from_mouse={ProjectSettings.GetSetting("input_devices/pointing/emulate_touch_from_mouse")}");
        Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = false, Position = new Vector2(2000, 2000) });
        Input.FlushBufferedEvents();

        // ---- S-13 injected pad on device 16/32: polled state
        Util.Log("=== S-13 does the engine accept pad indices 16 / 32 (Steam virtual-pad collision risk)?");
        foreach (int d in new[] { 0, 1, 16, 32 })
        {
            Deliver(rx, new InputEventJoypadButton { Device = d, ButtonIndex = JoyButton.A, Pressed = true });
            Util.Row("S-13", $"after injected JoyButton A pressed on device {d}: Input.IsJoyButtonPressed({d}, A)={Input.IsJoyButtonPressed(d, JoyButton.A)} delivered to _Input with Device={(Trace.Events.FirstOrDefault(x => x.Phase == "input").Ev?.Device.ToString() ?? "NOT DELIVERED")}");
            Deliver(rx, new InputEventJoypadButton { Device = d, ButtonIndex = JoyButton.A, Pressed = false });
        }

        // ---- S-14 joypad motion: one axis per event, raw (no deadzone), polled state after injection
        Util.Log("=== S-14 joypad motion: one axis per event, raw value, GetJoyAxis after an injected JoypadMotion");
        foreach (int d in new[] { 0, 16 })
        {
            Util.Row("S-14", $"device {d} before: GetJoyAxis LeftX={Input.GetJoyAxis(d, JoyAxis.LeftX)} TriggerLeft={Input.GetJoyAxis(d, JoyAxis.TriggerLeft)}");
            var got = Deliver(rx, new InputEventJoypadMotion { Device = d, Axis = JoyAxis.LeftX, AxisValue = 0.03f });
            Util.Row("S-14", $"device {d} injected LeftX=0.03 (below any default deadzone 0.2/0.5): delivered {(got == null ? "NOT DELIVERED" : Util.Desc(got))}; _Input calls this injection={Trace.Events.Count(x => x.Phase == "input")}; polled GetJoyAxis LeftX={Input.GetJoyAxis(d, JoyAxis.LeftX)} (injected event updates polled state? {(Input.GetJoyAxis(d, JoyAxis.LeftX) != 0f)})");
            got = Deliver(rx, new InputEventJoypadMotion { Device = d, Axis = JoyAxis.TriggerLeft, AxisValue = 0.75f });
            Util.Row("S-14", $"device {d} injected TriggerLeft=0.75: delivered {(got == null ? "NOT DELIVERED" : Util.Desc(got))} (triggers are axes: {got is InputEventJoypadMotion}); polled GetJoyAxis TriggerLeft={Input.GetJoyAxis(d, JoyAxis.TriggerLeft)}");
            Deliver(rx, new InputEventJoypadMotion { Device = d, Axis = JoyAxis.LeftX, AxisValue = 0.03f });
            Util.Row("S-14", $"device {d} SAME value injected twice (synthetic: no engine change-filtering?) -> _Input calls for the repeat injection = {Trace.Events.Count(x => x.Phase == "input")}");
            Deliver(rx, new InputEventJoypadMotion { Device = d, Axis = JoyAxis.LeftX, AxisValue = 0f });
            Deliver(rx, new InputEventJoypadMotion { Device = d, Axis = JoyAxis.TriggerLeft, AxisValue = 0f });
        }
        await Util.Frames(tree, 1);
        Util.Row("S-14", $"after one more frame: device 0 GetJoyAxis LeftX={Input.GetJoyAxis(0, JoyAxis.LeftX)}");

        // ---- S-15 joy info keys / connected pads
        Util.Log("=== S-15 Input.GetJoyInfo keys and connected pads (headless, no pad attached)");
        var info = Input.GetJoyInfo(0);
        Util.Row("S-15", $"GetConnectedJoypads.Count={Input.GetConnectedJoypads().Count} GetJoyInfo(0).Count={info.Count} keys=[{string.Join(", ", info.Keys)}] GetJoyName(0)='{Input.GetJoyName(0)}' GetJoyGuid(0)='{Input.GetJoyGuid(0)}' IsJoyKnown(0)={Input.IsJoyKnown(0)}");
        Util.Row("S-15", "GetJoyInfo keys for a REAL pad (xinput_index / vendor_id / product_id / raw_name ...): NOT RUN - needs a real pad (MANUAL M-1)");

        // ---- S-16 JoyConnectionChanged drivability
        Util.Log("=== S-16 can Input.JoyConnectionChanged be driven headless?");
        var inputMethods = ClassDB.ClassGetMethodList("Input", true).Select(d => (string)d["name"]).Where(n => n.Contains("joy", StringComparison.Ordinal) && (n.Contains("connect", StringComparison.Ordinal) || n.Contains("add_") || n.Contains("remove_"))).ToList();
        Util.Row("S-16", $"Input script-visible methods with joy+connect/add/remove: [{string.Join(", ", inputMethods)}] (a native joy_connection_changed() trigger is not script-callable)");
        int fired = 0; long lastIdx = -1; bool lastConn = false;
        Input.Singleton.JoyConnectionChanged += (device, connected) => { fired++; lastIdx = device; lastConn = connected; };
        Input.Singleton.EmitSignal(Input.SignalName.JoyConnectionChanged, 3, true);
        Util.Row("S-16", $"manual EmitSignal(JoyConnectionChanged, 3, true): handler fired={fired} idx={lastIdx} connected={lastConn} (proves the C# signal subscription/handler signature only; engine-originated hot-plug: NOT RUN - needs a real pad; hot-plug index reuse: NOT RUN - needs a real pad)");

        // ---- S-17 polled vs event with no flush
        Util.Log("=== S-17 polled state before the flush (ParseInputEvent buffered one frame)");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F8, PhysicalKeycode = Key.F8, Pressed = true });
        bool before = Input.IsKeyPressed(Key.F8);
        Input.FlushBufferedEvents();
        bool after = Input.IsKeyPressed(Key.F8);
        Util.Row("S-17", $"IsKeyPressed(F8) right after ParseInputEvent (no flush)={before}; after FlushBufferedEvents={after}");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F8, PhysicalKeycode = Key.F8, Pressed = false });
        Input.FlushBufferedEvents();

        Trace.Print = true;
        rx.QueueFree();
        await Util.Frames(tree, 1);
    }
}
