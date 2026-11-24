using System;
using System.Linq;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Mail;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.Vehicles;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Mail Truck (doc 04 §3.1, doc 10 §1.3 and §3): $9.00 at the counter from shift 2 with the
    /// Motor Pool blueprint; 14 m/s on the road, 7 off it; an Oil Can drives it 5 minutes; a cargo
    /// bay of 8x10 loaded at its rear by belts and inserters while it stands parked.
    /// </summary>
    public class TruckTests
    {
        private const int Tps = 30;
        private const float StreetZ = 28f;
        private uint _nextRequest = 1;
        private uint _nextMail = 985000;

        private static ContentDb Db => TestContent.Db;
        private static VehicleDef Truck => Db.Vehicle("mail_truck");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InPrep(int shift = 1)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            if (shift > 1) world.SetPlaytest(host, PlaytestFlags.None, shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal((RunPhase.Prep, shift), (world.Run.Phase, world.Run.Shift));
            world.Economy.AddLoot(3000);
            return (world, host);
        }

        private static void Drive(SimWorld world, Player p, float moveX, float moveZ, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.QueueInput(p.Id, new PlayerInput { Seq = p.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(moveX), MoveZ = PlayerInput.QuantizeAxis(moveZ) });
                world.Step();
            }
        }

        private Vehicle Mounted(SimWorld world, Player p, float x, float z, float heading)
        {
            var truck = world.AddVehicleAt(Truck, x, z, heading, p.Id);
            SimDriver.Teleport(p, x + 1.2f, z);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Mount(0, truck.Id.Value)));
            return truck;
        }

        private static int MailIn(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);

        [Fact]
        public void TheContentHasTheTruckOfTheDocs()
        {
            Assert.Equal((14f, 7f, 600f, 2), (Truck.RoadSpeed, Truck.OffRoadSpeed, Truck.Hp, Truck.Seats));
            Assert.Equal((8, 10), (Truck.Cargo.Cols, Truck.Cargo.Rows));
            Assert.Equal((300f, 600f, true), (Truck.FuelSecondsPerCan, Truck.TankSeconds, Truck.LoadingFace));
            Assert.True(Truck.BodyOffset > 1f);
            var card = Db.ShopItem("mail_truck");
            var bp = Db.ShopItem("bp_truck");
            Assert.Equal((ShopKind.Vehicle, 900, 2), (card.Kind, card.Price, card.FromShift));
            Assert.Equal((ShopKind.Blueprint, 500, 2), (bp.Kind, bp.Price, bp.FromShift));
            Assert.Same(bp, card.Requires);
            Assert.Same(Truck, card.Vehicle);
            Assert.False(Db.Vehicle("bike").Fuelled);
        }

        [Fact]
        public void TheTruckIsSoldWithTheMotorPoolBlueprint_BesideABike_AndComesWithFiveMinutesOfFuel()
        {
            var (world, host) = InPrep(shift: 2);
            var po = world.Map.PostOffice;
            SimDriver.Teleport(host, po.ShopX + 1f, po.ShopZ);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.NeedsBlueprint, Request(world, host, SimRequest.Buy(0, Db.ShopItem("mail_truck").Index)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, Db.ShopItem("bp_truck").Index)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, Db.ShopItem("bike").Index)));
            world.Events.Clear();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, Db.ShopItem("mail_truck").Index)));
            var truck = world.Vehicles.Single(v => v.Def == Truck);
            Assert.Equal(300f, truck.Fuel);
            // One of each kind for a solo carrier.
            Assert.Equal(OpResult.OnePerCarrier, Request(world, host, SimRequest.Buy(0, Db.ShopItem("mail_truck").Index)));
        }

        [Fact]
        public void ItRunsFourteenMetresASecondOnTheRoad_BurnsFuel_AndDryItSetsItsDriverDown()
        {
            var (world, host) = InPrep();
            var truck = Mounted(world, host, 30f, StreetZ, -MathF.PI / 2f);
            Drive(world, host, 1f, 0f, 5 * Tps);
            Assert.Equal(14f, host.Move.RideSpeed, 2);
            Assert.InRange(truck.Fuel, 300f - 5.1f, 300f - 4.0f);
            Assert.Contains(world.Events, e => e is VehicleFuelEvent f && f.Vehicle == truck.Id);
            // Nearly dry: it stops and the driver is on their feet.
            truck.Fuel = 0.2f;
            Drive(world, host, -1f, 0f, Tps);
            Assert.Equal(0f, truck.Fuel);
            Assert.False(truck.Ridden);
            Assert.False(host.Move.Riding);
            SimDriver.Teleport(host, truck.X, truck.Z + 1.5f);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.NoFuel, Request(world, host, SimRequest.Mount(0, truck.Id.Value)));
            // An Oil Can fills it five minutes; a second and a third: the tank holds ten.
            Assert.Equal(OpResult.NoOilCan, Request(world, host, SimRequest.Refuel(0, truck.Id.Value)));
            Assert.True(world.Give(host.Hotbar, ItemStack.OfItem(Db.Item("oil_can"), 3)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Refuel(0, truck.Id.Value)));
            Assert.Equal(300f, truck.Fuel);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Refuel(0, truck.Id.Value)));
            Assert.Equal(OpResult.TankFull, Request(world, host, SimRequest.Refuel(0, truck.Id.Value)));
            Assert.Equal(600f, truck.Fuel);
            Assert.Equal(1, host.Hotbar.Entries.Where(e => e.Stack.Kind == StackKind.Item).Sum(e => e.Stack.ItemCount));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, truck.Id.Value)));
        }

        [Fact]
        public void ItsNoseBumpsIntoAHouse_NotJustItsMiddle()
        {
            var (world, host) = InPrep();
            // House 1's building ends at z 20; drive north at it from the street.
            Mounted(world, host, 23f, StreetZ, 0f);
            Drive(world, host, 0f, -1f, 3 * Tps);
            float nose = host.Move.Z - Truck.BodyOffset;
            Assert.True(nose > 20f + Truck.Radius - 0.05f && nose < 20f + Truck.Radius + 0.3f, $"nose circle at z {nose}");
            Assert.True(host.Move.RideSpeed < 0.5f, $"speed {host.Move.RideSpeed}");
        }

        [Fact]
        public void ParkedWithItsRearAtTheEndOfABelt_OrBeforeAnInserter_ItIsLoaded()
        {
            var (world, host) = InPrep();
            // Two belts east along row 22 ending into tile (12, 22); the truck's rear on that tile, its nose east.
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 10)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 10)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 10)));
            SimDriver.Teleport(host, BuildRules.TileCentre(10), BuildRules.TileCentre(21));
            SimDriver.StepTicks(world, 12);
            var belt = Db.Building("belt_mk1");
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, belt.Index, 10, 22, 0)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, belt.Index, 11, 22, 0)));
            float heading = -MathF.PI / 2f;
            var truck = world.AddVehicleAt(Truck, BuildRules.TileCentre(12) + Truck.Length * 0.5f, BuildRules.TileCentre(22), heading);
            Assert.Same(truck, world.LoadingFaceOn(12, 22));
            var address = world.Map.Destinations[1].Address;
            var seg = world.Belts.SegmentAt(10, 22)!;
            for (int i = 0; i < 3; i++)
            {
                Assert.True(world.PutOnBelt(seg, TestContent.Mail("letter", address, _nextMail++, 1)));
                SimDriver.StepTicks(world, 10);
            }
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Equal(3, MailIn(truck.Cargo));
            Assert.Equal(0, world.Belts.ItemCount);

            // Ridden, it is not loaded: the next piece waits at the end.
            SimDriver.Teleport(host, truck.X, truck.Z + 1.5f);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, truck.Id.Value)));
            Assert.Null(world.LoadingFaceOn(12, 22));
            Assert.True(world.PutOnBelt(world.Belts.SegmentAt(10, 22)!, TestContent.Mail("letter", address, _nextMail++, 1)));
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Equal(1, world.Belts.ItemCount);

            // An inserter from a chest, into a truck parked before it.
            var (w2, h2) = InPrep();
            Assert.True(w2.Give(h2.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 10)));
            Assert.True(w2.Give(h2.Inventory, ItemStack.OfItem(Db.Item("plank"), 10)));
            Assert.True(w2.Give(h2.Inventory, ItemStack.OfItem(Db.Item("log"), 10)));
            SimDriver.Teleport(h2, BuildRules.TileCentre(10), BuildRules.TileCentre(21));
            SimDriver.StepTicks(w2, 12);
            Assert.Equal(OpResult.Ok, Request(w2, h2, SimRequest.PlaceConstruct(0, Db.Building("chest").Index, 10, 22, 0)));
            Assert.Equal(OpResult.Ok, Request(w2, h2, SimRequest.PlaceConstruct(0, Db.Building("inserter").Index, 11, 22, 0)));
            var t2 = w2.AddVehicleAt(Truck, BuildRules.TileCentre(12) + Truck.Length * 0.5f, BuildRules.TileCentre(22), heading);
            var chest = w2.GetContainer(w2.Constructs.At(10, 22)!.ContainerId)!;
            for (int i = 0; i < 2; i++) Assert.True(w2.Give(chest, ItemStack.OfMail(TestContent.Mail("letter", address, _nextMail++, 1))));
            SimDriver.StepTicks(w2, 3 * Tps);
            Assert.Equal((0, 2), (MailIn(chest), MailIn(t2.Cargo)));
        }
    }
}
