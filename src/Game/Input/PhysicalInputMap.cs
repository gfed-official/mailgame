using Godot;
using PerformativeMail.Client.Core.Input;

namespace PerformativeMail.Game.Input;

/// <summary>
/// Godot input codes to Client.Core's stable <see cref="PhysicalInput"/> ids (ADR-0012 section 7, "Physical keys"): physical
/// keycodes, mouse buttons (wheel up / down included), joypad buttons and joypad axes, for every input the M-1a built-in map
/// uses. Array tables built once, no hashing; a code outside a table, or with no entry, maps to <see cref="PhysicalInput.None"/>
/// and the adapter ignores and counts it. Keys use two tables: printable codes below 128 and the <see cref="Key.Special"/>
/// block, indexed by the offset into it.
/// </summary>
/// <example><code>PhysicalInput w = PhysicalInputMap.FromKey(keyEvent.PhysicalKeycode);   // KeyW on QWERTY and AZERTY alike</code></example>
internal static class PhysicalInputMap
{
    private const int PrintableCount = 128;
    private const int SpecialCount = 512;

    private static readonly PhysicalInput[] _printable = new PhysicalInput[PrintableCount];
    private static readonly PhysicalInput[] _special = new PhysicalInput[SpecialCount];
    private static readonly PhysicalInput[] _mouse = new PhysicalInput[16];
    private static readonly PhysicalInput[] _joyButtons = new PhysicalInput[(int)JoyButton.SdlMax];
    private static readonly PhysicalInput[] _joyAxes = new PhysicalInput[JoyAxisCache.AxisCount];

    static PhysicalInputMap()
    {
        Key(Godot.Key.W, PhysicalInput.KeyW);
        Key(Godot.Key.A, PhysicalInput.KeyA);
        Key(Godot.Key.S, PhysicalInput.KeyS);
        Key(Godot.Key.D, PhysicalInput.KeyD);
        Key(Godot.Key.Up, PhysicalInput.KeyUp);
        Key(Godot.Key.Down, PhysicalInput.KeyDown);
        Key(Godot.Key.Left, PhysicalInput.KeyLeft);
        Key(Godot.Key.Right, PhysicalInput.KeyRight);
        Key(Godot.Key.Shift, PhysicalInput.KeyShift);
        Key(Godot.Key.Space, PhysicalInput.KeySpace);
        Key(Godot.Key.E, PhysicalInput.KeyE);
        Key(Godot.Key.Q, PhysicalInput.KeyQ);
        Key(Godot.Key.R, PhysicalInput.KeyR);
        Key(Godot.Key.F, PhysicalInput.KeyF);
        Key(Godot.Key.P, PhysicalInput.KeyP);
        Key(Godot.Key.X, PhysicalInput.KeyX);
        Key(Godot.Key.Tab, PhysicalInput.KeyTab);
        Key(Godot.Key.Escape, PhysicalInput.KeyEscape);
        Key(Godot.Key.Enter, PhysicalInput.KeyEnter);
        Key(Godot.Key.KpEnter, PhysicalInput.KeyNumpadEnter);
        Key(Godot.Key.Key1, PhysicalInput.KeyDigit1);
        Key(Godot.Key.Key2, PhysicalInput.KeyDigit2);
        Key(Godot.Key.Key3, PhysicalInput.KeyDigit3);
        Key(Godot.Key.Key4, PhysicalInput.KeyDigit4);
        Key(Godot.Key.Key5, PhysicalInput.KeyDigit5);
        Key(Godot.Key.Key6, PhysicalInput.KeyDigit6);
        Key(Godot.Key.Key7, PhysicalInput.KeyDigit7);
        Key(Godot.Key.Key8, PhysicalInput.KeyDigit8);

        _mouse[(int)MouseButton.Left] = PhysicalInput.MouseLeft;
        _mouse[(int)MouseButton.Middle] = PhysicalInput.MouseMiddle;
        _mouse[(int)MouseButton.Right] = PhysicalInput.MouseRight;
        _mouse[(int)MouseButton.WheelUp] = PhysicalInput.WheelUp;
        _mouse[(int)MouseButton.WheelDown] = PhysicalInput.WheelDown;

        _joyButtons[(int)JoyButton.A] = PhysicalInput.PadA;
        _joyButtons[(int)JoyButton.B] = PhysicalInput.PadB;
        _joyButtons[(int)JoyButton.X] = PhysicalInput.PadX;
        _joyButtons[(int)JoyButton.Y] = PhysicalInput.PadY;
        _joyButtons[(int)JoyButton.LeftShoulder] = PhysicalInput.PadLeftBumper;
        _joyButtons[(int)JoyButton.RightShoulder] = PhysicalInput.PadRightBumper;
        _joyButtons[(int)JoyButton.LeftStick] = PhysicalInput.PadLeftStickClick;
        _joyButtons[(int)JoyButton.RightStick] = PhysicalInput.PadRightStickClick;
        _joyButtons[(int)JoyButton.DpadUp] = PhysicalInput.PadDpadUp;
        _joyButtons[(int)JoyButton.DpadDown] = PhysicalInput.PadDpadDown;
        _joyButtons[(int)JoyButton.DpadLeft] = PhysicalInput.PadDpadLeft;
        _joyButtons[(int)JoyButton.DpadRight] = PhysicalInput.PadDpadRight;
        _joyButtons[(int)JoyButton.Start] = PhysicalInput.PadMenu;
        _joyButtons[(int)JoyButton.Back] = PhysicalInput.PadView;

        _joyAxes[(int)JoyAxis.LeftX] = PhysicalInput.PadLeftStickX;
        _joyAxes[(int)JoyAxis.LeftY] = PhysicalInput.PadLeftStickY;
        _joyAxes[(int)JoyAxis.RightX] = PhysicalInput.PadRightStickX;
        _joyAxes[(int)JoyAxis.RightY] = PhysicalInput.PadRightStickY;
        _joyAxes[(int)JoyAxis.TriggerLeft] = PhysicalInput.PadLeftTrigger;
        _joyAxes[(int)JoyAxis.TriggerRight] = PhysicalInput.PadRightTrigger;
    }

    /// <summary>The input of a physical keycode, or <see cref="PhysicalInput.None"/>.</summary>
    /// <param name="physicalKeycode">The event's <c>PhysicalKeycode</c> (layout-independent).</param>
    /// <returns>The input.</returns>
    public static PhysicalInput FromKey(Key physicalKeycode)
    {
        long code = (long)physicalKeycode;
        if (code is >= 0 and < PrintableCount)
        {
            return _printable[code];
        }

        long offset = code - (long)Godot.Key.Special;
        return offset is >= 0 and < SpecialCount ? _special[offset] : PhysicalInput.None;
    }

    /// <summary>The input of a mouse button, or <see cref="PhysicalInput.None"/>.</summary>
    /// <param name="button">The button index.</param>
    /// <returns>The input.</returns>
    public static PhysicalInput FromMouseButton(MouseButton button) => Lookup(_mouse, (long)button);

    /// <summary>The input of a joypad button, or <see cref="PhysicalInput.None"/>.</summary>
    /// <param name="button">The button index.</param>
    /// <returns>The input.</returns>
    public static PhysicalInput FromJoyButton(JoyButton button) => Lookup(_joyButtons, (long)button);

    /// <summary>The input of a joypad axis, or <see cref="PhysicalInput.None"/>.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The input.</returns>
    public static PhysicalInput FromJoyAxis(JoyAxis axis) => Lookup(_joyAxes, (long)axis);

    /// <summary>Every input some Godot code maps to, for the adapter smoke's coverage row. Allocates.</summary>
    /// <returns>The mapped inputs, distinct.</returns>
    public static HashSet<PhysicalInput> AllMapped()
    {
        var all = new HashSet<PhysicalInput>();
        foreach (PhysicalInput[] table in new[] { _printable, _special, _mouse, _joyButtons, _joyAxes })
        {
            foreach (PhysicalInput p in table)
            {
                if (p != PhysicalInput.None)
                {
                    all.Add(p);
                }
            }
        }

        return all;
    }

    private static PhysicalInput Lookup(PhysicalInput[] table, long index) =>
        index >= 0 && index < table.Length ? table[index] : PhysicalInput.None;

    private static void Key(Key code, PhysicalInput input)
    {
        long value = (long)code;
        if (value is >= 0 and < PrintableCount)
        {
            _printable[value] = input;
            return;
        }

        long offset = value - (long)Godot.Key.Special;
        if (offset is < 0 or >= SpecialCount)
        {
            throw new InvalidOperationException($"Key {code} is outside the adapter's key tables.");
        }

        _special[offset] = input;
    }
}
