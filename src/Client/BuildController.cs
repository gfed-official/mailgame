using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Place, while a drag is held (doc 04 §1.1): the tiles of the line from where it began to
        /// the one under the crosshair, and what each would come to; <see cref="Rotation"/> is the
        /// line's. Null with no drag under way.
        /// </summary>
        public List<(int Tx, int Tz, OpResult State)>? Line;

        /// <summary>Tiles of the line that would be built.</summary>
        public int LineOk
        {
            get
            {
                int n = 0;
                if (Line != null) foreach (var t in Line) if (t.State == OpResult.Ok) n++;
                return n;
            }
        }
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
        /// <summary>
        /// What the server would not let be taken down (a chest with something in it, which the
        /// client cannot see shut): the button is let go before it is asked again.
        /// </summary>
        private Construct? _refusedTarget;
        private uint _removing;
        /// <summary>A drag line under way: the tile it began on.</summary>
        private bool _dragging;
        private int _dragX, _dragZ;
        private readonly List<(int, int, OpResult)> _line = new List<(int, int, OpResult)>();

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
            client.RequestCompleted += (request, result) =>
            {
                if (request.RequestId != _awaiting) return;
                if (request.RequestId == _removing && result != OpResult.Ok) _refusedTarget = _holdTarget;
                _awaiting = _removing = 0;
            };
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
            _dragging = false;
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
        /// <paramref name="placeHeld"/> whether it is still down (a drag line for belts and walls,
        /// laid when it is let go), <paramref name="removeHeld"/> whether the alt button is down;
        /// none counts under a panel.
        /// </summary>
        public void Update(double dt, bool placePressed, bool removeHeld, bool placeHeld = false)
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
                _dragging = false;
                Ghost = ghost;
                return;
            }
            Aim(world, ref ghost);
            // A drag stays on the ground it began on, whatever the crosshair passes over.
            if (_dragging && ghost.Aim == BuildAim.Construct) AimGround(world, ref ghost);

            if (ghost.Aim == BuildAim.Construct)
            {
                _dragging = false;
                ghost.State = world.RemoveState(ghost.Target!);
                if (_holdTarget != ghost.Target) _hold = 0;
                _holdTarget = ghost.Target;
                if (!removeHeld || _refusedTarget != ghost.Target) _refusedTarget = null;
                if (removeHeld && ghost.State == OpResult.Ok && _refusedTarget == null)
                {
                    // Held on as long as the last request is on its way; it goes when the answer is here.
                    _hold = Math.Min(RemoveHoldSeconds, _hold + dt);
                    if (_hold >= RemoveHoldSeconds && _awaiting == 0)
                    {
                        _hold = 0;
                        _awaiting = _removing = _client.RemoveConstruct(ghost.Target!);
                    }
                }
                else _hold = 0;
                ghost.HoldProgress = (float)Math.Min(1.0, _hold / RemoveHoldSeconds);
            }
            else
            {
                _hold = 0;
                _holdTarget = _refusedTarget = null;
                if (ghost.Aim == BuildAim.Place)
                {
                    ghost.State = world.PlaceState(recipe, ghost.Tx, ghost.Tz);
                    if (recipe.Building.Drags) Drag(world, recipe, ref ghost, placePressed, placeHeld);
                    else if (placePressed && ghost.State == OpResult.Ok && _awaiting == 0)
                        _awaiting = _client.PlaceConstruct(recipe, ghost.Tx, ghost.Tz, ghost.Rotation);
                }
                else _dragging = false;
            }
            Ghost = ghost;
        }

        /// <summary>
        /// Doc 04 §1.1 drag placement: the press begins the line on the tile under the crosshair, it
        /// runs along the row or the column the crosshair is further along, and letting go lays it —
        /// one PlaceLine request, a belt turned the way the line was drawn, a wall along it. A press
        /// let go on the tile it began on builds that one tile, as for anything else.
        /// </summary>
        private void Drag(ClientWorld world, RecipeDef recipe, ref BuildGhost ghost, bool pressed, bool held)
        {
            if (pressed && !_dragging)
            {
                _dragging = true;
                _dragX = ghost.Tx;
                _dragZ = ghost.Tz;
            }
            if (!_dragging) return;
            int dx = ghost.Tx - _dragX, dz = ghost.Tz - _dragZ;
            int ex = _dragX, ez = _dragZ;
            if (Math.Abs(dx) >= Math.Abs(dz)) ex += Math.Clamp(dx, -(BuildRules.MaxLine - 1), BuildRules.MaxLine - 1);
            else ez += Math.Clamp(dz, -(BuildRules.MaxLine - 1), BuildRules.MaxLine - 1);
            bool single = ex == _dragX && ez == _dragZ;
            int rotation = ghost.Rotation;
            if (!single)
            {
                int dir = ex > _dragX ? 0 : ez > _dragZ ? 1 : ex < _dragX ? 2 : 3;
                // A belt runs the way it was drawn; a wall stands along the line.
                rotation = recipe.Building.Carries ? dir : (dir & 1) % Math.Max(1, recipe.Building.Rotations);
            }
            _line.Clear();
            world.LineStates(recipe, _dragX, _dragZ, ex, ez, _line);
            ghost.Line = new List<(int, int, OpResult)>(_line);
            ghost.Rotation = rotation;
            ghost.Tx = _dragX;
            ghost.Tz = _dragZ;
            ghost.State = _line.Count > 0 ? _line[0].Item3 : OpResult.BadRequest;
            if (held) return;
            _dragging = false;
            if (_awaiting != 0) return;
            if (single)
            {
                if (ghost.State == OpResult.Ok) _awaiting = _client.PlaceConstruct(recipe, _dragX, _dragZ, rotation);
            }
            else _awaiting = _client.PlaceLine(recipe, _dragX, _dragZ, ex, ez, rotation);
            Rotation = rotation;
        }

        /// <summary>The tile of the ground the view ray comes down on, whatever stands on it.</summary>
        private void AimGround(ClientWorld world, ref BuildGhost ghost)
        {
            var ray = ViewRay.FromPlayer(world.Local, _client.Input.YawRadians, _client.Input.PitchRadians);
            float ground = ray.DirY < -1e-3f ? (world.Local.Y + SimConstants.EyeHeight) / -ray.DirY : float.MaxValue;
            ghost.Target = null;
            ghost.Aim = BuildAim.None;
            if (ground > MaxAim * 1.5f) return;
            ghost.Aim = BuildAim.Place;
            ghost.Tx = (int)MathF.Floor((ray.X + ray.DirX * ground) / SimConstants.TileSize);
            ghost.Tz = (int)MathF.Floor((ray.Z + ray.DirZ * ground) / SimConstants.TileSize);
            if (!world.Map.InBounds(ghost.Tx, ghost.Tz)) ghost.Aim = BuildAim.None;
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
