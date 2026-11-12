using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// A billboard label in the world (house number, sign, name tag) that keeps its real size from
    /// far away but is capped on screen: up close it shrinks instead of filling the first-person view.
    /// </summary>
    public partial class SizedLabel3D : Label3D
    {
        public const string Group = "sized_labels";

        /// <summary>The most of the screen height the text (one line, font size × pixel size) may cover.</summary>
        public float MaxScreenFraction = 0.05f;

        /// <summary>Height of one line of text in metres at scale 1.</summary>
        public float WorldHeight => FontSize * PixelSize;

        public override void _Ready()
        {
            AddToGroup(Group);
            // After the camera has moved this frame.
            ProcessPriority = 100;
            Fit();
        }

        public override void _Process(double delta) => Fit();

        /// <summary>Share of the screen height the text covers right now for <paramref name="camera"/>.</summary>
        public float ScreenFraction(Camera3D camera) =>
            LabelSizing.ScreenFraction(WorldHeight * Scale.Y, Depth(camera), Mathf.DegToRad(camera.Fov));

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
