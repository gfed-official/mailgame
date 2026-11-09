using Godot;

namespace PerformativeMail.Game.Hud;

/// <summary>
/// The solo pause overlay (WP-F; TR-sim-009): a dimmed full-screen layer with "Paused — Esc to resume", shown while the tick driver is
/// paused. The host shell's pause toggle shows and hides it; it reads nothing and never touches <c>SceneTree.Paused</c>.
/// </summary>
/// <example><code>var overlay = PauseOverlay.Create(); main.AddChild(overlay); overlay.Visible = paused;</code></example>
public partial class PauseOverlay : CanvasLayer
{
    /// <summary>Above the HUD layers.</summary>
    public const int LayerOrder = 900;

    /// <summary>The overlay's text.</summary>
    public const string Text = "Paused — Esc to resume";

    /// <summary>Creates the hidden overlay with its dim and label.</summary>
    /// <returns>The overlay.</returns>
    public static PauseOverlay Create()
    {
        var overlay = new PauseOverlay { Name = "PauseOverlay", Layer = LayerOrder, Visible = false };
        var dim = new ColorRect { Name = "Dim", Color = new Color(0f, 0f, 0f, 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var label = new Label
        {
            Name = "Label",
            Text = Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 40);
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(dim);
        overlay.AddChild(label);
        return overlay;
    }
}
