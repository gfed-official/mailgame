using System.Collections.Generic;
using PerformativeMail.Client;
using PerformativeMail.Client.Bot;
using PerformativeMail.Client.Meta;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>The onboarding tips of a first run (doc 09 §6; design choice 173) and the Lobby's "How to play" card.</summary>
    public class TipTests
    {
        private sealed class Game
        {
            public readonly ServerRuntime Server;
            public readonly ClientRuntime Client;
            public readonly DeliveryBot Bot;
            public readonly MemoryProfileStore Store = new MemoryProfileStore();
            public readonly ProfileTracker Profile;
            public readonly TipTracker Tips;

            public Game(MetaProfile profile, bool forced = false)
            {
                var hub = new LoopbackHub();
                Server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3 }, hub);
                Client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Bot");
                Bot = new DeliveryBot(Client);
                Profile = new ProfileTracker(Client, profile, Store);
                Tips = new TipTracker(Client, Profile, forced);
            }

            /// <summary>Plays until <paramref name="until"/> or the run is back in the Lobby; the tips seen, in order, once each.</summary>
            public List<string> Play(System.Func<bool> until)
            {
                var seen = new List<string>();
                bool started = false;
                for (int i = 0; i < 30 * 60 * 30; i++)
                {
                    Bot.Tick();
                    Client.Tick();
                    Server.Tick();
                    var world = Client.World;
                    if (world != null && world.Global.Phase != RunPhase.Lobby) started = true;
                    if (started && world?.Global.Phase == RunPhase.Lobby) break;
                    if (Tips.Current is { } tip && (seen.Count == 0 || seen[^1] != tip)) seen.Add(tip);
                    if (until()) break;
                }
                return seen;
            }
        }

        [Fact]
        public void AFirstRunShowsTheTipsInShiftOne_AndTheDoneOnesNeverAgain()
        {
            var profile = MetaProfile.Create("Bot");
            var game = new Game(profile);
            var seen = game.Play(() => game.Client.World?.Global.Shift > 1);

            // The bot takes mail (the Intake's tip goes), delivers (the address's), builds no belt: the belt's tip stays until
            // the last minute brings the raids' one, which the Payday then counts as read.
            Assert.Equal(new[] { TipTracker.Intake, TipTracker.Address, TipTracker.Belt, TipTracker.Raids }, seen);
            Assert.Equal(new[] { TipTracker.Intake, TipTracker.Address, TipTracker.Raids }, profile.TipsDone);
            Assert.Equal(profile.ToJson(), game.Store.Json);
            Assert.Equal(profile.TipsDone, MetaProfile.FromJson(profile.ToJson()).TipsDone);

            // Past shift 1 nothing more is said.
            Assert.Empty(game.Play(() => false));
            Assert.Equal(1, profile.Runs);

            // The next run of a profile with no run finished (here, the tips' list alone): only what is left, put away by the key.
            var again = MetaProfile.FromJson(profile.ToJson());
            again.Runs = 0;
            var second = new Game(again);
            bool dismissed = false;
            var left = second.Play(() =>
            {
                if (second.Tips.Current == TipTracker.Belt) dismissed = second.Tips.Dismiss();
                return dismissed;
            });
            Assert.Equal(new[] { TipTracker.Belt }, left);
            Assert.True(again.TipsDone.Contains(TipTracker.Belt));
            Assert.Null(second.Tips.Current);
            Assert.False(second.Tips.Dismiss());
        }

        [Fact]
        public void AProfileThatHasPlayedSeesNone_UnlessThePlaytestForcesThem()
        {
            var played = MetaProfile.Create("Bot");
            played.Runs = 3;
            var game = new Game(played);
            Assert.Empty(game.Play(() => game.Client.World?.Global.Shift > 1));
            // What it did counts all the same, should the tips ever be forced on it.
            Assert.Equal(new[] { TipTracker.Intake, TipTracker.Address }, played.TipsDone);

            var veteran = MetaProfile.Create("Bot");
            veteran.Runs = 3;
            veteran.TipsDone.AddRange(TipTracker.All);
            var forced = new Game(veteran, forced: true);
            var seen = forced.Play(() => forced.Client.World?.Global.Shift > 1);
            Assert.Equal(TipTracker.Intake, seen[0]);
            Assert.Contains(TipTracker.Address, seen);
        }

        [Fact]
        public void TheLobbyHintNamesTheHelpKey_AndTheCardSaysTheLoop()
        {
            var game = new Game(MetaProfile.Create("Bot"));
            for (int i = 0; i < 10; i++) { game.Client.Tick(); game.Server.Tick(); }
            var world = game.Client.World!;
            Assert.EndsWith("[H] how to play", HudText.PhaseHint(world));
            var lines = HudText.HowToPlayLines(world);
            Assert.Equal("HOW TO PLAY", lines[0]);
            Assert.Contains(lines, l => l.StartsWith("A run is 5 shifts"));
            Assert.Equal("[H] close", lines[^1]);
            Assert.Contains("[B], then [", HudText.Tip(world, TipTracker.Belt));
        }
    }
}
