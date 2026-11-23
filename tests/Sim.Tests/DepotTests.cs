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
    /// The Depot (doc 04 §2.6, doc 10 §2.2): team storage of 16 × 10 on four tiles; belts feed it
    /// directly on any side, inserters pull from it; a chest is fed by an inserter only.
    /// </summary>
    public class DepotTests
    {
        private const int Tps = 30;

        private static ContentDb Db => TestContent.Db;
        private static BuildingDef Depot => Db.Building("depot");
        private static BuildingDef Belt => Db.Building("belt_mk1");

        private uint _nextRequest = 1;
        private uint _nextMail = 960500;

        private OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            request.RequestId = _nextRequest++;
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Last(e => e.RequestId == request.RequestId).Result;
        }

        private (SimWorld World, Player Host) InPrep()
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var host = world.AddPlayer("host")!;
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.StartRun(0)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("iron_ingot"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("plank"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("stone"), 20)));
            Assert.True(world.Give(host.Inventory, ItemStack.OfItem(Db.Item("log"), 8)));
            SimDriver.Teleport(host, BuildRules.TileCentre(15), BuildRules.TileCentre(23));
            return (world, host);
        }

        private OpResult Place(SimWorld world, Player p, BuildingDef def, int tx, int tz, int rotation) =>
            Request(world, p, SimRequest.PlaceConstruct(0, def.Index, tx, tz, rotation));

        private static int MailIn(GridContainer c) => c.Entries.Where(e => e.Stack.Kind == StackKind.Mail).Sum(e => e.Stack.Mail.Count);

        [Fact]
        public void TheDepot_IsContent_FourTiles_OnTheChestsKey()
        {
            Assert.Equal((BuildingBehaviour.Container, 2, 2, 800), (Depot.Behaviour, Depot.Width, Depot.Length, Depot.Hp));
            Assert.Equal(("depot", 16, 10, "any_side"), (Depot.Container!.Id, Depot.Container.Cols, Depot.Container.Rows, Depot.Container.BeltAccess));
            Assert.Equal(new[] { ("stone", 8), ("iron_ingot", 4) }, Depot.Recipe.Inputs.Select(i => (i.Item.Id, i.Count)));
            Assert.Null(Depot.Recipe.Blueprint);
            Assert.Equal(Db.Building("chest").Recipe.Key, Depot.Recipe.Key);
        }

        [Fact]
        public void BeltsFeedTheDepotOnAnySide_AnInserterPullsFromIt_AndItIsOpenedFromBesideIt()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Place(world, host, Depot, 16, 21, 0));
            var built = world.Constructs.At(17, 22)!;
            Assert.Same(built, world.Constructs.At(16, 21));
            var store = world.GetContainer(built.ContainerId)!;
            // From the west into its first tile, and from the south into its far one.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceLine(0, Belt.Index, 13, 21, 15, 21, 0)));
            Assert.Equal(OpResult.Ok, Place(world, host, Belt, 17, 23, 3));
            world.Step();
            var west = world.Belts.SegmentAt(13, 21)!;
            var south = world.Belts.SegmentAt(17, 23)!;
            Assert.Equal((BeltEndKind.Container, built.ContainerId), (west.End, west.ContainerId));
            Assert.Equal(BeltEndKind.Container, south.End);
            var dest = world.Map.Destinations[0];
            for (int i = 0; i < 3; i++)
            {
                Assert.True(world.PutOnBelt(west, TestContent.Mail("letter", dest.Address, _nextMail++, 1)));
                Assert.True(world.PutOnBelt(south, TestContent.Mail("postcard", dest.Address, _nextMail++, 1)));
                SimDriver.StepTicks(world, 10);
            }
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.Equal(6, MailIn(store));
            Assert.Equal(0, world.Belts.ItemCount);

            // Opened from beside it, its middle further than a chest's would be.
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.Open(0, built.ContainerId)));
            // Not taken down with mail in it.
            Assert.Equal(OpResult.NotEmpty, Request(world, host, SimRequest.RemoveConstruct(0, 17, 22)));

            // An inserter on its east side pulls onto a belt going east.
            SimDriver.Teleport(host, BuildRules.TileCentre(19), BuildRules.TileCentre(23));
            SimDriver.StepTicks(world, 2);
            Assert.Equal(OpResult.Ok, Place(world, host, Db.Building("inserter"), 18, 21, 0));
            Assert.Equal(OpResult.Ok, Place(world, host, Belt, 19, 21, 0));
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.InRange(MailIn(store), 3, 4);
            Assert.Equal(6, MailIn(store) + world.Belts.ItemCount);
        }

        [Fact]
        public void AChest_IsNotFedByABelt_TheLineHoldsAtIt()
        {
            var (world, host) = InPrep();
            Assert.Equal(OpResult.Ok, Place(world, host, Db.Building("chest"), 16, 21, 0));
            Assert.Equal(OpResult.Ok, Request(world, host, SimRequest.PlaceLine(0, Belt.Index, 13, 21, 15, 21, 0)));
            world.Step();
            var west = world.Belts.SegmentAt(13, 21)!;
            Assert.Equal(BeltEndKind.None, west.End);
            Assert.True(world.PutOnBelt(west, TestContent.Mail("letter", world.Map.Destinations[0].Address, _nextMail++, 1)));
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.Equal(1, world.Belts.ItemCount);
        }
    }
}
