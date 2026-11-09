using Godot;
using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Game.Presentation;

namespace PerformativeMail.Game.Views.Inventory;

/// <summary>The inventory HUD layer: the hotbar strip, the panel and the Intake grid in one <see cref="PanelGridView"/>.</summary>
public partial class InventoryLayer : CanvasLayer
{
    /// <summary>Creates the layer around its single view.</summary>
    /// <param name="view">The view.</param>
    public InventoryLayer(PanelGridView view)
    {
        Name = "Inventory";
        AddChild(view);
    }
}

/// <summary>Entry point for the boot assembly.</summary>
public static class InventoryLayers
{
    /// <summary>The layer's place in the HUD stack, above the persistent layer.</summary>
    public const int InventoryOrder = 2;

    /// <summary>
    /// Adds the inventory layer to <paramref name="hud"/>. Wire after PR 87 (own-container mirror as <paramref name="source"/>,
    /// <c>HotbarSelect</c> op in <paramref name="actions"/>) and after WP-A part 2 (Intake open state, the <c>CheckInteraction</c> gate).
    /// </summary>
    /// <param name="hud">The HUD presenter.</param>
    /// <param name="source">The panel read model.</param>
    /// <param name="actions">The op sender.</param>
    /// <param name="gate">The Intake range gate.</param>
    /// <returns>The layer.</returns>
    public static InventoryLayer Install(HudPresenter hud, IInventoryPanelSource source, IInventoryActions actions, IIntakeRangeGate gate)
    {
        var layer = new InventoryLayer(new PanelGridView(source, actions, gate));
        hud.AddLayer(layer, InventoryOrder);
        return layer;
    }
}
