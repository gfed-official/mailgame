using System;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.Inventory;
using GridContainer = PerformativeMail.Sim.Inventory.GridContainer;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// Draws one grid container as the player should see it (doc 09 §2.1): cells, the stacks in
    /// them, counts and house numbers; a stack the server has not confirmed where it is shown is
    /// drawn faint (doc 09 §2.5). The pointer is only reported: the <see cref="InventoryPanel"/>
    /// decides what a press, a drag and a release mean.
    /// </summary>
    public partial class GridView : Control
    {
        public const float Cell = 44f;
        /// <summary>Doc 09 §2.5: a moved item shows at 60 % until the server confirms.</summary>
        public const float PendingAlpha = 0.6f;
        /// <summary>The place a stack was picked up from, while it hangs on the cursor.</summary>
        private const float LiftedAlpha = 0.25f;

        private static readonly Color CellColour = new Color(0.10f, 0.11f, 0.13f, 0.82f);
        private static readonly Color CellBorder = new Color(1f, 1f, 1f, 0.16f);
        private static readonly Color Ink = new Color("1b1b1e");
        private static readonly Color ItemColour = new Color("8a8f98");

        /// <summary>Left button down at a canvas position; the flag is the split modifier (Ctrl / Cmd).</summary>
        public event Action<Vector2, bool>? PointerPressed;
        /// <summary>The pointer moved; while the button is held this view keeps hearing it, wherever it goes.</summary>
        public event Action<Vector2>? PointerMoved;
        public event Action<Vector2>? PointerReleased;
        /// <summary>Right button down: "never mind".</summary>
        public event Action? PointerCancelled;

        /// <summary>Column highlighted as the held slot (the hotbar strip); -1 for none.</summary>
        public int SelectedColumn = -1;
        /// <summary>Print 1..n under the cells (the hotbar strip).</summary>
        public bool ShowSlotNumbers;
        /// <summary>The drag of the screen this view belongs to, so the lifted stack is drawn as lifted.</summary>
        public InventoryDrag? Drag;

        private ClientRuntime _client = null!;
        private Func<ClientWorld, GridContainer?> _pick = null!;
        private uint _drawnId, _drawnVersion;
        private int _drawnRevision = -1;
        private int _drawnSelected = -2;

        public void Setup(ClientRuntime client, Func<ClientWorld, GridContainer?> pick)
        {
            _client = client;
            _pick = pick;
            FocusMode = FocusModeEnum.None;
            Resize();
        }

        /// <summary>The container on show: the confirmed mirror plus the moves still on their way.</summary>
        public GridContainer? Shown => _client?.World == null ? null : _pick(_client.World);

        /// <summary>True if the canvas position is on this grid and the grid is on screen.</summary>
        public bool Covers(Vector2 canvasPosition) => IsVisibleInTree() && Shown != null && GetGlobalRect().HasPoint(canvasPosition);

        public Vector2I CellAt(Vector2 canvasPosition)
        {
            var local = (canvasPosition - GlobalPosition) / Cell;
            return new Vector2I(Mathf.FloorToInt(local.X), Mathf.FloorToInt(local.Y));
        }

        public Vector2 CellCentre(int x, int y) => GlobalPosition + new Vector2(x + 0.5f, y + 0.5f) * Cell;

        private void Resize()
        {
            var c = Shown;
            CustomMinimumSize = c == null ? Vector2.Zero : new Vector2(c.Cols * Cell, c.Rows * Cell);
        }

        public override void _Process(double delta)
        {
            var c = Shown;
            uint id = c?.Id ?? 0, version = c?.Version ?? 0;
            int revision = _client?.World?.Prediction.Revision ?? -1;
            if (id == _drawnId && version == _drawnVersion && revision == _drawnRevision && SelectedColumn == _drawnSelected) return;
            if (id != _drawnId) Resize();
            _drawnId = id;
            _drawnVersion = version;
            _drawnRevision = revision;
            _drawnSelected = SelectedColumn;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var c = Shown;
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

            bool lifting = Drag != null && Drag.Active && Drag.FromContainer == c.Id;
            foreach (var e in c.Entries)
            {
                var rect = new Rect2(e.X * Cell + 3f, e.Y * Cell + 3f, e.W * Cell - 6f, e.H * Cell - 6f);
                int count = e.Stack.Count;
                float alpha = world.Prediction.IsPending(c.Id, e) ? PendingAlpha : 1f;
                if (lifting && Drag!.EntryId == e.EntryId)
                {
                    // A whole stack leaves a faint mark where it lay; a split leaves the rest of the stack.
                    int carried = Drag.Count;
                    if (carried >= count) alpha = LiftedAlpha;
                    else count -= carried;
                }
                DrawStack(this, font, world, e.Stack, rect, count, alpha);
            }

            if (SelectedColumn >= 0 && SelectedColumn < c.Cols)
                DrawRect(new Rect2(SelectedColumn * Cell, 0f, Cell, c.Rows * Cell), Palette.Amber, false, 3f);
        }

        /// <summary>One stack in a rectangle of a canvas item: in a cell of a grid, or on the cursor.</summary>
        public static void DrawStack(CanvasItem canvas, Font font, ClientWorld world, ItemStack stack, Rect2 rect, int count, float alpha)
        {
            var ink = new Color(Ink, alpha);
            if (stack.Kind == StackKind.Mail)
            {
                canvas.DrawRect(rect, new Color(Palette.MailColour(stack.DefIndex), alpha));
                var dest = world.Map.FindDestination(stack.Address);
                // The stripe is the colour of the house the mail is addressed to.
                if (dest != null) canvas.DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 7f)), new Color(Palette.HouseColour(dest.Variant), alpha));
                canvas.DrawString(font, new Vector2(rect.Position.X + 2f, rect.Position.Y + 23f), "#" + stack.Address.Number, HorizontalAlignment.Left, rect.Size.X - 2f, 13, ink);
            }
            else
            {
                canvas.DrawRect(rect, new Color(ItemColour, alpha));
                canvas.DrawString(font, new Vector2(rect.Position.X + 2f, rect.Position.Y + 23f), HudText.Name(world.Content, stack), HorizontalAlignment.Left, rect.Size.X - 2f, 11, ink);
            }
            if (count > 1)
                canvas.DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 3f), "x" + count, HorizontalAlignment.Right, rect.Size.X - 2f, 12, ink);
        }

        public override void _GuiInput(InputEvent ev)
        {
            switch (ev)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                    if (button.Pressed) PointerPressed?.Invoke(GlobalPosition + button.Position, button.IsCommandOrControlPressed());
                    else PointerReleased?.Invoke(GlobalPosition + button.Position);
                    AcceptEvent();
                    break;
                case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                    PointerCancelled?.Invoke();
                    AcceptEvent();
                    break;
                case InputEventMouseMotion motion:
                    PointerMoved?.Invoke(GlobalPosition + motion.Position);
                    break;
            }
        }
    }
}
