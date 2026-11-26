using System;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Inventory;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Hog Rider (doc 05 §2 "120 HP, 20 dmg, 1.0 s, 7 m/s; Depot > Vehicle Depot > chest >
    /// player; Jumps over walls (1 m). Steals: each hit on a container pulls 1 random item out and
    /// drops it as a WorldItem 3 m away"; §2.2 "returns 1 stolen item (100%)"; design choice 132).
    /// </summary>
    public class HogRiderTests
    {
        private const int Tps = SimConstants.TickRate;
        private static ContentDb Db => TestContent.Db;
        private static EnemyDef Hog => Db.Enemy("hog_rider");

        private static (SimWorld World, Player Carrier) OnTheStreet(float x = 10f, float z = 29f)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var p = world.AddPlayer("carrier")!;
            p.MaxHp = p.Hp = 10000f;
            SimDriver.Teleport(p, x, z);
            return (world, p);
        }

        private static Construct Build(SimWorld world, string id, float x, float z = 29f)
        {
            var def = Db.Building(id);
            uint container = def.Behaviour == BuildingBehaviour.Container ? world.AddWorldContainer(def.Container!.Id, x, z).Id : 0;
            return world.Constructs.Add(def, (int)MathF.Floor(x / SimConstants.TileSize), (int)MathF.Floor(z / SimConstants.TileSize), 1, def.Hp, container);
        }

        private static int Units(GridContainer c) => c.Entries.Sum(e => e.Stack.Count);

        [Fact]
        public void TheContentHasTheHogRiderOfTheDocs()
        {
            Assert.Equal((120f, 20f, 1f, 7f, 25, 3), (Hog.Hp, Hog.Damage, Hog.AttackRate, Hog.Speed, Hog.Cost, Hog.UnlockShift));
            Assert.Equal(EnemyTraits.JumpsWalls | EnemyTraits.StealsItems, Hog.Traits);
            Assert.Equal(new[] { EnemyTarget.Chest, EnemyTarget.VehicleDepot, EnemyTarget.Player }, Hog.TargetPriority);
            Assert.Empty(Hog.Drops);
            Assert.All(new[] { 3, 4, 5 }, shift => Assert.Contains(Db.Waves.WeightsFor(shift), w => w.Key == Hog));
            Assert.DoesNotContain(Db.Waves.WeightsFor(2), w => w.Key == Hog);
        }

        [Fact]
        public void ItJumpsAStoneWall_ToTheCarrierBehindIt()
        {
            var (world, p) = OnTheStreet(50f, 29f);
            var wall = Build(world, "wall_stone", 56f);
            var e = world.SpawnEnemy(Hog, 64f, 29f)!;
            bool over = false;
            for (int i = 0; i < 4 * Tps && p.Hp == p.MaxHp; i++)
            {
                world.Step();
                over |= world.Constructs.At((int)MathF.Floor(e.X / SimConstants.TileSize), (int)MathF.Floor(e.Z / SimConstants.TileSize)) == wall;
            }
            Assert.True(over, "the hog rider never went over the wall's tile");
            Assert.Equal(p.MaxHp - 20f, p.Hp);
            Assert.Equal(wall.Def.Hp, wall.Hp);
            Assert.True(e.X < 56f);
        }

        [Fact]
        public void AChestBeforeACarrier_EachBlowPullsAnItemOut()
        {
            var (world, p) = OnTheStreet(46f, 29f);
            var chest = Build(world, "chest", 50f);
            var store = world.GetContainer(chest.ContainerId)!;
            Assert.True(world.Give(store, ItemStack.OfItem(Db.Item("stone"), 5)));
            Assert.True(world.Give(store, ItemStack.OfItem(Db.Item("log"), 2)));
            var e = world.SpawnEnemy(Hog, 62f, 29f)!;
            int ticks = 0;
            while (chest.Hp > chest.Def.Hp - 60 && ticks++ < 10 * Tps) world.Step();
            Assert.Equal(chest.Def.Hp - 60, chest.Hp);
            Assert.Equal(p.MaxHp, p.Hp);
            // Three blows, three things out: the first rides with it, the other two lie in one bag 3 m off.
            Assert.Equal(7 - 3, Units(store));
            Assert.Equal(1, e.Carried!.Count);
            var bag = Assert.Single(world.Bags);
            Assert.True(bag.Owner.IsNone);
            Assert.Equal(2, Units(bag.Container));
            float off = MathF.Sqrt((bag.X - chest.CentreX) * (bag.X - chest.CentreX) + (bag.Z - chest.CentreZ) * (bag.Z - chest.CentreZ));
            Assert.InRange(off, 2.9f, 3.1f);
        }

        [Fact]
        public void FelledItDropsWhatItRodeOffWith()
        {
            var (world, p) = OnTheStreet(10f, 29f);
            var chest = Build(world, "chest", 50f);
            var store = world.GetContainer(chest.ContainerId)!;
            Assert.True(world.Give(store, ItemStack.OfItem(Db.Item("stone"), 4)));
            var e = world.SpawnEnemy(Hog, 58f, 29f)!;
            int ticks = 0;
            while (e.Carried == null && ticks++ < 10 * Tps) world.Step();
            Assert.NotNull(e.Carried);
            Assert.Empty(world.Bags);
            float x = e.X, z = e.Z;
            world.DamageEnemy(e, 1000f, p);
            var bag = Assert.Single(world.Bags);
            Assert.Equal((x, z), (bag.X, bag.Z));
            Assert.Equal("stone", Db.Items[bag.Container.Entries.Single().Stack.DefIndex].Id);
            Assert.Equal(3 + 1, Units(store) + Units(bag.Container));
        }

        [Fact]
        public void WithNothingBuilt_ItRidesAtACarrier()
        {
            var (world, p) = OnTheStreet(50f, 29f);
            world.SpawnEnemy(Hog, 66f, 29f);
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.True(p.Hp < p.MaxHp);
        }
    }
}
