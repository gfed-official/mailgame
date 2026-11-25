using System;
using Godot;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.InputHandling
{
    /// <summary>
    /// Turns Godot input into the client's per-tick input and interaction requests (doc 09 §4).
    /// First person: the mouse or right stick turns the view, and movement is relative to it.
    /// </summary>
    public partial class PlayerInputController : Node
    {
        private const float MouseSensitivity = 0.0035f;
        private const float StickLookSpeed = 2.6f;
        private const int HotbarSlots = 8;

        /// <summary>Raised when the inventory action is pressed and no external container is open.</summary>
        public event Action? InventoryToggled;
        /// <summary>Raised on pause/escape; the screen closes whatever is open.</summary>
        public event Action? Cancelled;
        /// <summary>Raised by the map key, and by Esc while the map is open.</summary>
        public event Action? MapToggled;
        /// <summary>F6 in a debug build: the content is to be read again (doc 07 §6).</summary>
        public event Action? ContentReloadAsked;
        /// <summary>A chip's key while the map is up (Z, X, V).</summary>
        public event Action<MapChip>? MapChipPressed;
        /// <summary>True while the map is up: Esc puts it down first.</summary>
        public Func<bool>? MapOpen;
        /// <summary>The ping key's controller (doc 09 §2.6): while its wheel is up the mouse points on it, not the view.</summary>
        public PingController? Ping;
        /// <summary>True while the debug world's free camera has the keys and the mouse: the carrier stands still.</summary>
        public Func<bool>? Suspended;

        private ClientRuntime _client = null!;
        private InteractionController _interaction = null!;
        private BuildController? _build;
        private readonly HotbarAssist _assist = new HotbarAssist();
        private MouseCapture _mouse = null!;
        private bool _deliverAllQueued, _interactTapped, _attackSwallowed, _placeQueued;
        private uint _pauseRequest;

        /// <param name="mouse">Whose the mouse is; the screen says when a panel is open, the keys and clicks here do the rest.</param>
        public void Setup(ClientRuntime client, InteractionController interaction, MouseCapture mouse, BuildController? build = null)
        {
            _client = client;
            _interaction = interaction;
            _build = build;
            _mouse = mouse;
            client.RequestCompleted += (request, _) => { if (request.RequestId == _pauseRequest) _pauseRequest = 0; };
        }

        /// <summary>Turns the view: yaw wraps, pitch stops short of straight up and down.</summary>
        private void Look(float yawDelta, float pitchDelta)
        {
            ref var input = ref _client.Input;
            input.YawRadians = Mathf.Wrap(input.YawRadians + yawDelta, -Mathf.Pi, Mathf.Pi);
            input.PitchRadians = Mathf.Clamp(input.PitchRadians + pitchDelta, -ViewRay.MaxPitch, ViewRay.MaxPitch);
        }

        public override void _Ready() => ApplyMouseMode();

        /// <summary>Every frame, so that the mouse is the view's again with the frame a panel closes (issue (c) of the 25fbca5 playtest).</summary>
        public override void _Process(double delta) => ApplyMouseMode();

        public override void _Notification(int what)
        {
            // A grab does not outlive another window having had the pointer: back in front, the view takes it anew.
            if (what != NotificationApplicationFocusIn && what != NotificationWMWindowFocusIn) return;
            if (_mouse == null || !_mouse.Wanted || DisplayServer.GetName() == "headless") return;
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Captured;
        }

        public override void _ExitTree() => Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;

        public override void _UnhandledInput(InputEvent ev)
        {
            switch (ev)
            {
                case InputEventMouseMotion motion when Captured && Ping?.WheelOpen == true:
                    Ping.Steer(motion.Relative.X, motion.Relative.Y);
                    return;
                case InputEventMouseMotion when Suspended?.Invoke() == true:
                    return;
                case InputEventMouseMotion motion when Captured:
                    Look(-motion.Relative.X * MouseSensitivity, -motion.Relative.Y * MouseSensitivity);
                    return;

                case InputEventMouseButton button when button.Pressed:
                    if (button.ButtonIndex == MouseButton.Left && _mouse.Click())
                    {
                        // The click that takes the mouse back is not a swing of the hammer.
                        _attackSwallowed = true;
                        ApplyMouseMode();
                    }
                    else if (button.ButtonIndex == MouseButton.Left && Building) _placeQueued = true;
                    // In Build mode the wheel steps through the variants of a recipe (doc 09 §2.7: belt tiers).
                    else if (!UiOpen && Building && button.ButtonIndex == MouseButton.WheelUp) _build!.Cycle(-1);
                    else if (!UiOpen && Building && button.ButtonIndex == MouseButton.WheelDown) _build!.Cycle(1);
                    else if (!UiOpen && button.ButtonIndex == MouseButton.WheelUp) CycleSlot(-1);
                    else if (!UiOpen && button.ButtonIndex == MouseButton.WheelDown) CycleSlot(1);
                    return;

                case InputEventKey key when key.Pressed && !key.Echo:
                    // 1–9, 0 for the tenth, and the minus and equals keys beside it for the eleventh and twelfth (cards of the shop).
                    int digit = key.PhysicalKeycode == Key.Key0 ? 9 : key.PhysicalKeycode == Key.Minus ? 10 : key.PhysicalKeycode == Key.Equal ? 11
                        : key.PhysicalKeycode >= Key.Key1 && key.PhysicalKeycode <= Key.Key9 ? (int)key.PhysicalKeycode - (int)Key.Key1 : -1;
                    // At the shop's counter the numbers are its cards: first, so a key meant for them
                    // in the frame Payday turns into the Draft is no pick (playtest df77576).
                    if (_client.PressShopKey(digit)) return;
                    // With the filters of a sorter up, a key steps its output's filter on.
                    if (_client.PressFilterKey(digit)) return;
                    // In the Draft 1–3 are the cards, until one is taken.
                    if (_client.PressCardKey(digit)) return;
                    // In Build mode they are the recipes.
                    if (!UiOpen && _build != null && _build.PressKey(digit)) return;
                    if (digit >= 0 && digit < HotbarSlots)
                    {
                        _client.Input.HeldSlot = (byte)digit;
                        return;
                    }
                    break;
            }

            if (ev.IsActionPressed(InputActions.Inventory))
            {
                if (_client.World != null && _client.World.OpenContainerId != 0) _client.CloseContainer();
                else if (_client.World != null && _client.World.ShopOpen) _client.CloseShop();
                else if (_client.World?.FilterTarget != null) _client.CloseFilters();
                else InventoryToggled?.Invoke();
            }
            else if (ev.IsActionPressed(InputActions.Pause))
            {
                if (UiOpen) Cancelled?.Invoke();
                else if (MapOpen?.Invoke() == true) MapToggled?.Invoke();
                // Esc puts Build mode down before it lets the mouse go.
                else if (Building) _build!.Leave();
                else
                {
                    _mouse.Toggle();
                    ApplyMouseMode();
                }
            }
            else if (ev.IsActionPressed(InputActions.DeliverAll)) _deliverAllQueued = true;
            // A tap can start and end between two frames, where polling never sees it held.
            else if (ev.IsActionPressed(InputActions.Interact)) _interactTapped = true;
            else if (ev.IsActionPressed(InputActions.Go) && !Paused) _client.PressGo();
            else if (ev.IsActionPressed(InputActions.Build) && !UiOpen && !Paused && _build != null) _build.Toggle();
            else if (ev.IsActionPressed(InputActions.Rotate) && Building) _build!.Rotate();
            // In the Draft, before the card is chosen, R draws new cards (doc 01 §2.5).
            else if (ev.IsActionPressed(InputActions.Rotate) && _client.World?.CanReroll == true) _client.Reroll();
            // A pad's trigger builds as the left button does (that one is taken with the mouse above).
            else if (Building && ev is InputEventJoypadMotion && ev.IsActionPressed(InputActions.Attack)) _placeQueued = true;
            else if (ev.IsActionPressed(InputActions.Use) && Building) _build!.Pipette();
            else if (ev.IsActionPressed(InputActions.Use) && !UiOpen) _client.UseHeld();
            else if (ev.IsActionPressed(InputActions.Shop) && !Paused) _client.ToggleShop();
            else if (ev.IsActionPressed(InputActions.Map)) MapToggled?.Invoke();
            // While the map is up Z, X and V are its chips (V is the world's key only in the Lobby, with the map down).
            else if (MapOpen?.Invoke() == true && ev.IsActionPressed(InputActions.MapMail)) MapChipPressed?.Invoke(MapChip.Mail);
            else if (MapOpen?.Invoke() == true && ev.IsActionPressed(InputActions.MapRoutes)) MapChipPressed?.Invoke(MapChip.Routes);
            else if (MapOpen?.Invoke() == true && ev.IsActionPressed(InputActions.MapResources)) MapChipPressed?.Invoke(MapChip.Resources);
            else if (ev.IsActionPressed(InputActions.Kit) && !UiOpen) _client.CycleKit();
            else if (ev.IsActionPressed(InputActions.World) && !UiOpen) _client.CycleWorld();
            else if (ev.IsActionPressed(InputActions.Seed) && !UiOpen) _client.NewSeed();
            else if (ev.IsActionPressed(InputActions.ReloadContent) && OS.IsDebugBuild()) ContentReloadAsked?.Invoke();
        }

        /// <summary>
        /// Doc 09 §2.12: Esc "pauses the server in solo". Until there is a pause menu, the freed
        /// mouse is that menu: a solo host's run stands still exactly while the mouse is let go.
        /// </summary>
        private void SyncPause()
        {
            var world = _client.World;
            if (world == null || _pauseRequest != 0) return;
            bool want = _mouse.Released;
            if (want == world.Paused || (want && !world.CanPause)) return;
            _pauseRequest = _client.SetPaused(want);
        }

        /// <summary>Fills the client's input for the ticks of this frame; hooked to <c>GameSession.InputSource</c>.</summary>
        public void Sample(double delta)
        {
            SyncPause();
            Ping?.Update(delta, !UiOpen && !Paused && Godot.Input.IsActionPressed(InputActions.Ping));
            bool suspended = Suspended?.Invoke() == true;
            var move = UiOpen || suspended
                ? Vector2.Zero
                : Godot.Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveForward, InputActions.MoveBack);

            if (!UiOpen)
            {
                float lookX = Godot.Input.GetJoyAxis(0, JoyAxis.RightX), lookY = Godot.Input.GetJoyAxis(0, JoyAxis.RightY);
                if (Mathf.Abs(lookX) > 0.2f || Mathf.Abs(lookY) > 0.2f)
                    Look(-lookX * StickLookSpeed * (float)delta, -lookY * StickLookSpeed * (float)delta);
            }

            // Yaw 0 looks along −Z: forward = (−sin, −cos), right = (cos, −sin). The body turns with the view.
            float yaw = _client.Input.YawRadians;
            float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
            float forward = -move.Y;
            _client.Input.MoveX = move.X * cos - forward * sin;
            _client.Input.MoveZ = -move.X * sin - forward * cos;

            var buttons = InputButtons.None;
            bool interact = !UiOpen && Godot.Input.IsActionPressed(InputActions.Interact);
            if (!UiOpen && Godot.Input.IsActionPressed(InputActions.Sprint)) buttons |= InputButtons.Sprint;
            if (!UiOpen && Godot.Input.IsActionPressed(InputActions.Jump)) buttons |= InputButtons.Jump;
            if (interact) buttons |= InputButtons.Interact;
            // The weapons are in the hands only while the view is the player's: not under a panel, not with the mouse let go.
            bool attack = Godot.Input.IsActionPressed(InputActions.Attack);
            if (!attack) _attackSwallowed = false;
            // In Build mode the buttons build and take down instead.
            bool armed = !UiOpen && !Building && (Captured || DisplayServer.GetName() == "headless");
            if (armed && attack && !_attackSwallowed) buttons |= InputButtons.Attack;
            if (armed && Godot.Input.IsActionPressed(InputActions.AltAttack)) buttons |= InputButtons.AltAttack;
            _client.Input.Buttons = suspended ? InputButtons.None : buttons;

            // The Intake closes with the same key that opened it, so interact stays live under the panel.
            bool interactKey = !suspended && Godot.Input.IsActionPressed(InputActions.Interact);
            _assist.Update(_client);
            _interaction.Update(delta, interactKey, _deliverAllQueued && !UiOpen, _interactTapped);
            _deliverAllQueued = false;
            _interactTapped = false;
            bool live = !UiOpen && !suspended && (Captured || DisplayServer.GetName() == "headless");
            _build?.Update(delta, _placeQueued && live, live && Godot.Input.IsActionPressed(InputActions.AltAttack), live && Godot.Input.IsActionPressed(InputActions.Attack));
            _placeQueued = false;
        }

        private bool Building => _build != null && _build.Active;

        private bool Paused => _client.World != null && _client.World.Paused;

        /// <summary>While a panel is open the character stands still and the mouse is free.</summary>
        private bool UiOpen => _mouse.PanelOpen;

        private bool Captured => Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Captured;

        private void CycleSlot(int step)
        {
            int slot = (_client.Input.HeldSlot + step) % HotbarSlots;
            _client.Input.HeldSlot = (byte)(slot < 0 ? slot + HotbarSlots : slot);
        }

        private void ApplyMouseMode()
        {
            // No pointer to grab without a window.
            if (DisplayServer.GetName() == "headless") return;
            var mode = _mouse.Wanted ? Godot.Input.MouseModeEnum.Captured : Godot.Input.MouseModeEnum.Visible;
            if (Godot.Input.MouseMode != mode) Godot.Input.MouseMode = mode;
        }
    }
}
