namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// What a <see cref="RawInputRecord"/> reports (ADR-0012 section 1). Values are stable and append-only: the log stores
/// the numeric id (<see cref="UiEventEntry.Reason"/> of a coalesced motion entry).
/// </summary>
/// <example><code>bool isButton = record.Kind is RawKind.Key or RawKind.MouseButton or RawKind.PadButton;</code></example>
public enum RawKind : byte
{
    /// <summary>A keyboard key edge.</summary>
    Key = 0,

    /// <summary>A mouse button edge.</summary>
    MouseButton = 1,

    /// <summary>Relative mouse motion; <see cref="RawInputRecord.Value"/> and <see cref="RawInputRecord.Value2"/> are the counts.</summary>
    MouseMotion = 2,

    /// <summary>A wheel notch (<see cref="PhysicalInput.WheelUp"/> or <see cref="PhysicalInput.WheelDown"/>); <see cref="RawInputRecord.Value"/> is the notch count.</summary>
    Wheel = 3,

    /// <summary>A gamepad button edge.</summary>
    PadButton = 4,

    /// <summary>A gamepad axis sample; <see cref="RawInputRecord.Value"/> is Q15.</summary>
    PadAxis = 5,

    /// <summary>A synthetic record (focus loss release, tests, replay). Never changes the last-used device.</summary>
    Synthetic = 6,
}
