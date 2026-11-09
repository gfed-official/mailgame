using Godot;
using PerformativeMail.Client.Core.Ui.Throwing;
using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Views.Throwing;

/// <summary>The aim ribbon on its own canvas layer, below the HUD stack.</summary>
public partial class AimLayer : CanvasLayer
{
    /// <summary>Creates the layer around its view.</summary>
    /// <param name="view">The ribbon view.</param>
    public AimLayer(AimRibbonView view)
    {
        Name = "Aim";
        Layer = 0;
        AddChild(view);
    }
}

/// <summary>Entry point for the boot assembly.</summary>
public static class ThrowLayers
{
    /// <summary>
    /// Adds the aim layer to <paramref name="hud"/>. Wire after PR 87 (<paramref name="aim"/> over <c>ThrowPreviewState</c>) and after
    /// WP-A part 2 (the camera rig as <paramref name="camera"/>).
    /// </summary>
    /// <param name="hud">The HUD presenter.</param>
    /// <param name="aim">The aim preview port.</param>
    /// <param name="camera">Resolves the gameplay camera.</param>
    /// <returns>The layer.</returns>
    public static AimLayer InstallAim(HudPresenter hud, IAimArcSource aim, System.Func<Camera3D?> camera)
    {
        var layer = new AimLayer(new AimRibbonView(aim, camera));
        hud.AddLayer(layer, 0);
        return layer;
    }

    /// <summary>
    /// Builds the flight view. Wire after PR 87 (<paramref name="flights"/> over the flight mirror) and after WP-A part 2: the boot code
    /// parents the result under <c>World/Flights</c> and registers it with the world presenter.
    /// </summary>
    /// <param name="flights">The flight mirror port.</param>
    /// <returns>The view.</returns>
    public static FlightView CreateFlights(IFlightSource flights) => new(flights);
}
