using System.Linq;
using PerformativeMail.Client;
using PerformativeMail.Server;
using PerformativeMail.Server.Transport;
using PerformativeMail.Sim;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using Xunit;

namespace PerformativeMail.Net.Tests
{
    /// <summary>
    /// The map's layers and filter chips (doc 09 §2.6; design choice 121) as a client works them
    /// out: the belts and pipes the way they run, the machines, the vehicles, the crews' routes
    /// (Routes), the houses with mail waiting in the Intake and the storage as the server tells
    /// it, or in one's own pockets (Mail), and the nodes not spent (Resources).
    /// </summary>
    public class MapLayersFlowTests
    {
        private static (ServerRuntime Server, ClientRuntime Client, System.Action<int> Run) Start(string map)
        {
            var hub = new LoopbackHub();
            var server = new ServerRuntime(Shared.Content, new SimSettings { Seed = 3, MapId = map, QuickStart = true, Raids = false }, hub);
            var client = new ClientRuntime(Shared.Content, hub.CreateClient(), "Ada", PlaytestFlags.None, 0);
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++) { client.Tick(); server.Tick(); }
            }
            return (server, client, Run);
        }

        [Fact]
        public void OnTheDebugWorld_TheLayersShowItsBelts_Vehicles_CrewRoutes_AndTheHousesWithMail()
        {
            var (server, client, run) = Start("debug_world");
            run(20 * 30);
            var world = client.World!;
            var layers = new MapLayers();
            layers.Update(world, 0.0);
            var sim = server.World;

            int belts = sim.Constructs.All.Count(c => BeltNetwork.IsBelt(c) || c.Def.Behaviour == Sim.Content.BuildingBehaviour.Pipe);
            Assert.Equal(belts, layers.Belts.Count);
            Assert.Contains(layers.Belts, b => b.Kind == MapBeltKind.Fast);
            Assert.Contains(layers.Belts, b => b.Kind == MapBeltKind.Raised);
            Assert.Contains(layers.Belts, b => b.Kind == MapBeltKind.Pipe);
            // A belt's line runs the way it carries: the outfeed's runs north.
            var outfeed = layers.Belts.First(b => b.Kind == MapBeltKind.Belt && b.X0 == b.X1 && b.Z1 < b.Z0);
            Assert.True(outfeed.Z0 - outfeed.Z1 > 1.9f);
            Assert.Equal(sim.Constructs.All.Count - belts, layers.Machines.Count);
            Assert.Equal(2, layers.Vehicles.Count);

            // Routes: the driver round Beacon Road's three houses by the streets, the captain to the dock over the water.
            Assert.Equal(2, layers.Crews.Count);
            var driver = layers.Crews.Single(c => !c.Captain);
            Assert.Equal(3, driver.Stops.Count);
            Assert.All(driver.Stops, d => Assert.Equal("Beacon Road", d.Street.Name));
            Assert.True(driver.Points.Count > 4);
            var captain = layers.Crews.Single(c => c.Captain);
            Assert.True(Assert.Single(captain.Stops).IsDock);
            Assert.All(captain.Points, p => Assert.True(world.Map.IsWater((int)(p.X / 2f), (int)(p.Z / 2f)), $"the captain's way crosses land at {p}"));

            // Mail: what the server says waits in storage, as it says it.
            Assert.Equal(sim.MailWaiting().ToDictionary(kv => kv.Key, kv => kv.Value), world.MailWaiting);
            foreach (var kv in world.MailWaiting) Assert.True(layers.MailHouses[kv.Key] >= kv.Value);

            // Off, the chips take their layers away.
            layers.Toggle(MapChip.Mail);
            layers.Toggle(MapChip.Routes);
            layers.Update(world, 0.0);
            Assert.Empty(layers.MailHouses);
            Assert.Empty(layers.Crews);
            Assert.Equal("[Z] Mail off   [X] Routes off   [V] Resources off", layers.ChipsText());
        }

        [Fact]
        public void OnTheTestStreet_ResourcesShowTheNodesLeft_AndMailInThePocketsCounts()
        {
            var (server, client, run) = Start("test_street");
            run(30);
            var world = client.World!;
            var layers = new MapLayers();
            layers.Update(world, 0.0);
            Assert.Empty(layers.Nodes);
            layers.Toggle(MapChip.Resources);
            layers.Update(world, 0.0);
            Assert.Equal(world.Nodes.NodeCount, layers.Nodes.Count);
            Assert.Contains(layers.Nodes, n => n.Node.Resource.Length > 0);

            // A parcel in the chest by the Post Office: the server says so within a second.
            var other = world.Map.Houses[2];
            Assert.True(server.World.Give(server.World.MapChests[0], ItemStack.OfMail(new Sim.Mail.MailItem { Id = 990002, Kind = Shared.Content.MailKind("small").Index, Address = other.Address, Value = 30, SpawnShift = 1, DeadlineShift = 1 })));
            run(40);
            Assert.Equal(1, world.MailWaiting[other.Id]);
            layers.Update(world, 0.0);
            Assert.Equal(1, layers.MailHouses[other.Id]);

            // A letter in the pockets rings its house.
            var house = world.Map.Houses[4];
            var truth = server.World.GetPlayer(world.LocalId)!;
            Assert.True(server.World.Give(truth.Inventory, ItemStack.OfMail(new Sim.Mail.MailItem { Id = 990001, Kind = Shared.Content.MailKind("letter").Index, Address = house.Address, Value = 8, SpawnShift = 1, DeadlineShift = 1 })));
            run(10);
            layers.Update(world, 0.0);
            Assert.True(layers.MailHouses.TryGetValue(house.Id, out int n) && n >= 1);
        }
    }
}
