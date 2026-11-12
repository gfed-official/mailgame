using Godot;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The local player's eyes (doc 00: 3D, first person). Sits at eye height on the local player
    /// view and looks along the client's yaw and pitch: the same <see cref="ViewRay"/> the interact
    /// prompt is aimed with. The body is not drawn; the mail in hand is.
    /// </summary>
    public partial class FirstPersonCamera : Camera3D
    {
        public Node3D? Target;

        private ClientRuntime _client = null!;
        private MeshInstance3D _held = null!;

        public void Setup(ClientRuntime client)
        {
            _client = client;
            Fov = 75f;
            Near = 0.05f;
            Current = true;
            _held = new MeshInstance3D
            {
                Name = "HeldMail",
                Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.2f, 0.03f), Material = Palette.Flat(new Color("fff8e7")) },
                Position = new Vector3(0.3f, -0.26f, -0.6f),
                Rotation = new Vector3(-0.5f, 0.25f, 0f),
                Visible = false,
            };
            AddChild(_held);
            Sync();
        }

        public override void _Process(double delta) => Sync();

        /// <summary>Where the camera belongs for a player whose feet are at <paramref name="feet"/>.</summary>
        public static Vector3 EyePosition(Vector3 feet) => feet + new Vector3(0f, SimConstants.EyeHeight, 0f);

        /// <summary>Places the camera for this frame; runs after the player views have moved.</summary>
        public void Sync()
        {
            var world = _client?.World;
            if (world == null) return;
            if (Target != null) Position = EyePosition(Target.Position);
            Rotation = new Vector3(_client!.Input.PitchRadians, _client.Input.YawRadians, 0f);

            int slot = _client.Input.HeldSlot;
            var held = world.HasLocal && slot > 0 ? world.Hotbar.EntryAt(slot, 0) : null;
            _held.Visible = held != null && held.Stack.Kind == StackKind.Mail;
        }
    }
}
