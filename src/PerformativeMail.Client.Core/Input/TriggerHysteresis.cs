using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// GDD F4: an analog trigger's press with hysteresis, in integer permille against a Q15 value (ADR-0012 section 5).
/// <c>pressed_t = pressed_(t−1) ? v ≥ max(trigOn − trigHyst, floor) : v ≥ trigOn</c>, with <c>trigHyst</c> and the floor from
/// <see cref="InputDefaults"/>. A digital trigger (0 or 1.0) works at any setting. Pure, 0 B.
/// </summary>
/// <example><code>bool down = TriggerHysteresis.IsPressed(wasDown, record.Value, settings.TriggerOnPm);</code></example>
internal static class TriggerHysteresis
{
    /// <summary>The trigger's pressed state after a sample.</summary>
    /// <param name="wasPressed">The state before this sample.</param>
    /// <param name="vQ15">The trigger value, Q15 (0 at rest).</param>
    /// <param name="trigOnPm">The press point, permille.</param>
    /// <returns>True when the trigger counts as pressed.</returns>
    public static bool IsPressed(bool wasPressed, short vQ15, int trigOnPm)
    {
        int thresholdPm = wasPressed ? Math.Max(trigOnPm - InputDefaults.TrigHystPm, InputDefaults.TrigFloorPm) : trigOnPm;
        return (long)vQ15 * 1000 >= (long)thresholdPm * TrigQ15.One;
    }
}
