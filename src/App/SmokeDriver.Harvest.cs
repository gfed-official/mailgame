using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Client.UI;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.World;

namespace PerformativeMail.App
{
    /// <summary>
    /// Harvesting in the smoke's run (doc 02 §3.9): in the Prep before the visit to the shop the
    /// host picks a bush clean with the held key, is told what a boulder takes, chops a tree down
    /// by hand and looks at what else grows, is refused at a sand pile with bare hands and digs it
    /// out with a Shovel the server hands them, drawn in the hand — the prompt, the bar, the
    /// pockets, the feed and what stands in the world afterwards are right on both ends. The shop
    /// takes what was gathered, the Shovel too.
    /// </summary>
    public partial class SmokeDriver
    {
        /// <summary>Tool: worked with the tool it takes in the hand, which the server hands over (the run cannot pay for the shop's yet).</summary>
        private enum HarvestVisit { Work, Refused, Look, Tool }

        private static readonly (string Node, HarvestVisit Visit)[] HarvestRound =
        {
            ("fiber_bush", HarvestVisit.Work), ("boulder", HarvestVisit.Refused), ("tree", HarvestVisit.Work),
            ("berry_bush", HarvestVisit.Look), ("ore_vein", HarvestVisit.Look),
            ("sand_pile", HarvestVisit.Refused), ("sand_pile", HarvestVisit.Tool),
        };

        private const int HarvestStepFrames = 300, HarvestArriveFrames = 30, HarvestRefusedFrames = 30;
        private const float HarvestStandOff = 0.9f;

        private int _harvestStep, _harvestFrames, _harvestVisits, _harvestTry, _harvestNode = -1;
        private int _harvestSwings, _harvestYield, _harvestHad, _harvestBare, _harvestRedraws, _harvestWorked, _harvestUnits;
        private float _harvestX, _harvestZ;
        private bool _harvestHolding, _harvestBar, _harvestToolSeen;
        private string _harvestSeen = "";

        /// <summary>
        /// Stands the host beside a node of this kind nobody has worked yet, on ground with nothing
        /// else near; <paramref name="skip"/> passes over that many such places.
        /// </summary>
        private bool StandAtNode(string id, int skip)
        {
            var world = _session.Client.World!;
            var map = world.Map;
            var player = _session.Server!.World.GetPlayer(world.LocalId)!;
            for (int i = 0; i < world.Nodes.Count; i++)
            {
                if (world.Nodes.Def(i)?.Id != id || world.Nodes.Touched(i)) continue;
                var prop = map.Props[i];
                float reach = prop.Radius + HarvestStandOff;
                for (int side = 0; side < 4; side++)
                {
                    float x = prop.X + (side == 0 ? reach : side == 1 ? -reach : 0f), z = prop.Z + (side == 2 ? reach : side == 3 ? -reach : 0f);
                    if (!FreeGround(map, x, z, i) || skip-- > 0) continue;
                    _harvestNode = i;
                    _harvestX = player.Move.X = x;
                    _harvestZ = player.Move.Z = z;
                    return true;
                }
            }
            return false;
        }

        private static bool FreeGround(WorldMap map, float x, float z, int but)
        {
            const float tile = SimConstants.TileSize, body = 0.45f, clear = 1.2f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (map.IsSolid((int)MathF.Floor((x + dx * body) / tile), (int)MathF.Floor((z + dz * body) / tile))) return false;
            for (int i = 0; i < map.Props.Count; i++)
                if (i != but && map.Props[i].Solid && map.Props[i].Distance(x, z, out _, out _) < map.Props[i].Radius + clear) return false;
            return true;
        }

        private void NextHarvestStep()
        {
            _harvestStep++;
            _harvestFrames = 0;
            _harvestTry = 0;
            _harvestHolding = false;
            if (_harvestStep <= HarvestRound.Length && !StandAtNode(HarvestRound[_harvestStep - 1].Node, 0))
                Fail($"the map has no '{HarvestRound[_harvestStep - 1].Node}' with room to stand beside it");
            var client = _session.Client;
            if (_harvestStep > HarvestRound.Length || HarvestRound[_harvestStep - 1].Visit != HarvestVisit.Tool)
            {
                client.Input.HeldSlot = 0;
                return;
            }
            // The tool the node takes, on the hotbar of the server's player, and in the hand.
            var world = client.World!;
            var def = world.Nodes.Def(_harvestNode)!;
            var tool = world.Content.Items.First(i => i.HarvestsResource(def.Resource));
            var player = _session.Server!.World.GetPlayer(world.LocalId)!;
            Check(_session.Server.World.Give(player.Hotbar, ItemStack.OfItem(tool, 1)), $"no room on the hotbar for {tool.Name}");
            var entry = player.Hotbar.Entries.FirstOrDefault(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == tool.Index && e.X > 0);
            if (entry == null) { Fail($"{tool.Name} is not on the hotbar"); return; }
            client.Input.HeldSlot = (byte)entry.X;
            _harvestToolSeen = false;
        }

        /// <summary>A frame of Prep. Returns true while the round of the nodes goes on: the shop and the go key wait for it.</summary>
        private bool StepHarvest()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var view = _screen.Nodes;

            if (_harvestStep == 0)
            {
                if (_harvestVisits > 0 || !world.ShopServes || world.Local.Dead || client.PendingRequestCount > 0) return false;
                // The pace of a player: a tick a frame, so a swing takes its eighteen frames.
                _session.FixedTicksPerFrame = 1;
                client.Input.HeldSlot = 0;
                _harvestWorked = _harvestUnits = 0;
                _harvestSeen = "";
                NextHarvestStep();
                return true;
            }

            if (_harvestStep > HarvestRound.Length) return EndHarvest();
            var (id, visit) = HarvestRound[_harvestStep - 1];
            var def = world.Nodes.Def(_harvestNode)!;
            _session.Interaction.Update(1.0 / 30.0, _harvestHolding, false);
            hud.Refresh();
            var prompt = _session.Interaction.Prompt;
            int have = Units(world.Hotbar, def.Item) + Units(world.Inventory, def.Item);
            if (hud.HoldShown > 0f) _harvestBar = true;
            if (++_harvestFrames > HarvestStepFrames)
            {
                Fail($"the round of the nodes stopped at the {def.Name} ({id}, node {_harvestNode}): prompt {prompt.Kind} \"{hud.PromptText}\", {world.Nodes.HitsLeft(_harvestNode)} hits left "
                    + $"(server {server.Nodes.HitsLeft(_harvestNode)}), {have} {def.Item.Name} carried, {client.LocalHarvestSwings - _harvestSwings} swings, {client.PendingRequestCount} requests pending, "
                    + $"{view.Bare} nodes drawn bare");
                return false;
            }

            if (!_harvestHolding)
            {
                // Beside it, the view on it; where something else is in the way, the next place.
                float dx = world.Local.X - _harvestX, dz = world.Local.Z - _harvestZ;
                bool there = dx * dx + dz * dz < 0.1f;
                if (there)
                {
                    var box = AimBox.Node(world.Map, world.Map.Props[_harvestNode]);
                    ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                }
                if (!there || prompt.Kind != InteractKind.Harvest || prompt.NodeIndex != _harvestNode || _harvestFrames < 4)
                {
                    if (_harvestFrames < HarvestArriveFrames) return true;
                    _harvestFrames = 0;
                    if (!StandAtNode(id, ++_harvestTry)) Fail($"no {def.Name} ({id}) could be looked at from beside it: prompt {prompt.Kind} \"{hud.PromptText}\"");
                    return true;
                }

                string want = HudText.Prompt(world, prompt, out var tone);
                Check(want.Length > 0 && hud.PromptText == want, $"at the {def.Name} the prompt says \"{hud.PromptText}\", expected \"{want}\"");
                bool needsTool = prompt.NodeState == OpResult.NeedsTool;
                Check(prompt.NodeState == OpResult.Ok || needsTool, $"the {def.Name} cannot be worked: {prompt.NodeState}");
                Check(needsTool == (visit == HarvestVisit.Refused || (def.Tool == NodeTool.Required && visit != HarvestVisit.Tool)), $"bare hands at the {def.Name}: {prompt.NodeState}");
                if (visit == HarvestVisit.Tool)
                {
                    // The tool is drawn in the hand while it is held, before a swing and after.
                    var tool = world.Content.Items.First(i => i.HarvestsResource(def.Resource));
                    Check(_screen.Camera.ToolInHand == tool.Name, $"with {tool.Name} in the held slot the hand holds \"{_screen.Camera.ToolInHand}\"");
                    Shot($"harvest-{id}-tool");
                }
                if (needsTool)
                    Check(want.Contains("\nNeeds a", StringComparison.Ordinal) && hud.PromptMark == PromptTone.Mismatch && tone == PromptTone.Mismatch, $"the prompt for what the {def.Name} takes is \"{want}\" ({hud.PromptMark})");
                else
                {
                    // UI polish plan item 5: the noun over the verb row, its key drawn as a keycap.
                    Check(want.StartsWith(def.Name.ToUpperInvariant() + HudText.Dot, StringComparison.Ordinal)
                        && want.Contains($"\n[hold E] {HudText.Capitalised(def.Verb)}{HudText.Dot}{def.Item.Name} x{prompt.NodeYield} a hit", StringComparison.Ordinal), $"the prompt at the {def.Name} is \"{want}\"");
                    Check(hud.PromptKeycaps.FirstOrDefault() == "HOLD E", $"the prompt at the {def.Name} draws the keycaps {string.Join(", ", hud.PromptKeycaps)}");
                }
                Check(prompt.NodeHitsLeft == def.Hits && hud.HoldShown == 0f, $"the {def.Name} nobody has touched has {prompt.NodeHitsLeft} of {def.Hits} hits left, the bar at {hud.HoldShown:0.00}");
                foreach (char c in FontCheck.MissingGlyphs(hud))
                    if (_missingGlyphs.IndexOf(c) < 0) _missingGlyphs += c;
                Shot($"harvest-{id}");
                _harvestSeen += (_harvestSeen.Length > 0 ? ", " : "") + def.Name;

                if (visit == HarvestVisit.Look)
                {
                    if (_harvestStep == HarvestRound.Length) return EndHarvest();
                    NextHarvestStep();
                    return true;
                }
                _harvestHolding = true;
                _harvestBar = false;
                _harvestFrames = 0;
                _harvestSwings = client.LocalHarvestSwings;
                _harvestYield = prompt.NodeYield;
                _harvestHad = have;
                _harvestBare = view.Bare;
                _harvestRedraws = view.Redraws;
                return true;
            }

            if (visit == HarvestVisit.Refused)
            {
                // The key held on what takes a tool: no bar, no swing, nothing asked of the server.
                if (_harvestFrames < HarvestRefusedFrames) return true;
                Check(client.LocalHarvestSwings == _harvestSwings && client.PendingRequestCount == 0 && !_harvestBar, $"the key held at the {def.Name} with bare hands swung {client.LocalHarvestSwings - _harvestSwings} times");
                Check(!server.Nodes.Touched(_harvestNode), $"the {def.Name} was hit without a tool");
                NextHarvestStep();
                return true;
            }

            // Held until nothing is left of it, and the world is drawn that way.
            if (_harvestBar) Shot($"harvest-{id}-swing");
            if (visit == HarvestVisit.Tool && _screen.Camera.ToolInHand.Length > 0) _harvestToolSeen = true;
            int gathered = def.Hits * _harvestYield;
            if (!world.Nodes.Depleted(_harvestNode) || client.PendingRequestCount > 0 || have < _harvestHad + gathered || view.Bare != _harvestBare + 1) return true;
            int swings = client.LocalHarvestSwings - _harvestSwings;
            Check(swings == def.Hits, $"the {def.Name} of {def.Hits} hits took {swings} swings");
            Check(_harvestFrames >= def.Hits * 17, $"{def.Hits} swings at the {def.Name} took {_harvestFrames} ticks");
            Check(server.Nodes.Depleted(_harvestNode), $"the {def.Name} is bare on the client and has {server.Nodes.HitsLeft(_harvestNode)} hits left on the server");
            Check(have == _harvestHad + gathered, $"{def.Hits} hits at {_harvestYield} {def.Item.Name} gave {have - _harvestHad}");
            Check(_harvestBar, $"no bar filled under the prompt while the {def.Name} was worked");
            Check(prompt.Kind == InteractKind.None && hud.PromptText.Length == 0, $"the bare {def.Name} still has a prompt: \"{hud.PromptText}\"");
            Check(view.Redraws == _harvestRedraws + 1, $"the nodes were drawn {view.Redraws - _harvestRedraws} times anew for one {def.Name} worked out");
            string line = HudText.Gathered(world, world.LocalId, def.Item.Index, gathered);
            Check(_session.Feed.Entries.Any(e => e.Text == line), $"the feed does not say \"{line}\"");
            Shot($"harvest-{id}-bare");
            if (visit == HarvestVisit.Tool)
            {
                Check(_harvestToolSeen, $"the tool was not in the hand while the {def.Name} was worked");
                client.Input.HeldSlot = 0;
                _screen.Camera.Sync();
                Check(_screen.Camera.ToolInHand.Length == 0, $"with the hands empty the hand still holds \"{_screen.Camera.ToolInHand}\"");
            }
            _harvestWorked++;
            _harvestUnits += gathered;
            NextHarvestStep();
            return true;
        }

        private bool EndHarvest()
        {
            var world = _session.Client.World!;
            var view = _screen.Nodes;
            Check(view.Whole + view.Bare == world.Nodes.NodeCount && view.Bare == _harvestWorked, $"{view.Whole} nodes are drawn whole and {view.Bare} bare, of {world.Nodes.NodeCount} with {_harvestWorked} worked out");
            GD.Print($"[smoke] harvest: {_harvestSeen}; {_harvestWorked} worked out by hand for {_harvestUnits} items; {view.Whole} of {world.Nodes.NodeCount} nodes left whole");
            _harvestVisits++;
            _harvestStep = 0;
            _session.FixedTicksPerFrame = RunOutTicksPerFrame;
            return false;
        }

        /// <summary>After the run: the land was worked once, and in the Lobby it is whole again on both ends.</summary>
        private void VerifyHarvest()
        {
            var world = _session.Client.World!;
            var server = _session.Server!.World;
            var view = _screen.Nodes;
            Check(_harvestVisits == 1 && _harvestWorked == 3, $"the smoke worked the land {_harvestVisits} times and {_harvestWorked} nodes out");
            int touched = 0;
            for (int i = 0; i < world.Nodes.Count; i++)
                if (world.Nodes.Touched(i) || server.Nodes.Touched(i)) touched++;
            Check(touched == 0, $"{touched} nodes of the finished run are still worked in the Lobby");
            view.Refresh();
            Check(view.Bare == 0 && view.Whole == world.Nodes.NodeCount && view.Redraws > _harvestRedraws + 1, $"in the Lobby {view.Bare} nodes are still drawn bare ({view.Redraws} redraws)");
        }
    }
}
