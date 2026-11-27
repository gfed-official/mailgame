using System;
using System.Globalization;
using Godot;
using PerformativeMail.Client.InputHandling;
using PerformativeMail.Client.Meta;
using PerformativeMail.Client.Presentation;
using PerformativeMail.Sim;
using PerformativeMail.Sim.World;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.App
{
    /// <summary>
    /// Entry point (scenes/Main.tscn). M0 has no menu: it loads content and hosts a listen server on
    /// the test map, in the Lobby, or joins one. User arguments (after "--"): --seed=N, --name=X,
    /// --world=island (host: the seed's generated Small Island town, doc 02; --map=ID for a map by id),
    /// --quick (skip Lobby and Prep), --debug-world (the showcase map of design choice 119, everything
    /// built and running, the debug toggles on; with --smoke its own smoke, with --shots=DIR a picture
    /// of each station's bookmark, then quit), --profile=FILE (instead of user://profile.json),
    /// --port=N (host: the UDP port to listen on, 0 = any free one), --solo (host: do not listen),
    /// --join=ADDRESS[:PORT] (join that host instead of hosting), --bot (a DeliveryBot plays this
    /// client; with --wait-players=N a hosting bot starts the run at N players, with
    /// --quit-after=SECONDS it ends with a verdict), --smoke, and with a window --shots=DIR to make
    /// the smoke save screenshots, --low (no lamp lights, shadows, bloom or anti-aliasing), --lookdev=DIR (pictures of the world from a tour of viewpoints,
    /// then quit; --lookdev-views=name:ex,ey,ez:tx,ty,tz[:night];… for views of one's own).
    /// For live checks, on a host and on a --join guest alike, and all off unless given:
    /// --playtest-no-timer (the shift clock stands while this player is in the game; the host's
    /// "go" key ends the shift), --playtest-mailbox (this player starts, and starts every shift,
    /// before a mailbox with mail for it), --shift=N (the run starts at shift N, with that shift's
    /// quota and raid), --no-quota (a missed quota does not end the run), --raid (the night raid
    /// comes five seconds into every shift, also in shift 1) and --playtest-package (this player
    /// has four Small Packages for the mail cannon when a shift starts and when they are back on
    /// their feet), --playtest-wallet=N (runs start with N gold in the team's wallet; asked for in a run
    /// under way, the wallet is topped up to it at once), --playtest-materials (this player's pockets are topped up
    /// to 20 Logs and 40 Stone when a shift's Prep or Delivery starts and when they are back on their feet),
    /// --playtest-stamps (as host, every tier of the Postage Stamps is open whatever the Postal Rank), --event=ID (as host, that
    /// run event of content/events comes in every shift it may, doc 10 §6). With --smoke they are not played with but checked:
    /// "--smoke --shift=2 --no-quota --raid --playtest-package" is the smoke of the raid playtest.
    /// </summary>
    public partial class Boot : Node
    {
        private const string DefaultName = "Postie";

        public override void _Ready()
        {
            // The UI's faces into its theme before the first control is made (UI polish plan item 2).
            Client.UI.UiTheme.Load();
            uint seed = 1;
            string? name = null, shots = null, profilePath = null, join = null, lookDev = null, lookDevViews = null;
            bool smoke = false, quick = false, debugWorld = false, solo = false, bot = false, low = false;
            int? port = null;
            int waitPlayers = 1;
            double quitAfter = 0;
            var playtest = PlaytestFlags.None;
            int startShift = 0, startWallet = 0;
            string mapId = "test_street";
            string? forcedEvent = null;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--smoke") smoke = true;
                else if (arg == "--playtest-no-timer") playtest |= PlaytestFlags.NoTimer;
                else if (arg == "--playtest-mailbox") playtest |= PlaytestFlags.AtMailbox;
                else if (arg == "--no-quota") playtest |= PlaytestFlags.NoQuota;
                else if (arg == "--raid") playtest |= PlaytestFlags.RaidNow;
                else if (arg == "--playtest-package") playtest |= PlaytestFlags.Packages;
                else if (arg == "--playtest-materials") playtest |= PlaytestFlags.Materials;
                else if (arg == "--playtest-stamps" || arg == "--playtest-unlocks") playtest |= PlaytestFlags.AllUnlocks;
                else if (arg.StartsWith("--shift=", StringComparison.Ordinal) && int.TryParse(arg.Substring(8), out int n) && n >= 1 && n <= byte.MaxValue) startShift = n;
                // Gold, as the HUD shows it ("500"); the sim's amounts are one gold a unit (design choice 124).
                else if (arg.StartsWith("--playtest-wallet=", StringComparison.Ordinal)
                    && int.TryParse(arg.Substring(18), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int gold)
                    && gold > 0)
                    startWallet = Math.Min(PlaytestMask.MaxWallet, gold);
                else if (arg.StartsWith("--event=", StringComparison.Ordinal) && arg.Length > 8) forcedEvent = arg.Substring(8);
                else if (arg == "--quick") quick = true;
                // Design choice 119: the showcase map, everything built and running, the debug toggles on.
                else if (arg == "--debug-world") { debugWorld = true; mapId = "debug_world"; }
                // Doc 02: the seed's own Small Island town instead of the test street.
                else if (arg == "--world=island") mapId = WorldGenerator.SmallIsland;
                else if (arg.StartsWith("--map=", StringComparison.Ordinal) && arg.Length > 6) mapId = arg.Substring(6);
                else if (arg == "--solo") solo = true;
                else if (arg == "--bot") bot = true;
                else if (arg == "--low") low = true;
                else if (arg.StartsWith("--wait-players=", StringComparison.Ordinal) && int.TryParse(arg.Substring(15), out int w)) waitPlayers = w;
                else if (arg.StartsWith("--quit-after=", StringComparison.Ordinal) && double.TryParse(arg.Substring(13), NumberStyles.Float, CultureInfo.InvariantCulture, out double q)) quitAfter = q;
                else if (arg.StartsWith("--port=", StringComparison.Ordinal) && int.TryParse(arg.Substring(7), out int p) && p >= 0 && p <= 65535) port = p;
                else if (arg.StartsWith("--join=", StringComparison.Ordinal) && arg.Length > 7) join = arg.Substring(7);
                else if (arg.StartsWith("--shots=", StringComparison.Ordinal) && arg.Length > 8) shots = arg.Substring(8);
                else if (arg.StartsWith("--lookdev=", StringComparison.Ordinal) && arg.Length > 10) lookDev = arg.Substring(10);
                else if (arg.StartsWith("--lookdev-views=", StringComparison.Ordinal) && arg.Length > 16) lookDevViews = arg.Substring(16);
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

            // A software renderer (the smoke under xvfb) draws the full look at about a frame a second.
            string adapter = RenderingServer.GetVideoAdapterName() ?? "";
            WorldView.Cheap = low || (smoke && adapter.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase));

            InputActions.Register();
            // The smoke without playtest flags checks the game as it ships; with them, the flags themselves.
            bool playtesting = playtest != PlaytestFlags.None || startShift != 0 || startWallet != 0;
            if (playtesting) GD.Print($"[boot] playtest: {playtest}{(startShift != 0 ? $", shift {startShift}" : "")}{(startWallet != 0 ? $", wallet {startWallet} g" : "")}");
            var session = new GameSession { Name = "Session", Playtest = playtest, StartShift = startShift, StartWallet = startWallet };
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
                if (forcedEvent != null && !content.TryRunEvent(forcedEvent, out _))
                {
                    GD.PrintErr($"[boot] --event={forcedEvent}: no such run event (content/events)");
                    GetTree().Quit(2);
                    return;
                }
                if (forcedEvent != null) GD.Print($"[boot] playtest: the run event {forcedEvent} in every shift it may come");
                session.StartHost(content, seed, profile, store, quickStart: (quick && !smoke) || debugWorld, listenPort: listen, mapId: mapId, forcedEvent: forcedEvent);
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
                screen.Setup(session, humanInput: !smoke && !bot && lookDev == null);
                AddChild(screen);
                if (lookDev != null && !smoke)
                {
                    var tour = new LookDev { Name = "LookDev", Dir = lookDev, Extra = lookDevViews, Server = session.Server?.World };
                    AddChild(tour);
                    tour.Setup(screen);
                    return;
                }
                if (bot && !smoke)
                {
                    botDriver = new BotDriver { Name = "Bot", QuitAfterSeconds = quitAfter };
                    botDriver.Setup(session, waitPlayers);
                    AddChild(botDriver);
                }
                if (debugWorld && (smoke || shots != null))
                {
                    var tourer = new DebugWorldDriver { Name = "DebugWorldDriver", ShotDir = shots, Smoke = smoke };
                    tourer.Setup(session, screen);
                    AddChild(tourer);
                    return;
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
