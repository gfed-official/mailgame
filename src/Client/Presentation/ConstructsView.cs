using Godot;
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
    /// wall worn below half its HP is drawn darker and leaning. Batched like the town, drawn anew
    /// only when something is built, taken down or hit. In Build mode the ghost of the recipe
    /// stands on the tile under the crosshair, green where it can be built and red where not.
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
        private (BuildingDef?, int, bool) _ghostShape;

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

        public override void _Process(double delta) => Refresh();

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
            foreach (var c in field.All)
            {
                var at = new Vector3(c.X, world.Map.HeightAt(c.X, c.Z), c.Z);
                Draw(batch, c.Def, at, field.Arms(c), c.Rotation, null, c.Hp * 2 < c.Def.Hp);
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
            bool ok = ghost.State == OpResult.Ok;
            GhostGreen = ok;
            var shape = (def, ghost.Rotation, ok);
            if (shape != _ghostShape)
            {
                _ghostShape = shape;
                foreach (var child in _ghost.GetChildren())
                {
                    _ghost.RemoveChild(child);
                    child.QueueFree();
                }
                var batch = new MeshBatch();
                var mat = GhostMat(ok ? GhostOk : GhostBad);
                int arms = (ghost.Rotation & 1) != 0 ? ConstructField.ArmSouth | ConstructField.ArmNorth : ConstructField.ArmEast | ConstructField.ArmWest;
                Draw(batch, def, Vector3.Zero, arms, ghost.Rotation, mat, false);
                // The tile it takes, on the ground.
                batch.Box(new Vector3(0f, 0.02f, 0f), new Vector3(SimConstants.TileSize - 0.1f, 0.04f, SimConstants.TileSize - 0.1f), mat, 0.02f);
                batch.Flush(_ghost, "Ghost", castShadows: false);
            }
            float x = (ghost.Tx + 0.5f) * SimConstants.TileSize, z = (ghost.Tz + 0.5f) * SimConstants.TileSize;
            _ghost.Position = new Vector3(x, world.Map.HeightAt(x, z), z);
        }

        /// <summary>
        /// One construct of any kind, in its own colours or all in <paramref name="only"/> (the
        /// ghost's). <paramref name="arms"/> are a wall's, <paramref name="worn"/> under half its HP.
        /// </summary>
        public static void Draw(MeshBatch batch, BuildingDef def, Vector3 at, int arms, int rotation, Material? only, bool worn)
        {
            switch (def.Behaviour)
            {
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
        public static void Wall(MeshBatch batch, Vector3 at, int arms, float height, Material log, Material rail, bool worn)
        {
            float half = SimConstants.TileSize * 0.5f;
            var logMesh = MeshKit.Cylinder(LogRadius, LogRadius * 0.92f, 1f, 9);
            var tip = MeshKit.Cone(LogRadius, 0.32f, 9);
            AddLog(batch, at, Vector3.Zero, height, logMesh, tip, log, worn, 0);
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
