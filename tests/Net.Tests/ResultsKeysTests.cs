using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>Doc 09 §2.11: the Results' "Play again (same seed)" and "New seed", the host's R and N.</summary>
    public class ResultsKeysTests
    {
        [Fact]
        public void TheHostPlaysAgainOnTheSameSeed_OrOnANewOne()
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3, Raids = false, Draft = false }, hub);
            var host = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            var guest = new ClientRuntime(Shared.Content, hub.CreateClient(), "Bo");
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { host.Tick(); guest.Tick(); server.Tick(); }
            }
            void ToResults()
            {
                // Nobody delivers: shift 1's quota is missed and the run is over.
                for (int i = 0; i < 30 * 60 * 10 && host.World!.Global.Phase != RunPhase.Results; i++)
                {
                    if (host.World.Global.Phase == RunPhase.Lobby) host.PressGo();
                    Run(1);
                }
                Run(5);
                Assert.Equal(RunPhase.Results, host.World!.Global.Phase);
            }
            Run(10);
            ToResults();
            var world = host.World!;
            Assert.Equal("[R] play again (same seed)   ·   [N] new seed   ·   [G] back to the lobby", HudText.PhaseHint(world));
            Assert.Equal(0u, guest.PlayAgain(false));
            Assert.Equal(1, world.Results!.LastShift);

            // R: through the Lobby and straight into the next run, on seed 3 again.
            Assert.NotEqual(0u, host.PlayAgain(false));
            Assert.Equal(0u, host.PlayAgain(true));
            Run(10);
            Assert.Equal(RunPhase.Prep, host.World!.Global.Phase);
            Assert.Equal(3u, host.World.Seed);
            Assert.Equal(3u, server.World.Settings.Seed);

            // N: the same, on a new seed of the same world.
            ToResults();
            string map = host.World!.Map.Id;
            Assert.NotEqual(0u, host.PlayAgain(true));
            Run(10);
            Assert.Equal(RunPhase.Prep, host.World!.Global.Phase);
            Assert.NotEqual(3u, server.World.Settings.Seed);
            Assert.Equal(server.World.Settings.Seed, host.World.Seed);
            Assert.Equal(map, host.World.Map.Id);
        }
    }
}
