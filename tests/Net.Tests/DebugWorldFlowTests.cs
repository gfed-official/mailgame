using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The debug world over the loopback transport (design choice 119): the client that joins it
    /// mirrors the whole showcase — every construct with its filters, the truck and the boat, both
    /// crews — keeps its belts and containers in step with the server while the mail runs, sees
    /// the clock stand and the wallet full, and may build anything out of empty pockets.
    /// </summary>
    public class DebugWorldFlowTests
    {
        [Fact]
        public void AClientMirrorsTheShowcase_AndKeepsInStepWhileTheMailRuns()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, MapId = "debug_world", QuickStart = true, StartingKits = true }, hub);
            var client = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 0);
            var feed = new EventFeed();
            client.EventReceived += e => feed.OnEvent(client.World!, e);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); server.Tick(); }
            }
            Run(30);
            var sim = server.World;
            var a = client.World!;
            Assert.Equal("debug_world", a.Map.Id);
            Assert.Equal((RunPhase.Delivery, 5), (a.Global.Phase, (int)a.Global.Shift));
            Assert.True(a.Global.ClockStopped);

            Run(90 * 30);
            Assert.True(sim.Showcase!.AllDone, sim.Showcase.Missing());
            Assert.Equal(sim.Constructs.All.Count, a.Constructs.All.Count);
            foreach (var c in sim.Constructs.All)
            {
                var mine = a.Constructs.At(c.Tx, c.Tz);
                Assert.NotNull(mine);
                Assert.Equal((c.Def, c.Rotation), (mine!.Def, mine.Rotation));
                Assert.Equal(c.Filters, mine.Filters);
            }
            Assert.Equal(sim.Vehicles.Count, a.Vehicles.Count);
            Assert.Equal(2, a.Drivers.Values.Count(d => d.State != DriverState.None));
            Assert.True(a.Global.Wallet >= PlaytestMask.MaxWallet, $"wallet {a.Global.Wallet}");
            Assert.Equal(0, client.ContainerDesyncs);
            Assert.Equal(0, client.BadPackets);
            // What rides the belts is the same on both ends, segment by segment.
            foreach (var seg in sim.Belts.Segments)
            {
                var other = a.Belts.SegmentByHead(seg.Head);
                Assert.NotNull(other);
                Assert.Equal(seg.Items.Select(i => i.Mail.Id).OrderBy(i => i), other!.Items.Select(i => i.Mail.Id).OrderBy(i => i));
            }

            // Every recipe is open to empty pockets: blueprints owned, nothing taken.
            var truth = sim.GetPlayer(a.LocalId)!;
            foreach (var box in new[] { truth.Hotbar, truth.Inventory })
                foreach (uint id in box.Entries.Select(e => e.EntryId).ToList()) box.Remove(id);
            Run(5);
            foreach (var recipe in content.Recipes)
                Assert.Equal(OpResult.Ok, a.RecipeState(recipe));
        }
    }
}
