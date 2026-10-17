using Godot;

namespace SpikeOqIc9Input;

/// <summary>AC-2: B1-B6, reproduction of the 2026-10-16 headless findings on the Mono binary.</summary>
public static class BaselineProbe
{
    private const string JoyIgnore = "input_devices/joypads/ignore_joypad_on_unfocused_application";

    public static void Run()
    {
        // B1: InputEvent has no timestamp property or method (own class and every inheriter), via ClassDB and via C# reflection.
        var classes = new List<string> { "InputEvent" };
        foreach (string c in ClassDB.GetInheritersFromClass("InputEvent")) { classes.Add(c); }
        var hits = new List<string>();
        foreach (string c in classes)
        {
            foreach (var m in ClassDB.ClassGetMethodList(c, true))
            {
                string n = (string)m["name"];
                if (n.Contains("time", StringComparison.OrdinalIgnoreCase) || n.Contains("stamp", StringComparison.OrdinalIgnoreCase)) { hits.Add(c + "." + n + "()"); }
            }
            foreach (var p in ClassDB.ClassGetPropertyList(c, true))
            {
                string n = (string)p["name"];
                if (n.Contains("time", StringComparison.OrdinalIgnoreCase) || n.Contains("stamp", StringComparison.OrdinalIgnoreCase)) { hits.Add(c + "." + n); }
            }
        }
        var refl = new List<string>();
        foreach (var t in typeof(InputEvent).Assembly.GetTypes())
        {
            if (!typeof(InputEvent).IsAssignableFrom(t)) { continue; }
            foreach (var mi in t.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (mi.Name.Contains("time", StringComparison.OrdinalIgnoreCase) || mi.Name.Contains("stamp", StringComparison.OrdinalIgnoreCase)) { refl.Add(t.Name + "." + mi.Name); }
            }
        }
        Util.Row("B1", $"InputEvent + {classes.Count - 1} inheriters scanned. ClassDB methods/properties containing 'time'/'stamp': [{string.Join(", ", hits)}]; " +
                       $"ClassHasMethod(InputEvent,get_timestamp)={ClassDB.ClassHasMethod("InputEvent", "get_timestamp")}; C# reflection hits: [{string.Join(", ", refl)}]");
        Util.Row("B1", "InputEvent own methods (no inheritance): " + string.Join(", ", ClassDB.ClassGetMethodList("InputEvent", true).Select(d => (string)d["name"])));
        Util.Row("B1", "InputEvent own properties (no inheritance): " + string.Join(", ", ClassDB.ClassGetPropertyList("InputEvent", true).Select(d => (string)d["name"])));

        // B2: DEVICE_ID_* C# names (compile-checked) and default Device of new events.
        Util.Row("B2", $"InputEvent.DeviceIdKeyboard={InputEvent.DeviceIdKeyboard} DeviceIdMouse={InputEvent.DeviceIdMouse} DeviceIdEmulation={InputEvent.DeviceIdEmulation}");
        Util.Row("B2", $"default Device: InputEventAction={new InputEventAction().Device} InputEventKey={new InputEventKey().Device} " +
                       $"InputEventMouseButton={new InputEventMouseButton().Device} InputEventJoypadButton={new InputEventJoypadButton().Device} " +
                       $"InputEventMouseMotion={new InputEventMouseMotion().Device} InputEventJoypadMotion={new InputEventJoypadMotion().Device} " +
                       $"InputEventScreenTouch={new InputEventScreenTouch().Device}");

        // B3: ignore_joypad_on_unfocused_application default.
        bool has = ProjectSettings.HasSetting(JoyIgnore);
        Util.Row("B3", $"HasSetting={has} GetSetting={(has ? ProjectSettings.GetSetting(JoyIgnore) : "n/a")} " +
                       $"PropertyCanRevert={ProjectSettings.Singleton.Call("property_can_revert", JoyIgnore)} PropertyGetRevert={ProjectSettings.Singleton.Call("property_get_revert", JoyIgnore)} " +
                       $"Input.IgnoreJoypadOnUnfocusedApplication(runtime property)={Input.IgnoreJoypadOnUnfocusedApplication}");

        // B4
        Util.Row("B4", $"Input.UseAccumulatedInput={Input.UseAccumulatedInput}; project setting input_devices/pointing/... " +
                       $"HasSetting(input_devices/buffering/agile_event_flushing)={ProjectSettings.HasSetting("input_devices/buffering/agile_event_flushing")} " +
                       $"value={(ProjectSettings.HasSetting("input_devices/buffering/agile_event_flushing") ? ProjectSettings.GetSetting("input_devices/buffering/agile_event_flushing") : "n/a")}");

        // B5: events on ui_* actions.
        int uiCount = 0, joyOnUi = 0, joyAny = 0;
        var perAction = new List<string>();
        foreach (var a in InputMap.GetActions())
        {
            string name = a.ToString();
            int jb = 0, jm = 0, k = 0, mb = 0, other = 0;
            foreach (var ev in InputMap.ActionGetEvents(a))
            {
                if (ev is InputEventJoypadButton jbe) { Util.Row("B5", $"  joypad event on {a}: JoyButton index={jbe.ButtonIndex} ({(int)jbe.ButtonIndex}) device={jbe.Device}"); }
                if (ev is InputEventJoypadMotion jme) { Util.Row("B5", $"  joypad event on {a}: JoyMotion axis={jme.Axis} value={jme.AxisValue} device={jme.Device}"); }
                switch (ev) { case InputEventJoypadButton: jb++; break; case InputEventJoypadMotion: jm++; break; case InputEventKey: k++; break; case InputEventMouseButton: mb++; break; default: other++; break; }
            }
            joyAny += jb + jm;
            if (name.StartsWith("ui_", StringComparison.Ordinal))
            {
                uiCount++;
                joyOnUi += jb + jm;
                perAction.Add($"{name}[key={k} mouse={mb} joyBtn={jb} joyMotion={jm} other={other}]");
            }
        }
        Util.Row("B5", $"InputMap actions={InputMap.GetActions().Count} ui_* actions={uiCount} joypad events on ui_* actions={joyOnUi} (joypad events on ALL actions={joyAny})");
        foreach (string s in perAction) { Util.Row("B5", "  " + s); }
        Util.Row("B5", "  ui_accept HasAction=" + InputMap.HasAction("ui_accept") + " ui_accessibility_drag_and_drop HasAction=" + InputMap.HasAction("ui_accessibility_drag_and_drop"));

        // B6
        Util.Row("B6", $"Input.SignalName.JoyConnectionChanged='{Input.SignalName.JoyConnectionChanged}' Input.Singleton.HasSignal={Input.Singleton.HasSignal(Input.SignalName.JoyConnectionChanged)} " +
                       $"ClassDB.ClassHasSignal(Input,joy_connection_changed)={ClassDB.ClassHasSignal("Input", "joy_connection_changed")}");
        Util.Row("B6", $"Node.NotificationApplicationFocusOut={Node.NotificationApplicationFocusOut} NotificationApplicationFocusIn={Node.NotificationApplicationFocusIn} " +
                       $"NotificationWMWindowFocusOut={Node.NotificationWMWindowFocusOut} NotificationWMWindowFocusIn={Node.NotificationWMWindowFocusIn} " +
                       $"NotificationApplicationResumed={Node.NotificationApplicationResumed} NotificationApplicationPaused={Node.NotificationApplicationPaused}");
    }

    /// <summary>Run from a copy of the project whose project.godot sets the key true (run_b3_set.sh).</summary>
    public static void RunB3()
    {
        Util.Row("B3-set", $"GetSetting={ProjectSettings.GetSetting(JoyIgnore)} Input.IgnoreJoypadOnUnfocusedApplication(runtime property)={Input.IgnoreJoypadOnUnfocusedApplication} " +
                           $"PropertyCanRevert={ProjectSettings.Singleton.Call("property_can_revert", JoyIgnore)} PropertyGetRevert={ProjectSettings.Singleton.Call("property_get_revert", JoyIgnore)}");
    }
}
