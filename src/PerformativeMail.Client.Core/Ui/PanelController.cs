using PerformativeMail.Client.Core.Input;
using PerformativeMail.Sim.ClientLayer;
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
/// Both arrive as the router's immediates (<see cref="ActionId.HotbarSlot1"/> to <see cref="ActionId.HotbarSlot8"/>, <see cref="ActionId.QuickMove"/>),
/// so the action map's bindings and context gates apply (ADR-0012 section 6); the view only supplies the pointer.
/// </summary>
/// <example><code>var c = new PanelController(src, actions, gate, new PanelLayout(w, h)); c.RegisterSinks(router); c.SetPointer(x, y);</code></example>
public sealed class PanelController : IImmediateActionSink
{
    private readonly IInventoryPanelSource _source;
    private readonly IInventoryActions _actions;
    private readonly IIntakeRangeGate _gate;
    private int _predicted = -1;
    private int _lastMirror = -1;
    private int _pointerX = -1;
    private int _pointerY = -1;

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
        Layout = layout;
    }

    /// <summary>The current layout; the view replaces it when the viewport size changes.</summary>
    public PanelLayout Layout { get; set; }

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

    /// <summary>Hotbar slot <paramref name="slot"/> as a drawable entry (footprint 0, entry id 0 when empty), so the view names no Sim.ClientLayer type (ADR-0002).</summary>
    /// <param name="slot">The slot, 0 to 7.</param>
    /// <returns>The entry.</returns>
    public PanelGridEntry HotbarEntry(int slot)
    {
        PanelEntryFacts f = _source.Panel.Entry(PanelSection.Hotbar, slot);
        return new PanelGridEntry(f.EntryId.Value, 0, 0, 0, 0, f.Category, f.KindKey, f.Units, f.Address);
    }

    /// <summary>Registers this controller as the sink of the eight hotbar-slot actions and of Quick move.</summary>
    /// <param name="router">The input router.</param>
    public void RegisterSinks(InputRouter router)
    {
        for (ushort id = ActionId.HotbarSlot1; id <= ActionId.HotbarSlot8; id++)
        {
            router.RegisterSink(id, this);
        }

        router.RegisterSink(ActionId.QuickMove, this);
    }

    /// <summary>The pointer in panel pixels, sampled by the view each frame; Quick move hit-tests it.</summary>
    /// <param name="x">Pointer x.</param>
    /// <param name="y">Pointer y.</param>
    public void SetPointer(int x, int y)
    {
        _pointerX = x;
        _pointerY = y;
    }

    /// <inheritdoc />
    public void OnImmediate(in ImmediateAction a)
    {
        if (a.EdgeKind != EdgeKind.Press)
        {
            return;
        }

        if (a.ActionId == ActionId.QuickMove)
        {
            _ = OnShiftClick(_pointerX, _pointerY);
        }
        else if (a.ActionId is >= ActionId.HotbarSlot1 and <= ActionId.HotbarSlot8)
        {
            _ = OnHotbarKey(a.ActionId - ActionId.HotbarSlot1 + 1);
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

        uint entry = PanelLayout.EntryAt(intake, Layout.IntakeBounds(intake.Width, intake.Height), px, py);
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
