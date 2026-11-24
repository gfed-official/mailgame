using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// The vehicles of the run (doc 04 §3.1): a parked bike where it was left, turned the way it
    /// points; a ridden one under its rider — the local rider's where the prediction has them, the
    /// others' where their body is drawn —, its wheels turning with the ground it covers. A
    /// postman's bike, steel and enamel: a diamond frame in Post Office red, black tyres on
    /// spoked wheels, a sprung saddle, swept bars with a lamp, and a rack behind with the canvas
    /// mail satchel that is its cargo.
    /// </summary>
    public partial class VehiclesView : Node3D
    {
        private static readonly Color Frame = new Color("b3382c"), Steel = new Color("9aa3ad"), Tyre = new Color("1f2226"),
            Leather = new Color("5a3b25"), Canvas = new Color("c9a66b"), Strap = new Color("6e4a2e"), Lamp = new Color("fff1c1");
        private const float WheelRadius = 0.34f, WheelBase = 0.55f;
        /// <summary>The saddle's height over the ground: a rider is drawn this much higher than on foot.</summary>
        public const float SaddleRise = 0.32f;

        private sealed class Drawn
        {
            public Node3D Root = null!;
            public Node3D FrontWheel = null!, RearWheel = null!;
            public float WheelRadius = VehiclesView.WheelRadius;
            public Vector3 Last;
            public bool Placed;
        }

        private readonly Dictionary<EntityId, Drawn> _drawn = new Dictionary<EntityId, Drawn>();
        private readonly List<EntityId> _gone = new List<EntityId>();
        private ClientRuntime _client = null!;
        private PlayersView? _players;

        /// <summary>Vehicles drawn, and of them those under a rider, as of the last frame.</summary>
        public int Shown { get; private set; }
        public int RiddenShown { get; private set; }

        public void Setup(ClientRuntime client, PlayersView players)
        {
            _client = client;
            _players = players;
            Sync(0.0);
        }

        public override void _Process(double delta) => Sync(delta);

        /// <summary>Where a vehicle is drawn right now; false for one that is not.</summary>
        public bool TryGetPosition(EntityId id, out Vector3 position)
        {
            bool known = _drawn.TryGetValue(id, out var d);
            position = known ? d!.Root.Position : Vector3.Zero;
            return known;
        }

        private void Sync(double delta)
        {
            var world = _client?.World;
            int shown = 0, ridden = 0;
            if (world != null)
            {
                foreach (var v in world.Vehicles.Values)
                {
                    if (!_drawn.TryGetValue(v.Id, out var d))
                    {
                        d = v.Def.BodyOffset > 0f ? BuildTruck(v.Id, v.Def) : Build(v.Id);
                        _drawn[v.Id] = d;
                    }
                    Vector3 position;
                    float heading;
                    if (!v.Ridden)
                    {
                        position = new Vector3(v.X, world.Map.HeightAt(v.X, v.Z), v.Z);
                        heading = v.Heading;
                    }
                    else if (v.Rider == world.LocalId && world.Local.Riding)
                    {
                        // Under the camera: where the local body is drawn, pointing as the prediction has it.
                        position = _players?.LocalView?.Position ?? new Vector3(world.Local.X, world.Local.Y, world.Local.Z);
                        heading = Sim.Vehicles.Vehicle.YawOf(world.Local.HeadX, world.Local.HeadZ, v.Heading);
                    }
                    else if (_players != null && _players.TryGetPosition(v.Rider, out var body) && world.Players.TryGetValue(v.Rider, out var rider))
                    {
                        position = new Vector3(body.X, body.Y - SaddleRise, body.Z);
                        heading = rider.Ride != 0 ? rider.Heading : v.Heading;
                    }
                    else
                    {
                        position = new Vector3(v.X, world.Map.HeightAt(v.X, v.Z), v.Z);
                        heading = v.Heading;
                    }
                    Place(d, position, heading);
                    shown++;
                    if (v.Ridden) ridden++;
                }
            }
            Shown = shown;
            RiddenShown = ridden;

            int known = world?.Vehicles.Count ?? 0;
            if (_drawn.Count == known) return;
            _gone.Clear();
            foreach (var id in _drawn.Keys)
                if (world == null || !world.Vehicles.ContainsKey(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                _drawn[id].Root.QueueFree();
                _drawn.Remove(id);
            }
        }

        private static void Place(Drawn d, Vector3 position, float heading)
        {
            // The wheels turn with the ground covered along the way the bike points.
            if (d.Placed)
            {
                var step = position - d.Last;
                float along = -(step.X * Mathf.Sin(heading) + step.Z * Mathf.Cos(heading));
                if (Mathf.Abs(along) < 3f)
                {
                    float turn = -along / d.WheelRadius;
                    d.FrontWheel.RotateObjectLocal(Vector3.Right, turn);
                    d.RearWheel.RotateObjectLocal(Vector3.Right, turn);
                }
            }
            d.Last = position;
            d.Placed = true;
            d.Root.Position = position;
            d.Root.Rotation = new Vector3(0f, heading, 0f);
        }

        /// <summary>A bike pointing along −Z (yaw 0), its wheels on the ground.</summary>
        private Drawn Build(EntityId id)
        {
            var root = new Node3D { Name = $"Bike{id.Counter}" };
            AddChild(root);
            var frame = MeshKit.Mat(Frame, 0.45f, 0.35f);
            var steel = MeshKit.Mat(Steel, 0.35f, 0.8f);
            var tyre = MeshKit.Mat(Tyre, 0.9f);
            var leather = MeshKit.Mat(Leather, 0.7f);

            var front = Wheel(root, "FrontWheel", new Vector3(0f, WheelRadius, -WheelBase), tyre, steel);
            var rear = Wheel(root, "RearWheel", new Vector3(0f, WheelRadius, WheelBase), tyre, steel);

            var hubF = new Vector3(0f, WheelRadius, -WheelBase);
            var hubR = new Vector3(0f, WheelRadius, WheelBase);
            var crank = new Vector3(0f, 0.3f, 0.02f);
            var seat = new Vector3(0f, 0.82f, 0.2f);
            var head = new Vector3(0f, 0.84f, -0.4f);
            var headLow = new Vector3(0f, 0.72f, -0.44f);
            Tube(root, seat, head, 0.026f, frame);
            Tube(root, headLow, crank, 0.03f, frame);
            Tube(root, crank, seat, 0.028f, frame);
            foreach (float side in new[] { -0.05f, 0.05f })
            {
                Tube(root, crank + new Vector3(side, 0f, 0f), hubR + new Vector3(side, 0f, 0f), 0.016f, frame);
                Tube(root, seat + new Vector3(side * 0.6f, -0.04f, 0f), hubR + new Vector3(side, 0f, 0f), 0.014f, frame);
                Tube(root, headLow + new Vector3(side * 0.7f, 0f, 0f), hubF + new Vector3(side, 0f, 0f), 0.016f, steel);
            }
            // The head tube and the stem; the bars swept back to the rider.
            Tube(root, headLow, head + new Vector3(0f, 0.12f, 0.02f), 0.032f, frame);
            var bar = head + new Vector3(0f, 0.14f, 0.04f);
            Tube(root, bar + new Vector3(-0.26f, 0.02f, 0.1f), bar + new Vector3(0.26f, 0.02f, 0.1f), 0.014f, steel);
            foreach (float side in new[] { -0.27f, 0.27f })
                root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.02f, 0.02f, 0.1f, 10), leather, bar + new Vector3(side, 0.02f, 0.1f), new Vector3(0f, 0f, 90f)));
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.05f, 0.045f, 0.07f, 14), steel, head + new Vector3(0f, 0.02f, -0.06f), new Vector3(90f, 0f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.Sphere(0.035f, 12), MeshKit.Glow(Lamp, 1.6f), head + new Vector3(0f, 0.02f, -0.1f)));

            // The saddle on its post, the cranks and pedals.
            Tube(root, seat, seat + new Vector3(0f, 0.1f, 0.03f), 0.016f, steel);
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.16f, 0.06f, 0.26f), 0.03f), leather, seat + new Vector3(0f, 0.13f, 0.03f)));
            root.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.08f, 0.08f, 0.03f, 18), steel, crank + new Vector3(0.06f, 0f, 0f), new Vector3(0f, 0f, 90f)));
            foreach (float side in new[] { -1f, 1f })
            {
                var pedal = crank + new Vector3(side * 0.13f, side * 0.13f, 0f);
                Tube(root, crank + new Vector3(side * 0.07f, 0f, 0f), pedal, 0.012f, steel);
                root.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(0.09f, 0.02f, 0.06f)), tyre, pedal + new Vector3(side * 0.04f, 0f, 0f)));
            }
            Tube(root, crank + new Vector3(0.07f, 0f, 0f), hubR + new Vector3(0.07f, 0f, 0f), 0.008f, MeshKit.Mat(Tyre, 0.4f, 0.6f));

            // The rack over the rear wheel, and the satchel strapped to it: the cargo.
            var rackTop = hubR + new Vector3(0f, 0.42f, 0.02f);
            root.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(0.24f, 0.02f, 0.4f)), steel, rackTop));
            foreach (float side in new[] { -0.11f, 0.11f })
                Tube(root, rackTop + new Vector3(side, 0f, 0.16f), hubR + new Vector3(side * 0.6f, 0f, 0f), 0.01f, steel);
            var satchel = MeshKit.Mat(Canvas, 0.85f);
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.34f, 0.24f, 0.36f), 0.06f), satchel, rackTop + new Vector3(0f, 0.13f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.36f, 0.06f, 0.3f), 0.03f), MeshKit.Mat(Canvas.Darkened(0.15f), 0.85f), rackTop + new Vector3(0f, 0.25f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(0.37f, 0.25f, 0.04f)), MeshKit.Mat(Strap, 0.7f), rackTop + new Vector3(0f, 0.13f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(0.01f, 0.1f, 0.12f)), MeshKit.Mat(Palette.Intake, 0.6f), rackTop + new Vector3(0.175f, 0.13f, 0.08f)));

            // Mudguards and the kickstand it stands on.
            foreach (var hub in new[] { hubF, hubR })
                root.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(0.07f, 0.015f, 0.42f)), frame, hub + new Vector3(0f, WheelRadius + 0.05f, 0f)));
            Tube(root, crank + new Vector3(-0.05f, 0f, 0.08f), new Vector3(-0.2f, 0.01f, 0.16f), 0.012f, steel);

            return new Drawn { Root = root, FrontWheel = front, RearWheel = rear };
        }

        private static readonly Color VanBody = new Color("efe9dc"), VanStripe = new Color("c23a2b"), VanGlass = new Color("27323b"), VanTrim = new Color("3a3f46");
        private const float TruckWheel = 0.4f;

        /// <summary>
        /// A Mail Truck pointing along −Z (doc 04 §3.1), after the reference's chunky delivery vans:
        /// a cream body on a dark chassis, a cab with a wide dark windscreen and a taller cargo box
        /// behind with the post's red band; at its rear the roll-up door of its loading face in the
        /// belts' amber, lamps lit in front, four fat wheels on two axles.
        /// </summary>
        private Drawn BuildTruck(EntityId id, Sim.Content.VehicleDef def)
        {
            var root = new Node3D { Name = $"Truck{id.Counter}" };
            AddChild(root);
            float len = def.Length, half = len * 0.5f, w = 1.8f;
            var body = MeshKit.Mat(VanBody, 0.55f, 0.1f);
            var stripe = MeshKit.Mat(VanStripe, 0.5f, 0.1f);
            var glass = MeshKit.Mat(VanGlass, 0.15f, 0.5f);
            var trim = MeshKit.Mat(VanTrim, 0.6f, 0.4f);
            var tyre = MeshKit.Mat(Tyre, 0.9f);
            var steel = MeshKit.Mat(Steel, 0.35f, 0.8f);
            // Chassis and bumpers.
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(w - 0.1f, 0.3f, len - 0.1f), 0.08f), trim, new Vector3(0f, TruckWheel + 0.05f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(w, 0.22f, 0.16f), 0.06f), trim, new Vector3(0f, 0.42f, -half + 0.02f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(w, 0.22f, 0.16f), 0.06f), trim, new Vector3(0f, 0.42f, half - 0.02f)));
            // The cab, its bonnet and windscreen.
            float cabLen = 1.25f, cabZ = -half + cabLen * 0.5f;
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(w, 1.15f, cabLen), 0.18f), body, new Vector3(0f, 1.15f, cabZ)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(w - 0.2f, 0.5f, 0.06f), 0.04f), glass, new Vector3(0f, 1.42f, -half + 0.02f), new Vector3(-12f, 0f, 0f)));
            foreach (float side in new[] { -1f, 1f })
            {
                root.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(0.04f, 0.42f, 0.7f)), glass, new Vector3(side * (w * 0.5f + 0.005f), 1.4f, cabZ - 0.05f)));
                root.AddChild(MeshKit.Instance(MeshKit.Sphere(0.08f, 12), MeshKit.Glow(Lamp, 1.8f), new Vector3(side * (w * 0.5f - 0.25f), 0.75f, -half - 0.02f)));
                root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.08f, 0.12f, 0.18f), 0.03f), trim, new Vector3(side * (w * 0.5f + 0.06f), 1.45f, -half + 0.25f)));
            }
            // The cargo box: taller, the post's red band round it.
            float boxLen = len - cabLen - 0.05f, boxZ = half - boxLen * 0.5f;
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(w + 0.04f, 1.55f, boxLen), 0.12f), body, new Vector3(0f, 1.37f, boxZ)));
            root.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(w + 0.07f, 0.2f, boxLen - 0.2f)), stripe, new Vector3(0f, 1.25f, boxZ)));
            // Its rear: a roll-up door in amber slats, the loading face.
            var amber = MeshKit.Mat(new Color("e0a526"), 0.6f);
            root.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(w - 0.25f, 1.25f, 0.04f)), trim, new Vector3(0f, 1.3f, half + 0.01f)));
            for (int k = 0; k < 5; k++)
                root.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(w - 0.35f, 0.18f, 0.03f)), amber, new Vector3(0f, 0.8f + k * 0.24f, half + 0.04f)));
            // Wheels on two axles.
            var front = new Node3D { Name = "FrontWheel", Position = new Vector3(0f, TruckWheel, -half + 0.75f) };
            var rear = new Node3D { Name = "RearWheel", Position = new Vector3(0f, TruckWheel, half - 0.7f) };
            root.AddChild(front);
            root.AddChild(rear);
            foreach (var axle in new[] { front, rear })
                foreach (float side in new[] { -1f, 1f })
                {
                    axle.AddChild(MeshKit.Instance(MeshKit.Cylinder(TruckWheel, TruckWheel, 0.3f, 18), tyre, new Vector3(side * (w * 0.5f - 0.12f), 0f, 0f), new Vector3(0f, 0f, 90f)));
                    axle.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.2f, 0.2f, 0.32f, 12), steel, new Vector3(side * (w * 0.5f - 0.12f), 0f, 0f), new Vector3(0f, 0f, 90f)));
                }
            return new Drawn { Root = root, FrontWheel = front, RearWheel = rear, WheelRadius = TruckWheel };
        }

        private static Node3D Wheel(Node3D root, string name, Vector3 hub, Material tyre, Material steel)
        {
            var wheel = new Node3D { Name = name, Position = hub };
            root.AddChild(wheel);
            // A torus lies flat; turned onto its edge it stands in the bike's plane.
            wheel.AddChild(MeshKit.Instance(MeshKit.Torus(WheelRadius - 0.045f, WheelRadius), tyre, Vector3.Zero, new Vector3(0f, 0f, 90f)));
            wheel.AddChild(MeshKit.Instance(MeshKit.Torus(WheelRadius - 0.06f, WheelRadius - 0.045f), steel, Vector3.Zero, new Vector3(0f, 0f, 90f)));
            wheel.AddChild(MeshKit.Instance(MeshKit.Cylinder(0.035f, 0.035f, 0.12f, 12), steel, Vector3.Zero, new Vector3(0f, 0f, 90f)));
            int spokes = MeshKit.LowDetail ? 6 : 12;
            for (int i = 0; i < spokes; i++)
            {
                float a = Mathf.Tau * i / spokes;
                var rim = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a)) * (WheelRadius - 0.055f);
                Tube(wheel, new Vector3(i % 2 == 0 ? -0.04f : 0.04f, 0f, 0f), rim, 0.004f, steel);
            }
            return wheel;
        }

        /// <summary>A round tube between two points of the bike.</summary>
        private static void Tube(Node3D parent, Vector3 from, Vector3 to, float radius, Material material)
        {
            var span = to - from;
            float length = span.Length();
            if (length < 1e-4f) return;
            var tube = new MeshInstance3D
            {
                Mesh = MeshKit.Cylinder(radius, radius, length, MeshKit.LowDetail ? 6 : 10),
                MaterialOverride = material,
                Position = (from + to) * 0.5f,
                Basis = new Basis(new Quaternion(Vector3.Up, span / length)),
            };
            parent.AddChild(tube);
        }
    }
}
