namespace PerformativeMail.Client.Core.Input.Settings;

/// <summary>
/// What a <see cref="SettingsNote"/> records; also the <see cref="UiEventEntry.Reason"/> of its <see cref="LogKind.Config"/>
/// entry. Values are stable and append-only.
/// </summary>
public enum SettingsNoteKind : byte
{
    /// <summary>Warning: the file is not a JSON object of the expected types; defaults were loaded.</summary>
    Malformed = 1,

    /// <summary>Warning: the file declares a <c>version</c> other than 1; what could be read was loaded.</summary>
    UnsupportedVersion = 2,

    /// <summary>An unknown key was dropped (its value skipped). Logged once per key name.</summary>
    UnknownKey = 3,

    /// <summary>The <c>bindings</c> key was skipped unread (story 032, M1). Logged once.</summary>
    BindingsSkipped = 4,

    /// <summary>A value was out of range or off its step and was stored clamped / rounded.</summary>
    Clamped = 5,

    /// <summary>A mode or style name is not valid for its field; the field default was kept.</summary>
    UnknownValue = 6,
}

/// <summary>One line of a <see cref="SettingsLoadReport"/>.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Field">The field concerned, or null for a whole-file note or an unknown key.</param>
/// <param name="Read">The value read (the file version for <see cref="SettingsNoteKind.UnsupportedVersion"/>; 0 otherwise when there is no number).</param>
/// <param name="Stored">The value stored (0 when nothing was stored).</param>
/// <param name="Key">The key name for <see cref="SettingsNoteKind.UnknownKey"/> and <see cref="SettingsNoteKind.BindingsSkipped"/>; null otherwise.</param>
/// <example><code>var note = new SettingsNote(SettingsNoteKind.Clamped, SettingsField.DzInnerPm, 900, 400, null);</code></example>
public readonly record struct SettingsNote(SettingsNoteKind Kind, SettingsField? Field, long Read, int Stored, string? Key);

/// <summary>
/// What <see cref="InputSettingsCodec.Parse(ReadOnlySpan{byte}, out SettingsLoadReport)"/> did to a file: every clamp, drop and
/// fallback in file order, and the warnings. A malformed file reports exactly one note, the <see cref="SettingsNoteKind.Malformed"/>
/// warning. Built once at load; <see cref="WriteTo"/> copies it into the <see cref="UiEventLog"/>.
/// </summary>
/// <example>
/// <code>
/// InputSettings s = InputSettingsCodec.Parse(bytes, out SettingsLoadReport report);
/// report.WriteTo(uiEventLog, clock.NowMs);
/// if (report.WarningCount &gt; 0) { /* show "input.json: see log" */ }
/// </code>
/// </example>
public sealed class SettingsLoadReport
{
    private readonly SettingsNote[] _notes;

    internal SettingsLoadReport(int fileVersion, SettingsNote[] notes)
    {
        FileVersion = fileVersion;
        _notes = notes;

        // Mail Dev default pending Evan (TD-205 item 12): only Malformed and UnsupportedVersion count as warnings; clamps,
        // drops and unknown names are informational Config entries.
        foreach (SettingsNote note in notes)
        {
            if (note.Kind is SettingsNoteKind.Malformed or SettingsNoteKind.UnsupportedVersion)
            {
                WarningCount++;
            }
        }
    }

    /// <summary>The <c>version</c> the file declared (1 when absent or when the file is malformed).</summary>
    public int FileVersion { get; }

    /// <summary>The number of warnings: <see cref="SettingsNoteKind.Malformed"/> and <see cref="SettingsNoteKind.UnsupportedVersion"/> notes.</summary>
    public int WarningCount { get; }

    /// <summary>True when the file was malformed and <see cref="InputSettings.Defaults"/> were loaded.</summary>
    public bool IsMalformed => _notes.Length == 1 && _notes[0].Kind == SettingsNoteKind.Malformed;

    /// <summary>Every note, in file order.</summary>
    public IReadOnlyList<SettingsNote> Notes => _notes;

    /// <summary>
    /// Writes one <see cref="LogKind.Config"/> entry per note, in order. Layout: <see cref="UiEventEntry.Reason"/> =
    /// <see cref="SettingsNoteKind"/>; <see cref="UiEventEntry.IdA"/> = <see cref="SettingsField"/>, or
    /// <see cref="ushort.MaxValue"/> when the note has no field; <see cref="UiEventEntry.IdB"/> = the stored value and
    /// <see cref="UiEventEntry.IdC"/> = the read value, each saturated to 0..65535. Key names stay in <see cref="Notes"/>
    /// (the ring holds no strings).
    /// </summary>
    /// <param name="log">The shared event ring.</param>
    /// <param name="timeMs">The load time in ms (the <see cref="IInputClock"/> domain).</param>
    /// <example><code>report.WriteTo(log, clock.NowMs);</code></example>
    public void WriteTo(UiEventLog log, long timeMs)
    {
        // Mail Dev default pending Evan (TD-205 item 5): the Config entry layout above (reason = note kind, IdA = field,
        // IdB = stored, IdC = read).
        foreach (SettingsNote note in _notes)
        {
            ushort field = note.Field is { } f ? (ushort)f : ushort.MaxValue;
            log.Write(timeMs, LogKind.Config, (byte)note.Kind, field, Saturate(note.Stored), Saturate(note.Read));
        }
    }

    private static ushort Saturate(long value) => (ushort)Math.Clamp(value, 0, ushort.MaxValue);
}
