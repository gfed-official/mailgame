using System.Linq;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.Run;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>Playtest nit 9: a chest stands in the map, so the chest grid can be used in a live game.</summary>
    public class MapChestTests
    {
        private static WorldMap Map => TestContent.Db.Map("test_street");

        private static OpResult Request(SimWorld world, Player p, SimRequest request)
        {
            world.Events.Clear();
            world.Submit(p.Id, request);
            world.Step();
            return world.Events.OfType<RequestResultEvent>().Single(e => e.RequestId == request.RequestId).Result;
        }

        [Fact]
        public void TheTestStreetHasAChestInThePostOfficeYard()
        {
            var chest = Assert.Single(Map.Chests);
            Assert.Equal("chest", chest.Container.Id);
            int tx = (int)(chest.X / SimConstants.TileSize), tz = (int)(chest.Z / SimConstants.TileSize);
            Assert.Equal(TileType.Yard, Map.GetTile(tx, tz));

            // It is in the way like any other thing that stands there, and it is the last prop: the scenery was grown without it.
            var prop = Map.Props[Map.Props.Count - 1];
            Assert.Equal(PropKind.Chest, prop.Kind);
            Assert.True(prop.Solid);
            Assert.Equal(chest.X, prop.X);
            Assert.Equal(chest.Z, prop.Z);
            Assert.Equal(1, Map.Props.Count(p => p.Kind == PropKind.Chest));
            Assert.NotEqual(Map.SceneryKey, Map.MapHash);

            // Not on the way from the pad to the Intake or to the street.
            var po = Map.PostOffice;
            Assert.True(prop.Distance(po.IntakeX, po.IntakeZ, out _, out _) > 3f);
            Assert.True(prop.Distance(po.SpawnPadX, po.SpawnPadZ, out _, out _) > 3f);
        }

        [Fact]
        public void TheWorldStartsWithTheChestAndACarrierCanUseIt()
        {
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = 7 });
            var container = Assert.Single(world.MapChests);
            Assert.Same(world.GetContainer(container.Id), container);
            Assert.Equal(0, container.EntryCount);
            Assert.NotEqual(world.Intake.Id, container.Id);

            var p = world.AddPlayer("a")!;
            var chest = Map.Chests[0];
            // From the pad it is out of reach; beside it, it opens.
            Assert.NotEqual(OpResult.Ok, Request(world, p, SimRequest.Open(1, container.Id)));
            Assert.True(SimDriver.WalkTo(world, p, chest.X + 1.2f, chest.Z, 0.4f));
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Open(2, container.Id)));

            Assert.True(world.Give(p.Inventory, ItemStack.OfItem(world.Content.Item("stone"), 3)));
            uint stone = p.Inventory.Entries.Single().EntryId;
            Assert.Equal(OpResult.Ok, Request(world, p, SimRequest.Move(3, p.Inventory.Id, stone, container.Id, 0, 0)));
            Assert.Equal(1, container.EntryCount);
            Assert.Equal(0, p.Inventory.EntryCount);
        }

        [Fact]
        public void TheChestCannotBeWalkedThrough()
        {
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = 7 });
            var p = world.AddPlayer("a")!;
            var chest = Map.Chests[0];
            SimDriver.Teleport(p, chest.X + 2.5f, chest.Z);
            SimDriver.WalkTo(world, p, chest.X, chest.Z, 0.1f);
            float dx = p.Move.X - chest.X, dz = p.Move.Z - chest.Z;
            Assert.True(dx * dx + dz * dz >= MapChestDef.Radius * MapChestDef.Radius, $"the carrier stands inside the chest ({dx}, {dz})");
        }

        [Fact]
        public void ANewRunStartsWithAnEmptyChest()
        {
            var world = new SimWorld(TestContent.Db, new SimSettings { Seed = 7, QuickStart = true });
            world.AddPlayer("a");
            world.Step();
            var container = world.MapChests[0];
            Assert.True(world.Give(container, ItemStack.OfItem(world.Content.Item("stone"), 3)));

            // The only carrier leaves; after the grace period the run is over and the Lobby is clean.
            world.RemovePlayer(world.Players[0].Id);
            for (int i = 0; i < 30 * 600 && world.Run.Phase != RunPhase.Lobby; i++) world.Step();
            Assert.Equal(RunPhase.Lobby, world.Run.Phase);
            Assert.Equal(0, container.EntryCount);
        }
    }
}
