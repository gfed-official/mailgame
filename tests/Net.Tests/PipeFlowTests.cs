using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Pneumatic pipes over the loopback transport (doc 04 §2.2, doc 06 §4): a client builds an
    /// inlet on the outfeed and pipes to an outlet over a chest; the capsules the server sends are
    /// on the client's pipes along the same way and gone when the server takes them off; one who
    /// joins while they run gets them; E at the outlet puts its filter up.
    /// </summary>
    public class PipeFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        [Fact]
        public void TheClientsPipes_CarryTheServersCapsules_AndAJoinerGetsThoseUnderWay()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false }, hub);
            var client = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 3);
            ClientRuntime? late = null;
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); late?.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, client.PressGo());
            Run(10);
            var sim = server.World;
            Assert.Equal((RunPhase.Prep, 3), (sim.Run.Phase, sim.Run.Shift));
            var truth = sim.GetPlayer(client.World!.LocalId)!;
            sim.Economy.AddLoot(1000);
            truth.Move.X = sim.Map.PostOffice.ShopX + 1.0f;
            truth.Move.Z = sim.Map.PostOffice.ShopZ;
            Run(2);
            sim.Submit(truth.Id, SimRequest.Buy(900001, content.ShopItem("bp_pipes").Index));
            Run(2);
            Assert.True(sim.OwnsBlueprint("bp_pipes"));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("iron_ingot"), 20)));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("glass"), 10)));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("log"), 4)));
            truth.Move.X = BuildRules.TileCentre(5);
            truth.Move.Z = BuildRules.TileCentre(5);
            Run(10);
            var world = client.World;
            RecipeDef Of(string id) => content.Recipes.Single(r => r.Building.Id == id);
            Assert.NotEqual(0u, client.PlaceConstruct(Of("pipe_inlet"), 6, 8, 3));
            for (int z = 7; z >= 5; z--) Assert.NotEqual(0u, client.PlaceConstruct(Of("pipe"), 6, z, 3));
            Assert.NotEqual(0u, client.PlaceConstruct(Of("pipe_junction"), 6, 4, 0));
            Assert.NotEqual(0u, client.PlaceConstruct(Of("pipe"), 5, 4, 2));
            Assert.NotEqual(0u, client.PlaceConstruct(Of("pipe_outlet"), 4, 4, 2));
            Assert.NotEqual(0u, client.PlaceConstruct(Of("chest"), 3, 4, 0));
            Run(15);
            Assert.Equal(8, sim.Constructs.All.Count);
            Assert.Equal(8, world.Constructs.All.Count);
            Assert.Single(world.Pipes.Outlets);

            // E at the outlet: its one card, a default.
            var interaction = new InteractionController(client);
            var outletBuilt = world.Constructs.At(4, 4)!;
            var aim = AimBox.Built(world.Map, outletBuilt);
            ViewRay.Aim(world.Local, aim.X, aim.Y, aim.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Configure, interaction.Prompt.Kind);
            interaction.Update(Dt, true, false);
            Run(5);
            Assert.StartsWith("PIPE OUTLET   ·   what the inlets send it\n[1]  Takes\nwhatever no other outlet takes\n", HudText.FilterText(world));
            Assert.True(client.CloseFilters());

            // Mail in the Intake: it goes, a capsule each 0.2 s.
            foreach (var e in sim.Intake.Entries.ToList()) sim.Intake.Remove(e.EntryId);
            var dest = sim.Map.Destinations[1];
            for (uint i = 0; i < 6; i++)
                sim.Spawner.ReturnToIntake(new Sim.Mail.MailItem { Id = 991000 + i, Kind = content.MailKind("letter").Index, Address = dest.Address, Value = 8, SpawnShift = 3, DeadlineShift = 4 });
            Run(20);
            Assert.True(sim.Pipes.Capsules.Count >= 2, $"{sim.Pipes.Capsules.Count} capsules on the server");
            Assert.Equal(sim.Pipes.Capsules.Select(c => c.Mail.Id).OrderBy(x => x), world.Pipes.Capsules.Select(c => c.Mail.Id).OrderBy(x => x));
            foreach (var c in sim.Pipes.Capsules)
                Assert.Equal(c.Path, world.Pipes.Capsules.Single(m => m.Mail.Id == c.Mail.Id).Path);

            // One who joins now gets the capsules under way.
            late = new ClientRuntime(content, hub.CreateClient(), "Bo", PlaytestFlags.None, 0);
            for (int i = 0; i < 6 && (late.World == null || !late.World.HasLocal); i++) Run(1);
            Run(2);
            Assert.NotNull(late.World);
            // (As the first client: both hear of a capsule a tick after the server sends it.)
            Assert.True(late.World!.Pipes.Capsules.Count >= 2);
            Assert.Equal(world.Pipes.Capsules.Select(c => c.Mail.Id).OrderBy(x => x), late.World.Pipes.Capsules.Select(c => c.Mail.Id).OrderBy(x => x));

            Run(5 * 30);
            var chest = sim.GetContainer(sim.Constructs.At(3, 4)!.ContainerId)!;
            Assert.Equal(6, chest.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count));
            Assert.Empty(world.Pipes.Capsules);
            Assert.Empty(late.World.Pipes.Capsules);
            Assert.Equal(0, world.PipeMismatches + late.World.PipeMismatches);
        }
    }
}
