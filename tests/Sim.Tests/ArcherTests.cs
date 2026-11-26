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
    /// The Archer (doc 05 §2 "40 HP, 8 dmg, 1.5 s, 4 m/s, Player > turret > construct, Ranged 15 m.
    /// Keeps 10 m distance; retreats from approaching players"; design choice 128): it shoots a
    /// carrier from up to 15 m, backs off from one nearer than 10 m, a wall between takes its arrow,
    /// and with no carrier about it shoots a turret.
    /// </summary>
    public class ArcherTests
    {
        private const int Tps = SimConstants.TickRate;
        private static ContentDb Db => TestContent.Db;
        private static EnemyDef Archer => Db.Enemy("archer");

        private static (SimWorld World, Player Carrier) OnTheStreet(float x = 50f, float z = 28f)
        {
            var world = new SimWorld(Db, new SimSettings { Seed = 7, Raids = false, Draft = false });
            var p = world.AddPlayer("carrier")!;
            SimDriver.Teleport(p, x, z);
            return (world, p);
        }

        private static float Distance(Enemy e, Player p) => MathF.Sqrt((e.X - p.Move.X) * (e.X - p.Move.X) + (e.Z - p.Move.Z) * (e.Z - p.Move.Z));

        [Fact]
        public void TheContentHasTheArcherOfTheDocs()
        {
            Assert.Equal((40f, 8f, 1.5f, 4f, 15f, 10f), (Archer.Hp, Archer.Damage, Archer.AttackRate, Archer.Speed, Archer.AttackRange, Archer.KeepDistance));
            Assert.Equal((15, 2), (Archer.Cost, Archer.UnlockShift));
            Assert.Equal(EnemyTraits.Ranged, Archer.Traits);
            Assert.Equal(new[] { EnemyTarget.Player, EnemyTarget.Turret }, Archer.TargetPriority);
            Assert.All(Db.Enemies.Where(e => e != Archer), e => Assert.False(e.Ranged));
            Assert.All(new[] { 2, 3, 4, 5 }, shift => Assert.Contains(Db.Waves.WeightsFor(shift), w => w.Key == Archer));
        }

        [Fact]
        public void From13Metres_ItShootsTheCarrier_EveryOneAndAHalfSeconds_WithoutComingCloser()
        {
            var (world, p) = OnTheStreet();
            var e = world.SpawnEnemy(Archer, 63f, 28f)!;
            int arrows = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < 5 * Tps; i++)
            {
                world.Events.Clear();
                world.Step();
                arrows += world.Events.OfType<EnemyShotEvent>().Count(s => s.Kind == EnemyShotKind.Arrow && !s.AtConstruct);
                nearest = MathF.Min(nearest, Distance(e, p));
            }
            // Its first after the windup, then one each 1.5 s: four in five seconds.
            Assert.InRange(arrows, 3, 4);
            Assert.Equal(p.MaxHp - arrows * 8f, p.Hp);
            Assert.True(nearest > 12.9f, $"it came to {nearest:0.0} m");
        }

        [Fact]
        public void FromFarther_ItWalksUpToItsReach_AndNoFurther()
        {
            var (world, p) = OnTheStreet();
            var e = world.SpawnEnemy(Archer, 68f, 28f)!;
            SimDriver.StepTicks(world, 4 * Tps);
            Assert.True(p.Hp < p.MaxHp, "it never shot");
            Assert.InRange(Distance(e, p), 10f, 15.5f);
        }

        [Fact]
        public void ACarrierComingNear_ItBacksOff()
        {
            var (world, p) = OnTheStreet();
            var e = world.SpawnEnemy(Archer, 56f, 28f)!;
            world.Step();
            float before = Distance(e, p);
            SimDriver.StepTicks(world, Tps);
            float after = Distance(e, p);
            Assert.True(after > before + 3f, $"from {before:0.0} m to {after:0.0} m");
            // Still facing them as it goes.
            Assert.True(MathF.Abs(MathF.Sin(e.Yaw) - (e.X - p.Move.X) / after) < 0.2f);
        }

        [Fact]
        public void BackedIntoACornerFor2Seconds_ItShootsAnyway()
        {
            var (world, p) = OnTheStreet();
            var e = world.SpawnEnemy(Archer, 56f, 28f)!;
            // The carrier keeps 5 m from it however it backs off.
            int ticks = 0;
            while (p.Hp == p.MaxHp && ticks < 6 * Tps)
            {
                SimDriver.Teleport(p, e.X - 5f, e.Z);
                world.Step();
                ticks++;
            }
            Assert.True(p.Hp < p.MaxHp, "a carrier on its heels was never shot");
            Assert.InRange(ticks, 2 * Tps, 3 * Tps);
        }

        [Fact]
        public void AStoneWallBetweenTakesTheArrow()
        {
            var (world, p) = OnTheStreet(50f, 29f);
            int tx = (int)MathF.Floor(56f / SimConstants.TileSize), tz = (int)MathF.Floor(28f / SimConstants.TileSize);
            var wall = world.Constructs.Add(Db.Building("wall_stone"), tx, tz, 1, Db.Building("wall_stone").Hp, 0);
            var e = world.SpawnEnemy(Archer, 63f, 29f)!;
            int atWall = 0;
            for (int i = 0; i < 3 * Tps; i++)
            {
                world.Events.Clear();
                world.Step();
                atWall += world.Events.OfType<EnemyShotEvent>().Count(s => s.AtConstruct);
            }
            Assert.True(atWall > 0, $"no arrow struck the wall: carrier hp {p.Hp}, archer at {e.X:0.0},{e.Z:0.0} {e.State}, wall {wall.Tx},{wall.Tz} hp {wall.Hp}");
            Assert.Equal(p.MaxHp, p.Hp);
            Assert.True(wall.Hp < wall.Def.Hp);
        }

        [Fact]
        public void WithNoCarrierInReach_ItShootsATurret()
        {
            var (world, p) = OnTheStreet();
            SimDriver.Teleport(p, 10f, 60f);
            int tx = 30, tz = 14;
            var turret = world.Constructs.Add(Db.Building("turret"), tx, tz, 0, Db.Building("turret").Hp, 0);
            // A mega lives long enough under its guns to reach it.
            var e = world.SpawnEnemy(Archer, turret.CentreX + 19f, turret.CentreZ, mega: true)!;
            bool struck = false;
            for (int i = 0; i < 8 * Tps && !struck && world.Enemies.Contains(e); i++)
            {
                world.Events.Clear();
                world.Step();
                struck = world.Events.OfType<EnemyShotEvent>().Any(s => s.AtConstruct);
            }
            Assert.True(struck, "the turret was never shot at");
            Assert.True(turret.Hp < turret.Def.Hp);
            Assert.Equal(p.MaxHp, p.Hp);
        }

        [Fact]
        public void AMeleeRaiderStillWalksUpAndHits_ArchersTakeNoPlaceAtTheCarrier()
        {
            var (world, p) = OnTheStreet();
            var archer = world.SpawnEnemy(Archer, 62f, 28f)!;
            var raider = world.SpawnEnemy(Db.Enemy("barbarian"), 58f, 28f)!;
            SimDriver.StepTicks(world, 3 * Tps);
            Assert.False(raider.Waiting);
            Assert.True(Distance(raider, p) < 2.2f, $"the raider is {Distance(raider, p):0.0} m off");
            Assert.True(Distance(archer, p) > 9.5f);
        }
    }
}
