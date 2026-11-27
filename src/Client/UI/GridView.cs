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

        private static readonly Color Ink = new Color("1b1b1e");
        private static readonly Color ItemColour = new Color("b9bec8");
        /// <summary>The count on a stack: white on a dark rim, so it reads on cream mail and on grey items alike.</summary>
        private const int CountSize = 14, CountRim = 5;

        // The teal squares of the concept video's hotbar; the held slot is the gold one.
        private static readonly StyleBoxFlat CellBox = Rounded(new Color(0.16f, 0.39f, 0.52f, 0.86f), new Color(0.62f, 0.84f, 0.94f, 0.45f), 6, 1);
        private static readonly StyleBoxFlat ReservedBox = Rounded(new Color(0.10f, 0.22f, 0.30f, 0.8f), new Color(0.62f, 0.84f, 0.94f, 0.25f), 6, 1);
        private static readonly StyleBoxFlat SelectedBox = Rounded(new Color("f0a020"), new Color("ffe08a"), 6, 2);
        private static readonly StyleBoxFlat StackBox = Rounded(Colors.White, new Color(0f, 0f, 0f, 0.35f), 4, 1);
        // The HUD's hotbar (UI polish plan item 8; ref Valheim, Peak): the held slot keeps its teal, rimmed thick in gold
        // and lifted; an empty slot is half there.
        private static readonly StyleBoxFlat HeldBox = Rounded(new Color(0.20f, 0.47f, 0.62f, 0.95f), new Color("ffd24a"), 7, 3);
        private static readonly StyleBoxFlat EmptyBox = Rounded(new Color(0.16f, 0.39f, 0.52f, 0.43f), new Color(0.62f, 0.84f, 0.94f, 0.22f), 6, 1);
        private static readonly StyleBoxFlat BadgeBox = Rounded(new Color(0.05f, 0.08f, 0.12f, 0.92f), new Color(1f, 1f, 1f, 0.25f), 4, 1);
        /// <summary>How far the held slot rises out of the bar, px.</summary>
        public const float HeldLift = 5f;

        private static StyleBoxFlat Rounded(Color fill, Color border, int radius, int borderWidth)
        {
            var box = new StyleBoxFlat { BgColor = fill, BorderColor = border, AntiAliasing = true };
            box.SetCornerRadiusAll(radius);
            box.SetBorderWidthAll(borderWidth);
            return box;
        }

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
        /// <summary>The HUD's hotbar look: the held slot lifted in a gold rim, empty slots faint, a tool's uses as a badge.</summary>
        public bool HotbarStyle;
        /// <summary>The badge in a slot's corner ("90" Stone shots on a Slingshot), by its stack; null or empty for none.</summary>
        public Func<ItemStack, string>? Badge;
        private string _drawnBadges = "";
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
            string badges = "";
            if (Badge != null && c != null)
                foreach (var e in c.Entries) badges += Badge(e.Stack) + ",";
            if (id == _drawnId && version == _drawnVersion && revision == _drawnRevision && SelectedColumn == _drawnSelected && badges == _drawnBadges) return;
            _drawnBadges = badges;
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
                    var rect = new Rect2(x * Cell + 2f, y * Cell + 2f, Cell - 4f, Cell - 4f);
                    bool reserved = c.IsReserved(x, y);
                    if (HotbarStyle)
                    {
                        bool held = x == SelectedColumn;
                        if (held) rect.Position -= new Vector2(0f, HeldLift);
                        DrawStyleBox(held ? HeldBox : reserved ? ReservedBox : c.EntryAt(x, y) == null ? EmptyBox : CellBox, rect);
                    }
                    else DrawStyleBox(x == SelectedColumn ? SelectedBox : reserved ? ReservedBox : CellBox, rect);
                    float lift = HotbarStyle && x == SelectedColumn ? HeldLift : 0f;
                    if (reserved)
                        DrawString(font, new Vector2(x * Cell, y * Cell + Cell * 0.58f - lift), "hands", HorizontalAlignment.Center, Cell, 11, new Color(1f, 1f, 1f, 0.75f));
                    if (ShowSlotNumbers && y == c.Rows - 1)
                        DrawString(font, new Vector2(x * Cell + 6f, y * Cell + 15f - lift), (x + 1).ToString(), HorizontalAlignment.Left, -1f, 10, new Color(1f, 1f, 1f, 0.7f));
                }
            }

            bool lifting = Drag != null && Drag.Active && Drag.FromContainer == c.Id;
            foreach (var e in c.Entries)
            {
                var rect = new Rect2(e.X * Cell + 5f, e.Y * Cell + 5f, e.W * Cell - 10f, e.H * Cell - 10f);
                if (HotbarStyle && e.X == SelectedColumn) rect.Position -= new Vector2(0f, HeldLift);
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
                string badge = Badge?.Invoke(e.Stack) ?? "";
                if (badge.Length > 0)
                {
                    // Top right, over the corner of the slot.
                    float w = font.GetStringSize(badge, HorizontalAlignment.Left, -1f, 10).X + 7f;
                    var at = new Rect2(rect.End.X - w + 4f, rect.Position.Y - 5f, w, 14f);
                    DrawStyleBox(BadgeBox, at);
                    DrawString(font, new Vector2(at.Position.X + 3.5f, at.End.Y - 3f), badge, HorizontalAlignment.Left, -1f, 10, new Color(1f, 1f, 1f, alpha));
                }
            }

        }

        private const int TagFont = 9, TagMinFont = 7;

        /// <summary>
        /// A stack's tag on its bottom band, whole: drawn smaller, down to 7 px, until it fits the
        /// stack (the 0e940bc playtest read "CURSE" on a Letter at 1280×720).
        /// </summary>
        public static int TagFontSize(Font font, string tag, float stackWidth)
        {
            int size = TagFont;
            while (size > TagMinFont && TagWidth(font, tag, size) > stackWidth - 6f) size--;
            return size;
        }

        public static float TagWidth(Font font, string tag, int size) => font.GetStringSize(tag, HorizontalAlignment.Left, -1f, size).X;

        private static void DrawTag(CanvasItem canvas, Font font, Rect2 rect, string tag, Color colour)
        {
            int size = TagFontSize(font, tag, rect.Size.X);
            float w = TagWidth(font, tag, size);
            // Centred on the band, and never clipped: what does not fit at 7 px overhangs it evenly.
            canvas.DrawString(font, new Vector2(rect.GetCenter().X - w * 0.5f, rect.End.Y - 4f), tag, HorizontalAlignment.Left, -1f, size, colour);
        }

        /// <summary>One stack in a rectangle of a canvas item: in a cell of a grid, or on the cursor.</summary>
        public static void DrawStack(CanvasItem canvas, Font font, ClientWorld world, ItemStack stack, Rect2 rect, int count, float alpha)
        {
            var ink = new Color(Ink, alpha);
            StackBox.BorderColor = new Color(0f, 0f, 0f, 0.35f * alpha);
            if (stack.Kind == StackKind.Mail)
            {
                StackBox.BgColor = new Color(Palette.MailColour(stack.DefIndex), alpha);
                canvas.DrawStyleBox(StackBox, rect);
                var dest = world.Map.FindDestination(stack.Address);
                // The stripe is the colour of the house the mail is addressed to.
                if (dest != null) canvas.DrawRect(new Rect2(rect.Position + new Vector2(1f, 3f), new Vector2(rect.Size.X - 2f, 6f)), new Color(Palette.HouseColour(dest.Variant), alpha));
                canvas.DrawString(font, new Vector2(rect.Position.X + 3f, rect.Position.Y + 23f), "#" + stack.Address.Number, HorizontalAlignment.Left, rect.Size.X - 3f, 13, ink);
                // Doc 05 §6: cursed mail's glowing purple label — a purple rim and its word.
                if (stack.Cursed)
                {
                    var purple = new Color(Palette.Cursed, alpha);
                    canvas.DrawRect(rect.Grow(-1f), purple, filled: false, width: 3f);
                    canvas.DrawRect(new Rect2(rect.Position.X + 2f, rect.End.Y - 13f, rect.Size.X - 4f, 11f), new Color(Palette.Cursed, 0.85f * alpha));
                    DrawTag(canvas, font, rect, "CURSED", new Color(1f, 1f, 1f, alpha));
                }
                // Doc 05 §2.1: a Lost Parcel, from a felled Mega — an amber rim and its word.
                if (stack.Lost)
                {
                    canvas.DrawRect(rect.Grow(-1f), new Color(Palette.Lost, alpha), filled: false, width: 3f);
                    canvas.DrawRect(new Rect2(rect.Position.X + 2f, rect.End.Y - 13f, rect.Size.X - 4f, 11f), new Color(Palette.Lost, 0.85f * alpha));
                    DrawTag(canvas, font, rect, rect.Size.X - 6f < TagWidth(font, "LOST PARCEL", TagMinFont) ? "LOST" : "LOST PARCEL", new Color(0.1f, 0.07f, 0.02f, alpha));
                }
            }
            else
            {
                StackBox.BgColor = new Color(ItemColour, alpha);
                canvas.DrawStyleBox(StackBox, rect);
                string name = HudText.Name(world.Content, stack);
                canvas.DrawString(font, new Vector2(rect.Position.X + 3f, rect.Position.Y + 16f), name, HorizontalAlignment.Left, rect.Size.X - 3f, 11, ink);
                // A tool's letter-mark until there are icons (UI polish plan item 8): "Ax", "RH", under its name.
                if (world.Content.Items[stack.DefIndex].IsTool && rect.Size.Y >= 30f)
                {
                    var display = UiTheme.DisplayFont ?? font;
                    string mark = HudText.Monogram(name);
                    float w = display.GetStringSize(mark, HorizontalAlignment.Left, -1f, 18).X;
                    canvas.DrawString(display, new Vector2(rect.GetCenter().X - w * 0.5f, rect.Position.Y + Mathf.Max(34f, rect.Size.Y * 0.62f)), mark, HorizontalAlignment.Left, -1f, 18, new Color(Ink, 0.55f * alpha));
                }
            }
            if (count <= 1) return;
            // Bottom right, over the edge of the stack so two neighbours' counts never run together.
            var at = new Vector2(rect.Position.X, rect.End.Y + 2f);
            string text = HudText.Count(count);
            canvas.DrawStringOutline(font, at, text, HorizontalAlignment.Right, rect.Size.X + 3f, CountSize, CountRim, new Color(0f, 0f, 0f, 0.9f * alpha));
            canvas.DrawString(font, at, text, HorizontalAlignment.Right, rect.Size.X + 3f, CountSize, new Color(1f, 1f, 1f, alpha));
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
