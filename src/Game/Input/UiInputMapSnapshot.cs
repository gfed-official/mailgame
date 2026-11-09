using System.Text;
using Godot;

namespace PerformativeMail.Game.Input;

/// <summary>
/// The boot-time check that the stock <c>ui_*</c> InputMap is never mutated (ADR-0012 section 7, TR-input-007, story
/// input-controls 027): <see cref="Capture"/> records every <c>ui_*</c> action with its deadzone and events (class, device and
/// text) as the engine set them up; <see cref="Matches"/> compares the live map against that record. With the story 025 CI lint
/// deferred to M0 this is the only check at M-1a. <see cref="InputAdapter"/> captures in its constructor and compares on its
/// first frame. Reads <c>InputMap</c> only; allocates; not for hot paths. Main thread only.
/// </summary>
/// <example>
/// <code>
/// UiInputMapSnapshot startup = UiInputMapSnapshot.Capture();
/// if (!startup.Matches(UiInputMapSnapshot.Capture(), out string diff)) { GD.PushError(diff); }
/// </code>
/// </example>
public sealed class UiInputMapSnapshot
{
    private readonly string[] _lines;

    private UiInputMapSnapshot(string[] lines) => _lines = lines;

    /// <summary>The number of <c>ui_*</c> actions recorded.</summary>
    public int ActionCount { get; private init; }

    /// <summary>Records the live <c>ui_*</c> actions: one line per action, sorted by name (ordinal).</summary>
    /// <returns>The snapshot.</returns>
    public static UiInputMapSnapshot Capture()
    {
        var lines = new List<string>();
        var sb = new StringBuilder();
        foreach (StringName action in InputMap.GetActions())
        {
            string name = action.ToString();
            if (!name.StartsWith("ui_", StringComparison.Ordinal))
            {
                continue;
            }

            sb.Clear().Append(name).Append(" dz=").Append(InputMap.ActionGetDeadzone(action).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            foreach (InputEvent e in InputMap.ActionGetEvents(action))
            {
                sb.Append(" | ").Append(e.GetClass()).Append('@').Append(e.Device).Append(' ').Append(e.AsText());
            }

            lines.Add(sb.ToString());
        }

        lines.Sort(StringComparer.Ordinal);
        return new UiInputMapSnapshot([.. lines]) { ActionCount = lines.Count };
    }

    /// <summary>True when <paramref name="live"/> records the same actions, deadzones and events as this snapshot.</summary>
    /// <param name="live">A later capture.</param>
    /// <param name="diff">Empty on a match; otherwise the first differing action line, startup then live.</param>
    /// <returns>Whether the map is untouched.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="live"/> is null.</exception>
    public bool Matches(UiInputMapSnapshot live, out string diff)
    {
        ArgumentNullException.ThrowIfNull(live);
        int n = Math.Max(_lines.Length, live._lines.Length);
        for (int i = 0; i < n; i++)
        {
            string a = i < _lines.Length ? _lines[i] : "(none)";
            string b = i < live._lines.Length ? live._lines[i] : "(none)";
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                diff = $"ui_* InputMap changed since startup: startup [{a}] live [{b}]";
                return false;
            }
        }

        diff = string.Empty;
        return true;
    }
}
