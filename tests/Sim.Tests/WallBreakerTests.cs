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
    /// The Wall Breaker (doc 05 §2 "30 HP; 150 to walls/gates/belts, 20 otherwise; suicide; 6 m/s;
    /// Wall > gate > belt segment; runs to the nearest wall or belt … and explodes (r = 1.5 m)";
    /// §4 "Stone Wall: Wall Breaker damage reduced 50%"; design choice 129).
    /// </summary>
    public class WallBreakerTests
    {
        private const int Tps = SimConstants.TickRate;
        private static ContentDb Db => TestContent.Db;
        private static EnemyDef Breaker => Db.Enemy("wall_breaker");

        private static (SimWorld World, Player Carrier) OnTheStreet(float x = 10f, float z = 29f)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var p = world.AddPlayer("carrier")!;
            SimDriver.Teleport(p, x, z);
            return (world, p);
        }

        private static Construct Wall(SimWorld world, string id, float x, float z = 29f)
        {
            var def = Db.Building(id);
            return world.Constructs.Add(def, (int)MathF.Floor(x / SimConstants.TileSize), (int)MathF.Floor(z / SimConstants.TileSize), 1, def.Hp, 0);
        }

        /// <summary>Steps until it is gone (or the time is up); the blasts it made.</summary>
        private static int RunUntilGone(SimWorld world, Enemy e, int seconds = 6)
        {
            int blasts = 0;
            for (int i = 0; i < seconds * Tps && world.Enemies.Contains(e); i++)
            {
                world.Events.Clear();
                world.Step();
                blasts += world.Events.OfType<EnemyShotEvent>().Count(s => s.Kind == EnemyShotKind.Blast);
                Assert.Empty(world.Events.OfType<EnemyDiedEvent>());
            }
            Assert.DoesNotContain(e, world.Enemies);
            return blasts;
        }

        [Fact]
        public void TheContentHasTheWallBreakerOfTheDocs()
        {
            Assert.Equal((30f, 20f, 6f, 12, 3), (Breaker.Hp, Breaker.Damage, Breaker.Speed, Breaker.Cost, Breaker.UnlockShift));
            Assert.Equal((EnemyTraits.Suicide, 1.5f, 150f), (Breaker.Traits, Breaker.BlastRadius, Breaker.StructureDamage));
            Assert.Equal(new[] { EnemyTarget.Wall, EnemyTarget.Gate, EnemyTarget.Belt, EnemyTarget.Player }, Breaker.TargetPriority);
            Assert.Empty(Breaker.Drops);
            Assert.Equal((0.5f, 0f), (Db.Building("wall_stone").WallBreakerResist, Db.Building("wall_wood").WallBreakerResist));
            Assert.All(new[] { 3, 4, 5 }, shift => Assert.Contains(Db.Waves.WeightsFor(shift), w => w.Key == Breaker));
            Assert.DoesNotContain(Db.Waves.WeightsFor(2), w => w.Key == Breaker);
        }

        [Fact]
        public void ItRunsToTheWoodenWall_AndBlowsHalfOfItAway()
        {
            var (world, p) = OnTheStreet();
            var wall = Wall(world, "wall_wood", 57f);
            var e = world.SpawnEnemy(Breaker, 70f, 29f)!;
            Assert.Equal(1, RunUntilGone(world, e, 4));
            Assert.Equal(300 - 150, wall.Hp);
            Assert.Equal(p.MaxHp, p.Hp);
            Assert.Equal(0, world.EnemiesKilledTotal);
        }

        [Fact]
        public void AStoneWallTakesHalfOfIt()
        {
            var (world, _) = OnTheStreet();
            var wall = Wall(world, "wall_stone", 57f);
            var e = world.SpawnEnemy(Breaker, 70f, 29f)!;
            RunUntilGone(world, e, 4);
            Assert.Equal(800 - 75, wall.Hp);
        }

        [Fact]
        public void AWallBeforeACarrier_AndACarrierBesideTheWallIsHurtToo()
        {
            var (world, p) = OnTheStreet(56.3f, 29f);
            var wall = Wall(world, "wall_wood", 57f);
            var e = world.SpawnEnemy(Breaker, 70f, 29f)!;
            RunUntilGone(world, e, 4);
            Assert.Equal(150, wall.Hp);
            // Beyond the wall from the blast, but within its 1.5 m.
            Assert.Equal(p.MaxHp - 20f, p.Hp);
        }

        [Fact]
        public void WithNothingBuilt_ItRunsAtACarrier_AndGoesOffForTwenty()
        {
            var (world, p) = OnTheStreet(50f, 29f);
            var e = world.SpawnEnemy(Breaker, 64f, 29f)!;
            Assert.Equal(1, RunUntilGone(world, e, 4));
            Assert.Equal(p.MaxHp - 20f, p.Hp);
        }

        [Fact]
        public void FelledOnTheWay_ItDoesNotGoOff()
        {
            var (world, p) = OnTheStreet(50f, 29f);
            var wall = Wall(world, "wall_wood", 40f);
            var e = world.SpawnEnemy(Breaker, 55f, 29f)!;
            world.Step();
            world.DamageEnemy(e, 100f, p);
            Assert.DoesNotContain(e, world.Enemies);
            SimDriver.StepTicks(world, 2 * Tps);
            Assert.Equal(300, wall.Hp);
            Assert.Equal(p.MaxHp, p.Hp);
        }

        [Fact]
        public void NothingInItsReach_ItHoldsTheLine()
        {
            var (world, p) = OnTheStreet(10f, 29f);
            var e = world.SpawnEnemy(Breaker, 80f, 29f)!;
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.Contains(e, world.Enemies);
            Assert.Equal(p.MaxHp, p.Hp);
        }
    }
}
