using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Vehicles;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The NPC driver of a Vehicle Depot (doc 04 §3.2, doc 10 §3 npc_driver, doc 11 NPC speed ratio
    /// 0.6, doc 12 M2: "NPC truck completes a 3-stop route and delivers only matching mail (no
    /// misdeliveries) in an integration test; player takeover and hand-back within one tick").
    /// </summary>
    public class DriverTests
    {
        private const int Tps = 30;
        private const int Bx = 14, Bz = 21, East = 0;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef DepotDef => Db.Building("vehicle_depot");
        private static VehicleDef Truck => Db.Vehicle("mail_truck");

        private uint _nextRequest = 1;
        private uint _nextMail = 993000;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static void RunUntil(SimWorld world, Func<bool> done, int maxSeconds)
        {
            for (int i = 0; i < maxSeconds * Tps && !done(); i++) world.Step();
            Assert.True(done(), $"not done after {maxSeconds} s (phase {world.Run.Phase})");
        }

        /// <summary>A depot facing east on x 14–16, rows 21–23, a truck in its bay; Prep of the shift asked for.</summary>
        private (SimWorld World, Player Host, Construct Depot, Vehicle Truck) Setup(int shift = 2, int wallet = 3000)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            if (shift > 1) world.SetPlaytest(host, PlaytestFlags.None, shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal((RunPhase.Prep, shift), (world.Run.Phase, world.Run.Shift));
            world.Economy.AddLoot(wallet);
            Assert.True(world.Grant(Db.ShopItem("bp_truck"), host.Id));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 12)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 8)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 4)));
            SimDriver.Teleport(host, BuildRules.TileCentre(15), BuildRules.TileCentre(24));
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, DepotDef.Index, Bx, Bz, East)));
            var depot = world.Constructs.At(Bx, Bz)!;
            SimWorld.Bay(depot, out float bx, out float bz, out float heading);
            var truck = world.AddVehicleAt(Truck, bx, bz, heading);
            return (world, host, depot, truck);
        }

        private OpResult Hire(SimWorld world, Player p) => Request(world, p, SimRequest.Configure(0, 15, 22, SimRequest.ConfigureHire));

        private OpResult SetStop(SimWorld world, Player p, int stop, MailFilter f) =>
            Request(world, p, SimRequest.Configure(0, 15, 22, SimRequest.ConfigureSet, stop, 0, f.Encode()));

        private void Load(SimWorld world, Vehicle truck, DestinationDef dest, int letters)
        {
            for (int i = 0; i < letters; i++)
                Assert.True(world.Give(truck.Cargo, ItemStack.OfMail(TestContent.Mail("letter", dest.Address, _nextMail++, 2))));
        }

        private static int MailIn(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);

        private void ToDelivery(SimWorld world, Player host)
        {
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Delivery, 5);
        }

        [Fact]
        public void ADriverIsHiredAtTheConsole_ForAShift_FromShiftTwoSolo_OneADepot_ForADollarFifty()
        {
            var (early, h1, _, _) = Setup(shift: 1);
            Assert.Equal(OpResult.ShopLocked, Hire(early, h1));

            var (poor, h2, _, _) = Setup(wallet: 100);
            Assert.Equal(OpResult.CantAfford, Hire(poor, h2));

            var (world, host, depot, _) = Setup();
            int wallet = world.Economy.Wallet;
            Assert.Equal(OpResult.Ok, Hire(world, host));
            Assert.Equal(wallet - 150, world.Economy.Wallet);
            var said = world.Events.OfType<DriverEvent>().Single();
            Assert.Equal((DriverState.Waiting, host.Id), (said.State, said.Player));
            Assert.Equal(OpResult.AlreadyHired, Hire(world, host));
            Assert.Equal(DriverState.Waiting, world.DriverAt(depot)!.State);
        }

        [Fact]
        public void TheDriverRunsAThreeStopRoute_DeliveringOnlyTheMailForItsStops_AtSixtyPercent_AndComesBack()
        {
            var (world, host, depot, truck) = Setup();
            var houses = world.Map.Destinations;
            var a = houses[2]; var b = houses[5]; var c = houses[7]; var off = houses[1];
            Assert.Equal(OpResult.Ok, SetStop(world, host, 0, new MailFilter(FilterKind.Address, a.Id)));
            Assert.Equal(OpResult.Ok, SetStop(world, host, 1, new MailFilter(FilterKind.Address, b.Id)));
            Assert.Equal(OpResult.Ok, SetStop(world, host, 2, new MailFilter(FilterKind.Address, c.Id)));
            Load(world, truck, a, 2);
            Load(world, truck, b, 1);
            Load(world, truck, c, 3);
            Load(world, truck, off, 2);
            Assert.Equal(OpResult.Ok, Hire(world, host));
            // In Prep it waits: the mailboxes take nothing yet.
            SimDriver.StepTicks(world, 6 * Tps);
            Assert.Equal(DriverState.Waiting, world.DriverAt(depot)!.State);
            int paidBefore = world.Economy.ShiftDeliveries;
            ToDelivery(world, host);

            var d = world.DriverAt(depot)!;
            RunUntil(world, () => d.State == DriverState.Out, 6);
            Assert.True(truck.NpcDriving);
            Assert.Equal(new[] { a, b, c }, d.Houses.ToArray());
            Assert.Null(world.LoadingFaceOn(14, 22));
            // Its pace: 60 % of the truck's on the street.
            float topSpeed = 0f, lx = truck.X, lz = truck.Z;
            var seen = new HashSet<DriverState>();
            var delivered = new List<DeliveredEvent>();
            for (int i = 0; i < 120 * Tps && !(d.State == DriverState.Waiting && seen.Contains(DriverState.Returning)); i++)
            {
                world.Events.Clear();
                world.Step();
                delivered.AddRange(world.Events.OfType<DeliveredEvent>());
                Assert.DoesNotContain(world.Events, e => e is MisdeliveredEvent);
                topSpeed = MathF.Max(topSpeed, MathF.Sqrt((truck.X - lx) * (truck.X - lx) + (truck.Z - lz) * (truck.Z - lz)) * Tps);
                lx = truck.X; lz = truck.Z;
                seen.Add(d.State);
            }
            Assert.Equal(DriverState.Waiting, d.State);
            Assert.Contains(DriverState.Unloading, seen);
            Assert.InRange(topSpeed, 0.6f * Truck.RoadSpeed - 0.5f, 0.6f * Truck.RoadSpeed + 0.05f);
            // Six pieces for its three houses, each into its own box, by the driver; the two for another house ride home.
            Assert.Equal(6, delivered.Count);
            Assert.All(delivered, e => Assert.Equal(DeliveryMachine.Driver, e.Machine));
            Assert.Equal(new[] { (a.Id, 2), (b.Id, 1), (c.Id, 3) }, delivered.GroupBy(e => e.DestId).Select(g => (g.Key, g.Count())).OrderBy(t => Array.IndexOf(new[] { a.Id, b.Id, c.Id }, t.Key)));
            Assert.Equal(paidBefore + 6, world.Economy.ShiftDeliveries);
            Assert.Equal(2, MailIn(truck.Cargo));
            Assert.Equal(0, world.Economy.ShiftMisdeliveries);
            // Back in the bay, parked, loadable again; it burned fuel on the way.
            Assert.Same(truck, world.InBay(depot));
            Assert.False(truck.NpcDriving);
            Assert.True(truck.Fuel < Truck.FuelSecondsPerCan);
            Assert.Equal(6, d.Delivered);
        }

        [Fact]
        public void ACarrierTakesTheWheel_AndHandsItBack_EachWithinOneTick_TheDriverHandingOutTheMailMeanwhile()
        {
            var (world, host, depot, truck) = Setup();
            var far = world.Map.Destinations.OrderByDescending(h => h.MailboxX).First();
            Assert.Equal(OpResult.Ok, SetStop(world, host, 0, new MailFilter(FilterKind.Address, far.Id)));
            Load(world, truck, far, 3);
            Assert.Equal(OpResult.Ok, Hire(world, host));
            ToDelivery(world, host);
            var d = world.DriverAt(depot)!;
            RunUntil(world, () => d.State == DriverState.Out && truck.X > 40f, 20);

            // Beside it, the carrier gets in: in that tick the driver moves over and the carrier drives.
            SimDriver.Teleport(host, truck.X, truck.Z + 1.5f);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, truck.Id.Value)));
            Assert.Equal((DriverState.Passenger, false, host.Id), (d.State, truck.NpcDriving, truck.Rider));
            Assert.Contains(world.Events, e => e is DriverEvent de && de.State == DriverState.Passenger && de.Player == host.Id);
            // Full speed: faster than the driver goes.
            for (int i = 0; i < 4 * Tps; i++)
            {
                world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(1f) });
                world.Step();
            }
            Assert.True(host.Move.RideSpeed > 0.6f * Truck.RoadSpeed + 1f, $"{host.Move.RideSpeed} m/s at the wheel");
            // Stopped at the house: the driver hands its mail out.
            SimDriver.Teleport(host, far.MailboxX, 28f);
            host.Move.RideSpeed = 0f;
            int before = world.Economy.ShiftDeliveries;
            for (int i = 0; i < 3 * Tps; i++)
            {
                world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1 });
                world.Step();
            }
            Assert.Equal(before + 3, world.Economy.ShiftDeliveries);
            // Off again: in that same tick the driver has the wheel, on the way home.
            world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, Buttons = InputButtons.Jump });
            world.Step();
            Assert.False(truck.Ridden);
            Assert.True(truck.NpcDriving);
            Assert.Equal(DriverState.Returning, d.State);
            RunUntil(world, () => d.State == DriverState.Waiting, 60);
            Assert.Same(truck, world.InBay(depot));
        }

        [Fact]
        public void RaidersNearTheRoute_SendTheDriverHome()
        {
            var (world, host, depot, truck) = Setup();
            var far = world.Map.Destinations.OrderByDescending(h => h.MailboxX).First();
            Assert.Equal(OpResult.Ok, SetStop(world, host, 0, new MailFilter(FilterKind.Address, far.Id)));
            Load(world, truck, far, 2);
            Assert.Equal(OpResult.Ok, Hire(world, host));
            ToDelivery(world, host);
            var d = world.DriverAt(depot)!;
            RunUntil(world, () => d.State == DriverState.Out, 6);
            world.SpawnEnemy(Db.Enemy("barbarian"), far.MailboxX, far.MailboxZ + 2f);
            RunUntil(world, () => d.State == DriverState.Fleeing, 2);
            RunUntil(world, () => d.State == DriverState.Waiting, 60);
            Assert.Equal(2, MailIn(truck.Cargo));
            Assert.Same(truck, world.InBay(depot));
        }

        [Fact]
        public void AtPaydayTheHireEnds_AndTheTruckIsPutBackInItsBay()
        {
            var (world, host, depot, truck) = Setup();
            var far = world.Map.Destinations.OrderByDescending(h => h.MailboxX).First();
            Assert.Equal(OpResult.Ok, SetStop(world, host, 0, new MailFilter(FilterKind.Street, far.Address.Street)));
            Load(world, truck, far, 2);
            Assert.Equal(OpResult.Ok, Hire(world, host));
            ToDelivery(world, host);
            var d = world.DriverAt(depot)!;
            RunUntil(world, () => d.State == DriverState.Out, 6);
            SimDriver.StepTicks(world, Tps);
            Assert.True(truck.NpcDriving && world.InBay(depot) == null);
            // The clock runs out with the truck on the road.
            world.Run.PhaseEndTick = world.Tick + 1;
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 2);
            world.Step();
            Assert.Equal(DriverState.None, d.State);
            Assert.False(truck.NpcDriving);
            Assert.Same(truck, world.InBay(depot));
        }
    }
}
