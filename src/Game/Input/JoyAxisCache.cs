using Godot;

namespace PerformativeMail.Game.Input;

/// <summary>
/// The per-device joypad axis cache (ADR-0012 section 7 and Step 5.5 note 5, TR-input-016): Godot sends one axis per
/// <c>InputEventJoypadMotion</c>, so the last Q15 value of every <c>(pad, axis)</c> is kept here and a stick sample is fed
/// to the router as its x and y pair. The only polled-style state of the adapter; re-seeded from <c>Input.GetJoyAxis</c> on
/// focus-in and reconnect. Pads are indexed only after the event is classified as a joypad event, and indices from
/// <see cref="MaxPads"/> up are not cached. Preallocated; 0 B per call.
/// </summary>
/// <example>
/// <code>
/// cache.Store(0, JoyAxis.LeftX, JoyAxisCache.ToQ15(0.5f));
/// short y = cache.Get(0, JoyAxis.LeftY);   // the last LeftY sample, 0 before the first
/// </code>
/// </example>
internal sealed class JoyAxisCache
{
    /// <summary>Joypad indices cached (0 to 63; Steam virtual pads were seen at 16 and 32 by the spike, S-11).</summary>
    public const int MaxPads = 64;

    /// <summary>Axes per pad: both sticks and both triggers (<see cref="JoyAxis.LeftX"/> to <see cref="JoyAxis.TriggerRight"/>).</summary>
    public const int AxisCount = (int)JoyAxis.TriggerRight + 1;

    private readonly short[] _values = new short[MaxPads * AxisCount];

    /// <summary>True when <paramref name="axis"/> is one of the cached axes.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>Whether it is cached.</returns>
    public static bool IsCached(JoyAxis axis) => axis >= JoyAxis.LeftX && axis <= JoyAxis.TriggerRight;

    /// <summary>The other axis of the stick <paramref name="axis"/> belongs to, or <see cref="JoyAxis.Invalid"/> for a trigger.</summary>
    /// <param name="axis">A cached axis.</param>
    /// <returns>The partner axis.</returns>
    public static JoyAxis PartnerOf(JoyAxis axis) => axis switch
    {
        JoyAxis.LeftX => JoyAxis.LeftY,
        JoyAxis.LeftY => JoyAxis.LeftX,
        JoyAxis.RightX => JoyAxis.RightY,
        JoyAxis.RightY => JoyAxis.RightX,
        _ => JoyAxis.Invalid,
    };

    /// <summary>Converts Godot's raw axis value (−1 to 1, no deadzone) to Q15, rounded to nearest and clamped to ±32767.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The Q15 value.</returns>
    public static short ToQ15(float value)
    {
        if (float.IsNaN(value))
        {
            return 0;
        }

        float clamped = Math.Clamp(value, -1f, 1f);
        return (short)MathF.Round(clamped * short.MaxValue, MidpointRounding.AwayFromZero);
    }

    /// <summary>The last value of <paramref name="axis"/> on <paramref name="pad"/>, Q15 (0 before the first sample).</summary>
    /// <param name="pad">A pad index below <see cref="MaxPads"/>.</param>
    /// <param name="axis">A cached axis.</param>
    /// <returns>The value.</returns>
    public short Get(int pad, JoyAxis axis) => _values[Slot(pad, axis)];

    /// <summary>Stores a sample.</summary>
    /// <param name="pad">A pad index below <see cref="MaxPads"/>.</param>
    /// <param name="axis">A cached axis.</param>
    /// <param name="q15">The value, Q15.</param>
    public void Store(int pad, JoyAxis axis, short q15) => _values[Slot(pad, axis)] = q15;

    private static int Slot(int pad, JoyAxis axis) => (pad * AxisCount) + (int)axis;
}
