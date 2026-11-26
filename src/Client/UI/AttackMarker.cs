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
        /// <summary>How far right of the chevron its words start when they cannot stand over it, px.</summary>
        private const float SideOffset = 80f;
        private readonly Label _label;
        private float _angle;
        private bool _over;

        /// <summary>What the line says, as of the last placing (empty when hidden).</summary>
        public string Text => Visible ? _label.Text : "";
        /// <summary>Where the chevron's tip is, on screen.</summary>
        public Vector2 Tip => Position;
        public bool OverIt => _over;

        public AttackMarker()
        {
            Name = "AttackMarker";
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
            _label = new Label { MouseFilter = MouseFilterEnum.Ignore };
            _label.AddThemeColorOverride("font_color", Red);
            _label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
            _label.AddThemeConstantOverride("outline_size", 6);
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
            at.Y = EdgeMarker.TipClear(at.X, at.Y, keep);
            Position = at;
            _angle = angle;
            _over = over;
            if (_label.Text != text) _label.Text = text;
            var size = _label.GetMinimumSize();
            var screen = GetViewportRect().Size;
            // The words beside the chevron, on the side towards the middle of the screen, kept on it.
            var offset = over ? new Vector2(-size.X * 0.5f, -size.Y - 30f)
                : new Vector2(at.X > screen.X * 0.5f ? -size.X - 26f : 26f, at.Y > screen.Y * 0.5f ? -size.Y - 6f : 6f);
            var global = at + offset;
            global.X = Mathf.Clamp(global.X, 8f, screen.X - size.X - 8f);
            global.Y = Mathf.Clamp(global.Y, 8f, screen.Y - size.Y - 8f);
            float clear = EdgeMarker.WordsClear(global.X, global.Y, size.X, size.Y, keep, screen.Y);
            // Over the construct, words pushed down would sit on the chevron: beside it instead, clear of
            // the numbers rising under it (the f32ea2e smoke's wallbreaker-wall.png had "-150" under them).
            if (over && clear != global.Y) clear = EdgeMarker.WordsClear(global.X = Mathf.Min(at.X + SideOffset, screen.X - size.X - 8f), at.Y - size.Y, size.X, size.Y, keep, screen.Y);
            global.Y = clear;
            _label.Position = global - at;
            QueueRedraw();
        }

        public override void _Draw()
        {
            // A filled chevron whose tip is at the origin, pointing along the angle.
            var dir = new Vector2(Mathf.Cos(_angle), Mathf.Sin(_angle));
            var side = new Vector2(-dir.Y, dir.X);
            var back = -dir * 20f;
            var points = new[] { Vector2.Zero, back + side * 12f, back * 0.6f, back - side * 12f };
            DrawColoredPolygon(points, Red);
            DrawPolyline(new[] { points[0], points[1], points[2], points[3], points[0] }, new Color(0f, 0f, 0f, 0.8f), 2f);
        }
    }
}
