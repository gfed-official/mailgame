using System.Text.Json;

namespace PerformativeMail.Client.Core.Input.Settings;

/// <summary>
/// Reads <c>user://input.json</c> (ADR-0012 section 9, TR-input-020; GDD R10 "Validation at load"): a hand-written forward
/// <see cref="Utf8JsonReader"/> walk over the <see cref="SettingsRanges"/> table, integers only, ordinal key matching, no
/// reflection binding (ADR-0010). Unknown keys are dropped and logged, ranges are clamped, and a malformed file loads
/// <see cref="InputSettings.Defaults"/> with one warning. Allocates at load only. The overrides-only writer, the
/// <c>bindings</c> map and the migration chain are story 032 (M1); the adapter (story 028) reads the bytes.
/// </summary>
/// <example>
/// <code>
/// InputSettings settings = InputSettingsCodec.Parse(bytes, out SettingsLoadReport report);
/// report.WriteTo(uiEventLog, clock.NowMs);
/// holder.Apply(settings);
/// </code>
/// </example>
public static class InputSettingsCodec
{
    private const string VersionKey = "version";
    private const string BindingsKey = "bindings";

    // Mail Dev default pending Evan (TD-205 item 6): mode and style names are the enum names, matched ordinal and
    // case-sensitive ("Hold", "Toggle", "Auto"); Auto is valid only for the two Sprint classes, elsewhere it is an unknown
    // value and the class default is kept.
    private static readonly string[] _holdModeNames = ["Hold", "Toggle", "Auto"];
    private static readonly string[] _glyphStyleNames = ["Auto"];

    // Mail Dev default pending Evan (TD-205 item 7): the ADR-0010 content reader stance for a hand-edited file: comments
    // skipped, trailing commas allowed, a UTF-8 BOM stripped.
    private static readonly JsonReaderOptions _readerOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Parses a settings file. <c>version</c> is an integer (missing means 1); any other version loads what it can with one
    /// <see cref="SettingsNoteKind.UnsupportedVersion"/> warning. A missing field takes its default, an out-of-range value
    /// clamps to the nearest bound (the hold time also rounds to 50 ms), an unknown key (including <c>bindings</c>) is skipped
    /// and noted once per key name, and an unknown mode name keeps the class default. A malformed file (empty, truncated,
    /// not an object, trailing content) returns <see cref="InputSettings.Defaults"/> and a report holding only the
    /// <see cref="SettingsNoteKind.Malformed"/> warning. A value of the wrong type (a fraction, an exponent, a number past
    /// 64 bits, a string for a number, a number for a bool, an object or array) is malformed for its field only: the field
    /// keeps its default with a <see cref="SettingsNoteKind.WrongType"/> note and the rest of the file loads.
    /// </summary>
    /// <param name="utf8">The file bytes. A missing file is not an error: the caller uses <see cref="InputSettings.Defaults"/> without parsing.</param>
    /// <param name="rep">What was clamped, dropped or warned about, in file order.</param>
    /// <returns>The settings, every value normalized to its row.</returns>
    /// <example><code>InputSettings s = InputSettingsCodec.Parse("{\"holdSettingMs\":600}"u8, out SettingsLoadReport rep); // s.HoldSettingMs == 600</code></example>
    public static InputSettings Parse(ReadOnlySpan<byte> utf8, out SettingsLoadReport rep)
    {
        int[] values = SettingsRanges.CreateDefaultValues();
        var notes = new List<SettingsNote>();
        int fileVersion = SettingsRanges.CurrentVersion;
        bool ok;
        try
        {
            ok = TryWalk(utf8, values, notes, ref fileVersion);
        }
        catch (JsonException)
        {
            ok = false;
        }

        if (!ok)
        {
            // Mail Dev default pending Evan (TD-205 item 8): a malformed file keeps no other note, so the report and the
            // log both hold exactly the one warning. Only unparseable JSON gets here; a wrong-typed value (fraction,
            // exponent, out of 64 bits, wrong JSON type) is malformed for that field alone: it keeps its default, gets an
            // informational WrongType note, and the rest of the file loads.
            rep = new SettingsLoadReport(SettingsRanges.CurrentVersion, [new SettingsNote(SettingsNoteKind.Malformed, null, 0, 0, null)]);
            return InputSettings.Defaults;
        }

        int outer = values[(int)SettingsField.DzOuterPm];
        if (SettingsRanges.EnforceDeadzoneGap(values))
        {
            notes.Add(new SettingsNote(SettingsNoteKind.Clamped, SettingsField.DzOuterPm, outer, values[(int)SettingsField.DzOuterPm], null));
        }

        rep = new SettingsLoadReport(fileVersion, notes.ToArray());
        return new InputSettings(values);
    }

    private static bool TryWalk(ReadOnlySpan<byte> utf8, int[] values, List<SettingsNote> notes, ref int fileVersion)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        if (utf8.StartsWith(bom))
        {
            utf8 = utf8[bom.Length..];
        }

        var reader = new Utf8JsonReader(utf8, _readerOptions);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        List<string>? skippedKeys = null;
        int versionNote = -1;
        while (true)
        {
            if (!reader.Read())
            {
                return false;
            }

            if (reader.TokenType == JsonTokenType.EndObject)
            {
                // The root object closed; anything after it is malformed (the reader throws on a trailing token).
                return !reader.Read();
            }

            if (reader.ValueTextEquals(VersionKey))
            {
                if (!reader.Read())
                {
                    return false;
                }

                if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int declared))
                {
                    fileVersion = declared;
                    SetVersionNote(notes, ref versionNote, declared == SettingsRanges.CurrentVersion ? null
                        : new SettingsNote(SettingsNoteKind.UnsupportedVersion, null, declared, SettingsRanges.CurrentVersion, null));
                }
                else
                {
                    fileVersion = SettingsRanges.CurrentVersion;
                    SetVersionNote(notes, ref versionNote, new SettingsNote(SettingsNoteKind.WrongType, null, 0, SettingsRanges.CurrentVersion, VersionKey));
                    reader.Skip();
                }

                continue;
            }

            if (TryFindRow(ref reader, out SettingsRange row))
            {
                if (!reader.Read())
                {
                    return false;
                }

                if (!TryReadValue(ref reader, row, values, notes))
                {
                    reader.Skip();
                    values[(int)row.Field] = row.Default;
                    notes.Add(new SettingsNote(SettingsNoteKind.WrongType, row.Field, 0, row.Default, null));
                }

                continue;
            }

            // Mail Dev default pending Evan (TD-205 item 9): unknown keys are deduplicated by exact name across the file
            // (a duplicate known key is last-wins); bindings gets its own note kind. A repeated version key is last-wins too
            // and leaves at most one version note, so a doubled key never doubles the warning.
            string key = reader.GetString()!;
            reader.Skip();
            skippedKeys ??= [];
            if (!skippedKeys.Contains(key))
            {
                skippedKeys.Add(key);
                var kind = string.Equals(key, BindingsKey, StringComparison.Ordinal) ? SettingsNoteKind.BindingsSkipped : SettingsNoteKind.UnknownKey;
                notes.Add(new SettingsNote(kind, null, 0, 0, key));
            }
        }
    }

    // Keeps the one note a version key may leave: the latest key replaces the earlier one, and null (version 1) removes it.
    private static void SetVersionNote(List<SettingsNote> notes, ref int index, SettingsNote? note)
    {
        if (index >= 0 && note is { } replacement)
        {
            notes[index] = replacement;
        }
        else if (index >= 0)
        {
            notes.RemoveAt(index);
            index = -1;
        }
        else if (note is { } added)
        {
            index = notes.Count;
            notes.Add(added);
        }
    }

    private static bool TryFindRow(ref Utf8JsonReader reader, out SettingsRange row)
    {
        foreach (SettingsRange candidate in SettingsRanges.All)
        {
            if (reader.ValueTextEquals(candidate.Key))
            {
                row = candidate;
                return true;
            }
        }

        row = default;
        return false;
    }

    private static bool TryReadValue(ref Utf8JsonReader reader, in SettingsRange row, int[] values, List<SettingsNote> notes)
    {
        int index = (int)row.Field;
        switch (row.Kind)
        {
            case SettingsValueKind.Int:
                if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out long read))
                {
                    return false;
                }

                values[index] = SettingsRanges.Normalize(row, read);
                if (values[index] != read)
                {
                    notes.Add(new SettingsNote(SettingsNoteKind.Clamped, row.Field, read, values[index], null));
                }

                return true;

            case SettingsValueKind.Bool:
                if (reader.TokenType is not (JsonTokenType.True or JsonTokenType.False))
                {
                    return false;
                }

                values[index] = reader.TokenType == JsonTokenType.True ? 1 : 0;
                return true;

            default:
                if (reader.TokenType != JsonTokenType.String)
                {
                    return false;
                }

                int named = FindName(ref reader, row.Kind == SettingsValueKind.HoldMode ? _holdModeNames : _glyphStyleNames);
                if (named < row.Min || named > row.Max)
                {
                    values[index] = row.Default;
                    notes.Add(new SettingsNote(SettingsNoteKind.UnknownValue, row.Field, 0, row.Default, null));
                }
                else
                {
                    values[index] = named;
                }

                return true;
        }
    }

    private static int FindName(ref Utf8JsonReader reader, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (reader.ValueTextEquals(names[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
