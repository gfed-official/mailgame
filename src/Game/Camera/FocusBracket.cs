using Godot;
using PerformativeMail.Game.Hud;

namespace PerformativeMail.Game.Camera;

/// <summary>
/// The <see cref="FocusDebugOverlay"/>'s drawing (player-character story 026): an outline of <see cref="FocusFeed.ScreenBounds"/> and a
/// small outlined square on <see cref="FocusFeed.ScreenPos"/>, white when the target is usable and grey when not, drawn with
/// <c>DrawRect</c> outlines only (the manifest bans <c>DrawLine</c>/<c>DrawPolyline</c> in the HUD). Takes no focus and no mouse.
/// Main thread only.
/// </summary>
/// <example><code>bracket.SetFeed(feed.Current);</code></example>
public partial class FocusBracket : Control
{
    /// <summary>Outline width, pixels.</summary>
    public const float OutlinePx = 2f;

    /// <summary>Edge of the anchor-point marker, pixels.</summary>
    public const float MarkerPx = 8f;

    private FocusFeed _shown;

    /// <summary>Creates the control: no focus, mouse ignored.</summary>
    public FocusBracket()
    {
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Shows <paramref name="feed"/> from the next draw; redraws only while something is or was visible.</summary>
    /// <param name="feed">This frame's feed.</param>
    public void SetFeed(in FocusFeed feed)
    {
        bool redraw = feed.Visible || _shown.Visible;
        _shown = feed;
        if (redraw)
        {
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (!_shown.Visible)
        {
            return;
        }

        Color color = _shown.Available ? Colors.White : Colors.Gray;
        DrawRect(_shown.ScreenBounds, color, filled: false, width: OutlinePx);
        var half = new Vector2(MarkerPx / 2f, MarkerPx / 2f);
        DrawRect(new Rect2(_shown.ScreenPos - half, half * 2f), color, filled: false, width: OutlinePx);
    }
}
