using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The host's world over the loopback transport (doc 06 §5): every client builds the map the
    /// host picked from its id and seed and checks the hash, the Lobby's plates and the feed say
    /// which, only the host may pick, and who joins later comes into it.
    /// </summary>
    public class WorldFlowTests
    {
        [Fact]
        public void TheHostPicksTheIsland_EveryClientBuildsIt_AndALateJoinerComesIntoIt()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = true }, hub);
            var clients = new List<ClientRuntime>();
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++)
                {
                    foreach (var c in clients) c.Tick();
                    server.Tick();
                }
            }
            ClientRuntime Join(string name)
            {
                var c = new ClientRuntime(content, hub.CreateClient(), name);
                clients.Add(c);
                Run(10);
                return c;
            }
            var ada = Join("Ada");
            var bo = Join("Bo");
            var boFeed = new EventFeed { Content = content };
            bo.EventReceived += e => boFeed.OnEvent(bo.World!, e);
            int replaced = 0;
            ada.WorldReplaced += () => replaced++;

            Assert.Equal("Test Street (M0) · PM1-TEST-00000003", HudText.WorldTracker(ada.World!));
            Assert.Equal("Kit: Land   ·   [K]", HudText.KitTracker(ada.World!));
            Assert.True(ada.CanChooseWorld);
            Assert.False(bo.CanChooseWorld);
            Assert.Equal(0u, bo.CycleWorld());

            Assert.NotEqual(0u, ada.CycleWorld());
            Run(10);
            var sim = server.World;
            Assert.Equal(WorldGenerator.SmallIsland, sim.Map.Id);
            Assert.Equal(1, replaced);
            foreach (var c in clients)
            {
                Assert.Equal(sim.Map.MapHash, c.World!.Map.MapHash);
                Assert.Equal(3u, c.World.Seed);
                Assert.Equal(sim.MapChests.Select(x => x.Id), c.World.ChestContainerIds);
                Assert.Equal(ClientState.Joined, c.State);
            }
            Assert.Equal("Small Island · PM1-SMALL-00000003", HudText.WorldTracker(bo.World!));
            Assert.Contains(boFeed.Entries, e => e.Text.StartsWith("Ada chose Small Island · PM1-SMALL-00000003", System.StringComparison.Ordinal));

            // A new seed for it: another town.
            ulong first = sim.Map.MapHash;
            Assert.NotEqual(0u, ada.NewSeed());
            Run(10);
            Assert.NotEqual(first, sim.Map.MapHash);
            Assert.Equal(sim.Map.MapHash, bo.World!.Map.MapHash);
            Assert.Equal(sim.Settings.Seed, bo.World.Seed);
            Assert.Equal(2, replaced);
            // The prediction stands on the new pad with the server.
            Run(30);
            var truth = sim.GetPlayer(ada.World!.LocalId)!;
            Assert.Equal(truth.Move.X, ada.World.Local.X, 2);
            Assert.Equal(truth.Move.Z, ada.World.Local.Z, 2);

            // Who comes now is welcomed into it.
            var cara = Join("Cara");
            Assert.Equal(sim.Map.MapHash, cara.World!.Map.MapHash);

            // The run plays there: the Land kit's Logs are in the island's chest, as all of them see it.
            Assert.NotEqual(0u, ada.PressGo());
            Run(10);
            Assert.Equal(RunPhase.Prep, sim.Run.Phase);
            Assert.Equal(0u, ada.CycleWorld());
            Assert.Equal(10, sim.MapChests[0].Entries.Where(e => e.Stack.Kind == StackKind.Item).Sum(e => e.Stack.Count));
            Assert.Single(bo.World.Vehicles.Values);
        }
    }
}
