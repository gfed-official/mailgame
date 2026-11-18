using System.Collections.Generic;
using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// A billboard label in the world (house number, sign, name tag) that keeps its real size from
    /// far away but is capped on screen: up close it shrinks instead of filling the first-person view.
    /// From far away it thins out, and it gives way to the HUD and to nearer labels
    /// (<see cref="LabelDeclutterer"/>).
    /// </summary>
    public partial class SizedLabel3D : Label3D
    {
        public const string Group = "sized_labels";

        /// <summary>Every such label in the tree right now.</summary>
        public static IReadOnlyList<SizedLabel3D> Live => InTree;
        private static readonly List<SizedLabel3D> InTree = new List<SizedLabel3D>();

        /// <summary>The most of the screen height the text (one line, font size × pixel size) may cover.</summary>
        public float MaxScreenFraction = 0.05f;

        /// <summary>Metres from the camera at which the label starts to thin out, and at which it is gone. No end: read from anywhere.</summary>
        public float FadeStart, FadeEnd;

        /// <summary>Where labels would cover each other the higher rank stays, then the nearer one.</summary>
        public int Rank;

        /// <summary>How strongly the label should be drawn (0..1), as the declutterer last decided.</summary>
        public float Target { get; private set; } = 1f;

        /// <summary>How strongly it is drawn right now: eased towards <see cref="Target"/>.</summary>
        public float Shown { get; private set; } = 1f;

        /// <summary>Height of one line of text in metres at scale 1.</summary>
        public float WorldHeight => FontSize * PixelSize;

        /// <summary>Width of the text over the height of its line.</summary>
        public float Aspect
        {
            get
            {
                if (_aspectOf == Text) return _aspect;
                _aspectOf = Text;
                var font = Font ?? ThemeDB.FallbackFont;
                _aspect = FontSize > 0 ? font.GetStringSize(Text, HorizontalAlignment.Left, -1f, FontSize).X / FontSize : 1f;
                return _aspect;
            }
        }

        private const float FadePerSecond = 5f;

        private string? _aspectOf;
        private float _aspect = 1f;
        private float _alpha = 1f, _outlineAlpha = 1f;

        public override void _EnterTree() => InTree.Add(this);

        public override void _ExitTree() => InTree.Remove(this);

        public override void _Ready()
        {
            AddToGroup(Group);
            // After the camera has moved this frame.
            ProcessPriority = 100;
            _alpha = Modulate.A;
            _outlineAlpha = OutlineModulate.A;
            Fit();
        }

        public override void _Process(double delta) => Fit();

        /// <summary>Share of the screen height the text covers right now for <paramref name="camera"/>.</summary>
        public float ScreenFraction(Camera3D camera) =>
            LabelSizing.ScreenFraction(WorldHeight * Scale.Y, Depth(camera), Mathf.DegToRad(camera.Fov));

        /// <summary>Eases the label towards <paramref name="target"/> (0: not drawn, 1: in full).</summary>
        public void Show(float target, float delta)
        {
            Target = target;
            float shown = Mathf.MoveToward(Shown, target, delta * FadePerSecond);
            if (Mathf.IsEqualApprox(shown, Shown)) return;
            Shown = shown;
            // Changing a colour rebuilds the label's mesh, so only while it fades.
            Modulate = new Color(Modulate, _alpha * shown);
            OutlineModulate = new Color(OutlineModulate, _outlineAlpha * shown);
        }

        private void Fit()
        {
            var camera = GetViewport()?.GetCamera3D();
            if (camera == null) return;
            float scale = LabelSizing.Scale(WorldHeight, Depth(camera), Mathf.DegToRad(camera.Fov), MaxScreenFraction);
            Scale = new Vector3(scale, scale, scale);
        }

        /// <summary>Distance along the camera's view axis: what perspective divides by.</summary>
        private float Depth(Camera3D camera)
        {
            var view = camera.GlobalTransform;
            return Mathf.Max(0.05f, (GlobalPosition - view.Origin).Dot(-view.Basis.Z));
        }
    }
}
