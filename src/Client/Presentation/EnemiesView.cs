using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// One body per enemy the snapshots list. Enemies come at 10 Hz, so they are drawn further in
    /// the past than players; a hit flashes them white, a windup raises the arm, and one that the
    /// server says died falls over and sinks instead of vanishing.
    /// </summary>
    public partial class EnemiesView : Node3D
    {
        /// <summary>The enemy list comes every third tick; five ticks back there are always two samples to draw between.</summary>
        public const double InterpolationDelayTicks = 5.0;
        private const float FallSeconds = 0.7f, SinkSeconds = 0.5f;

        private sealed class View
        {
            public Node3D Root = null!;
            public CharacterModel Model = null!;
            public MeshInstance3D Bar = null!;
            /// <summary>The red mark over the head of a raider an Alarm Post has in reach, seen through walls.</summary>
            public MeshInstance3D Mark = null!;
            public ShaderMaterial BarMaterial = null!;
            public Vector3 Last;
            public bool Placed;
            public float Speed, Dying = -1f;
            public uint HurtTick;
            public byte Hp = 255;
            public EnemyState State;
        }

        public int ViewCount => _views.Count;

        /// <summary>The name of the model an enemy is drawn as ("Archer"); null for one not drawn.</summary>
        public string? ModelOf(ushort id) => _views.TryGetValue(id, out var view) ? view.Model.Name : null;
        /// <summary>How many raiders wear the Alarm Posts' mark as of the last frame.</summary>
        public int MarksShown { get; private set; }

        private readonly Dictionary<ushort, View> _views = new Dictionary<ushort, View>();
        private readonly List<View> _dying = new List<View>();
        private readonly List<ushort> _gone = new List<ushort>();
        private ClientRuntime _client = null!;

        public void Setup(ClientRuntime client)
        {
            _client = client;
            client.EventReceived += OnEvent;
        }

        public override void _ExitTree()
        {
            if (_client != null) _client.EventReceived -= OnEvent;
        }

        private void OnEvent(SimEvent ev)
        {
            if (!(ev is EnemyDiedEvent died) || !_views.Remove((ushort)died.Enemy.Counter, out var view)) return;
            view.Dying = 0f;
            view.Bar.Visible = false;
            view.Model.Flash();
            _dying.Add(view);
        }

        public override void _Process(double delta)
        {
            var world = _client?.World;
            if (world == null) return;
            float dt = (float)delta;
            double tick = world.ServerTick - InterpolationDelayTicks;
            int marks = 0;

            foreach (var enemy in world.Enemies.Values)
            {
                if (!_views.TryGetValue(enemy.Id, out var view)) _views[enemy.Id] = view = Create(enemy);
                enemy.Interpolate(tick, out float x, out float z, out float yaw);
                var position = new Vector3(x, 0f, z);
                float moved = view.Placed ? view.Last.DistanceTo(position) : 0f;
                if (dt > 0f) view.Speed = Mathf.Lerp(view.Speed, moved / dt, 1f - Mathf.Exp(-10f * dt));
                view.Last = position;
                view.Placed = true;
                view.Root.Position = position;
                view.Root.Rotation = new Vector3(0f, yaw, 0f);

                if (enemy.HurtTick != view.HurtTick)
                {
                    view.HurtTick = enemy.HurtTick;
                    view.Model.Flash();
                }
                // The blow lands as the windup ends.
                if (view.State == EnemyState.Windup && enemy.State != EnemyState.Windup) view.Model.Strike();
                view.State = enemy.State;
                if (enemy.Hp != view.Hp)
                {
                    view.Hp = enemy.Hp;
                    view.Bar.Visible = enemy.Hp < 255;
                    view.BarMaterial.SetShaderParameter("hp", enemy.Hp / 255f);
                }
                view.Model.Animate(dt, view.Speed, enemy.State == EnemyState.Windup, down: false);
                bool marked = world.Constructs.All.Count > 0 && RaidRules.Revealed(world.Constructs, x, z);
                view.Mark.Visible = marked;
                if (marked) marks++;
            }
            MarksShown = marks;

            if (_views.Count != world.Enemies.Count)
            {
                // Not killed, just no longer there: fled through the gate, or the phase swept the street.
                _gone.Clear();
                foreach (ushort id in _views.Keys)
                    if (!world.Enemies.ContainsKey(id)) _gone.Add(id);
                foreach (ushort id in _gone)
                {
                    _views[id].Root.QueueFree();
                    _views.Remove(id);
                }
            }

            for (int i = _dying.Count - 1; i >= 0; i--)
            {
                var view = _dying[i];
                view.Dying += dt;
                view.Model.Animate(dt, 0f, windup: false, down: true);
                float sink = Mathf.Clamp((view.Dying - FallSeconds) / SinkSeconds, 0f, 1f);
                view.Root.Scale = Vector3.One * (1f - sink * sink);
                if (sink < 1f) continue;
                view.Root.QueueFree();
                _dying.RemoveAt(i);
            }
        }

        private static StandardMaterial3D? _markMaterial;

        private static StandardMaterial3D MarkMaterial => _markMaterial ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.16f, 0.1f, 0.9f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            NoDepthTest = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RenderPriority = 2,
        };

        private View Create(ClientEnemy enemy)
        {
            float scale = enemy.Def.Scale * (enemy.Mega ? 1.4f : 1f);
            var view = new View
            {
                Root = new Node3D { Name = $"Enemy{enemy.Id}" },
                Model = Characters.Enemy(enemy.Def.Id, enemy.Mega),
                BarMaterial = Shaders.HealthBar(),
                State = enemy.State,
                HurtTick = enemy.HurtTick,
            };
            view.Model.Scale = Vector3.One * scale;
            view.Root.AddChild(view.Model);
            view.Bar = new MeshInstance3D
            {
                Name = "Health",
                Mesh = new QuadMesh { Size = new Vector2(Mathf.Clamp(0.9f * scale, 0.7f, 1.8f), 0.14f) },
                MaterialOverride = view.BarMaterial,
                Position = new Vector3(0f, view.Model.Height * scale + 0.28f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            view.Root.AddChild(view.Bar);
            // A red arrowhead pointing down at it, facing the camera and seen through walls.
            view.Mark = new MeshInstance3D
            {
                Name = "AlarmMark",
                Mesh = new PrismMesh { Size = new Vector3(0.5f, 0.45f, 0.02f) },
                MaterialOverride = MarkMaterial,
                Position = new Vector3(0f, view.Model.Height * scale + 0.75f, 0f),
                Scale = new Vector3(1f, -1f, 1f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            view.Root.AddChild(view.Mark);
            AddChild(view.Root);
            return view;
        }
    }
}
