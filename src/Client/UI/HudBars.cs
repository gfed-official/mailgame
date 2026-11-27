using Godot;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// A thin rounded bar of the concept video's HUD: the quota and the clock at the top, health in
    /// red and stamina in white segments at the bottom left.
    /// </summary>
    public partial class MeterBar : Control
    {
        private static readonly Color BackColour = new Color(0.06f, 0.08f, 0.12f, 0.55f);

        /// <summary>More than one: the bar is that many blocks, filled one after the other.</summary>
        public int Segments = 1;

        public Color Fill
        {
            get => _fill.BgColor;
            set { if (_fill.BgColor != value) { _fill.BgColor = value; QueueRedraw(); } }
        }

        /// <summary>The empty part's colour: lighter on a dark panel, where the default would not show.</summary>
        public Color Track
        {
            get => _back.BgColor;
            set { if (_back.BgColor != value) { _back.BgColor = value; QueueRedraw(); } }
        }

        /// <summary>0..1.</summary>
        public float Value
        {
            get => _value;
            set
            {
                float v = Mathf.Clamp(value, 0f, 1f);
                if (Mathf.IsEqualApprox(v, _value)) return;
                _value = v;
                QueueRedraw();
            }
        }

        private readonly StyleBoxFlat _back = new StyleBoxFlat { BgColor = BackColour, AntiAliasing = true };
        private readonly StyleBoxFlat _fill = new StyleBoxFlat { BgColor = Colors.White, AntiAliasing = true };
        private float _value;

        public MeterBar()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            int n = Mathf.Max(1, Segments);
            const float gap = 4f;
            float width = (Size.X - gap * (n - 1)) / n;
            int radius = Mathf.Max(1, Mathf.FloorToInt(Size.Y * 0.5f));
            _back.SetCornerRadiusAll(n > 1 ? Mathf.Min(radius, 3) : radius);
            _fill.SetCornerRadiusAll(n > 1 ? Mathf.Min(radius, 3) : radius);
            for (int i = 0; i < n; i++)
            {
                var rect = new Rect2(i * (width + gap), 0f, width, Size.Y);
                DrawStyleBox(_back, rect);
                float part = Mathf.Clamp(_value * n - i, 0f, 1f);
                // A sliver narrower than its own round ends would draw as a blob.
                if (part * width < 2f) continue;
                DrawStyleBox(_fill, new Rect2(rect.Position, new Vector2(width * part, Size.Y)));
            }
        }
    }

    /// <summary>The ring in the middle of the view: the exact point the interact ray and the hammer aim at.</summary>
    public partial class RingCrosshair : Control
    {
        public RingCrosshair()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            var centre = Size * 0.5f;
            DrawArc(centre, 8f, 0f, Mathf.Tau, 40, new Color(0f, 0f, 0f, 0.35f), 4f, true);
            DrawArc(centre, 8f, 0f, Mathf.Tau, 40, new Color(1f, 1f, 1f, 0.9f), 1.8f, true);
            DrawCircle(centre, 1.6f, new Color(1f, 1f, 1f, 0.9f));
        }
    }

    /// <summary>
    /// One row of the trackers at the bottom right: a dark rounded plate with a round mark, drawn
    /// only while its line has something to say. The line is the row's own label.
    /// </summary>
    public partial class TrackerRow : Control
    {
        public const float RowHeight = 28f;
        private static readonly Color Plate = new Color(0.09f, 0.17f, 0.24f, 0.86f);
        private static readonly Color Rim = new Color(0.45f, 0.72f, 0.86f, 0.35f);

        public Label Line { get; }
        public Color Mark = Colors.White;

        /// <summary>0..1: a bar of that much along the plate's foot (the Post Office's HP); below 0, none.</summary>
        public float Bar
        {
            get => _bar;
            set { if (!Mathf.IsEqualApprox(value, _bar)) { _bar = value; QueueRedraw(); } }
        }

        /// <summary>The mark beats red while set (the Post Office under attack).</summary>
        public bool Pulse;

        private readonly StyleBoxFlat _plate = new StyleBoxFlat { BgColor = Plate, BorderColor = Rim, AntiAliasing = true };
        private readonly StyleBoxFlat _barBack = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.45f), AntiAliasing = true };
        private readonly StyleBoxFlat _barFill = new StyleBoxFlat { BgColor = new Color("e5383b"), AntiAliasing = true };
        private bool _shown;
        private float _bar = -1f, _beat;

        public TrackerRow(Label line)
        {
            Line = line;
            MouseFilter = MouseFilterEnum.Ignore;
            _plate.SetCornerRadiusAll(7);
            _plate.SetBorderWidthAll(1);
            _barBack.SetCornerRadiusAll(2);
            _barFill.SetCornerRadiusAll(2);
            line.AnchorRight = 1f;
            line.AnchorBottom = 1f;
            line.OffsetLeft = 34f;
            line.OffsetRight = -10f;
            AddChild(line);
        }

        public override void _Process(double delta)
        {
            bool shown = Line.Visible && Line.Text.Length > 0;
            if (Pulse && shown)
            {
                _beat = (_beat + (float)delta * 2.2f) % 1f;
                QueueRedraw();
            }
            else _beat = 0f;
            if (shown == _shown) return;
            _shown = shown;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (!_shown) return;
            DrawStyleBox(_plate, new Rect2(Vector2.Zero, Size));
            var at = new Vector2(17f, Size.Y * 0.5f);
            DrawCircle(at, 7f, new Color(0f, 0f, 0f, 0.35f));
            DrawCircle(at, 5.5f, Mark);
            if (Pulse) DrawCircle(at, 5.5f + 4f * _beat, new Color(Mark, 0.5f * (1f - _beat)));
            if (_bar < 0f) return;
            // The HP as a bar along the foot of the plate, under the words (UI polish plan item 4).
            var track = new Rect2(34f, Size.Y - 7f, Size.X - 44f, 4f);
            DrawStyleBox(_barBack, track);
            if (_bar * track.Size.X >= 2f) DrawStyleBox(_barFill, new Rect2(track.Position, new Vector2(track.Size.X * _bar, track.Size.Y)));
        }
    }
}
