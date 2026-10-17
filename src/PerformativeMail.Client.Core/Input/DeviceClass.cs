namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The class of device a <see cref="RawInputRecord"/> came from (ADR-0012 section 7). Values are stable and append-only.
/// </summary>
/// <example><code>bool drivesGlyphs = record.Device != DeviceClass.Synthetic;</code></example>
public enum DeviceClass : byte
{
    /// <summary>The keyboard.</summary>
    Keyboard = 0,

    /// <summary>The mouse.</summary>
    Mouse = 1,

    /// <summary>A gamepad; <see cref="RawInputRecord.PadIndex"/> says which.</summary>
    Pad = 2,

    /// <summary>No physical device: a synthetic record. Must never change the last-used device.</summary>
    Synthetic = 3,
}
