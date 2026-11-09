using Godot;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Players.Client;
using PerformativeMail.Sim.Primitives;

namespace PerformativeMail.Game.Hud;

/// <summary>What the focus feed shows this frame (player-character story 026). Explicit, append-only values.</summary>
public enum FocusFeedKind : byte
{
    /// <summary>Nothing to draw: nothing focused, the anchor is behind the camera, or no interaction snapshot is published.</summary>
    None = 0,

    /// <summary>A focused anchor in front of the camera.</summary>
    Focused = 1,

    /// <summary>The too-far caret (the snapshot's <c>NearestBeyond</c>); reserved, not produced at M-1a.</summary>
    TooFarCaret = 2,
}

/// <summary>
/// The HUD's focus data for one frame (player-character story 026, PCM-UI-1 data rows; ADR-0014 section 7, ADR-0019 section 2): the
/// focused anchor projected to screen, its screen-space bounds and the snapshot's verb, availability, prompt args and interaction state,
/// copied unchanged. <see cref="PresentedSeq"/> is the <see cref="InteractionSnapshot.Seq"/> it was built from, the value the router
/// echoes in press edges. The feed never adds a verdict of its own: drawing (bracket, prompt, lock, hold ring) is the HUD's. Screen
/// values are in the viewport's canvas space (<c>Camera3D.UnprojectPosition</c>). Values only.
/// </summary>
/// <example><code>ref readonly FocusFeed f = ref feed.Current; if (f.Visible) { DrawBracket(f.ScreenBounds); }</code></example>
public readonly struct FocusFeed
{
    /// <summary>True when the focused anchor is in front of the camera and projected; an unavailable target stays visible.</summary>
    public bool Visible { get; init; }

    /// <summary>What to draw.</summary>
    public FocusFeedKind Kind { get; init; }

    /// <summary>The focused anchor; <c>default</c> when nothing is focused.</summary>
    public AnchorRef Focused { get; init; }

    /// <summary>The focused anchor's verb.</summary>
    public Verb Verb { get; init; }

    /// <summary>The focused anchor's interaction mode.</summary>
    public InteractMode Mode { get; init; }

    /// <summary>True when the focused anchor is usable (the snapshot's <c>Available</c> flag).</summary>
    public bool Available { get; init; }

    /// <summary>The one reason key when the focused anchor is not usable; <see cref="StringId.None"/> otherwise.</summary>
    public StringId Reason { get; init; }

    /// <summary>The focused owner's prompt args, as published.</summary>
    public PromptArgs PromptArgs { get; init; }

    /// <summary>The interaction state.</summary>
    public InteractState State { get; init; }

    /// <summary>Hold progress, permille.</summary>
    public ushort ProgressPm { get; init; }

    /// <summary>Why the last interaction ended or was turned down.</summary>
    public CancelReason LastCancel { get; init; }

    /// <summary>The anchor point on screen, canvas pixels; meaningful while <see cref="Visible"/>.</summary>
    public Vector2 ScreenPos { get; init; }

    /// <summary>The anchor entity's projected bounds, clamped to the viewport, canvas pixels; meaningful while <see cref="Visible"/>.</summary>
    public Rect2 ScreenBounds { get; init; }

    /// <summary>The <see cref="InteractionSnapshot.Seq"/> this feed was built from.</summary>
    public ushort PresentedSeq { get; init; }
}

/// <summary>
/// The focus feed the World presenter publishes once per frame for the HUD (player-character story 026). <c>World</c> is presented before
/// <c>Hud</c> (ADR-0019 section 2), so a HUD view reading <see cref="Current"/> in its <c>Present</c> sees this frame's value. Read from
/// <c>PresenterDeps.Published</c>. Main thread only.
/// </summary>
/// <example><code>IHudFocusFeed feed = deps.Published.Get&lt;IHudFocusFeed&gt;(); ushort seq = feed.Current.PresentedSeq;</code></example>
public interface IHudFocusFeed
{
    /// <summary>This frame's feed; <c>default</c> (not visible) until the first frame.</summary>
    ref readonly FocusFeed Current { get; }
}
