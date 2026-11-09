using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Sim.ClientLayer;

namespace PerformativeMail.Game.Boot;

/// <summary>
/// The inventory panel source boot step 7 binds until the panel port exists (WP-A part 2, PR 89, and Evan's answer on card t316s1406;
/// TD-260): closed, slot 0 selected, every hotbar slot empty, an empty own grid and no Intake. Stateless.
/// </summary>
/// <example><code>InventoryLayers.Install(hud, EmptyInventoryPanel.Instance, actions, gate, router: null);</code></example>
public sealed class EmptyInventoryPanel : IInventoryPanelSource, IPanelView, IGridSource
{
    /// <summary>The one instance.</summary>
    public static EmptyInventoryPanel Instance { get; } = new();

    private EmptyInventoryPanel()
    {
    }

    /// <inheritdoc />
    public bool IsOpen => false;

    /// <inheritdoc />
    public int SelectedSlot => 0;

    /// <inheritdoc />
    public IPanelView Panel => this;

    /// <inheritdoc />
    public IGridSource Own => this;

    /// <inheritdoc />
    public IGridSource? Intake => null;

    /// <inheritdoc />
    public int CursorSlot => -1;

    /// <inheritdoc />
    public uint ContainerId => 0;

    /// <inheritdoc />
    public int Width => 0;

    /// <inheritdoc />
    public int Height => 0;

    /// <inheritdoc />
    int IGridSource.EntryCount => 0;

    /// <inheritdoc />
    int IPanelView.EntryCount(PanelSection s) => 0;

    /// <inheritdoc />
    public PanelEntryFacts Entry(PanelSection s, int i) => default;

    /// <inheritdoc />
    public PanelGridEntry Entry(int i) => throw new ArgumentOutOfRangeException(nameof(i));
}
