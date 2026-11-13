using System;
using System.Globalization;
using Godot;
using PerformativeMail.Client.InputHandling;
using PerformativeMail.Client.Meta;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.App
{
    /// <summary>
    /// Entry point (scenes/Main.tscn). M0 has no menu: it loads content and hosts a listen server on
    /// the test map, in the Lobby, or joins one. User arguments (after "--"): --seed=N, --name=X,
    /// --quick (skip Lobby and Prep), --profile=FILE (instead of user://profile.json),
    /// --port=N (host: the UDP port to listen on, 0 = any free one), --solo (host: do not listen),
    /// --join=ADDRESS[:PORT] (join that host instead of hosting), --bot (a DeliveryBot plays this
    /// client; with --wait-players=N a hosting bot starts the run at N players, with
    /// --quit-after=SECONDS it ends with a verdict), --smoke, and with a window --shots=DIR to make
    /// the smoke save screenshots.
    /// </summary>
    public partial class Boot : Node
    {
        private const string DefaultName = "Postie";

        public override void _Ready()
        {
            uint seed = 1;
            string? name = null, shots = null, profilePath = null, join = null;
            bool smoke = false, quick = false, solo = false, bot = false;
            int? port = null;
            int waitPlayers = 1;
            double quitAfter = 0;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--smoke") smoke = true;
                else if (arg == "--quick") quick = true;
                else if (arg == "--solo") solo = true;
                else if (arg == "--bot") bot = true;
                else if (arg.StartsWith("--wait-players=", StringComparison.Ordinal) && int.TryParse(arg.Substring(15), out int w)) waitPlayers = w;
                else if (arg.StartsWith("--quit-after=", StringComparison.Ordinal) && double.TryParse(arg.Substring(13), NumberStyles.Float, CultureInfo.InvariantCulture, out double q)) quitAfter = q;
                else if (arg.StartsWith("--port=", StringComparison.Ordinal) && int.TryParse(arg.Substring(7), out int p) && p >= 0 && p <= 65535) port = p;
                else if (arg.StartsWith("--join=", StringComparison.Ordinal) && arg.Length > 7) join = arg.Substring(7);
                else if (arg.StartsWith("--shots=", StringComparison.Ordinal) && arg.Length > 8) shots = arg.Substring(8);
                else if (arg.StartsWith("--seed=", StringComparison.Ordinal) && uint.TryParse(arg.Substring(7), out uint s)) seed = s;
                else if (arg.StartsWith("--name=", StringComparison.Ordinal) && arg.Length > 7) name = arg.Substring(7);
                else if (arg.StartsWith("--profile=", StringComparison.Ordinal) && arg.Length > 10) profilePath = arg.Substring(10);
            }

            ContentDb content;
            try
            {
                content = ContentDb.Load(new GodotContentSource());
            }
            catch (ContentException e)
            {
                foreach (string error in e.Errors) GD.PrintErr("[content] ", error);
                GetTree().Quit(2);
                return;
            }
            GD.Print($"[boot] content {content.ContentHash:x8}, seed {seed}, {(smoke ? "smoke" : join != null ? $"join {join}" : "host")}");

            // The smoke and the bots never touch the player's own profile: they keep one in memory unless told a file.
            IProfileStore store = profilePath != null ? new FileProfileStore(profilePath)
                : smoke || bot ? new MemoryProfileStore()
                : new FileProfileStore(ProjectSettings.GlobalizePath("user://profile.json"));
            var profile = ProfileTracker.LoadOrCreate(store, DefaultName, out string? problem);
            if (problem != null) GD.PrintErr($"[profile] could not read the profile ({problem}); it was set aside and a new one started");
            if (name != null) profile.DisplayName = name;
            else if (profile.DisplayName.Length == 0) profile.DisplayName = DefaultName;
            int perRank = content.Balance.GetInt("rankXpPerRank");
            GD.Print($"[boot] profile {profile.DisplayName}: rank {profile.Rank(perRank)}, {profile.RankXp} XP, {profile.Runs} runs");

            InputActions.Register();
            var session = new GameSession { Name = "Session" };
            if (join != null && !smoke)
            {
                ParseAddress(join, out string address, out int joinPort);
                if (!session.StartRemote(content, address, joinPort, profile, store))
                {
                    GetTree().Quit(3);
                    return;
                }
                GD.Print($"[boot] joining {address}:{joinPort}");
            }
            else
            {
                // The smoke takes any free port, so two of them can run side by side.
                int listen = solo ? -1 : port ?? (smoke ? 0 : GameSession.DefaultPort);
                session.StartHost(content, seed, profile, store, quickStart: quick && !smoke, listenPort: listen);
            }
            AddChild(session);
            BotDriver? botDriver = null;
            session.ConnectionEnded += reason =>
            {
                GD.Print($"[boot] {reason}");
                if (botDriver != null) botDriver.Finish(reason);
                // Refused or never answered: there is no world to show and no menu to go back to yet.
                // After joining, the HUD carries the "Host lost" banner over the frozen world.
                else if (session.HasJoined) return;
                else if (DisplayServer.GetName() == "headless") GetTree().Quit(3);
                else ShowNotice(reason);
            };
            session.Joined += () =>
            {
                GD.Print($"[boot] joined as {session.Client.World!.LocalId} on {session.Client.World.Map.Name}");
                var screen = new GameScreen { Name = "Screen" };
                screen.Setup(session, humanInput: !smoke && !bot);
                AddChild(screen);
                if (bot && !smoke)
                {
                    botDriver = new BotDriver { Name = "Bot", QuitAfterSeconds = quitAfter };
                    botDriver.Setup(session, waitPlayers);
                    AddChild(botDriver);
                }
                if (!smoke) return;
                var driver = new SmokeDriver { Name = "Smoke", ShotDir = shots };
                driver.Setup(session, screen);
                AddChild(driver);
            };
        }

        /// <summary>"host", "host:port" or "[v6]:port"; without a port, the default one.</summary>
        private static void ParseAddress(string text, out string address, out int port)
        {
            address = text;
            port = GameSession.DefaultPort;
            int colon = text.LastIndexOf(':');
            // More than one colon and no brackets is a bare IPv6 address, not host:port.
            bool bare6 = colon >= 0 && text.IndexOf(':') != colon && !text.StartsWith('[');
            if (colon > 0 && !bare6 && int.TryParse(text.Substring(colon + 1), out int p) && p > 0 && p <= 65535)
            {
                address = text.Substring(0, colon);
                port = p;
            }
            address = address.TrimStart('[').TrimEnd(']');
        }

        /// <summary>A line in the middle of an otherwise empty window.</summary>
        private void ShowNotice(string text)
        {
            var layer = new CanvasLayer { Name = "Notice" };
            AddChild(layer);
            var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            label.AddThemeFontSizeOverride("font_size", 28);
            layer.AddChild(label);
        }
    }
}
