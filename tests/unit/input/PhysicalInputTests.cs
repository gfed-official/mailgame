using PerformativeMail.Client.Core.Input;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 003 AC-2: <see cref="PhysicalInput"/> numeric values are pinned by a golden table (a renumber is a
/// deliberate edit of this file), names are the persistence format and round-trip, and an unknown name is rejected.
/// </summary>
[Collection(NonParallelCollection.Name)]
[Trait("Id", "TR-input-001")]
public class PhysicalInputTests
{
    /// <summary>Every input with its pinned number and persisted name. Edit deliberately: persisted bindings depend on the names.</summary>
    private static readonly (PhysicalInput Input, ushort Value, string Name)[] _golden =
    [
        (PhysicalInput.KeyW, 0x0100, "Key.W"),
        (PhysicalInput.KeyA, 0x0101, "Key.A"),
        (PhysicalInput.KeyS, 0x0102, "Key.S"),
        (PhysicalInput.KeyD, 0x0103, "Key.D"),
        (PhysicalInput.KeyUp, 0x0104, "Key.Up"),
        (PhysicalInput.KeyDown, 0x0105, "Key.Down"),
        (PhysicalInput.KeyLeft, 0x0106, "Key.Left"),
        (PhysicalInput.KeyRight, 0x0107, "Key.Right"),
        (PhysicalInput.KeyShift, 0x0108, "Key.Shift"),
        (PhysicalInput.KeySpace, 0x0109, "Key.Space"),
        (PhysicalInput.KeyE, 0x010A, "Key.E"),
        (PhysicalInput.KeyQ, 0x010B, "Key.Q"),
        (PhysicalInput.KeyR, 0x010C, "Key.R"),
        (PhysicalInput.KeyF, 0x010D, "Key.F"),
        (PhysicalInput.KeyP, 0x010E, "Key.P"),
        (PhysicalInput.KeyX, 0x010F, "Key.X"),
        (PhysicalInput.KeyTab, 0x0110, "Key.Tab"),
        (PhysicalInput.KeyEscape, 0x0111, "Key.Esc"),
        (PhysicalInput.KeyEnter, 0x0112, "Key.Enter"),
        (PhysicalInput.KeyNumpadEnter, 0x0113, "Key.NumpadEnter"),
        (PhysicalInput.KeyDigit1, 0x0114, "Key.1"),
        (PhysicalInput.KeyDigit2, 0x0115, "Key.2"),
        (PhysicalInput.KeyDigit3, 0x0116, "Key.3"),
        (PhysicalInput.KeyDigit4, 0x0117, "Key.4"),
        (PhysicalInput.KeyDigit5, 0x0118, "Key.5"),
        (PhysicalInput.KeyDigit6, 0x0119, "Key.6"),
        (PhysicalInput.KeyDigit7, 0x011A, "Key.7"),
        (PhysicalInput.KeyDigit8, 0x011B, "Key.8"),
        (PhysicalInput.MouseLeft, 0x0200, "Mouse.Left"),
        (PhysicalInput.MouseMiddle, 0x0201, "Mouse.Middle"),
        (PhysicalInput.MouseRight, 0x0202, "Mouse.Right"),
        (PhysicalInput.WheelUp, 0x0203, "Mouse.WheelUp"),
        (PhysicalInput.WheelDown, 0x0204, "Mouse.WheelDown"),
        (PhysicalInput.MouseMotion, 0x0205, "Mouse.Motion"),
        (PhysicalInput.PadA, 0x0300, "Pad.A"),
        (PhysicalInput.PadB, 0x0301, "Pad.B"),
        (PhysicalInput.PadX, 0x0302, "Pad.X"),
        (PhysicalInput.PadY, 0x0303, "Pad.Y"),
        (PhysicalInput.PadLeftBumper, 0x0304, "Pad.LB"),
        (PhysicalInput.PadRightBumper, 0x0305, "Pad.RB"),
        (PhysicalInput.PadLeftStickClick, 0x0306, "Pad.L3"),
        (PhysicalInput.PadRightStickClick, 0x0307, "Pad.R3"),
        (PhysicalInput.PadDpadUp, 0x0308, "Pad.DpadUp"),
        (PhysicalInput.PadDpadDown, 0x0309, "Pad.DpadDown"),
        (PhysicalInput.PadDpadLeft, 0x030A, "Pad.DpadLeft"),
        (PhysicalInput.PadDpadRight, 0x030B, "Pad.DpadRight"),
        (PhysicalInput.PadMenu, 0x030C, "Pad.Menu"),
        (PhysicalInput.PadView, 0x030D, "Pad.View"),
        (PhysicalInput.PadLeftStickX, 0x030E, "Pad.LeftStickX"),
        (PhysicalInput.PadLeftStickY, 0x030F, "Pad.LeftStickY"),
        (PhysicalInput.PadRightStickX, 0x0310, "Pad.RightStickX"),
        (PhysicalInput.PadRightStickY, 0x0311, "Pad.RightStickY"),
        (PhysicalInput.PadLeftTrigger, 0x0312, "Pad.LT"),
        (PhysicalInput.PadRightTrigger, 0x0313, "Pad.RT"),
    ];

    /// <summary>
    /// The R6 default inputs, written independently from story 003 AC-2's list and the GDD R6 tables (not from
    /// <see cref="_golden"/>): Look's "mouse" is <c>Mouse.Motion</c>; MMB / R3 (quick ping) and View (Map) are R6 inputs the
    /// M-1a map does not bind yet, so this list is their only pin outside the golden table.
    /// </summary>
    private static readonly string[] _r6Inputs =
    [
        "Key.W", "Key.A", "Key.S", "Key.D", "Key.Up", "Key.Down", "Key.Left", "Key.Right", "Key.Shift", "Key.Space",
        "Key.E", "Key.Q", "Key.R", "Key.F", "Key.P", "Key.X", "Key.Tab", "Key.Esc", "Key.Enter", "Key.NumpadEnter",
        "Key.1", "Key.2", "Key.3", "Key.4", "Key.5", "Key.6", "Key.7", "Key.8",
        "Mouse.Left", "Mouse.Middle", "Mouse.Right", "Mouse.WheelUp", "Mouse.WheelDown", "Mouse.Motion",
        "Pad.A", "Pad.B", "Pad.X", "Pad.Y", "Pad.LB", "Pad.RB", "Pad.L3", "Pad.R3",
        "Pad.DpadUp", "Pad.DpadDown", "Pad.DpadLeft", "Pad.DpadRight", "Pad.Menu", "Pad.View",
        "Pad.LeftStickX", "Pad.LeftStickY", "Pad.RightStickX", "Pad.RightStickY", "Pad.LT", "Pad.RT",
    ];

    // The three golden pins loop the table in one test each and report every failing row in one message.
    [Fact]
    public void PhysicalInput_Value_MatchesTheGoldenNumber()
    {
        var failures = new List<string>();
        foreach ((PhysicalInput input, ushort value, string name) in _golden)
        {
            if (name.Length == 0 || value != (ushort)input)
            {
                failures.Add($"{name}: expected 0x{value:X4}, got 0x{(ushort)input:X4}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TryParse_WithThePersistedName_ReturnsTheInput()
    {
        var failures = new List<string>();
        foreach ((PhysicalInput input, ushort value, string name) in _golden)
        {
            bool ok = PhysicalInputNames.TryParse(name, out PhysicalInput parsed);

            if (value == 0)
            {
                failures.Add($"{name}: golden value is 0");
            }
            else if (!ok)
            {
                failures.Add($"{name}: TryParse returned false");
            }
            else if (input != parsed)
            {
                failures.Add($"{name}: expected {input}, got {parsed}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TryGetName_WithTheInput_ReturnsThePersistedName()
    {
        var failures = new List<string>();
        foreach ((PhysicalInput input, ushort value, string name) in _golden)
        {
            bool ok = PhysicalInputNames.TryGetName(input, out string actual);

            if (value == 0)
            {
                failures.Add($"{input}: golden value is 0");
            }
            else if (!ok)
            {
                failures.Add($"{input} (0x{value:X4}): TryGetName returned false");
            }
            else if (name != actual)
            {
                failures.Add($"{input} (0x{value:X4}): expected '{name}', got '{actual}'");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Table_ListsExactlyTheGoldenRowsInOrder()
    {
        ReadOnlySpan<PhysicalInputName> table = PhysicalInputNames.Table;

        Assert.Equal(_golden.Length, table.Length);
        for (int i = 0; i < table.Length; i++)
        {
            Assert.Equal(_golden[i].Input, table[i].Input);
            Assert.Equal(_golden[i].Name, table[i].Name);
        }
    }

    [Fact]
    public void Enum_EveryDefinedMemberExceptNone_IsInTheGoldenTable()
    {
        PhysicalInput[] defined = Enum.GetValues<PhysicalInput>();

        Assert.Equal(_golden.Length + 1, defined.Length);
        foreach (PhysicalInput input in defined)
        {
            if (input != PhysicalInput.None)
            {
                Assert.Contains(_golden, g => g.Input == input);
            }
        }

        // The independent R6 list: each entry is a defined input whose persisted name round-trips, and the list is every member.
        var failures = new List<string>();
        foreach (string name in _r6Inputs)
        {
            if (!PhysicalInputNames.TryParse(name, out PhysicalInput input) || !Enum.IsDefined(input))
            {
                failures.Add($"{name}: not a defined PhysicalInput");
            }
            else if (!PhysicalInputNames.TryGetName(input, out string persisted) || persisted != name)
            {
                failures.Add($"{name}: {input} persists as '{persisted}'");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Equal(_r6Inputs.Length, _r6Inputs.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(defined.Length - 1, _r6Inputs.Length);
    }

    [Fact]
    public void Names_AreUniqueAndValuesAreUnique()
    {
        Assert.Equal(_golden.Length, _golden.Select(g => g.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(_golden.Length, _golden.Select(g => g.Value).Distinct().Count());
    }

    [Fact]
    public void None_IsZeroAndHasNoName()
    {
        Assert.Equal(0, (ushort)PhysicalInput.None);
        Assert.False(PhysicalInputNames.TryGetName(PhysicalInput.None, out string name));
        Assert.Equal(string.Empty, name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Key.Nope")]
    [InlineData("key.w")]
    [InlineData("W")]
    [InlineData("Key.W ")]
    [InlineData("None")]
    public void TryParse_WithAnUnknownName_ReturnsFalseAndNone(string name)
    {
        bool ok = PhysicalInputNames.TryParse(name, out PhysicalInput parsed);

        Assert.False(ok);
        Assert.Equal(PhysicalInput.None, parsed);
    }

    [Fact]
    public void TryParse_WithNullString_ReturnsFalse()
    {
        Assert.False(PhysicalInputNames.TryParse((string?)null, out PhysicalInput parsed));
        Assert.Equal(PhysicalInput.None, parsed);
    }

    [Fact]
    public void TryParse_WithASpan_MatchesTheStringOverload()
    {
        Assert.True(PhysicalInputNames.TryParse("Pad.LB".AsSpan(), out PhysicalInput parsed));
        Assert.Equal(PhysicalInput.PadLeftBumper, parsed);
        Assert.False(PhysicalInputNames.TryParse("Pad.LB2".AsSpan(), out _));
    }

    [Fact]
    public void TryGetName_WithAnUndefinedValue_ReturnsFalse()
    {
        Assert.False(PhysicalInputNames.TryGetName((PhysicalInput)0xFFFF, out _));
    }

    [Fact]
    public void Lookups_AllocateNothing()
    {
        // Warm up inside AllocAssert; the lookups are plain loops over a preallocated table.
        PhysicalInput sink = PhysicalInput.None;
        _ = AllocAssert.Zero("PhysicalInputNames.TryParse/TryGetName", () =>
        {
            for (int i = 0; i < 200; i++)
            {
                _ = PhysicalInputNames.TryParse("Pad.RT", out sink);
                _ = PhysicalInputNames.TryGetName(sink, out _);
            }
        });
        Assert.Equal(PhysicalInput.PadRightTrigger, sink);
    }
}
