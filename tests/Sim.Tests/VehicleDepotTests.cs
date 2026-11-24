using System;
using System.Linq;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Vehicles;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Vehicle Depot (doc 04 §3.2, doc 10 §2.2): 3 × 3 tiles behind the Motor Pool blueprint,
    /// one for every two carriers; a truck parked on it stands in its bay, loaded at its rear by a
    /// belt or an inserter at the back; its Route Console holds the stops of a route.
    /// </summary>
    public class VehicleDepotTests
    {
        private const int Tps = 30;
        // Built facing east on tiles x 14–16, rows 21–23: its bay in the middle at (15, 22), its loading face on (14, 22).
        private const int Bx = 14, Bz = 21, East = 0;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef DepotDef => Db.Building("vehicle_depot");
        private static VehicleDef Truck => Db.Vehicle("mail_truck");

        private uint _nextRequest = 1;
        private uint _nextMail = 991000;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InPrep(bool blueprint = true, int players = 1)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            for (int i = 1; i < players; i++) world.AddPlayer($"guest{i}");
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            if (blueprint) Assert.True(world.Grant(Db.ShopItem("bp_truck"), host.Id));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 30)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 8)));
            SimDriver.Teleport(host, BuildRules.TileCentre(15), BuildRules.TileCentre(24));
            SimDriver.StepTicks(world, 12);
            return (world, host);
        }

        private OpResult Place(SimWorld world, Player p, BuildingDef def, int tx, int tz, int rotation) =>
            Request(world, p, SimRequest.PlaceConstruct(0, def.Index, tx, tz, rotation));

        private static int MailIn(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);

        [Fact]
        public void TheVehicleDepot_IsContent_NineTiles_BehindTheMotorPool_OnTheStorageKey()
        {
            Assert.Equal((BuildingBehaviour.VehicleDepot, 3, 3, 1000), (DepotDef.Behaviour, DepotDef.Width, DepotDef.Length, DepotDef.Hp));
            Assert.Equal(new[] { ("plank", 12), ("stone", 8), ("iron_ingot", 4) }, DepotDef.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Same(Db.ShopItem("bp_truck"), DepotDef.Recipe.Blueprint);
            Assert.Equal(Db.Building("chest").Recipe.Key, DepotDef.Recipe.Key);
            Assert.Equal((6, 150), (DepotDef.RouteStops, DepotDef.HireCost));
            Assert.Equal(6, DepotDef.FilterOutputs);
            Assert.False(DepotDef.Solid);
        }

        [Fact]
        public void ItIsBuiltWithTheBlueprint_OnePerTwoCarriers()
        {
            var (none, h0) = InPrep(blueprint: false);
            Assert.Equal(OpResult.NeedsBlueprint, Place(none, h0, DepotDef, Bx, Bz, East));

            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Place(world, host, DepotDef, Bx, Bz, East));
            var built = world.Constructs.At(16, 23)!;
            Assert.Same(built, world.Constructs.At(Bx, Bz));
            Assert.Equal(9, Enumerable.Range(0, 9).Select(built.TileAt).Distinct().Count());
            // Solo: one is all.
            SimDriver.Teleport(host, BuildRules.TileCentre(10), BuildRules.TileCentre(21));
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.DepotLimit, Place(world, host, DepotDef, 6, 20, East));

            // Three carriers: two.
            var (team, lead) = InPrep(players: 3);
            Assert.Equal(OpResult.Ok, Place(team, lead, DepotDef, Bx, Bz, East));
            Assert.Equal(2, team.VehicleDepotLimit);
        }

        [Fact]
        public void ATruckParkedOnIt_StandsInTheBay_AndABeltOrAnInserterAtTheBackLoadsIt()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Place(world, host, DepotDef, Bx, Bz, East));
            var depot = world.Constructs.At(Bx, Bz)!;
            SimWorld.Bay(depot, out float bx, out float bz, out float heading);
            Assert.Equal((BuildRules.TileCentre(15), BuildRules.TileCentre(22)), (bx, bz));
            Assert.Equal(-MathF.PI / 2f, heading, 4);
            // Driven on crooked and got off: it is set into the bay.
            var truck = world.AddVehicleAt(Truck, bx - 0.6f, bz + 0.5f, 0.4f, host.Id);
            SimDriver.Teleport(host, truck.X + 1.2f, truck.Z);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, truck.Id.Value)));
            world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, Buttons = InputButtons.Jump });
            world.Step();
            Assert.False(truck.Ridden);
            Assert.Equal((bx, bz, heading), (truck.X, truck.Z, truck.Heading));
            Assert.Same(truck, world.InBay(depot));
            Assert.Same(truck, world.LoadingFaceOn(14, 22));
            Assert.Contains(world.Events, e => e is VehicleParkedEvent p && p.X == bx && p.Z == bz);

            // A belt from the west ends into the back middle tile: mail rides into the bay.
            Assert.Equal(OpResult.Ok, Place(world, host, Db.Building("belt_mk1"), 13, 22, East));
            world.Step();
            var seg = world.Belts.SegmentAt(13, 22)!;
            var address = world.Map.Destinations[1].Address;
            for (int i = 0; i < 2; i++)
            {
                Assert.True(world.PutOnBelt(seg, TestContent.Mail("letter", address, _nextMail++, 1)));
                SimDriver.StepTicks(world, 15);
            }
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.Equal(2, MailIn(truck.Cargo));
            Assert.Equal(0, world.Belts.ItemCount);

            // An inserter from a chest behind it, its front on the loading face.
            var (w2, h2) = InPrep();
            Assert.Equal(OpResult.Ok, Place(w2, h2, DepotDef, Bx, Bz, East));
            var d2 = w2.Constructs.At(Bx, Bz)!;
            SimWorld.Bay(d2, out float x2, out float z2, out float hd2);
            var t2 = w2.AddVehicleAt(Truck, x2, z2, hd2);
            Assert.Equal(OpResult.Ok, Place(w2, h2, Db.Building("chest"), 12, 22, 0));
            Assert.Equal(OpResult.Ok, Place(w2, h2, Db.Building("inserter"), 13, 22, East));
            var chest = w2.GetContainer(w2.Constructs.At(12, 22)!.ContainerId)!;
            for (int i = 0; i < 2; i++) Assert.True(w2.Give(chest, ItemStack.OfMail(TestContent.Mail("letter", address, _nextMail++, 1))));
            SimDriver.StepTicks(w2, 3 * Tps);
            Assert.Equal((0, 2), (MailIn(chest), MailIn(t2.Cargo)));
        }

        [Fact]
        public void ItIsWalkedAndDrivenOver()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Place(world, host, DepotDef, Bx, Bz, East));
            Assert.True(SimDriver.WalkTo(world, host, BuildRules.TileCentre(15), BuildRules.TileCentre(22), maxTicks: 300));
        }

        [Fact]
        public void TheRouteConsole_HoldsHousesStreetsAndDistricts_NotKinds_AndCountsTheTrucksMailForEachStop()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Place(world, host, DepotDef, Bx, Bz, East));
            var depot = world.Constructs.At(Bx, Bz)!;
            SimWorld.Bay(depot, out float bx, out float bz, out float heading);
            var truck = world.AddVehicleAt(Truck, bx, bz, heading);
            var house = world.Map.Destinations[2];
            var other = world.Map.Destinations[5];
            int street = house.Address.Street, district = house.Address.District;
            OpResult Set(int stop, MailFilter f) => Request(world, host, SimRequest.Configure(0, 15, 22, SimRequest.ConfigureSet, stop, 0, f.Encode()));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Configure(0, 15, 22, SimRequest.ConfigureOpen)));
            Assert.Equal(OpResult.Ok, Set(0, new MailFilter(FilterKind.Address, house.Id)));
            Assert.Equal(OpResult.Ok, Set(1, new MailFilter(FilterKind.Street, street)));
            Assert.Equal(OpResult.Ok, Set(5, new MailFilter(FilterKind.District, district)));
            var configured = world.Events.OfType<ConstructConfiguredEvent>().Single();
            Assert.Equal(6, configured.Filters.Count);
            Assert.Equal(new MailFilter(FilterKind.District, district), MailFilter.Decode(configured.Filters[5]));
            Assert.Equal(OpResult.BadRequest, Set(2, new MailFilter(FilterKind.Kind, 0)));
            Assert.Equal(OpResult.BadRequest, Set(2, new MailFilter(FilterKind.Address, 999)));
            Assert.Equal(OpResult.BadRequest, Set(6, new MailFilter(FilterKind.Address, house.Id)));
            Assert.Equal((FilterKind.Address, (ushort)house.Id), (depot.Filter(0, 0).Kind, depot.Filter(0, 0).Value));

            // Two for the house, one for another on its street: the house's stop counts its two, the street the other.
            Assert.True(world.Give(truck.Cargo, ItemStack.OfMail(TestContent.Mail("letter", house.Address, _nextMail++, 1))));
            Assert.True(world.Give(truck.Cargo, ItemStack.OfMail(TestContent.Mail("letter", house.Address, _nextMail++, 1))));
            Assert.True(world.Give(truck.Cargo, ItemStack.OfMail(TestContent.Mail("small", other.Address, _nextMail++, 1))));
            world.Events.Clear();
            SimDriver.StepTicks(world, SimWorld.SorterStatusTicks + 1);
            var status = world.Events.OfType<SorterStatusEvent>().Last();
            Assert.Equal(SimWorld.DepotTruckInBay, status.Buffered);
            Assert.Equal(new ushort[] { 2, other.Address.Street == street ? (ushort)1 : (ushort)0, 0, 0, 0, other.Address.Street == street ? (ushort)0 : (ushort)1 }, status.Matches.ToArray());

            // The shared plan: the house first, then the rest of its street nearest first, each once.
            var houses = new System.Collections.Generic.List<DestinationDef>();
            RoutePlan.Houses(world.Map, depot.Filters, bx, bz, null, houses);
            Assert.Same(house, houses[0]);
            Assert.Equal(houses.Count, houses.Distinct().Count());
            Assert.Equal(world.Map.Destinations.Count(d => d.Address.District == district), houses.Count);
            Assert.True(RoutePlan.RoundTrip(world.Map, houses, bx, bz, new System.Collections.Generic.List<(float, float)>()) > 50f);
        }
    }
}
