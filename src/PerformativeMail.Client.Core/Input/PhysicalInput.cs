namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Every M-1a physical input the R6 default binding tables use, as a stable id (ADR-0012 section 1). Persisted settings store
/// the <see cref="PhysicalInputNames"/> name, never this number and never a Godot enum int, so a renumber cannot corrupt a
/// player's bindings; the numbers are still pinned by a golden test so a change is a deliberate edit. Blocks: keyboard
/// <c>0x01xx</c>, mouse <c>0x02xx</c>, gamepad <c>0x03xx</c>. Values are append-only; <c>0</c> is "no input". Axes
/// (<c>PadLeftStickX</c> and friends, the triggers) arrive as <see cref="RawKind.PadAxis"/> records.
/// </summary>
/// <example>
/// <code>
/// PhysicalInput w = PhysicalInput.KeyW;
/// bool ok = PhysicalInputNames.TryParse("Key.W", out PhysicalInput parsed) &amp;&amp; parsed == w;
/// </code>
/// </example>
public enum PhysicalInput : ushort
{
    /// <summary>No input. Never a valid binding.</summary>
    None = 0x0000,

    /// <summary>W. Persisted as <c>Key.W</c>.</summary>
    KeyW = 0x0100,

    /// <summary>A. Persisted as <c>Key.A</c>.</summary>
    KeyA = 0x0101,

    /// <summary>S. Persisted as <c>Key.S</c>.</summary>
    KeyS = 0x0102,

    /// <summary>D. Persisted as <c>Key.D</c>.</summary>
    KeyD = 0x0103,

    /// <summary>Up arrow. Persisted as <c>Key.Up</c>.</summary>
    KeyUp = 0x0104,

    /// <summary>Down arrow. Persisted as <c>Key.Down</c>.</summary>
    KeyDown = 0x0105,

    /// <summary>Left arrow. Persisted as <c>Key.Left</c>.</summary>
    KeyLeft = 0x0106,

    /// <summary>Right arrow. Persisted as <c>Key.Right</c>.</summary>
    KeyRight = 0x0107,

    /// <summary>Shift (either side). Persisted as <c>Key.Shift</c>.</summary>
    KeyShift = 0x0108,

    /// <summary>Space. Persisted as <c>Key.Space</c>.</summary>
    KeySpace = 0x0109,

    /// <summary>E. Persisted as <c>Key.E</c>.</summary>
    KeyE = 0x010A,

    /// <summary>Q. Persisted as <c>Key.Q</c>.</summary>
    KeyQ = 0x010B,

    /// <summary>R. Persisted as <c>Key.R</c>.</summary>
    KeyR = 0x010C,

    /// <summary>F. Persisted as <c>Key.F</c>.</summary>
    KeyF = 0x010D,

    /// <summary>P. Persisted as <c>Key.P</c>.</summary>
    KeyP = 0x010E,

    /// <summary>X. Persisted as <c>Key.X</c>.</summary>
    KeyX = 0x010F,

    /// <summary>Tab. Persisted as <c>Key.Tab</c>.</summary>
    KeyTab = 0x0110,

    /// <summary>Escape. Persisted as <c>Key.Esc</c>.</summary>
    KeyEscape = 0x0111,

    /// <summary>Enter (main). Persisted as <c>Key.Enter</c>.</summary>
    KeyEnter = 0x0112,

    /// <summary>Numpad Enter. Persisted as <c>Key.NumpadEnter</c>.</summary>
    KeyNumpadEnter = 0x0113,

    /// <summary>Digit 1 (top row). Persisted as <c>Key.1</c>.</summary>
    KeyDigit1 = 0x0114,

    /// <summary>Digit 2 (top row). Persisted as <c>Key.2</c>.</summary>
    KeyDigit2 = 0x0115,

    /// <summary>Digit 3 (top row). Persisted as <c>Key.3</c>.</summary>
    KeyDigit3 = 0x0116,

    /// <summary>Digit 4 (top row). Persisted as <c>Key.4</c>.</summary>
    KeyDigit4 = 0x0117,

    /// <summary>Digit 5 (top row). Persisted as <c>Key.5</c>.</summary>
    KeyDigit5 = 0x0118,

    /// <summary>Digit 6 (top row). Persisted as <c>Key.6</c>.</summary>
    KeyDigit6 = 0x0119,

    /// <summary>Digit 7 (top row). Persisted as <c>Key.7</c>.</summary>
    KeyDigit7 = 0x011A,

    /// <summary>Digit 8 (top row). Persisted as <c>Key.8</c>.</summary>
    KeyDigit8 = 0x011B,


    /// <summary>Left mouse button. Persisted as <c>Mouse.Left</c>.</summary>
    MouseLeft = 0x0200,

    /// <summary>Middle mouse button. Persisted as <c>Mouse.Middle</c>.</summary>
    MouseMiddle = 0x0201,

    /// <summary>Right mouse button. Persisted as <c>Mouse.Right</c>.</summary>
    MouseRight = 0x0202,

    /// <summary>Wheel notch up. Persisted as <c>Mouse.WheelUp</c>.</summary>
    WheelUp = 0x0203,

    /// <summary>Wheel notch down. Persisted as <c>Mouse.WheelDown</c>.</summary>
    WheelDown = 0x0204,

    /// <summary>Mouse motion (relative X and Y; a 2D device input, not a button). Persisted as <c>Mouse.Motion</c>. Appended for the Look and Grid cursor defaults.</summary>
    MouseMotion = 0x0205,


    /// <summary>Gamepad A (south face button). Persisted as <c>Pad.A</c>.</summary>
    PadA = 0x0300,

    /// <summary>Gamepad B (east face button). Persisted as <c>Pad.B</c>.</summary>
    PadB = 0x0301,

    /// <summary>Gamepad X (west face button). Persisted as <c>Pad.X</c>.</summary>
    PadX = 0x0302,

    /// <summary>Gamepad Y (north face button). Persisted as <c>Pad.Y</c>.</summary>
    PadY = 0x0303,

    /// <summary>Left bumper. Persisted as <c>Pad.LB</c>.</summary>
    PadLeftBumper = 0x0304,

    /// <summary>Right bumper. Persisted as <c>Pad.RB</c>.</summary>
    PadRightBumper = 0x0305,

    /// <summary>Left stick click. Persisted as <c>Pad.L3</c>.</summary>
    PadLeftStickClick = 0x0306,

    /// <summary>Right stick click. Persisted as <c>Pad.R3</c>.</summary>
    PadRightStickClick = 0x0307,

    /// <summary>D-pad up. Persisted as <c>Pad.DpadUp</c>.</summary>
    PadDpadUp = 0x0308,

    /// <summary>D-pad down. Persisted as <c>Pad.DpadDown</c>.</summary>
    PadDpadDown = 0x0309,

    /// <summary>D-pad left. Persisted as <c>Pad.DpadLeft</c>.</summary>
    PadDpadLeft = 0x030A,

    /// <summary>D-pad right. Persisted as <c>Pad.DpadRight</c>.</summary>
    PadDpadRight = 0x030B,

    /// <summary>Menu (start) button. Persisted as <c>Pad.Menu</c>.</summary>
    PadMenu = 0x030C,

    /// <summary>View (back/select) button. Persisted as <c>Pad.View</c>.</summary>
    PadView = 0x030D,

    /// <summary>Left stick X axis (Q15). Persisted as <c>Pad.LeftStickX</c>.</summary>
    PadLeftStickX = 0x030E,

    /// <summary>Left stick Y axis (Q15). Persisted as <c>Pad.LeftStickY</c>.</summary>
    PadLeftStickY = 0x030F,

    /// <summary>Right stick X axis (Q15). Persisted as <c>Pad.RightStickX</c>.</summary>
    PadRightStickX = 0x0310,

    /// <summary>Right stick Y axis (Q15). Persisted as <c>Pad.RightStickY</c>.</summary>
    PadRightStickY = 0x0311,

    /// <summary>Left trigger axis (Q15, 0 to 32767). Persisted as <c>Pad.LT</c>.</summary>
    PadLeftTrigger = 0x0312,

    /// <summary>Right trigger axis (Q15, 0 to 32767). Persisted as <c>Pad.RT</c>.</summary>
    PadRightTrigger = 0x0313,
}
