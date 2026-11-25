using Godot;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// What the carriers have built (doc 04 §1), drawn as the <see cref="ConstructField"/> has it:
    /// a Wooden Wall is a palisade of pointed logs along its arms, so that two walls that meet
    /// close their corner on screen as they do in the sim; a Chest is the map's banded chest. A
    /// wall worn below half its HP is drawn darker and leaning. A Gate is two posts under a beam
    /// in the palisade's line, its two plank leaves swinging open while a carrier is near; an
    /// Alarm Post is a tall post with a roof, a brass bell and a lamp that flashes red while a
    /// raider is within its reach. Batched like the town, drawn anew only when something is
    /// built, taken down or hit; the leaves and lamps move every frame. In Build mode the ghost
    /// of the recipe stands on the tile under the crosshair, green where it can be built and
    /// red where not.
    /// </summary>
    public partial class ConstructsView : Node3D
    {
        private static readonly Color LogColour = new Color("8a5e3b"), LogDark = new Color("5a3b25"), RailColour = new Color("6e4a2e");
        private static readonly Color GhostOk = new Color(0.35f, 0.95f, 0.45f, 0.42f), GhostBad = new Color(1f, 0.3f, 0.28f, 0.42f);
        private const float LogRadius = 0.15f, LogStep = 0.3f;

        /// <summary>Constructs drawn as of the last redraw, and how often that was.</summary>
        public int Drawn { get; private set; }
        public int Redraws { get; private set; }
        /// <summary>Whether the ghost is up, and in which colour.</summary>
        public bool GhostShown => _ghost != null && _ghost.Visible;
        public bool GhostGreen { get; private set; }
        /// <summary>Where the ghost stands (the middle of its tile on the ground).</summary>
        public Vector3 GhostPosition => _ghost?.Position ?? Vector3.Zero;

        private ClientRuntime? _client;
        private BuildController? _build;
        private ConstructField? _drawn;
        private int _version = -1;
        private Node3D? _built, _ghost;
        private (BuildingDef?, int, bool, string) _ghostShape;

        private sealed class GateLeaves
        {
            public Construct Built = null!;
            public Node3D Left = null!, Right = null!;
            public float Open;
        }

        private readonly System.Collections.Generic.List<GateLeaves> _gates = new System.Collections.Generic.List<GateLeaves>();
        private readonly System.Collections.Generic.List<(Construct Built, MeshInstance3D Lamp)> _lamps = new System.Collections.Generic.List<(Construct, MeshInstance3D)>();
        /// <summary>Each inserter's arm (a pivot turned about its post) and the piece it carries over.</summary>
        private readonly System.Collections.Generic.List<(Construct Built, Node3D Arm, MeshInstance3D Piece)> _arms = new System.Collections.Generic.List<(Construct, Node3D, MeshInstance3D)>();
        /// <summary>Inserters whose arm is part way through a swing (for the smoke).</summary>
        public int ArmsSwinging { get; private set; }
        /// <summary>Each Oil Pump's walking beam, nodding about its pivot while the run works.</summary>
        private readonly System.Collections.Generic.List<(Construct Built, Node3D Beam)> _beams = new System.Collections.Generic.List<(Construct, Node3D)>();
        private float _nod;
        /// <summary>Oil Pumps nodding, as of the last frame (for the smoke).</summary>
        public int PumpsNodding { get; private set; }
        private float _blink;
        /// <summary>Each turret's gun head (turned about its pedestal), its muzzle flash and its tracer.</summary>
        private readonly System.Collections.Generic.List<TurretGun> _guns = new System.Collections.Generic.List<TurretGun>();

        private sealed class TurretGun
        {
            public Construct Built = null!;
            public Node3D Head = null!;
            public MeshInstance3D Flash = null!, Tracer = null!;
            public float Yaw;
            public double Drawn = -1;
        }

        /// <summary>Turret shots the view has taken up since it began (for the smoke), and turrets whose gun is turned on a raider this frame.</summary>
        public int TurretShotsDrawn { get; private set; }
        public int TurretsAiming { get; private set; }

        /// <summary>Gates swinging open (a carrier is near), and Alarm Post lamps lit, as of the last frame.</summary>
        public int GatesOpening { get; private set; }
        public int LampsLit { get; private set; }

        /// <summary>How near a carrier has to be for a gate to swing open on screen.</summary>
        private const float GateOpenRange = 2.6f;

        public void Setup(ClientRuntime client, BuildController? build)
        {
            _client = client;
            _build = build;
            _built = new Node3D { Name = "Built" };
            AddChild(_built);
            _ghost = new Node3D { Name = "Ghost", Visible = false };
            AddChild(_ghost);
            Refresh();
        }

        public override void _Process(double delta)
        {
            Refresh();
            Animate((float)delta);
        }

        /// <summary>The gates swing towards open while a carrier is near; the lamps flash while a raider is in reach.</summary>
        public void Animate(float delta)
        {
            var world = _client?.World;
            if (world == null) return;
            int opening = 0;
            foreach (var g in _gates)
            {
                bool near = false;
                if (world.HasLocal && !world.Local.Dead) near = Near(world.Local.X, world.Local.Z, g.Built);
                foreach (var p in world.Players.Values)
                {
                    if (near) break;
                    if (p.Id == world.LocalId || p.Dead || !p.Interpolate(world.ServerTick - ClientRuntime.InterpolationDelayTicks, out float x, out _, out float z, out _)) continue;
                    near = Near(x, z, g.Built);
                }
                g.Open = Mathf.MoveToward(g.Open, near ? 1f : 0f, delta * 2.5f);
                float angle = Mathf.Ease(g.Open, -2f) * Mathf.DegToRad(100f);
                g.Left.Rotation = new Vector3(0f, -angle, 0f);
                g.Right.Rotation = new Vector3(0f, angle, 0f);
                if (near) opening++;
            }
            GatesOpening = opening;
            _blink += delta;
            int lit = 0;
            foreach (var (built, lamp) in _lamps)
            {
                bool alarm = false;
                foreach (var e in world.Enemies.Values)
                {
                    float dx = e.X - built.X, dz = e.Z - built.Z;
                    if (dx * dx + dz * dz > built.Def.RevealRadius * built.Def.RevealRadius) continue;
                    alarm = true;
                    break;
                }
                lamp.Visible = alarm && Mathf.PosMod(_blink, 0.8f) < 0.5f;
                if (alarm) lit++;
            }
            LampsLit = lit;

            // An inserter's arm rests over what is behind it; when it has moved a piece it swings
            // round to the front with it and back again, in the time of one move.
            int swinging = 0;
            foreach (var (built, arm, piece) in _arms)
            {
                float t = 1f;
                if (world.InserterSwings.TryGetValue(built.Tz * world.Map.Cols + built.Tx, out var swing))
                    t = (float)((world.ServerTick - swing.Tick) / (SimConstants.TickRate / Mathf.Max(0.1f, built.Def.Rate)));
                t = Mathf.Clamp(t, 0f, 1f);
                float turn = t < 0.5f ? Mathf.SmoothStep(0f, 1f, t * 2f) : Mathf.SmoothStep(1f, 0f, (t - 0.5f) * 2f);
                arm.Rotation = new Vector3(0f, ArmYaw(built.Facing) + Mathf.Pi * (1f - turn), 0f);
                piece.Visible = t < 0.5f;
                if (t < 1f) swinging++;
            }
            ArmsSwinging = swinging;

            // An Oil Pump's beam nods while the run is in a working phase (it pumps in Prep and Delivery).
            bool working = world.Global.Phase is Sim.Run.RunPhase.Prep or Sim.Run.RunPhase.Delivery;
            if (working) _nod += (float)delta;
            int nodding = 0;
            foreach (var (_, beam) in _beams)
            {
                beam.Rotation = new Vector3(Mathf.Sin(_nod * Mathf.Tau / PumpStrokeSeconds) * 0.28f, 0f, 0f);
                if (working) nodding++;
            }
            PumpsNodding = nodding;

            // A turret's gun turns on the raider it last shot at, or the nearest one in its range;
            // a shot is a muzzle flash and a tracer to where the raider stood, for a tenth of a second.
            int aiming = 0;
            foreach (var g in _guns)
            {
                var c = g.Built;
                float tx = float.NaN, tz = 0f;
                bool recent = world.TurretShots.TryGetValue(c.Tz * world.Map.Cols + c.Tx, out var shot);
                if (recent && world.ServerTick - shot.Tick < SimConstants.TickRate) (tx, tz) = (shot.X, shot.Z);
                else
                {
                    float best = c.Def.ShotRange * c.Def.ShotRange;
                    foreach (var e in world.Enemies.Values)
                    {
                        float dx = e.X - c.CentreX, dz = e.Z - c.CentreZ, d = dx * dx + dz * dz;
                        if (d <= best) { best = d; tx = e.X; tz = e.Z; }
                    }
                }
                if (!float.IsNaN(tx))
                {
                    float want = Mathf.Atan2(tx - c.CentreX, tz - c.CentreZ);
                    g.Yaw = Mathf.RotateToward(g.Yaw, want, delta * TurretTurnRate);
                    aiming++;
                }
                g.Head.Rotation = new Vector3(0f, g.Yaw, 0f);
                bool flash = recent && world.ServerTick - shot.Tick < SimConstants.TickRate * 0.1;
                g.Flash.Visible = flash;
                g.Tracer.Visible = flash;
                if (flash)
                {
                    var muzzle = g.Head.GlobalPosition + new Vector3(Mathf.Sin(g.Yaw), 0f, Mathf.Cos(g.Yaw)) * TurretMuzzle;
                    var to = new Vector3(shot.X, world.Map.HeightAt(shot.X, shot.Z) + 1.1f, shot.Z);
                    var span = to - muzzle;
                    float len = span.Length();
                    if (len > 0.1f)
                    {
                        g.Tracer.GlobalTransform = new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(span.X, span.Z)) * new Basis(Vector3.Right, -Mathf.Asin(span.Y / len)), (muzzle + to) * 0.5f);
                        g.Tracer.Scale = new Vector3(1f, 1f, len);
                    }
                }
                if (recent && g.Drawn != shot.Tick) { g.Drawn = shot.Tick; TurretShotsDrawn++; }
            }
            TurretsAiming = aiming;
        }

        /// <summary>Radians a second a turret's gun turns; how far ahead of the pedestal its muzzle is.</summary>
        private const float TurretTurnRate = 6f, TurretMuzzle = 0.95f, TurretHeadHeight = 1.45f;

        /// <summary>A turret's gun head on its own node over the pedestal, with the flash at the muzzle and the tracer (both hidden till it fires).</summary>
        private void TurretHead(Construct c, Vector3 at, float yaw, bool worn)
        {
            var head = new Node3D { Name = $"Turret{c.Tx}_{c.Tz}", Position = at + new Vector3(0f, TurretHeadHeight, 0f), Rotation = new Vector3(0f, yaw, 0f) };
            var batch = new MeshBatch();
            TurretHeadInto(batch, null, worn);
            batch.Flush(head, "Gun");
            var flash = MeshKit.Instance(MeshKit.Sphere(0.16f, 10), MeshKit.Glow(new Color("ffd27a"), 6f), new Vector3(0f, 0.02f, TurretMuzzle), default, "Flash");
            flash.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            flash.Visible = false;
            head.AddChild(flash);
            _built!.AddChild(head);
            var tracer = MeshKit.Instance(MeshKit.Box(new Vector3(0.04f, 0.04f, 1f)), MeshKit.Glow(new Color("ffe9a8"), 4f), at, default, $"Tracer{c.Tx}_{c.Tz}");
            tracer.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            tracer.Visible = false;
            _built.AddChild(tracer);
            _guns.Add(new TurretGun { Built = c, Head = head, Flash = flash, Tracer = tracer, Yaw = yaw });
        }

        /// <summary>
        /// A turret's base: a concrete pad, a squat steel ring with hazard trim and a pedestal up to
        /// the gun's bearing (the gun is drawn apart, as it turns; the ghost has it in, ahead).
        /// </summary>
        public static void TurretBase(MeshBatch batch, Vector3 at, int facing, Material? only, bool worn)
        {
            var pad = only ?? MeshKit.Mat(worn ? new Color("6d6a64") : new Color("a8a49b"), 0.9f);
            var steel = only ?? MeshKit.Mat(worn ? SorterDark : SorterBody, 0.45f, 0.6f);
            var orange = only ?? MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            var dark = only ?? MeshKit.Mat(SorterDark, 0.6f, 0.4f);
            batch.Box(at + new Vector3(0f, 0.08f, 0f), new Vector3(1.8f, 0.16f, 1.8f), pad, 0.05f);
            batch.Add(MeshKit.Cylinder(0.72f, 0.8f, 0.36f, 20), steel, at + new Vector3(0f, 0.34f, 0f));
            batch.Add(MeshKit.Cylinder(0.74f, 0.74f, 0.08f, 20), orange, at + new Vector3(0f, 0.56f, 0f));
            // Four bolted feet round the ring.
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.Pi * 0.5f + Mathf.Pi * 0.25f;
                batch.Box(at + new Vector3(Mathf.Sin(a) * 0.72f, 0.22f, Mathf.Cos(a) * 0.72f), new Vector3(0.22f, 0.12f, 0.22f), dark, 0.03f, new Vector3(0f, Mathf.RadToDeg(a), 0f));
            }
            batch.Add(MeshKit.Cylinder(0.3f, 0.38f, 0.8f, 16), dark, at + new Vector3(0f, 0.98f, 0f));
            batch.Add(MeshKit.Cylinder(0.46f, 0.46f, 0.1f, 18), steel, at + new Vector3(0f, 1.36f, 0f));
            if (only == null) return;
            var old = batch.Frame;
            batch.Frame = new Transform3D(new Basis(Vector3.Up, PumpYaw(facing)), at + new Vector3(0f, TurretHeadHeight, 0f));
            TurretHeadInto(batch, only, worn);
            batch.Frame = old;
        }

        /// <summary>The gun head about its bearing (its origin), pointing +Z: a sloped steel housing, twin barrels with shrouds, an ammo drum and a sight.</summary>
        private static void TurretHeadInto(MeshBatch batch, Material? only, bool worn)
        {
            var steel = only ?? MeshKit.Mat(worn ? SorterDark : SorterBody, 0.45f, 0.6f);
            var dark = only ?? MeshKit.Mat(SorterDark, 0.6f, 0.4f);
            var orange = only ?? MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            var barrel = only ?? MeshKit.Mat(new Color("26272b"), 0.35f, 0.8f);
            batch.Box(new Vector3(0f, 0.12f, -0.05f), new Vector3(0.78f, 0.42f, 0.9f), steel, 0.08f);
            batch.Box(new Vector3(0f, 0.36f, -0.12f), new Vector3(0.6f, 0.12f, 0.6f), steel, 0.05f, new Vector3(-8f, 0f, 0f));
            batch.Box(new Vector3(0f, 0.12f, 0.42f), new Vector3(0.62f, 0.3f, 0.1f), orange, 0.03f);
            var along = new Basis(Vector3.Right, Mathf.Pi * 0.5f);
            for (int k = -1; k <= 1; k += 2)
            {
                batch.Add(MeshKit.Cylinder(0.075f, 0.075f, 0.32f, 12), dark, new Transform3D(along, new Vector3(k * 0.17f, 0.1f, 0.6f)));
                batch.Add(MeshKit.Cylinder(0.045f, 0.045f, 0.62f, 10), barrel, new Transform3D(along, new Vector3(k * 0.17f, 0.1f, TurretMuzzle - 0.3f)));
            }
            batch.Add(MeshKit.Cylinder(0.2f, 0.2f, 0.22f, 16), dark, new Transform3D(new Basis(Vector3.Forward, Mathf.Pi * 0.5f), new Vector3(0.5f, 0.08f, -0.15f)));
            batch.Box(new Vector3(-0.22f, 0.48f, 0.1f), new Vector3(0.1f, 0.12f, 0.24f), dark, 0.02f);
        }

        /// <summary>One nod of a pump's beam, down and up.</summary>
        private const float PumpStrokeSeconds = 3.2f;

        /// <summary>How far out from its post the grab of the inserter on (tx, tz) is drawn; 0 for none (for the smoke).</summary>
        public float GrabReach(int tx, int tz)
        {
            foreach (var (built, arm, _) in _arms)
                if (built.Tx == tx && built.Tz == tz && arm.GetNodeOrNull<Node3D>("Grab") is { } grab) return -grab.Position.Z;
            return 0f;
        }

        /// <summary>The yaw that points an arm built along −Z the way <paramref name="dir"/> goes.</summary>
        private static float ArmYaw(int dir)
        {
            var v = Dir(dir);
            return Mathf.Atan2(-v.X, -v.Z);
        }

        /// <summary>How long an inserter's boom is: to the near edge of the tile it reaches (a Long Inserter's two on).</summary>
        public static float ArmLength(int reach) => 1.02f + (reach - 1) * SimConstants.TileSize;

        /// <summary>The height of an inserter's boom: a Long Inserter's swings over a carrier's head and the belt between.</summary>
        public static float ArmHeight(int reach) => reach > 1 ? 1.25f : 0.95f;

        /// <summary>
        /// An inserter's arm: a boom from the top of its post and a grab at its end, with a letter
        /// in it while carrying. A Long Inserter's boom is a tile longer, blue-banded, with a
        /// counterweight behind the post.
        /// </summary>
        private Node3D InserterArm(Construct c, Vector3 at, bool worn)
        {
            int reach = c.Def.Reach;
            float len = ArmLength(reach);
            var arm = new Node3D { Name = $"Arm{c.Tx}_{c.Tz}", Position = at + new Vector3(0f, ArmHeight(reach), 0f) };
            var steel = MeshKit.Mat(worn ? SorterDark : SorterBody, 0.45f, 0.6f);
            var orange = MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            arm.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.16f, 0.14f, len + 0.03f), 0.03f), orange, new Vector3(0f, 0f, -len * 0.5f + 0.01f), default, "Boom"));
            if (reach > 1)
            {
                var blue = MeshKit.Mat(BeltBlue, 0.55f, 0.2f);
                for (int band = 1; band <= 2; band++)
                    arm.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.19f, 0.17f, 0.14f), 0.02f), blue, new Vector3(0f, 0f, -len * band / 3f), default, $"Band{band}"));
                arm.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.34f, 0.26f, 0.3f), 0.04f), MeshKit.Mat(SorterDark, 0.6f, 0.4f), new Vector3(0f, -0.02f, 0.32f), default, "Counterweight"));
            }
            arm.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.3f, 0.32f, 0.12f), 0.03f), steel, new Vector3(0f, -0.12f, -len), default, "Grab"));
            var piece = MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.36f, 0.04f, 0.24f), 0.01f), MeshKit.Mat(new Color("f2ecd9"), 0.8f), new Vector3(0f, -0.3f, -len), default, "Piece");
            piece.Visible = false;
            arm.AddChild(piece);
            arm.Rotation = new Vector3(0f, ArmYaw(c.Facing) + Mathf.Pi, 0f);
            _built!.AddChild(arm);
            _arms.Add((c, arm, piece));
            return arm;
        }

        private static bool Near(float x, float z, Construct gate)
        {
            float dx = x - gate.X, dz = z - gate.Z;
            return dx * dx + dz * dz < GateOpenRange * GateOpenRange;
        }

        public void Refresh()
        {
            var world = _client?.World;
            if (world == null || _built == null) return;
            RefreshBuilt(world);
            RefreshGhost(world);
        }

        private void RefreshBuilt(ClientWorld world)
        {
            var field = world.Constructs;
            if (ReferenceEquals(field, _drawn) && field.Version == _version) return;
            _drawn = field;
            _version = field.Version;
            foreach (var child in _built!.GetChildren())
            {
                _built.RemoveChild(child);
                child.QueueFree();
            }
            var batch = new MeshBatch();
            var previous = new System.Collections.Generic.Dictionary<Construct, float>();
            foreach (var g in _gates) previous[g.Built] = g.Open;
            _gates.Clear();
            _lamps.Clear();
            _arms.Clear();
            _beams.Clear();
            var yaws = new System.Collections.Generic.Dictionary<Construct, float>();
            foreach (var g in _guns) yaws[g.Built] = g.Yaw;
            _guns.Clear();
            foreach (var c in field.All)
            {
                var at = new Vector3(c.X, world.Map.HeightAt(c.X, c.Z), c.Z);
                if (c.Def.Carries)
                {
                    bool belt = c.Def.Behaviour == BuildingBehaviour.Belt;
                    bool worn = c.Hp * 2 < c.Def.Hp;
                    if (c.Def.Ramp) RampTile(batch, at, c.Facing, BeltNetwork.RampUp(c), null, worn);
                    else if (c.Def.Level > 0) ElevatedTile(batch, at, BeltEntry(world, c), c.Facing, null, worn, c.Def.Speed);
                    else BeltTile(batch, at, belt ? BeltEntry(world, c) : c.Facing, c.Facing, null, worn, c.Def.Speed);
                    if (!belt) Housing(batch, at, c.Facing, c.Def.Behaviour == BuildingBehaviour.Splitter, null);
                    continue;
                }
                // A sorter stands over the middle of its four tiles.
                if (c.Def.Width > 1) at = new Vector3(c.CentreX, world.Map.HeightAt(c.CentreX, c.CentreZ), c.CentreZ);
                Draw(batch, c.Def, at, field.Arms(c), c.Rotation, null, c.Hp * 2 < c.Def.Hp);
                if (c.Def.Behaviour == BuildingBehaviour.Gate)
                {
                    var g = new GateLeaves { Built = c, Open = previous.TryGetValue(c, out float was) ? was : 0f };
                    var frame = new Node3D { Name = $"Gate{c.Tx}_{c.Tz}", Position = at, Rotation = new Vector3(0f, c.AlongZ ? Mathf.Pi * 0.5f : 0f, 0f) };
                    var plank = Mat(c.Hp * 2 < c.Def.Hp ? LogDark : PlankColour);
                    var iron = MeshKit.Mat(IronColour, 0.5f, 0.3f);
                    g.Left = Leaf(frame, -1, plank, iron);
                    g.Right = Leaf(frame, 1, plank, iron);
                    _built.AddChild(frame);
                    _gates.Add(g);
                }
                else if (c.Def.Behaviour == BuildingBehaviour.Inserter) InserterArm(c, at, c.Hp * 2 < c.Def.Hp);
                else if (c.Def.IsPump)
                {
                    // The beam nods about its pivot on a node turned the way the pump faces.
                    var holder = new Node3D { Name = $"Pump{c.Tx}_{c.Tz}", Position = at, Rotation = new Vector3(0f, PumpYaw(c.Rotation & 3), 0f) };
                    var beam = new Node3D { Name = "Beam", Position = new Vector3(0f, PumpPivotHeight, 0f) };
                    holder.AddChild(beam);
                    PumpBeam(beam, null, c.Hp * 2 < c.Def.Hp);
                    _built.AddChild(holder);
                    _beams.Add((c, beam));
                }
                else if (c.Def.IsTurret) TurretHead(c, at, yaws.TryGetValue(c, out float yaw) ? yaw : PumpYaw(c.Rotation & 3), c.Hp * 2 < c.Def.Hp);
                else if (c.Def.Behaviour == BuildingBehaviour.Alarm)
                {
                    var lamp = MeshKit.Instance(MeshKit.Sphere(0.11f, 12), MeshKit.Glow(new Color("ff2a1a"), 4f), at + new Vector3(0f, AlarmLampHeight, 0f), default, $"AlarmLamp{c.Tx}_{c.Tz}");
                    lamp.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                    lamp.Visible = false;
                    _built.AddChild(lamp);
                    _lamps.Add((c, lamp));
                }
            }
            batch.Flush(_built, "Built");
            Drawn = field.All.Count;
            Redraws++;
        }

        private void RefreshGhost(ClientWorld world)
        {
            var ghost = _build != null && _build.Active ? _build.Ghost : default;
            // Under a panel the view is not where the hands are.
            bool show = ghost.Aim == BuildAim.Place && ghost.Recipe != null && world.OpenContainerId == 0 && !world.ShopOpen;
            _ghost!.Visible = show;
            if (!show) return;
            var def = ghost.Recipe!.Building;
            bool ok = ghost.Line != null ? ghost.LineOk > 0 : ghost.State == OpResult.Ok;
            GhostGreen = ok;
            GhostTiles = ghost.Line?.Count ?? 1;
            string line = ghost.Line == null ? "" : string.Join(";", ghost.Line);
            var shape = (def, ghost.Rotation, ok, line);
            if (shape != _ghostShape)
            {
                _ghostShape = shape;
                foreach (var child in _ghost.GetChildren())
                {
                    _ghost.RemoveChild(child);
                    child.QueueFree();
                }
                var batch = new MeshBatch();
                var good = GhostMat(GhostOk);
                var bad = GhostMat(GhostBad);
                var tiles = ghost.Line ?? new System.Collections.Generic.List<(int, int, OpResult)> { (ghost.Tx, ghost.Tz, ghost.State) };
                int arms = (ghost.Rotation & 1) != 0 ? ConstructField.ArmSouth | ConstructField.ArmNorth : ConstructField.ArmEast | ConstructField.ArmWest;
                foreach (var (tx, tz, state) in tiles)
                {
                    var mat = state == OpResult.Ok ? good : bad;
                    var offset = new Vector3((tx - ghost.Tx) * SimConstants.TileSize, 0f, (tz - ghost.Tz) * SimConstants.TileSize);
                    if (def.Ramp) RampTile(batch, offset, ghost.Rotation & 3, ghost.Rotation < 4, mat, false);
                    else if (def.Level > 0) ElevatedTile(batch, offset, ghost.Rotation & 3, ghost.Rotation & 3, mat, false, def.Speed);
                    else if (def.Carries)
                    {
                        BeltTile(batch, offset, ghost.Rotation & 3, ghost.Rotation & 3, mat, false, def.Speed);
                        if (def.Behaviour != BuildingBehaviour.Belt) Housing(batch, offset, ghost.Rotation & 3, def.Behaviour == BuildingBehaviour.Splitter, mat);
                    }
                    else Draw(batch, def, offset + FootprintCentre(def, ghost.Rotation), arms, ghost.Rotation, mat, false);
                    // The tiles it takes, on the ground (a ramp's two, a sorter's four).
                    for (int i = 0; i < def.TileCount; i++)
                    {
                        var (fx, fz) = Construct.TileOf(0, 0, ghost.Rotation, i % def.Length, i / def.Length);
                        batch.Box(offset + new Vector3(fx * SimConstants.TileSize, 0.02f, fz * SimConstants.TileSize), new Vector3(SimConstants.TileSize - 0.1f, 0.04f, SimConstants.TileSize - 0.1f), mat, 0.02f);
                    }
                }
                batch.Flush(_ghost, "Ghost", castShadows: false);
            }
            float x = (ghost.Tx + 0.5f) * SimConstants.TileSize, z = (ghost.Tz + 0.5f) * SimConstants.TileSize;
            _ghost.Position = new Vector3(x, world.Map.HeightAt(x, z), z);
        }

        /// <summary>Tiles the ghost stands on: more than one while a drag line is held.</summary>
        public int GhostTiles { get; private set; }

        /// <summary>The way mail comes onto a belt's tile: from the belt before it in its segment, else straight from behind.</summary>
        private static int BeltEntry(ClientWorld world, Construct c)
        {
            var belts = world.Belts;
            var seg = belts.SegmentAt(c.Tx, c.Tz);
            int k = belts.IndexInSegment(c.Tx, c.Tz);
            if (seg == null || k <= 0) return c.Rotation & 3;
            int prev = seg.Tiles[k - 1];
            return world.Constructs.At(prev % world.Map.Cols, prev / world.Map.Cols) is { } p ? p.Rotation & 3 : c.Rotation & 3;
        }

        private static readonly Color BeltRubber = new Color("2b2b2e"), BeltSteel = new Color("8d939b"), BeltYellow = new Color("e0a526"), BeltArrow = new Color("c8c2b0");
        /// <summary>A Fast Belt's rail caps (doc 04 §2.1 Mk2): the tier is seen from across the street.</summary>
        private static readonly Color BeltBlue = new Color("2f7fd8");
        /// <summary>The speed of the slowest belt (Mk1); a faster one has blue caps and a second pair of chevrons.</summary>
        public const float BeltBaseSpeed = 2f;
        /// <summary>Top of a belt's running surface above the ground: where the mail rides.</summary>
        public const float BeltTop = Sim.Automation.BeltNetwork.SurfaceHeight;

        /// <summary>
        /// A Conveyor Belt tile in the look of the reference factory: a steel frame on four legs with
        /// yellow-capped side rails, a dark rubber belt between them and pale chevrons the way it
        /// runs. A corner is an L: half from the edge it comes in by to the middle, half on to the
        /// edge it faces.
        /// </summary>
        public static void BeltTile(MeshBatch batch, Vector3 at, int inDir, int dir, Material? only, bool worn, float speed = BeltBaseSpeed)
        {
            bool fast = speed > BeltBaseSpeed + 0.01f;
            var rubber = only ?? Mat(worn ? BeltRubber.Lightened(0.15f) : BeltRubber);
            var steel = only ?? MeshKit.Mat(BeltSteel, 0.45f, 0.6f);
            var yellow = only ?? MeshKit.Mat(fast ? BeltBlue : BeltYellow, 0.6f);
            var arrow = only ?? Mat(BeltArrow);
            float half = SimConstants.TileSize * 0.5f;
            const float width = 1.4f;
            for (int part = 0; part < 2; part++)
            {
                int d = part == 0 ? inDir : dir;
                var v = Dir(d);
                // Entry half: from the edge behind to the middle; exit half: from the middle on.
                var mid = part == 0 ? at - v * (half * 0.5f) : at + v * (half * 0.5f);
                float len = half + (inDir != dir ? width * 0.5f : 0f);
                var lenAxis = new Vector3(Mathf.Abs(v.X), 0f, Mathf.Abs(v.Z));
                var across = new Vector3(Mathf.Abs(v.Z), 0f, Mathf.Abs(v.X));
                batch.Box(mid + new Vector3(0f, BeltTop - 0.04f, 0f), lenAxis * len + across * width + new Vector3(0f, 0.07f, 0f), rubber, 0.02f);
                batch.Box(mid + new Vector3(0f, BeltTop - 0.12f, 0f), lenAxis * len + across * (width + 0.04f) + new Vector3(0f, 0.08f, 0f), steel, 0.01f);
                for (int side = -1; side <= 1; side += 2)
                {
                    var rail = mid + across * side * (width * 0.5f + 0.05f) + new Vector3(0f, BeltTop + 0.02f, 0f);
                    batch.Box(rail, lenAxis * len + across * 0.08f + new Vector3(0f, 0.12f, 0f), steel, 0.02f);
                    batch.Box(rail + new Vector3(0f, 0.08f, 0f), lenAxis * len + across * 0.09f + new Vector3(0f, 0.03f, 0f), yellow, 0.01f);
                }
                // A chevron on each half, pointing the way it runs; a fast belt has two.
                float yaw = Mathf.Atan2(v.X, v.Z);
                for (int n = 0; n < (fast ? 2 : 1); n++)
                    for (int arm = -1; arm <= 1; arm += 2)
                    {
                        var basis = new Basis(Vector3.Up, yaw + arm * 0.75f);
                        var c = mid + new Vector3(0f, BeltTop + 0.002f, 0f) - v * (fast ? 0.3f - n * 0.4f : 0.1f) + basis * new Vector3(0f, 0f, -0.18f);
                        batch.Add(MeshKit.Box(new Vector3(0.07f, 0.01f, 0.4f)), arrow, new Transform3D(basis, c));
                    }
            }
            // Legs at the corners of the tile.
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                    batch.Box(at + new Vector3(i * (width * 0.5f + 0.02f), (BeltTop - 0.16f) * 0.5f, j * 0.6f), new Vector3(0.08f, BeltTop - 0.16f, 0.08f), steel, 0.01f);
        }

        /// <summary>
        /// An Elevated Belt (doc 04 §2.1): the belt tile a level up, on two steel posts and a cross
        /// beam either side of the way it runs, clear of whoever walks the street under it.
        /// </summary>
        public static void ElevatedTile(MeshBatch batch, Vector3 at, int inDir, int dir, Material? only, bool worn, float speed = BeltBaseSpeed)
        {
            var steel = only ?? MeshKit.Mat(BeltSteel, 0.45f, 0.6f);
            const float lift = BeltNetwork.LevelHeight;
            BeltTile(batch, at + new Vector3(0f, lift, 0f), inDir, dir, only, worn, speed);
            var across = Dir(dir).Z != 0f ? Vector3.Right : Vector3.Back;
            float top = lift + BeltTop - 0.16f;
            for (int side = -1; side <= 1; side += 2)
                batch.Box(at + across * side * 0.82f + new Vector3(0f, top * 0.5f, 0f), new Vector3(0.14f, top, 0.14f), steel, 0.02f);
            batch.Box(at + new Vector3(0f, top - 0.06f, 0f), across * 1.78f + new Vector3(0.12f, 0.12f, 0.12f) - across * 0.12f, steel, 0.02f);
        }

        /// <summary>
        /// A Belt Ramp (doc 04 §2.1, two tiles long): the belt as a slope from the back edge of the
        /// tile it was built on to the front edge of the next, a level up or down the way it faces,
        /// on posts under its high half. <paramref name="at"/> is the first tile's middle.
        /// </summary>
        public static void RampTile(MeshBatch batch, Vector3 at, int dir, bool up, Material? only, bool worn)
        {
            var rubber = only ?? Mat(worn ? BeltRubber.Lightened(0.15f) : BeltRubber);
            var steel = only ?? MeshKit.Mat(BeltSteel, 0.45f, 0.6f);
            var yellow = only ?? MeshKit.Mat(BeltYellow, 0.6f);
            var arrow = only ?? Mat(BeltArrow);
            const float run = 2f * SimConstants.TileSize, rise = BeltNetwork.LevelHeight, width = 1.4f;
            var v = Dir(dir);
            var back = at - v * (SimConstants.TileSize * 0.5f);
            float h0 = up ? 0f : rise, h1 = up ? rise : 0f;
            float slope = Mathf.Atan2(h1 - h0, run);
            float len = Mathf.Sqrt(run * run + rise * rise) + 0.05f;
            // Local Z along the slope, local Y its normal.
            var basis = new Basis(Vector3.Up, Mathf.Atan2(v.X, v.Z)) * new Basis(Vector3.Right, -slope);
            var mid = back + v * (run * 0.5f) + new Vector3(0f, BeltTop + (h0 + h1) * 0.5f, 0f);
            void Part(Vector3 local, Vector3 size, Material mat) => batch.Add(MeshKit.Box(size), mat, new Transform3D(basis, mid + basis * local));
            Part(new Vector3(0f, -0.04f, 0f), new Vector3(width, 0.07f, len), rubber);
            Part(new Vector3(0f, -0.12f, 0f), new Vector3(width + 0.04f, 0.08f, len), steel);
            for (int side = -1; side <= 1; side += 2)
            {
                Part(new Vector3(side * (width * 0.5f + 0.05f), 0.02f, 0f), new Vector3(0.08f, 0.12f, len), steel);
                Part(new Vector3(side * (width * 0.5f + 0.05f), 0.1f, 0f), new Vector3(0.09f, 0.03f, len), yellow);
            }
            // Chevrons up the slope (or down it), the way the mail goes.
            for (int n = -1; n <= 1; n++)
                for (int arm = -1; arm <= 1; arm += 2)
                {
                    var chevron = basis * new Basis(Vector3.Up, arm * 0.75f);
                    var c = mid + basis * new Vector3(0f, 0.002f, n * 1.3f) + chevron * new Vector3(0f, 0f, -0.18f);
                    batch.Add(MeshKit.Box(new Vector3(0.07f, 0.01f, 0.4f)), arrow, new Transform3D(chevron, c));
                }
            // Posts where it is off the ground: a pair at the middle and at the high end.
            var across = new Vector3(Mathf.Abs(v.Z), 0f, Mathf.Abs(v.X));
            foreach (float t in new[] { 0.5f, 0.94f })
            {
                float along = up ? t : 1f - t;
                float h = BeltTop - 0.16f + h0 + (h1 - h0) * along;
                var foot = back + v * (run * along);
                for (int side = -1; side <= 1; side += 2)
                    batch.Box(foot + across * side * 0.78f + new Vector3(0f, h * 0.5f, 0f), new Vector3(0.12f, h, 0.12f), steel, 0.02f);
            }
        }

        /// <summary>
        /// A splitter's or merger's housing over its belt (doc 04 §2.3): two steel side plates under
        /// a roof, and yellow arrows on the roof — out to the front, left and right for a splitter,
        /// in from the back, left and right for a merger.
        /// </summary>
        public static void Housing(MeshBatch batch, Vector3 at, int dir, bool splitter, Material? only)
        {
            var steel = only ?? MeshKit.Mat(new Color("6f757d"), 0.4f, 0.7f);
            var yellow = only ?? MeshKit.Mat(BeltYellow, 0.6f);
            var v = Dir(dir);
            var across = new Vector3(Mathf.Abs(v.Z), 0f, Mathf.Abs(v.X));
            var along = new Vector3(Mathf.Abs(v.X), 0f, Mathf.Abs(v.Z));
            const float roof = 0.95f;
            for (int side = -1; side <= 1; side += 2)
                batch.Box(at + across * side * 0.78f + new Vector3(0f, roof * 0.5f, 0f), along * 1.1f + across * 0.1f + new Vector3(0f, roof, 0f), steel, 0.03f);
            batch.Box(at + new Vector3(0f, roof, 0f), along * 1.2f + across * 1.7f + new Vector3(0f, 0.1f, 0f), steel, 0.04f);
            // The arrows: for each way, a shaft and a head on the roof.
            for (int k = 0; k < 3; k++)
            {
                int d = k == 0 ? dir : k == 1 ? (dir + 3) & 3 : (dir + 1) & 3;
                if (!splitter && k == 0) d = (dir + 2) & 3;
                var w = Dir(d);
                // A splitter's arrows point out along their way; a merger's point in, towards the middle.
                var tip = splitter ? w * 0.55f : w * 0.15f;
                var tail = splitter ? w * 0.15f : w * 0.55f;
                var mid = (tip + tail) * 0.5f;
                float yaw = Mathf.Atan2((tip - tail).X, (tip - tail).Z);
                batch.Add(MeshKit.Box(new Vector3(0.09f, 0.02f, 0.4f)), yellow, new Transform3D(new Basis(Vector3.Up, yaw), at + mid + new Vector3(0f, roof + 0.06f, 0f)));
                batch.Add(MeshKit.Cone(0.12f, 0.2f, 3), yellow, new Transform3D(new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, Mathf.Pi * 0.5f), at + tip + new Vector3(0f, roof + 0.06f, 0f)));
            }
        }

        private static Vector3 Dir(int d) => d switch { 0 => Vector3.Right, 1 => Vector3.Back, 2 => Vector3.Left, _ => Vector3.Forward };

        /// <summary>From the middle of the tile a building is built on to the middle of all its tiles.</summary>
        public static Vector3 FootprintCentre(BuildingDef def, int rotation)
        {
            if (def.Width <= 1) return Vector3.Zero;
            var (fx, fz) = Construct.TileOf(0, 0, rotation, def.Length - 1, def.Width - 1);
            return new Vector3(fx, 0f, fz) * (SimConstants.TileSize * 0.5f);
        }

        private static readonly Color SorterBody = new Color("9aa3ad"), SorterTrim = new Color("e07a1f"), SorterDark = new Color("2a2f36");
        private static readonly Color PortIn = new Color("3fae5a"), PortOut = new Color("e0a526"), PortOverflow = new Color("c8402e");

        /// <summary>
        /// An Address Sorter (doc 04 §2.4) in the look of the reference factory: a steel machine on
        /// a base plate over its four tiles, an orange band round it, a hood with a lit console
        /// screen, and a framed mouth on each way: green for the input, amber for the three outputs,
        /// red for the overflow. <paramref name="at"/> is the middle of its tiles.
        /// </summary>
        public static void Sorter(MeshBatch batch, Vector3 at, int facing, Material? only, bool worn)
        {
            const float t = SimConstants.TileSize;
            var body = only ?? MeshKit.Mat(worn ? SorterDark : SorterBody, 0.45f, 0.6f);
            var trim = only ?? MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            var dark = only ?? MeshKit.Mat(SorterDark, 0.6f, 0.4f);
            float yaw = Mathf.RadToDeg(-facing * Mathf.Pi * 0.5f);
            batch.Box(at + new Vector3(0f, 0.08f, 0f), new Vector3(2f * t - 0.2f, 0.16f, 2f * t - 0.2f), dark, 0.03f);
            batch.Box(at + new Vector3(0f, 0.8f, 0f), new Vector3(2f * t - 0.9f, 1.3f, 2f * t - 0.9f), body, 0.08f);
            batch.Box(at + new Vector3(0f, 1.05f, 0f), new Vector3(2f * t - 0.84f, 0.16f, 2f * t - 0.84f), trim, 0.02f);
            batch.Box(at + new Vector3(0f, 1.55f, 0f), new Vector3(2f * t - 1.6f, 0.2f, 2f * t - 1.6f), body, 0.05f, new Vector3(0f, yaw, 0f));
            // The console: a slanted screen on the hood, towards the input side.
            var f = Dir(facing);
            var screen = only ?? MeshKit.Glow(new Color("4fd3e8"), 1.2f);
            batch.Box(at - f * 0.55f + new Vector3(0f, 1.72f, 0f), new Vector3(0.9f, 0.12f, 0.9f), dark, 0.02f, new Vector3(0f, yaw, 0f));
            batch.Box(at - f * 0.55f + new Vector3(0f, 1.79f, 0f), new Vector3(0.7f, 0.03f, 0.7f), screen, 0.0f, new Vector3(0f, yaw, 0f));
            // The mouths: the input into (0, 0) from behind, then the ports.
            Mouth(batch, at, facing, 0, 0, (facing + 2) & 3, only ?? MeshKit.Mat(PortIn, 0.5f), dark);
            for (int port = 0; port < Sim.Automation.SorterNode.Ports; port++)
            {
                var (along, across, dir) = Sim.Automation.SorterNode.Port(port, facing);
                var frame = only ?? MeshKit.Mat(port == Sim.Automation.SorterNode.Overflow ? PortOverflow : PortOut, 0.5f);
                Mouth(batch, at, facing, along, across, dir, frame, dark);
            }
        }

        /// <summary>
        /// An Inserter's base (doc 04 §2.5): a steel plate, a post with an orange collar, and a
        /// yellow arrow on the plate the way it moves mail. The arm is a node of its own
        /// (<see cref="InserterArm"/>); the ghost draws it pointing ahead.
        /// </summary>
        public static void InserterBase(MeshBatch batch, Vector3 at, int facing, Material? only, bool worn, int reach = 1)
        {
            var steel = only ?? MeshKit.Mat(worn ? SorterDark : SorterBody, 0.45f, 0.6f);
            var dark = only ?? MeshKit.Mat(SorterDark, 0.6f, 0.4f);
            var orange = only ?? MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            var yellow = only ?? MeshKit.Mat(BeltYellow, 0.6f);
            batch.Box(at + new Vector3(0f, 0.06f, 0f), new Vector3(1.3f, 0.12f, 1.3f), dark, 0.03f);
            float top = ArmHeight(reach) - 0.09f;
            batch.Box(at + new Vector3(0f, top * 0.5f + 0.05f, 0f), new Vector3(0.36f, top - 0.1f, 0.36f), steel, 0.06f);
            batch.Box(at + new Vector3(0f, top, 0f), new Vector3(0.46f, 0.12f, 0.46f), orange, 0.04f);
            if (reach > 1) batch.Box(at + new Vector3(0f, top - 0.3f, 0f), new Vector3(0.42f, 0.1f, 0.42f), only ?? MeshKit.Mat(BeltBlue, 0.55f, 0.2f), 0.03f);
            var v = Dir(facing);
            float yaw = Mathf.Atan2(v.X, v.Z);
            batch.Add(MeshKit.Box(new Vector3(0.1f, 0.02f, 0.4f)), yellow, new Transform3D(new Basis(Vector3.Up, yaw), at + v * 0.35f + new Vector3(0f, 0.13f, 0f)));
            batch.Add(MeshKit.Cone(0.13f, 0.22f, 3), yellow, new Transform3D(new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, Mathf.Pi * 0.5f), at + v * 0.58f + new Vector3(0f, 0.13f, 0f)));
            // The ghost has no arm node: its arm is drawn into the batch, pointing ahead.
            float len = ArmLength(reach) + 0.03f;
            if (only != null) batch.Box(at + v * (len * 0.5f) + new Vector3(0f, ArmHeight(reach), 0f), new Vector3(Mathf.Abs(v.Z) * 0.16f + Mathf.Abs(v.X) * len, 0.14f, Mathf.Abs(v.X) * 0.16f + Mathf.Abs(v.Z) * len), only, 0.03f);
        }

        /// <summary>The height of the middle of a pneumatic tube above the ground (for the capsules too).</summary>
        public const float PipeAxis = 0.5f;
        /// <summary>A tube's outer radius.</summary>
        public const float PipeRadius = 0.2f;

        private static Material? _pipeGlass;

        /// <summary>The glass of the tubes: clear enough that the capsules show through.</summary>
        private static Material PipeGlass => _pipeGlass ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.88f, 0.96f, 0.32f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.08f,
            Metallic = 0.1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        /// <summary>A length of glass tube from the middle of a tile <paramref name="from"/> metres out to <paramref name="to"/>, the way <paramref name="dir"/> goes.</summary>
        private static void Tube(MeshBatch batch, Vector3 at, int dir, float from, float to, Material glass)
        {
            var v = Dir(dir);
            float len = to - from;
            var basis = new Basis(Vector3.Up, Mathf.Atan2(v.X, v.Z)) * new Basis(Vector3.Right, Mathf.Pi * 0.5f);
            batch.Add(MeshKit.Cylinder(PipeRadius, PipeRadius, len, 16), glass, new Transform3D(basis, at + v * (from + len * 0.5f) + new Vector3(0f, PipeAxis, 0f)));
        }

        /// <summary>A steel ring round a tube <paramref name="along"/> metres out the way <paramref name="dir"/> goes.</summary>
        private static void Collar(MeshBatch batch, Vector3 at, int dir, float along, Material steel)
        {
            var v = Dir(dir);
            var basis = new Basis(Vector3.Up, Mathf.Atan2(v.X, v.Z)) * new Basis(Vector3.Right, Mathf.Pi * 0.5f);
            batch.Add(MeshKit.Cylinder(PipeRadius + 0.04f, PipeRadius + 0.04f, 0.12f, 16), steel, new Transform3D(basis, at + v * along + new Vector3(0f, PipeAxis, 0f)));
        }

        /// <summary>
        /// A piece of a pneumatic pipe network (doc 04 §2.2), after the reference's tube lines: a
        /// glass tube on a steel stand with orange collars at the tile's edges (the capsules show
        /// through it); an underground pipe a flush steel hatch with yellow chevrons; a junction a
        /// steel hub with a stub out of each side; an inlet a hopper with a green mouth behind and an
        /// outlet a box with an amber mouth in front, each with its stub on the pipe's side.
        /// </summary>
        public static void PipePiece(MeshBatch batch, PipeRole role, Vector3 at, int facing, Material? only, bool worn)
        {
            var glass = only ?? PipeGlass;
            var steel = only ?? MeshKit.Mat(worn ? SorterDark : SorterBody, 0.45f, 0.6f);
            var dark = only ?? MeshKit.Mat(SorterDark, 0.6f, 0.4f);
            var orange = only ?? MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            var yellow = only ?? MeshKit.Mat(BeltYellow, 0.6f);
            const float half = SimConstants.TileSize * 0.5f;
            var f = Dir(facing);
            switch (role)
            {
                case PipeRole.Pipe:
                {
                    Tube(batch, at, facing, -half, half, glass);
                    Collar(batch, at, facing, half - 0.06f, orange);
                    Collar(batch, at, facing, -half + 0.06f, orange);
                    // A stand: a foot and a saddle under the middle.
                    var across = Dir((facing + 1) & 3);
                    batch.Box(at + new Vector3(0f, 0.04f, 0f), new Vector3(0.5f, 0.08f, 0.5f), dark, 0.02f);
                    batch.Box(at + new Vector3(0f, (PipeAxis - PipeRadius) * 0.5f, 0f), new Vector3(0.1f, PipeAxis - PipeRadius, 0.1f), steel, 0.02f);
                    batch.Box(at + new Vector3(0f, PipeAxis - PipeRadius - 0.03f, 0f), new Vector3(Mathf.Abs(across.X) * 0.5f + Mathf.Abs(f.X) * 0.14f, 0.06f, Mathf.Abs(across.Z) * 0.5f + Mathf.Abs(f.Z) * 0.14f), steel, 0.02f);
                    return;
                }
                case PipeRole.Underground:
                {
                    batch.Box(at + new Vector3(0f, 0.03f, 0f), new Vector3(1.5f, 0.06f, 1.5f), dark, 0.02f);
                    batch.Add(MeshKit.Cylinder(0.5f, 0.5f, 0.04f, 20), steel, at + new Vector3(0f, 0.07f, 0f));
                    float yaw = Mathf.Atan2(f.X, f.Z);
                    for (int k = -1; k <= 1; k += 2)
                        batch.Add(MeshKit.Box(new Vector3(0.12f, 0.02f, 0.3f)), yellow, new Transform3D(new Basis(Vector3.Up, yaw), at + f * (k * 0.62f) + new Vector3(0f, 0.07f, 0f)));
                    return;
                }
                case PipeRole.Junction:
                {
                    batch.Box(at + new Vector3(0f, PipeAxis * 0.5f, 0f), new Vector3(0.36f, PipeAxis, 0.36f), dark, 0.03f);
                    batch.Box(at + new Vector3(0f, PipeAxis, 0f), new Vector3(0.62f, 0.62f, 0.62f), steel, 0.1f);
                    for (int d = 0; d < 4; d++)
                    {
                        Tube(batch, at, d, 0.3f, half, glass);
                        Collar(batch, at, d, half - 0.06f, orange);
                    }
                    return;
                }
                case PipeRole.Inlet:
                case PipeRole.Outlet:
                {
                    bool inlet = role == PipeRole.Inlet;
                    // The box sits back from the side its tube leaves by.
                    int tubeSide = inlet ? facing : (facing + 2) & 3;
                    var t = Dir(tubeSide);
                    var box = at - t * 0.25f;
                    batch.Box(box + new Vector3(0f, 0.5f, 0f), new Vector3(1.05f, 1.0f, 1.05f), steel, 0.08f);
                    batch.Box(box + new Vector3(0f, 1.03f, 0f), new Vector3(1.12f, 0.08f, 1.12f), orange, 0.03f);
                    // The mouth: an inlet's behind it, an outlet's in front.
                    var m = -t;
                    var side = new Vector3(Mathf.Abs(m.Z), 0f, Mathf.Abs(m.X));
                    var mouth = only ?? MeshKit.Mat(inlet ? PortIn : PortOut, 0.5f, 0.2f);
                    batch.Box(box + m * 0.53f + new Vector3(0f, 0.5f, 0f), side * 0.8f + new Vector3(0f, 0.6f, 0f) + new Vector3(Mathf.Abs(m.X), 0f, Mathf.Abs(m.Z)) * 0.06f, mouth, 0.03f);
                    batch.Box(box + m * 0.56f + new Vector3(0f, 0.47f, 0f), side * 0.6f + new Vector3(0f, 0.4f, 0f) + new Vector3(Mathf.Abs(m.X), 0f, Mathf.Abs(m.Z)) * 0.04f, dark, 0.02f);
                    Tube(batch, at, tubeSide, 0.3f, half, glass);
                    Collar(batch, at, tubeSide, half - 0.06f, orange);
                    // A yellow arrow on the roof, the way mail goes.
                    float yaw = Mathf.Atan2(f.X, f.Z);
                    batch.Add(MeshKit.Box(new Vector3(0.1f, 0.02f, 0.36f)), yellow, new Transform3D(new Basis(Vector3.Up, yaw), box + new Vector3(0f, 1.08f, 0f) - f * 0.08f));
                    batch.Add(MeshKit.Cone(0.13f, 0.2f, 3), yellow, new Transform3D(new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, Mathf.Pi * 0.5f), box + new Vector3(0f, 1.08f, 0f) + f * 0.18f));
                    return;
                }
            }
        }

        /// <summary>
        /// A Depot (doc 04 §2.6): a steel store over its four tiles in the look of the reference's
        /// storage containers — ribbed grey walls between orange corner posts, a flat roof with a
        /// lip, a roll-up door on the side it faces, and an amber band where belts come in.
        /// <paramref name="at"/> is the middle of its tiles.
        /// </summary>
        public static void Depot(MeshBatch batch, Vector3 at, int facing, Material? only, bool worn)
        {
            const float t = SimConstants.TileSize;
            float w = 2f * t - 0.5f, h = 1.9f;
            var body = only ?? MeshKit.Mat(worn ? SorterDark : SorterBody, 0.5f, 0.55f);
            var rib = only ?? MeshKit.Mat(new Color("7d858f"), 0.45f, 0.6f);
            var orange = only ?? MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            var dark = only ?? MeshKit.Mat(SorterDark, 0.6f, 0.4f);
            var yellow = only ?? MeshKit.Mat(BeltYellow, 0.6f);
            batch.Box(at + new Vector3(0f, 0.08f, 0f), new Vector3(2f * t - 0.15f, 0.16f, 2f * t - 0.15f), dark, 0.03f);
            batch.Box(at + new Vector3(0f, 0.16f + h * 0.5f, 0f), new Vector3(w, h, w), body, 0.06f);
            // Ribs along every side, a hand proud of the walls.
            for (int side = 0; side < 4; side++)
            {
                var n = Dir(side);
                var along = new Vector3(Mathf.Abs(n.Z), 0f, Mathf.Abs(n.X));
                for (int k = -2; k <= 2; k++)
                    batch.Box(at + n * (w * 0.5f + 0.03f) + along * (k * 0.62f) + new Vector3(0f, 0.16f + h * 0.5f, 0f), along * 0.1f + new Vector3(0f, h - 0.1f, 0f) + new Vector3(Mathf.Abs(n.X), 0f, Mathf.Abs(n.Z)) * 0.08f, rib, 0.02f);
            }
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                    batch.Box(at + new Vector3(cx * w * 0.5f, 0.16f + h * 0.5f + 0.05f, cz * w * 0.5f), new Vector3(0.24f, h + 0.1f, 0.24f), orange, 0.04f);
            batch.Box(at + new Vector3(0f, 0.16f + h + 0.08f, 0f), new Vector3(w + 0.3f, 0.16f, w + 0.3f), rib, 0.04f);
            // The belts' band, all round at the height mail comes in.
            batch.Box(at + new Vector3(0f, 0.42f, 0f), new Vector3(w + 0.06f, 0.12f, w + 0.06f), yellow, 0.02f);
            // The roll-up door on the side it faces.
            var f = Dir(facing);
            var across = new Vector3(Mathf.Abs(f.Z), 0f, Mathf.Abs(f.X));
            var depth = new Vector3(Mathf.Abs(f.X), 0f, Mathf.Abs(f.Z));
            batch.Box(at + f * (w * 0.5f + 0.04f) + new Vector3(0f, 0.16f + 0.75f, 0f), across * 1.7f + depth * 0.06f + new Vector3(0f, 1.4f, 0f), dark, 0.02f);
            for (int k = 0; k < 6; k++)
                batch.Box(at + f * (w * 0.5f + 0.08f) + new Vector3(0f, 0.3f + k * 0.22f, 0f), across * 1.6f + depth * 0.04f + new Vector3(0f, 0.05f, 0f), rib, 0.01f);
        }

        /// <summary>
        /// A Vehicle Depot (doc 04 §3.2) in the look of the reference's truck stations: a concrete
        /// pad over its nine tiles with the bay painted on it in yellow, a steel canopy on four
        /// orange posts with a ribbed roof high over a truck, the loading face at the back in
        /// yellow and black hatching with an amber arrow in, and the Route Console on the left of
        /// the bay: a grey pedestal with a lit screen turned to the bay. <paramref name="at"/> is
        /// the middle of its tiles.
        /// </summary>
        /// <summary>
        /// Doc 04 §3.3 Pier: a deck of weathered planks three tiles out over the water, level with
        /// the quay, on posts down into the bed, with a bollard at either corner of its end.
        /// </summary>
        public static void Pier(MeshBatch batch, Vector3 at, int facing, Material? only, bool worn)
        {
            const float t = SimConstants.TileSize;
            var plank = only ?? MeshKit.Mat(worn ? new Color("6b5440") : new Color("9a7a58"), 0.9f);
            var joist = only ?? MeshKit.Mat(new Color("5a4634"), 0.9f);
            var post = only ?? MeshKit.Mat(new Color("4a3a2c"), 0.95f);
            var iron = only ?? MeshKit.Mat(IronColour, 0.45f, 0.6f);
            var f = Dir(facing);
            var r = Dir((facing + 1) & 3);
            var along = new Vector3(Mathf.Abs(f.X), 0f, Mathf.Abs(f.Z));
            var across = new Vector3(Mathf.Abs(r.X), 0f, Mathf.Abs(r.Z));
            float length = 3f * t, width = t - 0.1f;
            // The planks across the way it runs, a finger's gap between them.
            int planks = 20;
            for (int i = 0; i < planks; i++)
            {
                float s = -length * 0.5f + (i + 0.5f) * length / planks;
                batch.Box(at + f * s + new Vector3(0f, -0.04f, 0f), across * width + along * (length / planks - 0.04f) + new Vector3(0f, 0.08f, 0f), plank, 0.01f);
            }
            for (int k = -1; k <= 1; k += 2)
                batch.Box(at + r * (k * (width * 0.5f - 0.12f)) + new Vector3(0f, -0.18f, 0f), along * length + across * 0.16f + new Vector3(0f, 0.2f, 0f), joist, 0.02f);
            // Posts in pairs a tile apart, down into the bed.
            for (int i = 0; i <= 3; i++)
                for (int k = -1; k <= 1; k += 2)
                    batch.Add(MeshKit.Cylinder(0.12f, 0.13f, 2.2f, 10), post, at + f * (-length * 0.5f + i * t * (i == 3 ? 0.97f : 1f)) + r * (k * (width * 0.5f - 0.05f)) + new Vector3(0f, -1.1f, 0f));
            // Bollards at the end, for the mooring lines.
            for (int k = -1; k <= 1; k += 2)
            {
                var b = at + f * (length * 0.5f - 0.3f) + r * (k * (width * 0.5f - 0.25f));
                batch.Add(MeshKit.Cylinder(0.11f, 0.13f, 0.4f, 12), iron, b + new Vector3(0f, 0.2f, 0f));
                batch.Add(MeshKit.Cylinder(0.16f, 0.16f, 0.05f, 12), iron, b + new Vector3(0f, 0.42f, 0f));
            }
        }

        /// <summary>
        /// Doc 04 §3.3 Small Port (design choice 112): a concrete quay on land with an orange-railed
        /// edge and bollards, a roller chute from the loading face at its back down to the boat, a
        /// derrick at one corner, and over the water two floating pontoons either side of the bay.
        /// </summary>
        public static void SmallPort(MeshBatch batch, Vector3 at, int facing, Material? only, bool worn)
        {
            const float t = SimConstants.TileSize;
            var slab = only ?? MeshKit.Mat(worn ? new Color("6d6a64") : new Color("a8a49b"), 0.9f);
            var edge = only ?? MeshKit.Mat(new Color("7c786f"), 0.9f);
            var orange = only ?? MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            var steel = only ?? MeshKit.Mat(worn ? SorterDark : SorterBody, 0.45f, 0.6f);
            var dark = only ?? MeshKit.Mat(SorterDark, 0.6f, 0.4f);
            var yellow = only ?? MeshKit.Mat(BeltYellow, 0.6f);
            var deck = only ?? MeshKit.Mat(new Color("8f6f50"), 0.9f);
            var float_ = only ?? MeshKit.Mat(new Color("dfe3e6"), 0.6f);
            var iron = only ?? MeshKit.Mat(IronColour, 0.45f, 0.6f);
            var f = Dir(facing);
            var r = Dir((facing + 1) & 3);
            var along = new Vector3(Mathf.Abs(f.X), 0f, Mathf.Abs(f.Z));
            var across = new Vector3(Mathf.Abs(r.X), 0f, Mathf.Abs(r.Z));
            float w = 3f * t - 0.1f;
            // The quay: its two rows of land, the waterline at the construct's middle.
            var quay = at - f * t;
            batch.Box(quay + new Vector3(0f, 0.05f, 0f), across * w + along * (2f * t - 0.05f) + new Vector3(0f, 0.1f, 0f), slab, 0.03f);
            batch.Box(at - f * 0.2f + new Vector3(0f, -0.6f, 0f), across * w + along * 0.4f + new Vector3(0f, 1.3f, 0f), edge, 0.02f);
            // The railing along the edge, the bay's gap in it; bollards either side of the gap.
            for (int k = -1; k <= 1; k += 2)
            {
                var side = at - f * 0.25f + r * (k * 2.0f);
                batch.Box(side + new Vector3(0f, 0.95f, 0f), across * 1.9f + along * 0.06f + new Vector3(0f, 0.06f, 0f), orange, 0.01f);
                batch.Box(side + new Vector3(0f, 0.55f, 0f), across * 1.9f + along * 0.05f + new Vector3(0f, 0.05f, 0f), orange, 0.01f);
                foreach (float p in new[] { -0.9f, 0f, 0.9f })
                    batch.Box(side + r * p + new Vector3(0f, 0.5f, 0f), new Vector3(0.06f, 0.9f, 0.06f), orange, 0.01f);
                var bollard = at - f * 0.35f + r * (k * 0.95f);
                batch.Add(MeshKit.Cylinder(0.13f, 0.15f, 0.45f, 12), iron, bollard + new Vector3(0f, 0.32f, 0f));
                batch.Add(MeshKit.Cylinder(0.19f, 0.19f, 0.05f, 12), iron, bollard + new Vector3(0f, 0.56f, 0f));
            }
            // The loading face at the back middle, hatched, and a roller chute from it to the boat.
            var back = at - f * (1.5f * t);
            batch.Box(back + new Vector3(0f, 0.104f, 0f), across * 1.9f + along * 1.8f + new Vector3(0f, 0.01f, 0f), dark, 0.0f);
            for (int k = -3; k <= 3; k++)
                batch.Add(MeshKit.Box(new Vector3(0.12f, 0.014f, 0.62f)), yellow, new Transform3D(new Basis(Vector3.Up, Mathf.Atan2(f.X, f.Z) + Mathf.Pi * 0.25f), back + r * (k * 0.27f) + new Vector3(0f, 0.11f, 0f)));
            var chuteFrom = back + f * 0.6f + new Vector3(0f, 0.55f, 0f);
            var chuteTo = at + f * 0.5f + new Vector3(0f, 0.1f, 0f);
            var chuteMid = (chuteFrom + chuteTo) * 0.5f;
            float chuteLength = chuteFrom.DistanceTo(chuteTo), slope = Mathf.Atan2(chuteFrom.Y - chuteTo.Y, (chuteTo - chuteFrom).Length());
            var chuteBasis = new Basis(Vector3.Up, Mathf.Atan2(f.X, f.Z)) * new Basis(Vector3.Right, slope);
            batch.Add(MeshKit.Box(new Vector3(0.9f, 0.08f, chuteLength)), steel, new Transform3D(chuteBasis, chuteMid));
            for (int k = -1; k <= 1; k += 2)
                batch.Add(MeshKit.Box(new Vector3(0.06f, 0.2f, chuteLength)), orange, new Transform3D(chuteBasis, chuteMid + r * (k * 0.45f) + new Vector3(0f, 0.08f, 0f)));
            foreach (float s in new[] { 0.15f, 0.85f })
            {
                var leg = chuteFrom.Lerp(chuteTo, s);
                batch.Box(new Vector3(leg.X, (leg.Y - 0.05f) * 0.5f, leg.Z), new Vector3(0.08f, Mathf.Max(0.05f, leg.Y), 0.08f), steel, 0.01f);
            }
            // The derrick at the back corner: a post, a boom out over the bay, its hook.
            var post = at - f * (1.6f * t) + r * (1.1f * t);
            batch.Add(MeshKit.Cylinder(0.14f, 0.18f, 3.4f, 12), orange, post + new Vector3(0f, 1.7f, 0f));
            var boomTip = at + f * 1.6f + r * 0.4f + new Vector3(0f, 3.1f, 0f);
            var boomFrom = post + new Vector3(0f, 3.2f, 0f);
            var boomBasis = Basis.LookingAt((boomTip - boomFrom).Normalized(), Vector3.Up);
            batch.Add(MeshKit.Box(new Vector3(0.14f, 0.14f, boomFrom.DistanceTo(boomTip))), orange, new Transform3D(boomBasis, (boomFrom + boomTip) * 0.5f));
            batch.Add(MeshKit.Cylinder(0.012f, 0.012f, 1.6f, 6), steel, boomTip + new Vector3(0f, -0.8f, 0f));
            batch.Box(boomTip + new Vector3(0f, -1.65f, 0f), new Vector3(0.18f, 0.12f, 0.18f), iron, 0.02f);
            // The pontoons over the water either side of the bay, on their floats, with posts at the end.
            for (int k = -1; k <= 1; k += 2)
            {
                var pontoon = at + f * t + r * (k * 2.25f);
                batch.Box(pontoon + new Vector3(0f, -0.2f, 0f), across * 1.0f + along * (2f * t - 0.1f) + new Vector3(0f, 0.1f, 0f), deck, 0.02f);
                batch.Box(pontoon + new Vector3(0f, -0.38f, 0f), across * 0.9f + along * (2f * t - 0.3f) + new Vector3(0f, 0.26f, 0f), float_, 0.08f);
                batch.Add(MeshKit.Cylinder(0.1f, 0.1f, 2.6f, 10), iron, pontoon + f * (t - 0.2f) + new Vector3(0f, -0.3f, 0f));
            }
            // The Route Console for its captain (doc 04 §3.3) on the quay left of the bay, where
            // SimWorld.Console puts it, its screen turned to the bay.
            var offset = -f * (0.5f * t) - r * t;
            var console = at + offset + offset.Normalized() * Sim.SimWorld.ConsoleOutset;
            float yaw = Mathf.Atan2(f.X, f.Z);
            var screen = only ?? MeshKit.Glow(new Color("4fd3e8"), 1.2f);
            batch.Box(console + new Vector3(0f, 0.08f, 0f), new Vector3(0.7f, 0.16f, 0.7f), dark, 0.03f);
            batch.Box(console + new Vector3(0f, 0.6f, 0f), new Vector3(0.46f, 0.9f, 0.4f), steel, 0.05f, new Vector3(0f, Mathf.RadToDeg(yaw), 0f));
            batch.Box(console + new Vector3(0f, 1.08f, 0f), new Vector3(0.56f, 0.07f, 0.5f), orange, 0.02f, new Vector3(0f, Mathf.RadToDeg(yaw), 0f));
            var tilt = new Basis(Vector3.Up, yaw + Mathf.Pi * 0.5f) * new Basis(Vector3.Right, -0.6f);
            batch.Add(MeshKit.Box(new Vector3(0.5f, 0.34f, 0.07f)), dark, new Transform3D(tilt, console + r * 0.06f + new Vector3(0f, 1.32f, 0f)));
            batch.Add(MeshKit.Box(new Vector3(0.42f, 0.27f, 0.02f)), screen, new Transform3D(tilt, console + r * 0.1f + new Vector3(0f, 1.34f, 0f)));
        }

        private const float PumpPivotHeight = 2.45f;
        private static float PumpYaw(int facing) => Mathf.Atan2(Dir(facing).X, Dir(facing).Z);

        /// <summary>
        /// Doc 10 §2.3 Oil Pump (design choice 116), a pumpjack of the concept video's industrial
        /// kit: a concrete pad, the gearbox and its crank behind, a steel A-frame, the wellhead with
        /// its pipe to a small tank of cans. The walking beam with its horse head and counterweight
        /// is <see cref="PumpBeam"/> (a node of its own that nods; in a ghost it is drawn in).
        /// </summary>
        public static void OilPump(MeshBatch batch, Vector3 at, int facing, Material? only, bool worn)
        {
            var pad = only ?? MeshKit.Mat(worn ? new Color("6d6a64") : new Color("a8a49b"), 0.9f);
            var steel = only ?? MeshKit.Mat(worn ? SorterDark : SorterBody, 0.45f, 0.6f);
            var orange = only ?? MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            var dark = only ?? MeshKit.Mat(SorterDark, 0.6f, 0.4f);
            var oil = only ?? MeshKit.Mat(new Color("15120f"), 0.15f, 0.2f);
            var basis = new Basis(Vector3.Up, PumpYaw(facing));
            var old = batch.Frame;
            batch.Frame = new Transform3D(basis, at);
            batch.Box(new Vector3(0f, 0.08f, 0f), new Vector3(1.9f, 0.16f, 1.9f), pad, 0.04f);
            // The wellhead in front (+Z), a stain of oil round it.
            batch.Add(MeshKit.Cylinder(0.42f, 0.46f, 0.02f, 16), oil, new Vector3(0f, 0.17f, 0.62f));
            batch.Add(MeshKit.Cylinder(0.11f, 0.13f, 0.7f, 12), dark, new Vector3(0f, 0.5f, 0.62f));
            batch.Add(MeshKit.Cylinder(0.17f, 0.17f, 0.12f, 12), orange, new Vector3(0f, 0.86f, 0.62f));
            // The A-frame: two legs splayed either side of the beam, a cross brace.
            for (int k = -1; k <= 1; k += 2)
            {
                var foot = new Vector3(k * 0.45f, 0.16f, 0f);
                var top = new Vector3(k * 0.1f, PumpPivotHeight, 0f);
                var mid = (foot + top) * 0.5f;
                var dir = (top - foot).Normalized();
                var leg = new Basis(dir.Cross(new Vector3(0f, 0f, 1f)).Normalized(), dir, new Vector3(0f, 0f, 1f));
                batch.Add(MeshKit.Box(new Vector3(0.12f, (top - foot).Length(), 0.12f)), orange, new Transform3D(leg, mid));
            }
            batch.Box(new Vector3(0f, 1.2f, 0f), new Vector3(0.7f, 0.08f, 0.08f), orange, 0.01f);
            batch.Box(new Vector3(0f, PumpPivotHeight, 0f), new Vector3(0.36f, 0.16f, 0.16f), dark, 0.03f);
            // The gearbox and its crank discs behind (−Z).
            batch.Box(new Vector3(0f, 0.55f, -0.62f), new Vector3(0.5f, 0.6f, 0.46f), steel, 0.05f);
            for (int k = -1; k <= 1; k += 2)
                batch.Add(MeshKit.Cylinder(0.32f, 0.32f, 0.07f, 18), dark, new Transform3D(new Basis(Vector3.Forward, Mathf.Pi * 0.5f), new Vector3(k * 0.3f, 0.8f, -0.62f)));
            // A small tank of cans at the side, a pipe to it from the wellhead.
            batch.Add(MeshKit.Cylinder(0.26f, 0.26f, 0.7f, 14), only ?? MeshKit.Mat(new Color("c0392b"), 0.5f), new Vector3(0.7f, 0.5f, 0.5f));
            batch.Box(new Vector3(0.35f, 0.4f, 0.62f), new Vector3(0.5f, 0.06f, 0.06f), dark, 0.01f);
            if (only != null)
            {
                // The ghost: the beam in, level.
                batch.Frame = new Transform3D(basis, at + new Vector3(0f, PumpPivotHeight, 0f));
                PumpBeamInto(batch, only, worn);
            }
            batch.Frame = old;
        }

        /// <summary>A pump's walking beam on its own node, nodding about the pivot (its origin).</summary>
        private static void PumpBeam(Node3D beam, Material? only, bool worn)
        {
            var batch = new MeshBatch();
            PumpBeamInto(batch, only, worn);
            batch.Flush(beam, "Beam");
        }

        private static void PumpBeamInto(MeshBatch batch, Material? only, bool worn)
        {
            var orange = only ?? MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            var dark = only ?? MeshKit.Mat(SorterDark, 0.6f, 0.4f);
            var steel = only ?? MeshKit.Mat(worn ? SorterDark : SorterBody, 0.45f, 0.6f);
            // The beam along Z, the horse head over the wellhead (+Z), the counterweight behind.
            batch.Box(new Vector3(0f, 0.12f, 0f), new Vector3(0.2f, 0.22f, 1.9f), orange, 0.03f);
            batch.Box(new Vector3(0f, 0.0f, 0.95f), new Vector3(0.24f, 0.62f, 0.3f), steel, 0.06f);
            batch.Box(new Vector3(0f, -0.28f, 1.06f), new Vector3(0.2f, 0.12f, 0.12f), dark, 0.02f);
            batch.Box(new Vector3(0f, -0.95f, 1.0f), new Vector3(0.03f, 1.3f, 0.03f), dark, 0.0f);
            batch.Box(new Vector3(0f, 0.0f, -0.9f), new Vector3(0.36f, 0.4f, 0.34f), dark, 0.05f);
        }

        public static void VehicleDepot(MeshBatch batch, Vector3 at, int facing, Material? only, bool worn)
        {
            const float t = SimConstants.TileSize;
            float w = 3f * t - 0.12f, half = w * 0.5f, roof = 3.25f;
            var slab = only ?? MeshKit.Mat(worn ? new Color("6d6a64") : new Color("a19d94"), 0.9f);
            var steel = only ?? MeshKit.Mat(worn ? SorterDark : SorterBody, 0.45f, 0.6f);
            var rib = only ?? MeshKit.Mat(new Color("7d858f"), 0.45f, 0.6f);
            var orange = only ?? MeshKit.Mat(SorterTrim, 0.5f, 0.2f);
            var dark = only ?? MeshKit.Mat(SorterDark, 0.6f, 0.4f);
            var yellow = only ?? MeshKit.Mat(BeltYellow, 0.6f);
            var f = Dir(facing);
            var r = Dir((facing + 1) & 3);
            var along = new Vector3(Mathf.Abs(f.X), 0f, Mathf.Abs(f.Z));
            var across = new Vector3(Mathf.Abs(r.X), 0f, Mathf.Abs(r.Z));
            float yaw = Mathf.Atan2(f.X, f.Z);
            batch.Box(at + new Vector3(0f, 0.05f, 0f), new Vector3(w, 0.1f, w), slab, 0.03f);
            // The bay: a yellow line down each side and a stop bar before the nose.
            for (int k = -1; k <= 1; k += 2)
                batch.Box(at + r * (k * 1.45f) + new Vector3(0f, 0.105f, 0f), along * (w - 0.9f) + across * 0.12f + new Vector3(0f, 0.012f, 0f), yellow, 0.0f);
            batch.Box(at + f * (half - 0.55f) + new Vector3(0f, 0.105f, 0f), across * 2.9f + along * 0.16f + new Vector3(0f, 0.012f, 0f), yellow, 0.0f);
            // The loading face at the back middle: hatching, and an arrow pointing in.
            var back = at - f * (half - 0.35f);
            batch.Box(back + new Vector3(0f, 0.104f, 0f), across * 1.9f + along * 0.5f + new Vector3(0f, 0.01f, 0f), dark, 0.0f);
            for (int k = -3; k <= 3; k++)
                batch.Add(MeshKit.Box(new Vector3(0.12f, 0.014f, 0.62f)), yellow, new Transform3D(new Basis(Vector3.Up, yaw + Mathf.Pi * 0.25f), back + r * (k * 0.27f) + new Vector3(0f, 0.11f, 0f)));
            var amber = only ?? MeshKit.Mat(PortOut, 0.5f);
            batch.Add(MeshKit.Box(new Vector3(0.14f, 0.02f, 0.5f)), amber, new Transform3D(new Basis(Vector3.Up, yaw), at - f * (half - 1.05f) + new Vector3(0f, 0.115f, 0f)));
            batch.Add(MeshKit.Cone(0.2f, 0.3f, 3), amber, new Transform3D(new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, Mathf.Pi * 0.5f), at - f * (half - 1.45f) + new Vector3(0f, 0.115f, 0f)));
            // The canopy: four posts, two beams along the bay, a ribbed roof.
            for (int a = -1; a <= 1; a += 2)
                for (int c = -1; c <= 1; c += 2)
                {
                    var post = at + f * (a * (half - 0.2f)) + r * (c * (half - 0.2f));
                    batch.Box(post + new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, 0.3f, 0.5f), dark, 0.04f);
                    batch.Box(post + new Vector3(0f, roof * 0.5f, 0f), new Vector3(0.24f, roof, 0.24f), orange, 0.04f);
                }
            for (int c = -1; c <= 1; c += 2)
                batch.Box(at + r * (c * (half - 0.2f)) + new Vector3(0f, roof + 0.1f, 0f), along * (w - 0.1f) + across * 0.28f + new Vector3(0f, 0.3f, 0f), steel, 0.04f);
            batch.Box(at + new Vector3(0f, roof + 0.32f, 0f), new Vector3(w + 0.2f, 0.12f, w + 0.2f), steel, 0.04f);
            for (int k = -4; k <= 4; k++)
                batch.Box(at + f * (k * 0.66f) + new Vector3(0f, roof + 0.42f, 0f), along * 0.12f + across * (w + 0.1f) + new Vector3(0f, 0.08f, 0f), rib, 0.02f);
            // The yellow band under the roof's edge, front and back.
            for (int a = -1; a <= 1; a += 2)
                batch.Box(at + f * (a * (half + 0.08f)) + new Vector3(0f, roof + 0.2f, 0f), across * (w + 0.2f) + along * 0.06f + new Vector3(0f, 0.16f, 0f), yellow, 0.01f);
            // The Route Console on the left of the bay, its screen turned to it.
            var console = at - r * (t + Sim.SimWorld.ConsoleOutset);
            batch.Box(console + new Vector3(0f, 0.08f, 0f), new Vector3(0.7f, 0.16f, 0.7f), dark, 0.03f);
            batch.Box(console + new Vector3(0f, 0.6f, 0f), new Vector3(0.46f, 0.9f, 0.4f), steel, 0.05f, new Vector3(0f, Mathf.RadToDeg(yaw), 0f));
            batch.Box(console + new Vector3(0f, 1.08f, 0f), new Vector3(0.56f, 0.07f, 0.5f), orange, 0.02f, new Vector3(0f, Mathf.RadToDeg(yaw), 0f));
            var screen = only ?? MeshKit.Glow(new Color("4fd3e8"), 1.2f);
            var tilt = new Basis(Vector3.Up, yaw + Mathf.Pi * 0.5f) * new Basis(Vector3.Right, -0.6f);
            batch.Add(MeshKit.Box(new Vector3(0.5f, 0.34f, 0.07f)), dark, new Transform3D(tilt, console + r * 0.06f + new Vector3(0f, 1.32f, 0f)));
            batch.Add(MeshKit.Box(new Vector3(0.42f, 0.27f, 0.02f)), screen, new Transform3D(tilt, console + r * 0.1f + new Vector3(0f, 1.34f, 0f)));
        }

        /// <summary>A framed opening at the edge of a sorter's tile (along, across) on the side <paramref name="dir"/>.</summary>
        private static void Mouth(MeshBatch batch, Vector3 centre, int facing, int along, int across, int dir, Material frame, Material hole)
        {
            const float t = SimConstants.TileSize;
            var f = Dir(facing);
            var r = Dir((facing + 1) & 3);
            var tile = centre + f * ((along - 0.5f) * t) + r * ((across - 0.5f) * t);
            var d = Dir(dir);
            var side = new Vector3(Mathf.Abs(d.Z), 0f, Mathf.Abs(d.X));
            var at = tile + d * (t * 0.5f - 0.45f);
            batch.Box(at + new Vector3(0f, 0.55f, 0f), side * 1.15f + new Vector3(0f, 0.75f, 0f) + new Vector3(Mathf.Abs(d.X), 0f, Mathf.Abs(d.Z)) * 0.12f, frame, 0.03f);
            batch.Box(at + d * 0.02f + new Vector3(0f, 0.5f, 0f), side * 0.9f + new Vector3(0f, 0.55f, 0f) + new Vector3(Mathf.Abs(d.X), 0f, Mathf.Abs(d.Z)) * 0.12f, hole, 0.02f);
        }

        /// <summary>
        /// One construct of any kind, in its own colours or all in <paramref name="only"/> (the
        /// ghost's). <paramref name="arms"/> are a wall's, <paramref name="worn"/> under half its HP.
        /// </summary>
        public static void Draw(MeshBatch batch, BuildingDef def, Vector3 at, int arms, int rotation, Material? only, bool worn)
        {
            switch (def.Behaviour)
            {
                case BuildingBehaviour.Gate:
                {
                    // Its own two arms are the opening; what joins it from the sides is palisade.
                    int own = (rotation & 1) != 0 ? ConstructField.ArmSouth | ConstructField.ArmNorth : ConstructField.ArmEast | ConstructField.ArmWest;
                    var log = only ?? Mat(worn ? LogDark : LogColour);
                    if ((arms & ~own) != 0) Wall(batch, at, arms & ~own, def.Height, log, only ?? Mat(RailColour), worn, centre: false);
                    GateFrame(batch, at, (rotation & 1) != 0, def.Height, log, only ?? Mat(RailColour), only != null);
                    return;
                }
                case BuildingBehaviour.Alarm:
                    AlarmPost(batch, at, only ?? Mat(LogColour), only ?? Mat(RoofColour), only ?? MeshKit.Mat(BrassColour, 0.35f, 0.6f), only ?? MeshKit.Mat(new Color("7a1a14"), 0.5f));
                    return;
                case BuildingBehaviour.Spikes:
                    Spikes(batch, at, only ?? Mat(PlateColour), only ?? MeshKit.Mat(IronColour, 0.4f, 0.6f));
                    return;
                case BuildingBehaviour.Sorter:
                    Sorter(batch, at, rotation & 3, only, worn);
                    return;
                case BuildingBehaviour.Inserter:
                    InserterBase(batch, at, rotation & 3, only, worn, def.Reach);
                    return;
                case BuildingBehaviour.Pipe:
                    PipePiece(batch, def.PipeRole, at, rotation & 3, only, worn);
                    return;
                case BuildingBehaviour.Container when def.Width > 1:
                    Depot(batch, at, rotation & 3, only, worn);
                    return;
                case BuildingBehaviour.VehicleDepot:
                    VehicleDepot(batch, at, rotation & 3, only, worn);
                    return;
                case BuildingBehaviour.Pier:
                    Pier(batch, at, rotation & 3, only, worn);
                    return;
                case BuildingBehaviour.Port:
                    SmallPort(batch, at, rotation & 3, only, worn);
                    return;
                case BuildingBehaviour.Pump:
                    OilPump(batch, at, rotation & 3, only, worn);
                    return;
                case BuildingBehaviour.Turret:
                    TurretBase(batch, at, rotation & 3, only, worn);
                    return;
                case BuildingBehaviour.Boat:
                    // Only ever a ghost: what is placed is the boat itself (VehiclesView).
                    VehiclesView.RowboatHull(batch, at + new Vector3(0f, IslandView.BasinLevel, 0f), Yaw(rotation) , only);
                    return;
                case BuildingBehaviour.Container:
                    if (only != null) batch.Box(new Vector3(0f, 0.4f, 0f), new Vector3(0.96f, 0.8f, 0.66f), only, 0.1f, new Vector3(0f, Mathf.RadToDeg(Yaw(rotation)), 0f));
                    else SceneryView.Chest(batch, at, Yaw(rotation));
                    return;
            }
            if (def.Id == "wall_stone") StoneWall(batch, at, arms, def.Height, only ?? Mat(worn ? StoneDark : StoneColour), only ?? Mat(MortarColour), worn);
            else Wall(batch, at, arms, def.Height, only ?? Mat(worn ? LogDark : LogColour), only ?? Mat(RailColour), worn);
        }

        private static readonly Color StoneColour = new Color("a9a49a"), StoneDark = new Color("6f6a62"), MortarColour = new Color("8b857a");
        private static readonly Color PlateColour = new Color("5c4330"), IronColour = new Color("3a3940");
        private static readonly Color PlankColour = new Color("9b6a40"), RoofColour = new Color("7d3b2a"), BrassColour = new Color("c9a23a");
        private const float GatePost = 0.18f, LeafHeight = 2.0f, AlarmLampHeight = 3.05f;

        /// <summary>
        /// A gate's frame: a squared post at each end of the opening and a beam across their
        /// tops. With <paramref name="leaves"/> (the ghost) the leaves are drawn shut into the batch.
        /// </summary>
        public static void GateFrame(MeshBatch batch, Vector3 at, bool alongZ, float height, Material post, Material beam, bool leaves)
        {
            float half = SimConstants.TileSize * 0.5f;
            var along = alongZ ? Vector3.Back : Vector3.Right;
            float h = height + 0.25f;
            for (int side = -1; side <= 1; side += 2)
            {
                batch.Box(at + along * side * (half - GatePost) + new Vector3(0f, h * 0.5f, 0f), new Vector3(GatePost * 2f, h, GatePost * 2f), post, 0.04f);
                batch.Add(MeshKit.Cone(GatePost * 1.1f, 0.3f, 4), post, at + along * side * (half - GatePost) + new Vector3(0f, h + 0.15f, 0f), new Vector3(0f, 45f, 0f));
            }
            var size = alongZ ? new Vector3(0.22f, 0.24f, SimConstants.TileSize) : new Vector3(SimConstants.TileSize, 0.24f, 0.22f);
            batch.Box(at + new Vector3(0f, h - 0.3f, 0f), size, beam, 0.04f);
            if (!leaves) return;
            float width = half - GatePost * 2f;
            var leaf = alongZ ? new Vector3(0.1f, LeafHeight, width * 2f) : new Vector3(width * 2f, LeafHeight, 0.1f);
            batch.Box(at + new Vector3(0f, LeafHeight * 0.5f + 0.08f, 0f), leaf, post, 0.03f);
        }

        /// <summary>One leaf of a gate, hung on the post on its side (−1 west, +1 east of the frame): planks, two battens and iron straps.</summary>
        private static Node3D Leaf(Node3D frame, int side, Material plank, Material iron)
        {
            float half = SimConstants.TileSize * 0.5f, width = half - GatePost * 2f;
            var hinge = new Node3D { Name = side < 0 ? "LeafL" : "LeafR", Position = new Vector3(side * (half - GatePost * 2f), 0f, 0f) };
            frame.AddChild(hinge);
            int planks = 4;
            float w = width / planks;
            for (int i = 0; i < planks; i++)
            {
                float x = -side * (w * (i + 0.5f));
                float top = LeafHeight - (i % 2) * 0.08f;
                hinge.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(w - 0.02f, top, 0.07f), 0.02f), plank, new Vector3(x, top * 0.5f + 0.08f, 0f)));
            }
            for (int k = 0; k < 2; k++)
            {
                float y = 0.08f + LeafHeight * (k == 0 ? 0.25f : 0.75f);
                hinge.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(width - 0.04f, 0.14f, 0.05f), 0.02f), plank, new Vector3(-side * width * 0.5f, y, 0.06f)));
                hinge.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(width * 0.6f, 0.06f, 0.03f), 0.01f), iron, new Vector3(-side * width * 0.3f, y, -0.05f)));
            }
            return hinge;
        }

        /// <summary>
        /// An Alarm Post: a tall post on a footing with a little pitched roof, a brass bell under
        /// it on a crossbar and the lamp's housing on top (the light itself flashes, so it is drawn apart).
        /// </summary>
        public static void AlarmPost(MeshBatch batch, Vector3 at, Material wood, Material roof, Material brass, Material housing)
        {
            batch.Box(at + new Vector3(0f, 0.12f, 0f), new Vector3(0.6f, 0.24f, 0.6f), wood, 0.05f);
            batch.Add(MeshKit.Cylinder(0.12f, 0.1f, 2.8f, 10), wood, at + new Vector3(0f, 1.4f, 0f));
            batch.Box(at + new Vector3(0f, 2.35f, 0f), new Vector3(0.9f, 0.1f, 0.12f), wood, 0.02f);
            for (int side = -1; side <= 1; side += 2)
                batch.Box(at + new Vector3(side * 0.24f, 2.78f, 0f), new Vector3(0.56f, 0.06f, 0.7f), roof, 0.02f, new Vector3(0f, 0f, side * -28f));
            // The bell hangs from the crossbar, its mouth down.
            batch.Add(MeshKit.Cylinder(0.2f, 0.09f, 0.3f, 14), brass, at + new Vector3(0.3f, 2.12f, 0f));
            batch.Add(MeshKit.Sphere(0.09f, 10), brass, at + new Vector3(0.3f, 2.28f, 0f));
            batch.Add(MeshKit.Sphere(0.05f, 8), housing, at + new Vector3(0.3f, 1.95f, 0f));
            batch.Add(MeshKit.Cylinder(0.14f, 0.12f, 0.12f, 10), housing, at + new Vector3(0f, AlarmLampHeight - 0.14f, 0f));
            batch.Add(MeshKit.Sphere(0.1f, 12), MeshKit.Mat(new Color("5a1610"), 0.3f), at + new Vector3(0f, AlarmLampHeight, 0f));
        }

        /// <summary>
        /// A dry-stone wall: courses of rounded blocks along every arm, each course set off by half
        /// a block from the one under it, on a footing a little wider.
        /// </summary>
        public static void StoneWall(MeshBatch batch, Vector3 at, int arms, float height, Material stone, Material mortar, bool worn)
        {
            float half = SimConstants.TileSize * 0.5f;
            const float thick = 0.5f, block = 0.5f;
            int courses = Mathf.Max(2, Mathf.RoundToInt(height / 0.36f));
            float course = height / courses;
            // The middle of the tile, where the arms meet.
            batch.Box(at + new Vector3(0f, height * 0.5f, 0f), new Vector3(thick + 0.04f, height, thick + 0.04f), stone, 0.08f);
            for (int d = 0; d < 4; d++)
            {
                if ((arms & (1 << d)) == 0) continue;
                var dir = d switch { 0 => Vector3.Right, 1 => Vector3.Back, 2 => Vector3.Left, _ => Vector3.Forward };
                batch.Box(at + dir * (half * 0.5f) + new Vector3(0f, 0.06f, 0f), new Vector3(dir.X != 0f ? half : thick + 0.12f, 0.12f, dir.Z != 0f ? half : thick + 0.12f), mortar, 0.03f);
                for (int k = 0; k < courses; k++)
                {
                    // A worn wall has lost its top course or two at the far end.
                    float offset = (k & 1) == 0 ? 0f : block * 0.5f;
                    for (float s = thick * 0.5f + offset; s < half; s += block)
                    {
                        float len = Mathf.Min(block - 0.03f, half - s);
                        if (len < 0.12f) continue;
                        if (worn && k >= courses - 2 && s > half * 0.4f) continue;
                        float jitter = 0.03f * (MeshKit.Noise3(at + dir * s + new Vector3(0f, k, 0f)) - 0.5f);
                        var centre = at + dir * (s + len * 0.5f) + new Vector3(0f, course * (k + 0.5f), 0f);
                        var size = new Vector3(dir.X != 0f ? len : thick + jitter, course - 0.03f, dir.Z != 0f ? len : thick + jitter);
                        batch.Box(centre, size, stone, 0.07f);
                    }
                }
            }
        }

        /// <summary>A Spike Strip: a low plank plate on the ground with rows of iron spikes standing up from it.</summary>
        public static void Spikes(MeshBatch batch, Vector3 at, Material plate, Material iron)
        {
            float size = SimConstants.TileSize - 0.25f;
            batch.Box(at + new Vector3(0f, 0.04f, 0f), new Vector3(size, 0.08f, size), plate, 0.03f);
            var spike = MeshKit.Cone(0.07f, 0.28f, 6);
            const int rows = 5;
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < rows; j++)
                {
                    float x = (i + 0.5f) / rows * size - size * 0.5f, z = (j + 0.5f) / rows * size - size * 0.5f;
                    if (((i + j) & 1) == 1) x += size / rows * 0.25f;
                    batch.Add(spike, iron, at + new Vector3(x, 0.08f + 0.14f, z));
                }
        }

        /// <summary>Local −Z is a chest's front: quarter turns from there.</summary>
        private static float Yaw(int rotation) => rotation * Mathf.Pi * 0.5f;

        /// <summary>
        /// A palisade: a post in the middle of the tile and pointed logs out along every arm to the
        /// tile's edge, tied by two rails.
        /// </summary>
        public static void Wall(MeshBatch batch, Vector3 at, int arms, float height, Material log, Material rail, bool worn, bool centre = true)
        {
            float half = SimConstants.TileSize * 0.5f;
            var logMesh = MeshKit.Cylinder(LogRadius, LogRadius * 0.92f, 1f, 9);
            var tip = MeshKit.Cone(LogRadius, 0.32f, 9);
            if (centre) AddLog(batch, at, Vector3.Zero, height, logMesh, tip, log, worn, 0);
            for (int d = 0; d < 4; d++)
            {
                if ((arms & (1 << d)) == 0) continue;
                var dir = d switch { 0 => Vector3.Right, 1 => Vector3.Back, 2 => Vector3.Left, _ => Vector3.Forward };
                int n = 0;
                for (float s = LogStep; s <= half - LogRadius * 0.5f; s += LogStep)
                    AddLog(batch, at, dir * s, height, logMesh, tip, log, worn, d * 7 + ++n);
                // The rails run the arm's whole length, in front and behind.
                var side = new Vector3(dir.Z, 0f, -dir.X) * (LogRadius + 0.03f);
                var size = new Vector3(dir.X != 0f ? half : 0.06f, 0.12f, dir.Z != 0f ? half : 0.06f);
                for (int k = 0; k < 2; k++)
                {
                    var up = new Vector3(0f, height * (k == 0 ? 0.3f : 0.72f), 0f);
                    batch.Box(at + dir * (half * 0.5f) + side + up, size, rail, 0.02f);
                    batch.Box(at + dir * (half * 0.5f) - side + up, size, rail, 0.02f);
                }
            }
        }

        private static void AddLog(MeshBatch batch, Vector3 at, Vector3 offset, float height, Mesh log, Mesh tip, Material mat, bool worn, int seed)
        {
            // A little uneven, as logs are; a worn wall leans and has lost some height.
            float h = height * (0.9f + 0.1f * MeshKit.Noise3(at + offset * 3.7f + new Vector3(seed, 0f, 0f)));
            if (worn) h *= 0.85f;
            var basis = Basis.Identity;
            if (worn) basis = new Basis(Vector3.Forward, 0.08f * (MeshKit.Noise3(at + offset + new Vector3(0f, seed, 0f)) - 0.5f) * 2f);
            batch.Add(log, mat, new Transform3D(basis.Scaled(new Vector3(1f, h, 1f)), at + offset + new Vector3(0f, h * 0.5f, 0f)));
            batch.Add(tip, mat, new Transform3D(basis, at + offset + new Vector3(0f, h + 0.16f, 0f)));
        }

        private static Material Mat(Color c) => MeshKit.Mat(c, 0.85f);

        private static Material GhostMat(Color c) => new StandardMaterial3D
        {
            AlbedoColor = c,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false,
        };
    }
}
