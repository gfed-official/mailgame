using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;

namespace PerformativeMail.App
{
    /// <summary>
    /// A ruin rebuilt in Prep (doc 05 §5, design choice 126; the 0abedd1 playtest saw ruins only
    /// cleared in Delivery). In the raid playtest's shift 2 a Wooden Wall is put up beside the host
    /// once the raid is beaten and broken as a raider's blow would: its ruin is on the client, and
    /// the feed says so. In shift 3's Prep the host stands beside it, reads "Rebuild the Wooden Wall
    /// [E]   ·   Log xN (half its cost)", taps E, and the wall stands again turned as it was, at
    /// full health, for half its Logs, the feed saying so.
    /// </summary>
    public partial class SmokeDriver
    {
        private enum RuinStep { None, Broken, Rebuild, Done }

        private const int RuinRotation = 1;

        private RuinStep _ruinStep;
        private int _ruinTx = -1, _ruinTz, _ruinFrames, _ruinLogs = -1, _ruinCost;
        private float _ruinX, _ruinZ;
        private bool _ruinFeedSeen;

        /// <summary>Every frame of the raid playtest: the feed's line for the broken wall, while it is up.</summary>
        private void TrackRuinFeed()
        {
            if (_ruinStep == RuinStep.Broken && !_ruinFeedSeen)
                _ruinFeedSeen = _session.Feed.Entries.Any(e => e.Text.StartsWith("The raiders broke a Wooden Wall: its ruin is left", StringComparison.Ordinal));
        }

        /// <summary>In Delivery: a Wooden Wall beside the host, broken, its ruin left for the next Prep.</summary>
        private void LeaveRuinForPrep()
        {
            var world = _session.Client.World!;
            var server = _session.Server!.World;
            if (!StandAtBuildSite(world.Local.X, world.Local.Z)) { Fail("no open ground beside the host for the wall of the ruin"); return; }
            (_ruinTx, _ruinTz) = (_buildTx, _buildTz);
            var wall = server.PutUp(world.Content.Building("wall_wood"), _ruinTx, _ruinTz, RuinRotation);
            server.DamageConstruct(wall, wall.Hp + 1f);
            Check(server.Constructs.RuinAt(_ruinTx, _ruinTz) != null && server.Constructs.At(_ruinTx, _ruinTz) == null, $"the broken Wooden Wall at ({_ruinTx}, {_ruinTz}) left no ruin");
            _ruinStep = RuinStep.Broken;
        }

        /// <summary>A frame of Prep. Returns true while the ruin is being rebuilt.</summary>
        private bool StepRuinRebuild()
        {
            if (_ruinStep == RuinStep.None || _ruinStep == RuinStep.Done) return false;
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var interaction = _session.Interaction;
            var player = server.GetPlayer(world.LocalId)!;
            var wallDef = world.Content.Building("wall_wood");

            if (_ruinStep == RuinStep.Broken)
            {
                if (world.Local.Dead || client.PendingRequestCount > 0) return true;
                var ruin = world.Constructs.RuinAt(_ruinTx, _ruinTz);
                Check(ruin != null && server.Constructs.RuinAt(_ruinTx, _ruinTz) != null, $"in Prep the client has no ruin at ({_ruinTx}, {_ruinTz})");
                Check(_ruinFeedSeen, "the feed did not say the Wooden Wall was broken");
                if (ruin == null) { _ruinStep = RuinStep.Done; return false; }
                // Half the Logs, rounded up: given if the pockets are short.
                var log = world.Content.Item("log");
                _ruinCost = BuildRules.RuinCost(wallDef.Recipe.Inputs.Single(i => i.Item == log).Count, wallDef.RuinRebuildRatio);
                int missing = _ruinCost - CarriedLogs(world);
                if (missing > 0) Check(server.Give(player.Inventory, ItemStack.OfItem(log, missing)), "no room in the pockets for the Logs of the rebuild");
                // West of it, where the build site was chosen to have room.
                _ruinX = BuildRules.TileCentre(_ruinTx) - 1.6f;
                _ruinZ = BuildRules.TileCentre(_ruinTz);
                player.Move.X = _ruinX;
                player.Move.Z = _ruinZ;
                _ruinFrames = 0;
                _ruinStep = RuinStep.Rebuild;
                return true;
            }

            // RuinStep.Rebuild
            if (++_ruinFrames > PlaytestStepFrames)
            {
                Fail($"the ruin at ({_ruinTx}, {_ruinTz}) was not rebuilt in Prep: prompt {interaction.Prompt.Kind} \"{hud.PromptText}\", {CarriedLogs(world)} Logs");
                _ruinStep = RuinStep.Done;
                return false;
            }
            bool arrived = MathF.Abs(world.Local.X - _ruinX) < 0.3f && MathF.Abs(world.Local.Z - _ruinZ) < 0.3f;
            var left = world.Constructs.RuinAt(_ruinTx, _ruinTz);
            if (left != null)
            {
                AimAtBuilt(left);
                interaction.Update(1.0 / 30.0, false, false);
                if (!arrived || _ruinFrames < 4 || _ruinLogs >= 0 || CarriedLogs(world) < _ruinCost) return true;
                hud.Refresh();
                string want = $"Rebuild the Wooden Wall  [E]   ·   {HudText.RuinCost(wallDef)} (half its cost)";
                Check(interaction.Prompt.Kind == InteractKind.RebuildRuin && interaction.Prompt.Built == left, $"beside the ruin in Prep the prompt is {interaction.Prompt.Kind}");
                Check(hud.PromptText == want, $"beside the ruin in Prep the prompt reads \"{hud.PromptText}\", expected \"{want}\"");
                Shot("ruin-rebuild");
                _ruinLogs = CarriedLogs(world);
                interaction.Update(1.0 / 30.0, false, false, interactTapped: true);
                return true;
            }
            var standing = world.Constructs.At(_ruinTx, _ruinTz);
            if (standing == null || server.Constructs.At(_ruinTx, _ruinTz) == null || client.PendingRequestCount > 0) return true;
            if (!_session.Feed.Entries.Any(e => e.Text == "You rebuilt the Wooden Wall") && _ruinFrames < PlaytestStepFrames / 2) return true;
            Check(standing.Def == wallDef && standing.Hp == wallDef.Hp && standing.Rotation == RuinRotation,
                $"rebuilt: a {standing.Def.Id} of {standing.Hp} HP turned {standing.Rotation} (the wall was turned {RuinRotation})");
            Check(CarriedLogs(world) == _ruinLogs - _ruinCost, $"the rebuild took {_ruinLogs - CarriedLogs(world)} Logs, half the wall's is {_ruinCost}");
            Check(_session.Feed.Entries.Any(e => e.Text == "You rebuilt the Wooden Wall"), "no \"You rebuilt the Wooden Wall\" line in the feed");
            Shot("ruin-rebuilt");
            GD.Print($"[smoke] ruin: the Wooden Wall broken in shift {RaidPlaytestShift}'s Delivery rebuilt in shift {server.Run.Shift}'s Prep at ({_ruinTx}, {_ruinTz}) for {_ruinCost} Logs, half its cost, through the prompt and E");
            _ruinStep = RuinStep.Done;
            return false;
        }
    }
}
