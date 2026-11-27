using Godot;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// Doc 05 §5: the HUD's marker for a construct under attack — a red chevron over it where it is
    /// in view, on the screen's edge pointing its way where not, with "Stone Wall under attack" beside.
    /// </summary>
    public partial class AttackMarker : Control
    {
        private static readonly Color Red = new Color("ff3b30");
        private readonly Label _label;
        private float _angle;
        private bool _over;

        /// <summary>What the line says, as of the last placing (empty when hidden).</summary>
        public string Text => Visible ? _label.Text : "";
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

        public AttackMarker()
        {
            Name = "AttackMarker";
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
            _label = new Label { MouseFilter = MouseFilterEnum.Ignore, ThemeTypeVariation = UiTheme.Hud };
            _label.AddThemeColorOverride("font_color", Red);
            _label.AddThemeFontSizeOverride("font_size", 18);
            AddChild(_label);
        }

        /// <summary>The boxes of the HUD the marker keeps off (the shift's plate, the banner, panels up); null: none.</summary>
        public System.Func<System.Collections.Generic.IReadOnlyList<ScreenBox>>? KeepClear;

        /// <summary>Where its words are, on screen, as of the last placing.</summary>
        public Rect2 WordsRect => new Rect2(Position + _label.Position, _label.GetMinimumSize());

        /// <summary>Puts it up at a screen point, pointing <paramref name="angle"/>; over the construct when <paramref name="over"/>.</summary>
        public void Place(Vector2 at, float angle, bool over, string text)
        {
            Visible = true;
            var keep = KeepClear?.Invoke();
            if (_label.Text != text) _label.Text = text;
            var size = _label.GetMinimumSize();
            var screen = GetViewportRect().Size;
            // The chevron and its words as one: cleared of the HUD together, so they never part (the f40acb7 playtest).
            var words = EdgeMarker.WordsAbout(at.X, angle, over, size.X, size.Y, screen.X);
            EdgeMarker.Extent(angle, out float x0, out float y0, out float x1, out float y1);
            var group = EdgeMarker.Union(words, new ScreenBox(x0, y0, x1, y1));
            at.Y = EdgeMarker.GroupClear(at.X, at.Y, group, keep, screen.Y);
            Position = at;
            _angle = angle;
            _over = over;
            _label.Position = new Vector2(words.X0, words.Y0);
            QueueRedraw();
        }

        public override void _Draw()
        {
            // A filled chevron whose tip is at the origin, pointing along the angle.
            var dir = new Vector2(Mathf.Cos(_angle), Mathf.Sin(_angle));
            var side = new Vector2(-dir.Y, dir.X);
            var back = -dir * EdgeMarker.ChevronBack;
            var points = new[] { Vector2.Zero, back + side * EdgeMarker.ChevronHalfWidth, back * 0.6f, back - side * EdgeMarker.ChevronHalfWidth };
            DrawColoredPolygon(points, Red);
            DrawPolyline(new[] { points[0], points[1], points[2], points[3], points[0] }, new Color(0f, 0f, 0f, 0.8f), 2f);
        }
    }
}
