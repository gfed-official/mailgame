using Godot;

namespace PerformativeMail.Game.Smoke;

/// <summary>
/// The <c>input-adapter</c> smoke's stand-in dev overlay (input-controls story 026, as in the OQ-IC-9 spike): a root child added
/// after the adapter that records every event its <c>_Input</c> receives, so the smoke can see which events the adapter marked
/// handled. Debug smoke code only.
/// </summary>
public partial class InputOverlayProbe : Node
{
    /// <summary>One line per event received: <c>key:Space:down</c>, <c>mouse:Left:up</c>, <c>motion</c> or <c>other</c>.</summary>
    public List<string> Seen { get; } = [];

    /// <summary>Records the event.</summary>
    /// <param name="event">The event.</param>
    public override void _Input(InputEvent @event) => Seen.Add(@event switch
    {
        InputEventKey k => $"key:{k.PhysicalKeycode}:{(k.Pressed ? "down" : "up")}",
        InputEventMouseButton b => $"mouse:{b.ButtonIndex}:{(b.Pressed ? "down" : "up")}",
        InputEventMouseMotion => "motion",
        _ => "other",
    });
}
