using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.App
{
    /// <summary>
    /// The Fortify blueprint in the smoke's run (doc 10 §3 bp_defense): in the first Prep from
    /// shift 2 on that the wallet allows it, after the visit to the shop, the host opens the
    /// counter, buys the blueprint with its card's key, and builds a Spike Strip in Build mode
    /// with two Iron Ingots the server hands them (the run has no refinery and the smoke no
    /// money for the shop's ten). The ghost goes from red ("Needs the Blueprint") to green, the
    /// strip is drawn, and the host walks over it unhurt.
    /// </summary>
    public partial class SmokeDriver
    {
        private int _fortifyStep, _fortifyFrames, _fortifyVisits, _fortifyWallet;

        private bool StepFortify()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var build = _session.Build;
            var fortify = world.Content.ShopItem("bp_defense");
            var spikes = world.Content.Recipes.First(r => r.Building.Id == "spike_strip");
            var player = server.GetPlayer(world.LocalId)!;
            var po = world.Map.PostOffice;

            if (_fortifyStep == 0)
            {
                if (_fortifyVisits > 0 || _shopVisits == 0 || _buildVisits == 0 || world.Local.Dead || client.PendingRequestCount > 0) return false;
                if (world.ShopState(fortify) != OpResult.Ok || world.Global.Wallet != server.Economy.Wallet) return false;
                _session.FixedTicksPerFrame = 1;
                player.Move.X = po.ShopX + ShopStandOff;
                player.Move.Z = po.ShopZ;
                _fortifyStep = 1;
                _fortifyFrames = 0;
                return true;
            }
            if (++_fortifyFrames > BuildStepFrames)
            {
                Fail($"the Fortify visit stopped at step {_fortifyStep}: shop {(world.ShopOpen ? "up" : "down")}, owned {world.ShopOwned.Contains(fortify)}, ghost {build.Ghost.Aim} {build.Ghost.State}, prompt \"{hud.PromptText}\"");
                return false;
            }
            bool next = false;
            switch (_fortifyStep)
            {
                case 1:
                {
                    // At the counter: the card of the blueprint, bought with its key.
                    float dx = world.Local.X - player.Move.X, dz = world.Local.Z - player.Move.Z;
                    if (dx * dx + dz * dz > 0.1f || _fortifyFrames < 4) break;
                    if (!world.ShopOpen) { client.OpenShop(); break; }
                    int place = world.ShopShelf.ToList().IndexOf(fortify);
                    Check(place >= 0 && HudText.ShopNote(world, fortify) == "Once per run, for the whole team", $"the blueprint's card is at {place}: \"{HudText.ShopNote(world, fortify)}\"");
                    _fortifyWallet = world.Global.Wallet;
                    Check(client.PressShopKey(place), $"the key of the blueprint's card {HudText.CardKey(place)} was not the shop's");
                    next = true;
                    break;
                }
                case 2:
                    if (!world.ShopOwned.Contains(fortify) || client.PendingRequestCount > 0 || world.Global.Wallet != server.Economy.Wallet) break;
                    // A blueprint is never a special: it costs what the card says.
                    Check(server.OwnsBlueprint("bp_defense") && world.Global.Wallet == _fortifyWallet - fortify.Price,
                        $"the wallet went from {_fortifyWallet} to {world.Global.Wallet} for the blueprint");
                    Check(HudText.ShopNote(world, fortify) == "The team owns it", $"the bought blueprint's card says \"{HudText.ShopNote(world, fortify)}\"");
                    Check(_session.Feed.Entries.Any(e => e.Text == HudText.BlueprintBought(world, fortify)), "the feed does not say what the blueprint lets the team build");
                    Shot("fortify-bought");
                    client.CloseShop();
                    Check(server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("iron_ingot"), 2)), "no room for two Iron Ingots");
                    if (!StandAtBuildSite()) Fail("no open ground to put the Spike Strip on");
                    next = true;
                    break;
                case 3:
                {
                    AimAtTile(_buildTx, _buildTz);
                    bool arrived = MathF.Abs(world.Local.X - _buildX) < 0.3f && MathF.Abs(world.Local.Z - _buildZ) < 0.3f;
                    if (!arrived || _fortifyFrames < 4 || client.PendingRequestCount > 0) break;
                    if (!build.Active) Check(build.Toggle(), $"B did not put Build mode up ({build.Refused})");
                    Check(build.PressKey(world.Content.Recipes.ToList().IndexOf(spikes)) && build.Recipe == spikes, "the Spike Strip has no key in Build mode");
                    build.Update(1.0 / 30.0, false, false);
                    hud.Refresh();
                    _screen.Constructs.Refresh();
                    if (build.Ghost.Aim != BuildAim.Place || build.Ghost.State != OpResult.Ok) break;
                    Check(hud.PromptText.StartsWith("Build Spike Strip", StringComparison.Ordinal) && _screen.Constructs.GhostGreen, $"the Spike Strip's ghost: \"{hud.PromptText}\"");
                    Shot("fortify-ghost");
                    build.Update(1.0 / 30.0, true, false);
                    next = true;
                    break;
                }
                case 4:
                {
                    var strip = world.Constructs.At(_buildTx, _buildTz);
                    if (strip == null || server.Constructs.At(_buildTx, _buildTz) == null || client.PendingRequestCount > 0) break;
                    Check(strip.Def.Id == "spike_strip" && !world.Constructs.Blocks(_buildTx, _buildTz), $"built a {strip.Def.Id} that blocks the way");
                    build.Leave();
                    _screen.Constructs.Refresh();
                    Shot("fortify-spikes");
                    // Over it, unhurt: the host stands on the middle of its tile.
                    player.Move.X = BuildRules.TileCentre(_buildTx);
                    player.Move.Z = BuildRules.TileCentre(_buildTz);
                    next = true;
                    break;
                }
                case 5:
                    if (_fortifyFrames < 30) break;
                    Check(MathF.Abs(world.Local.X - BuildRules.TileCentre(_buildTx)) < 0.05f && player.Hp == player.MaxHp,
                        $"on the Spike Strip the host stands at {world.Local.X} (tile middle {BuildRules.TileCentre(_buildTx)}) with {player.Hp} HP");
                    GD.Print($"[smoke] fortify: Blueprint: Fortify bought with its key for {HudText.Money(_fortifyWallet - world.Global.Wallet)}, a Spike Strip built at ({_buildTx}, {_buildTz}) and walked over");
                    _fortifyVisits++;
                    _fortifyStep = 0;
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    return false;
            }
            if (next)
            {
                _fortifyStep++;
                _fortifyFrames = 0;
            }
            return true;
        }

        private void VerifyFortify()
        {
            Check(_fortifyVisits == 1, $"the Fortify blueprint was bought and built with {_fortifyVisits} times");
        }
    }
}
