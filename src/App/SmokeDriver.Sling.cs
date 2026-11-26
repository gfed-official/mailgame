using System;
using System.Linq;
using Godot;
using PerformativeMail.Client;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;

namespace PerformativeMail.App
{
    /// <summary>
    /// The Slingshot (doc 05 §1.2; design choice 143), in the combat smoke once the Balloon has bombed
    /// the depot: the host takes it in the hand with a Stone in the pockets — the weapons' line reads
    /// "[LMB] slingshot, 5 shots …" — and holds attack at the Balloon, which the hammer cannot reach,
    /// until a Stone is drawn striking it and the server has it hurt.
    /// </summary>
    public partial class SmokeDriver
    {
        private enum SlingStep { None, Arming, Loosing, Done }

        private SlingStep _slingStep;
        private int _slingStones;

        /// <summary>A frame of the Balloon step after its bomb. Returns true while the Slingshot is at work.</summary>
        private bool StepSlingAtBalloon()
        {
            if (_slingStep == SlingStep.Done) return false;
            var client = _session.Client;
            var world = client.World!;
            var server = _session.Server!.World;
            var hud = _screen.Hud;
            var player = server.GetPlayer(world.LocalId)!;
            var sling = world.Content.Item("slingshot");
            var balloon = _giant;
            ViewRay.Aim(world.Local, balloon.X, balloon.Def.Altitude, balloon.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
            if (_slingStep == SlingStep.None)
            {
                Check(server.Give(player.Hotbar, ItemStack.OfItem(sling, 1)), "no room in the hotbar for the Slingshot");
                server.Give(player.Inventory, ItemStack.OfItem(world.Content.Item("stone"), 1));
                client.Input.Buttons = InputButtons.None;
                _fightFrames = 0;
                _slingStep = SlingStep.Arming;
                return true;
            }
            if (_slingStep == SlingStep.Arming)
            {
                var held = world.Hotbar.Entries.FirstOrDefault(e => e.Stack.Kind == StackKind.Item && e.Stack.DefIndex == sling.Index);
                if (held == null) return true;
                client.Input.HeldSlot = (byte)held.X;
                if (_fightFrames < 8) return true;
                hud.Refresh();
                Check(hud.WeaponsText.StartsWith($"[LMB] slingshot, {client.SlingShots} shots", StringComparison.Ordinal) && client.SlingShots >= 5,
                    $"with the Slingshot in the hand the weapons line reads \"{hud.WeaponsText}\" ({client.SlingShots} shots)");
                _slingStones = _screen.Effects.SlingStonesThatHit;
                client.Input.Buttons = InputButtons.Attack;
                _slingStep = SlingStep.Loosing;
                return true;
            }
            // SlingStep.Loosing: until a Stone is drawn striking the Balloon and the server has it hurt.
            if (_screen.Effects.SlingStonesThatHit == _slingStones || balloon.Hp >= balloon.MaxHp) return true;
            client.Input.Buttons = InputButtons.None;
            Check(client.LocalSlingShots > 0, "no Stone was loosed through the client's own input");
            Check(world.Enemies.TryGetValue((ushort)balloon.Id.Value, out var seen) && seen.Hp < 255, "the client does not see the Balloon hurt");
            Shot("slingshot");
            GD.Print($"[smoke] slingshot: a Stone loosed at the balloon {balloon.Def.Altitude:0} m up struck it ({balloon.Hp:0} of {balloon.MaxHp:0} HP left), {_screen.Effects.SlingStonesDrawn} stones drawn; \"{hud.WeaponsText}\"");
            client.Input.HeldSlot = 0;
            _slingStep = SlingStep.Done;
            return false;
        }
    }
}
