using System;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.Client
{
    /// <summary>What the crosshair is on in Build mode.</summary>
    public enum BuildAim : byte
    {
        None,
        /// <summary>A tile of the ground: the ghost of the selected recipe stands there.</summary>
        Place,
        /// <summary>Something already built: it can be taken down, or picked up as the recipe (pipette).</summary>
        Construct,
    }

    /// <summary>What Build mode shows right now (doc 09 §2.7): the ghost and why it is red, or the construct looked at.</summary>
    public struct BuildGhost
    {
        public BuildAim Aim;
        /// <summary>The selected recipe.</summary>
        public RecipeDef? Recipe;
        /// <summary>Place: the tile under the crosshair, and the quarter turns of the ghost.</summary>
        public int Tx, Tz;
        public int Rotation;
        /// <summary>Place: <see cref="ClientWorld.PlaceState"/>. Construct: <see cref="ClientWorld.RemoveState"/>.</summary>
        public OpResult State;
        /// <summary>Construct: what the crosshair is on.</summary>
        public Construct? Target;
        /// <summary>Construct: 0..1 of the hold that takes it down.</summary>
        public float HoldProgress;
    }

    /// <summary>
    /// Build mode (doc 09 §2.7), Godot-free: B puts it up and down, the number keys choose a
    /// recipe, R turns the ghost, the attack button builds where the ghost is green, the alt
    /// button held for half a second takes down what the crosshair is on, Q picks that up as the
    /// recipe ("pipette"). The ghost stands on the tile of the ground the view ray meets; what is
    /// built in front of it is looked at instead. It ends by itself when building does (out of
    /// Prep and Delivery) and while the carrier is down.
    /// </summary>
    public sealed class BuildController
    {
        /// <summary>Doc 09 §2.7: "hold on a construct for 0.5 s".</summary>
        public const double RemoveHoldSeconds = 0.5;
        /// <summary>The ghost follows the view along the ground this far; further, or looking up, there is none.</summary>
        public const float MaxAim = 14f;

        private readonly ClientRuntime _client;
        private uint _awaiting;
        private double _hold;
        private Construct? _holdTarget;

        public bool Active { get; private set; }
        public int RecipeIndex { get; private set; }
        /// <summary>Quarter turns of the ghost; each recipe keeps it below its building's rotations.</summary>
        public int Rotation { get; private set; }
        public BuildGhost Ghost { get; private set; }
        /// <summary>Why the last B did not open Build mode; Ok when it did or was not pressed.</summary>
        public OpResult Refused { get; private set; }
        /// <summary>Called with <see cref="Refused"/> when a B is turned down.</summary>
        public Action<OpResult>? OnRefused;

        public BuildController(ClientRuntime client)
        {
            _client = client;
            client.RequestCompleted += (request, _) => { if (request.RequestId == _awaiting) _awaiting = 0; };
        }

        public RecipeDef? Recipe
        {
            get
            {
                var recipes = _client.World?.Content.Recipes;
                return recipes == null || recipes.Count == 0 ? null : recipes[Math.Clamp(RecipeIndex, 0, recipes.Count - 1)];
            }
        }

        /// <summary>The B key. Returns whether Build mode is up after it; see <see cref="Refused"/> for why not.</summary>
        public bool Toggle()
        {
            Refused = OpResult.Ok;
            if (Active)
            {
                Leave();
                return false;
            }
            var world = _client.World;
            if (_client.State != ClientState.Joined || world == null || !world.HasLocal || world.Content.Recipes.Count == 0) return false;
            if (world.Local.Dead) Refused = OpResult.Dead;
            else if (!BuildRules.IsOpen(world.Global.Phase)) Refused = OpResult.WrongPhase;
            if (Refused != OpResult.Ok)
            {
                OnRefused?.Invoke(Refused);
                return false;
            }
            Active = true;
            return true;
        }

        public void Leave()
        {
            Active = false;
            Ghost = default;
            _hold = 0;
            _holdTarget = null;
        }

        /// <summary>A number key, counted from 0 for "1": in Build mode the keys are the recipes. Returns whether it was taken.</summary>
        public bool PressKey(int key)
        {
            var world = _client.World;
            if (!Active || world == null || key < 0 || key >= world.Content.Recipes.Count) return false;
            RecipeIndex = key;
            return true;
        }

        /// <summary>R: a quarter turn more. Returns whether Build mode took the key.</summary>
        public bool Rotate()
        {
            if (!Active) return false;
            var recipe = Recipe;
            int turns = Math.Max(1, recipe?.Building.Rotations ?? 1);
            Rotation = (Rotation + 1) % turns;
            return true;
        }

        /// <summary>Q: the recipe of what the crosshair is on, turned as it stands. Returns whether Build mode took the key.</summary>
        public bool Pipette()
        {
            if (!Active) return false;
            var world = _client.World;
            var target = Ghost.Target;
            if (world == null || Ghost.Aim != BuildAim.Construct || target == null) return true;
            for (int i = 0; i < world.Content.Recipes.Count; i++)
            {
                if (world.Content.Recipes[i].Building != target.Def) continue;
                RecipeIndex = i;
                Rotation = target.Rotation;
            }
            return true;
        }

        /// <summary>
        /// Call once per frame. <paramref name="placePressed"/> is the edge of the attack button,
        /// <paramref name="removeHeld"/> whether the alt button is down; neither counts under a panel.
        /// </summary>
        public void Update(double dt, bool placePressed, bool removeHeld)
        {
            var world = _client.World;
            if (!Active) return;
            if (_client.State != ClientState.Joined || world == null || !world.HasLocal || world.Local.Dead || !BuildRules.IsOpen(world.Global.Phase))
            {
                Leave();
                return;
            }
            var recipe = Recipe!;
            int turns = Math.Max(1, recipe.Building.Rotations);
            var ghost = new BuildGhost { Recipe = recipe, Rotation = Rotation % turns };
            if (world.Paused)
            {
                Ghost = ghost;
                return;
            }
            Aim(world, ref ghost);

            if (ghost.Aim == BuildAim.Construct)
            {
                ghost.State = world.RemoveState(ghost.Target!);
                if (_holdTarget != ghost.Target) _hold = 0;
                _holdTarget = ghost.Target;
                if (removeHeld && ghost.State == OpResult.Ok)
                {
                    // Held on as long as the last request is on its way; it goes when the answer is here.
                    _hold = Math.Min(RemoveHoldSeconds, _hold + dt);
                    if (_hold >= RemoveHoldSeconds && _awaiting == 0)
                    {
                        _hold = 0;
                        _awaiting = _client.RemoveConstruct(ghost.Target!);
                    }
                }
                else _hold = 0;
                ghost.HoldProgress = (float)Math.Min(1.0, _hold / RemoveHoldSeconds);
            }
            else
            {
                _hold = 0;
                _holdTarget = null;
                if (ghost.Aim == BuildAim.Place)
                {
                    ghost.State = world.PlaceState(recipe, ghost.Tx, ghost.Tz);
                    if (placePressed && ghost.State == OpResult.Ok && _awaiting == 0)
                        _awaiting = _client.PlaceConstruct(recipe, ghost.Tx, ghost.Tz, ghost.Rotation);
                }
            }
            Ghost = ghost;
        }

        /// <summary>The construct the view ray meets first, else the tile of the ground it comes down on.</summary>
        private void Aim(ClientWorld world, ref BuildGhost ghost)
        {
            var ray = ViewRay.FromPlayer(world.Local, _client.Input.YawRadians, _client.Input.PitchRadians);
            float ground = ray.DirY < -1e-3f ? (world.Local.Y + SimConstants.EyeHeight) / -ray.DirY : float.MaxValue;
            float best = MathF.Min(ground, MaxAim);
            foreach (var built in world.Constructs.All)
            {
                float t = ray.Hit(AimBox.Built(world.Map, built));
                if (t < 0f || t > best) continue;
                best = t;
                ghost.Aim = BuildAim.Construct;
                ghost.Target = built;
            }
            if (ghost.Aim == BuildAim.Construct || ground > MaxAim) return;
            ghost.Aim = BuildAim.Place;
            ghost.Tx = (int)MathF.Floor((ray.X + ray.DirX * ground) / SimConstants.TileSize);
            ghost.Tz = (int)MathF.Floor((ray.Z + ray.DirZ * ground) / SimConstants.TileSize);
            if (!world.Map.InBounds(ghost.Tx, ghost.Tz)) ghost.Aim = BuildAim.None;
        }
    }
}
