using System.Collections.Generic;
using Godot;

namespace PerformativeMail.Client.InputHandling
{
    /// <summary>
    /// Default input map (doc 09 §4), registered at boot so the project file stays free of
    /// serialized input events. An action that already exists (defined in the project or rebound)
    /// is left alone.
    /// </summary>
    public static class InputActions
    {
        public const string MoveForward = "move_forward";
        public const string MoveBack = "move_back";
        public const string MoveLeft = "move_left";
        public const string MoveRight = "move_right";
        public const string Sprint = "sprint";
        public const string Jump = "jump";
        public const string Interact = "interact";
        public const string DeliverAll = "deliver_all";
        public const string Inventory = "inventory";
        public const string Pause = "pause";

        private static readonly HashSet<string> Created = new HashSet<string>();

        public static void Register()
        {
            BindKey(MoveForward, Key.W);
            BindKey(MoveBack, Key.S);
            BindKey(MoveLeft, Key.A);
            BindKey(MoveRight, Key.D);
            BindKey(Sprint, Key.Shift);
            BindKey(Jump, Key.Space);
            BindKey(Interact, Key.E);
            BindKey(DeliverAll, Key.F);
            BindKey(Inventory, Key.Tab);
            BindKey(Pause, Key.Escape);

            BindAxis(MoveForward, JoyAxis.LeftY, -1f);
            BindAxis(MoveBack, JoyAxis.LeftY, 1f);
            BindAxis(MoveLeft, JoyAxis.LeftX, -1f);
            BindAxis(MoveRight, JoyAxis.LeftX, 1f);
            BindButton(Sprint, JoyButton.LeftStick);
            BindButton(Jump, JoyButton.A);
            BindButton(Interact, JoyButton.X);
            BindButton(Inventory, JoyButton.Y);
            BindButton(Pause, JoyButton.Start);
        }

        /// <summary>True if the action is ours to fill in.</summary>
        private static bool Ensure(string action)
        {
            if (Created.Contains(action)) return true;
            if (InputMap.HasAction(action)) return false;
            InputMap.AddAction(action, 0.3f);
            Created.Add(action);
            return true;
        }

        private static void BindKey(string action, Key key)
        {
            if (Ensure(action)) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }

        private static void BindAxis(string action, JoyAxis axis, float value)
        {
            if (Ensure(action)) InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = value });
        }

        private static void BindButton(string action, JoyButton button)
        {
            if (Ensure(action)) InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
        }
    }
}
