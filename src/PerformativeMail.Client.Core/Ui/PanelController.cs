using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Core.Ui;

/// <summary>What a panel click did.</summary>
public enum PanelClickResult : byte
{
    /// <summary>Nothing under the point, or the panel or Intake is closed.</summary>
    Ignored = 0,

    /// <summary>A quick-move was sent.</summary>
    Sent = 1,

    /// <summary>The range gate refused; nothing sent.</summary>
    OutOfRange = 2,

    /// <summary>The request port was busy; nothing sent.</summary>
    Busy = 3,
}

/// <summary>
/// The panel's interaction logic (grid-inventory 024, minimal): Shift-click quick-moves an Intake entry to the panel behind the
/// <c>CheckInteraction</c> range gate, and keys 1 to 8 select a hotbar slot, shown at once and reconciled with the mirror. No drag, rotate or drop.
/// </summary>
/// <example><code>var c = new PanelController(src, actions, gate, new PanelLayout(w, h)); c.OnShiftClick(x, y); c.OnHotbarKey(3);</code></example>
public sealed class PanelController
{
    private readonly IInventoryPanelSource _source;
    private readonly IInventoryActions _actions;
    private readonly IIntakeRangeGate _gate;
    private readonly PanelLayout _layout;
    private int _predicted = -1;
    private int _lastMirror = -1;

    /// <summary>Creates the controller.</summary>
    /// <param name="source">The panel read model.</param>
    /// <param name="actions">The op sender.</param>
    /// <param name="gate">The Intake range gate.</param>
    /// <param name="layout">The current layout.</param>
    public PanelController(IInventoryPanelSource source, IInventoryActions actions, IIntakeRangeGate gate, PanelLayout layout)
    {
        _source = source;
        _actions = actions;
        _gate = gate;
        _layout = layout;
    }

    /// <summary>The slot to highlight: the key just pressed until the mirror changes, else the mirror's.</summary>
    public int SelectedSlot
    {
        get
        {
            int mirror = _source.SelectedSlot;
            if (mirror != _lastMirror)
            {
                _lastMirror = mirror;
                _predicted = -1;
            }

            return _predicted >= 0 ? _predicted : mirror;
        }
    }

    /// <summary>Selects hotbar slot <paramref name="key"/> (1 to 8, as printed on the keys). Works with the panel closed.</summary>
    /// <param name="key">1 to 8.</param>
    /// <returns>True when the select was sent.</returns>
    public bool OnHotbarKey(int key)
    {
        if (key is < 1 or > PanelLayout.HotbarSlots)
        {
            return false;
        }

        _ = SelectedSlot;
        if (!_actions.SelectHotbar(key - 1))
        {
            return false;
        }

        _predicted = key - 1;
        return true;
    }

    /// <summary>Shift-click at a point: an Intake entry quick-moves to the panel.</summary>
    /// <param name="px">Point x.</param>
    /// <param name="py">Point y.</param>
    /// <returns>What happened.</returns>
    public PanelClickResult OnShiftClick(int px, int py)
    {
        IGridSource? intake = _source.Intake;
        if (!_source.IsOpen || intake is null)
        {
            return PanelClickResult.Ignored;
        }

        uint entry = PanelLayout.EntryAt(intake, _layout.IntakeBounds(intake.Width, intake.Height), px, py);
        if (entry == 0)
        {
            return PanelClickResult.Ignored;
        }

        if (_gate.Check() != InteractionCheckKind.Ok)
        {
            return PanelClickResult.OutOfRange;
        }

        return _actions.QuickMove(intake.ContainerId, entry) ? PanelClickResult.Sent : PanelClickResult.Busy;
    }
}
