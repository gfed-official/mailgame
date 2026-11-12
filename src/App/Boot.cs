using System;
using Godot;
using PerformativeMail.Client.InputHandling;
using PerformativeMail.Sim.Content;

namespace PerformativeMail.App
{
    /// <summary>
    /// Entry point (scenes/Main.tscn). M0 has no menu: it loads content and hosts a listen server on
    /// the test map, in the Lobby. User arguments (after "--"): --seed=N, --name=X, --quick (skip
    /// Lobby and Prep), --smoke, and with a window --shots=DIR to make the smoke save screenshots.
    /// </summary>
    public partial class Boot : Node
    {
        public override void _Ready()
        {
            uint seed = 1;
            string name = "Postie";
            bool smoke = false, quick = false;
            string? shots = null;
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--smoke") smoke = true;
                else if (arg == "--quick") quick = true;
                else if (arg.StartsWith("--shots=", StringComparison.Ordinal) && arg.Length > 8) shots = arg.Substring(8);
                else if (arg.StartsWith("--seed=", StringComparison.Ordinal) && uint.TryParse(arg.Substring(7), out uint s)) seed = s;
                else if (arg.StartsWith("--name=", StringComparison.Ordinal) && arg.Length > 7) name = arg.Substring(7);
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

            InputActions.Register();
            var session = new GameSession { Name = "Session" };
            session.StartHost(content, seed, name, quickStart: quick && !smoke);
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
