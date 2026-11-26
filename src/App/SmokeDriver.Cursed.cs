using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.App
{
    /// <summary>
    /// The Cursed Mail stamp (doc 01 §6, doc 05 §6; design choice 140) in the raid playtest: the host's
    /// T in the Lobby sets it and the plate says so; once shift 2's raid is beaten a cursed Letter goes,
    /// held in the hand with its purple label, to the wrong mailbox through the prompt — which warns —
    /// and 4 Barbarians and an Archer are at that mailbox at once, unannounced, the feed saying so.
    /// </summary>
    public partial class SmokeDriver
    {
        private enum CurseStep { None, Holding, Done }

        private CurseStep _curseStep;
        private int _curseFrames, _curseDest = -1;
        private bool _cursePromptSeen, _curseInHand;
        private RaidState _curseRaidWas;
        private float _curseFromX, _curseFromZ;
        private bool _curseLooked;

        /// <summary>The Lobby: true while the stamp is being set (the go key waits for it).</summary>
        private bool StepLobbyStamp()
        {
            var client = _session.Client;
            var world = client.World!;
            var hud = _screen.Hud;
            if (world.Global.Stamps == 0)
            {
                if (_ptFrames == BootFrames) Check(client.CycleStamps() != 0, "the host's stamps key sent nothing");
                if (_ptFrames > BootFrames + PlaytestStepFrames) { Fail("the Cursed Mail stamp was not set in the Lobby"); return false; }
                return true;
            }
            if (_ptFrames == BootFrames) return true;
            if (_curseStep == CurseStep.None && _curseDest == -1)
            {
                hud.Refresh();
                string want = "Stamps: Cursed Mail   ·   score ×1.15";
                Check(hud.StampsText == want, $"in the Lobby the stamps plate reads \"{hud.StampsText}\", expected \"{want}\"");
                Check(hud.WorldKeysText.EndsWith("[T] stamps", StringComparison.Ordinal), $"the host's keys read \"{hud.WorldKeysText}\"");
                Shot("lobby-stamps");
                GD.Print($"[smoke] stamps: Cursed Mail set in the Lobby with the host's key, \"{hud.StampsText}\"");
                _curseDest = -2;
            }
            return false;
        }

        /// <summary>A frame of shift 2's Delivery after the raid. Returns true while the cursed letter is on its way.</summary>
        private bool StepCursedMail()
        {
            if (_curseStep == CurseStep.Done)
            {
                // The picture's own view of the mini-raid given back to the carrier's eyes.
                if (_curseLooked) { _screen.Camera.FreeLook = false; _curseLooked = false; }
                return false;
            }
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var interaction = _session.Interaction;
            var player = server.GetPlayer(world.LocalId)!;
            var letter = world.Content.MailKind("letter");
            var houses = world.Map.Destinations.Where(d => DeliveryRules.KindAccepted(letter, d.Type)).ToList();
            if (_curseStep == CurseStep.None)
            {
                Check(server.Stamps == 1 && server.CursedStamp() != null, $"the run's stamps are {server.Stamps}");
                // The nearest house's mailbox, for a letter addressed to another.
                var other = houses.OrderBy(d => (d.MailboxX - player.Move.X) * (d.MailboxX - player.Move.X) + (d.MailboxZ - player.Move.Z) * (d.MailboxZ - player.Move.Z)).First();
                var home = houses.First(d => d.Address != other.Address);
                var item = new MailItem { Id = 990001, Kind = letter.Index, Address = home.Address, Value = (ushort)letter.BaseValue, SpawnShift = 2, DeadlineShift = 4, Flags = MailFlags.Cursed };
                Check(server.Give(player.Hotbar, ItemStack.OfMail(item)), "no room in the hotbar for the cursed letter");
                (_curseFromX, _curseFromZ) = (player.Move.X, player.Move.Z);
                player.Move.X = other.MailboxX;
                player.Move.Z = other.MailboxZ;
                _curseDest = other.Id;
                _curseRaidWas = server.Raid;
                _curseFrames = 0;
                _curseStep = CurseStep.Holding;
                return true;
            }
            var dest = world.Map.GetDestination(_curseDest)!;
            if (++_curseFrames > PlaytestStepFrames)
            {
                Fail($"the cursed letter brought no mini-raid at {dest.Label}: \"{hud.PromptText}\", {server.MiniRaids} mini-raids");
                _curseStep = CurseStep.Done;
                return false;
            }
            var held = world.Hotbar.Entries.FirstOrDefault(e => e.Stack.Kind == StackKind.Mail && e.Stack.Cursed);
            if (server.MiniRaids == 0)
            {
                _curseInHand |= _screen.Camera.CursedInHand;
                if (held == null || _curseFrames < 8) return true;
                client.Input.HeldSlot = (byte)held.X;
                var box = AimBox.Mailbox(world.Map, dest);
                ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
                interaction.Update(1.0 / 30.0, _cursePromptSeen, false);
                hud.Refresh();
                if (!_cursePromptSeen)
                {
                    if (interaction.Prompt.Kind != InteractKind.Deliver) return true;
                    Check(hud.PromptText.Contains(HudText.CursedWarning, StringComparison.Ordinal) && hud.PromptText.Contains("Cursed 1 Letter", StringComparison.Ordinal),
                        $"at the wrong mailbox with the cursed letter the prompt reads \"{hud.PromptText}\"");
                    Shot("cursed-prompt");
                    _cursePromptSeen = true;
                }
                return true;
            }
            interaction.Update(1.0 / 30.0, false, false);
            if (world.Enemies.Count < 5 && _curseFrames < PlaytestStepFrames / 2) return true;
            string line = $"Your cursed Letter at {dest.Label}: 5 raiders!";
            Check(_session.Feed.Entries.Any(e => e.Text == line), $"the feed has no \"{line}\"");
            Check(world.Enemies.Values.Count(e => e.Def.Id == "barbarian") == 4 && world.Enemies.Values.Count(e => e.Def.Id == "archer") == 1,
                $"the mini-raid is {string.Join(", ", world.Enemies.Values.Select(e => e.Def.Id))}");
            // No warning: the raid's state is what it was (the playtest's own raid, beaten, stays on till the shift ends).
            Check(server.Raid == _curseRaidWas && world.Raid == _curseRaidWas, $"the mini-raid changed the raid from {_curseRaidWas} to {server.Raid}");
            Check(_curseInHand, "the letter in the hand showed no cursed label");
            // The picture from over the way the host came, 7 m off and 4 m up, down at the mailbox and the raiders round it.
            {
                float dx = _curseFromX - dest.MailboxX, dz = _curseFromZ - dest.MailboxZ, len = MathF.Sqrt(dx * dx + dz * dz);
                if (len < 0.5f) (dx, dz, len) = (1f, 0f, 1f);
                float ground = world.Map.HeightAt(dest.MailboxX, dest.MailboxZ);
                _screen.Camera.LookFrom(new Vector3(dest.MailboxX + dx / len * 7f, ground + 4f, dest.MailboxZ + dz / len * 7f), new Vector3(dest.MailboxX, ground + 0.8f, dest.MailboxZ));
                _curseLooked = true;
            }
            Shot("cursed-raid");
            GD.Print($"[smoke] cursed mail: a cursed Letter put in the wrong mailbox at {dest.Label} brought 4 Barbarians and an Archer there, unannounced");
            // They have been seen; the shift goes on without them.
            foreach (var e in server.Enemies.ToList()) server.DamageEnemy(e, 1e6f, null);
            client.Input.HeldSlot = 0;
            _curseStep = CurseStep.Done;
            // A frame more: the picture is taken, and the next gives the carrier's eyes back.
            return true;
        }
    }
}
