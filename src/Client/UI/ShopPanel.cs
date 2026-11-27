using System.Collections.Generic;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The shop (doc 09 §2.8; UI polish plan item 6, ref Webfishing, DRG): on the right of the inventory screen, the
    /// counter in sections — Supplies, Tools, Vehicles, Blueprints — each a row of square tiles: a swatch with the
    /// item's letter-mark, its key and count as badges, its name, a price chip and a tag for what stands in the way.
    /// What it is and the whole note are the tile's tooltip. A special has a gold frame and its old price struck
    /// through; a tile that cannot be bought as things stand is dimmed, its chip red when the gold is short. A number
    /// key or a click buys. Every string comes from <see cref="HudText"/>.
    /// </summary>
    public partial class ShopPanel : VBoxContainer
    {
        /// <summary>How wide the sections may run before the next wraps under them (Tools and Vehicles share a row), px.</summary>
        private const float FlowWidth = 9 * ShopTile.Width + 8 * TileGap + 24f;
        private const int TileGap = 6;

        private ClientRuntime _client = null!;
        private readonly List<ShopTile> _tiles = new List<ShopTile>();
        private readonly List<(VBoxContainer Box, Label Title, Godot.GridContainer Grid)> _sections = new List<(VBoxContainer, Label, Godot.GridContainer)>();
        private Label _title = null!;
        private HFlowContainer _flow = null!;
        private Label _hint = null!;
        private string _layout = "";

        /// <summary>Everything the panel says, top to bottom (tooltips aside); empty while it is not shown.</summary>
        public string Text
        {
            get
            {
                if (!IsVisibleInTree()) return "";
                var lines = new List<string> { _title.Text };
                foreach (var section in _sections)
                {
                    if (!section.Box.Visible) continue;
                    lines.Add(section.Title.Text);
                    foreach (var child in section.Grid.GetChildren())
                    {
                        if (child is not ShopTile tile || !tile.Visible) continue;
                        foreach (var line in tile.Lines)
                            if (line.Length > 0) lines.Add(line);
                    }
                }
                lines.Add(_hint.Text);
                return string.Join("\n", lines);
            }
        }

        /// <summary>How many cards lie on the counter, how many of them are drawn greyed, how many framed as specials.</summary>
        public int CardCount { get; private set; }
        public int GreyedCount { get; private set; }
        public int SpecialCount { get; private set; }
        /// <summary>The tiles by their place on the shelf (their key), for the smoke's "nothing is clipped" check.</summary>
        public Control CardAt(int card) => _tiles[card];
        /// <summary>The request the last click on a card sent; 0 if it sent none.</summary>
        public uint LastRequestId { get; private set; }

        public void Setup(ClientRuntime client)
        {
            _client = client;
            Name = "Shop";
            Visible = false;
            AddThemeConstantOverride("separation", 6);

            _title = Hud.MakeDisplayLabel("Title", 20, HorizontalAlignment.Left);
            _title.Modulate = Palette.Amber;
            AddChild(_title);
            _flow = new HFlowContainer { Name = "Sections", CustomMinimumSize = new Vector2(FlowWidth, 0f), MouseFilter = MouseFilterEnum.Ignore };
            _flow.AddThemeConstantOverride("h_separation", 16);
            _flow.AddThemeConstantOverride("v_separation", 6);
            AddChild(_flow);
            for (int k = 0; k < HudText.ShopSectionNames.Length; k++)
            {
                var box = new VBoxContainer { Name = HudText.ShopSectionNames[k], MouseFilter = MouseFilterEnum.Ignore };
                box.AddThemeConstantOverride("separation", 3);
                var title = Hud.MakeDisplayLabel("Title", 14, HorizontalAlignment.Left);
                title.Text = HudText.ShopSectionNames[k];
                title.Modulate = SectionColours[k].Lightened(0.35f);
                box.AddChild(title);
                var grid = new Godot.GridContainer { Name = "Tiles", MouseFilter = MouseFilterEnum.Ignore };
                grid.AddThemeConstantOverride("h_separation", TileGap);
                grid.AddThemeConstantOverride("v_separation", TileGap);
                box.AddChild(grid);
                _flow.AddChild(box);
                _sections.Add((box, title, grid));
            }
            _hint = Hud.MakeLabel("Hint", 12, HorizontalAlignment.Left);
            _hint.Modulate = new Color(1f, 1f, 1f, 0.7f);
            _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _hint.CustomMinimumSize = new Vector2(FlowWidth, 0f);
            _hint.Text = HudText.ShopHint;
            AddChild(_hint);
        }

        /// <summary>The swatch of each section: kraft for supplies, steel for tools, postal red for vehicles, blueprint blue.</summary>
        internal static readonly Color[] SectionColours = { new Color("c9a66b"), new Color("7d8fa3"), new Color("c0392b"), new Color("2f6fde") };

        /// <summary>What a click on the card at <paramref name="place"/> does: it buys what the card shows, if that is to be had.</summary>
        public void Click(int place)
        {
            LastRequestId = 0;
            var world = _client?.World;
            if (world == null || !world.ShopOpen) return;
            var shelf = world.ShopShelf;
            if (place >= 0 && place < shelf.Count) LastRequestId = _client!.Buy(shelf[place]);
        }

        public void Refresh(ClientWorld world)
        {
            Visible = world.ShopOpen;
            if (!Visible) return;

            _title.Text = HudText.ShopTitle(world);
            var shelf = world.ShopShelf;
            while (_tiles.Count < shelf.Count) AddTile();
            Arrange(shelf);
            int greyed = 0, specials = 0;
            for (int i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i];
                tile.Visible = i < shelf.Count;
                if (!tile.Visible) continue;
                var def = shelf[i];
                bool special = world.IsSpecial(def), buying = world.IsBuying(def);
                var state = world.ShopState(def);
                bool live = state == OpResult.Ok && !buying;
                if (!live) greyed++;
                if (special) specials++;
                tile.Show(HudText.ShopTile(world, def, i), HudText.Monogram(HudText.ShopTileName(def)), i < 12 ? HudText.CardKey(i).Trim('[', ']') : "",
                    HudText.ShopCountBadge(def), HudText.ShopChip(world, def), HudText.ShopWasPrice(world, def), HudText.ShopTag(world, def),
                    SectionColours[(int)HudText.ShopSectionOf(def)], special, live, state == OpResult.CantAfford);
                tile.TooltipText = HudText.ShopTooltip(world, def);
            }
            CardCount = shelf.Count;
            GreyedCount = greyed;
            SpecialCount = specials;
        }

        /// <summary>Puts each tile in its section, in shelf order; only when what lies on the counter changed.</summary>
        private void Arrange(IReadOnlyList<ShopItemDef> shelf)
        {
            string layout = "";
            foreach (var def in shelf) layout += def.Id + ",";
            if (layout == _layout) return;
            _layout = layout;
            foreach (var tile in _tiles) tile.GetParent()?.RemoveChild(tile);
            var sections = HudText.ShopSections(shelf);
            for (int k = 0; k < sections.Length; k++)
            {
                var (box, _, grid) = _sections[k];
                box.Visible = sections[k].Count > 0;
                // One row as long as the flow is wide; a longer section wraps within itself.
                grid.Columns = System.Math.Max(1, System.Math.Min(sections[k].Count, 9));
                foreach (int place in sections[k]) grid.AddChild(_tiles[place]);
            }
        }

        private void AddTile()
        {
            int place = _tiles.Count;
            var tile = new ShopTile { Name = $"Card{place}" };
            tile.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Click(place);
            };
            _tiles.Add(tile);
        }
    }

    /// <summary>
    /// One square tile of the shop: drawn with StyleBoxes and strings (no art yet) — a swatch in its section's colour
    /// with a letter-mark, the key badge top left, the count badge top right, the name, the tag and the price chip.
    /// </summary>
    public partial class ShopTile : Control
    {
        public const float Width = 80f, Height = 84f;
        private static readonly Color Back = new Color(0.11f, 0.19f, 0.29f, 0.96f);
        private static readonly Color PlainFrame = new Color("6f8296");
        private static readonly Color Ink = new Color("1b1b1e");
        private static readonly Color ChipGreen = new Color("3d7a4f"), ChipRed = new Color("b23a3a"), ChipGold = new Color("b8860b");

        private readonly StyleBoxFlat _frame = Box(Back, 8), _swatch = Box(Colors.White, 6), _chip = Box(ChipGreen, 8), _badge = Box(new Color(0.05f, 0.08f, 0.12f, 0.92f), 4);
        private string _mark = "", _key = "", _count = "", _name = "", _price = "", _was = "", _tag = "";
        private Color _colour = Colors.White;
        private bool _special, _short;

        /// <summary>What the tile says, for the smoke: key and name, the chip (and the struck price), the tag.</summary>
        public string[] Lines { get; private set; } = System.Array.Empty<string>();

        public ShopTile()
        {
            CustomMinimumSize = new Vector2(Width, Height);
            MouseFilter = MouseFilterEnum.Stop;
            _frame.SetBorderWidthAll(1);
            _frame.BorderColor = PlainFrame;
            _chip.SetContentMarginAll(0f);
        }

        private static StyleBoxFlat Box(Color fill, int radius)
        {
            var box = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
            box.SetCornerRadiusAll(radius);
            return box;
        }

        public void Show(string[] lines, string mark, string key, string count, string price, string was, string tag, Color colour, bool special, bool live, bool tooDear)
        {
            Lines = lines;
            string name = lines[0];
            if (key.Length > 0) name = name.Substring(name.IndexOf(' ') + 2);
            if (count.Length > 0) name = name.Substring(0, name.Length - count.Length - 2);
            if (mark == _mark && key == _key && count == _count && name == _name && price == _price && was == _was && tag == _tag
                && colour == _colour && special == _special && tooDear == _short && Modulate.A == (live ? 1f : 0.5f)) return;
            (_mark, _key, _count, _name, _price, _was, _tag, _colour, _special, _short) = (mark, key, count, name, price, was, tag, colour, special, tooDear);
            Modulate = live ? Colors.White : new Color(1f, 1f, 1f, 0.5f);
            _frame.BorderColor = special ? Palette.Gold : PlainFrame;
            _frame.SetBorderWidthAll(special ? 3 : 1);
            _chip.BgColor = tooDear ? ChipRed : special ? ChipGold : ChipGreen;
            QueueRedraw();
        }

        public override void _Draw()
        {
            var body = GetThemeFont("font");
            var display = UiTheme.DisplayFont ?? body;
            DrawStyleBox(_frame, new Rect2(Vector2.Zero, Size));
            // The swatch and its letter-mark.
            var swatch = new Rect2(8f, 7f, Size.X - 16f, 30f);
            _swatch.BgColor = _colour;
            DrawStyleBox(_swatch, swatch);
            Centred(display, _mark, new Vector2(swatch.GetCenter().X, swatch.Position.Y + 23f), 20, Ink, swatch.Size.X);
            if (_key.Length > 0) Badge(body, _key, new Vector2(3f, 3f), left: true);
            if (_count.Length > 0) Badge(body, _count, new Vector2(Size.X - 3f, 3f), left: false);
            // The name, as large as fits (13 down to 9 px).
            int size = 13;
            while (size > 9 && body.GetStringSize(_name, HorizontalAlignment.Left, -1f, size).X > Size.X - 6f) size--;
            Centred(body, _name, new Vector2(Size.X * 0.5f, 51f), size, Colors.White, Size.X - 4f);
            // Under it a special's old price struck through (the gold frame says SPECIAL), else the tag.
            if (_was.Length > 0)
            {
                float ww = body.GetStringSize(_was, HorizontalAlignment.Left, -1f, 10).X;
                var at = new Vector2((Size.X - ww) * 0.5f, 62f);
                DrawString(body, at, _was, HorizontalAlignment.Left, -1f, 10, new Color(1f, 1f, 1f, 0.8f));
                // The strike as a thin filled box (no DrawLine: ADR-0018).
                DrawRect(new Rect2(at.X - 1f, at.Y - 4f, ww + 2f, 1.5f), new Color(1f, 0.42f, 0.42f, 0.95f));
            }
            else if (_tag.Length > 0) Centred(body, _tag, new Vector2(Size.X * 0.5f, 62f), 9, Palette.Amber, Size.X - 4f);
            // The price chip at the foot.
            float w = body.GetStringSize(_price, HorizontalAlignment.Left, -1f, 12).X + 12f;
            var chip = new Rect2((Size.X - w) * 0.5f, Size.Y - 19f, w, 15f);
            DrawStyleBox(_chip, chip);
            Centred(display, _price, new Vector2(chip.GetCenter().X, chip.End.Y - 3f), 12, Colors.White, w);
        }

        private void Centred(Font font, string text, Vector2 baseline, int size, Color colour, float width)
        {
            float w = Mathf.Min(width, font.GetStringSize(text, HorizontalAlignment.Left, -1f, size).X);
            DrawStringOutline(font, new Vector2(baseline.X - w * 0.5f, baseline.Y), text, HorizontalAlignment.Left, width, size, 3, new Color(0f, 0f, 0f, colour == Ink ? 0f : 0.7f));
            DrawString(font, new Vector2(baseline.X - w * 0.5f, baseline.Y), text, HorizontalAlignment.Left, width, size, colour);
        }

        private void Badge(Font font, string text, Vector2 corner, bool left)
        {
            float w = font.GetStringSize(text, HorizontalAlignment.Left, -1f, 10).X + 8f;
            var rect = new Rect2(left ? corner.X : corner.X - w, corner.Y, w, 15f);
            DrawStyleBox(_badge, rect);
            DrawString(font, new Vector2(rect.Position.X + 4f, rect.End.Y - 4f), text, HorizontalAlignment.Left, -1f, 10, Colors.White);
        }
    }
}
