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
    /// Doc 11 §4 "Motorboat to second town · Large Island only" (doc 04 §3.3, doc 10 motorboat / bp_motorboat): on the Large
    /// Island a Small Port in the Post Office's town and its Motor Boat take Cargo over the sea to the Business Dock of the town
    /// past the inlet, and the other mail for that town to the team's second Small Port there, where an inserter behind the
    /// port's loading face takes it out of the boat — whether a hired captain sails it or a carrier does (design choice 175).
    /// </summary>
    public class FarTownRouteTests
    {
        private uint _nextRequest = 1;
        private uint _nextMail = 880000;
        private const int Tps = SimConstants.TickRate;
        private const int South = 1, North = 3;

        private static ContentDb Db => TestContent.Db;
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

        private static int MailIn(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);

        /// <summary>The Business Dock of the town past the inlet (its street's district 3 or later), and the Post Office town's.</summary>
        internal static DestinationDef FarDock(WorldMap map) => map.Destinations.Single(d => d.IsDock && d.Address.District >= 3);
        internal static DestinationDef NearDock(WorldMap map) => map.Destinations.Single(d => d.IsDock && d.Address.District <= 2);

        /// <summary>The place for a Small Port facing the sea nearest a point of the beach, or none.</summary>
        internal static (int X, int Z)? PortSite(WorldMap map, ConstructField constructs, float nearX, bool roomBehind = false)
        {
            int cx = (int)(nearX / SimConstants.TileSize);
            (int, int)? best = null;
            int bestD = int.MaxValue;
            for (int x = Math.Max(0, cx - 40); x < Math.Min(map.Cols, cx + 40); x++)
                for (int z = 0; z < map.Rows; z++)
                {
                    if (Math.Abs(x - cx) >= bestD) continue;
                    if (BuildRules.CheckPlacement(map, constructs, Port, x, z, South) != OpResult.Ok) continue;
                    // An inserter and a chest behind its loading face (the back middle tile, a column west of the anchor).
                    if (roomBehind && (BuildRules.CheckTile(map, constructs, x - 1, z - 1) != OpResult.Ok || BuildRules.CheckTile(map, constructs, x - 1, z - 2) != OpResult.Ok)) continue;
                    best = (x, z);
                    bestD = Math.Abs(x - cx);
                }
            return best;
        }

        private Construct Build(SimWorld world, Player host, BuildingDef def, int x, int z, int rotation, float standX, float standZ)
        {
            SimDriver.Teleport(host, standX, standZ);
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, def.Index, x, z, rotation)));
            return world.Constructs.At(x, z)!;
        }

        private Construct BuildPort(SimWorld world, Player host, float nearX, bool roomBehind = false)
        {
            var site = PortSite(world.Map, world.Constructs, nearX, roomBehind);
            Assert.True(site.HasValue, $"no place for a Small Port near x {nearX}");
            var (x, z) = site!.Value;
            return Build(world, host, Port, x, z, South, BuildRules.TileCentre(x + 1), BuildRules.TileCentre(z - 1));
        }

        /// <summary>
        /// Shift 4 of a Large Island run (its docks open): a Small Port by the Post Office town's dock with a Motor Boat in its
        /// bay, and (if asked) a second one by the far town's dock with an inserter behind its loading face into a chest.
        /// </summary>
        private (SimWorld World, Player Host, Construct Home, Vehicle Boat, Construct? Far, GridContainer? Chest) Setup(uint seed, bool farPort)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = seed, MapId = LargeIslandGenerator.LargeIsland, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.NoQuota | PlaytestFlags.NoTimer, 4);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(5000);
            Assert.True(world.Grant(Db.ShopItem("bp_motorboat"), host.Id));
            foreach (var (item, n) in new[] { ("plank", 40), ("stone", 20), ("iron_ingot", 20), ("log", 20) })
                Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item(item), n)));
            var home = BuildPort(world, host, NearDock(world.Map).MailboxX);
            SimWorld.Bay(home, out float bx, out float bz, out float heading);
            var boat = world.AddVehicleAt(Db.Vehicle("motorboat"), bx, bz, heading, host.Id);
            Assert.Same(boat, world.InBay(home));
            if (!farPort) return (world, host, home, boat, null, null);

            var far = BuildPort(world, host, FarDock(world.Map).MailboxX, roomBehind: true);
            var (lx, lz) = SimWorld.PortLoadingTile(far);
            // Behind the quay: the inserter faces inland, its back on the loading face; the chest in front of it.
            Build(world, host, Db.Building("inserter"), lx, lz - 1, North, BuildRules.TileCentre(lx + 1), BuildRules.TileCentre(lz - 1));
            var chest = Build(world, host, Db.Building("chest"), lx, lz - 2, 0, BuildRules.TileCentre(lx + 1), BuildRules.TileCentre(lz - 2));
            // Back at the home port's console.
            SimDriver.Teleport(host, home.CentreX, home.CentreZ - 2f);
            SimDriver.StepTicks(world, 12);
            return (world, host, home, boat, far, world.GetContainer(chest.ContainerId)!);
        }

        private OpResult SetStop(SimWorld world, Player p, Construct port, int stop, MailFilter filter) =>
            Request(world, p, SimRequest.Configure(0, port.Tx, port.Tz, SimRequest.ConfigureSet, stop, 0, filter.Encode()));

        [Theory]
        [InlineData(1u)]
        [InlineData(2u)]
        public void ACaptainSailsCargoOverTheSea_ToTheFarTownsDock_AndBack(uint seed)
        {
            var (world, host, home, boat, _, _) = Setup(seed, farPort: false);
            var dock = FarDock(world.Map);
            Assert.Equal(OpResult.Ok, SetStop(world, host, home, 0, new MailFilter(FilterKind.Address, dock.Id)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Configure(0, home.Tx, home.Tz, SimRequest.ConfigureHire)));
            Assert.True(world.Give(boat.Cargo, ItemStack.OfMail(TestContent.Mail("cargo", dock.Address, _nextMail++, 4))));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Delivery, 5);
            var d = world.DriverAt(home)!;
            bool onWater = true;
            var delivered = new List<DeliveredEvent>();
            void Each()
            {
                int tx = (int)MathF.Floor(boat.X / 2f), tz = (int)MathF.Floor(boat.Z / 2f);
                if (boat.NpcDriving) onWater &= PlayerMovement.BoatGoes(world.Map, world.Constructs, tx, tz);
                delivered.AddRange(world.Events.OfType<DeliveredEvent>());
                world.Events.Clear();
            }
            RunUntil(world, () => d.State == DriverState.Out, 8, Each);
            RunUntil(world, () => d.State == DriverState.Waiting, 240, Each);
            Assert.True(onWater, "the boat left the water");
            var crate = Assert.Single(delivered);
            Assert.Equal((DeliveryMachine.Captain, dock.Id), (crate.Machine, crate.DestId));
            Assert.Same(boat, world.InBay(home));
        }

        /// <summary>A house of the far town open in shift 4 that the far port is the nearest port to.</summary>
        private static DestinationDef FarHouse(SimWorld world, Construct home) =>
            world.Map.Houses.Where(h => h.Address.District is 3 or 4 && SeaRoute.PortFor(world.Constructs, home, h) != null)
                .OrderBy(h => MathF.Abs(h.MailboxX - FarDock(world.Map).MailboxX)).First();

        [Theory]
        [InlineData(1u)]
        [InlineData(2u)]
        public void TheCaptainCallsAtTheFarDock_ThenLiesInTheFarPort_WhereItsInserterTakesTheTownsLettersOut(uint seed)
        {
            var (world, host, home, boat, far, chest) = Setup(seed, farPort: true);
            var dock = FarDock(world.Map);
            var house = FarHouse(world, home);
            var near = world.Map.Houses.First(h => h.Address.District == 1);
            Assert.Equal(OpResult.Ok, SetStop(world, host, home, 0, new MailFilter(FilterKind.Address, dock.Id)));
            Assert.Equal(OpResult.Ok, SetStop(world, host, home, 1, new MailFilter(FilterKind.District, house.Address.District)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Configure(0, home.Tx, home.Tz, SimRequest.ConfigureHire)));
            Assert.True(world.Give(boat.Cargo, ItemStack.OfMail(TestContent.Mail("cargo", dock.Address, _nextMail++, 4))));
            for (int i = 0; i < 3; i++) Assert.True(world.Give(boat.Cargo, ItemStack.OfMail(TestContent.Mail("letter", house.Address, _nextMail++, 4))));
            // A letter for the Post Office's town is not on the route: it stays aboard.
            Assert.True(world.Give(boat.Cargo, ItemStack.OfMail(TestContent.Mail("letter", near.Address, _nextMail++, 4))));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Delivery, 5);
            var d = world.DriverAt(home)!;
            var delivered = new List<DeliveredEvent>();
            var told = new List<DriverEvent>();
            bool lay = false;
            void Each()
            {
                delivered.AddRange(world.Events.OfType<DeliveredEvent>());
                told.AddRange(world.Events.OfType<DriverEvent>());
                lay |= d.State == DriverState.Unloading && d.PortLeg == far && world.LyingAt(far!) == boat;
                world.Events.Clear();
            }
            RunUntil(world, () => d.State == DriverState.Out, 8, Each);
            Assert.Equal((1, 1), (d.Houses.Count, d.Ports.Count));
            Assert.Same(far, d.Ports[0]);
            RunUntil(world, () => d.State == DriverState.Waiting, 300, Each);
            Assert.True(lay, "the boat never lay in the far port's bay");
            var crate = Assert.Single(delivered);
            Assert.Equal((DeliveryMachine.Captain, dock.Id), (crate.Machine, crate.DestId));
            Assert.Equal(3, MailIn(chest!));
            Assert.All(chest!.Entries, e => Assert.Equal(house.Address, e.Stack.Address));
            Assert.Equal(1, MailIn(boat.Cargo));
            Assert.Same(boat, world.InBay(home));
            // The clients hear the leg: sailing to the port, then lying there (the dock's leg first).
            Assert.Contains(told, e => e.State == DriverState.Out && e.ToPort && (e.PortX, e.PortY) == ((ushort)far!.Tx, (ushort)far.Tz) && e.Houses == 2 && e.At == 1);
            Assert.Contains(told, e => e.State == DriverState.Unloading && e.ToPort);
        }

        [Fact]
        public void ACarrierLeavesTheBoatInTheFarPortsBay_AndItsInserterUnloadsIt()
        {
            var (world, host, home, boat, far, chest) = Setup(1, farPort: true);
            var house = FarHouse(world, home);
            for (int i = 0; i < 2; i++) Assert.True(world.Give(boat.Cargo, ItemStack.OfMail(TestContent.Mail("letter", house.Address, _nextMail++, 4))));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            RunUntil(world, () => world.Run.Phase == RunPhase.Delivery, 5);
            // Nobody unloads it at home: no inserter behind the home port.
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Equal(2, MailIn(boat.Cargo));

            // Sailed across (moved there, rider and all), left with its middle on the far port's water: into the bay.
            SimWorld.Bay(far!, out float bx, out float bz, out _);
            boat.X = bx + 0.6f;
            boat.Z = bz - 0.3f;
            Assert.Null(world.LyingAt(far!));
            SimDriver.Teleport(host, boat.X, boat.Z);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, boat.Id.Value)));
            Assert.True(host.Move.Riding);
            for (int i = 0; i < 3 && host.Move.Riding; i++)
            {
                world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, Buttons = InputButtons.Jump });
                world.Step();
            }
            Assert.False(host.Move.Riding);
            Assert.Same(boat, world.LyingAt(far!));
            RunUntil(world, () => MailIn(chest!) == 2, 5);
            Assert.Equal(0, MailIn(boat.Cargo));
        }
    }
}
