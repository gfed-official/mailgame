using System;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using GridContainer = PerformativeMail.Sim.Inventory.GridContainer;

namespace PerformativeMail.App
{
    /// <summary>
    /// Healing in the smoke's run (doc 05 §1.1; playtest c59d497 could not see it live): in the
    /// first Prep after the shop visit the host is hurt and holds a Bandage, then Berries, and
    /// uses each with the use key. The Bandage gives its HP at once, the Berries theirs over their
    /// seconds — partly there halfway, all there at the end — and the health bar and its number
    /// follow the server. At full health the use key uses nothing.
    /// </summary>
    public partial class SmokeDriver
    {
        private const int HealStepFrames = 200;
        private const float HealHurt = 60f;

        private int _healStep, _healFrames, _healVisits, _healCount;
        private uint _healStartTick;
        private float _healFrom;
        private ItemDef? _healItem;

        private void NextHealStep()
        {
            _healStep++;
            _healFrames = 0;
        }

        /// <summary>A frame of Prep. Returns true while the check goes on: the go key waits for it.</summary>
        private bool StepHeal()
        {
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var player = server.GetPlayer(world.LocalId)!;
            var bandage = world.Content.Item("bandage");
            var berries = world.Content.Item("berries");

            if (_healStep == 0)
            {
                if (_healVisits > 0 || _shopVisits == 0 || world.Local.Dead || client.PendingRequestCount > 0) return false;
                _session.FixedTicksPerFrame = TicksPerFrame;
                Check(server.Give(player.Hotbar, ItemStack.OfItem(bandage, 1)) && server.Give(player.Hotbar, ItemStack.OfItem(berries, 2)),
                    "no room in the hotbar for a Bandage and two Berries");
                _healItem = bandage;
                NextHealStep();
                return true;
            }
            if (++_healFrames > HealStepFrames)
            {
                Fail($"the healing check stopped at step {_healStep}: {player.Hp:0.0} HP on the server, {world.LocalHp:0.0} on the client, HUD \"{hud.HealthText}\", held \"{hud.HeldText}\"");
                return false;
            }
            // No hit lands in Prep: the carrier's own slow mending is kept out of what is measured.
            player.LastDamageTick = server.Tick;
            hud.Refresh();
            var item = _healItem!;
            int slot = SlotOf(world.Hotbar, item);
            int held = Units(world.Hotbar, item);

            switch (_healStep)
            {
                case 1:
                case 4:
                {
                    // Hurt, with it in the hand.
                    if (slot < 0 || client.PendingRequestCount > 0) break;
                    player.Hp = player.MaxHp - HealHurt;
                    client.Input.HeldSlot = (byte)slot;
                    _healCount = held;
                    NextHealStep();
                    break;
                }
                case 2:
                case 5:
                {
                    if (_healFrames < 4 || MathF.Abs(world.LocalHp - player.Hp) > 0.01f) break;
                    var entry = world.Hotbar.EntryAt(slot, 0);
                    string want = entry == null ? "" : HudText.Held(world, entry.Stack);
                    Check(want.Contains(HudText.UseKey, StringComparison.Ordinal) && hud.HeldText == want, $"with {item.Name} in the hand the HUD says \"{hud.HeldText}\", expected \"{want}\"");
                    Check(hud.HealthText == HudText.Health(player.MaxHp - HealHurt), $"hurt to {player.MaxHp - HealHurt:0} the HUD shows \"{hud.HealthText}\"");
                    _healFrom = player.Hp;
                    _healStartTick = server.Tick;
                    Check(client.UseHeld() != 0, $"the use key did nothing with {item.Name} in the hand");
                    NextHealStep();
                    break;
                }
                case 3:
                {
                    // The Bandage: all of it at once.
                    if (client.PendingRequestCount > 0 || held != _healCount - 1 || MathF.Abs(world.LocalHp - player.Hp) > 0.01f) break;
                    Check(MathF.Abs(player.Hp - (_healFrom + item.Heal)) < 0.01f, $"a {item.Name} used at {_healFrom:0} HP left {player.Hp:0.0}, expected {_healFrom + item.Heal:0}");
                    CheckHealthBar("the Bandage");
                    Shot("heal-bandage");
                    _healItem = berries;
                    NextHealStep();
                    break;
                }
                case 6:
                {
                    // Berries: part of it halfway through their seconds, not all of it.
                    uint ticks = Sim.Core.SimConstants.SecondsToTicks(item.HealSeconds);
                    if (client.PendingRequestCount > 0 || held != _healCount - 1 || server.Tick - _healStartTick < ticks / 2) break;
                    float gained = player.Hp - _healFrom;
                    Check(gained > item.Heal * 0.25f && gained < item.Heal * 0.75f, $"halfway through the Berries' {item.HealSeconds} s the carrier has {gained:0.0} of their {item.Heal} HP");
                    CheckHealthBar("the Berries, halfway");
                    NextHealStep();
                    break;
                }
                case 7:
                {
                    uint ticks = Sim.Core.SimConstants.SecondsToTicks(item.HealSeconds);
                    if (server.Tick - _healStartTick < ticks + 4 || MathF.Abs(world.LocalHp - player.Hp) > 0.01f) break;
                    Check(MathF.Abs(player.Hp - (_healFrom + item.Heal)) < 0.05f && player.HealPool == 0f, $"after the Berries' {item.HealSeconds} s the carrier is at {player.Hp:0.0}, expected {_healFrom + item.Heal:0}");
                    CheckHealthBar("the Berries");
                    Shot("heal-berries");
                    // At full health nothing is used.
                    player.Hp = player.MaxHp;
                    _healCount = held;
                    NextHealStep();
                    break;
                }
                case 8:
                {
                    if (_healFrames < 4 || MathF.Abs(world.LocalHp - player.Hp) > 0.01f) break;
                    Check(client.UseHeld() != 0, "the use key sent nothing with Berries in the hand at full health");
                    NextHealStep();
                    break;
                }
                case 9:
                {
                    if (client.PendingRequestCount > 0 || _healFrames < 6) break;
                    Check(held == _healCount && Units(player.Hotbar, item) == _healCount, $"at full health the use key ate Berries: {held} left of {_healCount}");
                    // The last one is eaten: the run goes on with nothing but mail in the pockets.
                    player.Hp = player.MaxHp - HealHurt;
                    NextHealStep();
                    break;
                }
                case 10:
                {
                    if (_healFrames < 4 || MathF.Abs(world.LocalHp - player.Hp) > 0.01f) break;
                    Check(client.UseHeld() != 0, "the use key did nothing with the last Berries in the hand");
                    NextHealStep();
                    break;
                }
                case 11:
                {
                    if (client.PendingRequestCount > 0 || Units(player.Hotbar, item) != 0) break;
                    // Whole again for what comes next, with nothing still to come from the last one.
                    player.HealPool = 0f;
                    player.HealPerTick = 0f;
                    player.Hp = player.MaxHp;
                    client.Input.HeldSlot = 0;
                    GD.Print($"[smoke] heal: a Bandage gave {bandage.Heal} HP at once, Berries {berries.Heal} HP over {berries.HealSeconds} s; the health bar followed; none used at full health");
                    _healVisits++;
                    _healStep = 0;
                    _session.FixedTicksPerFrame = RunOutTicksPerFrame;
                    return false;
                }
            }
            return true;
        }

        private void CheckHealthBar(string after)
        {
            var world = _session.Client.World!;
            var hud = _screen.Hud;
            Check(hud.HealthText == HudText.Health(world.LocalHp) && MathF.Abs(hud.HealthShown - world.LocalHp / world.LocalMaxHp) < 0.01f,
                $"after {after} at {world.LocalHp:0.0} HP the HUD shows \"{hud.HealthText}\" and a bar at {hud.HealthShown:0.00}");
        }

        private static int SlotOf(GridContainer container, ItemDef item)
        {
            foreach (var e in container.Entries)
                if (e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == item.Index) return e.X;
            return -1;
        }

        private void VerifyHeal()
        {
            Check(_healVisits == 1, $"healing was checked {_healVisits} times");
        }
    }
}
