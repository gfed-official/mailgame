using System.Collections.Generic;
using Godot;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// Doc 05 §5 "Damage numbers and a health bar appear over constructs when damaged; the HUD shows
    /// a compass marker for any construct under attack" (design choice 130): a red number rises off
    /// each blow, a bar stands over what was hit for <see cref="ClientWorld.HitShownSeconds"/>, and
    /// the HUD's <see cref="AttackMarker"/> points at the nearest construct hit in the last
    /// <see cref="ClientWorld.UnderAttackSeconds"/>.
    /// </summary>
    public partial class ConstructHealthView : Node3D
    {
        private const float NumberSeconds = 1.2f, NumberRise = 1.1f, MarkerMargin = 48f, MarkerHeight = 1.7f;

        private sealed class Bar
        {
            public MeshInstance3D Node = null!;
            public ShaderMaterial Material = null!;
        }

        private sealed class Number
        {
            public Label3D Node = null!;
            public Vector3 From;
            public float Age;
        }

        private ClientRuntime _client = null!;
        private Camera3D _camera = null!;
        private AttackMarker _marker = null!;
        private readonly Dictionary<Construct, Bar> _bars = new Dictionary<Construct, Bar>();
        private readonly List<Construct> _gone = new List<Construct>();
        private readonly List<Number> _numbers = new List<Number>();
        private readonly HashSet<ConstructHit> _counted = new HashSet<ConstructHit>();

        /// <summary>Bars up as of the last frame, numbers shown so far, and the last number's text.</summary>
        public int BarsShown => _bars.Count;
        public int NumbersShown { get; private set; }
        /// <summary>A bar stands over what the Repair Hammer in the hand is held at, as of the last frame.</summary>
        public bool RepairBarShown { get; private set; }
        public string LastNumber { get; private set; } = "";
        /// <summary>Where the last number rising is, in the world; null once it has faded.</summary>
        public Vector3? LastNumberAt => _numbers.Count > 0 ? _numbers[^1].Node.GlobalPosition : null;
        /// <summary>Where the bars up are, in the world.</summary>
        public IEnumerable<Vector3> BarsAt
        {
            get { foreach (var bar in _bars.Values) yield return bar.Node.GlobalPosition; }
        }

        public void Setup(ClientRuntime client, Camera3D camera, AttackMarker marker)
        {
            _client = client;
            _camera = camera;
            _marker = marker;
        }

        /// <summary>A construct's bar stands this high over its ground: over its top.</summary>
        public static float BarHeight(Construct c) => (c.Def.Height > 0f ? c.Def.Height : 1.2f) + 0.45f;

        public override void _Process(double delta)
        {
            var world = _client?.World;
            if (world == null) return;
            world.PruneConstructHits();
            _counted.RemoveWhere(h => !world.ConstructHits.Contains(h));

            // A number off each new blow.
            foreach (var hit in world.ConstructHits)
            {
                if (!_counted.Add(hit)) continue;
                float y = world.Map.HeightAt(hit.X, hit.Z) + hit.Top + 0.2f;
                var label = new Label3D
                {
                    Text = "-" + hit.Amount,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    FixedSize = true,
                    PixelSize = 0.0016f,
                    FontSize = 30,
                    OutlineSize = 8,
                    Modulate = hit.Broke ? new Color("ffb347") : new Color("ff4d40"),
                    OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
                    // A little to one side of the last, so a run of blows does not stack in one place.
                    Position = new Vector3(hit.X + (NumbersShown % 3 - 1) * 0.25f, y, hit.Z),
                };
                AddChild(label);
                _numbers.Add(new Number { Node = label, From = label.Position });
                NumbersShown++;
                LastNumber = label.Text;
            }
            for (int i = _numbers.Count - 1; i >= 0; i--)
            {
                var n = _numbers[i];
                n.Age += (float)delta;
                float t = n.Age / NumberSeconds;
                if (t >= 1f) { n.Node.QueueFree(); _numbers.RemoveAt(i); continue; }
                n.Node.Position = n.From + Vector3.Up * (NumberRise * Mathf.Ease(t, 0.4f));
                n.Node.Modulate = new Color(n.Node.Modulate, 1f - t * t);
                n.Node.OutlineModulate = new Color(n.Node.OutlineModulate, (1f - t * t) * 0.85f);
            }

            // A bar over each construct hit lately that still stands hurt, and over what the Repair Hammer
            // in the hand is held at (doc 09 §3 "Repair hammer shows a target health bar").
            var mending = HudText.RepairTarget(world, _client!.Input.HeldSlot, out _, out _);
            RepairBarShown = false;
            foreach (var c in world.Constructs.All)
            {
                bool show = c.Hp < c.MaxHp && (world.HitWithin(c, ClientWorld.HitShownSeconds) || c == mending);
                if (show && c == mending) RepairBarShown = true;
                _bars.TryGetValue(c, out var bar);
                if (!show)
                {
                    if (bar != null) { bar.Node.QueueFree(); _bars.Remove(c); }
                    continue;
                }
                if (bar == null)
                {
                    bar = new Bar { Material = Shaders.HealthBar() };
                    bar.Node = new MeshInstance3D
                    {
                        Name = $"Health{c.Tx}_{c.Tz}",
                        Mesh = new QuadMesh { Size = new Vector2(Mathf.Clamp(c.Def.Width * 0.9f, 0.9f, 2.4f), 0.14f) },
                        MaterialOverride = bar.Material,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                        Position = new Vector3(c.CentreX, world.Map.HeightAt(c.CentreX, c.CentreZ) + BarHeight(c), c.CentreZ),
                    };
                    AddChild(bar.Node);
                    _bars[c] = bar;
                }
                bar.Material.SetShaderParameter("hp", c.Hp / (float)c.MaxHp);
            }
            _gone.Clear();
            foreach (var c in _bars.Keys)
                if (world.Constructs.At(c.Tx, c.Tz) != c) _gone.Add(c);
            foreach (var c in _gone) { _bars[c].Node.QueueFree(); _bars.Remove(c); }

            PlaceMarker(world);
        }

        private void PlaceMarker(ClientWorld world)
        {
            var hit = world.HasLocal ? world.UnderAttack(world.Local.X, world.Local.Z) : null;
            if (hit == null || _camera == null) { _marker.Visible = false; return; }
            // Over where its numbers rise to (playtest f32ea2e: "-200" sat on "Stone Wall under attack").
            var at = new Vector3(hit.X, world.Map.HeightAt(hit.X, hit.Z) + hit.Top + MarkerHeight, hit.Z);
            bool behind = _camera.IsPositionBehind(at);
            var p = _camera.UnprojectPosition(at);
            var screen = _marker.GetViewportRect().Size;
            bool over = EdgeMarker.Place(p.X, p.Y, behind, screen.X, screen.Y, MarkerMargin, out float x, out float y, out float angle);
            float dx = hit.X - world.Local.X, dz = hit.Z - world.Local.Z;
            _marker.Place(new Vector2(x, y), angle, over, HudText.UnderAttack(hit), HudText.MarkerTag(hit, Mathf.Sqrt(dx * dx + dz * dz)), !hit.Broke);
        }
    }
}
