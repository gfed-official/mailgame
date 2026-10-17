namespace PerformativeMail.Client.Core.Input;

/// <summary>One row of the <see cref="PhysicalInputNames"/> table: a persisted name and its <see cref="PhysicalInput"/>.</summary>
/// <param name="Name">The stable persisted name, for example <c>Key.W</c>. Ordinal, case-sensitive.</param>
/// <param name="Input">The input it names.</param>
public readonly record struct PhysicalInputName(string Name, PhysicalInput Input);

/// <summary>
/// The stable-name table for <see cref="PhysicalInput"/> and the name lookups (ADR-0012 section 9): names are the persistence
/// format for bindings. Names are ordinal and case-sensitive, <c>Group.Item</c> shaped, and never change once shipped.
/// Lookups are linear over about fifty rows and allocate 0 B; they run on settings load and glyph resolution, not per event.
/// Immutable after type initialisation; safe to read from any thread.
/// </summary>
/// <example>
/// <code>
/// if (PhysicalInputNames.TryParse("Pad.LB", out PhysicalInput lb)) { /* lb == PhysicalInput.PadLeftBumper */ }
/// bool named = PhysicalInputNames.TryGetName(PhysicalInput.MouseLeft, out string name); // name == "Mouse.Left"
/// foreach (PhysicalInputName row in PhysicalInputNames.Table) { /* save or list every input */ }
/// </code>
/// </example>
public static class PhysicalInputNames
{
    private static readonly PhysicalInputName[] _table =
    [
        new("Key.W", PhysicalInput.KeyW),
        new("Key.A", PhysicalInput.KeyA),
        new("Key.S", PhysicalInput.KeyS),
        new("Key.D", PhysicalInput.KeyD),
        new("Key.Up", PhysicalInput.KeyUp),
        new("Key.Down", PhysicalInput.KeyDown),
        new("Key.Left", PhysicalInput.KeyLeft),
        new("Key.Right", PhysicalInput.KeyRight),
        new("Key.Shift", PhysicalInput.KeyShift),
        new("Key.Space", PhysicalInput.KeySpace),
        new("Key.E", PhysicalInput.KeyE),
        new("Key.Q", PhysicalInput.KeyQ),
        new("Key.R", PhysicalInput.KeyR),
        new("Key.F", PhysicalInput.KeyF),
        new("Key.P", PhysicalInput.KeyP),
        new("Key.X", PhysicalInput.KeyX),
        new("Key.Tab", PhysicalInput.KeyTab),
        new("Key.Esc", PhysicalInput.KeyEscape),
        new("Key.Enter", PhysicalInput.KeyEnter),
        new("Key.NumpadEnter", PhysicalInput.KeyNumpadEnter),
        new("Key.1", PhysicalInput.KeyDigit1),
        new("Key.2", PhysicalInput.KeyDigit2),
        new("Key.3", PhysicalInput.KeyDigit3),
        new("Key.4", PhysicalInput.KeyDigit4),
        new("Key.5", PhysicalInput.KeyDigit5),
        new("Key.6", PhysicalInput.KeyDigit6),
        new("Key.7", PhysicalInput.KeyDigit7),
        new("Key.8", PhysicalInput.KeyDigit8),
        new("Mouse.Left", PhysicalInput.MouseLeft),
        new("Mouse.Middle", PhysicalInput.MouseMiddle),
        new("Mouse.Right", PhysicalInput.MouseRight),
        new("Mouse.WheelUp", PhysicalInput.WheelUp),
        new("Mouse.WheelDown", PhysicalInput.WheelDown),
        new("Mouse.Motion", PhysicalInput.MouseMotion),
        new("Pad.A", PhysicalInput.PadA),
        new("Pad.B", PhysicalInput.PadB),
        new("Pad.X", PhysicalInput.PadX),
        new("Pad.Y", PhysicalInput.PadY),
        new("Pad.LB", PhysicalInput.PadLeftBumper),
        new("Pad.RB", PhysicalInput.PadRightBumper),
        new("Pad.L3", PhysicalInput.PadLeftStickClick),
        new("Pad.R3", PhysicalInput.PadRightStickClick),
        new("Pad.DpadUp", PhysicalInput.PadDpadUp),
        new("Pad.DpadDown", PhysicalInput.PadDpadDown),
        new("Pad.DpadLeft", PhysicalInput.PadDpadLeft),
        new("Pad.DpadRight", PhysicalInput.PadDpadRight),
        new("Pad.Menu", PhysicalInput.PadMenu),
        new("Pad.View", PhysicalInput.PadView),
        new("Pad.LeftStickX", PhysicalInput.PadLeftStickX),
        new("Pad.LeftStickY", PhysicalInput.PadLeftStickY),
        new("Pad.RightStickX", PhysicalInput.PadRightStickX),
        new("Pad.RightStickY", PhysicalInput.PadRightStickY),
        new("Pad.LT", PhysicalInput.PadLeftTrigger),
        new("Pad.RT", PhysicalInput.PadRightTrigger),
    ];

    /// <summary>Every named input, in numeric order. <see cref="PhysicalInput.None"/> has no name.</summary>
    public static ReadOnlySpan<PhysicalInputName> Table => _table;

    /// <summary>Looks up an input by its persisted name.</summary>
    /// <param name="name">The name, for example <c>Key.W</c>. Case-sensitive.</param>
    /// <param name="input">The input when found; <see cref="PhysicalInput.None"/> otherwise.</param>
    /// <returns>True when <paramref name="name"/> is a known name.</returns>
    /// <example><code>bool known = PhysicalInputNames.TryParse("Key.Space".AsSpan(), out PhysicalInput i);</code></example>
    public static bool TryParse(ReadOnlySpan<char> name, out PhysicalInput input)
    {
        foreach (PhysicalInputName row in _table)
        {
            if (name.SequenceEqual(row.Name.AsSpan()))
            {
                input = row.Input;
                return true;
            }
        }

        input = PhysicalInput.None;
        return false;
    }

    /// <summary>Looks up an input by its persisted name; a null name is unknown.</summary>
    /// <param name="name">The name, or null.</param>
    /// <param name="input">The input when found; <see cref="PhysicalInput.None"/> otherwise.</param>
    /// <returns>True when <paramref name="name"/> is a known name.</returns>
    /// <example><code>bool known = PhysicalInputNames.TryParse("Mouse.Right", out PhysicalInput i);</code></example>
    public static bool TryParse(string? name, out PhysicalInput input)
    {
        if (name is null)
        {
            input = PhysicalInput.None;
            return false;
        }

        return TryParse(name.AsSpan(), out input);
    }

    /// <summary>Gets the persisted name of an input.</summary>
    /// <param name="input">The input.</param>
    /// <param name="name">The name when found; the empty string otherwise.</param>
    /// <returns>True when <paramref name="input"/> has a name (false for <see cref="PhysicalInput.None"/> and undefined values).</returns>
    /// <example><code>if (PhysicalInputNames.TryGetName(PhysicalInput.PadA, out string n)) { /* n == "Pad.A" */ }</code></example>
    public static bool TryGetName(PhysicalInput input, out string name)
    {
        foreach (PhysicalInputName row in _table)
        {
            if (row.Input == input)
            {
                name = row.Name;
                return true;
            }
        }

        name = string.Empty;
        return false;
    }
}
