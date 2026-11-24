using System.Collections.Generic;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The filters of an Address Sorter or a splitter (doc 09 §2.7 "configure"): on the right of the
    /// inventory screen, where an open chest would be, a card for each way out with its key, its
    /// filter and how much of the Intake's mail it would take now; a sorter's overflow last. A
    /// click or the output's number key steps its filter on, a right click back. Every string comes
    /// from <see cref="HudText"/>.
    /// </summary>
    public partial class FilterPanel : VBoxContainer
    {
        private const float CardWidth = 300f;
        private static readonly Color CardBack = new Color(0.11f, 0.19f, 0.29f, 0.96f);
        private static readonly Color FilterFrame = new Color("6f8296");
        private static readonly Color SetColour = new Color("ffd24a");

        private sealed class Card
        {
            public PanelContainer Panel = null!;
            public StyleBoxFlat Frame = null!;
            public Label[] Lines = null!;
        }

        private static readonly int[] FontSizes = { 15, 17, 12 };

        private ClientRuntime _client = null!;
        private readonly List<Card> _cards = new List<Card>();
        private Label _title = null!, _status = null!, _hint = null!;

        /// <summary>Everything the panel says, top to bottom; empty while it is not shown.</summary>
        public string Text
        {
            get
            {
                if (!IsVisibleInTree()) return "";
                var lines = new List<string> { _title.Text };
                if (_status.Visible) lines.Add(_status.Text);
                foreach (var card in _cards)
                {
                    if (!card.Panel.Visible) continue;
                    foreach (var line in card.Lines) if (line.Visible) lines.Add(line.Text);
                }
                lines.Add(_hint.Text);
                return string.Join("\n", lines);
            }
        }

        public int CardCount { get; private set; }
        public Control CardAt(int card) => _cards[card].Panel;
        /// <summary>The request the last click on a card sent; 0 if it sent none.</summary>
        public uint LastRequestId { get; private set; }

        public void Setup(ClientRuntime client)
        {
            _client = client;
            Name = "Filters";
            Visible = false;
            AddThemeConstantOverride("separation", 8);
            _title = Hud.MakeLabel("Title", 20, HorizontalAlignment.Left);
            _title.Modulate = Palette.Amber;
            AddChild(_title);
            _status = Hud.MakeLabel("Status", 13, HorizontalAlignment.Left);
            AddChild(_status);
            _hint = Hud.MakeLabel("Hint", 12, HorizontalAlignment.Left);
            _hint.Modulate = new Color(1f, 1f, 1f, 0.7f);
            _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _hint.CustomMinimumSize = new Vector2(CardWidth, 0f);
            _hint.Text = HudText.FilterHint;
            AddChild(_hint);
        }

        /// <summary>A click on the card of an output: its filter one on (<paramref name="step"/> 1), or one back (−1).</summary>
        public void Click(int output, int step = 1)
        {
            LastRequestId = 0;
            if (_client?.World?.FilterTarget == null) return;
            LastRequestId = _client.StepFilter(output, step);
        }

        public void Refresh(ClientWorld world)
        {
            var built = world.FilterTarget;
            Visible = built != null;
            if (built == null) return;
            _title.Text = HudText.FilterTitle(built);
            _status.Text = HudText.FilterStatusLine(world, built, world.FilterStatus);
            _hint.Text = HudText.FilterHintFor(built);
            _status.Visible = _status.Text.Length > 0;
            int rows = HudText.FilterRows(built);
            while (_cards.Count < rows) AddCard();
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                card.Panel.Visible = i < rows;
                if (!card.Panel.Visible) continue;
                string[] lines = HudText.FilterRow(world, built, i);
                for (int l = 0; l < card.Lines.Length; l++)
                {
                    card.Lines[l].Visible = l < lines.Length;
                    if (l < lines.Length) card.Lines[l].Text = lines[l];
                }
                bool output = i < built.Def.FilterOutputs;
                bool set = output && !world.ShownFilter(built, i, 0).IsEmpty;
                // A depot's card is two lines: what the stop is stands in the first.
                var what = lines.Length > 2 ? card.Lines[1] : card.Lines[0];
                what.Modulate = set ? SetColour : new Color(1f, 1f, 1f, lines.Length > 2 ? 0.7f : 1f);
                card.Frame.BorderColor = set ? Palette.Amber : FilterFrame;
                card.Panel.MouseDefaultCursorShape = output || built.Def.IsVehicleDepot ? CursorShape.PointingHand : CursorShape.Arrow;
            }
            CardCount = rows;
        }

        private void AddCard()
        {
            int output = _cards.Count;
            var card = new Card
            {
                Panel = new PanelContainer { Name = $"Card{output}", CustomMinimumSize = new Vector2(CardWidth, 0f), MouseFilter = MouseFilterEnum.Stop },
                Frame = new StyleBoxFlat { BgColor = CardBack, AntiAliasing = true },
                Lines = new Label[3],
            };
            card.Frame.SetCornerRadiusAll(7);
            card.Frame.SetContentMarginAll(8f);
            card.Frame.SetBorderWidthAll(1);
            card.Panel.AddThemeStyleboxOverride("panel", card.Frame);
            card.Panel.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { Pressed: true } button)
                {
                    if (button.ButtonIndex == MouseButton.Left) Click(output, 1);
                    else if (button.ButtonIndex == MouseButton.Right) Click(output, -1);
                }
            };
            var lines = new VBoxContainer { Name = "Lines", MouseFilter = MouseFilterEnum.Ignore };
            lines.AddThemeConstantOverride("separation", 2);
            card.Panel.AddChild(lines);
            for (int l = 0; l < card.Lines.Length; l++)
            {
                var label = Hud.MakeLabel($"Line{l}", FontSizes[l], HorizontalAlignment.Left);
                label.CustomMinimumSize = new Vector2(CardWidth - 16f, 0f);
                if (l == 2) label.Modulate = new Color(1f, 1f, 1f, 0.75f);
                card.Lines[l] = label;
                lines.AddChild(label);
            }
            _cards.Add(card);
            // Before the hint, under the others.
            AddChild(card.Panel);
            MoveChild(card.Panel, _hint.GetIndex());
        }
    }
}
