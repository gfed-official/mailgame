using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using GridContainer = PerformativeMail.Sim.Inventory.GridContainer;

namespace PerformativeMail.App
{
    /// <summary>
    /// The death bag (doc 01 §2.2), still in the Lobby after the fight: the host goes down with the
    /// giant's loot in their pockets, the bag lies where they fell with its countdown on the HUD,
    /// and once back on their feet they walk to it, open it through the prompt and take it all back.
    /// </summary>
    public partial class SmokeDriver
    {
        private const int BagStepFrames = 400;

        private int _bagStep, _bagFrames, _bagPromptFrames, _bagCarried;
        private uint _bagId;
        private bool _atBag;
        private Action? _bagTick;

        private static int Carried(GridContainer hotbar, GridContainer inventory) =>
            hotbar.Entries.Sum(e => e.Stack.Count) + inventory.Entries.Sum(e => e.Stack.Count);

        private void StartBag()
        {
            _bagStep = 0;
            _bagFrames = 0;
            _bagPromptFrames = 0;
            _atBag = false;
            Enter(Stage.Bag);
        }

        private void NextBagStep()
        {
            _bagStep++;
            _bagFrames = 0;
        }

        private void StepBag(double delta)
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var panel = _screen.Inventory;
            var player = server.GetPlayer(world.LocalId)!;
            // The bag is opened as a player does it: through the prompt, with a tap of interact.
            _session.Interaction.Update(delta, false, false, interactTapped: _bagStep == 3 && _bagPromptFrames == 2);
            hud.Refresh();
            var prompt = _session.Interaction.Prompt;
            foreach (char c in FontCheck.MissingGlyphs(hud))
                if (_missingGlyphs.IndexOf(c) < 0) _missingGlyphs += c;

            if (++_bagFrames > BagStepFrames)
            {
                Fail($"death bag step {_bagStep} did not finish: {world.Bags.Count} bags seen ({server.Bags.Count} on the server, {_screen.Effects.BagViewCount} drawn), "
                    + $"host {(world.Local.Dead ? "down" : "up")} at ({world.Local.X:0.0}, {world.Local.Z:0.0}), prompt {prompt.Kind} \"{hud.PromptText}\", tracker \"{hud.BagText}\", "
                    + $"open container {world.OpenContainerId}, {client.PendingRequestCount} requests pending, carrying {Carried(world.Hotbar, world.Inventory)} of {_bagCarried}");
                return;
            }

            switch (_bagStep)
            {
                case 0:
                    if (_bagFrames < 3) break;
                    _bagCarried = Carried(player.Hotbar, player.Inventory);
                    Check(_bagCarried > 0, "after the fight the host carries nothing to drop");
                    Check(world.Bags.Count == 0 && hud.BagText.Length == 0, $"before anyone fell there are {world.Bags.Count} bags and the tracker says \"{hud.BagText}\"");
                    server.DamagePlayer(player, 9999f, (byte)_session.Content.Enemy("barbarian").Index);
                    NextBagStep();
                    break;

                case 1:
                {
                    // Down: the pockets are empty, the bag lies there, and the HUD says how long it will.
                    if (!world.Local.Dead || world.Bags.Count != 1 || _screen.Effects.BagViewCount != 1 || Carried(world.Hotbar, world.Inventory) != 0) break;
                    var bag = world.LocalBag;
                    Check(bag != null && server.Bags.Count == 1 && server.Bags[0].Id == bag.ContainerId, "the bag on the ground is not the host's own");
                    if (bag == null) { Fail("the host fell and sees no bag of their own"); return; }
                    _bagId = bag.ContainerId;
                    Check(MathF.Abs(bag.X - world.Local.X) + MathF.Abs(bag.Z - world.Local.Z) < 0.5f, $"the bag lies at ({bag.X:0.0}, {bag.Z:0.0}), the host fell at ({world.Local.X:0.0}, {world.Local.Z:0.0})");
                    Check(hud.BagText == HudText.BagTracker(world) && hud.BagText.StartsWith("Your bag   ·   gone in 00:", StringComparison.Ordinal), $"with a bag on the ground the tracker says \"{hud.BagText}\"");
                    Check(hud.BannerText.StartsWith("DOWN", StringComparison.Ordinal), $"down, the banner says \"{hud.BannerText}\"");
                    Check(_session.Feed.Entries.Any(e => e.Text.StartsWith("Your bag lies where you fell", StringComparison.Ordinal)), "the feed does not say where the bag lies");
                    Check(prompt.Kind == InteractKind.None && hud.PromptText.Length == 0, $"down beside the bag the prompt is {prompt.Kind} \"{hud.PromptText}\"");
                    Shot("bag-down");
                    NextBagStep();
                    break;
                }

                case 2:
                {
                    // Back on their feet at the Post Office, they walk to the bag and look at it.
                    if (world.Local.Dead) break;
                    // Nit 17: nobody can hurt them for the first seconds, and the banner says so.
                    Check(world.LocalProtected && server.Protected(player) && hud.BannerText == HudText.Protected(world) && hud.BannerText.StartsWith("PROTECTED", StringComparison.Ordinal),
                        $"back on their feet the host is protected: {world.LocalProtected} (server: {server.Protected(player)}), the banner says \"{hud.BannerText}\"");
                    Shot("bag-protected");
                    if (!world.Bags.TryGetValue(_bagId, out var bag)) { Fail("the bag was gone before its owner stood up"); return; }
                    if (!ClearLine(server, bag.X, bag.Z, 1.4f, out float standX, out float standZ)) { Fail("no open ground beside the bag"); return; }
                    var box = AimBox.Bag(world.Map, bag.X, bag.Z);
                    _bagTick = () =>
                    {
                        client.Input.MoveX = 0f;
                        client.Input.MoveZ = 0f;
                        client.Input.Buttons = InputButtons.None;
                        if (!_atBag) _atBag = _bot.WalkTo(world, standX, standZ, 0.3f);
                        else ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                    };
                    client.TickStarting += _bagTick;
                    NextBagStep();
                    break;
                }

                case 3:
                    if (world.OpenContainerId == _bagId) { NextBagStep(); break; }
                    if (!world.Bags.ContainsKey(_bagId)) { Fail($"the bag was gone before its owner got back to it, at ({world.Local.X:0.0}, {world.Local.Z:0.0})"); return; }
                    if (!_atBag || prompt.Kind != InteractKind.OpenBag || prompt.Bag!.ContainerId != _bagId) break;
                    if (++_bagPromptFrames != 1) break;
                    Check(hud.PromptText.StartsWith("Open your bag  [E]   ·   gone in 00:", StringComparison.Ordinal), $"before the bag the HUD prompt reads \"{hud.PromptText}\"");
                    Shot("bag-prompt");
                    break;

                case 4:
                    // It opens as a grid beside the player's own, under its owner's name, with everything in it.
                    if (!panel.Visible || !panel.ExternalGrid.IsVisibleInTree() || _bagFrames < 4) break;
                    Check(panel.ExternalTitle == "Your bag", $"the open bag is titled \"{panel.ExternalTitle}\"");
                    Check(world.OpenContainer!.Entries.Sum(e => e.Stack.Count) == _bagCarried, $"the bag holds {world.OpenContainer.Entries.Sum(e => e.Stack.Count)} items, the host carried {_bagCarried}");
                    Check(prompt.Kind == InteractKind.CloseBag, $"with the bag open the prompt is {prompt.Kind}");
                    Shot("bag-open");
                    NextBagStep();
                    break;

                case 5:
                {
                    // Stack by stack back into the pockets; the emptied bag is gone at once, and its panel with it.
                    var open = world.OpenContainerId == _bagId ? world.OpenContainer : null;
                    if (open != null && open.EntryCount > 0 && client.PendingRequestCount == 0)
                        client.QuickMove(_bagId, open.Entries.Min(e => e.EntryId));
                    if (world.Bags.Count > 0 || _screen.Effects.BagViewCount > 0 || world.OpenContainerId != 0 || panel.Visible || client.PendingRequestCount > 0) break;
                    client.TickStarting -= _bagTick;
                    client.Input.MoveX = 0f;
                    client.Input.MoveZ = 0f;
                    Check(server.Bags.Count == 0, $"the emptied bag is still on the server ({server.Bags.Count})");
                    Check(Carried(player.Hotbar, player.Inventory) == _bagCarried, $"the host carried {_bagCarried} items and has {Carried(player.Hotbar, player.Inventory)} back");
                    Check(player.Hotbar.ComputeHash() == world.Hotbar.ComputeHash() && player.Inventory.ComputeHash() == world.Inventory.ComputeHash(), "after the bag the player's containers differ from the server's");
                    Check(client.ContainerDesyncs == 0 && client.BadPackets == 0, $"after the bag: {client.ContainerDesyncs} desyncs, {client.BadPackets} bad packets");
                    Check(hud.BagText.Length == 0, $"with the bag gone the tracker still says \"{hud.BagText}\"");
                    Check(_session.Feed.Entries.Any(e => e.Text == "Your bag has been picked up"), "the feed does not say the bag was picked up");
                    Check(_missingGlyphs.Length == 0, $"the UI font has no glyph for: {_missingGlyphs}");
                    GD.Print($"[smoke] death bag: the host fell with {_bagCarried} items, found the bag where they fell and took all of it back");
                    StartRemote();
                    break;
                }
            }
        }
    }
}
