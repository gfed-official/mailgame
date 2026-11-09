using Godot;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Game.Input;
using static PerformativeMail.Game.Smoke.SmokeChecks;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// Story input-controls 027: the real <see cref="InputAdapter"/> over a real <see cref="InputRouter"/> (built-in M-1a map, a
/// manual ms clock) in the un-booted <c>Main</c> tree, fed synthetic events through <c>Input.ParseInputEvent</c> +
/// <c>FlushBufferedEvents</c>. Rows: the joypad project setting read back (H-R11-3 clause 2); mouse mode requested Captured in
/// World and Visible in Panel, written only on a state change, capture re-asked on a click while the readback shows it
/// dropped (headless ignores capture, spike S-31, so the real-display "captured / visible" rows are story 029's); GUI focus
/// released on entering World, so a <c>Button</c> focused before Initialize, and one focused in Panel, is never activated by
/// Space or Enter in World (H-R11-3 clause 4); <see cref="FocusPolicyCheck"/> over <c>Main/Hud</c> and the <c>Screens</c>
/// exemption; the <c>ui_*</c> InputMap unchanged at the first frame. Pause is not reachable without story 016 (the router's
/// Pause mouse mode is covered by <c>RouterTransitionsTests</c>). Debug smoke code only; writes nothing to disk.
/// </summary>
[SmokeScenario("input-mouse-focus")]
public sealed class InputMouseFocusSmoke : SmokeScenario
{
    private const string JoypadSetting = "input_devices/joypads/ignore_joypad_on_unfocused_application";

    private readonly ManualClock _clock = new() { NowMs = 1000 };
    private InputRouter _router = null!;
    private InputAdapter _adapter = null!;
    private Control _gui = null!;
    private Button _button = null!;
    private int _buttonPresses;
    private int _step;

    /// <inheritdoc />
    public override void Start(SmokeContext ctx)
    {
        var interaction = new IdleInteraction();
        _router = new InputRouter(BuiltInActionMap.CreateM1a(), InputSettings.Defaults, _clock, interaction,
            new PresentedPromptLatch(interaction), new CarrierStatusSourceStub(), new NeverRefusingAim(), new ZeroTicks(), new UiEventLog());

        Window root = ctx.Tree.Root;
        _adapter = InputAdapter.AttachToRoot(root);
        _gui = new Control { Name = "SmokeMenu" };
        _button = new Button { Name = "Focused", Text = "OK", Size = new Vector2(100, 40) };
        _button.Pressed += () => _buttonPresses++;
        _gui.AddChild(_button);
        root.CallDeferred(Node.MethodName.AddChild, _gui);

        Expect(ctx, $"project setting {JoypadSetting}", ProjectSettings.GetSetting(JoypadSetting).AsBool(), true);
        Expect(ctx, "Input.IgnoreJoypadOnUnfocusedApplication at runtime", Godot.Input.IgnoreJoypadOnUnfocusedApplication, true);
    }

    /// <inheritdoc />
    public override SmokeStatus Poll(SmokeContext ctx)
    {
        switch (_step++)
        {
            case 0:
                return SmokeStatus.Running;
            case 1:
                CheckInitialize(ctx);
                CheckPanelRoundTrip(ctx);
                CheckClickReapply(ctx);
                CheckFocusPolicy(ctx);
                CheckBootChecks(ctx);
                _gui.QueueFree();
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

    private static void Tap(Key key)
    {
        Inject(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        Inject(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
    }

    private void CheckInitialize(SmokeContext ctx)
    {
        // A menu Button holds focus when play starts (Initialize in World): the adapter releases it.
        _button.GrabFocus();
        Expect(ctx, "Button focused before Initialize", _button.HasFocus(), true);
        _adapter.Initialize(new AdapterDeps { Router = _router, Clock = _clock, Settings = InputSettings.Defaults, Main = ctx.Loop });
        Expect(ctx, "Initialize in World: mouse mode applied, writes, focus released",
            $"{_adapter.Mouse.Applied}/{_adapter.Mouse.Writes}/{_button.HasFocus()}", "Captured/1/False");
        ctx.Pass($"capture readback after the World write: rejected={_adapter.Mouse.CaptureRejected} (headless ignores capture, S-31; real display is story 029)");
        ExpectSpaceEnterInert(ctx, "after Initialize");
    }

    private void CheckPanelRoundTrip(SmokeContext ctx)
    {
        Tap(Key.Tab);
        Expect(ctx, "Tab -> Panel: mouse mode Visible, writes", $"{_adapter.Mouse.Applied}/{_adapter.Mouse.Writes}", "Visible/2");
        long writes = _adapter.Mouse.Writes;
        Tap(Key.R);
        Tap(Key.W);
        Expect(ctx, "keys that change no state in Panel: no mouse-mode write", _adapter.Mouse.Writes, writes);

        _button.GrabFocus();
        Tap(Key.Tab);
        Expect(ctx, "Tab -> World: mouse mode Captured, writes, Button focus released",
            $"{_adapter.Mouse.Applied}/{_adapter.Mouse.Writes}/{_button.HasFocus()}", "Captured/3/False");
        ExpectSpaceEnterInert(ctx, "after Panel -> World");
    }

    // H-R11-3 clause 4: Space (consumed as Jump) and Enter (not live in World) never activate the once-focused Button.
    private void ExpectSpaceEnterInert(SmokeContext ctx, string when)
    {
        int before = _buttonPresses;
        Tap(Key.Space);
        Tap(Key.Enter);
        Tap(Key.KpEnter);
        Expect(ctx, $"H-R11-3: Space, Enter, KP Enter in World {when}: Button presses", _buttonPresses - before, 0);
    }

    private void CheckClickReapply(SmokeContext ctx)
    {
        long writes = _adapter.Mouse.Writes;
        bool rejected = _adapter.Mouse.CaptureRejected;
        Inject(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Inject(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        Expect(ctx, "mouse press in World re-asks capture only while the readback shows it dropped (D-2)",
            _adapter.Mouse.Writes - writes, rejected ? 1L : 0L);
    }

    private static void CheckFocusPolicy(SmokeContext ctx)
    {
        Node hud = ctx.Loop.GetNode("Hud");
        Node screens = ctx.Loop.GetNode("Screens");
        var layer = new CanvasLayer { Name = "SmokeHudLayer" };
        var stray = new Button { Name = "Stray" };
        var hidden = new Button { Name = "Hidden", Visible = false };
        var label = GameControlFocus.Apply(new Button { Name = "Game" });
        var menu = new Button { Name = "SmokeMenuButton" };
        layer.AddChild(stray);
        layer.AddChild(hidden);
        layer.AddChild(label);
        hud.AddChild(layer);
        screens.AddChild(menu);

        IReadOnlyList<string> found = FocusPolicyCheck.Run(ctx.Loop);
        Expect(ctx, "FocusPolicyCheck: the visible focusable Hud Button only (hidden, Apply-ed and Screens exempt)",
            string.Join(";", found), $"{stray.GetPath()} ({Control.FocusModeEnum.All})");
        GameControlFocus.Apply(stray);
        Expect(ctx, "FocusPolicyCheck after GameControlFocus.Apply on it", FocusPolicyCheck.Run(ctx.Loop).Count, 0);

        hud.RemoveChild(layer);
        layer.QueueFree();
        screens.RemoveChild(menu);
        menu.QueueFree();
    }

    private void CheckBootChecks(SmokeContext ctx)
    {
        Expect(ctx, "before the first frame: boot checks not run", _adapter.UiMapIntact is null, true);
        _adapter.OnPreFrame();
        Expect(ctx, "first frame: ui_* InputMap matches the startup snapshot; focus policy findings in the clean tree",
            $"{_adapter.UiMapIntact}/{_adapter.FocusPolicyFindings.Count}", "True/0");
        Expect(ctx, "ui_* actions in the startup snapshot (non-zero)", UiInputMapSnapshot.Capture().ActionCount > 0, true);
    }
}
