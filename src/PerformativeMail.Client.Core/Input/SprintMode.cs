using PerformativeMail.Client.Core.Input.Settings;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The Sprint hold class per device (story 012; GDD R6 Sprint row, G7, R10): keyboard and mouse read
/// <see cref="InputSettings.HoldModeSprintKbm"/> (default Hold), a pad <see cref="InputSettings.HoldModeSprintPad"/> (default
/// Toggle). Auto (Player Character P14) sprints whenever <c>|moveIntent| ≥ 0.9</c>.
/// </summary>
internal static class SprintMode
{
    /// <summary>The mode of a press on device-slot table entry <paramref name="index"/>.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="index">A <see cref="DeviceSlots"/> index.</param>
    /// <returns>The mode.</returns>
    public static HoldMode OfSlot(InputSettings settings, int index) => Of(settings, IsPadSlot(index));

    /// <summary>The mode of the keyboard and mouse (<paramref name="pad"/> false) or the pads.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="pad">True for a pad.</param>
    /// <returns>The mode.</returns>
    public static HoldMode Of(InputSettings settings, bool pad) => pad ? settings.HoldModeSprintPad : settings.HoldModeSprintKbm;

    /// <summary>True when table entry <paramref name="index"/> belongs to a pad.</summary>
    /// <param name="index">A <see cref="DeviceSlots"/> index.</param>
    /// <returns>True for a pad slot.</returns>
    public static bool IsPadSlot(int index) => index >= 2 * DeviceSlots.InputsPerSlot;

    /// <summary>True when the Q15 move vector reaches the Auto threshold (compared squared, integer only).</summary>
    /// <param name="x">Move x, Q15.</param>
    /// <param name="y">Move y, Q15.</param>
    /// <returns>True at or above <see cref="InputDefaults.SprintAutoMinQ15"/>.</returns>
    public static bool ReachesAuto(short x, short y) =>
        ((long)x * x) + ((long)y * y) >= (long)InputDefaults.SprintAutoMinQ15 * InputDefaults.SprintAutoMinQ15;
}
