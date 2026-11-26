using System;
using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// What a fight looks like in the concept video, played from the server's events: the package
    /// leaving the cannon, the orange blast with its smoke and embers, the spark of a hammer that
    /// connects, the dust where an enemy fell, and the glowing coins it left, which fly to whoever
    /// walks up to them. Nothing here feeds back into the game.
    /// </summary>
    public partial class CombatEffects : Node3D
    {
        private const float CoinFlySeconds = 0.4f;
        private const double BagHurrySeconds = 8.0;
        private static readonly Color Gold = new Color("ffc93c");
        private static readonly Color Fire = new Color("ff8a2a");

        private struct Particle
        {
            public Node3D Node;
            public Vector3 Velocity;
            public float Age, Life, Gravity, Drag, StartScale, EndScale;
        }

        private sealed class Shot
        {
            public Node3D Node = null!;
            public Vector3 Velocity;
            public float Age, Life, Trail;
        }

        private sealed class Flash
        {
            public MeshInstance3D Node = null!;
            public StandardMaterial3D Material = null!;
            public OmniLight3D? Light;
            public float Age, Life, Radius;
        }

        private sealed class LootView
        {
            public Node3D Root = null!;
            public Node3D Coins = null!;
            public int CoinCount, ItemCount;
            public float Age;
        }

        private sealed class FlyingCoin
        {
            public Node3D Node = null!;
            public Vector3 From;
            public EntityId To;
            public float Age, Delay;
        }

        /// <summary>Blasts played so far (the smoke test looks for the one it fired).</summary>
        public int Blasts { get; private set; }
        /// <summary>Those of them that hurt at least one raider.</summary>
        public int BlastsThatHit { get; private set; }
        public int LootViewCount => _loot.Count;
        /// <summary>Raiders' arrows drawn so far, and in the air right now (doc 05 §2 Archer).</summary>
        public int ArrowsDrawn { get; private set; }
        public int ArrowsFlying => _arrows.Count;

        /// <summary>An arrow from an Archer, flying to where the server says it struck.</summary>
        private sealed class Arrow
        {
            public Node3D Node = null!;
            public Vector3 From, To;
            public float Age, Life;
        }

        private const float ArrowSpeed = 32f;
        private readonly List<Arrow> _arrows = new List<Arrow>();
        /// <summary>Death bags drawn in the world right now.</summary>
        public int BagViewCount => _bags.Count;

        private readonly List<Particle> _particles = new List<Particle>();
        private readonly List<Shot> _shots = new List<Shot>();
        private readonly List<Flash> _flashes = new List<Flash>();
        private readonly List<FlyingCoin> _flying = new List<FlyingCoin>();
        private readonly Dictionary<uint, LootView> _loot = new Dictionary<uint, LootView>();
        private readonly Dictionary<uint, Node3D> _bags = new Dictionary<uint, Node3D>();
        private readonly List<uint> _gone = new List<uint>();
        private readonly Random _random = new Random(7);
        private ClientRuntime _client = null!;
        private PlayersView _players = null!;
        private float _cannonSpeed, _cannonRange, _blastRadius, _hammerRange;

        public void Setup(ClientRuntime client, PlayersView players)
        {
            _client = client;
            _players = players;
            ContentReloaded();
            client.EventReceived += OnEvent;
        }

        /// <summary>The balance's numbers for the shots and swings drawn; read again after a hot reload.</summary>
        public void ContentReloaded()
        {
            var balance = _client.World!.Content.Balance;
            _cannonSpeed = balance.GetFloat("cannonSpeed");
            _cannonRange = balance.GetFloat("cannonRange");
            _blastRadius = balance.GetFloat("cannonBlastRadius");
            _hammerRange = balance.GetFloat("hammerRange");
        }

        public override void _ExitTree()
        {
            if (_client != null) _client.EventReceived -= OnEvent;
        }

        private void OnEvent(SimEvent ev)
        {
            switch (ev)
            {
                case AttackEvent e:
                {
                    float yaw = PlayerInput.YawToRadians(e.Yaw);
                    var forward = new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
                    var from = new Vector3(e.X, 0f, e.Z);
                    if (e.Weapon == Weapon.Cannon) Shoot(from + forward * 0.7f + Vector3.Up * 1.25f, forward);
                    else if (e.Hits > 0) Spark(from + forward * (_hammerRange * 0.6f) + Vector3.Up * 1.0f);
                    break;
                }
                case BlastEvent e:
                    Blast(new Vector3(e.X, 0.7f, e.Z));
                    if (e.Hits > 0) BlastsThatHit++;
                    break;
                case EnemyShotEvent e when e.Kind == EnemyShotKind.Arrow:
                    Loose(new Vector3(e.FromX, Height(e.FromX, e.FromZ) + 1.45f, e.FromZ), new Vector3(e.ToX, Height(e.ToX, e.ToZ) + (e.AtConstruct ? 0.9f : 1.25f), e.ToZ));
                    break;
                case EnemyDiedEvent e:
                    Puff(new Vector3(e.X, 0.8f, e.Z), e.Mega ? 9 : 5, new Color("b9a892"), 0.45f);
                    break;
                case LootTakenEvent e:
                    if (e.Coins > 0 && _loot.TryGetValue(e.LootId, out var loot)) FlyCoins(loot.Root.Position, e.Player, e.Coins);
                    break;
            }
        }

        public override void _Process(double delta)
        {
            var world = _client?.World;
            if (world == null) return;
            float dt = (float)delta;
            SyncLoot(world, dt);
            SyncBags(world);
            StepShots(dt);
            StepArrows(dt);
            StepFlashes(dt);
            StepParticles(dt);
            StepCoins(dt);
        }

        // --- arrows -------------------------------------------------------------------------------

        private float Height(float x, float z) => _client.World?.Map.HeightAt(x, z) ?? 0f;

        private void Loose(Vector3 from, Vector3 to)
        {
            var arrow = new Node3D { Name = "Arrow" };
            var wood = MeshKit.Mat(new Color("8a5a32"), 0.7f);
            arrow.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(0.03f, 0.03f, 0.75f)), wood, Vector3.Zero));
            arrow.AddChild(MeshKit.Instance(MeshKit.Cone(0.035f, 0.1f, 6), MeshKit.Mat(new Color("8b9099"), 0.4f, 0.7f), new Vector3(0f, 0f, -0.42f), new Vector3(-90f, 0f, 0f)));
            arrow.AddChild(MeshKit.Instance(MeshKit.Box(new Vector3(0.01f, 0.08f, 0.14f)), MeshKit.Mat(new Color("e9dfc8"), 0.7f), new Vector3(0f, 0f, 0.33f)));
            arrow.Position = from;
            AddChild(arrow);
            _arrows.Add(new Arrow { Node = arrow, From = from, To = to, Life = Mathf.Max(0.12f, from.DistanceTo(to) / ArrowSpeed) });
            ArrowsDrawn++;
        }

        private void StepArrows(float dt)
        {
            for (int i = _arrows.Count - 1; i >= 0; i--)
            {
                var a = _arrows[i];
                a.Age += dt;
                if (a.Age >= a.Life)
                {
                    Spark(a.To);
                    a.Node.QueueFree();
                    _arrows.RemoveAt(i);
                    continue;
                }
                // A low arc, pointing along its flight.
                float t = a.Age / a.Life, rise = a.From.DistanceTo(a.To) * 0.06f;
                var at = a.From.Lerp(a.To, t) + Vector3.Up * (4f * rise * t * (1f - t));
                var ahead = a.From.Lerp(a.To, Mathf.Min(1f, t + 0.05f)) + Vector3.Up * (4f * rise * (t + 0.05f) * (1f - t - 0.05f));
                a.Node.Position = at;
                if (ahead.DistanceSquaredTo(at) > 1e-6f) a.Node.LookAt(ahead, Vector3.Up);
            }
        }

        // --- the cannon -------------------------------------------------------------------------

        private void Shoot(Vector3 from, Vector3 forward)
        {
            var parcel = WeaponModels.Parcel(0.3f);
            parcel.Position = from;
            AddChild(parcel);
            _shots.Add(new Shot { Node = parcel, Velocity = forward * _cannonSpeed, Life = _cannonRange / _cannonSpeed + 0.1f });
            // The muzzle's cough.
            Puff(from, 3, new Color("e9e2d6"), 0.2f);
        }

        private void StepShots(float dt)
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var shot = _shots[i];
                shot.Age += dt;
                shot.Node.Position += shot.Velocity * dt;
                shot.Node.Rotation += new Vector3(9f, 4f, 0f) * dt;
                if ((shot.Trail -= dt) <= 0f)
                {
                    shot.Trail = 0.035f;
                    Emit(MeshKit.Sphere(0.5f, 8), MeshKit.Mat(new Color("e9e2d6"), 1f), shot.Node.Position, Vector3.Up * 0.4f, 0.45f, 0.18f, 0.02f, 0f, 2f);
                }
                if (shot.Age < shot.Life) continue;
                shot.Node.QueueFree();
                _shots.RemoveAt(i);
            }
        }

        private void Blast(Vector3 at)
        {
            Blasts++;
            // The package that got there is the one closest to where the server says it went off.
            int nearest = -1;
            float best = 36f;
            for (int i = 0; i < _shots.Count; i++)
            {
                float d = _shots[i].Node.Position.DistanceSquaredTo(at);
                if (d >= best) continue;
                best = d;
                nearest = i;
            }
            if (nearest >= 0)
            {
                _shots[nearest].Node.QueueFree();
                _shots.RemoveAt(nearest);
            }

            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(1f, 0.72f, 0.3f, 0.9f),
            };
            var ball = MeshKit.Instance(MeshKit.Sphere(1f, 20), material, at);
            ball.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            AddChild(ball);
            var flash = new Flash { Node = ball, Material = material, Life = 0.28f, Radius = _blastRadius };
            if (!WorldView.Cheap)
            {
                flash.Light = new OmniLight3D { LightColor = Fire, LightEnergy = 7f, OmniRange = _blastRadius * 5f, Position = at + Vector3.Up * 0.5f };
                AddChild(flash.Light);
            }
            _flashes.Add(flash);

            int smoke = WorldView.Cheap ? 4 : 8, embers = WorldView.Cheap ? 6 : 16;
            var dark = MeshKit.Mat(new Color("3a3540"), 1f);
            for (int i = 0; i < smoke; i++)
            {
                var push = Ring(1.6f) + Vector3.Up * Range(1.2f, 3f);
                Emit(MeshKit.Blob(0.5f, 0.22f, 2.4f, 1 + i % 3, Vector3.One, 6), dark, at + Ring(0.5f), push, Range(1.1f, 1.8f), 0.5f, Range(1.6f, 2.4f), -0.4f, 1.6f);
            }
            var ember = MeshKit.Glow(Fire, 3.5f);
            for (int i = 0; i < embers; i++)
                Emit(MeshKit.Sphere(0.06f, 8), ember, at, Ring(Range(2.5f, 6.5f)) + Vector3.Up * Range(3f, 7.5f), Range(0.7f, 1.3f), 1f, 0.3f, 13f, 0.3f);
        }

        private void StepFlashes(float dt)
        {
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var flash = _flashes[i];
                flash.Age += dt;
                float t = Mathf.Clamp(flash.Age / flash.Life, 0f, 1f);
                flash.Node.Scale = Vector3.One * Mathf.Lerp(0.25f, 1f, Mathf.Ease(t, 0.4f)) * flash.Radius;
                var colour = flash.Material.AlbedoColor;
                colour.A = 0.9f * (1f - t) * (1f - t);
                flash.Material.AlbedoColor = colour;
                if (flash.Light != null) flash.Light.LightEnergy = 7f * (1f - t);
                if (t < 1f) continue;
                flash.Node.QueueFree();
                flash.Light?.QueueFree();
                _flashes.RemoveAt(i);
            }
        }

        // --- hits and falls ---------------------------------------------------------------------

        private void Spark(Vector3 at)
        {
            var spark = MeshKit.Glow(new Color("ffe9a8"), 3f);
            for (int i = 0; i < (WorldView.Cheap ? 4 : 8); i++)
                Emit(MeshKit.Sphere(0.045f, 8), spark, at, Ring(Range(1.5f, 4f)) + Vector3.Up * Range(0.5f, 3.5f), Range(0.25f, 0.45f), 1f, 0.2f, 9f, 0.5f);
            Emit(MeshKit.Sphere(0.5f, 12), spark, at, Vector3.Zero, 0.12f, 0.3f, 0.7f, 0f, 0f);
        }

        private void Puff(Vector3 at, int count, Color colour, float size)
        {
            var material = MeshKit.Mat(colour, 1f);
            for (int i = 0; i < count; i++)
                Emit(MeshKit.Sphere(0.5f, 10), material, at + Ring(size * 0.6f), Ring(0.9f) + Vector3.Up * Range(0.4f, 1.3f), Range(0.5f, 0.9f), size, size * 2.2f, -0.2f, 1.8f);
        }

        /// <summary>One mesh that flies, grows or shrinks from one size to another, and is gone at the end of its life.</summary>
        private void Emit(Mesh mesh, Material material, Vector3 at, Vector3 velocity, float life, float startScale, float endScale, float gravity, float drag)
        {
            var node = MeshKit.Instance(mesh, material, at);
            node.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            node.Scale = Vector3.One * startScale;
            AddChild(node);
            _particles.Add(new Particle { Node = node, Velocity = velocity, Life = life, Gravity = gravity, Drag = drag, StartScale = startScale, EndScale = endScale });
        }

        private void StepParticles(float dt)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    p.Node.QueueFree();
                    _particles.RemoveAt(i);
                    continue;
                }
                p.Velocity.Y -= p.Gravity * dt;
                p.Velocity *= Mathf.Exp(-p.Drag * dt);
                var position = p.Node.Position + p.Velocity * dt;
                // Embers bounce once and lie still.
                if (position.Y < 0.03f && p.Gravity > 0f)
                {
                    position.Y = 0.03f;
                    p.Velocity = new Vector3(p.Velocity.X * 0.4f, -p.Velocity.Y * 0.3f, p.Velocity.Z * 0.4f);
                }
                p.Node.Position = position;
                float t = p.Age / p.Life;
                // Grows to its size, then shrinks away over the last third.
                float fade = 1f - Mathf.SmoothStep(0.66f, 1f, t);
                p.Node.Scale = Vector3.One * Mathf.Max(0.001f, Mathf.Lerp(p.StartScale, p.EndScale, Mathf.Ease(t, 0.5f)) * fade);
                _particles[i] = p;
            }
        }

        // --- loot -------------------------------------------------------------------------------

        private void SyncLoot(ClientWorld world, float dt)
        {
            foreach (var loot in world.Loot.Values)
            {
                if (!_loot.TryGetValue(loot.Id, out var view))
                {
                    view = new LootView { Root = new Node3D { Name = $"Loot{loot.Id}", Position = new Vector3(loot.X, 0f, loot.Z) }, CoinCount = -1 };
                    AddChild(view.Root);
                    _loot[loot.Id] = view;
                }
                if (view.CoinCount != loot.Coins || view.ItemCount != loot.Items.Count) Rebuild(view, loot);
                view.Age += dt;
                // Out of the body with a hop, then turning and bobbing in place.
                float pop = Mathf.Clamp(view.Age / 0.35f, 0f, 1f);
                view.Root.Scale = Vector3.One * Mathf.Max(0.001f, Mathf.Ease(pop, 0.4f));
                view.Coins.Position = new Vector3(0f, 0.45f + Mathf.Sin(pop * Mathf.Pi) * 0.5f + Mathf.Sin(view.Age * 2.4f) * 0.07f, 0f);
                view.Coins.Rotation = new Vector3(0f, view.Age * 2.2f, 0f);
            }
            if (_loot.Count == world.Loot.Count) return;
            _gone.Clear();
            foreach (uint id in _loot.Keys)
                if (!world.Loot.ContainsKey(id)) _gone.Add(id);
            foreach (uint id in _gone)
            {
                _loot[id].Root.QueueFree();
                _loot.Remove(id);
            }
        }

        private void Rebuild(LootView view, ClientLoot loot)
        {
            foreach (var child in view.Root.GetChildren()) child.QueueFree();
            view.CoinCount = loot.Coins;
            view.ItemCount = loot.Items.Count;
            view.Coins = new Node3D { Name = "Coins" };
            view.Root.AddChild(view.Coins);
            float size = loot.Mega ? 1.35f : 1f;
            int coins = loot.Coins <= 0 ? 0 : Math.Clamp(1 + loot.Coins / 4, 1, 5);
            for (int i = 0; i < coins; i++)
            {
                float a = i * Mathf.Tau / Math.Max(1, coins);
                var at = coins == 1 ? Vector3.Zero : new Vector3(Mathf.Cos(a) * 0.28f, (i % 2) * 0.12f, Mathf.Sin(a) * 0.28f);
                var coin = Coin(size);
                coin.Position = at;
                coin.Rotation = new Vector3(0f, a, 0f);
                view.Coins.AddChild(coin);
            }
            if (loot.Items.Count == 0) return;
            var parcel = WeaponModels.Parcel(0.34f * size);
            parcel.Position = new Vector3(0f, 0.13f * size, 0f);
            parcel.Rotation = new Vector3(0f, loot.Id * 1.3f, 0f);
            view.Root.AddChild(parcel);
        }

        // --- death bags -------------------------------------------------------------------------

        private void SyncBags(ClientWorld world)
        {
            foreach (var bag in world.Bags.Values)
            {
                if (!_bags.TryGetValue(bag.ContainerId, out var view))
                {
                    view = Satchel();
                    view.Name = $"Bag{bag.ContainerId}";
                    view.Position = new Vector3(bag.X, world.Map.HeightAt(bag.X, bag.Z), bag.Z);
                    view.Rotation = new Vector3(0f, bag.ContainerId * 1.9f, 0f);
                    AddChild(view);
                    _bags[bag.ContainerId] = view;
                }
                // The last seconds it throbs: whoever means to fetch it has to go now.
                double left = world.BagSecondsLeft(bag);
                float throb = left < BagHurrySeconds ? 1f + 0.08f * Mathf.Sin((float)left * Mathf.Tau * 2f) : 1f;
                view.Scale = Vector3.One * throb;
            }
            if (_bags.Count == world.Bags.Count) return;
            _gone.Clear();
            foreach (uint id in _bags.Keys)
                if (!world.Bags.ContainsKey(id)) _gone.Add(id);
            foreach (uint id in _gone)
            {
                Puff(_bags[id].Position + Vector3.Up * 0.3f, 4, new Color("b9a892"), 0.3f);
                _bags[id].QueueFree();
                _bags.Remove(id);
            }
        }

        /// <summary>A carrier's satchel lying on its back: body, flap, buckle and the strap beside it.</summary>
        private static Node3D Satchel()
        {
            var root = new Node3D();
            var leather = MeshKit.Mat(new Color("7a4a2b"), 0.8f);
            var flap = MeshKit.Mat(new Color("5e3620"), 0.75f);
            var brass = MeshKit.Mat(new Color("d9a441"), 0.35f, 0.8f);
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.62f, 0.3f, 0.46f), 0.1f), leather, new Vector3(0f, 0.15f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.66f, 0.07f, 0.34f), 0.03f), flap, new Vector3(0f, 0.3f, -0.07f), new Vector3(-6f, 0f, 0f)));
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.1f, 0.04f, 0.08f), 0.015f), brass, new Vector3(0f, 0.325f, 0.08f)));
            root.AddChild(MeshKit.Instance(MeshKit.Torus(0.2f, 0.24f), leather, new Vector3(0.36f, 0.03f, 0.1f)));
            // An envelope half out of it: the bag is where the mail is.
            root.AddChild(MeshKit.Instance(MeshKit.RoundedBox(new Vector3(0.3f, 0.02f, 0.2f), 0.008f), MeshKit.Mat(new Color("f3ead6"), 0.9f), new Vector3(-0.12f, 0.31f, 0.2f), new Vector3(14f, 20f, 0f)));
            return root;
        }

        /// <summary>A glowing gold disc on its edge, as on the path in the video.</summary>
        private static Node3D Coin(float size)
        {
            var coin = new Node3D { Name = "Coin" };
            var disc = MeshKit.Instance(MeshKit.Cylinder(0.15f * size, 0.15f * size, 0.035f * size, 18), MeshKit.Glow(Gold, 1.6f), Vector3.Zero, new Vector3(90f, 0f, 0f));
            disc.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            coin.AddChild(disc);
            return coin;
        }

        private void FlyCoins(Vector3 from, EntityId to, int coins)
        {
            int count = Math.Clamp(1 + coins / 4, 1, 6);
            for (int i = 0; i < count; i++)
            {
                var coin = Coin(0.7f);
                coin.Position = from + Vector3.Up * 0.5f;
                coin.Visible = false;
                AddChild(coin);
                _flying.Add(new FlyingCoin { Node = coin, From = from + Vector3.Up * 0.5f + Ring(0.25f), To = to, Delay = i * 0.05f });
            }
        }

        private void StepCoins(float dt)
        {
            for (int i = _flying.Count - 1; i >= 0; i--)
            {
                var coin = _flying[i];
                if ((coin.Delay -= dt) > 0f) continue;
                coin.Age += dt;
                float t = Mathf.Clamp(coin.Age / CoinFlySeconds, 0f, 1f);
                if (t >= 1f || !_players.TryGetPosition(coin.To, out var feet))
                {
                    coin.Node.QueueFree();
                    _flying.RemoveAt(i);
                    continue;
                }
                // Up in an arc and into the satchel.
                var to = feet + Vector3.Up * 1.0f;
                var at = coin.From.Lerp(to, t * t);
                at.Y += Mathf.Sin(t * Mathf.Pi) * 0.6f;
                coin.Node.Visible = true;
                coin.Node.Position = at;
                coin.Node.Rotation = new Vector3(0f, coin.Age * 14f, 0f);
                coin.Node.Scale = Vector3.One * (1f - 0.5f * t);
            }
        }

        // --- chance -----------------------------------------------------------------------------

        private float Range(float from, float to) => from + (float)_random.NextDouble() * (to - from);

        /// <summary>A random horizontal direction, <paramref name="length"/> long.</summary>
        private Vector3 Ring(float length)
        {
            float a = Range(0f, Mathf.Tau);
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * length;
        }
    }
}
