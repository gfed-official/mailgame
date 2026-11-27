using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Run;
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
        private string _worldIslandText = "", _worldDailyText = "";
        private byte _worldStamps;
        private bool _worldLargeRefused;

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
                    _worldStamps = server.Stamps;
                    Check(hud.ModeKeysText == "[P] practice   ·   [O] daily   ·   [Ctrl+V] a seed", $"the host's practice / daily plate reads \"{hud.ModeKeysText}\"");
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
                    // Doc 01 §5.3: today's Daily Seed, its date and its fixed stamps.
                    Check(client.ToggleDaily() != 0, "the host's daily key asked for nothing");
                    _worldStep = 3;
                    _worldFrames = 0;
                    break;
                }
                case 3:
                {
                    if (world.Global.Mode != RunMode.Daily || world.Map.MapHash == _worldHash || !settled) break;
                    string day = RunModes.DayText(RunModes.Day(DateTime.UtcNow));
                    _worldDailyText = hud.WorldText;
                    Check(server.Run.Mode == RunMode.Daily && server.Stamps == RunModes.DailyStamps(server.Content) && world.Global.Stamps == server.Stamps,
                        $"with the Daily Seed the server is in {server.Run.Mode} with stamps {server.Stamps}");
                    Check(world.Seed == RunModes.DailySeed(RunModes.Day(DateTime.UtcNow)) && hud.WorldText.StartsWith($"Daily {day} · PM1-SMALL-", StringComparison.Ordinal),
                        $"with the Daily Seed the world plate reads \"{hud.WorldText}\" (seed {world.Seed})");
                    Check(hud.StampsText == HudText.StampsTracker(world) && hud.StampPickText.Length == 0 && hud.ModeKeysText.EndsWith("[O] leave the daily   ·   [Ctrl+V] a seed", StringComparison.Ordinal),
                        $"with the Daily Seed the plates read \"{hud.StampsText}\", \"{hud.StampPickText}\", \"{hud.ModeKeysText}\"");
                    Shot("lobby-daily");
                    // Doc 09 §6: the practice run, on the test street from its seed.
                    Check(client.TogglePractice() != 0, "the host's practice key asked for nothing");
                    _worldStep = 4;
                    _worldFrames = 0;
                    break;
                }
                case 4:
                {
                    if (world.Global.Mode != RunMode.Practice || world.Map.Id != "test_street" || !settled) break;
                    Check(server.Settings.Seed == RunModes.PracticeSeed(server.Content) && server.Stamps == 0 && server.ShiftCount == 2,
                        $"in practice the server has seed {server.Settings.Seed}, stamps {server.Stamps}, {server.ShiftCount} shifts");
                    Check(hud.WorldText == "Practice · 2 shifts, no raid, no XP" && hud.ModeKeysText.StartsWith("[P] leave practice", StringComparison.Ordinal),
                        $"in practice the plates read \"{hud.WorldText}\", \"{hud.ModeKeysText}\"");
                    // Doc 01 §5.2: the Large Island is rank 3's; a fresh profile's host cannot paste one (nor step onto it with [V]).
                    if (!client.WorldOpen(LargeIslandGenerator.LargeIsland))
                    {
                        string refused = client.PasteSeed("PM1-LARGE-00000007");
                        Check(refused == "Not pasted: the Large Island needs Postal Rank 3", $"pasting a Large Island at rank 1 says \"{refused}\"");
                        _worldLargeRefused = true;
                    }
                    // Doc 01 §5.3: back by the run's own seed string, pasted, with its stamps.
                    string text = SeedStrings.Format(server.Content, "test_street", _worldSeed, _worldStamps);
                    string line = client.PasteSeed(text.ToLowerInvariant());
                    Check(line == $"Seed {text} pasted", $"pasting \"{text}\" says \"{line}\"");
                    _worldStep = 5;
                    _worldFrames = 0;
                    break;
                }
                default:
                {
                    if (world.Map.Id != "test_street" || world.Global.Mode != RunMode.Normal || world.Global.Stamps != _worldStamps || !settled) break;
                    Check(world.Seed == _worldSeed && server.Settings.Seed == _worldSeed && world.Map.MapHash == _worldHashStreet, $"back on the test street the seed is {world.Seed}");
                    Check(server.Stamps == _worldStamps && world.Global.Stamps == _worldStamps, $"after the paste the stamps are {server.Stamps}, not {_worldStamps}");
                    GD.Print($"[smoke] world: Test Street, then Small Island (\"{_worldIslandText}\" after a new seed), the Daily Seed (\"{_worldDailyText}\"), practice, drawn anew each time, and back to the test street by its pasted seed string{(_worldLargeRefused ? "; the Large Island refused below Postal Rank 3" : "")}");
                    StartInventory();
                    break;
                }
            }
        }
    }
}
