using System;
using System.Linq;
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
    /// The bike (doc 04 §3.1, doc 10 §1.3 and §3): $1.20 at the counter, one for each carrier,
    /// parked by the counter; 8 m/s on the road, 5 off it, a 2x8 cargo rack only the rider reaches;
    /// the jump key gets off, and so does going down or losing the connection.
    /// </summary>
    public class VehicleTests
    {
        private const int Tps = 30;
        /// <summary>The middle of Larch Lane, east of the Post Office.</summary>
        private const float StreetZ = 28f;

        private uint _nextRequest = 1;

        private static ContentDb Db => TestContent.Db;
        private static VehicleDef Bike => Db.Vehicle("bike");
        private static ShopItemDef BikeCard => Db.ShopItem("bike");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InPrep(int players = 1)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            for (int i = 1; i < players; i++) world.AddPlayer($"p{i}");
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.Equal(RunPhase.Prep, world.Run.Phase);
            world.Economy.AddLoot(1000);
            return (world, host);
        }

        private static void Ride(SimWorld world, Player p, float moveX, float moveZ, int ticks, InputButtons buttons = InputButtons.None)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.QueueInput(p.Id, new PlayerInput
                {
                    Seq = p.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(moveX), MoveZ = PlayerInput.QuantizeAxis(moveZ), Buttons = buttons,
                });
                world.Step();
            }
        }

        private Vehicle BuyAndMount(SimWorld world, Player p)
        {
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Buy(0, BikeCard.Index)));
            var bike = world.Vehicles.Last();
            SimDriver.Teleport(p, bike.X + 1f, bike.Z);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Mount(0, bike.Id.Value)));
            return bike;
        }

        [Fact]
        public void TheContentHasTheBikeOfTheDocs()
        {
            Assert.Equal((8f, 5f, 150f, 1), (Bike.RoadSpeed, Bike.OffRoadSpeed, Bike.Hp, Bike.Seats));
            Assert.Equal((8, 2), (Bike.Cargo.Cols, Bike.Cargo.Rows));
            Assert.Equal((ShopKind.Vehicle, 120, 1), (BikeCard.Kind, BikeCard.Price, BikeCard.FromShift));
            Assert.Same(Bike, BikeCard.Vehicle);
            Assert.False(BikeCard.OncePerRun);
        }

        [Fact]
        public void ABoughtBike_StandsByTheCounter_OneForEachCarrier()
        {
            var (world, host) = InPrep(players: 2);
            SimDriver.Teleport(host, world.Map.PostOffice.ShopX + 1f, world.Map.PostOffice.ShopZ);
            SimDriver.StepTicks(world, 12);
            int wallet = world.Economy.Wallet;

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, BikeCard.Index)));
            var added = world.Events.OfType<VehicleAddedEvent>().Single();
            var bike = world.Vehicles.Single();
            Assert.Equal((bike.Id, Bike.Index, host.Id), (added.Vehicle, added.Def, added.Buyer));
            Assert.Equal(wallet - 120, world.Economy.Wallet);
            Assert.False(bike.Ridden);
            // Nothing lands in the pockets; it stands on open ground near the counter, on a tile of its own.
            Assert.Empty(host.Inventory.Entries);
            float dx = bike.X - world.Map.PostOffice.ShopX, dz = bike.Z - world.Map.PostOffice.ShopZ;
            Assert.InRange(MathF.Sqrt(dx * dx + dz * dz), 1.5f, 8f);
            int tx = (int)MathF.Floor(bike.X / SimConstants.TileSize), tz = (int)MathF.Floor(bike.Z / SimConstants.TileSize);
            Assert.False(world.Map.IsNavBlocked(tx, tz));

            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, BikeCard.Index)));
            Assert.Equal(2, world.Vehicles.Count);
            Assert.NotEqual((world.Vehicles[0].X, world.Vehicles[0].Z), (world.Vehicles[1].X, world.Vehicles[1].Z));
            // Two carriers, two bikes: the third is not sold, and costs nothing.
            wallet = world.Economy.Wallet;
            Assert.Equal(OpResult.OnePerCarrier, Request(world, host, SimRequest.Buy(0, BikeCard.Index)));
            Assert.Equal(wallet, world.Economy.Wallet);
            Assert.Equal(OpResult.OnePerCarrier, ShopRules.VehicleCheck(BikeCard, 2, 2));
            Assert.Equal(OpResult.Ok, ShopRules.VehicleCheck(Db.ShopItem("axe"), 9, 1));
        }

        [Fact]
        public void GettingOn_WantsTheBikeInReach_AndNobodyOnIt()
        {
            var (world, host) = InPrep(players: 2);
            var other = world.Players[1];
            SimDriver.Teleport(host, world.Map.PostOffice.ShopX + 1f, world.Map.PostOffice.ShopZ);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, BikeCard.Index)));
            var bike = world.Vehicles.Single();

            SimDriver.Teleport(host, bike.X + 6f, bike.Z);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.OutOfRange, Request(world, host, SimRequest.Mount(0, bike.Id.Value)));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Mount(0, 12345)));

            SimDriver.Teleport(host, bike.X + 1.5f, bike.Z);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, bike.Id.Value)));
            Assert.Equal(host.Id, world.Events.OfType<VehicleMountedEvent>().Single().Player);
            // In the saddle, where the bike stood.
            Assert.Equal((bike.X, bike.Z), (host.Move.X, host.Move.Z));
            Assert.Equal(Bike.Index + 1, host.Move.Ride);
            Assert.Same(bike, world.RiddenBy(host.Id));

            SimDriver.Teleport(other, bike.X - 1f, bike.Z);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ridden, Request(world, other, SimRequest.Mount(0, bike.Id.Value)));
            Assert.Equal(OpResult.AlreadyRiding, Request(world, host, SimRequest.Mount(0, bike.Id.Value)));
        }

        [Fact]
        public void OnTheStreetItRuns8_OffIt5_AndBothBeatASprint()
        {
            var (world, host) = InPrep();
            BuyAndMount(world, host);
            SimDriver.Teleport(host, 30f, StreetZ);
            Ride(world, host, 1f, 0f, 3 * Tps);
            Assert.True(PlayerMovement.OnRoad(world.Map, host.Move.X, host.Move.Z));
            Assert.Equal(8f, host.Move.RideSpeed, 3);
            float x0 = host.Move.X;
            Ride(world, host, 1f, 0f, Tps);
            Assert.Equal(8f, host.Move.X - x0, 1);
            Assert.True(8f > world.MoveConstants.SprintSpeed);

            // On the grass north of the street (row 12, between the houses' lots) it slows to 5.
            SimDriver.Teleport(host, 30f, 23f);
            Assert.False(PlayerMovement.OnRoad(world.Map, host.Move.X, host.Move.Z));
            Ride(world, host, 1f, 0f, 2 * Tps);
            Assert.Equal(5f, host.Move.RideSpeed, 3);

            // Let go, it rolls out; it does not stop dead.
            Ride(world, host, 0f, 0f, 1);
            Assert.InRange(host.Move.RideSpeed, 4.8f, 5f);
            Ride(world, host, 0f, 0f, 2 * Tps);
            Assert.Equal(0f, host.Move.RideSpeed);
        }

        [Fact]
        public void ItTurnsAtItsTurnRate_AndBrakesWhenSteeredBack()
        {
            var (world, host) = InPrep();
            BuyAndMount(world, host);
            SimDriver.Teleport(host, 30f, StreetZ);
            Ride(world, host, 1f, 0f, 2 * Tps);
            Assert.Equal((1f, 0f), (host.Move.HeadX, host.Move.HeadZ));

            // Steered square to the side, one tick turns it by turnRate / 30 and no more.
            Ride(world, host, 0f, 1f, 1);
            float turned = MathF.Atan2(host.Move.HeadZ, host.Move.HeadX);
            Assert.Equal(Bike.TurnRate / Tps, turned, 3);

            // Steered back against the way it rolls, it brakes instead of turning on the spot.
            Ride(world, host, 1f, 0f, Tps);
            float speed = host.Move.RideSpeed;
            Ride(world, host, -1f, 0f, 3);
            Assert.True(host.Move.RideSpeed < speed - 0.8f);
            Assert.True(host.Move.HeadX > 0.9f);
            // Stopped, it turns round where it stands.
            Ride(world, host, -1f, 0f, Tps);
            Assert.True(host.Move.HeadX < -0.9f);
        }

        [Fact]
        public void RunningIntoAHouse_TakesTheSpeed()
        {
            var (world, host) = InPrep();
            BuyAndMount(world, host);
            // House 1's building is tiles x 10..12, z 8..9: ride north at it from the street.
            SimDriver.Teleport(host, 23f, StreetZ);
            Ride(world, host, 0f, -1f, 3 * Tps);
            Assert.True(host.Move.Z > 19.9f && host.Move.Z < 20.6f, $"z {host.Move.Z}");
            Assert.True(host.Move.RideSpeed < 0.5f, $"speed {host.Move.RideSpeed}");
        }

        [Fact]
        public void TheJumpKeyGetsOff_AndTheBikeStandsWhereItWas()
        {
            var (world, host) = InPrep(players: 2);
            var other = world.Players[1];
            var bike = BuyAndMount(world, host);
            SimDriver.Teleport(host, 30f, StreetZ);
            Ride(world, host, 1f, 0f, Tps);
            float x = host.Move.X;
            Assert.Equal(x, bike.X);

            world.Events.Clear();
            Ride(world, host, 1f, 0f, 1, InputButtons.Jump);
            Assert.False(host.Move.Riding);
            Assert.Null(world.RiddenBy(host.Id));
            var parked = world.Events.OfType<VehicleParkedEvent>().Single();
            Assert.Equal((bike.Id, host.Id), (parked.Vehicle, parked.Player));
            // Off, they stand where the bike was, a walking step on, not in the air: the jump was the getting off.
            Assert.Equal(x, bike.X);
            Assert.True(host.Move.Grounded);
            Assert.InRange(host.Move.X - x, 0f, 0.2f);
            Ride(world, host, 1f, 0f, Tps);
            Assert.Equal(x, bike.X);

            SimDriver.Teleport(other, bike.X, bike.Z + 1f);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, other, SimRequest.Mount(0, bike.Id.Value)));
        }

        [Fact]
        public void TheCargo_IsTheRidersToOpen_AndRidesAlong()
        {
            var (world, host) = InPrep(players: 2);
            var other = world.Players[1];
            var bike = BuyAndMount(world, host);
            SimDriver.Teleport(other, bike.X + 1f, bike.Z);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.OutOfRange, Request(world, other, SimRequest.Open(0, bike.Cargo.Id)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Open(0, bike.Cargo.Id)));

            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 6)));
            var logs = host.Inventory.Entries.Single();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.QuickMove(0, host.Inventory.Id, logs.EntryId)));
            Assert.Single(bike.Cargo.Entries);
            Assert.Empty(host.Inventory.Entries);

            // Carried along: open from the saddle, far from where it was bought.
            SimDriver.Teleport(host, 60f, StreetZ);
            Ride(world, host, 1f, 0f, Tps);
            Assert.Equal(host.Id, world.Players[0].Id);
            Assert.Equal(host.Move.X, bike.X);
            Assert.Equal(bike.Cargo.Id, host.OpenContainer);

            // Off, the rack is shut to them as well.
            world.Events.Clear();
            Ride(world, host, 0f, 0f, 1, InputButtons.Jump);
            Ride(world, host, 0f, 0f, 1);
            Assert.Equal(0u, host.OpenContainer);
            Assert.Equal(OpResult.OutOfRange, Request(world, host, SimRequest.Open(0, bike.Cargo.Id)));
            Assert.Single(bike.Cargo.Entries);
        }

        [Fact]
        public void GoingDown_OrLosingTheConnection_LeavesTheBikeParked()
        {
            var (world, host) = InPrep(players: 2);
            var other = world.Players[1];
            var bike = BuyAndMount(world, host);
            SimDriver.Teleport(host, 30f, StreetZ);
            Ride(world, host, 1f, 0f, 10);
            world.DamagePlayer(host, 1000f);
            world.Step();
            Assert.False(host.Move.Riding);
            Assert.False(bike.Ridden);
            Assert.Equal(30f, bike.X, 0);

            SimDriver.Teleport(other, bike.X + 1f, bike.Z);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, other, SimRequest.Mount(0, bike.Id.Value)));
            Assert.True(world.Disconnect(other.Id));
            world.Step();
            Assert.False(other.Move.Riding);
            Assert.False(bike.Ridden);
        }

        [Fact]
        public void TheRunsEnd_TakesTheBikesWithIt()
        {
            var (world, host) = InPrep();
            var bike = BuyAndMount(world, host);
            Assert.True(host.Move.Riding);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            for (int i = 0; i < 2000 * Tps && world.Run.Phase != RunPhase.Results; i++) world.Step();
            Assert.Equal(RunPhase.Results, world.Run.Phase);
            Assert.Single(world.Vehicles);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Empty(world.Vehicles);
            Assert.False(host.Move.Riding);
            Assert.Single(world.Events.OfType<VehiclesClearedEvent>());
            Assert.Null(world.GetContainer(bike.Cargo.Id));
        }

        [Fact]
        public void TheRidersStepIsTheSameOnBothEnds()
        {
            // The client predicts with the very step the server runs: same state and inputs, same result.
            var c = MoveConstants.From(Db);
            var map = Db.Map("test_street");
            var a = new PlayerMoveState { X = 30f, Z = StreetZ, Grounded = true, Ride = (byte)(Bike.Index + 1), HeadX = 1f };
            var b = a;
            for (int i = 0; i < 200; i++)
            {
                var input = new PlayerInput { Seq = (uint)i, MoveX = (sbyte)(i % 60 < 30 ? 127 : 40), MoveZ = (sbyte)(i % 50 < 25 ? 60 : -60) };
                PlayerMovement.Step(ref a, input, map, 1f, c);
                PlayerMovement.Step(ref b, input, map, 1f, c);
            }
            Assert.Equal((a.X, a.Z, a.RideSpeed, a.HeadX), (b.X, b.Z, b.RideSpeed, b.HeadX));
            Assert.True(a.Riding);
        }
    
        [Theory]
        [InlineData(1u)]
        [InlineData(4u)]
        [InlineData(9u)]
        public void OnTheGeneratedTown_TheBikeStandsByTheCounter_AndRunsTheMainStreetAt8(uint seed)
        {
            // The bike and the counter were made on the test street; the seed's town has its own Post Office.
            var world = new SimWorld(Db, new SimSettings { Seed = seed, MapId = WorldGenerator.SmallIsland, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(1000);
            var po = world.Map.PostOffice;
            SimDriver.Teleport(host, po.ShopX + 1f, po.ShopZ);
            SimDriver.StepTicks(world, 12);
            var bike = BuyAndMount(world, host);
            int tx = (int)MathF.Floor(bike.X / SimConstants.TileSize), tz = (int)MathF.Floor(bike.Z / SimConstants.TileSize);
            Assert.False(world.Map.IsNavBlocked(tx, tz));

            // The main street is the first street; ridden east along its middle it is road all the way.
            var main = world.Map.Streets[0].Rect;
            float z = (main.Y + 1) * SimConstants.TileSize;
            SimDriver.Teleport(host, 6f, z);
            Ride(world, host, 1f, 0f, 3 * Tps);
            Assert.True(PlayerMovement.OnRoad(world.Map, host.Move.X, host.Move.Z));
            Assert.Equal(8f, host.Move.RideSpeed, 3);
            Assert.Equal(z, host.Move.Z, 1);
        }

        [Fact]
        public void BikeCourier_IsOfferedOnceABikeIsThere_AndItsHolderRidesFortyPercentFaster()
        {
            var (world, host) = InPrep();
            var perk = Db.Perk("bike_courier");
            Assert.False(world.PrerequisitesMet(perk));
            BuyAndMount(world, host);
            Assert.True(world.PrerequisitesMet(perk));
            Assert.Equal(OpResult.Ok, world.GrantPerk(host, perk));
            SimDriver.Teleport(host, 30f, StreetZ);
            Ride(world, host, 1f, 0f, 3 * Tps);
            Assert.Equal(8f * 1.4f, host.Move.RideSpeed, 2);
        }
}
}
