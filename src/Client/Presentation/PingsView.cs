using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The team's pings in the world (doc 09 §2.6 "3D marker, 10 s"; design choice 117): a beam of
    /// light in the ping's colour from a ring on the ground, a diamond at its head that turns, and
    /// "Ada · Danger" over it, readable from anywhere; it fades in its last two seconds.
    /// </summary>
    public partial class PingsView : Node3D
    {
        private ClientRuntime _client = null!;
        private readonly Dictionary<PingEvent, Node3D> _shown = new Dictionary<PingEvent, Node3D>();
        private readonly List<PingEvent> _gone = new List<PingEvent>();
        private float _spin;

        /// <summary>Markers standing now (for the smoke).</summary>
        public int Shown => _shown.Count;

        public void Setup(ClientRuntime client) => _client = client;

        /// <summary>A ping's colour: amber to look, green to deliver, blue to build, red for danger, violet for materials.</summary>
        public static Color Colour(PingKind kind) => kind switch
        {
            PingKind.Deliver => new Color("4cd964"),
            PingKind.Build => new Color("4fb3ff"),
            PingKind.Danger => new Color("ff3b30"),
            PingKind.Materials => new Color("c38bff"),
            _ => new Color("ffbf47"),
        };

        /// <summary>The marker of a ping, if it is drawn.</summary>
        public Node3D? MarkerOf(PingEvent ping) => _shown.TryGetValue(ping, out var n) ? n : null;

        public override void _Process(double delta)
        {
            var world = _client?.World;
            _gone.Clear();
            foreach (var ping in _shown.Keys)
                if (world == null || !world.Pings.Contains(ping)) _gone.Add(ping);
            foreach (var ping in _gone)
            {
                _shown[ping].QueueFree();
                _shown.Remove(ping);
            }
            if (world == null) return;
            _spin += (float)delta * 2f;
            foreach (var ping in world.Pings)
            {
                if (!_shown.TryGetValue(ping, out var marker))
                {
                    marker = Build(world, ping);
                    AddChild(marker);
                    _shown[ping] = marker;
                }
                double left = SimConstants.TicksToSeconds((long)ping.ExpiresTick - (long)world.ServerTick);
                float alpha = (float)Mathf.Clamp(left / 2.0, 0.0, 1.0);
                marker.GetNode<Node3D>("Diamond").Rotation = new Vector3(0f, _spin, 0f);
                foreach (var child in marker.FindChildren("*", "", true, false))
                {
                    if (child is Label3D label) label.Modulate = new Color(label.Modulate, alpha);
                    else if (child is GeometryInstance3D geometry) geometry.Transparency = 1f - alpha;
                }
            }
        }

        private static Node3D Build(ClientWorld world, PingEvent ping)
        {
            var colour = Colour(ping.Kind);
            float y = world.Map.HeightAt(ping.X, ping.Z);
            var root = new Node3D { Name = $"Ping{ping.Player.Counter}_{ping.ExpiresTick}", Position = new Vector3(ping.X, y, ping.Z) };
            var glow = MeshKit.Glow(colour, 2.2f);
            var beam = MeshKit.Instance(MeshKit.Cylinder(0.07f, 0.07f, 7f, 10), glow, new Vector3(0f, 3.5f, 0f), default, "Beam");
            beam.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            root.AddChild(beam);
            var ring = MeshKit.Instance(MeshKit.Torus(0.55f, 0.75f), glow, new Vector3(0f, 0.05f, 0f), default, "Ring");
            ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            root.AddChild(ring);
            // The diamond turns on a node of its own; the cube in it stands on a corner.
            var diamond = new Node3D { Name = "Diamond", Position = new Vector3(0f, 7.3f, 0f) };
            root.AddChild(diamond);
            var cube = MeshKit.Instance(MeshKit.Box(new Vector3(0.42f, 0.42f, 0.42f)), glow, Vector3.Zero, default, "Cube");
            cube.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            cube.Basis = new Basis(Vector3.Forward, Mathf.Pi * 0.25f) * new Basis(Vector3.Right, Mathf.Pi * 0.25f);
            diamond.AddChild(cube);
            var label = new SizedLabel3D
            {
                Name = "Label",
                Text = HudText.PingLabel(world, ping),
                Position = new Vector3(0f, 8.1f, 0f),
                FontSize = 64,
                PixelSize = 0.01f,
                OutlineSize = 10,
                Modulate = colour.Lightened(0.35f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                Rank = 3,
            };
            root.AddChild(label);
            return root;
        }
    }
}
