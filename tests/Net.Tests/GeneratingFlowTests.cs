using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The Generating phase on the clients (doc 01 §1): each says it is ready once its screen has
    /// the world drawn; the loading panel's lines and bar follow who has; Prep comes when all have.
    /// </summary>
    public class GeneratingFlowTests
    {
        [Fact]
        public void PrepWaits_ForTheClientStillDrawingTheWorld()
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 5, MapId = WorldGenerator.SmallIsland, Draft = false, Raids = false, Generating = true }, hub);
            var host = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            var guest = new ClientRuntime(Shared.Content, hub.CreateClient(), "Bo");
            bool guestDrawn = false;
            guest.WorldDrawn = () => guestDrawn;
            var feed = new EventFeed();
            host.EventReceived += e => feed.OnEvent(host.World!, e);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { host.Tick(); guest.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, host.PressGo());
            Run(15);
            var world = host.World!;
            Assert.Equal(RunPhase.Generating, world.Global.Phase);
            Assert.Equal(RunPhase.Generating, server.World.Run.Phase);
            Assert.Equal("GENERATING", HudText.ShiftAndPhase(world));
            // The loading panel says it; the feed's five lines are kept for the kits handed out.
            Assert.DoesNotContain(feed.Entries, e => e.Text.Contains("enerating"));

            // The host's headless client has it at once; the guest's screen is still drawing.
            var lines = HudText.GeneratingLines(world);
            Assert.Equal("GENERATING", lines[0]);
            Assert.StartsWith($"Small Island · {HudText.SeedString(WorldGenerator.SmallIsland, 5)}   ·   ", lines[1]);
            Assert.Contains("Ada (you)   ·   ready", lines);
            Assert.Contains("Bo   ·   building the world…", lines);
            Assert.StartsWith("The first Prep starts when everyone has the world, in 00:", lines[^1]);
            Assert.Equal(0.5f, HudText.GeneratingProgress(world));
            Assert.StartsWith("The world is ready here   ·   waiting for 1 of 2", HudText.PhaseHint(world));
            Assert.Equal("Building Small Island", HudText.PhaseHint(guest.World!));

            guestDrawn = true;
            Run(15);
            Assert.Equal(RunPhase.Prep, world.Global.Phase);
            Assert.Equal(RunPhase.Prep, guest.World!.Global.Phase);
            // Prep's own Ready starts afresh: nobody is ready for the shift yet.
            Assert.False(world.LocalReady || guest.World.LocalReady);
        }

        /// <summary>
        /// The ec62063 playtest's N23: in a real game (the real-time driver, not the tests' ticks) the client never said
        /// it had the world drawn, so Generating always ran out its 10 s with the card over the open map.
        /// </summary>
        [Fact]
        public void TheRealTimeClientSaysItHasTheWorld_AndPrepBeginsAtOnce()
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 5, MapId = WorldGenerator.SmallIsland, Draft = false, Raids = false, Generating = true }, hub);
            var host = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            const double frame = 1.0 / 60.0;
            void Run(int frames)
            {
                for (int i = 0; i < frames; i++) { host.Update(frame); server.Update(frame); }
            }
            Run(30);
            Assert.NotEqual(0u, host.PressGo());
            // Well under a second, not the 10 s deadline.
            Run(50);
            Assert.Equal(RunPhase.Prep, server.World.Run.Phase);
            Assert.Equal(RunPhase.Prep, host.World!.Global.Phase);
        }
    }
}
