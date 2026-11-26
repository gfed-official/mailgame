using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.App
{
    /// <summary>
    /// The Repair Hammer (doc 05 §4, doc 09 §3; design choice 139), in the raid playtest's shift 3 Prep
    /// once the ruin is rebuilt: the wall takes a blow of 150, the host takes the hammer in the hand
    /// and looks at it — the HUD's line reads "Mend the Wooden Wall  [hold LMB]   ·   150 / 300 HP …",
    /// a bar stands over it — and holds attack until it is whole, for a Log, the feed saying so.
    /// </summary>
    public partial class SmokeDriver
    {
        private enum RepairStep { None, Hurt, Mending, Done }

        private RepairStep _repairStep;
        private int _repairFrames, _repairLogs;

        /// <summary>A frame of Prep after the rebuild. Returns true while the wall is being mended.</summary>
        private bool StepRepairWall()
        {
            if (_repairStep == RepairStep.Done || _ruinTx < 0) return false;
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var player = server.GetPlayer(world.LocalId)!;
            var hammer = world.Content.Item("repair_hammer");
            var log = world.Content.Item("log");
            if (_repairStep == RepairStep.None)
            {
                var truth = server.Constructs.At(_ruinTx, _ruinTz);
                if (truth == null) { Fail("no rebuilt wall to mend"); _repairStep = RepairStep.Done; return false; }
                server.DamageConstruct(truth, 150f);
                Check(server.Give(player.Hotbar, ItemStack.OfItem(hammer, 1)), "no room in the hotbar for the Repair Hammer");
                if (BuildRules.Have(player.Hotbar, player.Inventory, log) == 0) server.Give(player.Inventory, ItemStack.OfItem(log, 1));
                _repairFrames = 0;
                _repairStep = RepairStep.Hurt;
                return true;
            }
            var wall = world.Constructs.At(_ruinTx, _ruinTz);
            if (++_repairFrames > PlaytestStepFrames)
            {
                Fail($"the Wooden Wall was not mended with the Repair Hammer ({_repairStep}): {wall?.Hp} HP, \"{hud.PromptText}\"");
                client.Input.Buttons = InputButtons.None;
                _repairStep = RepairStep.Done;
                return false;
            }
            if (wall == null) return true;
            AimAtBuilt(wall);
            if (_repairStep == RepairStep.Hurt)
            {
                var held = world.Hotbar.Entries.FirstOrDefault(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == hammer.Index);
                if (held == null || wall.Hp != wall.Def.Hp - 150) return true;
                client.Input.HeldSlot = (byte)held.X;
                if (_repairFrames < 8) return true;
                _session.Interaction.Update(1.0 / 30.0, false, false);
                hud.Refresh();
                string want = $"Mend the Wooden Wall  [hold LMB]   ·   {wall.Hp} / {wall.Def.Hp} HP   ·   Log x0.75 a full bar";
                Check(hud.PromptText == want, $"with the Repair Hammer at the hurt wall the line reads \"{hud.PromptText}\", expected \"{want}\"");
                Check(hud.WeaponsText.StartsWith("[LMB] mend, 50 HP a second", StringComparison.Ordinal), $"with the Repair Hammer in the hand the weapons line reads \"{hud.WeaponsText}\"");
                Check(_screen.ConstructHealth.RepairBarShown, "no health bar over the wall the Repair Hammer is held at");
                Check(_screen.Camera.ToolInHand == hammer.Name, $"the hand holds \"{_screen.Camera.ToolInHand}\", not the Repair Hammer");
                Shot("repair");
                _repairLogs = BuildRules.Have(player.Hotbar, player.Inventory, log);
                client.Input.Buttons = InputButtons.Attack;
                _repairStep = RepairStep.Mending;
                return true;
            }
            // RepairStep.Mending
            if (wall.Hp < wall.Def.Hp || !_session.Feed.Entries.Any(e => e.Text == "You mended the Wooden Wall")) return true;
            client.Input.Buttons = InputButtons.None;
            int logs = BuildRules.Have(player.Hotbar, player.Inventory, log);
            Check(server.Constructs.At(_ruinTx, _ruinTz)?.Hp == wall.Def.Hp, "the server's wall is not whole");
            Check(logs == _repairLogs - 1, $"mending 150 of 300 HP took {_repairLogs - logs} Logs, not 1 (a quarter of its 3 a full bar, rounded up)");
            _session.Interaction.Update(1.0 / 30.0, false, false);
            hud.Refresh();
            Check(hud.PromptText.Length == 0, $"whole, the wall still has the line \"{hud.PromptText}\"");
            Shot("repaired");
            GD.Print($"[smoke] repair: the Wooden Wall mended from 150 to {wall.Def.Hp} HP with the Repair Hammer held at it, for 1 Log, \"You mended the Wooden Wall\" in the feed");
            client.Input.HeldSlot = 0;
            _repairStep = RepairStep.Done;
            return false;
        }
    }
}
