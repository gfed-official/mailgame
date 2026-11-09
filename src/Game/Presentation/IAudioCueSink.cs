using Godot;

namespace PerformativeMail.Game.Presentation;

/// <summary>
/// Plays audio cues (godot-host-shell story 006). <see cref="AudioPresenter"/> is the cue entry views reach through
/// <see cref="PresenterDeps.Audio"/>; it forwards each cue to the sinks registered with <see cref="AudioPresenter.RegisterSink"/>
/// (mail-destinations 016, throwing-catching 021). Main thread only.
/// </summary>
/// <example><code>deps.Audio.Cue(new AudioCue(CueIds.Flag, flap.GlobalPosition, Positional: true));</code></example>
public interface IAudioCueSink
{
    /// <summary>Plays (or forwards) one cue.</summary>
    /// <param name="cue">The cue.</param>
    void Cue(in AudioCue cue);
}

/// <summary>One audio cue: an id the audio-director owns (no catalogue here), where it sounds and whether it is positional.</summary>
/// <param name="Id">The cue id.</param>
/// <param name="Position">World position (ignored when not positional).</param>
/// <param name="Positional">True for a 3D-positioned cue.</param>
public readonly record struct AudioCue(StringName Id, Vector3 Position, bool Positional);
