using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.App
{
    /// <summary>
    /// The starting kit in the smoke's first Prep (doc 03 §4.4): the host, at Postal Rank 1, has
    /// the Land kit and no other to pick; as the run starts an Axe and 4 Fiber are in their
    /// pockets, 10 Logs in the chest by the Post Office and a Bike by the counter, and the feed
    /// says so. Then the host does what a carrier would before a round: opens the chest and puts
    /// the Axe and the Fiber in with the Logs, so the pockets are free for the mail.
    /// </summary>
    public partial class SmokeDriver
    {
        private int _kitStep, _kitFrames;
        private bool _atKitChest;
        private Action? _kitTick;

        /// <summary>True while the kit's visit goes on; the Prep's own checks wait for it.</summary>
        private bool StepKit()
        {
            if (_kitStep >= 4) return false;
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var player = server.GetPlayer(world.LocalId)!;
            if (++_kitFrames > 300)
            {
                Fail($"the kit's visit stopped at step {_kitStep}: inventory {world.Inventory.EntryCount} entries, open container {world.OpenContainerId}");
                _kitStep = 4;
                return false;
            }
            var land = world.Content.DefaultKit;
            int Count(Sim.Inventory.GridContainer c, string item) =>
                c.Entries.Where(e => e.Stack.Kind == StackKind.Item && world.Content.Items[e.Stack.DefIndex].Id == item).Sum(e => e.Stack.Count);
            switch (_kitStep)
            {
                case 0:
                {
                    if (client.PendingRequestCount > 0 || world.Inventory.EntryCount < land.Personal.Count) return true;
                    Check(Count(world.Inventory, "axe") == 1 && Count(world.Inventory, "fiber") == 4, $"the Land kit put {Count(world.Inventory, "axe")} Axe and {Count(world.Inventory, "fiber")} Fiber in the pockets");
                    Check(Count(server.MapChests[0], "log") == 10, $"the team's kit put {Count(server.MapChests[0], "log")} Logs in the chest by the Post Office");
                    Check(server.Vehicles.Count == 1 && world.Vehicles.Count == 1, $"the team's kit parked {server.Vehicles.Count} bikes ({world.Vehicles.Count} seen)");
                    Check(_session.Feed.Entries.Any(e => e.Text == "Your Land kit is in your pockets: Axe, Fiber ×4"), "the feed does not say what the kit put in the pockets");
                    Check(_session.Feed.Entries.Any(e => e.Text == "The team's kit (your Land kit): Log ×10 in the chest by the Post Office   ·   a Bike by the counter"),
                        "the feed does not say what the team's kit gave");
                    // The host walks to the chest; the bot, which shuts whatever is open outside Delivery, waits.
                    client.TickStarting -= _botTick;
                    var chest = world.Map.Chests[0];
                    float standX = chest.X - MathF.Sin(chest.Yaw) * 1.4f, standZ = chest.Z - MathF.Cos(chest.Yaw) * 1.4f;
                    _atKitChest = false;
                    _kitTick = () =>
                    {
                        client.Input.MoveX = 0f;
                        client.Input.MoveZ = 0f;
                        client.Input.Buttons = Sim.Players.InputButtons.None;
                        if (!_atKitChest) _atKitChest = _bot.WalkTo(world, standX, standZ, 0.3f);
                    };
                    client.TickStarting += _kitTick;
                    _kitStep = 1;
                    _kitFrames = 0;
                    return true;
                }
                case 1:
                {
                    if (!_atKitChest) return true;
                    client.OpenContainer(world.ChestContainerIds[0]);
                    _kitStep = 2;
                    return true;
                }
                case 2:
                {
                    if (world.OpenContainerId != world.ChestContainerIds[0] || client.PendingRequestCount > 0) return true;
                    var entry = world.Inventory.Entries.FirstOrDefault(e => e.Stack.Kind == StackKind.Item);
                    if (entry != null) { client.QuickMove(world.Inventory.Id, entry.EntryId); return true; }
                    Check(Count(server.MapChests[0], "axe") == 1 && Count(server.MapChests[0], "fiber") == 4, "the kit's Axe and Fiber did not go into the chest");
                    client.CloseContainer();
                    _kitStep = 3;
                    return true;
                }
                default:
                {
                    if (world.OpenContainerId != 0 || client.PendingRequestCount > 0) return true;
                    // The round starts from the chest: the bot walks from here.
                    client.TickStarting -= _kitTick;
                    client.TickStarting += _botTick;
                    GD.Print("[smoke] kit: the Land kit's Axe and Fiber ×4 in the pockets, Log ×10 in the chest and a Bike by the counter; the Axe and the Fiber put in the chest");
                    _kitStep = 4;
                    return false;
                }
            }
        }
    }
}
