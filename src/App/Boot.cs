using System;
using Godot;
using PerformativeMail.Client.InputHandling;
using PerformativeMail.Client.Meta;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.App
{
    /// <summary>
    /// Entry point (scenes/Main.tscn). M0 has no menu: it loads content and hosts a listen server on
    /// the test map, in the Lobby. User arguments (after "--"): --seed=N, --name=X, --quick (skip
    /// Lobby and Prep), --profile=FILE (instead of user://profile.json), --smoke, and with a window
    /// --shots=DIR to make the smoke save screenshots.
    /// </summary>
    public partial class Boot : Node
    {
        private const string DefaultName = "Postie";

        public override void _Ready()
        {
            uint seed = 1;
            string? name = null, shots = null, profilePath = null;
            bool smoke = false, quick = false;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--smoke") smoke = true;
                else if (arg == "--quick") quick = true;
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
            GD.Print($"[boot] content {content.ContentHash:x8}, seed {seed}, {(smoke ? "smoke" : "host")}");

            // The smoke never touches the player's own profile: it keeps one in memory unless told a file.
            IProfileStore store = profilePath != null ? new FileProfileStore(profilePath)
                : smoke ? new MemoryProfileStore()
                : new FileProfileStore(ProjectSettings.GlobalizePath("user://profile.json"));
            var profile = ProfileTracker.LoadOrCreate(store, DefaultName, out string? problem);
            if (problem != null) GD.PrintErr($"[profile] could not read the profile ({problem}); it was set aside and a new one started");
            if (name != null) profile.DisplayName = name;
            else if (profile.DisplayName.Length == 0) profile.DisplayName = DefaultName;
            int perRank = content.Balance.GetInt("rankXpPerRank");
            GD.Print($"[boot] profile {profile.DisplayName}: rank {profile.Rank(perRank)}, {profile.RankXp} XP, {profile.Runs} runs");

            InputActions.Register();
            var session = new GameSession { Name = "Session" };
            session.StartHost(content, seed, profile, store, quickStart: quick && !smoke);
            AddChild(session);
            session.Joined += () =>
            {
                GD.Print($"[boot] joined as {session.Client.World!.LocalId} on {session.Client.World.Map.Name}");
                var screen = new GameScreen { Name = "Screen" };
                screen.Setup(session, humanInput: !smoke);
                AddChild(screen);
                if (!smoke) return;
                var driver = new SmokeDriver { Name = "Smoke", ShotDir = shots };
                driver.Setup(session, screen);
                AddChild(driver);
            };
        }
    }
}
