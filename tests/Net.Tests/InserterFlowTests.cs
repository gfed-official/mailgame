using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The Inserter over the loopback transport (doc 04 §2.5): built by a client between a chest and
    /// a mailbox, its moves seen as swings of its arm, its deliveries in the feed as an inserter's,
    /// and its kind filter on the filter panel by the interact key.
    /// </summary>
    public class InserterFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        [Fact]
        public void AnInserterFeedsAMailboxFromAChest_TheClientSeesItSwing_AndTheFeedSaysAnInserterDelivered()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false }, hub);
            var client = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 0);
            var feed = new EventFeed();
            client.EventReceived += e => feed.OnEvent(client.World!, e);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, client.PressGo());
            Run(10);
            var sim = server.World;
            var truth = sim.GetPlayer(client.World!.LocalId)!;
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("iron_ingot"), 4)));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("plank"), 2)));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("log"), 4)));
            truth.Move.X = BuildRules.TileCentre(9);
            truth.Move.Z = BuildRules.TileCentre(11);
            Run(10);
            var world = client.World;
            var inserter = content.Recipes.Single(r => r.Building.Id == "inserter");
            var chest = content.Recipes.Single(r => r.Building.Id == "chest");
            Assert.NotEqual(0u, client.PlaceConstruct(chest, 10, 12, 0));
            Run(5);
            Assert.NotEqual(0u, client.PlaceConstruct(inserter, 11, 12, 0));
            Run(10);
            var built = world.Constructs.At(11, 12)!;
            Assert.Equal("inserter", built.Def.Id);

            // E at it, from the street beside it, puts its kind filter up: one card, "Takes".
            truth.Move.X = 22.5f;
            truth.Move.Z = 27f;
            Run(10);
            var interaction = new InteractionController(client);
            var box = AimBox.Built(world.Map, built);
            ViewRay.Aim(world.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Configure, interaction.Prompt.Kind);
            Assert.Equal("Set the filters of the Inserter  [E]", HudText.Prompt(world, interaction.Prompt, out _));
            interaction.Update(Dt, true, false);
            Run(5);
            Assert.StartsWith("INSERTER   ·   filters by kind\n[1]  Takes\nany mail\n", HudText.FilterText(world));
            Assert.True(client.CloseFilters());

            var dest = sim.Map.Destinations[0];
            var box2 = sim.GetContainer(sim.Constructs.At(10, 12)!.ContainerId)!;
            for (uint i = 0; i < 2; i++)
                Assert.True(sim.Give(box2, ItemStack.OfMail(new Sim.Mail.MailItem
                {
                    Id = 990000 + i, Kind = content.MailKind("letter").Index, Address = dest.Address, Value = 8, SpawnShift = 1, DeadlineShift = 3,
                })));
            Assert.NotEqual(0u, client.PressGo());
            Run(10);
            Assert.Equal(RunPhase.Delivery, sim.Run.Phase);
            Run(60);
            Assert.True(world.InserterSwings.ContainsKey(12 * world.Map.Cols + 11));
            Assert.Contains(feed.Entries, e => e.Text.StartsWith($"An inserter delivered 2 Letters to {dest.Label}", System.StringComparison.Ordinal));
        }
    }
}
