using System;
using System.Linq;
using PerformativeMail.Sim.Combat;
using PerformativeMail.Sim.Content;
using PerformativeMail.Sim.Core;
using PerformativeMail.Sim.Players;
using PerformativeMail.Sim.World;
using Xunit;

namespace PerformativeMail.Sim.Tests
{
    /// <summary>
    /// The Giant's siege (doc 05 §2 "400 HP, 40 dmg, 2.0 s, 3 m/s; Construct (buildings first:
    /// depots, sorters) > player; Slow siege unit. Ignores players unless attacked by one within 5 m
    /// for 3 s"; design choice 131).
    /// </summary>
    public class GiantTests
    {
        private const int Tps = SimConstants.TickRate;
        private static ContentDb Db => TestContent.Db;
        private static EnemyDef Giant => Db.Enemy("giant");

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
            return world.Constructs.Add(def, (int)MathF.Floor(x / SimConstants.TileSize), (int)MathF.Floor(z / SimConstants.TileSize), 1, def.Hp, 0);
        }

        [Fact]
        public void TheContentHasTheGiantOfTheDocs()
        {
            Assert.Equal((400f, 40f, 2f, 3f, 60, 3), (Giant.Hp, Giant.Damage, Giant.AttackRate, Giant.Speed, Giant.Cost, Giant.UnlockShift));
            Assert.Equal((EnemyTraits.Siege, 5f, 3f, 30f), (Giant.Traits, Giant.ProvokeRange, Giant.ProvokeSeconds, Giant.AggroRange));
            Assert.Equal(new[] { EnemyTarget.Chest, EnemyTarget.Sorter, EnemyTarget.VehicleDepot, EnemyTarget.AnyConstruct, EnemyTarget.PostOffice, EnemyTarget.Player }, Giant.TargetPriority);
        }

        [Fact]
        public void ItIgnoresACarrierBesideIt()
        {
            var (world, p) = OnTheStreet(60f, 29f);
            var e = world.SpawnEnemy(Giant, 63f, 29f)!;
            SimDriver.StepTicks(world, 5 * Tps);
            Assert.True(e.Target.IsNone);
            Assert.Equal(p.MaxHp, p.Hp);
        }

        [Fact]
        public void ItBreaksAWall_FortyEveryTwoSeconds()
        {
            var (world, p) = OnTheStreet(20f, 29f);
            var wall = Build(world, "wall_wood", 57f);
            var e = world.SpawnEnemy(Giant, 70f, 29f)!;
            int ticks = 0;
            while (wall.Hp == wall.Def.Hp && ticks++ < 10 * Tps) world.Step();
            Assert.Equal(wall.Def.Hp - 40, wall.Hp);
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.Equal(wall.Def.Hp - 120, wall.Hp);
            // From its long reach, not up against it.
            Assert.True(world.Constructs.Distance(wall, e.X, e.Z, out _, out _) > 1.5f);
            Assert.Equal(p.MaxHp, p.Hp);
        }

        [Fact]
        public void AChestBeforeAWall()
        {
            var (world, _) = OnTheStreet(10f, 29f);
            var wall = Build(world, "wall_wood", 64f);
            var chest = Build(world, "chest", 50f);
            var e = world.SpawnEnemy(Giant, 64f, 31f)!;
            int ticks = 0;
            while (chest.Hp == chest.Def.Hp && ticks++ < 15 * Tps) world.Step();
            Assert.True(chest.Hp < chest.Def.Hp, $"the chest was never struck; the giant stands at {e.X:0.0}, {e.Z:0.0}");
            Assert.Equal(wall.Def.Hp, wall.Hp);
        }

        [Fact]
        public void HurtFromCloseBy_ItTurnsOnTheCarrierForThreeSeconds()
        {
            var (world, p) = OnTheStreet(60f, 29f);
            var wall = Build(world, "wall_wood", 50f);
            var e = world.SpawnEnemy(Giant, 63f, 29f)!;
            world.Step();
            world.DamageEnemy(e, 10f, p);
            world.Step();
            Assert.Equal(p.Id, e.Target);
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.True(p.Hp < p.MaxHp, "the giant did not strike the carrier who hurt it");
            Assert.Equal(p.Id, e.Target);
            // Three seconds after the blow it is back at what is built.
            SimDriver.StepTicks(world, Tps + 2);
            Assert.True(e.Target.IsNone);
            float hp = p.Hp;
            SimDriver.StepTicks(world, 8 * Tps);
            // Not struck again (they heal by themselves meanwhile).
            Assert.True(p.Hp >= hp);
            Assert.True(wall.Hp < wall.Def.Hp);
        }

        [Fact]
        public void HurtFromAfar_ItDoesNotNotice()
        {
            var (world, p) = OnTheStreet(50f, 29f);
            var e = world.SpawnEnemy(Giant, 60f, 29f)!;
            world.Step();
            world.DamageEnemy(e, 10f, p);
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.True(e.Target.IsNone);
            Assert.Equal(p.MaxHp, p.Hp);
            // The kill is still the carrier's.
            Assert.Equal(p.Id, e.LastHitBy);
        }

        [Fact]
        public void AnotherRaiderStillGoesForTheCarrier()
        {
            var (world, p) = OnTheStreet(60f, 29f);
            world.SpawnEnemy(Giant, 63f, 29f);
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), 66f, 29f)!;
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Equal(p.Id, raider.Target);
            Assert.True(p.Hp < p.MaxHp);
        }
    }
}
