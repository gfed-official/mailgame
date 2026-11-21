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
        /// <summary>Start the run / ready / continue. Doc 09 §4 has no key for it; R is Rotate and Enter is Chat there, so G ("go").</summary>
        public const string Go = "go";
        /// <summary>Turns the stack on the cursor a quarter (doc 09 §4: R).</summary>
        public const string Rotate = "rotate";
        /// <summary>The hammer: left mouse button, right trigger (the concept video's melee).</summary>
        public const string Attack = "attack";
        /// <summary>The mail cannon: right mouse button, left trigger.</summary>
        public const string AltAttack = "alt_attack";
        /// <summary>Uses the held consumable. Doc 09 §4 has no key for it and the mouse buttons are the weapons, so Q.</summary>
        public const string Use = "use";
        /// <summary>
        /// Puts the cards of the shop up or down without aiming at its counter: at the counter in
        /// Prep, anywhere at Payday. Doc 09 §4 has no key for it and B is Build mode there, so C.
        /// </summary>
        public const string Shop = "shop";

        /// <summary>Build mode (doc 09 §4: B).</summary>
        public const string Build = "build";
        /// <summary>The map overlay (doc 09 §4: M).</summary>
        public const string Map = "map";

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
            BindKey(Go, Key.G);
            BindKey(Rotate, Key.R);
            BindKey(Use, Key.Q);
            BindKey(Shop, Key.C);
            BindKey(Build, Key.B);
            BindKey(Map, Key.M);
            BindMouse(Attack, MouseButton.Left);
            BindMouse(AltAttack, MouseButton.Right);

            BindAxis(MoveForward, JoyAxis.LeftY, -1f);
            BindAxis(MoveBack, JoyAxis.LeftY, 1f);
            BindAxis(MoveLeft, JoyAxis.LeftX, -1f);
            BindAxis(MoveRight, JoyAxis.LeftX, 1f);
            BindButton(Sprint, JoyButton.LeftStick);
            BindButton(Jump, JoyButton.A);
            BindButton(Interact, JoyButton.X);
            BindButton(Inventory, JoyButton.Y);
            BindButton(Pause, JoyButton.Start);
            BindButton(Go, JoyButton.Back);
            BindButton(Use, JoyButton.B);
            BindButton(Shop, JoyButton.DpadUp);
            BindButton(Build, JoyButton.LeftShoulder);
            BindButton(Rotate, JoyButton.RightShoulder);
            BindButton(Map, JoyButton.DpadDown);
            BindAxis(Attack, JoyAxis.TriggerRight, 1f);
            BindAxis(AltAttack, JoyAxis.TriggerLeft, 1f);
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

        private static void BindMouse(string action, MouseButton button)
        {
            if (Ensure(action)) InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
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
