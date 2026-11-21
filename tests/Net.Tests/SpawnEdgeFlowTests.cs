using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The raid's ways in on a client (doc 05 §3.1): "Raid incoming from the east" with the way(s)
    /// it comes by, in the banner and the feed, and marked on the map (doc 09 §2.6).
    /// </summary>
    public class SpawnEdgeFlowTests
    {
        [Fact]
        public void TheRaidsWaysIn_ReachTheClient_AreNamed_AndMarkedOnTheMap()
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 1, MapId = WorldGenerator.SmallIsland, Draft = false }, hub);
            var client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada");
            var feed = new EventFeed();
            client.EventReceived += e => feed.OnEvent(client.World!, e);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); server.Tick(); }
            }
            Run(10);
            var world = client.World!;
            var sim = server.World;
            // The client built the same ways in from the seed.
            Assert.Equal(sim.Map.SpawnEdges.Select(e => (e.X, e.Z, e.UnlockShift)), world.Map.SpawnEdges.Select(e => (e.X, e.Z, e.UnlockShift)));
            // In the Lobby the map marks the way in open from the first shift, no raid by it.
            Assert.Equal(new[] { (0, false) }, MapGuide.Edges(world).Select(m => (m.Edge.Index, m.Raid)));

            sim.SetPlaytest(sim.GetPlayer(world.LocalId)!, PlaytestFlags.RaidNow, 2);
            Assert.NotEqual(0u, client.PressGo());
            Run(5);
            Assert.NotEqual(0u, client.PressGo());
            Run(5);
            Assert.Equal((RunPhase.Delivery, RaidState.Warning), (world.Global.Phase, world.Raid));
            Assert.Equal(((sbyte)sim.RaidEdgeA, (sbyte)sim.RaidEdgeB), (world.Global.RaidEdgeA, world.Global.RaidEdgeB));

            // Seed 1's shift 2: the east and the north are open, and the raid comes by both.
            var a = world.Map.SpawnEdges[sim.RaidEdgeA];
            var b = world.Map.SpawnEdges[sim.RaidEdgeB];
            Assert.Equal(new[] { "east", "north" }, new[] { a.Direction, b.Direction }.OrderBy(d => d));
            Assert.StartsWith($"Raid incoming from the {a.Direction} and the {b.Direction}   ·   00:0", HudText.RaidBanner(world));
            Assert.Contains($"Raid incoming from {a.Name}, later also {b.Name}", feed.Entries.Select(e => e.Text));
            Assert.Equal(new[] { (0, true), (1, true) }, MapGuide.Edges(world).Select(m => (m.Edge.Index, m.Raid)));

            // The raid's first pulse comes by its first way in.
            Run(6 * 30);
            Assert.Equal(RaidState.Active, world.Raid);
            Assert.NotEmpty(world.Enemies);
            Assert.All(sim.Enemies, e => Assert.Equal(sim.RaidEdgeA, e.Edge));
        }
    }
}
