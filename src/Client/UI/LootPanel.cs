using Godot;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The loot cards of the concept video: a dark blue plate over the hotbar with the fallen enemy's
    /// name and one card per thing picked up, the most valuable in gold. Draws <see cref="LootCards"/>.
    /// </summary>
    public partial class LootPanel : Control
    {
        private const float CardW = 66f, CardH = 74f, Gap = 10f, Pad = 14f, TitleH = 34f;
        private static readonly Color Gold = new Color("f0a020");
        private static readonly Color GoldRim = new Color("ffe08a");
        private static readonly Color Title = new Color("f6c453");

        public LootCards Cards { get; } = new LootCards();
        /// <summary>The bottom edge, in pixels up from the bottom of the screen.</summary>
        public float Lift;

        private readonly StyleBoxFlat _plate = new StyleBoxFlat { BgColor = new Color(0.08f, 0.13f, 0.21f, 0.93f), BorderColor = new Color(0.45f, 0.72f, 0.86f, 0.35f), AntiAliasing = true };
        private readonly StyleBoxFlat _card = new StyleBoxFlat { BgColor = new Color("2f6f8f"), BorderColor = new Color(1f, 1f, 1f, 0.35f), AntiAliasing = true };
        private readonly StyleBoxFlat _best = new StyleBoxFlat { BgColor = Gold, BorderColor = GoldRim, ShadowColor = new Color(Gold, 0.5f), ShadowSize = 8, AntiAliasing = true };
        private int _revision = -1;

        public LootPanel()
        {
            Name = "Loot";
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
            _plate.SetCornerRadiusAll(9);
            _plate.SetBorderWidthAll(1);
            _card.SetCornerRadiusAll(7);
            _card.SetBorderWidthAll(1);
            _best.SetCornerRadiusAll(7);
            _best.SetBorderWidthAll(2);
            AnchorLeft = AnchorRight = 0.5f;
            AnchorTop = AnchorBottom = 1f;
        }

        public override void _Process(double delta)
        {
            Cards.Update(delta);
            if (_revision == Cards.Revision) return;
            _revision = Cards.Revision;
            Visible = Cards.Visible;
            int n = Mathf.Max(1, Cards.Cards.Count);
            float width = Mathf.Max(220f, n * CardW + (n - 1) * Gap + Pad * 2f);
            OffsetLeft = -width * 0.5f;
            OffsetRight = width * 0.5f;
            OffsetBottom = -Lift;
            OffsetTop = -Lift - (TitleH + CardH + Pad * 1.5f);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var cards = Cards.Cards;
            if (cards.Count == 0) return;
            var font = GetThemeDefaultFont();
            DrawStyleBox(_plate, new Rect2(Vector2.Zero, Size));
            DrawString(font, new Vector2(0f, 27f), Cards.Title, HorizontalAlignment.Center, Size.X, 20, Title);

            int best = Cards.Best;
            float row = cards.Count * CardW + (cards.Count - 1) * Gap;
            float left = (Size.X - row) * 0.5f;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                var rect = new Rect2(left + i * (CardW + Gap), TitleH + Pad * 0.5f, CardW, CardH);
                DrawStyleBox(i == best ? _best : _card, rect);
                var mid = rect.Position + new Vector2(CardW * 0.5f, 34f);
                if (card.Coins)
                {
                    DrawCircle(mid, 13f, new Color("8a5a00"));
                    DrawCircle(mid, 11f, new Color("ffd24a"));
                    DrawCircle(mid, 6.5f, new Color("f0a020"));
                }
                else
                {
                    DrawRect(new Rect2(mid - new Vector2(12f, 12f), new Vector2(24f, 24f)), new Color("c9b08a"));
                    DrawRect(new Rect2(mid - new Vector2(12f, 12f), new Vector2(24f, 24f)), new Color("5a4630"), false, 2f);
                }
                Outlined(font, new Vector2(rect.Position.X, rect.Position.Y + 14f), card.Name, CardW, 10);
                if (!card.Coins && card.Count > 1) Outlined(font, new Vector2(rect.Position.X, rect.Position.Y + 48f), HudText.Count(card.Count), CardW - 8f, 11, HorizontalAlignment.Right);
                Outlined(font, new Vector2(rect.Position.X, rect.End.Y - 7f), HudText.Gold(card.Value), CardW, 14);
            }
        }

        private void Outlined(Font font, Vector2 at, string text, float width, int size, HorizontalAlignment align = HorizontalAlignment.Center)
        {
            DrawStringOutline(font, at, text, align, width, size, 4, new Color(0f, 0f, 0f, 0.8f));
            DrawString(font, at, text, align, width, size, Colors.White);
        }
    }
}
