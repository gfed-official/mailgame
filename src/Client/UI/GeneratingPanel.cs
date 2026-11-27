using System.Collections.Generic;
using Godot;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim.Run;

namespace PerformativeMail.Client.UI
{
    /// <summary>
    /// The loading screen of the Generating phase (doc 01 §1, "None (loading screen)"): the world
    /// and its seed, a bar that fills as the carriers' clients have it drawn, a line for each of
    /// them, and how long the run waits at most. Every string comes from
    /// <see cref="HudText.GeneratingLines"/>.
    /// </summary>
    public partial class GeneratingPanel : PanelContainer
    {
        private readonly VBoxContainer _column;
        private readonly Label _title, _world, _footer;
        private readonly VBoxContainer _players;
        private readonly MeterBar _bar;

        /// <summary>Everything the panel says, top to bottom; empty while it is not shown.</summary>
        public string Text { get; private set; } = "";
        /// <summary>How full the bar is drawn, 0..1.</summary>
        public float Progress => _bar.Value;

        public GeneratingPanel()
        {
            Name = "Generating";
            Visible = false;
            MouseFilter = MouseFilterEnum.Ignore;
            ZIndex = 2;
            AnchorLeft = AnchorRight = 0.5f;
            AnchorTop = AnchorBottom = 0.36f;
            GrowHorizontal = GrowDirection.Both;
            GrowVertical = GrowDirection.Both;
            CustomMinimumSize = new Vector2(560f, 0f);
            // The theme's PanelContainer panel (content/ui/theme.tres).

            _column = new VBoxContainer { Name = "Column", MouseFilter = MouseFilterEnum.Ignore };
            _column.AddThemeConstantOverride("separation", 10);
            AddChild(_column);
            _title = Hud.MakeDisplayLabel("Title", 26, HorizontalAlignment.Center);
            _title.Modulate = Palette.Amber;
            _column.AddChild(_title);
            _world = Hud.MakeLabel("World", 16, HorizontalAlignment.Center);
            _column.AddChild(_world);
            _bar = new MeterBar { Name = "Bar", Fill = Palette.Match, Track = new Color(1f, 1f, 1f, 0.16f), CustomMinimumSize = new Vector2(0f, 12f) };
            _column.AddChild(_bar);
            _players = new VBoxContainer { Name = "Players", MouseFilter = MouseFilterEnum.Ignore };
            _players.AddThemeConstantOverride("separation", 2);
            _column.AddChild(_players);
            _footer = Hud.MakeLabel("Footer", 13, HorizontalAlignment.Center);
            _footer.Modulate = new Color(1f, 1f, 1f, 0.75f);
            _column.AddChild(_footer);
        }

        public void Refresh(ClientWorld world)
        {
            bool open = world.Global.Phase == RunPhase.Generating;
            Visible = open;
            if (!open)
            {
                Text = "";
                return;
            }
            List<string> lines = HudText.GeneratingLines(world);
            _title.Text = lines[0];
            _world.Text = lines[1];
            int people = lines.Count - 3;
            while (_players.GetChildCount() < people) _players.AddChild(Hud.MakeLabel("Player", 15, HorizontalAlignment.Center));
            for (int i = 0; i < _players.GetChildCount(); i++)
            {
                var label = (Label)_players.GetChild(i);
                label.Visible = i < people;
                if (!label.Visible) continue;
                label.Text = lines[2 + i];
                label.Modulate = lines[2 + i].EndsWith("ready", System.StringComparison.Ordinal) ? Palette.Match : Colors.White;
            }
            _footer.Text = lines[^1];
            _bar.Value = HudText.GeneratingProgress(world);
            Text = string.Join("\n", lines);
        }
    }
}
