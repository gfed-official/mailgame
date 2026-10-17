using Godot;

namespace SpikeOqIc9Input;

/// <summary>AC-6: bytes/event table (S-20), ScreenRelative vs Relative (S-21), Echo (S-22), label API (S-23), accessibility (S-24). SYNTHETIC events, headless.</summary>
public static class AllocProbe
{
    private const int Warm = 300;
    private const int N = 10_000;

    private static Node _injector = null!;
    private static AllocRx _rx = null!;

    private static void GdRun(string kind, int n, int flushEvery) => _injector.Call("run", kind, n, flushEvery);

    private static (double perInjected, double perDelivered, long delivered, int gc0) Measure(Action<int> body, int n)
    {
        // Overhead of the harness call itself (variant marshalling etc.) with n=0.
        long o0 = GC.GetAllocatedBytesForCurrentThread();
        body(0);
        long overhead = GC.GetAllocatedBytesForCurrentThread() - o0;
        _rx.Delivered = 0;
        int g0 = GC.CollectionCount(0);
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        body(n);
        long b1 = GC.GetAllocatedBytesForCurrentThread();
        long bytes = b1 - b0 - overhead;
        long del = _rx.Delivered;
        return ((double)bytes / n, del > 0 ? (double)bytes / del : double.NaN, del, GC.CollectionCount(0) - g0);
    }

    private static void Row(string id, string name, Action<int> body, bool warm = true)
    {
        if (warm) { body(Warm); }
        var r = Measure(body, N);
        Util.Row(id, $"{name,-70} injected={N} delivered_to__Input={r.delivered,6} bytes/injected-event={r.perInjected,8:0.0} bytes/delivered-event={r.perDelivered,8:0.0} gen0_GCs_during_run={r.gc0}");
    }

    /// <summary>S-26: 10 000 natively-created key events delivered to a C# _Input, then quit WITHOUT a forced GC / without freeing the receiver. Observe the shutdown report and the process exit status (see logs/exitleak.engine.txt and run_probe.sh exit line).</summary>
    public static async Task RunExitLeak(Node host)
    {
        SceneTree tree = host.GetTree();
        Trace.Print = false;
        _rx = new AllocRx { Name = "AllocRx" };
        tree.Root.AddChild(_rx);
        _injector = new Node();
        _injector.SetScript(GD.Load<GDScript>("res://Injector.gd"));
        tree.Root.AddChild(_injector);
        await Util.Frames(tree, 3);
        GdRun("key", N, 1);
        Util.Row("S-26", $"delivered={_rx.Delivered} native-created InputEventKey events; quitting now with no GC.Collect and the receiver still in the tree. ObjectCount={Performance.GetMonitor(Performance.Monitor.ObjectCount)}");
    }

    public static async Task Run(Node host)
    {
        SceneTree tree = host.GetTree();
        Trace.Print = false;
        _rx = new AllocRx { Name = "AllocRx" };
        tree.Root.AddChild(_rx);
        _injector = new Node();
        _injector.SetScript(GD.Load<GDScript>("res://Injector.gd"));
        tree.Root.AddChild(_injector);
        await Util.Frames(tree, 3);
        bool acc0 = Input.UseAccumulatedInput;
        Util.Log($"# GC mode: server={System.Runtime.GCSettings.IsServerGC} latency={System.Runtime.GCSettings.LatencyMode}; Release? Debug build assembly (dotnet build default Debug); UseAccumulatedInput(default)={acc0}; warm-up={Warm}, measured={N}");
        Util.Log("# Method: GC.GetAllocatedBytesForCurrentThread() delta around N events, minus the delta of one n=0 call of the same harness (call marshalling overhead).");

        Util.Log("=== S-20a events created NATIVELY (GDScript Injector.gd, no managed wrapper exists before delivery) - the situation of OS-generated events");
        Input.UseAccumulatedInput = true;
        Row("S-20a", "InputEventKey, flush after every event", n => GdRun("key", n, 1));
        Row("S-20a", "InputEventMouseButton, flush after every event", n => GdRun("button", n, 1));
        Row("S-20a", "InputEventJoypadMotion, flush after every event", n => GdRun("joymotion", n, 1));
        Row("S-20a", "InputEventMouseMotion, UseAccumulatedInput=TRUE, 16 per flush ('frame')", n => GdRun("motion", n, 16));
        Input.UseAccumulatedInput = false;
        Row("S-20a", "InputEventMouseMotion, UseAccumulatedInput=FALSE, 16 per flush ('frame')", n => GdRun("motion", n, 16));
        Input.UseAccumulatedInput = true;
        Row("S-20a", "InputEventMouseMotion, UseAccumulatedInput=TRUE, 1 per flush", n => GdRun("motion", n, 1));

        Util.Log("=== S-20b events created in C# (fresh object per event; bytes INCLUDE the C# construction in the harness, so an upper bound)");
        Row("S-20b", "InputEventKey fresh object/event, flush each", n => { for (int i = 0; i < n; i++) { Input.ParseInputEvent(new InputEventKey { Keycode = Key.F5, PhysicalKeycode = Key.F5, Pressed = (i & 1) == 0 }); Input.FlushBufferedEvents(); } });
        Row("S-20b", "InputEventMouseButton fresh object/event, flush each", n => { for (int i = 0; i < n; i++) { Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = (i & 1) == 0, Position = new Vector2(2000, 2000) }); Input.FlushBufferedEvents(); } });
        Row("S-20b", "InputEventJoypadMotion fresh object/event, flush each", n => { for (int i = 0; i < n; i++) { Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = (i % 100) / 100f }); Input.FlushBufferedEvents(); } });
        Input.UseAccumulatedInput = false;
        Row("S-20b", "InputEventMouseMotion fresh object/event, Accumulated=FALSE, 16/flush", n => { for (int i = 0; i < n; i++) { Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(100, 100), Relative = new Vector2(1, 0), ScreenRelative = new Vector2(1, 0) }); if ((i & 15) == 15) { Input.FlushBufferedEvents(); } } Input.FlushBufferedEvents(); });
        Input.UseAccumulatedInput = true;
        Row("S-20b", "InputEventMouseMotion fresh object/event, Accumulated=TRUE, 16/flush", n => { for (int i = 0; i < n; i++) { Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(100, 100), Relative = new Vector2(1, 0), ScreenRelative = new Vector2(1, 0) }); if ((i & 15) == 15) { Input.FlushBufferedEvents(); } } Input.FlushBufferedEvents(); });

        Util.Log("=== S-20c one pre-built C# event instance re-injected (wrapper already exists; isolates the engine->_Input dispatch cost)");
        var keyEv = new InputEventKey { Keycode = Key.F5, PhysicalKeycode = Key.F5, Pressed = true };
        Row("S-20c", "InputEventKey reused instance, flush each", n => { for (int i = 0; i < n; i++) { Input.ParseInputEvent(keyEv); Input.FlushBufferedEvents(); } });
        var joyEv = new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0.5f };
        Row("S-20c", "InputEventJoypadMotion reused instance, flush each", n => { for (int i = 0; i < n; i++) { Input.ParseInputEvent(joyEv); Input.FlushBufferedEvents(); } });
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = 0f }); Input.FlushBufferedEvents();

        // ---- S-21 ScreenRelative vs Relative under a scaled stretch
        Util.Log("=== S-21 InputEventMouseMotion.ScreenRelative vs Relative under a scaled stretch viewport (Step 5.5 note 2)");
        Window root = tree.Root;
        Trace.Clear();
        var probeRx = new RecvNode { Name = "RxS21" };
        root.AddChild(probeRx);
        await Util.Frames(tree, 1);
        void Motion(string tag)
        {
            Trace.Clear();
            Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(100, 100), GlobalPosition = new Vector2(100, 100), Relative = new Vector2(36, 18), ScreenRelative = new Vector2(36, 18) });
            Input.FlushBufferedEvents();
            var d = Trace.Events.FirstOrDefault(x => x.Phase == "input").Ev as InputEventMouseMotion;
            Util.Row("S-21", $"{tag}: window size={root.Size} ContentScaleMode={root.ContentScaleMode} ContentScaleSize={root.ContentScaleSize} scale factor(ContentScaleFactor)={root.ContentScaleFactor} -> injected Relative=(36,18) ScreenRelative=(36,18); delivered " +
                              (d == null ? "NOT DELIVERED" : $"Relative={d.Relative} ScreenRelative={d.ScreenRelative}"));
        }
        Motion("no stretch (Disabled)");
        root.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        root.ContentScaleSize = new Vector2I(320, 180);
        root.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        await Util.Frames(tree, 2);
        Motion("stretch CanvasItems 320x180 into window");
        root.ContentScaleMode = Window.ContentScaleModeEnum.Viewport;
        await Util.Frames(tree, 2);
        Motion("stretch Viewport 320x180 into window");
        root.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        root.ContentScaleSize = new Vector2I(0, 0);
        await Util.Frames(tree, 2);
        probeRx.QueueFree();

        // ---- S-22 echo
        Util.Log("=== S-22 InputEventKey.Echo for an injected echo event (ADR item 11)");
        var rxE = new RecvNode { Name = "RxS22" };
        root.AddChild(rxE);
        await Util.Frames(tree, 1);
        foreach (bool echo in new[] { false, true })
        {
            Trace.Clear();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F9, PhysicalKeycode = Key.F9, Pressed = true, Echo = echo });
            Input.FlushBufferedEvents();
            var d = Trace.Events.FirstOrDefault(x => x.Phase == "input").Ev as InputEventKey;
            Util.Row("S-22", $"injected Echo={echo}: delivered Echo={d?.Echo} IsEcho()={d?.IsEcho()} IsPressed()={d?.IsPressed()}; polled IsKeyPressed(F9)={Input.IsKeyPressed(Key.F9)}");
        }
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F9, PhysicalKeycode = Key.F9, Pressed = false });
        Input.FlushBufferedEvents();
        Util.Row("S-22", "OS key-repeat actually setting Echo (a held physical key): NOT RUN - needs a real keyboard (MANUAL M-4); the injected flag round-trips but says nothing about OS behaviour");
        rxE.QueueFree();

        // ---- S-23 keyboard layout labels
        Util.Log("=== S-23 DisplayServer.KeyboardGetLabelFromPhysical (headless)");
        string D(Func<object> f) { try { return Convert.ToString(f()) ?? "null"; } catch (Exception ex) { return "EXC " + ex.GetType().Name; } }
        Util.Row("S-23", $"KeyboardGetLabelFromPhysical(Key.A)={D(() => DisplayServer.KeyboardGetLabelFromPhysical(Key.A))} (Key.Q)={D(() => DisplayServer.KeyboardGetLabelFromPhysical(Key.Q))} (Key.W)={D(() => DisplayServer.KeyboardGetLabelFromPhysical(Key.W))}");
        Util.Row("S-23", $"KeyboardGetKeycodeFromPhysical(Key.A)={D(() => DisplayServer.KeyboardGetKeycodeFromPhysical(Key.A))} KeyboardGetLayoutCount={D(() => DisplayServer.KeyboardGetLayoutCount())} KeyboardGetCurrentLayout={D(() => DisplayServer.KeyboardGetCurrentLayout())} KeyboardGetLayoutName(0)='{D(() => DisplayServer.KeyboardGetLayoutName(0))}' KeyboardGetLayoutLanguage(0)='{D(() => DisplayServer.KeyboardGetLayoutLanguage(0))}'");
        Util.Row("S-23", $"OS.GetKeycodeString(Key.A)='{OS.GetKeycodeString(Key.A)}' OS.GetKeycodeString(KeyboardGetLabelFromPhysical(A))='{OS.GetKeycodeString(DisplayServer.KeyboardGetLabelFromPhysical(Key.A))}'");
        Util.Row("S-23", "AZERTY check (physical Key.A label must read 'Q' on... i.e. Key.Q -> 'A', Key.W -> 'Z', Key.A -> 'Q' on a French layout): NOT RUN - MANUAL M-5 (needs a display session with an AZERTY layout active; H-EC-11)");

        // ---- S-24 accessibility
        Util.Log("=== S-24 AccessibilityServer / screen reader (not an M-1a requirement; recorded for completeness)");
        Util.Row("S-24", $"ClassDB.ClassExists(AccessibilityServer)={ClassDB.ClassExists("AccessibilityServer")} DisplayServer.HasFeature(AccessibilityScreenReader)={D(() => DisplayServer.HasFeature(DisplayServer.Feature.AccessibilityScreenReader))} DisplayServer.AccessibilityScreenReaderActive()={D(() => DisplayServer.AccessibilityScreenReaderActive())}");
        Util.Row("S-24", "Screen-reader path (NVDA / Orca) end to end: NOT RUN - not an M-1a requirement; needs a screen reader and a display session");

        // ---- S-25 wrapper lifetime for natively-created events (leak mechanism check)
        Util.Log("=== S-25 managed-wrapper lifetime of natively-created events: Performance ObjectCount before / after 10 000 events / after GC.Collect+WaitForPendingFinalizers");
        double Obj() => Performance.GetMonitor(Performance.Monitor.ObjectCount);
        foreach (var kind in new[] { "key", "button", "joymotion", "motion" })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Util.Frames(tree, 2);
            double o0 = Obj();
            GdRun(kind, N, 1);
            double o1 = Obj();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Util.Frames(tree, 2);
            double o2 = Obj();
            Util.Row("S-25", $"kind={kind,-9} ObjectCount baseline={o0} after 10000 events={o1} (+{o1 - o0}) after forced GC + finalizers + 2 frames={o2} (+{o2 - o0} vs baseline)");
        }

        Util.Log("=== S-25b same check with events created in C# and dropped (no native-only creation)");
        foreach (var kind in new[] { "key", "button" })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Util.Frames(tree, 2);
            double o0 = Obj();
            for (int i = 0; i < N; i++)
            {
                InputEvent e = kind == "key" ? new InputEventKey { Keycode = Key.F5, PhysicalKeycode = Key.F5, Pressed = (i & 1) == 0 }
                                             : new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = (i & 1) == 0, Position = new Vector2(2000, 2000) };
                Input.ParseInputEvent(e);
                Input.FlushBufferedEvents();
            }
            double o1 = Obj();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Util.Frames(tree, 2);
            double o2 = Obj();
            Util.Row("S-25b", $"C#-created {kind,-6} ObjectCount baseline={o0} after 10000={o1} (+{o1 - o0}) after forced GC + finalizers + 2 frames={o2} (+{o2 - o0} vs baseline)");
        }

        Util.Log("=== S-25c mitigation candidate: AllocRx._Input calls @event.Dispose() after reading the fields (natively-created key / button events)");
        _rx.DisposeAfter = true;
        foreach (var kind in new[] { "key", "button" })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Util.Frames(tree, 2);
            double o0 = Obj();
            GdRun(kind, N, 1);
            double o1 = Obj();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Util.Frames(tree, 2);
            double o2 = Obj();
            Util.Row("S-25c", $"Dispose-in-_Input kind={kind,-6} ObjectCount baseline={o0} after 10000={o1} (+{o1 - o0}) after forced GC={o2} (+{o2 - o0} vs baseline)");
        }
        _rx.DisposeAfter = false;

        Util.Log("=== S-25d engine-created (C++) events: ScreenTouch injected from GDScript makes the ENGINE create the emulated InputEventMouseButton (emulate_mouse_from_touch)");
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Util.Frames(tree, 2);
            _rx.Delivered = 0;
            double o0 = Obj();
            GdRun("touch", N, 1);
            double o1 = Obj();
            long del = _rx.Delivered;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Util.Frames(tree, 2);
            double o2 = Obj();
            Util.Row("S-25d", $"touch->emulated mouse: _Input calls={del} ObjectCount baseline={o0} after 10000 touches={o1} (+{o1 - o0}) after forced GC={o2} (+{o2 - o0} vs baseline)");
        }

        Input.UseAccumulatedInput = acc0;
        Trace.Print = true;
        // Remove the receivers and collect before quit so the shutdown "leaked unsafe reference" report (if any) is attributable.
        _rx.QueueFree();
        _injector.QueueFree();
        await Util.Frames(tree, 2);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Util.Row("S-25", "forced GC performed before quit (see logs/alloc.engine.txt for the shutdown 'Leaked unsafe reference' / FATAL lines, if any)");
    }
}
