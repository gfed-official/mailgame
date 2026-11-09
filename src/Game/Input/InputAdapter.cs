using Godot;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Game.Hosting;
using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Input;

/// <summary>
/// The Godot input adapter (ADR-0012 section 7 as amended by the OQ-IC-9 spike deltas, story input-controls 026): in
/// <c>_Input</c> it arrival-stamps each event with <see cref="AdapterDeps.Clock"/> (read once per call; Godot's
/// <c>InputEvent</c> has no timestamp), classifies it by event type first and then by <c>DEVICE_ID_*</c>, converts it to a
/// <see cref="RawInputRecord"/> through <see cref="PhysicalInputMap"/> and feeds the router; when the router reports the record
/// consumed (<see cref="InputRouter.LastFeedConsumed"/>) it marks the event handled, so a consumed press and its release never
/// reach the GUI (D-3). Mouse motion is summed and fed as one record per rendered frame (<see cref="OnPreFrame"/>, or before
/// the next non-motion event so order is kept); joypad axes go through the per-device <see cref="JoyAxisCache"/>.
/// <para>
/// Story 027: on every router-state change (<see cref="InputRouter.StateChanged"/>) it applies the router's mouse-mode request
/// through <see cref="Mouse"/> (written only when it changes; capture re-asked on each mouse press while the readback shows it
/// dropped, D-2) and, when the new state is a game context, calls <c>GuiReleaseFocus()</c>; it does the same once when it is
/// both initialised and in the tree. On its first pre-frame call it compares the <c>ui_*</c> InputMap with the snapshot taken
/// at construction and, in a debug build, runs <see cref="FocusPolicyCheck"/> over <see cref="AdapterDeps.Main"/>, pushing an
/// engine error or warning per finding. It never pauses anything: solo pause is <c>TickDriver.Pause</c> (story 028), never
/// <c>SceneTree.Paused</c>.
/// </para>
/// <para>
/// Placement (D-1): input callbacks run in reverse tree order and <c>ProcessPriority</c> does not reorder them (spike S-06), so
/// the adapter is the last child of the root window and moves itself back there whenever another node enters the root;
/// <see cref="AttachToRoot"/> adds it (deferred, spike H-1). <c>ProcessMode</c> is Always, so a paused tree still reaches it.
/// Inert until <see cref="Initialize"/>; it never looks anything up in <c>_Ready</c> (ADR-0019). The only code besides story
/// 027's that reads <c>Godot.Input</c> (the axis re-seed). Never calls <c>SimWorld</c>, <c>ClientLayer</c> or the frame runner.
/// Main thread only.
/// </para>
/// </summary>
/// <example>
/// <code>
/// InputAdapter adapter = InputAdapter.AttachToRoot(GetTree().Root);
/// adapter.Initialize(new AdapterDeps { Router = router, Clock = clock, Settings = settings });
/// loop.RegisterPreFrameHook(adapter);   // first, before DispatchImmediates and FlushToLatch
/// </code>
/// </example>
public partial class InputAdapter : Node, IInjectable<AdapterDeps>, IPreFrameHook
{
    /// <summary>The node's name under the root window.</summary>
    public const string NodeName = "InputAdapter";

    private readonly JoyAxisCache _axes = new();
    private readonly UiInputMapSnapshot _uiMapAtStartup;
    private AdapterDeps? _deps;
    private bool _subscribed;
    private bool _bootChecksDone;
    private Window? _root;
    private bool _motionPending;
    private long _motionMs;
    private float _motionX;
    private float _motionY;

    /// <summary>Creates an inert adapter: <c>ProcessMode</c> Always, <c>ProcessPriority</c> <see cref="int.MinValue"/>.</summary>
    public InputAdapter()
    {
        Name = NodeName;
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue;
        _uiMapAtStartup = UiInputMapSnapshot.Capture();
    }

    /// <summary>What <see cref="TryConvert"/> made of an event.</summary>
    internal enum Conversion : byte
    {
        /// <summary>A record for the router.</summary>
        Record,

        /// <summary>A key, mouse or joypad event with no M-1a input or an unexpected device id: ignored and counted.</summary>
        Unmapped,

        /// <summary>Not an input the router knows (touch, gesture, MIDI, shortcut) or a wheel release: ignored.</summary>
        Ignored,
    }

    /// <inheritdoc />
    public bool IsInitialized => _deps is not null;

    /// <summary>Key, mouse and joypad events ignored because they map to no input or carry an unexpected device id.</summary>
    public long UnmappedCount { get; private set; }

    /// <summary>Coalesced mouse-motion records fed to the router since construction.</summary>
    public long MotionRecords { get; private set; }

    /// <summary>The device class of the last physical event; null before the first. Synthetic events never change it.</summary>
    public DeviceClass? LastUsedDevice { get; private set; }

    /// <summary>The pad index of the last physical event (0 unless <see cref="LastUsedDevice"/> is a pad).</summary>
    public byte LastUsedPad { get; private set; }

    /// <summary>The mouse-mode switch (story 027).</summary>
    public MouseModeController Mouse { get; } = new();

    /// <summary>GUI-focus releases on entering a game context since construction (story 027).</summary>
    public long FocusReleases { get; private set; }

    /// <summary>The first-frame <c>ui_*</c> InputMap comparison: null until it has run, then whether the map was untouched.</summary>
    public bool? UiMapIntact { get; private set; }

    /// <summary>What the first-frame <see cref="FocusPolicyCheck"/> found (empty when skipped, not yet run, or clean).</summary>
    public IReadOnlyList<string> FocusPolicyFindings { get; private set; } = [];

    /// <summary>
    /// Creates an adapter and adds it to <paramref name="root"/> as its last child, deferred (adding to the root while the main
    /// scene sets up its children fails, spike H-1). From then on it moves itself back to the last place whenever another node
    /// enters the root (deferred too).
    /// </summary>
    /// <param name="root">The root window (<c>GetTree().Root</c>).</param>
    /// <returns>The adapter, not yet in the tree and not yet initialised.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
    public static InputAdapter AttachToRoot(Window root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var adapter = new InputAdapter { _root = root };
        root.ChildEnteredTree += adapter.OnRootChildEntered;
        root.CallDeferred(Node.MethodName.AddChild, adapter);
        return adapter;
    }

    /// <summary>Receives the router, its clock and the settings; events are ignored until this has run.</summary>
    /// <param name="deps">The dependencies.</param>
    /// <exception cref="ArgumentNullException"><paramref name="deps"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The adapter is already initialised.</exception>
    public void Initialize(AdapterDeps deps)
    {
        ArgumentNullException.ThrowIfNull(deps);
        if (_deps is not null)
        {
            throw new InvalidOperationException("The InputAdapter is already initialised.");
        }

        _deps = deps;
        if (IsInsideTree())
        {
            Subscribe();
        }
    }

    /// <summary>
    /// Feeds this frame's summed mouse motion. Registered as the first pre-frame hook (story 028), before the router's. The first
    /// call also runs the boot checks (the <c>ui_*</c> InputMap comparison and the debug-build focus policy check).
    /// </summary>
    public void OnPreFrame()
    {
        if (!_bootChecksDone && _deps is not null)
        {
            _bootChecksDone = true;
            RunBootChecks(_deps.Main);
        }

        FlushMotion();
    }

    /// <summary>
    /// Re-seeds the cache of pad <paramref name="pad"/> by polling <c>Godot.Input.GetJoyAxis</c> for every axis and feeds each
    /// value to the router (ADR-0012 Step 5.5 note 5: the only polled read). Called on focus-in (story 027) and reconnect
    /// (story 031). Does not change <see cref="LastUsedDevice"/>.
    /// </summary>
    /// <param name="pad">The joypad index; indices outside the cache are ignored.</param>
    public void ReseedAxes(int pad)
    {
        if (_deps is null || pad is < 0 or >= JoyAxisCache.MaxPads)
        {
            return;
        }

        long nowMs = _deps.Clock.NowMs;
        for (JoyAxis axis = JoyAxis.LeftX; axis <= JoyAxis.TriggerRight; axis++)
        {
            short q15 = JoyAxisCache.ToQ15(Godot.Input.GetJoyAxis(pad, axis));
            _axes.Store(pad, axis, q15);
            var r = new RawInputRecord(nowMs, RawKind.PadAxis, DeviceClass.Pad, (byte)pad, PhysicalInputMap.FromJoyAxis(axis), q15 != 0, false, q15);
            _deps.Router.Feed(in r);
        }
    }

    /// <summary>The cached value of <paramref name="axis"/> on <paramref name="pad"/>, Q15 (for the smoke and the dev overlay).</summary>
    /// <param name="pad">A pad index below <see cref="JoyAxisCache.MaxPads"/>.</param>
    /// <param name="axis">A stick or trigger axis.</param>
    /// <returns>The value.</returns>
    public short CachedAxis(int pad, JoyAxis axis) => _axes.Get(pad, axis);

    /// <summary>Stamps, classifies, converts and feeds one event, then marks it handled if the router consumed it.</summary>
    /// <param name="event">The event.</param>
    public override void _Input(InputEvent @event)
    {
        if (_deps is null)
        {
            return;
        }

        long nowMs = _deps.Clock.NowMs;
        if (@event is InputEventMouseMotion motion)
        {
            AddMotion(motion, nowMs);
            return;
        }

        FlushMotion();
        switch (TryConvert(@event, nowMs, out RawInputRecord r))
        {
            case Conversion.Unmapped:
                UnmappedCount++;
                return;
            case Conversion.Ignored:
                return;
            default:
                break;
        }

        if (r.Kind != RawKind.Synthetic)
        {
            LastUsedDevice = r.Device;
            LastUsedPad = r.PadIndex;
        }

        if (@event is InputEventJoypadMotion joy)
        {
            FeedAxis(in r, joy.Axis);
            return;
        }

        if (r.Kind == RawKind.MouseButton && r.Pressed)
        {
            Mouse.Reapply();
        }

        _deps.Router.Feed(in r);
        if (_deps.Router.LastFeedConsumed)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Starts following the router's state once initialised (story 027).</summary>
    public override void _EnterTree()
    {
        if (_deps is not null)
        {
            Subscribe();
        }
    }

    /// <summary>Stops following the router's state and stops re-asserting the root-last place.</summary>
    public override void _ExitTree()
    {
        if (_subscribed)
        {
            _deps!.Router.StateChanged -= OnRouterStateChanged;
            _subscribed = false;
        }

        if (_root is not null)
        {
            _root.ChildEnteredTree -= OnRootChildEntered;
            _root = null;
        }
    }

    /// <summary>
    /// Classifies by event type first, then checks the device id (ADR-0012 Step 5.5 note 1): a key event is the keyboard only
    /// with <c>DeviceIdKeyboard</c>; a mouse button or motion is the mouse with <c>DeviceIdMouse</c> or <c>DeviceIdEmulation</c>
    /// (touch emulation, −1); a joypad button or motion is pad <c>Device</c> (below <see cref="JoyAxisCache.MaxPads"/>); an
    /// <c>InputEventAction</c> is synthetic. No table is indexed by <c>Device</c> before this, so pads 16 and 32 never read as
    /// keyboard or mouse, and a keyboard event never reads as pad 0.
    /// </summary>
    /// <param name="e">The event.</param>
    /// <param name="device">The device class (<see cref="DeviceClass.Synthetic"/> when false).</param>
    /// <param name="pad">The pad index for a pad, else 0.</param>
    /// <returns>False for an event type the router does not take, or a device id that does not match its type.</returns>
    internal static bool TryClassify(InputEvent e, out DeviceClass device, out byte pad)
    {
        pad = 0;
        switch (e)
        {
            case InputEventKey:
                device = DeviceClass.Keyboard;
                return e.Device == InputEvent.DeviceIdKeyboard;
            case InputEventMouseButton or InputEventMouseMotion:
                device = DeviceClass.Mouse;
                return e.Device == InputEvent.DeviceIdMouse || e.Device == InputEvent.DeviceIdEmulation;
            case InputEventJoypadButton or InputEventJoypadMotion:
                device = DeviceClass.Pad;
                if (e.Device is < 0 or >= JoyAxisCache.MaxPads)
                {
                    return false;
                }

                pad = (byte)e.Device;
                return true;
            case InputEventAction:
                device = DeviceClass.Synthetic;
                return true;
            default:
                device = DeviceClass.Synthetic;
                return false;
        }
    }

    /// <summary>
    /// Converts one non-motion event to a record stamped <paramref name="nowMs"/>: keys by physical keycode with the echo flag,
    /// mouse buttons, a wheel press as one <see cref="RawKind.Wheel"/> notch (its release is ignored), joypad buttons, joypad
    /// axes in Q15, and an <c>InputEventAction</c> as <see cref="RawKind.Synthetic"/>.
    /// </summary>
    /// <param name="e">The event (not mouse motion, which <c>_Input</c> coalesces).</param>
    /// <param name="nowMs">The arrival stamp.</param>
    /// <param name="r">The record, when the result is <see cref="Conversion.Record"/>.</param>
    /// <returns>What the event became.</returns>
    internal static Conversion TryConvert(InputEvent e, long nowMs, out RawInputRecord r)
    {
        r = default;
        if (!TryClassify(e, out DeviceClass device, out byte pad))
        {
            return e is InputEventKey or InputEventMouseButton or InputEventMouseMotion or InputEventJoypadButton or InputEventJoypadMotion
                ? Conversion.Unmapped
                : Conversion.Ignored;
        }

        PhysicalInput input;
        switch (e)
        {
            case InputEventKey key:
                input = PhysicalInputMap.FromKey(key.PhysicalKeycode);
                r = new RawInputRecord(nowMs, RawKind.Key, device, 0, input, key.Pressed, key.Echo);
                break;
            case InputEventMouseButton button:
                input = PhysicalInputMap.FromMouseButton(button.ButtonIndex);
                if (input is PhysicalInput.WheelUp or PhysicalInput.WheelDown)
                {
                    if (!button.Pressed)
                    {
                        return Conversion.Ignored;
                    }

                    r = new RawInputRecord(nowMs, RawKind.Wheel, device, 0, input, true, false, value: 1);
                    break;
                }

                r = new RawInputRecord(nowMs, RawKind.MouseButton, device, 0, input, button.Pressed, false);
                break;
            case InputEventJoypadButton joyButton:
                input = PhysicalInputMap.FromJoyButton(joyButton.ButtonIndex);
                r = new RawInputRecord(nowMs, RawKind.PadButton, device, pad, input, joyButton.Pressed, false);
                break;
            case InputEventJoypadMotion joyMotion:
                input = JoyAxisCache.IsCached(joyMotion.Axis) ? PhysicalInputMap.FromJoyAxis(joyMotion.Axis) : PhysicalInput.None;
                short q15 = JoyAxisCache.ToQ15(joyMotion.AxisValue);
                r = new RawInputRecord(nowMs, RawKind.PadAxis, device, pad, input, q15 != 0, false, q15);
                break;
            case InputEventAction action:
                r = new RawInputRecord(nowMs, RawKind.Synthetic, DeviceClass.Synthetic, 0, PhysicalInput.None, action.Pressed, false);
                return Conversion.Record;
            default:
                return Conversion.Ignored;
        }

        return input == PhysicalInput.None ? Conversion.Unmapped : Conversion.Record;
    }

    // Follows the router's state from now on and applies the current one.
    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _deps!.Router.StateChanged += OnRouterStateChanged;
        OnRouterStateChanged();
    }

    // Applies the mouse mode (a write only when the request changed) and drops any GUI focus on entering a game context.
    private void OnRouterStateChanged()
    {
        InputRouter router = _deps!.Router;
        Viewport viewport = GetViewport();
        Mouse.Apply(router.MouseModeRequest, viewport);
        if (router.InGameContext)
        {
            viewport.GuiReleaseFocus();
            FocusReleases++;
        }
    }

    private void RunBootChecks(Node? main)
    {
        UiMapIntact = _uiMapAtStartup.Matches(UiInputMapSnapshot.Capture(), out string diff);
        if (UiMapIntact == false)
        {
            GD.PushError(diff);
        }

        if (main is not null)
        {
            FocusPolicyFindings = FocusPolicyCheck.Run(main);
            foreach (string finding in FocusPolicyFindings)
            {
                GD.PushWarning($"Focus policy: a game Control can take GUI focus: {finding}");
            }
        }
    }

    // Caches the sample; a stick axis is fed as its x and y pair (the cached partner beside the new value), a trigger alone.
    private void FeedAxis(in RawInputRecord r, JoyAxis axis)
    {
        _axes.Store(r.PadIndex, axis, r.Value);
        JoyAxis partner = JoyAxisCache.PartnerOf(axis);
        if (partner == JoyAxis.Invalid)
        {
            _deps!.Router.Feed(in r);
            return;
        }

        JoyAxis x = axis is JoyAxis.LeftX or JoyAxis.RightX ? axis : partner;
        FeedCachedAxis(in r, x);
        FeedCachedAxis(in r, x + 1);
    }

    private void FeedCachedAxis(in RawInputRecord source, JoyAxis axis)
    {
        short q15 = _axes.Get(source.PadIndex, axis);
        var r = new RawInputRecord(source.ArrivalMs, RawKind.PadAxis, DeviceClass.Pad, source.PadIndex, PhysicalInputMap.FromJoyAxis(axis), q15 != 0, false, q15);
        _deps!.Router.Feed(in r);
    }

    // Sums ScreenRelative (unaffected by viewport stretch, spike S-21) until the frame's flush; the stamp is the first event's.
    // Handled where Look is live: the router turns the look with it at the flush.
    private void AddMotion(InputEventMouseMotion motion, long nowMs)
    {
        if (!TryClassify(motion, out _, out _))
        {
            UnmappedCount++;
            return;
        }

        Vector2 d = motion.ScreenRelative;
        if (!float.IsFinite(d.X) || !float.IsFinite(d.Y))
        {
            return;
        }

        if (!_motionPending)
        {
            _motionPending = true;
            _motionMs = nowMs;
        }

        _motionX += d.X;
        _motionY += d.Y;
        LastUsedDevice = DeviceClass.Mouse;
        LastUsedPad = 0;
        if (_deps!.Router.MouseModeRequest == MouseModeRequest.Captured)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    // Feeds the summed whole counts (split only if past the 16-bit range) and carries the fraction to the next frame.
    private void FlushMotion()
    {
        if (!_motionPending || _deps is null)
        {
            return;
        }

        _motionPending = false;
        int dx = (int)MathF.Truncate(Math.Clamp(_motionX, -1_000_000f, 1_000_000f));
        int dy = (int)MathF.Truncate(Math.Clamp(_motionY, -1_000_000f, 1_000_000f));
        _motionX -= dx;
        _motionY -= dy;
        while (dx != 0 || dy != 0)
        {
            short cx = (short)Math.Clamp(dx, -short.MaxValue, short.MaxValue);
            short cy = (short)Math.Clamp(dy, -short.MaxValue, short.MaxValue);
            var r = new RawInputRecord(_motionMs, RawKind.MouseMotion, DeviceClass.Mouse, 0, PhysicalInput.MouseMotion, false, false, cx, cy);
            _deps.Router.Feed(in r);
            MotionRecords++;
            dx -= cx;
            dy -= cy;
        }
    }

    private void OnRootChildEntered(Node node)
    {
        if (node != this)
        {
            Callable.From(MoveToLast).CallDeferred();
        }
    }

    private void MoveToLast()
    {
        if (IsInsideTree() && GetParent() is Node parent && GetIndex() != parent.GetChildCount() - 1)
        {
            parent.MoveChild(this, parent.GetChildCount() - 1);
        }
    }
}
