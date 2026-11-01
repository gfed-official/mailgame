using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Client.Core.Input.Settings;

/// <summary>
/// The live <see cref="InputSettings"/> (ADR-0012 section 9): the Sim reads the hold time through
/// <see cref="IClientInputSettings"/>, the router reads <see cref="Current"/>. <see cref="ChangeCount"/> rises only when a field
/// actually changes, so stories 009 and 010 rebuild their curve and repeater tables outside the frame when it moves. This
/// type builds no table. Reads allocate 0 B. Not thread-safe: main thread only.
/// </summary>
/// <example>
/// <code>
/// var holder = new InputSettingsHolder();
/// holder.Apply(InputSettingsCodec.Parse(bytes, out SettingsLoadReport report));
/// if (holder.ChangeCount != seenCount) { seenCount = holder.ChangeCount; /* rebuild tables */ }
/// </code>
/// </example>
public sealed class InputSettingsHolder : IClientInputSettings
{
    // Approved by Evan 2026-11-01 (TD-204 item 2): the holder exposes the hold time to the Sim and keeps a change counter
    // that rises only when a field changed; it builds no table and there is no InputDefaults type.

    /// <summary>Starts at <see cref="InputSettings.Defaults"/> with <see cref="ChangeCount"/> 0.</summary>
    public InputSettingsHolder()
    {
        Current = InputSettings.Defaults;
    }

    /// <summary>The live settings.</summary>
    public InputSettings Current { get; private set; }

    /// <summary>The number of applied changes; equal settings never raise it.</summary>
    public int ChangeCount { get; private set; }

    /// <inheritdoc />
    public int HoldSettingMs => Current.HoldSettingMs;

    /// <summary>Replaces the live settings when any field differs.</summary>
    /// <param name="settings">The new settings (already normalized: they come from the codec or <see cref="InputSettings.With"/>).</param>
    /// <returns>True when a field changed and <see cref="ChangeCount"/> rose.</returns>
    /// <example><code>bool changed = holder.Apply(parsed);</code></example>
    public bool Apply(InputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Equals(Current))
        {
            return false;
        }

        Current = settings;
        ChangeCount++;
        return true;
    }

    // Mail Dev default pending Evan (TD-205 item 10): Set clamps and steps silently (no Config entry); only a file load logs.
    /// <summary>Changes one field, normalized by its <see cref="SettingsRanges"/> row (see <see cref="InputSettings.With"/>).</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The new stored integer (bools as 0 / 1, enums as their value).</param>
    /// <returns>True when the stored value changed and <see cref="ChangeCount"/> rose.</returns>
    /// <example><code>holder.Set(SettingsField.HoldSettingMs, 600);</code></example>
    public bool Set(SettingsField field, int value) => Apply(Current.With(field, value));
}
