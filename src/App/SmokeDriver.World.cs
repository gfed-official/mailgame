using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.World;

namespace PerformativeMail.App
{
    /// <summary>
    /// The host's world in the Lobby after the run (doc 01 §5.3, doc 06 §5, doc 09 §2.2): the
    /// Lobby's plates name the world, its seed and the kit, and the host's keys; the world key
    /// puts the game on the seed's Small Island — the screen is built anew for it, the host
    /// stands on its pad —, the seed key draws another town, and the host goes back to the test
    /// street from the smoke's seed for the stages that follow.
    /// </summary>
    public partial class SmokeDriver
    {
        private int _worldStep, _worldFrames;
        private ulong _worldHash;
        private uint _worldSeed;
        private ulong _worldHashStreet;
        private string _worldIslandText = "";

        private void StepWorld()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            // What a player's own frames do: the prompt is of what is in view now.
            _session.Interaction.Update(1.0 / 30.0, false, false);
            hud.Refresh();
            if (++_worldFrames > 240)
            {
                Fail($"the world's visit stopped at step {_worldStep}: the host has {world.Map.Id} #{world.Seed}, the server {server.Map.Id} #{server.Settings.Seed}");
                StartInventory();
                return;
            }
            bool settled = client.PendingRequestCount == 0 && world.Map.MapHash == server.Map.MapHash && _screen.World.Map == world.Map && _worldFrames > 3;
            switch (_worldStep)
            {
                case 0:
                {
                    _worldHash = _worldHashStreet = world.Map.MapHash;
                    _worldSeed = server.Settings.Seed;
                    string want = $"Test Street (M0) · {HudText.SeedString("test_street", server.Settings.Seed)}";
                    Check(hud.WorldText == want, $"in the Lobby the world plate reads \"{hud.WorldText}\", not \"{want}\"");
                    Check(hud.KitText == "Kit: Land   ·   [K]", $"in the Lobby the kit plate reads \"{hud.KitText}\"");
                    Check(hud.WorldKeysText == HudText.WorldKeys, $"the host's keys plate reads \"{hud.WorldKeysText}\"");
                    Check(client.CycleWorld() != 0, "the host's world key asked for nothing");
                    _worldStep = 1;
                    _worldFrames = 0;
                    break;
                }
                case 1:
                {
                    if (world.Map.Id != WorldGenerator.SmallIsland || !settled) break;
                    // The prediction comes to the new pad with the next snapshots.
                    world.Map.PostOffice.Seat(0, out float x, out float z);
                    if (MathF.Abs(world.Local.X - x) + MathF.Abs(world.Local.Z - z) > 0.5f) break;
                    Check(hud.WorldText.StartsWith("Small Island · PM1-SMALL-", StringComparison.Ordinal), $"on the island the world plate reads \"{hud.WorldText}\"");
                    Check(!world.Local.Riding && world.Vehicles.Count == 0, "on the island the host is still in a saddle");
                    Check(_session.Feed.Entries.Any(e => e.Text.StartsWith("You chose Small Island · PM1-SMALL-", StringComparison.Ordinal)), "the feed does not say the host chose the island");
                    Shot("lobby-island");
                    _worldHash = world.Map.MapHash;
                    Check(client.NewSeed() != 0, "the host's seed key asked for nothing");
                    _worldStep = 2;
                    _worldFrames = 0;
                    break;
                }
                case 2:
                {
                    if (world.Map.MapHash == _worldHash || !settled) break;
                    _worldIslandText = hud.WorldText;
                    Check(world.Map.Id == WorldGenerator.SmallIsland && world.Seed == server.Settings.Seed, $"after a new seed the host has {world.Map.Id} #{world.Seed}, the server #{server.Settings.Seed}");
                    Check(client.ChooseWorld(0, _worldSeed) != 0, "the host could not go back to the test street");
                    _worldStep = 3;
                    _worldFrames = 0;
                    break;
                }
                default:
                {
                    if (world.Map.Id != "test_street" || !settled) break;
                    Check(world.Seed == _worldSeed && server.Settings.Seed == _worldSeed && world.Map.MapHash == _worldHashStreet, $"back on the test street the seed is {world.Seed}");
                    GD.Print($"[smoke] world: Test Street, then Small Island (\"{_worldIslandText}\" after a new seed), drawn anew each time, and back to the test street");
                    StartInventory();
                    break;
                }
            }
        }
    }
}
