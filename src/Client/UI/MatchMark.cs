using Godot;
using PerformativeMail.Client.Presentation;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The tick or cross beside a delivery prompt. Drawn as lines because the UI font has no ✓ or ✗
    /// glyph: as text they would render as nothing at all.
    /// </summary>
    public partial class MatchMark : Control
    {
        /// <summary>Match draws a tick, Mismatch a cross; Neutral hides the mark.</summary>
        public PromptTone Tone
        {
            get => _tone;
            set
            {
                if (_tone == value) return;
                _tone = value;
                Visible = value != PromptTone.Neutral;
                QueueRedraw();
            }
        }

        private PromptTone _tone = PromptTone.Neutral;

        public MatchMark()
        {
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            if (_tone == PromptTone.Neutral) return;
            float s = Mathf.Min(Size.X, Size.Y);
            var origin = (Size - new Vector2(s, s)) * 0.5f;
            Vector2 P(float x, float y) => origin + new Vector2(x, y) * s;

            if (_tone == PromptTone.Match)
            {
                var tick = new[] { P(0.12f, 0.54f), P(0.40f, 0.82f), P(0.90f, 0.16f) };
                DrawPolyline(tick, Colors.Black, s * 0.30f, true);
                DrawPolyline(tick, Palette.Match, s * 0.17f, true);
            }
            else
            {
                DrawLine(P(0.16f, 0.16f), P(0.84f, 0.84f), Colors.Black, s * 0.30f, true);
                DrawLine(P(0.84f, 0.16f), P(0.16f, 0.84f), Colors.Black, s * 0.30f, true);
                DrawLine(P(0.16f, 0.16f), P(0.84f, 0.84f), Palette.Mismatch, s * 0.17f, true);
                DrawLine(P(0.84f, 0.16f), P(0.16f, 0.84f), Palette.Mismatch, s * 0.17f, true);
            }
        }
    }
}
