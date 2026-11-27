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
            // The e5be644 playtest's N7: the map's head on the hint's line.
            Check(map.PanelArea().Position.Y >= _screen.Hud.HintBottom, $"the map's panel starts at {map.PanelArea().Position.Y:0}, on the hint (to {_screen.Hud.HintBottom:0})");
            Check(map.TitleText.StartsWith("MAP · ", StringComparison.Ordinal), $"the map's title reads \"{map.TitleText}\"");
            // The raiders' way in (doc 02 §3.10) is marked: the east end of Larch Lane, no raid by it in shift 1.
            Check(map.EdgesDrawn.Count == 1 && map.EdgesDrawn[0].Edge.Index == 0 && !map.EdgesDrawn[0].Raid
                    && map.EdgesDrawn[0].Edge.Name == "the east, along Larch Lane",
                $"the map marks {map.EdgesDrawn.Count} ways in for the raiders");
            // The Resources chip (doc 09 §2.6): off at first, on it draws every node with something left.
            Check(map.NodesDrawn == 0 && !map.Layers.On(Client.MapChip.Resources), $"the map drew {map.NodesDrawn} nodes with Resources off");
            map.Layers.Toggle(Client.MapChip.Resources);
            map.Layers.Update(world, 0.0);
            int left = 0;
            for (int i = 0; i < world.Map.Props.Count; i++) if (world.Nodes.Def(i) != null && !world.Nodes.Depleted(i)) left++;
            Check(map.Layers.Nodes.Count == left && left > 0, $"Resources on lists {map.Layers.Nodes.Count} nodes of the {left} with something left");
            Check(map.Layers.MailHouses.Count > 0, "Mail on rings no house though mail is carried");
            map.Layers.Toggle(Client.MapChip.Resources);
            int underMap = CheckLabelsOffMap();
            Shot("map");
            GD.Print($"[smoke] map: up with mail in the hotbar, \"{map.RouteText}\" in {guide.Points.Count} points; {underMap} world labels under it kept off");
            _mapStep = 2;
        }

        /// <summary>No world label (a carrier's name, a house number) is drawn on the map while it is up: its back lets the view through.</summary>
        private int CheckLabelsOffMap()
        {
            var area = _screen.Map.PanelArea();
            var camera = _screen.GetViewport().GetCamera3D();
            int under = 0;
            foreach (var label in Client.Presentation.SizedLabel3D.Live)
            {
                if (!label.IsVisibleInTree() || camera == null || camera.IsPositionBehind(label.GlobalPosition)) continue;
                if (!area.HasPoint(camera.UnprojectPosition(label.GlobalPosition))) continue;
                under++;
                Check(label.Target == 0f, $"the world label \"{label.Text}\" is drawn on the map");
            }
            return under;
        }
    }
}
