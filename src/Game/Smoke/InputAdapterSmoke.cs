using Godot;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Game.Input;
using PerformativeMail.Sim.Players;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story input-controls 026: the real <see cref="InputAdapter"/> over a real <see cref="InputRouter"/> (built-in M-1a map, a
/// manual ms clock), fed synthetic events through <c>Input.ParseInputEvent</c> + <c>FlushBufferedEvents</c> (delivery is
/// synchronous inside the flush, spike S-01f). Rows: root-last placement re-asserted after later root children (D-1), inert
/// until <c>Initialize</c>; type-first classification incl. pads 16 / 32 and synthetic actions; conversion coverage, unmapped
/// counting, arrival stamps from the clock; handled state against a focused <c>Button</c> and a stand-in overlay, releases
/// reaching the router before the GUI; H-G4-1 (E held with 30 echoes in 1 s, one press resolution); mouse motion coalesced
/// with accumulation on and off; joypad axes cached and re-seeded. Router output is read from its <see cref="UiEventLog"/>:
/// the latch types live in the Sim namespace that <c>check-sim-access.sh</c> bans here, and the router's edge for each
/// resolved press is covered by the unit tests (<c>PressMachineTests</c>). Debug smoke code only; writes nothing to disk.
/// </summary>
[SmokeScenario("input-adapter")]
public sealed class InputAdapterSmoke : SmokeScenario
{
    private readonly ManualClock _clock = new() { NowMs = 1000 };
    private readonly UiEventLog _log = new();
    private InputRouter _router = null!;
    private InputAdapter _adapter = null!;
    private InputOverlayProbe _overlay = null!;
    private Control _gui = null!;
    private Button _button = null!;
    private int _buttonDowns;
    private int _buttonPresses;
    private int _step;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        var interaction = new IdleInteraction();
        _router = new InputRouter(BuiltInActionMap.CreateM1a(), InputSettings.Defaults, _clock, interaction,
            new PresentedPromptLatch(interaction), new CarrierStatusSourceStub(), new NeverRefusingAim(), new ZeroTicks(), _log);

        Window root = ctx.Tree.Root;
        _adapter = InputAdapter.AttachToRoot(root);
        _gui = new Control { Name = "SmokeGui" };
        _button = new Button { Name = "Focused", Text = "OK", Size = new Vector2(100, 40) };
        _button.ButtonDown += () => _buttonDowns++;
        _button.Pressed += () => _buttonPresses++;
        _gui.AddChild(_button);
        _overlay = new InputOverlayProbe { Name = "SmokeOverlay" };
        root.CallDeferred(Node.MethodName.AddChild, _gui);
        root.CallDeferred(Node.MethodName.AddChild, _overlay);

        CheckClassification(ctx);
        CheckConversion(ctx);
    }

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        switch (_step++)
        {
            case 0:
                // The overlay and GUI entered the root after the adapter; the re-assert runs deferred, so wait one more frame.
                return SmokeStatus.Running;
            case 1:
                CheckPlacementAndInert(ctx);
                _adapter.Initialize(new AdapterDeps { Router = _router, Clock = _clock, Settings = InputSettings.Defaults });
                _button.GrabFocus();
                Expect(ctx, "Button has focus", _button.HasFocus(), true);
                CheckStampsAndHandled(ctx);
                CheckEcho(ctx);
                CheckMouseLook(ctx);
                CheckAxes(ctx);
                CheckSynthetic(ctx);
                return SmokeStatus.Done;
            default:
                return SmokeStatus.Done;
        }
    }

    private static void Inject(InputEvent e)
    {
        Godot.Input.ParseInputEvent(e);
        Godot.Input.FlushBufferedEvents();
    }

    private static InputEventKey Key(Key key, bool pressed, bool echo = false) =>
        new() { PhysicalKeycode = key, Keycode = key, Pressed = pressed, Echo = echo };

    private static void CheckClassification(SmokeContext ctx)
    {
        Row(ctx, "key, default device", new InputEventKey(), true, DeviceClass.Keyboard, 0);
        Row(ctx, "key with device 0 (never pad 0)", new InputEventKey { Device = 0 }, false, DeviceClass.Keyboard, 0);
        Row(ctx, "mouse button, default device", new InputEventMouseButton(), true, DeviceClass.Mouse, 0);
        Row(ctx, "mouse button, emulation id", new InputEventMouseButton { Device = (int)InputEvent.DeviceIdEmulation }, true, DeviceClass.Mouse, 0);
        Row(ctx, "mouse motion, emulation id", new InputEventMouseMotion { Device = (int)InputEvent.DeviceIdEmulation }, true, DeviceClass.Mouse, 0);
        Row(ctx, "joypad button, device 0", new InputEventJoypadButton { Device = 0 }, true, DeviceClass.Pad, 0);
        Row(ctx, "joypad button, device 16 (not keyboard)", new InputEventJoypadButton { Device = 16 }, true, DeviceClass.Pad, 16);
        Row(ctx, "joypad motion, device 32 (not mouse)", new InputEventJoypadMotion { Device = 32 }, true, DeviceClass.Pad, 32);
        Row(ctx, "InputEventAction (device 0)", new InputEventAction { Action = "smoke_action" }, true, DeviceClass.Synthetic, 0);
    }

    private static void Row(SmokeContext ctx, string what, InputEvent e, bool ok, DeviceClass device, byte pad)
    {
        bool got = InputAdapter.TryClassify(e, out DeviceClass d, out byte p);
        Expect(ctx, $"classify {what} (device {e.Device})", $"{got}/{d}/{p}", $"{ok}/{device}/{pad}");
    }

    private static void CheckConversion(SmokeContext ctx)
    {
        var expected = new HashSet<PhysicalInput>(Enum.GetValues<PhysicalInput>());
        expected.ExceptWith([PhysicalInput.None, PhysicalInput.MouseMotion]);
        HashSet<PhysicalInput> mapped = PhysicalInputMap.AllMapped();
        Expect(ctx, "PhysicalInputMap covers every M-1a PhysicalInput (missing, extra)",
            $"{string.Join(",", expected.Except(mapped))};{string.Join(",", mapped.Except(expected))}", ";");

        InputAdapter.Conversion c = InputAdapter.TryConvert(new InputEventKey { PhysicalKeycode = Godot.Key.W, Keycode = Godot.Key.Z, Pressed = true, Echo = true }, 7, out RawInputRecord r);
        Expect(ctx, "key: physical W with label Z (AZERTY) -> KeyW, echo kept, stamp", $"{c}/{r.Kind}/{r.Input}/{r.Pressed}/{r.Echo}/{r.ArrivalMs}", "Record/Key/KeyW/True/True/7");
        c = InputAdapter.TryConvert(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true }, 8, out r);
        Expect(ctx, "wheel down press -> one Wheel notch", $"{c}/{r.Kind}/{r.Input}/{r.Value}", "Record/Wheel/WheelDown/1");
        c = InputAdapter.TryConvert(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = false }, 8, out _);
        Expect(ctx, "wheel down release -> ignored", c, InputAdapter.Conversion.Ignored);
        c = InputAdapter.TryConvert(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true }, 9, out r);
        Expect(ctx, "right mouse button", $"{c}/{r.Kind}/{r.Device}/{r.Input}", "Record/MouseButton/Mouse/MouseRight");
        c = InputAdapter.TryConvert(new InputEventJoypadButton { Device = 1, ButtonIndex = JoyButton.Start, Pressed = true }, 10, out r);
        Expect(ctx, "joypad 1 Start -> PadMenu", $"{c}/{r.Kind}/{r.PadIndex}/{r.Input}", "Record/PadButton/1/PadMenu");
        c = InputAdapter.TryConvert(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.TriggerRight, AxisValue = 1f }, 11, out r);
        Expect(ctx, "RT at 1.0 -> PadRightTrigger Q15", $"{c}/{r.Kind}/{r.Input}/{r.Value}", "Record/PadAxis/PadRightTrigger/32767");
        c = InputAdapter.TryConvert(new InputEventKey { PhysicalKeycode = Godot.Key.F12, Pressed = true }, 12, out _);
        Expect(ctx, "F12 (no M-1a input) -> unmapped", c, InputAdapter.Conversion.Unmapped);
    }

    private void CheckPlacementAndInert(SmokeContext ctx)
    {
        Window root = ctx.Tree.Root;
        Expect(ctx, "adapter parent is the root window", _adapter.GetParent() == root, true);
        Expect(ctx, "adapter is the last root child after the GUI and overlay entered (D-1 re-assert)",
            $"{_adapter.GetIndex()}/{_overlay.GetIndex() < _adapter.GetIndex() && _gui.GetIndex() < _adapter.GetIndex()}", $"{root.GetChildCount() - 1}/True");
        Expect(ctx, "adapter ProcessMode", _adapter.ProcessMode, Node.ProcessModeEnum.Always);
        Expect(ctx, "adapter ProcessPriority", _adapter.ProcessPriority, int.MinValue);

        long written = _log.TotalWritten;
        Inject(Key(Godot.Key.Space, true));
        Inject(Key(Godot.Key.Space, false));
        Expect(ctx, "before Initialize: router fed nothing, overlay saw both edges",
            $"{_adapter.IsInitialized}/{_log.TotalWritten - written}/{_overlay.Seen.Count(s => s.StartsWith("key:Space", StringComparison.Ordinal))}", "False/0/2");
        _overlay.Seen.Clear();
    }

    private void CheckStampsAndHandled(SmokeContext ctx)
    {
        // Space is Jump in World: consumed, so neither the overlay nor the focused Button (ui_accept) sees press or release.
        _clock.NowMs = 5000;
        Inject(Key(Godot.Key.Space, true));
        Expect(ctx, "Space press stamped with IInputClock.NowMs (GateFired time, input)", LastGate(LogKind.GateFired), $"5000/{(ushort)PhysicalInput.KeySpace}");
        _clock.NowMs = 5016;
        Inject(Key(Godot.Key.Space, false));
        Expect(ctx, "consumed Space: overlay events, Button downs, Button presses",
            $"{_overlay.Seen.Count(s => s.StartsWith("key:Space", StringComparison.Ordinal))}/{_buttonDowns}/{_buttonPresses}", "0/0/0");

        // Enter is not live in World: not consumed, so the GUI activates the Button, but the release reached the router first.
        _clock.NowMs = 5100;
        Inject(Key(Godot.Key.Enter, true));
        _clock.NowMs = 5150;
        Inject(Key(Godot.Key.Enter, false));
        Expect(ctx, "unconsumed Enter: router saw press (GateNotLive) and release (GateSwallowed)",
            $"{LastGate(LogKind.GateNotLive)};{LastGate(LogKind.GateSwallowed)}", $"5100/{(ushort)PhysicalInput.KeyEnter};5150/{(ushort)PhysicalInput.KeyEnter}");
        Expect(ctx, "unconsumed Enter: overlay events, Button downs, Button presses",
            $"{_overlay.Seen.Count(s => s.StartsWith("key:Enter", StringComparison.Ordinal))}/{_buttonDowns}/{_buttonPresses}", "2/1/1");

        long unmapped = _adapter.UnmappedCount;
        Inject(Key(Godot.Key.F12, true));
        Inject(Key(Godot.Key.F12, false));
        Expect(ctx, "F12 press and release ignored and counted (not thrown)", _adapter.UnmappedCount - unmapped, 2L);
    }

    private void CheckEcho(SmokeContext ctx)
    {
        // H-G4-1 integration clause: E held 1 s with OS repeats at 30 / s resolves one press; the echoes write no gate line.
        long first = _log.TotalWritten;
        _clock.NowMs = 6000;
        Inject(Key(Godot.Key.E, true));
        for (int i = 1; i <= 30; i++)
        {
            _clock.NowMs = 6000 + (i * 33);
            Inject(Key(Godot.Key.E, true, echo: true));
        }

        _clock.NowMs = 7000;
        Inject(Key(Godot.Key.E, false));
        Expect(ctx, "H-G4-1: E + 30 echoes + release -> gate lines for E (fired, other)", GateLinesFor(PhysicalInput.KeyE, first), "1,0");
    }

    // Gate lines (fired, any other gate kind) written for `input` since log position `since`.
    private string GateLinesFor(PhysicalInput input, long since)
    {
        int fired = 0;
        int other = 0;
        int start = (int)Math.Max(0, _log.Count - (_log.TotalWritten - since));
        for (int i = start; i < _log.Count; i++)
        {
            if (_log.TryGet(i, out UiEventEntry e) && e.IdB == (ushort)input
                && e.Kind is LogKind.GateFired or LogKind.GateNotLive or LogKind.GateMuted or LogKind.GateSwallowed)
            {
                if (e.Kind == LogKind.GateFired)
                {
                    fired++;
                }
                else
                {
                    other++;
                }
            }
        }

        return $"{fired},{other}";
    }

    private void CheckMouseLook(SmokeContext ctx)
    {
        (int on, long onRecords) = LookRun(accumulated: true);
        (int off, long offRecords) = LookRun(accumulated: false);
        Godot.Input.UseAccumulatedInput = true;
        Expect(ctx, "16 motions (3, -2) in one frame, accumulation on: records fed", onRecords, 1L);
        Expect(ctx, "16 motions (3, -2) in one frame, accumulation off: records fed", offRecords, 1L);
        if (on != 0 && on == off)
        {
            ctx.Pass($"look turn identical with accumulation on and off ({on} BAM yaw)");
        }
        else
        {
            ctx.Fail($"look turn on {on}, off {off} (expected equal and non-zero)");
        }

        Expect(ctx, "motion consumed where Look is live: overlay saw none", _overlay.Seen.Count(s => s == "motion"), 0);
    }

    private (int Yaw, long Records) LookRun(bool accumulated)
    {
        Godot.Input.UseAccumulatedInput = accumulated;
        ushort before = _router.Look.Yaw;
        long records = _adapter.MotionRecords;
        for (int i = 0; i < 16; i++)
        {
            Godot.Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(3, -2), ScreenRelative = new Vector2(3, -2) });
            if (!accumulated)
            {
                Godot.Input.FlushBufferedEvents();
            }
        }

        Godot.Input.FlushBufferedEvents();
        _adapter.OnPreFrame();
        return ((ushort)(_router.Look.Yaw - before), _adapter.MotionRecords - records);
    }

    private void CheckAxes(SmokeContext ctx)
    {
        Inject(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0.5f });
        Inject(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = -1f });
        Expect(ctx, "pad 0 LeftX 0.5 and LeftY -1 cached as Q15", $"{_adapter.CachedAxis(0, JoyAxis.LeftX)},{_adapter.CachedAxis(0, JoyAxis.LeftY)}", "16384,-32767");

        Inject(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftX, AxisValue = 0f });
        Inject(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.LeftY, AxisValue = 0f });
        _clock.NowMs = 8000;
        Inject(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.TriggerLeft, AxisValue = 1f });
        Expect(ctx, "LT axis at 1.0 -> F4 press (Throw aim GateFired)", LastGate(LogKind.GateFired), $"8000/{(ushort)PhysicalInput.PadLeftTrigger}");
        Inject(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.TriggerLeft, AxisValue = 0f });

        // Re-seed: an axis change the adapter did not see (input processing off) is picked up by polling GetJoyAxis.
        _adapter.SetProcessInput(false);
        Inject(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0.75f });
        _adapter.SetProcessInput(true);
        short missed = _adapter.CachedAxis(0, JoyAxis.RightX);
        _adapter.ReseedAxes(0);
        Expect(ctx, "RightX 0.75 unseen, then re-seeded from Input.GetJoyAxis", $"{missed},{_adapter.CachedAxis(0, JoyAxis.RightX)}", "0,24575");
        Inject(new InputEventJoypadMotion { Device = 0, Axis = JoyAxis.RightX, AxisValue = 0f });
    }

    private void CheckSynthetic(SmokeContext ctx)
    {
        string before = $"{_adapter.LastUsedDevice}/{_adapter.LastUsedPad}";
        Inject(new InputEventAction { Action = "smoke_action", Pressed = true });
        Inject(new InputEventAction { Action = "smoke_action", Pressed = false });
        Expect(ctx, "InputEventAction leaves the last-used device unchanged", $"{_adapter.LastUsedDevice}/{_adapter.LastUsedPad}", before);
        Expect(ctx, "last-used device after the pad rows", before, "Pad/0");
    }

    // The newest log entry of `kind`: "time/input" (IdB is the physical input on gate lines).
    private string LastGate(LogKind kind)
    {
        for (int i = _log.Count - 1; i >= 0; i--)
        {
            if (_log.TryGet(i, out UiEventEntry e) && e.Kind == kind)
            {
                return $"{e.TimeMs}/{e.IdB}";
            }
        }

        return "none";
    }

    private sealed class ManualClock : IInputClock
    {
        public long NowMs { get; set; }
    }

    private sealed class IdleInteraction : IInteractionQuery
    {
        private readonly InteractionSnapshot _snapshot;

        public ref readonly InteractionSnapshot Current => ref _snapshot;
    }

    private sealed class NeverRefusingAim : IThrowAimView
    {
        public byte NotThrowable => 0;

        public bool EntryStale => false;
    }

    private sealed class ZeroTicks : IClientTickSource
    {
        public uint ClientTick => 0;
    }
}
