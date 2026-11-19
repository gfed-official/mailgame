using System.Collections.Generic;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Perks;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The Draft (doc 09 §2.10): the cards in the middle of the view, each with its key, name,
    /// category badge, a frame in the colour of its rarity, what it does and whose it will be. A
    /// card somebody took says who as soon as they did; one that is not open to the local player
    /// is greyed, their own is framed in green. Under the cards the Draft's seconds as a bar and
    /// the perks of the run so far. Every string comes from <see cref="HudText"/>. A card takes a
    /// click as it takes its key; until the local player has chosen, the mouse is the pointer's.
    /// </summary>
    public partial class DraftPanel : PanelContainer
    {
        private const float CardWidth = 264f;
        private const double HurrySeconds = 10.0;
        private static readonly Color CardBack = new Color(0.11f, 0.19f, 0.29f, 0.96f);
        private static readonly Color[] RarityColours = { new Color("b8c4d0"), new Color("5bc0eb"), new Color("f6c453") };

        public static Color RarityColour(PerkRarity rarity) => RarityColours[Mathf.Clamp((int)rarity, 0, RarityColours.Length - 1)];

        private sealed class Card
        {
            public PanelContainer Panel = null!;
            public StyleBoxFlat Frame = null!;
            public Label[] Lines = null!;
        }

        // The lines of HudText.DraftCard.
        private const int Badge = 1, Does = 2, Scope = 3, Note = 4, LineCount = 5;
        private static readonly int[] FontSizes = { 20, 12, 16, 13, 14 };

        private readonly List<Card> _cards = new List<Card>();
        private ClientRuntime? _client;
        private readonly Label _title;
        private readonly HBoxContainer _row;
        private readonly MeterBar _clock;
        private readonly Label _perks;

        /// <summary>Everything the panel says, top to bottom; empty while it is not shown.</summary>
        public string Text
        {
            get
            {
                if (!Visible) return "";
                var lines = new List<string> { _title.Text };
                foreach (var card in _cards)
                {
                    if (!card.Panel.Visible) continue;
                    foreach (var line in card.Lines)
                        if (line.Text.Length > 0) lines.Add(line.Text);
                }
                if (_perks.Visible) lines.Add(_perks.Text);
                return string.Join("\n", lines);
            }
        }

        /// <summary>How many cards lie on the table, and how many of them are drawn greyed.</summary>
        public int CardCount { get; private set; }
        public int GreyedCount { get; private set; }
        /// <summary>How full the bar of the Draft's seconds is drawn, 0..1.</summary>
        public float ClockShown => _clock.Value;
        /// <summary>The cards, left to right, for the smoke's "nothing is clipped" check.</summary>
        public Control CardAt(int card) => _cards[card].Panel;
        /// <summary>The request the last click on a card sent; 0 if it sent none (the card was not open).</summary>
        public uint LastRequestId { get; private set; }

        /// <summary>Whose the mouse is: a card takes a click only while it is the pointer's, not the click of a swing with the cards set aside.</summary>
        public MouseCapture? Mouse;

        public void Setup(ClientRuntime client) => _client = client;

        /// <summary>A click on a card: what its key does.</summary>
        public void Click(int card) => LastRequestId = _client?.PickPerk(card) ?? 0;

        public DraftPanel()
        {
            Name = "Draft";
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
            // Over the ring in the middle of the view, which would otherwise sit on a card's edge.
            ZIndex = 1;
            AnchorLeft = AnchorRight = 0.5f;
            AnchorTop = AnchorBottom = 0.2f;
            GrowHorizontal = GrowDirection.Both;
            GrowVertical = GrowDirection.End;
            var back = new StyleBoxFlat { BgColor = new Color(0.08f, 0.13f, 0.21f, 0.9f), BorderColor = new Color(0.45f, 0.72f, 0.86f, 0.35f) };
            back.SetCornerRadiusAll(9);
            back.SetBorderWidthAll(1);
            back.SetContentMarginAll(18f);
            AddThemeStyleboxOverride("panel", back);

            var column = new VBoxContainer { Name = "Column", MouseFilter = MouseFilterEnum.Ignore };
            column.AddThemeConstantOverride("separation", 10);
            AddChild(column);
            _title = Hud.MakeLabel("Title", 24, HorizontalAlignment.Center);
            _title.Modulate = Palette.Amber;
            column.AddChild(_title);
            _row = new HBoxContainer { Name = "Cards", Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
            _row.AddThemeConstantOverride("separation", 12);
            column.AddChild(_row);
            _clock = new MeterBar { Name = "Clock", CustomMinimumSize = new Vector2(0f, 8f) };
            column.AddChild(_clock);
            _perks = Hud.MakeLabel("Perks", 14, HorizontalAlignment.Center);
            column.AddChild(_perks);
        }

        public void Refresh(ClientWorld world)
        {
            bool open = world.Global.Phase == RunPhase.Draft && world.DraftOffer.Count > 0;
            Visible = open;
            if (!open) return;

            _title.Text = HudText.DraftTitle(world);
            while (_cards.Count < world.DraftOffer.Count) AddCard();
            int pick = world.LocalDraftPick, greyed = 0;
            bool pointer = Mouse == null || !Mouse.Wanted;
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                card.Panel.Visible = i < world.DraftOffer.Count;
                if (!card.Panel.Visible) continue;

                var perk = world.DraftOffer[i];
                string[] lines = HudText.DraftCard(world, i);
                for (int l = 0; l < LineCount; l++) card.Lines[l].Text = lines[l];
                bool own = pick == i, live = own || world.CardState(i) == OpResult.Ok;
                if (!live) greyed++;
                var rarity = RarityColour(perk.Rarity);
                card.Frame.BorderColor = own ? Palette.Match : rarity;
                card.Frame.SetBorderWidthAll(own ? 3 : 2);
                card.Panel.Modulate = live ? Colors.White : new Color(1f, 1f, 1f, 0.42f);
                bool takes = live && !own && pointer;
                card.Panel.MouseFilter = takes ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
                card.Panel.MouseDefaultCursorShape = takes ? CursorShape.PointingHand : CursorShape.Arrow;
                card.Lines[Badge].Modulate = rarity;
                card.Lines[Note].Visible = lines[Note].Length > 0;
                card.Lines[Note].Modulate = own ? Palette.Match : Palette.Amber;
            }
            CardCount = world.DraftOffer.Count;
            GreyedCount = greyed;

            double total = world.Content.Balance.GetDouble("draftSeconds"), left = world.PhaseSecondsLeft;
            _clock.Value = total > 0.0 ? (float)(left / total) : 0f;
            _clock.Fill = left < HurrySeconds ? Palette.Amber : Colors.White;
            _perks.Text = HudText.Perks(world);
            _perks.Visible = _perks.Text.Length > 0;
        }

        private void AddCard()
        {
            var card = new Card
            {
                Panel = new PanelContainer { Name = $"Card{_cards.Count}", CustomMinimumSize = new Vector2(CardWidth, 0f), MouseFilter = MouseFilterEnum.Ignore },
                Frame = new StyleBoxFlat { BgColor = CardBack, AntiAliasing = true },
                Lines = new Label[LineCount],
            };
            card.Frame.SetCornerRadiusAll(8);
            card.Frame.SetContentMarginAll(12f);
            card.Panel.AddThemeStyleboxOverride("panel", card.Frame);
            var lines = new VBoxContainer { Name = "Lines", MouseFilter = MouseFilterEnum.Ignore };
            lines.AddThemeConstantOverride("separation", 6);
            card.Panel.AddChild(lines);
            for (int l = 0; l < LineCount; l++)
            {
                var label = Hud.MakeLabel($"Line{l}", FontSizes[l], HorizontalAlignment.Left);
                // A card is as wide as it is; what does not fit breaks into the next line.
                label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                label.CustomMinimumSize = new Vector2(CardWidth - 24f, 0f);
                if (l == Scope) label.Modulate = new Color(1f, 1f, 1f, 0.72f);
                // What the perk does takes what room there is, so that scope and note stand at the foot of every card.
                if (l == Does)
                {
                    label.SizeFlagsVertical = SizeFlags.ExpandFill;
                    label.VerticalAlignment = VerticalAlignment.Top;
                }
                card.Lines[l] = label;
                lines.AddChild(label);
            }
            int place = _cards.Count;
            card.Panel.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Click(place);
            };
            _cards.Add(card);
            _row.AddChild(card.Panel);
        }
    }
}
