using System;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// A Vehicle Depot's NPC driver over the loopback transport (doc 04 §3.2): hired by the
    /// console's key 7, its card and the feed say what it does, its truck is where the server has
    /// it, the prompt beside it offers the wheel, and one who joins hears what it is doing.
    /// </summary>
    public class DriverFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        [Fact]
        public void TheDriverIsHiredByKeySeven_ItsTruckIsFollowedHere_AndTheWheelIsOffered()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var client = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 2);
            var feed = new EventFeed();
            client.EventReceived += e => feed.OnEvent(client.World!, e);
            ClientRuntime? late = null;
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); late?.Tick(); server.Tick(); }
            }
            Run(10);
            Assert.NotEqual(0u, client.PressGo());
            Run(10);
            var sim = server.World;
            Assert.Equal((RunPhase.Prep, 2), (sim.Run.Phase, sim.Run.Shift));
            var truth = sim.GetPlayer(client.World!.LocalId)!;
            sim.Economy.AddLoot(1000);
            Assert.True(sim.Grant(content.ShopItem("bp_truck"), truth.Id));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("plank"), 12)));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("stone"), 8)));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("iron_ingot"), 4)));
            truth.Move.X = BuildRules.TileCentre(15);
            truth.Move.Z = BuildRules.TileCentre(24);
            Run(10);
            var a = client.World;
            Assert.NotEqual(0u, client.PlaceConstruct(content.Building("vehicle_depot").Recipe, 14, 21, 0));
            Run(10);
            var depot = sim.Constructs.At(14, 21)!;
            SimWorld.Bay(depot, out float bx, out float bz, out float heading);
            var truck = sim.AddVehicleAt(content.Vehicle("mail_truck"), bx, bz, heading);
            var far = sim.Map.Destinations.OrderByDescending(h => h.MailboxX).First();
            for (uint i = 0; i < 2; i++)
                Assert.True(sim.Give(truck.Cargo, ItemStack.OfMail(new MailItem { Id = 994000 + i, Kind = 1, Address = far.Address, Value = 8, SpawnShift = 2, DeadlineShift = 4 })));
            Run(10);

            // The console: stop 1 to the far house, key 7 hires the driver.
            var built = a.Constructs.At(14, 21)!;
            Assert.NotEqual(0u, client.OpenFilters(built));
            Run(5);
            Assert.NotEqual(0u, client.SetFilter(0, 0, new MailFilter(FilterKind.Address, far.Id)));
            Assert.Contains("[7]  Driver   ·   hire for this shift, $1.50", HudText.FilterText(a));
            Assert.True(client.PressFilterKey(6));
            Run(10);
            Assert.Equal(DriverState.Waiting, sim.DriverAt(depot)!.State);
            Assert.Contains("[7]  Driver   ·   hired for this shift   ·   0 delivered\nwaiting for a truck with mail for the route, in Delivery", HudText.FilterText(a));
            Assert.Contains(feed.Entries, e => e.Text == "You hired a driver for the Vehicle Depot   ·   $1.50");
            Assert.True(client.CloseFilters());

            // One who joins (in Prep) hears that a driver waits there, and the feed has no news of it.
            late = new ClientRuntime(content, hub.CreateClient(), "Bo", PlaytestFlags.None, 0);
            var boFeed = new EventFeed();
            late.EventReceived += e => boFeed.OnEvent(late.World!, e);
            Run(15);
            var b = late.World!;
            Assert.Equal(DriverState.Waiting, b.DriverAt(b.Constructs.At(14, 21)!)?.State);
            Assert.DoesNotContain(boFeed.Entries, e => e.Text.Contains("driver", StringComparison.Ordinal));

            // Delivery: it sets out, and the truck here is where it is there.
            Assert.NotEqual(0u, client.PressGo());
            Assert.NotEqual(0u, late.PressGo());
            for (int i = 0; i < 300 && sim.DriverAt(depot)!.State != DriverState.Out; i++) Run(1);
            Assert.Equal(DriverState.Out, sim.DriverAt(depot)!.State);
            Run(60);
            var here = a.Vehicles[truck.Id];
            Assert.True(here.NpcDriving && here.NpcAboard);
            Assert.True(b.Vehicles[truck.Id].NpcDriving);
            Assert.True(MathF.Abs(here.X - truck.X) < 0.6f && MathF.Abs(here.Z - truck.Z) < 0.6f, $"here ({here.X}, {here.Z}), there ({truck.X}, {truck.Z})");
            Assert.Contains(feed.Entries, e => e.Text == "The driver sets out with the truck: 1 house");

            // Beside it, the prompt offers the wheel.
            truth.Move.X = truck.X;
            truth.Move.Z = truck.Z + 2.2f;
            Run(3);
            var interaction = new InteractionController(client);
            var box = AimBox.Vehicle(a.Map, here.Def, here.X, here.Z, here.Heading);
            ViewRay.Aim(a.Local, box.X, box.Y, box.Z, out client.Input.YawRadians, out client.Input.PitchRadians);
            interaction.Update(Dt, false, false);
            Assert.Equal(InteractKind.Mount, interaction.Prompt.Kind);
            Assert.Equal("Take the wheel of the Mail Truck  [E]   ·   the driver moves over", HudText.Prompt(a, interaction.Prompt, out _));

        }
    }
}
