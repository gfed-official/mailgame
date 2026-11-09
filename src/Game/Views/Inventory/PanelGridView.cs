using Godot;
using PerformativeMail.Client.Core.Ui;
using PerformativeMail.Game.Presentation;
using PerformativeMail.Sim.ClientLayer;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Game.Views.Inventory;

/// <summary>
/// The one custom-drawn inventory <c>Control</c> (grid-inventory 023/024, greybox): hotbar strip (always), own 8x2 grid and the open
/// Intake 20x16 grid (while the panel is open). Shift-click quick-moves an Intake entry, keys 1 to 8 select a slot. All logic lives in
/// <see cref="PanelController"/>; this node draws filled rects and strings only (no line primitives: 4.7 removed the feathering).
/// </summary>
public partial class PanelGridView : Control, IPresentedView
{
    private static readonly Color _cellFill = new(0.12f, 0.12f, 0.14f, 0.85f);
    private static readonly Color _border = new(0.55f, 0.55f, 0.6f, 1f);
    private static readonly Color _selected = new(0.95f, 0.8f, 0.2f, 1f);
    private static readonly Color _mail = new(0.85f, 0.85f, 0.75f, 1f);
    private static readonly Color _item = new(0.45f, 0.65f, 0.85f, 1f);
    private static readonly Color _ink = new(0.05f, 0.05f, 0.05f, 1f);

    private readonly IInventoryPanelSource _source;
    private readonly IInventoryActions _actions;
    private readonly IIntakeRangeGate _gate;
    private PanelController _controller;
    private PanelLayout _layout = new(1920, 1080);
    private Vector2I _viewSize;

    /// <summary>Creates the view over its sources.</summary>
    /// <param name="source">The panel read model.</param>
    /// <param name="actions">The op sender.</param>
    /// <param name="gate">The Intake range gate.</param>
    public PanelGridView(IInventoryPanelSource source, IInventoryActions actions, IIntakeRangeGate gate)
    {
        _source = source;
        _actions = actions;
        _gate = gate;
        _controller = new PanelController(source, actions, gate, _layout);
        Name = "PanelGridView";
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <inheritdoc />
    public void Present(int alphaPermille)
    {
        var size = new Vector2I((int)Size.X, (int)Size.Y);
        if (size != _viewSize && size.X > 0)
        {
            _viewSize = size;
            _layout = new PanelLayout(size.X, size.Y);
            _controller = new PanelController(_source, _actions, _gate, _layout);
        }

        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key && key.Keycode is >= Key.Key1 and <= Key.Key8)
        {
            if (_controller.OnHotbarKey((int)(key.Keycode - Key.Key1) + 1))
            {
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left, ShiftPressed: true } mouse
            && _controller.OnShiftClick((int)mouse.Position.X, (int)mouse.Position.Y) != PanelClickResult.Ignored)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        DrawHotbar();
        if (!_source.IsOpen)
        {
            return;
        }

        DrawGrid(_source.Own, _layout.OwnBounds(_source.Own.Width, _source.Own.Height));
        IGridSource? intake = _source.Intake;
        if (intake is not null)
        {
            DrawGrid(intake, _layout.IntakeBounds(intake.Width, intake.Height));
        }
    }

    private void DrawHotbar()
    {
        int selected = _controller.SelectedSlot;
        IPanelView panel = _source.Panel;
        Font font = ThemeDB.FallbackFont;
        for (int i = 0; i < PanelLayout.HotbarSlots; i++)
        {
            PxRect r = _layout.HotbarSlot(i);
            Frame(r, i == selected ? _selected : _border, i == selected ? 3 : 2);
            DrawString(font, new Vector2(r.X + 3, r.Y + 12), (i + 1).ToString(), HorizontalAlignment.Left, -1, 10, _border);
            if (i == 0)
            {
                DrawString(font, new Vector2(r.X + 4, r.Y + 30), "hands", HorizontalAlignment.Left, -1, 10, _border);
                continue;
            }

            PanelEntryFacts f = panel.Entry(PanelSection.Hotbar, i);
            if (f.IsEntry)
            {
                Glyph(r, f.Category, f.KindKey, f.Units);
            }
        }
    }

    private void DrawGrid(IGridSource grid, PxRect bounds)
    {
        DrawRect(new Rect2(bounds.X, bounds.Y, bounds.W, bounds.H), _cellFill);
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                Frame(new PxRect(bounds.X + (x * PanelLayout.Cell), bounds.Y + (y * PanelLayout.Cell), PanelLayout.Cell, PanelLayout.Cell), _border, 1);
            }
        }

        Frame(bounds, _border, 3);
        for (int i = 0; i < grid.EntryCount; i++)
        {
            PanelGridEntry e = grid.Entry(i);
            Glyph(PanelLayout.EntryRect(bounds, in e), e.Category, e.KindKey, e.Units);
        }
    }

    private void Glyph(PxRect r, ItemCategory category, ushort kind, byte units)
    {
        DrawRect(new Rect2(r.X + 2, r.Y + 2, r.W - 4, r.H - 4), category == ItemCategory.Mail ? _mail : _item);
        string label = category == ItemCategory.Mail ? "L" + kind : "#" + kind;
        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(r.X + 6, r.Y + 20), label, HorizontalAlignment.Left, -1, 14, _ink);
        if (units > 1)
        {
            DrawString(font, new Vector2(r.X + 6, r.Y + r.H - 6), "x" + units, HorizontalAlignment.Left, -1, 11, _ink);
        }
    }

    /// <summary>A frame as four filled rects, since 4.7 draws no feathered lines.</summary>
    private void Frame(PxRect r, Color c, int t)
    {
        DrawRect(new Rect2(r.X, r.Y, r.W, t), c);
        DrawRect(new Rect2(r.X, r.Y + r.H - t, r.W, t), c);
        DrawRect(new Rect2(r.X, r.Y, t, r.H), c);
        DrawRect(new Rect2(r.X + r.W - t, r.Y, t, r.H), c);
    }
}
