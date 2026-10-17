namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// Device-class helpers over <see cref="PhysicalInput"/> (blocks: keyboard <c>0x01xx</c>, mouse <c>0x02xx</c>, gamepad
/// <c>0x03xx</c>). Based on the block, so any value in a block counts, defined or not; <see cref="PhysicalInput.None"/> is neither.
/// Pure; thread-safe.
/// </summary>
/// <example>
/// <code>
/// bool pad = PhysicalInputs.IsGamepad(PhysicalInput.PadA);                    // true
/// bool kbm = PhysicalInputs.IsKeyboardMouse(PhysicalInput.MouseLeft);         // true
/// </code>
/// </example>
public static class PhysicalInputs
{
    /// <summary>True for a gamepad input (block <c>0x03xx</c>).</summary>
    /// <param name="input">The input.</param>
    /// <returns>Whether it is a gamepad input.</returns>
    /// <example><code>bool pad = PhysicalInputs.IsGamepad(PhysicalInput.PadMenu);</code></example>
    public static bool IsGamepad(PhysicalInput input) => ((ushort)input >> 8) == 0x03;

    /// <summary>True for a keyboard or mouse input (blocks <c>0x01xx</c> and <c>0x02xx</c>).</summary>
    /// <param name="input">The input.</param>
    /// <returns>Whether it is a keyboard or mouse input.</returns>
    /// <example><code>bool kbm = PhysicalInputs.IsKeyboardMouse(PhysicalInput.KeyW);</code></example>
    public static bool IsKeyboardMouse(PhysicalInput input)
    {
        int block = (ushort)input >> 8;
        return block == 0x01 || block == 0x02;
    }
}
