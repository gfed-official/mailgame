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
    /// A Small Port's NPC captain over the loopback transport (doc 04 §3.3): the port's Route
    /// Console offers the docks a boat can lie at, its round trip over the water and the captain
    /// for hire by key 7; hired, the boat sails out under the captain as the server has it and the
    /// feed says so, and the Business Dock's crate is delivered by "A captain".
    /// </summary>
    public class CaptainFlowTests
    {
        private const double Dt = 1.0 / 30.0;

        [Fact]
        public void TheCaptainIsHiredAtThePortsConsole_AndItsBoatIsFollowedToTheDock()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 3, Raids = false, Draft = false, StartingKits = false }, hub);
            var client = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.None, 3);
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
            Assert.Equal((RunPhase.Prep, 3), (sim.Run.Phase, sim.Run.Shift));
            var truth = sim.GetPlayer(client.World!.LocalId)!;
            sim.Economy.AddLoot(1000);
            Assert.True(sim.Grant(content.ShopItem("bp_motorboat"), truth.Id));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("plank"), 16)));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("stone"), 10)));
            Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item("iron_ingot"), 6)));
            var port = content.Building("small_port");
            int x = Enumerable.Range(2, sim.Map.Cols - 2).First(tx => BuildRules.CheckPlacement(sim.Map, sim.Constructs, port, tx, 24, 1) == Sim.Inventory.OpResult.Ok);
            truth.Move.X = BuildRules.TileCentre(x - 1);
            truth.Move.Z = BuildRules.TileCentre(23);
            Run(10);
            var a = client.World;
            Assert.NotEqual(0u, client.PlaceConstruct(port.Recipe, x, 24, 1));
            Run(10);
            var built = sim.Constructs.At(x, 24)!;
            SimWorld.Bay(built, out float bx, out float bz, out float heading);
            var boat = sim.AddVehicleAt(content.Vehicle("motorboat"), bx, bz, heading);
            var dock = sim.Map.Destinations.Single(d => d.IsDock);
            Assert.True(sim.Give(boat.Cargo, ItemStack.OfMail(new MailItem { Id = 995000, Kind = content.MailKind("cargo").Index, Address = dock.Address, Value = 600, SpawnShift = 3, DeadlineShift = 4 })));
            Run(10);

            // The console: the dock is a stop, the round trip is over the water, key 7 hires the captain.
            var here = a.Constructs.At(x, 24)!;
            Assert.Equal(new[] { MailFilter.Empty, new MailFilter(FilterKind.Address, dock.Id) }, a.FilterChoices(here));
            Assert.NotEqual(0u, client.OpenFilters(here));
            Run(5);
            Assert.NotEqual(0u, client.SetFilter(0, 0, new MailFilter(FilterKind.Address, dock.Id)));
            Run(5);
            string text = HudText.FilterText(a);
            Assert.Contains("SMALL PORT   ·   route console", text);
            Assert.Contains("1 dock   ·   ", text);
            Assert.Contains("m round trip over the water", text);
            Assert.Contains($"[1]  Stop 1   ·   {dock.Label}\n1 for it in the boat", text);
            Assert.Contains("[7]  Captain   ·   hire for this shift, $1.50", text);
            Assert.Equal(HudText.CaptainHint, HudText.FilterHintFor(here));
            Assert.True(client.PressFilterKey(6));
            Run(10);
            Assert.Equal(DriverState.Waiting, sim.DriverAt(built)!.State);
            Assert.Contains("waiting for a boat with cargo for the route, in Delivery", HudText.FilterText(a));
            Assert.Contains(feed.Entries, e => e.Text == "You hired a captain for the Small Port   ·   $1.50");
            Assert.True(client.CloseFilters());

            // Delivery: it sails, the boat here is where it is there, and the dock gets its crate.
            Assert.NotEqual(0u, client.PressGo());
            for (int i = 0; i < 400 && sim.DriverAt(built)!.State != DriverState.Out; i++) Run(1);
            Assert.Equal(DriverState.Out, sim.DriverAt(built)!.State);
            Run(45);
            var mirror = a.Vehicles[boat.Id];
            Assert.True(mirror.NpcDriving && mirror.NpcAboard);
            Assert.True(MathF.Abs(mirror.X - boat.X) < 0.6f && MathF.Abs(mirror.Z - boat.Z) < 0.6f, $"here ({mirror.X}, {mirror.Z}), there ({boat.X}, {boat.Z})");
            Assert.Contains(feed.Entries, e => e.Text == "The captain sets out with the boat: 1 dock");
            for (int i = 0; i < 60 * 30 && sim.DriverAt(built)!.State != DriverState.Waiting; i++) Run(1);
            Assert.Equal(DriverState.Waiting, sim.DriverAt(built)!.State);
            Run(5);
            Assert.True(feed.Entries.Any(e => e.Text.StartsWith($"A captain delivered 1 Cargo to {dock.Label}", StringComparison.Ordinal)), string.Join(" | ", feed.Entries.Select(e => e.Text)));
            Assert.Contains(feed.Entries, e => e.Text == "The captain is back at the Small Port");
        }
    }
}
