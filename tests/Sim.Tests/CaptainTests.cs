using System;
using System.Collections.Generic;
using System.Linq;
using PerformativeMail.Sim.Automation;
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
    /// The water navmesh and the NPC captain (doc 04 §3.3 "NPC captains behave like NPC drivers on
    /// water routes … a coarse water navmesh generated per seed", doc 10 §3 npc_captain; design
    /// choice 115): ways over the water round what is built on it, berths by the docks, and a
    /// captain hired at a Small Port's Route Console sailing its Motor Boat to the Business Dock
    /// with the Cargo for it and back.
    /// </summary>
    public class CaptainTests
    {
        private uint _nextRequest = 1;
        private uint _nextMail = 1;
        private const int Tps = SimConstants.TickRate;
        private const int South = 1;

        private static ContentDb Db => TestContent.Db;
        private static VehicleDef Motorboat => Db.Vehicle("motorboat");
        private static BuildingDef Port => Db.Building("small_port");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static void RunUntil(SimWorld world, Func<bool> done, int maxSeconds, Action? each = null)
        {
            for (int i = 0; i < maxSeconds * Tps; i++)
            {
                if (done()) return;
                world.Step();
                each?.Invoke();
            }
            Assert.True(done(), $"not done after {maxSeconds} s (phase {world.Run.Phase})");
        }

        /// <summary>A Small Port as far west on the deep quay as it fits, a Motor Boat in its bay, in Prep of the shift.</summary>
        private (SimWorld World, Player Host, Construct Port, Vehicle Boat) Setup(int shift = 3)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(5000);
            Assert.True(world.Grant(Db.ShopItem("bp_motorboat"), host.Id));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 16)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 10)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 6)));
            int x = Enumerable.Range(2, world.Map.Cols - 2).First(tx => BuildRules.CheckPlacement(world.Map, world.Constructs, Port, tx, 24, South) == OpResult.Ok);
            SimDriver.Teleport(host, BuildRules.TileCentre(x - 1), BuildRules.TileCentre(23));
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Port.Index, x, 24, South)));
            var port = world.Constructs.At(x, 24)!;
            SimWorld.Bay(port, out float bx, out float bz, out float heading);
            var boat = world.AddVehicleAt(Motorboat, bx, bz, heading, host.Id);
            Assert.Same(boat, world.InBay(port));
            return (world, host, port, boat);
        }

        private OpResult Configure(SimWorld world, Player p, Construct port, ushort what, int output = 0, ushort filter = 0) =>
            Request(world, p, SimRequest.Configure(0, port.Tx, port.Tz, what, output, 0, filter));

        private static DestinationDef Dock(SimWorld world) => world.Map.Destinations.Single(d => d.IsDock);

        private static int MailIn(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);

        [Fact]
        public void TheWaterNavmesh_SailsRoundAPier_FindsNoWayOntoLand_AndBerthsByTheDockOnly()
        {
            var (world, host, _, _) = Setup();
            var map = world.Map;
            var points = new List<(float X, float Z)>();
            // West of where a pier will stand to east of it, in the shallows of row 27.
            float ax = BuildRules.TileCentre(3), bx = BuildRules.TileCentre(7), z = BuildRules.TileCentre(27);
            Assert.InRange(WaterNav.Route(map, world.Constructs, ax, z, bx, z, 0.5f, points), 7.9f, 8.1f);
            Assert.Equal(2, points.Count);

            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 6)));
            SimDriver.Teleport(host, BuildRules.TileCentre(5), BuildRules.TileCentre(24));
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Db.Building("pier").Index, 5, 26, South)));
            float length = WaterNav.Route(map, world.Constructs, ax, z, bx, z, 0.5f, points);
            Assert.True(length > 10f, $"round the pier is {length} m");
            for (int i = 1; i < points.Count; i++)
                Assert.True(WaterNav.Clear(map, world.Constructs, points[i - 1].X, points[i - 1].Z, points[i].X, points[i].Z, 0.5f));
            Assert.All(points, p => Assert.False((int)MathF.Floor(p.X / 2f) == 5 && (int)MathF.Floor(p.Z / 2f) <= 28, "a point on the pier"));

            // Onto the street: no way. A house: no berth. The dock: a berth at its quay.
            Assert.True(WaterNav.Route(map, world.Constructs, ax, z, 40f, 28f, 0.5f, points) < 0f);
            float reach = Db.Balance.GetFloat("dockZoneReach");
            Assert.False(WaterNav.Berth(map, world.Constructs, map.Destinations[0], reach, out _, out _));
            var dock = Dock(world);
            Assert.True(WaterNav.Berth(map, world.Constructs, dock, reach, out float qx, out float qz));
            Assert.True(map.IsWater((int)MathF.Floor(qx / 2f), (int)MathF.Floor(qz / 2f)));
            Assert.InRange(MathF.Sqrt((qx - dock.MailboxX) * (qx - dock.MailboxX) + (qz - dock.MailboxZ) * (qz - dock.MailboxZ)), 0f, reach);
        }

        [Fact]
        public void ACaptainIsHiredAtThePortsConsole_FromShiftThree_ForADollarFifty()
        {
            Assert.Equal((6, 150), (Port.RouteStops, Port.HireCost));
            Assert.True(Port.HasConsole);
            var (early, host2, port2, _) = Setup(shift: 2);
            Assert.Equal(OpResult.ShopLocked, Configure(early, host2, port2, SimRequest.ConfigureHire));

            var (world, host, port, _) = Setup();
            int wallet = world.Economy.Wallet;
            // Stops: the dock by its address; a kind is no stop.
            Assert.Equal(OpResult.Ok, Configure(world, host, port, SimRequest.ConfigureSet, 0, new MailFilter(FilterKind.Address, Dock(world).Id).Encode()));
            Assert.NotEqual(OpResult.Ok, Configure(world, host, port, SimRequest.ConfigureSet, 1, new MailFilter(FilterKind.Kind, 0).Encode()));
            Assert.Equal(OpResult.Ok, Configure(world, host, port, SimRequest.ConfigureHire));
            Assert.Equal(wallet - 150, world.Economy.Wallet);
            Assert.Equal(DriverState.Waiting, world.DriverAt(port)!.State);
            Assert.Equal(OpResult.AlreadyHired, Configure(world, host, port, SimRequest.ConfigureHire));
        }

        [Fact]
        public void TheCaptainSailsTheCargoToTheDock_OverTheWaterOnly_AtSixtyPercent_AndComesBackIntoTheBay()
        {
            var (world, host, port, boat) = Setup();
            var dock = Dock(world);
            Assert.Equal(OpResult.Ok, Configure(world, host, port, SimRequest.ConfigureSet, 0, new MailFilter(FilterKind.Address, dock.Id).Encode()));
            Assert.Equal(OpResult.Ok, Configure(world, host, port, SimRequest.ConfigureHire));
            Assert.True(world.Give(boat.Cargo, ItemStack.OfMail(TestContent.Mail("cargo", dock.Address, _nextMail++, 3))));
            Assert.True(world.Give(boat.Cargo, ItemStack.OfMail(TestContent.Mail("letter", world.Map.Destinations[0].Address, _nextMail++, 3))));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Delivery, 5);
            var d = world.DriverAt(port)!;

            bool onWater = true;
            void Check()
            {
                if (!boat.NpcDriving) return;
                int tx = (int)MathF.Floor(boat.X / 2f), tz = (int)MathF.Floor(boat.Z / 2f);
                onWater &= PlayerMovement.BoatGoes(world.Map, world.Constructs, tx, tz);
            }
            RunUntil(world, () => d.State == DriverState.Out, 8, Check);
            Assert.True(boat.NpcDriving);
            Assert.Equal(dock.Id, d.Houses.Single().Id);
            // Under way: 60 % of 9 m/s.
            SimDriver.StepTicks(world, Tps / 2);
            float moved = 0f;
            for (int i = 0; i < Tps / 2; i++)
            {
                float x0 = boat.X, z0 = boat.Z;
                world.Step();
                moved += MathF.Sqrt((boat.X - x0) * (boat.X - x0) + (boat.Z - z0) * (boat.Z - z0));
            }
            Assert.InRange(moved, 0.6f * 9f * 0.5f - 0.1f, 0.6f * 9f * 0.5f + 0.02f);

            world.Events.Clear();
            var delivered = new List<DeliveredEvent>();
            RunUntil(world, () => d.State == DriverState.Waiting, 60, () => { Check(); delivered.AddRange(world.Events.OfType<DeliveredEvent>()); world.Events.Clear(); });
            Assert.True(onWater, "the boat left the water");
            var crate = Assert.Single(delivered);
            Assert.Equal((DeliveryMachine.Captain, dock.Id), (crate.Machine, crate.DestId));
            // The letter rode home; the boat is back in the bay.
            Assert.Equal(1, MailIn(boat.Cargo));
            Assert.Same(boat, world.InBay(port));
            Assert.Equal(1, d.Delivered);
        }

        [Fact]
        public void AtPaydayTheCaptainsHireEnds_AndTheBoatIsPutBackInTheBay()
        {
            var (world, host, port, boat) = Setup();
            var dock = Dock(world);
            Assert.Equal(OpResult.Ok, Configure(world, host, port, SimRequest.ConfigureSet, 0, new MailFilter(FilterKind.Address, dock.Id).Encode()));
            Assert.Equal(OpResult.Ok, Configure(world, host, port, SimRequest.ConfigureHire));
            Assert.True(world.Give(boat.Cargo, ItemStack.OfMail(TestContent.Mail("cargo", dock.Address, _nextMail++, 3))));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Delivery, 5);
            var d = world.DriverAt(port)!;
            RunUntil(world, () => d.State == DriverState.Out, 8);
            SimDriver.StepTicks(world, Tps);
            Assert.True(boat.NpcDriving && world.InBay(port) == null);
            world.Run.PhaseEndTick = world.Tick + 1;
            RunUntil(world, () => world.Run.Phase == RunPhase.Payday, 2);
            SimDriver.StepTicks(world, 2);
            Assert.Equal(DriverState.None, d.State);
            Assert.False(boat.NpcDriving);
            Assert.Same(boat, world.InBay(port));
        }
    }
}
