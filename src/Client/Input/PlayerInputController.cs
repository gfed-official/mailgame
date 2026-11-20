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
                    else if (!UiOpen && button.ButtonIndex == MouseButton.WheelUp) CycleSlot(-1);
                    else if (!UiOpen && button.ButtonIndex == MouseButton.WheelDown) CycleSlot(1);
                    return;

                case InputEventKey key when key.Pressed && !key.Echo:
                    // 1–9, and 0 for the tenth (a tenth card of the shop).
                    int digit = key.PhysicalKeycode == Key.Key0 ? 9 : (int)key.PhysicalKeycode - (int)Key.Key1;
                    // In the Draft 1–3 are the cards, until one is taken.
                    if (_client.PressCardKey(digit)) return;
                    // At the shop's counter the numbers are its cards.
                    if (_client.PressShopKey(digit)) return;
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
                else InventoryToggled?.Invoke();
            }
            else if (ev.IsActionPressed(InputActions.Pause))
            {
                if (UiOpen) Cancelled?.Invoke();
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
            else if (ev.IsActionPressed(InputActions.Use) && Building) _build!.Pipette();
            else if (ev.IsActionPressed(InputActions.Use) && !UiOpen) _client.UseHeld();
            else if (ev.IsActionPressed(InputActions.Shop) && !Paused) _client.ToggleShop();
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
            var move = UiOpen
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
            _client.Input.Buttons = buttons;

            // The Intake closes with the same key that opened it, so interact stays live under the panel.
            bool interactKey = Godot.Input.IsActionPressed(InputActions.Interact);
            _assist.Update(_client);
            _interaction.Update(delta, interactKey, _deliverAllQueued && !UiOpen, _interactTapped);
            _deliverAllQueued = false;
            _interactTapped = false;
            bool live = !UiOpen && (Captured || DisplayServer.GetName() == "headless");
            _build?.Update(delta, _placeQueued && live, live && Godot.Input.IsActionPressed(InputActions.AltAttack));
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
