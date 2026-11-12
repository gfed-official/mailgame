using Godot;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>Third-person orbit camera (doc 00: 3D, third-person). The input layer turns it; movement is relative to its yaw.</summary>
    public partial class CameraRig : Node3D
    {
        public const float MinPitch = -1.2f;
        public const float MaxPitch = 0.3f;

        public Node3D? Target;
        public float Distance = 7f;
        public float Height = 1.7f;
        /// <summary>Radians about +Y; 0 looks along −Z.</summary>
        public float Yaw;
        public float Pitch = -0.45f;

        public Camera3D Camera { get; private set; } = null!;

        public override void _Ready()
        {
            Camera = new Camera3D { Name = "Camera", Position = new Vector3(0f, 0f, Distance), Fov = 70f, Current = true };
            AddChild(Camera);
        }

        public void Look(float yawDelta, float pitchDelta)
        {
            Yaw = Mathf.Wrap(Yaw + yawDelta, -Mathf.Pi, Mathf.Pi);
            Pitch = Mathf.Clamp(Pitch + pitchDelta, MinPitch, MaxPitch);
        }

        public override void _Process(double delta)
        {
            if (Target != null) Position = Target.Position + new Vector3(0f, Height, 0f);
            Rotation = new Vector3(Pitch, Yaw, 0f);
        }
    }
}
