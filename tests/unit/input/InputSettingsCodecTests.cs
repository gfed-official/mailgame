using System.Text;
using PerformativeMail.Client.Core.Input;
using PerformativeMail.Client.Core.Input.Settings;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Sim.Tests.Unit.Input;

/// <summary>
/// input-controls story 019: <see cref="InputSettingsCodec.Parse(ReadOnlySpan{byte}, out SettingsLoadReport)"/> over
/// <c>user://input.json</c> bytes (schema and defaults, version, <c>bindings</c> skip, H-R12-1 core half, clamping and
/// validation incl. the H-F3-1 clamp row) and <see cref="InputSettingsHolder"/> (supply to the Sim and router). In-memory
/// bytes only, no I/O.
/// </summary>
[Trait("Id", "TR-input-020")]
public class InputSettingsCodecTests
{
    private static InputSettings Parse(string json, out SettingsLoadReport rep) =>
        InputSettingsCodec.Parse(Encoding.UTF8.GetBytes(json), out rep);

    private static string Json(string key, long value) => "{\"" + key + "\":" + value + "}";

    // ------------------------------------------------------------ schema and defaults

    [Fact]
    public void Parse_UntouchedProfile_EqualsDefaultsWithNoNotes()
    {
        // Arrange - every key written at its default, plus the two minimal files
        var every = new StringBuilder("{\"version\":1");
        foreach (SettingsRange row in SettingsRanges.All)
        {
            every.Append(",\"").Append(row.Key).Append("\":").Append(row.Kind switch
            {
                SettingsValueKind.Bool => row.Default == 1 ? "true" : "false",
                SettingsValueKind.HoldMode => "\"" + (HoldMode)row.Default + "\"",
                SettingsValueKind.GlyphStyle => "\"" + (GlyphStyle)row.Default + "\"",
                _ => row.Default.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }

        string[] files = ["{}", "{\"version\":1}", every.Append('}').ToString()];

        foreach (string file in files)
        {
            // Act
            InputSettings settings = Parse(file, out SettingsLoadReport rep);

            // Assert
            Assert.Equal(InputSettings.Defaults, settings);
            Assert.Empty(rep.Notes);
        }
    }

    // ------------------------------------------------------------ parse: version and bindings

    [Theory]
    [InlineData("{\"holdSettingMs\":600}", 1, 0)]
    [InlineData("{\"version\":2,\"holdSettingMs\":600}", 2, 1)]
    public void Parse_Version_MissingIsOneAndOtherValuesLoadWithOneWarning(string json, int fileVersion, int warnings)
    {
        // Act
        InputSettings settings = Parse(json, out SettingsLoadReport rep);

        // Assert
        Assert.Equal(600, settings.HoldSettingMs);
        Assert.Equal(fileVersion, rep.FileVersion);
        Assert.Equal(warnings, rep.WarningCount);
        Assert.Equal(warnings, rep.Notes.Count(n => n.Kind == SettingsNoteKind.UnsupportedVersion));
    }

    [Fact]
    public void Parse_BindingsKey_IsSkippedAndLoggedOnce()
    {
        // Arrange
        const string Input = "{\"bindings\":{\"interact\":{\"kbm\":[\"KeyE\",null],\"pad\":[]}},\"holdSettingMs\":600,\"bindings\":{}}";

        // Act
        InputSettings settings = Parse(Input, out SettingsLoadReport rep);

        // Assert
        Assert.Equal(600, settings.HoldSettingMs);
        SettingsNote note = Assert.Single(rep.Notes);
        Assert.Equal(SettingsNoteKind.BindingsSkipped, note.Kind);
        Assert.Equal(0, rep.WarningCount);
    }

    // ------------------------------------------------------------ H-R12-1 (core half)

    [Fact]
    [Trait("Id", "H-R12-1")]
    public void Parse_HoldSetting600AndDeliverToggle_BothApply()
    {
        // Act
        InputSettings settings = Parse("{\"holdSettingMs\":600,\"holdModeDeliver\":\"Toggle\"}", out SettingsLoadReport rep);

        // Assert
        Assert.Equal(600, settings.HoldSettingMs);
        Assert.Equal(HoldMode.Toggle, settings.HoldModeDeliver);
        Assert.Empty(rep.Notes);
    }

    [Theory]
    [Trait("Id", "H-R12-1")]
    [InlineData("")]
    [InlineData("{\"holdSettingMs\":600")]
    [InlineData("{\"bindings\":{\"a\":1}")]
    [InlineData("{\"holdSettingMs\":\"600\"}")]
    [InlineData("{\"holdSettingMs\":600.5}")]
    [InlineData("[1]")]
    [InlineData("{\"version\":2,\"foo\":1,\"dzInnerPm\":900,\"invertMouseX\":1}")]
    public void Parse_MalformedFile_LoadsDefaultsWithExactlyOneWarningInReportAndLog(string json)
    {
        // Arrange
        var log = new UiEventLog();

        // Act
        InputSettings settings = Parse(json, out SettingsLoadReport rep);
        rep.WriteTo(log, timeMs: 5);

        // Assert
        Assert.Equal(InputSettings.Defaults, settings);
        Assert.Equal(1, rep.WarningCount);
        Assert.Equal(SettingsNoteKind.Malformed, Assert.Single(rep.Notes).Kind);
        Assert.Equal(1, log.Count);
        Assert.True(log.TryGet(0, out UiEventEntry entry));
        Assert.Equal(LogKind.Config, entry.Kind);
        Assert.Equal((byte)SettingsNoteKind.Malformed, entry.Reason);
    }

    // ------------------------------------------------------------ clamping and validation

    [Fact]
    [Trait("Id", "H-F3-1")]
    public void Parse_DzInner900_ClampsTo400WithDzOuterUnchangedAndLogs()
    {
        // Arrange
        var log = new UiEventLog();

        // Act
        InputSettings settings = Parse("{\"dzInnerPm\":900}", out SettingsLoadReport rep);
        rep.WriteTo(log, timeMs: 5);

        // Assert
        Assert.Equal(400, settings.DzInnerPm);
        Assert.Equal(950, settings.DzOuterPm);
        Assert.Equal(new SettingsNote(SettingsNoteKind.Clamped, SettingsField.DzInnerPm, 900, 400, null), Assert.Single(rep.Notes));
        Assert.True(log.TryGet(0, out UiEventEntry entry));
        Assert.Equal(LogKind.Config, entry.Kind);
        Assert.Equal((byte)SettingsNoteKind.Clamped, entry.Reason);
        Assert.Equal((ushort)SettingsField.DzInnerPm, entry.IdA);
        Assert.Equal(400, entry.IdB);
        Assert.Equal(900, entry.IdC);
    }

    [Theory]
    [InlineData("holdSettingMs", 430, 450)]
    [InlineData("holdSettingMs", 150, 200)]
    [InlineData("holdSettingMs", 1200, 1000)]
    [InlineData("trigOnPm", 0, 150)]
    [InlineData("repeatIntervalMs", 0, 40)]
    public void Parse_OutOfRangeOrOffStep_StoresNearestValidValueAndLogs(string key, int read, int stored)
    {
        // Act
        InputSettings settings = Parse(Json(key, read), out SettingsLoadReport rep);

        // Assert
        SettingsNote note = Assert.Single(rep.Notes);
        Assert.Equal(SettingsNoteKind.Clamped, note.Kind);
        Assert.Equal(stored, settings[note.Field!.Value]);
        Assert.Equal(key, SettingsRanges.Get(note.Field.Value).Key);
        Assert.Equal((read, stored), (note.Read, note.Stored));
    }

    [Fact]
    public void Parse_UnknownKeysAndModeNames_DropOncePerKeyAndKeepClassDefaults()
    {
        // Arrange - Auto is not a Deliver mode; names are case-sensitive
        const string Input = "{\"foo\":1,\"foo\":[1,{\"x\":2}],\"Bar\":true,\"holdModeDeliver\":\"Auto\",\"holdModeSprintPad\":\"hold\",\"holdSettingMs\":600}";

        // Act
        InputSettings settings = Parse(Input, out SettingsLoadReport rep);

        // Assert - every field the file did not set validly is its default
        Assert.Equal(InputSettings.Defaults.With(SettingsField.HoldSettingMs, 600), settings);
        Assert.Equal(
            [
                (SettingsNoteKind.UnknownKey, (SettingsField?)null, "foo"),
                (SettingsNoteKind.UnknownKey, null, "Bar"),
                (SettingsNoteKind.UnknownValue, SettingsField.HoldModeDeliver, null),
                (SettingsNoteKind.UnknownValue, SettingsField.HoldModeSprintPad, null),
            ],
            rep.Notes.Select(n => (n.Kind, n.Field, n.Key)));
        Assert.Equal(0, rep.WarningCount);
    }

    public static TheoryData<string> ClampedIntKeys()
    {
        var keys = new TheoryData<string>();
        foreach (SettingsRange row in SettingsRanges.All)
        {
            if (row.Kind == SettingsValueKind.Int && row.Min < row.Max)
            {
                keys.Add(row.Key);
            }
        }

        return keys;
    }

    // Approved by Evan 2026-11-01 (TD-204 item 3): one parameterised boundary test over the SettingsRanges rows (min,
    // max, one inside, one outside per clamped field), not a separate ranges file pinning each default.
    [Theory]
    [MemberData(nameof(ClampedIntKeys))]
    public void Parse_EachClampedField_KeepsMinMaxAndInsideAndClampsOutside(string key)
    {
        // Arrange
        SettingsRange row = default;
        foreach (SettingsRange candidate in SettingsRanges.All)
        {
            row = candidate.Key == key ? candidate : row;
        }

        // Act / Assert
        Assert.Equal(row.Min, Parse(Json(key, row.Min), out _)[row.Field]);
        Assert.Equal(row.Max, Parse(Json(key, row.Max), out _)[row.Field]);
        Assert.Equal(row.Min + row.Step, Parse(Json(key, row.Min + row.Step), out _)[row.Field]);
        Assert.Equal(row.Max, Parse(Json(key, row.Max + row.Step), out SettingsLoadReport rep)[row.Field]);
        Assert.Equal(SettingsNoteKind.Clamped, Assert.Single(rep.Notes).Kind);
    }

    // ------------------------------------------------------------ supply to the Sim and router

    [Fact]
    public void Holder_ChangeCount_RisesOnlyWhenAFieldChanges()
    {
        // Arrange
        var holder = new InputSettingsHolder();
        IClientInputSettings sim = holder;

        // Act / Assert - equal settings (another instance, the same value, a value that normalizes back) do not count
        Assert.False(holder.Apply(Parse("{}", out _)));
        Assert.False(holder.Set(SettingsField.HoldSettingMs, 400));
        Assert.False(holder.Set(SettingsField.HoldSettingMs, 410));
        Assert.Equal(0, holder.ChangeCount);

        Assert.True(holder.Apply(Parse("{\"holdSettingMs\":600,\"holdModeDeliver\":\"Toggle\"}", out _)));
        Assert.Equal(600, sim.HoldSettingMs);
        Assert.True(holder.Set(SettingsField.HoldModeDeliver, (int)HoldMode.Hold));
        Assert.False(holder.Set(SettingsField.HoldModeDeliver, (int)HoldMode.Hold));
        Assert.Equal(2, holder.ChangeCount);
        Assert.Equal(HoldMode.Hold, holder.Current.HoldModeDeliver);
    }
}
