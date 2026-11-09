using Godot;
using PerformativeMail.Client.Core.Input.Settings;

namespace PerformativeMail.Game.Input;

/// <summary>
/// What <see cref="InputSettingsFile.Read"/> found: the settings to apply, the codec's report (null when nothing was parsed), at
/// most one warning line, and a one-line summary for the boot log.
/// </summary>
/// <param name="Settings">The settings to apply; <see cref="InputSettings.Defaults"/> when the file is missing, unreadable or malformed.</param>
/// <param name="Report">The codec's report; null when the file was missing or unreadable.</param>
/// <param name="Warning">The single warning to log, or null.</param>
/// <param name="Summary">What happened, for the <c>BOOT 4</c> line.</param>
public sealed record InputSettingsRead(InputSettings Settings, SettingsLoadReport? Report, string? Warning, string Summary);

/// <summary>
/// The M-1a read of <c>user://input.json</c> (ADR-0012 section 9, TR-input-020, GDD H-R12-1; input-controls story 028): the
/// bytes come from <c>FileAccess.GetFileAsBytes</c> and <c>FileAccess.GetOpenError()</c> is checked on every call (never
/// <c>GetAsText</c>, ADR-0019 Implementation Guidelines); Client.Core's <see cref="InputSettingsCodec"/> parses them. A missing
/// file loads the defaults with no warning; an unreadable or malformed file loads the defaults with one warning; a file the
/// codec reports warnings for (an unsupported version) loads what it could with one warning naming the count. This story
/// never writes the file (story 032). Boot-time only; allocates. Main thread only.
/// </summary>
/// <example>
/// <code>
/// InputSettingsRead read = InputSettingsFile.Read(InputSettingsFile.DefaultPath);
/// if (read.Warning is { } w) { GD.PushWarning(w); }
/// holder.Apply(read.Settings);
/// </code>
/// </example>
public static class InputSettingsFile
{
    /// <summary>The M-1a settings file.</summary>
    public const string DefaultPath = "user://input.json";

    /// <summary>Reads and parses the settings file at <paramref name="path"/>.</summary>
    /// <param name="path">A Godot path, usually <see cref="DefaultPath"/>.</param>
    /// <returns>What was found.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or empty.</exception>
    public static InputSettingsRead Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
        Error open = Godot.FileAccess.GetOpenError();
        if (open == Error.FileNotFound)
        {
            return new InputSettingsRead(InputSettings.Defaults, null, null, $"settings {path}: missing, defaults");
        }

        if (open != Error.Ok)
        {
            return new InputSettingsRead(
                InputSettings.Defaults, null, $"[InputBoot] {path} could not be read ({open}); input settings use the defaults", $"settings {path}: unreadable ({open}), defaults");
        }

        InputSettings settings = InputSettingsCodec.Parse(bytes, out SettingsLoadReport report);
        if (report.IsMalformed)
        {
            return new InputSettingsRead(settings, report, $"[InputBoot] {path} is malformed; input settings use the defaults", $"settings {path}: malformed, defaults");
        }

        string loaded = $"settings {path}: loaded v{report.FileVersion}, {report.Notes.Count} note(s), holdSettingMs={settings.HoldSettingMs} holdModeDeliver={settings.HoldModeDeliver}";
        string? warning = report.WarningCount > 0
            ? $"[InputBoot] {path}: {report.WarningCount} warning(s) (version {report.FileVersion}); see the event log"
            : null;
        return new InputSettingsRead(settings, report, warning, loaded);
    }
}
