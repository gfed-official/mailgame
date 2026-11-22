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
        private float _blink;

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
                    else Draw(batch, def, offset, arms, ghost.Rotation, mat, false);
                    // The tiles it takes, on the ground (a ramp's two).
                    for (int i = 0; i < def.Length; i++)
                        batch.Box(offset + Dir(ghost.Rotation & 3) * (i * SimConstants.TileSize) + new Vector3(0f, 0.02f, 0f), new Vector3(SimConstants.TileSize - 0.1f, 0.04f, SimConstants.TileSize - 0.1f), mat, 0.02f);
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
