namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// One abstract raw input event, arrival-stamped by the adapter (ADR-0012 sections 1 and 4). This is the only thing the router
/// core is fed, so every rule is testable headless with a fake clock. Integer fields only, no references. Passed by
/// <c>in</c>; 24 bytes.
/// <para>
/// <see cref="Value"/> and <see cref="Value2"/> are context dependent: a <see cref="RawKind.PadAxis"/> sample carries the axis
/// in Q15 in <see cref="Value"/>; <see cref="RawKind.MouseMotion"/> carries the relative counts X and Y; a
/// <see cref="RawKind.Wheel"/> record carries the notch count in <see cref="Value"/>. Button and key records leave both 0.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var press = new RawInputRecord(clock.NowMs, RawKind.Key, DeviceClass.Keyboard, 0, PhysicalInput.KeyW, pressed: true, echo: false);
/// var stick = new RawInputRecord(clock.NowMs, RawKind.PadAxis, DeviceClass.Pad, 0, PhysicalInput.PadLeftStickX, true, false, value: 16384);
/// router.Feed(in press);
/// </code>
/// </example>
public readonly struct RawInputRecord
{
    /// <summary>Monotonic arrival time in milliseconds (the <see cref="IInputClock"/> domain).</summary>
    public readonly long ArrivalMs;

    /// <summary>What kind of event this is.</summary>
    public readonly RawKind Kind;

    /// <summary>The device class the event came from.</summary>
    public readonly DeviceClass Device;

    /// <summary>Which gamepad, for <see cref="DeviceClass.Pad"/>; 0 otherwise.</summary>
    public readonly byte PadIndex;

    /// <summary>The physical input (stable, persisted by name).</summary>
    public readonly PhysicalInput Input;

    /// <summary>True for a press (or an axis away from rest), false for a release.</summary>
    public readonly bool Pressed;

    /// <summary>True when the OS generated this press as a key repeat; the router ignores echoes.</summary>
    public readonly bool Echo;

    /// <summary>Axis value in Q15, mouse relative X counts, or wheel notches (see the type remarks).</summary>
    public readonly short Value;

    /// <summary>Mouse relative Y counts; 0 otherwise.</summary>
    public readonly short Value2;

    /// <summary>Creates a record.</summary>
    /// <param name="arrivalMs">Monotonic arrival time in milliseconds.</param>
    /// <param name="kind">The event kind.</param>
    /// <param name="device">The device class.</param>
    /// <param name="padIndex">The gamepad index, or 0.</param>
    /// <param name="input">The physical input.</param>
    /// <param name="pressed">True for press, false for release.</param>
    /// <param name="echo">True for an OS key repeat.</param>
    /// <param name="value">Axis Q15, mouse X counts or wheel notches.</param>
    /// <param name="value2">Mouse Y counts.</param>
    /// <example><code>var r = new RawInputRecord(0, RawKind.Key, DeviceClass.Keyboard, 0, PhysicalInput.KeySpace, true, false);</code></example>
    public RawInputRecord(
        long arrivalMs,
        RawKind kind,
        DeviceClass device,
        byte padIndex,
        PhysicalInput input,
        bool pressed,
        bool echo,
        short value = 0,
        short value2 = 0)
    {
        ArrivalMs = arrivalMs;
        Kind = kind;
        Device = device;
        PadIndex = padIndex;
        Input = input;
        Pressed = pressed;
        Echo = echo;
        Value = value;
        Value2 = value2;
    }
}
