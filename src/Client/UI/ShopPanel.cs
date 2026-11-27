using System.Collections.Generic;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The shop (doc 09 §2.8): a grid of cards on the right of the inventory screen, each with its
    /// key, name, price, what it is and what stands in the way of buying it. The shift's specials
    /// are framed in amber; a card that cannot be bought as things stand is greyed, and so is one
    /// whose purchase is on its way to the server. A number key or a click buys. Every string
    /// comes from <see cref="HudText"/>.
    /// </summary>
    public partial class ShopPanel : VBoxContainer
    {
        private const float CardWidth = 232f, CompactCardWidth = 196f;
        private static readonly Color CardBack = new Color(0.11f, 0.19f, 0.29f, 0.96f);
        private static readonly Color PlainFrame = new Color("6f8296");
        private static readonly Color PriceColour = new Color("ffd24a");

        private sealed class Card
        {
            public PanelContainer Panel = null!;
            public StyleBoxFlat Frame = null!;
            public Label[] Lines = null!;
        }

        // The lines of HudText.ShopCard.
        private const int Price = 1, What = 2, Note = 3, LineCount = 4;
        private static readonly int[] FontSizes = { 16, 14, 12, 12 };

        private ClientRuntime _client = null!;
        private readonly List<Card> _cards = new List<Card>();
        private Label _title = null!;
        private Godot.GridContainer _grid = null!;
        private Label _hint = null!;

        /// <summary>Everything the panel says, top to bottom; empty while it is not shown.</summary>
        public string Text
        {
            get
            {
                if (!IsVisibleInTree()) return "";
                var lines = new List<string> { _title.Text };
                foreach (var card in _cards)
                {
                    if (!card.Panel.Visible) continue;
                    foreach (var line in card.Lines)
                        if (line.Text.Length > 0) lines.Add(line.Text);
                }
                lines.Add(_hint.Text);
                return string.Join("\n", lines);
            }
        }

        /// <summary>How many cards lie on the counter, how many of them are drawn greyed, how many framed as specials.</summary>
        public int CardCount { get; private set; }
        public int GreyedCount { get; private set; }
        public int SpecialCount { get; private set; }
        /// <summary>The cards in the order of their keys, for the smoke's "nothing is clipped" check.</summary>
        public Control CardAt(int card) => _cards[card].Panel;
        /// <summary>The request the last click on a card sent; 0 if it sent none.</summary>
        public uint LastRequestId { get; private set; }

        public void Setup(ClientRuntime client)
        {
            _client = client;
            Name = "Shop";
            Visible = false;
            AddThemeConstantOverride("separation", 8);

            _title = Hud.MakeDisplayLabel("Title", 20, HorizontalAlignment.Left);
            _title.Modulate = Palette.Amber;
            AddChild(_title);
            _grid = new Godot.GridContainer { Name = "Cards", Columns = 2 };
            _grid.AddThemeConstantOverride("h_separation", 8);
            _grid.AddThemeConstantOverride("v_separation", 8);
            AddChild(_grid);
            _hint = Hud.MakeLabel("Hint", 12, HorizontalAlignment.Left);
            _hint.Modulate = new Color(1f, 1f, 1f, 0.7f);
            _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _hint.CustomMinimumSize = new Vector2(CardWidth * 2f, 0f);
            _hint.Text = HudText.ShopHint;
            AddChild(_hint);
        }

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
            // Three abreast once there are more than three rows of two: the panel stays clear of the HUD's foot;
            // four narrower ones without their descriptions once seven rows of three would not do.
            bool compact = HudText.ShopCompact(world);
            _grid.Columns = compact ? 4 : shelf.Count > 6 ? 3 : 2;
            float width = compact ? CompactCardWidth : CardWidth;
            while (_cards.Count < shelf.Count) AddCard();
            foreach (var c in _cards)
            {
                c.Panel.CustomMinimumSize = new Vector2(width, 0f);
                foreach (var line in c.Lines) line.CustomMinimumSize = new Vector2(width - 16f, 0f);
            }
            int greyed = 0, specials = 0;
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                card.Panel.Visible = i < shelf.Count;
                if (!card.Panel.Visible) continue;

                var def = shelf[i];
                string[] lines = HudText.ShopCard(world, def, i);
                for (int l = 0; l < LineCount; l++) card.Lines[l].Text = lines[l];
                bool special = world.IsSpecial(def), buying = world.IsBuying(def);
                var state = world.ShopState(def);
                bool live = state == OpResult.Ok && !buying;
                if (!live) greyed++;
                if (special) specials++;
                card.Frame.BorderColor = special ? Palette.Amber : PlainFrame;
                card.Frame.SetBorderWidthAll(special ? 2 : 1);
                card.Panel.Modulate = live ? Colors.White : new Color(1f, 1f, 1f, 0.42f);
                card.Lines[Price].Modulate = special ? Palette.Amber : PriceColour;
                card.Lines[Note].Visible = lines[Note].Length > 0;
                card.Lines[What].Visible = lines[What].Length > 0;
                card.Lines[Note].Modulate = state == OpResult.Ok ? Palette.Match : Palette.Mismatch;
            }
            CardCount = shelf.Count;
            GreyedCount = greyed;
            SpecialCount = specials;
        }

        private void AddCard()
        {
            int place = _cards.Count;
            var card = new Card
            {
                Panel = new PanelContainer { Name = $"Card{place}", CustomMinimumSize = new Vector2(CardWidth, 0f), MouseFilter = MouseFilterEnum.Stop },
                Frame = new StyleBoxFlat { BgColor = CardBack, AntiAliasing = true },
                Lines = new Label[LineCount],
            };
            card.Frame.SetCornerRadiusAll(7);
            card.Frame.SetContentMarginAll(8f);
            card.Panel.AddThemeStyleboxOverride("panel", card.Frame);
            card.Panel.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Click(place);
            };
            var lines = new VBoxContainer { Name = "Lines", MouseFilter = MouseFilterEnum.Ignore };
            lines.AddThemeConstantOverride("separation", 2);
            card.Panel.AddChild(lines);
            for (int l = 0; l < LineCount; l++)
            {
                var label = Hud.MakeLabel($"Line{l}", FontSizes[l], HorizontalAlignment.Left);
                label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                label.CustomMinimumSize = new Vector2(CardWidth - 16f, 0f);
                if (l == What)
                {
                    label.Modulate = new Color(1f, 1f, 1f, 0.78f);
                    // What it is takes what room there is, so that the note stands at the foot of every card.
                    label.SizeFlagsVertical = SizeFlags.ExpandFill;
                    label.VerticalAlignment = VerticalAlignment.Top;
                }
                card.Lines[l] = label;
                lines.AddChild(label);
            }
            _cards.Add(card);
            _grid.AddChild(card.Panel);
        }
    }
}
