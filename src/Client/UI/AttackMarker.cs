using Godot;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// Doc 05 §5: the HUD's marker for a construct under attack — a red chevron over it where it is
    /// in view, on the screen's edge pointing its way where not. Beside it, to be read at a glance (UI polish
    /// plan item 10; ref DRG, Lethal Company), a short tag and how far ("PO  42 m") over the words ("Post
    /// Office under attack"); the chevron beats while the blows keep coming.
    /// </summary>
    public partial class AttackMarker : Control
    {
        private static readonly Color Red = new Color("ff3b30");
        private readonly Color _colour;
        private readonly Label _label, _tag;
        private readonly VBoxContainer _words;
        private float _angle, _beat;
        private bool _over, _pulse;

        /// <summary>What the line says, as of the last placing (empty when hidden).</summary>
        public string Text => Visible ? _label.Text : "";
        /// <summary>The short tag over the words, "PO  42 m" (empty when hidden).</summary>
        public string Tag => Visible ? _tag.Text : "";
        /// <summary>Whether the chevron beats (the construct is being struck, not broken).</summary>
        public bool Pulsing => Visible && _pulse;
        /// <summary>Where the chevron's tip is, on screen.</summary>
        public Vector2 Tip => Position;
        public bool OverIt => _over;

        /// <summary>The box the chevron covers on screen, as of the last placing.</summary>
        public Rect2 ChevronRect
        {
            get
            {
                EdgeMarker.Extent(_angle, out float x0, out float y0, out float x1, out float y1);
                return new Rect2(Position.X + x0, Position.Y + y0, x1 - x0, y1 - y0);
            }
        }

        /// <summary>The marker in red for what is under attack; another colour and name for another use of it (Local Knowledge's mailbox).</summary>
        public AttackMarker(Color? colour = null, string name = "AttackMarker")
        {
            _colour = colour ?? Red;
            Name = name;
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
            _words = new VBoxContainer { Name = "Words", MouseFilter = MouseFilterEnum.Ignore };
            _words.AddThemeConstantOverride("separation", -2);
            _tag = new Label { Name = "Tag", MouseFilter = MouseFilterEnum.Ignore, ThemeTypeVariation = UiTheme.Display };
            _tag.AddThemeColorOverride("font_color", _colour);
            _tag.AddThemeFontSizeOverride("font_size", 20);
            _label = new Label { Name = "Line", MouseFilter = MouseFilterEnum.Ignore, ThemeTypeVariation = UiTheme.Hud };
            _label.AddThemeColorOverride("font_color", _colour);
            _label.AddThemeFontSizeOverride("font_size", 14);
            _words.AddChild(_tag);
            _words.AddChild(_label);
            AddChild(_words);
        }

        /// <summary>The boxes of the HUD the marker keeps off (the shift's plate, the banner, panels up); null: none.</summary>
        public System.Func<System.Collections.Generic.IReadOnlyList<ScreenBox>>? KeepClear;

        /// <summary>Where its words are, on screen, as of the last placing.</summary>
        public Rect2 WordsRect => new Rect2(Position + _words.Position, _words.GetCombinedMinimumSize());

        /// <summary>Puts it up at a screen point, pointing <paramref name="angle"/>; over the construct when <paramref name="over"/>.</summary>
        public void Place(Vector2 at, float angle, bool over, string text, string tag = "", bool pulse = false)
        {
            Visible = true;
            _pulse = pulse;
            var keep = KeepClear?.Invoke();
            if (_label.Text != text) _label.Text = text;
            if (_tag.Text != tag) _tag.Text = tag;
            _tag.Visible = tag.Length > 0;
            var size = _words.GetCombinedMinimumSize();
            var screen = GetViewportRect().Size;
            // The chevron and its words as one: cleared of the HUD together, so they never part (the f40acb7 playtest).
            var words = EdgeMarker.WordsAbout(at.X, angle, over, size.X, size.Y, screen.X);
            EdgeMarker.Extent(angle, out float x0, out float y0, out float x1, out float y1);
            var group = EdgeMarker.Union(words, new ScreenBox(x0, y0, x1, y1));
            at.Y = EdgeMarker.GroupClear(at.X, at.Y, group, keep, screen.Y);
            Position = at;
            _angle = angle;
            _over = over;
            _words.Position = new Vector2(words.X0, words.Y0);
            _words.Size = size;
            // Flush with the chevron's side: right-aligned when they stand left of it.
            var align = over ? HorizontalAlignment.Center : words.X1 <= 0f ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            _tag.HorizontalAlignment = _label.HorizontalAlignment = align;
            QueueRedraw();
        }

        public override void _Process(double delta)
        {
            if (!Visible || !_pulse) { _beat = 0f; return; }
            _beat = (_beat + (float)delta * 1.6f) % 1f;
            QueueRedraw();
        }

        public override void _Draw()
        {
            // A filled chevron whose tip is at the origin, pointing along the angle.
            var dir = new Vector2(Mathf.Cos(_angle), Mathf.Sin(_angle));
            var side = new Vector2(-dir.Y, dir.X);
            var back = -dir * EdgeMarker.ChevronBack;
            var points = new[] { Vector2.Zero, back + side * EdgeMarker.ChevronHalfWidth, back * 0.6f, back - side * EdgeMarker.ChevronHalfWidth };
            if (_pulse)
            {
                // A fading copy that swells from it: the beat of the blows (inside the chevron's keep-clear box, near enough).
                var mid = back * 0.5f;
                float grow = 1f + 0.45f * _beat;
                var halo = new Vector2[points.Length];
                for (int i = 0; i < points.Length; i++) halo[i] = mid + (points[i] - mid) * grow;
                DrawColoredPolygon(halo, new Color(_colour, 0.55f * (1f - _beat)));
            }
            DrawColoredPolygon(points, _colour);
            DrawPolyline(new[] { points[0], points[1], points[2], points[3], points[0] }, new Color(0f, 0f, 0f, 0.8f), 2f);
        }
    }
}
