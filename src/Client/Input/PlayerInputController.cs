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

        /// <summary>While true (a panel is open) the character stands still and the mouse is free.</summary>
        public bool UiOpen
        {
            get => _uiOpen;
            set
            {
                if (_uiOpen == value) return;
                _uiOpen = value;
                ApplyMouseMode();
            }
        }

        private ClientRuntime _client = null!;
        private InteractionController _interaction = null!;
        private readonly HotbarAssist _assist = new HotbarAssist();
        private bool _uiOpen;
        private bool _wantCapture = true;
        private bool _deliverAllQueued, _interactTapped;

        public void Setup(ClientRuntime client, InteractionController interaction)
        {
            _client = client;
            _interaction = interaction;
        }

        /// <summary>Turns the view: yaw wraps, pitch stops short of straight up and down.</summary>
        private void Look(float yawDelta, float pitchDelta)
        {
            ref var input = ref _client.Input;
            input.YawRadians = Mathf.Wrap(input.YawRadians + yawDelta, -Mathf.Pi, Mathf.Pi);
            input.PitchRadians = Mathf.Clamp(input.PitchRadians + pitchDelta, -ViewRay.MaxPitch, ViewRay.MaxPitch);
        }

        public override void _Ready() => ApplyMouseMode();

        public override void _ExitTree() => Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;

        public override void _UnhandledInput(InputEvent ev)
        {
            switch (ev)
            {
                case InputEventMouseMotion motion when Captured:
                    Look(-motion.Relative.X * MouseSensitivity, -motion.Relative.Y * MouseSensitivity);
                    return;

                case InputEventMouseButton button when button.Pressed:
                    if (!_uiOpen && !Captured && button.ButtonIndex == MouseButton.Left)
                    {
                        _wantCapture = true;
                        ApplyMouseMode();
                    }
                    else if (!_uiOpen && button.ButtonIndex == MouseButton.WheelUp) CycleSlot(-1);
                    else if (!_uiOpen && button.ButtonIndex == MouseButton.WheelDown) CycleSlot(1);
                    return;

                case InputEventKey key when key.Pressed && !key.Echo:
                    int digit = (int)key.PhysicalKeycode - (int)Key.Key1;
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
                else InventoryToggled?.Invoke();
            }
            else if (ev.IsActionPressed(InputActions.Pause))
            {
                if (_uiOpen) Cancelled?.Invoke();
                else
                {
                    _wantCapture = !_wantCapture;
                    ApplyMouseMode();
                }
            }
            else if (ev.IsActionPressed(InputActions.DeliverAll)) _deliverAllQueued = true;
            // A tap can start and end between two frames, where polling never sees it held.
            else if (ev.IsActionPressed(InputActions.Interact)) _interactTapped = true;
            else if (ev.IsActionPressed(InputActions.Go)) _client.PressGo();
        }

        /// <summary>Fills the client's input for the ticks of this frame; hooked to <c>GameSession.InputSource</c>.</summary>
        public void Sample(double delta)
        {
            var move = _uiOpen
                ? Vector2.Zero
                : Godot.Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveForward, InputActions.MoveBack);

            if (!_uiOpen)
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
            bool interact = !_uiOpen && Godot.Input.IsActionPressed(InputActions.Interact);
            if (!_uiOpen && Godot.Input.IsActionPressed(InputActions.Sprint)) buttons |= InputButtons.Sprint;
            if (!_uiOpen && Godot.Input.IsActionPressed(InputActions.Jump)) buttons |= InputButtons.Jump;
            if (interact) buttons |= InputButtons.Interact;
            _client.Input.Buttons = buttons;

            // The Intake closes with the same key that opened it, so interact stays live under the panel.
            bool interactKey = Godot.Input.IsActionPressed(InputActions.Interact);
            _assist.Update(_client);
            _interaction.Update(delta, interactKey, _deliverAllQueued && !_uiOpen, _interactTapped);
            _deliverAllQueued = false;
            _interactTapped = false;
        }

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
            Godot.Input.MouseMode = _wantCapture && !_uiOpen ? Godot.Input.MouseModeEnum.Captured : Godot.Input.MouseModeEnum.Visible;
        }
    }
}
