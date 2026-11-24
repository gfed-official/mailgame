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
    /// The Motor Boat and the Small Port (doc 04 §3.1 and §3.3, doc 10 §1.3, §2 and §3): behind the
    /// Harbour blueprint from shift 3, a boat of 9 m/s on Oil Cans with a hold of 8 × 10 for mail,
    /// and a port of 3 × 4 tiles — two on the quay, two over deep water — whose bay takes the boat
    /// and whose loading face on the quay loads it from a belt (or an inserter facing it).
    /// </summary>
    public class PortTests
    {
        private uint _nextRequest = 1;
        private uint _nextMail = 993000;
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

        private (SimWorld World, Player Host) InPrep(bool blueprint = true)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, 3);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(5000);
            if (blueprint) Assert.True(world.Grant(Db.ShopItem("bp_motorboat"), host.Id));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 10)));
            return (world, host);
        }

        private OpResult Place(SimWorld world, Player p, BuildingDef def, int tx, int tz, int rotation) =>
            Request(world, p, SimRequest.PlaceConstruct(0, def.Index, tx, tz, rotation));

        /// <summary>The first spot from the east along the quay where a port facing south fits, the host walked there.</summary>
        private static int PortSpot(SimWorld world, Player host)
        {
            for (int x = world.Map.Cols - 1; x >= 2; x--)
                if (BuildRules.CheckPlacement(world.Map, world.Constructs, Port, x, 24, South) == OpResult.Ok)
                {
                    SimDriver.Teleport(host, BuildRules.TileCentre(x - 1), BuildRules.TileCentre(23));
                    SimDriver.StepTicks(world, 12);
                    return x;
                }
            throw new InvalidOperationException("no room for a port on the quay");
        }

        private static int MailIn(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);

        [Fact]
        public void TheContentHasTheMotorBoatAndTheSmallPortOfTheDocs()
        {
            Assert.Equal((9f, 500f, 2, true, true, true), (Motorboat.WaterSpeed, Motorboat.Hp, Motorboat.Seats, Motorboat.OnWater, Motorboat.Fuelled, Motorboat.LoadingFace));
            Assert.Equal((8, 10), (Motorboat.Cargo.Cols, Motorboat.Cargo.Rows));
            Assert.Equal((300f, 600f), (Motorboat.FuelSecondsPerCan, Motorboat.TankSeconds));
            var bp = Db.ShopItem("bp_motorboat");
            var card = Db.ShopItem("motorboat");
            Assert.Equal((ShopKind.Blueprint, 500, 3), (bp.Kind, bp.Price, bp.FromShift));
            Assert.Equal((ShopKind.Vehicle, 700, 3), (card.Kind, card.Price, card.FromShift));
            Assert.Same(bp, card.Requires);
            Assert.Equal((3, 4, 1200, true, false), (Port.Width, Port.Length, Port.Hp, Port.IsPort, Port.Solid));
            Assert.Same(bp, Port.Recipe.Blueprint);
            Assert.Equal(new[] { ("plank", 16), ("stone", 10), ("iron_ingot", 6) }, Port.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
        }

        [Fact]
        public void AMotorBoatIsSoldWithTheHarbourBlueprint_DrivenAtNineMetresASecond_OnFuel()
        {
            var (world, host) = InPrep(blueprint: false);
            SimDriver.Teleport(host, world.Map.PostOffice.ShopX, world.Map.PostOffice.ShopZ + 1.2f);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.NeedsBlueprint, Request(world, host, SimRequest.Buy(0, Db.ShopItem("motorboat").Index)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, Db.ShopItem("bp_motorboat").Index)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, Db.ShopItem("motorboat").Index)));
            var boat = world.Vehicles.Single(v => v.Def == Motorboat);
            Assert.True(world.Map.IsWater((int)MathF.Floor(boat.X / 2f), (int)MathF.Floor(boat.Z / 2f)));
            Assert.Equal(300f, boat.Fuel);

            // Out in the deep water, east: 9 m/s, burning.
            boat.X = 20f; boat.Z = 62f; boat.Heading = -MathF.PI / 2f;
            SimDriver.Teleport(host, boat.X, boat.Z);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, boat.Id.Value)));
            for (int i = 0; i < 150; i++)
            {
                world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(1f) });
                world.Step();
            }
            Assert.Equal(9f, host.Move.RideSpeed, 2);
            Assert.InRange(boat.Fuel, 294f, 296f);
        }

        [Fact]
        public void APort_HasItsQuayOnLand_AndTwoRowsOverDeepWater()
        {
            var (bare, nobody) = InPrep(blueprint: false);
            int x0 = PortSpot(bare, nobody);
            Assert.Equal(OpResult.NeedsBlueprint, Place(bare, nobody, Port, x0, 24, South));

            var (world, host) = InPrep();
            int x = PortSpot(world, host);
            // Over the shallow water under the beach, or with its quay in the water: no.
            Assert.Equal(OpResult.NotOnWater, BuildRules.CheckPlacement(world.Map, world.Constructs, Port, 5, 24, South));
            Assert.NotEqual(OpResult.Ok, BuildRules.CheckPlacement(world.Map, world.Constructs, Port, x, 25, South));
            Assert.Equal(OpResult.Ok, Place(world, host, Port, x, 24, South));
            var port = world.Constructs.At(x, 24)!;
            Assert.All(Enumerable.Range(0, 12).Select(port.TileAt), t => Assert.Same(port, world.Constructs.At(t.Tx, t.Tz)));
            Assert.Equal((x - 1, 24), SimWorld.PortLoadingTile(port));
            // The quay is walked on; its water rows are a boat's, not a walker's.
            var (qx, qz) = port.TileAt(1);
            Assert.True(PlayerMovement.Walkable(world.Map, world.Constructs, qx, qz));
            var (wx, wz) = port.TileAt(port.Def.Length + 2);
            Assert.False(PlayerMovement.Walkable(world.Map, world.Constructs, wx, wz));
            Assert.True(PlayerMovement.BoatGoes(world.Map, world.Constructs, wx, wz));
            // Its pontoons either side of the bay are in a boat's way.
            var (px, pz) = port.TileAt(2);
            Assert.False(PlayerMovement.BoatGoes(world.Map, world.Constructs, px, pz));
        }

        [Fact]
        public void AMotorBoatLeftOnThePortsWater_LiesInTheBay_AndABeltIntoTheQuaysLoadingFaceLoadsIt()
        {
            var (world, host) = InPrep();
            int x = PortSpot(world, host);
            Assert.Equal(OpResult.Ok, Place(world, host, Port, x, 24, South));
            var port = world.Constructs.At(x, 24)!;
            SimWorld.Bay(port, out float bx, out float bz, out float heading);
            Assert.Equal((BuildRules.TileCentre(x - 1), 54f), (bx, bz));

            // Driven in crooked and left: set into the bay, its stern at the quay.
            var boat = world.AddVehicleAt(Motorboat, bx + 0.6f, bz - 0.3f, 0.4f, host.Id);
            SimDriver.Teleport(host, boat.X, boat.Z);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, boat.Id.Value)));
            for (int i = 0; i < 3; i++)
            {
                world.QueueInput(host.Id, new PlayerInput { Seq = host.LastQueuedSeq + 1, Buttons = InputButtons.Jump });
                world.Step();
            }
            Assert.False(host.Move.Riding);
            Assert.Equal((bx, bz, heading), (boat.X, boat.Z, boat.Heading));
            Assert.Same(boat, world.InBay(port));
            // Out onto the quay.
            Assert.Same(port, world.Constructs.At((int)MathF.Floor(host.Move.X / 2f), (int)MathF.Floor(host.Move.Z / 2f)));
            Assert.True(host.Move.Z < 52f);

            // A belt ending into the loading face from behind the quay loads the boat.
            var (lx, lz) = SimWorld.PortLoadingTile(port);
            Assert.Same(boat, world.LoadingFaceOn(lx, lz));
            SimDriver.Teleport(host, BuildRules.TileCentre(lx + 2), BuildRules.TileCentre(lz - 1));
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Place(world, host, Db.Building("belt_mk1"), lx, lz - 1, South));
            Assert.True(world.PutOnBelt(world.Belts.SegmentAt(lx, lz - 1)!, TestContent.Mail("letter", world.Map.Destinations[0].Address, _nextMail++, 1)));
            SimDriver.StepTicks(world, 60);
            Assert.Equal(1, MailIn(boat.Cargo));

            // Ridden, it is not loaded; the face beside the loading face is none.
            Assert.Null(world.LoadingFaceOn(lx + 1, lz));
            SimDriver.Teleport(host, BuildRules.TileCentre(lx), 51.4f);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, boat.Id.Value)));
            Assert.Null(world.LoadingFaceOn(lx, lz));
        }
    }
}
