using System;
using Godot;
using PerformativeMail.Sim.Inventory;

namespace PerformativeMail.App
{
    /// <summary>
    /// The map in the smoke's first Delivery (doc 09 §2.6): once the bot carries mail in the
    /// hotbar the map goes up, and it must name the house of that mail and draw the route there
    /// along the routing graph — no shorter than the crow flies — under the HUD's top plate,
    /// inside the screen; then it goes down again.
    /// </summary>
    public partial class SmokeDriver
    {
        private int _mapStep, _mapFrames;

        private void StepMap()
        {
            var world = _session.Client.World!;
            var map = _screen.Map;
            if (_mapStep == 0)
            {
                bool carrying = false;
                foreach (var e in world.Hotbar.Entries) carrying |= e.Stack.Kind == StackKind.Mail;
                if (!carrying || world.Local.Dead) return;
                map.Toggle();
                _mapStep = 1;
                _mapFrames = 0;
                return;
            }
            if (_mapStep == 2)
            {
                // A frame after the picture was asked for, so that it shows the map.
                map.Toggle();
                Check(!map.Visible, "the map did not go down again");
                _mapStep = 3;
                return;
            }
            if (_mapStep != 1 || ++_mapFrames < 3) return;
            var guide = map.Guide;
            var target = guide.Target;
            Check(map.Visible, "the map did not go up");
            Check(target != null, $"the map shows no route though mail is carried: \"{map.RouteText}\"");
            if (target != null)
            {
                bool carried = false;
                foreach (var e in world.Hotbar.Entries) carried |= e.Stack.Kind == StackKind.Mail && e.Stack.Address == target.Address;
                Check(carried, $"the map's route runs to {world.Map.FormatAddress(target.Address)}, which nobody carries");
                float dx = target.MailboxX - world.Local.X, dz = target.MailboxZ - world.Local.Z;
                Check(guide.Points.Count >= 2 && guide.Metres >= MathF.Sqrt(dx * dx + dz * dz) - 0.5f, $"the map's route is {guide.Metres:0} m in {guide.Points.Count} points");
                Check(map.RouteText.StartsWith("To " + world.Map.FormatAddress(target.Address), StringComparison.Ordinal), $"the map's line reads \"{map.RouteText}\"");
            }
            var screen = map.GetViewportRect();
            Check(screen.Encloses(map.MapRect), $"the map {map.MapRect} is not inside the screen {screen}");
            float plateBottom = _screen.Hud.TopPlate.GetGlobalRect().End.Y;
            Check(map.MapRect.Position.Y >= plateBottom, $"the map's top {map.MapRect.Position.Y:0} is under the top plate (to {plateBottom:0})");
            Check(map.TitleText.StartsWith("MAP · ", StringComparison.Ordinal), $"the map's title reads \"{map.TitleText}\"");
            Shot("map");
            GD.Print($"[smoke] map: up with mail in the hotbar, \"{map.RouteText}\" in {guide.Points.Count} points");
            _mapStep = 2;
        }
    }
}
