using System.Collections.Generic;
using Godot;
using PerformativeMail.Sim.Core;

namespace PerformativeMail.Client.Presentation
{
    /// <summary>
    /// Keeps one PlayerView per known player. The local player's (bodiless) view follows the
    /// predicted state and carries the camera; the others are drawn 100 ms in the past,
    /// interpolated between snapshots (doc 06 §3).
    /// </summary>
    public partial class PlayersView : Node3D
    {
        /// <summary>The local view chases the 30 Hz predicted position at this rate so frames between ticks stay smooth.</summary>
        private const float LocalFollowRate = 30f;
        private const float SnapDistance = 3f;

        public PlayerView? LocalView { get; private set; }
        public int ViewCount => _views.Count;

        private readonly Dictionary<EntityId, PlayerView> _views = new Dictionary<EntityId, PlayerView>();
        private readonly List<EntityId> _gone = new List<EntityId>();
        private ClientRuntime _client = null!;

        public void Setup(ClientRuntime client)
        {
            _client = client;
            Sync(0.0);
        }

        public override void _Process(double delta) => Sync(delta);

        /// <summary>Where a player's feet are drawn right now; false for a player without a view.</summary>
        public bool TryGetPosition(EntityId id, out Vector3 position)
        {
            bool known = _views.TryGetValue(id, out var view);
            position = known ? view!.Position : Vector3.Zero;
            return known;
        }

        /// <summary>The name tag over a player's head as drawn right now; empty without one.</summary>
        public string NameTagOf(EntityId id) => _views.TryGetValue(id, out var view) ? view.NameTagText : "";

        private void Sync(double delta)
        {
            var world = _client?.World;
            if (world == null) return;

            foreach (var player in world.Players.Values)
            {
                bool local = player.Id == world.LocalId;
                // Ids are used again (nit 11): the view of one who left is not the next one's.
                if (_views.TryGetValue(player.Id, out var view) && view.Carrier != player)
                {
                    RemoveChild(view);
                    view.QueueFree();
                    _views.Remove(player.Id);
                    view = null;
                }
                if (view == null)
                {
                    view = new PlayerView { Name = $"Player{player.Id.Counter}", Carrier = player };
                    view.Setup(player.Name, player.Slot, local);
                    AddChild(view);
                    _views[player.Id] = view;
                    if (local)
                    {
                        LocalView = view;
                        view.Position = new Vector3(world.Local.X, world.Local.Y, world.Local.Z);
                    }
                }

                if (local)
                {
                    var target = new Vector3(world.Local.X, world.Local.Y, world.Local.Z);
                    float t = 1f - Mathf.Exp(-LocalFollowRate * (float)delta);
                    view.Position = view.Position.DistanceTo(target) > SnapDistance ? target : view.Position.Lerp(target, t);
                }
                else if (player.Interpolate(world.ServerTick - ClientRuntime.InterpolationDelayTicks, out float x, out float y, out float z, out float yaw))
                {
                    view.Show(player, new Vector3(x, y, z), yaw, (float)delta, world.Content);
                }
            }

            if (_views.Count == world.Players.Count) return;
            _gone.Clear();
            foreach (var id in _views.Keys)
                if (!world.Players.ContainsKey(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                _views[id].QueueFree();
                _views.Remove(id);
            }
        }
    }
}
