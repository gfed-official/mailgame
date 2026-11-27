using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Net;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>Doc 09 §2.9: the Payday card's "upcoming district preview and mail forecast for the next shift".</summary>
    public class PaydayPreviewTests
    {
        private static (ClientRuntime Client, System.Action<int> Run) Start(string mapId, uint seed, int shift)
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = seed, MapId = mapId, Raids = false, Draft = false }, hub);
            var ada = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada", PlaytestFlags.NoQuota | PlaytestFlags.NoTimer, shift);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { ada.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            Assert.Equal(RunPhase.Delivery, ada.World!.Global.Phase);
            // The clock stood (--playtest-no-timer): the host's G ends the shift.
            Assert.NotEqual(0u, ada.PressGo());
            for (int i = 0; i < 300 && ada.World.Global.Phase != RunPhase.Payday; i++) Run(1);
            Assert.Equal(RunPhase.Payday, ada.World.Global.Phase);
            Run(5);
            return (ada, Run);
        }

        [Fact]
        public void AtPayday_TheCardSaysWhatOpensNextShift_ItsHousesStreetsAndNewMail()
        {
            var (ada, _) = Start(WorldGenerator.SmallIsland, 5, 1);
            var w = ada.World!;
            var lines = HudText.SummaryLines(w, w.LastSummary!);
            int houses = w.Map.Destinations.Count(d => d.Type.Id == "house" && w.Map.OpensAt(d) == 2);
            string next = Assert.Single(lines, l => l.StartsWith("Next: ", System.StringComparison.Ordinal));
            Assert.StartsWith($"Next: District 2 opens in shift 2  ·  {houses} houses on ", next);
            // Postcards and Large Packages first come in shift 2 (content's unlockShift).
            Assert.Equal("New mail: Postcards, Large Packages", lines[^1]);
            Assert.Same(next, lines[^2]);
        }

        [Fact]
        public void OnALargeIsland_TheFarTownSaysItIsReachedByTheFerry()
        {
            uint seed = Enumerable.Range(1, 20).Select(s => (uint)s).First(s => LargeIslandGenerator.Generate(Shared.Content, s).FerryLanes.Count > 0);
            var (ada, _) = Start(LargeIslandGenerator.LargeIsland, seed, 2);
            var w = ada.World!;
            var upcoming = HudText.Upcoming(w, w.LastSummary!);
            Assert.StartsWith("Next: District 3 opens in shift 3", upcoming[0]);
            Assert.StartsWith("By the ferry", upcoming[1]);
            // Cargo's first shift is 3, but nothing takes it before the docks open in shift 4.
            Assert.DoesNotContain("Cargo", upcoming[1]);
        }
    }
}
