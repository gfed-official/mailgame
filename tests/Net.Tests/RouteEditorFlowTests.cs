using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Players;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The route editor on the map (doc 09 §2.6; design choice 122) on the debug world's Vehicle
    /// Depot and Small Port: houses clicked become the next stops, a district's label the
    /// district, a stop dragged moves and the others move over, one taken off closes the gap; the
    /// server holds the same stops, and the edited route is planned as they stand.
    /// </summary>
    public class RouteEditorFlowTests
    {
        [Fact]
        public void HousesAndADistrictAreClickedIn_AStopIsDraggedAndOneTakenOff_TheServerAgrees()
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3, MapId = "debug_world", QuickStart = true, Raids = false }, hub);
            var client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada", PlaytestFlags.None, 0);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); server.Tick(); }
            }
            Run(10);
            var sim = server.World;
            var world = client.World!;
            var depot = sim.Constructs.All.Single(c => c.Def.IsVehicleDepot);
            var truth = sim.GetPlayer(world.LocalId)!;
            truth.Move.X = depot.CentreX + 5f;
            truth.Move.Z = depot.CentreZ - 5f;
            Run(30);
            var editor = new RouteEditor(client);
            Assert.False(editor.Active);
            Assert.NotEqual(0u, client.OpenFilters(world.Constructs.At(depot.Tx, depot.Tz)!));
            Assert.True(editor.Active);
            var beacon = sim.Map.Streets.Single(s => s.Name == "Beacon Road");
            Assert.Equal(new MailFilter(FilterKind.Street, beacon.Id), editor.Stops()[0]);

            var anvil = sim.Map.Destinations.Single(d => d.Label == "1 Anvil Close");
            var fern = sim.Map.Destinations.Single(d => d.Label == "1 Fern Hollow");
            var dock = sim.Map.Destinations.Single(d => d.IsDock);
            Assert.Equal(1, editor.AddHouse(anvil));
            Assert.Equal(1, editor.AddHouse(anvil));
            Assert.Equal(-1, editor.AddHouse(dock));
            Assert.Equal(2, editor.AddHouse(fern));
            Assert.Equal(3, editor.AddDistrict(1));
            Run(10);
            Assert.True(editor.Move(3, 0));
            Run(10);
            Assert.True(editor.Remove(2));
            Run(10);
            var want = new[] { new MailFilter(FilterKind.District, 1), new MailFilter(FilterKind.Street, beacon.Id), new MailFilter(FilterKind.Address, fern.Id) };
            Assert.Equal(want, editor.Stops().Take(3));
            Assert.Equal(want, sim.Constructs.At(depot.Tx, depot.Tz)!.Filters.Take(3));
            Assert.All(sim.Constructs.At(depot.Tx, depot.Tz)!.Filters.Skip(3), f => Assert.Equal(MailFilter.Empty, f));
            Assert.Contains("m round trip", editor.Line());

            // The edited route is planned as the stops stand: every house of the district once, the dock not.
            var layers = new MapLayers();
            layers.UpdateEdited(world, editor.Console);
            Assert.Equal(sim.Map.Houses.Count, layers.Edited!.Stops.Count);
            Assert.DoesNotContain(dock, layers.Edited.Stops);
            Assert.True(layers.Edited.Points.Count > layers.Edited.Stops.Count);

            // The port's console takes docks only.
            client.CloseFilters();
            var port = sim.Constructs.All.Single(c => c.Def.IsPort);
            truth.Move.X = port.CentreX;
            truth.Move.Z = port.CentreZ - 6f;
            Run(30);
            Assert.NotEqual(0u, client.OpenFilters(world.Constructs.At(port.Tx, port.Tz)!));
            Assert.Equal(-1, editor.AddHouse(anvil));
            Assert.Equal(0, editor.AddHouse(dock));
        }
    }
}
