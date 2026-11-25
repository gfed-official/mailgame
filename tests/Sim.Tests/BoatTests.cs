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
    /// The sea path's first part (doc 04 §3.1 and §3.3, doc 10 §1.3, §2 and §3, doc 02 §3.2): water
    /// on the map that nobody walks, the Pier built out over shallow water from the shore, and the
    /// Rowboat — placed from the Sea kit's blueprint or bought — rowed at 3 m/s on the water only,
    /// got out of onto a pier or the shore.
    /// </summary>
    public class BoatTests
    {
        private uint _nextRequest = 1;
        private const int South = 1;

        private static ContentDb Db => TestContent.Db;
        private static VehicleDef Rowboat => Db.Vehicle("rowboat");
        private static BuildingDef Pier => Db.Building("pier");
        private static BuildingDef RowboatBuild => Db.Building("rowboat_build");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InPrep(int shift = 1, string kit = "land")
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false, StartingKits = kit != "land" });
            var host = world.AddPlayer("host")!;
            world.SetRank(host, 10);
            if (kit != "land") Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetKit(0, Db.Kit(kit).Index)));
            if (shift > 1) world.SetPlaytest(host, PlaytestFlags.None, shift);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            world.Economy.AddLoot(3000);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 10)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 10)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("rope"), 4)));
            SimDriver.Teleport(host, BuildRules.TileCentre(3), BuildRules.TileCentre(24));
            SimDriver.StepTicks(world, 12);
            return (world, host);
        }

        private OpResult Place(SimWorld world, Player p, BuildingDef def, int tx, int tz, int rotation) =>
            Request(world, p, SimRequest.PlaceConstruct(0, def.Index, tx, tz, rotation));

        private static void Move(SimWorld world, Player p, float moveX, float moveZ, int ticks, bool jump = false)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.QueueInput(p.Id, new PlayerInput
                {
                    Seq = p.LastQueuedSeq + 1, MoveX = PlayerInput.QuantizeAxis(moveX), MoveZ = PlayerInput.QuantizeAxis(moveZ),
                    Buttons = jump ? InputButtons.Jump : 0,
                });
                world.Step();
            }
        }

        [Fact]
        public void TheContentHasTheRowboatAndThePierOfTheDocs()
        {
            Assert.Equal((3f, 120f, 1, true, false), (Rowboat.WaterSpeed, Rowboat.Hp, Rowboat.Seats, Rowboat.OnWater, Rowboat.Fuelled));
            Assert.Equal((8, 2), (Rowboat.Cargo.Cols, Rowboat.Cargo.Rows));
            Assert.Equal((1, 3, 300, true, true, false), (Pier.Width, Pier.Length, Pier.Hp, Pier.OnWater, Pier.Deck, Pier.Solid));
            Assert.Equal(("log", 6), (Pier.Recipe.Inputs.Single().Item.Id, Pier.Recipe.Inputs.Single().Count));
            Assert.Same(Rowboat, RowboatBuild.Vehicle);
            Assert.Equal(new[] { ("log", 8), ("rope", 2) }, RowboatBuild.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            var bp = Db.ShopItem("bp_rowboat");
            Assert.Same(bp, RowboatBuild.Recipe.Blueprint);
            Assert.True(bp.KitOnly);
            Assert.Contains(bp, Db.Kit("sea").TeamBlueprints);
            Assert.Empty(Db.Kit("land").TeamBlueprints);
            var card = Db.ShopItem("rowboat");
            Assert.Equal((ShopKind.Vehicle, 200, 2), (card.Kind, card.Price, card.FromShift));
            // The test street's harbour basin: three rows of shallow water along the south edge, then deep.
            var map = world().Map;
            Assert.True(map.IsShallowWater(3, 26) && map.IsShallowWater(19, 28) && !map.IsShallowWater(20, 26) && !map.IsShallowWater(3, 29) && map.IsWater(3, 33));
            Assert.Equal((26, 34), (map.LandRows, map.Rows));
            Assert.True(map.IsNavBlocked(10, 27));

            static SimWorld world() => new SimWorld(Db, new SimSettings { Seed = 7 });
        }

        [Fact]
        public void NobodyWalksIntoTheWater()
        {
            var (world, host) = InPrep();
            Move(world, host, 0f, 1f, 90);
            Assert.InRange(host.Move.Z, 50f, 52f);
        }

        [Fact]
        public void APier_GoesOverShallowWater_OutFromTheShore_AndIsWalkedOn()
        {
            var (world, host) = InPrep();
            // Not on land, not out of the deep, not out of nowhere.
            Assert.Equal(OpResult.NotOnWater, Place(world, host, Pier, 3, 23, South));
            Assert.Equal(OpResult.NotOnWater, Place(world, host, Pier, 3, 27, South));
            Assert.Equal(OpResult.NotFromShore, Place(world, host, Pier, 3, 26, 0));
            Assert.Equal(OpResult.NotOnWater, Place(world, host, Db.Building("chest"), 3, 26, 0));
            Assert.Equal(OpResult.Ok, Place(world, host, Pier, 3, 26, South));
            Assert.All(new[] { 26, 27, 28 }, z => Assert.Same(Pier, world.Constructs.At(3, z)?.Def));
            // Walked out to its end and no further.
            Move(world, host, 0f, 1f, 150);
            Assert.InRange(host.Move.Z, 56.5f, 58f);
            Assert.InRange(host.Move.X, 6f, 8f);
        }

        [Fact]
        public void TheSeaKitGivesTheBlueprint_ARowboatIsPlacedOnTheWater_RowedAtThreeMetresASecond_AndStaysOnIt()
        {
            var (bare, nobody) = InPrep();
            Assert.Equal(OpResult.NeedsBlueprint, Place(bare, nobody, RowboatBuild, 5, 27, 0));

            var (world, host) = InPrep(kit: "sea");
            Assert.Contains(Db.ShopItem("bp_rowboat"), world.ShopOwned);
            Assert.Equal(OpResult.NotOnWater, Place(world, host, RowboatBuild, 5, 24, 0));
            Assert.Equal(OpResult.Ok, Place(world, host, RowboatBuild, 5, 26, South));
            var boat = world.Vehicles.Single(v => v.Def == Rowboat);
            Assert.Equal((BuildRules.TileCentre(5), BuildRules.TileCentre(26)), (boat.X, boat.Z));
            Assert.Null(world.Constructs.At(5, 26));
            // One for each carrier.
            Assert.Equal(OpResult.OnePerCarrier, Place(world, host, RowboatBuild, 5, 27, South));

            // From the shore beside it.
            SimDriver.Teleport(host, boat.X, 51.4f);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, boat.Id.Value)));
            Assert.True(host.Move.Riding);
            // East along the basin: 3 m/s once it is under way.
            Move(world, host, 1f, 0f, 60);
            float x0 = host.Move.X;
            Move(world, host, 1f, 0f, 30);
            Assert.InRange(host.Move.X - x0, 2.9f, 3.05f);
            // North it runs into the shore and stays on the water.
            Move(world, host, 0f, -1f, 90);
            Assert.True(world.Map.IsWater((int)MathF.Floor(boat.X / 2f), (int)MathF.Floor((boat.Z - Rowboat.Radius) / 2f)));
            Assert.InRange(boat.Z, 52f, 54f);
        }

        [Fact]
        public void OutOfTheBoat_OntoThePierOrTheShore_NeverIntoTheOpenWater()
        {
            var (world, host) = InPrep(kit: "sea");
            Assert.Equal(OpResult.Ok, Place(world, host, Pier, 3, 26, South));
            var boat = world.AddVehicleAt(Rowboat, BuildRules.TileCentre(10), BuildRules.TileCentre(32), 0f, host.Id);
            SimDriver.Teleport(host, boat.X, boat.Z);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, boat.Id.Value)));
            // Out in the deep: the key does nothing.
            Move(world, host, 0f, 0f, 2, jump: true);
            Assert.True(host.Move.Riding);
            // Rowed up beside the pier's end, out onto its deck.
            Assert.True(PlayerMovement.Landing(world.Map, world.Constructs, BuildRules.TileCentre(4) + 0.2f, BuildRules.TileCentre(28), out float lx, out float lz));
            Assert.Equal(3, (int)MathF.Floor(lx / 2f));
            boat.X = host.Move.X = BuildRules.TileCentre(4) + 0.2f;
            boat.Z = host.Move.Z = BuildRules.TileCentre(28);
            Move(world, host, 0f, 0f, 2, jump: true);
            Assert.False(host.Move.Riding);
            Assert.Same(Pier, world.Constructs.At((int)MathF.Floor(host.Move.X / 2f), (int)MathF.Floor(host.Move.Z / 2f))?.Def);
            Assert.False(boat.Ridden);
            Assert.True(world.Map.IsWater((int)MathF.Floor(boat.X / 2f), (int)MathF.Floor(boat.Z / 2f)));
            // A boat does not go through the pier either.
            Assert.False(PlayerMovement.BoatGoes(world.Map, world.Constructs, 3, 27));
        }

        [Fact]
        public void TheCounterSellsARowboatFromShiftTwo_MooredOnTheWaterNearestIt()
        {
            var (world, host) = InPrep(shift: 2);
            SimDriver.Teleport(host, world.Map.PostOffice.ShopX, world.Map.PostOffice.ShopZ + 1.2f);
            SimDriver.StepTicks(world, 12);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Buy(0, Db.ShopItem("rowboat").Index)));
            var boat = world.Vehicles.Single(v => v.Def == Rowboat);
            int tx = (int)MathF.Floor(boat.X / 2f), tz = (int)MathF.Floor(boat.Z / 2f);
            Assert.True(world.Map.IsWater(tx, tz) && world.Map.IsWater(tx, tz - 1));
            Assert.Equal(27, tz);
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.Buy(0, Db.ShopItem("bp_rowboat").Index)));
        }
        [Fact]
        public void GoneDownInABoatOnOpenWater_TheBagWashesUpOnTheShore_AndTheBoatStaysAfloat()
        {
            var (world, host) = InPrep(kit: "sea");
            var boat = world.AddVehicleAt(Rowboat, BuildRules.TileCentre(10), BuildRules.TileCentre(32), 0f, host.Id);
            SimDriver.Teleport(host, boat.X, boat.Z);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Mount(0, boat.Id.Value)));
            world.DamagePlayer(host, 1000f);
            Assert.True(host.Dead);
            var bag = world.Bags.Single();
            Assert.True(PlayerMovement.Walkable(world.Map, world.Constructs, (int)MathF.Floor(bag.X / 2f), (int)MathF.Floor(bag.Z / 2f)));
            Move(world, host, 0f, 0f, 30);
            Assert.False(host.Move.Riding);
            Assert.False(boat.Ridden);
            // The body is not left thrashing in the water: it lies ashore with the bag.
            Assert.True(PlayerMovement.Walkable(world.Map, world.Constructs, (int)MathF.Floor(host.Move.X / 2f), (int)MathF.Floor(host.Move.Z / 2f)));
            Assert.True(world.Map.IsWater((int)MathF.Floor(boat.X / 2f), (int)MathF.Floor(boat.Z / 2f)));
        }
    }
}
