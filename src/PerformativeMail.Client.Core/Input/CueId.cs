namespace PerformativeMail.Client.Core.Input;

/// <summary>
/// The UI cues the router raises: the <see cref="UiEventEntry.Reason"/> of a <see cref="LogKind.Cue"/> entry (the strings are
/// HUD's). Values are stable and append-only.
/// </summary>
/// <example><code>bool putAway = e.Kind == LogKind.Cue &amp;&amp; (CueId)e.Reason == CueId.PutAwayFirst;</code></example>
public enum CueId : byte
{
    /// <summary>No cue.</summary>
    None = 0,

    /// <summary>"Put it away first": Throw or Tool pressed in Holding-closed (GDD R6 ruling 3).</summary>
    PutAwayFirst = 1,

    /// <summary>"Hold to aim": a throw aim released before <c>throwMinAimMs</c> (GDD F11, collision ruling 9; story 014).</summary>
    HoldToAim = 2,

    /// <summary>A Throw press refused because nothing can be thrown; Flags carry <see cref="IThrowAimView.NotThrowable"/> (story 014).</summary>
    NotThrowable = 3,
}

/// <summary>
/// Which device's wording a cue shows: the <see cref="UiEventEntry.IdB"/> of a <see cref="LogKind.Cue"/> entry ("[Esc]" or
/// "[B]"). Values are stable and append-only.
/// </summary>
/// <example><code>var wording = (CueWording)e.IdB;</code></example>
public enum CueWording : byte
{
    /// <summary>Keyboard and mouse wording.</summary>
    Kbm = 0,

    /// <summary>Gamepad wording.</summary>
    Pad = 1,
}
