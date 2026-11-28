using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// Doc 11 §4 "Motorboat to second town" over the loopback transport (design choice 175): on the Large Island the home
    /// Small Port's console offers the far town's district once the team has a second port there, its line counts the dock
    /// and the other port, and the captain's leg to that port and its lying there reach the client and its feed.
    /// </summary>
    public class FarTownFlowTests
    {
        private static (int X, int Z) PortSite(SimWorld sim, BuildingDef port, float nearX, bool roomBehind)
        {
            int cx = (int)(nearX / 2f);
            return Enumerable.Range(Math.Max(0, cx - 40), 80).Where(x => x < sim.Map.Cols)
                .SelectMany(x => Enumerable.Range(0, sim.Map.Rows).Select(z => (x, z)))
                .Where(t => BuildRules.CheckPlacement(sim.Map, sim.Constructs, port, t.x, t.z, 1) == OpResult.Ok)
                .Where(t => !roomBehind || BuildRules.CheckTile(sim.Map, sim.Constructs, t.x - 1, t.z - 1) == OpResult.Ok && BuildRules.CheckTile(sim.Map, sim.Constructs, t.x - 1, t.z - 2) == OpResult.Ok)
                .OrderBy(t => Math.Abs(t.x - cx)).First();
        }

        [Fact]
        public void TheHomePortsConsoleRoutesTheFarTown_AndTheCaptainsLegToTheOtherPortIsFollowed()
        {
            var hub = new LoopbackHub();
            var content = Shared.Content;
            var server = new ServerRuntime(content, new SimSettings { Seed = 1, MapId = LargeIslandGenerator.LargeIsland, Raids = false, Draft = false, StartingKits = false }, hub);
            var client = new ClientRuntime(content, hub.CreateClient(), "Ada", PlaytestFlags.NoQuota | PlaytestFlags.NoTimer, 4);
            var feed = new EventFeed();
            client.EventReceived += e => feed.OnEvent(client.World!, e);
            var said = new HashSet<string>();
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); server.Tick(); foreach (var e in feed.Entries) said.Add(e.Text); }
            }
            Run(10);
            Assert.NotEqual(0u, client.PressGo());
            Run(10);
            var sim = server.World;
            Assert.Equal((RunPhase.Prep, 4), (sim.Run.Phase, sim.Run.Shift));
            var truth = sim.GetPlayer(client.World!.LocalId)!;
            sim.Economy.AddLoot(2000);
            Assert.True(sim.Grant(content.ShopItem("bp_motorboat"), truth.Id));
            foreach (var (item, n) in new[] { ("plank", 40), ("stone", 20), ("iron_ingot", 20), ("log", 20) })
                Assert.True(sim.Give(truth.Inventory, ItemStack.OfItem(content.Item(item), n)));
            var a = client.World;
            void Build(BuildingDef def, int x, int z, int rotation)
            {
                truth.Move.X = BuildRules.TileCentre(x + 1);
                truth.Move.Z = BuildRules.TileCentre(z - 1);
                Run(10);
                Assert.NotEqual(0u, client.PlaceConstruct(def.Recipe, x, z, rotation));
                Run(10);
                Assert.Equal(def, sim.Constructs.At(x, z)?.Def);
            }
            var portDef = content.Building("small_port");
            var nearDock = sim.Map.Destinations.Single(d => d.IsDock && d.Address.District <= 2);
            var farDock = sim.Map.Destinations.Single(d => d.IsDock && d.Address.District >= 3);
            var (hx, hz) = PortSite(sim, portDef, nearDock.MailboxX, false);
            Build(portDef, hx, hz, 1);
            var home = sim.Constructs.At(hx, hz)!;
            var (fx, fz) = PortSite(sim, portDef, farDock.MailboxX, true);
            Build(portDef, fx, fz, 1);
            var far = sim.Constructs.At(fx, fz)!;
            var (lx, lz) = SimWorld.PortLoadingTile(far);
            Build(content.Building("inserter"), lx, lz - 1, 3);
            Build(content.Building("chest"), lx, lz - 2, 0);
            SimWorld.Bay(home, out float bx, out float bz, out float heading);
            var boat = sim.AddVehicleAt(content.Vehicle("motorboat"), bx, bz, heading);
            var house = sim.Map.Houses.Where(h => h.Address.District is 3 or 4 && Sim.Vehicles.SeaRoute.PortFor(sim.Constructs, home, h) == far)
                .OrderBy(h => MathF.Abs(h.MailboxX - farDock.MailboxX)).First();
            Assert.True(sim.Give(boat.Cargo, ItemStack.OfMail(new MailItem { Id = 996000, Kind = content.MailKind("cargo").Index, Address = farDock.Address, Value = 600, SpawnShift = 4, DeadlineShift = 5 })));
            for (uint i = 0; i < 2; i++)
                Assert.True(sim.Give(boat.Cargo, ItemStack.OfMail(new MailItem { Id = 996001 + i, Kind = content.MailKind("letter").Index, Address = house.Address, Value = 10, SpawnShift = 4, DeadlineShift = 6 })));
            truth.Move.X = home.CentreX;
            truth.Move.Z = home.CentreZ - 2f;
            Run(10);

            // The console: the far dock and the far town's district are stops; the line counts the dock and the other port.
            var here = a.Constructs.At(hx, hz)!;
            var district = new MailFilter(FilterKind.District, house.Address.District);
            Assert.Contains(new MailFilter(FilterKind.Address, farDock.Id), a.FilterChoices(here));
            Assert.Contains(district, a.FilterChoices(here));
            Assert.DoesNotContain(new MailFilter(FilterKind.District, 1), a.FilterChoices(here));
            Assert.NotEqual(0u, client.OpenFilters(here));
            Run(5);
            Assert.NotEqual(0u, client.SetFilter(0, 0, new MailFilter(FilterKind.Address, farDock.Id)));
            Assert.NotEqual(0u, client.SetFilter(1, 0, district));
            Run(5);
            string text = HudText.FilterText(a);
            Assert.Contains("1 dock, 1 other port   ·   ", text);
            Assert.Contains("2 for it in the boat", text);
            Assert.True(client.PressFilterKey(6));
            Run(10);
            Assert.True(client.CloseFilters());

            Assert.NotEqual(0u, client.PressGo());
            var doing = new List<string>();
            var d = sim.DriverAt(home)!;
            for (int i = 0; i < 300 * 30 && !(d.State == DriverState.Waiting && doing.Count > 0); i++)
            {
                Run(1);
                if (a.DriverAt(here) is { State: not DriverState.Waiting } shown) { string s = HudText.DriverDoing(a, shown); if (!doing.Contains(s)) doing.Add(s); }
            }
            Assert.Equal(DriverState.Waiting, d.State);
            Run(5);
            Assert.Contains("The captain sets out with the boat: 2 stops", said);
            Assert.Contains(said, t => t.StartsWith($"A captain delivered 1 Cargo to {farDock.Label}", StringComparison.Ordinal));
            Assert.Contains(doing, s => s.StartsWith("sailing to the Small Port by ", StringComparison.Ordinal) && s.EndsWith("(2 of 2)", StringComparison.Ordinal));
            Assert.Contains(doing, s => s.StartsWith("lying at the Small Port by ", StringComparison.Ordinal) && s.Contains("an inserter behind its loading face takes the mail out"));
            Assert.Equal(2, sim.GetContainer(sim.Constructs.At(lx, lz - 2)!.ContainerId)!.Entries.Sum(e => e.Stack.Mail.Count));
        }
    }
}
