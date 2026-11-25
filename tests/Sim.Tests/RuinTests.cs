using System.Linq;
using PerformativeMail.Sim.Automation;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// Ruins (doc 05 §5 "At 0 HP a construct becomes a Ruin: a non-functional ghost occupying its
    /// tiles … A Ruin can be rebuilt during Prep for 50% of its material cost, restoring its
    /// configuration … During Delivery a ruin must be deconstructed (free) and rebuilt at full
    /// cost, or left in place"; design choice 126).
    /// </summary>
    public class RuinTests
    {
        private const int Tx = 22, Tz = 15;

        private uint _nextRequest = 1;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef StoneWall => Db.Building("wall_stone");
        private static BuildingDef Sorter => Db.Building("address_sorter_mk1");

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private static int Stone(Player p) => BuildRules.Have(p.Hotbar, p.Inventory, Db.Item("stone"));

        /// <summary>A Stone Wall built in Prep of shift 2, its builder beside it with the stone for another.</summary>
        private (SimWorld World, Player Host, Construct Wall) WallInPrep()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            world.SetPlaytest(host, PlaytestFlags.None, 2);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.True(world.Grant(Db.ShopItem("bp_defense"), host.Id));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 8)));
            SimDriver.Teleport(host, BuildRules.TileCentre(Tx), BuildRules.TileCentre(Tz) + 2.6f);
            SimDriver.StepTicks(world, Player.HistoryLength);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, StoneWall.Index, Tx, Tz, 1)));
            return (world, host, world.Constructs.At(Tx, Tz)!);
        }

        [Fact]
        public void TheContentRebuildsARuinForHalf()
        {
            Assert.All(Db.Buildings, b => Assert.Equal(0.5f, b.RuinRebuildRatio));
            Assert.Equal(2, BuildRules.RuinCost(4, 0.5f));
            Assert.Equal(1, BuildRules.RuinCost(1, 0.5f));
            Assert.Equal(2, BuildRules.RuinCost(3, 0.5f));
            Assert.Equal(0, BuildRules.RuinCost(3, 0f));
        }

        [Fact]
        public void AtZeroHp_AWallLeavesARuin_ThatStopsNobody_AndHoldsItsTile()
        {
            var (world, host, wall) = WallInPrep();
            world.Events.Clear();
            world.DamageConstruct(wall, StoneWall.Hp);
            Assert.Null(world.Constructs.At(Tx, Tz));
            var ruin = world.Constructs.RuinAt(Tx, Tz);
            Assert.NotNull(ruin);
            Assert.Same(StoneWall, ruin!.Def);
            Assert.Equal((1, 0), (ruin.Rotation, ruin.Hp));
            Assert.Equal(1, world.RuinsLeft);
            var left = world.Events.OfType<RuinEvent>().Single();
            Assert.Equal(((byte)Tx, (byte)Tz, StoneWall.Index, (byte)1, false), (left.X, left.Y, left.Building, left.Rotation, left.Gone));
            Assert.True(world.Events.OfType<ConstructRemovedEvent>().Single().Destroyed);

            // Nothing blocks on its tile, and nothing else is built there.
            Assert.False(world.Constructs.Blocks(Tx, Tz));
            Assert.Equal(OpResult.Blocked, Request(world, host, SimRequest.PlaceConstruct(0, StoneWall.Index, Tx, Tz, 0)));
            Assert.Equal(OpResult.Blocked, BuildRules.CheckPlacement(world.Map, world.Constructs, Db.Building("chest"), Tx, Tz, 0));
        }

        [Fact]
        public void InPrep_ARuinIsRebuiltForHalfItsCost_AsItWasTurned()
        {
            var (world, host, wall) = WallInPrep();
            world.DamageConstruct(wall, StoneWall.Hp);
            int before = Stone(host);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RebuildRuin(0, Tx, Tz, true)));
            Assert.Equal(before - 2, Stone(host));
            var back = world.Constructs.At(Tx, Tz)!;
            Assert.Same(StoneWall, back.Def);
            Assert.Equal((1, StoneWall.Hp), (back.Rotation, back.Hp));
            Assert.Null(world.Constructs.RuinAt(Tx, Tz));
            Assert.Empty(world.Constructs.Ruins);
            Assert.Contains(world.Events.OfType<RuinEvent>(), e => e.Gone && e.Rebuilt && e.Player == host.Id);
            Assert.Contains(world.Events.OfType<ConstructPlacedEvent>(), e => e.X == Tx && e.Y == Tz && e.Player == host.Id);
        }

        [Fact]
        public void WithoutHalfTheStone_OrFromAfar_TheRuinStays()
        {
            var (world, host, wall) = WallInPrep();
            world.DamageConstruct(wall, StoneWall.Hp);
            foreach (var box in new[] { host.Hotbar, host.Inventory })
                foreach (uint id in box.Entries.Select(e => e.EntryId).ToList()) box.Remove(id);
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 1)));
            Assert.Equal(OpResult.MissingMaterials, Request(world, host, SimRequest.RebuildRuin(0, Tx, Tz, true)));
            Assert.NotNull(world.Constructs.RuinAt(Tx, Tz));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 1)));
            SimDriver.Teleport(host, BuildRules.TileCentre(Tx) + 14f, BuildRules.TileCentre(Tz) + 2.6f);
            SimDriver.StepTicks(world, Player.HistoryLength);
            Assert.Equal(OpResult.OutOfRange, Request(world, host, SimRequest.RebuildRuin(0, Tx, Tz, true)));
            Assert.Equal(OpResult.BadRequest, Request(world, host, SimRequest.RebuildRuin(0, Tx + 1, Tz, true)));
            Assert.NotNull(world.Constructs.RuinAt(Tx, Tz));
            Assert.Equal(2, Stone(host));
        }

        [Fact]
        public void InDelivery_ARuinIsNotRebuilt_ButClearedForNothing_AndTheTileIsBuiltOnAtFullCost()
        {
            var (world, host, wall) = WallInPrep();
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(RunPhase.Delivery, world.Run.Phase);
            world.DamageConstruct(wall, StoneWall.Hp);
            int before = Stone(host);
            Assert.Equal(OpResult.WrongPhase, Request(world, host, SimRequest.RebuildRuin(0, Tx, Tz, true)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RebuildRuin(0, Tx, Tz, false)));
            Assert.Null(world.Constructs.RuinAt(Tx, Tz));
            Assert.Equal(before, Stone(host));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, StoneWall.Index, Tx, Tz, 0)));
            Assert.Equal(before - 4, Stone(host));
        }

        [Fact]
        public void TakingDownARuin_InBuildMode_ClearsIt()
        {
            var (world, host, wall) = WallInPrep();
            world.DamageConstruct(wall, StoneWall.Hp);
            int before = Stone(host);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RemoveConstruct(0, Tx, Tz)));
            Assert.Null(world.Constructs.RuinAt(Tx, Tz));
            Assert.Equal(before, Stone(host));
        }

        [Fact]
        public void ASorterIsRebuiltWithItsFilters()
        {
            var (world, host, _) = WallInPrep();
            Assert.True(world.Grant(Db.ShopItem("bp_sorting"), host.Id));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 6)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 6)));
            SimDriver.Teleport(host, BuildRules.TileCentre(15), BuildRules.TileCentre(23));
            SimDriver.StepTicks(world, Player.HistoryLength);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Sorter.Index, 16, 21, 0)));
            var sorter = world.Constructs.At(16, 21)!;
            var filters = new[] { new MailFilter(FilterKind.Street, 4), new MailFilter(FilterKind.District, 2), MailFilter.Empty };
            world.Constructs.SetFilters(sorter, filters);

            world.DamageConstruct(sorter, Sorter.Hp);
            Assert.All(Enumerable.Range(0, Sorter.TileCount).Select(sorter.TileAt), t => Assert.Same(sorter, world.Constructs.RuinAt(t.Item1, t.Item2)));
            int iron = BuildRules.Have(host.Hotbar, host.Inventory, Db.Item("iron_ingot"));
            // From any of its tiles.
            var (ox, oz) = sorter.TileAt(3);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.RebuildRuin(0, ox, oz, true)));
            var back = world.Constructs.At(16, 21)!;
            Assert.Equal(filters, back.Filters);
            Assert.Equal(iron - 2, BuildRules.Have(host.Hotbar, host.Inventory, Db.Item("iron_ingot")));
            Assert.Contains(world.Events.OfType<ConstructConfiguredEvent>(), e => e.X == 16 && e.Y == 21);
        }

        [Fact]
        public void AChestBrokenSpillsWhatItHeld_AndLeavesARuin()
        {
            var (world, host, _) = WallInPrep();
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 4)));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceConstruct(0, Db.Building("chest").Index, Tx + 1, Tz, 0)));
            var chest = world.Constructs.At(Tx + 1, Tz)!;
            Assert.True(world.Give(world.GetContainer(chest.ContainerId)!, ItemStack.OfItem(Db.Item("stone"), 3)));
            int bags = world.Bags.Count;
            world.DamageConstruct(chest, Db.Building("chest").Hp);
            Assert.Equal(bags + 1, world.Bags.Count);
            Assert.NotNull(world.Constructs.RuinAt(Tx + 1, Tz));
            Assert.Null(world.GetContainer(chest.ContainerId));
        }

        [Fact]
        public void TheRunsEnd_ClearsTheRuins()
        {
            var (world, host, wall) = WallInPrep();
            world.DamageConstruct(wall, StoneWall.Hp);
            Assert.Single(world.Constructs.Ruins);
            // Shift 2's quota is missed with nothing delivered: the run is over, and back in the Lobby nothing stands.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.SetReady(0, true)));
            for (int i = 0; i < 600 * SimConstants.TickRate && world.Run.Phase != RunPhase.Results; i++) world.Step();
            Assert.Equal(RunPhase.Results, world.Run.Phase);
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.ContinueRun(0)));
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Empty(world.Constructs.Ruins);
            Assert.Null(world.Constructs.RuinAt(Tx, Tz));
        }
    }
}
