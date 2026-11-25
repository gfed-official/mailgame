using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The ping wheel (doc 09 §2.6 "Hold for ping wheel"; design choice 117): while the ping key is
    /// held it stands round the crosshair, its four words at the four points (up "Deliver here",
    /// right "Build here", down "Need materials", left "Danger"), the one the pointer is on lit in
    /// its ping's colour, "Look here" in the middle.
    /// </summary>
    public partial class PingWheel : Control
    {
        private PingController? _ping;
        private const float Radius = 120f;

        /// <summary>The words as drawn, the chosen one first, for the smoke ("" while down).</summary>
        public string Text { get; private set; } = "";

        public void Setup(PingController ping)
        {
            _ping = ping;
            SetAnchorsPreset(LayoutPreset.Center);
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
        }

        public override void _Process(double delta)
        {
            bool open = _ping?.WheelOpen == true;
            Visible = open;
            Text = open ? $"{HudText.PingName(_ping!.Choice)}" : "";
            if (open) QueueRedraw();
        }

        public override void _Draw()
        {
            if (_ping == null || !_ping.WheelOpen) return;
            var font = GetThemeDefaultFont();
            var choice = _ping.Choice;
            DrawCircle(Vector2.Zero, Radius + 34f, new Color(0.05f, 0.07f, 0.1f, 0.55f));
            DrawArc(Vector2.Zero, Radius + 34f, 0f, Mathf.Tau, 48, new Color(1f, 1f, 1f, 0.35f), 2f);
            foreach (var (kind, at) in new[]
            {
                (PingKind.Deliver, new Vector2(0f, -Radius)), (PingKind.Build, new Vector2(Radius, 0f)),
                (PingKind.Materials, new Vector2(0f, Radius)), (PingKind.Danger, new Vector2(-Radius, 0f)), (PingKind.Here, Vector2.Zero),
            })
            {
                var colour = PingsView.Colour(kind);
                bool on = kind == choice;
                DrawCircle(at, on ? 30f : 22f, new Color(colour, on ? 0.9f : 0.35f));
                DrawString(font, at + new Vector2(-80f, 48f), HudText.PingName(kind), HorizontalAlignment.Center, 160f, on ? 18 : 15, on ? Colors.White : new Color(1f, 1f, 1f, 0.7f));
            }
            var pointer = new Vector2(_ping.PointerX, _ping.PointerY);
            DrawLine(Vector2.Zero, pointer, new Color(1f, 1f, 1f, 0.6f), 2f);
            DrawCircle(pointer, 6f, Colors.White);
        }
    }
}
