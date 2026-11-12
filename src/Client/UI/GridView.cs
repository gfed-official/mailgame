using System;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.Inventory;
using GridContainer = PerformativeMail.Sim.Inventory.GridContainer;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// Draws one grid container mirror (doc 09 §2.1): cells, the stacks in them, counts and house
    /// numbers. Rough-loop interaction is a click on a stack (quick-move); drag and drop comes later.
    /// </summary>
    public partial class GridView : Control
    {
        public const float Cell = 44f;

        private static readonly Color CellColour = new Color(0.10f, 0.11f, 0.13f, 0.82f);
        private static readonly Color CellBorder = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color Ink = new Color("1b1b1e");

        public event Action<GridContainer, GridEntry>? EntryClicked;

        /// <summary>Column highlighted as the held slot (the hotbar strip); -1 for none.</summary>
        public int SelectedColumn = -1;
        /// <summary>Print 1..n under the cells (the hotbar strip).</summary>
        public bool ShowSlotNumbers;

        private ClientRuntime _client = null!;
        private Func<ClientWorld, GridContainer?> _pick = null!;
        private uint _drawnId, _drawnVersion;
        private int _drawnSelected = -2;

        public void Setup(ClientRuntime client, Func<ClientWorld, GridContainer?> pick)
        {
            _client = client;
            _pick = pick;
            FocusMode = FocusModeEnum.None;
            Resize();
        }

        private GridContainer? Container => _client?.World == null ? null : _pick(_client.World);

        private void Resize()
        {
            var c = Container;
            CustomMinimumSize = c == null ? Vector2.Zero : new Vector2(c.Cols * Cell, c.Rows * Cell);
        }

        public override void _Process(double delta)
        {
            var c = Container;
            uint id = c?.Id ?? 0, version = c?.Version ?? 0;
            if (id == _drawnId && version == _drawnVersion && SelectedColumn == _drawnSelected) return;
            if (id != _drawnId) Resize();
            _drawnId = id;
            _drawnVersion = version;
            _drawnSelected = SelectedColumn;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var c = Container;
            var world = _client?.World;
            if (c == null || world == null) return;
            var font = GetThemeDefaultFont();

            for (int y = 0; y < c.Rows; y++)
            {
                for (int x = 0; x < c.Cols; x++)
                {
                    var rect = new Rect2(x * Cell + 1f, y * Cell + 1f, Cell - 2f, Cell - 2f);
                    DrawRect(rect, CellColour);
                    DrawRect(rect, CellBorder, false, 1f);
                    if (c.IsReserved(x, y))
                        DrawString(font, new Vector2(x * Cell, y * Cell + Cell * 0.58f), "hands", HorizontalAlignment.Center, Cell, 11, new Color(1f, 1f, 1f, 0.6f));
                    if (ShowSlotNumbers && y == c.Rows - 1)
                        DrawString(font, new Vector2(x * Cell + 4f, y * Cell + 13f), (x + 1).ToString(), HorizontalAlignment.Left, -1f, 10, new Color(1f, 1f, 1f, 0.5f));
                }
            }

            foreach (var e in c.Entries)
            {
                var rect = new Rect2(e.X * Cell + 3f, e.Y * Cell + 3f, e.W * Cell - 6f, e.H * Cell - 6f);
                var stack = e.Stack;
                if (stack.Kind == StackKind.Mail)
                {
                    DrawRect(rect, Palette.MailColour(stack.DefIndex));
                    var dest = world.Map.FindDestination(stack.Address);
                    // The stripe is the colour of the house the mail is addressed to.
                    if (dest != null) DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 7f)), Palette.HouseColour(dest.Variant));
                    DrawString(font, new Vector2(rect.Position.X + 2f, rect.Position.Y + 23f), "#" + stack.Address.Number, HorizontalAlignment.Left, rect.Size.X - 2f, 13, Ink);
                }
                else
                {
                    DrawRect(rect, new Color("8a8f98"));
                    DrawString(font, new Vector2(rect.Position.X + 2f, rect.Position.Y + 23f), HudText.Name(world.Content, stack), HorizontalAlignment.Left, rect.Size.X - 2f, 11, Ink);
                }
                if (stack.Count > 1)
                    DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 3f), "x" + stack.Count, HorizontalAlignment.Right, rect.Size.X - 2f, 12, Ink);
            }

            if (SelectedColumn >= 0 && SelectedColumn < c.Cols)
                DrawRect(new Rect2(SelectedColumn * Cell, 0f, Cell, c.Rows * Cell), Palette.Amber, false, 3f);
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
            var c = Container;
            if (c == null) return;
            var entry = c.EntryAt((int)(click.Position.X / Cell), (int)(click.Position.Y / Cell));
            if (entry == null) return;
            EntryClicked?.Invoke(c, entry);
            AcceptEvent();
        }
    }
}
